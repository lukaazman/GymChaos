using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps lawns and grass reading green after dark. The blue night ambient and
/// moonlight turned the park grey; a small emission from the vegetation's own
/// texture, scaled by the time of day, keeps the hue while it stays darker
/// than by day. Lighting and weather still change the look.
/// </summary>
public sealed class GymVegetationNightTint : MonoBehaviour
{
    // Emission strength by day and at full night, as a fraction of albedo.
    public const float DayEmission = 0.1f;
    public const float NightEmission = 0.26f;
    private const float UpdateInterval = 0.5f;

    private static readonly List<Material> Materials = new List<Material>();
    private static GymVegetationNightTint instance;
    private float nextUpdate;

    public static float CurrentEmissionForVerification { get; private set; }
    public static int MaterialCountForVerification => Materials.Count;

    public static void Register(Material material, Texture texture)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_EmissionMap"))
        {
            material.SetTexture("_EmissionMap", texture);
        }
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        Materials.RemoveAll(existing => existing == null);
        if (!Materials.Contains(material))
        {
            Materials.Add(material);
        }

        if (instance == null)
        {
            GameObject host = new GameObject("Vegetation Night Tint (Runtime)");
            instance = host.AddComponent<GymVegetationNightTint>();
        }
        Apply(CurrentDaylight());
    }

    private void Update()
    {
        if (Time.unscaledTime < nextUpdate)
        {
            return;
        }
        nextUpdate = Time.unscaledTime + UpdateInterval;
        Apply(CurrentDaylight());
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            Materials.Clear();
        }
    }

    private static float CurrentDaylight()
    {
        GymTimeOfDay time = GymTimeOfDay.Instance;
        return time != null ? time.Daylight01 : 1f;
    }

    private static void Apply(float daylight)
    {
        float strength = Mathf.Lerp(NightEmission, DayEmission, Mathf.Clamp01(daylight));
        CurrentEmissionForVerification = strength;
        Color emission = new Color(strength, strength, strength, 1f);
        for (int i = Materials.Count - 1; i >= 0; i--)
        {
            Material material = Materials[i];
            if (material == null)
            {
                Materials.RemoveAt(i);
                continue;
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
            }
        }
    }
}
