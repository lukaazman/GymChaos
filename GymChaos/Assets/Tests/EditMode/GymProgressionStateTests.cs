using NUnit.Framework;

public sealed class GymProgressionStateTests
{
    [Test]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var state = new GymProgressionState
        {
            level = 0,
            experience = -5,
            totalExperience = -10,
            skillPoints = -1,
            strengthRank = 99,
            enduranceRank = -3,
            reputation = 500,
            gymDay = 0
        };

        state.Normalize();

        Assert.AreEqual(1, state.level);
        Assert.AreEqual(0, state.experience);
        Assert.AreEqual(0, state.totalExperience);
        Assert.AreEqual(0, state.skillPoints);
        Assert.AreEqual(GymExperienceService.MaxStatRank, state.strengthRank);
        Assert.AreEqual(0, state.enduranceRank);
        Assert.AreEqual(100, state.reputation);
        Assert.AreEqual(1, state.gymDay);
    }

    [Test]
    public void Normalize_RepairsMissingDailyArrays()
    {
        var state = new GymProgressionState { dailyProgress = null, dailyCompleted = new bool[1] };

        state.Normalize();

        Assert.AreEqual(3, state.dailyProgress.Length);
        Assert.AreEqual(3, state.dailyCompleted.Length);
    }

    [TestCase("red", "Red")]
    [TestCase("GOLD", "Gold")]
    [TestCase("tie-dye", "Black")]
    [TestCase(null, "Black")]
    public void Normalize_CanonicalizesShirt(string stored, string expected)
    {
        var state = new GymProgressionState { shirt = stored };

        state.Normalize();

        Assert.AreEqual(expected, state.shirt);
    }

    [TestCase("visor", "BucketHat")]
    [TestCase("beanie", "Beanie")]
    [TestCase("crown", "None")]
    public void Normalize_MigratesAndCanonicalizesHeadwear(string stored, string expected)
    {
        var state = new GymProgressionState { headwear = stored };

        state.Normalize();

        Assert.AreEqual(expected, state.headwear);
    }

    [Test]
    public void SetStatRank_ClampsAndRoundTripsEveryStat()
    {
        var state = new GymProgressionState();

        foreach (GymStat stat in System.Enum.GetValues(typeof(GymStat)))
        {
            state.SetStatRank(stat, 4);
            Assert.AreEqual(4, state.GetStatRank(stat), stat.ToString());
            state.SetStatRank(stat, GymExperienceService.MaxStatRank + 5);
            Assert.AreEqual(GymExperienceService.MaxStatRank, state.GetStatRank(stat), stat.ToString());
        }
    }
}
