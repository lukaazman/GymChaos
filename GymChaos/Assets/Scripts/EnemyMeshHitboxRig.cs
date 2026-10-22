using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replaces the broad enemy root capsule with moving, skeleton-following
/// compound body colliders and resolves impact points against the currently
/// deformed character surface. The capsules/spheres are attached to the
/// fighter Rigidbody, so the player cannot walk through an animated enemy;
/// their positions follow the actual rig rather than a static root cylinder.
/// </summary>
// The squat solver writes the final bone pose at order 1100. Update compound
// colliders afterwards so Physics.SyncTransforms never applies a one-frame
// idle hitbox pose during the squat handoff.
[DefaultExecutionOrder(1200)]
public sealed class EnemyMeshHitboxRig : MonoBehaviour
{
    private const float ThighGapClearance = 0.004f;

    private sealed class Segment
    {
        public Transform start;
        public Transform end;
        public Transform hitbox;
        public CapsuleCollider collider;
        public float baseRadius;
        public bool keepsLegGapOpen;
    }

    private readonly List<Segment> segments = new List<Segment>();
    private readonly HashSet<Collider> combatColliders = new HashSet<Collider>();
    private readonly List<(Transform center, float radius)> spheres = new List<(Transform, float)>();
    private SkinnedMeshRenderer bodyRenderer;
    private Mesh bakedSurface;
    private Transform leftThighAnchor;
    private Transform rightThighAnchor;
    private Transform leftThighEnd;
    private Transform rightThighEnd;

    public static EnemyMeshHitboxRig Configure(
        GameObject owner, BodybuilderEnemyVisual.Rig rig, SkinnedMeshRenderer renderer)
    {
        EnemyMeshHitboxRig hitboxes = owner.GetComponent<EnemyMeshHitboxRig>();
        if (hitboxes == null)
        {
            hitboxes = owner.AddComponent<EnemyMeshHitboxRig>();
        }
        hitboxes.Build(rig, renderer);
        return hitboxes;
    }

    private void Build(BodybuilderEnemyVisual.Rig rig, SkinnedMeshRenderer renderer)
    {
        bodyRenderer = renderer;
        float height = Mathf.Max(1f, renderer.bounds.size.y);
        segments.Clear();
        combatColliders.Clear();
        spheres.Clear();
        leftThighAnchor = rig.LeftThigh;
        rightThighAnchor = rig.RightThigh;
        leftThighEnd = rig.LeftShin;
        rightThighEnd = rig.RightShin;
        PrepareSkinFit(renderer, rig);

        CapsuleCollider[] broadColliders = GetComponents<CapsuleCollider>();
        for (int i = 0; i < broadColliders.Length; i++)
        {
            broadColliders[i].enabled = false;
        }

        // Keep the space between the thighs physically open. A single
        // hips-to-spine capsule creates a hidden bridge through the crotch,
        // so the lower torso is represented by the separate leg colliders
        // and the upper torso begins at the authored chest bone.
        AddSegment("Chest hitbox", rig.Chest, rig.Head, height * 0.095f);
        // Belly and pelvis: from the hips joint up, ending above the crotch
        // (the leg gap between the thighs stays open).
        AddSegment("Abdomen hitbox", rig.Spine, rig.Chest, height * 0.09f);
        AddSegment("Pelvis hitbox", rig.Hips, rig.Spine, height * 0.085f, PelvisRadiusLimit(rig));
        AddSegment("Left shoulder hitbox", rig.LeftShoulder, rig.LeftUpperArm, height * 0.05f);
        AddSegment("Left upper arm hitbox", rig.LeftUpperArm, rig.LeftForearm, height * 0.045f);
        AddSegment("Left forearm hitbox", rig.LeftForearm, rig.LeftHand, height * 0.038f);
        AddSegment("Right shoulder hitbox", rig.RightShoulder, rig.RightUpperArm, height * 0.05f);
        AddSegment("Right upper arm hitbox", rig.RightUpperArm, rig.RightForearm, height * 0.045f);
        AddSegment("Right forearm hitbox", rig.RightForearm, rig.RightHand, height * 0.038f);
        AddSegment("Left thigh hitbox", rig.LeftThigh, rig.LeftShin, height * 0.045f);
        AddSegment("Right thigh hitbox", rig.RightThigh, rig.RightShin, height * 0.045f);

        // The shin ends at the ankle bone. Rigs whose foot bone is not named
        // "LeftFoot" (Rigify DEF-foot.L) used to fall back to the shin head,
        // which collapsed the shin capsule to a ball at the knee: lower legs
        // and feet could not be hit.
        Transform leftFoot = rig.LeftFoot != null ? rig.LeftFoot
            : CreateFollower("Left foot endpoint", rig.LeftShin, renderer.transform.TransformPoint(rig.LeftFootPosition));
        Transform rightFoot = rig.RightFoot != null ? rig.RightFoot
            : CreateFollower("Right foot endpoint", rig.RightShin, renderer.transform.TransformPoint(rig.RightFootPosition));
        AddSegment("Left shin hitbox", rig.LeftShin, leftFoot, height * 0.042f);
        AddSegment("Right shin hitbox", rig.RightShin, rightFoot, height * 0.042f);
        AddFootSegment("Left foot hitbox", leftFoot, height * 0.04f);
        AddFootSegment("Right foot hitbox", rightFoot, height * 0.04f);

        AddSphere("Head hitbox", rig.Head, height * 0.052f, Vector3.up * height * 0.035f);
        AddSphere("Left hand hitbox", rig.LeftHand, height * 0.035f, Vector3.zero);
        AddSphere("Right hand hitbox", rig.RightHand, height * 0.035f, Vector3.zero);
        System.Text.StringBuilder radii = new System.Text.StringBuilder();
        foreach (Segment segment in segments)
        {
            radii.Append(segment.hitbox.name.Replace(" hitbox", "")).Append('=').Append(segment.baseRadius.ToString("F2")).Append(' ');
        }
        Debug.Log($"GYMCHAOS_HITBOX_FIT owner={name} skinFit={skinPoints != null} height={height:F2} {radii}", this);
        skinPoints = null;
        skinBones = null;
        skinOwner = null;
        UpdateSegments();
        Physics.SyncTransforms();
    }

