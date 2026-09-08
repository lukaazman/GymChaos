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
        bool changed = !hasAppliedState || nextShirt != currentShirt ||
            nextHeadwear != currentHeadwear;
        currentShirt = nextShirt;
        currentHeadwear = nextHeadwear;
        hasAppliedState = true;

        if (changed)
        {
            visualReady = false;
            ClearHeadwear();
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

    private static void FitHeadwearToPlayerHead(
        GameObject loaded, Transform head, GymHeadwear headwear)
    {
        Renderer[] renderers = loaded.GetComponentsInChildren<Renderer>(true);
        if (!TryGetRendererBounds(renderers, out Bounds sourceBounds))
        {
            return;
        }

        HeadwearFit fit = GetHeadwearFit(headwear);
        float playerHeight = ExternalRiggedCharacterVisual.StandardGameplayHeight;
        float sourceWidth = Mathf.Max(0.001f,
            Mathf.Max(sourceBounds.size.x, sourceBounds.size.z));
        loaded.transform.localScale *= playerHeight * fit.Width / sourceWidth;
        loaded.transform.localRotation = Quaternion.Euler(fit.EulerAngles);

        if (!TryGetRendererBounds(renderers, out Bounds fittedBounds))
        {
            return;
        }

        Vector3 desiredBottomCenter = head.position +
            head.up * (playerHeight * fit.BottomOffset) +
            head.forward * (playerHeight * fit.ForwardOffset);
        Vector3 currentBottomCenter = new Vector3(
            fittedBounds.center.x, fittedBounds.min.y, fittedBounds.center.z);
        loaded.transform.position += desiredBottomCenter - currentBottomCenter;
    }

    private static HeadwearFit GetHeadwearFit(GymHeadwear headwear)
    {
        switch (headwear)
        {
            case GymHeadwear.Beanie:
                return new HeadwearFit { Width = 0.122f, BottomOffset = 0.018f,
                    ForwardOffset = -0.002f, EulerAngles = Vector3.zero };
            case GymHeadwear.Headband:
                return new HeadwearFit { Width = 0.113f, BottomOffset = 0.025f,
                    ForwardOffset = 0f, EulerAngles = Vector3.zero };
            case GymHeadwear.Visor:
                return new HeadwearFit { Width = 0.145f, BottomOffset = 0.032f,
                    ForwardOffset = 0.004f, EulerAngles = Vector3.zero };
            case GymHeadwear.Cap:
            default:
                return new HeadwearFit { Width = 0.142f, BottomOffset = 0.03f,
                    ForwardOffset = 0.006f, EulerAngles = Vector3.zero };
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
            case GymHeadwear.Visor:
                return "BodyBuilders/wearables/bucket_hat.glb";
            default:
                return string.Empty;
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
