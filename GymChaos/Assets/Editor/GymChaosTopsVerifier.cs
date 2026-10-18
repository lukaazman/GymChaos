#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Top selection (2026-10-17): TANK TOP, OVERSIZED and ONYX, each in every
/// shirt colour.
/// - Save: the top survives a JSON round trip; an unknown or missing top
///   falls back to the tank top.
/// - Locker: a TOP row with the three named buttons above the colour row;
///   clicking one equips it.
/// - Look: every top x colour loads its own texture on the body; only
///   Oversized adds the skinned cloth shell, which shares the body's bones,
///   sits outside the body (a few cm, never inside) and follows animation.
/// With a graphics device it writes front/back captures to Logs/agent/tops.
/// </summary>
[InitializeOnLoad]
public static class GymChaosTopsVerifier
{
    private const string Key = "GymChaos.TopsVerificationRequested";
    private static bool launched;

    static GymChaosTopsVerifier()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/GymChaos/Run Tops Verification")]
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
        new GameObject("Tops verifier").AddComponent<GymChaosTopsVerifierRunner>();
    }

    public static void Finish(int code)
    {
        GymChaosVerifierExit.Record(code);
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosTopsVerifierRunner : MonoBehaviour
{
    private readonly System.Collections.Generic.List<string> notes = new System.Collections.Generic.List<string>();

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        IEnumerator body = Verify();
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
                Debug.LogError("GYMCHAOS_TOPS_FAIL " + exception.Message + " " + string.Join(" ", notes));
                GymChaosTopsVerifier.Finish(1);
                yield break;
            }
            yield return current;
        }
        Debug.Log("GYMCHAOS_TOPS_OK " + string.Join(" ", notes));
        GymChaosTopsVerifier.Finish(0);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private IEnumerator Verify()
    {
        // ---- save data
        GymProgressionState saved = new GymProgressionState { top = nameof(GymTopStyle.Onyx), shirt = "Red" };
        GymProgressionState loaded = JsonUtility.FromJson<GymProgressionState>(JsonUtility.ToJson(saved));
        loaded.Normalize();
        Require(loaded.top == "Onyx" && loaded.shirt == "Red", $"round trip top={loaded.top} shirt={loaded.shirt}");
        GymProgressionState unknown = new GymProgressionState { top = "Hoodie" };
        unknown.Normalize();
        Require(unknown.top == "TankTop", $"unknown top normalised to {unknown.top}");
        GymProgressionState legacy = JsonUtility.FromJson<GymProgressionState>("{\"level\":3,\"shirt\":\"Blue\"}");
        legacy.Normalize();
        Require(legacy.top == "TankTop" && legacy.shirt == "Blue", $"legacy save top={legacy.top}");
        notes.Add("save=ok");

        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>();
        PlayerCosmeticLoadout loadout = player.GetComponent<PlayerCosmeticLoadout>();
        GymExperienceService service = FindFirstObjectByType<GymExperienceService>();
        Require(loadout != null && service != null && service.State != null, "no loadout/service");
        service.State.level = Mathf.Max(service.State.level, 10);
        service.State.masteryRank = Mathf.Max(service.State.masteryRank, 1);

        // ---- locker menu
        MethodInfo open = typeof(GymExperienceService).GetMethod("OpenLockerMenu",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Require(open != null, "OpenLockerMenu missing");
        open.Invoke(service, new object[] { player });
        GymLockerMenuUI ui = null;
        float limit = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < limit)
        {
            ui = FindFirstObjectByType<GymLockerMenuUI>();
            if (ui != null && ui.TopButtonsForVerification != null && ui.GetComponent<CanvasGroup>().alpha > 0.99f) break;
            yield return null;
        }
        Require(ui != null && ui.TopButtonsForVerification != null, "locker menu has no TOP row");
        Button[] tops = ui.TopButtonsForVerification;
        Require(tops.Length == 3, $"{tops.Length} top buttons");
        string[] names = { "TANK TOP", "OVERSIZED", "ONYX" };
        for (int i = 0; i < 3; i++)
        {
            string label = tops[i].GetComponentInChildren<Text>().text;
            Require(label == names[i], $"top button {i} reads '{label}'");
            Require(tops[i].interactable, $"{label} locked at level 10");
        }
        RectTransform topRect = (RectTransform)tops[0].transform;
        RectTransform shirtRect = (RectTransform)ui.ShirtButtonsForVerification[0].transform;
        Require(topRect.position.y > shirtRect.position.y, "TOP row is not above the colour row");
        tops[1].onClick.Invoke();
        yield return null;
        Require(service.State.top == "Oversized", $"clicking OVERSIZED wore {service.State.top}");
        service.CloseLockerMenuFromUi();
        notes.Add("locker=ok");

        // ---- every top x colour
        progressionHold(service);
        foreach (GymTopStyle top in Enum.GetValues(typeof(GymTopStyle)))
        {
            foreach (GymShirtColor colour in Enum.GetValues(typeof(GymShirtColor)))
            {
                service.EquipTop(top);
                service.EquipShirt(colour);
                limit = Time.realtimeSinceStartup + 10f;
                while ((!loadout.IsShirtVisualReady || loadout.IsTopShellPending) &&
                    Time.realtimeSinceStartup < limit) yield return null;
                yield return null;
                Require(loadout.CurrentTop == top && loadout.CurrentShirt == colour,
                    $"loadout wears {loadout.CurrentTop}/{loadout.CurrentShirt}, wanted {top}/{colour}");
                string expected = Path.GetFileName(PlayerCosmeticLoadout.GetOutfitTexturePath(top, colour));
                Require(loadout.CurrentTexture != null && loadout.CurrentTexture.name == expected,
                    $"{top}/{colour} texture {loadout.CurrentTexture?.name}, wanted {expected}");
                SkinnedMeshRenderer bodyRenderer = FindBody(rig);
                // The tank top paints the body; the other tops keep the
                // scan's skin on the body (no colour under the sleeves).
                Texture onBody = bodyRenderer.sharedMaterial.GetTexture("_BaseMap");
                string bodyExpected = top == GymTopStyle.TankTop ? expected : "under_shell_body";
                Require(onBody != null && onBody.name == bodyExpected,
                    $"{top}/{colour}: body material shows {onBody?.name}, wanted {bodyExpected}");
                SkinnedMeshRenderer shell = loadout.TopShellRenderer;
                if (top != GymTopStyle.TankTop)
                {
                    Require(shell != null && shell.name == (top == GymTopStyle.Oversized
                        ? PlayerTopShell.ShellObjectName : PlayerTopShell.OnyxObjectName),
                        $"{top} {colour}: no cloth shell ({shell?.name})");
                    Require(shell.bones.Length == bodyRenderer.bones.Length && shell.rootBone == bodyRenderer.rootBone,
                        "shell does not share the body's bones");
                    Require(shell.gameObject.layer == bodyRenderer.gameObject.layer, "shell layer differs from the body");
                    Require(shell.sharedMesh.vertexCount > 5000, $"shell has {shell.sharedMesh.vertexCount} vertices");
                    Require(shell.sharedMaterials.Length == 2 &&
                        shell.sharedMaterials[0].GetTexture("_BaseMap") == loadout.CurrentTexture, "shell texture differs");
                    Require(shell.sharedMesh.subMeshCount == 2, "shell has no edge submesh");
                }
                else
                {
                    Require(shell == null, $"{top} {colour}: a cloth shell is still shown");
                }
            }
        }
        notes.Add("looks=15");

        // ---- shell fit over animation
        service.EquipTop(GymTopStyle.Oversized);
        service.EquipShirt(GymShirtColor.Black);
        yield return WaitShell(loadout);
        foreach (string clip in new[] { "idle1", "walking", "running", "punch_left" })
        {
            rig.HoldStablePoseForVerification(clip, 0.45f, out _);
            yield return null;
            MeasureFit(FindBody(rig), loadout.TopShellRenderer, clip, 0.004f, 0.06f);
        }
        service.EquipTop(GymTopStyle.Onyx);
        yield return WaitShell(loadout);
        foreach (string clip in new[] { "idle1", "running" })
        {
            rig.HoldStablePoseForVerification(clip, 0.45f, out _);
            yield return null;
            MeasureFit(FindBody(rig), loadout.TopShellRenderer, "onyx_" + clip, 0.0004f, 0.012f);
        }

        // ---- shell geometry for offline inspection (rest pose OBJ)
        ExportShell(loadout, GymTopStyle.Onyx, "onyx");
        service.EquipTop(GymTopStyle.Oversized);
        yield return WaitShell(loadout);
        ExportShell(loadout, GymTopStyle.Oversized, "oversized");

        // ---- evidence
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            yield return CaptureLooks(player, rig, loadout, service);
        }
        notes.Add($"shells={PlayerTopShell.CachedShellCount}");
    }

    private static void ExportShell(PlayerCosmeticLoadout loadout, GymTopStyle top, string name)
    {
        SkinnedMeshRenderer shell = loadout.TopShellRenderer;
        if (shell == null || shell.sharedMesh == null) return;
        Mesh mesh = shell.sharedMesh;
        System.Text.StringBuilder obj = new System.Text.StringBuilder();
        foreach (Vector3 v in mesh.vertices)
            obj.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "v {0} {1} {2}\n", v.x * 100f, v.y * 100f, v.z * 100f);
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            obj.Append("g sub").Append(sub).Append('\n');
            int[] tris = mesh.GetTriangles(sub);
            for (int i = 0; i < tris.Length; i += 3)
                obj.Append("f ").Append(tris[i] + 1).Append(' ').Append(tris[i + 1] + 1).Append(' ').Append(tris[i + 2] + 1).Append('\n');
        }
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "tops");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "shell_" + name + ".obj"), obj.ToString());
    }

    private static IEnumerator WaitShell(PlayerCosmeticLoadout loadout)
    {
        float limit = Time.realtimeSinceStartup + 10f;
        yield return null;
        while (loadout.IsTopShellPending && Time.realtimeSinceStartup < limit) yield return null;
        yield return null;
    }

    private static void progressionHold(GymExperienceService service)
    {
        // Keep the reward HUD and autosave quiet while outfits change.
        service.State.lockerPrepCompleted = true;
    }

    private static SkinnedMeshRenderer FindBody(PlayerHandRig rig)
    {
        SkinnedMeshRenderer best = null;
        foreach (SkinnedMeshRenderer renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.gameObject.layer == PlanarGymMirror.FirstPersonPlayerLayer ||
                PlayerTopShell.IsShellObject(renderer.name) || renderer.sharedMesh == null) continue;
            if (best == null || renderer.sharedMesh.vertexCount > best.sharedMesh.vertexCount) best = renderer;
        }
        return best;
    }

    /// <summary>Shell vertices sit outside the body: nearest body vertex is a
    /// few cm away on average and the shell is not sunk into the torso.</summary>
    private void MeasureFit(SkinnedMeshRenderer body, SkinnedMeshRenderer shell, string clip, float minGap, float maxGap)
    {
        Require(body != null && shell != null, "fit: missing renderers");
        Mesh bodyMesh = new Mesh();
        Mesh shellMesh = new Mesh();
        body.BakeMesh(bodyMesh, true);
        shell.BakeMesh(shellMesh, true);
        Vector3[] bv = bodyMesh.vertices;
        Vector3[] sv = shellMesh.vertices;
        Matrix4x4 bm = body.transform.localToWorldMatrix;
        Matrix4x4 sm = shell.transform.localToWorldMatrix;
        Vector3[] bodyWorld = new Vector3[bv.Length / 3 + 1];
        int bodyCount = 0;
        for (int i = 0; i < bv.Length; i += 3) bodyWorld[bodyCount++] = bm.MultiplyPoint3x4(bv[i]);
        Bounds shellBounds = new Bounds(sm.MultiplyPoint3x4(sv[0]), Vector3.zero);
        double sum = 0d;
        int samples = 0;
        float worst = 0f;
        int worstIndex = -1;
        int step = Mathf.Max(1, sv.Length / 400);
        for (int i = 0; i < sv.Length; i++)
        {
            Vector3 p = sm.MultiplyPoint3x4(sv[i]);
            shellBounds.Encapsulate(p);
            if (i % step != 0) continue;
            float best = float.MaxValue;
            for (int k = 0; k < bodyCount; k++)
            {
                float d = (bodyWorld[k] - p).sqrMagnitude;
                if (d < best) best = d;
            }
            best = Mathf.Sqrt(best);
            sum += best;
            if (best > worst) { worst = best; worstIndex = i; }
            samples++;
        }
        float mean = (float)(sum / Math.Max(1, samples));
        float height = body.bounds.size.y;
        UnityEngine.Object.Destroy(bodyMesh);
        UnityEngine.Object.Destroy(shellMesh);
        // Typical gap 1-6 % of body height; no shell part drifting off.
        Require(mean > height * minGap && mean < height * maxGap,
            $"{clip}: shell mean gap {mean:F4} for body height {height:F2} bodyScale={body.transform.lossyScale} " +
            $"shellScale={shell.transform.lossyScale} shellBounds={shellBounds.size} bodyBounds={body.bounds.size} worst={worst:F4}");
        if (worst >= height * 0.12f)
        {
            BoneWeight w = shell.sharedMesh.boneWeights[worstIndex];
            throw new InvalidOperationException($"{clip}: shell vertex {worstIndex} {worst:F3} from the body " +
                $"rest={shell.sharedMesh.vertices[worstIndex]} weights={w.boneIndex0}:{w.weight0:F2} " +
                $"{w.boneIndex1}:{w.weight1:F2} {w.boneIndex2}:{w.weight2:F2} {w.boneIndex3}:{w.weight3:F2} " +
                $"baked={sm.MultiplyPoint3x4(sv[worstIndex])} bodyCentre={body.bounds.center} quality={shell.quality}/{body.quality}");
        }
        Require(body.bounds.Intersects(shellBounds), $"{clip}: shell left the body");
        notes.Add($"{clip}Gap={mean / height:F3}h");
    }

    private IEnumerator CaptureLooks(PlayerMovement player, PlayerHandRig rig,
        PlayerCosmeticLoadout loadout, GymExperienceService service)
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "tops");
        Directory.CreateDirectory(folder);
        Camera camera = new GameObject("Tops evidence camera").AddComponent<Camera>();
        camera.cullingMask = 1 << PlanarGymMirror.MirrorPlayerLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.25f, 0.28f, 0.31f);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 20f;
        camera.orthographic = true;
        camera.orthographicSize = 0.62f;
        GameObject lightObject = new GameObject("Tops evidence light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        (GymTopStyle, GymShirtColor)[] looks =
        {
            (GymTopStyle.TankTop, GymShirtColor.Black), (GymTopStyle.Oversized, GymShirtColor.Black),
            (GymTopStyle.Oversized, GymShirtColor.White), (GymTopStyle.Oversized, GymShirtColor.Red),
            (GymTopStyle.Onyx, GymShirtColor.Black), (GymTopStyle.Onyx, GymShirtColor.Blue),
            (GymTopStyle.Onyx, GymShirtColor.Gold)
        };
        foreach ((GymTopStyle top, GymShirtColor colour) in looks)
        {
            service.EquipTop(top);
            service.EquipShirt(colour);
            yield return WaitShell(loadout);
            rig.HoldStablePoseForVerification("idle1", 0f, out _);
            yield return null;
            SkinnedMeshRenderer body = FindBody(rig);
            Vector3 chest = body.bounds.center + Vector3.up * body.bounds.size.y * 0.18f;
            foreach ((string side, Vector3 direction) in new[] { ("front", player.transform.forward), ("back", -player.transform.forward),
                ("side", (player.transform.forward + player.transform.right).normalized) })
            {
                camera.transform.position = chest + direction * 3f;
                camera.transform.LookAt(chest);
                light.transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * 0.6f + player.transform.right * 0.3f);
                Capture(camera, Path.Combine(folder, $"{top}_{colour}_{side}.png".ToLowerInvariant()));
            }
        }
        Destroy(lightObject);
        Destroy(camera.gameObject);
    }

    private static void Capture(Camera camera, string path)
    {
        RenderTexture rt = new RenderTexture(512, 640, 24);
        RenderTexture old = RenderTexture.active;
        camera.targetTexture = rt;
        camera.aspect = 512f / 640f;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(512, 640, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 512, 640), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = old;
        Destroy(image);
        Destroy(rt);
    }
}
#endif
