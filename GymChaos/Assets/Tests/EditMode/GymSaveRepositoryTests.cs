using System.IO;
using System.Linq;
using NUnit.Framework;

public sealed class GymSaveRepositoryTests
{
    private string directory;
    private GymSaveRepository repository;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "GymChaosTests", System.Guid.NewGuid().ToString("N"));
        repository = new GymSaveRepository(directory, GymClassCatalog.All.Select(c => c.id));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Test]
    public void ClassCatalog_LoadsFiveClassesWithBaseline()
    {
        Assert.AreEqual(5, GymClassCatalog.All.Count);
        Assert.AreEqual(GymClassCatalog.BaselineId, GymClassCatalog.Baseline.id);
    }

    [Test]
    public void WriteThenRead_RoundTripsCharacter()
    {
        GymCharacterSave save = GymCharacterSave.New(GymClassCatalog.BaselineId);
        save.progression.level = 7;
        save.progression.shirt = "Gold";

        repository.Write(save.slotId, repository.Serialize(save));
        GymSaveReadResult result = repository.Read(save.slotId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(result.RecoveredBackup);
        Assert.AreEqual(save.classId, result.Data.classId);
        Assert.AreEqual(7, result.Data.progression.level);
        Assert.AreEqual("Gold", result.Data.progression.shirt);
    }

    [Test]
    public void Read_MissingSlotReportsMissing()
    {
        GymSaveReadResult result = repository.Read(System.Guid.NewGuid().ToString("N"));

        Assert.AreEqual(GymSaveFailure.Missing, result.Failure);
    }

    [Test]
    public void Read_CorruptCurrentFileRecoversPreviousSave()
    {
        GymCharacterSave save = GymCharacterSave.New(GymClassCatalog.BaselineId);
        save.progression.level = 2;
        repository.Write(save.slotId, repository.Serialize(save));
        save.progression.level = 3;
        repository.Write(save.slotId, repository.Serialize(save));

        File.WriteAllText(Path.Combine(directory, save.slotId + ".json"), "{not json");
        GymSaveReadResult result = repository.Read(save.slotId);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.RecoveredBackup);
        Assert.AreEqual(2, result.Data.progression.level);
    }

    [Test]
    public void Decode_RejectsTamperedChecksum()
    {
        GymCharacterSave save = GymCharacterSave.New(GymClassCatalog.BaselineId);
        string serialized = repository.Serialize(save);
        string tampered = serialized.Replace("\\\"level\\\":1", "\\\"level\\\":99");
        Assume.That(tampered, Is.Not.EqualTo(serialized));

        Assert.AreEqual(GymSaveFailure.Corrupt, repository.Decode(tampered, save.slotId).Failure);
    }

    [Test]
    public void Decode_FlagsUnknownClass()
    {
        GymCharacterSave save = GymCharacterSave.New(GymClassCatalog.BaselineId);
        string serialized = repository.Serialize(save);
        var limited = new GymSaveRepository(directory, new[] { "strongman" });

        Assert.AreEqual(GymSaveFailure.UnknownClass, limited.Decode(serialized, save.slotId).Failure);
    }
}
