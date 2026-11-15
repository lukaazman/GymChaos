using UnityEngine;

/// <summary>
/// The world half of a tutorial step: a gold column and a ring on the target,
/// chevrons on the floor between the player and the target, and a bobbing
/// diamond above it. One material, no colliders, no allocations per frame.
/// </summary>
public sealed class GymTutorialMarker : MonoBehaviour
{
    public const int ChevronTotal = 8;
    public const float ChevronSpacing = 1.5f;
    public const float CloseDistance = 4f;
    /// <summary>Inside this the target is reachable, so the floating arrow goes away.</summary>
    public const float ArrowHideDistance = 2.5f;

    private Transform pillar;
    private Transform ring;
    private Transform diamond;
    private readonly Transform[] chevrons = new Transform[ChevronTotal];
    private Material material;
    private Vector3 target;
    private float bob;

    public bool IsVisible { get; private set; }
    public int VisibleChevrons { get; private set; }
    public Vector3 TargetPosition => target;
    public bool PillarActive => pillar != null && pillar.gameObject.activeSelf;
    public bool RingActive => ring != null && ring.gameObject.activeSelf;
    public bool ArrowActive => diamond != null && diamond.gameObject.activeSelf;

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
            material = new Material(shader) { name = "Tutorial Marker Gold" };
            Color gold = PersonaMenuStyle.Gold;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", gold);
            if (material.HasProperty("_Color")) material.SetColor("_Color", gold);
        }

        pillar = Primitive("Column", PrimitiveType.Cylinder,
            new Vector3(0.35f, 1.5f, 0.35f), Vector3.up * 1.5f);
        ring = Primitive("Ring", PrimitiveType.Cylinder,
            new Vector3(2.4f, 0.02f, 2.4f), Vector3.up * 0.03f);
        diamond = Primitive("Arrow", PrimitiveType.Cube,
            new Vector3(0.28f, 0.28f, 0.28f), Vector3.up * 2.4f);
        diamond.localRotation = Quaternion.Euler(45f, 45f, 0f);

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
        target = point;
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
        if (diamond != null)
        {
            diamond.gameObject.SetActive(Vector3.Distance(from, point) > ArrowHideDistance);
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
        if (diamond != null) diamond.gameObject.SetActive(false);
        for (int i = 0; i < chevrons.Length; i++)
        {
            if (chevrons[i] != null) chevrons[i].gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!IsVisible || diamond == null)
        {
            return;
        }
        bob += Time.deltaTime * 2.2f;
        diamond.localPosition = Vector3.up * (2.4f + Mathf.Sin(bob) * 0.15f);
        diamond.Rotate(Vector3.up, Time.deltaTime * 90f, Space.World);
    }

    /// <summary>Chevrons from the player toward the target, only while it is far away.</summary>
    public void UpdateChevrons(Vector3 from, Vector3 point)
    {
        Vector3 delta = point - from;
        delta.y = 0f;
        float distance = delta.magnitude;
        bool far = distance > CloseDistance;
        Vector3 direction = far ? delta / distance : Vector3.zero;
        if (diamond != null)
        {
            bool arrowOn = distance > ArrowHideDistance;
            if (diamond.gameObject.activeSelf != arrowOn)
            {
                diamond.gameObject.SetActive(arrowOn);
            }
        }
        int shown = far ? ChevronTotal : 0;
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
            Vector3 step = from + direction * (1.2f + i * ChevronSpacing);
            step.y = point.y + 0.04f;
            chevron.position = step;
            chevron.rotation = Quaternion.LookRotation(direction, Vector3.up) *
                Quaternion.Euler(0f, 0f, 45f);
        }
        VisibleChevrons = shown;
    }
}
