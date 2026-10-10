using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum FixerIssueKind
{
    Panel,
    Item,
    Corpse
}

/// <summary>One thing Jolly Dog will repair: a broken pane, a displaced prop or a corpse.</summary>
public sealed class FixerIssue
{
    public string Key;
    public FixerIssueKind Kind;
    public GlassShatterPanel Panel;
    public PickupItem Item;
    public EnemyFighter Fighter;
    public float SinceDay;

    public bool Exists => Kind == FixerIssueKind.Panel ? Panel != null
        : Kind == FixerIssueKind.Item ? Item != null
        : Fighter != null;

    public Vector3 Position
    {
        get
        {
            switch (Kind)
            {
                case FixerIssueKind.Panel:
                    return Panel != null ? Panel.transform.position : Vector3.zero;
                case FixerIssueKind.Item:
                    return Item != null ? Item.transform.position : Vector3.zero;
                default:
                    return Fighter != null ? Fighter.VisitorPhysicsPosition : Vector3.zero;
            }
        }
    }
}

/// <summary>
/// Watches the gym for things that are out of the ordinary compared with the
/// state at the start of play: shattered mirrors/windows, props that were
/// moved away from where they were placed and are lying still, and people
/// lying dead. When anything stays unrepaired for five in-game days, Jolly
/// Dog drops from the daytime sky (with a rainbow) and fixes everything that
/// was broken when he arrived.
/// </summary>
public sealed class GymFixerDirector : MonoBehaviour
{
    public const float DaysBeforeArrival = 5f;
    private const float ScanInterval = 1f;
    private const float BaselineSettleSeconds = 3f;
    private const float BaselineRefreshSeconds = 30f;
    private const float DisplacedDistance = 0.35f;
    private const float DisplacedAngle = 30f;
    private const float RestSecondsBeforeDisplaced = 2f;
    private const float RainbowFadeInSeconds = 3f;
    private const float RainbowFadeOutSeconds = 4f;
    private static readonly int RainbowFadeId = Shader.PropertyToID("_GymRainbowFade");
    private static readonly int RainbowAxisId = Shader.PropertyToID("_GymRainbowAxis");

    private sealed class ItemBaseline
    {
        public PickupItem item;
        public Rigidbody body;
        public Transform parent;
        public bool hadParent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public bool kinematic;
        public bool gravity;
        public bool detectCollisions;
        public Vector3 lastPosition;
        public float restSince;
        public bool restoring;

        public Vector3 HomePosition => hadParent && parent != null
            ? parent.TransformPoint(localPosition) : worldPosition;
        public Quaternion HomeRotation => hadParent && parent != null
            ? parent.rotation * localRotation : worldRotation;
    }

    private static readonly Dictionary<string, float> restoredAges =
        new Dictionary<string, float>(System.StringComparer.Ordinal);

    private readonly Dictionary<string, float> issueSince =
        new Dictionary<string, float>(System.StringComparer.Ordinal);
    private readonly List<FixerIssue> currentIssues = new List<FixerIssue>();
    private readonly Dictionary<PickupItem, ItemBaseline> baselines =
        new Dictionary<PickupItem, ItemBaseline>();
    private readonly List<GlassShatterPanel> panels = new List<GlassShatterPanel>();
    private readonly HashSet<EnemyFighter> reviving = new HashSet<EnemyFighter>();
    private readonly HashSet<string> seenThisScan = new HashSet<string>(System.StringComparer.Ordinal);
    private float startedAt;
    private bool baselineCaptured;
    private float nextScanTime;
    private float nextBaselineRefresh;
    private GymFixerAgent agent;
    private bool visitActive;
    private float rainbowFade;
    private bool spawnSuspended;
    private bool agentPrepareFailed;
    private const float PrepareAheadDays = 1f;

    public static GymFixerDirector Instance { get; private set; }
    public IReadOnlyList<FixerIssue> CurrentIssues => currentIssues;
    public GymFixerAgent Agent => agent;
    public bool IsVisitActive => visitActive;
    public float RainbowFade => rainbowFade;
    public int VisitCount { get; private set; }
    public int FixedCount { get; private set; }
    public bool BaselineCaptured => baselineCaptured;
    public int TrackedItemCount => baselines.Count;

    public static GymFixerDirector CreateForScene()
    {
        if (Instance != null)
        {
            return Instance;
        }
        GameObject host = new GameObject("Gym Fixer Director");
        return host.AddComponent<GymFixerDirector>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        restoredAges.Clear();
        Instance = null;
    }

