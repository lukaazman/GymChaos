using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-40)]
public sealed class GymDialogueDirector : MonoBehaviour
{
    private static GymDialogueDirector instance;
    private PlayerMovement player;
    private EnemyFighter target;
    private DialogueDefinition definition;
    private GymDialogueCamera dialogueCamera;
    private GymDialogueUI dialogueUi;
    private int currentNodeIndex;
    private float startedAt;

    private GUIStyle dialogueSpeakerStyle;
    private GUIStyle dialogueBodyStyle;
    private GUIStyle dialogueChoiceStyle;
    private GUIStyle dialogueHintStyle;
    private GUIStyle dialoguePromptStyle;

    public static GymDialogueDirector Active => instance;
    public static bool IsDialogueActive => instance != null && instance.target != null;
    public EnemyFighter Target => target;
    public DialogueNode CurrentNode => definition != null && currentNodeIndex >= 0 &&
        currentNodeIndex < definition.nodes.Count ? definition.nodes[currentNodeIndex] : null;
    public float LetterboxBlend => Mathf.Clamp01((Time.unscaledTime - startedAt) / 0.34f);

    public static GymDialogueDirector CreateForScene(PlayerMovement targetPlayer)
    {
        if (instance != null)
        {
            instance.player = targetPlayer != null ? targetPlayer : instance.player;
            return instance;
        }

        GameObject directorObject = new GameObject("Gym Dialogue Director");
        instance = directorObject.AddComponent<GymDialogueDirector>();
        instance.player = targetPlayer;
        instance.dialogueCamera = directorObject.AddComponent<GymDialogueCamera>();
        instance.dialogueUi = directorObject.AddComponent<GymDialogueUI>();
        return instance;
    }

    public static bool TryStartNearby(PlayerMovement targetPlayer, bool interactPressed)
    {
        if (instance == null || targetPlayer == null || IsDialogueActive || !interactPressed)
        {
            return false;
        }

        EnemyFighter nearby = instance.FindNearbyTalkTarget(targetPlayer.transform.position);
        if (nearby == null)
        {
            return false;
        }

        instance.Open(targetPlayer, nearby);
        return true;
    }

    public static void TickActiveInput(
        PlayerMovement targetPlayer, bool advancePressed, bool escapePressed)
    {
        if (instance == null || !IsDialogueActive || instance.player != targetPlayer)
        {
            return;
        }

        if (instance.target.IsDead)
        {
            instance.Close("target-dead");
            return;
        }

        if (escapePressed)
        {
            instance.Close("cancelled");
            return;
        }

        if (instance.CurrentNode != null && instance.CurrentNode.choices.Count > 0)
        {
            int choice = instance.ReadChoicePressed();
            if (choice >= 0)
            {
                instance.SelectChoice(choice);
            }
            return;
        }

        if (advancePressed)
        {
            instance.AdvanceNode();
        }
    }

    public EnemyFighter FindNearbyTalkTarget(Vector3 position)
    {
        EnemyFighter[] fighters = FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        EnemyFighter closest = null;
        float bestDistance = 3.2f * 3.2f;
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null || fighter.IsDead || fighter.IsAggressive)
            {
                continue;
            }

