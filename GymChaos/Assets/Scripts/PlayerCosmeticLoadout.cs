using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds a deliberately small first cosmetic set directly onto the player's
/// runtime Mixamo rig. The overlays live on the mirror layer, so the same item
/// is visible in third-person, dialogue close-ups and the realtime mirror.
/// </summary>
public sealed class PlayerCosmeticLoadout : MonoBehaviour
{
    private PlayerMovement player;
    private Transform shirtOverlay;
    private Transform headwearOverlay;
    private Material shirtMaterial;
    private GymShirtColor currentShirt = GymShirtColor.Black;
    private GymHeadwear currentHeadwear = GymHeadwear.None;
    private bool initialized;

    public GymShirtColor CurrentShirt => currentShirt;
    public GymHeadwear CurrentHeadwear => currentHeadwear;
    public Color CurrentShirtMaterialColor => GetShirtColor(currentShirt);
    public Color RenderedShirtColor
    {
        get
        {
            Renderer renderer = shirtOverlay != null
                ? shirtOverlay.GetComponent<Renderer>()
                : null;
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null)
            {
                return Color.clear;
            }

            return material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.color;
        }
    }
    public bool IsShirtVisualReady => shirtOverlay != null &&
        shirtOverlay.gameObject.activeInHierarchy &&
        shirtOverlay.GetComponent<Renderer>() != null &&
        shirtOverlay.GetComponent<Renderer>().enabled;

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

        if (Enum.TryParse(state.shirt, true, out GymShirtColor shirt))
        {
            currentShirt = shirt;
        }
        if (Enum.TryParse(state.headwear, true, out GymHeadwear headwear))
        {
            currentHeadwear = headwear;
        }

        RefreshVisuals();
    }

    private void LateUpdate()
    {
        if (initialized && (shirtOverlay == null || headwearOverlay == null))
        {
            RefreshVisuals();
        }
    }

    private void RefreshVisuals()
    {
        Transform rig = transform.Find("PlayerAvatarRig");
        if (rig == null)
        {
            return;
        }

        Transform chest = FindBone(rig, "spine2", "spine1", "spine", "chest");
        Transform head = FindBone(rig, "head");
        if (chest != null && shirtOverlay == null)
        {
            shirtOverlay = CreateShirt(chest);
        }
        if (head != null && headwearOverlay == null)
        {
            headwearOverlay = CreateHeadwear(head);
        }

        if (shirtOverlay != null)
        {
            shirtOverlay.gameObject.SetActive(true);
            Renderer[] shirtRenderers = shirtOverlay.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < shirtRenderers.Length; i++)
            {
                ApplyShirtMaterial(shirtRenderers[i], currentShirt);
            }
        }
        if (headwearOverlay != null)
        {
            headwearOverlay.gameObject.SetActive(currentHeadwear != GymHeadwear.None);
            ConfigureHeadwear(headwearOverlay, currentHeadwear);
        }
    }

    private static Transform CreateShirt(Transform chest)
    {
        GameObject shirt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        shirt.name = "Player Cosmetic Shirt";
        shirt.transform.SetParent(chest, false);
        shirt.transform.localPosition = new Vector3(0f, 0.025f, 0.015f);
        shirt.transform.localRotation = Quaternion.identity;
        shirt.transform.localScale = new Vector3(0.36f, 0.31f, 0.23f);
        RemoveCollider(shirt);
        CreateShirtPanel(shirt.transform, "Player Cosmetic Shirt Front", 0.5f);
        CreateShirtPanel(shirt.transform, "Player Cosmetic Shirt Back", -0.5f);
        SetLayerRecursively(shirt.transform, PlanarGymMirror.MirrorPlayerLayer);
        return shirt.transform;
    }

    private static void CreateShirtPanel(Transform shirt, string name, float localZ)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        panel.transform.SetParent(shirt, false);
        panel.transform.localPosition = new Vector3(0f, 0.02f, localZ);
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(0.92f, 0.72f, 0.05f);
        RemoveCollider(panel);
    }

    private static Transform CreateHeadwear(Transform head)
    {
        GameObject hat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hat.name = "Player Cosmetic Headwear";
        hat.transform.SetParent(head, false);
        hat.transform.localPosition = new Vector3(0f, 0.13f, 0f);
        hat.transform.localRotation = Quaternion.identity;
        hat.transform.localScale = new Vector3(0.22f, 0.07f, 0.22f);
        RemoveCollider(hat);
        SetLayerRecursively(hat.transform, PlanarGymMirror.MirrorPlayerLayer);
        return hat.transform;
    }

    private static void ConfigureHeadwear(Transform overlay, GymHeadwear headwear)
    {
        if (overlay == null)
        {
            return;
        }

        Renderer renderer = overlay.GetComponent<Renderer>();
        if (headwear == GymHeadwear.Cap)
        {
            overlay.localScale = new Vector3(0.23f, 0.07f, 0.27f);
            overlay.localPosition = new Vector3(0f, 0.13f, 0.02f);
            ApplyMaterial(renderer, new Color(0.035f, 0.045f, 0.065f), "Cap");
        }
        else if (headwear == GymHeadwear.Beanie)
        {
            overlay.localScale = new Vector3(0.24f, 0.13f, 0.24f);
            overlay.localPosition = new Vector3(0f, 0.1f, 0f);
            ApplyMaterial(renderer, new Color(0.55f, 0.08f, 0.045f), "Beanie");
        }
        else if (headwear == GymHeadwear.Headband)
        {
            overlay.localScale = new Vector3(0.245f, 0.035f, 0.245f);
            overlay.localPosition = new Vector3(0f, 0.055f, 0f);
            ApplyMaterial(renderer, new Color(0.92f, 0.68f, 0.12f), "Headband");
        }
        else if (headwear == GymHeadwear.Visor)
        {
            overlay.localScale = new Vector3(0.265f, 0.045f, 0.31f);
            overlay.localPosition = new Vector3(0f, 0.075f, 0.06f);
            ApplyMaterial(renderer, new Color(0.1f, 0.8f, 0.78f), "Visor");
        }
    }

    private void ApplyShirtMaterial(Renderer renderer, GymShirtColor shirt)
    {
        if (renderer == null)
        {
            return;
        }

        Color color = GetShirtColor(shirt);
        if (shirtMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return;
            }

            // Keep a dedicated runtime material for the shirt. Reusing the
            // primitive's shared package material can make an outfit change
            // invisible or mutate the source asset used by other meshes.
            shirtMaterial = new Material(shader)
            {
                name = "Player Cosmetic Shirt"
            };
        }

        renderer.sharedMaterial = shirtMaterial;
        shirtMaterial.name = "Player Cosmetic Shirt " + shirt;
        shirtMaterial.color = color;
        if (shirtMaterial.HasProperty("_BaseColor"))
        {
            shirtMaterial.SetColor("_BaseColor", color);
        }
        if (shirtMaterial.HasProperty("_Color"))
        {
            shirtMaterial.SetColor("_Color", color);
        }
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
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

    private static void ApplyMaterial(Renderer renderer, Color color, string name)
    {
        if (renderer == null)
        {
            return;
        }

        Material material = renderer.sharedMaterial;
        if (material == null ||
            !material.name.StartsWith("Player Cosmetic ", StringComparison.Ordinal))
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return;
            }
            // Use a fresh shader material instead of copying the primitive's
            // package-backed Lit.mat. The overlay must never mutate an asset.
            material = new Material(shader);
            renderer.sharedMaterial = material;
        }
        material.name = "Player Cosmetic " + name;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static Transform FindBone(Transform root, params string[] candidates)
    {
        Transform[] bones = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            string normalized = Normalize(bones[i].name);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string candidate = Normalize(candidates[candidateIndex]);
                if (normalized == candidate || normalized.EndsWith(candidate, StringComparison.Ordinal))
                {
                    return bones[i];
                }
            }
        }
        return null;
    }

    private static string Normalize(string value)
    {
        return value.Replace("mixamorig:", string.Empty)
            .Replace("mixamorig", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.Destroy(collider);
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
}
