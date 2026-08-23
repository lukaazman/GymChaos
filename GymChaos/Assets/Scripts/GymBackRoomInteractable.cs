using UnityEngine;

public enum GymBackRoomInteractionType
{
    Prep,
    Locker,
    Bathroom
}

public sealed class GymBackRoomInteractable : MonoBehaviour
{
    public GymBackRoomInteractionType InteractionType { get; private set; }
    public string DisplayName { get; private set; }

    public void Configure(GymBackRoomInteractionType type, string displayName)
    {
        InteractionType = type;
        DisplayName = displayName;
    }
}
