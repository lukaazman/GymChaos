using UnityEngine;

public sealed class GymDialogueCamera : MonoBehaviour
{
    private const float DialogueCameraDistance = 2.85f;
    private const float DialogueCameraSideOffset = 0.48f;
    private const float DialogueCameraFieldOfView = 50f;
    private const float DialogueCameraTransitionDuration = 0.42f;
    private const float DialogueCameraResponseDuration = 0.24f;
    private const float DialogueCameraAmbientFrequency = 0.72f;
    private const float DialogueCameraAmbientSideOffset = 0.0035f;
    private const float DialogueCameraAmbientVerticalOffset = 0.0045f;
    private const float DialogueCameraResponseSideOffset = 0.012f;
    private const float DialogueCameraResponseVerticalOffset = 0.006f;

    private Camera playerCamera;
    private PlayerMovement player;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private float savedFieldOfView;
    private int savedCullingMask;
    private bool savedCursorCaptured;
    private CursorLockMode savedCursorLockState;
    private bool savedCursorVisible;
    private bool savedCinematicLock;
    private float savedTimeScale;

    private Vector3 dialogueFocusPoint;
    private Vector3 dialoguePosition;
    private Quaternion dialogueRotation;
    private Vector3 transitionStartPosition;
    private Quaternion transitionStartRotation;
    private float transitionStartedAt;
    private float responseStartedAt = -1f;
    private DialogueNode lastDialogueNode;
    private bool stateSaved;
    private bool active;

    public bool Begin(PlayerMovement targetPlayer, Transform npc)
    {
        if (targetPlayer == null || targetPlayer.playerCamera == null || npc == null)
        {
            return false;
        }

        if (active)
        {
            End();
        }

        player = targetPlayer;
        playerCamera = targetPlayer.playerCamera;
        savedLocalPosition = playerCamera.transform.localPosition;
        savedLocalRotation = playerCamera.transform.localRotation;
        savedFieldOfView = playerCamera.fieldOfView;
        savedCullingMask = playerCamera.cullingMask;
        savedCursorCaptured = player.CursorCaptured;
        savedCursorLockState = Cursor.lockState;
        savedCursorVisible = Cursor.visible;
        savedCinematicLock = player.IsCinematicLocked;
        savedTimeScale = Time.timeScale;
        stateSaved = true;

        // Use the actual head bone to choose the frame, but aim the camera at
        // the upper torso below it. That places the head just under the top
        // letterbox and leaves the dialogue panel covering the lower body,
        // rather than centering the head and hiding everything else.
        dialogueFocusPoint = GetDialogueFocusPoint(npc);
        Vector3 fromNpcToPlayer = Vector3.ProjectOnPlane(
            targetPlayer.transform.position - npc.position, Vector3.up);
        if (fromNpcToPlayer.sqrMagnitude < 0.01f)
        {
            fromNpcToPlayer = -npc.forward;
        }
        fromNpcToPlayer.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fromNpcToPlayer).normalized;
        dialoguePosition = dialogueFocusPoint +
            fromNpcToPlayer * DialogueCameraDistance +
            side * DialogueCameraSideOffset + Vector3.up * 0.08f;
        dialogueRotation = Quaternion.LookRotation(
            dialogueFocusPoint - dialoguePosition, Vector3.up);

        transitionStartPosition = playerCamera.transform.position;
        transitionStartRotation = playerCamera.transform.rotation;
        transitionStartedAt = Time.unscaledTime;
        responseStartedAt = -1f;
        lastDialogueNode = GetCurrentDialogueNode();

        // The normal gameplay camera deliberately excludes both player-only
        // layers. Re-adding the mirror layer here put the full player mesh
        // directly in front of this close-up camera during conversations.
        playerCamera.cullingMask = savedCullingMask &
            ~(1 << PlanarGymMirror.MirrorPlayerLayer) &
            ~(1 << PlanarGymMirror.FirstPersonPlayerLayer);
        player.SetCinematicLock(true);
        player.SetCursorCaptured(false);

