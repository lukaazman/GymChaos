using UnityEditor;
using UnityEngine;

internal static class GymDoorHeadDiagnostic
{
    [MenuItem("Tools/GymChaos/Diagnose Door and Headwear")]
    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.Log("Run this diagnostic in Play Mode.");
            return;
        }
        foreach (var agent in Object.FindObjectsByType<GymVisitorAgent>(FindObjectsSortMode.None))
        {
            var fighter = agent.GetComponent<EnemyFighter>();
            Debug.Log($"DOOR_DIAG {fighter.Identity} pos={fighter.transform.position} target={agent.TravelTargetForVerification} blocker={fighter.LastVisitorRouteBlocker}");
        }
        var rig = Object.FindFirstObjectByType<PlayerHandRig>();
        if (rig == null) return;
        foreach (var bone in rig.GetComponentsInChildren<Transform>(true))
            if (bone.name.Contains("spine.00") || bone.name.StartsWith("Equipped "))
                Debug.Log($"HEAD_DIAG {bone.name} pos={bone.position} scale={bone.lossyScale} up={bone.up} forward={bone.forward} active={bone.gameObject.activeInHierarchy}");
        foreach (var renderer in rig.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.name.StartsWith("Equipped "))
                Debug.Log($"HAT_DIAG {renderer.name} bounds={renderer.bounds} enabled={renderer.enabled} forceOff={renderer.forceRenderingOff}");
    }
}
