using System.Collections.Generic;
using UnityEngine;

public enum GymBackRoomInteractionType
{
    Prep,
    Locker,
    Bathroom
}

public sealed class GymBackRoomInteractable : MonoBehaviour
{
    private static readonly List<GymBackRoomInteractable> RegisteredItems =
        new List<GymBackRoomInteractable>();

    internal static IReadOnlyList<GymBackRoomInteractable> RegisteredInteractables =>
        RegisteredItems;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        RegisteredItems.Clear();
    }
    public GymBackRoomInteractionType InteractionType { get; private set; }
    public string DisplayName { get; private set; }

    private void OnEnable()
    {
        if (!RegisteredItems.Contains(this))
        {
            RegisteredItems.Add(this);
        }
    }

    private void OnDisable()
    {
        RegisteredItems.Remove(this);
    }

    private void OnDestroy()
    {
        RegisteredItems.Remove(this);
    }

    public void Configure(GymBackRoomInteractionType type, string displayName)
    {
        InteractionType = type;
        DisplayName = displayName;
    }
}