        // Keep the global clock untouched so the existing Mixamo conversation
        // idle continues to run. The saved value is still restored on close
        // in case another system changes it while the talk is active.
        active = true;
        Debug.Log("GYMCHAOS_CINEMATIC_CAMERA_OK open=1 transition=1", this);
        return true;
    }

    private void LateUpdate()
    {
        if (!active || playerCamera == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        float transition = Ease01((now - transitionStartedAt) /
            DialogueCameraTransitionDuration);
        DialogueNode currentNode = GetCurrentDialogueNode();
        if (currentNode != null && currentNode != lastDialogueNode)
        {
            lastDialogueNode = currentNode;
            responseStartedAt = now;
        }

        float response = responseStartedAt >= 0f
            ? 1f - Ease01((now - responseStartedAt) /
                DialogueCameraResponseDuration)
            : 0f;
        float ambientPhase = Mathf.Max(0f, now - transitionStartedAt) *
            DialogueCameraAmbientFrequency;
        Vector3 cameraRight = dialogueRotation * Vector3.right;
        Vector3 cameraUp = dialogueRotation * Vector3.up;
        Vector3 offset = cameraRight *
            (Mathf.Sin(ambientPhase * 0.65f + 0.8f) *
                DialogueCameraAmbientSideOffset * transition +
             DialogueCameraResponseSideOffset * response);
        offset += cameraUp *
            (Mathf.Sin(ambientPhase) * DialogueCameraAmbientVerticalOffset *
                transition + DialogueCameraResponseVerticalOffset * response);

        Vector3 position = Vector3.Lerp(
            transitionStartPosition, dialoguePosition, transition) + offset;
        Quaternion rotation = Quaternion.Slerp(
            transitionStartRotation, dialogueRotation, transition);
        Vector3 lookPoint = dialogueFocusPoint + cameraUp * (response * 0.018f);
        Quaternion composedRotation = Quaternion.LookRotation(
            lookPoint - position, Vector3.up);
        rotation = Quaternion.Slerp(rotation, composedRotation, transition);

        playerCamera.transform.SetPositionAndRotation(position, rotation);
        playerCamera.fieldOfView = Mathf.Lerp(
            savedFieldOfView, DialogueCameraFieldOfView, transition);
    }

    public void End()
    {
        if (!stateSaved)
        {
            active = false;
            return;
        }

        active = false;
        if (playerCamera != null)
        {
            playerCamera.transform.localPosition = savedLocalPosition;
            playerCamera.transform.localRotation = savedLocalRotation;
            playerCamera.fieldOfView = savedFieldOfView;
            playerCamera.cullingMask = savedCullingMask;
        }
        if (player != null)
        {
            player.SetCinematicLock(savedCinematicLock);
            player.SetCursorCaptured(savedCursorCaptured);
        }

        // SetCursorCaptured keeps PlayerMovement's cursor ownership in sync;
        // restoring the exact Unity cursor values also preserves confined or
        // temporarily visible states that are not represented by its bool.
        Cursor.lockState = savedCursorLockState;
        Cursor.visible = savedCursorVisible;
        Time.timeScale = savedTimeScale;

        playerCamera = null;
        player = null;
        lastDialogueNode = null;
        responseStartedAt = -1f;
        stateSaved = false;
        Debug.Log("GYMCHAOS_CINEMATIC_CAMERA_OK open=0", this);
    }

    private static DialogueNode GetCurrentDialogueNode()
    {
        GymDialogueDirector director = GymDialogueDirector.Active;
        return director != null && GymDialogueDirector.IsDialogueActive
            ? director.CurrentNode
            : null;
    }

    private static float Ease01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static Vector3 GetDialogueFocusPoint(Transform npc)
    {
        Transform[] bones = npc.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            if (string.Equals(bones[i].name, "Head",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[i].position - Vector3.up * 0.72f;
            }
        }

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name.IndexOf("head",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return bones[i].position - Vector3.up * 0.72f;
            }
        }

        Renderer[] renderers = npc.GetComponentsInChildren<Renderer>(true);
        Bounds bodyBounds = default;
        bool hasBodyBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
            {
                continue;
            }

            if (!hasBodyBounds)
            {
                bodyBounds = renderer.bounds;
                hasBodyBounds = true;
            }
            else
            {
                bodyBounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBodyBounds)
        {
            return new Vector3(
                bodyBounds.center.x,
                Mathf.Lerp(bodyBounds.min.y, bodyBounds.max.y, 0.54f),
                bodyBounds.center.z);
        }

        return npc.position + Vector3.up * 1.65f;
    }

    private void OnDisable()
    {
        End();
    }

    private void OnDestroy()
    {
        End();
    }
}
