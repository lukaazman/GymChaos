using System;
using UnityEngine;

/// <summary>
/// Applies shirt textures and supplied wearable GLBs to the animated player.
/// Wearables are parented to the runtime head bone so they follow every pose
/// and remain on the mirror player layer without procedural replacement meshes.
/// </summary>
public sealed class PlayerCosmeticLoadout : MonoBehaviour
{
    private struct HeadwearFit
    {
        public float Width;
        public float BottomOffset;
        public float ForwardOffset;
        public Vector3 EulerAngles;
    }

    private GymShirtColor currentShirt = GymShirtColor.Black;
    private GymHeadwear currentHeadwear = GymHeadwear.None;
    private bool initialized;
    private PlayerMovement player;
    private GameObject headwearRoot;
    private Texture currentTexture;
    private bool visualReady;
    private bool hasAppliedState;
    private bool headwearLoading;
    private int headwearRequestVersion;

    public GymShirtColor CurrentShirt => currentShirt;
    public GymHeadwear CurrentHeadwear => currentHeadwear;
    public Color CurrentShirtMaterialColor => GetShirtColor(currentShirt);
    public Color RenderedShirtColor => visualReady ? GetShirtColor(currentShirt) : Color.clear;
    public bool IsShirtVisualReady => visualReady;
    public bool IsHeadwearVisualReady =>
        currentHeadwear == GymHeadwear.None ||
        (!headwearLoading && HasVisibleHeadwearRenderer(headwearRoot));
    public string CurrentHeadwearAssetPath => GetHeadwearAssetPath(currentHeadwear);

    public void Initialize(PlayerMovement targetPlayer)
    {
        player = targetPlayer != null ? targetPlayer : GetComponent<PlayerMovement>();
        if (!initialized)
        {
            foreach (GymHeadwear item in Enum.GetValues(typeof(GymHeadwear)))
            {
                if (item == GymHeadwear.None) continue;
                RuntimeGlbModelLoader.Request(GetHeadwearAssetPath(item), transform,
                    transform.position, Quaternion.identity, Vector3.one,
                    "Preload Headwear " + item, PlanarGymMirror.MirrorPlayerLayer,
                    onLoaded: loaded =>
                    {
                        if (loaded == null) return;
                        loaded.SetActive(false);
                        Destroy(loaded);
                    });
            }
        }
        initialized = true;
    }

    public void ApplyFromState(GymProgressionState state)
    {
        if (state == null)
        {
            return;
        }

        GymShirtColor nextShirt = currentShirt;
        GymHeadwear nextHeadwear = currentHeadwear;
        Enum.TryParse(state.shirt, true, out nextShirt);
        Enum.TryParse(state.headwear, true, out nextHeadwear);
        bool headwearChanged = !hasAppliedState || nextHeadwear != currentHeadwear;
        bool changed = !hasAppliedState || nextShirt != currentShirt ||
            nextHeadwear != currentHeadwear;
        currentShirt = nextShirt;
        currentHeadwear = nextHeadwear;
        hasAppliedState = true;

        if (changed)
        {
            visualReady = false;
            if (headwearChanged) ClearHeadwear();
        }

        bool headwearNeedsVisual = currentHeadwear != GymHeadwear.None &&
            !headwearLoading && !HasVisibleHeadwearRenderer(headwearRoot);
        if (changed || !visualReady || headwearNeedsVisual)
        {
            ApplyVisuals();
            RequestMirrorRefresh();
        }
    }

    private void LateUpdate()
    {
        bool headwearNeedsVisual = currentHeadwear != GymHeadwear.None &&
            !headwearLoading && !HasVisibleHeadwearRenderer(headwearRoot);
        if (initialized && (!visualReady || headwearNeedsVisual) && !headwearLoading)
        {
            ApplyVisuals();
        }
    }

