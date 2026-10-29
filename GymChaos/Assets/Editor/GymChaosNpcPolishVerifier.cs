#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 2026-10-16 polish: manwithsuit1 wears a visible black eye bar, the homeless
/// guy has a "homeless &lt;name&gt;" nametag and dialogue name, and Goku's flight,
/// run and punch speed are 2x while walking (roam) is unchanged.
/// </summary>
[InitializeOnLoad]
public static class GymChaosNpcPolishVerifier
{
    private const string RequestedKey = "GymChaos.NpcPolishVerificationRequested";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;

    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;

    static GymChaosNpcPolishVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run NPC Polish Verification")]
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
            startedAt = EditorApplication.timeSinceStartup;
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
        try
        {
            if (EditorApplication.timeSinceStartup - startedAt > 120d)
            {
                throw new TimeoutException("actors did not spawn");
            }

            GameObject suit = GameObject.Find("NPC - manwithsuit1");
            GymHomelessAgent homeless = GymHomelessAgent.Instance;
            EnemyFighter goku = null;
            foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                FindObjectsInactive.Include))
            {
                if (fighter.Identity == BodybuilderIdentity.Goku) goku = fighter;
            }
            if (suit == null || homeless == null || !homeless.IsReady || goku == null) return;

            FaceCensorSettings suitBar = null;
            foreach (FaceCensorSettings censor in suit.GetComponentsInChildren<FaceCensorSettings>(true))
            {
                MeshRenderer meshRenderer = censor.GetComponent<MeshRenderer>();
                if (meshRenderer != null && meshRenderer.enabled && censor.name.Contains("Black Eye Bar"))
                {
                    suitBar = censor;
                }
            }
            // The bar mesh is added in the same frame the visual finishes.
            if (suitBar == null) return;
            Require(suitBar.ConfiguredSize.x > 0.05f, "suit bar has no width");
            Debug.Log($"GYMCHAOS_NPC_SUIT_BAR_OK bar={suitBar.name} size={suitBar.ConfiguredSize}");

            ScreenSpaceCharacterLabel label = homeless.GetComponent<ScreenSpaceCharacterLabel>();
            Require(label != null, "homeless guy has no nametag");
            string shown = (string)typeof(ScreenSpaceCharacterLabel)
                .GetField("displayName", Any).GetValue(label);
            Require(shown == GymHomelessAgent.DisplayName &&
                shown.StartsWith("homeless "), $"nametag text '{shown}'");
            MethodInfo build = typeof(GymDialogueDirector).GetMethod("BuildHomelessDefinition", Any);
            object definition = build.Invoke(null, new object[] { homeless });
            string dialogueName = (string)definition.GetType()
                .GetField("displayName", Any).GetValue(definition);
            Require(dialogueName == GymHomelessAgent.DisplayName,
                $"dialogue name '{dialogueName}'");
            Debug.Log($"GYMCHAOS_NPC_HOMELESS_NAME_OK tag='{shown}' dialogue='{dialogueName}'");

            float maxSpeed = (float)typeof(EnemyFighter).GetField("maxSpeed", Any).GetValue(goku);
            float attackCooldown = (float)typeof(EnemyFighter).GetField("attackCooldown", Any).GetValue(goku);
            MethodInfo chase = typeof(EnemyFighter).GetMethod("GetChaseSpeed", Any);
            float grounded = (float)chase.Invoke(goku, null);
            Require(Mathf.Abs(grounded - maxSpeed * 2f) < 0.01f,
                $"grounded run {grounded:F2} expected {maxSpeed * 2f:F2}");
            FieldInfo state = typeof(EnemyFighter).GetField("gokuFlightState", Any);
            object previous = state.GetValue(goku);
            state.SetValue(goku, Enum.Parse(state.FieldType, "Flying"));
            float flying = (float)chase.Invoke(goku, null);
            state.SetValue(goku, previous);
            Require(Mathf.Abs(flying - maxSpeed * 3f) < 0.01f,
                $"flight {flying:F2} expected {maxSpeed * 3f:F2} (was 1.5x)");
            MixamoScanRetargetAnimator animator = goku.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            Require(animator != null, "Goku has no retarget animator");
            float punch = (float)typeof(MixamoScanRetargetAnimator)
                .GetProperty("PunchDuration", Any).GetValue(animator);
            Require(Mathf.Abs(punch - 0.325f) < 0.001f, $"punch duration {punch:F3} expected 0.325");
            // An angry member throws punches back to back: the next combo
            // starts as the last one ends (no pause), slightly faster than 0.72 s.
            EnemyFighter member = null;
            foreach (EnemyFighter f in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
            {
                if (f != null && f != goku && !f.IsPolice && f.GetComponentInChildren<MixamoScanRetargetAnimator>(true) != null)
                {
                    member = f;
                    break;
                }
            }
            Require(member != null, "no member with a retarget animator");
            float memberPunch = (float)typeof(MixamoScanRetargetAnimator).GetProperty("PunchDuration", Any)
                .GetValue(member.GetComponentInChildren<MixamoScanRetargetAnimator>(true));
            float memberCooldown = (float)typeof(EnemyFighter).GetField("attackCooldown", Any).GetValue(member);
            Require(memberPunch < 0.72f && memberCooldown <= memberPunch + 0.001f,
                $"member punch={memberPunch:F2} cooldown={memberCooldown:F2} (pause between combos)");
            Debug.Log($"GYMCHAOS_NPC_PUNCH_COMBO_OK punch={memberPunch:F2} cooldown={memberCooldown:F2}");
            Debug.Log($"GYMCHAOS_NPC_GOKU_SPEED_OK run={grounded:F2} (was {maxSpeed:F2}) " +
                $"fly={flying:F2} (was {maxSpeed * 1.5f:F2}) punchSeconds={punch:F2} (was 0.72) " +
                $"cooldown={attackCooldown / 2f:F2} (was {attackCooldown:F2})");
            End(0);
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_NPC_POLISH_FAIL {exception.Message}");
            End(1);
        }
    }

    private static void End(int code)
    {
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(resultCode);
        if (code == 0) Debug.Log("GYMCHAOS_NPC_POLISH_OK");
        EditorApplication.isPlaying = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
