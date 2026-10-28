#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// protein.com counter shop. At the customer side of the counter with Mark at
/// his post, F offers SHOP (E stays TALK); away from the counter it does not.
/// The overlay blocks gameplay input, browses all nine products as loaded 3D
/// models on the preview layer and turns them. Buying checks the level lock,
/// the stamp price and the active-supplement cap; effects scale the real
/// progression getters, refresh instead of stacking, expire back to 1 and
/// pre-workout leaves its crash. Stamps respect the daily cap.
/// Logs GYMCHAOS_PROTEIN_SHOP_OK and GYMCHAOS_PROTEIN_SHOP_BUFF_OK.
/// </summary>
public static class GymChaosProteinShopVerifier
{
    private const string RequestedKey = "GymChaos.ProteinShopVerificationRequested";
    private const string ProgressionKey = "GymChaos.Progression.v1";
    private const string ProgressionBackupKey = "GymChaos.ProteinShopVerifier.ProgressionBackup";
    private const string ProgressionHadKey = "GymChaos.ProteinShopVerifier.ProgressionHad";
    private const string SessionDirectoryKey = "GymChaos.ProteinShopVerifier.SessionDirectory";

    private static IEnumerator script;
    private static double started;
    private static double waitUntil;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);
    }

    public static void Run()
    {
        SessionState.SetBool(ProgressionHadKey, PlayerPrefs.HasKey(ProgressionKey));
        SessionState.SetString(ProgressionBackupKey, PlayerPrefs.GetString(ProgressionKey, string.Empty));
        PlayerPrefs.DeleteKey(ProgressionKey);
        if (!Application.isBatchMode)
        {
            // In the open Editor the boot menu owns character slots: point
            // them at a throwaway folder so the user's saves are never touched.
            string directory = Path.Combine(Path.GetTempPath(), "GymChaosProteinShopVerify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SessionState.SetString(SessionDirectoryKey, directory);
            Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, directory);
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            waitUntil = 0d;
            AudioListener.pause = true;
            // The open Editor stops playing when it loses focus unless the
            // player is allowed to run in the background.
            Application.runInBackground = true;
            script = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        RestoreProgression();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || script == null) return;
        AudioListener.pause = true;
        // An unfocused open Editor throttles play mode; keep frames coming.
        if (!Application.isBatchMode) EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup - started > 240d)
                throw new InvalidOperationException("Protein shop verification timed out.");
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (!script.MoveNext())
            {
                script = null;
                Debug.Log("GYMCHAOS_PROTEIN_SHOP_BUFF_OK");
                Finish(0);
                return;
            }
            if (script.Current is float seconds)
                waitUntil = EditorApplication.timeSinceStartup + seconds;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_PROTEIN_SHOP_FAILED " + exception.Message);
            script = null;
            GymProteinShopUI.Close();
            Finish(1);
        }
    }

    private static IEnumerator Scenario()
    {
        if (GymSessionService.SessionMode)
        {
            // Through the real boot menu: Play, New Game, confirm the class.
            foreach (object step in Until(() => GymStartScreen.IsMenuVisible && GymStartScreen.SessionMenu != null &&
                         GymExperienceService.Active != null, 120d, "the boot menu")) yield return step;
            yield return 0.5f;
            GymSessionMenu menu = GymStartScreen.SessionMenu;
            GymStartScreen.PlayButton.onClick.Invoke();
            foreach (object step in Until(() => menu.Current == GymSessionMenu.State.SessionChoice, 20d,
                         "the session choice")) yield return step;
            // Each menu page ignores input for a moment after it opens; keep
            // pressing until the page actually advances.
            foreach (object step in Until(() =>
                     {
                         menu.ChooseNewGame();
                         return menu.Current == GymSessionMenu.State.ClassSelect;
                     }, 20d, () => "class selection (menu=" + menu.Current + " frame=" + Time.frameCount +
                         " unscaled=" + Time.unscaledTime.ToString("F2") + ")")) yield return step;
            foreach (object step in Until(() =>
                     {
                         menu.ConfirmClass();
                         return menu.Current != GymSessionMenu.State.ClassSelect;
                     }, 20d, "the class confirmation")) yield return step;
            foreach (object step in Until(() => GymArenaBootstrap.IsGameplayStarted, 60d, "gameplay after the menu"))
                yield return step;
        }
        foreach (object step in Until(() => GymProteinStoreEnvironment.IsLoaded &&
                     GymExperienceService.Active != null && GymExperienceService.Active.State != null &&
                     GymDialogueDirector.Active != null && FindMark() != null,
                     120d, "the protein store and Mark")) yield return step;
        EnemyFighter mark = FindMark();
        foreach (object step in Until(() => Flat(mark.transform.position, GymProteinStoreEnvironment.StoreWorkerPoint) <=
                     GymProteinShopUI.WorkerPostRange, 60d, "Mark at the counter")) yield return step;
        GymExperienceService service = GymExperienceService.Active;
        GymProgressionState state = service.State;
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        Require(player != null, "No player");
        GymBuffs.Clear();

        // --- F at the counter -----------------------------------------------
        Teleport(player, GymProteinStoreEnvironment.StoreFrontClearPoint);
        player.LookAtForVerification(mark.transform.position + Vector3.up * 1.5f);
        PlayerMovement.ContextAction atCounter = player.ResolveFActionForVerification();
        Require(player.NearbyTalkTargetForVerification == mark,
            $"Mark is not the talk target at the counter ({player.NearbyTalkTargetForVerification})");
        Require(atCounter == PlayerMovement.ContextAction.Shop, "F at the counter offers " + atCounter);
        Require(PlayerMovement.ResolveContextAction(PlayerMovement.ContextKey.E, true, false, false, false, false,
                false, false, true) == PlayerMovement.ContextAction.Talk, "E at the counter is not TALK");

        // Away from the counter (still in the store) F must not open the shop.
        Vector3 away = GymProteinStoreEnvironment.StoreFrontClearPoint +
            Vector3.ProjectOnPlane(GymProteinStoreEnvironment.StoreFrontClearPoint - mark.transform.position,
                Vector3.up).normalized * 3.5f;
        Teleport(player, away);
        player.LookAtForVerification(mark.transform.position + Vector3.up * 1.5f);
        PlayerMovement.ContextAction awayAction = player.ResolveFActionForVerification();
        Require(awayAction != PlayerMovement.ContextAction.Shop, "F offers SHOP 3.5 m away from the counter");
        Teleport(player, GymProteinStoreEnvironment.StoreFrontClearPoint);
        player.LookAtForVerification(mark.transform.position + Vector3.up * 1.5f);

        // --- overlay ----------------------------------------------------------
        int stampsBefore = service.LoyaltyStamps;
        GymProteinShopUI shop = GymProteinShopUI.Open(player);
        yield return 0.2f;
        Require(shop != null && GymProteinShopUI.IsOpen, "Shop did not open");
        Require(service.IsBlockingPlayerInput, "Open shop does not block gameplay input");
        Require(!player.CursorCaptured, "Cursor stays captured while the shop is open");
        Require(service.LoyaltyStamps == stampsBefore + 1, "First counter visit of the day gave no stamp");
        Require(ScreenSpaceCharacterLabel.CurrentAlpha < 0.5f, "Name labels stay opaque over the open shop");
        if (GymHud.Active != null)
            Require(GymHud.Active.IsHiddenForVerification, "Gameplay HUD stays on screen over the open shop");

        // Layout: product view, hint, info plate, buttons and the header
        // never overlap, at the live screen size and at 4:3, 16:10, 16:9 and 21:9.
        Button[] buttons = shop.ButtonsForVerification;
        CheckLayout(shop, new Rect(0f, 0f, Screen.width, Screen.height), "screen");
        foreach (float aspect in new[] { 4f / 3f, 16f / 10f, 16f / 9f, 21f / 9f })
            CheckAspect(shop, aspect);

        // Hovered/focused buttons stay one filled red plate (rim = face).
        foreach (Button button in buttons)
        {
            // Start each button from nothing selected, so OnSelect always fires.
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            yield return 0.35f;
            PersonaMenuButton persona = button.GetComponent<PersonaMenuButton>();
            Color rim = persona.CurrentOutlineColorForVerification;
            Color face = persona.CurrentFaceColorForVerification;
            Require(persona.IsFocusedForVerification, button.name + " did not take focus");
            Require(Mathf.Abs(rim.r - face.r) + Mathf.Abs(rim.g - face.g) + Mathf.Abs(rim.b - face.b) < 0.05f,
                $"{button.name} focus shows a band: rim={rim} face={face}");
            if (HasGraphics && (button.name == "Shop Buy" || button.name == "Shop Next" ||
                                button.name == "Shop Flavour Next"))
            {
                CaptureOverlay(shop, "shop-overlay-hover-" +
                    button.name.Substring(5).Replace(" ", "-").ToLowerInvariant() + ".png");
                yield return 0.2f;
            }
        }
        EventSystem.current.SetSelectedGameObject(null);

        // Traffic is silent inside the store, as inside the gym.
        Require(GymProteinStoreEnvironment.IsInsideStore(player.transform.position), "Counter is not inside the store");
        Require(!GymProteinStoreEnvironment.IsInsideStore(GymProteinStoreEnvironment.StoreFrontClearPoint +
            Vector3.right * -40f), "Far outside counts as inside the store");
        GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None);
        int audibleVehicles = 0;
        foreach (GymVisitorVehicle vehicle in vehicles)
            if (!vehicle.IsEngineMutedForVerification) audibleVehicles++;
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_TRAFFIC vehicles={vehicles.Length} audible={audibleVehicles}");
        Require(audibleVehicles == 0, audibleVehicles + " vehicle engines are audible inside the store");

        HashSet<string> seen = new HashSet<string>();
        int productCount = GymProteinShopCatalog.Products.Length;
        Require(productCount == 10, "Catalogue has " + productCount + " products");
        int variantCount = 0;
        for (int i = 0; i < productCount; i++)
        {
            GymProductInspectView view = shop.InspectView;
            GymProteinProduct product = shop.Current;
            Require(product.variants != null && product.variants.Length > 0, product.id + " has no variants");
            Require(shop.VariantIndex == 0, product.id + " did not open on its first flavour");
            // W / S: every flavour and pack of this type, then back to the first.
            for (int v = 0; v < product.variants.Length; v++)
            {
                string id = shop.CurrentVariant.modelId;
                foreach (object step in Until(() => view.ModelFitted && view.ModelId == id, 20d,
                             "the fitted 3D model of " + id)) yield return step;
                Require(view.FittedScale > 0.5f, $"{id} was not scaled to the inspect size ({view.FittedScale})");
                GameObject model = GameObject.Find("Inspect " + id);
                Require(model != null, "No inspect model object for " + id);
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
                Require(renderers.Length > 0, id + " model has no renderers");
                foreach (Renderer renderer in renderers)
                    Require(renderer.gameObject.layer == GymProductInspectView.PreviewLayer,
                        id + " renderer is not on the preview layer");
                Require(shop.VariantLabelForVerification.GetComponent<Text>().text
                        .StartsWith(product.variants[v].label), id + " flavour label is wrong");
                Require(seen.Add(id), "Variant shown twice: " + id);
                if (HasGraphics) CaptureInspect(view, id);
                variantCount++;
                shop.StepVariant(1);
                yield return null;
            }
            Require(shop.VariantIndex == 0, product.id + " flavours did not wrap");
            float yawBefore = view.Yaw;
            view.RotateForVerification(45f);
            Require(Mathf.Abs(Mathf.DeltaAngle(yawBefore, view.Yaw) - 45f) < 0.01f, "Rotation did not turn " + product.id);
            shop.Step(1);
            yield return null;
        }
        Require(variantCount == 31, "Shop shows " + variantCount + " variants, expected all 31 shelf products");
        // Menu order: whey pouches before tubs; creatine pouch, monohydrate, reload;
        // no bar boxes; energy drinks and shakes are separate products.
        GymProteinVariant[] whey = GymProteinShopCatalog.Find("tub_whey").variants;
        for (int w = 0; w < whey.Length; w++)
            Require(whey[w].modelId.StartsWith(w < 6 ? "pouch_" : "tub_"), "Whey order wrong at " + w + ": " + whey[w].modelId);
        GymProteinVariant[] creatineOrder = GymProteinShopCatalog.Find("tub_creatine").variants;
        Require(creatineOrder[0].modelId == "pouch_rebel" && creatineOrder[1].modelId == "tub_creatine" &&
                creatineOrder[2].modelId == "tub_reload", "Creatine order is not pouch, monohydrate, reload");
        foreach (GymProteinProduct type in GymProteinShopCatalog.Products)
            foreach (GymProteinVariant variant in type.variants)
                Require(!variant.modelId.StartsWith("barbox_"), "Bar box still on sale: " + variant.modelId);
        Require(GymProteinShopCatalog.Find("can_lime").variants.Length == 4 &&
                GymProteinShopCatalog.Find("rtd_choc").variants.Length == 2, "Energy drinks and shakes are not split");

        // One wheel notch zooms 22%; the camera follows within a few frames.
        GymProductInspectView zoomView = shop.InspectView;
        float before = zoomView.TargetDistance;
        zoomView.ZoomBy(1f);
        Require(zoomView.TargetDistance < before * 0.8f, $"Zoom notch too weak ({before:F2} -> {zoomView.TargetDistance:F2})");
        zoomView.ZoomBy(-1f);
        Require(Mathf.Abs(zoomView.TargetDistance - before) < 0.001f, "Zoom out did not return");
        Require(shop.Index == 0, "Next did not wrap back to the first product");
        if (HasGraphics)
        {
            yield return 0.3f;
            CaptureOverlay(shop, "shop-overlay.png");
            yield return 0.3f;
        }
        shop.Step(-1);
        Require(shop.Index == productCount - 1, "Previous did not wrap to the last product");
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_OK products={productCount} variants={seen.Count} stamps={service.LoyaltyStamps}");

        // --- purchase rules -------------------------------------------------------
        state.reputation = 0;
        state.level = 1;
        state.loyaltyStamps = 0;
        SelectProduct(shop, "tub_creatine");
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Locked, "Creatine was not locked at level 1");
        state.level = 10;
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.NotEnoughStamps, "Creatine sold without stamps");
        state.loyaltyStamps = 20;
        float baseDamage = service.ScaleStrengthDamage(10f);
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Bought, "Creatine purchase failed");
        Require(service.LoyaltyStamps == 17, "Creatine cost " + (20 - service.LoyaltyStamps) + " stamps, expected 3");
        float boosted = service.ScaleStrengthDamage(10f);
        Require(Mathf.Abs(boosted / baseDamage - 1.15f) < 0.001f, $"Creatine damage ratio {boosted / baseDamage}");
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Bought, "Creatine refresh failed");
        Require(Mathf.Abs(service.ScaleStrengthDamage(10f) / baseDamage - 1.15f) < 0.001f, "Creatine stacked");
        Require(GymBuffs.ActiveProductCount == 1, "Refresh added a second creatine effect");

        SelectProduct(shop, "tub_whey");
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Bought, "Whey purchase failed");
        SelectProduct(shop, "tub_bcaa");
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Bought, "BCAA purchase failed");
        Require(Mathf.Abs(service.GetTechniqueDifficultyScale(1f) -
                0.75f * GetUnbuffedTechnique(service)) < 0.01f, "BCAA did not ease technique timing");
        int stampsAtCap = service.LoyaltyStamps;
        SelectProduct(shop, "vit_d3");
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.TooManyActive, "A fourth supplement was allowed");
        Require(service.LoyaltyStamps == stampsAtCap, "A refused purchase took stamps");

        GymBuffs.ExpireAllForVerification();
        Require(GymBuffs.ActiveProductCount == 0, "Effects did not expire");
        Require(Mathf.Abs(service.ScaleStrengthDamage(10f) - baseDamage) < 0.001f, "Damage did not return to base");

        // Pre-workout: boost, then the crash, then nothing.
        SelectProduct(shop, "tub_pre_green");
        float sprintBase = GymBuffs.GetMultiplier(GymBuffStat.SprintSpeed);
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Bought, "Pre-workout purchase failed");
        Require(Mathf.Abs(GymBuffs.GetMultiplier(GymBuffStat.SprintSpeed) - 1.2f) < 0.001f, "Pre-workout sprint boost");
        Require(Mathf.Abs(GymBuffs.GetMultiplier(GymBuffStat.SprintDrain) - 0.7f) < 0.001f, "Pre-workout sprint drain");
        GymBuffs.ExpireAllForVerification();
        Require(Mathf.Abs(GymBuffs.GetMultiplier(GymBuffStat.SprintSpeed) - 0.9f) < 0.001f, "Pre-workout crash missing");
        GymBuffs.ExpireAllForVerification();
        Require(Mathf.Abs(GymBuffs.GetMultiplier(GymBuffStat.SprintSpeed) - sprintBase) < 0.001f, "Crash did not end");

        // Protein bar heals at once.
        player.ReceiveEnemyPunch(60f, Vector3.zero, null);
        float healthBefore = player.CurrentHealth;
        SelectProduct(shop, "bar_choc");
        Require(shop.Buy() == GymProteinShopUI.PurchaseResult.Bought, "Protein bar purchase failed");
        Require(player.CurrentHealth >= healthBefore + 39.9f, $"Bar healed {player.CurrentHealth - healthBefore}");
        GymBuffs.Clear();

        // Reputation moves the price.
        GymProteinProduct creatine = GymProteinShopCatalog.Find("tub_creatine");
        Require(GymProteinShopCatalog.GetPrice(creatine, 60) == 2 && GymProteinShopCatalog.GetPrice(creatine, -60) == 4,
            "Reputation does not move the creatine price");

        // Daily stamp cap.
        state.loyaltyStamps = 0;
        state.loyaltyStampDay = state.gymDay;
        state.loyaltyStampsToday = 0;
        int added = service.AwardLoyaltyStamps(20, "verifier");
        Require(added == GymExperienceService.DailyLoyaltyStampCap, "Daily cap let " + added + " stamps through");
        Require(service.AwardLoyaltyStamps(1, "verifier") == 0, "Stamps past the daily cap");

        // Name labels are IMGUI and only show in a real game-view capture,
        // which the open Editor has: Mark's label near-invisible over the
        // open shop, readable again once it closes.
        bool nameCapture = !Application.isBatchMode;
        Rect nameRegion = default;
        if (nameCapture)
        {
            nameRegion = NameLabelRegion(mark);
            ScreenCapture.CaptureScreenshot(Path.Combine(CaptureFolder, "shop-nametag-open.png"));
            yield return 1f;
        }
        GymProteinShopUI.Close();
        yield return null;
        yield return null;
        Require(!GymProteinShopUI.IsOpen, "Shop did not close");
        if (nameCapture)
        {
            yield return 0.5f;
            ScreenCapture.CaptureScreenshot(Path.Combine(CaptureFolder, "shop-nametag-closed.png"));
            yield return 1f;
            int open = CountLabelPixels(Path.Combine(CaptureFolder, "shop-nametag-open.png"), nameRegion);
            int closed = CountLabelPixels(Path.Combine(CaptureFolder, "shop-nametag-closed.png"), nameRegion);
            Debug.Log($"GYMCHAOS_PROTEIN_SHOP_NAMETAG open={open} closed={closed} region={nameRegion}");
            Require(closed >= 12, "Mark's name label was not found in the closed capture (" + closed + " px)");
            Require(open * 4 <= closed, $"Mark's name label is not faded over the shop (open={open} closed={closed})");
        }
        Require(!service.IsBlockingPlayerInput, "Closed shop still blocks input");
        Require(ScreenSpaceCharacterLabel.CurrentAlpha > 0.99f, "Name labels stay faded after the shop closed");
    }

    private static void CheckLayout(GymProteinShopUI shop, Rect bounds, string label)
    {
        Rect viewRect = ScreenRect(shop.ModelViewForVerification);
        Rect hint = ScreenRect(shop.HintForVerification);
        Rect info = ScreenRect(shop.InfoPanelForVerification);
        Rect flavour = ScreenRect(shop.VariantLabelForVerification);
        Require(flavour.yMax <= viewRect.yMin + 0.5f && flavour.yMin >= hint.yMax - 0.5f,
            $"[{label}] flavour row is not between the product and the hint (view={viewRect} flavour={flavour} hint={hint})");
        Require(hint.yMax <= viewRect.yMin + 0.5f && hint.yMin >= info.yMax - 0.5f,
            $"[{label}] hint is not between the product and the info plate (view={viewRect} hint={hint} info={info})");
        var rects = new List<KeyValuePair<string, Rect>>();
        Button[] buttons = shop.ButtonsForVerification;
        for (int b = 0; b < buttons.Length; b++)
        {
            Rect rect = ScreenRect((RectTransform)buttons[b].transform);
            Require(!rect.Overlaps(info) && !rect.Overlaps(hint),
                $"[{label}] {buttons[b].name} overlaps the info plate or hint");
            Require(b >= 2 || !rect.Overlaps(viewRect), $"[{label}] {buttons[b].name} covers the product view");
            Text glyph = buttons[b].GetComponentInChildren<Text>();
            Require(glyph.alignment == TextAnchor.MiddleCenter && glyph.rectTransform.offsetMin == Vector2.zero,
                $"[{label}] {buttons[b].name} label is not centred");
            rects.Add(new KeyValuePair<string, Rect>(buttons[b].name, rect));
        }
        foreach (RectTransform header in shop.HeaderForVerification)
        {
            Rect rect = ScreenRect(header);
            Require(!rect.Overlaps(viewRect), $"[{label}] {header.name} overlaps the product view");
            rects.Add(new KeyValuePair<string, Rect>(header.name, rect));
        }
        rects.Add(new KeyValuePair<string, Rect>("hint", hint));
        rects.Add(new KeyValuePair<string, Rect>("flavour", flavour));
        for (int a = 0; a < rects.Count; a++)
        {
            Rect r = rects[a].Value;
            Require(r.xMin >= bounds.xMin - 0.5f && r.xMax <= bounds.xMax + 0.5f &&
                    r.yMin >= bounds.yMin - 0.5f && r.yMax <= bounds.yMax + 0.5f,
                $"[{label}] {rects[a].Key} runs off the screen ({r} outside {bounds})");
            for (int c = a + 1; c < rects.Count; c++)
                Require(!r.Overlaps(rects[c].Value), $"[{label}] {rects[a].Key} overlaps {rects[c].Key}");
        }
    }

    /// <summary>
    /// Lays the shop canvas out at another aspect (it matches height, so it is
    /// 1080 units tall and 1080 x aspect wide) as a world-space canvas, checks
    /// the layout and renders it to Logs/agent/shop-aspect-W.png.
    /// </summary>
    private static void CheckAspect(GymProteinShopUI shop, float aspect)
    {
        Canvas canvas = shop.CanvasForVerification;
        RectTransform root = (RectTransform)canvas.transform;
        int width = Mathf.RoundToInt(1080f * aspect);
        Vector3 origin = new Vector3(0f, -3000f, 0f);
        canvas.renderMode = RenderMode.WorldSpace;
        root.localScale = Vector3.one;
        root.rotation = Quaternion.identity;
        root.sizeDelta = new Vector2(width, 1080f);
        root.position = origin;
        Canvas.ForceUpdateCanvases();
        GameObject cameraObject = new GameObject("Shop Aspect Capture Camera");
        RenderTexture target = new RenderTexture(width / 2, 540, 24, RenderTextureFormat.ARGB32);
        try
        {
            CheckLayout(shop, new Rect(origin.x - width / 2f, origin.y - 540f, width, 1080f), width + "x1080");
            if (!HasGraphics) return;
            target.Create();
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 540f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.25f, 0.27f, 0.3f, 1f);
            camera.enabled = false;
            cameraObject.transform.position = origin + new Vector3(0f, 0f, -10f);
            shop.InspectView.CameraForVerification.SubmitRenderRequest(
                new RenderPipeline.StandardRequest { destination = shop.InspectView.Texture });
            camera.SubmitRenderRequest(new RenderPipeline.StandardRequest { destination = target });
            Texture2D image = ReadBack(target, TextureFormat.RGB24);
            Directory.CreateDirectory(CaptureFolder);
            File.WriteAllBytes(Path.Combine(CaptureFolder, "shop-aspect-" + width + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }
        finally
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UnityEngine.Object.Destroy(cameraObject);
            target.Release();
            UnityEngine.Object.Destroy(target);
            Canvas.ForceUpdateCanvases();
        }
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_ASPECT_OK width={width}");
    }

    /// <summary>Screen-capture rect (top-left origin) where Mark's IMGUI name is drawn.</summary>
    private static Rect NameLabelRegion(EnemyFighter mark)
    {
        Renderer body = mark.GetComponentInChildren<SkinnedMeshRenderer>();
        Bounds bounds = body != null
            ? body.bounds
            : new Bounds(mark.transform.position + Vector3.up * 1.9f, Vector3.zero);
        Vector3 screen = Camera.main.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
        float guiY = Screen.height - screen.y;
        return Rect.MinMaxRect(screen.x - 110f, guiY - 70f, screen.x + 110f, guiY + 10f);
    }

    private static int CountLabelPixels(string path, Rect region)
    {
        Texture2D image = new Texture2D(2, 2);
        image.LoadImage(File.ReadAllBytes(path));
        int count = 0;
        int x0 = Mathf.Clamp((int)region.xMin, 0, image.width - 1);
        int x1 = Mathf.Clamp((int)region.xMax, 0, image.width - 1);
        int y0 = Mathf.Clamp((int)region.yMin, 0, image.height - 1);
        int y1 = Mathf.Clamp((int)region.yMax, 0, image.height - 1);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                // Texture rows run bottom-up; the region is top-down.
                Color c = image.GetPixel(x, image.height - 1 - y);
                if (c.r > 0.92f && c.g > 0.92f && c.b > 0.92f) count++;
            }
        }
        UnityEngine.Object.Destroy(image);
        return count;
    }

    private static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
    private static string CaptureFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "agent"));

    /// <summary>Writes the inspect RenderTexture as-is (with alpha) and reports its coverage.</summary>
    private static void CaptureInspect(GymProductInspectView view, string id)
    {
        Camera camera = view.CameraForVerification;
        camera.SubmitRenderRequest(new RenderPipeline.StandardRequest { destination = view.Texture });
        Texture2D image = ReadBack(view.Texture, TextureFormat.RGBA32);
        Color32[] pixels = image.GetPixels32();
        int transparent = 0, lit = 0;
        foreach (Color32 pixel in pixels)
        {
            if (pixel.a < 8) transparent++;
            if (pixel.r + pixel.g + pixel.b > 120) lit++;
        }
        Directory.CreateDirectory(CaptureFolder);
        string path = Path.Combine(CaptureFolder, "shop-inspect-" + id + ".png");
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_CAPTURE product={id} transparent={transparent / (float)pixels.Length:F2} " +
            $"lit={lit / (float)pixels.Length:F3} path={path}");
        Require(lit > pixels.Length / 200, id + " inspect render shows almost nothing");
    }

    /// <summary>Renders the game camera with the shop canvas composited on top, as the player sees it.</summary>
    private static void CaptureOverlay(GymProteinShopUI shop, string fileName)
    {
        if (!Application.isBatchMode)
        {
            // The open Editor has a real game view: capture exactly what the
            // player sees (written at the end of this frame).
            Directory.CreateDirectory(CaptureFolder);
            string screenshot = Path.Combine(CaptureFolder, fileName);
            ScreenCapture.CaptureScreenshot(screenshot);
            Debug.Log("GYMCHAOS_PROTEIN_SHOP_OVERLAY_CAPTURE path=" + screenshot);
            return;
        }
        Camera main = Camera.main;
        Require(main != null, "No main camera for the overlay capture");
        Canvas canvas = shop.CanvasForVerification;
        GymProductInspectView view = shop.InspectView;
        view.CameraForVerification.SubmitRenderRequest(new RenderPipeline.StandardRequest { destination = view.Texture });
        RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        RenderMode mode = canvas.renderMode;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = main;
        canvas.planeDistance = main.nearClipPlane + 0.05f;
        Canvas.ForceUpdateCanvases();
        try
        {
            main.SubmitRenderRequest(new RenderPipeline.StandardRequest { destination = target });
            Texture2D image = ReadBack(target, TextureFormat.RGB24);
            Directory.CreateDirectory(CaptureFolder);
            string path = Path.Combine(CaptureFolder, fileName);
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            Debug.Log("GYMCHAOS_PROTEIN_SHOP_OVERLAY_CAPTURE path=" + path);
        }
        finally
        {
            canvas.renderMode = mode;
            canvas.worldCamera = null;
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
    }

    private static Texture2D ReadBack(RenderTexture source, TextureFormat format)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        Texture2D image = new Texture2D(source.width, source.height, format, false);
        image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        image.Apply(false, false);
        RenderTexture.active = previous;
        return image;
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float xMin = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        float xMax = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        float yMin = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
        float yMax = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static float GetUnbuffedTechnique(GymExperienceService service) =>
        service.GetTechniqueDifficultyScale(1f) / GymBuffs.GetMultiplier(GymBuffStat.TechniqueDifficulty);

    private static void SelectProduct(GymProteinShopUI shop, string id)
    {
        for (int i = 0; i < GymProteinShopCatalog.Products.Length && shop.Current.id != id; i++)
            shop.Step(1);
        Require(shop.Current.id == id, "Could not select " + id);
    }

    private static void Teleport(PlayerMovement player, Vector3 position)
    {
        position.y = player.transform.position.y;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = position;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static EnemyFighter FindMark()
    {
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Mark && fighter.isActiveAndEnabled &&
                !fighter.IsDead) return fighter;
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable Until(Func<bool> condition, double timeoutSeconds, string what) =>
        Until(condition, timeoutSeconds, () => what);

    private static IEnumerable Until(Func<bool> condition, double timeoutSeconds, Func<string> what)
    {
        double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new InvalidOperationException("Timed out waiting for " + what());
            yield return null;
        }
    }

    private static void RestoreProgression()
    {
        string directory = SessionState.GetString(SessionDirectoryKey, string.Empty);
        if (!string.IsNullOrEmpty(directory))
        {
            Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, null);
            SessionState.EraseString(SessionDirectoryKey);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException) { }
        }
        if (!SessionState.GetBool(ProgressionHadKey, false))
            PlayerPrefs.DeleteKey(ProgressionKey);
        else
            PlayerPrefs.SetString(ProgressionKey, SessionState.GetString(ProgressionBackupKey, string.Empty));
        PlayerPrefs.Save();
    }

    private static void Finish(int code)
    {
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        GymBuffs.Clear();
        RestoreProgression();
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
