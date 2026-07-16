using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off cleanup: removes the legacy plain-text stat block (BossNameText/BossTypeText/
/// BossStatsRoot/WarningText under BossIntroPanel) that was overlapping with the panel's own
/// icon-based stats display, plus the redundant BossNameText under BossHealthPanel. Safe to
/// re-run — an already-removed child is silently skipped. BossIntroUI/BossHealthUI already
/// null-check every one of these fields before use, so leaving their references pointing at
/// now-deleted objects is harmless (they just skip setting that particular text). Editor-only.
/// </summary>
public static class BossUICleanup
{
    [MenuItem("Tools/Zombie War/Remove Redundant Boss Intro-Health Text")]
    public static void RemoveRedundantText()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BossUICleanup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        int removedCount = 0;

        Transform bossIntroPanel = FindInActiveScene(activeScene, "BossIntroPanel");
        removedCount += RemoveChildIfPresent(bossIntroPanel, "BossNameText");
        removedCount += RemoveChildIfPresent(bossIntroPanel, "BossTypeText");
        removedCount += RemoveChildIfPresent(bossIntroPanel, "BossStatsRoot");
        removedCount += RemoveChildIfPresent(bossIntroPanel, "WarningText");

        Transform bossHealthPanel = FindInActiveScene(activeScene, "BossHealthPanel");
        removedCount += RemoveChildIfPresent(bossHealthPanel, "BossNameText");

        if (removedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Debug.Log($"[BossUICleanup] Removed {removedCount} GameObject(s) — save the scene (Ctrl+S) to persist.");
    }

    private static int RemoveChildIfPresent(Transform parent, string childName)
    {
        if (parent == null)
        {
            return 0;
        }

        Transform child = parent.Find(childName);

        if (child == null)
        {
            return 0;
        }

        Object.DestroyImmediate(child.gameObject);
        return 1;
    }

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
