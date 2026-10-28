#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Automatic doors (2026-10-17): the gym entrance (double-acting glass
/// leaves) and the protein.com storefront (sliding glass leaves).
/// - Contract: both doors exist with two leaves, no leaf collider, the GLB's
///   parked store leaves are hidden, the old black inner panel is gone.
/// - Sensing: a person standing far away, walking past or walking slowly a
///   few metres out does not open a door; a person at the door, or one
///   sprinting at it, does.
/// - Scripted walks: the player walks and sprints through both doors in both
///   directions. Each door is fully open before the player reaches the door
///   plane, did not start opening while the player was still far away, and
///   is shut again shortly after the player has left.
/// - Natural traffic: for a while every person crossing a door plane finds
///   it open (nobody walks through closed glass).
/// With a graphics device it writes evidence PNGs to Logs/agent/doors.
/// </summary>
[InitializeOnLoad]
public static class GymChaosAutoDoorVerifier
{
    private const string RequestedKey = "GymChaos.AutoDoorVerificationRequested";
    private const float PlayerRootHeight = 1.05f;
    private const float MaxOpeningStartDistance = GymAutoDoor.PredictMaxDistance + 0.6f;
    private const double TrafficSeconds = 70d;

    private enum Stage
    {
        WaitReady,
        Walk,
        Traffic,
        Done
    }

    private sealed class Walk
    {
        public string Name;
        public GymAutoDoor Door;
        public float FromAlong;
        public float ToAlong;
        public float Speed;
        public float Lateral;
        public float Hold;
    }

    private static Stage stage;
    private static double startedAt;
    private static double stageStarted;
    private static bool finished;
    private static int resultCode = 1;
    private static float previousMaximumDeltaTime;
    private static readonly List<string> notes = new List<string>();
    private static readonly HashSet<string> captured = new HashSet<string>();
    private static readonly List<Walk> walks = new List<Walk>();
    private static int walkIndex;
    private static int walkPhase;
    private static float walkAlong;
    private static double phaseStarted;
    private static float openingStartAlong;
    private static float crossBlend;
    private static bool crossed;
    private static PlayerMovement player;
    private static int trafficCrossings;
    private static readonly Dictionary<Transform, float>[] lastSides =
        { new Dictionary<Transform, float>(), new Dictionary<Transform, float>() };

