using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the small physical space behind the main gym. It stays procedural so
/// it follows the same runtime scene path as the existing gym walls and floor.
/// </summary>
public static class GymBackRoomBuilder
{
    private const string RootName = "Gym Back Area (Runtime)";
    private const float BackWidth = 12f;
    private const float BackDepth = 10f;
    private const float BackHeight = 4.2f;
    private const float DoorWidth = 2.8f;
    private static Bounds roomBounds;
    private static bool roomBoundsReady;
    private static float roomFloorY;
    private static Vector3 roomCenter;

    public static bool TryGetRoomBounds(out Bounds bounds)
    {
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
        }

        bounds = roomBounds;
        return roomBoundsReady;
    }

    public static bool IsInsideRoom(Vector3 position)
    {
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
            if (!roomBoundsReady)
            {
                return false;
            }
        }

        return position.x >= roomBounds.min.x && position.x <= roomBounds.max.x &&
            position.z >= roomBounds.min.z && position.z <= roomBounds.max.z &&
            position.y >= roomBounds.min.y - 1f && position.y <= roomBounds.max.y + 1f;
    }

    public static void Build(
        Transform parent, Vector3 mainCenter, float mainWidth, float mainDepth,
        float mainHeight, PlayerMovement player)
    {
        GameObject existingRoot = GameObject.Find(RootName);
        if (existingRoot != null)
        {
            RebuildBoundsFromExistingRoot(existingRoot);
            return;
        }

        float floorY = mainCenter.y;
        float mainSouth = mainCenter.z - mainDepth * 0.5f;
        roomCenter = new Vector3(mainCenter.x, floorY, mainSouth - BackDepth * 0.5f - 0.18f);
        roomFloorY = floorY;
        roomBounds = new Bounds(
            roomCenter + Vector3.up * (BackHeight * 0.5f),
            new Vector3(BackWidth, BackHeight, BackDepth));
        roomBoundsReady = true;
        GameObject rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(parent, true);

        Material floorMaterial = CreateMaterial("Locker room rubber floor", new Color(0.025f, 0.04f, 0.075f), 0.1f, 0.38f);
        Material wallMaterial = CreateMaterial("Locker room wall", new Color(0.22f, 0.25f, 0.29f), 0f, 0.28f);
        Material trimMaterial = CreateMaterial("Locker room trim", new Color(0.035f, 0.045f, 0.06f), 0.35f, 0.64f);
        Material accentMaterial = CreateMaterial("Locker room accent", new Color(0.82f, 0.13f, 0.08f), 0.05f, 0.42f);
        Material tileMaterial = CreateMaterial("Bathroom tile", new Color(0.26f, 0.33f, 0.41f), 0.02f, 0.52f);
        Material mirrorMaterial = CreateMaterial("Locker room mirror", new Color(0.58f, 0.68f, 0.76f), 0.9f, 0.95f);
        Material metalMaterial = CreateMaterial("Locker metal", new Color(0.12f, 0.15f, 0.19f), 0.72f, 0.48f);
        SetEmission(accentMaterial, new Color(0.55f, 0.025f, 0.01f));

        CreateBox("Locker Room Floor", rootObject.transform,
            roomCenter + Vector3.down * 0.12f, new Vector3(BackWidth, 0.24f, BackDepth), floorMaterial, true);
        CreateBox("Locker Room Ceiling", rootObject.transform,
            roomCenter + Vector3.up * BackHeight, new Vector3(BackWidth, 0.18f, BackDepth), trimMaterial, true);

        float northZ = roomCenter.z + BackDepth * 0.5f;
        float southZ = roomCenter.z - BackDepth * 0.5f;
        float minX = roomCenter.x - BackWidth * 0.5f;
        float maxX = roomCenter.x + BackWidth * 0.5f;
        float doorMinX = roomCenter.x - DoorWidth * 0.5f;
        float doorMaxX = roomCenter.x + DoorWidth * 0.5f;

        CreateBox("Locker Room West Wall", rootObject.transform,
            new Vector3(minX, floorY + BackHeight * 0.5f, roomCenter.z),
            new Vector3(0.24f, BackHeight, BackDepth), wallMaterial, true);
        CreateBox("Locker Room East Wall", rootObject.transform,
            new Vector3(maxX, floorY + BackHeight * 0.5f, roomCenter.z),
            new Vector3(0.24f, BackHeight, BackDepth), wallMaterial, true);
        CreateBox("Locker Room South Wall", rootObject.transform,
            new Vector3(roomCenter.x, floorY + BackHeight * 0.5f, southZ),
            new Vector3(BackWidth, BackHeight, 0.24f), wallMaterial, true);
        CreateBox("Locker Room North Wall Left", rootObject.transform,
            new Vector3((minX + doorMinX) * 0.5f, floorY + BackHeight * 0.5f, northZ),
            new Vector3(doorMinX - minX, BackHeight, 0.24f), wallMaterial, true);
        CreateBox("Locker Room North Wall Right", rootObject.transform,
            new Vector3((doorMaxX + maxX) * 0.5f, floorY + BackHeight * 0.5f, northZ),
            new Vector3(maxX - doorMaxX, BackHeight, 0.24f), wallMaterial, true);
        CreateBox("Locker Room Door Header", rootObject.transform,
            new Vector3(roomCenter.x, floorY + 3.55f, northZ),
            new Vector3(DoorWidth, BackHeight - 3.55f, 0.24f), wallMaterial, true);

        CreateBox("Locker Room Door Frame Left", rootObject.transform,
            new Vector3(doorMinX, floorY + 1.8f, northZ - 0.08f),
            new Vector3(0.16f, 3.6f, 0.18f), accentMaterial, false);
        CreateBox("Locker Room Door Frame Right", rootObject.transform,
            new Vector3(doorMaxX, floorY + 1.8f, northZ - 0.08f),
            new Vector3(0.16f, 3.6f, 0.18f), accentMaterial, false);
        CreateLockerBays(rootObject.transform, roomCenter, floorY, minX, maxX, metalMaterial, accentMaterial);
        CreateBenches(rootObject.transform, roomCenter, floorY, trimMaterial, accentMaterial);
        CreateBathroom(rootObject.transform, roomCenter, floorY, tileMaterial, metalMaterial, accentMaterial);
        CreateMirror(rootObject.transform, roomCenter, floorY, mirrorMaterial, trimMaterial, player);

        CreateInteractable(rootObject.transform, "Locker Room Prep Point",
            new Vector3(roomCenter.x, floorY + 1.0f, northZ - 1.0f),
            new Vector3(DoorWidth - 0.3f, 2f, 1.8f),
            GymBackRoomInteractionType.Prep, "Get ready");
        CreateInteractable(rootObject.transform, "Changing Locker",
            new Vector3(minX + 1.5f, floorY + 1.1f, roomCenter.z + 0.4f),
            new Vector3(2.2f, 2.1f, 1.8f),
            GymBackRoomInteractionType.Locker, "Open locker");

        Debug.Log(
            $"GYMCHAOS_BACK_ROOM_OK center={roomCenter} size={BackWidth:F1}x{BackDepth:F1} " +
            $"doorWidth={DoorWidth:F1} bathroom=1 mirror=1",
            rootObject);
    }

    private static void RebuildBoundsFromExistingRoot(GameObject rootObject)
    {
        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            roomBounds = bounds;
            Renderer floorRenderer = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].name == "Locker Room Floor")
                {
                    floorRenderer = renderers[i];
                    break;
                }
            }

            roomFloorY = floorRenderer != null
                ? floorRenderer.bounds.max.y
                : bounds.min.y + 0.24f;
            roomCenter = new Vector3(
                floorRenderer != null ? floorRenderer.bounds.center.x : bounds.center.x,
                roomFloorY,
                floorRenderer != null ? floorRenderer.bounds.center.z : bounds.center.z);
            roomBoundsReady = true;
        }
    }

    public static bool TryGetLockerPreviewPose(
        out Vector3 playerPosition, out Quaternion playerRotation)
    {
        playerPosition = default;
        playerRotation = Quaternion.identity;
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
            if (!roomBoundsReady)
            {
                return false;
            }
        }

        float mirrorZ = roomCenter.z - BackDepth * 0.5f + 0.18f;
        playerPosition = new Vector3(roomCenter.x, roomFloorY + 1.05f, mirrorZ + 2.25f);
        playerRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        return true;
    }

    private static void CreateLockerBays(
        Transform parent, Vector3 center, float floorY, float minX, float maxX,
        Material metal, Material accent)
    {
        CreateModernLockerBank(
            parent, center.z, floorY, minX + 0.58f, -1, 5, 1.45f, metal, accent);
        // The east (+X) wall belongs to the bathroom/toilet zone. Keeping a
        // second full locker bank there intersected the divider, stall and
        // toilet props, so lockers intentionally remain on the west wall only.
    }

    /// <summary>
    /// Reuses the locker-room's current double-door/vented cabinet design for
    /// another wall without duplicating an older single-box locker variant.
    /// </summary>
    public static void CreateModernLockerBank(
        Transform parent,
        float centerZ,
        float floorY,
        float lockerX,
        int side,
        int count,
        float spacing,
        Material metal,
        Material accent)
    {
        side = side < 0 ? -1 : 1;
        count = Mathf.Max(1, count);
        Material door = CreateMaterial(
            "Locker enamel", new Color(0.19f, 0.24f, 0.25f), 0.45f, 0.32f);
        Material recess = CreateMaterial(
            "Locker vents", new Color(0.022f, 0.029f, 0.03f), 0.1f, 0.2f);
        float firstOffset = (count - 1) * 0.5f;
        for (int i = 0; i < count; i++)
        {
            float z = centerZ + (i - firstOffset) * spacing;
            float front = lockerX - side * 0.37f;
            CreateBox("Locker cabinet", parent,
                new Vector3(lockerX, floorY + 1.15f, z),
                new Vector3(0.7f, 2.3f, 1.16f), metal, true);
            for (int bay = -1; bay <= 1; bay += 2)
            {
                float doorZ = z + bay * 0.285f;
                CreateBox("Inset locker door", parent,
                    new Vector3(front, floorY + 1.2f, doorZ),
                    new Vector3(0.035f, 2.08f, 0.545f), door, false);
                CreateBox("Locker pull handle", parent,
                    new Vector3(front - side * 0.055f,
                        floorY + 1.2f, doorZ + 0.17f),
                    new Vector3(0.06f, 0.18f, 0.035f), accent, false);
                CreateBox("Locker number plate", parent,
                    new Vector3(front - side * 0.023f,
                        floorY + 1.85f, doorZ),
                    new Vector3(0.015f, 0.09f, 0.13f), metal, false);
                for (int vent = 0; vent < 3; vent++)
                {
                    CreateBox("Locker ventilation slot", parent,
                        new Vector3(front - side * 0.025f,
                            floorY + 0.34f + vent * 0.055f, doorZ),
                        new Vector3(0.014f, 0.016f, 0.29f), recess, false);
                }
            }
        }
    }

    private static void CreateBenches(
        Transform parent, Vector3 center, float floorY, Material trim, Material accent)
    {
        for (int i = -1; i <= 1; i += 2)
        {
            float x = center.x + i * 2.2f;
            CreateBox("Locker room bench", parent,
                new Vector3(x, floorY + 0.52f, center.z + 0.15f),
                new Vector3(2.7f, 0.18f, 0.58f), trim, true);
            CreateBox("Locker room bench leg", parent,
                new Vector3(x - 0.92f, floorY + 0.24f, center.z + 0.15f),
                new Vector3(0.14f, 0.56f, 0.42f), accent, true);
            CreateBox("Locker room bench leg", parent,
                new Vector3(x + 0.92f, floorY + 0.24f, center.z + 0.15f),
                new Vector3(0.14f, 0.56f, 0.42f), accent, true);
        }
    }

    private static void CreateBathroom(
        Transform parent, Vector3 center, float floorY,
        Material tile, Material metal, Material accent)
    {
        float x = center.x + 3.0f;
        CreateBox("Bathroom divider", parent,
            new Vector3(x, floorY + 1.45f, center.z - 1.7f),
            new Vector3(0.18f, 2.9f, 5.4f), tile, true);

        for (int i = -1; i <= 1; i++)
        {
            float z = center.z - 1.9f + i * 1.45f;
            Vector3 basin = new Vector3(center.x + 4.5f, floorY + 1.02f, z);
            Material porcelain = CreateMaterial("Porcelain basin", new Color(0.87f, 0.87f, 0.82f), 0f, 0.7f);
            CreateBox("Basin bottom", parent, basin, new Vector3(0.62f, 0.07f, 0.54f), porcelain, true);
            for (int edge = -1; edge <= 1; edge += 2)
            {
                CreateBox("Basin rim", parent, basin + new Vector3(edge * 0.32f, 0.08f, 0f), new Vector3(0.07f, 0.17f, 0.65f), porcelain, false);
                CreateBox("Basin rim", parent, basin + new Vector3(0f, 0.08f, edge * 0.29f), new Vector3(0.65f, 0.17f, 0.07f), porcelain, false);
            }
            CreateBox("Chrome drain", parent, basin + Vector3.up * 0.042f, new Vector3(0.07f, 0.01f, 0.07f), metal, false);
            CreateBox("Tap stem", parent, basin + new Vector3(0.28f, 0.23f, 0f), new Vector3(0.045f, 0.29f, 0.045f), metal, false);
            CreateBox("Tap spout", parent, basin + new Vector3(0.18f, 0.36f, 0f), new Vector3(0.24f, 0.04f, 0.045f), metal, false);
            CreateBox("Waste pipe", parent, basin - Vector3.up * 0.32f, new Vector3(0.07f, 0.6f, 0.07f), metal, false);
            CreateBox("Soap dispenser", parent, basin + new Vector3(0.28f, 0.19f, 0.22f), new Vector3(0.1f, 0.18f, 0.08f), accent, false);
        }

        CreateBox("Bathroom stall block", parent,
            new Vector3(center.x + 4.45f, floorY + 1.25f, center.z + 2.25f),
            new Vector3(2.2f, 2.5f, 0.18f), tile, true);
        CreateBox("Bathroom stall side", parent,
            new Vector3(center.x + 5.4f, floorY + 1.25f, center.z + 1.25f),
            new Vector3(0.18f, 2.5f, 2.0f), tile, true);
        Material ceramic = CreateMaterial("Toilet porcelain", new Color(0.86f, 0.86f, 0.8f), 0f, 0.6f);
        Vector3 toilet = new Vector3(center.x + 4.5f, floorY, center.z + 1.2f);
        CreateBox("Toilet cistern", parent, toilet + new Vector3(0f, 0.76f, 0.44f), new Vector3(0.58f, 0.7f, 0.22f), ceramic, true);
        CreateBox("Toilet pedestal", parent, toilet + new Vector3(0f, 0.24f, 0f), new Vector3(0.3f, 0.48f, 0.4f), ceramic, true);
        for (int side = -1; side <= 1; side += 2)
            CreateBox("Toilet seat side", parent, toilet + new Vector3(side * 0.23f, 0.49f, 0f), new Vector3(0.1f, 0.08f, 0.63f), ceramic, false);
        CreateBox("Toilet seat front", parent, toilet + new Vector3(0f, 0.49f, -0.29f), new Vector3(0.52f, 0.08f, 0.12f), ceramic, false);
        CreateBox("Toilet seat back", parent, toilet + new Vector3(0f, 0.49f, 0.29f), new Vector3(0.52f, 0.08f, 0.12f), ceramic, false);
        CreateBox("Stall door", parent, toilet + new Vector3(-0.76f, 1.3f, 0f), new Vector3(0.08f, 2.25f, 0.82f), metal, true);
        CreateBox("Stall latch", parent, toilet + new Vector3(-0.82f, 1.25f, -0.24f), new Vector3(0.06f, 0.08f, 0.14f), accent, false);
        CreateBox("Paper holder", parent, toilet + new Vector3(0.68f, 0.78f, 0f), new Vector3(0.14f, 0.14f, 0.24f), ceramic, false);
        CreateBox("Bathroom towel shelf", parent, new Vector3(center.x + 3.8f, floorY + 1.48f, center.z - 3.3f), new Vector3(0.75f, 0.05f, 0.32f), metal, false);
        for (int towel = 0; towel < 3; towel++)
            CreateBox("Folded towel", parent, new Vector3(center.x + 3.8f, floorY + 1.54f + towel * 0.06f, center.z - 3.3f), new Vector3(0.48f, 0.055f, 0.27f), ceramic, false);
        CreateInteractable(parent, "Bathroom Cooldown",
            new Vector3(center.x + 4.15f, floorY + 1.0f, center.z - 0.85f),
            new Vector3(1.2f, 2f, 1.4f),
            GymBackRoomInteractionType.Bathroom, "Use bathroom");
    }

    private static void CreateMirror(
        Transform parent, Vector3 center, float floorY,
        Material mirror, Material trim, PlayerMovement player)
    {
        Vector3 panelCenter = new Vector3(center.x, floorY + 2.35f, center.z - BackDepth * 0.5f + 0.18f);
        GameObject panel = CreateBox("Locker room mirror panel", parent, panelCenter,
            new Vector3(4.0f, 3.8f, 0.055f), mirror, true);
        panel.transform.rotation = Quaternion.identity;
        panel.AddComponent<GlassShatterPanel>();
        CreateBox("Locker room mirror top frame", parent,
            panelCenter + Vector3.up * 1.98f, new Vector3(4.2f, 0.1f, 0.1f), trim, false);
        CreateBox("Locker room mirror bottom frame", parent,
            panelCenter - Vector3.up * 1.98f, new Vector3(4.2f, 0.1f, 0.1f), trim, false);

        Renderer renderer = panel.GetComponent<Renderer>();
        if (player != null && player.playerCamera != null && renderer != null)
        {
            PlanarGymMirror.Create(parent, player.playerCamera, new[] { renderer },
                panelCenter + Vector3.forward * 0.03f, Vector3.forward);
        }
    }

    private static GymBackRoomInteractable CreateInteractable(
        Transform parent, string name, Vector3 position, Vector3 size,
        GymBackRoomInteractionType type, string displayName)
    {
        GameObject objectRoot = new GameObject(name);
        objectRoot.transform.SetParent(parent, true);
        objectRoot.transform.position = position;
        BoxCollider trigger = objectRoot.AddComponent<BoxCollider>();
        trigger.size = size;
        trigger.isTrigger = true;
        GymBackRoomInteractable interactable = objectRoot.AddComponent<GymBackRoomInteractable>();
        interactable.Configure(type, displayName);
        return interactable;
    }

    private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = name,
            color = color
        };
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static void SetEmission(Material material, Color emission)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
    }

    private static GameObject CreateBox(
        string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, true);
        box.transform.position = position;
        box.transform.localScale = scale;
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
        if (!keepCollider)
        {
            Object.Destroy(box.GetComponent<Collider>());
        }
        return box;
    }
}
