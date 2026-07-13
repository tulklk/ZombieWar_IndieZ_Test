using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Flips MainMenuUIController.useLoadingScene on so LevelButton clicks route through
/// LoadingScene before the target level loads, instead of loading it directly. The
/// LoadingScene destination itself already defaults to "MainMenu" on a cold boot (see
/// LoadingSceneController.defaultSceneName) and to whichever level SceneLoadData carries
/// when routed from here. Editor-only, lives under Assets/Editor. Safe to run more than
/// once.
/// </summary>
public static class LoadingFlowSetup
{
    private const string ManagerName = "MainMenuManager";

    [MenuItem("Tools/Zombie War/Enable Loading Scene Flow")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[LoadingFlowSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Transform managerTransform = FindInActiveScene(activeScene, ManagerName);
        MainMenuUIController controller = managerTransform != null ? managerTransform.GetComponent<MainMenuUIController>() : null;

        if (controller == null)
        {
            Debug.LogError($"[LoadingFlowSetup] Could not find '{ManagerName}' with a MainMenuUIController component. Run Tools > Zombie War > Create Level Select Panel first, and make sure MainMenu.unity is the open scene.");
            return;
        }

        SerializedObject serializedController = new SerializedObject(controller);
        SerializedProperty useLoadingSceneProperty = serializedController.FindProperty("useLoadingScene");

        if (useLoadingSceneProperty == null)
        {
            Debug.LogError("[LoadingFlowSetup] MainMenuUIController has no 'useLoadingScene' field — the script may have changed.");
            return;
        }

        useLoadingSceneProperty.boolValue = true;
        serializedController.ApplyModifiedProperties();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = controller.gameObject;

        Debug.Log("[LoadingFlowSetup] MainMenuUIController.useLoadingScene enabled — LevelButton clicks now route through LoadingScene first. Save the scene (Ctrl+S).");
    }

    /// <summary>Searches every root GameObject's full hierarchy, including inactive objects.</summary>
    private static Transform FindInActiveScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