    static GymChaosAutoDoorVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Automatic Door Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeStateChanged);

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            startedAt = GymChaosVerifierClock.Now;
            stage = Stage.WaitReady;
            stageStarted = startedAt;
            notes.Clear();
            captured.Clear();
            walks.Clear();
            walkIndex = 0;
            walkPhase = 0;
            trafficCrossings = 0;
            lastSides[0].Clear();
            lastSides[1].Clear();
            previousMaximumDeltaTime = Time.maximumDeltaTime;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = GymChaosVerifierClock.Now;
        try
        {
            if (now - startedAt > 400d) throw new TimeoutException($"stage {stage} did not finish");
            GymAutoDoor gym = GymAutoDoor.Find(GymDoorway.AutoDoorName);
            GymAutoDoor store = GymAutoDoor.Find(GymProteinStoreEnvironment.AutoDoorName);
            switch (stage)
            {
                case Stage.WaitReady:
                    GymTimeOfDay.Instance?.SetTimeForVerification(0.5f);
                    if (gym == null || store == null || !GymProteinStoreEnvironment.IsLoaded) return;
                    if (now - stageStarted < 2d) return;
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                    Require(player != null, "no player");
                    Time.maximumDeltaTime = 0.05f;
                    CheckContract(gym, store);
                    CheckSensing(gym);
                    CheckSensing(store);
                    BuildWalks(gym, store);
                    player.enabled = false;
                    stage = Stage.Walk;
                    stageStarted = now;
                    BeginWalk(now);
                    break;
                case Stage.Walk:
                    TickWalk(now);
                    break;
                case Stage.Traffic:
                    player.transform.position = gym.Center - gym.Normal * 14f + Vector3.up * (PlayerRootHeight - 1f);
                    TickTraffic(gym, 0);
                    TickTraffic(store, 1);
                    if (now - stageStarted < TrafficSeconds) return;
                    Finish(gym, store, now);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_AUTO_DOOR_FAIL " + exception.Message + " " + string.Join(" ", notes));
            End(1);
        }
    }

    private static void CheckContract(GymAutoDoor gym, GymAutoDoor store)
    {
        Require(gym.Kind == GymAutoDoor.DoorKind.Swing && gym.LeafCount == 2,
            $"gym door kind={gym.Kind} leaves={gym.LeafCount}");
        Require(store.Kind == GymAutoDoor.DoorKind.Slide && store.LeafCount == 2,
            $"store door kind={store.Kind} leaves={store.LeafCount}");
        Require(gym.GetComponentsInChildren<Collider>(true).Length == 0, "gym door parts carry colliders");
        Require(store.GetComponentsInChildren<Collider>(true).Length == 0, "store door parts carry colliders");
        Require(GameObject.Find("Visitor Door Panel") == null, "old black visitor door panel still exists");
        Require(GymDoorway.Instance != null && GymDoorway.Instance.AutoDoor == gym, "doorway is not bound to the gym door");
        int hiddenLeaves = 0;
        int visibleLeaves = 0;
        Transform storeRoot = GymProteinStoreEnvironment.RuntimeRoot;
        foreach (MeshRenderer renderer in storeRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            bool leaf = renderer.name.StartsWith("Door_Leaves", StringComparison.Ordinal) ||
                (renderer.transform.parent != null &&
                 renderer.transform.parent.name.StartsWith("Door_Leaves", StringComparison.Ordinal));
            if (!leaf) continue;
            if (renderer.enabled) visibleLeaves++; else hiddenLeaves++;
        }
        Require(hiddenLeaves > 0 && visibleLeaves == 0,
            $"GLB parked store leaves hidden={hiddenLeaves} visible={visibleLeaves}");
        notes.Add($"glbLeavesHidden={hiddenLeaves}");
    }

    private static void CheckSensing(GymAutoDoor door)
    {
        Vector3 n = door.Normal;
        Vector3 t = Vector3.Cross(Vector3.up, n);
        Vector3 c = door.Center;
        // Standing still far outside and inside.
        Require(!door.ShouldOpenFor(c + n * 6f, Vector3.zero), $"{door.DoorName}: opens for someone standing 6 m outside");
        Require(!door.ShouldOpenFor(c - n * 6f, Vector3.zero), $"{door.DoorName}: opens for someone standing 6 m inside");
        // Walking slowly toward it, still 4 m out.
        Require(!door.ShouldOpenFor(c + n * 4f, -n * 1.4f), $"{door.DoorName}: opens for a slow walker 4 m out");
        // Walking past along the facade 3 m out.
        Require(!door.ShouldOpenFor(c + n * 3f + t * 2f, t * 4f), $"{door.DoorName}: opens for someone walking past");
        // Beside the opening along the wall, not in front of it.
        Require(!door.ShouldOpenFor(c + n * 0.6f + t * (door.HalfWidth + 2f), Vector3.zero),
            $"{door.DoorName}: opens for someone beside the opening");
        // At the door, from both sides.
        Require(door.ShouldOpenFor(c + n * 1.5f, Vector3.zero), $"{door.DoorName}: shut for someone at the door outside");
        Require(door.ShouldOpenFor(c - n * 1.5f, Vector3.zero), $"{door.DoorName}: shut for someone at the door inside");
        // Sprinting at it from 8 m.
        Require(door.ShouldOpenFor(c + n * 8f, -n * 11f), $"{door.DoorName}: shut for a sprinter 8 m out");
        Require(door.ShouldOpenFor(c - n * 8f, n * 11f), $"{door.DoorName}: shut for a sprinter 8 m in");
    }

    private static void BuildWalks(GymAutoDoor gym, GymAutoDoor store)
    {
        // Positive along = outside. Hold = seconds to wait at the end.
        walks.Add(new Walk { Name = "gymWalkIn", Door = gym, FromAlong = 10f, ToAlong = -6f, Speed = 4.5f, Hold = 3f });
        walks.Add(new Walk { Name = "gymSprintOut", Door = gym, FromAlong = -10f, ToAlong = 8f, Speed = 11f, Hold = 3f });
        walks.Add(new Walk { Name = "gymWalkInOffCentre", Door = gym, FromAlong = 8f, ToAlong = -6f, Speed = 1.6f, Lateral = 0.9f, Hold = 3f });
        walks.Add(new Walk { Name = "storeWalkIn", Door = store, FromAlong = 6.5f, ToAlong = -5f, Speed = 4.5f, Hold = 3f });
        walks.Add(new Walk { Name = "storeSprintOut", Door = store, FromAlong = -9f, ToAlong = 6.5f, Speed = 11f, Hold = 3f });
        walks.Add(new Walk { Name = "storeStrollIn", Door = store, FromAlong = 6f, ToAlong = -5f, Speed = 1.4f, Lateral = -0.6f, Hold = 3f });
    }

    private static Vector3 WalkPoint(Walk walk, float along)
    {
        GymAutoDoor door = walk.Door;
        Vector3 t = Vector3.Cross(Vector3.up, door.Normal);
        return door.Center + door.Normal * along + t * walk.Lateral +
            Vector3.up * (PlayerRootHeight - 1f);
    }

    private static void BeginWalk(double now)
    {
        Walk walk = walks[walkIndex];
        walkPhase = 0;
        walkAlong = walk.FromAlong;
        phaseStarted = now;
        openingStartAlong = float.NaN;
        crossed = false;
        crossBlend = -1f;
        player.transform.position = WalkPoint(walk, walkAlong);
        Physics.SyncTransforms();
    }

    private static void TickWalk(double now)
    {
        Walk walk = walks[walkIndex];
        GymAutoDoor door = walk.Door;
        switch (walkPhase)
        {
            case 0:
                // Start far away; the door must settle shut (unless someone
                // else is at it), then the walk starts.
                player.transform.position = WalkPoint(walk, walkAlong);
                if (now - phaseStarted < 2.5d) return;
                if (door.IsSensingSomeone || !door.IsClosed)
                {
                    Require(now - phaseStarted < 40d, $"{walk.Name}: door never settled shut: {door.DescribeSensed()}");
                    return;
                }
                Capture(walk.Name + "_0_closed", door, walk.FromAlong);
                walkPhase = 1;
                phaseStarted = now;
                return;
            case 1:
                float previous = walkAlong;
                walkAlong = Mathf.MoveTowards(walkAlong, walk.ToAlong, walk.Speed * Time.deltaTime);
                player.transform.position = WalkPoint(walk, walkAlong);
                if (float.IsNaN(openingStartAlong) && door.OpenBlend > 0.001f)
                {
                    openingStartAlong = Mathf.Abs(previous);
                }
                if (!crossed && Mathf.Sign(previous) != Mathf.Sign(walkAlong))
                {
                    crossed = true;
                    crossBlend = door.OpenBlend;
                    Capture(walk.Name + "_1_crossing", door, walk.FromAlong);
                }
                if (Mathf.Abs(walkAlong - walk.ToAlong) > 0.001f) return;
                Require(crossed && crossBlend >= 0.98f,
                    $"{walk.Name}: door blend {crossBlend:F2} when the player reached the door plane");
                Require(!float.IsNaN(openingStartAlong) && openingStartAlong <= MaxOpeningStartDistance,
                    $"{walk.Name}: door started opening {openingStartAlong:F1} m away");
                notes.Add($"{walk.Name}=openAt{openingStartAlong:F1}m,cross{crossBlend:F2}");
                walkPhase = 2;
                phaseStarted = now;
                return;
            case 2:
                // Wait at the end point: once nobody is near, it shuts.
                player.transform.position = WalkPoint(walk, walk.ToAlong);
                if (!door.IsClosed)
                {
                    Require(now - phaseStarted < 40d, $"{walk.Name}: door stayed open after the player left: {door.DescribeSensed()}");
                    return;
                }
                notes.Add($"{walk.Name}Shut={now - phaseStarted:F1}s");
                walkIndex++;
                if (walkIndex >= walks.Count)
                {
                    player.enabled = true;
                    stage = Stage.Traffic;
                    stageStarted = now;
                    Time.timeScale = GymChaosVerifierClock.ScenarioTimeScale;
                    return;
                }
                BeginWalk(now);
                return;
        }
    }

    /// <summary>Every person crossing a door plane inside the opening finds it open.</summary>
    private static void TickTraffic(GymAutoDoor door, int index)
    {
        Dictionary<Transform, float> sides = lastSides[index];
        Vector3 t = Vector3.Cross(Vector3.up, door.Normal);
        void Check(Transform person)
        {
            if (person == null || !person.gameObject.activeInHierarchy) return;
            Vector3 offset = person.position - door.Center;
            if (Mathf.Abs(offset.y) > 2.6f) return;
            float along = Vector3.Dot(offset, door.Normal);
            float lateral = Vector3.Dot(offset, t);
            if (Mathf.Abs(lateral) > door.HalfWidth + 0.3f || Mathf.Abs(along) > 1.5f)
            {
                sides.Remove(person);
                return;
            }
            float side = Mathf.Sign(along);
            if (sides.TryGetValue(person, out float last) && last != side)
            {
                trafficCrossings++;
                Require(door.OpenBlend >= 0.9f,
                    $"{person.name} crossed {door.DoorName} at blend {door.OpenBlend:F2}");
            }
            sides[person] = side;
        }
        foreach (GymVisitorAgent visitor in GymVisitorAgent.ActiveAgents) Check(visitor != null ? visitor.transform : null);
        foreach (EnemyFighter fighter in EnemyFighter.RegisteredFighters)
        {
            if (fighter != null && !fighter.IsDead) Check(fighter.transform);
        }
    }

    private static void Finish(GymAutoDoor gym, GymAutoDoor store, double now)
    {
        notes.Add($"trafficCrossings={trafficCrossings}");
        Debug.Log("GYMCHAOS_AUTO_DOOR_OK " + string.Join(" ", notes) + $" seconds={now - startedAt:F0}");
        End(0);
    }

    private static void End(int code)
    {
        finished = true;
        resultCode = code;
        if (player != null) player.enabled = true;
        Time.timeScale = 1f;
        Time.maximumDeltaTime = previousMaximumDeltaTime > 0f ? previousMaximumDeltaTime : 0.3333f;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Capture(string name, GymAutoDoor door, float side)
    {
        if (captured.Contains(name) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        captured.Add(name);
        Vector3 t = Vector3.Cross(Vector3.up, door.Normal);
        float s = Mathf.Sign(side);
        Vector3 eye = door.Center + door.Normal * (s * 5.5f) + t * 1.6f + Vector3.up * 0.7f;
        Vector3 target = door.Center + Vector3.up * 0.3f;
        GameObject cameraObject = new GameObject("Door Evidence Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = eye;
        camera.transform.LookAt(target);
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 300f;
        RenderTexture rt = new RenderTexture(960, 640, 24);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(960, 640, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "doors");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        camera.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(cameraObject);
    }
}
#endif
