using System;
using System.Collections.Generic;

[Serializable]
public sealed class DialogueChoice
{
    public string text;
    public int nextNodeIndex = -1;
    public int reputationDelta;
    // Optional effect of picking this choice (not serialized).
    [NonSerialized] public Action onChosen;

    public DialogueChoice(string choiceText, int nextNode, int reputationChange)
    {
        text = choiceText;
        nextNodeIndex = nextNode;
        reputationDelta = reputationChange;
    }
}

[Serializable]
public sealed class DialogueNode
{
    public string speaker;
    public string text;
    public readonly List<DialogueChoice> choices = new List<DialogueChoice>();

    public DialogueNode(string nodeSpeaker, string nodeText)
    {
        speaker = nodeSpeaker;
        text = nodeText;
    }

    public DialogueNode AddChoice(string choiceText, int nextNode, int reputationChange, Action onChosen = null)
    {
        choices.Add(new DialogueChoice(choiceText, nextNode, reputationChange) { onChosen = onChosen });
        return this;
    }
}

[Serializable]
public sealed class DialogueDefinition
{
    public string id;
    public string displayName;
    public readonly List<DialogueNode> nodes = new List<DialogueNode>();
}
