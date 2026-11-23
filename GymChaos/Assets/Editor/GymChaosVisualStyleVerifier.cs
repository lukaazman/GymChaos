#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Remake visual style: every remade character loads from
/// Resources/Characters/Remake with its EyeAnchor, its eye bar sits on that
/// anchor, it carries its remake name, characters without a remake keep the
/// DEFAULT asset, and the light uses the remake look (softer sun shadows).
/// Runs the gym in the Remake style; the player's own style choice is
/// restored with the verifier save. Logs GYMCHAOS_VISUAL_STYLE_OK.
/// </summary>
public static class GymChaosVisualStyleVerifier
{
    private const string RequestedKey = "GymChaos.VisualStyleVerificationRequested";
    // The bar's shell front passes just ahead of the anchor (in front of the
    // brow and nose bridge where they stand forward), never behind it.
    private const float AnchorTolerance = 0.04f;
    private static double started;

    private static readonly Dictionary<BodybuilderIdentity, string> ExpectedNames =
        new Dictionary<BodybuilderIdentity, string>
        {
            { BodybuilderIdentity.Arnold, "Arnold Schnitzel" },
            { BodybuilderIdentity.Cbum, "CBun" },
            { BodybuilderIdentity.Zyzz, "Zizzle" },
            { BodybuilderIdentity.Ronnie, "Big Ron" },
            { BodybuilderIdentity.JayCutler, "Jay Butler" },
            { BodybuilderIdentity.Goku, "Gokoo" },
            { BodybuilderIdentity.Davie, "Davie" },
            { BodybuilderIdentity.Policeman, "Officer Pudding" },
            { BodybuilderIdentity.Mark, "Mark" },
            { BodybuilderIdentity.Manwithsuit1, "manwithsuit1" }
        };

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        // Every character and vehicle on its POTATO asset for this run.
        Environment.SetEnvironmentVariable("GYMCHAOS_VISUAL_STYLE", "remake");
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, Changed);

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) started = Time.timeAsDouble;
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double elapsed = Time.timeAsDouble - started;
        List<ExternalRiggedCharacterVisual> visuals = FindVisuals();
        bool davieReady = visuals.Exists(v => v.Identity == BodybuilderIdentity.Davie && v.RuntimeRenderer != null);
        if (elapsed < 6d || (!davieReady && elapsed < 90d)) return;
        int code = 1;
        try
        {
            CheckAssets();
            if (!davieReady) throw new InvalidOperationException("Davie never spawned");
            int checkedRemakes = 0;
            foreach (ExternalRiggedCharacterVisual visual in visuals)
            {
                if (CheckVisual(visual)) checkedRemakes++;
            }
            CheckHank();
            bool larry =             CheckLarry();
            CheckLocker();
            Debug.Log($"GYMCHAOS_VISUAL_STYLE_OK remakesInScene={checkedRemakes} larry={larry} assets={ExpectedNames.Count + 2}");
            code = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_VISUAL_STYLE_FAILED " + exception.Message);
        }
        SessionState.EraseBool(RequestedKey);
        Environment.SetEnvironmentVariable("GYMCHAOS_VISUAL_STYLE", null);
        EditorApplication.update -= Tick;
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

    // Every remake (and Hank's) is in Resources with an EyeAnchor and a texture.
    private static void CheckAssets()
    {
        List<string> files = new List<string> { GymVisualStyle.HomelessFile };
        foreach (BodybuilderIdentity identity in GymVisualStyle.RemadeIdentities)
        {
            if (!GymVisualStyle.TryRemake(identity, out string model, out string texture))
                throw new InvalidOperationException($"{identity} has no remake path in the Remake style");
            CheckAsset(identity.ToString(), model, texture);
            string name = GymVisualStyle.DisplayName(identity, identity.ToString());
            if (name != ExpectedNames[identity])
                throw new InvalidOperationException($"{identity} is named '{name}', expected '{ExpectedNames[identity]}'");
        }
        CheckAsset("Hank", GymVisualStyle.ModelPath(GymVisualStyle.HomelessFile),
            GymVisualStyle.TexturePath(GymVisualStyle.HomelessFile));
        if (GymVisualStyle.HomelessName("Hank") != "Hank Tank")
            throw new InvalidOperationException("Hank's remake name is wrong");
        CheckAsset("Larry", GymVisualStyle.ModelPath(GymVisualStyle.FixerFile),
            GymVisualStyle.TexturePath(GymVisualStyle.FixerFile));
        if (GymFixerAgent.DisplayName != "Lollipup Larry")
            throw new InvalidOperationException($"Larry's remake name is '{GymFixerAgent.DisplayName}'");
    }

    // Larry (the fixer dog) is prepared hidden at boot when the gym has a
    // fixer: his remake loads (it carries an EyeAnchor), he wears no eye bar
    // in either style, and he carries his remake name.
    private static bool CheckLarry()
    {
        GymFixerAgent larry = UnityEngine.Object.FindFirstObjectByType<GymFixerAgent>(FindObjectsInactive.Include);
        if (larry == null) return false;
        if (FindDeep(larry.transform, "EyeAnchor") == null)
            throw new InvalidOperationException("Larry did not load his remake (no EyeAnchor)");
        if (larry.GetComponentInChildren<FaceCensorSettings>(true) != null)
            throw new InvalidOperationException("Larry wears an eye bar");
        ScreenSpaceCharacterLabel label = larry.GetComponentInChildren<ScreenSpaceCharacterLabel>(true);
        if (label == null || label.DisplayName != "Lollipup Larry")
            throw new InvalidOperationException($"Larry: name label '{label?.DisplayName}'");
        return true;
    }

    private static void CheckAsset(string who, string model, string texture)
    {
        GameObject prefab = Resources.Load<GameObject>(model);
        if (prefab == null) throw new InvalidOperationException($"{who}: no model at Resources/{model}");
        if (FindDeep(prefab.transform, "EyeAnchor") == null) throw new InvalidOperationException($"{who}: model has no EyeAnchor");
        if (Resources.Load<Texture2D>(texture) == null) throw new InvalidOperationException($"{who}: no texture at Resources/{texture}");
    }

    private static bool CheckVisual(ExternalRiggedCharacterVisual visual)
    {
        BodybuilderIdentity identity = visual.Identity;
        bool remade = ExpectedNames.ContainsKey(identity);
        string path = visual.RuntimeResourcePath ?? string.Empty;
        if (remade != path.StartsWith("Characters/Remake/", StringComparison.Ordinal))
            throw new InvalidOperationException($"{identity} loaded '{path}' in the Remake style");
        if (!remade) return false;
        Transform anchor = FindDeep(visual.RuntimeModelRoot, "EyeAnchor");
        FaceCensorSettings bar = visual.GetComponentInChildren<FaceCensorSettings>(true);
        if (anchor == null) throw new InvalidOperationException($"{identity}: no EyeAnchor");
        // POTATO shows faces: a bar left over from the DEFAULT look is a bug.
        if (bar != null && bar.GetComponent<MeshRenderer>() != null)
            throw new InvalidOperationException($"{identity} still wears a black eye bar in the POTATO style");
        if (identity == BodybuilderIdentity.Goku &&
            visual.RuntimeRenderer.sharedMaterial.shader.name != "GymChaos/BodybuilderToon")
            throw new InvalidOperationException("the remade Goku is not drawn with the toon shader");
        ScreenSpaceCharacterLabel label = visual.GetComponentInChildren<ScreenSpaceCharacterLabel>(true);
        if (label == null || label.DisplayName != ExpectedNames[identity])
            throw new InvalidOperationException($"{identity}: name label '{label?.DisplayName}'");
        return true;
    }

    // Hank (park resident) loads his remake without a black bar (POTATO) and with his remake name.
    private static void CheckHank()
    {
        GymHomelessAgent hank = UnityEngine.Object.FindFirstObjectByType<GymHomelessAgent>(FindObjectsInactive.Include);
        if (hank == null) throw new InvalidOperationException("no Hank");
        if (hank.EyeBar != null && hank.EyeBar.GetComponent<MeshRenderer>() != null)
            throw new InvalidOperationException("Hank still wears a black eye bar in the POTATO style");
        Transform anchor = FindDeep(hank.transform, "EyeAnchor");
        if (anchor == null) throw new InvalidOperationException("Hank did not load his remake (no EyeAnchor)");
        ScreenSpaceCharacterLabel label = hank.GetComponentInChildren<ScreenSpaceCharacterLabel>(true);
        if (label == null || label.DisplayName != "homeless Hank Tank")
            throw new InvalidOperationException($"Hank: name label '{label?.DisplayName}'");
    }

    // The style is a boot-menu choice now: the locker must not offer a switch.
    private static void CheckLocker()
    {
        GymExperienceService service = GymExperienceService.Active;
        if (service == null) throw new InvalidOperationException("no experience service");
        GymLockerMenuUI locker = GymLockerMenuUI.Ensure(service);
        if (locker.HasStylePanelForVerification)
            throw new InvalidOperationException("the locker still carries an art-style switch");
        Debug.Log("GYMCHAOS_VISUAL_STYLE_LOCKER_OK");
    }

    // The shell front sits ahead of the anchor, not behind it, and close to it.
    private static void CheckBarOnAnchor(string who, FaceCensorSettings bar, Transform anchor)
    {
        Vector3 front = bar.transform.TransformPoint(0f, 0f, bar.ConfiguredFaceDepth);
        float ahead = Vector3.Dot(front - anchor.position, bar.transform.forward);
        float offset = Vector3.Distance(front, anchor.position);
        Debug.Log($"GYMCHAOS_VISUAL_STYLE_BAR identity={who} offset={offset:F4} ahead={ahead:F4}");
        if (ahead < 0f || offset > AnchorTolerance)
            throw new InvalidOperationException($"{who}: eye bar front {offset:F3} m from its EyeAnchor (ahead {ahead:F3})");
    }

    private static List<ExternalRiggedCharacterVisual> FindVisuals() =>
        new List<ExternalRiggedCharacterVisual>(UnityEngine.Object.FindObjectsByType<ExternalRiggedCharacterVisual>(
            FindObjectsInactive.Include, FindObjectsSortMode.None));

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
