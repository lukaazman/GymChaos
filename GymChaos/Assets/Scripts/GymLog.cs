using System.Diagnostics;
using Object = UnityEngine.Object;

/// <summary>
/// High-frequency diagnostic markers (animation transitions, doorway yields,
/// route samples). Editor and development builds keep them for verifiers and
/// debugging; release builds drop the call and its string formatting, since
/// every Debug.Log in a WebGL player goes to the browser console.
/// </summary>
public static class GymLog
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Diag(string message, Object context = null)
    {
        UnityEngine.Debug.Log(message, context);
    }
}
