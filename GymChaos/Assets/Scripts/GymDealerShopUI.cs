using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Homeless Hank's stash: the protein.com inspect overlay cut down. One
/// product at a time as a turnable 3D model, A / D to browse, no flavours and
/// no categories, and less on the label: side effects and how long it lasts
/// read "?????", only a risk meter hints at how bad it is. Opens with F next
/// to Hank once his friendly talk ended in a yes
/// (<see cref="GymExperienceService.IsHankDealerUnlocked"/>).
/// </summary>
public sealed class GymDealerShopUI : MonoBehaviour
{
    private static readonly Vector3 StageOrigin = new Vector3(0f, -2000f, 400f);

    private static GymDealerShopUI instance;
    private static bool open;
    private static int lastClosedFrame = -1;

    private PlayerMovement player;
    private bool playerCursorWasCaptured;
    // Hank holds still while you browse, like in a conversation: his day
    // (walk, bench, tent) waits until the stash closes.
    private GymHomelessAgent dealer;
    private Canvas canvas;
    private CanvasGroup group;
    private RawImage modelView;
    private RectTransform infoPanel;
    private Vector2 infoPanelBase;
    private Text stampLabel;
    private Text activeLabel;
    private Text nameLabel;
    private Text effectLabel;
    private Text riskLabel;
    private Text priceLabel;
    private Text buyText;
    private Button buyButton;
    private Button backButton;
    private Button previousButton;
    private Button nextButton;
    private GymProductInspectView inspect;
    private int index;
    private int openedFrame;
    private float openedAt;
    private string lastResult;
    private float lastResultUntil;
    private bool dragStartedOnModel;

    public static bool IsOpen => open || lastClosedFrame == Time.frameCount;
    public int Index => index;
    public GymProteinProduct Current => GymDealerCatalog.Products[index];
    public GymProductInspectView InspectView => inspect;
    internal Text EffectLabelForVerification => effectLabel;
    internal Text RiskLabelForVerification => riskLabel;

    public enum PurchaseResult { Bought, Locked, NotEnoughStamps, TooManyActive, Unavailable }

    /// <summary>Hank sells only once unlocked, and only while he can talk.</summary>
    public static bool CanDealWith(PlayerMovement player)
    {
        GymExperienceService service = GymExperienceService.Active;
        return player != null && service != null && service.IsHankDealerUnlocked &&
            GymDialogueDirector.Active != null &&
            GymDialogueDirector.Active.FindNearbyHomeless(player) != null;
    }

    public static GymDealerShopUI Open(PlayerMovement player)
    {
        if (open || player == null)
        {
            return instance;
        }
        if (instance == null)
        {
            GameObject root = new GameObject("Dealer Shop UI");
            instance = root.AddComponent<GymDealerShopUI>();
            instance.Build();
        }
        instance.Show(player);
        return instance;
    }

    public static void Close()
    {
        if (instance != null && open)
        {
            instance.Hide();
        }
    }

