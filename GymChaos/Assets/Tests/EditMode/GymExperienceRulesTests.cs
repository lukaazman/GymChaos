using NUnit.Framework;

public sealed class GymExperienceRulesTests
{
    [TestCase(1, 100)]
    [TestCase(2, 141)]
    [TestCase(5, 336)]
    public void ExperienceToNextLevel_FollowsCurve(int level, int expected)
    {
        Assert.AreEqual(expected, GymExperienceService.GetExperienceToNextLevel(level));
    }

    [Test]
    public void ExperienceToNextLevel_TreatsInvalidLevelsAsLevelOne()
    {
        Assert.AreEqual(GymExperienceService.GetExperienceToNextLevel(1), GymExperienceService.GetExperienceToNextLevel(0));
        Assert.AreEqual(GymExperienceService.GetExperienceToNextLevel(1), GymExperienceService.GetExperienceToNextLevel(-7));
    }

    [Test]
    public void ExperienceToNextLevel_StrictlyIncreases()
    {
        for (int level = 1; level < 50; level++)
            Assert.Less(GymExperienceService.GetExperienceToNextLevel(level), GymExperienceService.GetExperienceToNextLevel(level + 1));
    }

    [TestCase(WorkoutResult.Perfect, 10)]
    [TestCase(WorkoutResult.AutoPerfect, 10)]
    [TestCase(WorkoutResult.Good, 5)]
    [TestCase(WorkoutResult.Miss, 1)]
    public void RepReward_PaysMostForPerfect(WorkoutResult result, int expected)
    {
        Assert.AreEqual(expected, GymExperienceService.GetRepReward(result));
    }

    [TestCase(0, "No title")]
    [TestCase(1, "Gym Regular")]
    [TestCase(3, "Iron Fixture")]
    [TestCase(6, "Chaos Veteran")]
    [TestCase(10, "Gym Myth")]
    public void MasteryTitle_MatchesRankBands(int rank, string expected)
    {
        Assert.AreEqual(expected, GymExperienceService.GetMasteryTitle(rank));
    }

    [Test]
    public void MasteryChallenge_CyclesEveryFourRanks()
    {
        Assert.AreEqual("Locked", GymExperienceService.GetMasteryChallengeLabel(0));
        for (int rank = 1; rank <= 4; rank++)
            Assert.AreEqual(GymExperienceService.GetMasteryChallengeLabel(rank), GymExperienceService.GetMasteryChallengeLabel(rank + 4));
    }
}
