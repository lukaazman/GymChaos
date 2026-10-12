using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Treadmill console: matches the light grey motor hood to the dark frame and
/// draws a small live readout (speed, time, distance) on the screen panel in
/// the HUD's colours and font.
/// </summary>
[DisallowMultipleComponent]
public sealed class GymTreadmillConsole : MonoBehaviour
{
    private const float RefreshSeconds = 0.25f;
    private const float CanvasPixelWidth = 400f;
    private const float MaxDisplaySpeed = 18f;
    private static readonly Color ScreenPanel = new Color(0.02f, 0.025f, 0.04f, 1f);

    private GymExerciseStation station;
    private Renderer beltRenderer;
    private Renderer screenRenderer;
    private Text speedValue;
    private Text timeValue;
    private Text distanceValue;
    private Text stateLabel;
    private RectTransform speedBar;
    private float barWidth;
    private float nextRefresh;
    private float elapsed;
    private int shownSpeedTenths = -1;
    private int shownSeconds = -1;
    private int shownDistanceHundredths = -1;
    private bool shownActive;
    private bool built;

    internal Canvas ConsoleCanvasForVerification { get; private set; }
    internal Vector3 DisplayFaceNormalForVerification { get; private set; }
    internal Vector3 RunnerEyeForVerification { get; private set; }
    internal Renderer ScreenRendererForVerification => screenRenderer;
    internal int RecoloredHoodPartsForVerification { get; private set; }

    public void Bind(GymExerciseStation owner, Renderer belt)
    {
        station = owner;
        beltRenderer = belt;
    }

    private void Start()
    {
        BuildOnce();
    }

    private void BuildOnce()
    {
        if (built)
        {
            return;
        }
        built = true;
        Renderer frame = null;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            string lower = renderers[i].name.ToLowerInvariant();
            if (lower == "screen")
            {
                screenRenderer = renderers[i];
            }
            else if (frame == null && lower.StartsWith("side"))
            {
                frame = renderers[i];
            }
        }

