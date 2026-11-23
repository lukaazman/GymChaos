using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Locker outfit selector in the menu and HUD style: an upright ink-rimmed
/// plate with a red title tag and page tabs. Every top, bottom, headwear item,
/// member and vehicle is a Persona tile holding its baked picture
/// (Resources/UI/Outfit); tops and bottoms each pick their colour from a row of
/// swatches beside the section label. The worn item keeps the selected plate,
/// locked items are shown as a dark silhouette with a padlock; hovering any tile
/// names it, and a locked one also names its unlock requirement. Equip rules stay in
/// <see cref="GymExperienceService"/>; this only presents and forwards clicks.
/// </summary>
public sealed class GymLockerMenuUI : MonoBehaviour
{
    private const float PanelWidth = 860f;
    private const float PanelHeight = 840f;
    private const float Padding = 34f;
    private const float TileSize = 120f;
    private const float TileGap = 14f;
    // Section label row to its tiles. A hovered tile grows and tilts
    // (PersonaMenuButton), so this leaves the colour swatches clear of it.
    private const float SectionToTiles = 56f;
    private const float TopSection = 96f;
    private const float BottomSection = 296f;
    private const float HeadwearSection = 496f;
    private const float HoverNameY = 690f;
    private const string ArtFolder = "UI/Outfit/";
    private static readonly Color LockedArt = new Color(0.07f, 0.075f, 0.09f, 1f);

    private static GymLockerMenuUI instance;

    private GymExperienceService service;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform panel;
    private Text rankLabel;
    private Text hoverNameLabel;
    private Text challengeLabel;
    private Button[] topButtons;
    private Button[] shirtButtons;
    private Button[] bottomButtons;
    private Button[] bottomColorButtons;
    private Button[] headwearButtons;
    private Button doneButton;

    // PLAYER / MEMBERS / VEHICLES: the outfit sections stay on the first tab, the
    // per-character and per-vehicle DEFAULT/POTATO picks live on the other two.
    private Transform playerRoot;
    private GameObject membersRoot;
    private GameObject vehiclesRoot;
    private readonly Button[] tabButtons = new Button[3];
    private static readonly string[] TabLabels = { "PLAYER", "MEMBERS", "VEHICLES" };
    private Button[] memberOgButtons;
    private Button[] memberSlopButtons;
    private Button[] vehicleOgButtons;
    private Button[] vehicleSlopButtons;
    private int lockerTab;

    private bool shown;
    private System.Func<string> hoveredRequirement;
    private string hoveredName;
    private float shownAt;

    public static GymLockerMenuUI Active => instance;
    public bool IsShowingForVerification => shown;
    public Button[] TopButtonsForVerification => topButtons;
    public Button[] ShirtButtonsForVerification => shirtButtons;
    public Button[] BottomButtonsForVerification => bottomButtons;
    public Button[] BottomColorButtonsForVerification => bottomColorButtons;
    public static bool IsSwatchSelectedForVerification(Button swatch) =>
        swatch.transform.Find("Worn").gameObject.activeSelf;
    public Button[] HeadwearButtonsForVerification => headwearButtons;
    public Button DoneButtonForVerification => doneButton;
    public int TabForVerification => lockerTab;
    public string HoverNameForVerification => hoverNameLabel != null ? hoverNameLabel.text : string.Empty;
    public Button[] TabButtonsForVerification => tabButtons;
    public Button[] MemberSlopButtonsForVerification => memberSlopButtons;
    public Button[] MemberOgButtonsForVerification => memberOgButtons;
    public Button[] VehicleSlopButtonsForVerification => vehicleSlopButtons;
    public string HintTextForVerification => challengeLabel != null ? challengeLabel.text : string.Empty;
    public bool MembersTabShownForVerification => membersRoot != null && membersRoot.activeSelf;
    public bool VehiclesTabShownForVerification => vehiclesRoot != null && vehiclesRoot.activeSelf;

    private bool forceShownForVerification;

    /// <summary>Verifier seam: opens the panel without a running HUD interaction.</summary>
    public void ShowForVerification()
    {
        forceShownForVerification = true;
        SetShown(true);
    }

    public void HideForVerification()
    {
        forceShownForVerification = false;
        SetShown(false);
    }

