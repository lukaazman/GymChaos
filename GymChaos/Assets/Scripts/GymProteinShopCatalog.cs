using UnityEngine;

/// <summary>Stats a protein.com product can temporarily scale.</summary>
public enum GymBuffStat
{
    StrengthDamage,
    StrengthForce,
    SprintSpeed,
    SprintCapacity,
    SprintDrain,
    ExercisePerformance,
    CardioCapacity,
    TechniqueDifficulty,
    HealthRegen,
    FatigueGain
}

/// <summary>One multiplier a product applies while its effect lasts.</summary>
public readonly struct GymBuffModifier
{
    public readonly GymBuffStat stat;
    public readonly float multiplier;

    public GymBuffModifier(GymBuffStat stat, float multiplier)
    {
        this.stat = stat;
        this.multiplier = multiplier;
    }
}

/// <summary>One flavour or pack of a product, with its own shelf model.</summary>
public readonly struct GymProteinVariant
{
    /// <summary>Shelf product kind; also the GLB name.</summary>
    public readonly string modelId;
    public readonly string label;

    public GymProteinVariant(string modelId, string label)
    {
        this.modelId = modelId;
        this.label = label;
    }

    public string ModelPath => GymProteinShopCatalog.ModelFolder + modelId + ".glb";
}

/// <summary>
/// A product type sold at the protein.com counter. Every flavour and pack on
/// the shelves is a variant: same price and effect, its own model.
/// </summary>
public sealed class GymProteinProduct
{
    /// <summary>Stable type key (buffs refresh per type).</summary>
    public string id;
    public string displayName;
    public string category;
    public string effectText;
    public int stampPrice;
    public int unlockLevel;
    public float durationSeconds;
    public float instantHeal;
    public GymBuffModifier[] modifiers;
    // Pre-workout leaves a short crash once it wears off.
    public GymBuffModifier[] crashModifiers;
    public float crashSeconds;
    public GymProteinVariant[] variants;
}

/// <summary>
/// The counter shop's stock. Each variant's model is the shelf product itself,
/// exported by Assets/ProteinStore/v2/export_protein_products.py, so variant ids
/// must match its SHOP_KINDS list. Prices are loyalty-card stamps, not money.
/// </summary>
public static class GymProteinShopCatalog
{
    public const string ModelFolder = "BodyBuilders/protein_products/";
    public const int MaxActiveProducts = 3;