    // Skin fit: each body part's radius covers the skin weighted to its
    // bones (bodybuilder arms, thighs and Goku's 1.15x build are far wider
    // than a proportional radius), so thrown items and punches that visibly
    // touch the body land.
    private const float SkinCoverPercentile = 0.9f;
    private const float SkinFitMinScale = 0.8f;
    private const float SkinFitMaxScale = 2.6f;
    private Vector3[] skinPoints;
    private Transform[] skinBones;
    // Per skin point: the rig bone (hitbox part) its main bone belongs to.
    private Transform[] skinOwner;

    private void PrepareSkinFit(SkinnedMeshRenderer renderer, BodybuilderEnemyVisual.Rig rig)
    {
        skinPoints = null;
        Mesh source = renderer.sharedMesh;
        if (source == null || !source.isReadable || renderer.bones == null || renderer.bones.Length == 0)
        {
            return;
        }
        BoneWeight[] weights = source.boneWeights;
        if (weights == null || weights.Length != source.vertexCount)
        {
            return;
        }
        Mesh baked = new Mesh();
        Bounds target = renderer.bounds;
        Matrix4x4 full = renderer.transform.localToWorldMatrix;
        Matrix4x4 noScale = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
        float bestError = float.PositiveInfinity;
        // BakeMesh output depends on the importer scale already in the
        // skinning matrices: take the pairing that matches the renderer bounds.
        for (int useScale = 0; useScale < 2; useScale++)
        {
            renderer.BakeMesh(baked, useScale == 1);
            Vector3[] local = baked.vertices;
            if (local.Length != weights.Length)
            {
                continue;
            }
            for (int m = 0; m < 2; m++)
            {
                Matrix4x4 toWorld = m == 0 ? full : noScale;
                Vector3[] world = new Vector3[local.Length];
                Bounds bounds = new Bounds(toWorld.MultiplyPoint3x4(local[0]), Vector3.zero);
                for (int i = 0; i < local.Length; i++)
                {
                    world[i] = toWorld.MultiplyPoint3x4(local[i]);
                    bounds.Encapsulate(world[i]);
                }
                float error = (bounds.center - target.center).magnitude + (bounds.size - target.size).magnitude;
                if (error < bestError)
                {
                    bestError = error;
                    skinPoints = world;
                }
            }
        }
        Destroy(baked);
        if (skinPoints == null)
        {
            return;
        }
        skinBones = renderer.bones;
        HashSet<Transform> parts = new HashSet<Transform>();
        foreach (Transform part in new[]
        {
            rig.Hips, rig.Spine, rig.Chest, rig.Head, rig.LeftShoulder, rig.LeftUpperArm,
            rig.LeftForearm, rig.LeftHand, rig.RightShoulder, rig.RightUpperArm, rig.RightForearm,
            rig.RightHand, rig.LeftThigh, rig.LeftShin, rig.LeftFoot, rig.RightThigh, rig.RightShin, rig.RightFoot
        })
        {
            if (part != null) parts.Add(part);
        }
        Dictionary<Transform, Transform> ownerOfBone = new Dictionary<Transform, Transform>();
        skinOwner = new Transform[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            int index = weights[i].boneIndex0;
            Transform bone = index >= 0 && index < skinBones.Length ? skinBones[index] : null;
            if (bone == null) continue;
            if (!ownerOfBone.TryGetValue(bone, out Transform owner))
            {
                owner = bone;
                while (owner != null && !parts.Contains(owner)) owner = owner.parent;
                ownerOfBone[bone] = owner;
            }
            skinOwner[i] = owner;
        }
    }

