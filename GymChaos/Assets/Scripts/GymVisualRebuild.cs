using UnityEngine;

/// <summary>
/// Applies a version change to the scene that is already running: every
/// character on an authored rig swaps its body in place, so an appearance-menu
/// pick is visible immediately instead of after the next scene load.
/// </summary>
public static class GymVisualRebuild
{
    /// <summary>Rebuilds every character visual whose version changed.</summary>
    public static int ApplyAll()
    {
        int rebuilt = 0;
        ExternalRiggedCharacterVisual[] visuals =
            Object.FindObjectsByType<ExternalRiggedCharacterVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < visuals.Length; i++)
        {
            ExternalRiggedCharacterVisual visual = visuals[i];
            if (visual == null) continue;
            if (visual.RebuildForVersion()) rebuilt++;
        }
        GymVisitorVehicle[] vehicles =
            Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < vehicles.Length; i++)
        {
            GymVisitorVehicle vehicle = vehicles[i];
            if (vehicle == null) continue;
            if (vehicle.RebuildVersionForAppearance()) rebuilt++;
        }
        // The police cruiser is not a visitor vehicle: its director owns it.
        GymPoliceDirector police = GymPoliceDirector.ActiveInstance;
        if (police != null && police.RebuildVersionForAppearance()) rebuilt++;
        Debug.Log("GYMCHAOS_VISUAL_REBUILD_APPLIED characters+vehicles=" + rebuilt);
        return rebuilt;
    }
}
