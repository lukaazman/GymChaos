using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The world as the network sees it. Only the host simulates people, cars and
/// doors. It sends a snapshot about 8 times a second ('n'): every actor's id,
/// kind, position and yaw, and every automatic door's openness. Guests turn
/// their own actors off and show a <see cref="GymNetPuppets"/> body per id,
/// blended 0.12 s in the past; guest doors follow the host.
/// Wire format: "n" + actors ';'-separated "id,kind,x,y,z,yaw" + '|' + doors ';'-separated "name=blend".
/// </summary>
public static class GymNetWorld
{
    public const char SnapshotTag = 'n';
    public const float SnapshotIntervalSeconds = 0.125f;
    public const int MaxActors = 160;
    public const int MaxDoors = 8;

    public enum Kind { Fighter = 0, Police = 1, Homeless = 2, Fixer = 3, Vehicle = 4, Corpse = 5 }

    private static readonly Dictionary<Component, int> hostIds = new Dictionary<Component, int>(128);
    private static int nextId = 1;
    private static readonly StringBuilder builder = new StringBuilder(4096);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        hostIds.Clear();
        nextId = 1;
    }

    private static int IdOf(Component actor)
    {
        if (!hostIds.TryGetValue(actor, out int id))
        {
            id = nextId++;
            hostIds[actor] = id;
        }
        return id;
    }

    private static void Add(StringBuilder text, Component actor, Kind kind, ref int count)
    {
        if (count >= MaxActors) return;
        Vector3 position = actor.transform.position;
        if (count > 0) text.Append(';');
        text.Append(IdOf(actor)).Append(',').Append((int)kind).Append(',');
        AppendFixed(text, position.x);
        text.Append(',');
        AppendFixed(text, position.y);
        text.Append(',');
        AppendFixed(text, position.z);
        text.Append(',').Append(Mathf.RoundToInt(actor.transform.eulerAngles.y));
        count++;
    }

    // Two decimals without allocating a string per number.
    private static void AppendFixed(StringBuilder text, float value)
    {
        int hundredths = Mathf.RoundToInt(value * 100f);
        if (hundredths < 0) { text.Append('-'); hundredths = -hundredths; }
        text.Append(hundredths / 100).Append('.');
        int fraction = hundredths % 100;
        if (fraction < 10) text.Append('0');
        text.Append(fraction);
    }

    /// <summary>Host: the snapshot of everything that moves or opens in this world.</summary>
    public static string EncodeSnapshot()
    {
        builder.Clear();
        builder.Append(SnapshotTag);
        int count = 0;
        IReadOnlyList<EnemyFighter> fighters = EnemyFighter.RegisteredFighters;
        for (int i = 0; i < fighters.Count; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null || !fighter.isActiveAndEnabled || GymRaceDirector.IsRaceDriver(fighter)) continue;
            // A dead fighter stays in the list as a body lying on the floor, so guests see it too.
            Add(builder, fighter, fighter.IsDead ? Kind.Corpse : fighter.IsPolice ? Kind.Police : Kind.Fighter, ref count);
        }
        GymHomelessAgent homeless = GymHomelessAgent.Instance;
        if (homeless != null && homeless.isActiveAndEnabled) Add(builder, homeless, Kind.Homeless, ref count);
        GymFixerAgent fixer = GymFixerDirector.Instance != null ? GymFixerDirector.Instance.Agent : null;
        if (fixer != null && fixer.isActiveAndEnabled) Add(builder, fixer, Kind.Fixer, ref count);
        IReadOnlyList<GymVisitorVehicle> cars = GymVisitorVehicle.ActiveGroundTraffic;
        for (int i = 0; i < cars.Count; i++)
        {
            if (cars[i] != null && cars[i].isActiveAndEnabled) Add(builder, cars[i], Kind.Vehicle, ref count);
        }
        builder.Append('|');
        IReadOnlyList<GymAutoDoor> doors = GymAutoDoor.AllDoors;
        for (int i = 0; i < doors.Count && i < MaxDoors; i++)
        {
            if (doors[i] == null) continue;
            if (i > 0) builder.Append(';');
            builder.Append(doors[i].DoorName).Append('=');
            AppendFixed(builder, doors[i].SignedOpenBlend);
        }
        // The day clock: a paused guest (or a host resting on a bench) would otherwise drift out of step.
        builder.Append('|');
        GymTimeOfDay clock = GymTimeOfDay.Instance;
        if (clock != null)
        {
            AppendFixed(builder, clock.Time01);
            builder.Append(',').Append(clock.CurrentDay);
        }
        if (hostIds.Count > MaxActors * 4) PurgeIds();
        return builder.ToString();
    }

    private static void PurgeIds()
    {
        var dead = new List<Component>();
        foreach (Component key in hostIds.Keys) if (key == null) dead.Add(key);
        foreach (Component key in dead) hostIds.Remove(key);
    }

    public struct ActorState
    {
        public int Id;
        public Kind Kind;
        public GymNetPose Pose;
    }

    /// <summary>Guest: reads a snapshot from an untrusted host. False when it is not one; bad entries are skipped.</summary>
    public static bool TryDecode(string body, List<ActorState> actors, Dictionary<string, float> doors) =>
        TryDecode(body, actors, doors, out _, out _);

    /// <summary>As above, plus the host's day clock (time01 -1 when the snapshot carries none).</summary>
    public static bool TryDecode(string body, List<ActorState> actors, Dictionary<string, float> doors, out float time01, out int day)
    {
        time01 = -1f;
        day = 0;
        actors.Clear();
        doors.Clear();
        if (string.IsNullOrEmpty(body) || body[0] != SnapshotTag || body.Length > 60000) return false;
        int split = body.IndexOf('|');
        if (split < 0) return false;
        // Scanned in place: no substrings or arrays per entry.
        int i = 1;
        while (i < split && actors.Count < MaxActors)
        {
            int entryEnd = body.IndexOf(';', i);
            if (entryEnd < 0 || entryEnd > split) entryEnd = split;
            int p = i;
            if (Number(body, ref p, entryEnd, out float id) && Number(body, ref p, entryEnd, out float kind) && kind >= 0f && kind <= (float)Kind.Corpse &&
                Number(body, ref p, entryEnd, out float x) && Number(body, ref p, entryEnd, out float y) &&
                Number(body, ref p, entryEnd, out float z) && Number(body, ref p, entryEnd, out float r))
            {
                actors.Add(new ActorState { Id = (int)id, Kind = (Kind)(int)kind, Pose = new GymNetPose { x = x, y = y, z = z, r = r } });
            }
            i = entryEnd + 1;
        }
        int clockSplit = body.IndexOf('|', split + 1);
        int doorsEnd = clockSplit < 0 ? body.Length : clockSplit;
        if (clockSplit >= 0)
        {
            int p = clockSplit + 1;
            if (Number(body, ref p, body.Length, out float t) && Number(body, ref p, body.Length, out float d) && t >= 0f && t <= 1f)
            {
                time01 = t;
                day = (int)d;
            }
        }
        i = split + 1;
        while (i < doorsEnd && doors.Count < MaxDoors)
        {
            int entryEnd = body.IndexOf(';', i);
            if (entryEnd < 0 || entryEnd > doorsEnd) entryEnd = doorsEnd;
            int eq = body.IndexOf('=', i, entryEnd - i);
            if (eq > i)
            {
                int p = eq + 1;
                if (Number(body, ref p, entryEnd, out float blend)) doors[body.Substring(i, eq - i)] = Mathf.Clamp(blend, -1f, 1f);
            }
            i = entryEnd + 1;
        }
        return true;
    }

    // Reads "-12.34" up to ',' or the end; false for anything else (NaN, letters, empty, huge).
    private static bool Number(string s, ref int p, int end, out float value)
    {
        value = 0f;
        int start = p;
        bool negative = false;
        if (p < end && s[p] == '-') { negative = true; p++; }
        float whole = 0f;
        int digits = 0;
        while (p < end && s[p] >= '0' && s[p] <= '9') { whole = whole * 10f + (s[p] - '0'); p++; digits++; }
        float fraction = 0f;
        if (p < end && s[p] == '.')
        {
            p++;
            float scale = 0.1f;
            while (p < end && s[p] >= '0' && s[p] <= '9') { fraction += (s[p] - '0') * scale; scale *= 0.1f; p++; digits++; }
        }
        if (digits == 0 || (p < end && s[p] != ',')) return false;
        if (p < end) p++;
        value = (whole + fraction) * (negative ? -1f : 1f);
        return Mathf.Abs(value) < 10000000f;
    }

    /// <summary>Guest: the host is gone, doors sense people again.</summary>
    public static void ReleaseDoors()
    {
        IReadOnlyList<GymAutoDoor> doors = GymAutoDoor.AllDoors;
        for (int i = 0; i < doors.Count; i++) if (doors[i] != null) doors[i].StopNetworkDrive();
    }

    /// <summary>Guest: turns off everything the host simulates. Run once, after the world is built.</summary>
    public static int SuppressLocalActors()
    {
        int suppressed = 0;
        if (GymVisitorDirector.Instance != null) { GymVisitorDirector.Instance.enabled = false; suppressed++; }
        if (GymPoliceDirector.ActiveInstance != null) { GymPoliceDirector.ActiveInstance.enabled = false; suppressed++; }
        if (GymFixerDirector.Instance != null) { GymFixerDirector.Instance.enabled = false; suppressed++; }
        if (GymHomelessAgent.Instance != null) { GymHomelessAgent.Instance.gameObject.SetActive(false); suppressed++; }
        // Copies: deactivating removes the entries from the live lists.
        var fighters = new List<EnemyFighter>(EnemyFighter.RegisteredFighters);
        foreach (EnemyFighter fighter in fighters)
        {
            if (fighter == null) continue;
            fighter.gameObject.SetActive(false);
            suppressed++;
        }
        var cars = new List<GymVisitorVehicle>(GymVisitorVehicle.ActiveGroundTraffic);
        foreach (GymVisitorVehicle car in cars)
        {
            if (car == null) continue;
            car.gameObject.SetActive(false);
            suppressed++;
        }
        return suppressed;
    }
}

