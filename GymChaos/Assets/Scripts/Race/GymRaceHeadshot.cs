using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Head-only portraits of the racers for the standings board. The character
/// is rendered alone on the preview layer twice, over black and over white;
/// the difference gives a clean alpha so the head has no background.
/// </summary>
public static class GymRaceHeadshot
{
    private const int Size = 128;
    private static readonly Dictionary<string, Texture2D> fixedHeads = new Dictionary<string, Texture2D>();

    /// <summary>The bake tool sets this to render live instead of loading the committed portraits.</summary>
    public static bool LiveOnly;

    public static string FileKey(string racerName)
    {
        System.Text.StringBuilder key = new System.Text.StringBuilder(racerName.Length);
        foreach (char c in racerName) key.Append(char.IsLetterOrDigit(c) ? c : '_');
        return key.ToString();
    }

    /// <summary>The committed head-on portrait (black eye bar) of this racer, shared and never destroyed; null when none is baked.</summary>
    public static Texture2D Fixed(string racerName)
    {
        if (LiveOnly) return null;
        if (!fixedHeads.TryGetValue(racerName, out Texture2D head) || head == null)
        {
            head = Resources.Load<Texture2D>("UI/RaceHeads/" + FileKey(racerName));
            fixedHeads[racerName] = head;
        }
        return head;
    }

    public static bool IsFixed(Texture2D texture) => texture != null && fixedHeads.ContainsValue(texture);
    private const int CaptureLayer = GymProductInspectView.PreviewLayer;

    /// <summary>Returns a transparent head portrait, or null when nothing rendered (no GPU, no body).</summary>
    public enum FaceSearch
    {
        /// <summary>Shoot along the given facing.</summary>
        None,
        /// <summary>Pick the side with the most dark face features (eyes, mouth) on a plain head.</summary>
        DarkFeatures
    }

    /// <param name="faceFollowsFacing">True when the model faces its travel direction (the flyer); car rigs face sideways and are told apart by the bar's offset from the head.</param>
    public static Texture2D Capture(Transform character, Vector3 facing, bool faceFollowsFacing = false)
    {
        if (character == null)
        {
            return null;
        }
        // Goku's aura sheets are effects around the body: they would fill the frame.
        Renderer[] renderers = System.Array.FindAll(character.GetComponentsInChildren<Renderer>(false),
            r => r.name.IndexOf("Aura", System.StringComparison.OrdinalIgnoreCase) < 0);
        // The black eye bar sits on the face, parented to the head bone: the
        // line from the head to the bar is the face direction on every rig.
        FaceCensorSettings bar = character.GetComponentInChildren<FaceCensorSettings>(false);
        Renderer barRenderer = bar != null ? bar.GetComponentInChildren<Renderer>(false) : null;
        if (barRenderer != null && renderers.Length > 0)
        {
            // The bar's pivot is on the bone; its quad is offset onto the face.
            Vector3 barCenter = barRenderer.bounds.center;
            Vector3 headBone = FindHead(renderers, character);
            // The bar is a flat strip across the eyes: its normal is exactly
            // where the face looks, whatever the bone offsets are.
            // Which way the face looks: the bar sits only a little in front of the head bone (and above it on a
            // tilted head), so travel direction decides first, the bar offset only when none is given.
            Vector3 outward = Vector3.ProjectOnPlane(barCenter - headBone, Vector3.up);
            Vector3 look = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (TryBarFrame(barRenderer, faceFollowsFacing && look.sqrMagnitude > 1e-4f || outward.sqrMagnitude < 1e-4f ? look : outward, out Vector3 normal, out Vector3 barUp))
            {
                // Head-on to the bar: the camera looks straight along its normal with the strip level,
                // so a tilted or bowed head (seated driver, flyer) still gives a straight portrait.
                camUp = barUp;
                fullFront = true;
                // Goku's bar is half as wide again (bigger hair): step back a little, the face itself is no bigger.
                shotScale = Mathf.Max(1f, Mathf.Sqrt(barRenderer.GetComponent<MeshFilter>().sharedMesh.bounds.size.x * barRenderer.transform.lossyScale.x / 0.31f));
                try
                {
                    // Eyes a little above the middle of the frame, whatever the hair does.
                    return Capture(renderers, normal.normalized, character, FaceSearch.None,
                        barCenter - normal.normalized * 0.1f - barUp * EyeDrop);
                }
                finally
                {
                    camUp = Vector3.up;
                    fullFront = false;
                    shotScale = 1f;
                }
            }
        }
        return Capture(renderers, facing, character, FaceSearch.None);
    }

