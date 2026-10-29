#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Race track gate: the traced layout is a closed drivable lap with sane
/// slopes and spiral clearance, the built road has a collider under every
/// metre of both branches, the city towers and exterior walls on the road are
/// gone while the track exists, the race fill closes the empty stretches
/// beside the road (towers beside at least 97 % of it), and everything comes
/// back afterwards.
/// Logs GYMCHAOS_RACE_TRACK_OK.
/// </summary>
public static class GymChaosRaceTrackVerifier
{
    private const string RequestedKey = "GymChaos.RaceTrackVerificationRequested";
    private const float MinLap = 550f;
    private const float MaxLap = 800f;
    private const float MaxSlopeDegrees = 14f;
    private const float MinDeckGap = 4.5f;
    private static double started;
    private static int stage;
    private static long trianglesBefore;
    private static GymRaceTrack track;
    private static IEnumerator filling;
    private static float coverageBefore;
    private static readonly StringBuilder report = new StringBuilder();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Race Track Verification")]
    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        stage = 0;
        report.Length = 0;
        GymChaosVerifierHooks.Hook(Tick, Changed);
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) started = EditorApplication.timeSinceStartup;
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double elapsed = EditorApplication.timeSinceStartup - started;
        try
        {
            switch (stage)
            {
                case 0:
                    if (!GymRaceCityCarver.CityReady())
                    {
                        if (elapsed > 150d) Finish(1, "city ring never finished loading");
                        return;
                    }
                    if (elapsed < 4d) return;
                    CheckLayout();
                    trianglesBefore = GymRaceCityCarver.CountCityTriangles();
                    GymRaceCorridorClearance.Clear();
                    track = GymRaceTrackBuilder.Build(null);
                    coverageBefore = GymRaceCityFiller.MeasureCoverage();
                    filling = GymRaceCityFiller.Fill();
                    stage = 3;
                    return;
                case 3:
                    if (filling.MoveNext()) return;
                    GymRaceCityCarver.CarveNow();
                    stage = 1;
                    return;
                case 1:
                    Physics.SyncTransforms();
                    CheckBuilt();
                    GymRaceCityCarver.Restore();
                    GymRaceCorridorClearance.Restore();
                    Object.Destroy(track.gameObject);
                    stage = 2;
                    return;
                case 2:
                    CheckRestored();
                    Finish(0, report.ToString());
                    return;
            }
        }
        catch (VerificationFailure failure)
        {
            Finish(1, failure.Message + " | " + report);
        }
    }

    private sealed class VerificationFailure : System.Exception
    {
        public VerificationFailure(string message) : base(message) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new VerificationFailure(message);
    }

    private static void CheckLayout()
    {
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        GymRaceTrackPath shortcut = GymRaceTrackLayout.Shortcut;
        Require(main.Closed, "main path is not closed");
        Require(main.Length >= MinLap && main.Length <= MaxLap, $"lap length {main.Length:F1} outside {MinLap}-{MaxLap}");

        float maxSlope = Mathf.Max(MaxSlope(main), MaxSlope(shortcut));
        Require(maxSlope <= MaxSlopeDegrees, $"max slope {maxSlope:F1} > {MaxSlopeDegrees}");

        // Where two parts of the lap overlap in plan (spiral entry over its
        // exit), the decks must be far enough apart to drive underneath.
        float minGap = float.MaxValue;
        for (int i = 0; i < main.Count; i++)
        {
            for (int j = i + 40; j < main.Count; j++)
            {
                if (main.Count - (j - i) < 40) continue;
                Vector3 a = main.Points[i];
                Vector3 b = main.Points[j];
                float reach = main.HalfWidths[i] + main.HalfWidths[j];
                float dx = a.x - b.x;
                float dz = a.z - b.z;
                if (dx * dx + dz * dz < reach * reach)
                {
                    minGap = Mathf.Min(minGap, Mathf.Abs(a.y - b.y));
                }
            }
        }
        Require(minGap >= MinDeckGap, $"overlapping decks only {minGap:F2} m apart");

        float bypassed = main.WrapDistance(GymRaceTrackLayout.ShortcutMergeDistance - GymRaceTrackLayout.ShortcutForkDistance);
        float ratio = shortcut.Length / Mathf.Max(bypassed, 1f);
        Require(ratio > 0.8f && ratio < 1.02f, $"shortcut/outer length ratio {ratio:F2}");

        float minShortcutDepth = float.MaxValue;
        for (int i = 0; i < shortcut.Count; i++) minShortcutDepth = Mathf.Min(minShortcutDepth, shortcut.Points[i].y);
        Require(minShortcutDepth < GymRaceTrackLayout.FloorY - 3f, $"shortcut is not sunken (min y {minShortcutDepth:F2})");

        float maxHeight = float.MinValue;
        for (int i = 0; i < main.Count; i++) maxHeight = Mathf.Max(maxHeight, main.Points[i].y);
        Require(maxHeight > GymRaceTrackLayout.FloorY + 10f, $"spiral is not raised (max y {maxHeight:F2})");

        Vector3 grid0 = GymRaceTrackLayout.GridSlot(0).position;
        Vector3 grid5 = GymRaceTrackLayout.GridSlot(5).position;
        Require(Mathf.Abs(GymRaceTrackLayout.SignedDistanceToRoad(grid0)) < 6f && GymRaceTrackLayout.SignedDistanceToRoad(grid5) < -1f,
            "grid slots are not on the road");

        report.Append($"lap={main.Length:F1} shortcut={shortcut.Length:F1} bypassed={bypassed:F1} ratio={ratio:F2} ");
        report.Append($"maxSlope={maxSlope:F1} deckGap={minGap:F2} trenchY={minShortcutDepth:F2} spiralTop={maxHeight:F2} ");
    }

    private static float MaxSlope(GymRaceTrackPath path)
    {
        float max = 0f;
        int segments = path.Closed ? path.Count : path.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = path.Points[i];
            Vector3 b = path.Points[path.Wrap(i + 1)];
            float flat = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            if (flat < 0.05f) continue;
            max = Mathf.Max(max, Mathf.Atan2(Mathf.Abs(b.y - a.y), flat) * Mathf.Rad2Deg);
        }
        return max;
    }

    private static int CountSlabsOverTrench()
    {
        // A visible ground slab has no collider: over the dipping shortcut it
        // reads as a box the kart drives through.
        int count = 0;
        GymRaceTrackPath shortcut = GymRaceTrackLayout.Shortcut;
        foreach (string side in new[] { "West", "North", "South", "East" })
        {
            GameObject slab = GameObject.Find("City Dystopia Ground " + side);
            Renderer renderer = slab != null ? slab.GetComponent<Renderer>() : null;
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }
            Bounds b = renderer.bounds;
            for (int i = 0; i < shortcut.Count; i++)
            {
                Vector3 p = shortcut.Points[i];
                if (p.y < b.max.y - 0.01f && p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static void CheckBuilt()
    {
        Require(track != null, "track was not built");
        Require(GymRaceCityCarver.FootprintCount > 100, $"only {GymRaceCityCarver.FootprintCount} footprints loaded");
        Require(GymRaceCityCarver.CityCopyCount == 8 + GymRaceCityFiller.CopyCount,
            $"carver found {GymRaceCityCarver.CityCopyCount} city copies, fill added {GymRaceCityFiller.CopyCount}");
        float coverage = GymRaceCityFiller.MeasureCoverage();
        Require(coverage >= 0.97f && GymRaceCityFiller.CopyCount > 0,
            $"towers beside {coverage:P0} of the road (before fill {coverageBefore:P0}, fill copies {GymRaceCityFiller.CopyCount})");
        report.Append($"cityFill={GymRaceCityFiller.CopyCount} coverage={coverageBefore:P0}->{coverage:P0} ");
        Require(GymRaceCityCarver.RemovedBuildingCount > 0, "no building was removed");
        int onRoad = GymRaceCityCarver.CountTrianglesOnRoad(0f);
        Require(onRoad == 0, $"{onRoad} city triangles remain on the road");
        int remnants = GymRaceCityCarver.CountRemnantTriangles();
        Require(remnants == 0, $"{remnants} triangles of removed buildings still stand");
        report.Append($"remnants={remnants} ");
        int slabOverTrench = CountSlabsOverTrench();
        Require(slabOverTrench == 0, $"{slabOverTrench} sunken shortcut samples sit under a solid city ground slab");
        report.Append($"slabOverTrench={slabOverTrench} ");
        long carved = GymRaceCityCarver.CountCityTriangles();
        report.Append($"buildings={GymRaceCityCarver.RemovedBuildingCount} triangles={GymRaceCityCarver.RemovedTriangleCount} ");
        report.Append($"loose={GymRaceCityCarver.RemovedLooseTriangleCount} cityBefore={trianglesBefore} cityCarved={carved} ");

        int gaps = 0;
        string firstGap = null;
        gaps += CountColliderGaps(GymRaceTrackLayout.Main, ref firstGap);
        gaps += CountColliderGaps(GymRaceTrackLayout.Shortcut, ref firstGap);
        Require(gaps == 0, $"{gaps} road samples without a race collider, first {firstGap}");

        List<string> foreign = FindForeignColliders();
        Require(foreign.Count == 0, $"{foreign.Count} foreign colliders on the road: {string.Join(", ", foreign)}");
        report.Append($"clearedColliders={GymRaceCorridorClearance.DisabledColliderCount} hiddenRenderers={GymRaceCorridorClearance.HiddenRendererCount} ");
        report.Append($"skippedLarge={GymRaceCorridorClearance.SkippedLargeRenderers} ");
        Require(track.CountdownLights.Count == 4, "gantry lights missing");
        Require(track.HiddenRenderers.Count >= 1, "no city ground slab was swapped for the trench ground");
        report.Append($"trenchSlabs={track.HiddenRenderers.Count} ");
    }

    private static int CountColliderGaps(GymRaceTrackPath path, ref string first)
    {
        int gaps = 0;
        int mask = 1 << GymRaceTrackBuilder.RaceTrackLayer;
        for (int i = 0; i < path.Count; i++)
        {
            for (int lane = -1; lane <= 1; lane++)
            {
                Vector3 p = path.Points[i] + path.Rights[i] * (lane * path.HalfWidths[i] * 0.7f);
                Vector3 origin = p + path.Ups[i] * 2.2f;
                bool hit = Physics.Raycast(origin, -path.Ups[i], out RaycastHit info, 3f, mask, QueryTriggerInteraction.Ignore);
                if (hit && Mathf.Abs(info.point.y - p.y) < 0.2f && track.RoadColliders.Contains(info.collider as MeshCollider))
                {
                    continue;
                }
                gaps++;
                if (first == null) first = $"{path.Name}#{i} lane={lane} at={p} hit={(hit ? info.collider.name + "@" + info.point.y.ToString("F2") : "none")}";
            }
        }
        return gaps;
    }

    private static List<string> FindForeignColliders()
    {
        List<string> names = new List<string>();
        HashSet<Collider> seen = new HashSet<Collider>();
        int ignore = (1 << GymRaceTrackBuilder.RaceTrackLayer) | (1 << GymCityDystopiaSurroundings.CityBackgroundLayer);
        int mask = ~ignore;
        Collider[] buffer = new Collider[32];
        foreach (GymRaceTrackPath path in new[] { GymRaceTrackLayout.Main, GymRaceTrackLayout.Shortcut })
        {
            for (int i = 0; i < path.Count; i += 2)
            {
                Vector3 center = path.Points[i] + path.Ups[i] * 1.2f;
                Quaternion rotation = Quaternion.LookRotation(path.Tangents[i], path.Ups[i]);
                Vector3 half = new Vector3(path.HalfWidths[i] - 0.3f, 0.75f, 0.6f);
                int count = Physics.OverlapBoxNonAlloc(center, half, buffer, rotation, mask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < count; k++)
                {
                    Collider collider = buffer[k];
                    if (collider == null || !seen.Add(collider)) continue;
                    // Moving actors (visitors, cars, the player) may cross the road.
                    if (collider.attachedRigidbody != null || collider is CharacterController) continue;
                    if (collider.GetComponentInParent<EnemyFighter>() != null || collider.GetComponentInParent<GymVisitorVehicle>() != null) continue;
                    names.Add($"{Path(collider.transform)}@{collider.bounds.center}");
                }
            }
        }
        return names;
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private static void CheckRestored()
    {
        long after = GymRaceCityCarver.CountCityTriangles();
        Require(after == trianglesBefore, $"city triangles after restore {after} != before {trianglesBefore}");
        Require(!GymRaceCityCarver.IsCarved, "carver still reports carved");
        Require(GymRaceCorridorClearance.DisabledColliderCount == 0 && GymRaceCorridorClearance.HiddenRendererCount == 0, "corridor clearance not restored");
        GameObject west = GameObject.Find("City Dystopia Ground West");
        Require(west != null && west.GetComponent<Renderer>().enabled, "west city ground not restored");
        Require(GameObject.Find(GymRaceTrackBuilder.RootName) == null, "race track root still exists");
        Require(GymRaceCityFiller.FillCount == 0 && GameObject.Find("City Dystopia Race Fill 1") == null,
            "race city fill still stands");
        report.Append("restored=1");
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RACE_TRACK_OK " + detail);
        else Debug.LogError("GYMCHAOS_RACE_TRACK_FAILED " + detail);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        GymRaceCityCarver.Restore();
        GymRaceCorridorClearance.Restore();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Record(code);
            EditorApplication.Exit(code);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
