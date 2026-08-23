using UnityEngine;

public enum WorkoutResult
{
    None,
    Perfect,
    Good,
    Miss,
    AutoPerfect
}

/// <summary>
/// Small circular timing check used by strength reps and cardio cadence beats.
/// The marker loops around a ring; the narrow green segment is Perfect and the
/// wider amber segment is still acceptable.
/// </summary>
public sealed class TechniqueSkillCheck
{
    private const float Timeout = 4.5f;
    private float elapsed;
    private float markerSpeed;
    private int direction;
    private float difficulty;

    public bool IsActive { get; private set; }
    public float MarkerAngle { get; private set; }
    public float PerfectHalfAngle { get; private set; }
    public float AcceptableHalfAngle { get; private set; }

    public void Begin(int techniqueRank, float difficultyScale)
    {
        difficulty = Mathf.Max(0.75f, difficultyScale);
        int rank = Mathf.Clamp(techniqueRank, 0, GymExperienceService.MaxStatRank);
        PerfectHalfAngle = Mathf.Clamp(5.5f + rank * 1.15f, 5.5f, 12f) / difficulty;
        AcceptableHalfAngle = Mathf.Clamp(22f + rank * 1.8f, 22f, 33f) / difficulty;
        markerSpeed = Mathf.Clamp(286f * difficulty - rank * 12f, 128f, 360f);
        direction = Random.value > 0.5f ? 1 : -1;
        MarkerAngle = Random.Range(0f, 360f);
        elapsed = 0f;
        IsActive = true;
    }

    public WorkoutResult Tick(float deltaTime, bool actionPressed)
    {
        if (!IsActive)
        {
            return WorkoutResult.None;
        }

        elapsed += Mathf.Max(0f, deltaTime);
        MarkerAngle = Mathf.Repeat(MarkerAngle + markerSpeed * direction * deltaTime, 360f);
        if (actionPressed)
        {
            return Resolve();
        }

        if (elapsed >= Timeout)
        {
            IsActive = false;
            return WorkoutResult.Miss;
        }

        return WorkoutResult.None;
    }

    public WorkoutResult Resolve()
    {
        if (!IsActive)
        {
            return WorkoutResult.None;
        }

        float distanceFromPerfect = Mathf.Abs(Mathf.DeltaAngle(MarkerAngle, 0f));
        IsActive = false;
        if (distanceFromPerfect <= PerfectHalfAngle)
        {
            return WorkoutResult.Perfect;
        }
        if (distanceFromPerfect <= AcceptableHalfAngle)
        {
            return WorkoutResult.Good;
        }
        return WorkoutResult.Miss;
    }

    public void DrawGUI(Rect rect)
    {
        if (!IsActive)
        {
            return;
        }

        Texture2D ring = TechniqueSkillCheckTextures.GetRing();
        GUI.DrawTexture(rect, ring, ScaleMode.ScaleToFit, true);

        float radius = Mathf.Min(rect.width, rect.height) * 0.39f;
        Vector2 center = rect.center;
        float radians = MarkerAngle * Mathf.Deg2Rad;
        Vector2 marker = center + new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * radius;
        GUI.color = new Color(1f, 0.95f, 0.8f, 1f);
        GUI.DrawTexture(new Rect(marker.x - 6f, marker.y - 6f, 12f, 12f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle label = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        label.normal.textColor = new Color(1f, 0.86f, 0.4f);
        GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.42f, rect.width, 24f), "SPACE", label);
    }
}

internal static class TechniqueSkillCheckTextures
{
    private static Texture2D ring;

    public static Texture2D GetRing()
    {
        if (ring != null)
        {
            return ring;
        }

        const int size = 256;
        ring = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Technique Timing Ring",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 offset = new Vector2(x, y) - center;
                float distance = offset.magnitude;
                if (distance < 94f || distance > 108f)
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float angle = Mathf.Abs(Mathf.DeltaAngle(
                    Mathf.Atan2(offset.x, -offset.y) * Mathf.Rad2Deg, 0f));
                Color color = angle <= 8.5f
                    ? new Color(0.26f, 0.95f, 0.52f, 0.98f)
                    : angle <= 27f
                        ? new Color(1f, 0.68f, 0.18f, 0.94f)
                        : new Color(0.34f, 0.44f, 0.58f, 0.82f);
                pixels[y * size + x] = color;
            }
        }
        ring.SetPixels32(pixels);
        ring.Apply(false, true);
        return ring;
    }
}
