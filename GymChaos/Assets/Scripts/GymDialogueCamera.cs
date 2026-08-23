using UnityEngine;

public sealed class GymDialogueCamera : MonoBehaviour
{
    private const float DialogueCameraDistance = 2.85f;
    private const float DialogueCameraSideOffset = 0.48f;
    private const float DialogueCameraFieldOfView = 50f;

    private Camera playerCamera;
    private PlayerMovement player;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private float savedFieldOfView;
    private int savedCullingMask;
    private bool savedCursorCaptured;
    private bool active;

    public bool Begin(PlayerMovement targetPlayer, Transform npc)
    {
        if (targetPlayer == null || targetPlayer.playerCamera == null || npc == null)
        {
            return false;
        }

        player = targetPlayer;
        playerCamera = targetPlayer.playerCamera;
        savedLocalPosition = playerCamera.transform.localPosition;
        savedLocalRotation = playerCamera.transform.localRotation;
        savedFieldOfView = playerCamera.fieldOfView;
        savedCullingMask = playerCamera.cullingMask;
        savedCursorCaptured = player.CursorCaptured;

        // Use the actual head bone to choose the frame, but aim the camera at
        // the upper torso below it. That places the head just under the top
        // letterbox and leaves the dialogue panel covering the lower body,
        // rather than centering the head and hiding everything else.
        Vector3 target = GetDialogueFocusPoint(npc);
        Vector3 fromNpcToPlayer = Vector3.ProjectOnPlane(
            targetPlayer.transform.position - npc.position, Vector3.up);
        if (fromNpcToPlayer.sqrMagnitude < 0.01f)
        {
            fromNpcToPlayer = -npc.forward;
        }
        fromNpcToPlayer.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fromNpcToPlayer).normalized;
        Vector3 cameraPosition = target +
            fromNpcToPlayer * DialogueCameraDistance +
            side * DialogueCameraSideOffset + Vector3.up * 0.08f;
        playerCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(target - cameraPosition, Vector3.up));
        playerCamera.fieldOfView = DialogueCameraFieldOfView;
        // The normal gameplay camera deliberately excludes both player-only
        // layers. Re-adding the mirror layer here put the full player mesh
        // directly in front of this close-up camera during conversations.
        playerCamera.cullingMask = savedCullingMask &
            ~(1 << PlanarGymMirror.MirrorPlayerLayer) &
            ~(1 << PlanarGymMirror.FirstPersonPlayerLayer);
        player.SetCinematicLock(true);
        player.SetCursorCaptured(false);
        active = true;
        Debug.Log("GYMCHAOS_CINEMATIC_CAMERA_OK open=1", this);
        return true;
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

    public void End()
    {
        if (!active)
        {
            return;
        }

        if (playerCamera != null)
        {
            playerCamera.transform.localPosition = savedLocalPosition;
            playerCamera.transform.localRotation = savedLocalRotation;
            playerCamera.fieldOfView = savedFieldOfView;
            playerCamera.cullingMask = savedCullingMask;
        }
        if (player != null)
        {
            player.SetCinematicLock(false);
            player.SetCursorCaptured(savedCursorCaptured);
        }
        active = false;
        Debug.Log("GYMCHAOS_CINEMATIC_CAMERA_OK open=0", this);
    }

    private void OnDestroy()
    {
        End();
    }
}