    // Skin points whose main bone belongs to this hitbox part.
    private bool OwnsSkinPoint(int vertex, Transform start, Transform end) =>
        skinOwner[vertex] == start;

    private float FitSegmentRadius(Transform start, Transform end, Vector3 a, Vector3 b, float radius)
    {
        if (skinPoints == null || start == null)
        {
            return radius;
        }
        List<float> distances = new List<float>();
        for (int i = 0; i < skinPoints.Length; i += 3)
        {
            if (OwnsSkinPoint(i, start, end))
            {
                distances.Add(DistanceToSegment(skinPoints[i], a, b));
            }
        }
        if (distances.Count < 12)
        {
            return radius;
        }
        distances.Sort();
        float fitted = distances[Mathf.Clamp((int)(distances.Count * SkinCoverPercentile), 0, distances.Count - 1)];
        return Mathf.Clamp(fitted, radius * SkinFitMinScale, radius * SkinFitMaxScale);
    }

    // The pelvis capsule must stay above the gap between the thighs.
    private static float PelvisRadiusLimit(BodybuilderEnemyVisual.Rig rig)
    {
        if (rig.Hips == null || rig.LeftThigh == null || rig.RightThigh == null ||
            rig.LeftShin == null || rig.RightShin == null)
        {
            return float.PositiveInfinity;
        }
        Vector3 gap = (rig.LeftThigh.position + rig.LeftShin.position +
            rig.RightThigh.position + rig.RightShin.position) * 0.25f;
        return Mathf.Max(0.05f, Vector3.Distance(rig.Hips.position, gap) * 0.75f);
    }

    private void AddFootSegment(string objectName, Transform foot, float radius)
    {
        if (foot == null)
        {
            return;
        }
        // Ankle to toes: the foot's first child bone, else a point ahead.
        Transform toe = foot.childCount > 0 ? foot.GetChild(0) : null;
        Vector3 toePoint = toe != null && (toe.position - foot.position).sqrMagnitude > 0.0004f
            ? toe.position
            : foot.position + Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * radius * 3f;
        Transform tip = CreateFollower(objectName + " tip", foot, toePoint);
        AddSegment(objectName, foot, tip, radius, float.PositiveInfinity, null);
    }

    private Transform CreateFollower(string objectName, Transform parent, Vector3 worldPosition)
    {
        Transform follower = new GameObject(objectName).transform;
        follower.SetParent(parent, true);
        follower.position = worldPosition;
        follower.gameObject.layer = gameObject.layer;
        return follower;
    }

    private void AddSegment(string objectName, Transform start, Transform end, float radius,
        float maxRadius = float.PositiveInfinity)
    {
        AddSegment(objectName, start, end, radius, maxRadius, end);
    }

    // skinEnd: the bone where this part's skin ends (null: everything below start).
    private void AddSegment(string objectName, Transform start, Transform end, float radius,
        float maxRadius, Transform skinEnd)
    {
        if (start == null || end == null)
        {
            return;
        }
        radius = Mathf.Min(maxRadius, FitSegmentRadius(start, skinEnd, start.position, end.position, radius));

        GameObject hitboxObject = new GameObject(objectName);
        hitboxObject.layer = gameObject.layer;
        hitboxObject.transform.SetParent(transform, true);
        CapsuleCollider capsule = hitboxObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = radius;
        // These are the real compound body colliders. Keeping them physical
        // makes CharacterController/Rigidbody movement stop at the animated
        // body while the same colliders remain usable by punch/blood queries.
        capsule.isTrigger = false;
        segments.Add(new Segment
        {
            start = start,
            end = end,
            hitbox = hitboxObject.transform,
            collider = capsule,
            baseRadius = radius,
            keepsLegGapOpen = objectName.IndexOf("thigh", System.StringComparison.OrdinalIgnoreCase) >= 0
        });
        combatColliders.Add(capsule);
    }

