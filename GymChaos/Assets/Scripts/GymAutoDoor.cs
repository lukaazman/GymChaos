using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Automatic door used by the gym entrance (double-acting swing leaves) and
/// the protein.com storefront (sliding leaves). The door opens only for
/// people close to the opening or about to reach it (velocity prediction, so
/// a sprinting player never meets a closed leaf) and closes after the
/// opening is clear. Each leaf has a box collider that is on only while the
/// door is mostly shut (below <see cref="SolidBelowBlend"/>), so a closed door
/// blocks people but an opening or open one never traps anybody: the door
/// opens before a sensed person arrives and only closes once the opening is
/// clear.
/// </summary>
public sealed class GymAutoDoor : MonoBehaviour
{
    public enum DoorKind
    {
        Swing,
        Slide
    }

    private sealed class Leaf
    {
        public Transform Transform;
        public Vector3 ClosedLocalPosition;
        public Quaternion ClosedLocalRotation;
        // Swing: +1 or -1, the hinge side seen from the door's tangent.
        public float HingeSign;
        // Slide: local offset of the fully open leaf.
        public Vector3 SlideLocalOffset;
        public BoxCollider Body;
    }

    // Depth (along the door normal) and lateral margin of the presence zone.
    public const float PresenceDepth = 2.2f;
    public const float PresenceLateralMargin = 0.9f;
    // Prediction: people heading for the opening open it this long before
    // they would reach the door plane.
    public const float PredictSeconds = 0.8f;
    public const float PredictMaxDistance = 9f;
    private const float PredictMinSpeed = 0.6f;
    private const float CloseDelay = 0.55f;
    private const float SampleInterval = 0.08f;
    private const float VerticalReach = 2.6f;
    // Leaf colliders are solid below this open amount.
    public const float SolidBelowBlend = 0.5f;
    private const float MinimumLeafThickness = 0.08f;

    private static readonly List<GymAutoDoor> doors = new List<GymAutoDoor>();
    private static readonly List<Vector3> peoplePositions = new List<Vector3>(48);
    private static readonly List<Vector3> peopleVelocities = new List<Vector3>(48);
    private static readonly List<Transform> peopleIds = new List<Transform>(48);
    private static Dictionary<Transform, Vector3> lastPositions = new Dictionary<Transform, Vector3>(64);
    private static Dictionary<Transform, Vector3> nextPositions = new Dictionary<Transform, Vector3>(64);
    private static float lastSampleTime = -1f;
    private static int lastSampleFrame = -1;
    private static PlayerMovement cachedPlayer;
    private static float nextPlayerSearchTime;

    private readonly List<Leaf> leaves = new List<Leaf>();
    private DoorKind kind;
    private string doorName;
    private Vector3 center;
    private Vector3 normal;
    private Vector3 tangent;
    private float halfWidth;
    private float openSeconds = 0.6f;
    private float closeSeconds = 0.9f;
    private float swingAngle = 88f;
    private float blend;
    private float appliedBlend = -1f;
    private float swingDirection = 1f;
    private float lastWantedTime = -100f;
    private bool sensedLastSample;
    private bool networkDriven;
    private float networkTarget;
    private int sensedCount;
    public string DoorName => doorName;
    public DoorKind Kind => kind;
    public float OpenBlend => blend;
    /// <summary>Open amount with the swing side as its sign (swing doors), for the snapshot.</summary>
    public float SignedOpenBlend => kind == DoorKind.Swing && swingDirection < 0f ? -blend : blend;
    public bool IsClosed => blend <= 0.002f;
    public bool IsSensingSomeone => sensedLastSample;
    public Vector3 Center => center;
    public Vector3 Normal => normal;
    public float HalfWidth => halfWidth;
    public int LeafCount => leaves.Count;
    internal static IReadOnlyList<GymAutoDoor> AllDoors => doors;
    public bool IsNetworkDriven => networkDriven;

