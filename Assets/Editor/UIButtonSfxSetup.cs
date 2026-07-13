using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds a UIButtonClickSfx component to every known menu button in the currently
/// open scene (PlayBtn, SettingBtn, the Quality preset buttons, the Music/SFX toggle
/// buttons). Idempotent — skips buttons that already have the component. Editor-only,
/// lives under Assets/Editor.
/// </summary>
public static class UIButtonSfxSetup
{
    private static readonly string[] TargetButtonNames =
    {
        "PlayBtn",
        "SettingBtn",
        "LowButton",
        "MediumButton",
        "HighButton",
        "MusicButton",
        "SfxButton"
    };

    [MenuItem("Tools/Zombie War/Setup Button Click SFX")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[UIButtonSfxSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        int addedCount = 0;
        int alreadyPresentCount = 0;

        foreach (string buttonName in TargetButtonNames)
        {
            Transform buttonTransform = FindInActiveScene(activeScene, buttonName);

            if (buttonTransform == null)
            {
                continue;
            }

            Button button = buttonTransform.GetComponent<Button>();

            if (button == null)
            {
                continue;
            }

            if (buttonTransform.GetComponent<UIButtonClickSfx>() != null)
            {
                alreadyPresentCount++;
                continue;
            }

            Undo.AddComponent<UIButtonClickSfx>(buttonTransform.gameObject);
            addedCount++;
        }

        if (addedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Debug.Log($"[UIButtonSfxSetup] Added click SFX to {addedCount} button(s), {alreadyPresentCount} already had it. Make sure Tools > Zombie War > Setup Audio Manager has been run (for ButtonPress.mp3 to be wired), then save the scene (Ctrl+S).");
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
