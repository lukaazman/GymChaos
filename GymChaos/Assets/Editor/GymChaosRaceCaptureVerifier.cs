#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.EventSystems;

/// <summary>
/// Visual evidence for the race (run with -Graphics): top-down city before and
/// with the track, the grid with seated drivers, the player in the buggy, the
/// spiral, the sunken shortcut, the sign, and chase-camera frames at the
/// start, at speed, drifting and boosting. PNGs go to Logs/agent/race/.
/// Logs GYMCHAOS_RACE_CAPTURE_OK.
/// </summary>
public static class GymChaosRaceCaptureVerifier
{
    private const string RequestedKey = "GymChaos.RaceCaptureRequested";
    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly List<string> written = new List<string>();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Capture Race Evidence")]
    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        routine = null;
        written.Clear();
        GymChaosVerifierHooks.Hook(Tick, Changed);
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            stack.Clear();
            routine = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null) return;
        try
        {
            if (stack.Count == 0) stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                return;
            }
            Finish(written.Count >= 10 ? 0 : 1, $"images={written.Count} {string.Join(",", written)}");
        }
        catch (System.Exception exception)
        {
            Finish(1, "exception " + exception);
        }
    }

    private static IEnumerator Scenario()
    {
        float started = Time.realtimeSinceStartup;
        PlayerMovement player = null;
        while (player == null || !GymRaceCityCarver.CityReady() || GymRaceSign.Instance == null || GymRaceDirector.Instance == null)
        {
            if (Time.realtimeSinceStartup - started > 160f) throw new System.Exception("scene not ready");
            player = Object.FindFirstObjectByType<PlayerMovement>();
            yield return null;
        }
        float settle = Time.time + 2f;
        while (Time.time < settle) yield return null;

        GymRaceSign sign = GymRaceSign.Instance;
        TopDown("topdown_before");
        Shot("sign", sign.StandPoint + new Vector3(-1.5f, 1.7f, -2.5f), sign.transform.position + Vector3.up * 2.3f, 55f);

        GymRaceDirector director = GymRaceDirector.Instance;
        float trafficLimit = Time.time + 90f;
        while (director.BlockReasonForVerification != null)
        {
            if (Time.time > trafficLimit) throw new System.Exception("race blocked: " + director.BlockReasonForVerification);
            yield return null;
        }
        // Night race: every car shows its lamps from the grid to the finish.
        GymTimeOfDay clock = GymTimeOfDay.Instance;
        if (clock == null) throw new System.Exception("no day clock");
        clock.SetTimeForVerification(0.95f);
        int visitorLamps = 0;
        foreach (GymVehicleLights lamps in GymVehicleLights.All)
        {
            lamps.RefreshNow();
            if (lamps.IsLit) visitorLamps++;
        }
        written.Add("visitorLampsLit=" + visitorLamps);
        // A parked visitor car stays dark even at night.
        int parkedDark = 0;
        foreach (GymVisitorVehicle visitor in Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None))
        {
            GymVehicleLights lamps = visitor.GetComponent<GymVehicleLights>();
            if (lamps == null || !visitor.IsParked) continue;
            lamps.RefreshNow();
            if (lamps.IsLit) throw new System.Exception($"{visitor.name} is parked but its lamps are on");
            parkedDark++;
        }
        written.Add("parkedDark=" + parkedDark);
        if (!director.StartRaceForVerification(player, true)) throw new System.Exception("race did not start");
        // The popup is driven by real key events below, not auto-continue.
        director.AutoContinueResults = false;
        float boardLimit = Time.time + 60f;
        while (director.PlayerRacer == null && Time.time < boardLimit) yield return null;
        if (director.PlayerRacer == null) throw new System.Exception("karts never spawned");
        Transform buggyHolder = director.Cars[director.PlayerRacer.GridSlot].Holder;
        if (Vector3.Distance(player.transform.position, buggyHolder.position) > 2.5f)
            throw new System.Exception("player not in the buggy right after the karts spawned");
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            yield return null;
        }
        TopDown("topdown_race");
        float raceClock = clock.Time01;
        int carLamps = 0;
        foreach (GymRaceVehicleFactory.Car car in director.Cars)
        {
            GymVehicleLights lamps = car.Holder.GetComponent<GymVehicleLights>();
            if (lamps == null) continue;
            lamps.RefreshNow();
            if (lamps.IsLit) carLamps++;
            // The glow is the car's own lamp geometry, not added boxes.
            if (lamps.LampPieceCount == 0) throw new System.Exception($"{car.Holder.name} has no lamp parts from its model");
            written.Add($"lamps:{car.Holder.name.Replace(" Race Car Holder", "").Replace(" Holder", "")}={lamps.LampPieceCount}");
        }
        if (carLamps < 6) throw new System.Exception($"only {carLamps}/6 race cars lit at night");
        // Lamp atlas: each textured car body alone, by day, straight on from
        // the front and the back, framed in the lamp code's own coordinates.
        clock.SetTimeForVerification(0.5f);
        foreach (GymRaceVehicleFactory.Car car in director.Cars)
        {
            GymVehicleLights lamps = car.Holder.GetComponent<GymVehicleLights>();
            if (lamps != null) LampAtlas(lamps, car.Holder.name.Replace(" Race Car Holder", "").Replace(" Holder", "").Replace(' ', '_'));
        }
        GymVisitorVehicle ronnie = GymVisitorVehicle.CreateRaceVisual(BodybuilderIdentity.Ronnie, new Vector3(0f, -500f, 0f), Quaternion.identity);
        GymVehicleLights ronnieLamps = GymVehicleLights.Attach(ronnie.transform);
        ronnieLamps.RefreshNow();
        LampAtlas(ronnieLamps, "Ronnie");
        Object.Destroy(ronnie.gameObject);
        clock.SetTimeForVerification(raceClock);
        foreach (GymVehicleLights lamps in GymVehicleLights.All) lamps.RefreshNow();
        written.Add("raceLampsLit=" + carLamps);
        // Standings portraits: transparent head-only renders of each racer.
        int heads = 0;
        foreach (GymRaceRacer racer in director.Racers)
        {
            if (racer.Headshot == null) continue;
            heads++;
            string headFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "race");
            Directory.CreateDirectory(headFolder);
            File.WriteAllBytes(Path.Combine(headFolder, "head_" + racer.DisplayName.Replace(' ', '_') + ".png"), racer.Headshot.EncodeToPNG());
            written.Add("head_" + racer.DisplayName);
        }
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && heads < 6)
        {
            throw new System.Exception($"only {heads} racer headshots rendered");
        }
        foreach (GymRaceVehicleFactory.Car car in director.Cars)
        {
            // Close-ups of every car's own lamps, nose and tail.
            Transform holder = car.Holder;
            string label = holder.name.Replace(" Race Car Holder", "").Replace(" Holder", "").Replace(' ', '_');
            Vector3 centre = holder.position + Vector3.up * 0.8f;
            // Big cars (the Hummer) need the camera further out.
            float reach = car.Identity == BodybuilderIdentity.Arnold ? 1.25f : 1f;
            // From the outer side of the lane, so the neighbour cars never block the view.
            Vector3 outward = Vector3.right * Mathf.Sign(holder.position.x - GymRaceTrackLayout.GridRoadCenterX);
            Shot("car_front_" + label, centre + holder.forward * 3.4f * reach + outward * 2.4f * reach + Vector3.up * 0.3f * reach, centre + holder.forward * 1.6f, 50f);
            Shot("car_rear_" + label, centre - holder.forward * 3.4f * reach + outward * 2.4f * reach + Vector3.up * 0.3f * reach, centre - holder.forward * 1.6f, 50f);
        }
        Shot("lamps_front", new Vector3(208f, 1.6f, 70f), new Vector3(208f, 0.7f, 52f), 45f);
        Shot("lamps_rear", new Vector3(208f, 1.6f, 22f), new Vector3(208f, 0.7f, 40f), 45f);
        Shot("grid_side", new Vector3(203.5f, 3.4f, 74f), new Vector3(209f, 0.6f, 50f), 50f);
        Shot("grid_rear", new Vector3(208f, 4.5f, 30f), new Vector3(208f, 0.8f, 60f), 50f);
        GymRaceRacer pole = director.Racers[0];
        Vector3 pole0 = pole.Kart.transform.position;
        Shot("driver_window", pole0 + new Vector3(-4.2f, 1.4f, 0.6f), pole0 + new Vector3(0f, 1.1f, 0.3f), 45f);
        GymRaceRacer me = director.PlayerRacer;
        Vector3 buggy = me.Kart.transform.position;
        Shot("player_buggy_side", buggy + new Vector3(-4f, 1.6f, 1.2f), buggy + new Vector3(0f, 0.8f, 0f), 45f);
        Shot("player_buggy_front", buggy + new Vector3(-1.2f, 1.8f, 4.6f), buggy + new Vector3(0f, 0.8f, 0f), 45f);
        Shot("spiral", new Vector3(285f, 28f, -40f), new Vector3(255f, 6f, -2f), 55f);
        Shot("trench", new Vector3(103.5f, 6f, 82f), new Vector3(102.8f, -4f, 45f), 55f);
        Shot("sw_turn", new Vector3(130f, 8f, -71f), new Vector3(90f, 0f, -62f), 55f);
        Shot("grid_topdown", new Vector3(208f, 60f, 46f), new Vector3(208f, 0f, 46.5f), 50f);

        while (director.CurrentPhase == GymRaceDirector.Phase.Countdown) yield return null;
        MainCamera("chase_go");
        float t = Time.time + 6f;
        while (Time.time < t) yield return null;
        MainCamera("chase_speed");
        bool drift = false;
        bool boost = false;
        float limit = Time.time + 120f;
        while ((!drift || !boost) && Time.time < limit && director.CurrentPhase == GymRaceDirector.Phase.Racing)
        {
            if (!drift && me.Kart.IsDrifting && me.Kart.DriftTier >= 1)
            {
                drift = true;
                MainCamera("chase_drift");
            }
            if (!boost && me.Kart.IsBoosting)
            {
                boost = true;
                float wait = Time.time + 0.6f;
                while (Time.time < wait) yield return null;
                MainCamera("chase_boost");
            }
            yield return null;
        }
        // ------------------------------------------- overlay HUD + popup, by keys
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            if (director.Hud.StandingHeadsShown < 6) throw new System.Exception($"standings show {director.Hud.StandingHeadsShown}/6 heads");
            if (!director.Hud.StandingsUncoloured) throw new System.Exception("standings rows still coloured");
            yield return ScreenShot("hud_standings");
        }
        if (!Mathf.Approximately(clock.Time01, raceClock)) throw new System.Exception($"day clock moved during the race {raceClock:F4}->{clock.Time01:F4}");
        foreach (GymRaceVehicleFactory.Car car in director.Cars)
        {
            GymVehicleLights lamps = car.Holder.GetComponent<GymVehicleLights>();
            if (lamps != null && !lamps.IsLit) throw new System.Exception("a race car lost its lamps mid-race");
        }
        director.ForceResultsForVerification();
        float popupLimit = Time.time + 5f;
        while (!director.Hud.ChoiceVisible)
        {
            if (Time.time > popupLimit) throw new System.Exception("no popup after the race");
            yield return null;
        }
        float inputReady = Time.time + 1.3f;
        while (Time.time < inputReady) yield return null;
        yield return ScreenShot("popup_back_focused");
        if (!GymRaceDirector.IsChoiceOpen) throw new System.Exception("director does not report the popup open");
        if (Cursor.lockState != CursorLockMode.None) throw new System.Exception($"cursor still {Cursor.lockState} on the popup");
        for (int b = 0; b < 2; b++)
        {
            // uGUI navigation would move the focus a second time per arrow press.
            if (director.Hud.ChoiceButtonForVerification(b).navigation.mode != UnityEngine.UI.Navigation.Mode.None)
                throw new System.Exception("popup buttons still use uGUI navigation");
        }
        yield return PressKey(Key.RightArrow);
        if (director.Hud.FocusedChoice != 1) throw new System.Exception($"Right arrow did not move focus to RACE AGAIN ({director.Hud.FocusedChoice})");
        yield return ScreenShot("popup_again_focused");
        yield return PressKey(Key.Enter);
        float rematchLimit = Time.time + 10f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            if (Time.time > rematchLimit) throw new System.Exception($"Enter on RACE AGAIN did not restart ({director.CurrentPhase})");
            yield return null;
        }
        if (Vector3.Distance(player.transform.position, buggyHolder.position) > 2.5f) throw new System.Exception("player left the buggy for the rematch");
        while (director.CurrentPhase != GymRaceDirector.Phase.Racing) yield return null;
        director.ForceResultsForVerification();
        popupLimit = Time.time + 5f;
        while (!director.Hud.ChoiceVisible)
        {
            if (Time.time > popupLimit) throw new System.Exception("no popup after the rematch");
            yield return null;
        }
        inputReady = Time.time + 1.3f;
        while (Time.time < inputReady) yield return null;
        if (director.Hud.FocusedChoice != 0) throw new System.Exception("popup did not reset focus to BACK TO GYM");
        // Mouse: a pointer at the button centre must hit the button (nothing
        // on top of it), and its click picks BACK TO GYM.
        UnityEngine.UI.Button backButton = director.Hud.ChoiceButtonForVerification(0);
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, backButton.transform.position);
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = screenPoint, button = PointerEventData.InputButton.Left };
        List<RaycastResult> hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(backButton.transform))
            throw new System.Exception($"pointer at BACK TO GYM hit {(hits.Count > 0 ? hits[0].gameObject.name : "nothing")}");
        pointer.pointerCurrentRaycast = hits[0];
        pointer.pointerPress = hits[0].gameObject;
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        written.Add("mouseBack=1");
        float backLimit = Time.time + 10f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Idle)
        {
            if (Time.time > backLimit) throw new System.Exception($"click on BACK TO GYM did not return ({director.CurrentPhase})");
            yield return null;
        }
        if (GameObject.Find(GymRaceTrackBuilder.RootName) != null) throw new System.Exception("track still present after BACK TO GYM");
        yield return null;
        yield return ScreenShot("back_in_gym");
        written.Add($"rematch=1 back=1 races={director.RacesRunForVerification}");
    }

    private static void LampAtlas(GymVehicleLights lamps, string label)
    {
        MeshFilter body = lamps.BodyFilter;
        if (body == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Vector3 origin = body.transform.position;
        Vector3 forward = lamps.transform.forward;
        Vector3 up = Vector3.up;
        Vector3 side = Vector3.Cross(up, forward).normalized;
        float minA = float.MaxValue, maxA = float.MinValue, minU = float.MaxValue, maxU = float.MinValue, maxS = 0f;
        Matrix4x4 toWorld = body.transform.localToWorldMatrix;
        foreach (Vector3 vertex in body.sharedMesh.vertices)
        {
            Vector3 w = toWorld.MultiplyPoint3x4(vertex) - origin;
            float a = Vector3.Dot(w, forward), h = Vector3.Dot(w, up);
            minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
            minU = Mathf.Min(minU, h); maxU = Mathf.Max(maxU, h);
            maxS = Mathf.Max(maxS, Mathf.Abs(Vector3.Dot(w, side)));
        }
        int layer = body.gameObject.layer;
        body.gameObject.layer = GymProductInspectView.PreviewLayer;
        GameObject rig = new GameObject("Lamp Atlas Camera");
        Camera camera = rig.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = (maxU - minU) * 0.5f;
        camera.cullingMask = 1 << GymProductInspectView.PreviewLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.5f, 0.5f, 0.55f);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 30f;
        int width = 640;
        int height = Mathf.Max(64, Mathf.RoundToInt(width * (maxU - minU) / (2f * maxS)));
        for (int end = 0; end < 2; end++)
        {
            float sign = end == 0 ? 1f : -1f;
            float face = end == 0 ? maxA + 4f : minA - 4f;
            rig.transform.SetPositionAndRotation(origin + up * ((minU + maxU) * 0.5f) + forward * face,
                Quaternion.LookRotation(-forward * sign, up));
            Render(camera, "atlas_" + label + (end == 0 ? "_front" : "_rear"), width, height);
        }
        Object.Destroy(rig);
        body.gameObject.layer = layer;
    }

    private static IEnumerator PressKey(Key key)
    {
        // Batch Unity has no focused Game view: let queued keys reach the game.
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Keyboard keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    // Main camera view with the HUD canvas, unlike the plain camera renders.
    private static IEnumerator ScreenShot(string name)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
        // Batch has no game view to grab: draw the HUD canvas through the
        // main camera for this one render, then put it back on the overlay.
        Camera camera = Camera.main;
        GameObject hudObject = GameObject.Find("Race HUD (Runtime)");
        Canvas canvas = hudObject != null ? hudObject.GetComponent<Canvas>() : null;
        if (camera == null) yield break;
        RenderTexture rt = new RenderTexture(1280, 720, 24);
        RenderTexture previous = camera.targetTexture;
        camera.targetTexture = rt;
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.05f;
        }
        Canvas.ForceUpdateCanvases();
        yield return null;
        camera.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = previous;
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "race");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        Object.Destroy(rt);
        Object.Destroy(image);
        written.Add(name);
    }

    private static void TopDown(string name)
    {
        GameObject go = new GameObject("Race Evidence Top Camera");
        Camera camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 128f;
        camera.transform.position = new Vector3(168f, 260f, 6f);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        camera.farClipPlane = 600f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.33f, 0.62f, 1f);
        Render(camera, name, 1400, 1400);
        Object.Destroy(go);
    }

    private static void Shot(string name, Vector3 eye, Vector3 target, float fov)
    {
        GameObject go = new GameObject("Race Evidence Camera");
        Camera camera = go.AddComponent<Camera>();
        camera.transform.position = eye;
        camera.transform.LookAt(target);
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 600f;
        Render(camera, name, 1280, 720);
        Object.Destroy(go);
    }

    private static void MainCamera(string name)
    {
        Camera camera = Camera.main;
        if (camera != null) Render(camera, name, 1280, 720);
    }

    private static void Render(Camera camera, string name, int width, int height)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        RenderTexture rt = new RenderTexture(width, height, 24);
        RenderTexture previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = previous;
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "race");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        written.Add(name);
        Object.Destroy(rt);
        Object.Destroy(image);
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RACE_CAPTURE_OK " + detail);
        else Debug.LogError("GYMCHAOS_RACE_CAPTURE_FAILED " + detail);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        routine = null;
        Time.timeScale = 1f;
        GymRaceCityCarver.Restore();
        GymRaceCorridorClearance.Restore();
        GymChaosVerifierClock.EndFixedStep();
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
}
#endif
