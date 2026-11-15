using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum GymRaceItem
{
    None,
    PreWorkout,
    EnergyCan,
    ShakeSpill,
    Creatine
}

/// <summary>
/// Chaos race pickups. Protein tubs stand in rows across the road; a kart that
/// drives through one draws an item:
/// PRE-WORKOUT is a boost, the ENERGY CAN rolls up the road into the kart
/// ahead, the SHAKE SPILL is left behind and spins whoever reaches it, and
/// CREATINE soaks up the next hit. Karts further back draw the faster, meaner
/// items. Every contact test runs in the fixed step, so a slow frame cannot
/// carry a kart through a tub or a can through a kart.
/// </summary>
public sealed class GymRacePickups : MonoBehaviour
{
    public const float BoxRespawnSeconds = 5f;
    public const float CanSeconds = 4f;
    private const int MainRows = 5;
    private const float DrawSeconds = 0.9f;
    private const float BoxReach = 1.1f;
    private const float HitReach = 0.9f;
    private const float SpinSeconds = 1.1f;
    private const float ShieldSeconds = 8f;
    private const float PreWorkoutSeconds = 2.2f;
    private const float CanSpeed = 24f;
    private const float CanSteer = 10f;
    private const float SpillSeconds = 25f;
    private const float SpillArmSeconds = 1.5f;
    // An AI driver that finds no good moment uses its item anyway after this long.
    private const float HoldSeconds = 6f;

    private static readonly string[] Names = { string.Empty, "PRE-WORKOUT", "ENERGY CAN", "SHAKE SPILL", "CREATINE" };
    private static readonly string[] ReadyNames =
        { string.Empty, "PRE-WORKOUT  [E]", "ENERGY CAN  [E]", "SHAKE SPILL  [E]", "CREATINE  [E]" };

    private struct Box
    {
        public Transform Prop;
        public Vector3 Position;
        public float ReturnAt;
    }

    private struct Spill
    {
        public Transform Prop;
        public Vector3 Position;
        public GymRaceRacer Owner;
        public float ArmedAt;
        public float EndsAt;
    }

    private struct Can
    {
        public Transform Prop;
        public Vector3 Position;
        public bool OnShortcut;
        public float Distance;
        public float Lateral;
        public float Speed;
        public GymRaceRacer Owner;
        public GymRaceRacer Target;
        public float EndsAt;
    }

    private readonly List<Box> boxes = new List<Box>();
    private readonly List<Spill> spills = new List<Spill>();
    private readonly List<Can> cans = new List<Can>();
    private IReadOnlyList<GymRaceRacer> racers;
    private Transform[] shieldProps;
    private float clock;

    /// <summary>The racer who presses the item key; every other kart decides for itself.</summary>
    public GymRaceRacer Human;
    /// <summary>Verification: off, the drivers keep what they draw.</summary>
    public bool AiUsesItems = true;
    public bool Running { get; private set; }
    public int Collected { get; private set; }
    public int HitsLanded { get; private set; }
    public int BoxCount => boxes.Count;
    public int SpillCount => spills.Count;
    public int CanCount => cans.Count;
    public Vector3 BoxPosition(int index) => boxes[index].Position;
    public bool BoxActive(int index) => clock >= boxes[index].ReturnAt;
    public Vector3 SpillPosition(int index) => spills[index].Position;
    public Vector3 CanPosition(int index) => cans[index].Position;
    public bool IsShielded(GymRaceRacer racer) => clock < racer.ShieldUntil;

    public static GymRacePickups Create(Transform parent, IReadOnlyList<GymRaceRacer> field)
    {
        GymRacePickups pickups = new GameObject("Race Pickups").AddComponent<GymRacePickups>();
        pickups.transform.SetParent(parent, false);
        pickups.racers = field;
        pickups.shieldProps = new Transform[field.Count];
        float lap = GymRaceTrackLayout.Main.Length;
        for (int row = 0; row < MainRows; row++)
        {
            pickups.AddRow(false, GymRaceTrackLayout.StartLineDistance + lap * (row + 0.5f) / MainRows);
        }
        pickups.AddRow(true, GymRaceTrackLayout.Shortcut.Length * 0.5f);
        Debug.Log("GYMCHAOS_RACE_PICKUPS_BUILT tubs=" + pickups.boxes.Count);
        return pickups;
    }

