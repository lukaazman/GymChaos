using System;
using UnityEngine;

public enum GymStat
{
    Strength,
    Endurance,
    Technique,
    Reputation
}

public enum GymShirtColor
{
    Black,
    White,
    Red,
    Blue,
    Gold
}

public enum GymHeadwear
{
    None,
    Cap,
    Beanie,
    Headband,
    Visor
}

/// <summary>
/// Local progression data. It deliberately contains no currency or shop state.
/// The level can keep growing after the four stat trees are complete; those
/// later points become Mastery instead of unlimited combat power.
/// </summary>
[Serializable]
public sealed class GymProgressionState
{
    public int level = 1;
    public int experience;
    public int totalExperience;
    public int skillPoints;
    public int masteryRank;

    public int strengthRank;
    public int enduranceRank;
    public int techniqueRank;
    public int reputationRank;
    public int reputation;

    public int gymDay = 1;
    public bool lockerPrepCompleted;
    public string shirt = nameof(GymShirtColor.Black);
    public string headwear = nameof(GymHeadwear.None);

    public int[] dailyProgress = { 0, 0, 0 };
    public bool[] dailyCompleted = { false, false, false };

    public void Normalize()
    {
        level = Mathf.Max(1, level);
        experience = Mathf.Max(0, experience);
        totalExperience = Mathf.Max(totalExperience, experience);
        skillPoints = Mathf.Max(0, skillPoints);
        masteryRank = Mathf.Max(0, masteryRank);
        strengthRank = Mathf.Clamp(strengthRank, 0, GymExperienceService.MaxStatRank);
        enduranceRank = Mathf.Clamp(enduranceRank, 0, GymExperienceService.MaxStatRank);
        techniqueRank = Mathf.Clamp(techniqueRank, 0, GymExperienceService.MaxStatRank);
        reputationRank = Mathf.Clamp(reputationRank, 0, GymExperienceService.MaxStatRank);
        reputation = Mathf.Clamp(reputation, -100, 100);
        gymDay = Mathf.Max(1, gymDay);

        if (dailyProgress == null || dailyProgress.Length != 3)
        {
            dailyProgress = new[] { 0, 0, 0 };
        }
        if (dailyCompleted == null || dailyCompleted.Length != 3)
        {
            dailyCompleted = new[] { false, false, false };
        }

        if (!Enum.TryParse(shirt, true, out GymShirtColor parsedShirt))
        {
            shirt = nameof(GymShirtColor.Black);
        }
        else
        {
            shirt = parsedShirt.ToString();
        }

        if (!Enum.TryParse(headwear, true, out GymHeadwear parsedHeadwear))
        {
            headwear = nameof(GymHeadwear.None);
        }
        else
        {
            headwear = parsedHeadwear.ToString();
        }
    }

    public int GetStatRank(GymStat stat)
    {
        switch (stat)
        {
            case GymStat.Strength:
                return strengthRank;
            case GymStat.Endurance:
                return enduranceRank;
            case GymStat.Technique:
                return techniqueRank;
            case GymStat.Reputation:
                return reputationRank;
            default:
                return 0;
        }
    }

    public void SetStatRank(GymStat stat, int value)
    {
        value = Mathf.Clamp(value, 0, GymExperienceService.MaxStatRank);
        switch (stat)
        {
            case GymStat.Strength:
                strengthRank = value;
                break;
            case GymStat.Endurance:
                enduranceRank = value;
                break;
            case GymStat.Technique:
                techniqueRank = value;
                break;
            case GymStat.Reputation:
                reputationRank = value;
                break;
        }
    }
}
