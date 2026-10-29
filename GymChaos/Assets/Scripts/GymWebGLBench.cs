using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WebGL players opened with ?bench=1: skips the start screen, starts
/// gameplay and holds the player at the GymChaosPerformanceVerifier views,
/// logging GYMCHAOS_WEBGL_BENCH scenario=... avg/p95 per view and
/// GYMCHAOS_WEBGL_BENCH_DONE at the end. Tools/Invoke-WebGLSmoke.ps1 -Bench
/// reads it, so gameplay FPS is measured without a person at the mouse.
/// </summary>
public sealed class GymWebGLBench : MonoBehaviour
{
    private const float SettleSeconds = 3f;
    private const float SampleSeconds = 5f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!Application.absoluteURL.Contains("bench=1")) return;
        new GameObject("WebGL Bench").AddComponent<GymWebGLBench>();
    }
#endif

    private IEnumerator Start()
    {
        float giveUp = Time.unscaledTime + 120f;
        while (!GymOutdoorBuilder.IsBuilt || !GymProteinStoreEnvironment.IsLoaded || GymDoorway.Instance == null)
        {
            if (Time.unscaledTime > giveUp)
            {
                Debug.LogError("GYMCHAOS_WEBGL_BENCH_FAILED world did not finish loading");
                yield break;
            }
            yield return null;
        }
        GymStartScreen startScreen = FindFirstObjectByType<GymStartScreen>();
        if (startScreen != null) Destroy(startScreen.gameObject);
        FindFirstObjectByType<GymArenaBootstrap>()?.BeginGameplay();
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        if (player == null)
        {
            Debug.LogError("GYMCHAOS_WEBGL_BENCH_FAILED no player");
            yield break;
        }
        if (Application.absoluteURL.Contains("race=1"))
        {
            yield return RaceSmoke(player);
            yield break;
        }

        Vector3 gym = GymDoorway.Instance.InteriorPoint + Vector3.left * 8f;
        Vector3 parking = new Vector3(GymOutdoorBuilder.ParkingBounds.max.x - 2f,
            GymDoorway.Instance.ExteriorPoint.y, GymOutdoorBuilder.ParkingBounds.center.z);
        Vector3 locker = GymBackRoomBuilder.TryGetRoomBounds(out Bounds room)
            ? new Vector3(room.center.x + 1.5f, GymDoorway.Instance.InteriorPoint.y, room.center.z)
            : gym;
        var views = new (string name, Vector3 position, float yaw, float time)[]
        {
            ("mainGymMirror", gym, -90f, 0.5f),
            ("lockerMirror", locker, 90f, 0.5f),
            ("outdoorParking", parking, -90f, 0.5f),
            ("proteinStore", GymProteinStoreEnvironment.StoreFrontClearPoint + Vector3.right, 90f, 0.5f),
            ("mainGymMirrorNight", gym, -90f, 0.95f),
            ("outdoorParkingNight", parking, -90f, 0.95f)
        };
        var frames = new List<float>(512);
        foreach (var view in views)
        {
            Vector3 position = view.position + Vector3.up * 0.05f;
            Quaternion rotation = Quaternion.Euler(0f, view.yaw, 0f);
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.linearVelocity = Vector3.zero;
            }
            player.transform.SetPositionAndRotation(position, rotation);
            GymTimeOfDay.Instance?.SetTimeForVerification(view.time);
            yield return new WaitForSecondsRealtime(SettleSeconds);

            frames.Clear();
            float end = Time.unscaledTime + SampleSeconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
            }
            frames.Sort();
            float sum = 0f;
            for (int i = 0; i < frames.Count; i++) sum += frames[i];
            float average = sum / frames.Count;
            float p95 = frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))];
            Debug.Log($"GYMCHAOS_WEBGL_BENCH scenario={view.name} fps={1000f / average:F1} " +
                $"avgMs={average:F1} p95Ms={p95:F1} frames={frames.Count}");
        }
        Debug.Log("GYMCHAOS_WEBGL_BENCH_DONE");
    }

    // ?race=1: start the race from the sign like the player does (player
    // kart, no autopilot) and run into the race, the path that crashed the
    // WebGL player.
    private static IEnumerator RaceSmoke(PlayerMovement player)
    {
        yield return new WaitForSecondsRealtime(20f);
        float giveUp = Time.unscaledTime + 90f;
        while (GymRaceDirector.Instance == null || GymRaceSign.Instance == null ||
            !StartFromSign(player))
        {
            if (Time.unscaledTime > giveUp)
            {
                Debug.LogError("GYMCHAOS_WEBGL_RACE_FAILED race never became available");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);
        }
        Debug.Log("GYMCHAOS_WEBGL_RACE_STARTED " + MemoryLine());
        giveUp = Time.unscaledTime + 90f;
        while (GymRaceDirector.Instance.CurrentPhase != GymRaceDirector.Phase.Racing)
        {
            if (Time.unscaledTime > giveUp)
            {
                Debug.LogError($"GYMCHAOS_WEBGL_RACE_FAILED stuck in {GymRaceDirector.Instance.CurrentPhase}");
                yield break;
            }
            yield return null;
        }
        Debug.Log("GYMCHAOS_WEBGL_RACE_RACING " + MemoryLine());
        yield return new WaitForSecondsRealtime(10f);
        Debug.Log("GYMCHAOS_WEBGL_BENCH_DONE race=1 " + MemoryLine());
    }

    private static string MemoryLine() =>
        $"reservedMB={UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} " +
        $"allocatedMB={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} " +
        $"managedMB={System.GC.GetTotalMemory(false) / 1048576}";

    private static bool StartFromSign(PlayerMovement player)
    {
        Vector3 stand = GymRaceSign.Instance.StandPoint + Vector3.up * 0.05f;
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null) body.position = stand;
        player.transform.position = stand;
        return GymRaceDirector.Instance.TryStartFromSign(player);
    }
}
