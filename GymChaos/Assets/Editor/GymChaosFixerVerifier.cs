#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// End-to-end check of Jolly Dog, the sky-dropping fixer. Breaks a mirror,
/// moves a prop, kills a member, Ronnie and the police officer that answers
/// Ronnie's death, then checks: no visit before five days, none at night,
/// a daytime drop with a rainbow, falling/landing/running/casting/flying in
/// order with blended (non-popping) animation, one glowing cast per repair,
/// every listed repair done, later breakage left alone, flight only under
/// open sky, despawn high up, rainbow gone, and the officer driving away.
/// Logs GYMCHAOS_FIXER_OK. With -Graphics it also writes evidence PNGs to
/// Logs/agent/fixer/.
/// </summary>
public static class GymChaosFixerVerifier
{
    private const string RequestedKey = "GymChaos.FixerVerificationRequested";
    private const string ProgressionKey = "GymChaos.Progression.v1";
    private const string ProgressionBackupKey = "GymChaos.FixerVerifier.ProgressionBackup";
    private const string ProgressionHadKey = "GymChaos.FixerVerifier.ProgressionHad";
    private const float DayTime = 0.42f;
    private const float NightTime = 0.9f;

    private static IEnumerator script;
    private static double started;
    private static double waitUntil;
    private static float savedMaximumDeltaTime;
    private static string captureDirectory;

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
        SessionState.SetBool(ProgressionHadKey, PlayerPrefs.HasKey(ProgressionKey));
        SessionState.SetString(ProgressionBackupKey, PlayerPrefs.GetString(ProgressionKey, string.Empty));
        PlayerPrefs.DeleteKey(ProgressionKey);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Debug.Log("GYMCHAOS_FIXER_VERIFICATION_STARTED");
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            waitUntil = 0d;
            savedMaximumDeltaTime = Time.maximumDeltaTime;
            Time.maximumDeltaTime = 1f / 30f;
            AudioListener.pause = true;
            script = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        RestoreProgression();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || script == null) return;
        AudioListener.pause = true;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 480d)
                throw new InvalidOperationException("Fixer verification timed out.");
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (!script.MoveNext())
            {
                script = null;
                Debug.Log("GYMCHAOS_FIXER_OK");
                Finish(0);
                return;
            }
            if (script.Current is float seconds)
                waitUntil = EditorApplication.timeSinceStartup + seconds;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_FIXER_FAILED " + exception.Message);
            script = null;
            Finish(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable Until(Func<bool> condition, double timeoutSeconds, string what)
    {
        double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new InvalidOperationException("Timed out waiting for " + what);
            yield return null;
        }
    }

    private static IEnumerator Scenario()
    {
        foreach (object step in Until(() => GymFixerDirector.Instance != null &&
                     GymTimeOfDay.Instance != null &&
                     GymOutdoorBuilder.IsBuilt && GymDoorway.Instance != null,
                     90d, "the fixer director")) yield return step;
        GymFixerDirector fixers = GymFixerDirector.Instance;
        GymTimeOfDay time = GymTimeOfDay.Instance;
        Require(fixers.Agent == null, "Jolly Dog was loaded before anything was broken");
        GymFixerAgent dog = fixers.EnsureAgentPrepared();
        Require(dog != null, "Jolly Dog could not be prepared");
        Require(dog.GetComponentInChildren<EnemyFighter>(true) == null &&
            dog.GetComponentInChildren<EnemyMeshHitboxRig>(true) == null,
            "Jolly Dog must not carry a damageable fighter or hitboxes");
        Require(Mathf.Abs(dog.MeasuredHeight - GymFixerAgent.GameplayHeight) < 0.08f,
            $"Jolly Dog height {dog.MeasuredHeight:F2} is not ~{GymFixerAgent.GameplayHeight}");
        fixers.SetSpawnSuspendedForVerification(true);
        GymVisitorDirector visitors = GymVisitorDirector.Instance;
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        Require(player != null, "No player");
        captureDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "agent", "fixer"));
        Directory.CreateDirectory(captureDirectory);

        // Members start the day inside; give the roster a moment to settle.
        yield return 4f;
        fixers.ScanNowForVerification();
        Require(fixers.BaselineCaptured && fixers.TrackedItemCount > 5,
            $"Baseline not captured (items={fixers.TrackedItemCount})");
        Require(fixers.CurrentIssues.Count == 0,
            $"A fresh gym already reports {fixers.CurrentIssues.Count} issues");
        if (visitors != null) visitors.PauseVisitorScheduleForVerification();

        // --- break things ---------------------------------------------
        time.SetTimeForVerification(DayTime);
        List<GlassShatterPanel> mirrors = new List<GlassShatterPanel>();
        foreach (GlassShatterPanel panel in UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(FindObjectsSortMode.None))
            if (panel.name == "Mirror panel" && !panel.IsShattered) mirrors.Add(panel);
        Require(mirrors.Count >= 2, "Need two gym mirrors");
        GlassShatterPanel brokenMirror = mirrors[0];
        GlassShatterPanel lateMirror = mirrors[1];
        brokenMirror.ShatterFromPowerImpact(
            brokenMirror.transform.position, brokenMirror.transform.forward, brokenMirror.transform.forward * 12f);
        Require(brokenMirror.IsShattered && !brokenMirror.gameObject.activeSelf, "Mirror did not break");

        PickupItem movedItem = FindLooseItem();
        Require(movedItem != null, "No resting loose prop inside the gym");
        Vector3 itemHome = movedItem.transform.position;
        Quaternion itemHomeRotation = movedItem.transform.rotation;
        Rigidbody itemBody = movedItem.GetComponent<Rigidbody>();
        GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym);
        Vector3 drop = new Vector3(gym.center.x + 1.3f, gym.min.y + 0.6f, gym.center.z - 1.7f);
        itemBody.position = drop;
        movedItem.transform.position = drop;
        itemBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();

        EnemyFighter member = FindMemberInside(visitors);
        Require(member != null, "No member inside the gym to knock out");
        EnemyFighter ronnie = FindFighter(BodybuilderIdentity.Ronnie);
        Require(ronnie != null, "Ronnie is missing");
        Kill(member, player);
        Kill(ronnie, player);
        foreach (object step in Until(() => GymPoliceDirector.LastOfficer != null,
                     90d, "the police officer answering Ronnie's death")) yield return step;
        EnemyFighter officer = GymPoliceDirector.LastOfficer;
        yield return 0.5f;
        Kill(officer, player);
        foreach (object step in Until(() => member.IsSettledCorpse && ronnie.IsSettledCorpse &&
                     officer.IsSettledCorpse, 20d, "corpses to settle")) yield return step;
        yield return 3f;
        fixers.ScanNowForVerification();
        int issueCount = fixers.CurrentIssues.Count;
        Require(issueCount >= 5,
            $"Expected mirror, prop and three corpses as issues, got {issueCount}: {DescribeIssues(fixers)}");
        Debug.Log($"GYMCHAOS_FIXER_VERIFY_ISSUES count={issueCount} {DescribeIssues(fixers)}");

        // --- not before five days -------------------------------------
        fixers.SetSpawnSuspendedForVerification(false);
        yield return 2.5f;
        Require(!fixers.IsVisitActive, "Jolly Dog came before five days had passed");

        // --- not at night, even after five days ------------------------
        time.SetTimeForVerification(NightTime);
        time.AdvanceForVerification(time.SimulatedDayLengthSeconds * 5.05f);
        Require(time.IsNight, "Clock is not at night after the advance");
        fixers.ScanNowForVerification();
        Require(fixers.OldestIssueAgeDays >= 5f, $"Issue age {fixers.OldestIssueAgeDays:F2} < 5 days");
        yield return 2.5f;
        Require(!fixers.IsVisitActive, "Jolly Dog came at night");

        // --- save round trip of the issue ages -------------------------
        string mirrorKey = "panel:" + brokenMirror.StableId;
        float mirrorSince = fixers.GetIssueSinceForVerification(mirrorKey);
        GymFixerDirector.CaptureIssueAges(out string[] ids, out float[] since);
        Require(Array.IndexOf(ids, mirrorKey) >= 0, "Broken mirror age is not saved");
        GymFixerDirector.RestoreIssueAges(ids, since);
        fixers.ScanNowForVerification();
        Require(Mathf.Abs(fixers.GetIssueSinceForVerification(mirrorKey) - mirrorSince) < 0.001f,
            "Restored mirror age changed after a reload");

        // --- daytime drop ----------------------------------------------
        time.SetTimeForVerification(DayTime);
        foreach (object step in Until(() => fixers.IsVisitActive, 6d, "the daytime drop")) yield return step;
        Require(!time.IsNight, "Drop happened at night");
        Require(dog.State == GymFixerAgent.FixerState.Falling, "Visit did not start falling");
        Require(dog.transform.position.y - dog.LandingPoint.y > 40f, "Jolly Dog did not start high in the sky");
        // Break another mirror after he arrived: it must wait for a later visit.
        lateMirror.ShatterFromPowerImpact(
            lateMirror.transform.position, lateMirror.transform.forward, lateMirror.transform.forward * 12f);

        float maxFallSpeed = 0f;
        float lastY = dog.transform.position.y;
        float peakRainbow = 0f;
        float rainbowSunDot = 1f;
        bool itemReturned = false;
        bool officerReturning = false;
        bool capturedFall = false, capturedCast = false, capturedRainbow = false, capturedFlight = false;
        int fixedBefore = fixers.FixedCount;
        while (fixers.IsVisitActive)
        {
            peakRainbow = Mathf.Max(peakRainbow, fixers.RainbowFade);
            if (fixers.RainbowFade > 0.5f)
                rainbowSunDot = Mathf.Min(rainbowSunDot, Vector3.Dot(
                    ((Vector3)Shader.GetGlobalVector("_GymRainbowAxis")).normalized, time.SunDirection));
            if (dog.State == GymFixerAgent.FixerState.Falling && Time.deltaTime > 0f)
            {
                maxFallSpeed = Mathf.Max(maxFallSpeed, (lastY - dog.transform.position.y) / Time.deltaTime);
                if (!capturedFall && dog.transform.position.y - dog.LandingPoint.y < 18f)
                    capturedFall = Capture("fall", dog.LandingPoint + Flat(dog.transform.forward) * 7f + Vector3.up * 1.6f,
                        dog.transform.position + Vector3.down * 2f);
            }
            lastY = dog.transform.position.y;
            if (!capturedRainbow && fixers.RainbowFade > 0.95f)
                capturedRainbow = CaptureRainbow(dog.LandingPoint);
            if (!capturedCast && dog.State == GymFixerAgent.FixerState.Casting && dog.GlowEnvelope > 0.9f)
            {
                Vector3 eye = dog.transform.position + dog.transform.forward * 2.3f +
                    dog.transform.right * 0.6f + Vector3.up * 1.1f;
                capturedCast = Capture("cast", eye, dog.transform.position + Vector3.up * 0.85f);
            }
            if (!capturedFlight && dog.State == GymFixerAgent.FixerState.Flying &&
                dog.transform.position.y - dog.GroundY > 6f)
                capturedFlight = Capture("flight", dog.transform.position - dog.transform.forward * 8f +
                    Vector3.down * dog.transform.position.y + Vector3.up * (dog.GroundY + 2f),
                    dog.transform.position);
            if (officer != null && officer.IsReturningToPoliceCar) officerReturning = true;
            // Revived members may kick the prop again later; judge the repair itself.
            if (!itemReturned && movedItem != null &&
                Vector3.Distance(movedItem.transform.position, itemHome) < 0.06f &&
                Quaternion.Angle(movedItem.transform.rotation, itemHomeRotation) < 3f)
                itemReturned = true;
            yield return null;
        }

        string history = string.Join(">", dog.StateHistory);
        Debug.Log(
            $"GYMCHAOS_FIXER_VERIFY_VISIT states={history} casts={dog.CastCount} " +
            $"fixed={fixers.FixedCount - fixedBefore} fall={maxFallSpeed:F1} rainbow={peakRainbow:F2} " +
            $"poseSpeed={dog.MaxPoseSpeed:F2} glowOutsideCast={dog.MaxGlowWhileNotCasting:F2} " +
            $"peaks={string.Join(",", dog.CastGlowPeaks)} altitude={dog.PeakAltitude:F1}");
        RequireOrder(dog.StateHistory, "Falling", "Landing", "Running", "Casting", "Exiting", "Flying", "Hidden");
        Require(maxFallSpeed > 25f, $"Fall was not very fast ({maxFallSpeed:F1} m/s)");
        Require(peakRainbow > 0.9f, $"Rainbow never faded in ({peakRainbow:F2})");
        // A real rainbow is centred on the antisolar point.
        Require(rainbowSunDot < -0.5f, $"Rainbow is not opposite the sun (dot {rainbowSunDot:F2})");
        int fixedThisVisit = fixers.FixedCount - fixedBefore;
        // The repair list is fixed at the drop; it holds at least our five.
        Require(dog.VisitIssueCount >= issueCount,
            $"Drop listed {dog.VisitIssueCount} issues, fewer than the {issueCount} staged");
        Require(fixedThisVisit == dog.VisitIssueCount,
            $"Fixed {fixedThisVisit} of {dog.VisitIssueCount} listed issues");
        Require(dog.CastCount == fixedThisVisit, $"Casts {dog.CastCount} != repairs {fixedThisVisit}");
        foreach (float peak in dog.CastGlowPeaks)
            Require(peak > 0.9f, $"A cast's lollipop glow peaked at only {peak:F2}");
        Require(dog.MaxGlowWhileNotCasting < 0.12f,
            $"Lollipop glowed outside casts ({dog.MaxGlowWhileNotCasting:F2})");
        Require(dog.MaxPoseSpeed < 14f,
            $"A pose popped (bone speed {dog.MaxPoseSpeed:F1} m/s relative to the body)");
        Require(dog.TakeoffHadOpenSky && dog.TookOffOutside, "Flew off under a ceiling or inside");
        Require(dog.PeakAltitude >= 89f && !dog.IsVisible, "Did not despawn high in the sky");

        // --- results ----------------------------------------------------
        Require(!brokenMirror.IsShattered && brokenMirror.gameObject.activeInHierarchy, "Mirror was not repaired");
        Require(lateMirror.IsShattered, "A mirror broken after the drop was repaired in the same visit");
        Require(itemReturned, "Prop was never put back where it started");
        foreach (EnemyFighter revived in new[] { member, ronnie })
        {
            Require(revived != null && !revived.IsDead, $"{revived?.Identity} was not revived");
            Require(Mathf.Abs(revived.CurrentHealth - revived.MaxHealth) < 0.01f,
                $"{revived.Identity} not at full health");
            Require(!revived.IsAggressive, $"{revived.Identity} came back angry");
        }
        Require(officerReturning || officer == null, "Revived officer did not head back to the car");
        foreach (object step in Until(() => !GymPoliceDirector.IsDispatchActive, 120d,
                     "the police car to drive away")) yield return step;
        Require(officer == null, "Officer is still in the world after the car left");

        foreach (object step in Until(() => fixers.RainbowFade <= 0.001f, 8d, "the rainbow to fade out"))
            yield return step;
        // --- a second visit reuses the prepared actor ----------------------
        Require(!fixers.IsVisitActive, "Visit restarted straight away");
        time.AdvanceForVerification(time.SimulatedDayLengthSeconds * 5.05f);
        time.SetTimeForVerification(DayTime);
        fixers.ScanNowForVerification();
        foreach (object step in Until(() => fixers.IsVisitActive, 6d, "the second visit")) yield return step;
        foreach (object step in Until(() => !fixers.IsVisitActive, 150d, "the second visit to end")) yield return step;
        Require(!lateMirror.IsShattered, "Second visit did not repair the later mirror");
        Require(dog.CastCount == dog.VisitIssueCount && dog.MaxPoseSpeed < 14f,
            $"Second visit casts {dog.CastCount}/{dog.VisitIssueCount} poseSpeed {dog.MaxPoseSpeed:F1}");
        Debug.Log($"GYMCHAOS_FIXER_VERIFY_SECOND_VISIT states={string.Join(">", dog.StateHistory)}");
        foreach (object step in Until(() => fixers.RainbowFade <= 0.001f, 8d, "the rainbow to fade out again"))
            yield return step;

        Debug.Log(
            $"GYMCHAOS_FIXER_VERIFY_CAPTURES fall={capturedFall} cast={capturedCast} " +
            $"rainbow={capturedRainbow} flight={capturedFlight} dir={captureDirectory}");
    }

    private static void RequireOrder(List<string> history, params string[] expected)
    {
        int cursor = 0;
        for (int i = 0; i < history.Count && cursor < expected.Length; i++)
            if (history[i] == expected[cursor]) cursor++;
        Require(cursor == expected.Length, "State order wrong: " + string.Join(">", history));
    }

    private static void Kill(EnemyFighter fighter, PlayerMovement player)
    {
        Vector3 push = Vector3.ProjectOnPlane(fighter.transform.position - player.transform.position, Vector3.up);
        push = push.sqrMagnitude > 0.01f ? push.normalized : Vector3.forward;
        fighter.TakeThrowableHit(push * 4f, 100000f, 0f, false);
        Require(fighter.IsDead, $"{fighter.Identity} did not die");
    }

    private static EnemyFighter FindFighter(BodybuilderIdentity identity)
    {
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
            if (fighter.Identity == identity && !fighter.IsDead) return fighter;
        return null;
    }

    private static EnemyFighter FindMemberInside(GymVisitorDirector visitors)
    {
        if (visitors == null) return null;
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            if (!fighter.IsDead && visitors.IsScheduledVisitor(fighter) &&
                !GymOutdoorBuilder.IsPlayerOutsideGym(fighter.transform.position) &&
                fighter.Identity != BodybuilderIdentity.Goku)
                return fighter;
        }
        return null;
    }

    private static PickupItem FindLooseItem()
    {
        if (!GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym)) return null;
        foreach (PickupItem item in UnityEngine.Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
        {
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || item.IsHeld || item.ItemType == WeightType.None ||
                item.ItemType == WeightType.Radio || item.ItemType == WeightType.Barbell) continue;
            Vector3 position = item.transform.position;
            if (position.x > gym.min.x && position.x < gym.max.x && position.z > gym.min.z && position.z < gym.max.z)
                return item;
        }
        return null;
    }

    private static string DescribeIssues(GymFixerDirector fixers)
    {
        List<string> parts = new List<string>();
        foreach (FixerIssue issue in fixers.CurrentIssues) parts.Add(issue.Kind + ":" + issue.Key);
        return string.Join(",", parts);
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.forward;
    }

    private static bool CaptureRainbow(Vector3 from)
    {
        Vector4 axis = Shader.GetGlobalVector("_GymRainbowAxis");
        Vector3 look = Flat(new Vector3(axis.x, 0f, axis.z));
        // Stand beside the touchdown point so Jolly Dog is not in the lens.
        Vector3 eye = from + Vector3.Cross(Vector3.up, look) * 4f - look * 2f + Vector3.up * 1.7f;
        return Capture("rainbow", eye, eye + look * 10f + Vector3.up * 4.5f, 75f);
    }

    private static bool Capture(string name, Vector3 eye, Vector3 target, float fov = 55f)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return true;
        GameObject host = new GameObject("Fixer Evidence Camera");
        RenderTexture rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        try
        {
            Camera camera = host.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1500f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.targetTexture = rt;
            Camera main = Camera.main;
            if (main != null) camera.cullingMask = main.cullingMask;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string path = Path.Combine(captureDirectory, name + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            Debug.Log($"GYMCHAOS_FIXER_CAPTURE name={name} path={path}");
            return true;
        }
        finally
        {
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void RestoreProgression()
    {
        if (!SessionState.GetBool(ProgressionHadKey, false))
            PlayerPrefs.DeleteKey(ProgressionKey);
        else
            PlayerPrefs.SetString(ProgressionKey, SessionState.GetString(ProgressionBackupKey, string.Empty));
        PlayerPrefs.Save();
    }

    private static void Finish(int code)
    {
        Time.maximumDeltaTime = savedMaximumDeltaTime;
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        RestoreProgression();
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
