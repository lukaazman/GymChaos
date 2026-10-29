using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Short arrival shot for Lollipop Larry, like the race intro: the player
/// camera cuts over for the lower half of his fall, the landing (with the
/// "LOLLIPOP LARRY HAS ARRIVED" title) and hands back as he starts running.
/// The player is cinematic-locked meanwhile. His departure has no shot.
/// </summary>
public sealed class GymFixerArrivalCamera : MonoBehaviour
{
    public const string Title = "LOLLIPOP LARRY HAS ARRIVED";
    private const float BlendSeconds = 0.4f;
    private const float ShotDistance = 5.5f;
    private const float ShotHeight = 1.6f;
    private const float ShotFieldOfView = 40f;

    private PlayerMovement player;
    private Camera playerCamera;
    private Transform target;
    private Vector3 shotPosition;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private int savedCullingMask;
    private float savedFieldOfView;
    private Vector3 blendFromPosition;
    private Quaternion blendFromRotation;
    private float blendStartedAt;
    private bool focusing;
    private bool returning;
    private GameObject titleCanvas;
    private CanvasGroup titleGroup;

    public bool IsActive => focusing || returning;
    public bool IsTitleVisible => titleCanvas != null && titleCanvas.activeSelf;

    /// <summary>Starts the shot unless the player is busy (talking, driving, racing, dead).</summary>
    public bool Begin(Transform fixer, Vector3 landingPoint)
    {
        player = FindAnyObjectByType<PlayerMovement>();
        if (IsActive || player == null || player.playerCamera == null || player.IsDead ||
            player.IsCinematicLocked || player.IsExercising || GymRaceDirector.IsRaceActive)
        {
            return false;
        }
        playerCamera = player.playerCamera;
        target = fixer;
        shotPosition = FindShotPosition(landingPoint, playerCamera.transform.position);
        savedLocalPosition = playerCamera.transform.localPosition;
        savedLocalRotation = playerCamera.transform.localRotation;
        savedCullingMask = playerCamera.cullingMask;
        savedFieldOfView = playerCamera.fieldOfView;
        // Third-person shot: the first-person arms and held item stay out of frame.
        playerCamera.cullingMask &= ~(1 << PlanarGymMirror.FirstPersonPlayerLayer);
        blendFromPosition = playerCamera.transform.position;
        blendFromRotation = playerCamera.transform.rotation;
        blendStartedAt = Time.time;
        player.SetCinematicLock(true);
        focusing = true;
        Debug.Log($"GYMCHAOS_FIXER_ARRIVAL_CAM begin shot={shotPosition}", this);
        return true;
    }

    public void ShowTitle()
    {
        if (!focusing) return;
        if (titleCanvas == null) BuildTitle();
        titleCanvas.SetActive(true);
        titleGroup.alpha = 0f;
    }

    /// <summary>Blends back to the player's eyes, then unlocks.</summary>
    public void End()
    {
        if (!focusing) return;
        focusing = false;
        returning = true;
        blendFromPosition = playerCamera.transform.position;
        blendFromRotation = playerCamera.transform.rotation;
        blendStartedAt = Time.time;
        if (titleCanvas != null) titleCanvas.SetActive(false);
        Debug.Log("GYMCHAOS_FIXER_ARRIVAL_CAM end", this);
    }

