#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Name tags and health bars must not show through walls. The player stands
/// in the open facing the homeless man: his tag shows. A wall is dropped
/// between the camera and him: the tag hides. The wall goes away: it shows
/// again.
/// </summary>
public static class GymChaosLabelOcclusionVerifier
{
    private const string RequestedKey = "GymChaos.LabelOcclusionVerificationRequested";
    private const double Settle = 1.2d;

    private enum Stage { Wait, Open, Walled, Cleared }

    private static Stage stage;
    private static double startedAt;
    private static double stageAt;
    private static bool finished;
    private static int resultCode = 1;
    private static PlayerMovement player;
    private static GymHomelessAgent homeless;
    private static ScreenSpaceCharacterLabel label;
    private static GameObject wall;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

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
            startedAt = stageAt = GymChaosVerifierClock.Now;
            stage = Stage.Wait;
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
            if (now - startedAt > 240d) throw new TimeoutException($"stage {stage} did not finish");
            switch (stage)
            {
                case Stage.Wait:
                    homeless = GymHomelessAgent.Instance;
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                    if (homeless == null || !homeless.IsReady || player == null || Camera.main == null) return;
                    label = homeless.GetComponent<ScreenSpaceCharacterLabel>();
                    Require(label != null, "homeless man has no label");
                    GymTimeOfDay.Instance?.SetTimeForVerification(0.5f);
                    player.enabled = false;
                    PlacePlayerInOpen();
                    Enter(Stage.Open, now);
                    break;
                case Stage.Open:
                    if (now - stageAt < Settle) return;
                    Require(label.IsShowing, "label hidden in an open view");
                    BuildWall();
                    Enter(Stage.Walled, now);
                    break;
                case Stage.Walled:
                    if (now - stageAt < Settle) return;
                    Require(!label.IsShowing, "label still shows through a wall");
                    UnityEngine.Object.Destroy(wall);
                    Enter(Stage.Cleared, now);
                    break;
                case Stage.Cleared:
                    if (now - stageAt < Settle) return;
                    Require(label.IsShowing, "label did not return once the wall was gone");
                    Debug.Log("GYMCHAOS_LABEL_OCCLUSION_OK");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LABEL_OCCLUSION_FAILED " + exception.Message);
            Finish(1);
        }
    }

    private static void Enter(Stage next, double now)
    {
        stage = next;
        stageAt = now;
    }

    // First of eight compass spots 5 m out with a clear line to the man's head.
    private static void PlacePlayerInOpen()
    {
        Vector3 target = homeless.transform.position + Vector3.up * 1.2f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 spot = homeless.transform.position + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 5f;
            Vector3 eye = spot + Vector3.up * 1.7f;
            Physics.SyncTransforms();
            bool blocked = Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<GymHomelessAgent>() == null;
            if (blocked) continue;
            Vector3 look = homeless.transform.position - spot;
            look.y = 0f;
            player.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(look));
            Physics.SyncTransforms();
            return;
        }
        throw new InvalidOperationException("no open spot around the homeless man");
    }

    private static void BuildWall()
    {
        Vector3 from = Camera.main.transform.position;
        Vector3 to = homeless.transform.position + Vector3.up * 1.2f;
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Label Occlusion Test Wall";
        wall.transform.position = Vector3.Lerp(from, to, 0.5f);
        wall.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(to - from, Vector3.up));
        wall.transform.localScale = new Vector3(8f, 8f, 0.3f);
        Physics.SyncTransforms();
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        if (player != null) player.enabled = true;
        if (wall != null) UnityEngine.Object.Destroy(wall);
        GymChaosVerifierClock.EndFixedStep();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(code);
        else EditorApplication.isPlaying = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
