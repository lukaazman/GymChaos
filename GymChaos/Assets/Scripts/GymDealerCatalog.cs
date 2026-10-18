/// <summary>
/// Homeless Hank's stash: performance enhancing drugs, sold for protein.com
/// loyalty stamps (he resells them) and a little reputation. Each one beats
/// any supplement and costs health while it runs, plus a share of max health
/// for the rest of the session. The stats are a game reading of how strong
/// and how harsh the real compounds are: trenbolone is the strongest and the
/// worst, oxandrolone mild, the SARM and the GH secretagogue in between, the
/// recovery peptide the gentlest (its long-term risks unknown). Part of every
/// label is unknown in the menu ("?????"): Hank does not hand out leaflets.
/// Models: Assets/ProteinStore/v2/export_dealer_products.py.
/// </summary>
public static class GymDealerCatalog
{
    public const string Unknown = "?????";

    public static readonly GymProteinProduct[] Products =
    {
        new GymProteinProduct
        {
            id = "dealer_tren", displayName = "TREN", category = "VIAL  10 ML",
            effectText = "+60% PUNCH  +35% PERFORMANCE   CARDIO " + Unknown + "   HEART " + Unknown,
            stampPrice = 6, unlockLevel = 6, durationSeconds = 240f, gear = true,
            healthDrainPerSecond = 2.5f, maxHealthToll = 0.12f, instantHarm = 20f, reputationCost = 4,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.StrengthDamage, 1.6f),
                new GymBuffModifier(GymBuffStat.StrengthForce, 1.5f),
                new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.35f),
                new GymBuffModifier(GymBuffStat.CardioCapacity, 0.6f),
                new GymBuffModifier(GymBuffStat.SprintCapacity, 0.75f),
                new GymBuffModifier(GymBuffStat.HealthRegen, 0.35f)
            },
            crashModifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.StrengthDamage, 0.8f),
                new GymBuffModifier(GymBuffStat.ExercisePerformance, 0.85f)
            },
            crashSeconds = 120f,
            variants = new[] { new GymProteinVariant("dealer_tren", "UNLABELLED BATCH") }
        },
        new GymProteinProduct
        {
            id = "dealer_anavar", displayName = "ANAVAR", category = "120 CAPSULES",
            effectText = "+20% PERFORMANCE  +15% PUNCH  +8% SPRINT   LIVER " + Unknown,
            stampPrice = 3, unlockLevel = 3, durationSeconds = 300f, gear = true,
            healthDrainPerSecond = 0.4f, maxHealthToll = 0.04f, reputationCost = 2,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.2f),
                new GymBuffModifier(GymBuffStat.StrengthDamage, 1.15f),
                new GymBuffModifier(GymBuffStat.SprintSpeed, 1.08f),
                new GymBuffModifier(GymBuffStat.HealthRegen, 0.75f)
            },
            variants = new[] { new GymProteinVariant("dealer_anavar", Unknown + " MG") }
        },
        new GymProteinProduct
        {
            id = "dealer_mk677", displayName = "MK-677", category = "60 CAPSULES",
            effectText = "+80% RECOVERY  -30% FATIGUE   SLUGGISH " + Unknown,
            stampPrice = 3, unlockLevel = 2, durationSeconds = 360f, gear = true,
            maxHealthToll = 0.03f, reputationCost = 1,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.HealthRegen, 1.8f),
                new GymBuffModifier(GymBuffStat.FatigueGain, 0.7f),
                new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.1f),
                new GymBuffModifier(GymBuffStat.SprintSpeed, 0.9f),
                new GymBuffModifier(GymBuffStat.CardioCapacity, 0.85f),
                new GymBuffModifier(GymBuffStat.TechniqueDifficulty, 1.1f)
            },
            variants = new[] { new GymProteinVariant("dealer_mk677", "GH " + Unknown) }
        },
        new GymProteinProduct
        {
            id = "dealer_rad140", displayName = "RAD-140", category = "60 CAPSULES",
            effectText = "+30% PUNCH  +25% PERFORMANCE   AFTER IT " + Unknown,
            stampPrice = 4, unlockLevel = 4, durationSeconds = 240f, gear = true,
            healthDrainPerSecond = 1.2f, maxHealthToll = 0.07f, reputationCost = 3,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.StrengthDamage, 1.3f),
                new GymBuffModifier(GymBuffStat.StrengthForce, 1.3f),
                new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.25f),
                new GymBuffModifier(GymBuffStat.HealthRegen, 0.6f)
            },
            crashModifiers = new[] { new GymBuffModifier(GymBuffStat.ExercisePerformance, 0.85f) },
            crashSeconds = 90f,
            variants = new[] { new GymProteinVariant("dealer_rad140", "RESEARCH USE " + Unknown) }
        },
        new GymProteinProduct
        {
            id = "dealer_bpc157", displayName = "BPC-157", category = "120 CAPSULES",
            effectText = "+30 HP  FAST HEALING  -20% FATIGUE   LONG TERM " + Unknown,
            stampPrice = 2, unlockLevel = 1, durationSeconds = 300f, gear = true,
            instantHeal = 30f, maxHealthToll = 0.01f, reputationCost = 1,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.HealthRegen, 2.2f),
                new GymBuffModifier(GymBuffStat.FatigueGain, 0.8f)
            },
            variants = new[] { new GymProteinVariant("dealer_bpc157", "BLEND OF " + Unknown) }
        },
    };

    /// <summary>
    /// How bad it is for you, 1 (mild) to 5 (severe): shown as a meter, the
    /// exact numbers stay hidden.
    /// </summary>
    public static int GetSeverity(GymProteinProduct product)
    {
        if (product == null)
        {
            return 0;
        }
        float score = product.healthDrainPerSecond + product.maxHealthToll * 20f +
            product.instantHarm * 0.08f + product.crashSeconds / 120f;
        return UnityEngine.Mathf.Clamp(1 + UnityEngine.Mathf.FloorToInt(score), 1, 5);
    }

    public static GymProteinProduct Find(string id)
    {
        for (int i = 0; i < Products.Length; i++)
        {
            if (Products[i].id == id)
            {
                return Products[i];
            }
        }
        return null;
    }
}