    private void ApplyVisuals()
    {
        if (player == null)
        {
            return;
        }

        PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>(true);
        if (rig == null)
        {
            return;
        }

        SkinnedMeshRenderer[] renderers =
            rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        currentTexture = Resources.Load<Texture2D>(
            currentShirt == GymShirtColor.Black
                ? "Characters/Textures/player_authored"
                : "Player/Outfits/shirt_" +
                    currentShirt.ToString().ToLowerInvariant());
        if (currentTexture == null)
        {
            visualReady = false;
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer == null ||
                renderer.gameObject.layer == PlanarGymMirror.FirstPersonPlayerLayer)
            {
                // Shirt cosmetics belong to the mirror body. The first-person
                // arm clone owns a stable skin material and must never inherit
                // the outfit texture (blue shirts previously tinted the arms).
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", currentTexture);
                }
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", currentTexture);
                }
            }
        }

        Transform head = rig.RuntimeHead;
        if (head == null)
        {
            Transform[] bones = rig.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null &&
                    (bones[i].name == "DEF-spine.005" ||
                        bones[i].name.Equals("head",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    head = bones[i];
                    break;
                }
            }
        }

        if (head == null)
        {
            visualReady = false;
            return;
        }

        if (currentHeadwear != GymHeadwear.None &&
            !headwearLoading && headwearRoot == null)
        {
            RequestHeadwear(head);
        }

        visualReady = true;
    }

    private void RequestHeadwear(Transform head)
    {
        string assetPath = GetHeadwearAssetPath(currentHeadwear);
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        int requestVersion = ++headwearRequestVersion;
        headwearLoading = true;
        RuntimeGlbModelLoader.Request(
            assetPath,
            null,
            head.position,
            head.rotation,
            Vector3.one,
            "Equipped " + currentHeadwear,
            PlanarGymMirror.MirrorPlayerLayer,
            onLoaded: loaded =>
            {
                if (requestVersion != headwearRequestVersion ||
                    head == null || this == null)
                {
                    if (loaded != null)
                    {
                        Destroy(loaded);
                    }
                    return;
                }

                if (loaded == null)
                {
                    headwearLoading = false;
                    visualReady = false;
                    Debug.LogError(
                        $"GYMCHAOS_HEADWEAR_LOAD_FAILED type={currentHeadwear} " +
                        $"asset={assetPath}", this);
                    return;
                }

                loaded.transform.SetParent(head, false);
                loaded.transform.localPosition = Vector3.zero;
                loaded.transform.localRotation = Quaternion.identity;
                loaded.transform.localScale = Vector3.one;
                FitHeadwearToPlayerHead(loaded, head, currentHeadwear);
                SetLayerRecursively(
                    loaded.transform, PlanarGymMirror.MirrorPlayerLayer);
                headwearRoot = loaded;
                headwearLoading = false;
                Debug.Log($"GYMCHAOS_HEADWEAR_READY type={currentHeadwear} asset={assetPath} parent={head.name} layer={loaded.layer}", loaded);
                RequestMirrorRefresh();
            });
    }

    private void ClearHeadwear()
    {
        ++headwearRequestVersion;
        headwearLoading = false;
        if (headwearRoot != null)
        {
            Destroy(headwearRoot);
            headwearRoot = null;
        }

        GameObject[] pending = FindObjectsByType<GameObject>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pending.Length; i++)
        {
            GameObject candidate = pending[i];
            if (candidate != null &&
                candidate.name == "Equipped " + currentHeadwear &&
                candidate.GetComponent<MeshRenderer>() == null &&
                candidate.transform.parent == null)
            {
                Destroy(candidate);
            }
        }
    }

    private static bool HasVisibleHeadwearRenderer(GameObject root)
    {
        if (root == null || !root.activeInHierarchy)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled &&
                renderers[i].gameObject.activeInHierarchy)
            {
                return true;
            }
        }
        return false;
    }

    private void FitHeadwearToPlayerHead(
        GameObject loaded, Transform head, GymHeadwear headwear)
    {
        // Mesh bounds must be measured in an upright frame, not in the
        // imported head bone's axes (the FBX head axis is not world up).
        HeadwearFit fit = GetHeadwearFit(headwear);
        Quaternion correction = headwear == GymHeadwear.Headband
            ? Quaternion.FromToRotation(new Vector3(-0.403753f, 0.914509f, 0.025610f), Vector3.up)
            : Quaternion.Euler(fit.EulerAngles);
        loaded.transform.rotation = player.transform.rotation * correction;
        Renderer[] renderers = loaded.GetComponentsInChildren<Renderer>(true);
        if (!TryGetWearableBounds(renderers, out Bounds sourceBounds))
        {
            return;
        }

        Bounds skull = GetSkullBounds(head);
        float sourceWidth = Mathf.Max(0.001f, sourceBounds.size.x);
        float targetWidth = Mathf.Max(0.15f, skull.size.x) * fit.Width;
        loaded.transform.localScale *= targetWidth / sourceWidth;

        if (!TryGetWearableBounds(renderers, out Bounds fittedBounds))
        {
            return;
        }

        float inset = skull.size.y * fit.BottomOffset;
        Vector3 desiredBottomCenter = player.transform.TransformPoint(new Vector3(
            skull.center.x, skull.max.y - inset,
            skull.center.z + skull.size.z * fit.ForwardOffset));
        Vector3 currentBottomCenter = new Vector3(
            fittedBounds.center.x, fittedBounds.min.y, fittedBounds.center.z);
        loaded.transform.position += desiredBottomCenter -
            player.transform.TransformPoint(currentBottomCenter);
    }

    private Bounds GetSkullBounds(Transform head)
    {
        Bounds result = new Bounds(player.transform.InverseTransformPoint(head.position) + Vector3.up * 0.12f,
            new Vector3(0.25f, 0.28f, 0.25f));
        bool found = false;
        foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.gameObject.layer != PlanarGymMirror.MirrorPlayerLayer ||
                renderer.sharedMesh == null) continue;
            var weights = renderer.sharedMesh.boneWeights;
            var bones = renderer.bones;
            Mesh baked = new Mesh();
            renderer.BakeMesh(baked, true);
            var vertices = baked.vertices;
            for (int i = 0; i < vertices.Length && i < weights.Length; i++)
            {
                var w = weights[i];
                float influence = HeadWeight(bones, head, w.boneIndex0, w.weight0) +
                    HeadWeight(bones, head, w.boneIndex1, w.weight1) +
                    HeadWeight(bones, head, w.boneIndex2, w.weight2) +
                    HeadWeight(bones, head, w.boneIndex3, w.weight3);
                if (influence < 0.5f) continue;
                Vector3 point = player.transform.InverseTransformPoint(
                    renderer.transform.TransformPoint(vertices[i]));
                if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                else result.Encapsulate(point);
            }
            Destroy(baked);
        }
        return result;
    }

    private bool TryGetWearableBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            // The headband is authored on a tilted plane. Rotating its AABB
            // measures empty corners and lifts the real band off the scalp.
            // Fit the actual vertices after the per-item rotation instead.
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 point = player.transform.InverseTransformPoint(
                    renderer.transform.TransformPoint(vertex));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        return found;
    }

    private static float HeadWeight(Transform[] bones, Transform head, int index, float weight)
    {
        if (index < 0 || index >= bones.Length || bones[index] == null) return 0f;
        return bones[index] == head || bones[index].IsChildOf(head) ? weight : 0f;
    }

    private static HeadwearFit GetHeadwearFit(GymHeadwear headwear)
    {
        switch (headwear)
        {
            case GymHeadwear.Beanie:
                return new HeadwearFit { Width = 1.12f, BottomOffset = 0.55f,
                    ForwardOffset = 0f, EulerAngles = Vector3.zero };
            case GymHeadwear.Headband:
                return new HeadwearFit { Width = 1.10f, BottomOffset = 0.52f,
                    ForwardOffset = 0f, EulerAngles = Vector3.zero };
            case GymHeadwear.BucketHat:
                return new HeadwearFit { Width = 1.65f, BottomOffset = 0.57f,
                    ForwardOffset = 0f, EulerAngles = Vector3.zero };
            case GymHeadwear.JollyCap:
                return new HeadwearFit { Width = 1.20f, BottomOffset = 0.54f,
                    ForwardOffset = 0.12f, EulerAngles = Vector3.zero };
            case GymHeadwear.Cap:
            default:
                return new HeadwearFit { Width = 1.12f, BottomOffset = 0.54f,
                    ForwardOffset = 0.20f, EulerAngles = Vector3.zero };
        }
    }

    private static bool TryGetRendererBounds(
        Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return found;
    }

    private static string GetHeadwearAssetPath(GymHeadwear headwear)
    {
        switch (headwear)
        {
            case GymHeadwear.Cap:
                return "BodyBuilders/wearables/baseball_cap.glb";
            case GymHeadwear.Beanie:
                return "BodyBuilders/wearables/beanie.glb";
            case GymHeadwear.Headband:
                return "BodyBuilders/wearables/headband.glb";
            case GymHeadwear.BucketHat:
                return "BodyBuilders/wearables/bucket_hat.glb";
            case GymHeadwear.JollyCap:
                return "BodyBuilders/wearables/jolly_cap.glb";
            default:
                return string.Empty;
        }
    }

    public static string GetHeadwearDisplayName(GymHeadwear item)
    {
        switch (item)
        {
            case GymHeadwear.Cap: return "Baseball cap";
            case GymHeadwear.Beanie: return "Beanie";
            case GymHeadwear.BucketHat: return "Bucket hat";
            case GymHeadwear.Headband: return "Headband";
            case GymHeadwear.JollyCap: return "Jolly cap";
            default: return "None";
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }

    private void OnDestroy()
    {
        ++headwearRequestVersion;
        if (headwearRoot != null)
        {
            Destroy(headwearRoot);
        }
    }

    private static void RequestMirrorRefresh()
    {
        PlanarGymMirror[] mirrors = UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < mirrors.Length; i++)
        {
            if (mirrors[i] != null)
            {
                mirrors[i].RequestImmediateRefresh();
            }
        }
    }

    private static Color GetShirtColor(GymShirtColor shirt)
    {
        return shirt == GymShirtColor.Black
            ? new Color(0.025f, 0.03f, 0.04f)
            : shirt == GymShirtColor.White
                ? new Color(0.9f, 0.92f, 0.9f)
                : shirt == GymShirtColor.Red
                    ? new Color(0.72f, 0.045f, 0.025f)
                    : shirt == GymShirtColor.Blue
                        ? new Color(0.035f, 0.22f, 0.78f)
                        : new Color(0.92f, 0.58f, 0.08f);
    }
}