/// <summary>A guest's stand-in bodies for the host's actors: one per id, interpolated, removed when the host stops listing them.</summary>
public sealed class GymNetPuppets
{
    private const double ExpireSeconds = 1.5d;
    // The guest's day clock is pulled to the host's only when it is this far off (a fraction of a day).
    private const float ClockToleranceDay = 0.01f;
    private const float WalkBobMetres = 0.05f;

    private sealed class Puppet
    {
        public GameObject Root;
        public Transform Body;
        public readonly GymNetInterpolator Interpolator = new GymNetInterpolator();
        public double LastSeen;
        public Vector3 LastPosition;
        public float Speed;
        public float Cycle;
        public GymNetWorld.Kind Kind;
        public float BaseY;
    }

    private readonly Dictionary<int, Puppet> puppets = new Dictionary<int, Puppet>(64);
    private readonly List<GymNetWorld.ActorState> actors = new List<GymNetWorld.ActorState>(64);
    private readonly Dictionary<string, float> doors = new Dictionary<string, float>(GymNetWorld.MaxDoors);
    private readonly List<int> expired = new List<int>(8);
    private Material[] materials;

    public int Count => puppets.Count;
    public bool TryGetPosition(int id, out Vector3 position)
    {
        position = default;
        if (!puppets.TryGetValue(id, out Puppet puppet) || puppet.Root == null) return false;
        position = puppet.Root.transform.position;
        return true;
    }

