#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
/// - Locker: a TOP row with the three named buttons under the TOP label,
///   whose colour swatches sit on the label row; a BOTTOMS row (SHORTS,
///   PANTS) with its own swatches. Clicking a button or swatch equips it.
/// - Bottoms: Shorts and Pants are skinned shells tinted with the bottoms
///   colour; pants reach the ankles, shorts stop above the knee.
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
        GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);
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

    /// <summary>
    /// How far the crest print stands off the cloth it was lifted from, in mesh
    /// space. A screen print is a film of ink on the fabric; a badge is not.
    /// </summary>
    /// <summary>
    /// Mean dot between the mesh's own normals and the normals Unity derives
    /// from its triangle winding. A negative value means the cloth is lit from
    /// the inside: black under a lit shader while the unlit print still shows.
    /// </summary>
    private static float WindingAgreement(Mesh mesh)
    {
        Mesh probe = new Mesh { indexFormat = mesh.indexFormat };
        probe.vertices = mesh.vertices;
        probe.subMeshCount = mesh.subMeshCount;
        for (int sub = 0; sub < mesh.subMeshCount; sub++) probe.SetTriangles(mesh.GetTriangles(sub), sub);
        probe.RecalculateNormals();
        Vector3[] ours = mesh.normals, auto = probe.normals;
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < ours.Length && i < auto.Length; i += 7)
        {
            sum += Vector3.Dot(ours[i], auto[i]);
            count++;
        }
        UnityEngine.Object.DestroyImmediate(probe);
        return count > 0 ? sum / count : 0f;
    }

    /// <summary>Mean magnitude of the mesh's own normals.</summary>
    private static float NormalMean(Mesh mesh)
    {
        Vector3[] normals = mesh.normals;
        float sum = 0f;
        for (int i = 0; i < normals.Length; i++) sum += normals[i].magnitude;
        return normals.Length > 0 ? sum / normals.Length : 0f;
    }

    private static float MaxCrestLift(Mesh mesh)
    {
        Vector3[] points = mesh.vertices;
        int[] fabric = mesh.GetTriangles(0);
        int[] crest = mesh.GetTriangles(2);
        float worst = 0f;
        // Every eighth print vertex is enough to catch a print that floats, and
        // keeps the triangle sweep quick. Distance is to the cloth surface, not
        // to its vertices: a print sits between them.
        for (int t = 0; t < crest.Length; t += 24)
        {
            Vector3 print = points[crest[t]];
            float nearest = float.MaxValue;
            for (int f = 0; f < fabric.Length; f += 3)
            {
                Vector3 a = points[fabric[f]], b = points[fabric[f + 1]], c = points[fabric[f + 2]];
                if ((a - print).sqrMagnitude > 0.01f && (b - print).sqrMagnitude > 0.01f &&
                    (c - print).sqrMagnitude > 0.01f) continue;
                nearest = Mathf.Min(nearest, (ClosestOnTriangle(print, a, b, c) - print).magnitude);
            }
            worst = Mathf.Max(worst, nearest);
        }
        return worst;
    }

    /// <summary>Closest point to p on the triangle abc, in the triangle's plane.</summary>
    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>Depth-first child lookup.</summary>
    private static Transform FindChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name) return child;
            Transform found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
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
        Require(shirtRect.position.y > topRect.position.y && shirtRect.position.x > topRect.position.x,
            "TOP colour swatches are not on the TOP label row");
        tops[1].onClick.Invoke();
        yield return null;
        Require(service.State.top == "Oversized", $"clicking OVERSIZED wore {service.State.top}");
        Button[] bottoms = ui.BottomButtonsForVerification;
        Require(bottoms.Length == 2 && bottoms[0].GetComponentInChildren<Text>().text == "SHORTS" &&
            bottoms[1].GetComponentInChildren<Text>().text == "PANTS", "BOTTOMS row is not SHORTS / PANTS");
        Require(((RectTransform)bottoms[0].transform).position.y < topRect.position.y, "BOTTOMS row is not under TOP");
        bottoms[1].onClick.Invoke();
        int white = Array.IndexOf(GymExperienceService.LockerShirts.ToArray(), GymShirtColor.White);
        ui.BottomColorButtonsForVerification[white].onClick.Invoke();
        yield return null;
        Require(service.State.bottom == "Pants" && service.State.bottomColor == "White" &&
            GymLockerMenuUI.IsSwatchSelectedForVerification(ui.BottomColorButtonsForVerification[white]),
            $"BOTTOMS clicks wore {service.State.bottom}/{service.State.bottomColor}");
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
                    Require(shell.sharedMaterials.Length == shell.sharedMesh.subMeshCount,
                        "shell submeshes and materials disagree");
                    Texture shellMap = shell.sharedMaterials[0].GetTexture("_BaseMap");
                    if (top == GymTopStyle.Oversized)
                    {
                        // The tee is lofted from the body: its own UVs on a
                        // tiling jersey instead of the scan atlas. The crown is
                        // shaped cloth in submesh 2, printed in flat ink.
                        Require(shell.sharedMesh.subMeshCount == 3, "oversized tee has no crest submesh");
                        Require(shellMap != null && shellMap.name == "jersey_heavy",
                            $"oversized {colour}: shell fabric {shellMap?.name}");
                        int[] crestFaces = shell.sharedMesh.GetTriangles(2);
                        Require(crestFaces.Length >= 24, "oversized: the crest has no faces");
                        // A print is ink on cloth, not a badge: unlit, so it keeps
                        // its own colour whatever the light on the fabric is.
                        Material crestInk = shell.sharedMaterials[2];
                        Require(crestInk != null && crestInk.shader != null &&
                            crestInk.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0,
                            $"oversized: the crest is drawn by {crestInk?.shader?.name}, not a print shader");
                        // The crown is white on every shirt colour except the
                        // white tee, where white ink would vanish and it prints
                        // black.
                        bool whiteTee = colour == GymShirtColor.White;
                        Require(crestInk != null && (whiteTee
                                ? crestInk.color.grayscale < 0.4f
                                : crestInk.color.grayscale > 0.6f),
                            $"oversized {colour}: crest ink {crestInk?.color} is not white ink for this cloth");
                        // The rest of the shirt is the colour the player picked.
                        // The material carries the tint, so the fabric grey comes
                        // back out of it.
                        Color clothTint = shell.sharedMaterials[0].color;
                        Color cloth = clothTint * PlayerTopShell.BottomsFabricLevel;
                        Color wanted = PlayerCosmeticLoadout.GetSwatchColor(colour);
                        float off = Vector3.Distance(new Vector3(cloth.r, cloth.g, cloth.b),
                            new Vector3(wanted.r, wanted.g, wanted.b));
                        Require(off < 0.12f,
                            $"oversized {colour}: the cloth is {cloth}, not the selected colour {wanted}");
                        // Cloth with no normals, or normals facing into the
                        // shirt, shades black under every lit shader whatever
                        // colour it is: they have to agree with the winding the
                        // renderer actually draws.
                        float normalMean = NormalMean(shell.sharedMesh);
                        float agreement = WindingAgreement(shell.sharedMesh);
                        Require(normalMean > 0.5f && agreement > 0.3f,
                            $"oversized {colour}: cloth normals are wrong (mean={normalMean:F3} agreement={agreement:F3})");
                        // Cloth motion: the hem and cuffs trail a moving body, come
                        // back onto it when it stops, and never swing into it.
                        PlayerTopShellCloth clothMotion = shell.GetComponent<PlayerTopShellCloth>();
                        Require(clothMotion != null && clothMotion.IsConfigured,
                            $"oversized {colour}: the tee has no cloth motion");
                        clothMotion.ResetForVerification();
                        for (int step = 0; step < 60; step++)
                            clothMotion.AdvanceForVerification(player.transform.forward * 3.2f, 1f / 60f);
                        float trail = clothMotion.CurrentLag;
                        Require(trail > 0.004f, $"oversized: the cloth does not trail the body ({trail:F4} m)");
                        Require(trail <= PlayerTopShellCloth.HemMaxLag + 1e-4f,
                            $"oversized: the cloth trails {trail:F4} m, further than it may");
                        Require(clothMotion.InwardDepthForVerification < 0.0005f,
                            $"oversized: the cloth swings {clothMotion.InwardDepthForVerification:F4} m into the body");
                        for (int step = 0; step < 180; step++)
                            clothMotion.AdvanceForVerification(Vector3.zero, 1f / 60f);
                        Require(clothMotion.CurrentLag < 0.001f,
                            $"oversized: the cloth does not settle back onto the body ({clothMotion.CurrentLag:F4} m)");
                        notes.Add($"clothTrail={trail:F4} settle={clothMotion.CurrentLag:F4}");
                        clothMotion.ResetForVerification();
                        if (colour == GymShirtColor.Black)
                        {
                            // The print has to lie on the cloth, not float in
                            // front of it: measure it against the fabric it was
                            // lifted from, in the mesh's own space.
                            float hug = MaxCrestLift(shell.sharedMesh);
                            Require(hug <= 0.006f,
                                $"oversized: the crest floats {hug:F4} off the cloth");
                        }
                    }
                    else
                    {
                        Require(shell.sharedMesh.subMeshCount == 2, "shell has no edge submesh");
                        Require(shellMap == loadout.CurrentTexture, "shell texture differs");
                    }
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

        // ---- bottoms: both cuts in every colour, tinted on the shell
        foreach (GymBottomStyle bottom in GymExperienceService.LockerBottoms)
        {
            Color previousTint = Color.clear;
            foreach (GymShirtColor colour in Enum.GetValues(typeof(GymShirtColor)))
            {
                service.EquipBottom(bottom);
                service.EquipBottomColor(colour);
                limit = Time.realtimeSinceStartup + 15f;
                yield return null;
                while (loadout.IsBottomShellPending && Time.realtimeSinceStartup < limit) yield return null;
                yield return null;
                SkinnedMeshRenderer bottomShell = loadout.BottomShellRenderer;
                string expectedName = bottom == GymBottomStyle.Pants
                    ? PlayerTopShell.PantsObjectName : PlayerTopShell.ShortsObjectName;
                Require(bottomShell != null && bottomShell.name == expectedName,
                    $"{bottom} {colour}: shell {bottomShell?.name ?? "missing"}");
                Color tint = bottomShell.sharedMaterials[0].GetColor("_BaseColor");
                Require(loadout.CurrentBottomColor == colour && tint != previousTint,
                    $"{bottom} {colour}: tint {tint} did not change");
                previousTint = tint;
            }
        }
        SkinnedMeshRenderer legs = FindBody(rig);
        float bodyBottom = legs.bounds.min.y;
        float bodyHeight = legs.bounds.size.y;
        service.EquipBottom(GymBottomStyle.Pants);
        yield return WaitBottom(loadout);
        float pantsLow = loadout.BottomShellRenderer.bounds.min.y;
        ExportShell(loadout.BottomShellRenderer, "pants");
        foreach (string clip in new[] { "idle1", "running" })
        {
            rig.HoldStablePoseForVerification(clip, 0.45f, out _);
            yield return null;
            MeasureFit(legs, loadout.BottomShellRenderer, "pants_" + clip, 0.006f, 0.03f);
        }
        // Striding legs must not pull a web of cloth between them (a leg's
        // inner side skinned to the other leg did).
        foreach (string clip in new[] { "walking", "running" })
        {
            foreach (float phase in new[] { 0.2f, 0.45f, 0.7f })
            {
                rig.HoldStablePoseForVerification(clip, phase, out _);
                yield return null;
                MeasureStretch(legs, loadout.BottomShellRenderer, $"pants_{clip}_{phase:F2}");
            }
        }
        MeasureShape(loadout.BottomShellRenderer, "pants", true);
        MeasurePantsFabric(loadout.BottomShellRenderer);
        // The side stripes: ink on both outer seams, white on every colour of
        // trousers and black on the white pair.
        service.EquipBottomColor(GymShirtColor.Blue);
        yield return WaitBottom(loadout);
        MeasurePantsStripe(loadout.BottomShellRenderer, GymShirtColor.Blue);
        MeasurePantsStripeFit(loadout.BottomShellRenderer);
        service.EquipBottomColor(GymShirtColor.White);
        yield return WaitBottom(loadout);
        MeasurePantsStripe(loadout.BottomShellRenderer, GymShirtColor.White);
        service.EquipBottomColor(GymShirtColor.Blue);
        yield return WaitBottom(loadout);
        service.EquipBottom(GymBottomStyle.Shorts);
        yield return WaitBottom(loadout);
        float shortsLow = loadout.BottomShellRenderer.bounds.min.y;
        ExportShell(loadout.BottomShellRenderer, "shorts");
        rig.HoldStablePoseForVerification("idle1", 0.45f, out _);
        yield return null;
        MeasureFit(legs, loadout.BottomShellRenderer, "shorts_idle1", 0.0004f, 0.02f);
        Require(pantsLow < bodyBottom + bodyHeight * 0.12f && shortsLow > bodyBottom + bodyHeight * 0.25f,
            $"pants reach {(pantsLow - bodyBottom) / bodyHeight:F2}h, shorts {(shortsLow - bodyBottom) / bodyHeight:F2}h");
        notes.Add($"bottoms=10 pantsLow={(pantsLow - bodyBottom) / bodyHeight:F2}h shortsLow={(shortsLow - bodyBottom) / bodyHeight:F2}h");

        // ---- shell geometry for offline inspection (rest pose OBJ)
        ExportShell(loadout, GymTopStyle.Onyx, "onyx");
        service.EquipTop(GymTopStyle.Oversized);
        yield return WaitShell(loadout);
        ExportShell(loadout, GymTopStyle.Oversized, "oversized");
        MeasureShape(loadout.TopShellRenderer, "oversized", false);
        MeasureTee(loadout.TopShellRenderer);

        // ---- evidence
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            yield return CaptureLooks(player, rig, loadout, service);
        }
        notes.Add($"shells={PlayerTopShell.CachedShellCount}");
    }

    private static void ExportShell(PlayerCosmeticLoadout loadout, GymTopStyle top, string name) =>
        ExportShell(loadout.TopShellRenderer, name);

    private static void ExportShell(SkinnedMeshRenderer shell, string name)
    {
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

    private static IEnumerator WaitBottom(PlayerCosmeticLoadout loadout)
    {
        float limit = Time.realtimeSinceStartup + 15f;
        yield return null;
        while (loadout.IsBottomShellPending && Time.realtimeSinceStartup < limit) yield return null;
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
    /// <summary>No baked cloth edge longer than 5 % of the body height: a web
    /// between the legs stretches edges across the gap.</summary>
    private void MeasureStretch(SkinnedMeshRenderer body, SkinnedMeshRenderer shell, string label)
    {
        Mesh baked = new Mesh();
        shell.BakeMesh(baked, true);
        Vector3[] v = baked.vertices;
        Matrix4x4 m = shell.transform.localToWorldMatrix;
        for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
        float height = body.bounds.size.y;
        float longest = 0f;
        int from = -1, to = -1;
        for (int sub = 0; sub < baked.subMeshCount; sub++)
        {
            int[] t = baked.GetTriangles(sub);
            for (int i = 0; i < t.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = t[i + k], b = t[i + (k + 1) % 3];
                    float d = (v[a] - v[b]).magnitude;
                    if (d > longest) { longest = d; from = a; to = b; }
                }
            }
        }
        UnityEngine.Object.Destroy(baked);
        float bottom = body.bounds.min.y;
        Require(longest < height * 0.05f, $"{label}: cloth edge {longest / height:F3}h long (web between the legs) " +
            $"vertices {from}@{(v[from].y - bottom) / height:F2}h-{to}@{(v[to].y - bottom) / height:F2}h of {v.Length} " +
            $"bones {Bones(shell, from)} / {Bones(shell, to)}");
        notes.Add($"{label}Edge={longest / height:F3}h");
    }

    private static string Bones(SkinnedMeshRenderer shell, int vertex)
    {
        BoneWeight w = shell.sharedMesh.boneWeights[vertex];
        string Name(int i) => i >= 0 && i < shell.bones.Length && shell.bones[i] != null ? shell.bones[i].name : "?";
        return $"{Name(w.boneIndex0)}:{w.weight0:F2},{Name(w.boneIndex1)}:{w.weight1:F2}";
    }

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
                $"bones={Bones(shell, worstIndex)} " +
                $"rest={shell.sharedMesh.vertices[worstIndex].ToString("F4")} weights={w.boneIndex0}:{w.weight0:F2} " +
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
            if (top == GymTopStyle.Oversized && colour == GymShirtColor.Black)
            {
                // The print has to show on the chest: hide the body so a crest
                // that ends up inside the skin cannot hide behind it.
                body.enabled = false;
                camera.orthographicSize = 0.24f;
                camera.transform.position = chest + player.transform.forward * 3f;
                camera.transform.LookAt(chest + player.transform.forward * 0.12f);
                light.transform.rotation = Quaternion.LookRotation(
                    -player.transform.forward + Vector3.down * 0.6f + player.transform.right * 0.3f);
                Capture(camera, Path.Combine(folder, "oversized_crest.png"));
                camera.orthographicSize = 0.62f;
                body.enabled = true;
                // Crew neck over the traps: the collar has to sit on the
                // shoulders without a ledge, a notch or a strip of skin.
                camera.orthographicSize = 0.30f;
                camera.transform.position = chest + player.transform.forward * 3f + Vector3.up * 0.24f;
                camera.transform.LookAt(chest + player.transform.forward * 0.12f + Vector3.up * 0.24f);
                Capture(camera, Path.Combine(folder, "oversized_neck.png"));
                // Shoulder and armhole seam: no sliver of skin between the
                // sleeve and the torso, and no ridge where the sleeve leaves it.
                camera.orthographicSize = 0.34f;
                Vector3 shoulderAim = chest + Vector3.up * 0.16f;
                Vector3 shoulderFrom = (player.transform.forward + player.transform.right * 0.75f).normalized;
                camera.transform.position = shoulderAim + shoulderFrom * 3f;
                camera.transform.LookAt(shoulderAim);
                Capture(camera, Path.Combine(folder, "oversized_shoulder.png"));
                // The underside of the armhole from behind and below: the
                // triceps must not show between the sleeve and the torso.
                camera.orthographicSize = 0.30f;
                Vector3 pitAim = chest + Vector3.down * 0.10f;
                Vector3 pitFrom = (-player.transform.forward + player.transform.right * 0.6f).normalized;
                camera.transform.position = pitAim + pitFrom * 3f + Vector3.down * 0.35f;
                camera.transform.LookAt(pitAim);
                Capture(camera, Path.Combine(folder, "oversized_armpit.png"));
                camera.orthographicSize = 0.62f;
                // The cloth motion sheet: the hem trailing a run, from the side.
                PlayerTopShellCloth motion = loadout.TopShellRenderer != null
                    ? loadout.TopShellRenderer.GetComponent<PlayerTopShellCloth>() : null;
                if (motion != null)
                {
                    motion.ResetForVerification();
                    for (int step = 0; step < 60; step++)
                        motion.AdvanceForVerification(player.transform.forward * 3.2f, 1f / 60f);
                    camera.orthographicSize = 0.72f;
                    camera.transform.position = chest + player.transform.right * 3f;
                    camera.transform.LookAt(chest - Vector3.up * 0.08f);
                    Capture(camera, Path.Combine(folder, "oversized_cloth_lag.png"));
                    camera.orthographicSize = 0.62f;
                    motion.ResetForVerification();
                }
            }
        }
        // Pants standing and mid-stride: nothing may hang between the legs.
        service.EquipTop(GymTopStyle.TankTop);
        service.EquipBottom(GymBottomStyle.Pants);
        camera.orthographicSize = 0.8f;
        foreach (GymShirtColor colour in new[] { GymShirtColor.Black, GymShirtColor.Blue, GymShirtColor.White })
        {
            service.EquipBottomColor(colour);
            yield return WaitBottom(loadout);
            foreach ((string clip, float phase) in new[] { ("idle1", 0f), ("walking", 0.2f), ("running", 0.45f) })
            {
                rig.HoldStablePoseForVerification(clip, phase, out _);
                yield return null;
                SkinnedMeshRenderer body = FindBody(rig);
                Vector3 hips = body.bounds.center - Vector3.up * body.bounds.size.y * 0.2f;
                foreach ((string side, Vector3 direction) in new[] { ("front", player.transform.forward), ("back", -player.transform.forward),
                    ("side", player.transform.right) })
                {
                    camera.transform.position = hips + direction * 3f;
                    camera.transform.LookAt(hips);
                    light.transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * 0.6f + player.transform.right * 0.3f);
                    Capture(camera, Path.Combine(folder, $"pants_{colour}_{clip}_{side}.png".ToLowerInvariant()));
                }
                if (colour == GymShirtColor.Blue && clip == "idle1")
                {
                    // Close enough to see the knit and the waistband rib as two
                    // different cloths rather than one flat colour.
                    camera.orthographicSize = 0.15f;
                    camera.transform.position = hips + player.transform.right * 2f;
                    camera.transform.LookAt(hips + Vector3.down * 0.06f);
                    Capture(camera, Path.Combine(folder, "pants_knit_closeup.png"));
                    camera.orthographicSize = 0.22f;
                    Vector3 waist = hips + Vector3.up * 0.16f;
                    camera.transform.position = waist + (player.transform.right + player.transform.forward * 0.4f).normalized * 2f;
                    camera.transform.LookAt(waist);
                    Capture(camera, Path.Combine(folder, "pants_waistband_closeup.png"));
                    // The inside of the thigh from below: the seat's edge must
                    // not surface there.
                    camera.orthographicSize = 0.20f;
                    Vector3 inner = hips + Vector3.down * 0.04f;
                    camera.transform.position = inner + Vector3.down * 0.5f +
                        (player.transform.forward * 0.6f + player.transform.right * 0.8f).normalized * 2f;
                    camera.transform.LookAt(inner);
                    Capture(camera, Path.Combine(folder, "pants_inner_thigh.png"));
                    camera.orthographicSize = 0.8f;
                }
            }
        }
        service.EquipBottom(GymBottomStyle.Shorts);
        Destroy(lightObject);
        Destroy(camera.gameObject);
    }

    /// <summary>Shape gates: no interior holes in the fabric submesh, an
    /// A-line silhouette (no balloon from thigh to hem) and no sharp creases
    /// from a stepped radius. Edges are sorted, not hashed: the editor asmdef
    /// does not always carry System.Collections.Generic into this file.</summary>
    /// <summary>The cloth tile a material samples, whichever slot its shader uses.</summary>
    private static Texture TileOf(Material material)
    {
        if (material == null) return null;
        if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) return material.GetTexture("_BaseMap");
        if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null) return material.GetTexture("_MainTex");
        return null;
    }

    /// <summary>The side stripes hang on the leg cloth, one on each outer side,
    /// symmetric and down to the hem. A stripe skinned from the body instead
    /// pulled off the leg in a stride and trailed through the air like a wire.</summary>
    private void MeasurePantsStripeFit(SkinnedMeshRenderer shell)
    {
        Mesh mesh = shell.sharedMesh;
        int[] stripe = mesh.GetTriangles(2);
        Require(stripe.Length > 0, "pants: the stripe submesh is empty");
        Vector3[] v = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        bool[] isStripe = new bool[v.Length];
        foreach (int index in stripe) isStripe[index] = true;
        float lo = mesh.bounds.min.z, hi = mesh.bounds.max.z;
        float leftLow = float.MaxValue, rightLow = float.MaxValue;
        float leftOut = 0f, rightOut = 0f;
        int leftCount = 0, rightCount = 0;
        float drift = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            if (!isStripe[i]) continue;
            if (v[i].x < 0f)
            {
                leftLow = Mathf.Min(leftLow, v[i].z);
                leftOut = Mathf.Max(leftOut, -v[i].x);
                leftCount++;
            }
            else
            {
                rightLow = Mathf.Min(rightLow, v[i].z);
                rightOut = Mathf.Max(rightOut, v[i].x);
                rightCount++;
            }
            int nearest = -1;
            float best = float.MaxValue;
            for (int j = 0; j < v.Length; j++)
            {
                if (isStripe[j]) continue;
                float d = (v[j] - v[i]).sqrMagnitude;
                if (d < best) { best = d; nearest = j; }
            }
            Require(nearest >= 0, "pants: no cloth under a stripe vertex");
            drift = Mathf.Max(drift,
                Mathf.Abs(weights[i].weight0 - weights[nearest].weight0) +
                Mathf.Abs(weights[i].weight1 - weights[nearest].weight1) +
                Mathf.Abs(weights[i].weight2 - weights[nearest].weight2) +
                Mathf.Abs(weights[i].weight3 - weights[nearest].weight3) +
                (weights[i].boneIndex0 != weights[nearest].boneIndex0 ? 1f : 0f));
        }
        Require(leftCount > 0 && rightCount > 0, $"pants: stripes on one leg only ({leftCount}/{rightCount})");
        Require(drift < 0.02f, $"pants: a stripe is not skinned with the leg under it (drift {drift:F3})");
        Require(leftLow < lo + (hi - lo) * 0.02f && rightLow < lo + (hi - lo) * 0.02f,
            $"pants: stripes stop short of the hem ({leftLow:F4}/{rightLow:F4} against {lo:F4})");
        Require(Mathf.Abs(leftOut - rightOut) < (hi - lo) * 0.02f,
            $"pants: stripes are not symmetric (x {leftOut:F4} against {rightOut:F4})");
        notes.Add($"pantsStripeFit verts={leftCount}/{rightCount} hem={leftLow:F4}/{rightLow:F4} drift={drift:F3}");
    }

    /// <summary>The trousers read as track knit with a ribbed waistband: the
    /// legs and the band must carry different tiles, or the band is just more
    /// trouser and there is no waistband at all.</summary>
    private void MeasurePantsFabric(SkinnedMeshRenderer shell)
    {
        Require(shell != null && shell.sharedMaterials.Length > 1, "pants: no band material");
        Material clothMaterial = shell.sharedMaterials[0];
        Material bandMaterial = shell.sharedMaterials[1];
        Texture cloth = TileOf(clothMaterial);
        Texture band = TileOf(bandMaterial);
        notes.Add($"pantsBand shader={bandMaterial.shader.name} base={bandMaterial.HasProperty("_BaseMap")} " +
            $"main={bandMaterial.HasProperty("_MainTex")}");
        Require(cloth != null && cloth.name.Contains("track_knit"),
            $"pants: legs wear {cloth?.name ?? "no tile"}, not the track knit");
        Require(band != null && band.name.Contains("rib_waistband"),
            $"pants: waistband wears {band?.name ?? "no tile"}, not the rib");
        Require(cloth != band, "pants: the waistband is wearing the trouser's own knit");
        Require(cloth.width >= 512 && band.width >= 256,
            $"pants: tiles are {cloth.width} and {band.width} texels");
        notes.Add($"pantsFabric cloth={cloth.name} {cloth.width} band={band.name} {band.width}");
    }

    /// <summary>Side stripes: a third, ink-printed submesh down both outer
    /// seams, white on every colour of trousers and black on the white pair.</summary>
    private void MeasurePantsStripe(SkinnedMeshRenderer shell, GymShirtColor colour)
    {
        Require(shell != null && shell.sharedMesh != null, $"pants {colour}: no shell for the side stripe");
        Mesh mesh = shell.sharedMesh;
        Require(mesh.subMeshCount >= 3, $"pants {colour}: {mesh.subMeshCount} submeshes, no side stripe");
        int[] stripe = mesh.GetTriangles(2);
        Require(stripe.Length >= 96, $"pants {colour}: side stripe has {stripe.Length / 3} triangles");
        Color ink = shell.sharedMaterials[2].color;
        bool white = colour == GymShirtColor.White;
        Require(white ? ink.grayscale < 0.45f : ink.grayscale > 0.55f,
            $"pants {colour}: stripe ink {ink} is the wrong ink for these trousers");
        Vector3[] v = mesh.vertices;
        Bounds bounds = mesh.bounds;
        // The pants shell is built in the body's frame: X is left/right and Z
        // runs up the leg (Y is front to back).
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (int i in stripe)
        {
            minX = Mathf.Min(minX, v[i].x);
            maxX = Mathf.Max(maxX, v[i].x);
            minZ = Mathf.Min(minZ, v[i].z);
            maxZ = Mathf.Max(maxZ, v[i].z);
        }
        float half = bounds.size.x * 0.5f;
        Require(minX < bounds.center.x - half * 0.4f && maxX > bounds.center.x + half * 0.4f,
            $"pants {colour}: stripe runs down one leg only (x {minX:F4}..{maxX:F4})");
        Require(maxZ - minZ > bounds.size.z * 0.55f,
            $"pants {colour}: stripe covers {(maxZ - minZ) / bounds.size.z:F2} of the leg");
        notes.Add($"pantsStripe {colour} tris={stripe.Length / 3} ink={ink.grayscale:F2} length={(maxZ - minZ) / bounds.size.z:F2}");
    }

    private void MeasureShape(SkinnedMeshRenderer shell, string label, bool pants)
    {
        Require(shell != null && shell.sharedMesh != null, $"{label}: no shell mesh");
        Mesh mesh = shell.sharedMesh;
        Vector3[] v = mesh.vertices;
        int[] t = mesh.GetTriangles(0);
        Require(t.Length > 0, $"{label}: fabric submesh is empty");
        int faces = t.Length / 3;
        long[] edgeKeys = new long[t.Length];
        int[] edgeFace = new int[t.Length];
        for (int i = 0, f = 0; i < t.Length; i += 3, f++)
            for (int k = 0; k < 3; k++)
            {
                int a = t[i + k], b = t[i + (k + 1) % 3];
                edgeKeys[i + k] = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                edgeFace[i + k] = f;
            }
        int[] order = new int[edgeKeys.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        long[] keys = edgeKeys;
        int[] slot = order;
        Array.Sort((long[])keys.Clone(), slot);
        Vector3[] faceNormals = new Vector3[faces];
        for (int i = 0, f = 0; i < t.Length; i += 3, f++)
            faceNormals[f] = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
        int open = 0;
        float longestOpen = 0f;
        int sharp = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0 && keys[i] == keys[i - 1]) continue;
            int run = 1;
            int last = edgeFace[slot[i]];
            for (int j = i + 1; j < keys.Length && keys[j] == keys[i]; j++)
            {
                if (edgeFace[slot[j]] != last)
                {
                    if (Vector3.Angle(faceNormals[last], faceNormals[edgeFace[slot[j]]]) > 60f) sharp++;
                    last = edgeFace[slot[j]];
                }
                run++;
            }
            if (run == 1)
            {
                open++;
                int a = (int)(keys[i] >> 32), b = (int)(keys[i] & 0xffffffff);
                longestOpen = Mathf.Max(longestOpen, (v[a] - v[b]).magnitude);
            }
        }

        // Silhouette: half widths along the garment, up is +z in this mesh.
        Bounds bounds = mesh.bounds;
        float Height(Vector3 p) => (p.z - bounds.min.z) / Mathf.Max(bounds.size.z, 1e-6f);
        float Width(float level)
        {
            float widest = 0f;
            for (int i = 0; i < v.Length; i++)
                if (Mathf.Abs(Height(v[i]) - level) < 0.05f)
                    widest = Mathf.Max(widest, Mathf.Abs(v[i].x));
            return widest;
        }
        float hip = Width(0.72f), thigh = Width(0.55f), knee = Width(0.35f), hem = Width(0.08f);
        if (pants)
        {
            Require(hem <= thigh * 1.05f && knee <= thigh * 1.02f,
                $"{label}: silhouette balloons (thigh={thigh:F4} knee={knee:F4} hem={hem:F4})");
            // Straight leg: the hem hangs as wide as the thigh, not narrower
            // into a kink above the knee and not bell-shaped over the shoe.
            Require(hem >= thigh * 0.88f && knee >= thigh * 0.90f,
                $"{label}: leg is not straight (thigh={thigh:F4} knee={knee:F4} hem={hem:F4})");
        }
        Require(sharp <= Mathf.Max(8, faces / 400),
            $"{label}: {sharp} sharp creases over 60 deg of {faces} faces");
        notes.Add($"{label}Shape open={open} longestOpen={longestOpen:F4} sharp={sharp} " +
            $"hip={hip:F4} thigh={thigh:F4} knee={knee:F4} hem={hem:F4}");
    }

    /// <summary>
    /// The oversized tee hangs off the body: the hem falls as wide as the
    /// chest and the waist never pinches in under it. A balloon or an A-line
    /// flare outside this band is a different garment than the reference.
    /// </summary>
    private void MeasureTee(SkinnedMeshRenderer shell)
    {
        Require(shell != null && shell.sharedMesh != null, "oversized: no shell mesh");
        Mesh mesh = shell.sharedMesh;
        Vector3[] v = mesh.vertices;
        Bounds bounds = mesh.bounds;
        float height = Mathf.Max(bounds.size.z, 1e-6f);
        float Width(float level)
        {
            float widest = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                float at = (v[i].z - bounds.min.z) / height;
                if (Mathf.Abs(at - level) < 0.06f) widest = Mathf.Max(widest, Mathf.Abs(v[i].x));
            }
            return widest;
        }
        // Levels are fractions of the shell height. 0.72 sits on the sleeves,
        // so it measures the shoulder span; 0.25 and 0.05 are below the cuff
        // and measure the body of the tee alone.
        float hem = Width(0.05f), waist = Width(0.25f), shoulders = Width(0.72f);
        Require(waist > 0f, "oversized: the tee measured no waist width");
        Require(hem >= waist * 0.92f && hem <= waist * 1.15f,
            $"oversized: hem {hem:F4} against waist {waist:F4} is not a boxy fall");
        Require(shoulders >= hem * 1.10f,
            $"oversized: shoulders {shoulders:F4} are not wider than the hem {hem:F4}");
        // Half width in shell units, where the body is 1 unit tall: 0.0021 is a
        // 40 cm half width on a 1.9 m body, well past any tee into a tent.
        Require(hem <= 0.0021f, $"oversized: hem {hem:F4} is a tent, not a tee");
        notes.Add($"teeWidth hem={hem:F4} waist={waist:F4} shoulders={shoulders:F4}");
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
