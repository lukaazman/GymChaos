using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 5 contract for the customizable player, in edit mode: the remade
/// player asset (shape keys, parts, clips), every class x gender x height
/// build in both visual styles (resolved meshes, height, hair, skin), cloth
/// shells on the remade body, save round trip and version-2 migration, and
/// the host-side sanitizing of names and looks from the network.
/// Logs GYMCHAOS_PLAYER_CUSTOMIZE_OK or _FAILED and exits non-zero on failure.
/// </summary>
public static class GymChaosPlayerCustomizeVerifier
{
    private static readonly string[] Keys = { "Female", "Upper", "Lower", "Soft" };
    private static readonly int[] Heights = { 160, 180, 200 };

    [MenuItem("Tools/GymChaos/Run Player Customize Verification")]
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GymChaosCustomize-" + Guid.NewGuid().ToString("N"));
        var spawned = new List<GameObject>();
        try
        {
            string asset = VerifyAsset();
            int builds = VerifyBuilds(spawned);
            string shells = VerifyOutfits(spawned);
            string capture = CaptureOutfits(spawned);
            VerifyStyles();
            VerifySaves(directory);
            VerifyNet();
            VerifyNames();
            Debug.Log($"GYMCHAOS_PLAYER_CUSTOMIZE_OK {asset} builds={builds} {shells} {capture} " +
                "save=roundtrip migration=v5 net=sanitized versions=per-member");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_PLAYER_CUSTOMIZE_FAILED " + exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            foreach (GameObject go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string VerifyAsset()
    {
        GameObject prefab = Resources.Load<GameObject>(PlayerAppearance.RemakeModel);
        Require(prefab != null, "remade player missing at Resources/" + PlayerAppearance.RemakeModel);
        SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var names = new HashSet<string>(renderers.Select(r => r.name));
        string[] expected = { PlayerAppearance.BodyMeshName, "Tank", "Onyx", "Oversized", "Shorts", "Pants" };
        expected = expected.Concat(Enumerable.Range(1, PlayerAppearance.HairNames.Length - 1).Select(i => "Hair_" + i))
            .Concat(Enumerable.Range(1, PlayerAppearance.FacialHairNames.Length - 1).Select(i => "Beard_" + i)).ToArray();
        foreach (string name in expected) Require(names.Contains(name), "remade player has no part " + name);
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            Mesh mesh = renderer.sharedMesh;
            Require(mesh != null && mesh.isReadable, renderer.name + " mesh missing or not readable");
            foreach (string key in Keys)
                Require(mesh.GetBlendShapeIndex(key) >= 0, renderer.name + " lacks shape key " + key);
        }
        int bones = prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("DEF-"));
        Require(bones == 72, "remade player has " + bones + " DEF bones, expected 72");
        string[] clips = Resources.LoadAll<AnimationClip>(PlayerAppearance.RemakeModel).Select(c => c.name).ToArray();
        foreach (string clip in new[] { "idle1", "walking", "running", "punch_left", "punch_right", "jumping" })
            Require(clips.Any(c => c == clip || c.EndsWith("|" + clip)), "remade player lacks clip " + clip);
        SkinnedMeshRenderer body = renderers.First(r => r.name == PlayerAppearance.BodyMeshName);
        Require(body.sharedMaterials.Any(m => m != null && m.name.StartsWith("Skin")) &&
            body.sharedMaterials.Any(m => m != null && m.name.StartsWith("Gear")), "body lacks the Skin and Gear slots");
        Require(Resources.Load<Texture2D>(PlayerAppearance.RemakeTexture) != null, "remade player texture missing");
        int tris = body.sharedMesh.triangles.Length / 3;
        Require(tris <= 8000, "body has " + tris + " triangles (budget 8000)");
        return "parts=" + renderers.Length + " bodyTris=" + tris + " clips=" + clips.Length;
    }

    private static GameObject Spawn(List<GameObject> spawned)
    {
        GameObject model = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(PlayerAppearance.RemakeModel));
        spawned.Add(model);
        return model;
    }

    private static SkinnedMeshRenderer Part(GameObject model, string name) =>
        model.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == name);

    private static Bounds MeshBounds(SkinnedMeshRenderer renderer)
    {
        Vector3[] vertices = renderer.sharedMesh.vertices;
        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 v in vertices) bounds.Encapsulate(v);
        return bounds;
    }

    private static int VerifyBuilds(List<GameObject> spawned)
    {
        int builds = 0;
        var widths = new Dictionary<string, float>();
        float baseHeight = -1f;
        int heightAxis = 1;
        foreach (GymClassDefinition definition in GymClassCatalog.All)
        {
            for (int gender = 0; gender < 2; gender++)
            {
                foreach (int height in Heights)
                {
                    var look = new GymCharacterSave
                    {
                        username = "Tester", gender = gender, heightCm = height, skinTone = (builds % 6),
                        hair = builds % PlayerAppearance.HairNames.Length,
                        hairColor = builds % 6, facialHair = builds % PlayerAppearance.FacialHairNames.Length
                    };
                    GameObject model = Spawn(spawned);
                    PlayerAppearance.ApplyBody(model, look, definition);
                    foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Require(renderer.sharedMesh.blendShapeCount == 0,
                            renderer.name + " still carries blend shapes after resolving");
                        if (renderer.name.StartsWith("Hair_"))
                            Require(renderer.gameObject.activeSelf == (renderer.name == "Hair_" + look.hair),
                                renderer.name + " visibility wrong for hair " + look.hair);
                        if (renderer.name.StartsWith("Beard_"))
                            Require(renderer.gameObject.activeSelf == (renderer.name == "Beard_" + look.facialHair),
                                renderer.name + " visibility wrong for facial hair " + look.facialHair);
                    }
                    SkinnedMeshRenderer body = Part(model, PlayerAppearance.BodyMeshName);
                    Material skin = body.sharedMaterials.First(m => m.name.StartsWith("Skin"));
                    Require(skin.GetColor("_BaseColor") == GymPalette.SkinTones[look.skinTone], "skin tone not applied");
                    Require(skin.GetTexture("_BaseMap") != null, "skin lost its texture");
                    // Height comes from the uniform fit (180 cm = the gameplay height): the shape keys keep the
                    // T-pose height within 1 cm of the base, so the fitted body lands at heightCm +-1 cm.
                    Bounds bounds = MeshBounds(body);
                    Require(Mathf.Abs(PlayerAppearance.HeightScale(look) * 180f - height) < 0.5f, "height scale for " + height);
                    // The FBX keeps Blender's axes in mesh space: the height axis is the largest T-pose extent.
                    if (baseHeight < 0f)
                    {
                        heightAxis = bounds.size.x > bounds.size.y && bounds.size.x > bounds.size.z ? 0
                            : bounds.size.y > bounds.size.z ? 1 : 2;
                        baseHeight = bounds.size[heightAxis];
                    }
                    float cm = bounds.size[heightAxis] / baseHeight * height;
                    Require(Mathf.Abs(cm - height) < 1f, definition.id + " gender " + gender + " fits to " + cm.ToString("F1") +
                        " cm instead of " + height);
                    if (height == 180)
                        widths[definition.id + gender] = bounds.size.x * bounds.size.y * bounds.size.z / bounds.size[heightAxis];
                    builds++;
                }
            }
        }
        // Class bulk shows: strongman is the widest male, cardio the narrowest; female differs from male.
        Require(widths["strongman0"] > widths["bodybuilding0"] && widths["bodybuilding0"] > widths["cardio0"],
            "class bulk does not change the body: " + string.Join(",", widths.Select(p => p.Key + "=" + p.Value.ToString("F4"))));
        Require(Mathf.Abs(widths["bodybuilding1"] - widths["bodybuilding0"]) > 1e-5f, "female body equals male");
        return builds;
    }

    private static readonly (GymTopStyle, GymBottomStyle, GymShirtColor, GymShirtColor)[] Outfits =
    {
        (GymTopStyle.TankTop, GymBottomStyle.Shorts, GymShirtColor.White, GymShirtColor.Black),
        (GymTopStyle.Oversized, GymBottomStyle.Shorts, GymShirtColor.Blue, GymShirtColor.Black),
        (GymTopStyle.Onyx, GymBottomStyle.Pants, GymShirtColor.Red, GymShirtColor.Blue),
    };

    // Remake outfits are authored parts: exactly one top and one bottom shown, Onyx/Oversized on their
    // DEFAULT atlas, Tank and bottoms tinted, hair hidden under a hat but not under a headband.
    private static string VerifyOutfits(List<GameObject> spawned)
    {
        int checkedOutfits = 0;
        foreach (int gender in new[] { 0, 1 })
        {
            GameObject model = Spawn(spawned);
            PlayerAppearance.ApplyBody(model, new GymCharacterSave { gender = gender, hair = 3 },
                GymClassCatalog.Get(gender == 0 ? "strongman" : "cardio"));
            foreach (var (top, bottom, shirt, bottomColor) in Outfits)
            {
                PlayerAppearance.SetOutfit(model, top, shirt, bottom, bottomColor, 3, GymHeadwear.Cap);
                string topPart = top == GymTopStyle.TankTop ? "Tank" : top.ToString();
                string bottomPart = bottom.ToString();
                foreach (string part in new[] { "Tank", "Onyx", "Oversized", "Shorts", "Pants" })
                    Require(Part(model, part).gameObject.activeSelf == (part == topPart || part == bottomPart),
                        part + " shown wrongly for " + top + "/" + bottom);
                Material topMaterial = Part(model, topPart).sharedMaterials[0];
                if (top == GymTopStyle.TankTop)
                    Require(topMaterial.GetColor("_BaseColor") == PlayerCosmeticLoadout.GetShirtColor(shirt), "tank not tinted");
                else
                    Require(topMaterial.GetTexture("_BaseMap") != null &&
                        topMaterial.GetTexture("_BaseMap").name ==
                        (top + "_" + shirt).ToLowerInvariant(), top + " lacks its " + shirt + " atlas");
                Require(Part(model, bottomPart).sharedMaterials[0].GetColor("_BaseColor") ==
                    PlayerCosmeticLoadout.GetShirtColor(bottomColor), bottom + " not tinted");
                Require(!Part(model, "Hair_3").gameObject.activeSelf, "hair shows through a cap");
                checkedOutfits++;
            }
            PlayerAppearance.SetOutfit(model, GymTopStyle.TankTop, GymShirtColor.Black, GymBottomStyle.Shorts,
                GymShirtColor.Black, 3, GymHeadwear.Headband);
            Require(Part(model, "Hair_3").gameObject.activeSelf, "a headband hid the hair");
        }
        return "outfits=" + checkedOutfits;
    }

    // With a GPU (-Graphics): every remade outfit as the game builds it, male and female, front and
    // back, in one review sheet.
    private static string CaptureOutfits(List<GameObject> spawned)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return "capture=skipped";
        Vector3 origin = new Vector3(0f, -600f, 0f);
        var models = new List<GameObject>();
        int column = 0;
        foreach (int gender in new[] { 0, 1 })
        {
            foreach (var (top, bottom, shirt, bottomColor) in Outfits)
            {
                GameObject model = Spawn(spawned);
                model.transform.position = origin + Vector3.right * (column++ * 1.1f);
                PlayerAppearance.ApplyBody(model, new GymCharacterSave { gender = gender, hair = 2, skinTone = 2 + gender },
                    GymClassCatalog.Get(gender == 0 ? "bodybuilding" : "cardio"));
                PlayerAppearance.SetOutfit(model, top, shirt, bottom, bottomColor, 2, GymHeadwear.None);
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderer.updateWhenOffscreen = true;
                // Standing idle, as in the mirror and the turntable (the prefab holds the first clip's pose).
                AnimationClip idle = Resources.LoadAll<AnimationClip>(PlayerAppearance.RemakeModel)
                    .FirstOrDefault(c => c.name == "idle1" || c.name.EndsWith("|idle1"));
                if (idle != null) GymClipSampler.Sample(idle, model, 0.5f);
                models.Add(model);
            }
        }
        var bounds = new Bounds(models[0].transform.position, Vector3.zero);
        foreach (GameObject model in models)
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(false))
                bounds.Encapsulate(renderer.bounds);

        var cameraObject = new GameObject("Outfit Capture Camera");
        var lightObject = new GameObject("Outfit Capture Light");
        spawned.Add(cameraObject);
        spawned.Add(lightObject);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = bounds.extents.y * 1.08f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = PersonaMenuStyle.ButtonIdle;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 50f;
        int width = 2400, height = Mathf.RoundToInt(width * bounds.size.y / (bounds.size.x * 1.05f));
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        var sheet = new Texture2D(width, height * 2, TextureFormat.RGB24, false);
        for (int view = 0; view < 2; view++)
        {
            Vector3 direction = view == 0 ? Vector3.back : Vector3.forward;
            camera.transform.SetPositionAndRotation(bounds.center + direction * 20f, Quaternion.LookRotation(-direction));
            lightObject.transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * 0.6f + Vector3.right * 0.3f);
            camera.Render();
            RenderTexture.active = target;
            sheet.ReadPixels(new Rect(0, 0, width, height), 0, view == 0 ? height : 0);
        }
        sheet.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        string path = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", "customize", "unity_outfits.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return "capture=" + models.Count;
    }

    // The player is always the DEFAULT scan now: one body, its cloth shells and the
    // height as a uniform transform scale. The POTATO player is retired.
    private static void VerifyStyles()
    {
        Require(!PlayerAppearance.UsesRemakeBody, "the player must always use the DEFAULT scan body");
        foreach (GymTopStyle top in new[] { GymTopStyle.Oversized, GymTopStyle.Onyx })
            foreach (GymShirtColor color in Enum.GetValues(typeof(GymShirtColor)))
                Require(Resources.Load<Texture2D>(PlayerCosmeticLoadout.GetOutfitTexturePath(top, color)) != null,
                    "DEFAULT outfit texture missing " + top + "/" + color);
        Require(Mathf.Abs(PlayerAppearance.HeightScale(new GymCharacterSave { heightCm = 160 }) - 160f / 180f) < 1e-4f,
            "DEFAULT height scale");
        Require(Mathf.Abs(PlayerAppearance.HeightScale(new GymCharacterSave { heightCm = 200 }) - 200f / 180f) < 1e-4f,
            "height scale must run to 200 cm");
        // Every character with both models has a POTATO entry in the table.
        Require(GymVersionedLooks.MemberCount == 12, "the member table lost an entry");
        Require(GymVersionedLooks.VehicleCount == 7, "the vehicle table lost an entry");
    }

    private static void VerifySaves(string directory)
    {
        Directory.CreateDirectory(directory);
        var repository = new GymSaveRepository(directory, GymClassCatalog.All.Select(c => c.id));
        var look = new GymCharacterSave
        {
            username = "Squat Queen", gender = 1, heightCm = 172, skinTone = 5, hair = 4, hairColor = 2, facialHair = 0
        };
        GymCharacterSave save = GymCharacterSave.New("cardio", look);
        repository.Write(save.slotId, repository.Serialize(save));
        GymSaveReadResult read = repository.Read(save.slotId);
        Require(read.Success, "round trip failed: " + read.Message);
        GymCharacterSave back = read.Data;
        Require(back.username == "Squat Queen" && back.gender == 1 && back.heightCm == 172 && back.skinTone == 5 &&
            back.hair == 4 && back.hairColor == 2 && back.facialHair == 0 && back.version == GymCharacterSave.CurrentVersion,
            "look did not survive the round trip");

        // A version 2 file has no look. Before the DEFAULT / POTATO split it opened the
        // create screen; now it loads as the one standard character instead.
        string id = Guid.NewGuid().ToString("N");
        string now = DateTime.UtcNow.ToString("O");
        string payload = "{\"version\":2,\"slotId\":\"" + id + "\",\"classId\":\"strongman\",\"createdUtc\":\"" + now +
            "\",\"savedUtc\":\"" + now + "\",\"hasWorld\":true,\"progression\":{\"level\":3}}";
        File.WriteAllText(Path.Combine(directory, id + ".json"), GymChaosClassSessionVerifier.Envelope(payload));
        GymSaveReadResult legacy = repository.Read(id);
        Require(legacy.Success && legacy.Data.IsCustomized &&
            legacy.Data.username == GymCharacterSave.LegacyName && legacy.Data.classId == "strongman" &&
            legacy.Data.gender == 0 && legacy.Data.heightCm == PlayerAppearance.DefaultMaleHeight &&
            legacy.Data.version == GymCharacterSave.CurrentVersion,
            "a pre-split save did not migrate to the standard character");
        legacy.Data.CopyLook(look);
        repository.Write(id, repository.Serialize(legacy.Data));
        Require(repository.Read(id).Data.username == "Squat Queen" && repository.Read(id).Data.classId == "strongman",
            "migrated look was not written");

        // Out-of-range values on disk are clamped; a pre-split file takes the
        // standard character, name included.
        string bad = Guid.NewGuid().ToString("N");
        string badPayload = "{\"version\":3,\"slotId\":\"" + bad + "\",\"classId\":\"cardio\",\"createdUtc\":\"" + now +
            "\",\"savedUtc\":\"" + now + "\",\"username\":\"<b>\",\"gender\":7,\"heightCm\":999,\"skinTone\":-3," +
            "\"hair\":42,\"hairColor\":9,\"facialHair\":-1,\"progression\":{}}";
        File.WriteAllText(Path.Combine(directory, bad + ".json"), GymChaosClassSessionVerifier.Envelope(badPayload));
        GymCharacterSave clamped = repository.Read(bad).Data;
        Require(clamped != null && clamped.username == GymCharacterSave.LegacyName &&
            clamped.gender == 0 && clamped.heightCm == PlayerAppearance.DefaultMaleHeight &&
            clamped.skinTone == 2, "a pre-split file did not take the standard character");
    }

    private static void VerifyNet()
    {
        GymCharacterSave save = GymCharacterSave.New("powerlifting", new GymCharacterSave
        {
            username = "Deadlift Dan", gender = 0, heightCm = 190, skinTone = 3, hair = 2, hairColor = 1, facialHair = 3
        });
        Require(GymNetCharacter.TryValidate(GymNetCharacter.ToJson(save), out GymCharacterSave clean, out string reason),
            "valid character rejected: " + reason);
        Require(clean.username == "Deadlift Dan" && clean.heightCm == 190 && clean.facialHair == 3,
            "host lost the guest's look");

        var hostile = new GymNetLook
        {
            name = "<color=red>X</color>" + new string('A', 40), gender = 9, heightCm = -5, skinTone = 99,
            hair = -2, hairColor = 77, facialHair = 12
        };
        GymCharacterSave cleaned = GymNetCharacter.CleanLook(hostile);
        Require(cleaned.username.Length >= PlayerAppearance.NameMin && cleaned.username.Length <= PlayerAppearance.NameMax &&
            cleaned.username.IndexOfAny(new[] { '<', '>', '=', '/' }) < 0, "hostile name passed: " + cleaned.username);
        Require(cleaned.gender == 1 && cleaned.heightCm == PlayerAppearance.DefaultFemaleHeight && cleaned.skinTone == 5 &&
            cleaned.hair == 0 && cleaned.hairColor == 5 && cleaned.facialHair == PlayerAppearance.FacialHairNames.Length - 1,
            "hostile look was not clamped");
        Require(GymNetCharacter.CleanLook(new GymNetLook { name = "a" }).username == string.Empty, "1-char name accepted");
        Require(GymNetCharacter.CleanLook("{not json") == null, "garbage look accepted");
    }

    private static void VerifyNames()
    {
        Require(PlayerAppearance.SanitizeName("  Big   Ron  ") == "Big Ron", "spaces not collapsed");
        Require(PlayerAppearance.SanitizeName("ab") == null, "short name accepted");
        Require(PlayerAppearance.SanitizeName(new string('Z', 30)).Length == PlayerAppearance.NameMax, "long name not capped");
        string line = PlayerAppearance.FillName("Welcome, {name}.");
        Require(line == "Welcome, champ." || (GymSessionService.Active != null && line.Contains(GymSessionService.Active.username)),
            "dialogue {name} not filled: " + line);
        Require(PlayerAppearance.FillName("No token") == "No token", "line without token changed");
    }
}
