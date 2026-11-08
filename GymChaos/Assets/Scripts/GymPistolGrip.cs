using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the policeman's hands hold the Glock. Solved once per officer from the
/// prop's measured shape and the baked hand skin, then replayed every frame
/// while the gun is out:
/// - the wrist points the barrel where the officer faces;
/// - the handle runs along the knuckle line, its side in the palm, its front
///   strap at the knuckles, the trigger at the index finger's height;
/// - middle, ring and pinky bend joint by joint until they touch the handle
///   (wrapped, never through it); the index finger reaches the trigger; the
///   thumb lies forward on the far side of the frame;
/// - in the two-hand stance the left hand cups the right fingers from the far
///   side: its palm on them, its fingers wrapped round the front, its thumb
///   forward under the right thumb.
/// Finger and thumb rotations and the gun and support-hand poses are stored in
/// the right hand's space, so the hold is identical in every frame of both clips.
/// </summary>
public sealed class GymPistolGrip
{
    // Prop shape in prop-local units (+X barrel, +Y slide), measured on
    // glock17.glb (1.0 long, 0.708 tall, 0.156 wide).
    // shortcut: hard-coded for glock17.glb; measure the mesh if another pistol is added.
    private static readonly Vector3 HandleCentre = new Vector3(-0.305f, -0.188f, 0f);
    private static readonly Vector3 HandleUp = new Vector3(0.07f, 0.29f, 0f).normalized;
    private static readonly Vector3 HandleFront = new Vector3(HandleUp.y, -HandleUp.x, 0f);
    private const float HandleHalfLength = 0.155f;
    private const float HandleHalfDepth = 0.13f;
    private const float HandleHalfWidth = 0.074f;
    private static readonly Vector3 TriggerPoint = new Vector3(0.03f, 0.035f, 0f);
    // Index finger height on the frame (just under the trigger's centre).
    private const float IndexHeight = 0.045f;
    // Solid blocks besides the handle: slide and frame top, the rear frame
    // under the slide (where the web of the hand sits), the trigger guard bar.
    private static readonly Bounds[] Blocks =
    {
        MinMax(-0.5f, 0.09f, -0.078f, 0.5f, 0.354f, 0.078f),
        MinMax(-0.46f, -0.05f, -0.075f, -0.10f, 0.10f, 0.075f),
        MinMax(-0.12f, -0.045f, -0.03f, 0.20f, -0.01f, 0.03f),
    };
    // Grip angle between the barrel and the handle's front normal.
    private static readonly float GripAngle = Mathf.Atan2(HandleUp.x, HandleUp.y) * Mathf.Rad2Deg;
    private const float AimPitchDown = 25f;
    private const float AimPitchUp = 10f;
    private const float ThumbScale = 0.85f;
    private const float IndexScale = 0.7f;
    // Left hand counts as supporting while its clip pose is this close to its grip.
    private const float SupportNear = 0.15f;
    private const float SupportFar = 0.40f;
    private static readonly string[] FingerNames = { "index", "middle", "ring", "pinky" };

    // Solved holds per body mesh: every officer shares the rig. The search
    // takes a few hundred milliseconds, so the verifier bakes its result to
    // Resources/PoliceGrip.json (keyed by the rig) and the game loads that;
    // it only solves at run time when the bake is missing or stale.
    [System.Serializable]
    internal sealed class Solution
    {
        public string Rig;
        public Vector3 GunPos, LeftPos;
        public Quaternion GunRot, LeftRot;
        public Quaternion[] Right = new Quaternion[15];
        public Quaternion[] Left = new Quaternion[15];
        public bool HasSupport;
    }

    private const string BakedAsset = "PoliceGrip";
    private static readonly Dictionary<Mesh, Solution> Solutions = new Dictionary<Mesh, Solution>();
    internal static bool IgnoreBaked;

    // The hold depends on the skeleton, not the vertex count (render-only
    // additions change that between batch and graphics runs).
    private string RigKey => skin != null && skin.sharedMesh != null
        ? skin.sharedMesh.name + ":" + skin.bones.Length : "";

    private Solution FindSolution()
    {
        if (skin == null || skin.sharedMesh == null) return null;
        if (Solutions.TryGetValue(skin.sharedMesh, out Solution known)) return known;
        if (IgnoreBaked) return null;
        TextAsset baked = Resources.Load<TextAsset>(BakedAsset);
        if (baked == null) return null;
        Solution data = JsonUtility.FromJson<Solution>(baked.text);
        if (data == null || data.Rig != RigKey || data.Right == null || data.Right.Length != 15)
        {
            Debug.LogWarning($"GYMCHAOS_POLICE_GRIP_BAKE_STALE rig={RigKey} baked={data?.Rig}", officer);
            return null;
        }
        Solutions[skin.sharedMesh] = data;
        return data;
    }

    private sealed class Hand
    {
        public Transform Wrist, Forearm, UpperArm;
        // index, middle, ring, pinky x 3 joints, then thumb x 3.
        public readonly Transform[] Bones = new Transform[15];
        public readonly Quaternion[] Base = new Quaternion[15];
        public readonly Quaternion[] Written = new Quaternion[15];
        public readonly Quaternion[] Solved = new Quaternion[15];
        public readonly float[] Radius = new float[15];
        // Bone-local offset from the bone line to the finger's centre line.
        public readonly Vector3[] Offset = new Vector3[15];
        public bool Valid;
        public Transform IndexBase => Bones[0];
        public Transform PinkyBase => Bones[9];
    }

    private readonly Transform officer;
    private readonly SkinnedMeshRenderer skin;
    private readonly Hand right = new Hand();
    private readonly Hand left = new Hand();
    private bool solved, supportSolved;
    private Vector3 gunLocalPos;
    private Quaternion gunLocalRot;
    private Vector3 leftLocalPos;
    private Quaternion leftLocalRot;
    public float SupportWeight { get; private set; }

    public GymPistolGrip(Transform officer, BodybuilderEnemyVisual.Rig rig, SkinnedMeshRenderer skin)
    {
        this.officer = officer;
        this.skin = skin;
        Bind(right, rig.RightHand, rig.RightForearm, rig.RightUpperArm);
        // The scan's index is too thick for the trigger guard: a slimmer
        // trigger finger (uniform, so the bent joints are not sheared).
        if (right.Valid) right.Bones[0].localScale *= IndexScale;
        Bind(left, rig.LeftHand, rig.LeftForearm, rig.LeftUpperArm);
    }

    public bool IsSolved => solved;
    // True when the hold came from the bake (no run-time solve).
    public bool UsedBake { get; private set; }
    public Vector3 PalmPoint => right.Valid ? Vector3.Lerp(right.Wrist.position, Knuckles(right), 0.55f) : Vector3.zero;

    /// <summary>Call from LateUpdate after the animator has posed the body.</summary>
    public void Update(Transform gun, bool held)
    {
        if (!right.Valid || gun == null) return;
        if (!held)
        {
            Release(right);
            Release(left);
            SupportWeight = 0f;
            return;
        }
        AimWrist();
        if (!solved)
        {
            AdoptBase(right);
            Solve(gun);
        }
        Apply(right, 1f);
        gun.SetPositionAndRotation(right.Wrist.TransformPoint(gunLocalPos), right.Wrist.rotation * gunLocalRot);
        UpdateSupport(gun);
    }

    // ---------------------------------------------------------------- bones

