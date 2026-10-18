using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary supplement effects. Each active product contributes its
/// multipliers until it expires; the progression getters multiply by
/// <see cref="GetMultiplier"/>. Buying the same product again refreshes it
/// instead of stacking, at most <see cref="GymProteinShopCatalog.MaxActiveProducts"/>
/// products run at once and every stat is capped. Effects run on scaled game
/// time and are not saved: they end with the session.
/// </summary>
public static class GymBuffs
{
    public const float MaxBoost = 1.35f;
    public const float MaxReduction = 0.6f;
    public const float MaxHealthRegen = 2f;
    // Hank's gear lifts the caps: much stronger, and much worse.
    public const float GearMaxBoost = 1.8f;
    public const float GearMaxReduction = 0.4f;
    public const float GearMaxHealthRegen = 2.5f;
    public const float MaxHealthTollCap = 0.5f;

    /// <summary>Share of max health lost to gear for the rest of the session.</summary>
    public static float MaxHealthToll01 { get; private set; }

    public sealed class ActiveBuff
    {
        public GymProteinProduct product;
        public GymBuffModifier[] modifiers;
        public float endTime;
        public bool crash;

        public float Remaining => Mathf.Max(0f, endTime - Time.time);
    }

    private static readonly List<ActiveBuff> active = new List<ActiveBuff>();

    public static IReadOnlyList<ActiveBuff> Active
    {
        get
        {
            Prune();
            return active;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        MaxHealthToll01 = 0f;
    }

    public static void Clear()
    {
        active.Clear();
        MaxHealthToll01 = 0f;
    }

    /// <summary>True while any gear effect (or its crash) runs.</summary>
    public static bool IsGearActive
    {
        get
        {
            Prune();
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].product.gear)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Health per second the running gear costs.</summary>
    public static float HealthDrainPerSecond
    {
        get
        {
            Prune();
            float drain = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                if (!active[i].crash)
                {
                    drain += active[i].product.healthDrainPerSecond;
                }
            }
            return drain;
        }
    }

    public static int ActiveProductCount
    {
        get
        {
            Prune();
            int count = 0;
            for (int i = 0; i < active.Count; i++)
            {
                if (!active[i].crash)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public static bool IsActive(string productId) => Find(productId, false) != null;

    /// <summary>True when buying would refresh an effect or fits under the active cap.</summary>
    public static bool CanApply(GymProteinProduct product)
    {
        if (product == null)
        {
            return false;
        }
        if (product.durationSeconds <= 0f || IsActive(product.id))
        {
            return true;
        }
        return ActiveProductCount < GymProteinShopCatalog.MaxActiveProducts;
    }

    public static bool Apply(GymProteinProduct product, PlayerMovement player)
    {
        if (!CanApply(product))
        {
            return false;
        }
        if (product.maxHealthToll > 0f)
        {
            MaxHealthToll01 = Mathf.Min(MaxHealthTollCap, MaxHealthToll01 + product.maxHealthToll);
        }
        if (product.instantHeal > 0f && player != null)
        {
            player.RestoreHealth(product.instantHeal);
        }
        if (product.instantHarm > 0f && player != null)
        {
            player.ApplySideEffectDamage(product.instantHarm);
        }
        if (player != null)
        {
            player.EnforceHealthCeiling();
        }
        if (product.durationSeconds > 0f && product.modifiers != null && product.modifiers.Length > 0)
        {
            ActiveBuff existing = Find(product.id, false);
            if (existing != null)
            {
                existing.endTime = Time.time + product.durationSeconds;
            }
            else
            {
                // A fresh dose clears its own crash.
                ActiveBuff crash = Find(product.id, true);
                if (crash != null)
                {
                    active.Remove(crash);
                }
                active.Add(new ActiveBuff
                {
                    product = product,
                    modifiers = product.modifiers,
                    endTime = Time.time + product.durationSeconds
                });
            }
        }
        Debug.Log($"GYMCHAOS_BUFF_APPLIED product={product.id} active={ActiveProductCount} " +
            $"gear={product.gear} toll={MaxHealthToll01:F2}");
        return true;
    }

    public static float GetMultiplier(GymBuffStat stat)
    {
        Prune();
        float multiplier = 1f;
        for (int i = 0; i < active.Count; i++)
        {
            GymBuffModifier[] modifiers = active[i].modifiers;
            for (int m = 0; m < modifiers.Length; m++)
            {
                if (modifiers[m].stat == stat)
                {
                    multiplier *= modifiers[m].multiplier;
                }
            }
        }
        bool gear = IsGearActive;
        float max = stat == GymBuffStat.HealthRegen
            ? (gear ? GearMaxHealthRegen : MaxHealthRegen)
            : (gear ? GearMaxBoost : MaxBoost);
        return Mathf.Clamp(multiplier, gear ? GearMaxReduction : MaxReduction, max);
    }

    /// <summary>Verifier hook: ends every effect as if its time ran out.</summary>
    public static void ExpireAllForVerification()
    {
        for (int i = 0; i < active.Count; i++)
        {
            active[i].endTime = Time.time - 0.01f;
        }
        Prune();
    }

    private static ActiveBuff Find(string productId, bool crash)
    {
        Prune();
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].product.id == productId && active[i].crash == crash)
            {
                return active[i];
            }
        }
        return null;
    }

    private static void Prune()
    {
        float now = Time.time;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveBuff buff = active[i];
            if (buff.endTime > now)
            {
                continue;
            }
            active.RemoveAt(i);
            GymProteinProduct product = buff.product;
            if (!buff.crash && product.crashModifiers != null && product.crashModifiers.Length > 0 &&
                product.crashSeconds > 0f && buff.endTime + product.crashSeconds > now)
            {
                active.Add(new ActiveBuff
                {
                    product = product,
                    modifiers = product.crashModifiers,
                    endTime = buff.endTime + product.crashSeconds,
                    crash = true
                });
            }
        }
    }
}
