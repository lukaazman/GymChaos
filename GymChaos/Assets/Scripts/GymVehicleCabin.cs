using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime side of Tools/build_vehicle_cabins.py: the see-through cabin of a
/// driven car. Parts are named Group__Role__RRGGBB; this gives them their
/// materials (glass, flat interior colours), reads the tiny Marker__ meshes,
/// and gathers the driver door, the driver seat and the steering wheel under
/// pivots so the driver can open the door and be fitted to the seat/wheel.
/// </summary>
public sealed class GymVehicleCabin : MonoBehaviour
{
    // Wide open: a 2.5 m bodybuilder climbs out between door and pillar.
    public const float DoorOpenDegrees = 78f;
    // Authored rim radius (Tools/build_vehicle_cabins.py, 0.19 m on a 5 m car).
    public const float AuthoredWheelRadius = 0.19f;

    private static readonly Dictionary<string, Material> colourMaterials =
        new Dictionary<string, Material>();
    private static Material glassMaterial;

    private readonly Dictionary<string, Vector3> markers = new Dictionary<string, Vector3>();
    private Transform doorPivot;
    private Transform seatPivot;
    private Transform wheelPivot;
    private Quaternion doorClosedLocal;
    private Vector3 seatRestLocal;
    private Vector3 wheelRestLocal;
    private Quaternion wheelRestLocalRotation;
    private float doorSign = 1f;
    private float doorOpen01;