    private static void Bind(Hand hand, Transform wrist, Transform forearm, Transform upperArm)
    {
        hand.Wrist = wrist;
        hand.Forearm = forearm;
        hand.UpperArm = upperArm;
        if (wrist == null) return;
        Transform[] all = wrist.GetComponentsInChildren<Transform>(true);
        for (int f = 0; f < 5; f++)
        {
            for (int j = 0; j < 3; j++)
            {
                string suffix = f < 4 ? $"f_{FingerNames[f]}.0{j + 1}" : $"thumb.0{j + 1}";
                foreach (Transform child in all)
                {
                    if (child.name.StartsWith("DEF-") && child.name.Contains(suffix))
                    {
                        hand.Bones[f * 3 + j] = child;
                        break;
                    }
                }
                if (hand.Bones[f * 3 + j] == null) return;
            }
        }
        // shortcut: thumbs stay 0.85x (and the right index 0.7x) also with the gun away; scale only while held if it shows.
        hand.Bones[12].localScale *= ThumbScale;
        for (int i = 0; i < 15; i++)
        {
            hand.Base[i] = hand.Bones[i].localRotation;
            hand.Written[i] = hand.Bones[i].localRotation;
        }
        hand.Valid = true;
    }

    // Only a pose this code did not write becomes the new animated base.
    private static void AdoptBase(Hand hand)
    {
        for (int i = 0; i < 15; i++)
        {
            if (Quaternion.Angle(hand.Bones[i].localRotation, hand.Written[i]) > 0.01f)
                hand.Base[i] = hand.Bones[i].localRotation;
        }
    }

    private static void Release(Hand hand)
    {
        if (!hand.Valid) return;
        AdoptBase(hand);
        for (int i = 0; i < 15; i++)
        {
            hand.Bones[i].localRotation = hand.Base[i];
            hand.Written[i] = hand.Base[i];
        }
    }

    private static void Apply(Hand hand, float weight)
    {
        AdoptBase(hand);
        for (int i = 0; i < 15; i++)
        {
            Quaternion pose = Quaternion.Slerp(hand.Base[i], hand.Solved[i], weight);
            hand.Bones[i].localRotation = pose;
            hand.Written[i] = pose;
        }
    }

    // Hand frame: forward wrist-to-knuckles, down index-to-pinky, palm out of the palm.
    private static void Frame(Hand hand, bool isRight, out Vector3 forward, out Vector3 down, out Vector3 palm)
    {
        Vector3 knuckles = (hand.IndexBase.position + hand.PinkyBase.position) * 0.5f;
        forward = (knuckles - hand.Wrist.position).normalized;
        down = Vector3.ProjectOnPlane(hand.PinkyBase.position - hand.IndexBase.position, forward).normalized;
        // Right hand: down x forward points out of the palm; a left hand is the mirror.
        palm = isRight ? Vector3.Cross(down, forward) : Vector3.Cross(forward, down);
    }

    private static Vector3 Knuckles(Hand hand) => (hand.IndexBase.position + hand.PinkyBase.position) * 0.5f;

    // ----------------------------------------------------------------- wrist

