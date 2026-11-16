using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The guest's copy of the host's world, with the real cast in it.
///
/// The old guest path switched every actor off and drew a tinted capsule where
/// each one stood, so a joined player lost every mesh, texture and animation in
/// the room. Here the local actors stay exactly as they are in solo play (the
/// same authored rigs, materials and animators) and only their simulation is
/// frozen: AI, physics and attacks stop, the host owns those. Each frame the
/// body is placed at the host's interpolated pose and its authored locomotion
/// is told how fast it is moving, so the guest sees the same walking, running
/// and idling bodies the host sees.
///
/// Bodies are matched to the host's actor ids by kind and nearest position and
/// kept for as long as the host lists them; an actor the host has that this
/// client never spawned still falls back to a stand-in capsule.
/// </summary>
public sealed class GymNetActorBodies
{
    private const float PersonBindRadius = 25f;
    private const float VehicleBindRadius = 45f;
    private const float WalkClipSpeed = 3.4f;
    private const float RunClipSpeed = 5.4f;
    private const float MovingThreshold = 0.25f;
    private const double ExpireSeconds = 1.5d;

    private sealed class Body
    {
        public int HostId;
        public GymNetWorld.Kind Kind;
        public Component Actor;
        public Transform Root;
        public MixamoScanRetargetAnimator Animator;
        public Rigidbody Body3D;
        public readonly GymNetInterpolator Interpolator = new GymNetInterpolator();
        public double LastSeen;
        public Vector3 LastPosition;
        public float Speed;
        public bool Moving;
        public bool Running;
        public bool Frozen;
        public bool Lying;
        public bool Attacking;
        public bool Swinging;
        // BodybuilderIdentity of this body (-1 unknown): a host actor binds to the same person, not the nearest one.
        public int Identity = -1;
        // Built here for a host actor this client never had (arrived later, or a response NPC): removed when the host drops it.
        public bool Spawned;
        // Hidden because the host does not list this actor right now.
        public bool Hidden;
        // Was not in the room when it was adopted: put back out of it when the room ends.
        public bool WasAway;
        // Actors whose model has no authored locomotion animator (the homeless
        // man and the fixer load GLB bodies): their own script used to move them,
        // so the shared walk bob keeps them alive on the guest.
        public Transform BobTarget;
        public Vector3 BobBasePosition;
        public float BobPhase;
    }

    private readonly List<Body> bodies = new List<Body>(GymNetWorld.MaxActors);
    private readonly List<Component> frozenActors = new List<Component>(GymNetWorld.MaxActors);
    private bool directorsFrozen;
    private bool snapshotSeen;