    /// <summary>Portrait of exactly these renderers (e.g. the player's full body without first-person arms).</summary>
    public static Texture2D Capture(IReadOnlyList<Renderer> renderers, Vector3 facing, Transform skeleton = null,
        FaceSearch search = FaceSearch.None, Vector3? headCenter = null)
    {
        if (renderers == null || renderers.Count == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return null;
        }
        Vector3 head = headCenter ?? FindHead(renderers, skeleton);
        Vector3 forward = fullFront ? facing : Vector3.ProjectOnPlane(facing, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        // Only this character on the capture layer, fully lit.
        List<KeyValuePair<GameObject, int>> layers = new List<KeyValuePair<GameObject, int>>();
        List<KeyValuePair<Renderer, ShadowCastingMode>> shadowModes = new List<KeyValuePair<Renderer, ShadowCastingMode>>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer)
            {
                continue;
            }
            layers.Add(new KeyValuePair<GameObject, int>(renderer.gameObject, renderer.gameObject.layer));
            renderer.gameObject.layer = CaptureLayer;
            if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                // First-person bodies are shadow-only: show them for the shot.
                shadowModes.Add(new KeyValuePair<Renderer, ShadowCastingMode>(renderer, renderer.shadowCastingMode));
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        GameObject rig = new GameObject("Race Headshot Rig");
        Camera camera = rig.AddComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 1 << CaptureLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.fieldOfView = ShotFov;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 4f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        GameObject lightObject = new GameObject("Race Headshot Light");
        Light key = lightObject.AddComponent<Light>();
        key.type = LightType.Point;
        key.range = 3f;
        key.intensity = 2.2f;
        key.cullingMask = 1 << CaptureLayer;

        RenderTexture target = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
        Texture2D overBlack = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        Texture2D overWhite = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        Texture2D result = null;
        try
        {
            camera.targetTexture = target;
            Vector3 front = forward;
            if (search == FaceSearch.DarkFeatures)
            {
                // Centre the head first so the probes look at the face area.
                Texture2D first = Shoot(camera, rig.transform, lightObject.transform, target, overBlack, overWhite, head, forward);
                if (first != null && TryFrameOffset(first, out float firstRight, out float firstUp))
                {
                    head += Vector3.Cross(Vector3.up, -forward).normalized * firstRight + Vector3.up * firstUp;
                }
                if (first != null) Object.Destroy(first);
                // Head-on, the eyes and mouth sit symmetric about the middle:
                // among the views that show the features, take the one whose
                // dark features are best centred.
                Vector3[] views = new Vector3[Directions];
                int[] counts = new int[Directions];
                float[] offsets = new float[Directions];
                int most = 0;
                int peak = 0;
                for (int side = 0; side < Directions; side++)
                {
                    views[side] = Quaternion.AngleAxis(side * 360f / Directions, Vector3.up) * forward;
                    Texture2D probe = Shoot(camera, rig.transform, lightObject.transform, target, overBlack, overWhite, head, views[side]);
                    if (probe == null)
                    {
                        continue;
                    }
                    counts[side] = DarkFeatures(probe, out float centre);
                    offsets[side] = Mathf.Abs(centre - Size * 0.5f);
                    if (counts[side] > most)
                    {
                        most = counts[side];
                        peak = side;
                    }
                    Object.Destroy(probe);
                }
                float bestOffset = float.MaxValue;
                for (int side = 0; side < Directions; side++)
                {
                    // Only the half facing the feature peak: a dark collar seen
                    // from behind must not win.
                    int steps = Mathf.Abs(side - peak);
                    steps = Mathf.Min(steps, Directions - steps);
                    if (most > 0 && steps <= Directions / 4 && counts[side] >= most * 0.35f && offsets[side] < bestOffset)
                    {
                        bestOffset = offsets[side];
                        front = views[side];
                    }
                }
            }
            // Same straight-on angle for everyone; one pass re-centres the head.
            result = Shoot(camera, rig.transform, lightObject.transform, target, overBlack, overWhite, head, front);
            if (result != null && TryFrameOffset(result, out float right, out float up))
            {
                Object.Destroy(result);
                Vector3 cameraRight = Vector3.Cross(camUp, -front).normalized;
                // A known eye line keeps its height; only an unmarked head is
                // framed by its top.
                head += cameraRight * right + (headCenter.HasValue ? Vector3.zero : camUp * up);
                result = Shoot(camera, rig.transform, lightObject.transform, target, overBlack, overWhite, head, front);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(overBlack);
            Object.Destroy(overWhite);
            Object.Destroy(rig);
            Object.Destroy(lightObject);
            foreach (KeyValuePair<GameObject, int> entry in layers)
            {
                if (entry.Key != null) entry.Key.layer = entry.Value;
            }
            foreach (KeyValuePair<Renderer, ShadowCastingMode> entry in shadowModes)
            {
                if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
            }
        }
        return result;
    }

    private const int Directions = 24;

    private static Texture2D Shoot(Camera camera, Transform rig, Transform light, RenderTexture target,
        Texture2D overBlack, Texture2D overWhite, Vector3 head, Vector3 view)
    {
        // Level camera at head height: no look down onto the hair.
        rig.position = head + view * (ShotDistance * shotScale);
        rig.rotation = Quaternion.LookRotation(-view, camUp);
        light.position = head + view * 0.7f + camUp * 0.4f + Vector3.Cross(camUp, view) * 0.3f;
        Render(camera, target, Color.black, overBlack);
        Render(camera, target, Color.white, overWhite);
        return Matte(overBlack, overWhite);
    }

    // Camera up and view mode of the shot in progress (the bar's level and the full 3D head-on direction).
    private static Vector3 camUp = Vector3.up;
    private static bool fullFront;
    private static float shotScale = 1f;

    /// <summary>
    /// The eye bar is a box: longest side across the eyes, shortest its height, the middle one its depth.
    /// Returns the face direction (depth axis, turned outward from the head) and the bar's own up.
    /// </summary>
    private static bool TryBarFrame(Renderer bar, Vector3 outward, out Vector3 normal, out Vector3 up)
    {
        normal = outward;
        up = Vector3.up;
        MeshFilter filter = bar.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null) return false;
        Vector3 size = mesh.bounds.size;
        int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        int shortest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        if (longest == shortest) return false;
        int depth = 3 - longest - shortest;
        normal = bar.transform.TransformDirection(Axis(depth)).normalized;
        up = bar.transform.TransformDirection(Axis(shortest)).normalized;
        if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
        return true;
    }

