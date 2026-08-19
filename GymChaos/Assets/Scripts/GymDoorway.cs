using UnityEngine;

/// <summary>
/// Runtime doorway used by the visitor system. The opening is built by
/// GymInteriorBuilder; this component only owns the shared passage points and
/// keeps the visual door panel in its authored doorway position while a visitor
/// is using the passage. The gameplay passage is intentionally non-solid for
/// visitors; the black inner panel is a static visual wall, not a swinging or
/// sliding door leaf.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class GymDoorway : MonoBehaviour
{
    public static GymDoorway Instance { get; private set; }

    private Transform doorPanel;
    private Vector3 closedLocalPosition;
    private Vector3 openLocalPosition;
    private Quaternion closedLocalRotation;
    private Quaternion openLocalRotation;
    private Collider panelCollider;
    private float openBlend;
    private int openRequests;

    public Vector3 InteriorPoint { get; private set; }
    public Vector3 ExteriorPoint { get; private set; }
    public Vector3 DoorCenter { get; private set; }
    public bool IsOpen => openBlend >= 0.96f;
    public Transform DoorPanel => doorPanel;
    public bool HasStaticPanelPose => doorPanel == null ||
        (Vector3.Distance(doorPanel.localPosition, closedLocalPosition) < 0.0001f &&
         Quaternion.Angle(doorPanel.localRotation, closedLocalRotation) < 0.01f);

    public static GymDoorway Create(
        Transform parent,
        Vector3 doorCenter,
        Vector3 interiorPoint,
        Vector3 exteriorPoint,
        Transform panel)
    {
        GameObject doorwayObject = new GameObject("Gym Visitor Doorway");
        doorwayObject.transform.SetParent(parent, true);
        doorwayObject.transform.position = doorCenter;
        GymDoorway doorway = doorwayObject.AddComponent<GymDoorway>();
        doorway.Configure(doorCenter, interiorPoint, exteriorPoint, panel);
        return doorway;
    }

    public void Configure(
        Vector3 doorCenter,
        Vector3 interiorPoint,
        Vector3 exteriorPoint,
        Transform panel)
    {
        DoorCenter = doorCenter;
        InteriorPoint = interiorPoint;
        ExteriorPoint = exteriorPoint;
        doorPanel = panel;
        openRequests = 0;

        if (doorPanel != null)
        {
            closedLocalPosition = doorPanel.localPosition;
            closedLocalRotation = doorPanel.localRotation;
            // Keep the black inner wall/panel exactly where the builder placed
            // it. Visitors ignore the doorway trigger/collider in their route
            // probe, so the panel can remain visually present without ever
            // rotating or changing its initial position.
            openLocalPosition = closedLocalPosition;
            openLocalRotation = closedLocalRotation;
            panelCollider = doorPanel.GetComponent<Collider>();
            if (panelCollider == null)
            {
                panelCollider = doorPanel.gameObject.AddComponent<BoxCollider>();
            }
            openBlend = 0f;
            ApplyPanelPose();
        }

        Debug.Log(
            $"GYMCHAOS_DOOR_OK interior={InteriorPoint} exterior={ExteriorPoint} open={IsOpen}",
            this);
    }

    public void RequestOpen()
    {
        openRequests++;
    }

    public void ReleaseOpen()
    {
        openRequests = Mathf.Max(0, openRequests - 1);
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

    private void Update()
    {
        float target = openRequests > 0 ? 1f : 0f;
        openBlend = Mathf.MoveTowards(openBlend, target, Time.deltaTime * 5f);
        ApplyPanelPose();
    }

    private void ApplyPanelPose()
    {
        if (doorPanel == null)
        {
            return;
        }

        doorPanel.localPosition = closedLocalPosition;
        doorPanel.localRotation = closedLocalRotation;
        if (panelCollider != null)
        {
            // The panel is a visual inner wall. Its collision would make the
            // static visual contradict the visitor passage, so leave it
            // non-solid for the whole runtime.
            panelCollider.enabled = false;
        }
    }
}