    public bool IsReady { get; private set; }
    public float DoorOpen01 => doorOpen01;
    // Hinge to rear edge along the car (vehicle-local z, signed).
    public float DoorReach { get; private set; }
    public float DoorOpenSign => doorSign;
    // Lowest point of the driver door skin: the top of the sill under it.
    public float DoorSillLocalY { get; private set; }
    public Transform WheelPivot => wheelPivot;
    public int GlassRendererCount { get; private set; }
    public int InteriorRendererCount { get; private set; }
    public int DoorRendererCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        colourMaterials.Clear();
        glassMaterial = null;
    }

    /// <summary>Vehicle-root-local marker position (Seat, Roof, Wheel, Floor, Hinge).</summary>
    public bool TryGetMarker(string name, out Vector3 local) => markers.TryGetValue(name, out local);

    public Vector3 MarkerWorld(string name) =>
        markers.TryGetValue(name, out Vector3 local) ? transform.TransformPoint(local) : transform.position;

    /// <summary>Sets up the cabin parts under <paramref name="visual"/>; null when the car has none.</summary>
    public static GymVehicleCabin Attach(Transform vehicleRoot, GameObject visual)
    {
        if (vehicleRoot == null || visual == null) return null;
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        bool hasCabin = false;
        for (int i = 0; i < renderers.Length && !hasCabin; i++)
        {
            hasCabin = PartName(renderers[i].name).StartsWith("Marker__", StringComparison.Ordinal);
        }
        if (!hasCabin) return null;
        GymVehicleCabin cabin = vehicleRoot.gameObject.GetComponent<GymVehicleCabin>() ??
            vehicleRoot.gameObject.AddComponent<GymVehicleCabin>();
        cabin.Build(renderers);
        return cabin;
    }

    // Unity and the GLB loader decorate names: "X.001", "X Primitive 0".
    public static string PartName(string name)
    {
        int primitive = name.IndexOf(" Primitive ", StringComparison.Ordinal);
        if (primitive >= 0) name = name.Substring(0, primitive);
        int dot = name.LastIndexOf('.');
        if (dot > 0 && dot >= name.Length - 4) name = name.Substring(0, dot);
        return name;
    }

    /// <summary>True for parts that keep the scan texture of the body.</summary>
    public static bool UsesScanTexture(string rendererName)
    {
        string name = PartName(rendererName);
        if (name.StartsWith("Marker__", StringComparison.Ordinal)) return false;
        int split = name.IndexOf("__", StringComparison.Ordinal);
        return split < 0 || name.EndsWith("__Shell", StringComparison.Ordinal);
    }

    private void Build(Renderer[] renderers)
    {
        markers.Clear();
        var doorParts = new List<Transform>();
        var seatParts = new List<Transform>();
        var wheelParts = new List<Transform>();
        GlassRendererCount = 0;
        InteriorRendererCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            string name = PartName(renderer.name);
            string[] parts = name.Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length < 2) continue;
            if (parts[0] == "Marker")
            {
                markers[parts[1]] = transform.InverseTransformPoint(renderer.bounds.center);
                renderer.enabled = false;
                continue;
            }
            string role = parts[1];
            if (role == "Glass")
            {
                renderer.sharedMaterial = GlassMaterial();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                GlassRendererCount++;
            }
            else if (role != "Shell" && parts.Length >= 3)
            {
                renderer.sharedMaterial = ColourMaterial(parts[2], role);
                // Small interior parts never cast: the body already shades them.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                InteriorRendererCount++;
            }
            if (parts[0] == "Door_Driver") doorParts.Add(renderer.transform);
            else if (parts[0] == "Driver_Seat") seatParts.Add(renderer.transform);
            else if (parts[0] == "Steering_Wheel") wheelParts.Add(renderer.transform);
        }
        DoorRendererCount = doorParts.Count;
        doorPivot = MakePivot("Driver Door Pivot", "Hinge", doorParts);
        seatPivot = MakePivot("Driver Seat Pivot", "Seat", seatParts);
        wheelPivot = MakePivot("Steering Wheel Pivot", "Wheel", wheelParts);
        if (doorPivot != null)
        {
            doorClosedLocal = doorPivot.localRotation;
            doorSign = ResolveDoorOpenSign(doorParts);
        }
        if (seatPivot != null) seatRestLocal = seatPivot.localPosition;
        if (wheelPivot != null)
        {
            wheelRestLocal = wheelPivot.localPosition;
            wheelRestLocalRotation = wheelPivot.localRotation;
        }
        IsReady = doorPivot != null && seatPivot != null && wheelPivot != null &&
            markers.ContainsKey("Roof");
        Debug.Log(
            $"GYMCHAOS_VEHICLE_CABIN_READY vehicle={name} ready={(IsReady ? 1 : 0)} " +
            $"glass={GlassRendererCount} interior={InteriorRendererCount} " +
            $"doorParts={doorParts.Count} markers={markers.Count} doorSign={doorSign:F0}",
            this);
    }

    private Transform MakePivot(string pivotName, string marker, List<Transform> parts)
    {
        if (parts.Count == 0 || !markers.TryGetValue(marker, out Vector3 local)) return null;
        Transform pivot = new GameObject(pivotName).transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = local;
        pivot.localRotation = Quaternion.identity;
        for (int i = 0; i < parts.Count; i++)
        {
            parts[i].SetParent(pivot, true);
        }
        return pivot;
    }

    // The door swings out: its rear edge must move away from the car's
    // centre line. Decide the sign from the door's own geometry.
    private float ResolveDoorOpenSign(List<Transform> doorParts)
    {
        Vector3 hinge = doorPivot.localPosition;
        Vector3 rear = hinge;
        float farthest = -1f;
        for (int i = 0; i < doorParts.Count; i++)
        {
            MeshFilter filter = doorParts[i].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            // Mesh-local bounds: the car need not be axis-aligned here.
            Bounds b = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = transform.InverseTransformPoint(doorParts[i].TransformPoint(new Vector3(
                    (corner & 1) == 0 ? b.min.x : b.max.x,
                    (corner & 2) == 0 ? b.min.y : b.max.y,
                    (corner & 4) == 0 ? b.min.z : b.max.z)));
                float d = Mathf.Abs(p.z - hinge.z);
                if (d > farthest)
                {
                    farthest = d;
                    rear = new Vector3(hinge.x, hinge.y, p.z);
                }
            }
        }
        DoorSillLocalY = float.PositiveInfinity;
        for (int i = 0; i < doorParts.Count; i++)
        {
            MeshFilter filter = doorParts[i].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = transform.InverseTransformPoint(doorParts[i].TransformPoint(new Vector3(
                    (corner & 1) == 0 ? b.min.x : b.max.x,
                    (corner & 2) == 0 ? b.min.y : b.max.y,
                    (corner & 4) == 0 ? b.min.z : b.max.z)));
                DoorSillLocalY = Mathf.Min(DoorSillLocalY, p.y);
            }
        }
        if (float.IsInfinity(DoorSillLocalY)) DoorSillLocalY = hinge.y - 0.5f;
        Vector3 arm = rear - hinge;
        DoorReach = arm.z;
        Vector3 turned = Quaternion.Euler(0f, 30f, 0f) * arm;
        return Mathf.Abs(hinge.x + turned.x) > Mathf.Abs(hinge.x + arm.x) ? 1f : -1f;
    }

    public void SetDoorOpen01(float open01)
    {
        if (doorPivot == null) return;
        doorOpen01 = Mathf.Clamp01(open01);
        float eased = doorOpen01 * doorOpen01 * (3f - 2f * doorOpen01);
        doorPivot.localRotation = doorClosedLocal *
            Quaternion.Euler(0f, doorSign * DoorOpenDegrees * eased, 0f);
    }

    /// <summary>Moves the driver seat so its cushion top sits at <paramref name="cushionLocalY"/>.</summary>
    public void SetSeatCushionHeight(float cushionLocalY)
    {
        if (seatPivot == null) return;
        seatPivot.localPosition = new Vector3(seatRestLocal.x, cushionLocalY, seatRestLocal.z);
    }

    /// <summary>
    /// Puts the rim through both hands (vehicle-local points) and turns it
    /// with them; the wheel keeps its authored tilt.
    /// </summary>
    public void FitSteeringWheel(Vector3 leftLocal, Vector3 rightLocal, float restAngle)
    {
        if (wheelPivot == null) return;
        Vector3 normal = WheelNormalLocal;
        Vector3 across = Vector3.ProjectOnPlane(rightLocal - leftLocal, normal);
        float radius = Mathf.Clamp(across.magnitude * 0.5f, 0.13f, 0.3f);
        wheelPivot.localPosition = (leftLocal + rightLocal) * 0.5f;
        wheelPivot.localScale = Vector3.one * (radius / AuthoredWheelRadius);
        float angle = Vector3.SignedAngle(
            Vector3.ProjectOnPlane(Vector3.right, normal), across, normal);
        wheelPivot.localRotation = Quaternion.AngleAxis(angle - restAngle, normal) *
            wheelRestLocalRotation;
    }

    public float WheelAngleFor(Vector3 leftLocal, Vector3 rightLocal)
    {
        Vector3 normal = WheelNormalLocal;
        return Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.right, normal),
            Vector3.ProjectOnPlane(rightLocal - leftLocal, normal), normal);
    }

    // The authored rim faces the driver: from the wheel toward the seat,
    // tilted 24 degrees up.
    private Vector3 WheelNormalLocal
    {
        get
        {
            Vector3 toSeat = markers.TryGetValue("Seat", out Vector3 seat) &&
                markers.TryGetValue("Wheel", out Vector3 wheel)
                ? Vector3.ProjectOnPlane(seat - wheel, Vector3.up) : Vector3.back;
            if (toSeat.sqrMagnitude < 1e-6f) toSeat = Vector3.back;
            return (toSeat.normalized * Mathf.Cos(24f * Mathf.Deg2Rad) +
                Vector3.up * Mathf.Sin(24f * Mathf.Deg2Rad)).normalized;
        }
    }

    private static Material GlassMaterial()
    {
        if (glassMaterial != null) return glassMaterial;
        // The Resources transparent Lit material keeps the transparent shader
        // variant in builds (URP strips variants no material uses).
        Material template = Resources.Load<Material>("GymLitTransparentVariant");
        glassMaterial = template != null
            ? new Material(template)
            : new Material(GymMaterialLibrary.LitShader);
        glassMaterial.name = "Vehicle Cabin Glass";
        Color tint = new Color(0.10f, 0.14f, 0.17f, 0.26f);
        glassMaterial.SetColor("_BaseColor", tint);
        glassMaterial.color = tint;
        glassMaterial.SetFloat("_Smoothness", 0.93f);
        glassMaterial.SetFloat("_Metallic", 0f);
        if (template == null)
        {
            glassMaterial.SetFloat("_Surface", 1f);
            glassMaterial.SetFloat("_Blend", 0f);
            glassMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            glassMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            glassMaterial.SetInt("_ZWrite", 0);
            glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glassMaterial.SetOverrideTag("RenderType", "Transparent");
            glassMaterial.renderQueue = (int)RenderQueue.Transparent;
        }
        return glassMaterial;
    }

    private static Material ColourMaterial(string hex, string role)
    {
        string key = hex + "/" + role;
        if (colourMaterials.TryGetValue(key, out Material cached)) return cached;
        if (!ColorUtility.TryParseHtmlString("#" + hex, out Color colour)) colour = Color.gray;
        Shader shader = GymMaterialLibrary.LitShader;
        Material material = new Material(shader) { name = "Vehicle Cabin " + role + " " + hex };
        material.SetColor("_BaseColor", colour);
        material.color = colour;
        material.SetFloat("_Smoothness", role == "Screen" ? 0.85f : role == "Trim" ? 0.35f : 0.22f);
        material.SetFloat("_Metallic", 0f);
        colourMaterials[key] = material;
        return material;
    }
}
