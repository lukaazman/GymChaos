using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-40)]
public sealed class GymDialogueDirector : MonoBehaviour
{
    private const float TalkTargetRange = 3.2f;
    private const float DialogueTargetHeight = 1.45f;
    private const float DialogueLineOfSightPadding = 0.08f;
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

        EnemyFighter nearby = instance.FindNearbyTalkTarget(targetPlayer);
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
        return FindNearbyTalkTarget(player, position);
    }

    public EnemyFighter FindNearbyTalkTarget(PlayerMovement targetPlayer)
    {
        if (targetPlayer == null)
        {
            return null;
        }

        return FindNearbyTalkTarget(targetPlayer, targetPlayer.transform.position);
    }

    private EnemyFighter FindNearbyTalkTarget(
        PlayerMovement targetPlayer, Vector3 position)
    {
        var fighters = EnemyFighter.RegisteredFighters;
        EnemyFighter closest = null;
        float bestDistance = TalkTargetRange * TalkTargetRange;
        for (int i = 0; i < fighters.Count; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null || !fighter.isActiveAndEnabled || fighter.IsDead || fighter.IsAggressive)
            {
                continue;
            }

            if (fighter.Identity == BodybuilderIdentity.Manwithsuit1 &&
                (targetPlayer == null ||
                 !IsPlayerInsideMainGym(targetPlayer.transform.position)))
            {
                continue;
            }

            if (!HasDialogueLineOfSight(targetPlayer, fighter))
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

    private static bool IsPlayerInsideMainGym(Vector3 position)
    {
        return !GymOutdoorBuilder.IsPlayerOutsideGym(position) &&
            !GymBackRoomBuilder.IsInsideRoom(position);
    }

    private static bool HasDialogueLineOfSight(
        PlayerMovement targetPlayer, EnemyFighter fighter)
    {
        if (targetPlayer == null || fighter == null)
        {
            return false;
        }

        Vector3 origin = targetPlayer.playerCamera != null
            ? targetPlayer.playerCamera.transform.position
            : targetPlayer.transform.position + Vector3.up * DialogueTargetHeight;
        Vector3 targetPoint = GetDialogueTargetPoint(fighter);
        Vector3 toTarget = targetPoint - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.001f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin, toTarget / distance, distance + DialogueLineOfSightPadding,
            ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) =>
            left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == targetPlayer.transform ||
                hit.transform.IsChildOf(targetPlayer.transform))
            {
                continue;
            }

            return hit.transform == fighter.transform ||
                hit.transform.IsChildOf(fighter.transform);
        }

        return false;
    }

    private static Vector3 GetDialogueTargetPoint(EnemyFighter fighter)
    {
        Transform[] bones = fighter.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            if (string.Equals(
                    bones[i].name, "Head", System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[i].position;
            }
        }

        return fighter.transform.position + Vector3.up * DialogueTargetHeight;
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

        switch (fighter.Identity)
        {
            case BodybuilderIdentity.Goku:
                AddMemberConversation(
                    result,
                    name,
                    negativeRep
                        ? "You keep bringing bad energy into the room. Train with me, or leave the drama outside."
                        : positiveRep
                            ? "I heard you helped some people today. Nice! Now, are we training or talking?"
                            : "Hey! You look strong! Want to train together? I was going to eat, but one more round sounds better.",
                    "Show me one clean technique.",
                    "Good choice. Clean first, power second. If the stance breaks, reset and try again.",
                    3,
                    "Race me to the next set.",
                    "All right! No flying this time, promise. Winner picks lunch!",
                    1,
                    "I need a quiet session.",
                    "Oh, okay! I will try to keep it down. Let me know if you want a training partner.",
                    0);
                break;

            case BodybuilderIdentity.Cbum:
                AddMemberConversation(
                    result,
                    name,
                    negativeRep
                        ? "Your last few reps looked rushed. Fix the shape before you ask for more."
                        : positiveRep
                            ? "You have been helping the room. Good. Keep that same control in your own sets."
                            : "Honestly, some days I still overthink everything. Then I get here, breathe, and do the next set.",
                    "Check my form.",
                    "Lower the weight, own the path, and make every rep look the same.",
                    3,
                    "I want to push heavier.",
                    "I get it. But I care how the rep feels in the muscle. Leave the ego out of this one.",
                    1,
                    "How do you handle pressure?",
                    "I try to stop treating every session like a verdict on who I am. Do the work, then go be a person.",
                    0);
                break;

            case BodybuilderIdentity.Ronnie:
                AddMemberConversation(
                    result,
                    name,
                    negativeRep
                        ? "You got a lot of noise in your sets and not enough work. Change that."
                        : positiveRep
                            ? "People say you have been helping. Good. Now bring that work ethic to your own set."
                            : "Yeah buddy! You warmed up? Come on over, we have some work to do!",
                    "I will earn the next rep.",
                    "Light weight, baby! Get yourself set. I am right here with you for this rep.",
                    4,
                    "I need a spot.",
                    "You got it, buddy. Tell me how many you are going for, and when you want a hand.",
                    2,
                    "I want to test my strength.",
                    "Ha! I like the enthusiasm. Work up to it first. We have all afternoon.",
                    0);
                break;

            case BodybuilderIdentity.JayCutler:
                AddMemberConversation(
                    result,
                    name,
                    negativeRep
                        ? "The room does not owe you respect. Your training has to earn it."
                        : positiveRep
                            ? "I see the work. Keep the standard high and the excuses short."
                            : "I have already got my session written down. What are you training today?",
                    "Tell me what to fix.",
                    "Start with the weak point you keep avoiding. Make it the first thing you train.",
                    3,
                    "I want a heavier set.",
                    "I have moved plenty of weight. These days I want the muscle doing the work. Slow that rep down.",
                    1,
                    "I am here to learn.",
                    "Keep a record. Training, food, recovery. It is easier to adjust something you actually track.",
                    2);
                break;

            case BodybuilderIdentity.Arnold:
                AddMemberConversation(
                    result,
                    name,
                    negativeRep
                        ? "You are spending too much energy making a scene. Put that energy into a goal."
                        : positiveRep
                            ? "You have started to become part of the room. Give that momentum a direction."
                            : "Tell me what you want to build. If you can picture it clearly, we can give this workout a purpose.",
                    "Help me choose a goal.",
                    "Choose something you can name, measure, and pursue when the room is empty.",
                    3,
                    "I want to look stronger.",
                    "Good! But do not just stand in front of the mirror. Give yourself something new to see tomorrow.",
                    1,
                    "I just want to enjoy the session.",
                    "Of course! Training should be something you look forward to. Bring a friend and push each other.",
                    1);
                break;

            case BodybuilderIdentity.Zyzz:
                AddMemberConversation(
                    result,
                    name,
                    negativeRep
                        ? "You are chasing attention before you have earned the session. Fix the order."
                        : positiveRep
                            ? "The room has noticed your energy. Use it to lift the mood, not just your profile."
                            : "There he is! Shoulders back, brah. You came to train, not apologise for taking up space.",
                    "Show me how to carry myself.",
                    "Start by enjoying yourself, brah. Get your set in, hit a pose, hype up the guy next to you.",
                    3,
                    "I want the session to be memorable.",
                    "Put a good track on. Train with your mates. There is a whole life outside counting reps.",
                    1,
                    "I am not here to perform.",
                    "Fair enough, brah. Do your thing. You do not need an audience to feel good about yourself.",
                    2);
                break;

            default:
                AddMemberConversation(
                    result,
                    name,
                    "The room has its own rhythm. Find yours before you start making noise.",
                    "I will keep it clean.",
                    "Good. A clean session gives everyone else room to train.",
                    2,
                    "I want to push harder.",
                    "Push with a plan, then leave the equipment ready for the next member.",
                    1,
                    "I am just looking around.",
                    "Look, learn, and do not block the lane.",
                    0);
                break;
        }

        return result;
    }

    private static void AddMemberConversation(
        DialogueDefinition result,
        string speaker,
        string opening,
        string firstChoice,
        string firstResponse,
        int firstDelta,
        string secondChoice,
        string secondResponse,
        int secondDelta,
        string thirdChoice,
        string thirdResponse,
        int thirdDelta)
    {
        int firstResponseIndex = result.nodes.Count + 1;
        result.nodes.Add(new DialogueNode(speaker, opening)
            .AddChoice(firstChoice, firstResponseIndex, firstDelta)
            .AddChoice(secondChoice, firstResponseIndex + 1, secondDelta)
            .AddChoice(thirdChoice, firstResponseIndex + 2, thirdDelta));
        result.nodes.Add(new DialogueNode(speaker, firstResponse)
            .AddChoice("I'll put it into practice.", -1, 1));
        result.nodes.Add(new DialogueNode(speaker, secondResponse)
            .AddChoice("Understood.", -1, 0));
        result.nodes.Add(new DialogueNode(speaker, thirdResponse)
            .AddChoice("I'll remember that.", -1, 0));
    }
    private static string GetDisplayName(EnemyFighter fighter)
    {
        if (fighter == null)
        {
            return "Gym regular";
        }
        switch (fighter.Identity)
        {
            case BodybuilderIdentity.Manwithsuit1: return "Reception";
            case BodybuilderIdentity.Cbum: return "Cbum";
            case BodybuilderIdentity.Ronnie: return "Ronnie Coleman";
            case BodybuilderIdentity.JayCutler: return "Jay Cutler";
            case BodybuilderIdentity.Arnold: return "Arnold";
            case BodybuilderIdentity.Zyzz: return "Zyzz";
            case BodybuilderIdentity.Goku: return "Goku";
            default: return fighter.Identity.ToString();
        }
    }

    private void OnGUI()
    {
        if (GymStartScreen.IsMenuVisible || GymPauseMenu.IsVisible || dialogueUi == null)
        {
            return;
        }

        if (IsDialogueActive)
        {
            DrawDialogueUI();
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
