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
/// First run: builds WinPanel/LosePanel content under GameCanvas (background, title, time
/// text, Restart/Main Menu buttons) and wires a new LevelResultManager up to
/// ZombieWaveManager.OnAllWavesCompletedEvent + PlayerHealth.OnDied.
/// Every later run: if LevelResultManager.winPanel/losePanel are ALREADY assigned (i.e. this
/// isn't the first run), their contents are left completely untouched — no child is searched
/// for or rebuilt by name, so any hand customization (custom art, renamed/restructured
/// children, re-skinned buttons) survives re-running this tool. Only a CanvasGroup is
/// ensured on each panel (for the show animation) and the manager's non-child references
/// (timer text, player, wave-complete hookup) are refreshed. Editor-only, Assets/Editor.
/// </summary>
public static class LevelResultSetup
{
    private const string UndoLabel = "Setup Level Result";
    private const string WinSfxPath = "Assets/Audio/SFX/Panel/WinSFX.mp3";
    private const string LoseSfxPath = "Assets/Audio/SFX/Panel/LoseSFX.mp3";

    [MenuItem("Tools/Zombie War/Setup Level Result (Win Lose)")]
    public static void Setup()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        Transform gameCanvasTransform = FindInActiveScene(activeScene, "GameCanvas");

        if (gameCanvasTransform == null)
        {
            Debug.LogError("[LevelResultSetup] Could not find 'GameCanvas' in the active scene. Aborted.");
            return;
        }

        Transform timeCountTextTransform = FindInActiveScene(activeScene, "TimeCountText");
        TMP_Text timeCountText = timeCountTextTransform != null ? timeCountTextTransform.GetComponent<TMP_Text>() : null;

        if (timeCountText == null)
        {
            Debug.LogWarning("[LevelResultSetup] Could not find a 'TimeCountText' TMP object in the active scene — the timer won't have anywhere to display.");
        }

        Transform playerTransform = FindInActiveScene(activeScene, "Player");
        PlayerHealth playerHealth = playerTransform != null ? playerTransform.GetComponent<PlayerHealth>() : null;

        Transform waveManagerTransform = FindInActiveScene(activeScene, "WaveManager");
        ZombieWaveManager waveManager = waveManagerTransform != null ? waveManagerTransform.GetComponent<ZombieWaveManager>() : null;

        Transform managerTransform = FindInActiveScene(activeScene, "LevelResultManager");
        GameObject managerObject;

