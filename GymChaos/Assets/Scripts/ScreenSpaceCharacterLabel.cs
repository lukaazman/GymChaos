using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class ScreenSpaceCharacterLabel : MonoBehaviour
{
    private const float NameRevealDistance = 7.5f;
    private const float NameRevealDistanceSqr = NameRevealDistance * NameRevealDistance;
    private const float BehindOverlayAlpha = 0.18f;

    /// <summary>
    /// The labels draw above the world but the shop overlays sit on top of
    /// them, so while a shop is open the labels behind it fade.
    /// </summary>
    public static float CurrentAlpha => GymProteinShopUI.IsOpen || GymDealerShopUI.IsOpen ? BehindOverlayAlpha : 1f;

    private SkinnedMeshRenderer bodyRenderer;
    private Camera playerCamera;
    private string displayName;
    private float heightOffset;
    private EnemyFighter fighter;
    // Name-only label for a character that is not an EnemyFighter.
    private bool standalone;
    /// <summary>Standalone characters (Hank) show a bar at this fraction; below 0 hides it.</summary>
    public float StandaloneHealthFraction { get; set; } = -1f;

    // uGUI elements on the shared label canvas.
    private RectTransform root;
    private RectTransform nameRect;
    private Text nameText;
    private RectTransform barRect;
    private Image barBorder;
    private Image barBackground;
    private Image barFill;
    private RectTransform barFillRect;

    public void Configure(
        SkinnedMeshRenderer targetRenderer, string label, float offset)
    {
        bodyRenderer = targetRenderer;
        displayName = label;
        heightOffset = offset;
        playerCamera = Camera.main;
        fighter = GetComponentInParent<EnemyFighter>();
        if (nameText != null) nameText.text = displayName;
    }

    public void ConfigureStandalone(
        SkinnedMeshRenderer targetRenderer, string label, float offset)
    {
        bodyRenderer = targetRenderer;
        displayName = label;
        heightOffset = offset;
        playerCamera = Camera.main;
        standalone = true;
        if (nameText != null) nameText.text = displayName;
    }

    private void OnEnable() => LabelCanvas.Register(this);

    private void OnDisable()
    {
        LabelCanvas.Unregister(this);
        if (root != null) root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    // Called once per frame for every enabled label by LabelCanvas.
    private void Refresh()
    {
        bool showName;
        bool showHealth;
        Vector3 screen;
        if (!TryGetScreenAnchor(out screen, out showName, out showHealth) || (!showName && !showHealth))
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            return;
        }

        if (root == null) Build();
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);

        float alpha = CurrentAlpha;
        // The anchor sits just above the head: the stack grows upward from
        // it (health bar, then name) so the name stays close to the head.
        float stackBottom = screen.y + AnchorGapPixels;
        root.anchoredPosition = new Vector2(screen.x, stackBottom);
        float nameBottom = 0f;
        barRect.gameObject.SetActive(showHealth);
        if (showHealth)
        {
            float health01 = standalone
                ? StandaloneHealthFraction
                : fighter.CurrentHealth / Mathf.Max(1f, fighter.MaxHealth);
            Color fillColor = standalone ? new Color(0.92f, 0.04f, 0.04f, 1f) : fighter.HealthBarColor;
            barBorder.color = new Color(0f, 0f, 0f, alpha);
            barBackground.color = new Color(0.18f, 0.04f, 0.04f, alpha);
            barFill.color = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * alpha);
            barFillRect.anchorMax = new Vector2(Mathf.Clamp01(health01), 1f);
            nameBottom = HealthBarHeight + 2f;
        }

        nameRect.gameObject.SetActive(showName);
        if (showName)
        {
            nameRect.anchoredPosition = new Vector2(0f, nameBottom);
            nameText.color = new Color(1f, 1f, 1f, alpha);
        }
    }

    private bool TryGetScreenAnchor(out Vector3 screen, out bool showName, out bool showHealth)
    {
        screen = default;
        showName = false;
        showHealth = false;
        bool menuNametag = GymStartScreen.IsMenuVisible && IsReceptionist;
        if (bodyRenderer == null || !bodyRenderer.enabled ||
            (GymStartScreen.IsMenuVisible && !(menuNametag && GymStartScreen.ShowsReceptionistNametag)) ||
            GymDialogueDirector.IsDialogueActive ||
            GymPauseMenu.IsVisible)
        {
            return false;
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        if (playerCamera == null)
        {
            return false;
        }

        if (fighter == null && !standalone)
        {
            fighter = GetComponentInParent<EnemyFighter>();
        }
        if (fighter == null && !standalone)
        {
            return false;
        }

        Bounds bounds = bodyRenderer.bounds;
        screen = playerCamera.WorldToScreenPoint(
            new Vector3(bounds.center.x, bounds.max.y + heightOffset, bounds.center.z));
        if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width ||
            screen.y < 0f || screen.y > Screen.height)
        {
            return false;
        }

        Vector3 anchor = standalone ? transform.position : fighter.transform.position;
        bool nearby = (anchor - playerCamera.transform.position).sqrMagnitude <=
            NameRevealDistanceSqr;
        GymDialogueDirector director = GymDialogueDirector.Active;
        bool isDialogueTarget = !standalone && director != null && director.Target == fighter;
        showName = nearby || menuNametag || (!standalone && fighter.IsAggressive) ||
            isDialogueTarget;
        showHealth = standalone ? StandaloneHealthFraction >= 0f : fighter.HasTakenDamage;
        return true;
    }

    private bool IsReceptionist
    {
        get
        {
            if (fighter == null && !standalone)
            {
                fighter = GetComponentInParent<EnemyFighter>();
            }
            return !standalone && fighter != null &&
                fighter.Identity == BodybuilderIdentity.Manwithsuit1;
        }
    }

    /// <summary>Verifier hook: whether the label would show this name now.</summary>
    public bool WouldShowNameInMenu => IsReceptionist && GymStartScreen.ShowsReceptionistNametag;

    private const float AnchorGapPixels = 1f;
    private const float HealthBarHeight = 9f;
    private const float BarWidth = 86f;
    private const float NameWidth = 180f;
    private const float NameHeight = 16f;
    public static float NameHeightFactor => 0.035f;

    private void Build()
    {
        root = new GameObject(displayName + " Label", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(LabelCanvas.Root, false);
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0.5f, 0f);
        root.sizeDelta = Vector2.zero;

        barRect = Child("Health Bar", root, new Vector2(BarWidth, HealthBarHeight));
        barBorder = barRect.gameObject.AddComponent<Image>();
        RectTransform background = Child("Background", barRect, Vector2.zero);
        Stretch(background, 1f);
        barBackground = background.gameObject.AddComponent<Image>();
        barFillRect = Child("Fill", background, Vector2.zero);
        Stretch(barFillRect, 0f);
        barFillRect.pivot = new Vector2(0f, 0.5f);
        barFill = barFillRect.gameObject.AddComponent<Image>();

        nameRect = Child("Name", root, new Vector2(NameWidth, NameHeight));
        nameText = nameRect.gameObject.AddComponent<Text>();
        nameText.font = PersonaMenuStyle.BuiltinFont;
        nameText.fontSize = 11;
        nameText.alignment = TextAnchor.LowerCenter;
        nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        nameText.verticalOverflow = VerticalWrapMode.Overflow;
        nameText.text = displayName;
    }

    private static RectTransform Child(string name, RectTransform parent, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>
    /// One overlay canvas and one LateUpdate for every label (they used to be
    /// one IMGUI OnGUI per character, called for layout and repaint events
    /// even when nothing was drawn). Sorted above the world, below the HUD
    /// and the menus.
    /// </summary>
    private sealed class LabelCanvas : MonoBehaviour
    {
        private static readonly List<ScreenSpaceCharacterLabel> Labels = new List<ScreenSpaceCharacterLabel>();
        private static LabelCanvas instance;

        public static RectTransform Root
        {
            get
            {
                if (instance == null)
                {
                    GameObject host = new GameObject("Character Labels", typeof(RectTransform));
                    Canvas canvas = host.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 40;
                    instance = host.AddComponent<LabelCanvas>();
                }
                return (RectTransform)instance.transform;
            }
        }

        public static void Register(ScreenSpaceCharacterLabel label)
        {
            if (!Labels.Contains(label)) Labels.Add(label);
            _ = Root;
        }

        public static void Unregister(ScreenSpaceCharacterLabel label) => Labels.Remove(label);

        private static readonly Unity.Profiling.ProfilerMarker RefreshMarker =
            new Unity.Profiling.ProfilerMarker("GymChaos.Late.CharacterLabels");

        private void LateUpdate()
        {
            using (RefreshMarker.Auto())
            {
                for (int i = Labels.Count - 1; i >= 0; i--)
                {
                    if (Labels[i] == null) Labels.RemoveAt(i);
                    else Labels[i].Refresh();
                }
            }
        }
    }
}
