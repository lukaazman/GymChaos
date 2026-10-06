#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Graphics-only capture of the policeman's Glock during the draw and shoot
/// clips. Dispatches the real officer, freezes each clip phase with
/// Time.timeScale = 0, and writes Logs/agent/police-pose-*.png.
/// </summary>
[InitializeOnLoad]
public static class GymChaosPoliceGunPoseCapture
{
    private const string RequestedKey = "GymChaos.PoliceGunPoseCaptureRequested";
    private static readonly float[] DrawPhases = { 0.35f, 0.6f, 0.8f, 0.97f };
    private static readonly float[] ShootPhases = { 0.1f, 0.4f, 0.7f };
    private static double started;
    private static bool killRequested;
    private static bool frozen;
    private static int step;
    private static int settleFrames;
    private static int resultCode;
    private static EnemyFighter officer;
    private static GymPoliceWeapon weapon;
    private static MixamoScanRetargetAnimator animator;

    static GymChaosPoliceGunPoseCapture()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        started = EditorApplication.timeSinceStartup;
        killRequested = false;
        frozen = false;
        step = 0;
        settleFrames = 0;
        officer = null;
        weapon = null;
        animator = null;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Time.timeScale = 3f;
            AudioListener.volume = 0f;
            // Keeps play mode ticking while the live Editor is unfocused.
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup - started > 420d)
            {
                throw new InvalidOperationException(
                    $"Police gun pose capture timed out: kill={killRequested} frozen={frozen} " +
                    $"officer={GymPoliceDirector.LastOfficer != null} " +
                    $"phase={GymPoliceDirector.ActiveInstance?.DispatchPhaseForVerification} " +
                    $"weaponReady={GymPoliceDirector.LastOfficer?.GetComponent<GymPoliceWeapon>()?.IsReady} " +
                    $"time={Time.time:F1} step={step}");
            }
            if (!killRequested)
            {
                RequestDispatch();
                return;
            }
            if (!frozen)
            {
                TryFreezeOfficer();
                return;
            }
            AdvanceCapture();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void RequestDispatch()
    {
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        if (player == null)
        {
            return;
        }
        director?.SuspendVisitorSimulationForVerification();
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Ronnie && !fighter.IsDead)
            {
                fighter.TakeMeleeHit(Vector3.zero, fighter.MaxHealth + 1f, 0.02f);
                killRequested = true;
                return;
            }
        }
    }

    private static void TryFreezeOfficer()
    {
        officer = GymPoliceDirector.LastOfficer;
        if (officer == null || officer.IsDead)
        {
            return;
        }
        weapon = officer.GetComponent<GymPoliceWeapon>();
        animator = officer.GetComponent<MixamoScanRetargetAnimator>();
        if (weapon == null || animator == null || !weapon.IsReady)
        {
            return;
        }
        GymPoliceOfficer brain = officer.GetComponent<GymPoliceOfficer>();
        if (brain != null)
        {
            brain.enabled = false;
        }
        Time.timeScale = 0f;
        // Stand the player where the "player" camera renders from, so the
        // held gun aims at the viewer as in gameplay.
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player != null)
        {
            Vector3 spot = officer.transform.position + officer.transform.forward * 1.5f;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = spot;
            }
            player.transform.position = spot;
            Physics.SyncTransforms();
        }
        weapon.SetVisible(true);
        ExternalRiggedCharacterVisual visual = officer.GetComponent<ExternalRiggedCharacterVisual>();
        Transform hand = visual != null && visual.RuntimeRig != null ? visual.RuntimeRig.RightHand : null;
        if (hand != null)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Transform child in hand.GetComponentsInChildren<Transform>(true))
            {
                names.Add(child.name);
            }
            Debug.Log($"GYMCHAOS_POLICE_GUN_POSE_HAND bones={string.Join(",", names)}");
        }
        frozen = true;
        settleFrames = 0;
        ApplyStep();
    }

    private static int TotalSteps => DrawPhases.Length + ShootPhases.Length;

    private static void ApplyStep()
    {
        bool drawing = step < DrawPhases.Length;
        float phase = drawing ? DrawPhases[step] : ShootPhases[step - DrawPhases.Length];
        if (!animator.HoldGunActionForVerification(drawing, phase))
        {
            throw new InvalidOperationException("Policeman gun clip is missing.");
        }
        weapon.SetVisible(true);
    }

    private static void AdvanceCapture()
    {
        // Let Update/LateUpdate apply the held pose and weapon placement.
        if (++settleFrames < 4)
        {
            return;
        }
        bool drawing = step < DrawPhases.Length;
        float phase = drawing ? DrawPhases[step] : ShootPhases[step - DrawPhases.Length];
        string label = $"{(drawing ? "draw" : "shoot")}-{Mathf.RoundToInt(phase * 100f):D2}";
        Transform body = officer.transform;
        Vector3 hand = weapon.HandPositionForVerification;
        Vector3 chest = body.position + Vector3.up * 1.45f;
        Capture(label + "-player", chest + body.forward * 1.5f + Vector3.up * 0.2f, hand);
        Capture(label + "-side", hand + body.right * 0.8f + body.forward * 0.15f + Vector3.up * 0.05f, hand);
        Debug.Log(
            $"GYMCHAOS_POLICE_GUN_POSE label={label} " +
            $"handLocal={body.InverseTransformPoint(hand)} " +
            $"palmLocal={body.InverseTransformPoint(weapon.PalmPointForVerification)} " +
            $"gunLocal={body.InverseTransformPoint(weapon.WeaponCenterForVerification)} " +
            $"length={weapon.WorldLengthForVerification:F3}", weapon);
        step++;
        settleFrames = 0;
        if (step >= TotalSteps)
        {
            Debug.Log("GYMCHAOS_POLICE_GUN_POSE_CAPTURE_OK");
            Finish(0);
            return;
        }
        ApplyStep();
    }

    private static void Capture(string label, Vector3 eye, Vector3 focus)
    {
        GameObject host = new GameObject("Police Gun Pose Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.nearClipPlane = 0.03f;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
        RenderTexture target = new RenderTexture(720, 720, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(720, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        string path = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", $"police-pose-{label}.png");
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Finish(int code)
    {
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        Time.timeScale = 1f;
        EditorApplication.isPlaying = false;
    }
}
#endif
