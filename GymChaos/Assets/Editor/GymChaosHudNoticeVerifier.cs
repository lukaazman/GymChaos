#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Edit-mode check of the HUD notice popups: the canvas scales with screen
/// height only (same share of the screen at every resolution and aspect),
/// labels never wrap or best-fit shrink, and each plate fits its text on one
/// line. Logs GYMCHAOS_HUD_NOTICE_OK or GYMCHAOS_HUD_NOTICE_FAIL.
/// </summary>
public static class GymChaosHudNoticeVerifier
{
    private const string LongText = "LOLLIPOP LARRY HAS ARRIVED  +120 XP  CHAOS GOAL COMPLETE";
    private const string ShortText = "+5 XP";

    [MenuItem("Tools/GymChaos/Run HUD Notice Verification")]
    public static void Run()
    {
        int exitCode = 1;
        GymHud hud = null;
        try
        {
            Require(!GymHud.IsActive, "Run in a fresh process: a HUD already exists.");
            hud = GymHud.CreateForScene(null);
            CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
            Require(scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                    Mathf.Approximately(scaler.referenceResolution.y, 1080f) &&
                    scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.MatchWidthOrHeight &&
                    Mathf.Approximately(scaler.matchWidthOrHeight, 1f),
                "HUD canvas must scale by screen height only (1080 reference).");

            hud.PushNotice(LongText, 5f, 2);
            hud.PushNotice(ShortText, 5f, 0);
            hud.TickNoticesForVerification();
            Text[] labels = hud.NoticeLabelsForVerification;
            float longWidth = 0f;
            float shortWidth = 0f;
            for (int i = 0; i < 2; i++)
            {
                Text label = labels[i];
                Require(label.horizontalOverflow == HorizontalWrapMode.Overflow && !label.resizeTextForBestFit &&
                        label.fontSize == GymHud.NoticeFontSize,
                    $"Notice {i} can wrap or shrink: overflow={label.horizontalOverflow} " +
                    $"bestFit={label.resizeTextForBestFit} size={label.fontSize}.");
                RectTransform line = (RectTransform)label.transform.parent;
                float textWidth = label.preferredWidth * label.rectTransform.localScale.x;
                Require(line.rect.width >= textWidth + 20f && Mathf.Approximately(line.rect.height, GymHud.NoticeHeight),
                    $"Notice {i} plate {line.rect.width}x{line.rect.height} does not hold '{label.text}' " +
                    $"({textWidth:F0} wide) on one line.");
                if (label.text.StartsWith("LOLLIPOP", StringComparison.Ordinal)) longWidth = line.rect.width;
                else shortWidth = line.rect.width;
            }
            Require(longWidth > shortWidth + 200f, $"Plates do not fit their text: long={longWidth} short={shortWidth}.");

            // E/F prompts (IMGUI): same share of the screen height at every
            // resolution and aspect, one line.
            int[,] screens = { { 1280, 720 }, { 1920, 1080 }, { 2560, 1080 }, { 1280, 1024 }, { 2560, 1440 }, { 3840, 2160 } };
            for (int i = 0; i < screens.GetLength(0); i++)
            {
                int height = screens[i, 1];
                float share = PlayerMovement.PromptVirtualHeight * PlayerMovement.PromptScaleFor(height) / height;
                Require(Mathf.Abs(share - PlayerMovement.PromptVirtualHeight / 1080f) < 0.0005f,
                    $"E/F prompt takes {share:P2} of a {screens[i, 0]}x{height} screen.");
            }
            Require(!PlayerMovement.PromptMessageWraps, "E/F prompt messages may wrap onto a second line.");
            Debug.Log($"GYMCHAOS_HUD_NOTICE_OK long={longWidth:F0} short={shortWidth:F0} scaleBy=height");
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_HUD_NOTICE_FAIL " + exception.Message);
        }
        finally
        {
            if (hud != null) UnityEngine.Object.DestroyImmediate(hud.gameObject);
        }
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