    private void AddRow(bool onShortcut, float distance)
    {
        RoadPoint(onShortcut, distance, 0f, out _, out _, out float halfWidth);
        float spread = Mathf.Min(halfWidth - 1.8f, 3.2f);
        int count = spread > 1.5f ? 3 : 1;
        for (int i = 0; i < count; i++)
        {
            RoadPoint(onShortcut, distance, count == 1 ? 0f : (i - 1) * spread, out Vector3 point, out Vector3 up, out _);
            Vector3 position = point + up * 0.9f;
            boxes.Add(new Box { Position = position, Prop = Prop("tub_whey", position, 1.1f) });
        }
    }

    /// <summary>GO: a clean field and a clock from zero.</summary>
    public void Begin()
    {
        clock = 0f;
        ResetField();
        Running = true;
    }

    public void Stop()
    {
        Running = false;
    }

    /// <summary>Every tub back, nothing on the road, nobody holding or shielded.</summary>
    public void ResetField()
    {
        for (int i = 0; i < spills.Count; i++) Remove(spills[i].Prop);
        for (int i = 0; i < cans.Count; i++) Remove(cans[i].Prop);
        spills.Clear();
        cans.Clear();
        for (int i = 0; i < boxes.Count; i++)
        {
            Box box = boxes[i];
            box.ReturnAt = 0f;
            boxes[i] = box;
            if (box.Prop != null) box.Prop.gameObject.SetActive(true);
        }
        for (int i = 0; i < racers.Count; i++)
        {
            racers[i].Item = GymRaceItem.None;
            racers[i].ShieldUntil = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (!Running)
        {
            return;
        }
        clock += Time.fixedDeltaTime;
        StepBoxes();
        StepCans(Time.fixedDeltaTime);
        StepSpills();
        if (AiUsesItems)
        {
            StepDrivers();
        }
    }

    private void Update()
    {
        float turn = Time.deltaTime * 90f;
        for (int i = 0; i < boxes.Count; i++)
        {
            if (boxes[i].Prop != null) boxes[i].Prop.Rotate(0f, turn, 0f, Space.World);
        }
        for (int i = 0; i < cans.Count; i++)
        {
            if (cans[i].Prop != null) cans[i].Prop.Rotate(turn * 8f, 0f, 0f, Space.Self);
        }
        // The creatine tub circles the kart it protects.
        for (int i = 0; i < racers.Count; i++)
        {
            bool shielded = Running && racers[i] != null && clock < racers[i].ShieldUntil;
            if (!shielded)
            {
                if (shieldProps[i] != null)
                {
                    Remove(shieldProps[i]);
                    shieldProps[i] = null;
                }
                continue;
            }
            GymRaceKart kart = racers[i].Kart;
            Vector3 centre = kart.transform.position;
            if (shieldProps[i] == null) shieldProps[i] = Prop("tub_creatine", centre, 0.5f);
            Vector3 arm = Quaternion.AngleAxis(Time.time * 220f, Vector3.up) * Vector3.forward;
            shieldProps[i].position = centre + arm * (kart.Radius + 0.9f) + Vector3.up * 0.2f;
        }
    }

    private void StepBoxes()
    {
        for (int i = 0; i < boxes.Count; i++)
        {
            Box box = boxes[i];
            if (clock < box.ReturnAt)
            {
                continue;
            }
            if (box.Prop != null && !box.Prop.gameObject.activeSelf) box.Prop.gameObject.SetActive(true);
            for (int r = 0; r < racers.Count; r++)
            {
                GymRaceRacer racer = racers[r];
                if (racer.Finished || !Touching(racer, box.Position, BoxReach))
                {
                    continue;
                }
                box.ReturnAt = clock + BoxRespawnSeconds;
                boxes[i] = box;
                if (box.Prop != null) box.Prop.gameObject.SetActive(false);
                if (racer.Item == GymRaceItem.None)
                {
                    Grant(racer, Draw(racer), DrawSeconds);
                    Collected++;
                    if (racer == Human) GymAudio.Play2D(GymSoundEffect.RaceBeep, 0.5f);
                }
                break;
            }
        }
    }

    private void StepCans(float dt)
    {
        for (int i = cans.Count - 1; i >= 0; i--)
        {
            Can can = cans[i];
            can.Distance += can.Speed * dt;
            float cut = GymRaceTrackLayout.Shortcut.Length;
            if (can.OnShortcut && can.Distance > cut)
            {
                can.Distance = GymRaceTrackLayout.ShortcutMergeDistance + (can.Distance - cut);
                can.OnShortcut = false;
            }
            // It curves into its target's lane while both are on the same road.
            if (can.Target != null && can.Target.OnShortcut == can.OnShortcut)
            {
                can.Lateral = Mathf.MoveTowards(can.Lateral, can.Target.Lateral, CanSteer * dt);
            }
            RoadPoint(can.OnShortcut, can.Distance, can.Lateral, out Vector3 point, out Vector3 up, out _);
            can.Position = point + up * 0.7f;
            GymRaceRacer struck = Struck(can.Position, can.Owner);
            if (struck != null || clock >= can.EndsAt)
            {
                if (struck != null) Hit(struck);
                Remove(can.Prop);
                cans.RemoveAt(i);
                continue;
            }
            if (can.Prop != null) can.Prop.position = can.Position;
            cans[i] = can;
        }
    }

    private void StepSpills()
    {
        for (int i = spills.Count - 1; i >= 0; i--)
        {
            Spill spill = spills[i];
            // Its owner is still driving off it for the first moment.
            GymRaceRacer struck = Struck(spill.Position, clock < spill.ArmedAt ? spill.Owner : null);
            if (struck == null && clock < spill.EndsAt)
            {
                continue;
            }
            if (struck != null) Hit(struck);
            Remove(spill.Prop);
            spills.RemoveAt(i);
        }
    }

    private void StepDrivers()
    {
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            if (racer == Human || racer.Item == GymRaceItem.None || clock < racer.ItemUseAt)
            {
                continue;
            }
            if (clock > racer.ItemUseAt + HoldSeconds || WorthUsing(racer))
            {
                Use(racer);
            }
        }
    }

