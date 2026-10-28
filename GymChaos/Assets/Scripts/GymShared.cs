using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Small helpers that several runtime scripts had as identical private copies.</summary>
internal static class GymShared
{
    internal static void ApplyRendererBoundsToBox(Transform target, Renderer renderer, BoxCollider boxCollider)
    {
        Vector3 localCenter = target.InverseTransformPoint(renderer.bounds.center);
        Vector3 lossy = target.lossyScale;
        boxCollider.center = localCenter;
        boxCollider.size = new Vector3(
            renderer.bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
            renderer.bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.0001f),
            renderer.bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.0001f));
    }

    internal static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    internal static Bounds CalculateRendererBounds(GameObject target)
    {
        MeshRenderer[] renderers =
            target.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(target.transform.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    internal static void Select(Selectable selectable)
    {
        if (EventSystem.current != null && selectable != null)
        {
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }

    internal static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    internal static bool ComputeRoomFloorInHierarchy(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName.Contains("rubber floor") ||
                lowerName == "plane" || lowerName.StartsWith("plane("))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool IsPlateType(WeightType type)
    {
        return type == WeightType.Plate || type == WeightType.Plate5 || type == WeightType.Plate10 || type == WeightType.Plate20;
    }

    internal static void Center(RectTransform rect, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    internal static bool ReadEscape()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    internal static float Ease01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    internal static Bounds GetBoundsRelativeTo(Transform relativeTo, Renderer[] renderers)
    {
        Bounds result = new Bounds(relativeTo.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds bounds = renderers[i].bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                result.Encapsulate(relativeTo.InverseTransformPoint(new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z)));
            }
        }

        return result;
    }

    internal static void PlaceTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-right * scale, -top * scale);
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    internal static void FillOnFocus(Button button)
    {
        button.GetComponent<PersonaMenuButton>().SetFocusOutlineColor(PersonaMenuStyle.Accent);
    }

    internal static void CenterLabel(Button button)
    {
        Text label = button.GetComponentInChildren<Text>();
        label.alignment = TextAnchor.MiddleCenter;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
    }

    internal static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // glTF is right-handed (+Z forward), Unity left-handed: mirror X.
    internal static Vector3 ConvertGltfVector(Vector3 source)
    {
        return new Vector3(-source.x, source.y, source.z);
    }

    internal static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }

    /// <summary>UnityWebRequest URL for a file under StreamingAssets (file:/// on desktop, http on WebGL).</summary>
    internal static string JoinStreamingAssetsPath(string relativePath)
    {
        string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath;
        if (path.Contains("://"))
        {
            return path;
        }

        return "file:///" + path.Replace('\\', '/').TrimStart('/');
    }
}