    /// <summary>Verifier seam: the same hover a pointer makes over a version plate.</summary>
    public void HoverForVerification(bool members, int index, bool inside)
    {
        string key = members ? GymVersionedLooks.MemberKeyAt(index) : GymVersionedLooks.VehicleKeyAt(index);
        hoveredRequirement = inside ? (System.Func<string>)(() => RequirementFor(key, members)) : null;
        hoveredName = inside ? (members ? GymVersionedLooks.MemberLabel(index) : GymVersionedLooks.VehicleLabel(index)) : null;
        RefreshHint();
    }
    /// <summary>Verifier: the locker must not carry an art-style switch any more.</summary>
    public bool HasStylePanelForVerification =>
        transform.Find("Style Panel") != null || transform.Find("Panel/Style Panel") != null;

    public static GymLockerMenuUI Ensure(GymExperienceService owner)
    {
        if (instance == null)
        {
            GameObject root = new GameObject("Gym Locker Menu");
            instance = root.AddComponent<GymLockerMenuUI>();
            instance.service = owner;
            instance.Build();
        }
        instance.service = owner;
        return instance;
    }

    private void Build()
    {
        GymRuntimeSettings.EnsureEventSystem();
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(gameObject.AddComponent<CanvasScaler>());
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        GameObject panelObject = new GameObject("Locker Panel", typeof(RectTransform));
        panelObject.transform.SetParent(transform, false);
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(56f, 0f);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        PersonaMenuStyle.AddUprightPlate(panel, PersonaMenuStyle.Ink, PersonaMenuStyle.PlateBlack);

        GameObject tagObject = new GameObject("Title Tag", typeof(RectTransform));
        tagObject.transform.SetParent(panel, false);
        RectTransform titleTag = tagObject.GetComponent<RectTransform>();
        titleTag.anchorMin = titleTag.anchorMax = new Vector2(0f, 1f);
        titleTag.pivot = new Vector2(0f, 0.5f);
        titleTag.anchoredPosition = new Vector2(Padding - 6f, 0f);
        titleTag.sizeDelta = new Vector2(200f, 60f);
        PersonaMenuStyle.AddUprightPlate(titleTag, PersonaMenuStyle.Ink, PersonaMenuStyle.Accent);
        Text titleText = PersonaMenuStyle.CreateLayoutLabel(titleTag, "Title", title, "OUTFIT",
            new Vector2(20f, 0f), new Vector2(170f, 60f), 38, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

        rankLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Rank", body, string.Empty,
            new Vector2(Padding, 48f), new Vector2(PanelWidth - Padding * 2f, 34f), 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);

        // Page tabs sit on the panel's top edge, right of the OUTFIT tag, with a
        // red rail along the edge so the open tab reads as part of the page.
        PersonaShape rail = PersonaMenuStyle.CreateShape("Tab Rail", panel, PersonaMenuStyle.Accent);
        RectTransform railRect = rail.rectTransform;
        railRect.anchorMin = new Vector2(0f, 1f);
        railRect.anchorMax = new Vector2(1f, 1f);
        railRect.pivot = new Vector2(0.5f, 1f);
        railRect.offsetMin = new Vector2(236f, -5f);
        railRect.offsetMax = Vector2.zero;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            tabButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Tab " + TabLabels[i], body, TabLabels[i],
                new Vector2(238f + i * 160f, -50f), new Vector2(152f, 50f), 26, () => SelectTab(index));
            tabButtons[i].GetComponent<PersonaMenuButton>().SetFocusMotion(1.03f, 0f);
        }

        GameObject playerObject = new GameObject("Player Tab", typeof(RectTransform));
        playerObject.transform.SetParent(panel, false);
        GymShared.Stretch(playerObject.GetComponent<RectTransform>());
        playerRoot = playerObject.transform;

        PersonaMenuStyle.CreateLayoutLabel(playerRoot, "Top Section", body, "TOP",
            new Vector2(Padding, TopSection), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        topButtons = new Button[GymExperienceService.LockerTops.Count];
        for (int i = 0; i < topButtons.Length; i++)
        {
            GymTopStyle top = GymExperienceService.LockerTops[i];
            string label = PlayerCosmeticLoadout.GetTopDisplayName(top);
            topButtons[i] = CreateTile(playerRoot, "Top " + top, body, label,
                Tile(i, TopSection + SectionToTiles), new Vector2(TileSize, TileSize),
                "top_" + top.ToString().ToLowerInvariant() + "_black", () => { service?.EquipTop(top); Refresh(); });
            AddHover(topButtons[i], label, () => service != null ? service.GetLockerTopRequirement(top) : string.Empty);
        }

        // The swatches beside TOP colour whichever top is worn.
        shirtButtons = BuildSwatches(playerRoot, "Shirt", TopSection,
            shirt => { service?.EquipShirt(shirt); Refresh(); });

        PersonaMenuStyle.CreateLayoutLabel(playerRoot, "Bottom Section", body, "BOTTOMS",
            new Vector2(Padding, BottomSection), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        bottomButtons = new Button[GymExperienceService.LockerBottoms.Length];
        for (int i = 0; i < bottomButtons.Length; i++)
        {
            GymBottomStyle bottom = GymExperienceService.LockerBottoms[i];
            string label = PlayerCosmeticLoadout.GetBottomDisplayName(bottom);
            bottomButtons[i] = CreateTile(playerRoot, "Bottom " + bottom, body, label,
                Tile(i, BottomSection + SectionToTiles), new Vector2(TileSize, TileSize),
                "bottom_" + bottom.ToString().ToLowerInvariant() + "_black", () => { service?.EquipBottom(bottom); Refresh(); });
            AddHover(bottomButtons[i], label, null);
        }
        bottomColorButtons = BuildSwatches(playerRoot, "Bottom Color", BottomSection,
            color => { service?.EquipBottomColor(color); Refresh(); });

        PersonaMenuStyle.CreateLayoutLabel(playerRoot, "Headwear Section", body, "HEADWEAR",
            new Vector2(Padding, HeadwearSection), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        headwearButtons = new Button[GymExperienceService.LockerHeadwear.Count];
        for (int i = 0; i < headwearButtons.Length; i++)
        {
            GymHeadwear item = GymExperienceService.LockerHeadwear[i];
            string label = PlayerCosmeticLoadout.GetHeadwearDisplayName(item).ToUpperInvariant();
            headwearButtons[i] = CreateTile(playerRoot, "Headwear " + item, body, label,
                Tile(i, HeadwearSection + 44f), new Vector2(TileSize, TileSize),
                item == GymHeadwear.None ? null : "head_" + item.ToString().ToLowerInvariant(),
                () => { service?.EquipHeadwear(item); Refresh(); });
            AddHover(headwearButtons[i], label, () => service != null ? service.GetLockerHeadwearRequirement(item) : string.Empty);
        }

        // The hovered tile's name; empty while nothing is hovered.
        hoverNameLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Hovered Name", body, string.Empty,
            new Vector2(Padding, HoverNameY), new Vector2(PanelWidth - Padding * 2f - 250f, 34f), 30,
            PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        // On the panel, not the player page: every tab's hover hint lands here.
        challengeLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Mastery Challenge", body, string.Empty,
            new Vector2(Padding, HoverNameY + 40f), new Vector2(PanelWidth - Padding * 2f - 250f, 30f), 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
        doneButton = PersonaMenuStyle.CreateLayoutButton(panel, "Done", body, "DONE   ESC",
            new Vector2(PanelWidth - Padding - 210f, PanelHeight - Padding - 58f), new Vector2(210f, 58f), 34,
            () => service?.CloseLockerMenuFromUi());

        membersRoot = BuildVersionList(panel, "Members", GymVersionedLooks.MemberKeys,
            out memberOgButtons, out memberSlopButtons, true);
        vehiclesRoot = BuildVersionList(panel, "Vehicles", GymVersionedLooks.VehicleKeys,
            out vehicleOgButtons, out vehicleSlopButtons, false);
        SelectTab(0);
        SetShown(false);
    }



    private const float SwatchSize = 34f;
    private const float SwatchGap = 12f;

    // One colour square per shirt colour on a section's label row, right
    // aligned; a white frame marks the worn colour, locked ones are dimmed.
    /// <summary>
    /// One page of per-object DEFAULT/POTATO plates: two columns, a name over each pair.
    /// A POTATO plate is disabled until its unlock fires (a talk, a race win).
    /// </summary>
    private GameObject BuildVersionList(Transform parent, string name, string[] keys,
        out Button[] ogButtons, out Button[] slopButtons, bool members)
    {
        GameObject root = new GameObject(name + " Tab", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        GymShared.Stretch(root.GetComponent<RectTransform>());
        Font body = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);
        ogButtons = new Button[keys.Length];
        slopButtons = new Button[keys.Length];
        // Members: four per row, portrait pairs. Vehicles: two per row, wide pairs.
        int perRow = members ? 4 : 2;
        float pairGap = 8f;
        float cellWidth = (PanelWidth - Padding * 2f - (perRow - 1) * pairGap) / perRow;
        Vector2 tile = members
            ? new Vector2((cellWidth - pairGap) * 0.5f, (cellWidth - pairGap) * 0.5f * 1.25f)
            : new Vector2((cellWidth - pairGap) * 0.5f, (cellWidth - pairGap) * 0.5f * 0.667f);
        float rowHeight = tile.y + 28f + 12f;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            string label = members ? GymVersionedLooks.MemberLabel(i) : GymVersionedLooks.VehicleLabel(i);
            float x = Padding + (i % perRow) * (cellWidth + pairGap);
            float y = 100f + (i / perRow) * rowHeight;
            PersonaMenuStyle.CreateLayoutLabel(root.transform, name + " Name " + key, body, label,
                new Vector2(x, y), new Vector2(cellWidth, 26f), 22, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
            string art = (members ? "member_" : "vehicle_") + key;
            ogButtons[i] = CreateTile(root.transform, name + " DEFAULT " + key, body, "DEFAULT",
                new Vector2(x, y + 28f), tile, art + "_og", () => ChooseVersion(key, false, members));
            slopButtons[i] = CreateTile(root.transform, name + " POTATO " + key, body, "POTATO",
                new Vector2(x + tile.x + pairGap, y + 28f), tile, art + "_slop", () => ChooseVersion(key, true, members));
            AddHover(ogButtons[i], label + "   DEFAULT", null);
            AddHover(slopButtons[i], label + "   POTATO", () => RequirementFor(key, members));
        }
        root.SetActive(false);
        return root;
    }

    /// <summary>
    /// A Persona button holding a baked picture (Resources/UI/Outfit/<paramref name="art"/>).
    /// The caption stays in the hierarchy for the verifiers but only draws when the
    /// picture is missing.
    /// </summary>
    private static Button CreateTile(Transform parent, string name, Font font, string label, Vector2 topLeft,
        Vector2 size, string art, UnityEngine.Events.UnityAction onClick)
    {
        Button tile = PersonaMenuStyle.CreateLayoutButton(parent, name, font, label, topLeft, size, 24, onClick);
        tile.GetComponent<PersonaMenuButton>().SetFocusMotion(1.06f, 2f);
        Text caption = tile.GetComponentInChildren<Text>();
        caption.alignment = TextAnchor.MiddleCenter;
        Texture2D picture = art != null ? Resources.Load<Texture2D>(ArtFolder + art) : null;
        caption.enabled = picture == null;
        if (picture == null) return tile;
        GameObject artObject = new GameObject("Art", typeof(RectTransform), typeof(RawImage));
        artObject.transform.SetParent(tile.transform, false);
        RectTransform rect = (RectTransform)artObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -6f);
        RawImage image = artObject.GetComponent<RawImage>();
        image.texture = picture;
        image.raycastTarget = false;
        // Under the caption, over the face.
        artObject.transform.SetSiblingIndex(caption.transform.GetSiblingIndex());
        return tile;
    }

    /// <summary>Swaps a garment tile to the picture of its worn colourway (each is baked from the game's own cloth).</summary>
    private static void SetArt(Button tile, string art)
    {
        Transform image = tile.transform.Find("Art");
        Texture2D picture = Resources.Load<Texture2D>(ArtFolder + art);
        if (image != null && picture != null) image.GetComponent<RawImage>().texture = picture;
    }

    /// <summary>Art colour: the worn colour for garments, a dark silhouette while locked.</summary>
    private static void TintTile(Button tile, Color colour, bool unlocked)
    {
        Transform art = tile.transform.Find("Art");
        if (art != null) art.GetComponent<RawImage>().color = unlocked ? colour : LockedArt;
    }

    private static Vector2 Tile(int index, float top) =>
        new Vector2(Padding + index * (TileSize + TileGap), top);

    private static string RequirementFor(string key, bool members) =>
        members ? "TALK TO UNLOCK" : RequirementForVehicle(key);

    private static string RequirementForVehicle(string key)
    {
        for (int i = 0; i < GymVersionedLooks.VehicleCount; i++)
        {
            if (GymVersionedLooks.VehicleKeyAt(i) == key) return GymVersionedLooks.VehicleRequirement(i);
        }
        return string.Empty;
    }

    private void ChooseVersion(string key, bool slop, bool members)
    {
        bool applied = members
            ? GymVersionedLooks.SetMemberVersion(key, slop)
            : GymVersionedLooks.SetVehicleVersion(key, slop);
        if (!applied && slop)
        {
            Debug.Log("GYMCHAOS_VERSION_LOCKED key=" + key);
        }
        Refresh();
    }

    public void SelectTab(int index)
    {
        lockerTab = Mathf.Clamp(index, 0, 2);
        if (playerRoot != null) playerRoot.gameObject.SetActive(lockerTab == 0);
        if (membersRoot != null) membersRoot.SetActive(lockerTab == 1);
        if (vehiclesRoot != null) vehiclesRoot.SetActive(lockerTab == 2);
        for (int i = 0; i < tabButtons.Length; i++)
        {
            tabButtons[i].GetComponent<PersonaMenuButton>().SetSelected(i == lockerTab);
        }
        Refresh();
    }

    private Button[] BuildSwatches(Transform parent, string name, float rowTop, System.Action<GymShirtColor> equip)
    {
        int count = GymExperienceService.LockerShirts.Count;
        Button[] swatches = new Button[count];
        float right = PanelWidth - Padding - 12f;
        for (int i = 0; i < count; i++)
        {
            GymShirtColor color = GymExperienceService.LockerShirts[i];
            GameObject root = new GameObject(name + " " + color, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(right - (count - i) * (SwatchSize + SwatchGap) + SwatchGap, -rowTop + 1f);
            rect.sizeDelta = new Vector2(SwatchSize, SwatchSize);
            root.GetComponent<Image>().color = PersonaMenuStyle.Ink;
            Image worn = SwatchPart(rect, "Worn", -4f, Color.white);
            worn.transform.SetAsFirstSibling();
            Image fill = SwatchPart(rect, "Colour", 3f, PlayerCosmeticLoadout.GetSwatchColor(color));
            Button button = root.GetComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => equip(color));
            swatches[i] = button;
            AddHover(button, service != null ? service.GetLockerShirtLabel(color) : color.ToString().ToUpperInvariant(),
                () => service != null ? service.GetLockerShirtRequirement(color) : string.Empty);
        }
        return swatches;
    }

    private static Image SwatchPart(RectTransform parent, string name, float inset, Color color)
    {
        GameObject part = new GameObject(name, typeof(RectTransform), typeof(Image));
        part.transform.SetParent(parent, false);
        RectTransform rect = part.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        Image image = part.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = name == "Colour";
        return image;
    }

    private static void ConfigureSwatch(Button swatch, bool unlocked, bool worn)
    {
        swatch.interactable = unlocked;
        swatch.transform.Find("Worn").gameObject.SetActive(worn);
        Image fill = swatch.transform.Find("Colour").GetComponent<Image>();
        Color c = fill.color;
        c.a = unlocked ? 1f : 0.3f;
        fill.color = c;
        // The dimmed square already says locked; the hover hint names the unlock.
        Transform padlock = swatch.transform.Find("Padlock");
        if (padlock != null) padlock.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        bool open = forceShownForVerification || service != null && service.IsLockerMenuOpen &&
            !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible;
        if (open != shown)
        {
            SetShown(open);
        }
        if (!shown)
        {
            return;
        }

        float reveal = Mathf.Clamp01((Time.unscaledTime - shownAt) / PersonaMenuStyle.TransitionSeconds);
        group.alpha = reveal;
        panel.anchoredPosition = new Vector2(56f - (1f - reveal) * 40f, 0f);

        // LateUpdate runs after PlayerMovement, so this ESC can never also open
        // the pause menu in the same frame.
        if (GymShared.ReadEscape())
        {
            GymAudio.Play2D(GymSoundEffect.UiBack, 0.55f);
            service.CloseLockerMenuFromUi();
            SetShown(false);
        }
    }

    private void SetShown(bool value)
    {
        shown = value;
        group.alpha = 0f;
        group.blocksRaycasts = value;
        group.interactable = value;
        canvas.enabled = value;
        if (value)
        {
            shownAt = Time.unscaledTime;
            // PLAYER is the page the locker opens on.
            SelectTab(0);
            if (EventSystem.current != null)
            {
                Button focus = topButtons.Length > 0 ? topButtons[0] : doneButton;
                for (int i = 0; i < topButtons.Length; i++)
                {
                    if (service.IsTopWorn(GymExperienceService.LockerTops[i])) focus = topButtons[i];
                }
                EventSystem.current.SetSelectedGameObject(focus.gameObject);
            }
        }
        else if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void Refresh()
    {
        if (service == null)
        {
            return;
        }
        rankLabel.text = service.LockerRankLine;
        RefreshHint();
        string topColour = "black";
        string bottomColour = "black";
        for (int i = 0; i < shirtButtons.Length; i++)
        {
            GymShirtColor shirt = GymExperienceService.LockerShirts[i];
            if (service.IsShirtWorn(shirt)) topColour = shirt.ToString().ToLowerInvariant();
            if (service.IsBottomColorWorn(shirt)) bottomColour = shirt.ToString().ToLowerInvariant();
        }
        for (int i = 0; i < topButtons.Length; i++)
        {
            GymTopStyle top = GymExperienceService.LockerTops[i];
            Configure(topButtons[i], service.GetLockerTopLabel(top),
                service.IsCosmeticUnlocked(top), service.IsTopWorn(top));
            SetArt(topButtons[i], "top_" + top.ToString().ToLowerInvariant() + "_" + topColour);
            TintTile(topButtons[i], Color.white, service.IsCosmeticUnlocked(top));
        }
        for (int i = 0; i < shirtButtons.Length; i++)
        {
            GymShirtColor shirt = GymExperienceService.LockerShirts[i];
            ConfigureSwatch(shirtButtons[i], service.IsCosmeticUnlocked(shirt), service.IsShirtWorn(shirt));
            ConfigureSwatch(bottomColorButtons[i], service.IsCosmeticUnlocked(shirt), service.IsBottomColorWorn(shirt));
        }
        for (int i = 0; i < bottomButtons.Length; i++)
        {
            GymBottomStyle bottom = GymExperienceService.LockerBottoms[i];
            Configure(bottomButtons[i], PlayerCosmeticLoadout.GetBottomDisplayName(bottom), true,
                service.IsBottomWorn(bottom));
            SetArt(bottomButtons[i], "bottom_" + bottom.ToString().ToLowerInvariant() + "_" + bottomColour);
            TintTile(bottomButtons[i], Color.white, true);
        }
        for (int i = 0; i < headwearButtons.Length; i++)
        {
            GymHeadwear item = GymExperienceService.LockerHeadwear[i];
            Configure(headwearButtons[i], service.GetLockerHeadwearLabel(item),
                service.IsCosmeticUnlocked(item), service.IsHeadwearWorn(item));
            TintTile(headwearButtons[i], Color.white, service.IsCosmeticUnlocked(item));
        }
        bool editable = !GymVersionedLooks.HostOverrideActive;
        for (int i = 0; i < memberOgButtons.Length; i++)
        {
            string key = GymVersionedLooks.MemberKeyAt(i);
            bool slop = GymVersionedLooks.IsSlop(key);
            bool unlocked = GymVersionedLooks.IsMemberUnlocked(key) && editable;
            ConfigureVersion(memberOgButtons[i], !slop, editable);
            ConfigureVersion(memberSlopButtons[i], slop, unlocked);
            TintTile(memberSlopButtons[i], Color.white, unlocked);
        }
        for (int i = 0; i < vehicleOgButtons.Length; i++)
        {
            string key = GymVersionedLooks.VehicleKeyAt(i);
            bool slop = GymVersionedLooks.IsSlopVehicle(key);
            bool unlocked = GymVersionedLooks.IsVehicleUnlocked(key) && editable;
            ConfigureVersion(vehicleOgButtons[i], !slop, editable);
            ConfigureVersion(vehicleSlopButtons[i], slop, unlocked);
            TintTile(vehicleSlopButtons[i], Color.white, unlocked);
        }
    }

    // A version plate: the chosen one is selected, a locked POTATO one is dimmed
    // and keeps its padlock; the hint row explains the unlock on hover.
    private static void ConfigureVersion(Button button, bool selected, bool unlocked)
    {
        button.interactable = unlocked;
        Transform padlock = button.transform.Find("Padlock");
        if (padlock != null) padlock.gameObject.SetActive(!unlocked);
        button.GetComponent<PersonaMenuButton>().SetSelected(selected);
    }

    private void AddHover(Button button, string displayName, System.Func<string> requirement)
    {
        LockHover hover = button.gameObject.AddComponent<LockHover>();
        hover.Changed = inside =>
        {
            hoveredRequirement = inside ? requirement : null;
            hoveredName = inside ? displayName : null;
            RefreshHint();
        };
        BuildPadlock(button.transform);
    }

    // The hint row shows the hovered item's unlock requirement, otherwise the
    // mastery challenge once one exists; it never shows a bare "locked".
    private void RefreshHint()
    {
        if (service == null) return;
        hoverNameLabel.text = hoveredName ?? string.Empty;
        string requirement = hoveredRequirement != null ? hoveredRequirement() : string.Empty;
        if (!string.IsNullOrEmpty(requirement))
        {
            challengeLabel.text = requirement;
            challengeLabel.color = PersonaMenuStyle.Gold;
            return;
        }
        string challenge = service.MasteryChallengeLabel;
        challengeLabel.text = challenge == GymExperienceService.GetMasteryChallengeLabel(0)
            ? string.Empty : challenge.ToUpperInvariant();
        challengeLabel.color = PersonaMenuStyle.Muted;
    }

    // Small drawn padlock in the top-right corner of a tile; shown only while locked.
    private static void BuildPadlock(Transform button)
    {
        GameObject root = new GameObject("Padlock", typeof(RectTransform));
        root.transform.SetParent(button, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-10f, -10f);
        rect.sizeDelta = new Vector2(14f, 18f);
        PadlockPart(rect, 0f, 0f, 14f, 10f);   // body
        PadlockPart(rect, 2f, 9f, 2.5f, 6f);   // shackle left
        PadlockPart(rect, 9.5f, 9f, 2.5f, 6f); // shackle right
        PadlockPart(rect, 2f, 14f, 10f, 2.5f); // shackle top
        root.SetActive(false);
    }

    private static void PadlockPart(RectTransform parent, float x, float y, float width, float height)
    {
        GameObject part = new GameObject("Part", typeof(RectTransform), typeof(Image));
        part.transform.SetParent(parent, false);
        RectTransform rect = part.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        Image image = part.GetComponent<Image>();
        image.color = PersonaMenuStyle.Muted;
        image.raycastTarget = false;
    }

    private static void Configure(Button button, string label, bool unlocked, bool worn)
    {
        button.interactable = unlocked;
        Transform padlock = button.transform.Find("Padlock");
        if (padlock != null) padlock.gameObject.SetActive(!unlocked);
        button.GetComponentInChildren<Text>().text = label;
        PersonaMenuStyle.FitLabelToButton(button, 24);
        button.GetComponent<PersonaMenuButton>().SetSelected(worn);
    }

    // Disabled buttons still receive pointer events; this only reports hover
    // so locked plates keep their look while the hint row explains the unlock.
    private sealed class LockHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<bool> Changed;
        public void OnPointerEnter(PointerEventData eventData) => Changed?.Invoke(true);
        public void OnPointerExit(PointerEventData eventData) => Changed?.Invoke(false);
        private void OnDisable() => Changed?.Invoke(false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
