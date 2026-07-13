using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wires SaveBtn (under SettingPanel) to close SettingPanel via MainMenuUIController.
/// Settings (Quality preset, Music/SFX) already persist to PlayerPrefs the moment
/// they're changed — SaveBtn's job is purely "confirm and close" the panel. Adds a
/// persistent onClick listener (shows up in the Button's Inspector like any other
/// wired button) rather than a runtime-only one. Safe to run more than once.
/// Editor-only, lives under Assets/Editor.
/// </summary>
public static class SaveButtonSetup
{
    private const string SaveBtnName = "SaveBtn";
    private const string ManagerName = "MainMenuManager";

    [MenuItem("Tools/Zombie War/Setup Save Button")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[SaveButtonSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        Transform saveBtnTransform = FindInActiveScene(activeScene, SaveBtnName);
        if (saveBtnTransform == null)
        {
            Debug.LogError($"[LevelSelectPanelSetup] Could not find '{SaveBtnName}' in the active scene. Aborted.");
            return;
        }

        Transform managerTransform = FindInActiveScene(activeScene, ManagerName);
        MainMenuUIController controller = managerTransform != null ? managerTransform.GetComponent<MainMenuUIController>() : null;

        if (controller == null)
        {
            Debug.LogError($"[SaveButtonSetup] Could not find '{ManagerName}' with a MainMenuUIController component. Run Tools > Zombie War > Create Level Select Panel first.");
            return;
        }

        Button saveButton = saveBtnTransform.GetComponent<Button>();
        if (saveButton == null)
        {
            Debug.LogError($"[SaveButtonSetup] '{SaveBtnName}' has no Button component. Aborted.");
            return;
        }

        RemoveExistingCloseSettingPanelListener(saveButton, controller);
        UnityEventTools.AddPersistentListener(saveButton.onClick, controller.CloseSettingPanel);

        EditorUtility.SetDirty(saveButton);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = saveBtnTransform.gameObject;

        Debug.Log($"[SaveButtonSetup] '{SaveBtnName}' now closes SettingPanel on click. Settings themselves already save to PlayerPrefs immediately on change. Consider also running Setup Button Click SFX / Setup Button Click Animation to cover this button. Save the scene (Ctrl+S).");
    }

    /// <summary>Avoids stacking a duplicate listener if this tool is run more than once.</summary>
    private static void RemoveExistingCloseSettingPanelListener(Button button, MainMenuUIController controller)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (button.onClick.GetPersistentTarget(i) == controller &&
                button.onClick.GetPersistentMethodName(i) == nameof(MainMenuUIController.CloseSettingPanel))
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }
        }
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
