using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the CutScene scene from scratch (Main Camera, Canvas, EventSystem, the full
/// slideshow hierarchy, and a wired CutSceneController) and registers it in Build
/// Settings. Safe to run more than once — every step finds-or-creates against whichever
/// scene is currently open if it's already named "CutScene", otherwise opens (or creates)
/// Assets/Scenes/CutScene.unity. Editor-only, lives under Assets/Editor.
/// </summary>
public static class CutSceneSetup
{
    private const string SceneName = "CutScene";
    private const string ScenePath = "Assets/Scenes/CutScene.unity";

    private const string CutSceneSpriteFolder = "Assets/UI/Images/CutScene";
    private const string VignetteSpritePath = "Assets/UI/Images/Material/HitFlashVignette.png";

    private const int CutSceneCount = 6;

    // The source art is 1817x866 (~2.10:1) — noticeably wider than 16:9 (1.78:1) — so the
    // fitter is driven by the sprites' own native aspect rather than a hardcoded 16/9,
    // otherwise Envelope Parent would stretch/crop them against the wrong shape.
    private const float CutSceneImageAspectRatio = 1817f / 866f;

    private const string UndoLabel = "Setup CutScene Scene";

    private static readonly string[] DefaultSubtitles =
    {
        "The world collapsed overnight.",
        "A mysterious plague turned humanity into the undead.",
        "Those who survived had only one choice: fight to stay alive.",
        "In the dead city, one soldier keeps hunting the undead.",
        "He must find a way to survive, destroy the undead, and uncover the truth.",
        "The fight for survival begins."
    };

    // Skip is a plain dim-white label with no background box (transparent, click-through
    // hit area only) — Next no longer exists as a button, tapping the screen advances instead.
    private static readonly Color SkipTextColor = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color SkipTextPressedColor = new Color(1f, 1f, 1f, 0.35f);

    [MenuItem("Tools/Zombie War/Create CutScene Scene")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[CutSceneSetup] Skipped — run this outside Play Mode.");
            return;
        }

        // Every CreateGameObject/AddComponent call below registers its own separate Undo
        // step. Without grouping them, a stray Ctrl+Z after running this tool only undoes
        // the last few objects created (e.g. ProgressRoot onward) instead of the whole
        // setup, leaving the scene in a half-built state. Collapsing into one group makes
        // a single Ctrl+Z undo the entire run atomically.
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        bool isNewScene = !File.Exists(ScenePath);
        Scene scene = OpenOrCreateScene();

        SetupMainCamera(scene);
        Transform canvasTransform = SetupCanvas(scene);
        SetupEventSystem(scene);

        SetupBackground(canvasTransform);
        RectTransform cutSceneRootRect = SetupCutSceneRoot(canvasTransform, out CanvasGroup cutSceneCanvasGroup);
        Image cutSceneImage = SetupCutSceneImage(cutSceneRootRect);
        SetupDarkVignette(cutSceneRootRect);
        TMP_Text subtitleText = SetupSubtitleText(cutSceneRootRect);

        RemoveProgressRootIfPresent(canvasTransform);
        GameObject inputBlocker = SetupInputBlocker(canvasTransform);
        RemoveNextButtonIfPresent(canvasTransform);
        Button skipButton = SetupSkipButton(canvasTransform);

        Sprite[] cutSceneSprites = LoadCutSceneSprites();

        if (cutSceneSprites.Length > 0 && cutSceneSprites[0] != null)
        {
            cutSceneImage.sprite = cutSceneSprites[0];
            EditorUtility.SetDirty(cutSceneImage);
        }

        CutSceneController controller = SetupControllerObject(
            scene,
            cutSceneSprites,
            cutSceneImage,
            cutSceneCanvasGroup,
            subtitleText,
            skipButton,
            inputBlocker);

        if (isNewScene)
        {
            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        else
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        AddSceneToBuildSettingsIfMissing();
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = controller.gameObject;

        string saveHint = isNewScene
            ? "Scene created and saved, and added to Build Settings."
            : "Save the scene (Ctrl+S) to keep these changes.";

        Debug.Log($"[CutSceneSetup] CutScene setup complete — CutSceneManager has all {cutSceneSprites.Length} sprite(s) and subtitles wired. {saveHint} Enter Play Mode to preview, or check Device Simulator (Samsung Galaxy S10e / 16:9 / 18:9 / 20:9).");
    }

    private static Scene OpenOrCreateScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();

        if (activeScene.name == SceneName)
        {
            return activeScene;
        }

        if (File.Exists(ScenePath))
        {
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static void SetupMainCamera(Scene scene)
    {
        Transform existing = FindInActiveScene(scene, "Main Camera");
        GameObject cameraObject;

        if (existing != null)
        {
            cameraObject = existing.gameObject;
        }
        else
        {
            cameraObject = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(cameraObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
        }

        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.GetComponent<Camera>();
        if (camera == null)
        {
            camera = Undo.AddComponent<Camera>(cameraObject);
        }

        // Pure UI slideshow — the camera exists only so Camera.main/AudioListener are
        // valid, it renders nothing itself (Screen Space Overlay draws independently of it).
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 0;
        camera.orthographic = true;
        EditorUtility.SetDirty(camera);

        if (cameraObject.GetComponent<AudioListener>() == null)
        {
            Undo.AddComponent<AudioListener>(cameraObject);
        }
    }

    private static Transform SetupCanvas(Scene scene)
    {
        Transform existing = FindInActiveScene(scene, "Canvas");
        GameObject canvasObject;

        if (existing != null)
        {
            canvasObject = existing.gameObject;
        }
        else
        {
            canvasObject = new GameObject("Canvas", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
        }

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = Undo.AddComponent<Canvas>(canvasObject);
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = Undo.AddComponent<CanvasScaler>(canvasObject);
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
        {
            Undo.AddComponent<GraphicRaycaster>(canvasObject);
        }

        EditorUtility.SetDirty(canvasObject);
        return canvasObject.transform;
    }

    private static void SetupEventSystem(Scene scene)
    {
        Transform existing = FindInActiveScene(scene, "EventSystem");
        GameObject eventSystemObject;

        if (existing != null)
        {
            eventSystemObject = existing.gameObject;
        }
        else
        {
            eventSystemObject = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
        }

        if (eventSystemObject.GetComponent<EventSystem>() == null)
        {
            Undo.AddComponent<EventSystem>(eventSystemObject);
        }

        if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
        {
            Undo.AddComponent<StandaloneInputModule>(eventSystemObject);
        }

        EditorUtility.SetDirty(eventSystemObject);
    }

    private static void SetupBackground(Transform canvasTransform)
    {
        RectTransform rect = GetOrCreateUIChild<Image>(canvasTransform, "Background", out Image image);
        StretchFull(rect);
        image.color = new Color(0.03f, 0.03f, 0.03f, 1f);
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
    }

    private static RectTransform SetupCutSceneRoot(Transform canvasTransform, out CanvasGroup canvasGroup)
    {
        RectTransform rect = GetOrCreateUIChild<RectTransform>(canvasTransform, "CutSceneRoot", out _);
        StretchFull(rect);

        canvasGroup = rect.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(rect.gameObject);
        }

        // Visible at edit time (like SettingPanel/LevelSelectPanel) so it can be tweaked by
        // hand in the Scene view — CutSceneController.FadeCanvasGroup always starts from an
        // explicit "from" value at runtime, so this has no effect on the actual fade-in.
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        EditorUtility.SetDirty(rect.gameObject);
        return rect;
    }

    private static Image SetupCutSceneImage(RectTransform parent)
    {
        RectTransform rect = GetOrCreateUIChild<Image>(parent, "CutSceneImage", out Image image);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1920f, 1080f);
        rect.localScale = Vector3.one;

        image.type = Image.Type.Simple;
        image.color = Color.white;
        image.raycastTarget = false;
        image.preserveAspect = false;
        EditorUtility.SetDirty(image);

        AspectRatioFitter fitter = rect.GetComponent<AspectRatioFitter>();
        if (fitter == null)
        {
            fitter = Undo.AddComponent<AspectRatioFitter>(rect.gameObject);
        }

        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = CutSceneImageAspectRatio;
        EditorUtility.SetDirty(fitter);

        return image;
    }

    private static void SetupDarkVignette(RectTransform parent)
    {
        RectTransform rect = GetOrCreateUIChild<Image>(parent, "DarkVignette", out Image image);
        StretchFull(rect);

        Sprite vignette = AssetDatabase.LoadAssetAtPath<Sprite>(VignetteSpritePath);

        if (vignette != null)
        {
            image.sprite = vignette;
            image.type = Image.Type.Simple;
            image.color = new Color(0f, 0f, 0f, 0.55f);
        }
        else
        {
            Debug.LogWarning($"[CutSceneSetup] Vignette sprite not found at '{VignetteSpritePath}' (run Tools > Zombie War > Setup Hit Flash Overlay once to generate it) — DarkVignette stays fully transparent for now.");
            image.color = new Color(0f, 0f, 0f, 0f);
        }

        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
    }

    // A GameObject can only host one Graphic component, so the background (Image) and the
    // label (TextMeshProUGUI) can't live on the same object — "SubtitleText" is the
    // background bar, "Text (TMP)" is its child. CutSceneController hides/shows the
    // parent (via subtitleText.transform.parent) so both move together.
    private static TMP_Text SetupSubtitleText(RectTransform parent)
    {
        RectTransform bgRect = GetOrCreateUIChild<Image>(parent, "SubtitleText", out Image bgImage);
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0f);
        bgRect.sizeDelta = new Vector2(1700f, 160f);
        bgRect.anchoredPosition = new Vector2(0f, 55f);

        // No background panel — the bar is purely a hidden hit/layout container now, the
        // text itself is the only visible thing.
        bgImage.sprite = null;
        bgImage.color = Color.clear;
        bgImage.raycastTarget = false;
        EditorUtility.SetDirty(bgImage);

        RectTransform textRect = GetOrCreateUIChild<TextMeshProUGUI>(bgRect, "Text (TMP)", out TextMeshProUGUI text);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(50f, 15f);
        textRect.offsetMax = new Vector2(-50f, -15f);

        text.text = string.Empty;
        text.fontSize = 46f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 0.95f, 0.8f, 1f);
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);

        return text;
    }

    private static void RemoveProgressRootIfPresent(Transform canvasTransform)
    {
        Transform existing = FindImmediateChild(canvasTransform, "ProgressRoot");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }
    }

    private static GameObject SetupInputBlocker(Transform canvasTransform)
    {
        RectTransform rect = GetOrCreateUIChild<Image>(canvasTransform, "InputBlocker", out Image image);
        StretchFull(rect);
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
        EditorUtility.SetDirty(image);

        Button button = rect.GetComponent<Button>();
        if (button == null)
        {
            button = Undo.AddComponent<Button>(rect.gameObject);
        }

        button.transition = Selectable.Transition.None;
        button.targetGraphic = image;
        EditorUtility.SetDirty(button);

        return rect.gameObject;
    }

    // NextButton is removed entirely — tapping the screen (InputBlocker) is the only way
    // to advance now, no visible Next control.
    private static void RemoveNextButtonIfPresent(Transform canvasTransform)
    {
        Transform existing = FindImmediateChild(canvasTransform, "NextButton");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }
    }

    // Plain dim-white "SKIP" label, no background box — only the text itself is visible,
    // the Image stays fully transparent but keeps raycastTarget on so the label's rect is
    // still the clickable hit area.
    private static Button SetupSkipButton(Transform canvasTransform)
    {
        RectTransform rect = GetOrCreateUIChild<Image>(canvasTransform, "SkipButton", out Image image);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(200f, 90f);
        rect.anchoredPosition = new Vector2(-90f, -90f);

        image.sprite = null;
        image.color = Color.clear;
        image.raycastTarget = true;
        EditorUtility.SetDirty(image);

        RectTransform textRect = GetOrCreateUIChild<TextMeshProUGUI>(rect, "Text (TMP)", out TextMeshProUGUI text);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        text.text = "SKIP";
        text.fontSize = 32f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = SkipTextColor;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);

        Button button = rect.GetComponent<Button>();
        if (button == null)
        {
            button = Undo.AddComponent<Button>(rect.gameObject);
        }

        // targetGraphic is the TMP label itself (not the invisible background Image) so the
        // ColorTint transition dims the visible "SKIP" text on press instead of tinting
        // something transparent that can't be seen.
        button.targetGraphic = text;

        ColorBlock colors = button.colors;
        colors.normalColor = SkipTextColor;
        colors.highlightedColor = SkipTextColor;
        colors.pressedColor = SkipTextPressedColor;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.25f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        EditorUtility.SetDirty(button);

        return button;
    }

    private static Sprite[] LoadCutSceneSprites()
    {
        Sprite[] sprites = new Sprite[CutSceneCount];

        for (int i = 0; i < CutSceneCount; i++)
        {
            string path = $"{CutSceneSpriteFolder}/CutScene{i + 1}.png";
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprites[i] == null)
            {
                Debug.LogWarning($"[CutSceneSetup] Could not load sprite at '{path}' — CutSceneController's cutSceneSprites[{i}] will be empty until this is fixed.");
            }
        }

        return sprites;
    }

    private static CutSceneController SetupControllerObject(
        Scene scene,
        Sprite[] cutSceneSprites,
        Image cutSceneImage,
        CanvasGroup cutSceneCanvasGroup,
        TMP_Text subtitleText,
        Button skipButton,
        GameObject inputBlocker)
    {
        Transform managerTransform = FindInActiveScene(scene, "CutSceneManager");
        GameObject managerObject;

        if (managerTransform != null)
        {
            managerObject = managerTransform.gameObject;
        }
        else
        {
            managerObject = new GameObject("CutSceneManager");
            Undo.RegisterCreatedObjectUndo(managerObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(managerObject, scene);
        }

        CutSceneController controller = managerObject.GetComponent<CutSceneController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<CutSceneController>(managerObject);
        }

        AudioSource audioSource = managerObject.GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = Undo.AddComponent<AudioSource>(managerObject);
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        EditorUtility.SetDirty(audioSource);

        SerializedObject serializedController = new SerializedObject(controller);

        SerializedProperty spritesProperty = serializedController.FindProperty("cutSceneSprites");
        spritesProperty.arraySize = cutSceneSprites.Length;

        for (int i = 0; i < cutSceneSprites.Length; i++)
        {
            spritesProperty.GetArrayElementAtIndex(i).objectReferenceValue = cutSceneSprites[i];
        }

        SerializedProperty subtitlesProperty = serializedController.FindProperty("subtitles");
        subtitlesProperty.arraySize = DefaultSubtitles.Length;

        for (int i = 0; i < DefaultSubtitles.Length; i++)
        {
            subtitlesProperty.GetArrayElementAtIndex(i).stringValue = DefaultSubtitles[i];
        }

        // Progress dots are removed from this scene by design (see RemoveProgressRootIfPresent) —
        // clear the array so no dangling references to destroyed dots remain.
        SerializedProperty dotsProperty = serializedController.FindProperty("progressDots");
        dotsProperty.arraySize = 0;

        SerializedProperty showSubtitlesProperty = serializedController.FindProperty("showSubtitles");
        showSubtitlesProperty.boolValue = true;

        // Off by default — image stays still, no zoom/pan.
        SerializedProperty kenBurnsProperty = serializedController.FindProperty("enableKenBurnsEffect");
        kenBurnsProperty.boolValue = false;

        // NextButton was removed from the scene entirely — tap-anywhere (InputBlocker) is
        // the only way to advance now, so this reference is explicitly cleared rather than
        // left pointing at a destroyed object.
        SerializedProperty nextButtonProperty = serializedController.FindProperty("nextButton");
        if (nextButtonProperty != null)
        {
            nextButtonProperty.objectReferenceValue = null;
        }

        // CutScene -> LoadingScene -> Level1 (matching MainMenu -> LoadingScene -> CutScene
        // on the way in), so both hops look like the same "loading data" screen.
        SerializedProperty useLoadingSceneProperty = serializedController.FindProperty("useLoadingScene");
        useLoadingSceneProperty.boolValue = true;

        SetReference(serializedController, "cutSceneImage", cutSceneImage);
        SetReference(serializedController, "cutSceneCanvasGroup", cutSceneCanvasGroup);
        SetReference(serializedController, "subtitleText", subtitleText);
        SetReference(serializedController, "skipButton", skipButton);
        SetReference(serializedController, "inputBlocker", inputBlocker);
        SetReference(serializedController, "audioSource", audioSource);

        serializedController.ApplyModifiedProperties();

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

    private static void AddSceneToBuildSettingsIfMissing()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene existing in scenes)
        {
            if (existing.path == ScenePath)
            {
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
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

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
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