    private void AddSphere(string objectName, Transform anchor, float radius, Vector3 worldOffset)
    {
        if (anchor == null)
        {
            return;
        }

        if (skinPoints != null)
        {
            // Hands and head: cover the skin of that bone and its children.
            List<float> distances = new List<float>();
            Vector3 center = anchor.position + worldOffset;
            for (int i = 0; i < skinPoints.Length; i += 3)
            {
                if (OwnsSkinPoint(i, anchor, null))
                {
                    distances.Add(Vector3.Distance(skinPoints[i], center));
                }
            }
            if (distances.Count >= 12)
            {
                distances.Sort();
                float fitted = distances[Mathf.Clamp((int)(distances.Count * SkinCoverPercentile), 0, distances.Count - 1)];
                radius = Mathf.Clamp(fitted, radius * SkinFitMinScale, radius * SkinFitMaxScale);
            }
        }

        GameObject hitboxObject = new GameObject(objectName);
        hitboxObject.layer = gameObject.layer;
        hitboxObject.transform.SetParent(anchor, false);
        // Authored FBX rigs can carry a large import/unit scale on their bone
        // hierarchy even when the skinned mesh is fitted to the same world
        // height. Keep the hitbox radius in world units instead of multiplying
        // it by that character-specific bone scale.
        Vector3 anchorScale = anchor.lossyScale;
        hitboxObject.transform.localScale = new Vector3(
            SafeInverseScale(anchorScale.x),
            SafeInverseScale(anchorScale.y),
            SafeInverseScale(anchorScale.z));
        hitboxObject.transform.position = anchor.position + worldOffset;
        SphereCollider sphere = hitboxObject.AddComponent<SphereCollider>();
        sphere.radius = radius;
        sphere.isTrigger = false;
        combatColliders.Add(sphere);
        spheres.Add((hitboxObject.transform, radius));
    }

    private static float SafeInverseScale(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / Mathf.Abs(value) : 1f;
    }

    internal SkinnedMeshRenderer BodyRendererForVerification => bodyRenderer;

    /// <summary>
    /// The body as capsules (a, b, radius; b == a for a ball) at the bones'
    /// current pose, sized to the skin: callers plan clearances with it
    /// (climbing into or out of a car) without baking the mesh.
    /// </summary>
    internal void GetBodyVolumes(List<(Vector3 a, Vector3 b, float radius)> into)
    {
        into.Clear();
        for (int i = 0; i < segments.Count; i++)
        {
            Segment segment = segments[i];
            if (segment.start != null && segment.end != null)
            {
                into.Add((segment.start.position, segment.end.position, segment.baseRadius));
            }
        }
        for (int i = 0; i < spheres.Count; i++)
        {
            if (spheres[i].center != null)
            {
                Vector3 c = spheres[i].center.position;
                into.Add((c, c, spheres[i].radius));
            }
        }
    }
    internal IReadOnlyCollection<Collider> CombatCollidersForVerification => combatColliders;

    public bool IsTightCombatSurface(Collider collider)
    {
        return collider != null && collider.enabled && !collider.isTrigger &&
               combatColliders.Contains(collider);
    }

    private static readonly Unity.Profiling.ProfilerMarker LateUpdateMarker =
        new Unity.Profiling.ProfilerMarker("GymChaos.Late.EnemyMeshHitboxRig");

    private void LateUpdate()
    {
        using (LateUpdateMarker.Auto()) LateUpdateBody();
    }

    private void LateUpdateBody()
    {
        UpdateSegments();
        // One physics sync per frame after every rig moved its colliders,
        // instead of one per character (a full scene sync each time).
        HitboxSyncCoordinator.RequestSync();
    }

