using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the level-up stat choice uGUI in edit mode around a throwaway
/// progression service and proves it uses the menu language (slanted Persona
/// buttons, Bebas/Anton type, navy veil) and that a click spends the point.
/// With -Graphics it writes Logs/agent/levelup-ui.png.
/// Marker: GYMCHAOS_LEVELUP_UI_OK / GYMCHAOS_LEVELUP_UI_FAIL.
/// </summary>
public static class GymChaosLevelUpUiVerifier
{
    [MenuItem("Tools/GymChaos/Run Level Up UI Verification")]
    public static void Run()
    {
        int exitCode = 1;
        GameObject serviceObject = null;
        GameObject cameraObject = null;
        try
        {
            serviceObject = new GameObject("Level Up Verifier Service");
            GymExperienceService service = serviceObject.AddComponent<GymExperienceService>();
            GymProgressionState state = new GymProgressionState
            {
                level = 7,
                skillPoints = 2,
                strengthRank = 3,
                enduranceRank = 1,
                techniqueRank = 5,
            };
            service.ApplySession(state, Array.Empty<string>(), false);
            Require(service.IsSkillChoiceVisible && service.SkillPoints == 2,
                "Service did not open the stat choice for two points.");

            GymLevelUpMenuUI ui = GymLevelUpMenuUI.Ensure(service);
            ui.ShowForVerification();
            Require(ui.IsShowingForVerification, "Level-up menu did not show.");

            Canvas canvas = ui.GetComponent<Canvas>();
            Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay &&
                canvas.enabled, "Level-up canvas is not an enabled overlay.");
            CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
            Require(scaler != null && scaler.referenceResolution == new Vector2(1920f, 1080f),
                "Level-up canvas does not use the menu 1920x1080 scaler.");

            Font bebas = PersonaMenuStyle.LoadButtonFont(null);
            Font anton = PersonaMenuStyle.LoadTitleFont(null);
            Require(bebas != null && anton != null, "Menu fonts did not load.");
            Button[] buttons = ui.StatButtonsForVerification;
            Require(buttons != null && buttons.Length == 4, "Expected four stat buttons.");
            foreach (Button button in buttons)
            {
                Require(button.GetComponent<PersonaMenuButton>() != null,
                    button.name + " is not a Persona menu button.");
                Require(button.GetComponentInChildren<Text>().font == bebas,
                    button.name + " label is not Bebas.");
                float tilt = Mathf.DeltaAngle(0f, button.transform.localEulerAngles.z);
                Require(tilt < -0.5f && tilt > -5f, button.name + " is not slanted like the menu stack.");
            }
            bool hasTitle = false;
            bool hasVeil = false;
            foreach (Text text in ui.GetComponentsInChildren<Text>(true))
            {
                hasTitle |= text.font == anton && text.text == "LEVEL UP!";
            }
            foreach (Image image in ui.GetComponentsInChildren<Image>(true))
            {
                hasVeil |= image.color == PersonaMenuStyle.Veil;
            }
            Require(hasTitle, "Anton LEVEL UP! title missing.");
            Require(hasVeil, "Menu navy veil missing.");
            Require(ui.InfoTitleForVerification == "STRENGTH", "Info plate did not preview the focused stat.");
            Require(buttons[1].GetComponentInChildren<Text>().text.Contains("1"),
                "Endurance button does not show its rank.");

            cameraObject = new GameObject("Level Up Capture Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.32f, 0.36f, 0.4f, 1f);
            GymChaosUiCapture.Capture(camera, "levelup-ui.png", canvas);

            buttons[1].onClick.Invoke();
            Require(service.GetStatRank(GymStat.Endurance) == 2,
                "Endurance click did not raise Endurance to rank 2.");
            Require(service.SkillPoints == 1, "Endurance click did not spend one point.");
            Require(ui.InfoTitleForVerification == "ENDURANCE", "Info plate did not follow the click.");
            buttons[0].onClick.Invoke();
            Require(service.SkillPoints == 0 && !service.IsSkillChoiceVisible &&
                service.GetStatRank(GymStat.Strength) == 4,
                "Second click did not spend the last point and close the choice.");

            Debug.Log("GYMCHAOS_LEVELUP_UI_OK buttons=4 fonts=bebas+anton veil=1 clicks=2");
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_LEVELUP_UI_FAIL " + exception.Message);
        }
        finally
        {
            if (GymLevelUpMenuUI.Active != null) UnityEngine.Object.DestroyImmediate(GymLevelUpMenuUI.Active.gameObject);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (serviceObject != null) UnityEngine.Object.DestroyImmediate(serviceObject);
            Time.timeScale = 1f;
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