        if (managerTransform != null)
        {
            managerObject = managerTransform.gameObject;
        }
        else
        {
            managerObject = new GameObject("LevelResultManager");
            Undo.RegisterCreatedObjectUndo(managerObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(managerObject, activeScene);
        }

        LevelResultManager resultManager = managerObject.GetComponent<LevelResultManager>();

        if (resultManager == null)
        {
            resultManager = Undo.AddComponent<LevelResultManager>(managerObject);
        }

        SerializedObject serializedManager = new SerializedObject(resultManager);

        GameObject winPanel = GetExistingReference(serializedManager, "winPanel");
        TMP_Text winTimeText = null;
        Button winRestartButton = null;
        Button winMenuButton = null;

        if (winPanel == null)
        {
            winPanel = BuildResultPanel(
                gameCanvasTransform, activeScene, "WinPanel",
                "LEVEL COMPLETE", new Color(0.02f, 0.08f, 0.02f, 0.85f), new Color(0.4f, 1f, 0.4f, 1f),
                out winTimeText, out winRestartButton, out winMenuButton);
        }
        else
        {
            Debug.Log("[LevelResultSetup] WinPanel is already set up — leaving its contents untouched.");
        }

        GameObject losePanel = GetExistingReference(serializedManager, "losePanel");
        TMP_Text loseTimeText = null;
        Button loseRestartButton = null;
        Button loseMenuButton = null;

        if (losePanel == null)
        {
            losePanel = BuildResultPanel(
                gameCanvasTransform, activeScene, "LosePanel",
                "YOU DIED", new Color(0.1f, 0.02f, 0.02f, 0.85f), new Color(1f, 0.3f, 0.3f, 1f),
                out loseTimeText, out loseRestartButton, out loseMenuButton);
        }
        else
        {
            Debug.Log("[LevelResultSetup] LosePanel is already set up — leaving its contents untouched.");
        }

        CanvasGroup winCanvasGroup = EnsureCanvasGroup(winPanel);
        CanvasGroup loseCanvasGroup = EnsureCanvasGroup(losePanel);

        EnsureIntroAnimator(winPanel, PanelIntroAnimator.TitleAnimationStyle.SlideFromTop);
        EnsureIntroAnimator(losePanel, PanelIntroAnimator.TitleAnimationStyle.PunchScale);

        SetReference(serializedManager, "timeCountText", timeCountText);
        SetReference(serializedManager, "playerHealth", playerHealth);
        SetReference(serializedManager, "winPanel", winPanel);
        SetReference(serializedManager, "winCanvasGroup", winCanvasGroup);
        SetReferenceIfEmpty(serializedManager, "killCountText", FindChildTmpText(winPanel, "ZombieKillText"));
        SetReferenceIfEmpty(serializedManager, "scoreText", FindChildTmpText(winPanel, "ScoreText"));
        SetReference(serializedManager, "losePanel", losePanel);
        SetReference(serializedManager, "loseCanvasGroup", loseCanvasGroup);

        // Only fills these in the first time (while still unset) — a deliberately cleared or
        // hand-swapped SFX clip on a later run is left exactly as the user set it.
        SetReferenceIfEmpty(serializedManager, "winSfxClip", AssetDatabase.LoadAssetAtPath<AudioClip>(WinSfxPath));
        SetReferenceIfEmpty(serializedManager, "loseSfxClip", AssetDatabase.LoadAssetAtPath<AudioClip>(LoseSfxPath));

        // Same reasoning as PanelIntroAnimator's pacing fields above — explicitly requested
        // ("run slower"), safe to always reapply.
        SetFloatProperty(serializedManager, "countUpDuration", 1f);
        SetFloatProperty(serializedManager, "countUpGap", 0.2f);

        // Only overwritten when freshly built this run — an already-configured panel's
        // existing winTimeText/loseTimeText wiring (however the user has since renamed or
        // restructured that child) is left exactly as it was.
        if (winTimeText != null)
        {
            SetReference(serializedManager, "winTimeText", winTimeText);
        }

        if (loseTimeText != null)
        {
            SetReference(serializedManager, "loseTimeText", loseTimeText);
        }

        serializedManager.ApplyModifiedProperties();

        AddButtonListenerOnce(winRestartButton, resultManager, nameof(LevelResultManager.RestartLevel));
        AddButtonListenerOnce(winMenuButton, resultManager, nameof(LevelResultManager.ReturnToMainMenu));
        AddButtonListenerOnce(loseRestartButton, resultManager, nameof(LevelResultManager.RestartLevel));
        AddButtonListenerOnce(loseMenuButton, resultManager, nameof(LevelResultManager.ReturnToMainMenu));

        if (waveManager != null)
        {
            AddPersistentListenerOnce(waveManager.OnAllWavesCompletedEvent, resultManager, nameof(LevelResultManager.ShowWinPanel));
        }
        else
        {
            Debug.LogWarning("[LevelResultSetup] Could not find 'WaveManager' (ZombieWaveManager) in the active scene — WinPanel won't be shown automatically when all waves complete.");
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = managerObject;

        Debug.Log("[LevelResultSetup] LevelResultManager wired (fade-in CanvasGroup ensured on both panels). Enter Play Mode to see the timer counting on TimeCountText, then save the scene (Ctrl+S).");
    }

    private static GameObject GetExistingReference(SerializedObject serializedManager, string propertyName)
    {
        SerializedProperty property = serializedManager.FindProperty(propertyName);
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

        // interactable/blocksRaycasts are structural requirements for the panel to work as a
        // modal at all — alpha is deliberately left untouched, LevelResultManager's fade-in
        // coroutine drives it at runtime every time the panel is shown.
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        return canvasGroup;
    }

    /// <summary>
    /// Adds PanelIntroAnimator to the panel if missing, then (re)wires its title/stagger
    /// element references every run — these are tool-owned target references, not user
    /// art/naming, so keeping them in sync with whatever named children currently exist is
    /// safe. Only the component's own tunable settings (durations/distances/style — set
    /// once from the C# defaults when first added) are left alone on later runs.
    /// Title is looked up as "Title" (hand-renamed), then "Victory" (WinPanel's own
    /// hand-renamed title), then "TitleText" (BuildResultPanel's default name).
    /// Stagger elements are whichever of Cup/RestartButton/NextBtn/MenuButton exist.
    /// </summary>
    private static void EnsureIntroAnimator(GameObject panel, PanelIntroAnimator.TitleAnimationStyle titleStyle)
    {
        if (panel == null)
        {
            return;
        }

        PanelIntroAnimator introAnimator = panel.GetComponent<PanelIntroAnimator>();
        bool isNew = introAnimator == null;

        if (isNew)
        {
            introAnimator = Undo.AddComponent<PanelIntroAnimator>(panel);
        }

        Transform titleTransform = FindImmediateChild(panel.transform, "Title")
            ?? FindImmediateChild(panel.transform, "Victory")
            ?? FindImmediateChild(panel.transform, "TitleText");

        Transform[] staggerTransforms =
        {
            FindImmediateChild(panel.transform, "Cup"),
            FindImmediateChild(panel.transform, "RestartButton"),
            FindImmediateChild(panel.transform, "RestartBtn"),
            FindImmediateChild(panel.transform, "NextBtn"),
            FindImmediateChild(panel.transform, "MenuButton"),
            FindImmediateChild(panel.transform, "MenuBtn"),
        };

        SerializedObject serializedAnimator = new SerializedObject(introAnimator);

        SerializedProperty titleProperty = serializedAnimator.FindProperty("titleRect");

        if (titleProperty != null)
        {
            titleProperty.objectReferenceValue = titleTransform as RectTransform;
        }

        SerializedProperty titleStyleProperty = serializedAnimator.FindProperty("titleAnimationStyle");

        if (titleStyleProperty != null)
        {
            titleStyleProperty.enumValueIndex = (int)titleStyle;
        }

        SerializedProperty staggerProperty = serializedAnimator.FindProperty("staggerElements");

        if (staggerProperty != null)
        {
            int count = 0;

            foreach (Transform staggerTransform in staggerTransforms)
            {
                if (staggerTransform != null)
                {
                    count++;
                }
            }

            staggerProperty.arraySize = count;
            int index = 0;

            foreach (Transform staggerTransform in staggerTransforms)
            {
                if (staggerTransform != null)
                {
                    staggerProperty.GetArrayElementAtIndex(index++).objectReferenceValue = staggerTransform;
                }
            }
        }

        // Explicit, always-reapplied pacing values — requested directly ("run the
        // animations slower"), not a per-panel creative choice like the durations on other
        // tool-built elements, so it's safe to push these every run.
        SetFloatProperty(serializedAnimator, "titleDuration", 0.6f);
        SetFloatProperty(serializedAnimator, "elementDuration", 0.4f);
        SetFloatProperty(serializedAnimator, "staggerDelay", 0.2f);
        SetFloatProperty(serializedAnimator, "elementsStartDelay", 0.25f);

        serializedAnimator.ApplyModifiedProperties();
    }

    private static void SetFloatProperty(SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static TMP_Text FindChildTmpText(GameObject panel, string name)
    {
        if (panel == null)
        {
            return null;
        }

        Transform found = FindImmediateChild(panel.transform, name);
        return found != null ? found.GetComponent<TMP_Text>() : null;
    }

    /// <summary>
    /// Only ever called when winPanel/losePanel isn't wired yet on LevelResultManager (see
    /// Setup()) — but every position/color/size assignment below is ALSO individually gated
    /// behind its own element's isNew flag, as defense in depth: even if that outer guard
    /// somehow misses (e.g. the manager's reference got cleared while the named GameObjects
    /// still exist, hand-repositioned, in the scene), re-finding an already-existing child
    /// by name here will not silently reset its position/color back to the placeholder
    /// default.
    /// </summary>
    private static GameObject BuildResultPanel(
        Transform gameCanvasTransform, Scene scene, string panelName,
        string titleText, Color backgroundColor, Color titleColor,
        out TMP_Text timeText, out Button restartButton, out Button menuButton)
    {
        RectTransform panelRect = GetOrCreateUIChildImage(gameCanvasTransform, panelName, out Image background, out bool panelIsNew);

        if (panelIsNew)
        {
            StretchFull(panelRect);
            background.color = backgroundColor;
        }

        background.raycastTarget = true;

        TMP_Text title = GetOrCreateTmpText(panelRect, "TitleText", titleText, 72f, out bool titleIsNew);

        if (titleIsNew)
        {
            title.color = titleColor;
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.65f);
            titleRect.anchorMax = new Vector2(0.5f, 0.65f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(900f, 120f);
            titleRect.anchoredPosition = Vector2.zero;
        }

        timeText = GetOrCreateTmpText(panelRect, "TimeText", "00:00", 44f, out bool timeIsNew);

        if (timeIsNew)
        {
            timeText.color = Color.white;
            RectTransform timeRect = timeText.rectTransform;
            timeRect.anchorMin = new Vector2(0.5f, 0.52f);
            timeRect.anchorMax = new Vector2(0.5f, 0.52f);
            timeRect.pivot = new Vector2(0.5f, 0.5f);
            timeRect.sizeDelta = new Vector2(700f, 70f);
            timeRect.anchoredPosition = Vector2.zero;
        }

        restartButton = GetOrCreateButton(panelRect, "RestartButton", "PLAY AGAIN", out bool restartIsNew);

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

        AddPersistentListenerGeneric(button.onClick, target, methodName);
    }

    private static void AddPersistentListenerOnce(UnityEvent unityEvent, Object target, string methodName)
    {
        AddPersistentListenerGeneric(unityEvent, target, methodName);
    }

    private static void AddPersistentListenerGeneric(UnityEventBase unityEvent, Object target, string methodName)
    {
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) == target && unityEvent.GetPersistentMethodName(i) == methodName)
            {
                return;
            }
        }

        System.Reflection.MethodInfo method = target.GetType().GetMethod(methodName, System.Type.EmptyTypes);

        if (method == null)
        {
            Debug.LogWarning($"[LevelResultSetup] Could not find parameterless method '{methodName}' on {target.GetType().Name}.");
            return;
        }

        UnityAction action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, method);

        if (unityEvent is UnityEvent typedEvent)
        {
            UnityEventTools.AddPersistentListener(typedEvent, action);
        }
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
            Debug.LogWarning($"[LevelResultSetup] Property '{propertyName}' not found on {serializedObject.targetObject?.GetType().Name}.");
        }
    }

    /// <summary>Only wires the reference the first time (while it's still unset) — a later rename of the target child won't wipe out an already-working reference.</summary>
    private static void SetReferenceIfEmpty(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null && property.objectReferenceValue == null)
        {
            property.objectReferenceValue = value;
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