    private bool WorthUsing(GymRaceRacer racer)
    {
        switch (racer.Item)
        {
            case GymRaceItem.EnergyCan:
                return Nearest(racer, 45f, true) != null;
            case GymRaceItem.ShakeSpill:
                return Nearest(racer, 25f, false) != null;
            case GymRaceItem.PreWorkout:
                GymRaceTrackPath path = racer.OnShortcut ? GymRaceTrackLayout.Shortcut : GymRaceTrackLayout.Main;
                return !racer.Kart.IsBoosting && GymRaceAIDriver.MaxCurvature(path, racer.BranchDistance, 45f) < 1f / 70f;
            default:
                return true;
        }
    }

    /// <summary>Uses the racer's item; false while there is none, it is still being drawn, or the kart is spinning.</summary>
    public bool Use(GymRaceRacer racer)
    {
        if (!Running || racer == null || racer.Finished || racer.Item == GymRaceItem.None ||
            clock < racer.ItemReadyAt || racer.Kart.SpinTime > 0f)
        {
            return false;
        }
        GymRaceItem item = racer.Item;
        racer.Item = GymRaceItem.None;
        GymRaceKart kart = racer.Kart;
        float halfLength = kart.FootprintHalfExtents.y;
        switch (item)
        {
            case GymRaceItem.PreWorkout:
                kart.StartBoost(PreWorkoutSeconds);
                break;
            case GymRaceItem.EnergyCan:
                Can can = new Can
                {
                    OnShortcut = racer.OnShortcut,
                    Distance = racer.BranchDistance + halfLength + 1.5f,
                    Lateral = racer.Lateral,
                    Speed = Mathf.Max(kart.ForwardSpeed, 0f) + CanSpeed,
                    Owner = racer,
                    Target = Nearest(racer, 60f, true),
                    EndsAt = clock + CanSeconds
                };
                RoadPoint(can.OnShortcut, can.Distance, can.Lateral, out Vector3 point, out Vector3 up, out _);
                can.Position = point + up * 0.7f;
                can.Prop = Prop("can_red", can.Position, 0.7f);
                cans.Add(can);
                GymAudio.Play(GymSoundEffect.RaceMiniTurbo, can.Position, racer == Human ? 0.9f : 0.5f);
                break;
            case GymRaceItem.ShakeSpill:
                DropSpill(racer.OnShortcut, racer.BranchDistance - halfLength - 2.5f, racer.Lateral, racer);
                break;
            case GymRaceItem.Creatine:
                racer.ShieldUntil = clock + ShieldSeconds;
                break;
        }
        return true;
    }