        // The hood over the belt shipped with a plain light grey "Rubber"
        // slot that stood out against the dark frame.
        if (frame != null && frame.sharedMaterial != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                string lower = renderers[i].name.ToLowerInvariant();
                if (renderers[i] != beltRenderer &&
                    (lower.StartsWith("machine") || lower == "back"))
                {
                    renderers[i].sharedMaterial = frame.sharedMaterial;
                    RecoloredHoodPartsForVerification++;
                }
            }
        }

        if (screenRenderer != null)
        {
            // The authored glass block pokes half out of the console housing
            // and hid half of any readout placed on either. The readout's own
            // dark panel on the housing face replaces it.
            screenRenderer.enabled = false;
            BuildCanvas();
        }
    }

    private sealed class FaceGroup
    {
        public Vector3 normal;
        public float area;
        public float score;
        public Transform owner;
        public readonly List<Vector3> points = new List<Vector3>();
    }

    private void BuildCanvas()
    {
        Vector3 deck = beltRenderer != null ? beltRenderer.bounds.center : transform.position;
        Bounds screenBounds = screenRenderer.bounds;
        // The player's real first-person eye on this treadmill.
        Vector3 eye = new Vector3(deck.x, screenBounds.center.y, deck.z);
        if (station != null)
        {
            station.GetCameraPose(out Vector3 cameraLocal, out Quaternion _);
            eye = station.PlayerPosition + station.PlayerRotation * cameraLocal;
        }
        RunnerEyeForVerification = eye;

        // Put the readout on whichever flat face of the console housing looks
        // most directly at the runner.
        Bounds search = screenBounds;
        search.Expand(screenBounds.size.magnitude * 0.25f);
        var groups = new List<FaceGroup>();
        foreach (Renderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == beltRenderer || renderer == screenRenderer ||
                !renderer.bounds.Intersects(search))
            {
                continue;
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }
            CollectFaces(renderer.transform, mesh, eye, groups);
        }
        FaceGroup best = null;
        foreach (FaceGroup group in groups)
        {
            if (best == null || group.score > best.score)
            {
                best = group;
            }
        }
        if (best == null || best.score <= 0f)
        {
            return;
        }

        // Stay on the one flat face: where the housing curves, its edge
        // would rise in front of a larger panel and cut the readout.
        List<Vector3> displayPoints = best.points;
        Vector3 normal = best.normal;
        Vector3 right = Vector3.Cross(Vector3.up, normal);
        if (right.sqrMagnitude < 1e-6f)
        {
            right = transform.right;
        }
        right.Normalize();
        Vector3 planeUp = Vector3.Cross(normal, right).normalized;

        Vector3 origin = best.points[0];
        float minY = float.MaxValue, maxY = float.MinValue;
        float front = float.MinValue;
        foreach (Vector3 point in displayPoints)
        {
            Vector3 offset = point - origin;
            float y = Vector3.Dot(offset, planeUp);
            minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            front = Mathf.Max(front, Vector3.Dot(offset, normal));
        }
        // Width follows the screen itself; the housing face also spans the
        // side rails.
        float minX = float.MaxValue, maxX = float.MinValue;
        MeshFilter screenFilter = screenRenderer.GetComponent<MeshFilter>();
        Mesh screenMesh = screenFilter != null ? screenFilter.sharedMesh : null;
        if (screenMesh == null || !screenMesh.isReadable)
        {
            return;
        }
        foreach (Vector3 vertex in screenMesh.vertices)
        {
            float x = Vector3.Dot(screenRenderer.transform.TransformPoint(vertex) - origin, right);
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
        }
        float width = (maxX - minX) * 0.9f;
        float height = (maxY - minY) * 0.86f;
        if (width < 0.02f || height < 0.02f)
        {
            return;
        }
        DisplayFaceNormalForVerification = normal;

        var canvasObject = new GameObject("Treadmill Console", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ConsoleCanvasForVerification = canvas;
        RectTransform root = canvasObject.GetComponent<RectTransform>();
        float pixelHeight = CanvasPixelWidth * height / width;
        root.sizeDelta = new Vector2(CanvasPixelWidth, pixelHeight);
        float scale = width / CanvasPixelWidth;
        root.position = origin + right * ((minX + maxX) * 0.5f) +
            planeUp * ((minY + maxY) * 0.5f) + normal * (front + 0.025f);
        // A world-space canvas is seen from behind its forward axis.
        root.rotation = Quaternion.LookRotation(-normal, planeUp);
        root.localScale = Vector3.one * scale;
        root.SetParent(transform, true);

        // The display: a dark panel on the console face, the readout on top.
        Image panel = CreateImage(root, "Display", ScreenPanel);
        panel.rectTransform.anchorMin = Vector2.zero;
        panel.rectTransform.anchorMax = Vector2.one;
        panel.rectTransform.offsetMin = panel.rectTransform.offsetMax = Vector2.zero;

        Font font = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);
        float h = pixelHeight;
        float w = CanvasPixelWidth;
        float pad = Mathf.Round(h * 0.07f);
        int Size(float fraction) => Mathf.Max(8, Mathf.RoundToInt(h * fraction));

        // Top row: thin accent rule (HUD panel edge colour) and the state.
        Image rule = CreateImage(root, "Accent", PersonaMenuStyle.Accent);
        Place(rule.rectTransform, pad, h - pad - h * 0.035f, w * 0.08f, h * 0.025f);
        stateLabel = CreateText(root, "State", font, Size(0.11f), PersonaMenuStyle.Muted, TextAnchor.UpperRight);
        Place(stateLabel.rectTransform, w * 0.5f, h - pad - h * 0.13f, w * 0.5f - pad, h * 0.13f);

        // Speed: the one large figure, unit beside it.
        float speedBase = h * 0.31f;
        speedValue = CreateText(root, "Speed", font, Size(0.4f), PersonaMenuStyle.Ink, TextAnchor.LowerLeft);
        Place(speedValue.rectTransform, pad, speedBase, w * 0.6f, h * 0.44f);
        Text unit = CreateText(root, "Unit", font, Size(0.11f), PersonaMenuStyle.Muted, TextAnchor.LowerRight);
        Place(unit.rectTransform, w * 0.5f, speedBase + h * 0.03f, w * 0.5f - pad, h * 0.13f);
        unit.text = "KM/H";

        barWidth = w - pad * 2f;
        float barHeight = Mathf.Max(2f, h * 0.015f);
        Image track = CreateImage(root, "Speed Track", new Color(1f, 1f, 1f, 0.12f));
        Place(track.rectTransform, pad, speedBase - h * 0.05f, barWidth, barHeight);
        Image fill = CreateImage(root, "Speed Fill", PersonaMenuStyle.Gold);
        speedBar = fill.rectTransform;
        Place(speedBar, pad, speedBase - h * 0.05f, 0f, barHeight);

        // Bottom row: label and value on one line each side.
        timeValue = CreateText(root, "Time", font, Size(0.13f), PersonaMenuStyle.Ink, TextAnchor.LowerLeft);
        Place(timeValue.rectTransform, pad, pad, w * 0.5f - pad, h * 0.16f);
        distanceValue = CreateText(root, "Distance", font, Size(0.13f), PersonaMenuStyle.Ink, TextAnchor.LowerRight);
        Place(distanceValue.rectTransform, w * 0.5f, pad, w * 0.5f - pad, h * 0.16f);

        Refresh(true);
    }

    // Groups coplanar triangles that face the eye; score = area seen head-on.
    private static void CollectFaces(Transform node, Mesh mesh, Vector3 eye, List<FaceGroup> groups)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        var local = new List<FaceGroup>();
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            Vector3 a = node.TransformPoint(vertices[triangles[i]]);
            Vector3 b = node.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 c = node.TransformPoint(vertices[triangles[i + 2]]);
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float area = cross.magnitude * 0.5f;
            if (area < 1e-6f)
            {
                continue;
            }
            Vector3 n = cross.normalized;
            float facing = Vector3.Dot(n, (eye - (a + b + c) / 3f).normalized);
            if (facing < 0.35f)
            {
                continue;
            }
            FaceGroup group = null;
            foreach (FaceGroup candidate in local)
            {
                if (Vector3.Dot(candidate.normal, n) > 0.995f)
                {
                    group = candidate;
                    break;
                }
            }
            if (group == null)
            {
                group = new FaceGroup { normal = n, owner = node };
                local.Add(group);
            }
            group.area += area;
            group.score += area * facing;
            group.points.Add(a);
            group.points.Add(b);
            group.points.Add(c);
        }
        groups.AddRange(local);
    }

    private void Update()
    {
        if (station == null || speedValue == null)
        {
            return;
        }
        float speed = station.CurrentTreadmillSpeed;
        if (station.IsOccupied && speed > 0.1f)
        {
            elapsed += Time.deltaTime;
        }
        else if (!station.IsOccupied)
        {
            elapsed = 0f;
        }
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh(false);
        }
    }

    private void Refresh(bool force)
    {
        bool active = station != null && station.IsOccupied;
        float speed = station != null ? station.CurrentTreadmillSpeed : 0f;
        int speedTenths = Mathf.RoundToInt(speed * 10f);
        int seconds = Mathf.FloorToInt(elapsed);
        int distanceHundredths = station != null
            ? Mathf.FloorToInt(station.CurrentCardioDistance / 10f) : 0;
        if (force || active != shownActive)
        {
            shownActive = active;
            stateLabel.text = active ? "RUN" : "READY";
            stateLabel.color = active ? PersonaMenuStyle.Gold : PersonaMenuStyle.Muted;
        }
        if (force || speedTenths != shownSpeedTenths)
        {
            shownSpeedTenths = speedTenths;
            speedValue.text = (speedTenths / 10f).ToString("0.0", CultureInfo.InvariantCulture);
            Vector2 size = speedBar.sizeDelta;
            size.x = barWidth * Mathf.Clamp01(speed / MaxDisplaySpeed);
            speedBar.sizeDelta = size;
        }
        if (force || seconds != shownSeconds)
        {
            shownSeconds = seconds;
            timeValue.text = MutedLabel("TIME") +
                (seconds / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        if (force || distanceHundredths != shownDistanceHundredths)
        {
            shownDistanceHundredths = distanceHundredths;
            distanceValue.text = MutedLabel("KM") +
                (distanceHundredths / 100f).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    private static string mutedHex;

    private static string MutedLabel(string label)
    {
        if (mutedHex == null)
        {
            mutedHex = ColorUtility.ToHtmlStringRGB(PersonaMenuStyle.Muted);
        }
        return $"<color=#{mutedHex}>{label}</color>  ";
    }

    internal string ReadoutForVerification =>
        speedValue == null ? string.Empty :
        $"{stateLabel.text} {speedValue.text} {timeValue.text} {distanceValue.text}";

    private static Text CreateText(
        RectTransform parent, string name, Font font, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        return text;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // Bottom-left anchored placement in canvas pixels.
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
