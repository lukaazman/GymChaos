using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Exercises the authored Davie bus through the same runtime vehicle path used
/// by the visitor director: road arrival, local pull-off parking and a clear
/// U-turn back onto the normal road lane.
/// </summary>
public static class GymChaosDavieBusVerifier
{
    private const string RequestedKey =
        "GymChaos.DavieBusLifecycleVerificationRequested";
    private static double started;
    private static double parkedAt;
    private static int phase;
    private static GymVisitorVehicle bus;
    private static bool arrivalDone;
    private static bool departureDone;
    private static bool departureStarted;
    private static bool parkedInLocalBay;
    private static bool passengerOutsideBus;
    private static bool sawTurnaround;
    private static bool sawReturnLane;
    // After the U-turn the bus must drive east on the south (right-hand)
    // lane, turn north on the outbound lane and vanish only at the road's
    // vehicle spawn point.
    private static bool sawEastboundRightLane;
    private static bool sawNorthboundOutboundLane;
    private static int wrongLaneSamples;
    private static string wrongLaneContext;
    private static Vector3 lastActivePosition;
    // Largest angle between the bus's planar motion and its heading. A car
    // body drives along its nose; sideways translation reads as a slip.
    private const float MaxAllowedSlipDegrees = 12f;
    private static float maxSlipDegrees;
    private static string maxSlipContext;
    private static Vector3 lastSamplePosition;
    private static bool hasSamplePosition;
    private static string wallContacts;
    private static readonly Collider[] overlapBuffer = new Collider[32];

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        ResetState();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Debug.Log("GYMCHAOS_DAVIE_BUS_LIFECYCLE_STARTED");
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        started = GymChaosVerifierClock.Now;
        parkedAt = 0d;
        phase = 0;
        bus = null;
        arrivalDone = false;
        departureDone = false;
        departureStarted = false;
        parkedInLocalBay = false;
        passengerOutsideBus = false;
        sawTurnaround = false;
        sawReturnLane = false;
        sawEastboundRightLane = false;
        sawNorthboundOutboundLane = false;
        wrongLaneSamples = 0;
        wrongLaneContext = "none";
        lastActivePosition = Vector3.zero;
        maxSlipDegrees = 0f;
        maxSlipContext = "none";
        hasSamplePosition = false;
        wallContacts = string.Empty;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            ResetState();
            Time.timeScale = 3f;
            AudioListener.pause = true;
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        GymChaosVerifierClock.EndFixedStep();
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
        SessionState.EraseBool(RequestedKey);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.pause = true;
        try
        {
            GymVisitorDirector director =
                UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director != null)
            {
                director.enabled = false;
            }

            if (!GymOutdoorBuilder.IsBuilt || !GymRoadsideBusStop.IsBuilt ||
                !GymRoadsideBusStop.IsDavieBusReady)
            {
                CheckTimeout();
                return;
            }

