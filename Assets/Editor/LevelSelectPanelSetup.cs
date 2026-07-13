using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click LevelSelectPanel setup for MainMenu: wraps the existing PlayBtn/SettingBtn
/// in a new MainButtonsRoot (CanvasGroup added, so they can be dimmed/locked while a
/// panel is open), builds LevelSelectPanel (DarkOverlay, PanelBackground, TitleText,
/// an 8-button LevelGrid, BackButton), and wires a MainMenuUIController with every
/// reference filled in. Also fixes MainMenu's CanvasScaler to Scale With Screen Size.
/// Safe to run more than once — every step finds-or-creates, never touches SettingPanel's
/// own contents. Editor-only, lives under Assets/Editor.
/// </summary>
public static class LevelSelectPanelSetup
{
    private const string CanvasName = "Canvas";
    private const string PlayBtnName = "PlayBtn";
    private const string SettingBtnName = "SettingBtn";
    private const string SettingPanelName = "SettingPanel";
    private const string UndoLabel = "Setup Level Select Panel";

    private const string PanelSpritePath = "Assets/UI/Images/Material/RoundedPanel.png";
    private const int PanelSpriteTextureSize = 128;
    private const int PanelSpriteCornerRadius = 22;
    private const int PanelSpriteBorder = 30;

    private const string LevelButtonSpritePath = "Assets/UI/Images/Btn/LevelBtn.png";
    private const string LevelLockSpritePath = "Assets/UI/Images/Btn/LevelLockBtn.png";

    // No longer rendered (PanelBackground removed) — kept purely as layout metrics so
    // TitleText/LevelGrid/BackButton positions still have a sensible reference size.
    private const float PanelWidth = 1500f;
    private const float PanelHeight = 860f;

