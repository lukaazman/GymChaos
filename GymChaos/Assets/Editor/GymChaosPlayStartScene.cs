using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The game is built at runtime from the one gameplay scene. Pressing Play
/// always starts from that scene (boot menu first), whatever scene is open in
/// the Editor, and an empty "Untitled" scene left by a batch run is replaced
/// with it when the Editor loads.
/// </summary>
[InitializeOnLoad]
public static class GymChaosPlayStartScene
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";

    static GymChaosPlayStartScene()
    {
        if (Application.isBatchMode)
        {
            return;
        }
        SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene != null)
        {
            EditorSceneManager.playModeStartScene = scene;
        }
        EditorApplication.delayCall += OpenGameSceneIfUntitled;
    }

    private static void OpenGameSceneIfUntitled()
    {
        // Once per Editor session, so a scene the user creates later is kept.
        if (SessionState.GetBool("GymChaos.StartSceneChecked", false) ||
            EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1)
        {
            return;
        }
        SessionState.SetBool("GymChaos.StartSceneChecked", true);
        Scene active = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(active.path) && !active.isDirty && active.rootCount <= 2)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }
}
