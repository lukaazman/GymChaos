using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Chaos race: a vehicle as big as the bus fills most of the chase view when
/// it cuts across the camera, so while it sits between the camera and the
/// player's kart it is drawn see-through instead of blanking the screen.
/// Every other car, and the whole normal race, keeps solid bodywork: only a
/// body that large gets the component at all.
/// </summary>
[DefaultExecutionOrder(210)]
public sealed class GymRaceVehicleFade : MonoBehaviour
{
    /// <summary>Opacity the body drops to while it blocks the view.</summary>
    public const float FadedAlpha = 0.5f;
    private const float FadeSpeed = 9f;
    /// <summary>Only a body at least this long is faded (the bus, not the cars).</summary>
    private const float BusSized = 8f;

    private GymRaceChaseCamera chase;
    private Transform cameraTransform;
    private Transform focus;
    private Renderer[] renderers;
    private Material[][] solid;
    private Material[][] faded;
    private ShadowCastingMode[] shadowModes;
    private bool[] skip;
    private Bounds bodyLocal;
    private bool measured;
    private bool isFaded;
    private float alpha = 1f;

    public bool IsFaded => isFaded;
    public float CurrentAlpha => alpha;

    public static GymRaceVehicleFade Attach(Transform holder, GymRaceChaseCamera chase, Transform focus)
    {
        if (holder == null || chase == null)
        {
            return null;
        }
        GymRaceVehicleFade fade = holder.gameObject.AddComponent<GymRaceVehicleFade>();
        fade.chase = chase;
        fade.focus = focus;
        return fade;
    }