    private const float GridCellWidth = 300f;
    private const float GridCellHeight = 210f;
    private const float GridSpacingX = 40f;
    private const float GridSpacingY = 40f;
    private const float GridPaddingLeft = 40f;
    private const float GridPaddingRight = 40f;
    private const float GridPaddingTop = 30f;
    private const float GridPaddingBottom = 30f;
    private const int GridColumns = 4;
    private const int LevelCount = 8;

    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.72f);
    // Level buttons now use real art (LevelBtn.png) instead of the generated rounded
    // rect, so Normal stays untinted white (shows the art as-is) and only Highlighted/
    // Pressed/Disabled apply a color multiply on top of it.
    private static readonly Color ButtonNormalColor = Color.white;
    private static readonly Color ButtonHighlightedColor = new Color(1f, 0.85f, 0.55f, 1f);
    private static readonly Color ButtonPressedColor = new Color(0.75f, 0.55f, 0.4f, 1f);
    private static readonly Color ButtonDisabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Color BackButtonNormalColor = new Color(0.6f, 0.15f, 0.1f, 1f);
    private static readonly Color BackButtonHighlightedColor = new Color(0.85f, 0.3f, 0.15f, 1f);
    private static readonly Color BackButtonPressedColor = new Color(0.4f, 0.08f, 0.05f, 1f);

    [MenuItem("Tools/Zombie War/Create Level Select Panel")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[LevelSelectPanelSetup] Skipped — run this outside Play Mode, otherwise the setup won't be saved.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        Transform canvasTransform = FindInActiveScene(activeScene, CanvasName);
        if (canvasTransform == null)
        {
            Debug.LogError($"[LevelSelectPanelSetup] Could not find '{CanvasName}' in the active scene. Aborted — make sure MainMenu.unity is the open scene.");
            return;
        }

        Transform playBtnTransform = FindInActiveScene(activeScene, PlayBtnName);
        Transform settingBtnTransform = FindInActiveScene(activeScene, SettingBtnName);
        Transform settingPanelTransform = FindInActiveScene(activeScene, SettingPanelName);

        if (playBtnTransform == null || settingBtnTransform == null)
        {
            Debug.LogError($"[LevelSelectPanelSetup] Could not find '{PlayBtnName}' and/or '{SettingBtnName}'. Aborted.");
            return;
        }

        ConfigureCanvasScaler(canvasTransform);

        RectTransform mainButtonsRect = SetupMainButtonsRoot(canvasTransform, playBtnTransform, settingBtnTransform, out CanvasGroup mainButtonsCanvasGroup);

        RectTransform levelSelectRect = SetupLevelSelectPanel(canvasTransform, out CanvasGroup levelSelectCanvasGroup);
        SetupDarkOverlay(levelSelectRect);
        RemovePanelBackgroundIfPresent(levelSelectRect);
        SetupTitleText(levelSelectRect);
        RectTransform gridRect = SetupLevelGrid(levelSelectRect);
        Button[] levelButtons = SetupLevelButtons(gridRect);
        Button backButton = SetupBackButton(levelSelectRect);

        SettingPanelToggle settingPanelToggle = settingPanelTransform != null
            ? FindOrWarnSettingPanelToggle(settingBtnTransform)
            : null;

        MainMenuUIController controller = SetupControllerObject(
            activeScene,
            mainButtonsRect,
            mainButtonsCanvasGroup,
            playBtnTransform,
            settingBtnTransform,
            levelSelectRect,
            levelSelectCanvasGroup,
            levelButtons,
            backButton,
            settingPanelToggle);

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = controller.gameObject;

        Debug.Log("[LevelSelectPanelSetup] LevelSelectPanel created/updated. Now run Tools > Zombie War > Setup Button Click SFX and Setup Button Click Animation to cover the new Level/Back buttons too. Test in Play Mode, then save the scene (Ctrl+S).");
    }

    private static void ConfigureCanvasScaler(Transform canvasTransform)
    {
        CanvasScaler scaler = canvasTransform.GetComponent<CanvasScaler>();

        if (scaler == null)
        {
            return;
        }

        Undo.RecordObject(scaler, UndoLabel);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        EditorUtility.SetDirty(scaler);
    }

    /// <summary>
    /// Wraps the existing PlayBtn/SettingBtn in a new MainButtonsRoot without changing
    /// their own components/wiring — reparenting with worldPositionStays keeps their
    /// visual position identical.
    /// </summary>
    private static RectTransform SetupMainButtonsRoot(Transform canvasTransform, Transform playBtnTransform, Transform settingBtnTransform, out CanvasGroup canvasGroup)
    {
        Transform existingRoot = FindImmediateChild(canvasTransform, "MainButtonsRoot");
        GameObject rootObject;

        if (existingRoot != null)
        {
            rootObject = existingRoot.gameObject;
        }
        else
        {
            rootObject = new GameObject("MainButtonsRoot", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(rootObject, UndoLabel);
            rootObject.transform.SetParent(canvasTransform, false);

            // Keep it at the same sibling position PlayBtn used to occupy, so nothing
            // that rendered behind/above PlayBtn before changes draw order unexpectedly.
            rootObject.transform.SetSiblingIndex(playBtnTransform.GetSiblingIndex());
        }

        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        if (playBtnTransform.parent != rootRect)
        {
            playBtnTransform.SetParent(rootRect, true);
        }

        if (settingBtnTransform.parent != rootRect)
        {
            settingBtnTransform.SetParent(rootRect, true);
        }

        canvasGroup = rootObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(rootObject);
        }

        EditorUtility.SetDirty(rootObject);
        return rootRect;
    }

    private static RectTransform SetupLevelSelectPanel(Transform canvasTransform, out CanvasGroup canvasGroup)
    {
        RectTransform rect = GetOrCreateUIChild<RectTransform>(canvasTransform, "LevelSelectPanel", out _);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // Reset to a clean resting state every run: MainMenuUIController's own
        // Awake() is what hides the panel at Play time (scale/alpha/position), so at
        // edit time it should stay fully visible/selectable for manual tweaking — same
        // convention as SettingPanel. Also clears out any scale/position a Play Mode
        // animation left behind if Play was stopped mid-tween.
        rect.localScale = Vector3.one;
        rect.anchoredPosition = Vector2.zero;

        // Last sibling so it draws above BG, MainButtonsRoot, TitleImage and SettingPanel.
        rect.SetAsLastSibling();

        canvasGroup = rect.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(rect.gameObject);
        }

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        EditorUtility.SetDirty(rect.gameObject);
        return rect;
    }

    private static void SetupDarkOverlay(RectTransform parent)
    {
        RectTransform rect = GetOrCreateUIChild<Image>(parent, "DarkOverlay", out Image image);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();

        image.sprite = null;
        image.color = OverlayColor;
        image.raycastTarget = true;

        EditorUtility.SetDirty(image);
    }

    /// <summary>Removes a "PanelBackground" left over from a previous run — DarkOverlay
    /// alone now dims the background behind Title/Grid/Back.</summary>
    private static void RemovePanelBackgroundIfPresent(RectTransform parent)
    {
        Transform existing = FindImmediateChild(parent, "PanelBackground");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }
    }

    private static void SetupTitleText(RectTransform parent)
    {
        RectTransform rect = GetOrCreateUIChild<TextMeshProUGUI>(parent, "TitleText", out TextMeshProUGUI text);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900f, 90f);
        rect.anchoredPosition = new Vector2(0f, PanelHeight / 2f - 80f);

        text.text = "SELECT LEVEL";
        text.fontSize = 56f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.95f, 0.85f, 0.5f, 1f);
        text.raycastTarget = false;

        EditorUtility.SetDirty(text);
    }

    private static RectTransform SetupLevelGrid(RectTransform parent)
    {
        float gridWidth = GridPaddingLeft + GridPaddingRight + GridCellWidth * GridColumns + GridSpacingX * (GridColumns - 1);
        int rows = Mathf.CeilToInt(LevelCount / (float)GridColumns);
        float gridHeight = GridPaddingTop + GridPaddingBottom + GridCellHeight * rows + GridSpacingY * (rows - 1);

        RectTransform rect = GetOrCreateUIChild<GridLayoutGroup>(parent, "LevelGrid", out GridLayoutGroup grid);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(gridWidth, gridHeight);
        rect.anchoredPosition = new Vector2(0f, -20f);

        grid.cellSize = new Vector2(GridCellWidth, GridCellHeight);
        grid.spacing = new Vector2(GridSpacingX, GridSpacingY);
        grid.padding = new RectOffset((int)GridPaddingLeft, (int)GridPaddingRight, (int)GridPaddingTop, (int)GridPaddingBottom);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = GridColumns;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperCenter;

        EditorUtility.SetDirty(grid);
        return rect;
    }

    private static Button[] SetupLevelButtons(RectTransform gridRect)
    {
        Sprite levelButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LevelButtonSpritePath);

        if (levelButtonSprite == null)
        {
            Debug.LogWarning($"[LevelSelectPanelSetup] Could not load '{LevelButtonSpritePath}' — falling back to the generated rounded rect for Level buttons.");
            levelButtonSprite = GetOrCreateRoundedPanelSprite();
        }

        Button[] buttons = new Button[LevelCount];

        for (int i = 0; i < LevelCount; i++)
        {
            int levelNumber = i + 1;
            string buttonName = $"Level{levelNumber}Button";

            RectTransform buttonRect = GetOrCreateUIChild<Image>(gridRect, buttonName, out Image buttonImage);
            buttonImage.sprite = levelButtonSprite;
            buttonImage.type = Image.Type.Simple;
            buttonImage.color = ButtonNormalColor;
            buttonImage.raycastTarget = true;

            Button button = buttonRect.GetComponent<Button>();
            if (button == null)
            {
                button = Undo.AddComponent<Button>(buttonRect.gameObject);
            }

            button.targetGraphic = buttonImage;
            button.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = button.colors;
            colors.normalColor = ButtonNormalColor;
            colors.highlightedColor = ButtonHighlightedColor;
            colors.pressedColor = ButtonPressedColor;
            colors.disabledColor = ButtonDisabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            RectTransform labelRect = GetOrCreateUIChild<TextMeshProUGUI>(buttonRect, "Text (TMP)", out TextMeshProUGUI label);
            StretchWithMargin(labelRect, 10f);
            label.text = $"LEVEL {levelNumber}";
            label.fontSize = 30f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            EditorUtility.SetDirty(buttonImage);
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(label);

            buttons[i] = button;
        }

        return buttons;
    }

    private static Button SetupBackButton(RectTransform parent)
    {
        Sprite panelSprite = GetOrCreateRoundedPanelSprite();

        RectTransform rect = GetOrCreateUIChild<Image>(parent, "BackButton", out Image image);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(260f, 80f);
        rect.anchoredPosition = new Vector2(0f, -(PanelHeight / 2f - 70f));

        image.sprite = panelSprite;
        image.type = Image.Type.Sliced;
        image.color = BackButtonNormalColor;
        image.raycastTarget = true;

        Button button = rect.GetComponent<Button>();
        if (button == null)
        {
            button = Undo.AddComponent<Button>(rect.gameObject);
        }

        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = BackButtonNormalColor;
        colors.highlightedColor = BackButtonHighlightedColor;
        colors.pressedColor = BackButtonPressedColor;
        colors.disabledColor = ButtonDisabledColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        RectTransform labelRect = GetOrCreateUIChild<TextMeshProUGUI>(rect, "Text (TMP)", out TextMeshProUGUI label);
        StretchWithMargin(labelRect, 8f);
        label.text = "BACK";
        label.fontSize = 32f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;

        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(label);

        return button;
    }

    private static SettingPanelToggle FindOrWarnSettingPanelToggle(Transform settingBtnTransform)
    {
        SettingPanelToggle toggle = settingBtnTransform.GetComponent<SettingPanelToggle>();

        if (toggle == null)
        {
            Debug.LogWarning("[LevelSelectPanelSetup] SettingBtn has no SettingPanelToggle component — run Tools > Zombie War > Setup Setting Panel Toggle first for SettingPanel/LevelSelectPanel mutual exclusion to work.");
        }

        return toggle;
    }

    private static MainMenuUIController SetupControllerObject(
        Scene scene,
        RectTransform mainButtonsRect,
        CanvasGroup mainButtonsCanvasGroup,
        Transform playBtnTransform,
        Transform settingBtnTransform,
        RectTransform levelSelectRect,
        CanvasGroup levelSelectCanvasGroup,
        Button[] levelButtons,
        Button backButton,
        SettingPanelToggle settingPanelToggle)
    {
        Transform managerTransform = FindInActiveScene(scene, "MainMenuManager");
        GameObject managerObject;

        if (managerTransform != null)
        {
            managerObject = managerTransform.gameObject;
        }
        else
        {
            managerObject = new GameObject("MainMenuManager");
            Undo.RegisterCreatedObjectUndo(managerObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(managerObject, scene);
        }

        MainMenuUIController controller = managerObject.GetComponent<MainMenuUIController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<MainMenuUIController>(managerObject);
        }

        SerializedObject serializedController = new SerializedObject(controller);

        SetReference(serializedController, "mainButtonsRoot", mainButtonsRect.gameObject);
        SetReference(serializedController, "mainButtonsCanvasGroup", mainButtonsCanvasGroup);
        SetReference(serializedController, "playButton", playBtnTransform.GetComponent<Button>());
        SetReference(serializedController, "settingButton", settingBtnTransform.GetComponent<Button>());

        SetReference(serializedController, "levelSelectPanel", levelSelectRect.gameObject);
        SetReference(serializedController, "levelSelectRect", levelSelectRect);
        SetReference(serializedController, "levelSelectCanvasGroup", levelSelectCanvasGroup);
        SetReference(serializedController, "backButton", backButton);
        SetReference(serializedController, "settingPanelToggle", settingPanelToggle);

        Sprite lockedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LevelLockSpritePath);
        if (lockedSprite != null)
        {
            SetReference(serializedController, "lockedIcon", lockedSprite);
        }
        else
        {
            Debug.LogWarning($"[LevelSelectPanelSetup] Could not load '{LevelLockSpritePath}' — locked level buttons won't have a padlock sprite until this is assigned manually.");
        }

        SerializedProperty levelButtonsProperty = serializedController.FindProperty("levelButtons");
        levelButtonsProperty.arraySize = levelButtons.Length;
        for (int i = 0; i < levelButtons.Length; i++)
        {
            levelButtonsProperty.GetArrayElementAtIndex(i).objectReferenceValue = levelButtons[i];
        }

        serializedController.ApplyModifiedProperties();

        EditorUtility.SetDirty(managerObject);
        return controller;
    }

    private static void SetReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }

    /// <summary>
    /// Generates (once) a white, 9-sliceable rounded-rectangle sprite used for
    /// PanelBackground and every Level/Back button — no matching "rusted metal panel"
    /// art exists in the project, so this gives a clean rounded shape (tinted via each
    /// Image's own color) instead of a flat hard-cornered rectangle.
    /// </summary>
    private static Sprite GetOrCreateRoundedPanelSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);

        if (existing != null)
        {
            return existing;
        }

        Texture2D texture = new Texture2D(PanelSpriteTextureSize, PanelSpriteTextureSize, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[PanelSpriteTextureSize * PanelSpriteTextureSize];
        float radius = PanelSpriteCornerRadius;

        for (int y = 0; y < PanelSpriteTextureSize; y++)
        {
            for (int x = 0; x < PanelSpriteTextureSize; x++)
            {
                float alpha = 1f;

                float cornerX = 0f;
                float cornerY = 0f;
                bool nearCorner = false;

                if (x < radius && y < radius) { cornerX = radius; cornerY = radius; nearCorner = true; }
                else if (x > PanelSpriteTextureSize - radius && y < radius) { cornerX = PanelSpriteTextureSize - radius; cornerY = radius; nearCorner = true; }
                else if (x < radius && y > PanelSpriteTextureSize - radius) { cornerX = radius; cornerY = PanelSpriteTextureSize - radius; nearCorner = true; }
                else if (x > PanelSpriteTextureSize - radius && y > PanelSpriteTextureSize - radius) { cornerX = PanelSpriteTextureSize - radius; cornerY = PanelSpriteTextureSize - radius; nearCorner = true; }

                if (nearCorner)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cornerX, cornerY));
                    alpha = Mathf.Clamp01(radius - distance);
                }

                pixels[y * PanelSpriteTextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        byte[] pngData = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        string folder = System.IO.Path.GetDirectoryName(PanelSpritePath)?.Replace('\\', '/');

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/'), System.IO.Path.GetFileName(folder));
        }

        System.IO.File.WriteAllBytes(PanelSpritePath, pngData);
        AssetDatabase.ImportAsset(PanelSpritePath);

        TextureImporter importer = AssetImporter.GetAtPath(PanelSpritePath) as TextureImporter;

        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(PanelSpriteBorder, PanelSpriteBorder, PanelSpriteBorder, PanelSpriteBorder);
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
    }

    private static RectTransform GetOrCreateUIChild<T>(Transform parent, string name, out T component) where T : Component
    {
        Transform existing = FindImmediateChild(parent, name);
        GameObject childObject;

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

        component = childObject.GetComponent<T>();

        if (component == null)
        {
            component = typeof(T) == typeof(RectTransform)
                ? childObject.GetComponent<RectTransform>() as T
                : Undo.AddComponent<T>(childObject);
        }

        return childObject.GetComponent<RectTransform>();
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

    private static void StretchWithMargin(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
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