    private static Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;


    private const float ShotDistance = 1.05f;
    // Frame centre below the eye line (m): eyes sit at about 60 % height.
    private const float EyeDrop = 0.05f;
    private const float ShotFov = 26f;

    /// <summary>Dark marks painted on the head (eyes, mouth) and their mean column.</summary>
    private static int DarkFeatures(Texture2D portrait, out float centre)
    {
        Color32[] pixels = portrait.GetPixels32();
        int count = 0;
        long sumX = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Color32 c = pixels[y * Size + x];
                // Painted on the head: bright head colour on both sides, so
                // dark clothes and hair outlines do not count.
                if (c.a > 200 && c.r + c.g + c.b < 180 &&
                    IsHeadTone(pixels, x - Gap, y) && IsHeadTone(pixels, x + Gap, y))
                {
                    count++;
                    sumX += x;
                }
            }
        }
        centre = count > 0 ? (float)sumX / count : Size * 0.5f;
        return count;
    }

    private const int Gap = 7;

    private static bool IsHeadTone(Color32[] pixels, int x, int y)
    {
        if (x < 0 || x >= Size)
        {
            return false;
        }
        Color32 c = pixels[y * Size + x];
        return c.a > 200 && c.r > 140 && c.r + c.g + c.b > 300;
    }

    /// <summary>World offset that puts the top of the head near the top edge and the head centred.</summary>
    private static bool TryFrameOffset(Texture2D portrait, out float right, out float up)
    {
        right = 0f;
        up = 0f;
        Color32[] pixels = portrait.GetPixels32();
        int top = -1;
        for (int y = Size - 1; y >= 0 && top < 0; y--)
        {
            for (int x = 0; x < Size; x++)
            {
                if (pixels[y * Size + x].a > 128)
                {
                    top = y;
                    break;
                }
            }
        }
        if (top < 0)
        {
            return false;
        }
        // Horizontal centre of the head band (top 40 % of the figure).
        long sumX = 0;
        int count = 0;
        int bottom = Mathf.Max(0, top - Mathf.RoundToInt(Size * 0.4f));
        for (int y = bottom; y <= top; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (pixels[y * Size + x].a > 128)
                {
                    sumX += x;
                    count++;
                }
            }
        }
        if (count == 0)
        {
            return false;
        }
        float metresPerPixel = 2f * ShotDistance * Mathf.Tan(ShotFov * 0.5f * Mathf.Deg2Rad) / Size;
        right = ((float)sumX / count - Size * 0.5f) * metresPerPixel;
        up = (top - Size * 0.9f) * metresPerPixel;
        return Mathf.Abs(right) > metresPerPixel * 2f || Mathf.Abs(up) > metresPerPixel * 2f;
    }

    private static void Render(Camera camera, RenderTexture target, Color background, Texture2D into)
    {
        camera.backgroundColor = background;
        camera.Render();
        RenderTexture.active = target;
        into.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
        into.Apply(false);
    }

    private static Texture2D Matte(Texture2D overBlack, Texture2D overWhite)
    {
        Color32[] black = overBlack.GetPixels32();
        Color32[] white = overWhite.GetPixels32();
        Color32[] output = new Color32[black.Length];
        int covered = 0;
        for (int i = 0; i < black.Length; i++)
        {
            Color32 b = black[i];
            Color32 w = white[i];
            // Over white the background adds (1 - alpha) to every channel.
            float spread = ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / (3f * 255f);
            float alpha = Mathf.Clamp01(1f - spread);
            if (alpha < 0.02f)
            {
                output[i] = new Color32(0, 0, 0, 0);
                continue;
            }
            covered++;
            output[i] = new Color32(
                (byte)Mathf.Clamp(b.r / alpha, 0f, 255f),
                (byte)Mathf.Clamp(b.g / alpha, 0f, 255f),
                (byte)Mathf.Clamp(b.b / alpha, 0f, 255f),
                (byte)Mathf.RoundToInt(alpha * 255f));
        }
        if (covered < black.Length / 50)
        {
            return null;
        }
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "Race Headshot",
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(output);
        texture.Apply(false, false);
        return texture;
    }

    private static Vector3 FindHead(IReadOnlyList<Renderer> renderers, Transform skeleton)
    {
        // Skinned bones first: rigs often live outside the renderer's branch.
        List<Transform> bones = new List<Transform>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer is SkinnedMeshRenderer skinned && skinned.bones != null)
            {
                foreach (Transform bone in skinned.bones)
                {
                    if (bone != null) bones.Add(bone);
                }
            }
        }
        if (skeleton != null)
        {
            bones.AddRange(skeleton.GetComponentsInChildren<Transform>(true));
        }
        Transform partial = null;
        for (int i = 0; i < bones.Count; i++)
        {
            string name = bones[i].name;
            if (string.Equals(name, "Head", System.StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(":Head", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "DEF-head", System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[i].position + Vector3.up * 0.08f;
            }
            if (partial == null && name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("end", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("top", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                partial = bones[i];
            }
        }
        if (partial != null)
        {
            return partial.position + Vector3.up * 0.08f;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return new Vector3(bounds.center.x, bounds.max.y - 0.14f, bounds.center.z);
    }
}
