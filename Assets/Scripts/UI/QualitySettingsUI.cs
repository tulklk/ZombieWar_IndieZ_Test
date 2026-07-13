using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Three-button Low/Medium/High selector that drives MobilePerformanceManager and
/// reflects its currently active preset. Lives on the "Quality" GameObject inside
/// MainMenu's SettingPanel.
/// </summary>
public class QualitySettingsUI : MonoBehaviour
{
    [SerializeField] private Button lowButton;
    [SerializeField] private Button mediumButton;
    [SerializeField] private Button highButton;

    [SerializeField] private Color selectedColor = new Color(0.85f, 0.65f, 0.13f);
    [SerializeField] private Color unselectedColor = Color.white;

    private void Awake()
    {
        if (lowButton != null) lowButton.onClick.AddListener(() => ApplyPreset(PerformancePreset.Low));
        if (mediumButton != null) mediumButton.onClick.AddListener(() => ApplyPreset(PerformancePreset.Medium));
        if (highButton != null) highButton.onClick.AddListener(() => ApplyPreset(PerformancePreset.High));
    }

    private void Start()
    {
        RefreshVisuals();
    }

    private void OnEnable()
    {
        if (MobilePerformanceManager.Instance != null)
        {
            RefreshVisuals();
        }
    }

    private void ApplyPreset(PerformancePreset preset)
    {
        if (MobilePerformanceManager.Instance == null)
        {
            return;
        }

        MobilePerformanceManager.Instance.SetPreset(preset);
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        if (MobilePerformanceManager.Instance == null)
        {
            return;
        }

        PerformancePreset current = MobilePerformanceManager.Instance.CurrentPreset;
        SetButtonState(lowButton, current == PerformancePreset.Low);
        SetButtonState(mediumButton, current == PerformancePreset.Medium);
        SetButtonState(highButton, current == PerformancePreset.High);
    }

    private void SetButtonState(Button button, bool selected)
    {
        if (button == null || button.targetGraphic == null)
        {
            return;
        }

        button.targetGraphic.color = selected ? selectedColor : unselectedColor;
    }
}
