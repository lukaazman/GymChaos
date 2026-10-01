using System;
using System.Collections.Generic;
using UnityEngine;

public enum GymExerciseCategory { GeneralResistance, Bodyweight, Compound, Cardio }

[Serializable]
public sealed class GymClassDefinition
{
    public string id;
    public string displayName;
    public string description;
    public string strength;
    public string weakness;
    public string artwork;
    public string appearance;
    public float bodyweight = 1f;
    public float compound = 1f;
    public float cardio = 1f;
    public float sprintSpeed = 1f;
    public float staminaCapacity = 1f;
    public float sprintCost = 1f;
    public float upperBulk = 1f;
    public float lowerBulk = 1f;
    public float softTissue;

    public float Performance(GymExerciseCategory category)
    {
        switch (category)
        {
            case GymExerciseCategory.Bodyweight: return bodyweight;
            case GymExerciseCategory.Compound: return compound;
            case GymExerciseCategory.Cardio: return cardio;
            default: return 1f;
        }
    }
}

/// <summary>One authored catalog drives gameplay, UI, appearance and save compatibility.</summary>
public static class GymClassCatalog
{
    public const string BaselineId = "bodybuilding";
    [Serializable] private sealed class Catalog { public GymClassDefinition[] classes; }
    private static GymClassDefinition[] definitions;
    private static IReadOnlyList<GymClassDefinition> readOnly;
    public static IReadOnlyList<GymClassDefinition> All { get { EnsureLoaded(); return readOnly; } }
    public static GymClassDefinition Baseline => Get(BaselineId);

    public static bool TryGet(string id, out GymClassDefinition definition)
    {
        EnsureLoaded();
        foreach (GymClassDefinition candidate in definitions)
            if (string.Equals(candidate.id, id, StringComparison.Ordinal)) { definition = candidate; return true; }
        definition = null;
        return false;
    }

    public static GymClassDefinition Get(string id)
    {
        if (TryGet(id, out GymClassDefinition value)) return value;
        throw new ArgumentException("Unsupported character class: " + id, nameof(id));
    }

    public static GymExerciseCategory Category(GymExerciseType exercise)
    {
        switch (exercise)
        {
            case GymExerciseType.Dips:
            case GymExerciseType.PullUps: return GymExerciseCategory.Bodyweight;
            case GymExerciseType.FlatBenchPress:
            case GymExerciseType.InclineBenchPress:
            case GymExerciseType.BarbellSquat:
            case GymExerciseType.Deadlift: return GymExerciseCategory.Compound;
            case GymExerciseType.Treadmill:
            case GymExerciseType.ExerciseBike: return GymExerciseCategory.Cardio;
            case GymExerciseType.PreacherCurl:
            case GymExerciseType.LatPulldown: return GymExerciseCategory.GeneralResistance;
            default: throw new ArgumentOutOfRangeException(nameof(exercise));
        }
    }

    private static void EnsureLoaded()
    {
        if (definitions != null) return;
        TextAsset asset = Resources.Load<TextAsset>("Classes/catalog");
        Catalog catalog = asset == null ? null : JsonUtility.FromJson<Catalog>(asset.text);
        if (catalog?.classes == null || catalog.classes.Length != 5)
            throw new InvalidOperationException("The five-class catalog is missing or invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GymClassDefinition item in catalog.classes)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id) ||
                string.IsNullOrWhiteSpace(item.displayName) || !Positive(item.bodyweight) ||
                !Positive(item.compound) || !Positive(item.cardio) || !Positive(item.sprintSpeed) ||
                !Positive(item.staminaCapacity) || !Positive(item.sprintCost) ||
                !Positive(item.upperBulk) || !Positive(item.lowerBulk))
                throw new InvalidOperationException("Invalid character class configuration.");
        }
        if (!ids.Contains(BaselineId)) throw new InvalidOperationException("Bodybuilding baseline is missing.");
        definitions = catalog.classes;
        readOnly = Array.AsReadOnly(definitions);
    }

    private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
}