    private void Build()
    {
        GymRuntimeSettings.EnsureEventSystem();
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 140;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        Color veilColor = PersonaMenuStyle.Veil;
        veilColor.a = 0.94f;
        Image veil = GymRuntimeSettings.CreateImage("Dealer Veil", transform, veilColor);
        GymRuntimeSettings.Stretch(veil.rectTransform);

        // Same frame as the protein.com shop, darker slab: this is not a store.
        RectTransform slab = PersonaMenuStyle.CreatePanel(transform, "Dealer Slab", 250f, 70f, 380f, 84f, -3f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Accent);
        Text slabText = PersonaMenuStyle.CreateLabel(slab, "Title", title, "THE STASH",
            0f, 0f, 360f, 84f, 58, PersonaMenuStyle.Accent, TextAnchor.MiddleCenter);
        GymShared.Center(slabText.rectTransform, 360f, 84f);
        stampLabel = PersonaMenuStyle.CreateLabel(transform, "Stamps", body, string.Empty,
            0f, 0f, 640f, 44f, 34, PersonaMenuStyle.Gold, TextAnchor.MiddleRight);
        GymShared.PlaceTopRight(stampLabel.rectTransform, 40f, 28f, 640f, 44f);
        activeLabel = PersonaMenuStyle.CreateLabel(transform, "Active", body, string.Empty,
            0f, 0f, 640f, 32f, 24, PersonaMenuStyle.Muted, TextAnchor.MiddleRight);
        GymShared.PlaceTopRight(activeLabel.rectTransform, 40f, 76f, 640f, 32f);

        RectTransform column = new GameObject("Dealer Column", typeof(RectTransform)).GetComponent<RectTransform>();
        column.SetParent(transform, false);
        PersonaMenuStyle.PlaceInArt(column, 836f, 525f, 1000f, 810f);

        inspect = new GymProductInspectView(StageOrigin, "Dealer Inspect Stage");
        GameObject viewObject = new GameObject("Dealer Product View", typeof(RectTransform), typeof(RawImage));
        viewObject.transform.SetParent(column, false);
        modelView = viewObject.GetComponent<RawImage>();
        modelView.texture = inspect.Texture;
        modelView.raycastTarget = false;
        PlaceInColumn(modelView.rectTransform, 0f, 230f, 460f, 460f);

        previousButton = CreateArrow(title, "<", -1);
        nextButton = CreateArrow(title, ">", 1);

        Text hint = PersonaMenuStyle.CreateLabel(column, "Hint", body,
            "A / D  BROWSE      DRAG  ROTATE      SCROLL  ZOOM", 0f, 0f, 900f, 30f, 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleCenter);
        PlaceInColumn(hint.rectTransform, 0f, 488f, 900f, 30f);

        infoPanel = PersonaMenuStyle.CreatePanel(column, "Dealer Info", 0f, 0f, 980f, 200f, -1f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Accent);
        PlaceInColumn(infoPanel, 0f, 616f, 980f, 200f);
        infoPanelBase = infoPanel.anchoredPosition;
        nameLabel = InfoLabel("Name", title, 16f, 900f, 66f, 56, PersonaMenuStyle.Ink);
        effectLabel = InfoLabel("Effect", body, 84f, 900f, 38f, 30, PersonaMenuStyle.Gold);
        riskLabel = InfoLabel("Risk", body, 122f, 900f, 34f, 28, PersonaMenuStyle.Accent);
        priceLabel = InfoLabel("Price", body, 158f, 900f, 34f, 28, PersonaMenuStyle.Ink);

        backButton = PersonaMenuStyle.CreateButton(column, "Dealer Back", body, "BACK   E", 0f, 0f, 260f, 62f,
            44, -1f, Hide);
        PlaceInColumn((RectTransform)backButton.transform, -276f, 769f, 260f, 62f);
        buyButton = PersonaMenuStyle.CreateButton(column, "Dealer Buy", body, "BUY   F", 0f, 0f, 260f, 62f,
            44, -1f, () => Buy());
        PlaceInColumn((RectTransform)buyButton.transform, 276f, 769f, 260f, 62f);
        buyText = buyButton.GetComponentInChildren<Text>();
        foreach (Button button in new[] { backButton, buyButton })
        {
            GymShared.FillOnFocus(button);
            GymShared.CenterLabel(button);
        }

        SetCanvasShown(false);
    }