            float distance = Vector3.ProjectOnPlane(fighter.transform.position - position, Vector3.up).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = fighter;
            }
        }
        return closest;
    }

    public void DrawDialogueUI()
    {
        if (!IsDialogueActive || dialogueUi == null)
        {
            return;
        }

        DialogueNode node = CurrentNode;
        if (node == null)
        {
            return;
        }

        EnsureDialogueStyles();
        float blend = LetterboxBlend;
        float barHeight = Mathf.Lerp(
            0f, Mathf.Clamp(Screen.height * 0.1f, 34f, 72f), blend);
        GUI.color = new Color(0.005f, 0.007f, 0.012f, 0.98f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, barHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, Screen.height - barHeight, Screen.width, barHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        int choiceCount = node.choices.Count;
        int columns = choiceCount >= 3 && Screen.width >= 720f
            ? 3
            : choiceCount == 2 && Screen.width >= 620f
                ? 2
                : 1;
        int rows = choiceCount > 0
            ? Mathf.CeilToInt(choiceCount / (float)columns)
            : 0;
        float panelPadding = Mathf.Clamp(Screen.width * 0.02f, 22f, 34f);
        float panelMargin = Mathf.Clamp(Screen.width * 0.025f, 18f, 36f);
        float bottomMargin = Mathf.Clamp(Screen.height * 0.028f, 12f, 26f);
        float choiceHeight = Mathf.Clamp(Screen.height * 0.085f, 46f, 64f);
        float choiceGap = Mathf.Clamp(Screen.width * 0.012f, 8f, 14f);
        float choiceAreaHeight = rows > 0
            ? rows * choiceHeight + Mathf.Max(0, rows - 1) * choiceGap
            : 0f;
        float desiredPanelHeight = choiceCount > 0
            ? 148f + choiceAreaHeight
            : 174f;
        float panelBottom = Screen.height - barHeight - bottomMargin;
        float maxPanelHeight = Mathf.Max(140f, panelBottom - 12f);
        float panelHeight = Mathf.Min(desiredPanelHeight, maxPanelHeight);
        panelHeight = Mathf.Max(140f, panelHeight);
        Rect panel = new Rect(
            panelMargin,
            Mathf.Max(8f, panelBottom - panelHeight),
            Screen.width - panelMargin * 2f,
            panelHeight);

        GUI.color = new Color(0f, 0f, 0f, 0.34f);
        GUI.DrawTexture(
            new Rect(panel.x + 4f, panel.y + 5f, panel.width, panel.height),
            Texture2D.whiteTexture);
        GUI.color = new Color(0.015f, 0.022f, 0.045f, 0.985f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;

        dialogueSpeakerStyle.fontSize = Mathf.Clamp(Screen.height / 42, 18, 28);
        dialogueBodyStyle.fontSize = Mathf.Clamp(Screen.height / 58, 15, 22);
        dialogueChoiceStyle.fontSize = Mathf.Clamp(Screen.height / 64, 13, 18);
        dialogueHintStyle.fontSize = Mathf.Clamp(Screen.height / 76, 12, 16);

        GUI.Label(
            new Rect(panel.x + panelPadding, panel.y + 15f,
                panel.width - panelPadding * 2f, 34f),
            node.speaker,
            dialogueSpeakerStyle);

        float choiceTop = panel.y + panel.height - 18f - choiceAreaHeight;
        float bodyTop = panel.y + 54f;
        float bodyBottom = choiceCount > 0
            ? choiceTop - 12f
            : panel.y + panel.height - 48f;
        GUI.Label(
            new Rect(panel.x + panelPadding, bodyTop,
                panel.width - panelPadding * 2f, Mathf.Max(30f, bodyBottom - bodyTop)),
            node.text,
            dialogueBodyStyle);

        if (choiceCount > 0)
        {
            float choiceWidth = (panel.width - panelPadding * 2f -
                Mathf.Max(0, columns - 1) * choiceGap) / columns;
            for (int i = 0; i < choiceCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Rect choiceRect = new Rect(
                    panel.x + panelPadding + column * (choiceWidth + choiceGap),
                    choiceTop + row * (choiceHeight + choiceGap),
                    choiceWidth,
                    choiceHeight);
                if (GUI.Button(choiceRect,
                    $"{i + 1}  {node.choices[i].text}", dialogueChoiceStyle))
                {
                    SelectChoice(i);
                }
            }
        }
        else
        {
            GUI.Label(
                new Rect(panel.x + panelPadding, panel.y + panel.height - 37f,
                    panel.width - panelPadding * 2f, 24f),
                "SPACE continue   ESC close",
                dialogueHintStyle);
        }

        GUI.color = Color.white;
    }

    public void DrawNearbyPrompt(PlayerMovement targetPlayer)
    {
        if (IsDialogueActive || targetPlayer == null)
        {
            return;
        }

        EnemyFighter nearby = FindNearbyTalkTarget(targetPlayer.transform.position);
        if (nearby == null)
        {
            return;
        }

        EnsureDialogueStyles();
        dialoguePromptStyle.fontSize = Mathf.Clamp(Screen.height / 38, 16, 20);
        float width = Mathf.Min(520f, Screen.width - 36f);
        Rect promptRect = new Rect(
            (Screen.width - width) * 0.5f, Screen.height - 106f, width, 48f);
        GUI.color = new Color(0.015f, 0.022f, 0.045f, 0.88f);
        GUI.DrawTexture(promptRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(promptRect.x, promptRect.y + 5f, promptRect.width, 38f),
            $"[E] Talk to {GetDisplayName(nearby)}", dialoguePromptStyle);
    }

    private void Open(PlayerMovement targetPlayer, EnemyFighter fighter)
    {
        player = targetPlayer;
        target = fighter;
        definition = BuildDefinition(fighter);
        currentNodeIndex = 0;
        startedAt = Time.unscaledTime;
        fighter.SetDialogueLocked(true, player.transform);
        if (!dialogueCamera.Begin(player, fighter.transform))
        {
            fighter.SetDialogueLocked(false);
            target = null;
            definition = null;
            currentNodeIndex = -1;
            return;
        }
        GymExperienceService progression = GymExperienceService.Active;
        progression?.RegisterDialogueStarted(fighter.Identity);
        Debug.Log($"GYMCHAOS_DIALOGUE_OPEN speaker={fighter.Identity}", this);
    }

    private void Close(string reason)
    {
        if (!IsDialogueActive)
        {
            return;
        }

        EnemyFighter closedTarget = target;
        dialogueCamera.End();
        closedTarget.SetDialogueLocked(false);
        Debug.Log($"GYMCHAOS_DIALOGUE_CLOSED speaker={closedTarget.Identity} reason={reason}", this);
        target = null;
        definition = null;
        currentNodeIndex = -1;
    }

    private void AdvanceNode()
    {
        if (definition == null || CurrentNode == null)
        {
            Close("invalid");
            return;
        }

        int nextNode = currentNodeIndex + 1;
        if (nextNode >= definition.nodes.Count)
        {
            Close("complete");
            return;
        }
        currentNodeIndex = nextNode;
    }

    private void SelectChoice(int choiceIndex)
    {
        DialogueNode node = CurrentNode;
        if (node == null || choiceIndex < 0 || choiceIndex >= node.choices.Count)
        {
            return;
        }

        DialogueChoice choice = node.choices[choiceIndex];
        GymExperienceService progression = GymExperienceService.Active;
        string sourceKey = target.Identity + "-node-" + currentNodeIndex;
        progression?.RegisterDialogueChoice(choice.reputationDelta, sourceKey);
        if (choice.reputationDelta > 0 &&
            target.GetComponent<GymVisitorAgent>() != null)
        {
            progression?.RegisterVisitorHelp(target.Identity, false);
        }
        Debug.Log(
            $"GYMCHAOS_DIALOGUE_CHOICE speaker={target.Identity} index={choiceIndex} " +
            $"rep={choice.reputationDelta}",
            this);

        if (choice.nextNodeIndex < 0 || choice.nextNodeIndex >= definition.nodes.Count)
        {
            Close("choice-complete");
            return;
        }
        currentNodeIndex = choice.nextNodeIndex;
    }

    private int ReadChoicePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
        {
            return -1;
        }
        if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) return 0;
        if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) return 1;
        if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame) return 2;
        return -1;