    /// <summary>Actors handed over to the host that are not carrying a host id right now.</summary>
    public int AdoptedCount { get; private set; }
    public int BoundCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++) if (bodies[i].HostId != 0) count++;
            return count;
        }
    }

    /// <summary>Bodies this client built itself for host actors it never had.</summary>
    public int SpawnedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++) if (bodies[i].Spawned) count++;
            return count;
        }
    }

    /// <summary>Bodies built here that are drawn and carry a texture.</summary>
    public int SpawnedDrawnCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Spawned && IsDrawn(bodies[i].Root) && HasTexture(bodies[i].Root)) count++;
            return count;
        }
    }

    /// <summary>Bodies mid-swing because the host's actor is punching.</summary>
    public int PunchingBodyCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Animator != null && bodies[i].Animator.IsPunching) count++;
            return count;
        }
    }

    /// <summary>Adopted actors whose authored animator is still on the body.</summary>
    public int AnimatedBodyCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++) if (bodies[i].Animator != null) count++;
            return count;
        }
    }

    /// <summary>Adopted actors that visibly move: authored clips or the walk bob.</summary>
    public int MovingBodyCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Animator != null || bodies[i].BobTarget != null) count++;
            return count;
        }
    }

    /// <summary>Adopted actors still drawn in the room, not hidden behind a stand-in.</summary>
    public int VisibleBodyCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++) if (IsDrawn(bodies[i].Root)) count++;
            return count;
        }
    }

    /// <summary>Adopted actors that still carry a texture, i.e. kept their look.</summary>
    public int TexturedBodyCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < bodies.Count; i++) if (HasTexture(bodies[i].Root)) count++;
            return count;
        }
    }

    /// <summary>Where one host-driven body stands, for a look at the room.</summary>
    public bool TryGetFirstBoundPosition(out Vector3 position)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            Body body = bodies[i];
            if (body.HostId == 0 || body.Root == null) continue;
            position = body.Root.position;
            return true;
        }
        position = Vector3.zero;
        return false;
    }

    private static bool IsDrawn(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled) return true;
        }
        return false;
    }

    private static bool HasTexture(Transform root)
    {
        if (root == null) return false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i] != null ? renderers[i].sharedMaterial : null;
            if (material == null) continue;
            if (material.mainTexture != null) return true;
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) return true;
        }
        return false;
    }

    /// <summary>
    /// Guest: takes the whole local cast away from its own AI and gives it to
    /// the host. Call once, after the world is built and the join world applied.
    /// </summary>
    public int Adopt()
    {
        // No new people are spawned here any more: the host's snapshot is the
        // population. Already spawned actors stay visible and become bodies.
        if (GymVisitorDirector.Instance != null) GymVisitorDirector.Instance.enabled = false;
        if (GymPoliceDirector.ActiveInstance != null) GymPoliceDirector.ActiveInstance.enabled = false;
        if (GymFixerDirector.Instance != null) GymFixerDirector.Instance.enabled = false;
        directorsFrozen = true;

        var fighters = new List<EnemyFighter>(EnemyFighter.RegisteredFighters);
        for (int i = 0; i < fighters.Count; i++)
        {
            if (fighters[i] != null) Adopt(fighters[i], GymNetWorld.Kind.Fighter);
        }
        if (GymHomelessAgent.Instance != null)
        {
            Adopt(GymHomelessAgent.Instance, GymNetWorld.Kind.Homeless);
        }
        GymFixerAgent fixer = GymFixerDirector.Instance != null ? GymFixerDirector.Instance.Agent : null;
        if (fixer != null) Adopt(fixer, GymNetWorld.Kind.Fixer);
        var cars = new List<GymVisitorVehicle>(GymVisitorVehicle.ActiveGroundTraffic);
        for (int i = 0; i < cars.Count; i++)
        {
            if (cars[i] != null) Adopt(cars[i], GymNetWorld.Kind.Vehicle);
        }
        AdoptedCount = bodies.Count;
        return AdoptedCount;
    }

    private void Adopt(Component actor, GymNetWorld.Kind kind)
    {
        if (actor == null) return;
        GameObject host = actor.gameObject;
        // An actor that is out of the room right now (a visitor not arrived yet) is
        // adopted too, hidden: when the host lists it, it appears with its own body.
        bool away = !host.activeInHierarchy;
        Freeze(host, kind);
        MixamoScanRetargetAnimator animator = host.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        Transform bobTarget = null;
        Vector3 bobBase = Vector3.zero;
        if (animator == null)
        {
            // The first rendered child is the loaded body (a GLB scene or a
            // skinned mesh); bobbing it keeps the walk readable without clips.
            Renderer[] renderers = host.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0 && renderers[0] != null && renderers[0].transform != host.transform)
            {
                bobTarget = renderers[0].transform;
                bobBase = bobTarget.localPosition;
            }
        }
        bodies.Add(new Body
        {
            Identity = actor is EnemyFighter fighter ? (int)fighter.Identity
                : actor is GymVisitorVehicle vehicle ? (int)vehicle.Identity : -1,
            Hidden = away,
            WasAway = away,
            Kind = kind,
            Actor = actor,
            Root = host.transform,
            Animator = animator,
            BobTarget = bobTarget,
            BobBasePosition = bobBase,
            Body3D = host.GetComponent<Rigidbody>(),
            LastPosition = host.transform.position
        });
        frozenActors.Add(actor);
    }

    /// <summary>Stops this body's own simulation; its mesh, rig and animator stay.</summary>
    private static void Freeze(GameObject host, GymNetWorld.Kind kind)
    {
        MonoBehaviour[] behaviours = host.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;
            if (behaviour is EnemyFighter || behaviour is GymVisitorAgent || behaviour is GymPoliceOfficer ||
                behaviour is GymFixerAgent || behaviour is GymHomelessAgent ||
                (kind == GymNetWorld.Kind.Vehicle && behaviour is GymVisitorVehicle))
            {
                behaviour.enabled = false;
            }
        }
        // The host resolves who touches whom. A collider that keeps moving under
        // a stale pose would only trap the local player in the guest's copy.
        Collider[] colliders = host.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
        Rigidbody body = host.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.detectCollisions = false;
        }
    }

    /// <summary>
    /// Guest: one pose from the host. True when a real local body took it, so
    /// no stand-in is needed for this actor.
    /// </summary>
    public bool TryDrive(int hostId, GymNetWorld.Kind kind, GymNetPose pose, int identity = -1, bool attacking = false)
    {
        snapshotSeen = true;
        // A corpse is the same person as the fighter it used to be: it keeps the
        // real body, laid on its side, instead of growing a second stand-in next
        // to the one that was already standing there.
        GymNetWorld.Kind bodyKind = kind == GymNetWorld.Kind.Corpse
            ? GymNetWorld.Kind.Fighter
            : kind;
        Body body = Find(hostId);
        if (body != null)
        {
            if (body.Kind != bodyKind)
            {
                Release(body);
                body = null;
            }
            else
            {
                body.Lying = kind == GymNetWorld.Kind.Corpse;
                body.Attacking = attacking;
                body.LastSeen = Time.unscaledTimeAsDouble;
                body.Interpolator.Add(Time.unscaledTimeAsDouble, pose);
                return true;
            }
        }
        body = Bind(hostId, bodyKind, pose.Position, identity) ?? Spawn(bodyKind, pose.Position, identity);
        if (body == null) return false;
        if (body.Hidden) Show(body);
        body.HostId = hostId;
        body.Attacking = attacking;
        body.Lying = kind == GymNetWorld.Kind.Corpse;
        body.LastSeen = Time.unscaledTimeAsDouble;
        body.Interpolator.Add(Time.unscaledTimeAsDouble, pose);
        return true;
    }

    private Body Find(int hostId)
    {
        if (hostId == 0) return null;
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].HostId == hostId) return bodies[i];
        }
        return null;
    }

    // Nearest free body of the same kind. Both clients built the same world, so
    // the nearest free actor is the one the host means; a plain index would
    // drift as soon as one side had spawned one more visitor.
    private Body Bind(int hostId, GymNetWorld.Kind kind, Vector3 position, int identity)
    {
        Body best = null;
        // The same person wherever it stands; only an actor without an identity falls back to the nearest.
        float bestDistance = identity >= 0 ? float.MaxValue
            : kind == GymNetWorld.Kind.Vehicle ? VehicleBindRadius : PersonBindRadius;
        for (int i = 0; i < bodies.Count; i++)
        {
            Body candidate = bodies[i];
            if (candidate.HostId != 0 || candidate.Root == null) continue;
            if (candidate.Kind != kind) continue;
            if (identity >= 0 && candidate.Identity != identity) continue;
            float distance = Vector3.Distance(candidate.Root.position, position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// A host actor with no body here (it arrived after the join, or only the
    /// host's scene creates it): the same authored body is built for it.
    /// </summary>
    private Body Spawn(GymNetWorld.Kind kind, Vector3 position, int identity)
    {
        bool car = kind == GymNetWorld.Kind.Vehicle;
        if ((!car && kind != GymNetWorld.Kind.Fighter && kind != GymNetWorld.Kind.Police) ||
            !System.Enum.IsDefined(typeof(BodybuilderIdentity), identity) || bodies.Count >= GymNetWorld.MaxActors)
        {
            return null;
        }
        BodybuilderIdentity who = (BodybuilderIdentity)identity;
        GameObject root;
        Component actor;
        if (car)
        {
            GymVisitorVehicle vehicle = GymVisitorVehicle.CreateRaceVisual(who, position, Quaternion.identity);
            actor = vehicle;
            root = vehicle.gameObject;
        }
        else
        {
            root = new GameObject("Net Body - " + who);
            root.transform.position = position;
            BodybuilderEnemyVisual.Build(root, who);
            if (root.GetComponentInChildren<Renderer>(true) == null)
            {
                Object.Destroy(root);
                return null;
            }
            actor = root.transform;
        }
        // The host resolves every touch: a body here is only a picture.
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
        Body body = new Body
        {
            Kind = kind, Actor = actor, Root = root.transform, Identity = identity, Spawned = true,
            Animator = root.GetComponentInChildren<MixamoScanRetargetAnimator>(true),
            LastPosition = position
        };
        bodies.Add(body);
        Debug.Log("GYMCHAOS_NET_BODY_SPAWNED kind=" + kind + " identity=" + who);
        return body;
    }

    private static void Show(Body body)
    {
        body.Hidden = false;
        if (body.Root != null) body.Root.gameObject.SetActive(true);
    }

    private void Release(Body body)
    {
        body.HostId = 0;
        body.Moving = false;
        body.Running = false;
        body.Lying = false;
        body.Speed = 0f;
        if (body.Root != null) body.Root.rotation = Quaternion.Euler(0f, body.Root.eulerAngles.y, 0f);
    }

    /// <summary>A body disappeared from the host's list: it stays where it was.</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < bodies.Count; i++) Release(bodies[i]);
    }

    /// <summary>The room ended: the guest simulates its own world again.</summary>
    public void Restore()
    {
        if (directorsFrozen)
        {
            directorsFrozen = false;
            if (GymVisitorDirector.Instance != null) GymVisitorDirector.Instance.enabled = true;
            if (GymPoliceDirector.ActiveInstance != null) GymPoliceDirector.ActiveInstance.enabled = true;
            if (GymFixerDirector.Instance != null) GymFixerDirector.Instance.enabled = true;
        }
        for (int i = 0; i < frozenActors.Count; i++)
        {
            Component actor = frozenActors[i];
            if (actor == null) continue;
            MonoBehaviour[] behaviours = actor.gameObject.GetComponents<MonoBehaviour>();
            for (int b = 0; b < behaviours.Length; b++)
            {
                MonoBehaviour behaviour = behaviours[b];
                if (behaviour == null) continue;
                if (behaviour is EnemyFighter || behaviour is GymVisitorAgent || behaviour is GymPoliceOfficer ||
                    behaviour is GymFixerAgent || behaviour is GymHomelessAgent ||
                    behaviour is GymVisitorVehicle)
                {
                    behaviour.enabled = true;
                }
            }
            Collider[] colliders = actor.gameObject.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < colliders.Length; c++) colliders[c].enabled = true;
            Rigidbody body = actor.gameObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
            }
        }
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].WasAway) { if (bodies[i].Root != null) bodies[i].Root.gameObject.SetActive(false); }
            else if (bodies[i].Hidden) Show(bodies[i]);
            if (bodies[i].Animator != null) bodies[i].Animator.SetMoving(false, 0f, false);
        }
        frozenActors.Clear();
        snapshotSeen = false;
    }

    /// <summary>Guest: place every bound body at the host's pose and animate it.</summary>
    public void Update(double now, float deltaTime)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            Body body = bodies[i];
            if (body.Root == null) continue;
            if (body.HostId == 0)
            {
                // After the first snapshot the host's list is the population: an actor
                // it does not list is not in the room and is not drawn here either.
                if (snapshotSeen && !body.Hidden)
                {
                    body.Hidden = true;
                    body.Root.gameObject.SetActive(false);
                }
                continue;
            }
            if (!body.Interpolator.TrySample(now, out GymNetPose pose)) continue;
            // The host stopped listing this actor (it died, left or despawned).
            if (now - body.LastSeen > ExpireSeconds)
            {
                if (body.Spawned)
                {
                    Object.Destroy(body.Root.gameObject);
                    bodies.RemoveAt(i--);
                    continue;
                }
                Release(body);
                continue;
            }

            Vector3 position = pose.Position;
            float step = deltaTime > 0f
                ? Vector3.ProjectOnPlane(position - body.LastPosition, Vector3.up).magnitude / deltaTime
                : 0f;
            body.Speed = Mathf.Lerp(body.Speed, step, 0.25f);
            body.LastPosition = position;

            Quaternion rotation = Quaternion.Euler(0f, pose.r, 0f);
            // A dead actor lies where it fell, on its side, at hip height.
            Quaternion facing = body.Lying
                ? rotation * Quaternion.Euler(0f, 0f, 90f)
                : rotation;
            if (body.Body3D != null)
            {
                body.Body3D.position = position + (body.Lying ? Vector3.up * 0.5f : Vector3.zero);
                body.Body3D.rotation = facing;
            }
            body.Root.SetPositionAndRotation(position + (body.Lying ? Vector3.up * 0.5f : Vector3.zero),
                facing);
            if (body.Lying)
            {
                if (body.Animator != null && body.Moving) body.Animator.SetMoving(false, 0f, false);
                body.Moving = false;
                body.Running = false;
                continue;
            }

            bool moving = body.Speed > MovingThreshold;
            bool running = body.Speed > RunClipSpeed;
            if (body.Animator != null)
            {
                // The host's swing, replayed on this body's own punch clip.
                bool swingDone = body.Animator.IsPunchComplete;
                if (body.Attacking && !body.Swinging) body.Animator.TriggerAttack();
                body.Swinging = body.Attacking;
                if (moving != body.Moving || running != body.Running || swingDone)
                {
                    body.Moving = moving;
                    body.Running = running;
                    body.Animator.SetMoving(moving, Mathf.Clamp01(body.Speed / WalkClipSpeed), running);
                }
            }
            else if (body.BobTarget != null)
            {
                body.Moving = moving;
                if (moving) body.BobPhase += deltaTime * Mathf.Clamp(1.6f + body.Speed * 0.35f, 1.6f, 3.2f);
                float bob = moving ? Mathf.Abs(Mathf.Sin(body.BobPhase * Mathf.PI)) * 0.05f : 0f;
                body.BobTarget.localPosition = body.BobBasePosition + Vector3.up * bob;
            }
        }
    }

    public void Clear()
    {
        ReleaseAll();
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].Spawned && bodies[i].Root != null) Object.Destroy(bodies[i].Root.gameObject);
        }
        bodies.Clear();
        frozenActors.Clear();
        AdoptedCount = 0;
    }
}
