using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Two ON/OFF buttons — Music and SFX — that drive AudioManager and reflect its
/// current mute state. Each button shows one of two icon children (On/Off), swapped
/// with a pop-in/pop-out animation. Lives on "AudioSettings" inside MainMenu's
/// SettingPanel, alongside QualitySettingsUI.
/// </summary>
public class AudioSettingsUI : MonoBehaviour
{
    [SerializeField] private Button musicButton;
    [SerializeField] private GameObject musicOnIcon;
    [SerializeField] private GameObject musicOffIcon;

    [SerializeField] private Button sfxButton;
    [SerializeField] private GameObject sfxOnIcon;
    [SerializeField] private GameObject sfxOffIcon;

    [Header("Icon Swap Animation")]
    [SerializeField] private float iconInDuration = 0.2f;
    [SerializeField] private float iconOutDuration = 0.15f;

    private void Awake()
    {
        if (musicButton != null) musicButton.onClick.AddListener(ToggleMusic);
        if (sfxButton != null) sfxButton.onClick.AddListener(ToggleSfx);
    }

    private void Start()
    {
        RefreshVisuals(animate: false);
    }

    private void OnEnable()
    {
        if (AudioManager.Instance != null)
        {
            RefreshVisuals(animate: false);
        }
    }

    private void ToggleMusic()
    {
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.ToggleMusic();
        RefreshVisuals(animate: true);
    }

    private void ToggleSfx()
    {
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.ToggleSfx();
        RefreshVisuals(animate: true);
    }

    private void RefreshVisuals(bool animate)
    {
        if (AudioManager.Instance == null)
        {
            return;
        }

        SetIconState(musicOnIcon, musicOffIcon, AudioManager.Instance.MusicEnabled, animate);
        SetIconState(sfxOnIcon, sfxOffIcon, AudioManager.Instance.SfxEnabled, animate);
    }

    /// <summary>
    /// Swaps which of the two icons is active — old one shrinks away, new one pops in.
    /// Instant (no animation) on the very first refresh so the panel doesn't visibly
    /// "settle" the moment it's opened.
    /// </summary>
    private void SetIconState(GameObject onIcon, GameObject offIcon, bool isOn, bool animate)
    {
        GameObject shownIcon = isOn ? onIcon : offIcon;
        GameObject hiddenIcon = isOn ? offIcon : onIcon;

        if (!animate)
        {
            if (shownIcon != null)
            {
                shownIcon.transform.localScale = Vector3.one;
                shownIcon.SetActive(true);
            }

            if (hiddenIcon != null)
            {
                hiddenIcon.SetActive(false);
            }

            return;
        }

        if (hiddenIcon != null && hiddenIcon.activeSelf)
        {
            Transform hiddenTransform = hiddenIcon.transform;
            hiddenTransform.DOKill();
            hiddenTransform.DOScale(Vector3.zero, iconOutDuration)
                .SetEase(Ease.InBack)
                .SetUpdate(true)
                .OnComplete(() => hiddenIcon.SetActive(false));
        }

        if (shownIcon != null && !shownIcon.activeSelf)
        {
            shownIcon.SetActive(true);
            Transform shownTransform = shownIcon.transform;
            shownTransform.DOKill();
            shownTransform.localScale = Vector3.zero;
            shownTransform.DOScale(Vector3.one, iconInDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }
    }
}
