using System.Collections.Generic;
using UnityEngine;

public class GymArenaBootstrap : MonoBehaviour
{
    private static readonly BodybuilderIdentity[] EnemyRoster =
    {
        BodybuilderIdentity.Cbum,
        BodybuilderIdentity.Zyzz,
        BodybuilderIdentity.Arnold,
        BodybuilderIdentity.Ronnie,
        BodybuilderIdentity.JayCutler,
        BodybuilderIdentity.Goku
    };

    private static readonly string[] StaticNameBlocks =
    {
        "player",
        "main camera",
        "carryanchor",
        "playerhandrig",
        "gym fighter",
        "manwithsuit1"
    };

    private static GymArenaBootstrap instance;

    private readonly Dictionary<Transform, WeightType> weightCandidates = new Dictionary<Transform, WeightType>();
    private readonly HashSet<Transform> configuredPickupRoots = new HashSet<Transform>();
    private readonly HashSet<Transform> stabilizedEquipmentRoots = new HashSet<Transform>();
    private readonly HashSet<Transform> spatiallyMountedWeightRoots = new HashSet<Transform>();

    private PlayerMovement player;

    public static void EnsureExists(PlayerMovement player)
    {
        if (instance != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("GymArenaBootstrap");
        instance = bootstrapObject.AddComponent<GymArenaBootstrap>();
        instance.Initialize(player);
    }

    public void EnsureSceneColliders()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        HashSet<Transform> processedRenderers = new HashSet<Transform>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Transform target = renderer.transform;
            if (processedRenderers.Contains(target) || ShouldIgnoreRenderer(target))
            {
                continue;
            }

            processedRenderers.Add(target);

            Transform mountedEquipment = FindMountedEquipmentRoot(target);
            if (mountedEquipment != null)
            {
                Transform mountedWeight = FindNamedWeightRoot(target, mountedEquipment);
                if (mountedWeight != null)
                {
                    WeightType mountedType = TryGetWeightType(mountedWeight.name);
                    if (IsWeightType(mountedType))
                    {
                        if (configuredPickupRoots.Add(mountedWeight))
                        {
                            weightCandidates[mountedWeight] = mountedType;
                        // Mounted bars and plates remain fixed until E picks them
                        // up, but are still registered as real pickup items.
                        spatiallyMountedWeightRoots.Add(mountedWeight);
                            EnsurePickupRootGameplay(mountedWeight.gameObject, mountedType);
                        }

                        continue;
                    }
                }

                StabilizeMountedEquipment(mountedEquipment);
                EnsureStaticCollider(target.gameObject, renderer);
                continue;
            }

            Transform pickupRoot = FindPickupRoot(target);
            if (pickupRoot != null)
            {
                if (configuredPickupRoots.Add(pickupRoot))
                {
                    WeightType weightType = TryGetWeightType(pickupRoot.name);
                    weightCandidates[pickupRoot] = weightType;
                    EnsurePickupRootGameplay(pickupRoot.gameObject, weightType);
                }

                continue;
            }

            EnsureStaticCollider(target.gameObject, renderer);
        }
    }

    public void EnsureWeightGameplayObjects()
    {
        foreach (KeyValuePair<Transform, WeightType> entry in weightCandidates)
        {
            if (entry.Key == null)
            {
                continue;
            }

            GameObject item = entry.Key.gameObject;
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = item.AddComponent<Rigidbody>();
            }

            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
            {
                continue;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider meshCollider = colliders[i] as MeshCollider;
                if (meshCollider != null)
                {
                    meshCollider.convex = true;
                }
            }

            PickupItem pickup = item.GetComponent<PickupItem>();
            if (pickup == null)
            {
                pickup = item.AddComponent<PickupItem>();
            }

            pickup.Configure(body, entry.Value, colliders);
            if (spatiallyMountedWeightRoots.Contains(entry.Key))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }
        }
    }

    private void Initialize(PlayerMovement targetPlayer)
    {
        player = targetPlayer;
        GymInteriorBuilder.Build(player);
        EnsureSceneColliders();
        MarkSpatiallyMountedWeightCandidates();
        EnsureWeightGameplayObjects();
        GymExerciseStation.CreateForScene();
        SpawnNeutralReceptionNpc();
        SpawnEnemies();
    }

    private bool ShouldIgnoreRenderer(Transform target)
    {
        string lowerName = target.name.ToLowerInvariant();
        for (int i = 0; i < StaticNameBlocks.Length; i++)
        {
            if (lowerName.Contains(StaticNameBlocks[i]))
            {
                return true;
            }
        }

        if (target.GetComponentInParent<PlayerMovement>() != null || target.GetComponentInParent<EnemyFighter>() != null)
        {
            return true;
        }

        return target.GetComponent<ParticleSystemRenderer>() != null;
    }

    private Transform FindPickupRoot(Transform target)
    {
        if (FindMountedEquipmentRoot(target) != null)
        {
            return null;
        }

        Transform current = target;
        while (current != null)
        {
            if (TryGetWeightType(current.name) != WeightType.None)
            {
                return current;
            }

            if (ShouldIgnoreRenderer(current))
            {
                return null;
            }

            current = current.parent;
        }

        return null;
    }

    private static Transform FindMountedEquipmentRoot(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            string lower = current.name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            if (lower.Contains("bench") || lower.Contains("cage") || lower.Contains("smithmachine") ||
                lower.Contains("powerrack") || lower.Contains("squatrack") || lower.Contains("preacher") ||
                lower.Contains("dips") || lower.Contains("dipstation") || lower.Contains("treadmill") ||
                lower.Contains("bike") || lower.Contains("weightstand") || lower == "stand" || lower.StartsWith("stand("))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private static Transform FindNamedWeightRoot(Transform target, Transform mountedEquipment)
    {
        Transform current = target;
        while (current != null)
        {
            if (TryGetWeightType(current.name) != WeightType.None)
            {
                return current;
            }

            if (current == mountedEquipment)
            {
                break;
            }

            current = current.parent;
        }

        return null;
    }

    private static bool IsPlateType(WeightType type)
    {
        return type == WeightType.Plate || type == WeightType.Plate5 || type == WeightType.Plate10 || type == WeightType.Plate20;
    }

    private void StabilizeMountedEquipment(Transform equipmentRoot)
    {
        if (equipmentRoot == null || !stabilizedEquipmentRoots.Add(equipmentRoot))
        {
            return;
        }

        Rigidbody[] bodies = equipmentRoot.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].linearVelocity = Vector3.zero;
            bodies[i].angularVelocity = Vector3.zero;
            bodies[i].useGravity = false;
            bodies[i].isKinematic = true;
        }

        PickupItem[] pickups = equipmentRoot.GetComponentsInChildren<PickupItem>(true);
        for (int i = 0; i < pickups.Length; i++)
        {
            Destroy(pickups[i]);
        }
    }

    private void MarkSpatiallyMountedWeightCandidates()
    {
        if (weightCandidates.Count == 0)
        {
            return;
        }

        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        List<Bounds> equipmentBounds = new List<Bounds>();
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] == null || !IsMountedEquipmentName(transforms[i].name))
            {
                continue;
            }

            Renderer[] renderers = transforms[i].GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                continue;
            }

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
            {
                bounds.Encapsulate(renderers[r].bounds);
            }

            bounds.Expand(new Vector3(0.5f, 0.7f, 0.5f));
            equipmentBounds.Add(bounds);
        }

        foreach (KeyValuePair<Transform, WeightType> entry in weightCandidates)
        {
            if (entry.Key == null)
            {
                continue;
            }

            Renderer renderer = entry.Key.GetComponentInChildren<Renderer>(true);
            Vector3 center = renderer != null ? renderer.bounds.center : entry.Key.position;
            for (int i = 0; i < equipmentBounds.Count; i++)
            {
                if (equipmentBounds[i].Contains(center))
                {
                    spatiallyMountedWeightRoots.Add(entry.Key);
                    break;
                }
            }
        }
    }

    private static bool IsMountedEquipmentName(string objectName)
    {
        string lower = objectName.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return lower.Contains("bench") || lower.Contains("cage") || lower.Contains("smithmachine") ||
               lower.Contains("powerrack") || lower.Contains("squatrack") || lower.Contains("preacher") ||
               lower.Contains("weightstand") || lower == "stand" || lower.StartsWith("stand(");
    }

    private void EnsureStaticCollider(GameObject target, Renderer renderer)
    {
        if (target.GetComponent<Collider>() != null)
        {
            return;
        }

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            MeshCollider meshCollider = target.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
            return;
        }

        BoxCollider boxCollider = target.AddComponent<BoxCollider>();
        ApplyRendererBoundsToBox(target.transform, renderer, boxCollider);
    }

    private void EnsurePickupRootGameplay(GameObject rootObject, WeightType type)
    {
        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            GameObject renderObject = renderer.gameObject;
            if (renderObject.GetComponent<Collider>() != null)
            {
                continue;
            }

            MeshFilter meshFilter = renderObject.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                MeshCollider meshCollider = renderObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = true;
                continue;
            }

            AddFallbackCollider(renderObject.transform, renderer, type);
        }
    }

    private void AddFallbackCollider(Transform target, Renderer renderer, WeightType type)
    {
        switch (type)
        {
            case WeightType.Barbell:
            case WeightType.EzBar:
            {
                CapsuleCollider capsule = target.gameObject.AddComponent<CapsuleCollider>();
                capsule.direction = 2;
                capsule.height = Mathf.Max(renderer.bounds.size.z, 0.8f);
                capsule.radius = Mathf.Max(renderer.bounds.extents.x, 0.06f);
                capsule.center = target.InverseTransformPoint(renderer.bounds.center);
                break;
            }
            default:
            {
                SphereCollider sphere = target.gameObject.AddComponent<SphereCollider>();
                float maxExtent = Mathf.Max(renderer.bounds.extents.x, Mathf.Max(renderer.bounds.extents.y, renderer.bounds.extents.z));
                sphere.radius = maxExtent * 0.9f;
                sphere.center = target.InverseTransformPoint(renderer.bounds.center);
                break;
            }
        }
    }

    private static void ApplyRendererBoundsToBox(Transform target, Renderer renderer, BoxCollider boxCollider)
    {
        Vector3 localCenter = target.InverseTransformPoint(renderer.bounds.center);
        Vector3 lossy = target.lossyScale;
        boxCollider.center = localCenter;
        boxCollider.size = new Vector3(
            renderer.bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
            renderer.bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.0001f),
            renderer.bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.0001f));
    }

    private static WeightType TryGetWeightType(string objectName)
    {
        string lower = objectName.ToLowerInvariant()
            .Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        if (lower.StartsWith("barbell"))
        {
            return WeightType.Barbell;
        }

        if (lower.StartsWith("ezbar"))
        {
            return WeightType.EzBar;
        }

        if (lower.StartsWith("plate20"))
        {
            return WeightType.Plate20;
        }

        if (lower.StartsWith("plate10"))
        {
            return WeightType.Plate10;
        }

        if (lower.StartsWith("plate5"))
        {
            return WeightType.Plate5;
        }

        // Unity names a duplicated plain plate "Plate (1)". Spaces are
        // normalized above, so accept both "Plate" and "Plate(1)"; otherwise
        // the second factory plate on the incline bar stays part of the bar.
        if (lower == "plate" || lower.StartsWith("plate("))
        {
            return WeightType.Plate;
        }

        return WeightType.None;
    }

    private static bool IsWeightType(WeightType type)
    {
        return type == WeightType.Barbell || type == WeightType.EzBar || IsPlateType(type);
    }

    private void SpawnEnemies()
    {
        if (player == null)
        {
            return;
        }

        EnemyFighter[] existingFighters =
            FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < EnemyRoster.Length; i++)
        {
            bool alreadySpawned = false;
            for (int existingIndex = 0; existingIndex < existingFighters.Length; existingIndex++)
            {
                if (existingFighters[existingIndex] != null &&
                    existingFighters[existingIndex].Identity == EnemyRoster[i])
                {
                    alreadySpawned = true;
                    break;
                }
            }
            if (alreadySpawned)
            {
                continue;
            }

            GetEnemyDisplayPose(EnemyRoster[i], i, out Vector3 position, out Quaternion rotation);
            CreateEnemy(EnemyRoster[i], position, rotation);
        }
    }

    private void SpawnNeutralReceptionNpc()
    {
        if (GameObject.Find("NPC - manwithsuit1") != null || player == null)
        {
            return;
        }

        GameObject desk = GameObject.Find("Reception desk");
        GameObject floor = GameObject.Find("Rubber Floor");
        if (desk == null || !TryGetCombinedBounds(desk.transform, out Bounds deskBounds))
        {
            return;
        }

        Vector3 roomCenter = floor != null && floor.TryGetComponent(out Renderer floorRenderer)
            ? floorRenderer.bounds.center
            : player.transform.position;
        Vector3 towardWall = deskBounds.center - roomCenter;
        towardWall.y = 0f;
        if (towardWall.sqrMagnitude < 0.01f)
        {
            towardWall = Vector3.back;
        }
        towardWall.Normalize();

        float floorY = floor != null && floor.TryGetComponent(out Renderer groundRenderer)
            ? groundRenderer.bounds.max.y
            : player.transform.position.y - 1.05f;
        Vector3 position = deskBounds.center + towardWall * (deskBounds.extents.z + 0.58f);
        position.y = floorY + 0.08f;

        GameObject npc = new GameObject("NPC - manwithsuit1");
        npc.transform.SetPositionAndRotation(
            position, Quaternion.LookRotation(-towardWall, Vector3.up));
        CapsuleCollider collider = npc.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1.02375f, 0f);
        collider.height = 2.0475f;
        collider.radius = 0.34f;
        Rigidbody body = npc.AddComponent<Rigidbody>();
        body.mass = 78f;
        body.linearDamping = 1.5f;
        body.angularDamping = 0.5f;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        EnemyFighter fighter = npc.AddComponent<EnemyFighter>();
        fighter.Configure(
            BodybuilderIdentity.Manwithsuit1, player, 1f, false,
            passive: true, countAsOpponent: false);
        BodybuilderEnemyVisual.BuildNeutralNpc(npc, BodybuilderIdentity.Manwithsuit1);

        ReceptionDeathScreen.Create(npc.transform, fighter, floorY);

        PlacePlayerAcrossReceptionDesk(deskBounds, towardWall, floorY, npc.transform);
    }

    private void PlacePlayerAcrossReceptionDesk(
        Bounds deskBounds, Vector3 towardWall, float floorY, Transform receptionist)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        float playerClearance = controller != null ? controller.radius + 0.45f : 0.95f;
        float deskHalfDepth = Mathf.Abs(towardWall.x) * deskBounds.extents.x
            + Mathf.Abs(towardWall.z) * deskBounds.extents.z;
        Vector3 position = deskBounds.center - towardWall * (deskHalfDepth + playerClearance);
        position.y = controller != null
            ? floorY + controller.height * 0.5f - controller.center.y
            : floorY + 1f;

        Vector3 faceReceptionist = Vector3.ProjectOnPlane(receptionist.position - position, Vector3.up);
        Quaternion rotation = faceReceptionist.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(faceReceptionist.normalized, Vector3.up)
            : player.transform.rotation;

        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
        {
            controller.enabled = false;
        }
        player.transform.SetPositionAndRotation(position, rotation);
        if (controllerWasEnabled)
        {
            controller.enabled = true;
        }
    }

    private void GetEnemyDisplayPose(
        BodybuilderIdentity identity, int fallbackIndex, out Vector3 position, out Quaternion rotation)
    {
        float floorY = player.transform.position.y - 1.05f;
        Vector3 roomCenter = player.transform.position;
        GameObject floor = GameObject.Find("Rubber Floor");
        if (floor != null && floor.TryGetComponent(out Renderer floorRenderer))
        {
            roomCenter = floorRenderer.bounds.center;
        }

        if (identity == BodybuilderIdentity.Ronnie && TryGetLockerBounds(out Bounds lockerBounds))
        {
            Vector3 awayFromLockers = Vector3.ProjectOnPlane(roomCenter - lockerBounds.center, Vector3.up);
            if (awayFromLockers.sqrMagnitude < 0.01f)
            {
                awayFromLockers = Vector3.left;
            }
            awayFromLockers.Normalize();

            float lockerDepth = Mathf.Abs(awayFromLockers.x) * lockerBounds.extents.x
                + Mathf.Abs(awayFromLockers.z) * lockerBounds.extents.z;
            Vector3 lockerFront = lockerBounds.center + awayFromLockers * (lockerDepth + 1.15f);
            position = new Vector3(lockerFront.x, floorY, lockerFront.z);

            Vector3 towardSpawn = Vector3.ProjectOnPlane(player.transform.position - position, Vector3.up);
            rotation = towardSpawn.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(towardSpawn.normalized, Vector3.up)
                : Quaternion.LookRotation(awayFromLockers, Vector3.up);
            return;
        }

        if (identity == BodybuilderIdentity.JayCutler &&
            (TryGetNamedBounds("bike", out Bounds bikeBoundsForJay) ||
             TryGetNamedBounds("treadmill", out bikeBoundsForJay)))
        {
            Vector3 towardRoom = Vector3.ProjectOnPlane(roomCenter - bikeBoundsForJay.center, Vector3.up);
            if (towardRoom.sqrMagnitude < 0.01f)
            {
                towardRoom = Vector3.forward;
            }
            towardRoom.Normalize();
            float cardioDepth = Mathf.Abs(towardRoom.x) * bikeBoundsForJay.extents.x +
                Mathf.Abs(towardRoom.z) * bikeBoundsForJay.extents.z;
            // Put Jay clearly on the mat in front of the cardio row, rather than
            // inside or between the nearest bike/treadmill footprints.
            position = bikeBoundsForJay.center + towardRoom * (cardioDepth + 2.25f);
            position.y = floorY;
            rotation = Quaternion.LookRotation(towardRoom, Vector3.up);
            return;
        }

        if (identity == BodybuilderIdentity.Goku && TryGetNamedBounds("bike", out Bounds bikeBounds))
        {
            Vector3 towardWall = GetNearestRoomWallDirection(bikeBounds.center, roomCenter);
            GameObject receptionist = GameObject.Find("NPC - manwithsuit1");
            if (receptionist != null)
            {
                towardWall = GetNearestRoomWallDirection(receptionist.transform.position, roomCenter);
            }
            position = ProjectPointToWall(bikeBounds.center, towardWall, 1.35f);
            position.y = floorY;
            Vector3 towardPlayer = Vector3.ProjectOnPlane(player.transform.position - position, Vector3.up);
            rotation = towardPlayer.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(towardPlayer.normalized, Vector3.up)
                : Quaternion.LookRotation(-towardWall, Vector3.up);
            return;
        }

        if (identity == BodybuilderIdentity.Zyzz)
        {
            GameObject cableMachine = GameObject.Find("CableMachineDual");
            if (cableMachine != null && TryGetCombinedBounds(cableMachine.transform, out Bounds cableBounds))
            {
                position = new Vector3(cableBounds.center.x, floorY, cableBounds.center.z);
                Vector3 faceRoom = Vector3.ProjectOnPlane(roomCenter - position, Vector3.up);
                rotation = faceRoom.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(faceRoom.normalized, Vector3.up)
                    : cableMachine.transform.rotation;
                return;
            }
        }
        else if (TryGetMirrorBounds(out Bounds mirrorBounds))
        {
            Vector3 towardRoom = Vector3.ProjectOnPlane(roomCenter - mirrorBounds.center, Vector3.up);
            if (towardRoom.sqrMagnitude < 0.01f)
            {
                towardRoom = Vector3.back;
            }
            towardRoom.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, towardRoom).normalized;
            float sideOffset = identity == BodybuilderIdentity.Cbum ? -1.55f : 1.55f;
            Vector3 mirrorFront = mirrorBounds.center + towardRoom * 2.25f + side * sideOffset;
            position = new Vector3(mirrorFront.x, floorY, mirrorFront.z);
            rotation = Quaternion.LookRotation(towardRoom, Vector3.up);
            return;
        }

        Vector3[] fallbackOffsets =
        {
            new Vector3(7f, 0f, 3f),
            new Vector3(11f, 0f, -4f),
            new Vector3(15f, 0f, 4f),
            new Vector3(12f, 0f, 7f)
        };
        Vector3 offset = fallbackOffsets[Mathf.Clamp(fallbackIndex, 0, fallbackOffsets.Length - 1)];
        position = player.transform.position + player.transform.right * offset.x +
                   player.transform.forward * offset.z;
        position.y = floorY;
        rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(roomCenter - position, Vector3.up).normalized, Vector3.up);
    }

    private static bool TryGetMirrorBounds(out Bounds bounds)
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].name != "Mirror panel")
            {
                continue;
            }

            if (!found)
            {
                bounds = renderers[i].bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }
        return found;
    }

    private static bool TryGetLockerBounds(out Bounds bounds)
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].name != "Locker")
            {
                continue;
            }

            if (!found)
            {
                bounds = renderers[i].bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }
        return found;
    }

    private static bool TryGetNamedBounds(string nameFragment, out Bounds bounds)
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !HasNameInHierarchy(renderers[i].transform, nameFragment))
            {
                continue;
            }

            if (!found)
            {
                bounds = renderers[i].bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }
        return found;
    }

    private static bool HasNameInHierarchy(Transform target, string nameFragment)
    {
        string normalizedFragment = nameFragment.ToLowerInvariant();
        for (Transform current = target; current != null; current = current.parent)
        {
            if (current.name.ToLowerInvariant().Contains(normalizedFragment))
            {
                return true;
            }
        }
        return false;
    }

    private static Vector3 GetNearestRoomWallDirection(Vector3 point, Vector3 roomCenter)
    {
        GameObject floor = GameObject.Find("Rubber Floor");
        if (floor != null && floor.TryGetComponent(out Renderer floorRenderer))
        {
            Bounds floorBounds = floorRenderer.bounds;
            float west = Mathf.Abs(point.x - floorBounds.min.x);
            float east = Mathf.Abs(floorBounds.max.x - point.x);
            float south = Mathf.Abs(point.z - floorBounds.min.z);
            float north = Mathf.Abs(floorBounds.max.z - point.z);
            float nearest = Mathf.Min(west, east, south, north);
            if (nearest == west) return Vector3.left;
            if (nearest == east) return Vector3.right;
            if (nearest == south) return Vector3.back;
            return Vector3.forward;
        }
        return Vector3.ProjectOnPlane(roomCenter - point, Vector3.up).normalized;
    }

    private static Vector3 ProjectPointToWall(Vector3 point, Vector3 towardWall, float wallInset)
    {
        GameObject floor = GameObject.Find("Rubber Floor");
        if (floor == null || !floor.TryGetComponent(out Renderer floorRenderer))
        {
            return point + towardWall * wallInset;
        }

        Bounds floorBounds = floorRenderer.bounds;
        Vector3 projected = point;
        if (Mathf.Abs(towardWall.z) > Mathf.Abs(towardWall.x))
        {
            projected.z = towardWall.z < 0f
                ? floorBounds.min.z + wallInset
                : floorBounds.max.z - wallInset;
        }
        else
        {
            projected.x = towardWall.x < 0f
                ? floorBounds.min.x + wallInset
                : floorBounds.max.x - wallInset;
        }
        return projected;
    }

    private static bool TryGetCombinedBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return true;
    }

    private void CreateEnemy(BodybuilderIdentity identity, Vector3 position, Quaternion rotation)
    {
        GameObject enemy = new GameObject($"Enemy - {identity}");
        enemy.tag = "Enemies";
        enemy.transform.position = position;
        enemy.transform.rotation = rotation;

        CapsuleCollider collider = enemy.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1.15f, 0f);
        collider.height = 2.3f;
        collider.radius = identity == BodybuilderIdentity.Cbum || identity == BodybuilderIdentity.Ronnie ? 0.54f : 0.48f;

        Rigidbody body = enemy.AddComponent<Rigidbody>();
        body.mass = 85f;
        body.linearDamping = 1.5f;
        body.angularDamping = 0.5f;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        BodybuilderEnemyVisual.Build(enemy, identity);
        EnemyFighter fighter = enemy.AddComponent<EnemyFighter>();
        float health = identity == BodybuilderIdentity.Goku ? 1000f
            : identity == BodybuilderIdentity.Ronnie || identity == BodybuilderIdentity.JayCutler ? 100f
            : identity == BodybuilderIdentity.Zyzz ? 45f : 60f;
        fighter.Configure(identity, player, health, identity == BodybuilderIdentity.Ronnie);
    }

}
