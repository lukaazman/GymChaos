#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Headless outfit evidence: bakes the player body and its cloth shells in
/// idle, running and jumping poses for several outfit combinations and writes
/// player-local OBJs plus a material manifest to
/// Logs/agent/outfit-poses/&lt;GYMCHAOS_OUTFIT_LABEL&gt;. Tools/render_outfit_poses.py
/// renders them in Blender, so runtime shell geometry is reviewed without a GPU run.
/// </summary>
[InitializeOnLoad]
public static class GymChaosOutfitPoseExport
{
    private const string Key = "GymChaos.OutfitPoseExportRequested";
    private static bool launched;

    static GymChaosOutfitPoseExport()
    {
        GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);
    }

    public static void Run()
    {
        launched = false;
        GymChaosVerifierExit.Record(1);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        PlayerHandRig rig = UnityEngine.Object.FindFirstObjectByType<PlayerHandRig>();
        if (rig == null || rig.RuntimeHead == null) return;
        if (UnityEngine.Object.FindFirstObjectByType<GymExperienceService>() == null) return;
        launched = true;
        new GameObject("Outfit pose export").AddComponent<GymChaosOutfitPoseExportRunner>();
    }

    public static void Finish(int code)
    {
        GymChaosVerifierExit.Record(code);
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosOutfitPoseExportRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        IEnumerator body = Export();
        while (true)
        {
            object current;
            try
            {
                if (!body.MoveNext()) break;
                current = body.Current;
            }
            catch (Exception exception)
            {
                Debug.LogError("GYMCHAOS_OUTFIT_POSES_FAIL " + exception);
                GymChaosOutfitPoseExport.Finish(1);
                yield break;
            }
            yield return current;
        }
        GymChaosOutfitPoseExport.Finish(0);
    }

    private IEnumerator Export()
    {
        string label = Environment.GetEnvironmentVariable("GYMCHAOS_OUTFIT_LABEL");
        if (string.IsNullOrEmpty(label)) label = "current";
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "outfit-poses", label);
        Directory.CreateDirectory(folder);
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>();
        PlayerCosmeticLoadout loadout = player.GetComponent<PlayerCosmeticLoadout>();
        GymExperienceService service = FindFirstObjectByType<GymExperienceService>();
        service.State.level = Mathf.Max(service.State.level, 10);
        service.State.masteryRank = Mathf.Max(service.State.masteryRank, 1);
        service.State.lockerPrepCompleted = true;
        (string, GymTopStyle, GymShirtColor, GymBottomStyle, GymShirtColor)[] looks =
        {
            ("oversized_pants", GymTopStyle.Oversized, GymShirtColor.White, GymBottomStyle.Pants, GymShirtColor.Black),
            ("tank_pants", GymTopStyle.TankTop, GymShirtColor.Black, GymBottomStyle.Pants, GymShirtColor.White),
            ("onyx_pants", GymTopStyle.Onyx, GymShirtColor.Gold, GymBottomStyle.Pants, GymShirtColor.White),
            ("tank_shorts", GymTopStyle.TankTop, GymShirtColor.Red, GymBottomStyle.Shorts, GymShirtColor.White),
            ("onyx_shorts", GymTopStyle.Onyx, GymShirtColor.Blue, GymBottomStyle.Shorts, GymShirtColor.White),
        };
        (string, float)[] poses = { ("idle1", 0f), ("running", 0.2f), ("running", 0.45f), ("running", 0.7f), ("jumping", 0.3f), ("jumping", 0.5f) };
        StringBuilder manifest = new StringBuilder("{\n");
        bool firstLook = true;
        foreach ((string name, GymTopStyle top, GymShirtColor shirt, GymBottomStyle bottom, GymShirtColor bottomColor) in looks)
        {
            service.EquipTop(top);
            service.EquipShirt(shirt);
            service.EquipBottom(bottom);
            service.EquipBottomColor(bottomColor);
            float limit = Time.realtimeSinceStartup + 20f;
            yield return null;
            while ((!loadout.IsShirtVisualReady || loadout.IsTopShellPending || loadout.IsBottomShellPending) &&
                Time.realtimeSinceStartup < limit) yield return null;
            yield return null;
            if (!firstLook) manifest.Append(",\n");
            firstLook = false;
            manifest.Append("\"").Append(name).Append("\": {");
            bool firstPart = true;
            foreach (SkinnedMeshRenderer renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (renderer.gameObject.layer == PlanarGymMirror.FirstPersonPlayerLayer || renderer.sharedMesh == null) continue;
                bool shell = PlayerTopShell.IsShellObject(renderer.name);
                if (!shell && renderer.sharedMesh.vertexCount < 5000) continue;
                Material[] materials = renderer.sharedMaterials;
                if (!firstPart) manifest.Append(", ");
                firstPart = false;
                manifest.Append("\"").Append(Safe(renderer.name)).Append("\": [");
                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    Texture texture = material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    Color color = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
                    if (m > 0) manifest.Append(", ");
                    manifest.AppendFormat(CultureInfo.InvariantCulture, "{{\"texture\": \"{0}\", \"color\": [{1}, {2}, {3}]}}",
                        texture != null ? texture.name : "", color.r, color.g, color.b);
                }
                manifest.Append("]");
            }
            manifest.Append("}");
            foreach ((string clip, float phase) in poses)
            {
                if (clip == "running") SampleRun(rig, phase);
                else
                {
                    rig.HoldStablePoseForVerification(clip, phase, out _);
                    yield return null;
                }
                foreach (SkinnedMeshRenderer renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (renderer.gameObject.layer == PlanarGymMirror.FirstPersonPlayerLayer || renderer.sharedMesh == null) continue;
                    bool shell = PlayerTopShell.IsShellObject(renderer.name);
                    if (!shell && renderer.sharedMesh.vertexCount < 5000) continue;
                    WriteObj(renderer, player.transform,
                        Path.Combine(folder, $"{name}__{clip}_{phase:F2}__{Safe(renderer.name)}.obj"));
                }
                if (clip == "running") rig.ResetToVerificationIdlePose();
            }
        }
        manifest.Append("\n}\n");
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest.ToString());
        Debug.Log("GYMCHAOS_OUTFIT_POSES_OK " + folder);
    }

    // Gameplay sprint pose (PlayerHandRig.SampleRunForVerification without its
    // reset, so the bake sees the stride).
    private static void SampleRun(PlayerHandRig rig, float phase)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Type type = typeof(PlayerHandRig);
        AnimationClip run = (AnimationClip)type.GetField("runClip", flags).GetValue(rig);
        type.GetField("moveAmount", flags).SetValue(rig, 1f);
        type.GetField("sprinting", flags).SetValue(rig, true);
        type.GetField("locomotionElapsed", flags).SetValue(rig, phase * Mathf.Max(0.01f, run.length - 0.001f));
        type.GetMethod("RestoreAnimatedBones", flags).Invoke(rig, null);
        type.GetMethod("SampleLocomotion", flags).Invoke(rig, null);
        type.GetMethod("UpdateBakedRenderers", flags).Invoke(rig, null);
    }

    private static string Safe(string name) => name.Replace(' ', '_').Replace("\"", "");

    private static void WriteObj(SkinnedMeshRenderer renderer, Transform frame, string path)
    {
        Mesh baked = new Mesh();
        renderer.BakeMesh(baked, true);
        Vector3[] v = baked.vertices;
        Vector2[] uv = baked.uv;
        Matrix4x4 m = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        StringBuilder obj = new StringBuilder(v.Length * 60);
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = m.MultiplyPoint3x4(v[i]);
            // OBJ is right-handed: mirror x.
            obj.AppendFormat(CultureInfo.InvariantCulture, "v {0:F5} {1:F5} {2:F5}\n", -p.x, p.y, p.z);
        }
        for (int i = 0; i < uv.Length; i++)
            obj.AppendFormat(CultureInfo.InvariantCulture, "vt {0:F5} {1:F5}\n", uv[i].x, uv[i].y);
        for (int sub = 0; sub < baked.subMeshCount; sub++)
        {
            obj.Append("o sub").Append(sub).Append("\nusemtl sub").Append(sub).Append('\n');
            int[] t = baked.GetTriangles(sub);
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i] + 1, b = t[i + 2] + 1, c = t[i + 1] + 1;
                obj.Append("f ").Append(a).Append('/').Append(a).Append(' ').Append(b).Append('/').Append(b)
                    .Append(' ').Append(c).Append('/').Append(c).Append('\n');
            }
        }
        File.WriteAllText(path, obj.ToString());
        UnityEngine.Object.Destroy(baked);
    }
}
#endif
