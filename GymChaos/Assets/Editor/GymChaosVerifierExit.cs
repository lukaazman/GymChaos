#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Keeps a batch verifier's result across the domain reload that can happen
/// when play mode exits (static fields reset to their initial failure value).
/// </summary>
public static class GymChaosVerifierExit
{
    private const string ResultKey = "GymChaos.VerifierResult";

    public static void Record(int code) => SessionState.SetInt(ResultKey, code);

    public static void Exit(int fallback)
    {
        int code = SessionState.GetInt(ResultKey, fallback);
        SessionState.EraseInt(ResultKey);
        EditorApplication.Exit(code);
    }
}
#endif
