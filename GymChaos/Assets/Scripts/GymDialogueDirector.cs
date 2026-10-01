using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-40)]
public sealed class GymDialogueDirector : MonoBehaviour
{
    private const float TalkTargetRange = 3.2f;
    private const float MarkTalkTargetRange = 5.5f;
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

    // Frame of the last node change, so a UI submit and the gameplay key in
    // the same frame cannot advance twice.
    private int lastStepFrame = -1;

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
        instance.dialogueUi.Bind(instance);
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

        if (instance.lastStepFrame == Time.frameCount)
        {
            // The panel already handled a button this frame.
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

    public bool HasAuthoredOpeningForVerification(EnemyFighter fighter, string requiredText)
    {
        if (fighter == null || string.IsNullOrEmpty(requiredText)) return false;
        DialogueDefinition authored = BuildDefinition(fighter);
        return authored.nodes.Count > 0 && authored.nodes[0].text != null &&
            authored.nodes[0].text.IndexOf(requiredText, System.StringComparison.OrdinalIgnoreCase) >= 0;
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
        float bestDistance = float.PositiveInfinity;
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

            float targetRange = fighter.Identity == BodybuilderIdentity.Mark
                ? MarkTalkTargetRange
                : TalkTargetRange;

            float distance = Vector3.ProjectOnPlane(fighter.transform.position - position, Vector3.up).sqrMagnitude;
            if (distance > targetRange * targetRange || distance >= bestDistance)
            {
                continue;
            }

            if (!HasDialogueLineOfSight(targetPlayer, fighter))
            {
                continue;
            }

            if (distance <= targetRange * targetRange && distance < bestDistance)
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

            if (fighter.Identity == BodybuilderIdentity.Mark &&
                IsProteinStoreCounterOccluder(hit.transform))
            {
                continue;
            }

            return hit.transform == fighter.transform ||
                hit.transform.IsChildOf(fighter.transform);
        }

        // Mark's body collider sits below the counter-top ray; once the only
        // hits are checkout props, nothing blocks the view across the counter.
        return fighter.Identity == BodybuilderIdentity.Mark;
    }

    private static bool IsProteinStoreCounterOccluder(Transform hitTransform)
    {
        for (Transform current = hitTransform; current != null;
             current = current.parent)
        {
            // Counter, terminal screen, and other checkout props all sit
            // between the customer side and Mark.
            if (current.name.StartsWith(
                    "Checkout_",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
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

    /// <summary>Choice button pressed in the dialogue panel.</summary>
    public void ChooseFromUi(int choiceIndex)
    {
        if (IsDialogueActive && lastStepFrame != Time.frameCount)
        {
            SelectChoice(choiceIndex);
        }
    }

    /// <summary>Continue button pressed in the dialogue panel.</summary>
    public void AdvanceFromUi()
    {
        if (IsDialogueActive && lastStepFrame != Time.frameCount &&
            CurrentNode != null && CurrentNode.choices.Count == 0)
        {
            AdvanceNode();
        }
    }

    private void AdvanceNode()
    {
        lastStepFrame = Time.frameCount;
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
        lastStepFrame = Time.frameCount;
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
            case BodybuilderIdentity.Mark:
                result.nodes.Add(new DialogueNode(name,
                    positiveRep
                        ? "Welcome back. Protein.com is stocked, and I saved you a spot by the good shaker display."
                        : "Hey. Welcome to protein.com. Take a look around, and ask if you need help finding a clean stack.")
                    .AddChoice("What do you recommend?", 1, 2)
                    .AddChoice("I am just browsing.", 1, 0)
                    .AddChoice("I need a quick reset.", 1, 1));
                result.nodes.Add(new DialogueNode(name,
                    "Start with something you will actually use. Train first, then make the next choice with a clear head.")
                    .AddChoice("Thanks, Mark.", -1, 1));
                return result;

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
            case BodybuilderIdentity.Mark: return "Mark";
            case BodybuilderIdentity.Davie: return "Davie";
            default: return fighter.Identity.ToString();
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
}
