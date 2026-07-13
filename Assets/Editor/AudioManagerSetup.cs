using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds/updates the Resources/Audio/AudioManager prefab that AudioManager.cs
/// self-instantiates at runtime (RuntimeInitializeOnLoadMethod), wiring it to the
/// project's existing MusicBackground.mp3. The temp GameObject used to author the
/// prefab is created and destroyed entirely within this method — nothing is left
/// behind in whatever scene happens to be open, so it's safe to run at any time.
/// </summary>
public static class AudioManagerSetup
{
    private const string MusicClipPath = "Assets/Audio/Music/MusicBackground.mp3";
    private const string ButtonClickClipPath = "Assets/Audio/SFX/Button/ButtonPress.mp3";
    private const string PrefabFolder = "Assets/Resources/Audio";
    private const string PrefabPath = PrefabFolder + "/AudioManager.prefab";

    /// <summary>
    /// Places a real, linked instance of the AudioManager prefab as a root object in
    /// whichever scene is currently open — for when you want to see/select it in the
    /// Hierarchy instead of relying purely on the runtime auto-bootstrap. Its own
    /// Awake() singleton guard means having one in every scene (MainMenu, LoadingScene,
    /// Level1...) is safe — duplicates from later scene loads destroy themselves.
    /// Run this once per scene you want it visible in, then save that scene (Ctrl+S).
    /// </summary>
    [MenuItem("Tools/Zombie War/Add Audio Manager To Scene")]
    public static void AddToScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AudioManagerSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        foreach (GameObject root in activeScene.GetRootGameObjects())
        {
            if (root.name == "AudioManager" && root.GetComponent<AudioManager>() != null)
            {
                Debug.Log("[AudioManagerSetup] This scene already has an AudioManager. Selecting it.");
                Selection.activeGameObject = root;
                return;
            }
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"[AudioManagerSetup] No prefab at '{PrefabPath}' yet. Run Tools > Zombie War > Setup Audio Manager first, then run this again.");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, activeScene);
        Undo.RegisterCreatedObjectUndo(instance, "Add Audio Manager To Scene");
        instance.transform.position = Vector3.zero;

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = instance;

        Debug.Log($"[AudioManagerSetup] AudioManager added to '{activeScene.name}'. Save the scene (Ctrl+S), then repeat in any other scene you want it visible in (MainMenu, LoadingScene, ...) — its Awake() guard already prevents duplicates if more than one ends up loaded at once.");
    }

    [MenuItem("Tools/Zombie War/Setup Audio Manager")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AudioManagerSetup] Skipped — run this outside Play Mode.");
            return;
        }

        EnsureFolder("Assets/Resources");
        EnsureFolder(PrefabFolder);

        AudioClip musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicClipPath);

        if (musicClip == null)
        {
            Debug.LogWarning($"[AudioManagerSetup] Music clip not found at '{MusicClipPath}' — the prefab will be created without a default music clip. Assign one manually on the AudioManager prefab afterward.");
        }

        AudioClip buttonClickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ButtonClickClipPath);

        if (buttonClickClip == null)
        {
            Debug.LogWarning($"[AudioManagerSetup] Button click clip not found at '{ButtonClickClipPath}' — assign one manually on the AudioManager prefab afterward.");
        }

        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        GameObject tempRoot = existingPrefab != null
            ? Object.Instantiate(existingPrefab)
            : new GameObject("AudioManager");

        tempRoot.name = "AudioManager";

        AudioManager audioManager = tempRoot.GetComponent<AudioManager>();
        if (audioManager == null)
        {
            audioManager = tempRoot.AddComponent<AudioManager>();
        }

        AudioSource musicSource = GetOrCreateChildAudioSource(tempRoot.transform, "MusicSource");
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = 0.5f;

        AudioSource sfxSource = GetOrCreateChildAudioSource(tempRoot.transform, "SfxSource");
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.volume = 1f;

        SerializedObject serializedManager = new SerializedObject(audioManager);
        serializedManager.FindProperty("musicSource").objectReferenceValue = musicSource;
        serializedManager.FindProperty("sfxSource").objectReferenceValue = sfxSource;

        if (musicClip != null)
        {
            serializedManager.FindProperty("defaultMusicClip").objectReferenceValue = musicClip;
        }

        if (buttonClickClip != null)
        {
            serializedManager.FindProperty("buttonClickClip").objectReferenceValue = buttonClickClip;
        }

        serializedManager.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(tempRoot, PrefabPath);
        Object.DestroyImmediate(tempRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[AudioManagerSetup] AudioManager prefab created/updated at '{PrefabPath}'. It self-instantiates at runtime in every scene — nothing further to place by hand. Enter Play Mode to verify music starts and Tools > Zombie War > Setup Music/SFX Toggle UI to add the ON/OFF buttons.");
    }

    private static AudioSource GetOrCreateChildAudioSource(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        GameObject childObject = existing != null ? existing.gameObject : new GameObject(name);

        if (existing == null)
        {
            childObject.transform.SetParent(parent, false);
        }

        AudioSource source = childObject.GetComponent<AudioSource>();
        return source != null ? source : childObject.AddComponent<AudioSource>();
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
}
