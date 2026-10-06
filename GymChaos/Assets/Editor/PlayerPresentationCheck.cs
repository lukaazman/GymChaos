using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class PlayerPresentationCheck
{
    const string Key = "GymChaos.FocusedPlayerCheck";
    static double start;
    static bool running;
    static PlayerPresentationCheck() { EditorApplication.update += Tick; }
    public static void Run() {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }
    static void Tick() {
        if (!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || running) return;
        if (start == 0) start = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup-start < 8) return;
        running = true;
        new GameObject("Focused presentation check").AddComponent<PlayerPresentationCheckRunner>();
    }
    public static void Finish() { SessionState.SetBool(Key,false); EditorApplication.Exit(0); }
}
public class PlayerPresentationCheckRunner : MonoBehaviour
{
    IEnumerator Start() {
        AudioListener.pause = true;
        var player = FindFirstObjectByType<PlayerMovement>();
        var rig = player.GetComponentInChildren<PlayerHandRig>(true);
        foreach (var v in FindObjectsByType<GymVisitorAgent>(FindObjectsSortMode.None)) v.enabled=false;
        foreach (var v in FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None)) v.enabled=false;
        player.enabled=false; rig.enabled=false;
        if (GymBackRoomBuilder.TryGetLockerPreviewPose(out Vector3 p,out Quaternion q)) {
            player.GetComponent<CharacterController>().enabled=false;
            player.transform.SetPositionAndRotation(p,q);
            player.playerCamera.transform.localRotation=Quaternion.identity;
        }
        string[] clips={"idle1","walking","running","jumping","punch_left","punch_right","throw_object_hard","throw_frisbee"};
        foreach(string clip in clips) {
          foreach(float phase in new[]{0f,.25f,.5f,.75f,.95f}) {
            rig.HoldStablePoseForVerification(clip,phase,out string detail);
            yield return null; yield return null;
            var model=rig.RuntimeModelRoot;
            var body=model.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh=new Mesh();body.BakeMesh(mesh,true);Bounds b=default;bool first=true;
            foreach(var v in mesh.vertices) {var w=body.transform.TransformPoint(v);if(first){b=new Bounds(w,Vector3.zero);first=false;}else b.Encapsulate(w);}
            Vector3 rootForward = Vector3.ProjectOnPlane(model.forward, Vector3.up).normalized;
            Vector3 gameplayForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            float rootYawError = Vector3.Angle(rootForward, gameplayForward);
            Transform leftThigh = rig.RuntimeLeftThigh;
            Transform rightThigh = rig.RuntimeRightThigh;
            Transform upperBody = rig.RuntimeUpperBodyRootBone;
            Vector3 lowerAxis = leftThigh != null && rightThigh != null
                ? Vector3.ProjectOnPlane(rightThigh.position - leftThigh.position, Vector3.up).normalized
                : Vector3.zero;
            Vector3 upperAxis = rig.RuntimeLeftHand != null && rig.RuntimeRightHand != null
                ? Vector3.ProjectOnPlane(
                    rig.RuntimeFirstPersonRightShoulder != null
                        ? rig.RuntimeFirstPersonRightShoulder.position - rig.RuntimeFirstPersonLeftShoulder.position
                        : rig.RuntimeRightHand.position - rig.RuntimeLeftHand.position,
                    Vector3.up).normalized
                : Vector3.zero;
            float lowerYaw = lowerAxis.sqrMagnitude > 0.001f
                ? Vector3.SignedAngle(model.right, lowerAxis, Vector3.up) : 0f;
            float upperYaw = upperAxis.sqrMagnitude > 0.001f
                ? Vector3.SignedAngle(model.right, upperAxis, Vector3.up) : 0f;
            float relativeYaw = lowerAxis.sqrMagnitude > 0.001f && upperAxis.sqrMagnitude > 0.001f
                ? Vector3.SignedAngle(upperAxis, lowerAxis, Vector3.up) : 0f;
            float lowerRootYaw = rig.RuntimeAnimationRootBone != null ? Vector3.SignedAngle(model.right, Vector3.ProjectOnPlane(rig.RuntimeAnimationRootBone.right, Vector3.up).normalized, Vector3.up) : 0f;
            Debug.Log($"PLAYER_FORWARD {clip}@{phase} root={rootForward} gameplay={gameplayForward} yawError={rootYawError:F2} lowerRootYaw={lowerRootYaw:F2} thighAxisYaw={lowerYaw:F2} upperYaw={upperYaw:F2} lowerUpperYaw={relativeYaw:F2} upperRoot={(upperBody != null ? upperBody.localRotation.eulerAngles.ToString() : "missing")}");

            Transform[] fpBoundary = {
                rig.RuntimeFirstPersonLeftShoulder, rig.RuntimeFirstPersonRightShoulder,
                rig.RuntimeFirstPersonLeftUpperArm, rig.RuntimeFirstPersonRightUpperArm
            };
            float maxBoundaryDepth = float.NegativeInfinity;
            bool boundaryVisible = false;
            foreach (Transform boundary in fpBoundary)
            {
                if (boundary == null) continue;
                Vector3 cameraPoint = player.playerCamera.transform.InverseTransformPoint(boundary.position);
                Vector3 viewport = player.playerCamera.WorldToViewportPoint(boundary.position);
                maxBoundaryDepth = Mathf.Max(maxBoundaryDepth, cameraPoint.z);
                boundaryVisible |= cameraPoint.z > player.playerCamera.nearClipPlane &&
                    viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
            }
            Transform fpRoot = rig.transform.Find("Player First Person Arm Rig");
            Vector3 fpRootCamera = fpRoot != null
                ? player.playerCamera.transform.InverseTransformPoint(fpRoot.position)
                : Vector3.zero;
            Debug.Log($"FP_BOUNDARY {clip}@{phase} maxDepth={maxBoundaryDepth:F3} visible={boundaryVisible} fpRootCamera={fpRootCamera}");
            foreach(var mirror in FindObjectsByType<PlanarGymMirror>(FindObjectsSortMode.None)) {mirror.RequestImmediateRefresh();mirror.ReflectionCamera.Render();}
            Capture(player.playerCamera,"focused-"+clip+"-"+Mathf.RoundToInt(phase*100));
            rig.TryGetFirstPersonHandCameraPositions(out Vector3 left,out Vector3 right);
            Debug.Log($"FP_FRAME {clip}@{phase} left={player.playerCamera.WorldToViewportPoint(rig.RuntimeFirstPersonLeftHand.position)} right={player.playerCamera.WorldToViewportPoint(rig.RuntimeFirstPersonRightHand.position)}");
            Destroy(mesh);
          }
        }
        Debug.Log("FOCUSED_PLAYER_CAPTURE_DONE");
        PlayerPresentationCheck.Finish();
    }
    static void Capture(Camera cam,string name) {
        var rt=new RenderTexture(1280,720,24);var old=cam.targetTexture;cam.targetTexture=rt;cam.Render();
        var prev=RenderTexture.active;RenderTexture.active=rt;var im=new Texture2D(1280,720,TextureFormat.RGB24,false);im.ReadPixels(new Rect(0,0,1280,720),0,0);im.Apply();
        File.WriteAllBytes(Path.GetFullPath("../.tools/"+name+".png"),im.EncodeToPNG());
        cam.targetTexture=old;RenderTexture.active=prev;Destroy(im);Destroy(rt);
    }
}
