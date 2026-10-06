using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosVisitorRouteProbe
{
    private const string RequestedKey = "GymChaos.VisitorRouteProbeRequested";
    private static double startedAt;
    private static double nextSampleAt;
    private static EnemyFighter fighter;
    private static bool requested;
    private static bool routeVehiclesLogged;

    static GymChaosVisitorRouteProbe()
    {
        if (!GymChaosVerifierPrefs.GetBool(RequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Probe Visitor Entry Route")]
    public static void Run()
    {
        startedAt = 0d;
        nextSampleAt = 0d;
        fighter = null;
        requested = false;
        GymChaosVerifierPrefs.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResumeAfterDomainReload()
    {
        if (EditorApplication.isPlaying)
        {
            StartProbe();
        }
    }

    private static void StartProbe()
    {
        startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            StartProbe();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            GymChaosVerifierPrefs.DeleteKey(RequestedKey);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        Time.timeScale = 1f;
        GymVisitorDirector director = Object.FindAnyObjectByType<GymVisitorDirector>();
        if (director == null || !GymOutdoorBuilder.IsBuilt)
        {
            return;
        }

        if (!requested && EditorApplication.timeSinceStartup - startedAt > 2.5d)
        {
            requested = director.BeginEntryForVerification(out fighter);
            Debug.Log($"GYMCHAOS_ENTRY_ROUTE_PROBE_STARTED requested={requested}");
        }

        if (fighter != null)
        {
            if (!routeVehiclesLogged)
            {
                LogRouteVehicles(fighter);
                routeVehiclesLogged = true;
            }

            GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
            if (agent != null && EditorApplication.timeSinceStartup >= nextSampleAt)
            {
                nextSampleAt = EditorApplication.timeSinceStartup + 0.5d;
                Collider[] nearbyHits = Physics.OverlapSphere(
                    fighter.transform.position + Vector3.up * 0.9f, 1.35f,
                    ~0, QueryTriggerInteraction.Ignore);
                string nearby = "none";
                for (int hitIndex = 0; hitIndex < nearbyHits.Length; hitIndex++)
                {
                    Collider nearbyHit = nearbyHits[hitIndex];
                    if (nearbyHit == null || nearbyHit.transform == fighter.transform ||
                        nearbyHit.transform.IsChildOf(fighter.transform)) continue;
                    nearby = nearby == "none" ? nearbyHit.name : nearby + "|" + nearbyHit.name;
                    if (nearby.Length > 180) break;
                }
                Rigidbody body = fighter.GetComponent<Rigidbody>();
                Debug.Log(
                    $"GYMCHAOS_ENTRY_ROUTE_PROBE_SAMPLE state={agent.State} " +
                    $"pos={fighter.transform.position} target={agent.TravelTargetForVerification} " +
                    $"waypoint={agent.VehicleEntryWaypointForVerification} " +
                    $"vel={(body != null ? body.linearVelocity : Vector3.zero)} " +
                    $"blocker={fighter.LastVisitorRouteBlocker} nearby={nearby} " +
                    $"capsule={DescribeForwardCapsule(fighter)}");
                if (agent.HasEnteredGym)
                {
                    Debug.Log("GYMCHAOS_ENTRY_ROUTE_PROBE_OK");
                    GymChaosVerifierPrefs.DeleteKey(RequestedKey);
                    EditorApplication.isPlaying = false;
                    return;
                }
            }
        }

        if (EditorApplication.timeSinceStartup - startedAt > 30d)
        {
            Debug.LogError("GYMCHAOS_ENTRY_ROUTE_PROBE_TIMEOUT");
            GymChaosVerifierPrefs.DeleteKey(RequestedKey);
            EditorApplication.isPlaying = false;
        }
    }


    private static void LogRouteVehicles(EnemyFighter target)
    {
        Collider[] targetColliders = target.GetComponentsInChildren<Collider>(true);
        GymVisitorVehicle[] vehicles = Object.FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < vehicles.Length; i++)
        {
            GymVisitorVehicle vehicle = vehicles[i];
            if (vehicle == null) continue;
            Collider[] colliders = vehicle.GetComponentsInChildren<Collider>(true);
            Bounds bounds = new Bounds(vehicle.transform.position, Vector3.zero);
            bool hasBounds = false;
            bool ignored = false;
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null) continue;
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
                for (int t = 0; t < targetColliders.Length; t++)
                {
                    if (targetColliders[t] != null &&
                        Physics.GetIgnoreCollision(targetColliders[t], collider))
                    {
                        ignored = true;
                        break;
                    }
                }
            }

            Debug.Log(
                $"GYMCHAOS_ENTRY_ROUTE_PROBE_VEHICLE name={vehicle.name} " +
                $"identity={vehicle.name} parked={vehicle.IsParked} " +
                $"driving={vehicle.IsDriving} active={vehicle.gameObject.activeInHierarchy} " +
                $"pos={vehicle.transform.position} passenger={vehicle.PassengerPoint} " +
                $"aisle={vehicle.AislePointForVerification} " +
                $"boundsCenter={(hasBounds ? bounds.center : Vector3.zero)} " +
                $"boundsExtents={(hasBounds ? bounds.extents : Vector3.zero)} " +
                $"ignoredWithTarget={ignored}");
        }
    }
    private static string DescribeForwardCapsule(EnemyFighter target)
    {
        if (target == null)
        {
            return "none";
        }

        Vector3 origin = target.transform.position;
        Vector3 lower = origin + Vector3.up * EnemyFighter.VisitorProbeLower;
        Vector3 upper = origin + Vector3.up * EnemyFighter.VisitorProbeUpper;
        float radius = EnemyFighter.GetBodyRadiusForIdentity(target.Identity);
        RaycastHit[] hits = Physics.CapsuleCastAll(
            lower, upper, radius, Vector3.right, 1.35f,
            ~0, QueryTriggerInteraction.Ignore);
        string result = "none";
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == target.transform ||
                hit.transform.IsChildOf(target.transform))
            {
                continue;
            }

            string item = hit.name + "@" + hits[i].distance.ToString("F2");
            result = result == "none" ? item : result + "|" + item;
            if (result.Length > 220)
            {
                break;
            }
        }

        return result;
    }}