    private void LateUpdate()
    {
        if (!IsActive) return;
        if (playerCamera == null || player == null)
        {
            Restore();
            return;
        }
        float t = GymShared.Ease01((Time.time - blendStartedAt) / BlendSeconds);
        if (focusing)
        {
            Vector3 look = target != null ? target.position + Vector3.up * 0.9f : shotPosition;
            Quaternion aim = Quaternion.LookRotation(look - shotPosition, Vector3.up);
            playerCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(blendFromPosition, shotPosition, t), Quaternion.Slerp(blendFromRotation, aim, t));
            playerCamera.fieldOfView = Mathf.Lerp(savedFieldOfView, ShotFieldOfView, t);
            if (titleGroup != null) titleGroup.alpha = Mathf.MoveTowards(titleGroup.alpha, 1f, Time.deltaTime * 4f);
            return;
        }
        Transform parent = playerCamera.transform.parent;
        Vector3 eye = parent != null ? parent.TransformPoint(savedLocalPosition) : savedLocalPosition;
        Quaternion eyeRotation = parent != null ? parent.rotation * savedLocalRotation : savedLocalRotation;
        playerCamera.transform.SetPositionAndRotation(
            Vector3.Lerp(blendFromPosition, eye, t), Quaternion.Slerp(blendFromRotation, eyeRotation, t));
        playerCamera.fieldOfView = Mathf.Lerp(ShotFieldOfView, savedFieldOfView, t);
        if (t >= 1f) Restore();
    }

    private void Restore()
    {
        if (playerCamera != null)
        {
            playerCamera.transform.localPosition = savedLocalPosition;
            playerCamera.transform.localRotation = savedLocalRotation;
            playerCamera.cullingMask = savedCullingMask;
            playerCamera.fieldOfView = savedFieldOfView;
        }
        if (player != null) player.SetCinematicLock(false);
        if (titleCanvas != null) titleCanvas.SetActive(false);
        focusing = returning = false;
    }

    private void OnDisable()
    {
        if (IsActive) Restore();
    }

    private void OnDestroy()
    {
        if (titleCanvas != null) Destroy(titleCanvas);
    }

    // Beside the landing point, on whichever side has a clear view of him.
    private static Vector3 FindShotPosition(Vector3 landingPoint, Vector3 viewer)
    {
        Vector3 toViewer = Vector3.ProjectOnPlane(viewer - landingPoint, Vector3.up);
        Vector3 forward = toViewer.sqrMagnitude > 0.01f ? toViewer.normalized : Vector3.forward;
        Vector3 head = landingPoint + Vector3.up * 1.2f;
        Vector3 feet = landingPoint + Vector3.up * 0.3f;
        float[] angles = { 35f, -35f, 70f, -70f, 0f, 110f, -110f, 180f };
        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 candidate = landingPoint + Quaternion.AngleAxis(angles[i], Vector3.up) * forward * ShotDistance +
                Vector3.up * ShotHeight;
            if (!Physics.CheckSphere(candidate, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                !Physics.Linecast(candidate, head, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                !Physics.Linecast(candidate, feet, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return candidate;
            }
        }
        return landingPoint + forward * ShotDistance + Vector3.up * ShotHeight;
    }

    private void BuildTitle()
    {
        titleCanvas = new GameObject("Fixer Arrival Title", typeof(RectTransform));
        Canvas canvas = titleCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        CanvasScaler scaler = titleCanvas.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        scaler.matchWidthOrHeight = 1f;
        titleGroup = titleCanvas.AddComponent<CanvasGroup>();
        titleGroup.interactable = false;
        titleGroup.blocksRaycasts = false;

        GameObject plateObject = new GameObject("Title Plate", typeof(RectTransform));
        plateObject.transform.SetParent(titleCanvas.transform, false);
        RectTransform plate = plateObject.GetComponent<RectTransform>();
        plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0.78f);
        plate.sizeDelta = new Vector2(860f, 92f);
        plate.localRotation = Quaternion.Euler(0f, 0f, 1.5f);
        PersonaShape backing = PersonaMenuStyle.CreateShape("Backing", plate, PersonaMenuStyle.Accent);
        Stretch(backing.rectTransform);
        backing.SetSlant(0.02f);

        GameObject textObject = new GameObject("Title", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(plate, false);
        Text text = textObject.GetComponent<Text>();
        Stretch(text.rectTransform);
        text.font = PersonaMenuStyle.LoadTitleFont(PersonaMenuStyle.BuiltinFont);
        text.text = Title;
        text.fontSize = 54;
        text.color = PersonaMenuStyle.Ink;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(3f, -3f);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