            SampleSlip();
            switch (phase)
            {
                case 0:
                    BeginBusLifecycle();
                    break;
                case 1:
                    TickArrival();
                    break;
                case 2:
                    TickDeparture();
                    break;
            }
            CheckTimeout();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            CleanupBus();
            Finish(1);
        }
    }

    private static void BeginBusLifecycle()
    {
        DisableSceneActors();
        bus = GymVisitorVehicle.Create(
            BodybuilderIdentity.Davie, 0, null, false);
        if (bus == null)
        {
            throw new InvalidOperationException("Davie bus could not be created.");
        }

        bus.DriveIn(() => arrivalDone = true);
        phase = 1;
        Debug.Log(
            $"GYMCHAOS_DAVIE_BUS_ARRIVAL_STARTED road={bus.RoadPointForVerification} " +
            $"bay={GymRoadsideBusStop.DavieBusCenterPoint}", bus);
    }

    private static void TickArrival()
    {
        if (!arrivalDone) return;
        parkedInLocalBay = IsParkedInsideLocalBay();
        passengerOutsideBus = bus.PassengerPoint.z >
            bus.transform.position.z + 1.5f;
        AssertNoSlip("arrival");
        if (!parkedInLocalBay || !passengerOutsideBus ||
            !bus.RuntimeVisualReadyForVerification || !bus.IsParked)
        {
            throw new InvalidOperationException(
                $"Davie bus arrival contract failed parked={bus.IsParked} " +
                $"localBay={parkedInLocalBay} passengerOutside={passengerOutsideBus} " +
                $"runtimeGlb={bus.RuntimeVisualReadyForVerification} " +
                $"position={bus.transform.position}.");
        }

        parkedAt = GymChaosVerifierClock.Now;
        phase = 2;
        Debug.Log(
            $"GYMCHAOS_DAVIE_BUS_ARRIVAL_OK parkedInLocalBay={parkedInLocalBay} " +
            $"passengerOutsideBus={passengerOutsideBus} " +
            $"runtimeGlb={bus.RuntimeVisualReadyForVerification} " +
            $"passengerPoint={bus.PassengerPoint}", bus);
    }

    private static void TickDeparture()
    {
        if (!departureStarted && !departureDone &&
            GymChaosVerifierClock.Now - parkedAt > 1.25d)
        {
            departureStarted = true;
            bus.DriveOut(() => departureDone = true);
            Debug.Log("GYMCHAOS_DAVIE_BUS_DEPARTURE_STARTED", bus);
        }

        if (bus != null && bus.IsDriving && bus.gameObject.activeInHierarchy)
        {
            Vector3 position = bus.transform.position;
            Vector3 turnaround = GymRoadsideBusStop.DavieBusTurnaroundCenterPoint;
            Vector3 returnLane = GymRoadsideBusStop.DavieBusReturnLanePoint;
            sawTurnaround |= position.x < turnaround.x - 1.0f &&
                position.z < GymRoadsideBusStop.DavieBusTurnaroundStartPoint.z - 0.35f;
            sawReturnLane |= Mathf.Abs(position.z - returnLane.z) < 0.8f;
            SampleLane(position);
            lastActivePosition = position;
        }

        if (!departureDone) return;
        bool departed = bus.HasCompletedDeparture &&
            !bus.gameObject.activeInHierarchy;
        if (!departed)
        {
            throw new InvalidOperationException(
                $"Davie bus departure contract failed departed={departed} " +
                $"turnaround={sawTurnaround} returnLane={sawReturnLane} " +
                $"clearancePath={sawTurnaround && sawReturnLane} " +
                $"position={bus.transform.position}.");
        }

        AssertNoSlip("departure");
        Vector3 spawn = GymOutdoorBuilder.VehicleDepartureRoadSpawnPoint;
        float despawnDistance = Vector3.ProjectOnPlane(lastActivePosition - spawn, Vector3.up).magnitude;
        if (wrongLaneSamples > 0 || !sawEastboundRightLane || !sawNorthboundOutboundLane ||
            despawnDistance > 3f)
        {
            throw new InvalidOperationException(
                $"GYMCHAOS_DAVIE_BUS_LANE_FAIL wrongLane={wrongLaneSamples} ({wrongLaneContext}) " +
                $"eastboundRight={sawEastboundRightLane} northboundOutbound={sawNorthboundOutboundLane} " +
                $"despawnAt={lastActivePosition} spawn={spawn} distance={despawnDistance:F2}");
        }
        Debug.Log(
            $"GYMCHAOS_DAVIE_BUS_EXIT_LANE_OK eastboundRightLane=True northboundOutbound=True " +
            $"despawnDistance={despawnDistance:F2}");
        Debug.Log(
            "GYMCHAOS_DAVIE_BUS_LIFECYCLE_OK " +
            "arrived=True parkedInLocalBay=True actualGlb=True " +
            "passengerOutsideBus=True departed=True clearGatedUTurn=True " +
            "returnedToNormalLane=True");
        CleanupBus();
        Finish(0);
    }

    private static void SampleLane(Vector3 position)
    {
        if (!sawTurnaround) return;
        Vector3 heading = Vector3.ProjectOnPlane(bus.transform.forward, Vector3.up).normalized;
        Vector3 turn = GymOutdoorBuilder.VehicleRoadTurnPoint;
        // Main road runs along x (centre line z = turn.z); the side road runs
        // north along x = turn.x.
        if (heading.x > 0.9f && position.x < turn.x - 6f)
        {
            if (position.z > turn.z - 0.3f)
            {
                wrongLaneSamples++;
                wrongLaneContext = $"eastbound z={position.z:F2} centre={turn.z:F2}";
            }
            else sawEastboundRightLane = true;
        }
        if (heading.z > 0.9f && position.z > turn.z + 9f)
        {
            if (position.x < turn.x + 0.3f)
            {
                wrongLaneSamples++;
                wrongLaneContext = $"northbound x={position.x:F2} centre={turn.x:F2}";
            }
            else sawNorthboundOutboundLane = true;
        }
    }

    private static void SampleSlip()
    {
        if (bus == null || !bus.IsDriving || !bus.gameObject.activeInHierarchy)
        {
            hasSamplePosition = false;
            return;
        }
        Vector3 position = bus.transform.position;
        if (!hasSamplePosition)
        {
            lastSamplePosition = position;
            hasSamplePosition = true;
            return;
        }
        Vector3 motion = Vector3.ProjectOnPlane(position - lastSamplePosition, Vector3.up);
        if (motion.sqrMagnitude < 0.03f * 0.03f)
        {
            return;
        }
        lastSamplePosition = position;
        SampleWallContacts();
        Vector3 heading = Vector3.ProjectOnPlane(bus.transform.forward, Vector3.up);
        float slip = Vector3.Angle(motion, heading);
        if (slip > maxSlipDegrees)
        {
            maxSlipDegrees = slip;
            maxSlipContext = $"phase={phase} position={position} " +
                $"motion={motion.normalized} heading={heading.normalized}";
        }
    }

    // The driven body must not sweep through walls or fences.
    private static void SampleWallContacts()
    {
        BoxCollider body = bus.GetComponent<BoxCollider>();
        if (body == null) return;
        Transform t = body.transform;
        Vector3 center = t.TransformPoint(body.center);
        Vector3 half = Vector3.Scale(body.size * 0.5f, t.lossyScale) * 0.95f;
        int count = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, t.rotation,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = overlapBuffer[i];
            if (hit == null || hit.transform.IsChildOf(bus.transform)) continue;
            string name = hit.name.ToLowerInvariant();
            if ((name.Contains("wall") || name.Contains("fence")) &&
                !wallContacts.Contains(hit.name))
            {
                wallContacts += $"{hit.name}@{bus.transform.position};";
            }
        }
    }

    private static void AssertNoSlip(string leg)
    {
        if (!string.IsNullOrEmpty(wallContacts))
        {
            throw new InvalidOperationException(
                $"GYMCHAOS_DAVIE_BUS_WALL_CONTACT_FAIL leg={leg} contacts={wallContacts}");
        }
        if (maxSlipDegrees > MaxAllowedSlipDegrees)
        {
            throw new InvalidOperationException(
                $"GYMCHAOS_DAVIE_BUS_SLIP_FAIL leg={leg} maxSlip={maxSlipDegrees:F1} " +
                $"limit={MaxAllowedSlipDegrees:F0} {maxSlipContext}");
        }
        Debug.Log($"GYMCHAOS_DAVIE_BUS_SLIP_OK leg={leg} maxSlip={maxSlipDegrees:F1} wallContacts=0");
    }

    private static void Finish(int code)
    {
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(code);
            return;
        }
        // Never close a live Editor; just leave play mode.
        EditorApplication.isPlaying = false;
    }

    private static bool IsParkedInsideLocalBay()
    {
        if (bus == null) return false;
        Vector3 position = bus.transform.position;
        Vector3 expected = GymRoadsideBusStop.DavieBusCenterPoint;
        return Vector3.ProjectOnPlane(position - expected, Vector3.up).sqrMagnitude <
            0.16f && position.z > GymRoadsideBusStop.BusBayRoadEdgeZ + 0.15f &&
            position.z < GymRoadsideBusStop.BusBayOuterZ - 0.15f &&
            position.x > GymRoadsideBusStop.BusBayStartX + 0.15f &&
            position.x < GymRoadsideBusStop.BusBayEndX - 0.15f;
    }

    private static void CheckTimeout()
    {
        if (GymChaosVerifierClock.Now - started <= 90d) return;
        throw new InvalidOperationException(
            $"Davie bus lifecycle timed out phase={phase} arrival={arrivalDone} " +
            $"departure={departureDone} parked={bus?.IsParked} " +
            $"driving={bus?.IsDriving} speed={bus?.CurrentDriveSpeedForVerification:F2} " +
            $"blocker={bus?.LastTrafficBlockerForVerification} " +
            $"position={bus?.transform.position}.");
    }

    private static void DisableSceneActors()
    {
        GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<
            GymVisitorVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < vehicles.Length; i++)
        {
            if (vehicles[i] != null) vehicles[i].gameObject.SetActive(false);
        }

        GymVisitorAgent[] agents = UnityEngine.Object.FindObjectsByType<GymVisitorAgent>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null) agents[i].gameObject.SetActive(false);
        }
    }

    private static void CleanupBus()
    {
        if (bus != null)
        {
            UnityEngine.Object.Destroy(bus.gameObject);
            bus = null;
        }
    }
}