    private void Awake()
    {
        Instance = this;
        startedAt = Time.time;
        Shader.SetGlobalFloat(RainbowFadeId, 0f);
    }

    private void Start()
    {
        Debug.Log("GYMCHAOS_FIXER_DIRECTOR_READY", this);
    }

    /// <summary>
    /// Loads and fits Jolly Dog (~0.2 s). Done lazily, a day before he can
    /// be due, so sessions where nothing breaks never pay for him.
    /// </summary>
    public GymFixerAgent EnsureAgentPrepared()
    {
        if (agent != null || agentPrepareFailed)
        {
            return agent;
        }
        GameObject agentObject = new GameObject("Jolly Dog");
        agentObject.transform.SetParent(transform, false);
        agent = agentObject.AddComponent<GymFixerAgent>();
        if (!agent.Prepare(this))
        {
            Debug.LogError("GYMCHAOS_FIXER_PREPARE_FAILED", this);
            Destroy(agentObject);
            agent = null;
            agentPrepareFailed = true;
        }
        return agent;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Shader.SetGlobalFloat(RainbowFadeId, 0f);
        }
    }

    private float DayStamp
    {
        get
        {
            GymTimeOfDay time = GymTimeOfDay.Instance;
            return time != null ? time.CurrentDay + time.Time01 : 0f;
        }
    }

    private void Update()
    {
        if (!baselineCaptured && Time.time - startedAt >= BaselineSettleSeconds)
        {
            CaptureBaselines();
        }
        if (baselineCaptured && Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + ScanInterval;
            Scan();
            TrySpawn();
        }
        UpdateRainbow();
    }

    // ----------------------------------------------------------- baselines

    private void CaptureBaselines()
    {
        baselineCaptured = true;
        nextBaselineRefresh = Time.time + BaselineRefreshSeconds;
        RefreshTrackedObjects();
        Debug.Log(
            $"GYMCHAOS_FIXER_BASELINE items={baselines.Count} panels={panels.Count}", this);
    }

    private void RefreshTrackedObjects()
    {
        panels.Clear();
        panels.AddRange(FindObjectsByType<GlassShatterPanel>(
            FindObjectsInactive.Include, FindObjectsSortMode.None));
        List<PickupItem> gone = null;
        foreach (PickupItem tracked in baselines.Keys)
        {
            if (tracked == null)
            {
                (gone ??= new List<PickupItem>()).Add(tracked);
            }
        }
        if (gone != null)
        {
            for (int i = 0; i < gone.Count; i++)
            {
                baselines.Remove(gone[i]);
            }
        }
        PickupItem[] items = FindObjectsByType<PickupItem>(FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            PickupItem item = items[i];
            if (item == null || baselines.ContainsKey(item) || item.IsHeld)
            {
                continue;
            }
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null || (!body.isKinematic && !body.IsSleeping() &&
                body.linearVelocity.sqrMagnitude > 0.01f))
            {
                // Only record props resting where the level placed them.
                continue;
            }
            Transform itemTransform = item.transform;
            baselines.Add(item, new ItemBaseline
            {
                item = item,
                body = body,
                parent = itemTransform.parent,
                hadParent = itemTransform.parent != null,
                localPosition = itemTransform.localPosition,
                localRotation = itemTransform.localRotation,
                worldPosition = itemTransform.position,
                worldRotation = itemTransform.rotation,
                kinematic = body.isKinematic,
                gravity = body.useGravity,
                detectCollisions = body.detectCollisions,
                lastPosition = itemTransform.position,
                restSince = Time.time
            });
        }
    }

    // ---------------------------------------------------------------- scan

#if UNITY_EDITOR
    internal bool IsTrackingForVerification(PickupItem item)
    {
        RefreshTrackedObjects();
        return item != null && baselines.ContainsKey(item);
    }