    /// <summary>Back to local sensing (the host went away).</summary>
    public void StopNetworkDrive() => networkDriven = false;

    /// <summary>Guest: the host decides when this door is open; local sensing stops.</summary>
    public void DriveFromNetwork(float hostBlend)
    {
        networkDriven = true;
        networkTarget = Mathf.Clamp01(Mathf.Abs(hostBlend));
        // The host's door swings away from whoever opened it, who may be a player this guest does not sense.
        if (kind == DoorKind.Swing && networkTarget > 0.01f && blend <= 0.01f) swingDirection = hostBlend < 0f ? -1f : 1f;
    }
    public bool LeavesSolid => leaves.Count > 0 && leaves[0].Body != null && leaves[0].Body.enabled;

    public bool OwnsCollider(Collider collider)
    {
        for (int i = 0; i < leaves.Count; i++)
        {
            if (leaves[i].Body == collider) return true;
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        doors.Clear();
        peoplePositions.Clear();
        peopleVelocities.Clear();
        peopleIds.Clear();
        lastPositions.Clear();
        nextPositions.Clear();
        lastSampleTime = -1f;
        lastSampleFrame = -1;
        cachedPlayer = null;
        nextPlayerSearchTime = 0f;
    }

    public static GymAutoDoor Find(string name)
    {
        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null && doors[i].doorName == name) return doors[i];
        }
        return null;
    }

    /// <summary>
    /// Sets the opening. <paramref name="outwardNormal"/> is horizontal and
    /// points to the door's outer side; the tangent runs along the opening.
    /// </summary>
    public void Configure(string name, DoorKind doorKind, Vector3 doorCenter,
        Vector3 outwardNormal, float openingWidth, float openTime, float closeTime)
    {
        doorName = name;
        kind = doorKind;
        center = doorCenter;
        normal = Vector3.ProjectOnPlane(outwardNormal, Vector3.up).normalized;
        tangent = Vector3.Cross(Vector3.up, normal).normalized;
        halfWidth = openingWidth * 0.5f;
        openSeconds = Mathf.Max(0.1f, openTime);
        closeSeconds = Mathf.Max(0.1f, closeTime);
    }

    public void AddSwingLeaf(Transform hingePivot, float hingeSign, float maxAngle)
    {
        if (hingePivot == null) return;
        swingAngle = maxAngle;
        leaves.Add(new Leaf
        {
            Transform = hingePivot,
            ClosedLocalPosition = hingePivot.localPosition,
            ClosedLocalRotation = hingePivot.localRotation,
            HingeSign = Mathf.Sign(hingeSign),
            Body = CreateBody(hingePivot)
        });
        appliedBlend = -1f;
    }

    public void AddSlideLeaf(Transform leaf, Vector3 openLocalOffset)
    {
        if (leaf == null) return;
        leaves.Add(new Leaf
        {
            Transform = leaf,
            ClosedLocalPosition = leaf.localPosition,
            ClosedLocalRotation = leaf.localRotation,
            SlideLocalOffset = openLocalOffset,
            Body = CreateBody(leaf)
        });
        appliedBlend = -1f;
    }

    /// <summary>Box over the closed leaf's renderers, in the leaf's own space.</summary>
    private static BoxCollider CreateBody(Transform leaf)
    {
        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds local = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds world = renderers[i].bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = leaf.InverseTransformPoint(world.center + Vector3.Scale(world.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                if (!any) { local = new Bounds(point, Vector3.zero); any = true; }
                else local.Encapsulate(point);
            }
        }
        if (!any) return null;
        BoxCollider body = leaf.gameObject.AddComponent<BoxCollider>();
        body.center = local.center;
        // Rotated leaf bounds overshoot a little; the door face stays thin.
        Vector3 size = local.size;
        body.size = new Vector3(
            Mathf.Max(size.x, MinimumLeafThickness), Mathf.Max(size.y, MinimumLeafThickness),
            Mathf.Max(size.z, MinimumLeafThickness));
        return body;
    }

    /// <summary>True when a person at <paramref name="position"/> moving at
    /// <paramref name="velocity"/> should have this door open.</summary>
    public bool ShouldOpenFor(Vector3 position, Vector3 velocity)
    {
        Vector3 offset = position - center;
        if (Mathf.Abs(offset.y) > VerticalReach) return false;
        float along = Vector3.Dot(offset, normal);
        float lateral = Vector3.Dot(offset, tangent);
        float absAlong = Mathf.Abs(along);
        if (absAlong <= PresenceDepth && Mathf.Abs(lateral) <= halfWidth + PresenceLateralMargin)
        {
            return true;
        }

        if (absAlong > PredictMaxDistance) return false;
        // Speed toward the door plane.
        float approach = -Mathf.Sign(along) * Vector3.Dot(velocity, normal);
        if (approach < PredictMinSpeed) return false;
        float eta = (absAlong - PresenceDepth * 0.5f) / approach;
        if (eta > PredictSeconds) return false;
        float lateralAtArrival = lateral + Vector3.Dot(velocity, tangent) * (absAlong / approach);
        return Mathf.Abs(lateralAtArrival) <= halfWidth + PresenceLateralMargin + 0.4f;
    }

    /// <summary>Who currently holds this door open (verification diagnostics).</summary>
    public string DescribeSensed()
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        for (int i = 0; i < peoplePositions.Count; i++)
        {
            if (!ShouldOpenFor(peoplePositions[i], peopleVelocities[i])) continue;
            Vector3 offset = peoplePositions[i] - center;
            text.Append(peopleIds[i] != null ? peopleIds[i].name : "?")
                .Append('@').Append(Vector3.Dot(offset, normal).ToString("F1"))
                .Append('/').Append(Vector3.Dot(offset, tangent).ToString("F1"))
                .Append(" v=").Append(peopleVelocities[i].magnitude.ToString("F1")).Append(';');
        }
        return text.ToString();
    }

    private void OnEnable()
    {
        if (!doors.Contains(this)) doors.Add(this);
    }

    private void OnDisable()
    {
        doors.Remove(this);
    }

    private void Update()
    {
        SamplePeople();

        int nearestSide = 0;
        float nearestDistance = float.MaxValue;
        sensedCount = 0;
        for (int i = 0; i < peoplePositions.Count; i++)
        {
            if (!ShouldOpenFor(peoplePositions[i], peopleVelocities[i])) continue;
            sensedCount++;
            Vector3 offset = peoplePositions[i] - center;
            float distance = offset.sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSide = Vector3.Dot(offset, normal) >= 0f ? 1 : -1;
            }
        }
        sensedLastSample = sensedCount > 0;

        bool want = sensedLastSample;
        if (want)
        {
            lastWantedTime = Time.time;
            // Double-acting leaves swing away from whoever opens them; the
            // direction only changes while the door is shut.
            if (kind == DoorKind.Swing && blend <= 0.01f && nearestSide != 0)
            {
                swingDirection = -nearestSide;
            }
        }

        float target = Time.time - lastWantedTime <= CloseDelay ? 1f : 0f;
        // A guest's door also opens for the guest itself at once (the host only learns of it a round trip later) and follows the host for everybody else.
        if (networkDriven) target = Mathf.Max(target, networkTarget);
        float duration = target > blend ? openSeconds : closeSeconds;
        blend = Mathf.MoveTowards(blend, target, Time.deltaTime / duration);
        ApplyPose();
    }

    private void ApplyPose()
    {
        if (Mathf.Approximately(blend, appliedBlend)) return;
        appliedBlend = blend;
        bool solid = blend < SolidBelowBlend;
        // Ease in and out so the leaves start and stop softly.
        float eased = blend * blend * (3f - 2f * blend);
        for (int i = 0; i < leaves.Count; i++)
        {
            Leaf leaf = leaves[i];
            if (leaf.Transform == null) continue;
            if (leaf.Body != null) leaf.Body.enabled = solid;
            if (kind == DoorKind.Swing)
            {
                // Hinge pivots are children of this door; rotating about
                // local up turns the leaf toward +/- normal.
                float angle = eased * swingAngle * swingDirection * leaf.HingeSign;
                leaf.Transform.localRotation = leaf.ClosedLocalRotation *
                    Quaternion.AngleAxis(angle, Vector3.up);
            }
            else
            {
                leaf.Transform.localPosition = leaf.ClosedLocalPosition + leaf.SlideLocalOffset * eased;
            }
        }
    }

    /// <summary>Positions and velocities of everyone who can walk through a door.</summary>
    private static void SamplePeople()
    {
        if (lastSampleFrame == Time.frameCount) return;
        float now = Time.time;
        if (lastSampleTime >= 0f && now - lastSampleTime < SampleInterval && peoplePositions.Count > 0)
        {
            return;
        }
        float dt = lastSampleTime >= 0f ? Mathf.Max(0.001f, now - lastSampleTime) : 0f;
        lastSampleTime = now;
        lastSampleFrame = Time.frameCount;
        peoplePositions.Clear();
        peopleVelocities.Clear();
        peopleIds.Clear();
        nextPositions.Clear();

        if (cachedPlayer == null && now >= nextPlayerSearchTime)
        {
            cachedPlayer = Object.FindAnyObjectByType<PlayerMovement>();
            nextPlayerSearchTime = now + 2f;
        }
        if (cachedPlayer != null && cachedPlayer.gameObject.activeInHierarchy)
        {
            AddPerson(cachedPlayer.transform, dt);
        }

        IReadOnlyList<EnemyFighter> fighters = EnemyFighter.RegisteredFighters;
        for (int i = 0; i < fighters.Count; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter != null && fighter.isActiveAndEnabled && !fighter.IsDead)
            {
                AddPerson(fighter.transform, dt);
            }
        }

        IReadOnlyList<GymVisitorAgent> visitors = GymVisitorAgent.ActiveAgents;
        for (int i = 0; i < visitors.Count; i++)
        {
            GymVisitorAgent visitor = visitors[i];
            if (visitor != null && visitor.isActiveAndEnabled)
            {
                AddPerson(visitor.transform, dt);
            }
        }

        GymHomelessAgent homeless = GymHomelessAgent.Instance;
        if (homeless != null && homeless.isActiveAndEnabled)
        {
            AddPerson(homeless.transform, dt);
        }

        GymFixerAgent fixer = GymFixerDirector.Instance != null ? GymFixerDirector.Instance.Agent : null;
        if (fixer != null && fixer.isActiveAndEnabled)
        {
            AddPerson(fixer.transform, dt);
        }

        // Other players are people too: the host's doors open for them.
        GymNetSession session = GymNetSession.Instance;
        if (session != null)
        {
            foreach (GymRemoteAvatar avatar in session.Avatars)
            {
                if (avatar != null) AddPerson(avatar.transform, dt);
            }
        }

        Dictionary<Transform, Vector3> swap = lastPositions;
        lastPositions = nextPositions;
        nextPositions = swap;
    }

    private static void AddPerson(Transform person, float dt)
    {
        Transform id = person;
        if (peopleIds.Contains(id)) return;
        Vector3 position = person.position;
        Vector3 velocity = Vector3.zero;
        if (dt > 0f && lastPositions.TryGetValue(id, out Vector3 previous))
        {
            velocity = (position - previous) / dt;
            velocity.y = 0f;
            // Teleports and respawns are not motion.
            if (velocity.sqrMagnitude > 30f * 30f) velocity = Vector3.zero;
        }
        nextPositions[id] = position;
        peopleIds.Add(id);
        peoplePositions.Add(position);
        peopleVelocities.Add(velocity);
    }
}
