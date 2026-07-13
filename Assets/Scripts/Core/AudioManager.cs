using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Cross-scene music + SFX manager. Self-bootstraps from a Resources prefab so it
/// exists no matter which scene is loaded first, and survives every scene load via
/// DontDestroyOnLoad. Music/SFX are independent ON-OFF toggles (mute, not volume),
/// persisted to PlayerPrefs. See AudioManagerSetup.cs (Editor) for how the
/// Resources/Audio/AudioManager prefab is built and wired to MusicBackground.mp3.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private const string MusicEnabledPrefKey = "ZombieWar.MusicEnabled";
    private const string SfxEnabledPrefKey = "ZombieWar.SfxEnabled";
    private const string ResourcesPrefabPath = "Audio/AudioManager";

    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip defaultMusicClip;
    [SerializeField] private AudioClip buttonClickClip;

    [Header("Music Volume")]
    [Tooltip("Music volume in menus (MainMenu, LoadingScene, etc).")]
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume = 0.5f;
    [Tooltip("Music volume while in a Level scene — lowered a bit so gunfire/zombie SFX stand out.")]
    [SerializeField, Range(0f, 1f)] private float levelMusicVolume = 0.3f;

    public bool MusicEnabled { get; private set; } = true;
    public bool SfxEnabled { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstanceExists()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(ResourcesPrefabPath);

        if (prefab == null)
        {
            Debug.LogWarning($"[AudioManager] No prefab found at Resources/{ResourcesPrefabPath}. Run Tools > Zombie War > Setup Audio Manager in the Editor first.");
            return;
        }

        Instantiate(prefab);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        MusicEnabled = PlayerPrefs.GetInt(MusicEnabledPrefKey, 1) != 0;
        SfxEnabled = PlayerPrefs.GetInt(SfxEnabledPrefKey, 1) != 0;

        if (musicSource != null)
        {
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.mute = !MusicEnabled;

            if (musicSource.clip == null)
            {
                musicSource.clip = defaultMusicClip;
            }

            musicSource.volume = SceneManager.GetActiveScene().name.StartsWith("Level") ? levelMusicVolume : defaultMusicVolume;

            if (musicSource.clip != null && !musicSource.isPlaying)
            {
                musicSource.Play();
            }
        }

        if (sfxSource != null)
        {
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.mute = !SfxEnabled;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// Lowers music volume in Level scenes (so gunfire/zombie SFX stand out) and restarts
    /// it from the beginning each time a Level scene loads, so every run starts fresh
    /// instead of picking up wherever the music happened to be.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (musicSource == null || musicSource.clip == null)
        {
            return;
        }

        bool isLevelScene = scene.name.StartsWith("Level");
        musicSource.volume = isLevelScene ? levelMusicVolume : defaultMusicVolume;

        if (isLevelScene)
        {
            musicSource.Stop();
            musicSource.Play();
        }
    }

    public void SetMusicEnabled(bool isEnabled)
    {
        MusicEnabled = isEnabled;

        if (musicSource != null)
        {
            musicSource.mute = !isEnabled;
        }

        PlayerPrefs.SetInt(MusicEnabledPrefKey, isEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetSfxEnabled(bool isEnabled)
    {
        SfxEnabled = isEnabled;

        if (sfxSource != null)
        {
            sfxSource.mute = !isEnabled;
        }

        PlayerPrefs.SetInt(SfxEnabledPrefKey, isEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void ToggleMusic() => SetMusicEnabled(!MusicEnabled);

    public void ToggleSfx() => SetSfxEnabled(!SfxEnabled);

    /// <summary>Non-positional SFX (UI clicks, hit markers, etc.) — overlaps freely via PlayOneShot.</summary>
    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfxSource == null)
        {
            return;
        }

        sfxSource.PlayOneShot(clip, volumeScale);
    }

    public void PlayButtonClickSfx() => PlaySfx(buttonClickClip);

    /// <summary>Positional one-off SFX (explosions, etc.) — respects the SFX toggle, unlike raw AudioSource.PlayClipAtPoint.</summary>
    public void PlaySfxAtPoint(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (!SfxEnabled || clip == null)
        {
            return;
        }

        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
