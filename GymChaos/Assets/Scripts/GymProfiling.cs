using Unity.Profiling;

/// <summary>Shared profiler markers for main-thread work that can hitch.</summary>
public static class GymProfiling
{
    /// <summary>PNG/JPEG decode of a runtime GLB texture.</summary>
    public static readonly ProfilerMarker GlbTextureDecode =
        new ProfilerMarker("GymChaos.GlbTextureDecode");

    /// <summary>Building Unity meshes from runtime GLB buffers.</summary>
    public static readonly ProfilerMarker GlbMeshBuild =
        new ProfilerMarker("GymChaos.GlbMeshBuild");
}
