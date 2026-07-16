using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds a PauseButton (top-right HUD corner, opposite the HP bar) and a PausePanel (Resume/
/// Restart/Main Menu) under GameCanvas, and wires a PauseManager up to both. Idempotent like
/// LevelResultSetup: if PauseManager.pausePanel is already assigned, the panel's contents are
/// left completely untouched (no child searched for/rebuilt by name) so any hand customization
/// survives re-running this tool. Run once per Level scene (Level1, Level2, ...). Editor-only.
/// </summary>
public static class PauseSetup
{
    private const string UndoLabel = "Setup Pause";

    [MenuItem("Tools/Zombie War/Setup Pause Button and Panel")]
    public static void Setup()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        Transform gameCanvasTransform = FindInActiveScene(activeScene, "GameCanvas");

        if (gameCanvasTransform == null)
        {
            Debug.LogError("[PauseSetup] Could not find 'GameCanvas' in the active scene. Aborted.");
            return;
        }

        Transform playerTransform = FindInActiveScene(activeScene, "Player");
        WeaponController weaponController = playerTransform != null ? playerTransform.GetComponent<WeaponController>() : null;
        PlayerBombController bombController = playerTransform != null ? playerTransform.GetComponent<PlayerBombController>() : null;

        Transform levelResultManagerTransform = FindInActiveScene(activeScene, "LevelResultManager");
        LevelResultManager levelResultManager = levelResultManagerTransform != null ? levelResultManagerTransform.GetComponent<LevelResultManager>() : null;

        if (levelResultManager == null)
        {
            Debug.LogWarning("[PauseSetup] Could not find 'LevelResultManager' in the active scene — run Tools > Zombie War > Setup Level Result first. PausePanel's Restart/Main Menu buttons won't work without it.");
        }

        Transform managerTransform = FindInActiveScene(activeScene, "PauseManager");
        GameObject managerObject;

        if (managerTransform != null)
        {
            managerObject = managerTransform.gameObject;
        }
        else
        {
            managerObject = new GameObject("PauseManager");
            Undo.RegisterCreatedObjectUndo(managerObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(managerObject, activeScene);
        }

        PauseManager pauseManager = managerObject.GetComponent<PauseManager>();

        if (pauseManager == null)
        {
            pauseManager = Undo.AddComponent<PauseManager>(managerObject);
        }

        SerializedObject serializedManager = new SerializedObject(pauseManager);

        GameObject pausePanel = GetExistingReference(serializedManager, "pausePanel");
        Button resumeButton = null;
        Button restartButton = null;
        Button menuButton = null;

        if (pausePanel == null)
        {
            pausePanel = BuildPausePanel(gameCanvasTransform, activeScene, out resumeButton, out restartButton, out menuButton);
        }
        else
        {
            Debug.Log("[PauseSetup] PausePanel is already set up — leaving its contents untouched.");
        }

        CanvasGroup pauseCanvasGroup = EnsureCanvasGroup(pausePanel);

        Button pauseButton = GetOrCreatePauseButton(gameCanvasTransform, out bool pauseButtonIsNew);

        SetReference(serializedManager, "pausePanel", pausePanel);
        SetReference(serializedManager, "pauseCanvasGroup", pauseCanvasGroup);
        SetReference(serializedManager, "weaponController", weaponController);
        SetReference(serializedManager, "bombController", bombController);
        SetReference(serializedManager, "levelResultManager", levelResultManager);

        serializedManager.ApplyModifiedProperties();

        AddButtonListenerOnce(pauseButton, pauseManager, nameof(PauseManager.TogglePause));
        AddButtonListenerOnce(resumeButton, pauseManager, nameof(PauseManager.Resume));
        AddButtonListenerOnce(restartButton, pauseManager, nameof(PauseManager.RestartLevel));
        AddButtonListenerOnce(menuButton, pauseManager, nameof(PauseManager.ReturnToMainMenu));

        if (pauseButtonIsNew)
        {
            Debug.Log("[PauseSetup] Created 'PauseButton' in the top-right HUD corner.");
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = managerObject;

        Debug.Log("[PauseSetup] PauseManager wired. Save the scene (Ctrl+S), then repeat this on every other Level scene (Level1, Level2, ...).");
    }

    private static GameObject GetExistingReference(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.objectReferenceValue as GameObject : null;
    }

    private static CanvasGroup EnsureCanvasGroup(GameObject panel)
    {
        if (panel == null)
        {
            return null;
        }

        CanvasGroup canvasGroup = panel.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(panel);
        }

        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        return canvasGroup;
    }

    private static Button GetOrCreatePauseButton(Transform gameCanvasTransform, out bool isNew)
    {
        Transform existing = FindImmediateChild(gameCanvasTransform, "PauseButton");
        isNew = existing == null;

        GameObject buttonObject;

        if (existing != null)
        {
            buttonObject = existing.gameObject;
        }
        else
        {
            buttonObject = new GameObject("PauseButton", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(buttonObject, UndoLabel);
            buttonObject.transform.SetParent(gameCanvasTransform, false);
        }

        Image image = buttonObject.GetComponent<Image>();

        if (image == null)
        {
            image = Undo.AddComponent<Image>(buttonObject);
        }

        Button button = buttonObject.GetComponent<Button>();

        if (button == null)
        {
            button = Undo.AddComponent<Button>(buttonObject);
            button.targetGraphic = image;
        }

        if (isNew)
        {
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.75f);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(88f, 88f);
            rect.anchoredPosition = new Vector2(-24f, -24f);

            TMP_Text label = GetOrCreateTmpText(buttonObject.transform, "Label", "II", 40f, out _);
            RectTransform labelRect = label.rectTransform;
            StretchFull(labelRect);
            label.color = Color.white;
        }

        return button;
    }

    private static GameObject BuildPausePanel(Transform gameCanvasTransform, Scene scene, out Button resumeButton, out Button restartButton, out Button menuButton)
    {
        RectTransform panelRect = GetOrCreateUIChildImage(gameCanvasTransform, "PausePanel", out Image background, out bool panelIsNew);

        if (panelIsNew)
        {
            StretchFull(panelRect);
            background.color = new Color(0.03f, 0.03f, 0.03f, 0.85f);
        }

        background.raycastTarget = true;

        TMP_Text title = GetOrCreateTmpText(panelRect, "TitleText", "PAUSED", 72f, out bool titleIsNew);

        if (titleIsNew)
        {
            title.color = Color.white;
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.65f);
            titleRect.anchorMax = new Vector2(0.5f, 0.65f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(900f, 120f);
            titleRect.anchoredPosition = Vector2.zero;
        }

        resumeButton = GetOrCreateButton(panelRect, "ResumeButton", "RESUME", out bool resumeIsNew);

        if (resumeIsNew)
        {
            RectTransform resumeRect = resumeButton.GetComponent<RectTransform>();
            resumeRect.anchorMin = new Vector2(0.5f, 0.48f);
            resumeRect.anchorMax = new Vector2(0.5f, 0.48f);
            resumeRect.pivot = new Vector2(0.5f, 0.5f);
            resumeRect.sizeDelta = new Vector2(420f, 90f);
            resumeRect.anchoredPosition = Vector2.zero;
        }

        restartButton = GetOrCreateButton(panelRect, "RestartButton", "RESTART", out bool restartIsNew);

        if (restartIsNew)
        {
            RectTransform restartRect = restartButton.GetComponent<RectTransform>();
            restartRect.anchorMin = new Vector2(0.5f, 0.35f);
            restartRect.anchorMax = new Vector2(0.5f, 0.35f);
            restartRect.pivot = new Vector2(0.5f, 0.5f);
            restartRect.sizeDelta = new Vector2(420f, 90f);
            restartRect.anchoredPosition = Vector2.zero;
        }

        menuButton = GetOrCreateButton(panelRect, "MenuButton", "MAIN MENU", out bool menuIsNew);

        if (menuIsNew)
        {
            RectTransform menuRect = menuButton.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0.5f, 0.22f);
            menuRect.anchorMax = new Vector2(0.5f, 0.22f);
            menuRect.pivot = new Vector2(0.5f, 0.5f);
            menuRect.sizeDelta = new Vector2(420f, 90f);
            menuRect.anchoredPosition = Vector2.zero;
        }

        if (panelIsNew)
        {
            panelRect.gameObject.SetActive(false);
        }

        return panelRect.gameObject;
    }

    private static Button GetOrCreateButton(Transform parent, string name, string label, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject buttonObject;
        isNew = existing == null;

        if (existing != null)
        {
            buttonObject = existing.gameObject;
        }
        else
        {
            buttonObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(buttonObject, UndoLabel);
            buttonObject.transform.SetParent(parent, false);
        }

        Image image = buttonObject.GetComponent<Image>();

        if (image == null)
        {
            image = Undo.AddComponent<Image>(buttonObject);
        }

        if (isNew)
        {
            image.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        }

        Button button = buttonObject.GetComponent<Button>();

        if (button == null)
        {
            button = Undo.AddComponent<Button>(buttonObject);
            button.targetGraphic = image;
        }

        if (isNew)
        {
            TMP_Text label3D = GetOrCreateTmpText(buttonObject.transform, "Label", label, 32f, out _);
            RectTransform labelRect = label3D.rectTransform;
            StretchFull(labelRect);
            label3D.color = Color.white;
        }

        return button;
    }

    private static void AddButtonListenerOnce(Button button, Object target, string methodName)
    {
        if (button == null)
        {
            return;
        }

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentTarget(i) == target && button.onClick.GetPersistentMethodName(i) == methodName)
            {
                return;
            }
        }

        System.Reflection.MethodInfo method = target.GetType().GetMethod(methodName, System.Type.EmptyTypes);

        if (method == null)
        {
            Debug.LogWarning($"[PauseSetup] Could not find parameterless method '{methodName}' on {target.GetType().Name}.");
            return;
        }

        UnityAction action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, method);
        UnityEventTools.AddPersistentListener(button.onClick, action);
    }

    private static void SetReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            property.objectReferenceValue = value;
        }
        else
        {
            Debug.LogWarning($"[PauseSetup] Property '{propertyName}' not found on {serializedObject.targetObject?.GetType().Name}.");
        }
    }

    private static TMP_Text GetOrCreateTmpText(Transform parent, string name, string defaultText, float fontSize, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject textObject;
        isNew = existing == null;

        if (existing != null)
        {
            textObject = existing.gameObject;
        }
        else
        {
            textObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, UndoLabel);
            textObject.transform.SetParent(parent, false);
        }

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();

        if (text == null)
        {
            text = Undo.AddComponent<TextMeshProUGUI>(textObject);
            text.text = defaultText;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        EditorUtility.SetDirty(text);
        return text;
    }

    private static RectTransform GetOrCreateUIChildImage(Transform parent, string name, out Image image, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject childObject;
        isNew = existing == null;

        if (existing != null)
        {
            childObject = existing.gameObject;
        }
        else
        {
            childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);
            childObject.transform.SetParent(parent, false);
        }

        image = childObject.GetComponent<Image>();

        if (image == null)
        {
            image = Undo.AddComponent<Image>(childObject);
        }

        EditorUtility.SetDirty(childObject);
        return childObject.GetComponent<RectTransform>();
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static Transform FindImmediateChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == name)
            {
                return child;
            }
        }

        return null;
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
