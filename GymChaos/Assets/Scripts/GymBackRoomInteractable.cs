using System.Collections.Generic;
using UnityEngine;

public enum GymBackRoomInteractionType
{
    Prep,
    Locker,
    ToiletSeat
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
    // Reach measured from the edge of the trigger box; zero keeps the
    // caller's default interaction distance.
    public float InteractionRange { get; private set; }
    private Collider area;

    // Planar distance from a point to the trigger box (zero inside it), so a
    // long locker bank or bench is reachable along its whole length.
    public float PlanarDistance(Vector3 position)
    {
        Bounds bounds = area != null ? area.bounds : new Bounds(transform.position, Vector3.zero);
        position.y = bounds.center.y;
        Vector3 offset = bounds.ClosestPoint(position) - position;
        offset.y = 0f;
        return offset.magnitude;
    }

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

    public void SetDisplayName(string displayName)
    {
        DisplayName = displayName;
    }

    public void Configure(GymBackRoomInteractionType type, string displayName,
        float interactionRange = 0f)
    {
        InteractionType = type;
        DisplayName = displayName;
        InteractionRange = Mathf.Max(0f, interactionRange);
        area = GetComponent<Collider>();
    }
}
