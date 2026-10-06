#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Play-mode checks for the 2026-10-06 overhaul: sound effects (UI hooks on
/// every menu control, hover/press cues, jump and landing, silent parked
/// vehicles), the joined road corner line, 1.25x vehicles inside the larger
/// stalls, the designed park and its seamless ground, and the detailed gym
/// and locker floors. Logs GYMCHAOS_OVERHAUL_OK or GYMCHAOS_OVERHAUL_FAILED.
/// </summary>
[InitializeOnLoad]
public static class GymChaosOverhaulVerifier
{
    private const string RequestedKey = "GymChaos.OverhaulVerifierRequested";
    private static double started;
    private static double readyAt = -1d;
    private static int stage;
    private static int stageFrames;
    private static int landBaseline;
    private static int jumpBaseline;
    private static int hoverBaseline;
    private static int pressBaseline;
    private static Keyboard keyboard;

    static GymChaosOverhaulVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Verify Overhaul")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierExit.Record(1);
        stage = 0;
        readyAt = -1d;
        EditorSceneManager.OpenScene(GymChaosPlayStartScene.ScenePath);
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
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(1);
    }

    private static void Finish(bool passed, string message)
    {
        if (passed)
        {
            Debug.Log("GYMCHAOS_OVERHAUL_OK " + message);
        }
        else
        {
            Debug.LogError("GYMCHAOS_OVERHAUL_FAILED " + message);
        }
        GymChaosVerifierExit.Record(passed ? 0 : 1);
        if (keyboard != null && keyboard.added)
        {
            InputSystem.RemoveDevice(keyboard);
            keyboard = null;
        }
        EditorApplication.isPlaying = false;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (now - started > 240d)
            {
                throw new TimeoutException($"stage {stage} did not finish");
            }
            switch (stage)
            {
                case 0:
                    if (!GymOutdoorBuilder.IsBuilt || !GymProteinStoreEnvironment.IsLoaded ||
                        !GymRoadsideBusStop.IsDavieBusReady)
                    {
                        return;
                    }
                    if (readyAt < 0d) readyAt = now;
                    GymAudio audio = UnityEngine.Object.FindAnyObjectByType<GymAudio>();
                    if (audio == null || !audio.AllClipsLoadedForVerification ||
                        now - readyAt < 4d || GymOutdoorFoliage.LoadedPlants + GymOutdoorFoliage.FailedPlants <
                            GymOutdoorFoliage.RequestedPlants)
                    {
                        return;
                    }
                    VerifyStaticLayout();
                    // A real menu: the pause menu (batch runs skip the start screen).
                    movementPlayer = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                    GymPauseMenu.CreateForScene(movementPlayer);
                    GymPauseMenu.Open(movementPlayer);
                    readyAt = now;
                    stage = 1;
                    stageFrames = 0;
                    return;
                case 1:
                    // Let the menu's own open cue clear the repeat guard.
                    if (++stageFrames < 3 || now - readyAt < 0.5d) return;
                    VerifyUiSounds();
                    VerifyCursorPolicy();
                    ResumeFromPause();
                    BeginMovementCheck();
                    stage = 2;
                    stageFrames = 0;
                    return;
                case 2:
                    if (TickMovementCheck())
                    {
                        Finish(true, "all stages passed");
                    }
                    return;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.Message);
        }
    }

    // ---- layout: road line, vehicles, park, floors ----

    private static void VerifyStaticLayout()
    {
        Renderer horizontal = FindRenderer("Road Center Line");
        Renderer vertical = FindRenderer("Road Extension Center Line");
        if (horizontal == null || vertical == null)
            throw new InvalidOperationException("road centre lines missing");
        Bounds h = horizontal.bounds;
        Bounds v = vertical.bounds;
        // The horizontal line must reach the vertical line and the vertical
        // line must reach down to the horizontal line: one joined L.
        float gapX = v.min.x - h.max.x;
        float gapZ = v.min.z - h.max.z;
        bool joined = gapX <= 0.01f && gapZ <= 0.01f &&
            h.max.x <= v.max.x + 0.2f && v.min.z >= h.min.z - 0.2f;
        if (!joined)
            throw new InvalidOperationException(
                $"road corner line gap: horizontal={h.min}-{h.max} vertical={v.min}-{v.max}");
        Debug.Log($"GYMCHAOS_ROAD_CORNER_LINE_OK gapX={gapX:F3} gapZ={gapZ:F3}");

        VerifyVehicles();
        VerifyPark();
        VerifyFloors();
    }

    private static void VerifyVehicles()
    {
        float scale = GymOutdoorBuilder.VehicleScale;
        if (Mathf.Abs(scale - 1.25f) > 0.001f)
            throw new InvalidOperationException($"vehicle scale {scale}");
        if (Mathf.Abs(GymOutdoorBuilder.ParkingVehicleTargetLength - 5f) > 0.01f)
            throw new InvalidOperationException(
                $"car target length {GymOutdoorBuilder.ParkingVehicleTargetLength}");
        float bayStep = GymOutdoorBuilder.ParkingBayStep;
        if (bayStep < 4.2f * scale - 0.01f)
            throw new InvalidOperationException($"parking bay step {bayStep:F2} not scaled");

        GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Exclude);
        int parkedCars = 0;
        List<Bounds> parked = new List<Bounds>();
        foreach (GymVisitorVehicle vehicle in vehicles)
        {
            if (!vehicle.IsParked || !vehicle.gameObject.activeInHierarchy) continue;
            if (!vehicle.IsEngineStoppedForVerification)
                throw new InvalidOperationException($"{vehicle.name} engine plays while parked");
            BoxCollider body = vehicle.GetComponent<BoxCollider>();
            if (body == null) continue;
            Bounds bounds = body.bounds;
            float length = Mathf.Max(bounds.size.x, bounds.size.z);
            if (vehicle.name.Contains("Davie") || vehicle.name.Contains("Goku")) continue;
            parkedCars++;
            if (length < 4.6f || length > 5.4f)
                throw new InvalidOperationException($"{vehicle.name} length {length:F2} is not 1.25x");
            if (!GymOutdoorBuilder.ParkingBounds.Contains(new Vector3(bounds.center.x,
                GymOutdoorBuilder.ParkingBounds.center.y, bounds.center.z)))
                throw new InvalidOperationException($"{vehicle.name} parked outside the lot");
            // Inside its stall with padding on both sides.
            float column = (bounds.center.x - GymOutdoorBuilder.ParkingBayStartX) / bayStep;
            float sidePadding = (bayStep - bounds.size.x) * 0.5f;
            if (sidePadding < 0.5f)
                throw new InvalidOperationException(
                    $"{vehicle.name} side padding {sidePadding:F2} in a {bayStep:F2} m stall");
            float stallCenterX = GymOutdoorBuilder.GetParkingBayCenterX(Mathf.FloorToInt(column));
            if (Mathf.Abs(bounds.center.x - stallCenterX) > 0.35f)
                throw new InvalidOperationException(
                    $"{vehicle.name} off stall centre by {bounds.center.x - stallCenterX:F2}");
            foreach (Bounds other in parked)
            {
                if (other.Intersects(bounds))
                    throw new InvalidOperationException($"{vehicle.name} overlaps another parked vehicle");
            }
            parked.Add(bounds);
        }

        Bounds bus = GymRoadsideBusStop.DavieBusBounds;
        float busLength = Mathf.Max(bus.size.x, bus.size.z);
        if (busLength < 10.4f * scale * 0.9f || busLength > 10.4f * scale * 1.1f)
            throw new InvalidOperationException($"bus length {busLength:F2}");
        Debug.Log($"GYMCHAOS_VEHICLE_SCALE_OK scale={scale:F2} parkedCars={parkedCars} " +
            $"bayStep={bayStep:F2} parkingDepth={GymOutdoorBuilder.ParkingBounds.size.z:F2} " +
            $"roadWidth={GymOutdoorBuilder.VehicleRoadWidthForVerification:F2} bus={busLength:F2}");
    }

    private static void VerifyPark()
    {
        if (GymOutdoorFoliage.PathCellCount < 150 || GymOutdoorFoliage.GrassBladeCount < 20000 ||
            GymOutdoorFoliage.BenchCount < 3 || GymOutdoorFoliage.LampCount < 3 ||
            GymOutdoorFoliage.TreeCount < 15 || GymOutdoorFoliage.BushCount < 30 ||
            GymOutdoorFoliage.FailedPlants != 0)
            throw new InvalidOperationException(
                $"park incomplete pathCells={GymOutdoorFoliage.PathCellCount} " +
                $"grass={GymOutdoorFoliage.GrassBladeCount} benches={GymOutdoorFoliage.BenchCount} " +
                $"lamps={GymOutdoorFoliage.LampCount} trees={GymOutdoorFoliage.TreeCount} " +
                $"bushes={GymOutdoorFoliage.BushCount} failed={GymOutdoorFoliage.FailedPlants}");
        Renderer ground = FindRenderer(GymOutdoorFoliage.GroundName);
        Renderer cityGround = FindRenderer("City Dystopia Ground North");
        if (ground == null || cityGround == null)
            throw new InvalidOperationException("park or city ground missing");
        float step = Mathf.Abs(ground.bounds.max.y - cityGround.bounds.max.y);
        if (step > 0.005f)
            throw new InvalidOperationException($"park/city ground step {step:F3} m");
        Texture groundTexture = ground.sharedMaterial != null ? ground.sharedMaterial.mainTexture : null;
        if (groundTexture == null || groundTexture.width < 256)
            throw new InvalidOperationException("park ground not baked");
        // Park edge colour must equal the city paving colour (seamless).
        Color edge = GymOutdoorFoliage.EdgeColorForVerification;
        Color paving = GymOutdoorFoliage.CityPavingColor;
        if (Mathf.Abs(edge.r - paving.r) + Mathf.Abs(edge.g - paving.g) + Mathf.Abs(edge.b - paving.b) > 0.04f)
            throw new InvalidOperationException($"park edge {edge} does not fade into paving {paving}");
        Color cityColor = cityGround.sharedMaterial.GetColor("_BaseColor");
        if (Mathf.Abs(cityColor.r - paving.r) + Mathf.Abs(cityColor.g - paving.g) > 0.01f)
            throw new InvalidOperationException("city ground is not the paving colour");
        // Paths, grass and props are outside the fences: no colliders.
        GameObject details = GameObject.Find(GymOutdoorFoliage.DetailsRootName);
        if (details == null || details.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("park details missing or collidable");
        Debug.Log($"GYMCHAOS_CENTRAL_PARK_OK pathCells={GymOutdoorFoliage.PathCellCount} " +
            $"grassBlades={GymOutdoorFoliage.GrassBladeCount} benches={GymOutdoorFoliage.BenchCount} " +
            $"lamps={GymOutdoorFoliage.LampCount} trees={GymOutdoorFoliage.TreeCount} " +
            $"bushes={GymOutdoorFoliage.BushCount} groundStep={step:F3}");
    }

    private static void VerifyFloors()
    {
        foreach (string name in new[] { "Rubber Floor", "Locker Room Floor" })
        {
            Renderer floor = FindRenderer(name);
            Texture texture = floor != null && floor.sharedMaterial != null
                ? floor.sharedMaterial.GetTexture("_BaseMap") : null;
            if (texture == null || texture.width != GymSurfaceMaterialFactory.DetailTextureSizeForVerification ||
                texture.mipmapCount < 2)
                throw new InvalidOperationException($"{name} lacks the detailed floor texture");
            Vector2 tiling = floor.sharedMaterial.GetTextureScale("_BaseMap");
            if (tiling.x < 5f)
                throw new InvalidOperationException($"{name} tiling {tiling} is not metre scale");
        }
        Debug.Log("GYMCHAOS_GYM_FLOOR_DETAIL_OK");
    }

    // ---- sounds ----

    private static void VerifyUiSounds()
    {
        if (GymAudio.DefinitionCountForVerification < 18)
            throw new InvalidOperationException("sound definitions missing");
        GymUiSounds.ScanNow();
        Selectable[] selectables = UnityEngine.Object.FindObjectsByType<Selectable>(
            FindObjectsInactive.Exclude);
        int hooked = 0;
        foreach (Selectable selectable in selectables)
        {
            if (!selectable.isActiveAndEnabled) continue;
            if (selectable.GetComponent<GymUiSoundHook>() == null)
                throw new InvalidOperationException($"{selectable.name} has no UI sound hook");
            hooked++;
        }
        if (hooked == 0)
            throw new InvalidOperationException("no live UI controls to hook");

        Button play = null;
        foreach (Selectable selectable in selectables)
        {
            if (selectable is Button button && button.isActiveAndEnabled && button.IsInteractable())
            {
                play = button;
                break;
            }
        }
        if (play == null)
            throw new InvalidOperationException("no interactable menu button");
        hoverBaseline = Count(GymSoundEffect.UiHover);
        PointerEventData pointer = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(play.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        if (Count(GymSoundEffect.UiHover) != hoverBaseline + 1)
            throw new InvalidOperationException("hover over a menu button made no sound");
        // Press cue through the hook only (no onClick: the menu stays put).
        pressBaseline = PressCount();
        play.GetComponent<GymUiSoundHook>().OnSubmit(new BaseEventData(EventSystem.current));
        int pressed = PressCount();
        if (pressed != pressBaseline + 1)
            throw new InvalidOperationException("pressing a menu button made no sound");
        Debug.Log($"GYMCHAOS_UI_SOUNDS_OK hooked={hooked} hover=1 press=1 " +
            $"hooksCreated={GymUiSounds.HookCountForVerification}");
    }

    private static void VerifyCursorPolicy()
    {
        if (!GymPauseMenu.IsVisible)
            throw new InvalidOperationException("pause menu did not open");
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player != null && player.CursorCaptureAllowedForVerification)
            throw new InvalidOperationException("cursor capture allowed while a menu is open");
        Debug.Log("GYMCHAOS_WEBGL_CURSOR_POLICY_OK menuCapture=0");
    }

    private static void ResumeFromPause()
    {
        GymPauseMenu menu = UnityEngine.Object.FindAnyObjectByType<GymPauseMenu>();
        MethodInfo resume = typeof(GymPauseMenu).GetMethod(
            "Resume", BindingFlags.Instance | BindingFlags.NonPublic);
        if (menu == null || resume == null)
            throw new InvalidOperationException("pause menu resume not found");
        resume.Invoke(menu, null);
        Time.timeScale = 1f;
    }

    private static MethodInfo handleMovement;
    private static PlayerMovement movementPlayer;

    private static void BeginMovementCheck()
    {
        movementPlayer = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (movementPlayer == null)
            throw new InvalidOperationException("player missing");
        handleMovement = typeof(PlayerMovement).GetMethod(
            "HandleMovement", BindingFlags.Instance | BindingFlags.NonPublic);
        if (handleMovement == null)
            throw new InvalidOperationException("HandleMovement not found");
        keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        landBaseline = movementPlayer.LandSoundCountForVerification;
        jumpBaseline = movementPlayer.JumpSoundCountForVerification;
    }

    // Drives the real movement step frame by frame: settle on the floor,
    // press Space once (jump sound), then fall back (landing sound).
    private static bool TickMovementCheck()
    {
        stageFrames++;
        CharacterController controller = movementPlayer.GetComponent<CharacterController>();
        if (controller != null && !controller.enabled) controller.enabled = true;
        bool pressSpace = stageFrames == 20;
        InputSystem.QueueStateEvent(keyboard, pressSpace ? new KeyboardState(Key.Space) : new KeyboardState());
        InputSystem.Update();
        handleMovement.Invoke(movementPlayer, null);
        if (stageFrames == 30 && movementPlayer.JumpSoundCountForVerification <= jumpBaseline)
            throw new InvalidOperationException("jump made no sound");
        if (movementPlayer.LandSoundCountForVerification > landBaseline &&
            movementPlayer.JumpSoundCountForVerification > jumpBaseline)
        {
            Debug.Log($"GYMCHAOS_SFX_VERIFICATION_OK jump={movementPlayer.JumpSoundCountForVerification - jumpBaseline} " +
                $"land={movementPlayer.LandSoundCountForVerification - landBaseline} " +
                $"jumpPlays={Count(GymSoundEffect.Jump)} landPlays={Count(GymSoundEffect.Land)}");
            return true;
        }
        if (stageFrames > 600)
            throw new InvalidOperationException(
                $"no landing sound jump={movementPlayer.JumpSoundCountForVerification - jumpBaseline} " +
                $"grounded={controller != null && controller.isGrounded}");
        return false;
    }

    private static int PressCount() =>
        Count(GymSoundEffect.UiConfirm) + Count(GymSoundEffect.UiClick) + Count(GymSoundEffect.UiBack);

    private static int Count(GymSoundEffect effect) =>
        GymAudio.PlayCountsForVerification.TryGetValue(effect, out int count) ? count : 0;

    private static Renderer FindRenderer(string name)
    {
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Include))
        {
            if (renderer.name == name) return renderer;
        }
        return null;
    }
}
#endif
