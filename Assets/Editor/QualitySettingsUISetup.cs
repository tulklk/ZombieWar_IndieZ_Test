using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the Low/Medium/High quality selector inside MainMenu's
/// SettingPanel/Quality GameObject. Safe to run more than once — every step
/// finds-or-creates by name and re-applies layout, so re-running just refreshes
/// the same objects instead of duplicating them. Editor-only, lives under Assets/Editor.
/// </summary>
public static class QualitySettingsUISetup
{
    private const string SettingPanelName = "SettingPanel";
    private const string QualityContainerName = "Quality";
    private const string UndoLabel = "Setup Quality Settings UI";

    [MenuItem("Tools/Zombie War/Setup Quality Settings UI")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[QualitySettingsUISetup] Skipped — run this outside Play Mode, otherwise the setup won't be saved.");
            return;
        }

        Transform settingPanel = FindInActiveScene(SettingPanelName);

        if (settingPanel == null)
        {
            Debug.LogError($"[QualitySettingsUISetup] Could not find a GameObject named '{SettingPanelName}' in the active scene. Aborted — nothing was created.");
            return;
        }

        RectTransform qualityRect = GetOrCreateUIChild<RectTransform>(settingPanel, QualityContainerName, out _);
        Undo.RecordObject(qualityRect, UndoLabel);
        qualityRect.anchorMin = new Vector2(0.5f, 0.5f);
        qualityRect.anchorMax = new Vector2(0.5f, 0.5f);
        qualityRect.pivot = new Vector2(0.5f, 0.5f);
        qualityRect.sizeDelta = new Vector2(900f, 260f);
        qualityRect.anchoredPosition = Vector2.zero;

        CreateTitle(qualityRect);

        Button lowButton = CreateOptionButton(qualityRect, "LowButton", "LOW", -300f);
        Button mediumButton = CreateOptionButton(qualityRect, "MediumButton", "MEDIUM", 0f);
        Button highButton = CreateOptionButton(qualityRect, "HighButton", "HIGH", 300f);

        QualitySettingsUI controller = qualityRect.GetComponent<QualitySettingsUI>();

        if (controller == null)
        {
            controller = Undo.AddComponent<QualitySettingsUI>(qualityRect.gameObject);
        }

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("lowButton").objectReferenceValue = lowButton;
        serializedController.FindProperty("mediumButton").objectReferenceValue = mediumButton;
        serializedController.FindProperty("highButton").objectReferenceValue = highButton;
        serializedController.ApplyModifiedProperties();

        EditorUtility.SetDirty(qualityRect.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = qualityRect.gameObject;

        Debug.Log("[QualitySettingsUISetup] Quality selector created/updated under SettingPanel/Quality. Select it in Hierarchy to review, test in Play Mode, then save the scene (Ctrl+S).");
    }

    private static void CreateTitle(Transform parent)
    {
        RectTransform titleRect = GetOrCreateUIChild<TextMeshProUGUI>(parent, "QualityTitle", out TextMeshProUGUI titleText);
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, 0f);
        titleRect.sizeDelta = new Vector2(700f, 60f);

        titleText.text = "GRAPHIC QUALITY";
        titleText.fontSize = 32f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.raycastTarget = false;

        EditorUtility.SetDirty(titleText);
    }

    private static Button CreateOptionButton(Transform parent, string name, string label, float xOffset)
    {
        RectTransform buttonRect = GetOrCreateUIChild<Image>(parent, name, out Image buttonImage);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(xOffset, -50f);
        buttonRect.sizeDelta = new Vector2(240f, 90f);

        if (buttonImage.sprite == null)
        {
            buttonImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            buttonImage.type = Image.Type.Sliced;
        }

        buttonImage.color = Color.white;

        Button button = buttonRect.GetComponent<Button>();

        if (button == null)
        {
            button = Undo.AddComponent<Button>(buttonRect.gameObject);
            button.targetGraphic = buttonImage;
        }

        RectTransform labelRect = GetOrCreateUIChild<TextMeshProUGUI>(buttonRect, "Label", out TextMeshProUGUI labelText);
        StretchFull(labelRect);
        labelText.text = label;
        labelText.fontSize = 26f;
        labelText.fontStyle = FontStyles.Bold;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.black;
        labelText.raycastTarget = false;

        EditorUtility.SetDirty(buttonImage);
        EditorUtility.SetDirty(labelText);

        return button;
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

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Searches every root GameObject's full hierarchy, including inactive objects.</summary>
    private static Transform FindInActiveScene(string name)
    {
        Scene activeScene = SceneManager.GetActiveScene();

        foreach (GameObject root in activeScene.GetRootGameObjects())
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
