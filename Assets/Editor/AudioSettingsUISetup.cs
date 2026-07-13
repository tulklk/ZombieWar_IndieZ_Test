using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the Music/SFX ON-OFF toggle buttons inside MainMenu's
/// SettingPanel, placed below the Quality selector (see QualitySettingsUISetup.cs).
/// Safe to run more than once — every step finds-or-creates by name and re-applies
/// layout. Editor-only, lives under Assets/Editor.
/// </summary>
public static class AudioSettingsUISetup
{
    private const string SettingPanelName = "SettingPanel";
    private const string AudioContainerName = "AudioSettings";
    private const string UndoLabel = "Setup Audio Settings UI";

    [MenuItem("Tools/Zombie War/Setup Music-SFX Toggle UI")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AudioSettingsUISetup] Skipped — run this outside Play Mode, otherwise the setup won't be saved.");
            return;
        }

        Transform settingPanel = FindInActiveScene(SettingPanelName);

        if (settingPanel == null)
        {
            Debug.LogError($"[AudioSettingsUISetup] Could not find a GameObject named '{SettingPanelName}' in the active scene. Aborted — nothing was created.");
            return;
        }

        RectTransform audioRect = GetOrCreateUIChild<RectTransform>(settingPanel, AudioContainerName, out _);
        Undo.RecordObject(audioRect, UndoLabel);
        audioRect.anchorMin = new Vector2(0.5f, 0.5f);
        audioRect.anchorMax = new Vector2(0.5f, 0.5f);
        audioRect.pivot = new Vector2(0.5f, 0.5f);
        audioRect.sizeDelta = new Vector2(900f, 220f);
        audioRect.anchoredPosition = new Vector2(0f, -320f);

        CreateTitle(audioRect);

        Button musicButton = CreateToggleButton(audioRect, "MusicButton", "MUSIC: ON", -230f, out TextMeshProUGUI musicLabel);
        Button sfxButton = CreateToggleButton(audioRect, "SfxButton", "SFX: ON", 230f, out TextMeshProUGUI sfxLabel);

        AudioSettingsUI controller = audioRect.GetComponent<AudioSettingsUI>();

        if (controller == null)
        {
            controller = Undo.AddComponent<AudioSettingsUI>(audioRect.gameObject);
        }

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("musicButton").objectReferenceValue = musicButton;
        serializedController.FindProperty("musicButtonLabel").objectReferenceValue = musicLabel;
        serializedController.FindProperty("sfxButton").objectReferenceValue = sfxButton;
        serializedController.FindProperty("sfxButtonLabel").objectReferenceValue = sfxLabel;
        serializedController.ApplyModifiedProperties();

        EditorUtility.SetDirty(audioRect.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = audioRect.gameObject;

        Debug.Log("[AudioSettingsUISetup] Music/SFX toggle buttons created/updated under SettingPanel/AudioSettings. Make sure Tools > Zombie War > Setup Audio Manager has been run at least once, then test in Play Mode and save the scene (Ctrl+S).");
    }

    private static void CreateTitle(Transform parent)
    {
        RectTransform titleRect = GetOrCreateUIChild<TextMeshProUGUI>(parent, "AudioTitle", out TextMeshProUGUI titleText);
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(700f, 60f);

        titleText.text = "SOUND";
        titleText.fontSize = 32f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.raycastTarget = false;

        EditorUtility.SetDirty(titleText);
    }

    private static Button CreateToggleButton(Transform parent, string name, string defaultLabel, float xOffset, out TextMeshProUGUI labelText)
    {
        RectTransform buttonRect = GetOrCreateUIChild<Image>(parent, name, out Image buttonImage);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(xOffset, -50f);
        buttonRect.sizeDelta = new Vector2(380f, 90f);

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

        RectTransform labelRect = GetOrCreateUIChild<TextMeshProUGUI>(buttonRect, "Label", out labelText);
        StretchFull(labelRect);
        labelText.text = defaultLabel;
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
