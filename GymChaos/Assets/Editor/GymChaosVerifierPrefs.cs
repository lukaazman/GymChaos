using UnityEditor;
using UnityEngine;

/// <summary>
/// EditorPrefs scoped to this Unity project. EditorPrefs live in the user's
/// registry and are shared by every Unity process, so a verifier's
/// "requested" flag set in one regression lane used to be picked up by the
/// [InitializeOnLoad] hook of a different verifier run in another lane
/// (e.g. GymChaosVisitorVerifier.Tick asserting inside a DoorwayPriority run).
/// Prefixing every key with the project path keeps lanes independent while
/// keeping the crash-recovery persistence EditorPrefs gave the verifiers.
/// </summary>
public static class GymChaosVerifierPrefs
{
    private static string scope;

    private static string Scope
    {
        get
        {
            if (scope == null)
            {
                string project = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/').ToLowerInvariant();
                scope = "GymChaos.Verifier[" + Hash128.Compute(project) + "].";
            }
            return scope;
        }
    }

    private static string Key(string key) => Scope + key;

    public static bool GetBool(string key, bool defaultValue = false) => EditorPrefs.GetBool(Key(key), defaultValue);
    public static void SetBool(string key, bool value) => EditorPrefs.SetBool(Key(key), value);
    public static string GetString(string key, string defaultValue = "") => EditorPrefs.GetString(Key(key), defaultValue);
    public static void SetString(string key, string value) => EditorPrefs.SetString(Key(key), value);
    public static bool HasKey(string key) => EditorPrefs.HasKey(Key(key));
    public static void DeleteKey(string key) => EditorPrefs.DeleteKey(Key(key));
}