    private void BuildMaterials()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        solid = new Material[renderers.Length][];
        faded = new Material[renderers.Length][];
        shadowModes = new ShadowCastingMode[renderers.Length];
        skip = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer ||
                renderer is TrailRenderer)
            {
                continue;
            }
            // Shadow-only proxies are a decimated, textureless white copy of
            // the body that exists to cast a cheap shadow. The fade leaves them
            // completely alone: any other cast mode (Off included) makes them
            // visible to the camera, and at half opacity that copy showed up as
            // white flakes all over the see-through bus.
            if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                skip[i] = true;
                continue;
            }
            // Remembered so the fade can hand every renderer back its own mode.
            shadowModes[i] = renderer.shadowCastingMode;
            Material[] source = renderer.sharedMaterials;
            solid[i] = source;
            Material[] seeThrough = new Material[source.Length];
            for (int m = 0; m < source.Length; m++)
            {
                seeThrough[m] = source[m] != null ? TransparentCopy(source[m]) : null;
            }
            faded[i] = seeThrough;
        }
    }

    /// <summary>The same surface, drawn over whatever is behind it (URP Lit
    /// and the built-in Standard shader both expose these properties).</summary>
    private static Material TransparentCopy(Material source)
    {
        Material copy = new Material(source) { name = source.name + " (race fade)" };
        if (copy.HasProperty("_Surface")) copy.SetFloat("_Surface", 1f);
        if (copy.HasProperty("_Blend")) copy.SetFloat("_Blend", 0f);
        if (copy.HasProperty("_SrcBlend")) copy.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (copy.HasProperty("_DstBlend")) copy.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        // Keep depth while blending: the bus body ships double-sided, and with
        // ZWrite off its own far side and interior blended through the near
        // shell as torn bright patches over the livery.
        if (copy.HasProperty("_ZWrite")) copy.SetFloat("_ZWrite", 1f);
        if (copy.HasProperty("_Mode")) copy.SetFloat("_Mode", 2f);   // built-in Standard: Fade
        copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        copy.SetOverrideTag("RenderType", "Transparent");
        copy.renderQueue = (int)RenderQueue.Transparent;
        SetAlpha(copy, FadedAlpha);
        return copy;
    }

    private static void SetAlpha(Material material, float alpha)
    {
        if (material.HasProperty("_BaseColor"))
        {
            Color colour = material.GetColor("_BaseColor");
            colour.a = alpha;
            material.SetColor("_BaseColor", colour);
        }
        if (material.HasProperty("_Color"))
        {
            Color colour = material.GetColor("_Color");
            colour.a = alpha;
            material.SetColor("_Color", colour);
        }
    }

    private void Measure()
    {
        if (measured)
        {
            return;
        }
        measured = true;
        bool any = false;
        // The model arrives from a GLB after this component is attached, so
        // collect what exists now; the build is retried until it is real.
        Renderer[] current = renderers != null ? renderers : GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in current)
        {
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer) continue;
            Bounds world = renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = world.center + Vector3.Scale(world.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                Vector3 local = transform.InverseTransformPoint(point);
                if (!any) bodyLocal = new Bounds(local, Vector3.zero);
                else bodyLocal.Encapsulate(local);
                any = true;
            }
        }
        // The model arrives from a GLB after this component is attached, so
        // the materials are collected once its renderers exist.
        measured = any && bodyLocal.size.sqrMagnitude > 0.01f;
        if (measured && solid == null)
        {
            BuildMaterials();
        }
    }

    /// <summary>Does the straight line from the camera to the player pass
    /// through this body (and not merely start inside it)?</summary>
    private bool Blocks(Vector3 cameraPosition, Vector3 focusPosition)
    {
        Measure();
        if (bodyLocal.size.sqrMagnitude < 0.01f)
        {
            return false;
        }
        // Only a bus-sized body can blanket the view; the cars never fade.
        if (bodyLocal.size.z < BusSized && bodyLocal.size.x < BusSized)
        {
            return false;
        }
        Vector3 a = transform.InverseTransformPoint(cameraPosition);
        Vector3 b = transform.InverseTransformPoint(focusPosition);
        Vector3 direction = b - a;
        float length = direction.magnitude;
        if (length < 0.01f)
        {
            return false;
        }
        direction /= length;
        float enter = 0f, exit = length;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = a[axis];
            float step = direction[axis];
            float min = bodyLocal.min[axis], max = bodyLocal.max[axis];
            if (Mathf.Abs(step) < 1e-5f)
            {
                if (origin < min || origin > max) return false;
                continue;
            }
            float t0 = (min - origin) / step;
            float t1 = (max - origin) / step;
            if (t0 > t1) { float swap = t0; t0 = t1; t1 = swap; }
            enter = Mathf.Max(enter, t0);
            exit = Mathf.Min(exit, t1);
            if (enter > exit) return false;
        }
        // Any overlap in front of the camera and short of the player counts,
        // including a camera that ends up inside the body.
        return exit > 0.3f && enter < length - 0.2f;
    }

    private void Apply(bool block, bool instant)
    {
        float target = block ? FadedAlpha : 1f;
        alpha = instant ? target : Mathf.MoveTowards(alpha, target, FadeSpeed * Time.unscaledDeltaTime);
        if (!instant && Mathf.Approximately(alpha, target))
        {
            alpha = target;
        }
        bool seeThrough = alpha < 0.995f;
        if (seeThrough != isFaded)
        {
            isFaded = seeThrough;
            SwapMaterials(seeThrough);
        }
        if (seeThrough)
        {
            for (int i = 0; i < (faded != null ? faded.Length : 0); i++)
            {
                Material[] set = faded[i];
                if (set == null) continue;
                for (int m = 0; m < set.Length; m++)
                {
                    if (set[m] != null) SetAlpha(set[m], alpha);
                }
            }
        }
    }

    private void SwapMaterials(bool seeThrough)
    {
        if (renderers == null)
        {
            return;
        }
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || (skip != null && skip[i])) continue;
            Material[] set = seeThrough ? faded[i] : solid[i];
            if (set != null) renderer.sharedMaterials = set;
            // A ghost body still dropping a solid shadow reads as a bug.
            renderer.shadowCastingMode = seeThrough
                ? ShadowCastingMode.Off
                : shadowModes != null ? shadowModes[i] : renderer.shadowCastingMode;
        }
    }

    private void LateUpdate()
    {
        // The chase camera takes over the player camera once the race starts:
        // resolve it then, not when the kart was built.
        cameraTransform = chase != null && chase.Target != null ? chase.Target.transform : null;
        if (cameraTransform == null)
        {
            return;
        }
        Vector3 at = focus != null ? focus.position + Vector3.up * 0.7f : transform.position;
        Apply(Blocks(cameraTransform.position, at), false);
    }

    /// <summary>One evaluation from an explicit camera pose, applied at once.</summary>
    public bool EvaluateForVerification(Vector3 cameraPosition, Vector3 focusPosition)
    {
        bool block = Blocks(cameraPosition, focusPosition);
        Apply(block, true);
        return block;
    }

    private void OnDestroy()
    {
        if (faded == null) return;
        for (int i = 0; i < faded.Length; i++)
        {
            if (faded[i] == null) continue;
            for (int m = 0; m < faded[i].Length; m++)
            {
                if (faded[i][m] != null) Destroy(faded[i][m]);
            }
        }
    }
}
