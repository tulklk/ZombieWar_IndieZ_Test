using System;
using UnityEngine;

public enum PerformancePreset
{
    Low,
    Medium,
    High
}

/// <summary>
/// Single place that owns Application.targetFrameRate and QualitySettings level for the
/// whole game — nothing else should call SetQualityLevel/targetFrameRate directly.
/// Defaults to Medium (30 FPS) for a mid-range Android device; High (60 FPS) is opt-in
/// only via SetPreset(), never auto-forced. Reuses the project's existing Low/Medium/High
/// Quality tiers instead of introducing a second, parallel preset system.
/// </summary>
public class MobilePerformanceManager : MonoBehaviour
{
    private const string PresetPrefKey = "ZombieWar.PerformancePreset";

    public static MobilePerformanceManager Instance { get; private set; }

    [Header("Quality Level Mapping")]
    [Tooltip("QualitySettings level index applied for each preset — must match this project's Quality tiers (Project Settings > Quality).")]
    [SerializeField] private int lowQualityLevel = 1;
    [SerializeField] private int mediumQualityLevel = 2;
    [SerializeField] private int highQualityLevel = 3;

    [Header("Target Frame Rate")]
    [SerializeField] private int lowTargetFrameRate = 30;
    [SerializeField] private int mediumTargetFrameRate = 60;
    [SerializeField] private int highTargetFrameRate = 120;

    public PerformancePreset CurrentPreset { get; private set; } = PerformancePreset.Medium;

    /// <summary>Ensures a manager exists even if none was placed in the scene by hand.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstanceExists()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("MobilePerformanceManager");
        managerObject.AddComponent<MobilePerformanceManager>();
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

        ApplyPreset(LoadSavedPreset(), save: false);
    }

    public void SetPreset(PerformancePreset preset)
    {
        ApplyPreset(preset, save: true);
    }

    private void ApplyPreset(PerformancePreset preset, bool save)
    {
        CurrentPreset = preset;

        // vSync fights with a deliberately-chosen targetFrameRate on mobile; the frame
        // rate is controlled here, not by the display refresh divisor.
        QualitySettings.vSyncCount = 0;

        switch (preset)
        {
            case PerformancePreset.Low:
                QualitySettings.SetQualityLevel(lowQualityLevel, true);
                Application.targetFrameRate = lowTargetFrameRate;
                break;

            case PerformancePreset.Medium:
                QualitySettings.SetQualityLevel(mediumQualityLevel, true);
                Application.targetFrameRate = mediumTargetFrameRate;
                break;

            case PerformancePreset.High:
                QualitySettings.SetQualityLevel(highQualityLevel, true);
                Application.targetFrameRate = highTargetFrameRate;
                break;
        }

        if (save)
        {
            PlayerPrefs.SetInt(PresetPrefKey, (int)preset);
            PlayerPrefs.Save();
        }
    }

    private PerformancePreset LoadSavedPreset()
    {
        if (PlayerPrefs.HasKey(PresetPrefKey))
        {
            int savedValue = PlayerPrefs.GetInt(PresetPrefKey);

            if (Enum.IsDefined(typeof(PerformancePreset), savedValue))
            {
                return (PerformancePreset)savedValue;
            }
        }

        return PerformancePreset.Medium;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
