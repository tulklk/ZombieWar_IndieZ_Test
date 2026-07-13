using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds UIButtonClickAnimation to every Button in the active scene (including inactive
/// ones, e.g. a closed SettingPanel). Idempotent — skips buttons that already have it.
/// Run once per scene (MainMenu, Level1, LoadingScene, ...) you want covered.
/// Editor-only, lives under Assets/Editor.
/// </summary>
public static class UIButtonClickAnimationSetup
{
    [MenuItem("Tools/Zombie War/Setup Button Click Animation")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[UIButtonClickAnimationSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        int addedCount = 0;
        int alreadyPresentCount = 0;

        foreach (GameObject root in activeScene.GetRootGameObjects())
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.GetComponent<UIButtonClickAnimation>() != null)
                {
                    alreadyPresentCount++;
                    continue;
                }

                Undo.AddComponent<UIButtonClickAnimation>(button.gameObject);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Debug.Log($"[UIButtonClickAnimationSetup] Added click animation to {addedCount} button(s) in '{activeScene.name}', {alreadyPresentCount} already had it. Test in Play Mode, then save the scene (Ctrl+S). Re-run this in every other scene (MainMenu/Level1/LoadingScene) you want covered.");
    }
}