    private void UpdateSegments()
    {
        bool hasLegGap = leftThighAnchor != null && rightThighAnchor != null && leftThighEnd != null && rightThighEnd != null;
        Vector3 leftThighCenter = hasLegGap
            ? (leftThighAnchor.position + leftThighEnd.position) * 0.5f
            : Vector3.zero;
        Vector3 rightThighCenter = hasLegGap
            ? (rightThighAnchor.position + rightThighEnd.position) * 0.5f
            : Vector3.zero;
        Vector3 legGap = hasLegGap
            ? (leftThighCenter + rightThighCenter) * 0.5f
            : Vector3.zero;
        for (int i = 0; i < segments.Count; i++)
        {
            Segment segment = segments[i];
            if (segment.start == null || segment.end == null || segment.hitbox == null)
            {
                continue;
            }

            Vector3 delta = segment.end.position - segment.start.position;
            float radius = segment.baseRadius;
            if (segment.keepsLegGapOpen && hasLegGap)
            {
                // In a punch/landing pose the knees can rotate inward. Keep
                // each thigh capsule inside its own side of the midpoint gap.
                // The limit is based on the actual animated thigh segment,
                // rather than only the distance between its two anchors.
                float gapDistance = DistanceToSegment(
                    legGap, segment.start.position, segment.end.position);
                float maxWorldRadius = Mathf.Max(0f, gapDistance - ThighGapClearance);
                float largestRadialScale = Mathf.Max(
                    Mathf.Abs(segment.hitbox.lossyScale.x),
                    Mathf.Abs(segment.hitbox.lossyScale.z));
                radius = Mathf.Min(
                    radius,
                    maxWorldRadius / Mathf.Max(0.001f, largestRadialScale));
            }
            segment.collider.radius = radius;
            float length = Mathf.Max(radius * 2f, delta.magnitude);
            segment.hitbox.SetPositionAndRotation(
                (segment.start.position + segment.end.position) * 0.5f,
                delta.sqrMagnitude > 0.000001f
                    ? Quaternion.FromToRotation(Vector3.up, delta)
                    : Quaternion.identity);
            segment.collider.center = Vector3.zero;
            segment.collider.height = length / Mathf.Max(0.001f, segment.hitbox.lossyScale.y);
        }
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float segmentLengthSquared = segment.sqrMagnitude;
        if (segmentLengthSquared <= 0.000001f)
        {
            return Vector3.Distance(point, start);
        }

        float projection = Mathf.Clamp01(Vector3.Dot(point - start, segment) / segmentLengthSquared);
        return Vector3.Distance(point, start + segment * projection);
    }
    public bool TrySnapToSurface(Vector3 approximatePoint, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfacePoint = approximatePoint;
        surfaceNormal = (approximatePoint - transform.position).normalized;
        if (bodyRenderer == null || bodyRenderer.sharedMesh == null)
        {
            return false;
        }

        if (bakedSurface == null)
        {
            bakedSurface = new Mesh { name = name + " impact surface snapshot" };
            bakedSurface.MarkDynamic();
        }
        bodyRenderer.BakeMesh(bakedSurface);

        Vector3 localPoint = bodyRenderer.transform.InverseTransformPoint(approximatePoint);
        Vector3[] vertices = bakedSurface.vertices;
        Vector3[] normals = bakedSurface.normals;
        if (vertices.Length == 0)
        {
            return false;
        }

        int nearest = 0;
        float nearestSquared = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            float squared = (vertices[i] - localPoint).sqrMagnitude;
            if (squared < nearestSquared)
            {
                nearestSquared = squared;
                nearest = i;
            }
        }

        surfacePoint = bodyRenderer.transform.TransformPoint(vertices[nearest]);
        if (nearest < normals.Length && normals[nearest].sqrMagnitude > 0.0001f)
        {
            surfaceNormal = bodyRenderer.transform.TransformDirection(normals[nearest]).normalized;
        }
        return true;
    }

    private void OnDestroy()
    {
        if (bakedSurface != null)
        {
            Destroy(bakedSurface);
        }
    }
}

/// <summary>Runs after every <see cref="EnemyMeshHitboxRig"/> (order 1200) and
/// syncs physics transforms once for all of them.</summary>
[DefaultExecutionOrder(1250)]
public sealed class HitboxSyncCoordinator : MonoBehaviour
{
    private static HitboxSyncCoordinator instance;
    private static bool pending;

    public static void RequestSync()
    {
        pending = true;
        if (instance == null)
        {
            GameObject host = new GameObject("Hitbox Sync Coordinator");
            host.hideFlags = HideFlags.HideInHierarchy;
            instance = host.AddComponent<HitboxSyncCoordinator>();
        }
    }

    private void LateUpdate()
    {
        if (pending)
        {
            pending = false;
            Physics.SyncTransforms();
        }
    }
}
