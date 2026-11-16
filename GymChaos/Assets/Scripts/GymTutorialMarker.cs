using UnityEngine;

/// <summary>
/// The world half of a tutorial step: a yellow column and a ring on the target
/// and dashes on the floor between the player and the target. The screen card
/// already says where to go, so nothing floats in the air above the goal: a
/// distant bobbing marker used to read as a second, misplaced sign. One
/// material, no colliders, no allocations per frame.
/// </summary>
public sealed class GymTutorialMarker : MonoBehaviour
{
    public const int ChevronTotal = 8;
    public const float ChevronSpacing = 1.5f;
    /// <summary>First dash sits this far ahead of the player.</summary>
    public const float ChevronLead = 1.1f;
    /// <summary>And the dashes stop this far short of the goal.</summary>
    public const float ChevronStop = 1.2f;
    public const float CloseDistance = 4f;
    /// <summary>Inside this the floor mark steps aside: the player is already there.</summary>
    public const float HideWithinDistance = 2.6f;

    // A warm yellow reads as a floor guide on the gym's navy rubber; the gold
    // UI accent is too close to the painted floor stripes to stand out.
    private static readonly Color DashYellow = new Color(1f, 0.86f, 0.12f, 1f);

    private Transform pillar;
    private Transform ring;
    private readonly Transform[] chevrons = new Transform[ChevronTotal];
    private Material material;
    private Vector3 target;

    public bool IsVisible { get; private set; }
    public int VisibleChevrons { get; private set; }
    public Vector3 TargetPosition => target;
    public bool PillarActive => pillar != null && pillar.gameObject.activeSelf;
    public bool RingActive => ring != null && ring.gameObject.activeSelf;

    public static GymTutorialMarker Create(Transform parent)
    {
        GameObject root = new GameObject("Tutorial Marker");
        root.transform.SetParent(parent, false);
        GymTutorialMarker marker = root.AddComponent<GymTutorialMarker>();
        marker.Build();
        marker.Hide();
        return marker;
    }

    private void Build()
    {
        Shader shader = FallbackShader();
        if (shader != null)
        {
            material = new Material(shader) { name = "Tutorial Marker Yellow" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", DashYellow);
            if (material.HasProperty("_Color")) material.SetColor("_Color", DashYellow);
        }

        pillar = Primitive("Column", PrimitiveType.Cylinder,
            new Vector3(0.35f, 1.5f, 0.35f), Vector3.up * 1.5f);
        ring = Primitive("Ring", PrimitiveType.Cylinder,
            new Vector3(2.4f, 0.02f, 2.4f), Vector3.up * 0.03f);

        for (int i = 0; i < ChevronTotal; i++)
        {
            Transform chevron = Primitive("Chevron " + (i + 1), PrimitiveType.Cube,
                new Vector3(0.22f, 0.02f, 0.22f), Vector3.zero);
            chevron.localRotation = Quaternion.Euler(0f, 45f, 0f);
            chevrons[i] = chevron;
        }
    }

    private static Shader FallbackShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return shader;
    }

    private Transform Primitive(string name, PrimitiveType type, Vector3 scale, Vector3 localPosition)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        part.transform.SetParent(transform, false);
        part.transform.localScale = scale;
        part.transform.localPosition = localPosition;
        return part.transform;
    }

    public void Show(Vector3 point, Vector3 from)
    {
        // A step target is usually an object pivot above or below the floor: put
        // the column and the ring on the ground it stands on, and when nothing
        // is under it keep them at the player's own floor height rather than at
        // whatever height the target object happened to have.
        point.y = GroundHeight(point, from.y);
        target = point;
        Vector2 flat = new Vector2(point.x - from.x, point.z - from.z);
        if (flat.magnitude < HideWithinDistance)
        {
            // Standing on the spot: the ring and the column used to surround the
            // camera here, so the mark gets out of the way and the card carries
            // the step.
            Hide();
            return;
        }
        IsVisible = true;
        gameObject.SetActive(true);
        if (pillar != null)
        {
            pillar.gameObject.SetActive(true);
            pillar.localPosition = Vector3.up * 1.5f;
        }
        if (ring != null)
        {
            ring.gameObject.SetActive(true);
            ring.position = new Vector3(point.x, point.y + 0.03f, point.z);
        }
        transform.position = new Vector3(point.x, point.y, point.z);
        UpdateChevrons(from, point);
    }

    public void Hide()
    {
        IsVisible = false;
        VisibleChevrons = 0;
        if (pillar != null) pillar.gameObject.SetActive(false);
        if (ring != null) ring.gameObject.SetActive(false);
        for (int i = 0; i < chevrons.Length; i++)
        {
            if (chevrons[i] != null) chevrons[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Dashes from the player toward the target, only while it is far away.</summary>
    public void UpdateChevrons(Vector3 from, Vector3 point)
    {
        Vector3 delta = point - from;
        delta.y = 0f;
        float distance = delta.magnitude;
        bool far = distance > CloseDistance;
        Vector3 direction = far ? delta / distance : Vector3.zero;
        // The dashes fill only the gap between the player and the target: they
        // start a step ahead of the player and stop short of the goal. Spacing
        // them 1.5 m apart from a fixed origin used to send the last dashes past
        // the target and through the floor of whatever room came next.
        int shown = 0;
        if (far)
        {
            float span = Mathf.Max(0f, distance - ChevronLead - ChevronStop);
            shown = Mathf.Clamp(Mathf.FloorToInt(span / ChevronSpacing) + 1, 1, ChevronTotal);
        }
        for (int i = 0; i < chevrons.Length; i++)
        {
            Transform chevron = chevrons[i];
            if (chevron == null)
            {
                continue;
            }
            bool visible = i < shown;
            if (chevron.gameObject.activeSelf != visible)
            {
                chevron.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                continue;
            }
            Vector3 step = from + direction * (ChevronLead + i * ChevronSpacing);
            step.y = GroundHeight(step, point.y) + 0.05f;
            chevron.position = step;
            chevron.rotation = Quaternion.LookRotation(direction, Vector3.up) *
                Quaternion.Euler(0f, 0f, 45f);
        }
        VisibleChevrons = shown;
    }

    // Reused by every ray: GroundHeight runs once per chevron per frame, and a
    // fresh array per call would garbage-collect while the tutorial walks.
    // Main thread only, like every other Unity physics query here.
    private static readonly RaycastHit[] GroundHits = new RaycastHit[32];

    /// <summary>
    /// Floor under a point. Characters are skipped: a single ray used to hit the
    /// player's own capsule when the player stood on the mark, which lifted the
    /// ring and the column to head height and drove the column into the camera.
    /// </summary>
    private static float GroundHeight(Vector3 point, float fallback)
    {
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 2.5f, Vector3.down, GroundHits,
            12f, ~0, QueryTriggerInteraction.Ignore);
        float floor = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = GroundHits[i].collider;
            if (collider == null) continue;
            if (collider.GetComponentInParent<PlayerMovement>() != null) continue;
            if (collider.GetComponentInParent<EnemyFighter>() != null) continue;
            if (GroundHits[i].point.y < floor) floor = GroundHits[i].point.y;
        }
        return float.IsInfinity(floor) ? fallback : floor;
    }

    /// <summary>The target point dropped onto the floor beneath it.</summary>
    public static Vector3 SnapToGround(Vector3 point)
    {
        point.y = GroundHeight(point, point.y);
        return point;
    }
}