    // Barrel along the officer's facing (pitch follows the forearm, clamped),
    // slide upright; the knuckles point down the grip angle from the barrel
    // and the knuckle line runs along the handle.
    private void AimWrist()
    {
        Vector3 forearm = right.Forearm != null ? (right.Wrist.position - right.Forearm.position).normalized : officer.forward;
        Vector3 facing = Vector3.ProjectOnPlane(officer.forward, Vector3.up);
        if (facing.sqrMagnitude < 1e-6f) return;
        facing.Normalize();
        float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(forearm.y, -1f, 1f)) * Mathf.Rad2Deg, -AimPitchDown, AimPitchUp);
        Vector3 barrel = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, facing).normalized) * facing;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, barrel).normalized;
        Quaternion tilt = Quaternion.AngleAxis(-GripAngle, Vector3.Cross(barrel, up));
        Vector3 knucklesGoal = tilt * barrel;
        Vector3 handleUp = tilt * up;
        if (solved)
        {
            // Gun +X barrel, +Y slide: its world rotation, then the hand's
            // through the solved socket.
            Quaternion gunGoal = Quaternion.LookRotation(Vector3.Cross(barrel, up), up);
            right.Wrist.rotation = gunGoal * Quaternion.Inverse(gunLocalRot);
            return;
        }
        Frame(right, true, out Vector3 forward, out Vector3 down, out _);
        Quaternion goal = Quaternion.LookRotation(knucklesGoal, handleUp);
        Quaternion now = Quaternion.LookRotation(forward, -down);
        right.Wrist.rotation = goal * Quaternion.Inverse(now) * right.Wrist.rotation;
    }

    // ----------------------------------------------------------------- solve

    // Fingers and thumb to the skin's bind pose, so the solve never depends
    // on the clip frame it happens to run in.
    private void ToBindPose(Hand hand)
    {
        if (skin == null) return;
        Transform[] skinBones = skin.bones;
        Matrix4x4[] binds = skin.sharedMesh.bindposes;
        for (int i = 0; i < 15; i++)
        {
            Transform bone = hand.Bones[i];
            int b = System.Array.IndexOf(skinBones, bone);
            int parent = System.Array.IndexOf(skinBones, bone.parent);
            if (b < 0 || parent < 0 || b >= binds.Length || parent >= binds.Length) continue;
            Matrix4x4 local = binds[parent] * binds[b].inverse;
            bone.localRotation = local.rotation;
        }
    }

    private void Solve(Transform gun)
    {
        solved = true;
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        Solution cached = FindSolution();
        UsedBake = cached != null && !IgnoreBaked;
        if (cached != null)
        {
            gunLocalPos = cached.GunPos;
            gunLocalRot = cached.GunRot;
            for (int i = 0; i < 15; i++)
            {
                right.Solved[i] = cached.Right[i];
                right.Written[i] = right.Bones[i].localRotation;
            }
            return;
        }
        ToBindPose(right);
        Frame(right, true, out Vector3 forward, out Vector3 down, out Vector3 palm);
        Straighten(right, forward);
        float s = Mathf.Abs(gun.lossyScale.x);
        BakeSkin(out Vector3[] world, out int[] bones);
        MeasureRadii(right, world, bones, palm);
        float palmDepth = PalmDepth(right, true, world, bones);
        List<Vector3> palmSkin = PalmSkin(right, world, bones);

        // Start: handle along the knuckle line, front strap toward the
        // knuckles, its near side on the palm skin, index at trigger height.
        Vector3 up = -down;
        Quaternion baseRotation = Quaternion.LookRotation(forward, up) * Quaternion.Inverse(Quaternion.LookRotation(HandleFront, HandleUp));
        gun.rotation = baseRotation;
        Vector3 wrist = right.Wrist.position;
        Vector3 Gun(Vector3 local) => gun.rotation * (local * s);
        Vector3 centre = Gun(HandleCentre);
        Vector3 front = Gun(HandleCentre + HandleFront * HandleHalfDepth);
        Vector3 indexLine = Gun(new Vector3(HandleCentre.x, IndexHeight, 0f));
        float alongPalm = palmDepth + HandleHalfWidth * s;
        float alongForward = Vector3.Dot(Knuckles(right) - wrist, forward) + 0.7f * Segment(right, 1, 0).magnitude
            - Vector3.Dot(front - centre, forward);
        float alongUp = Vector3.Dot(right.IndexBase.position - wrist, up) - Vector3.Dot(indexLine - centre, up);
        Vector3 baseCentre = wrist + palm * alongPalm + forward * alongForward + up * alongUp;

        // Search the handle's turn about its own axis, its place in the palm
        // and how high the hand sits on it: least palm skin inside the gun,
        // middle/ring/pinky tips on the handle and round onto its far side,
        // the index able to reach the trigger (as on the reference).
        float indexLength = (Segment(right, 0, 0).magnitude + Segment(right, 0, 1).magnitude + Segment(right, 0, 2).magnitude);
        Quaternion[] straight = new Quaternion[12];
        for (int i = 0; i < 12; i++) straight[i] = right.Bones[i].localRotation;
        float best = float.MaxValue;
        Quaternion bestRotation = baseRotation;
        Vector3 bestCentre = baseCentre;
        float margin = 0.003f / Mathf.Max(1e-4f, s);
        for (float dh = -0.025f; dh <= 0.0251f; dh += 0.025f)
        for (float tilt = -10f; tilt <= 10.1f; tilt += 10f)
        for (float yaw = -40f; yaw <= 40f; yaw += 10f)
        {
            // tilt > 0 swings the handle's bottom forward (toward the pinky's
            // set-back knuckle); yaw turns it about its own axis.
            Quaternion rotation = Quaternion.AngleAxis(tilt, palm) * Quaternion.AngleAxis(yaw, up) * baseRotation;
            for (float df = -0.04f; df <= 0.0401f; df += 0.01f)
            {
                for (float dp = -0.02f; dp <= 0.0201f; dp += 0.01f)
                {
                    // dh > 0: the hand higher on the handle (the gun lower).
                    Vector3 handleCentre = baseCentre + forward * df + palm * dp - up * (dh * s);
                    gun.rotation = rotation;
                    gun.position = handleCentre - gun.rotation * (HandleCentre * s);
                    int inside = 0;
                    for (int v = 0; v < palmSkin.Count; v++)
                        if (InsideSolid(gun.InverseTransformPoint(palmSkin[v]), margin)) inside++;
                    float cost = palmSkin.Count > 0 ? 10f * inside / palmSkin.Count : 0f;
                    for (int i = 3; i < 12; i++) right.Bones[i].localRotation = straight[i];
                    for (int f = 1; f < 4; f++)
                    {
                        Wrap(right, f, gun, s, palm, null);
                        cost += FingerCost(right, f, gun, s, palm);
                    }
                    float toTrigger = Vector3.Distance(right.IndexBase.position, gun.TransformPoint(TriggerPoint));
                    cost += Mathf.Max(0f, toTrigger - 0.85f * indexLength);
                    if (cost < best)
                    {
                        best = cost;
                        bestRotation = rotation;
                        bestCentre = handleCentre;
                    }
                }
            }
        }
        gun.rotation = bestRotation;
        gun.position = bestCentre - gun.rotation * (HandleCentre * s);
        for (int i = 0; i < 12; i++) right.Bones[i].localRotation = straight[i];
        for (int f = 1; f < 4; f++) WrapSearch(right, f, gun, s, palm);
        ReachTrigger(gun, s, palm);
        GuideThumb(gun, s, palm);
        Relax(right, gun, s, palm, null);
        // Relaxing may lift fingertips and the thumb off the gun: lay the tips
        // back on, then clear any skin that pressed in again.
        SettleFingersOnSkin(gun, s, palm);
        SettleThumbOnSkin(gun, s);

        for (int i = 0; i < 15; i++)
        {
            right.Solved[i] = right.Bones[i].localRotation;
            right.Written[i] = right.Solved[i];
        }
        gunLocalPos = right.Wrist.InverseTransformPoint(gun.position);
        gunLocalRot = Quaternion.Inverse(right.Wrist.rotation) * gun.rotation;
        if (skin != null)
        {
            Solution solution = new Solution { Rig = RigKey, GunPos = gunLocalPos, GunRot = gunLocalRot };
            System.Array.Copy(right.Solved, solution.Right, 15);
            Solutions[skin.sharedMesh] = solution;
        }
        Debug.Log($"GYMCHAOS_POLICE_GRIP_SOLVED ms={clock.ElapsedMilliseconds} cost={best:F3} palmDepth={palmDepth:F3} " +
            $"radii={right.Radius[3]:F3}/{right.Radius[4]:F3}/{right.Radius[5]:F3} thumbR={right.Radius[13]:F3} gunScale={s:F3} " +
            $"palmInside={SkinInside(gun, s, world, bones, right, false):P1}", officer);
    }

    // The capsule model is an estimate of the skin: re-bake and, while any
    // finger segment's own skin is inside the gun (or inside the other hand's
    // fingers), open that joint 3 degrees. Stops when the skin is clear (or
    // after 12 rounds).
    private void Relax(Hand hand, Transform gun, float s, Vector3 palm, Hand other)
    {
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        float margin = 0.003f / Mathf.Max(1e-4f, s);
        int[] inside = new int[15];
        int[] count = new int[15];
        for (int round = 0; round < 12; round++)
        {
            BakeSkin(out Vector3[] world, out int[] bones);
            System.Array.Clear(inside, 0, 15);
            System.Array.Clear(count, 0, 15);
            for (int v = 0; v < world.Length && v < bones.Length; v++)
            {
                int b = bones[v];
                if (b < 0 || b >= skinBones.Length || skinBones[b] == null) continue;
                Transform bone = skinBones[b];
                int finger = -1;
                for (int i = 0; i < 15 && finger < 0; i++) if (hand.Bones[i] == bone) finger = i;
                if (finger < 0) continue;
                bool hit = InsideSolid(gun.InverseTransformPoint(world[v]), margin) ||
                    (other != null && FingerDistance(other, world[v]) < -0.003f);
                count[finger]++;
                if (hit) inside[finger]++;
            }
            bool changed = false;
            for (int i = 0; i < 15; i++)
            {
                // Only the joint whose own segment's skin is inside opens. The
                // index's base joints (placed on the trigger) and the thumb's
                // metacarpal (routed round the backstrap) keep their searched poses.
                if (i < 2 || i == 12) continue;
                if (count[i] == 0 || inside[i] <= (i < 12 ? 0.01f : 0.02f) * count[i]) continue;
                changed = true;
                Transform bone = hand.Bones[i];
                // Fingers open about the knuckle line (one axis for every
                // joint, so a tip wrapped past the side still opens outward);
                // a thumb joint turns its segment away from the frame.
                Vector3 axis = i < 12
                    ? Vector3.Cross(Knuckles(hand) - hand.Wrist.position, palm)
                    : Vector3.Cross(Segment(hand, 4, i - 12), gun.TransformPoint(HandleCentre) - bone.position);
                if (axis.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.AngleAxis(-3f, axis.normalized) * bone.rotation;
            }
            if (!changed) break;
        }
    }

    // Final wrap of finger f: try every bend of its three joints (from
    // straight, about the knuckle line) and keep the one that touches the gun
    // least while its tip rests on the handle round on the far side.
    private void WrapSearch(Hand hand, int f, Transform gun, float s, Vector3 palm)
    {
        Vector3 axis = Vector3.Cross(Knuckles(hand) - hand.Wrist.position, palm).normalized;
        Quaternion[] start = { hand.Bones[f * 3].localRotation, hand.Bones[f * 3 + 1].localRotation, hand.Bones[f * 3 + 2].localRotation };
        Quaternion[] bestPose = (Quaternion[])start.Clone();
        float best = float.MaxValue;
        for (float a1 = 0f; a1 <= 100f; a1 += 5f)
        for (float a2 = 0f; a2 <= 105f; a2 += 7f)
        for (float a3 = 0f; a3 <= 80f; a3 += 10f)
        {
            for (int j = 0; j < 3; j++) hand.Bones[f * 3 + j].localRotation = start[j];
            float[] bends = { a1, a2, a3 };
            for (int j = 0; j < 3; j++)
                hand.Bones[f * 3 + j].rotation = Quaternion.AngleAxis(bends[j], axis) * hand.Bones[f * 3 + j].rotation;
            float cost = Contacts(hand, f, 0, gun, s, null) * 0.05f + FingerCost(hand, f, gun, s, palm);
            if (cost < best)
            {
                best = cost;
                for (int j = 0; j < 3; j++) bestPose[j] = hand.Bones[f * 3 + j].localRotation;
            }
        }
        for (int j = 0; j < 3; j++) hand.Bones[f * 3 + j].localRotation = bestPose[j];
    }

    // Middle, ring and pinky on the real skin: each round re-bakes; a finger
    // whose middle or tip skin is inside the gun opens 2 degrees at that
    // joint, one whose pad is more than 4 mm off closes its tip (and middle)
    // joint, until every pad rests on the handle.
    private void SettleFingersOnSkin(Transform gun, float s, Vector3 palm)
    {
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        Vector3 axis = Vector3.Cross(Knuckles(right) - right.Wrist.position, palm).normalized;
        float margin = 0.003f / Mathf.Max(1e-4f, s);
        int[] inside = new int[12];
        int[] count = new int[12];
        float[] nearest = new float[4];
        for (int round = 0; round < 40; round++)
        {
            BakeSkin(out Vector3[] world, out int[] bones);
            System.Array.Clear(inside, 0, 12);
            System.Array.Clear(count, 0, 12);
            for (int f = 0; f < 4; f++) nearest[f] = float.MaxValue;
            for (int v = 0; v < world.Length && v < bones.Length; v++)
            {
                int b = bones[v];
                if (b < 0 || b >= skinBones.Length) continue;
                for (int i = 3; i < 12; i++)
                {
                    if (skinBones[b] != right.Bones[i]) continue;
                    Vector3 local = gun.InverseTransformPoint(world[v]);
                    count[i]++;
                    if (InsideSolid(local, margin)) inside[i]++;
                    if (i % 3 == 2) nearest[i / 3] = Mathf.Min(nearest[i / 3], SolidDistance(local) * s);
                    break;
                }
            }
            bool changed = false;
            for (int f = 1; f < 4; f++)
            {
                bool opened = false;
                for (int j = 0; j < 3; j++)
                {
                    int i = f * 3 + j;
                    if (count[i] == 0 || inside[i] <= 0.01f * count[i]) continue;
                    right.Bones[i].rotation = Quaternion.AngleAxis(-2f, axis) * right.Bones[i].rotation;
                    opened = changed = true;
                }
                if (!opened && nearest[f] > 0.004f)
                {
                    // Knuckle first: a finger hanging off the handle's bottom
                    // swings forward onto it, then the outer joints close.
                    right.Bones[f * 3].rotation = Quaternion.AngleAxis(1.5f, axis) * right.Bones[f * 3].rotation;
                    right.Bones[f * 3 + 1].rotation = Quaternion.AngleAxis(1f, axis) * right.Bones[f * 3 + 1].rotation;
                    right.Bones[f * 3 + 2].rotation = Quaternion.AngleAxis(1f, axis) * right.Bones[f * 3 + 2].rotation;
                    changed = true;
                }
            }
            if (round == 39 || !changed)
                Debug.Log($"GYMCHAOS_POLICE_GRIP_SETTLE round={round} gaps={nearest[1]:F3}/{nearest[2]:F3}/{nearest[3]:F3} " +
                    $"inside={inside[4]}/{count[4]},{inside[5]}/{count[5]},{inside[7]}/{count[7]},{inside[8]}/{count[8]},{inside[10]}/{count[10]},{inside[11]}/{count[11]} " +
                    $"pinkyTip={gun.InverseTransformPoint(Tip(right, 3)):F2}", officer);
            if (!changed) break;
        }
    }

    // Right thumb, as on the reference: the metacarpal round behind the
    // backstrap under the tang, the proximal phalanx to the far side, the tip
    // forward along it under the slide. Waypoints first, then a small search
    // round them for the pose that touches least.
    private void GuideThumb(Transform gun, float s, Vector3 palm)
    {
        float side = Vector3.Dot(gun.rotation * Vector3.forward, palm) >= 0f ? 1f : -1f;
        float r = right.Radius[13] / s;
        Vector3 behind = gun.TransformPoint(new Vector3(-0.46f - r, 0.03f, 0f));
        float reach = (Segment(right, 4, 1).magnitude + Segment(right, 4, 2).magnitude) / s;
        Vector3 along = gun.TransformPoint(new Vector3(-0.46f - r + 0.6f * reach, 0.05f, side * (HandleHalfWidth + r)));
        Vector3 forward = gun.rotation * Vector3.right;
        Vector3 axis = (gun.rotation * HandleUp).normalized;
        Vector3[] aims = { behind, along };
        Quaternion[] start = { right.Bones[12].localRotation, right.Bones[13].localRotation, right.Bones[14].localRotation };
        Quaternion[] bestPose = (Quaternion[])start.Clone();
        float best = float.MaxValue;
        for (float y1 = -20f; y1 <= 20f; y1 += 10f)
        for (float p1 = -20f; p1 <= 20f; p1 += 10f)
        for (float y2 = -30f; y2 <= 30f; y2 += 10f)
        {
            for (int j = 0; j < 3; j++) right.Bones[12 + j].localRotation = start[j];
            for (int j = 0; j < 3; j++)
            {
                Transform bone = right.Bones[12 + j];
                Vector3 dir = j < 2 ? (aims[j] - bone.position).normalized : forward;
                if (j == 0) dir = Quaternion.AngleAxis(y1, axis) * Quaternion.AngleAxis(p1, Vector3.Cross(axis, dir)) * dir;
                if (j == 1) dir = Quaternion.AngleAxis(y2, axis) * dir;
                Vector3 now = Segment(right, 4, j);
                if (now.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.FromToRotation(now, dir) * bone.rotation;
            }
            float cost = Contacts(right, 4, 0, gun, s, null) * 0.05f + Vector3.Distance(right.Bones[14].position, along);
            if (cost < best)
            {
                best = cost;
                for (int j = 0; j < 3; j++) bestPose[j] = right.Bones[12 + j].localRotation;
            }
        }
        for (int j = 0; j < 3; j++) right.Bones[12 + j].localRotation = bestPose[j];
        PressThumb(gun, s, 0);
        Debug.Log($"GYMCHAOS_POLICE_GRIP_THUMB base={gun.InverseTransformPoint(right.Bones[12].position):F2} " +
            $"tip={gun.InverseTransformPoint(Tip(right, 4)):F2} behind={gun.InverseTransformPoint(behind):F2} along={gun.InverseTransformPoint(along):F2} " +
            $"length={(Segment(right, 4, 0).magnitude + Segment(right, 4, 1).magnitude + Segment(right, 4, 2).magnitude) / s:F2} cost={best:F3}", officer);
    }

    // Press the thumb onto the frame: turn each joint from `fromJoint` on
    // toward the gun a degree at a time until the thumb touches it, then back
    // off a little (the capsule is an estimate of the pad).
    private void PressThumb(Transform gun, float s, int fromJoint)
    {
        Vector3 centre = gun.TransformPoint(new Vector3(-0.2f, 0.05f, 0f));
        for (int j = fromJoint; j < 3; j++)
        {
            Transform bone = right.Bones[12 + j];
            for (int step = 0; step < 40 && Contacts(right, 4, j, gun, s, null) == 0; step++)
            {
                Vector3 tip = Tip(right, 4) - bone.position;
                Vector3 turn = Vector3.Cross(tip, centre - bone.position);
                if (turn.sqrMagnitude < 1e-10f) break;
                Quaternion before = bone.rotation;
                bone.rotation = Quaternion.AngleAxis(1f, turn.normalized) * bone.rotation;
                if (Contacts(right, 4, j, gun, s, null) > 0)
                {
                    bone.rotation = Quaternion.AngleAxis(-8f, turn.normalized) * before;
                    break;
                }
            }
        }
    }

    // Last thumb step on the real skin: turn the thumb's two outer joints
    // toward the frame 2 degrees at a time until its pad is within 5 mm of
    // it, stopping before any of the tip's skin goes in.
    private void SettleThumbOnSkin(Transform gun, float s)
    {
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        Vector3 centre = gun.TransformPoint(new Vector3(-0.2f, 0.05f, 0f));
        float margin = 0.003f / Mathf.Max(1e-4f, s);
        for (int step = 0; step < 20; step++)
        {
            BakeSkin(out Vector3[] world, out int[] bones);
            float nearest = float.MaxValue;
            int inside = 0;
            for (int v = 0; v < world.Length && v < bones.Length; v++)
            {
                int b = bones[v];
                if (b < 0 || b >= skinBones.Length || skinBones[b] != right.Bones[14]) continue;
                Vector3 local = gun.InverseTransformPoint(world[v]);
                if (InsideSolid(local, margin)) inside++;
                nearest = Mathf.Min(nearest, SolidDistance(local) * s);
            }
            if (inside > 0 || nearest < 0.005f) break;
            for (int j = 1; j < 3; j++)
            {
                Transform bone = right.Bones[12 + j];
                Vector3 turn = Vector3.Cross(Tip(right, 4) - bone.position, centre - bone.position);
                if (turn.sqrMagnitude > 1e-10f) bone.rotation = Quaternion.AngleAxis(2f, turn.normalized) * bone.rotation;
            }
        }
    }

    // A point on the far side of the frame (the side the palm faces), the
    // given radius off its surface; `local` gives x and y, z is the side.
    private static Vector3 FarSide(Transform gun, float s, Vector3 palm, Vector3 local, float radius)
    {
        float side = Vector3.Dot(gun.rotation * Vector3.forward, palm) >= 0f ? 1f : -1f;
        return gun.TransformPoint(new Vector3(local.x, local.y, side * HandleHalfWidth)) + palm * radius;
    }

    // How far a wrapped finger is from resting round onto the far panel:
    // its tip's gap to the gun plus how short of the far side it stops.
    private static float FingerCost(Hand hand, int f, Transform gun, float s, Vector3 palm)
    {
        Vector3 tip = Tip(hand, f);
        float gap = Mathf.Max(0f, SolidDistance(gun.InverseTransformPoint(tip)) * s - hand.Radius[f * 3 + 2]);
        Vector3 centre = gun.TransformPoint(HandleCentre);
        float far = Vector3.Dot(tip - centre, palm);
        return gap + Mathf.Max(0f, HandleHalfWidth * s - far);
    }

    // Palm skin (hand and palm bones, not fingers or thumb) in world space.
    private List<Vector3> PalmSkin(Hand hand, Vector3[] world, int[] bones)
    {
        List<Vector3> points = new List<Vector3>();
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        for (int v = 0; v < world.Length && v < bones.Length; v += 2)
        {
            int b = bones[v];
            if (b < 0 || b >= skinBones.Length || skinBones[b] == null) continue;
            Transform bone = skinBones[b];
            if (bone == hand.Wrist || (bone.parent == hand.Wrist && bone.name.StartsWith("DEF-palm"))) points.Add(world[v]);
        }
        return points;
    }

    // Every finger and the thumb straight ahead from its knuckle (bends are
    // solved from here, in planes across the knuckle line).
    private static void Straighten(Hand hand, Vector3 forward)
    {
        for (int f = 0; f < 4; f++)
        {
            for (int j = 0; j < 3; j++)
            {
                Transform bone = hand.Bones[f * 3 + j];
                Vector3 along = Segment(hand, f, j);
                if (along.sqrMagnitude > 1e-8f)
                    bone.rotation = Quaternion.FromToRotation(along, forward) * bone.rotation;
            }
        }
    }

    // Knuckle-to-tip direction of finger f (4 = thumb), joint j.
    private static Vector3 Segment(Hand hand, int f, int j)
    {
        Transform bone = hand.Bones[f * 3 + j];
        if (j < 2) return hand.Bones[f * 3 + j + 1].position - bone.position;
        if (bone.childCount > 0) return bone.GetChild(0).position - bone.position;
        return bone.position - hand.Bones[f * 3 + j - 1].position;
    }

    private static Vector3 Tip(Hand hand, int f) => hand.Bones[f * 3 + 2].position + Segment(hand, f, 2);

    // Bend each joint toward the palm in 2 degree steps until the next step
    // would touch a solid (finger skin radius), so the finger rests on it.
    private void Wrap(Hand hand, int f, Transform gun, float s, Vector3 palm, Hand other)
    {
        float[] limits = { 100f, 105f, 80f };
        for (int j = 0; j < 3; j++)
        {
            Transform bone = hand.Bones[f * 3 + j];
            for (float angle = 0f; angle < limits[j]; angle += 2f)
            {
                Quaternion before = bone.rotation;
                // Toward the palm side: a segment stops turning once it points
                // across the handle, so the finger hooks round, never back.
                Vector3 axis = Vector3.Cross(Segment(hand, f, j), palm);
                if (axis.sqrMagnitude < 1e-8f) break;
                bone.rotation = Quaternion.AngleAxis(2f, axis.normalized) * bone.rotation;
                // Only this segment: the later ones fold round the solid next.
                if (Contacts(hand, f, j, gun, s, other, j) > 0)
                {
                    bone.rotation = before;
                    break;
                }
            }
        }
    }

    // Capsule samples of finger f, joints fromJoint..toJoint, within their
    // skin radius of a solid (or of the other hand's fingers).
    private int Contacts(Hand hand, int f, int fromJoint, Transform gun, float s, Hand other, int toJoint = 2, bool trigger = false)
    {
        int contacts = 0;
        for (int j = fromJoint; j <= toJoint; j++)
        {
            Vector3 a = hand.Bones[f * 3 + j].position;
            Vector3 b = a + Segment(hand, f, j);
            float r = hand.Radius[f * 3 + j];
            for (int k = 1; k <= 4; k++)
            {
                Vector3 p = Centre(hand, f * 3 + j, Vector3.Lerp(a, b, k / 4f));
                // The index may rest on the guard and the frame's side; the
                // middle finger's top rests against the guard's underside.
                int skip = trigger ? 0b110 : (f == 1 ? 0b100 : 0);
                if (SolidDistance(gun.InverseTransformPoint(p), skip) * s < r) contacts++;
                else if (other != null && FingerDistance(other, p) < r) contacts++;
            }
        }
        return contacts;
    }

    // Distance from a world point to the nearest finger segment surface of a hand.
    private static float FingerDistance(Hand hand, Vector3 p)
    {
        float best = float.MaxValue;
        for (int f = 0; f < 5; f++)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 a = Centre(hand, f * 3 + j, hand.Bones[f * 3 + j].position);
                Vector3 ab = Segment(hand, f, j);
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector3.Distance(p, a + ab * t) - hand.Radius[f * 3 + j]);
            }
        }
        return best;
    }

    // Index finger: the knuckle-and-middle joint pair (third joint half the
    // middle) that brings the tip closest to the trigger without touching the
    // frame, the guard or the handle.
    private void ReachTrigger(Transform gun, float s, Vector3 palm)
    {
        Vector3 trigger = gun.TransformPoint(TriggerPoint);
        Quaternion[] start = { right.Bones[0].localRotation, right.Bones[1].localRotation, right.Bones[2].localRotation };
        float best = float.MaxValue;
        Quaternion[] bestPose = (Quaternion[])start.Clone();
        for (float a1 = 0f; a1 <= 50f; a1 += 5f)
        {
            for (float a2 = 0f; a2 <= 100f; a2 += 5f)
            {
                for (int j = 0; j < 3; j++) right.Bones[j].localRotation = start[j];
                float[] bends = { a1, a2, a2 * 0.5f };
                for (int j = 0; j < 3; j++)
                {
                    Vector3 axis = Vector3.Cross(Segment(right, 0, j), palm);
                    if (axis.sqrMagnitude > 1e-8f)
                        right.Bones[j].rotation = Quaternion.AngleAxis(bends[j], axis.normalized) * right.Bones[j].rotation;
                }
                float distance = Contacts(right, 0, 0, gun, s, null, 2, true) * 0.05f + Vector3.Distance(Tip(right, 0), trigger);
                if (distance < best)
                {
                    best = distance;
                    for (int j = 0; j < 3; j++) bestPose[j] = right.Bones[j].localRotation;
                }
            }
        }
        for (int j = 0; j < 3; j++) right.Bones[j].localRotation = bestPose[j];
    }

    // Thumb: search the metacarpal's direction over the half space toward the
    // target and the phalanges' turn about the handle axis (across behind the
    // backstrap, then forward along the far side), keeping the pose that
    // touches least and ends closest to `target`.
    private void PlaceThumb(Hand hand, Transform gun, float s, Vector3 palm, Vector3 target, Hand other)
    {
        Vector3 axis = (gun.rotation * HandleUp).normalized;
        Vector3 reach = (target - hand.Bones[12].position).normalized;
        Vector3 side = Vector3.Cross(axis, reach).normalized;
        Quaternion[] start = { hand.Bones[12].localRotation, hand.Bones[13].localRotation, hand.Bones[14].localRotation };
        Quaternion[] bestPose = (Quaternion[])start.Clone();
        float best = float.MaxValue;
        for (float yaw = -90f; yaw <= 90f; yaw += 15f)
        {
            for (float pitch = -45f; pitch <= 45f; pitch += 15f)
            {
                Vector3 d1 = Quaternion.AngleAxis(yaw, axis) * Quaternion.AngleAxis(pitch, side) * reach;
                for (float turn = -75f; turn <= 75f; turn += 25f)
                {
                    for (int j = 0; j < 3; j++) hand.Bones[12 + j].localRotation = start[j];
                    Vector3 dir = d1;
                    for (int j = 0; j < 3; j++)
                    {
                        if (j > 0) dir = Quaternion.AngleAxis(j == 1 ? turn : turn * 0.6f, axis) * dir;
                        Transform bone = hand.Bones[12 + j];
                        Vector3 along = Segment(hand, 4, j);
                        if (along.sqrMagnitude > 1e-8f) bone.rotation = Quaternion.FromToRotation(along, dir) * bone.rotation;
                    }
                    float cost = Contacts(hand, 4, 0, gun, s, other) * 0.05f + Vector3.Distance(Tip(hand, 4), target);
                    if (cost < best)
                    {
                        best = cost;
                        for (int j = 0; j < 3; j++) bestPose[j] = hand.Bones[12 + j].localRotation;
                    }
                }
            }
        }
        for (int j = 0; j < 3; j++) hand.Bones[12 + j].localRotation = bestPose[j];
    }

    // ------------------------------------------------------------- support

    private void UpdateSupport(Transform gun)
    {
        if (!left.Valid || left.Forearm == null || left.UpperArm == null) return;
        float s = Mathf.Abs(gun.lossyScale.x);
        Vector3 clipWrist = left.Wrist.position;
        Vector3 rightGrip = gun.TransformPoint(HandleCentre);
        SupportWeight = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(SupportNear, SupportFar, Vector3.Distance(clipWrist, rightGrip)));
        if (SupportWeight <= 0f)
        {
            Release(left);
            return;
        }
        if (!supportSolved)
        {
            AdoptBase(left);
            Quaternion[] arm = { left.UpperArm.rotation, left.Forearm.rotation, left.Wrist.rotation };
            SolveSupport(gun, s);
            // Back to the clip pose; the blend below moves the arm.
            left.UpperArm.rotation = arm[0];
            left.Forearm.rotation = arm[1];
            left.Wrist.rotation = arm[2];
            clipWrist = left.Wrist.position;
        }
        Vector3 wristGoal = Vector3.Lerp(clipWrist, right.Wrist.TransformPoint(leftLocalPos), SupportWeight);
        Quaternion rotationGoal = Quaternion.Slerp(left.Wrist.rotation, right.Wrist.rotation * leftLocalRot, SupportWeight);
        SolveTwoBoneArm(left.UpperArm, left.Forearm, left.Wrist, wristGoal);
        left.Wrist.rotation = rotationGoal;
        Apply(left, SupportWeight);
    }

    // Left palm flat on the right fingers on the far side of the handle,
    // knuckle line along the handle; then fingers wrap round the front over
    // the right fingers and the thumb lies forward under the right thumb.
    private void SolveSupport(Transform gun, float s)
    {
        supportSolved = true;
        Solution cached = FindSolution();
        if (cached != null && cached.HasSupport && cached.Left != null && cached.Left.Length == 15)
        {
            leftLocalPos = cached.LeftPos;
            leftLocalRot = cached.LeftRot;
            for (int i = 0; i < 15; i++) left.Solved[i] = cached.Left[i];
            return;
        }
        Frame(right, true, out Vector3 forward, out Vector3 down, out Vector3 palm);
        ToBindPose(left);
        Frame(left, false, out Vector3 leftForward, out Vector3 leftDown, out _);
        left.Wrist.rotation = Quaternion.LookRotation(forward, -down) *
            Quaternion.Inverse(Quaternion.LookRotation(leftForward, -leftDown)) * left.Wrist.rotation;
        Straighten(left, forward);
        BakeSkin(out Vector3[] world, out int[] bones);
        if (right.Radius[4] <= 0f) MeasureRadii(right, world, bones, palm);
        MeasureRadii(left, world, bones, -palm);
        float leftPalmDepth = PalmDepth(left, false, world, bones);
        float rightFinger = 2f * right.Radius[3];
        Vector3 centre = gun.TransformPoint(HandleCentre);
        // Bone-level palm point: the palm skin rests on the right fingertips.
        Vector3 palmGoal = centre + palm * (HandleHalfWidth * s + rightFinger + leftPalmDepth) + down * (HandleHalfLength * s * 0.3f);
        Vector3 palmNow = Vector3.Lerp(left.Wrist.position, Knuckles(left), 0.55f);
        Vector3 wristGoal = left.Wrist.position + (palmGoal - palmNow);
        SolveTwoBoneArm(left.UpperArm, left.Forearm, left.Wrist, wristGoal);
        left.Wrist.rotation = Quaternion.LookRotation(forward, -down) *
            Quaternion.Inverse(Quaternion.LookRotation((Knuckles(left) - left.Wrist.position).normalized,
                -Vector3.ProjectOnPlane(left.PinkyBase.position - left.IndexBase.position, forward).normalized)) * left.Wrist.rotation;
        // Solve the fingers where the arm actually reaches; that is the pose replayed.
        float reachGap = Vector3.Distance(wristGoal, left.Wrist.position);
        wristGoal = left.Wrist.position;
        Vector3 leftPalm = -palm;
        for (int f = 0; f < 4; f++) Wrap(left, f, gun, s, leftPalm, right);
        PlaceThumb(left, gun, s, leftPalm,
            FarSide(gun, s, palm, new Vector3(-0.08f, -0.02f, 0f), left.Radius[14] + right.Radius[14]), right);
        Relax(left, gun, s, leftPalm, right);
        for (int i = 0; i < 15; i++)
        {
            left.Solved[i] = left.Bones[i].localRotation;
            left.Written[i] = left.Solved[i];
        }
        leftLocalPos = right.Wrist.InverseTransformPoint(wristGoal);
        leftLocalRot = Quaternion.Inverse(right.Wrist.rotation) * left.Wrist.rotation;
        if (skin != null && Solutions.TryGetValue(skin.sharedMesh, out Solution solution))
        {
            solution.LeftPos = leftLocalPos;
            solution.LeftRot = leftLocalRot;
            System.Array.Copy(left.Solved, solution.Left, 15);
            solution.HasSupport = true;
        }
        Debug.Log($"GYMCHAOS_POLICE_SUPPORT_SOLVED palmDepth={leftPalmDepth:F3} reachGap={reachGap:F3}", officer);
    }

    private static void SolveTwoBoneArm(Transform upper, Transform fore, Transform end, Vector3 target)
    {
        Vector3 a = upper.position;
        Vector3 b = fore.position;
        Vector3 c = end.position;
        float l1 = Vector3.Distance(a, b);
        float l2 = Vector3.Distance(b, c);
        Vector3 toTarget = target - a;
        if (l1 < 0.001f || l2 < 0.001f || toTarget.sqrMagnitude < 1e-6f) return;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
        Vector3 dir = toTarget.normalized;
        // Bend in the clip's own elbow plane.
        Vector3 pole = Vector3.ProjectOnPlane(b - a, dir);
        if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(Vector3.down, dir);
        pole.Normalize();
        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
        Vector3 elbow = a + dir * (l1 * cosA) + pole * (l1 * sinA);
        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        fore.rotation = Quaternion.FromToRotation(end.position - fore.position, a + dir * d - fore.position) * fore.rotation;
    }

    // ---------------------------------------------------------------- shape

    private static Bounds MinMax(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        Bounds b = new Bounds();
        b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
        return b;
    }

    /// <summary>Distance (prop units) from a prop-local point to the solid gun; 0 inside.</summary>
    // `skip`: bit mask of Blocks left out (1 = rear frame, 2 = guard bar).
    public static float SolidDistance(Vector3 local, int skip = 0)
    {
        Vector3 d = local - HandleCentre;
        float u = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, HandleUp)) - HandleHalfLength);
        float n = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, HandleFront)) - HandleHalfDepth);
        float w = Mathf.Max(0f, Mathf.Abs(d.z) - HandleHalfWidth);
        float best = Mathf.Sqrt(u * u + n * n + w * w);
        for (int i = 0; i < Blocks.Length; i++)
        {
            if ((skip & (1 << i)) != 0) continue;
            Vector3 e = Vector3.Max(Vector3.zero, Vector3.Max(Blocks[i].min - local, local - Blocks[i].max));
            best = Mathf.Min(best, e.magnitude);
        }
        return best;
    }

    /// <summary>True when a prop-local point is deeper than `margin` (prop units) inside the solid gun.</summary>
    public static bool InsideSolid(Vector3 local, float margin)
    {
        Vector3 d = local - HandleCentre;
        if (Mathf.Abs(Vector3.Dot(d, HandleUp)) < HandleHalfLength - margin &&
            Mathf.Abs(Vector3.Dot(d, HandleFront)) < HandleHalfDepth - margin &&
            Mathf.Abs(d.z) < HandleHalfWidth - margin) return true;
        for (int i = 0; i < Blocks.Length; i++)
        {
            Bounds b = Blocks[i];
            if (local.x > b.min.x + margin && local.x < b.max.x - margin &&
                local.y > b.min.y + margin && local.y < b.max.y - margin &&
                local.z > b.min.z + margin && local.z < b.max.z - margin) return true;
        }
        return false;
    }

    // ----------------------------------------------------------------- skin

    private void BakeSkin(out Vector3[] world, out int[] bones)
    {
        world = System.Array.Empty<Vector3>();
        bones = System.Array.Empty<int>();
        if (skin == null || skin.sharedMesh == null || !skin.sharedMesh.isReadable) return;
        Mesh scratch = new Mesh();
        world = GymSkinSampler.BakeWorld(skin, scratch);
        Object.Destroy(scratch);
        bones = GymSkinSampler.GetDominantBones(skin.sharedMesh);
    }

    // Finger thickness per bone: how far its skin reaches from the bone line.
    private void MeasureRadii(Hand hand, Vector3[] world, int[] bones, Vector3 palm)
    {
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        List<float> palmar = new List<float>();
        List<float> dorsal = new List<float>();
        for (int i = 0; i < 15; i++)
        {
            int f = i / 3, j = i % 3;
            Vector3 a = hand.Bones[i].position;
            Vector3 ab = Segment(hand, f, j);
            Vector3 n = Vector3.ProjectOnPlane(palm, ab).normalized;
            palmar.Clear();
            dorsal.Clear();
            for (int v = 0; v < world.Length && v < bones.Length; v++)
            {
                int b = bones[v];
                if (b < 0 || b >= skinBones.Length || skinBones[b] != hand.Bones[i]) continue;
                // Mid-segment skin only: the knuckle ends carry palm and joint bulges.
                float t = Vector3.Dot(world[v] - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude);
                if (t < 0.25f || t > 0.75f) continue;
                float h = Vector3.Dot(world[v] - (a + ab * t), n);
                if (h >= 0f) palmar.Add(h); else dorsal.Add(-h);
            }
            palmar.Sort();
            dorsal.Sort();
            float p90 = palmar.Count > 0 ? palmar[(int)(palmar.Count * 0.9f)] : 0.012f;
            float d90 = dorsal.Count > 0 ? dorsal[(int)(dorsal.Count * 0.9f)] : 0.012f;
            // The bone runs near the back of the finger: the capsule's centre
            // sits toward the pad, its radius is half the finger's thickness.
            hand.Radius[i] = 0.5f * (p90 + d90);
            // The thumb's pad does not face the palm direction: centred capsule.
            hand.Offset[i] = f < 4 ? hand.Bones[i].InverseTransformDirection(n) * (0.5f * (p90 - d90)) : Vector3.zero;
        }
        // The knuckle bone's skin includes the palm's knuckle pad (the thumb's
        // its thenar): use the next phalanx's thickness for it.
        for (int f = 0; f < 5; f++)
        {
            hand.Radius[f * 3] = hand.Radius[f * 3 + 1];
            Vector3 next = hand.Bones[f * 3 + 1].TransformDirection(hand.Offset[f * 3 + 1]);
            hand.Offset[f * 3] = hand.Bones[f * 3].InverseTransformDirection(next);
        }
    }

    // Capsule axis point of a finger segment: the bone line moved to the
    // finger's own centre.
    private static Vector3 Centre(Hand hand, int i, Vector3 bonePoint) =>
        bonePoint + hand.Bones[i].TransformDirection(hand.Offset[i]);

    // Palm skin height above the hand bone line toward the palm (90th percentile
    // of the palm skin between the wrist and the knuckles).
    private float PalmDepth(Hand hand, bool isRight, Vector3[] world, int[] bones)
    {
        Frame(hand, isRight, out Vector3 forward, out _, out Vector3 palm);
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        Vector3 wrist = hand.Wrist.position;
        float length = Vector3.Dot(Knuckles(hand) - wrist, forward);
        List<float> heights = new List<float>();
        for (int v = 0; v < world.Length && v < bones.Length; v++)
        {
            int b = bones[v];
            if (b < 0 || b >= skinBones.Length || skinBones[b] != hand.Wrist) continue;
            float along = Vector3.Dot(world[v] - wrist, forward);
            if (along < length * 0.3f || along > length * 0.9f) continue;
            heights.Add(Vector3.Dot(world[v] - wrist, palm));
        }
        if (heights.Count == 0) return 0.03f;
        heights.Sort();
        return Mathf.Max(0.01f, heights[(int)(heights.Count * 0.9f)]);
    }

    // Share of a hand's skin (the hand bone, or its fingers too) inside the solid gun.
    private float SkinInside(Transform gun, float s, Vector3[] world, int[] bones, Hand hand, bool fingers)
    {
        Transform[] skinBones = skin != null ? skin.bones : System.Array.Empty<Transform>();
        int inside = 0, count = 0;
        float margin = 0.003f / Mathf.Max(1e-4f, s);
        for (int v = 0; v < world.Length && v < bones.Length; v++)
        {
            int b = bones[v];
            if (b < 0 || b >= skinBones.Length || skinBones[b] == null) continue;
            Transform bone = skinBones[b];
            if (fingers ? !(bone == hand.Wrist || bone.IsChildOf(hand.Wrist)) : bone != hand.Wrist) continue;
            count++;
            if (InsideSolid(gun.InverseTransformPoint(world[v]), margin)) inside++;
        }
        return count > 0 ? inside / (float)count : 0f;
    }