    private static void PlaceInColumn(RectTransform rect, float centerX, float centerY, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(centerX * scale, -centerY * scale);
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    private Button CreateArrow(Font font, string glyph, int side)
    {
        const float size = 104f;
        const float gap = 28f;
        Button button = PersonaMenuStyle.CreateButton(transform, side < 0 ? "Dealer Previous" : "Dealer Next", font,
            glyph, 0f, 0f, size, size, 80, side * 2f, () => Step(side));
        RectTransform rect = (RectTransform)button.transform;
        rect.SetParent(modelView.rectTransform, false);
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(side * (gap + size * 0.5f) * scale, 0f);
        GymShared.FillOnFocus(button);
        GymShared.CenterLabel(button);
        return button;
    }

    private Text InfoLabel(string name, Font font, float top, float width, float height, int size, Color color)
    {
        Text text = PersonaMenuStyle.CreateLabel(infoPanel, name, font, string.Empty,
            0f, 0f, width, height, size, color, TextAnchor.MiddleLeft);
        RectTransform rect = text.rectTransform;
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(40f * scale, -top * scale);
        return text;
    }

    private void Show(PlayerMovement targetPlayer)
    {
        player = targetPlayer;
        playerCursorWasCaptured = player.CursorCaptured;
        player.SetCursorCaptured(false);
        GymHomelessAgent hank = GymHomelessAgent.Instance;
        dealer = hank != null && hank.BeginConversation(player.transform) ? hank : null;
        open = true;
        openedFrame = Time.frameCount;
        openedAt = Time.unscaledTime;
        lastResult = null;
        SetCanvasShown(true);
        inspect.SetActive(true);
        ShowProduct(index);
        GymAudio.Play2D(GymSoundEffect.UiClick, 0.5f);
        Debug.Log($"GYMCHAOS_DEALER_SHOP_OPEN product={Current.id} stamps={Stamps}");
    }

    private void Hide()
    {
        if (!open)
        {
            return;
        }
        open = false;
        lastClosedFrame = Time.frameCount;
        ReleaseDealer();
        SetCanvasShown(false);
        inspect.SetActive(false);
        if (player != null)
        {
            player.SetCursorCaptured(playerCursorWasCaptured);
        }
        GymAudio.Play2D(GymSoundEffect.UiBack, 0.45f);
        Debug.Log("GYMCHAOS_DEALER_SHOP_CLOSED");
    }

    public GymHomelessAgent PausedDealer => dealer;

    private void ReleaseDealer()
    {
        if (dealer != null)
        {
            dealer.EndConversation();
            dealer = null;
        }
    }

    private void SetCanvasShown(bool value)
    {
        canvas.enabled = value;
        group.alpha = 0f;
        group.blocksRaycasts = value;
        group.interactable = value;
        if (!value && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private static int Stamps => GymExperienceService.Active != null ? GymExperienceService.Active.LoyaltyStamps : 0;
    private static int Level => GymExperienceService.Active != null ? GymExperienceService.Active.Level : 1;

    public void Step(int direction)
    {
        int count = GymDealerCatalog.Products.Length;
        ShowProduct(((index + direction) % count + count) % count);
        GymAudio.Play2D(GymSoundEffect.UiClick, 0.35f);
    }

    private void ShowProduct(int newIndex)
    {
        index = newIndex;
        inspect.Show(Current.variants[0], false);
        Refresh();
    }

    public PurchaseResult Buy()
    {
        GymExperienceService service = GymExperienceService.Active;
        GymProteinProduct product = Current;
        PurchaseResult result;
        if (service == null)
        {
            result = PurchaseResult.Unavailable;
        }
        else if (Level < product.unlockLevel)
        {
            result = PurchaseResult.Locked;
        }
        else if (!GymBuffs.CanApply(product))
        {
            result = PurchaseResult.TooManyActive;
        }
        else if (!service.TrySpendLoyaltyStamps(product.stampPrice))
        {
            result = PurchaseResult.NotEnoughStamps;
        }
        else
        {
            GymBuffs.Apply(product, player);
            service.RegisterShadyPurchase(product.reputationCost);
            result = PurchaseResult.Bought;
        }

        lastResult = result == PurchaseResult.Bought ? "TAKEN  " + product.displayName :
            result == PurchaseResult.Locked ? "\"NOT FOR BEGINNERS. LEVEL " + product.unlockLevel + "\"" :
            result == PurchaseResult.TooManyActive ? "\"ONE THING AT A TIME, KID\"" :
            result == PurchaseResult.NotEnoughStamps ? "\"NO STAMPS, NO STASH\"" : "UNAVAILABLE";
        lastResultUntil = Time.unscaledTime + 2.2f;
        GymAudio.Play2D(result == PurchaseResult.Bought ? GymSoundEffect.ActionConfirm : GymSoundEffect.UiBack,
            result == PurchaseResult.Bought ? 0.6f : 0.45f);
        Debug.Log($"GYMCHAOS_DEALER_PURCHASE product={product.id} result={result} stamps={Stamps} " +
            $"toll={GymBuffs.MaxHealthToll01:F2}");
        Refresh();
        return result;
    }

    private void Refresh()
    {
        GymProteinProduct product = Current;
        bool locked = Level < product.unlockLevel;
        stampLabel.text = "HANK TAKES STAMPS   " + Stamps + " / " + GymExperienceService.MaxLoyaltyStamps;
        nameLabel.text = product.displayName;
        // Duration and the side effects stay unknown; only the risk meter
        // tells how hard it hits back.
        effectLabel.text = product.effectText + "   " + GymDealerCatalog.Unknown + " MIN";
        int severity = GymDealerCatalog.GetSeverity(product);
        riskLabel.text = "SIDE EFFECTS  " + new string('#', severity) + new string('-', 5 - severity) +
            "   " + (severity >= 4 ? "SEVERE" : severity >= 2 ? "REAL" : GymDealerCatalog.Unknown);
        if (lastResult != null && Time.unscaledTime < lastResultUntil)
        {
            priceLabel.text = lastResult;
            priceLabel.color = PersonaMenuStyle.Accent;
        }
        else
        {
            priceLabel.text = locked
                ? "LOCKED   LEVEL " + product.unlockLevel
                : product.stampPrice + " STAMPS   -" + product.reputationCost + " KARMA" +
                  (GymBuffs.IsActive(product.id) ? "   STILL IN YOUR SYSTEM" : string.Empty);
            priceLabel.color = locked ? PersonaMenuStyle.Muted : PersonaMenuStyle.Ink;
        }
        bool affordable = !locked && Stamps >= product.stampPrice && GymBuffs.CanApply(product);
        buyText.color = affordable ? Color.white : PersonaMenuStyle.Muted;
        modelView.color = locked ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white;

        System.Text.StringBuilder active = new System.Text.StringBuilder();
        var buffs = GymBuffs.Active;
        for (int i = 0; i < buffs.Count; i++)
        {
            active.Append(buffs[i].crash ? buffs[i].product.displayName + " CRASH" : buffs[i].product.displayName)
                .Append("     ");
        }
        if (GymBuffs.MaxHealthToll01 > 0f)
        {
            active.Append("MAX HP -").Append(Mathf.RoundToInt(GymBuffs.MaxHealthToll01 * 100f)).Append('%');
        }
        activeLabel.text = active.ToString();
    }

    private void Update()
    {
        if (!open)
        {
            return;
        }
        if (player == null || player.IsDead || GymPauseMenu.IsVisible || GymStartScreen.IsMenuVisible)
        {
            Hide();
            return;
        }

        float reveal = Mathf.Clamp01((Time.unscaledTime - openedAt) / PersonaMenuStyle.TransitionSeconds);
        group.alpha = reveal;
        infoPanel.anchoredPosition = infoPanelBase + new Vector2(0f, (1f - reveal) * -40f);

        inspect.Tick(ReadDrag(), ReadScroll(), Time.unscaledDeltaTime);
        if (Time.frameCount % 15 == 0)
        {
            Refresh();
        }

        // The F that opened the stash must not also buy.
        if (Time.frameCount == openedFrame)
        {
            return;
        }
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k == null)
        {
            return;
        }
        if (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame) Step(-1);
        else if (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame) Step(1);
        else if (k.fKey.wasPressedThisFrame) Buy();
        else if (k.eKey.wasPressedThisFrame || k.qKey.wasPressedThisFrame ||
            k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) Hide();
#else
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Step(-1);
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Step(1);
        else if (Input.GetKeyDown(KeyCode.F)) Buy();
        else if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Q) ||
            Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) Hide();
#endif
    }

    private Vector2 ReadDrag()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed)
        {
            dragStartedOnModel = false;
            return Vector2.zero;
        }
        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragStartedOnModel = RectTransformUtility.RectangleContainsScreenPoint(modelView.rectTransform,
                mouse.position.ReadValue(), null);
        }
        return dragStartedOnModel ? mouse.delta.ReadValue() : Vector2.zero;
#else
        return Input.GetMouseButton(0)
            ? new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f
            : Vector2.zero;
#endif
    }

    private static float ReadScroll()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            return 0f;
        }
        float y = Mouse.current.scroll.ReadValue().y;
        return Mathf.Abs(y) >= 10f ? y / 120f : y;
#else
        return Input.mouseScrollDelta.y;
#endif
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            ReleaseDealer();
            instance = null;
            open = false;
        }
        inspect?.Dispose();
    }
}