#endif

    public void ScanNowForVerification()
    {
        if (!baselineCaptured)
        {
            CaptureBaselines();
        }
        Scan();
    }

    private void Scan()
    {
        if (Time.time >= nextBaselineRefresh)
        {
            nextBaselineRefresh = Time.time + BaselineRefreshSeconds;
            RefreshTrackedObjects();
        }

        float now = DayStamp;
        currentIssues.Clear();
        seenThisScan.Clear();
        for (int i = 0; i < panels.Count; i++)
        {
            GlassShatterPanel panel = panels[i];
            if (panel != null && panel.IsShattered)
            {
                AddIssue("panel:" + panel.StableId, FixerIssueKind.Panel, now, panel: panel);
            }
        }

        foreach (KeyValuePair<PickupItem, ItemBaseline> pair in baselines)
        {
            if (IsDisplaced(pair.Value))
            {
                AddIssue("item:" + pair.Key.GetEntityId(), FixerIssueKind.Item, now,
                    item: pair.Key);
            }
        }

        EnemyFighter[] fighters = FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && fighters[i].IsSettledCorpse && !reviving.Contains(fighters[i]))
            {
                AddIssue("corpse:" + fighters[i].GetEntityId(), FixerIssueKind.Corpse, now,
                    fighter: fighters[i]);
            }
        }

        // Anything repaired by other means stops counting toward a visit.
        if (issueSince.Count != seenThisScan.Count)
        {
            List<string> stale = null;
            foreach (string key in issueSince.Keys)
            {
                if (!seenThisScan.Contains(key))
                {
                    (stale ??= new List<string>()).Add(key);
                }
            }
            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    issueSince.Remove(stale[i]);
                }
            }
        }
    }

    private void AddIssue(
        string key, FixerIssueKind kind, float now,
        GlassShatterPanel panel = null, PickupItem item = null, EnemyFighter fighter = null)
    {
        if (!issueSince.TryGetValue(key, out float since))
        {
            since = restoredAges.TryGetValue(key, out float restored) ? restored : now;
            restoredAges.Remove(key);
            issueSince[key] = since;
            Debug.Log($"GYMCHAOS_FIXER_ISSUE_SEEN key={key} since={since:F2}", this);
        }
        seenThisScan.Add(key);
        currentIssues.Add(new FixerIssue
        {
            Key = key,
            Kind = kind,
            Panel = panel,
            Item = item,
            Fighter = fighter,
            SinceDay = since
        });
    }

    private bool IsDisplaced(ItemBaseline baseline)
    {
        PickupItem item = baseline.item;
        if (item == null || baseline.body == null || baseline.restoring)
        {
            return false;
        }
        Transform itemTransform = item.transform;
        Vector3 position = itemTransform.position;
        bool resting = (position - baseline.lastPosition).sqrMagnitude < 0.0004f;
        baseline.lastPosition = position;
        if (!resting || item.IsHeld)
        {
            baseline.restSince = Time.time;
            return false;
        }
        // A kinematic prop that moved to another parent is in use by an
        // exercise station (a racked bar on a squatting member), not lost.
        if (baseline.body.isKinematic && itemTransform.parent != baseline.parent)
        {
            return false;
        }
        if (Time.time - baseline.restSince < RestSecondsBeforeDisplaced)
        {
            return false;
        }
        return IsAwayFromHome(baseline, DisplacedDistance, DisplacedAngle);
    }

    private static bool IsAwayFromHome(ItemBaseline baseline, float distance, float angle)
    {
        Transform itemTransform = baseline.item.transform;
        bool moved = (itemTransform.position - baseline.HomePosition).sqrMagnitude >
            distance * distance;
        // Balls look the same at any rotation.
        bool turned = baseline.item.ItemType != WeightType.Ball &&
            Quaternion.Angle(itemTransform.rotation, baseline.HomeRotation) > angle;
        return moved || turned;
    }

    // --------------------------------------------------------------- spawn

    public float OldestIssueAgeDays
    {
        get
        {
            float now = DayStamp;
            float oldest = 0f;
            for (int i = 0; i < currentIssues.Count; i++)
            {
                oldest = Mathf.Max(oldest, now - currentIssues[i].SinceDay);
            }
            return oldest;
        }
    }

    public void SetSpawnSuspendedForVerification(bool suspended)
    {
        spawnSuspended = suspended;
    }

    private void TrySpawn()
    {
        if (visitActive || spawnSuspended || currentIssues.Count == 0)
        {
            return;
        }
        float oldest = OldestIssueAgeDays;
        if (oldest >= DaysBeforeArrival - PrepareAheadDays && EnsureAgentPrepared() == null)
        {
            return;
        }
        GymTimeOfDay time = GymTimeOfDay.Instance;
        if (time == null || time.IsNight || time.Daylight01 < 0.5f ||
            GymDoorway.Instance == null || !GymOutdoorBuilder.IsBuilt)
        {
            return;
        }
        if (oldest < DaysBeforeArrival || agent == null)
        {
            return;
        }
        StartVisit(GymFixerAgent.DisplayName + " drops in to fix the gym", false);
    }

    public enum SummonResult { Summoned, AlreadyHere, NothingBroken, Unavailable }

    /// <summary>
    /// Reception's call: brings him in now for whatever is broken, without the
    /// five-day wait or the daylight window.
    /// </summary>
    public SummonResult Summon()
    {
        if (visitActive)
        {
            return SummonResult.AlreadyHere;
        }
        if (!baselineCaptured)
        {
            CaptureBaselines();
        }
        Scan();
        if (currentIssues.Count == 0)
        {
            return SummonResult.NothingBroken;
        }
        if (GymDoorway.Instance == null || !GymOutdoorBuilder.IsBuilt ||
            EnsureAgentPrepared() == null)
        {
            return SummonResult.Unavailable;
        }
        return StartVisit("Reception called " + GymFixerAgent.DisplayName + ". He is on his way", true)
            ? SummonResult.Summoned
            : SummonResult.Unavailable;
    }

    private bool StartVisit(string notice, bool summoned)
    {
        // The repair list is fixed now; anything broken later waits for the
        // next visit, so endless vandalism cannot keep him here forever.
        List<FixerIssue> work = new List<FixerIssue>(currentIssues);
        if (!agent.BeginVisit(work))
        {
            return false;
        }
        visitActive = true;
        VisitCount++;
        GymHud.Active?.PushNotice(notice, 4f, 1);
        GymTimeOfDay time = GymTimeOfDay.Instance;
        Debug.Log(
            $"GYMCHAOS_FIXER_SPAWNED issues={work.Count} oldestDays={OldestIssueAgeDays:F2} " +
            $"summoned={summoned} day={time?.CurrentDay} time={time?.Time01:F3}", this);
        return true;
    }

    internal void NotifyVisitFinished(List<FixerIssue> visited)
    {
        visitActive = false;
        // Anything on this visit's list that is still broken (skipped while
        // held, out of reach...) waits a full five days again instead of
        // calling him straight back.
        float now = DayStamp;
        if (visited != null)
        {
            for (int i = 0; i < visited.Count; i++)
            {
                if (visited[i] != null && issueSince.ContainsKey(visited[i].Key))
                {
                    issueSince[visited[i].Key] = now;
                }
            }
        }
        nextScanTime = Time.time;
        Debug.Log($"GYMCHAOS_FIXER_VISIT_FINISHED fixed={FixedCount}", this);
    }

    /// <summary>Outdoor touchdown spot in front of the door, open to the sky.</summary>
    internal Vector3 FindLandingPoint()
    {
        GymDoorway doorway = GymDoorway.Instance;
        Vector3 exterior = doorway.ExteriorPoint;
        Vector3 outward = Vector3.ProjectOnPlane(exterior - doorway.InteriorPoint, Vector3.up);
        outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.forward;
        float[] angles = { 0f, 25f, -25f, 50f, -50f, 75f, -75f };
        float[] distances = { 5f, 4f, 6.5f, 3f, 8f, 10f };
        for (int d = 0; d < distances.Length; d++)
        {
            for (int a = 0; a < angles.Length; a++)
            {
                Vector3 candidate = exterior +
                    Quaternion.AngleAxis(angles[a], Vector3.up) * outward * distances[d];
                // Outside the building footprint and under open sky, so the
                // drop can never come down through a roof.
                if (GymOutdoorBuilder.IsPlayerOutsideGym(candidate) &&
                    GymFixerAgent.TryGroundAt(candidate, exterior.y, null, out float ground) &&
                    GymFixerAgent.HasOpenSky(new Vector3(candidate.x, ground + 0.2f, candidate.z)) &&
                    !Physics.CheckCapsule(
                        new Vector3(candidate.x, ground + 0.45f, candidate.z),
                        new Vector3(candidate.x, ground + 1.3f, candidate.z), 0.35f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    return new Vector3(candidate.x, ground, candidate.z);
                }
            }
        }
        return exterior + outward * 4f;
    }

    // ----------------------------------------------------------------- fix

    internal bool IsStillBroken(FixerIssue issue)
    {
        if (issue == null || !issue.Exists)
        {
            return false;
        }
        switch (issue.Kind)
        {
            case FixerIssueKind.Panel:
                return issue.Panel.IsShattered;
            case FixerIssueKind.Item:
                return baselines.TryGetValue(issue.Item, out ItemBaseline baseline) &&
                    !issue.Item.IsHeld && !baseline.restoring &&
                    IsAwayFromHome(baseline, 0.05f, 3f);
            default:
                return issue.Fighter.IsDead;
        }
    }

    internal void ApplyFix(FixerIssue issue, GymFixerAgent fixer)
    {
        if (!IsStillBroken(issue))
        {
            return;
        }
        FixedCount++;
        Debug.Log($"GYMCHAOS_FIXER_FIXED kind={issue.Kind} key={issue.Key}", this);
        GymAudio.Play(GymSoundEffect.FixerGlitter, issue.Position, 0.65f);
        switch (issue.Kind)
        {
            case FixerIssueKind.Panel:
                GymFixerMagicEffect.Burst(issue.Panel.transform.position, 0.8f, 70);
                issue.Panel.RepairByFixer();
                break;
            case FixerIssueKind.Item:
                StartCoroutine(GlideItemHome(baselines[issue.Item]));
                break;
            default:
                StartCoroutine(ReviveCorpse(issue.Fighter, fixer));
                break;
        }
        issueSince.Remove(issue.Key);
    }

    private IEnumerator GlideItemHome(ItemBaseline baseline)
    {
        PickupItem item = baseline.item;
        Rigidbody body = baseline.body;
        baseline.restoring = true;
        Transform itemTransform = item.transform;
        Vector3 start = itemTransform.position;
        Quaternion startRotation = itemTransform.rotation;
        GymFixerMagicEffect.Burst(start, 0.35f, 30);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.detectCollisions = false;
        const float Duration = 1.1f;
        float trail = 0f;
        for (float elapsed = 0f; elapsed < Duration; elapsed += Time.deltaTime)
        {
            if (item == null)
            {
                yield break;
            }
            if (item.IsHeld)
            {
                // The player grabbed it mid-flight: hand it over untouched.
                body.detectCollisions = true;
                baseline.restoring = false;
                yield break;
            }
            float t = Mathf.SmoothStep(0f, 1f, elapsed / Duration);
            Vector3 home = baseline.HomePosition;
            float lift = Mathf.Sin(t * Mathf.PI) * Mathf.Clamp(
                Vector3.Distance(start, home) * 0.25f, 0.3f, 1.4f);
            itemTransform.SetPositionAndRotation(
                Vector3.Lerp(start, home, t) + Vector3.up * lift,
                Quaternion.Slerp(startRotation, baseline.HomeRotation, t));
            trail -= Time.deltaTime;
            if (trail <= 0f)
            {
                trail = 0.08f;
                GymFixerMagicEffect.Burst(itemTransform.position, 0.12f, 6);
            }
            yield return null;
        }

        if (baseline.hadParent && baseline.parent != null)
        {
            itemTransform.SetParent(baseline.parent, false);
            itemTransform.localPosition = baseline.localPosition;
            itemTransform.localRotation = baseline.localRotation;
        }
        else
        {
            itemTransform.SetPositionAndRotation(baseline.worldPosition, baseline.worldRotation);
        }
        body.isKinematic = baseline.kinematic;
        body.useGravity = baseline.gravity;
        body.detectCollisions = baseline.detectCollisions;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }
        Physics.SyncTransforms();
        baseline.lastPosition = itemTransform.position;
        baseline.restSince = Time.time;
        baseline.restoring = false;
        GymFixerMagicEffect.Burst(itemTransform.position, 0.3f, 24);
        Debug.Log(
            $"GYMCHAOS_FIXER_ITEM_RESTORED item={item.name} " +
            $"error={Vector3.Distance(itemTransform.position, baseline.HomePosition):F3}", this);
    }

    private IEnumerator ReviveCorpse(EnemyFighter fighter, GymFixerAgent fixer)
    {
        Vector3 corpse = fighter.VisitorPhysicsPosition;
        reviving.Add(fighter);
        GymFixerMagicEffect.Burst(corpse + Vector3.up * 0.4f, 0.7f, 60);
        List<Renderer> hidden = new List<Renderer>();
        Renderer[] renderers = fighter.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].enabled)
            {
                renderers[i].enabled = false;
                hidden.Add(renderers[i]);
            }
        }
        yield return new WaitForSeconds(0.45f);
        reviving.Remove(fighter);
        if (fighter == null)
        {
            yield break;
        }

        GymVisitorDirector visitors = GymVisitorDirector.Instance;
        bool visitor = visitors != null && visitors.IsScheduledVisitor(fighter);
        Vector3 stand = corpse;
        if (visitor && GymOutdoorBuilder.IsPlayerOutsideGym(corpse) && GymDoorway.Instance != null)
        {
            // Members resume their visit inside, just past the door.
            GymDoorway doorway = GymDoorway.Instance;
            Vector3 inward = Vector3.ProjectOnPlane(
                doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up).normalized;
            stand = doorway.InteriorPoint + inward * 1.6f;
        }
        if (fixer != null && fixer.NavGrid != null &&
            fixer.NavGrid.TryNearestWalkable(stand, 1.5f, out Vector3 open))
        {
            stand = open;
        }
        if (GymFixerAgent.TryGroundAt(stand, stand.y, fighter.transform, out float ground))
        {
            stand.y = ground;
        }
        Vector3 look = fixer != null
            ? Vector3.ProjectOnPlane(fixer.transform.position - stand, Vector3.up)
            : Vector3.zero;
        Quaternion facing = look.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(look.normalized, Vector3.up)
            : fighter.transform.rotation;
        fighter.ReviveByFixer(stand, facing);
        for (int i = 0; i < hidden.Count; i++)
        {
            if (hidden[i] != null)
            {
                hidden[i].enabled = true;
            }
        }
        GymFixerMagicEffect.Burst(stand + Vector3.up * 1.1f, 0.7f, 60);
        if (visitor)
        {
            visitors.NotifyFighterRevived(fighter);
        }
        if (fighter.Identity == BodybuilderIdentity.Policeman && fighter.IsPolice)
        {
            GymPoliceDirector.BeginOfficerReturn(fighter);
        }
    }

    // ------------------------------------------------------------- rainbow

    private void UpdateRainbow()
    {
        float target = visitActive ? 1f : 0f;
        float rate = Time.deltaTime / (target > rainbowFade
            ? RainbowFadeInSeconds : RainbowFadeOutSeconds);
        rainbowFade = Mathf.MoveTowards(rainbowFade, target, rate);
        Shader.SetGlobalFloat(RainbowFadeId, rainbowFade);
        if (rainbowFade <= 0f)
        {
            return;
        }

        // Rainbows stand opposite the sun. Keep the antisolar point a little
        // below the horizon so the arc always reads in the visible sky.
        GymTimeOfDay time = GymTimeOfDay.Instance;
        Vector3 axis = time != null ? -time.SunDirection : new Vector3(0.6f, -0.3f, 0.74f);
        Vector3 horizontal = Vector3.ProjectOnPlane(axis, Vector3.up);
        if (horizontal.sqrMagnitude < 0.0001f)
        {
            horizontal = Vector3.forward;
        }
        horizontal.Normalize();
        float elevation = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(axis.y, -1f, 1f)) * Mathf.Rad2Deg,
            -14f, -6f) * Mathf.Deg2Rad;
        Vector3 antisolar = horizontal * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation);
        Shader.SetGlobalVector(RainbowAxisId, antisolar);
    }

    // ---------------------------------------------------------------- save

    /// <summary>Ages of unrepaired panels (the only issues that survive a reload).</summary>
    public static void CaptureIssueAges(out string[] ids, out float[] since)
    {
        List<string> keys = new List<string>();
        List<float> values = new List<float>();
        if (Instance != null)
        {
            foreach (KeyValuePair<string, float> pair in Instance.issueSince)
            {
                if (pair.Key.StartsWith("panel:", System.StringComparison.Ordinal))
                {
                    keys.Add(pair.Key);
                    values.Add(pair.Value);
                }
            }
        }
        foreach (KeyValuePair<string, float> pair in restoredAges)
        {
            if (!keys.Contains(pair.Key))
            {
                keys.Add(pair.Key);
                values.Add(pair.Value);
            }
        }
        ids = keys.ToArray();
        since = values.ToArray();
    }

    public static void RestoreIssueAges(string[] ids, float[] since)
    {
        restoredAges.Clear();
        if (Instance != null)
        {
            Instance.issueSince.Clear();
        }
        if (ids == null || since == null)
        {
            return;
        }
        int count = Mathf.Min(ids.Length, since.Length);
        for (int i = 0; i < count; i++)
        {
            if (!string.IsNullOrEmpty(ids[i]))
            {
                restoredAges[ids[i]] = since[i];
            }
        }
    }

    public float GetIssueSinceForVerification(string key)
    {
        return issueSince.TryGetValue(key, out float since) ? since : float.NaN;
    }
}