    public static readonly GymProteinProduct[] Products =
    {
        new GymProteinProduct
        {
            id = "tub_whey", displayName = "WHEY PROTEIN", category = "PROTEIN",
            effectText = "+15% EXERCISE PERFORMANCE", stampPrice = 2, unlockLevel = 1, durationSeconds = 300f,
            modifiers = new[] { new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.15f) },
            variants = new[]
            {
                new GymProteinVariant("pouch_choc", "CHOCOLATE POUCH"),
                new GymProteinVariant("pouch_vanilla", "VANILLA POUCH"),
                new GymProteinVariant("pouch_straw", "STRAWBERRY POUCH"),
                new GymProteinVariant("pouch_cookie", "COOKIES & CREAM POUCH"),
                new GymProteinVariant("pouch_banana", "BANANA POUCH"),
                new GymProteinVariant("pouch_caramel", "SALTED CARAMEL POUCH"),
                new GymProteinVariant("tub_whey", "CLASSIC TUB 1800 G"),
                new GymProteinVariant("tub_whey_choc", "CHOCOLATE TUB 1800 G")
            }
        },
        new GymProteinProduct
        {
            id = "pouch_isolate", displayName = "WHEY ISOLATE", category = "ISOLATE",
            effectText = "+10% PERFORMANCE   -30% FATIGUE", stampPrice = 3, unlockLevel = 4, durationSeconds = 240f,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.1f),
                new GymBuffModifier(GymBuffStat.FatigueGain, 0.7f)
            },
            variants = new[]
            {
                new GymProteinVariant("pouch_isolate", "DOUBLE CHOCOLATE")
            }
        },
        new GymProteinProduct
        {
            id = "tub_creatine", displayName = "CREATINE", category = "CREATINE",
            effectText = "+15% PUNCH DAMAGE AND FORCE", stampPrice = 3, unlockLevel = 5, durationSeconds = 240f,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.StrengthDamage, 1.15f),
                new GymBuffModifier(GymBuffStat.StrengthForce, 1.15f)
            },
            variants = new[]
            {
                new GymProteinVariant("pouch_rebel", "REBEL WATERMELON POUCH"),
                new GymProteinVariant("tub_creatine", "MONOHYDRATE 300 G"),
                new GymProteinVariant("tub_reload", "RELOAD+ 400 G")
            }
        },
        new GymProteinProduct
        {
            id = "tub_pre_green", displayName = "PRE-WORKOUT", category = "PRE-WORKOUT",
            effectText = "+20% SPRINT SPEED   -30% SPRINT DRAIN   THEN A SHORT CRASH", stampPrice = 4,
            unlockLevel = 8, durationSeconds = 180f,
            modifiers = new[]
            {
                new GymBuffModifier(GymBuffStat.SprintSpeed, 1.2f),
                new GymBuffModifier(GymBuffStat.SprintDrain, 0.7f)
            },
            crashModifiers = new[] { new GymBuffModifier(GymBuffStat.SprintSpeed, 0.9f) },
            crashSeconds = 30f,
            variants = new[]
            {
                new GymProteinVariant("tub_pre_green", "LIME"),
                new GymProteinVariant("tub_pre_pink", "BERRY"),
                new GymProteinVariant("tub_pre_blue", "ICE")
            }
        },
        new GymProteinProduct
        {
            id = "bar_choc", displayName = "PROTEIN BAR", category = "BARS",
            effectText = "+40 HP   DOUBLE HEALTH REGEN", stampPrice = 1, unlockLevel = 1, durationSeconds = 90f,
            instantHeal = 40f,
            modifiers = new[] { new GymBuffModifier(GymBuffStat.HealthRegen, 2f) },
            variants = new[]
            {
                new GymProteinVariant("bar_choc", "BANANA"),
                new GymProteinVariant("bar_coconut", "COCONUT"),
                new GymProteinVariant("bar_peanut", "PEANUT"),
                new GymProteinVariant("bar_berry", "BERRY")
            }
        },
        new GymProteinProduct
        {
            id = "can_lime", displayName = "ENERGY DRINK", category = "ENERGY",
            effectText = "+30% SPRINT CAPACITY", stampPrice = 2, unlockLevel = 2, durationSeconds = 180f,
            modifiers = new[] { new GymBuffModifier(GymBuffStat.SprintCapacity, 1.3f) },
            variants = new[]
            {
                new GymProteinVariant("can_lime", "ULTRA"),
                new GymProteinVariant("can_orange", "MANGO"),
                new GymProteinVariant("can_purple", "GRAPE"),
                new GymProteinVariant("can_red", "CHERRY")
            }
        },
        new GymProteinProduct
        {
            id = "rtd_choc", displayName = "PROTEIN SHAKE", category = "SHAKES",
            effectText = "+20 HP   +10% EXERCISE PERFORMANCE", stampPrice = 2, unlockLevel = 1,
            durationSeconds = 180f, instantHeal = 20f,
            modifiers = new[] { new GymBuffModifier(GymBuffStat.ExercisePerformance, 1.1f) },
            variants = new[]
            {
                new GymProteinVariant("rtd_choc", "CHOCOLATE 330 ML"),
                new GymProteinVariant("rtd_vanilla", "VANILLA 330 ML")
            }
        },
        new GymProteinProduct
        {
            id = "pb_jar", displayName = "PEANUT BUTTER", category = "SNACKS",
            effectText = "+25 HP", stampPrice = 1, unlockLevel = 1, durationSeconds = 0f,
            instantHeal = 25f, modifiers = new GymBuffModifier[0],
            variants = new[]
            {
                new GymProteinVariant("pb_jar", "SMOOTH 350 G")
            }
        },
        new GymProteinProduct
        {
            id = "vit_d3", displayName = "VITAMINS", category = "VITAMINS",
            effectText = "+25% CARDIO CAPACITY", stampPrice = 2, unlockLevel = 3, durationSeconds = 300f,
            modifiers = new[] { new GymBuffModifier(GymBuffStat.CardioCapacity, 1.25f) },
            variants = new[]
            {
                new GymProteinVariant("vit_d3", "D3 + K2"),
                new GymProteinVariant("omega3", "OMEGA 3"),
                new GymProteinVariant("zinc", "ZINC"),
                new GymProteinVariant("magnesium", "MAGNESIUM")
            }
        },
        new GymProteinProduct
        {
            id = "tub_bcaa", displayName = "BCAA", category = "AMINO",
            effectText = "-25% TECHNIQUE DIFFICULTY", stampPrice = 2, unlockLevel = 4, durationSeconds = 240f,
            modifiers = new[] { new GymBuffModifier(GymBuffStat.TechniqueDifficulty, 0.75f) },
            variants = new[]
            {
                new GymProteinVariant("tub_bcaa", "2:1:1  300 G")
            }
        },
    };

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

    /// <summary>
    /// Stamp price after Mark's opinion of you: a liked regular gets a stamp
    /// off the dearer products, a troublemaker pays one more.
    /// </summary>
    public static int GetPrice(GymProteinProduct product, int reputation)
    {
        if (product == null)
        {
            return 0;
        }
        int price = product.stampPrice;
        if (reputation >= 40 && price >= 3)
        {
            price--;
        }
        else if (reputation <= -40)
        {
            price++;
        }
        return Mathf.Max(1, price);
    }
}
