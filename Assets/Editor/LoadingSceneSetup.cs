using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click LoadingScene setup: adds a fill/left-point/right-point to the existing
/// LoadingBar (without touching its own Image/sprite), builds a small offscreen
/// camera + RenderTexture that previews an instantiated (AI-disabled) Zombie prefab,
/// wires it up behind a RawImage that LoadingSceneController slides across the bar,
/// and adds a LoadingSceneController component with everything wired. Safe to run
/// more than once — every step finds-or-creates. Editor-only, lives under Assets/Editor.
/// </summary>
public static class LoadingSceneSetup
{
    private const string CanvasName = "Canvas";
    private const string LoadingBarName = "LoadingBar";
    private const string LoadingTextName = "LoadingText";
    private const string ZombiePrefabPath = "Assets/Prefabs/Zombie/Zombie.prefab";
    private const string LoadingZombieLayerName = "LoadingZombie";

    private const string RenderTextureFolder = "Assets/Resources/UI/Loading";
    private const string RenderTexturePath = RenderTextureFolder + "/LoadingZombieRT.renderTexture";
    private const int RenderTextureSize = 256;

    private const float FillPaddingX = 30f;
    private const float FillPaddingY = 40f;
    private const float ZombieIconSize = 110f;

    private const string UndoLabel = "Setup Loading Scene";

