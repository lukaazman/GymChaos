using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosDaviePedestrianVerifier
{
    private const string RequestedKey = "GymChaos.DaviePedestrianVerificationRequested";
    private const double TotalTimeoutSeconds = 180d;
    private static double startedAt;
    private static double phaseStartedAt;
    private static double nextSampleAt;
    private static bool launched;
    private static bool busArrivedForDropoff;
    private static bool entryConfirmed;
    private static bool busDepartedAfterEntry;
    private static bool exitRequested;
    private static bool passengerExited;
    private static bool busReturnedForPickup;
    private static bool passengerBoarded;
    private static bool finished;
    private static EnemyFighter davie;
    private static GymVisitorAgent agent;
    private static GymVisitorVehicle bus;
    private static GymVisitorDirector director;

    static GymChaosDaviePedestrianVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    public static void Run()
    {
        ResetState();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        startedAt = 0d;
        phaseStartedAt = 0d;
        nextSampleAt = 0d;
        launched = false;
        busArrivedForDropoff = false;
        entryConfirmed = false;
        busDepartedAfterEntry = false;
        exitRequested = false;
        passengerExited = false;
        busReturnedForPickup = false;
        passengerBoarded = false;
        finished = false;
        davie = null;
        agent = null;
        bus = null;
        director = null;
    }

    private static void ResumeAfterDomainReload()
    {
        if (EditorApplication.isPlaying) StartVerifier();
    }

    private static void StartVerifier()
    {
        startedAt = EditorApplication.timeSinceStartup;
        phaseStartedAt = startedAt;
        Time.timeScale = 1f;
        AudioListener.pause = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            StartVerifier();
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;

        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        bool wasRequested = SessionState.GetBool(RequestedKey, false);
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode && wasRequested)
            EditorApplication.Exit(finished ? 0 : 1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        Time.timeScale = 1f;
        AudioListener.pause = true;

        double now = EditorApplication.timeSinceStartup;
        if (now - startedAt > TotalTimeoutSeconds)
        {
            Fail("total_timeout");
            return;
        }

        if (director == null)
            director = Object.FindAnyObjectByType<GymVisitorDirector>();
        if (director == null || !GymOutdoorBuilder.IsBuilt ||
            !GymRoadsideBusStop.IsBuilt)
        {
            return;
        }
        if (!GymRoadsideBusStop.HasClearPedestrianGateForVerification())
        {
            Fail("bus_stop_pedestrian_gate_blocked_by_fence");
            return;
        }

        if (!launched)
        {
            if (now - startedAt < 2.5d) return;
            if (!director.BeginDavieBusLifecycleForVerification(
                out davie, out bus))
            {
                Fail("director_could_not_start_davie_shuttle");
                return;
            }
            agent = davie.GetComponent<GymVisitorAgent>();
            if (agent == null || !bus.IsBus)
            {
                Fail("davie_agent_or_bus_missing");
                return;
            }
            launched = true;
            phaseStartedAt = now;
            Debug.Log("GYMCHAOS_DAVIE_PASSENGER_LIFECYCLE_STARTED");
        }

        if (davie == null || agent == null || bus == null)
        {
            Fail("runtime_actor_lost");
            return;
        }

        if (now >= nextSampleAt)
        {
            nextSampleAt = now + 1.25d;
            Debug.Log(
                $"GYMCHAOS_DAVIE_LIFECYCLE_SAMPLE state={agent.State} " +
                $"pos={davie.VisitorPhysicsPosition} entered={agent.HasEnteredGym} " +
                $"target={agent.TravelTargetForVerification} waypoint={agent.VehicleExitWaypointForVerification} " +
                $"left={agent.HasLeftGym} busParked={bus.IsParked} " +
                $"busDriving={bus.IsDriving} active={bus.gameObject.activeInHierarchy} " +
                $"boarded={director.DaviePassengerBoardedForVerification} " +
                $"blocker={davie.LastVisitorRouteBlocker}");
        }

        if (!busArrivedForDropoff)
        {
            if (bus.IsParked && !bus.IsDriving)
            {
                busArrivedForDropoff = true;
                phaseStartedAt = now;
                Debug.Log(
                    $"GYMCHAOS_DAVIE_BUS_DROP_OFF_ARRIVED passenger={bus.PassengerPoint}");
            }
            else if (now - phaseStartedAt > 65d)
            {
                Fail("bus_arrival_timeout");
                return;
            }
        }
        else if (!entryConfirmed)
        {
            if (agent.HasEnteredGym)
            {
                entryConfirmed = true;
                phaseStartedAt = now;
                Debug.Log(
                    $"GYMCHAOS_DAVIE_BUS_ENTRY_CONFIRMED position={davie.VisitorPhysicsPosition}");
            }
            else if (!bus.IsParked || bus.IsDriving ||
                !bus.gameObject.activeInHierarchy)
            {
                Fail("bus_left_before_davie_entered_gym");
                return;
            }
            else if (now - phaseStartedAt > 75d)
            {
                Fail("passenger_entry_timeout");
                return;
            }
        }
        else if (!busDepartedAfterEntry)
        {
            if (bus.HasCompletedDeparture && !bus.gameObject.activeInHierarchy)
            {
                busDepartedAfterEntry = true;
                phaseStartedAt = now;
                Debug.Log("GYMCHAOS_DAVIE_BUS_DEPARTED_AFTER_ENTRY");
            }
            else if (now - phaseStartedAt > 65d)
            {
                Fail("bus_did_not_depart_after_entry");
                return;
            }
        }
        else if (!exitRequested)
        {
            if (!agent.IsInsideGym || !davie.gameObject.activeInHierarchy)
            {
                Fail("davie_not_inside_while_bus_away");
                return;
            }
            if (now - phaseStartedAt >= 1.25d)
            {
                if (!director.RequestDavieDepartureForVerification())
                {
                    Fail("director_rejected_davie_departure");
                    return;
                }
                exitRequested = true;
                phaseStartedAt = now;
                Debug.Log("GYMCHAOS_DAVIE_EXIT_FOR_PICKUP_REQUESTED");
            }
        }
        else if (!passengerExited)
        {
            if (agent.HasLeftGym)
            {
                passengerExited = true;
                phaseStartedAt = now;
                Debug.Log("GYMCHAOS_DAVIE_EXIT_CONFIRMED");
            }
            else if (now - phaseStartedAt > 90d)
            {
                Fail("davie_exit_timeout");
                return;
            }
        }
        else if (!busReturnedForPickup)
        {
            if (bus.IsParked && !bus.IsDriving &&
                bus.gameObject.activeInHierarchy)
            {
                busReturnedForPickup = true;
                phaseStartedAt = now;
                Debug.Log(
                    $"GYMCHAOS_DAVIE_BUS_RETURNED_FOR_PICKUP passenger={bus.PassengerPoint}");
            }
            else if (now - phaseStartedAt > 65d)
            {
                Fail("bus_return_timeout");
                return;
            }
        }
        else if (!passengerBoarded)
        {
            if (director.DaviePassengerBoardedForVerification)
            {
                passengerBoarded = true;
                phaseStartedAt = now;
                Debug.Log(
                    $"GYMCHAOS_DAVIE_PASSENGER_BOARDED " +
                    $"gateClear={GymRoadsideBusStop.HasClearPedestrianGateForVerification()}");
            }
            else if (now - phaseStartedAt > 60d)
            {
                Fail("passenger_pickup_timeout");
                return;
            }
        }
        else
        {
            if (bus.HasCompletedDeparture && !bus.gameObject.activeInHierarchy)
            {
                finished = true;
                Debug.Log(
                    "GYMCHAOS_DAVIE_PASSENGER_LIFECYCLE_OK " +
                    "entryBeforeDeparture=True busReturnsAfterExit=True " +
                    $"passengerBoards={director.DaviePassengerBoardedForVerification} " +
                    $"pedestrianGateClear={GymRoadsideBusStop.HasClearPedestrianGateForVerification()} " +
                    "busDepartsAgain=True");
                EditorApplication.isPlaying = false;
            }
            else if (now - phaseStartedAt > 65d)
            {
                Fail("final_bus_departure_timeout");
            }
        }
    }

    private static void Fail(string reason)
    {
        if (!EditorApplication.isPlaying) return;
        Debug.LogError(
            $"GYMCHAOS_DAVIE_PASSENGER_LIFECYCLE_FAIL reason={reason} " +
            $"state={agent?.State} pos={davie?.VisitorPhysicsPosition} " +
            $"entered={agent?.HasEnteredGym} left={agent?.HasLeftGym} " +
            $"busParked={bus?.IsParked} busDriving={bus?.IsDriving} " +
            $"busActive={bus?.gameObject.activeInHierarchy} " +
            $"blocker={davie?.LastVisitorRouteBlocker}");
        finished = false;
        EditorApplication.isPlaying = false;
    }
}