    /// <summary>A spilled shake on the road at a branch distance and lane.</summary>
    public void DropSpill(bool onShortcut, float distance, float lateral, GymRaceRacer owner)
    {
        RoadPoint(onShortcut, distance, lateral, out Vector3 point, out Vector3 up, out _);
        Vector3 position = point + up * 0.45f;
        Transform prop = Prop("rtd_choc", position, 0.9f);
        // The bottle lies on its side across the lane.
        prop.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.Cross(up, Vector3.forward).normalized);
        spills.Add(new Spill
        {
            Position = position, Prop = prop, Owner = owner,
            ArmedAt = clock + SpillArmSeconds, EndsAt = clock + SpillSeconds
        });
    }

    /// <summary>An item lands on a racer: creatine soaks up one hit, otherwise the kart spins out.</summary>
    public bool Hit(GymRaceRacer racer)
    {
        Vector3 at = racer.Kart.transform.position;
        if (clock < racer.ShieldUntil)
        {
            racer.ShieldUntil = 0f;
            GymAudio.Play(GymSoundEffect.RaceWallHit, at, 0.6f);
            return false;
        }
        if (racer.Kart.SpinTime > 0f)
        {
            return false;
        }
        racer.Kart.SpinOut(SpinSeconds);
        HitsLanded++;
        GymAudio.Play(GymSoundEffect.RaceBump, at, racer == Human ? 1f : 0.7f);
        return true;
    }

    /// <summary>Verification: hands the racer an item that is ready now.</summary>
    public void Give(GymRaceRacer racer, GymRaceItem item)
    {
        Grant(racer, item, 0f);
    }

    /// <summary>Item slot text: names flick past while the item is drawn, then the item and its key.</summary>
    public string Display(GymRaceRacer racer)
    {
        if (!Running || racer == null || racer.Item == GymRaceItem.None)
        {
            return string.Empty;
        }
        if (clock < racer.ItemReadyAt)
        {
            return Names[1 + Mathf.FloorToInt(clock * 14f) % (Names.Length - 1)];
        }
        return ReadyNames[(int)racer.Item];
    }

    private void Grant(GymRaceRacer racer, GymRaceItem item, float draw)
    {
        racer.Item = item;
        racer.ItemReadyAt = clock + draw;
        racer.ItemUseAt = racer.ItemReadyAt + Random.Range(0.5f, 3f);
    }

    private GymRaceItem Draw(GymRaceRacer racer)
    {
        // 0 for the leader, 1 for the last kart: the back of the field gets speed
        // and something to throw, the front gets something to leave behind.
        float back = racers.Count > 1 ? Mathf.Clamp01((racer.Place - 1f) / (racers.Count - 1f)) : 0f;
        float boost = 1f + 2f * back;
        float can = racer.Place <= 1 ? 0f : 1.5f;
        float spill = 2f - 1.6f * back;
        float shield = 1.5f - back;
        float roll = Random.value * (boost + can + spill + shield);
        if ((roll -= boost) < 0f) return GymRaceItem.PreWorkout;
        if ((roll -= can) < 0f) return GymRaceItem.EnergyCan;
        if ((roll -= spill) < 0f) return GymRaceItem.ShakeSpill;
        return GymRaceItem.Creatine;
    }

    private GymRaceRacer Struck(Vector3 point, GymRaceRacer immune)
    {
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            if (racer != immune && !racer.Finished && Touching(racer, point, HitReach))
            {
                return racer;
            }
        }
        return null;
    }

    private static bool Touching(GymRaceRacer racer, Vector3 point, float reach)
    {
        GymRaceKart kart = racer.Kart;
        float range = kart.Radius + reach;
        return (kart.transform.position - point).sqrMagnitude < range * range;
    }

    /// <summary>The closest racer within <paramref name="range"/> metres of lap progress, ahead or behind.</summary>
    private GymRaceRacer Nearest(GymRaceRacer racer, float range, bool ahead)
    {
        GymRaceRacer best = null;
        float progress = racer.TotalProgress;
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer other = racers[i];
            if (other == racer || other.Finished)
            {
                continue;
            }
            float gap = ahead ? other.TotalProgress - progress : progress - other.TotalProgress;
            if (gap > 0f && gap < range)
            {
                range = gap;
                best = other;
            }
        }
        return best;
    }

    /// <summary>Road point at a branch distance and lane; past either end of the shortcut it carries on along the main lap.</summary>
    private static void RoadPoint(bool onShortcut, float distance, float lateral, out Vector3 point, out Vector3 up, out float halfWidth)
    {
        GymRaceTrackPath path = GymRaceTrackLayout.Main;
        if (onShortcut)
        {
            GymRaceTrackPath cut = GymRaceTrackLayout.Shortcut;
            if (distance < 0f) distance += GymRaceTrackLayout.ShortcutForkDistance;
            else if (distance > cut.Length) distance = GymRaceTrackLayout.ShortcutMergeDistance + (distance - cut.Length);
            else path = cut;
        }
        path.Sample(distance, out Vector3 centre, out Vector3 tangent, out Vector3 right, out halfWidth);
        float lane = Mathf.Max(0f, halfWidth - 0.8f);
        up = Vector3.Cross(tangent, right).normalized;
        point = centre + right * Mathf.Clamp(lateral, -lane, lane);
    }

    /// <summary>A product model from the protein shop, centred on its holder and scaled to a height.</summary>
    private Transform Prop(string model, Vector3 position, float height)
    {
        Transform holder = new GameObject("Race Item " + model).transform;
        holder.SetParent(transform, false);
        holder.position = position;
        RuntimeGlbSceneLoader.Request(GymProteinShopCatalog.ModelFolder + model + ".glb", holder, position,
            holder.rotation, Vector3.one, model, onLoaded: loaded => Fit(loaded, holder, height));
        return holder;
    }

    private static void Fit(GameObject loaded, Transform holder, float height)
    {
        if (loaded == null || holder == null)
        {
            return;
        }
        Bounds bounds = default;
        bool any = false;
        foreach (MeshFilter filter in loaded.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                continue;
            }
            Bounds local = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 inHolder = holder.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any) bounds = new Bounds(inHolder, Vector3.zero);
                else bounds.Encapsulate(inHolder);
                any = true;
            }
            // Small and moving: not worth a shadow pass.
            if (filter.TryGetComponent(out Renderer renderer)) renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        if (!any || bounds.size.y < 1e-4f)
        {
            return;
        }
        float scale = height / bounds.size.y;
        Transform root = loaded.transform;
        root.localScale *= scale;
        root.localPosition = (root.localPosition - bounds.center) * scale;
    }

    private static void Remove(Transform prop)
    {
        if (prop != null) Destroy(prop.gameObject);
    }
}
