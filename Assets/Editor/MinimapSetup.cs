using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click minimap setup: creates MiniMapCamera + its RenderTexture, builds the
/// MiniMapRoot UI under GameCanvas (top-right), and wires a MinimapController.
/// Safe to run more than once — every step finds-or-creates, and never overwrites a
/// reference you've already assigned by hand. Editor-only, lives under Assets/Editor.
/// </summary>
public static class MinimapSetup
{
    private const string RenderTextureFolder = "Assets/Resources/UI/Minimap";
    private const string RenderTexturePath = RenderTextureFolder + "/MinimapRT.renderTexture";
    private const int RenderTextureSize = 256;

    private const string GameCanvasName = "GameCanvas";
    private const string MinimapCameraName = "MiniMapCamera";
    private const string PlayerMarkerIconPath = "Assets/UI/Images/Material/IconGPS.png";

    [MenuItem("Tools/Zombie War/Setup Minimap")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[MinimapSetup] Skipped — run this outside Play Mode, otherwise the setup won't be saved.");
            return;
        }

        Canvas gameCanvas = FindGameCanvas();

        if (gameCanvas == null)
        {
            Debug.LogError($"[MinimapSetup] Could not find a GameObject named '{GameCanvasName}' with a Canvas component in the active scene. Aborted — nothing was created.");
            return;
        }

        RenderTexture renderTexture = GetOrCreateRenderTexture();

        if (renderTexture == null)
        {
            Debug.LogError("[MinimapSetup] Could not create the minimap RenderTexture. Aborted.");
            return;
        }

        Camera minimapCamera = GetOrCreateMinimapCamera(renderTexture);

        RectTransform minimapRoot = GetOrCreateMinimapUI(
            gameCanvas,
            renderTexture,
            out RectTransform rawImageRect,
            out RectTransform playerMarkerRect);

        MinimapController controller = minimapRoot.GetComponent<MinimapController>();

        if (controller == null)
        {
            controller = Undo.AddComponent<MinimapController>(minimapRoot.gameObject);
        }

        ApplyControllerReferences(controller, minimapCamera, rawImageRect, playerMarkerRect);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = minimapRoot.gameObject;

