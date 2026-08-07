using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replaces the broad enemy root capsule with moving, skeleton-following
/// compound body colliders and resolves impact points against the currently
/// deformed character surface. The capsules/spheres are attached to the
/// fighter Rigidbody, so the player cannot walk through an animated enemy;
/// their positions follow the actual rig rather than a static root cylinder.
/// </summary>
[DefaultExecutionOrder(1100)]
public sealed class EnemyMeshHitboxRig : MonoBehaviour
{
    private sealed class Segment
    {
        public Transform start;
        public Transform end;
        public Transform hitbox;
        public CapsuleCollider collider;
    }

    private readonly List<Segment> segments = new List<Segment>();
    private SkinnedMeshRenderer bodyRenderer;
    private Mesh bakedSurface;

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

        CapsuleCollider[] broadColliders = GetComponents<CapsuleCollider>();
        for (int i = 0; i < broadColliders.Length; i++)
        {
            broadColliders[i].enabled = false;
        }

        AddSegment("Pelvis hitbox", rig.Hips, rig.Spine, height * 0.085f);
        AddSegment("Chest hitbox", rig.Spine, rig.Head, height * 0.095f);
        AddSegment("Left shoulder hitbox", rig.LeftShoulder, rig.LeftUpperArm, height * 0.05f);
        AddSegment("Left upper arm hitbox", rig.LeftUpperArm, rig.LeftForearm, height * 0.045f);
        AddSegment("Left forearm hitbox", rig.LeftForearm, rig.LeftHand, height * 0.038f);
        AddSegment("Right shoulder hitbox", rig.RightShoulder, rig.RightUpperArm, height * 0.05f);
        AddSegment("Right upper arm hitbox", rig.RightUpperArm, rig.RightForearm, height * 0.045f);
        AddSegment("Right forearm hitbox", rig.RightForearm, rig.RightHand, height * 0.038f);
        AddSegment("Left thigh hitbox", rig.LeftThigh, rig.LeftShin, height * 0.045f);
        AddSegment("Right thigh hitbox", rig.RightThigh, rig.RightShin, height * 0.045f);

        Transform leftFoot = CreateFollower("Left foot endpoint", rig.LeftShin, renderer.transform.TransformPoint(rig.LeftFootPosition));
        Transform rightFoot = CreateFollower("Right foot endpoint", rig.RightShin, renderer.transform.TransformPoint(rig.RightFootPosition));
        AddSegment("Left shin hitbox", rig.LeftShin, leftFoot, height * 0.042f);
        AddSegment("Right shin hitbox", rig.RightShin, rightFoot, height * 0.042f);

        AddSphere("Head hitbox", rig.Head, height * 0.052f, Vector3.up * height * 0.035f);
        AddSphere("Left hand hitbox", rig.LeftHand, height * 0.035f, Vector3.zero);
        AddSphere("Right hand hitbox", rig.RightHand, height * 0.035f, Vector3.zero);
        AddSphere("Left foot hitbox", leftFoot, height * 0.045f, Vector3.zero);
        AddSphere("Right foot hitbox", rightFoot, height * 0.045f, Vector3.zero);
        UpdateSegments();
        Physics.SyncTransforms();
    }

    private Transform CreateFollower(string objectName, Transform parent, Vector3 worldPosition)
    {
        Transform follower = new GameObject(objectName).transform;
        follower.SetParent(parent, true);
        follower.position = worldPosition;
        follower.gameObject.layer = gameObject.layer;
        return follower;
    }

    private void AddSegment(string objectName, Transform start, Transform end, float radius)
    {
        if (start == null || end == null)
        {
            return;
        }

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
            collider = capsule
        });
    }

    private void AddSphere(string objectName, Transform anchor, float radius, Vector3 worldOffset)
    {
        if (anchor == null)
        {
            return;
        }

        GameObject hitboxObject = new GameObject(objectName);
        hitboxObject.layer = gameObject.layer;
        hitboxObject.transform.SetParent(anchor, false);
        hitboxObject.transform.position = anchor.position + worldOffset;
        SphereCollider sphere = hitboxObject.AddComponent<SphereCollider>();
        sphere.radius = radius;
        sphere.isTrigger = false;
    }

    private void LateUpdate()
    {
        UpdateSegments();
        Physics.SyncTransforms();
    }

    private void UpdateSegments()
    {
        for (int i = 0; i < segments.Count; i++)
        {
            Segment segment = segments[i];
            if (segment.start == null || segment.end == null || segment.hitbox == null)
            {
                continue;
            }

            Vector3 delta = segment.end.position - segment.start.position;
            float length = Mathf.Max(segment.collider.radius * 2f, delta.magnitude);
            segment.hitbox.SetPositionAndRotation(
                (segment.start.position + segment.end.position) * 0.5f,
                delta.sqrMagnitude > 0.000001f
                    ? Quaternion.FromToRotation(Vector3.up, delta)
                    : Quaternion.identity);
            segment.collider.center = Vector3.zero;
            segment.collider.height = length / Mathf.Max(0.001f, segment.hitbox.lossyScale.y);
        }
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