    [MenuItem("Tools/Zombie War/Setup Loading Scene")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[LoadingSceneSetup] Skipped — run this outside Play Mode, otherwise the setup won't be saved.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        Transform canvasTransform = FindInActiveScene(activeScene, CanvasName);
        if (canvasTransform == null)
        {
            Debug.LogError($"[LoadingSceneSetup] Could not find a GameObject named '{CanvasName}' in the active scene. Aborted — make sure LoadingScene.unity is the open scene.");
            return;
        }

        Transform loadingBarTransform = FindInActiveScene(activeScene, LoadingBarName);
        if (loadingBarTransform == null)
        {
            Debug.LogError($"[LoadingSceneSetup] Could not find '{LoadingBarName}' under Canvas. Aborted.");
            return;
        }

        Transform loadingTextTransform = FindInActiveScene(activeScene, LoadingTextName);
        TMP_Text loadingText = loadingTextTransform != null ? loadingTextTransform.GetComponent<TMP_Text>() : null;

        ConfigureCanvasScaler(canvasTransform);

        // LoadingBar may either be the original plain Image, or a proper Slider (Background /
        // Fill Area / Fill) if you've since rebuilt it — handle both without assuming either.
        Slider loadingBarSlider = loadingBarTransform.GetComponent<Slider>();
        Image fillImage = null;
        RectTransform fillContainerRect;
        float edgePadding;

        if (loadingBarSlider != null)
        {
            ConfigureSlider(loadingBarSlider);

            fillContainerRect = (loadingBarSlider.fillRect != null && loadingBarSlider.fillRect.parent is RectTransform fillAreaRect)
                ? fillAreaRect
                : loadingBarTransform as RectTransform;

            edgePadding = 0f;
        }
        else
        {
            RectTransform fillRect = GetOrCreateUIChild<Image>(loadingBarTransform, "LoadingBarFill", out fillImage);
            ConfigureFillRect(fillRect);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;
            fillImage.color = new Color(0.75f, 0.1f, 0.05f, 0.9f);
            fillImage.raycastTarget = false;
            EditorUtility.SetDirty(fillImage);

            fillContainerRect = loadingBarTransform as RectTransform;
            edgePadding = FillPaddingX;
        }

        RectTransform leftPointRect = GetOrCreateUIChild<RectTransform>(fillContainerRect, "LoadingBarLeftPoint", out _);
        ConfigureEdgePoint(leftPointRect, atRightEdge: false, padding: edgePadding);

        RectTransform rightPointRect = GetOrCreateUIChild<RectTransform>(fillContainerRect, "LoadingBarRightPoint", out _);
        ConfigureEdgePoint(rightPointRect, atRightEdge: true, padding: edgePadding);

        RenderTexture renderTexture = GetOrCreateRenderTexture();

        RectTransform zombieRawImageRect = GetOrCreateUIChild<RawImage>(fillContainerRect, "ZombieRunner", out RawImage zombieRawImage);
        zombieRawImageRect.anchorMin = new Vector2(0f, 0.5f);
        zombieRawImageRect.anchorMax = new Vector2(0f, 0.5f);
        zombieRawImageRect.pivot = new Vector2(0.5f, 0f);
        zombieRawImageRect.sizeDelta = new Vector2(ZombieIconSize, ZombieIconSize);
        zombieRawImageRect.anchoredPosition = Vector2.zero;
        zombieRawImage.texture = renderTexture;
        zombieRawImage.raycastTarget = false;
        EditorUtility.SetDirty(zombieRawImage);

        Animator zombieAnimator = SetupZombiePreviewStage(activeScene, renderTexture);

        LoadingSceneController controller = SetupControllerObject(
            activeScene,
            fillImage,
            loadingBarSlider,
            loadingText,
            zombieRawImageRect,
            leftPointRect,
            rightPointRect,
            zombieAnimator);

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = controller.gameObject;

        Debug.Log("[LoadingSceneSetup] LoadingScene setup complete. Select LoadingManager to review LoadingSceneController fields (defaultSceneName in particular), enter Play Mode to preview, then save the scene (Ctrl+S).");
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

    private static void ConfigureFillRect(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(FillPaddingX, FillPaddingY);
        rect.offsetMax = new Vector2(-FillPaddingX, -FillPaddingY);
    }

    private static void ConfigureSlider(Slider slider)
    {
        Undo.RecordObject(slider, UndoLabel);
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 0f;
        EditorUtility.SetDirty(slider);
    }

    private static void ConfigureEdgePoint(RectTransform rect, bool atRightEdge, float padding)
    {
        float anchorX = atRightEdge ? 1f : 0f;
        float xOffset = atRightEdge ? -padding : padding;

        rect.anchorMin = new Vector2(anchorX, 0.5f);
        rect.anchorMax = new Vector2(anchorX, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(xOffset, 0f);
        rect.sizeDelta = Vector2.zero;
    }

    private static RenderTexture GetOrCreateRenderTexture()
    {
        RenderTexture existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);

        if (existing != null)
        {
            return existing;
        }

        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/UI");
        EnsureFolder(RenderTextureFolder);

        RenderTexture renderTexture = new RenderTexture(RenderTextureSize, RenderTextureSize, 16, RenderTextureFormat.ARGB32)
        {
            name = "LoadingZombieRT",
            antiAliasing = 1,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        AssetDatabase.CreateAsset(renderTexture, RenderTexturePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
    }

    /// <summary>
    /// A world-space rig living outside the Canvas: a small orthographic camera on its
    /// own layer rendering just one instantiated (AI/NavMesh/collider disabled) Zombie
    /// into the RenderTexture the ZombieRunner RawImage displays. No shadows, no HDR/
    /// MSAA, no post-processing — this is purely a decorative preview, not gameplay.
    /// </summary>
    private static Animator SetupZombiePreviewStage(Scene scene, RenderTexture renderTexture)
    {
        int loadingZombieLayer = LayerMask.NameToLayer(LoadingZombieLayerName);

        if (loadingZombieLayer < 0)
        {
            Debug.LogWarning($"[LoadingSceneSetup] Layer '{LoadingZombieLayerName}' not found — did TagManager.asset get updated? Falling back to Default layer (the preview camera will render everything on Default, not ideal but non-fatal).");
            loadingZombieLayer = 0;
        }

        Transform stageTransform = FindInActiveScene(scene, "ZombiePreviewStage");
        GameObject stageObject;

        if (stageTransform != null)
        {
            stageObject = stageTransform.gameObject;
        }
        else
        {
            stageObject = new GameObject("ZombiePreviewStage");
            Undo.RegisterCreatedObjectUndo(stageObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(stageObject, scene);
        }

        Transform cameraTransform = stageTransform != null ? stageTransform.Find("LoadingZombieCamera") : null;
        GameObject cameraObject;
        Camera previewCamera;

        if (cameraTransform != null)
        {
            cameraObject = cameraTransform.gameObject;
            previewCamera = cameraObject.GetComponent<Camera>();
        }
        else
        {
            cameraObject = new GameObject("LoadingZombieCamera");
            Undo.RegisterCreatedObjectUndo(cameraObject, UndoLabel);
            cameraObject.transform.SetParent(stageObject.transform, false);
            previewCamera = cameraObject.AddComponent<Camera>();
        }

        // Framed generously (tall + a fair margin, no tilt) so a roughly human-sized
        // character is fully in frame regardless of this particular model's exact
        // height/pivot — a previous, tighter framing (size 1.1, pitched 5°) cropped
        // the top of the zombie. Nudge orthographicSize/position further in the
        // Inspector if this specific prefab still isn't centered well.
        cameraObject.transform.position = new Vector3(0f, 1f, -4f);
        cameraObject.transform.rotation = Quaternion.identity;

        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.cullingMask = 1 << loadingZombieLayer;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = 1.8f;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 10f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = false;
        previewCamera.useOcclusionCulling = false;
        previewCamera.targetTexture = renderTexture;
        EditorUtility.SetDirty(previewCamera);

        Transform zombieTransform = stageTransform != null ? stageTransform.Find("ZombiePreview") : null;
        GameObject zombieObject;

        if (zombieTransform != null)
        {
            zombieObject = zombieTransform.gameObject;
        }
        else
        {
            GameObject zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePrefabPath);

            if (zombiePrefab == null)
            {
                Debug.LogError($"[LoadingSceneSetup] Could not load prefab at '{ZombiePrefabPath}' — the loading zombie preview will be empty. Fix the path and re-run.");
                return null;
            }

            zombieObject = (GameObject)PrefabUtility.InstantiatePrefab(zombiePrefab, scene);
            Undo.RegisterCreatedObjectUndo(zombieObject, UndoLabel);
            zombieObject.name = "ZombiePreview";
            zombieObject.transform.SetParent(stageObject.transform, false);
            zombieObject.transform.localPosition = Vector3.zero;
            zombieObject.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);
        }

        SetLayerRecursively(zombieObject, loadingZombieLayer);
        DisableGameplayComponents(zombieObject);
        DisableShadowsRecursively(zombieObject);

        Animator animator = zombieObject.GetComponent<Animator>();

        EditorUtility.SetDirty(stageObject);
        return animator;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    /// <summary>
    /// This is a decorative preview instance, not a live gameplay zombie — its AI,
    /// NavMeshAgent (there's no baked NavMesh in LoadingScene), collider, and health
    /// script would either no-op harmlessly or just waste cycles, so they're switched
    /// off. The Animator stays enabled — LoadingSceneController drives it directly.
    /// </summary>
    private static void DisableGameplayComponents(GameObject zombieObject)
    {
        DisableComponent<UnityEngine.AI.NavMeshAgent>(zombieObject);
        DisableCollider(zombieObject);
        DisableBehaviourByTypeName(zombieObject, "ZombieAI");
        DisableBehaviourByTypeName(zombieObject, "ZombieHealth");
    }

    private static void DisableComponent<T>(GameObject root) where T : Behaviour
    {
        T component = root.GetComponent<T>();

        if (component != null)
        {
            component.enabled = false;
        }
    }

    // Collider does not derive from Behaviour, so it needs its own (non-generic) toggle.
    private static void DisableCollider(GameObject root)
    {
        Collider collider = root.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
        }
    }

    private static void DisableBehaviourByTypeName(GameObject root, string typeName)
    {
        foreach (Behaviour behaviour in root.GetComponents<Behaviour>())
        {
            if (behaviour != null && behaviour.GetType().Name == typeName)
            {
                behaviour.enabled = false;
            }
        }
    }

    private static void DisableShadowsRecursively(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static LoadingSceneController SetupControllerObject(
        Scene scene,
        Image fillImage,
        Slider loadingSlider,
        TMP_Text loadingText,
        RectTransform zombieRect,
        RectTransform leftPoint,
        RectTransform rightPoint,
        Animator zombieAnimator)
    {
        Transform managerTransform = FindInActiveScene(scene, "LoadingManager");
        GameObject managerObject;

        if (managerTransform != null)
        {
            managerObject = managerTransform.gameObject;
        }
        else
        {
            managerObject = new GameObject("LoadingManager");
            Undo.RegisterCreatedObjectUndo(managerObject, UndoLabel);
            SceneManager.MoveGameObjectToScene(managerObject, scene);
        }

        LoadingSceneController controller = managerObject.GetComponent<LoadingSceneController>();

        if (controller == null)
        {
            controller = Undo.AddComponent<LoadingSceneController>(managerObject);
        }

        SerializedObject serializedController = new SerializedObject(controller);

        // Unconditional (not "if empty") for these two: LoadingBar is definitively either
        // a plain Image or a Slider right now, so whichever one isn't in use this run
        // should be cleared rather than left holding a reference from a previous setup.
        SerializedProperty fillImageProperty = serializedController.FindProperty("loadingFillImage");
        if (fillImageProperty != null)
        {
            fillImageProperty.objectReferenceValue = fillImage;
        }

        SerializedProperty sliderProperty = serializedController.FindProperty("loadingSlider");
        if (sliderProperty != null)
        {
            sliderProperty.objectReferenceValue = loadingSlider;
        }

        SetReferenceIfEmpty(serializedController, "loadingText", loadingText);
        SetReferenceIfEmpty(serializedController, "zombieRect", zombieRect);
        SetReferenceIfEmpty(serializedController, "loadingBarLeftPoint", leftPoint);
        SetReferenceIfEmpty(serializedController, "loadingBarRightPoint", rightPoint);

        if (zombieAnimator != null)
        {
            SetReferenceIfEmpty(serializedController, "zombieAnimator", zombieAnimator);
        }

        serializedController.ApplyModifiedProperties();

        return controller;
    }

    private static void SetReferenceIfEmpty(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null && property.objectReferenceValue == null)
        {
            property.objectReferenceValue = value;
        }
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

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(path);

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