        Debug.Log("[MinimapSetup] Minimap created/updated. Select MiniMapRoot in Hierarchy to review the MinimapController fields (player/mapRoot/mode), test in Play Mode, then save the scene (Ctrl+S).");
    }

    private static Canvas FindGameCanvas()
    {
        GameObject canvasObject = GameObject.Find(GameCanvasName);
        return canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
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
            name = "MinimapRT",
            antiAliasing = 1,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        AssetDatabase.CreateAsset(renderTexture, RenderTexturePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
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

    private static Camera GetOrCreateMinimapCamera(RenderTexture targetTexture)
    {
        GameObject cameraObject = GameObject.Find(MinimapCameraName);
        Camera minimapCamera;

        if (cameraObject != null)
        {
            minimapCamera = cameraObject.GetComponent<Camera>();

            if (minimapCamera == null)
            {
                minimapCamera = Undo.AddComponent<Camera>(cameraObject);
            }
        }
        else
        {
            cameraObject = new GameObject(MinimapCameraName);
            Undo.RegisterCreatedObjectUndo(cameraObject, "Setup Minimap");
            minimapCamera = cameraObject.AddComponent<Camera>();
        }

        Undo.RecordObject(cameraObject.transform, "Setup Minimap");
        Undo.RecordObject(minimapCamera, "Setup Minimap");

        cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        if (cameraObject.transform.position.y < 1f)
        {
            Vector3 position = cameraObject.transform.position;
            position.y = 40f;
            cameraObject.transform.position = position;
        }

        minimapCamera.orthographic = true;

        if (minimapCamera.orthographicSize <= 0f)
        {
            minimapCamera.orthographicSize = 20f;
        }

        minimapCamera.targetTexture = targetTexture;
        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = Color.black;
        minimapCamera.nearClipPlane = 1f;
        minimapCamera.farClipPlane = 500f;

        // Renders Everything by default — narrow this down in the Inspector (e.g. uncheck
        // Zombie/UI layers) once you've confirmed the base minimap looks right.
        if (minimapCamera.cullingMask == 0)
        {
            minimapCamera.cullingMask = ~0;
        }

        EditorUtility.SetDirty(minimapCamera);

        return minimapCamera;
    }

    private static RectTransform GetOrCreateMinimapUI(
        Canvas gameCanvas,
        RenderTexture renderTexture,
        out RectTransform rawImageRect,
        out RectTransform playerMarkerRect)
    {
        RectTransform rootRect = GetOrCreateUIChild<RectTransform>(gameCanvas.transform, "MiniMapRoot", out _);
        ConfigureTopRightAnchor(rootRect, new Vector2(-20f, -20f), new Vector2(260f, 260f));

        RectTransform frameRect = GetOrCreateUIChild<Image>(rootRect, "MiniMapFrame", out Image frameImage);
        StretchFull(frameRect);

        if (frameImage.sprite == null)
        {
            frameImage.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
        }

        frameImage.raycastTarget = false;

        rawImageRect = GetOrCreateUIChild<RawImage>(rootRect, "MiniMapRawImage", out RawImage rawImage);
        StretchWithMargin(rawImageRect, 4f);
        rawImage.texture = renderTexture;
        rawImage.raycastTarget = false;

        playerMarkerRect = GetOrCreateUIChild<Image>(rawImageRect, "PlayerMarker", out Image markerImage);
        playerMarkerRect.anchorMin = new Vector2(0.5f, 0.5f);
        playerMarkerRect.anchorMax = new Vector2(0.5f, 0.5f);
        playerMarkerRect.pivot = new Vector2(0.5f, 0.5f);
        playerMarkerRect.sizeDelta = new Vector2(28f, 28f);

        Sprite playerMarkerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlayerMarkerIconPath);

        if (playerMarkerSprite != null)
        {
            markerImage.sprite = playerMarkerSprite;
            markerImage.color = Color.white;
        }
        else
        {
            Debug.LogWarning($"[MinimapSetup] Marker icon not found at {PlayerMarkerIconPath} — kept the plain red square instead.");
            markerImage.color = Color.red;
        }

        markerImage.raycastTarget = false;

        EditorUtility.SetDirty(rootRect.gameObject);

        return rootRect;
    }

    private static RectTransform GetOrCreateUIChild<T>(Transform parent, string name, out T component) where T : Component
    {
        Transform existing = parent.Find(name);
        GameObject childObject;

        if (existing != null)
        {
            childObject = existing.gameObject;
        }
        else
        {
            childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, "Setup Minimap");
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

    private static void ConfigureTopRightAnchor(RectTransform rect, Vector2 offsetFromCorner, Vector2 size)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = offsetFromCorner;
        rect.sizeDelta = size;
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void StretchWithMargin(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin);
        rect.offsetMax = new Vector2(-margin, -margin);
    }

    private static void ApplyControllerReferences(
        MinimapController controller,
        Camera minimapCamera,
        RectTransform minimapRect,
        RectTransform playerMarker)
    {
        SerializedObject serializedController = new SerializedObject(controller);

        SetReferenceIfEmpty(serializedController, "player", FindPlayerTransform());
        SetReferenceIfEmpty(serializedController, "mapRoot", FindMapRoot());
        SetReferenceIfEmpty(serializedController, "minimapCamera", minimapCamera);
        SetReferenceIfEmpty(serializedController, "playerMarker", playerMarker);
        SetReferenceIfEmpty(serializedController, "minimapRect", minimapRect);

        serializedController.ApplyModifiedProperties();
    }

    private static void SetReferenceIfEmpty(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null && property.objectReferenceValue == null)
        {
            property.objectReferenceValue = value;
        }
    }

    private static Transform FindPlayerTransform()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        return player != null ? player.transform : null;
    }

    private static Transform FindMapRoot()
    {
        GameObject map = GameObject.Find("Map");

        if (map != null)
        {
            return map.transform;
        }

        GameObject ground = GameObject.Find("Ground");
        return ground != null ? ground.transform : null;
    }
}
