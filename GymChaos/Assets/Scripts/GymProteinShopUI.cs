using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// protein.com counter shop. F at the counter (with Mark behind it) opens a
/// Resident Evil style inspect overlay: one product at a time as a real 3D
/// model the player can turn and zoom, &lt; and &gt; to browse, and Buy pays
/// with loyalty-card stamps. Prices, unlocks and effects live in
/// <see cref="GymProteinShopCatalog"/>, the effects in <see cref="GymBuffs"/>.
/// </summary>
public sealed class GymProteinShopUI : MonoBehaviour
{
    public const float CounterRange = 2.6f;
    public const float WorkerPostRange = 2.2f;

    private static GymProteinShopUI instance;
    private static bool open;
    private static int lastClosedFrame = -1;

    private PlayerMovement player;
    private bool playerCursorWasCaptured;
    private Canvas canvas;
    private CanvasGroup group;
    private RawImage modelView;
    private RectTransform infoPanel;
    private Text stampLabel;
    private Text nameLabel;
    private Text categoryLabel;
    private Text effectLabel;
    private Text priceLabel;
    private Text activeLabel;
    private Button buyButton;
    private Button backButton;
    private Button previousButton;
    private Button nextButton;
    private Text hintLabel;
    private Text variantLabel;
    private RectTransform variantRow;
    private Button previousVariantButton;
    private Button nextVariantButton;
    private RectTransform column;
    private Vector2 infoPanelBase;
    private Text buyText;
    private GymProductInspectView inspect;
    private int index;
    private int variantIndex;
    // Last flavour looked at per product type, so browsing back keeps it.
    private readonly int[] chosenVariant = new int[GymProteinShopCatalog.Products.Length];
    private int openedFrame;
    private float openedAt;
    private string lastResult;
    private bool dragStartedOnModel;
    private float lastResultUntil;

    /// <summary>True while open, and on the frame it closed so the closing key does not leak into gameplay.</summary>
    public static bool IsOpen => open || lastClosedFrame == Time.frameCount;
    public static GymProteinShopUI Active => instance;
    public int Index => index;
    public int VariantIndex => variantIndex;
    public GymProteinProduct Current => GymProteinShopCatalog.Products[index];
    public GymProteinVariant CurrentVariant => Current.variants[variantIndex];
    public GymProductInspectView InspectView => inspect;
    internal Button[] ButtonsForVerification =>
        new[] { previousButton, nextButton, previousVariantButton, nextVariantButton, backButton, buyButton };
    internal RectTransform VariantLabelForVerification => variantLabel.rectTransform;
    internal RectTransform ModelViewForVerification => modelView.rectTransform;
    internal RectTransform InfoPanelForVerification => infoPanel;
    internal RectTransform HintForVerification => hintLabel.rectTransform;
    internal RectTransform[] HeaderForVerification => new[] { stampLabel.rectTransform, activeLabel.rectTransform };
    internal Canvas CanvasForVerification => canvas;

    public enum PurchaseResult { Bought, Locked, NotEnoughStamps, TooManyActive, Unavailable }