    /// <summary>True when the puppet of this actor is drawn as a body lying down.</summary>
    public bool IsLying(int id) => puppets.TryGetValue(id, out Puppet puppet) && puppet.Body != null && Mathf.Abs(puppet.Body.up.y) < 0.1f;

    /// <summary>Takes a snapshot message from the host: new poses for puppets, door targets for the doors.</summary>
    public bool Apply(string body, double now)
    {
        if (!GymNetWorld.TryDecode(body, actors, doors, out float hostTime, out int hostDay)) return false;
        GymTimeOfDay clock = GymTimeOfDay.Instance;
        if (clock != null && hostTime >= 0f)
        {
            float drift = Mathf.Abs(Mathf.DeltaAngle(clock.Time01 * 360f, hostTime * 360f)) / 360f;
            if (drift > ClockToleranceDay || clock.CurrentDay != hostDay) clock.RestoreSession(hostTime, hostDay);
        }
        for (int i = 0; i < actors.Count; i++)
        {
            GymNetWorld.ActorState actor = actors[i];
            // Dead or revived: the same actor now looks different, so it gets a new puppet.
            if (puppets.TryGetValue(actor.Id, out Puppet existing) && existing.Kind != actor.Kind) Remove(actor.Id);
            if (!puppets.TryGetValue(actor.Id, out Puppet puppet))
            {
                if (puppets.Count >= GymNetWorld.MaxActors) continue;
                puppet = puppets[actor.Id] = Create(actor.Kind, actor.Id);
            }
            puppet.LastSeen = now;
            puppet.Interpolator.Add(now, actor.Pose);
        }
        foreach (KeyValuePair<string, float> door in doors)
        {
            GymAutoDoor target = GymAutoDoor.Find(door.Key);
            if (target != null) target.DriveFromNetwork(door.Value);
        }
        return true;
    }

