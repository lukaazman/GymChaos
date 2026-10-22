using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hides the exterior fences, blockers and props that stand on the race road
/// (mostly the visitor-road walls around the start grid) and restores them
/// after the race. Flat ground surfaces stay: karts drive over them.
/// </summary>
public static class GymRaceCorridorClearance
{
    private const float Margin = 0.4f;
    private const float StandingHeight = 0.25f;
    // A renderer this large is a shared surface or a merged run; hiding it
    // would blank far more than the corridor.
    private const float MaxHiddenRendererSpan = 70f;

    private static readonly string[] Roots =
    {
        "Gym Exterior (Runtime)",
        "Davie Roadside Bus Stop (Runtime)",
        GymOutdoorFoliage.RootName,
        GymOutdoorFoliage.DetailsRootName,
        GymParkAccess.RootName
    };

    private static readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private static readonly List<Collider> disabledColliders = new List<Collider>();

    public static int HiddenRendererCount => hiddenRenderers.Count;
    public static int DisabledColliderCount => disabledColliders.Count;
    public static int SkippedLargeRenderers { get; private set; }

    public static void Clear()
    {
        Restore();
        SkippedLargeRenderers = 0;
        GymRaceCorridorMask mask = GymRaceCorridorMask.Build(Margin, 0.5f);
        float standing = GymRaceTrackLayout.FloorY + StandingHeight;
        for (int r = 0; r < Roots.Length; r++)
        {
            GameObject root = GameObject.Find(Roots[r]);
            if (root == null)
            {
                continue;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(false))
            {
                if (!collider.enabled || collider.isTrigger)
                {
                    continue;
                }
                Bounds b = collider.bounds;
                if (b.max.y <= standing || !mask.Overlaps(b.min.x, b.min.z, b.max.x, b.max.z))
                {
                    continue;
                }
                collider.enabled = false;
                disabledColliders.Add(collider);
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                // City towers are carved by GymRaceCityCarver instead.
                if (!renderer.enabled || renderer.gameObject.layer == GymCityDystopiaSurroundings.CityBackgroundLayer)
                {
                    continue;
                }
                Bounds b = renderer.bounds;
                // Flat ground stays (max at road level); a thin fence coping at
                // 1.35 m is not ground even though it is only 0.16 m thick.
                if (b.max.y <= standing ||
                    !mask.Overlaps(b.min.x, b.min.z, b.max.x, b.max.z))
                {
                    continue;
                }
                if (Mathf.Max(b.size.x, b.size.z) > MaxHiddenRendererSpan)
                {
                    SkippedLargeRenderers++;
                    Debug.Log($"GYMCHAOS_RACE_CORRIDOR_LARGE name={renderer.name} bounds={b}");
                    continue;
                }
                renderer.enabled = false;
                hiddenRenderers.Add(renderer);
            }
        }
        Debug.Log(
            $"GYMCHAOS_RACE_CORRIDOR_CLEARED colliders={disabledColliders.Count} " +
            $"renderers={hiddenRenderers.Count} skippedLarge={SkippedLargeRenderers}");
    }

    public static void Restore()
    {
        for (int i = 0; i < disabledColliders.Count; i++)
        {
            if (disabledColliders[i] != null)
            {
                disabledColliders[i].enabled = true;
            }
        }
        for (int i = 0; i < hiddenRenderers.Count; i++)
        {
            if (hiddenRenderers[i] != null)
            {
                hiddenRenderers[i].enabled = true;
            }
        }
        disabledColliders.Clear();
        hiddenRenderers.Clear();
    }
}