    /// <summary>
    /// The shop only opens at the customer side of the counter while Mark is
    /// at his post behind it; anywhere else F keeps its usual meaning.
    /// </summary>
    public static bool CanShopAt(PlayerMovement player, EnemyFighter talkTarget)
    {
        if (player == null || talkTarget == null || talkTarget.Identity != BodybuilderIdentity.Mark ||
            talkTarget.IsDead || talkTarget.IsAggressive || !GymProteinStoreEnvironment.IsBuilt)
        {
            return false;
        }
        return FlatDistance(talkTarget.transform.position, GymProteinStoreEnvironment.StoreWorkerPoint) <=
                WorkerPostRange &&
            FlatDistance(player.transform.position, GymProteinStoreEnvironment.StoreFrontClearPoint) <=
                CounterRange;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public static GymProteinShopUI Open(PlayerMovement player)
    {
        if (open || player == null)
        {
            return instance;
        }
        if (instance == null)
        {
            GameObject root = new GameObject("Protein Shop UI");
            instance = root.AddComponent<GymProteinShopUI>();
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
        // Match height: the canvas is always 1080 units tall, so the vertical
        // stack below fits exactly on any aspect; only the width varies.
        scaler.matchWidthOrHeight = 1f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        // Darker than the menu veil: the product is the only thing to look at.
        Color veilColor = PersonaMenuStyle.Veil;
        veilColor.a = 0.94f;
        Image veil = GymRuntimeSettings.CreateImage("Shop Veil", transform, veilColor);
        GymRuntimeSettings.Stretch(veil.rectTransform);

        // Header row anchored to the top corners; everything else sits in
        // one centred column with fixed offsets, so the vertical stack keeps
        // its spacing at any screen aspect: product view 0-470, controls
        // hint 482-512, info plate 526-706, buttons 724-786.
        RectTransform slab = PersonaMenuStyle.CreatePanel(transform, "Shop Slab", 250f, 70f, 380f, 84f, -3f,
            PersonaMenuStyle.Accent, PersonaMenuStyle.Ink);
        Text slabText = PersonaMenuStyle.CreateLabel(slab, "Title", title, "SUPPLEMENTS",
            0f, 0f, 360f, 84f, 58, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        Center(slabText.rectTransform, 360f, 84f);
        // Stamps and the active supplements share the top-right corner, one
        // line each, above the column.
        stampLabel = PersonaMenuStyle.CreateLabel(transform, "Stamps", body, string.Empty,
            0f, 0f, 640f, 44f, 34, PersonaMenuStyle.Gold, TextAnchor.MiddleRight);
        PlaceTopRight(stampLabel.rectTransform, 40f, 28f, 640f, 44f);
        activeLabel = PersonaMenuStyle.CreateLabel(transform, "Active", body, string.Empty,
            0f, 0f, 640f, 32f, 24, PersonaMenuStyle.Muted, TextAnchor.MiddleRight);
        PlaceTopRight(activeLabel.rectTransform, 40f, 76f, 640f, 32f);

        column = new GameObject("Shop Column", typeof(RectTransform)).GetComponent<RectTransform>();
        column.SetParent(transform, false);
        PersonaMenuStyle.PlaceInArt(column, 836f, 525f, 1000f, 810f);

        inspect = new GymProductInspectView();
        GameObject viewObject = new GameObject("Product View", typeof(RectTransform), typeof(RawImage));
        viewObject.transform.SetParent(column, false);
        modelView = viewObject.GetComponent<RawImage>();
        modelView.texture = inspect.Texture;
        modelView.raycastTarget = false;
        PlaceInColumn(modelView.rectTransform, 0f, 220f, 440f, 440f);

        // The arrows hang off the view's sides, so they never cover the
        // product whatever the screen aspect.
        previousButton = CreateArrow(title, "<", -1);
        nextButton = CreateArrow(title, ">", 1);

        hintLabel = PersonaMenuStyle.CreateLabel(column, "Hint", body,
            "A / D  PRODUCT      W / S  FLAVOUR      DRAG  ROTATE      SCROLL  ZOOM", 0f, 0f, 900f, 30f, 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleCenter);
        PlaceInColumn(hintLabel.rectTransform, 0f, 512f, 900f, 30f);

        // Flavour / pack toggle right under the product: small arrows either
        // side of the variant name. A and D still step between product types.
        variantRow = new GameObject("Shop Flavour Row", typeof(RectTransform)).GetComponent<RectTransform>();
        variantRow.SetParent(column, false);
        PlaceInColumn(variantRow, 0f, 470f, 640f, 44f);
        variantLabel = PersonaMenuStyle.CreateLabel(variantRow, "Flavour", body, string.Empty,
            0f, 0f, 480f, 44f, 34, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        variantLabel.rectTransform.anchorMin = variantLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        variantLabel.rectTransform.anchoredPosition = Vector2.zero;
        previousVariantButton = CreateVariantArrow(body, "<", -1);
        nextVariantButton = CreateVariantArrow(body, ">", 1);

        infoPanel = PersonaMenuStyle.CreatePanel(column, "Shop Info", 0f, 0f, 980f, 180f, -1f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Accent);
        PlaceInColumn(infoPanel, 0f, 630f, 980f, 180f);
        infoPanelBase = infoPanel.anchoredPosition;
        nameLabel = InfoLabel("Name", title, 16f, 600f, 66f, 56, PersonaMenuStyle.Ink);
        categoryLabel = InfoLabel("Category", body, 22f, 900f, 40f, 32, PersonaMenuStyle.Muted);
        categoryLabel.alignment = TextAnchor.MiddleRight;
        effectLabel = InfoLabel("Effect", body, 84f, 900f, 38f, 32, PersonaMenuStyle.Gold);
        priceLabel = InfoLabel("Price", body, 126f, 900f, 38f, 30, PersonaMenuStyle.Ink);

        backButton = PersonaMenuStyle.CreateButton(column, "Shop Back", body, "BACK   E", 0f, 0f, 260f, 62f,
            44, -1f, Hide);
        PlaceInColumn((RectTransform)backButton.transform, -276f, 769f, 260f, 62f);
        buyButton = PersonaMenuStyle.CreateButton(column, "Shop Buy", body, "BUY   F", 0f, 0f, 260f, 62f,
            44, -1f, () => Buy());
        PlaceInColumn((RectTransform)buyButton.transform, 276f, 769f, 260f, 62f);
        buyText = buyButton.GetComponentInChildren<Text>();
        foreach (Button button in new[] { backButton, buyButton })
        {
            FillOnFocus(button);
            CenterLabel(button);
        }

        SetCanvasShown(false);
    }

    private static void PlaceTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-right * scale, -top * scale);
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    /// <summary>Places a rect in the column by art pixels: x from its centre, y from its top.</summary>
    private void PlaceInColumn(RectTransform rect, float centerX, float centerY, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(centerX * scale, -centerY * scale);
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    private Button CreateVariantArrow(Font font, string glyph, int side)
    {
        const float size = 44f;
        Button button = PersonaMenuStyle.CreateButton(variantRow, side < 0 ? "Shop Flavour Previous" : "Shop Flavour Next",
            font, glyph, 0f, 0f, size, size, 36, 0f, () => StepVariant(side));
        RectTransform rect = (RectTransform)button.transform;
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-side * size * 0.5f * scale, 0f);
        button.GetComponent<PersonaMenuButton>().SetFocusMotion(1.05f, 0f);
        FillOnFocus(button);
        CenterLabel(button);
        return button;
    }

    private Button CreateArrow(Font font, string glyph, int side)
    {
        const float size = 104f;
        const float gap = 28f;
        Button button = PersonaMenuStyle.CreateButton(transform, side < 0 ? "Shop Previous" : "Shop Next", font,
            glyph, 0f, 0f, size, size, 80, side * 2f, () => Step(side));
        RectTransform rect = (RectTransform)button.transform;
        rect.SetParent(modelView.rectTransform, false);
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(side * (gap + size * 0.5f) * scale, 0f);
        FillOnFocus(button);
        CenterLabel(button);
        return button;
    }

    // Hovered or focused, the rim stays red like the face: the plate reads as
    // one filled shape with no dark band between rim and fill.
    private static void FillOnFocus(Button button)
    {
        button.GetComponent<PersonaMenuButton>().SetFocusOutlineColor(PersonaMenuStyle.Accent);
    }

    // The menu buttons pad their labels from the slanted left edge; these are
    // short and centred in their plates instead.
    private static void CenterLabel(Button button)
    {
        Text label = button.GetComponentInChildren<Text>();
        label.alignment = TextAnchor.MiddleCenter;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
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

    private static void Center(RectTransform rect, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    private void Show(PlayerMovement targetPlayer)
    {
        player = targetPlayer;
        playerCursorWasCaptured = player.CursorCaptured;
        player.SetCursorCaptured(false);
        open = true;
        openedFrame = Time.frameCount;
        openedAt = Time.unscaledTime;
        lastResult = null;
        SetCanvasShown(true);
        inspect.SetActive(true);
        GymExperienceService.Active?.RegisterShopVisit();
        ShowProduct(index);
        GymAudio.Play2D(GymSoundEffect.UiClick, 0.5f);
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_OPEN product={Current.id} stamps={Stamps}");
    }

    private void Hide()
    {
        if (!open)
        {
            return;
        }
        open = false;
        lastClosedFrame = Time.frameCount;
        SetCanvasShown(false);
        inspect.SetActive(false);
        if (player != null)
        {
            player.SetCursorCaptured(playerCursorWasCaptured);
        }
        GymAudio.Play2D(GymSoundEffect.UiBack, 0.45f);
        Debug.Log("GYMCHAOS_PROTEIN_SHOP_CLOSED");
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
    private static int Reputation => GymExperienceService.Active != null ? GymExperienceService.Active.Reputation : 0;

    public void Step(int direction)
    {
        int count = GymProteinShopCatalog.Products.Length;
        ShowProduct(((index + direction) % count + count) % count);
        GymAudio.Play2D(GymSoundEffect.UiClick, 0.35f);
    }

    /// <summary>Next or previous flavour / pack of the current product.</summary>
    public void StepVariant(int direction)
    {
        int count = Current.variants.Length;
        if (count < 2)
        {
            return;
        }
        variantIndex = ((variantIndex + direction) % count + count) % count;
        chosenVariant[index] = variantIndex;
        // Same product, other flavour: keep the zoom to compare.
        inspect.Show(CurrentVariant, true);
        Refresh();
        GymAudio.Play2D(GymSoundEffect.UiClick, 0.3f);
    }

    private void ShowProduct(int newIndex)
    {
        index = newIndex;
        variantIndex = Mathf.Clamp(chosenVariant[index], 0, Current.variants.Length - 1);
        inspect.Show(CurrentVariant, false);
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
        else if (!service.TrySpendLoyaltyStamps(GymProteinShopCatalog.GetPrice(product, Reputation)))
        {
            result = PurchaseResult.NotEnoughStamps;
        }
        else
        {
            GymBuffs.Apply(product, player);
            result = PurchaseResult.Bought;
        }

        lastResult = result == PurchaseResult.Bought ? "BOUGHT  " + product.displayName :
            result == PurchaseResult.Locked ? "UNLOCKS AT LEVEL " + product.unlockLevel :
            result == PurchaseResult.TooManyActive ? "MAX " + GymProteinShopCatalog.MaxActiveProducts + " SUPPLEMENTS AT ONCE" :
            result == PurchaseResult.NotEnoughStamps ? "NOT ENOUGH STAMPS" : "SHOP UNAVAILABLE";
        lastResultUntil = Time.unscaledTime + 2.2f;
        GymAudio.Play2D(result == PurchaseResult.Bought ? GymSoundEffect.ActionConfirm : GymSoundEffect.UiBack,
            result == PurchaseResult.Bought ? 0.6f : 0.45f);
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_PURCHASE product={product.id} result={result} stamps={Stamps}");
        Refresh();
        return result;
    }

    private void Refresh()
    {
        GymProteinProduct product = Current;
        int price = GymProteinShopCatalog.GetPrice(product, Reputation);
        bool locked = Level < product.unlockLevel;
        GymExperienceService service = GymExperienceService.Active;
        int today = service != null ? service.LoyaltyStampsEarnedToday : 0;
        stampLabel.text = "LOYALTY CARD   " + Stamps + " / " + GymExperienceService.MaxLoyaltyStamps +
            "   TODAY " + today + " / " + GymExperienceService.DailyLoyaltyStampCap;
        nameLabel.text = product.displayName;
        int variants = product.variants.Length;
        variantLabel.text = variants > 1
            ? CurrentVariant.label + "   " + (variantIndex + 1) + " / " + variants
            : CurrentVariant.label;
        previousVariantButton.gameObject.SetActive(variants > 1);
        nextVariantButton.gameObject.SetActive(variants > 1);
        categoryLabel.text = product.category + "   " + (index + 1) + " / " + GymProteinShopCatalog.Products.Length;
        string duration = product.durationSeconds > 0f ? "   " + FormatTime(product.durationSeconds) : "   INSTANT";
        effectLabel.text = product.effectText + duration;
        if (lastResult != null && Time.unscaledTime < lastResultUntil)
        {
            priceLabel.text = lastResult;
            priceLabel.color = PersonaMenuStyle.Accent;
        }
        else
        {
            priceLabel.text = locked
                ? "LOCKED   UNLOCKS AT LEVEL " + product.unlockLevel
                : price + (price == 1 ? " STAMP" : " STAMPS") +
                  (GymBuffs.IsActive(product.id) ? "   ACTIVE - BUYING REFRESHES IT" : string.Empty);
            priceLabel.color = locked ? PersonaMenuStyle.Muted : PersonaMenuStyle.Ink;
        }
        bool affordable = !locked && Stamps >= price && GymBuffs.CanApply(product);
        buyText.color = affordable ? Color.white : PersonaMenuStyle.Muted;
        modelView.color = locked ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white;

        System.Text.StringBuilder active = new System.Text.StringBuilder();
        var buffs = GymBuffs.Active;
        for (int i = 0; i < buffs.Count; i++)
        {
            active.Append(buffs[i].crash ? buffs[i].product.displayName + " CRASH" : buffs[i].product.displayName)
                .Append(' ').Append(FormatTime(buffs[i].Remaining)).Append("     ");
        }
        activeLabel.text = active.ToString();
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.CeilToInt(seconds);
        return (total / 60) + ":" + (total % 60).ToString("00");
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

        // The F that opened the shop must not also buy.
        if (Time.frameCount == openedFrame)
        {
            return;
        }
        if (ReadKey(KeyCodeLike.Previous))
        {
            Step(-1);
        }
        else if (ReadKey(KeyCodeLike.PreviousVariant))
        {
            StepVariant(-1);
        }
        else if (ReadKey(KeyCodeLike.NextVariant))
        {
            StepVariant(1);
        }
        else if (ReadKey(KeyCodeLike.Next))
        {
            Step(1);
        }
        else if (ReadKey(KeyCodeLike.Buy))
        {
            Buy();
        }
        else if (ReadKey(KeyCodeLike.Back))
        {
            Hide();
        }
    }

    private enum KeyCodeLike { Previous, Next, PreviousVariant, NextVariant, Buy, Back }

    private static bool ReadKey(KeyCodeLike key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k == null) return false;
        switch (key)
        {
            case KeyCodeLike.Previous: return k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame;
            case KeyCodeLike.Next: return k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame;
            case KeyCodeLike.PreviousVariant: return k.wKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame;
            case KeyCodeLike.NextVariant: return k.sKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame;
            // Enter and Space submit the focused button through the EventSystem.
            case KeyCodeLike.Buy: return k.fKey.wasPressedThisFrame;
            default: return k.eKey.wasPressedThisFrame || k.qKey.wasPressedThisFrame ||
                k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame;
        }
#else
        switch (key)
        {
            case KeyCodeLike.Previous: return Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            case KeyCodeLike.Next: return Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
            case KeyCodeLike.PreviousVariant: return Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            case KeyCodeLike.NextVariant: return Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            case KeyCodeLike.Buy: return Input.GetKeyDown(KeyCode.F);
            default: return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Q) ||
                Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace);
        }
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
        // Only a press that starts on the model turns it, never one on a button.
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
        // Windows reports 120 per wheel notch, other backends 1: count notches.
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
            instance = null;
            open = false;
        }
        inspect?.Dispose();
    }
}

/// <summary>
/// Off-screen turntable for the shop: the product GLB sits on its own layer
/// far below the world with its own camera and lights, rendered into a
/// RenderTexture shown by the shop overlay. The camera only renders while
/// the shop is open.
/// </summary>
public sealed class GymProductInspectView
{
    public const int PreviewLayer = 26;
    private static readonly Vector3 StageOrigin = new Vector3(0f, -2000f, 0f);
    private const float PitchLimit = 60f;

    private readonly GameObject stage;
    private readonly Camera camera;
    private readonly Transform pivot;
    private readonly RenderTexture texture;
    private GameObject model;
    private string modelId;
    private float yaw;
    private float pitch;
    private float yawVelocity;
    private float distance = DefaultDistance;
    private float targetDistance = DefaultDistance;
    private float idleTime;
    private bool fitted;

    public RenderTexture Texture => texture;
    internal Camera CameraForVerification => camera;
    public float Yaw => yaw;
    public bool ModelLoaded => model != null && model.transform.childCount > 0 &&
        model.GetComponentInChildren<MeshFilter>() != null;
    public string ModelId => modelId;

    public GymProductInspectView() : this(StageOrigin, "Product Inspect Stage")
    {
    }

    /// <summary>Each overlay gets its own stage so the models never share a view.</summary>
    public GymProductInspectView(Vector3 origin, string stageName)
    {
        stage = new GameObject(stageName);
        stage.transform.position = origin;
        stage.layer = PreviewLayer;
        texture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { name = "Product Inspect RT" };

        GameObject cameraObject = new GameObject("Product Inspect Camera");
        cameraObject.transform.SetParent(stage.transform, false);
        camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        // Veil-coloured and transparent: if a pipeline drops the alpha the
        // view still reads as part of the overlay instead of a black square.
        camera.backgroundColor = new Color(0.012f, 0.05f, 0.13f, 0f);
        camera.cullingMask = 1 << PreviewLayer;
        camera.fieldOfView = 30f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 20f;
        camera.targetTexture = texture;
        camera.enabled = false;

        pivot = new GameObject("Product Pivot").transform;
        pivot.SetParent(stage.transform, false);
        pivot.gameObject.layer = PreviewLayer;

        AddLight("Key", new Vector3(1.2f, 1.4f, -1.6f), 3.2f, new Color(1f, 0.96f, 0.9f));
        AddLight("Fill", new Vector3(-1.6f, 0.4f, -1.2f), 1.4f, new Color(0.75f, 0.85f, 1f));
        AddLight("Rim", new Vector3(0f, 1.2f, 1.8f), 2.2f, Color.white);
        PlaceCamera();
    }

    private void AddLight(string name, Vector3 offset, float intensity, Color color)
    {
        GameObject lightObject = new GameObject("Inspect " + name + " Light");
        lightObject.transform.SetParent(stage.transform, false);
        lightObject.transform.localPosition = offset;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 6f;
        light.intensity = intensity;
        light.color = color;
        light.shadows = LightShadows.None;
    }

    public void SetActive(bool active)
    {
        camera.enabled = active;
        if (active)
        {
            yawVelocity = 0f;
            idleTime = 0f;
        }
    }

    public void Show(GymProteinVariant variant, bool keepZoom)
    {
        if (string.IsNullOrEmpty(variant.modelId) || variant.modelId == modelId)
        {
            return;
        }
        if (model != null)
        {
            Object.Destroy(model);
        }
        modelId = variant.modelId;
        // Every model starts facing the camera; a flavour change keeps the
        // zoom so packs can be compared at the same size.
        yaw = 0f;
        pitch = 12f;
        yawVelocity = 0f;
        idleTime = 0f;
        if (!keepZoom)
        {
            targetDistance = DefaultDistance;
            distance = DefaultDistance;
            PlaceCamera();
        }
        // The exporter puts the label on Blender -Y, which arrives as Unity +Z
        // (away from the camera at -Z), so the model starts turned 180 degrees.
        model = RuntimeGlbSceneLoader.Request(variant.ModelPath, pivot, pivot.position,
            pivot.rotation * Quaternion.Euler(0f, 180f, 0f), Vector3.one, "Inspect " + variant.modelId, PreviewLayer,
            renderProfile: RuntimeGlbRenderProfile.Default);
        fitted = false;
        FittedScale = 0f;
        ApplyRotation();
    }

    /// <summary>
    /// Scales the product to a common size and centres it on the pivot. Bounds
    /// come from the meshes themselves: renderer bounds can still be empty on
    /// the frame the loader attaches the parts.
    /// </summary>
    private bool Fit(GameObject loaded)
    {
        MeshFilter[] filters = loaded.GetComponentsInChildren<MeshFilter>();
        bool hasBounds = false;
        Bounds bounds = default;
        Matrix4x4 toPivot = pivot.worldToLocalMatrix;
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null || mesh.vertexCount == 0)
            {
                continue;
            }
            if (mesh.bounds.size.sqrMagnitude < 1e-10f)
            {
                mesh.RecalculateBounds();
            }
            Bounds local = mesh.bounds;
            Matrix4x4 toPivotSpace = toPivot * filters[i].transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 inPivot = toPivotSpace.MultiplyPoint3x4(point);
                if (!hasBounds)
                {
                    bounds = new Bounds(inPivot, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(inPivot);
                }
            }
            Renderer renderer = filters[i].GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (!hasBounds || largest < 0.0001f)
        {
            return false;
        }
        // Scale about the pivot, then move the scaled centre onto it.
        float scale = FittedSize / largest;
        Transform root = loaded.transform;
        root.localScale *= scale;
        root.localPosition = (root.localPosition - bounds.center) * scale;
        FittedScale = scale;
        Debug.Log($"GYMCHAOS_PROTEIN_SHOP_FIT product={modelId} size={largest:F3} scale={scale:F2}");
        return true;
    }

    public const float FittedSize = 0.62f;
    public float FittedScale { get; private set; }
    public bool ModelFitted => fitted;

    public void Tick(Vector2 drag, float scroll, float deltaTime)
    {
        if (!fitted && ModelLoaded)
        {
            fitted = Fit(model);
        }
        if (drag.sqrMagnitude > 0.0001f)
        {
            yaw -= drag.x * 0.35f;
            yawVelocity = -drag.x * 0.35f / Mathf.Max(0.005f, deltaTime);
            pitch = Mathf.Clamp(pitch + drag.y * 0.25f, -PitchLimit, PitchLimit);
            idleTime = 0f;
        }
        else
        {
            idleTime += deltaTime;
            // Momentum after a flick, then a slow showroom turn when idle.
            yawVelocity = Mathf.Lerp(yawVelocity, 0f, 1f - Mathf.Exp(-deltaTime * 4f));
            yaw += yawVelocity * deltaTime;
            if (idleTime > 2.5f)
            {
                yaw += 18f * deltaTime;
            }
        }
        // Each wheel notch moves 22% closer or further; the camera eases to
        // the target so fast spins stay smooth.
        if (Mathf.Abs(scroll) > 0.001f)
        {
            ZoomBy(scroll);
        }
        if (Mathf.Abs(distance - targetDistance) > 0.0005f)
        {
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-deltaTime * 18f));
            PlaceCamera();
        }
        ApplyRotation();
    }

    public const float DefaultDistance = 1.65f;
    public const float MinDistance = 0.55f;
    public const float MaxDistance = 2.8f;
    private const float ZoomPerNotch = 0.78f;
    public float TargetDistance => targetDistance;

    /// <summary>Wheel notches: positive zooms in.</summary>
    public void ZoomBy(float notches)
    {
        targetDistance = Mathf.Clamp(targetDistance * Mathf.Pow(ZoomPerNotch, notches), MinDistance, MaxDistance);
    }

    /// <summary>Verifier hook: turns the model like a mouse drag would.</summary>
    public void RotateForVerification(float degrees)
    {
        yaw += degrees;
        ApplyRotation();
    }

    private void ApplyRotation()
    {
        pivot.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void PlaceCamera()
    {
        camera.transform.localPosition = new Vector3(0f, 0f, -distance);
        camera.transform.localRotation = Quaternion.identity;
    }

    public void Dispose()
    {
        if (stage != null)
        {
            Object.Destroy(stage);
        }
        if (texture != null)
        {
            texture.Release();
            Object.Destroy(texture);
        }
    }
}