    public void Update(double now, float deltaTime)
    {
        expired.Clear();
        foreach (KeyValuePair<int, Puppet> pair in puppets)
        {
            Puppet puppet = pair.Value;
            if (puppet.Root == null || now - puppet.LastSeen > ExpireSeconds) { expired.Add(pair.Key); continue; }
            if (!puppet.Interpolator.TrySample(now, out GymNetPose pose)) continue;
            Vector3 position = pose.Position;
            float step = deltaTime > 0f ? Vector3.ProjectOnPlane(position - puppet.LastPosition, Vector3.up).magnitude / deltaTime : 0f;
            puppet.Speed = Mathf.Lerp(puppet.Speed, step, 0.2f);
            puppet.LastPosition = position;
            puppet.Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, pose.r, 0f));
            bool moving = puppet.Speed > 0.3f;
            if (moving) puppet.Cycle += deltaTime * 2.2f;
            puppet.Body.localPosition = new Vector3(0f, puppet.BaseY + (moving ? Mathf.Abs(Mathf.Sin(puppet.Cycle * Mathf.PI)) * WalkBobMetres : 0f), 0f);
        }
        foreach (int id in expired) Remove(id);
    }

    public void Clear()
    {
        foreach (Puppet puppet in puppets.Values) if (puppet.Root != null) Discard(puppet.Root);
        puppets.Clear();
        if (materials == null) return;
        foreach (Material material in materials) if (material != null) Discard(material);
        materials = null;
    }

    private void Remove(int id)
    {
        if (puppets.TryGetValue(id, out Puppet puppet) && puppet.Root != null) Discard(puppet.Root);
        puppets.Remove(id);
    }

    private static void Discard(UnityEngine.Object target)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(target); else UnityEngine.Object.DestroyImmediate(target);
    }

    private Puppet Create(GymNetWorld.Kind kind, int id)
    {
        if (materials == null)
        {
            materials = new[]
            {
                GymMaterialLibrary.NewLit("Puppet Fighter", PersonaMenuStyle.Muted, 0f, 0.3f),
                GymMaterialLibrary.NewLit("Puppet Police", PersonaMenuStyle.Navy, 0f, 0.3f),
                GymMaterialLibrary.NewLit("Puppet Homeless", GymPalette.Concrete, 0f, 0.2f),
                GymMaterialLibrary.NewLit("Puppet Fixer", PersonaMenuStyle.Gold, 0f, 0.3f),
                GymMaterialLibrary.NewLit("Puppet Vehicle", GymPalette.Steel, 0.2f, 0.4f),
                GymMaterialLibrary.NewLit("Puppet Corpse", GymPalette.Concrete, 0f, 0.5f)
            };
        }
        bool car = kind == GymNetWorld.Kind.Vehicle;
        GameObject root = new GameObject("Net Puppet " + id);
        GameObject shape = GameObject.CreatePrimitive(car ? PrimitiveType.Cube : PrimitiveType.Capsule);
        shape.name = "Body";
        shape.transform.SetParent(root.transform, false);
        shape.transform.localScale = car ? new Vector3(2f, 1.4f, 4.6f) : new Vector3(0.6f, 0.9f, 0.6f);
        shape.transform.localPosition = car ? new Vector3(0f, 0.7f, 0f) : Vector3.zero;
        float baseY = 0f;
        if (kind == GymNetWorld.Kind.Corpse)
        {
            // Lying on the floor: the capsule on its side, its radius above the ground.
            shape.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            baseY = 0.3f;
        }
        Discard(shape.GetComponent<Collider>());
        MeshRenderer renderer = shape.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = materials[(int)kind];
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return new Puppet { Root = root, Body = shape.transform, LastPosition = root.transform.position, Kind = kind, BaseY = baseY };
    }
}
