using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wires SettingBtn to open/close SettingPanel with an animated pop scale + fade
/// (see SettingPanelToggle.cs) — adds a CanvasGroup to SettingPanel if it doesn't
/// already have one, adds SettingPanelToggle to SettingBtn, and wires the references.
/// Safe to run more than once. Editor-only, lives under Assets/Editor.
/// </summary>
public static class SettingPanelToggleSetup
{
    private const string SettingBtnName = "SettingBtn";
    private const string SettingPanelName = "SettingPanel";

    [MenuItem("Tools/Zombie War/Setup Setting Panel Toggle")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[SettingPanelToggleSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        Transform settingBtnTransform = FindInActiveScene(activeScene, SettingBtnName);
        if (settingBtnTransform == null)
        {
            Debug.LogError($"[SettingPanelToggleSetup] Could not find '{SettingBtnName}' in the active scene. Aborted.");
            return;
        }

        Transform settingPanelTransform = FindInActiveScene(activeScene, SettingPanelName);
        if (settingPanelTransform == null)
        {
            Debug.LogError($"[SettingPanelToggleSetup] Could not find '{SettingPanelName}' in the active scene. Aborted.");
            return;
        }

        RectTransform settingPanelRect = settingPanelTransform as RectTransform;

        CanvasGroup canvasGroup = settingPanelTransform.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(settingPanelTransform.gameObject);
        }

        SettingPanelToggle toggle = settingBtnTransform.GetComponent<SettingPanelToggle>();
        if (toggle == null)
        {
            toggle = Undo.AddComponent<SettingPanelToggle>(settingBtnTransform.gameObject);
        }

        SerializedObject serializedToggle = new SerializedObject(toggle);
        serializedToggle.FindProperty("settingPanel").objectReferenceValue = settingPanelRect;
        serializedToggle.FindProperty("settingPanelCanvasGroup").objectReferenceValue = canvasGroup;
        serializedToggle.ApplyModifiedProperties();

        EditorUtility.SetDirty(settingBtnTransform.gameObject);
        EditorUtility.SetDirty(settingPanelTransform.gameObject);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = settingBtnTransform.gameObject;

        Debug.Log("[SettingPanelToggleSetup] SettingBtn now opens/closes SettingPanel with an animated pop scale + fade. Test in Play Mode, then save the scene (Ctrl+S).");
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
