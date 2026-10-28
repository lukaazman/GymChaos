using UnityEngine;

/// <summary>
/// Runtime doorway used by the visitor system. The opening is built by
/// GymInteriorBuilder; this component owns the shared passage points and the
/// automatic glass double door in the opening. The leaves carry no colliders,
/// so the passage stays clear for visitors, police and the player; the door
/// opens for anyone near or heading for it and closes once the opening is
/// clear.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class GymDoorway : MonoBehaviour
{
    public const string AutoDoorName = "Gym Entrance";

    public static GymDoorway Instance { get; private set; }

    private Transform doorPanel;
    private GymAutoDoor autoDoor;

    public Vector3 InteriorPoint { get; private set; }
    public Vector3 ExteriorPoint { get; private set; }
    public Vector3 DoorCenter { get; private set; }
    /// <summary>Root of the door leaves (no colliders; route grids ignore it).</summary>
    public Transform DoorPanel => doorPanel;
    public GymAutoDoor AutoDoor => autoDoor;

    public void Configure(
        Vector3 doorCenter,
        Vector3 interiorPoint,
        Vector3 exteriorPoint,
        Transform leavesRoot,
        GymAutoDoor door)
    {
        DoorCenter = doorCenter;
        InteriorPoint = interiorPoint;
        ExteriorPoint = exteriorPoint;
        doorPanel = leavesRoot;
        autoDoor = door;

        Debug.Log(
            $"GYMCHAOS_DOOR_OK interior={InteriorPoint} exterior={ExteriorPoint} " +
            $"autoDoor={(autoDoor != null ? autoDoor.LeafCount : 0)}",
            this);
    }

    // Visitors, police and the fixer used to hold the passage "open" for
    // their whole door route (and some held it for minutes). The automatic
    // door senses all of them directly, so these calls no longer drive it.
    public void RequestOpen()
    {
    }

    public void ReleaseOpen()
    {
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