#else
        if (Input.GetKeyDown(KeyCode.Alpha1)) return 0;
        if (Input.GetKeyDown(KeyCode.Alpha2)) return 1;
        if (Input.GetKeyDown(KeyCode.Alpha3)) return 2;
        return -1;
#endif
    }

    private DialogueDefinition BuildDefinition(EnemyFighter fighter)
    {
        string name = GetDisplayName(fighter);
        DialogueDefinition result = new DialogueDefinition
        {
            id = fighter.Identity.ToString(),
            displayName = name
        };
        GymExperienceService progression = GymExperienceService.Active;
        bool positiveRep = progression != null && progression.Reputation >= 50;
        bool negativeRep = progression != null && progression.Reputation <= -50;

        if (fighter.Identity == BodybuilderIdentity.Manwithsuit1)
        {
            result.nodes.Add(new DialogueNode(name,
                negativeRep
                    ? "You are becoming a problem people have to plan around. Locker room is through the back door. Cool off before you start another scene."
                    : positiveRep
                        ? "People have noticed you helping out. Locker room is through the back door. Get changed, then I will mark you down for today's briefing."
                        : "You're new here. Locker room is through the back door. Get changed before you start your first workout.")
                .AddChoice("I'll check it out.", 1, 3)
                .AddChoice("I'm here to train.", 1, 0)
                .AddChoice("I know what I'm doing.", 1, -2));
            result.nodes.Add(new DialogueNode(name,
                negativeRep
                    ? "Grab a shirt, check the mirror, and keep your hands off the equipment that is not yours. I will be watching."
                    : "Grab a shirt, check the mirror, then pick a station. The gym gets loud when people stop listening.")
                .AddChoice("Got it.", -1, 1));
            return result;
        }

        if (fighter.Identity == BodybuilderIdentity.Ronnie)
        {
            result.nodes.Add(new DialogueNode(name,
                negativeRep
                    ? "You planning to train, or make me chase you around the room again?"
                    : positiveRep
                        ? "Heard you have been keeping the room together. You planning to train, or just collect compliments?"
                        : "You planning to train, or start another scene?")
                .AddChoice("Train. No trouble.", 1, 4)
                .AddChoice("Try me.", 1, -6)
                .AddChoice("I need a spot.", 1, 2));
            result.nodes.Add(new DialogueNode(name,
                "Then keep your hands to yourself. I don't want to cross the room because you got bored.")
                .AddChoice("Fair enough.", -1, 2)
                .AddChoice("Make me.", -1, -5));
            return result;
        }

        result.nodes.Add(new DialogueNode(name,
            negativeRep
                ? "You keep picking fights between sets. Is that your whole workout?"
                : positiveRep
                    ? "You are the one people keep thanking. Need a partner, or are you just making the rounds?"
                    : "You always stare between sets, or am I getting special treatment?")
            .AddChoice("Just saying hi.", 1, 3)
            .AddChoice("I'm sizing you up.", 1, -3)
            .AddChoice("Need a partner?", 1, 4));
        result.nodes.Add(new DialogueNode(name,
            "Good. Finish your set before the room finds a reason to notice you.")
            .AddChoice("Deal.", -1, 1));
        return result;
    }

    private static string GetDisplayName(EnemyFighter fighter)
    {
        if (fighter == null)
        {
            return "Gym regular";
        }
        return fighter.Identity == BodybuilderIdentity.Manwithsuit1
            ? "Reception"
            : fighter.Identity.ToString();
    }

    private void OnGUI()
    {
        if (GymStartScreen.IsMenuVisible || dialogueUi == null)
        {
            return;
        }

        if (IsDialogueActive)
        {
            DrawDialogueUI();
        }
        else
        {
            DrawNearbyPrompt(player != null ? player : FindFirstObjectByType<PlayerMovement>());
        }
    }

    private void OnDestroy()
    {
        if (dialogueCamera != null)
        {
            dialogueCamera.End();
        }
        if (target != null)
        {
            target.SetDialogueLocked(false);
        }
        if (instance == this)
        {
            instance = null;
        }
    }

    private void EnsureDialogueStyles()
    {
        if (dialogueSpeakerStyle != null)
        {
            return;
        }

        dialogueSpeakerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };
        dialogueSpeakerStyle.normal.textColor = new Color(1f, 0.78f, 0.28f);

        dialogueBodyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Normal,
            wordWrap = true,
            padding = new RectOffset(0, 0, 0, 0)
        };
        dialogueBodyStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);

        dialogueChoiceStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            padding = new RectOffset(14, 14, 8, 8)
        };
        dialogueChoiceStyle.normal.textColor = new Color(0.95f, 0.97f, 1f);
        dialogueChoiceStyle.hover.textColor = Color.white;
        dialogueChoiceStyle.active.textColor = Color.white;
        dialogueChoiceStyle.focused.textColor = Color.white;
        dialogueChoiceStyle.normal.background = CreateDialogueTexture(
            new Color(0.075f, 0.16f, 0.28f, 0.98f), "Dialogue choice");
        dialogueChoiceStyle.hover.background = CreateDialogueTexture(
            new Color(0.14f, 0.3f, 0.48f, 1f), "Dialogue choice hover");
        dialogueChoiceStyle.active.background = CreateDialogueTexture(
            new Color(0.92f, 0.58f, 0.16f, 1f), "Dialogue choice active");
        dialogueChoiceStyle.focused.background = dialogueChoiceStyle.hover.background;

        dialogueHintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Normal,
            padding = new RectOffset(0, 0, 0, 0)
        };
        dialogueHintStyle.normal.textColor = new Color(0.63f, 0.7f, 0.84f);

        dialoguePromptStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };
        dialoguePromptStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
    }

    private static Texture2D CreateDialogueTexture(Color color, string textureName)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = textureName,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