#if UNITY_EDITOR
    /// <summary>Verifier: the solved hold as the Resources/PoliceGrip.json text, or null.</summary>
    public string ExportBaked()
    {
        if (skin == null || !Solutions.TryGetValue(skin.sharedMesh, out Solution solution) || !solution.HasSupport) return null;
        return JsonUtility.ToJson(solution, true);
    }

    /// <summary>Verifier: forget solved holds and, when `ignoreBaked`, solve instead of loading the bake.</summary>
    public static void ResetForVerification(bool ignoreBaked)
    {
        Solutions.Clear();
        IgnoreBaked = ignoreBaked;
    }

    /// <summary>Verifier measures of the current pose (skin baked now).</summary>
    public struct Measures
    {
        public float RightInside, LeftInside, LeftThroughRight, WrapGap, TriggerGap, ThumbSide, ThumbGap;
        public bool ThumbInside;
        public string InsideBones;
    }

    public Measures Measure(Transform gun)
    {
        Measures m = default;
        float s = Mathf.Abs(gun.lossyScale.x);
        BakeSkin(out Vector3[] world, out int[] bones);
        if (right.Radius[4] <= 0f)
        {
            Frame(right, true, out _, out _, out Vector3 measurePalm);
            MeasureRadii(right, world, bones, measurePalm);
        }
        m.RightInside = SkinInside(gun, s, world, bones, right, true);
        {
            Transform[] skinBones = skin.bones;
            Dictionary<string, int> perBone = new Dictionary<string, int>();
            float margin = 0.003f / Mathf.Max(1e-4f, s);
            for (int v = 0; v < world.Length && v < bones.Length; v++)
            {
                int b = bones[v];
                if (b < 0 || b >= skinBones.Length || skinBones[b] == null) continue;
                Transform bone = skinBones[b];
                if (bone != right.Wrist && !bone.IsChildOf(right.Wrist)) continue;
                if (!InsideSolid(gun.InverseTransformPoint(world[v]), margin)) continue;
                perBone.TryGetValue(bone.name, out int n);
                perBone[bone.name] = n + 1;
            }
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> pair in perBone) text.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
            m.InsideBones = text.ToString();
        }
        m.LeftInside = left.Valid && SupportWeight > 0.5f ? SkinInside(gun, s, world, bones, left, true) : 0f;
        // Fingertip skin (the distal bones' vertices): middle, ring and pinky
        // touching the gun, the index pad at the trigger, the thumb on the far side.
        Transform[] tipBones = skin.bones;
        float[] nearest = { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
        Vector3 thumbSum = Vector3.zero;
        int thumbCount = 0;
        int thumbInsideCount = 0;
        Vector3 trigger = gun.TransformPoint(TriggerPoint);
        float tipMargin = 0.003f / Mathf.Max(1e-4f, s);
        for (int v = 0; v < world.Length && v < bones.Length; v++)
        {
            int b = bones[v];
            if (b < 0 || b >= tipBones.Length) continue;
            for (int f = 0; f < 5; f++)
            {
                if (tipBones[b] != right.Bones[f * 3 + 2]) continue;
                Vector3 local = gun.InverseTransformPoint(world[v]);
                float d = f == 0 ? Vector3.Distance(world[v], trigger) : SolidDistance(local) * s;
                nearest[f] = Mathf.Min(nearest[f], d);
                if (f == 4)
                {
                    thumbSum += world[v];
                    thumbCount++;
                    if (InsideSolid(local, tipMargin)) thumbInsideCount++;
                }
            }
        }
        m.WrapGap = Mathf.Max(nearest[1], Mathf.Max(nearest[2], nearest[3]));
        m.InsideBones += $" tipGaps={nearest[1]:F3}/{nearest[2]:F3}/{nearest[3]:F3}";
        m.TriggerGap = nearest[0];
        m.ThumbGap = nearest[4];
        Frame(right, true, out _, out _, out Vector3 palm);
        Vector3 centre = gun.TransformPoint(new Vector3(HandleCentre.x, 0f, 0f));
        Vector3 thumbPad = thumbCount > 0 ? thumbSum / thumbCount : Tip(right, 4);
        m.ThumbSide = Vector3.Dot(thumbPad - centre, palm) / Mathf.Max(1e-4f, HandleHalfWidth * s);
        // A few pad vertices pressing the frame is contact, not passing through.
        m.ThumbInside = thumbInsideCount > 0.02f * Mathf.Max(1, thumbCount);
        if (left.Valid && SupportWeight > 0.5f)
        {
            // Left finger skin deeper than half a right finger inside the right fingers: passing through.
            Transform[] skinBones = skin.bones;
            int through = 0, count = 0;
            for (int v = 0; v < world.Length && v < bones.Length; v++)
            {
                int b = bones[v];
                if (b < 0 || b >= skinBones.Length || skinBones[b] == null) continue;
                Transform bone = skinBones[b];
                if (bone == left.Wrist || !bone.IsChildOf(left.Wrist)) continue;
                count++;
                if (FingerDistance(right, world[v]) < -right.Radius[3] * 0.5f) through++;
            }
            m.LeftThroughRight = count > 0 ? through / (float)count : 0f;
        }
        return m;
    }
#endif
}
