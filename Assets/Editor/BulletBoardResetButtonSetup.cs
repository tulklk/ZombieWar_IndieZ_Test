using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Turns "BulletBoard" (the ammo HUD — GunIcon + bullet counts) into a tap target that
/// holsters the weapon (WeaponController.ResetToIdlePose) — a manual way to cancel aiming
/// and return to Idle/Walk/Run at any time. Adds a transparent Image (if the board doesn't
/// already have one) purely as the raycast/click target, no visual change. Safe to run
/// more than once. Editor-only, lives under Assets/Editor.
/// </summary>
public static class BulletBoardResetButtonSetup
{
    private const string BulletBoardName = "BulletBoard";
    private const string PlayerName = "Player";

    [MenuItem("Tools/Zombie War/Setup BulletBoard Holster Button")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BulletBoardResetButtonSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        Transform bulletBoardTransform = FindInActiveScene(activeScene, BulletBoardName);

        if (bulletBoardTransform == null)
        {
            Debug.LogError($"[BulletBoardResetButtonSetup] Could not find '{BulletBoardName}' in the active scene. Aborted — make sure Level1.unity is the open scene.");
            return;
        }

        Transform playerTransform = FindInActiveScene(activeScene, PlayerName);
        WeaponController weaponController = playerTransform != null ? playerTransform.GetComponentInChildren<WeaponController>(true) : null;

        if (weaponController == null)
        {
            Debug.LogError($"[BulletBoardResetButtonSetup] Could not find a WeaponController under '{PlayerName}'. Aborted.");
            return;
        }

        Image image = bulletBoardTransform.GetComponent<Image>();

        if (image == null)
        {
            image = Undo.AddComponent<Image>(bulletBoardTransform.gameObject);
            image.color = new Color(0f, 0f, 0f, 0f);
        }

        image.raycastTarget = true;
        EditorUtility.SetDirty(image);

        Button button = bulletBoardTransform.GetComponent<Button>();

        if (button == null)
        {
            button = Undo.AddComponent<Button>(bulletBoardTransform.gameObject);
        }

        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;

        RemoveExistingResetListener(button, weaponController);
        UnityEventTools.AddPersistentListener(button.onClick, weaponController.ResetToIdlePose);

        EditorUtility.SetDirty(button);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = bulletBoardTransform.gameObject;

        Debug.Log($"[BulletBoardResetButtonSetup] Tapping '{BulletBoardName}' now calls WeaponController.ResetToIdlePose() (holsters the weapon, back to Idle/Walk/Run). Save the scene (Ctrl+S).");
    }

    /// <summary>Avoids stacking a duplicate listener if this tool is run more than once.</summary>
    private static void RemoveExistingResetListener(Button button, WeaponController weaponController)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (button.onClick.GetPersistentTarget(i) == weaponController &&
                button.onClick.GetPersistentMethodName(i) == nameof(WeaponController.ResetToIdlePose))
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
