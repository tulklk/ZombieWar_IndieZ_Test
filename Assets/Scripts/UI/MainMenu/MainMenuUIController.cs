using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Owns MainMenu's top-level flow: PlayBtn opens LevelSelectPanel, SettingBtn opens
/// SettingPanel (via the existing SettingPanelToggle, not reimplemented here), and
/// only one of the two panels is ever open at once. Level buttons load their scene
/// directly, or hand off through LoadingScene via SceneLoadData if useLoadingScene
/// is enabled. Button-click SFX is handled per-button by the existing
/// UIButtonClickSfx component (already on PlayBtn/SettingBtn) rather than a second,
/// parallel audio path here — Level/Back buttons get the same component wired on by
/// the LevelSelectPanelSetup editor tool.
/// </summary>
public class MainMenuUIController : MonoBehaviour
{
    private const string LevelUnlockedPrefKey = "ZombieWar.HighestUnlockedLevel";

    [Header("Main Menu")]
    [SerializeField] private GameObject mainButtonsRoot;
    [SerializeField] private CanvasGroup mainButtonsCanvasGroup;
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingButton;

    [Header("Level Select")]
    [SerializeField] private GameObject levelSelectPanel;
    [SerializeField] private RectTransform levelSelectRect;
    [SerializeField] private CanvasGroup levelSelectCanvasGroup;
    [SerializeField] private Button backButton;
    [SerializeField] private Button[] levelButtons;

    [Header("Setting Panel")]
    [Tooltip("The existing SettingPanel animation controller — this script coordinates with it rather than re-implementing SettingPanel's open/close.")]
    [SerializeField] private SettingPanelToggle settingPanelToggle;

    [Header("Animation")]
    [SerializeField] private float openDuration = 0.3f;
    [SerializeField] private float closeDuration = 0.25f;
    [SerializeField] private float startScale = 0.88f;
    [SerializeField] private float verticalOffset = -40f;

    [Header("Scene Names")]
    [SerializeField]
    private string[] levelSceneNames =
    {
        "Level1", "Level2", "Level3", "Level4",
        "Level5", "Level6", "Level7", "Level8"
    };

    [Header("Scene Loading")]
    [SerializeField] private bool useLoadingScene;
    [SerializeField] private string loadingSceneName = "LoadingScene";

    [Header("Level Lock (optional, off by default)")]
    [SerializeField] private bool enableLevelLocking;
    [SerializeField] private Sprite lockedIcon;
    [SerializeField] private int defaultUnlockedLevel = 1;

    [Header("Cut Scene (optional)")]
    [Tooltip("Plays CutScene before Level1 specifically (the story intro) — other levels are unaffected.")]
    [SerializeField] private bool playCutSceneBeforeLevel1 = true;
    [SerializeField] private string cutSceneName = "CutScene";

    private RectTransform mainButtonsRect;
    private Vector2 levelSelectHomePosition;
    private Tween levelSelectTween;
    private Sprite[] levelButtonUnlockedSprites;
    private bool[] levelButtonLocked;

    private bool isLevelSelectOpen;
    private bool isTransitioning;
    private bool isSceneLoading;

    private void Awake()
    {
        if (levelSelectRect != null)
        {
            levelSelectHomePosition = levelSelectRect.anchoredPosition;
        }

        if (mainButtonsRoot != null)
        {
            mainButtonsRect = mainButtonsRoot.transform as RectTransform;
        }

        ValidateReferences();
        WireButtons();
        ApplyLevelAvailability();
        SetLevelSelectImmediate(open: false);
    }

    private void ValidateReferences()
    {
        if (playButton == null)
        {
            Debug.LogWarning("[MainMenuUIController] playButton is not assigned — LevelSelectPanel can't be opened from the Play button.");
        }

        if (levelSelectPanel == null || levelSelectRect == null || levelSelectCanvasGroup == null)
        {
            Debug.LogWarning("[MainMenuUIController] LevelSelectPanel references are incomplete — level selection will not be available.");
        }

        if (levelButtons == null || levelButtons.Length == 0)
        {
            Debug.LogWarning("[MainMenuUIController] No level buttons assigned.");
        }

        if (settingPanelToggle == null)
        {
            Debug.LogWarning("[MainMenuUIController] settingPanelToggle is not assigned — the Setting button will not open anything.");
        }
    }

    private void WireButtons()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonPressed);
        }

        if (settingButton != null)
        {
            settingButton.onClick.AddListener(OnSettingButtonPressed);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonPressed);
        }

        if (levelButtons != null)
        {
            for (int i = 0; i < levelButtons.Length; i++)
            {
                Button button = levelButtons[i];

                if (button == null)
                {
                    continue;
                }

                int capturedIndex = i;
                button.onClick.AddListener(() => OnLevelButtonPressed(capturedIndex));
            }
        }
    }

    /// <summary>
    /// Swaps a level button's art to lockedIcon with the "LEVEL N" label hidden whenever
    /// its scene isn't in Build Settings yet or it's progression-locked — restoring the
    /// original unlocked sprite + label the moment it becomes available. Locked buttons
    /// stay interactable (see OnLevelButtonPressed) so tapping one gives shake/SFX
    /// feedback instead of doing nothing.
    /// </summary>
    private void ApplyLevelAvailability()
    {
        if (levelButtons == null)
        {
            return;
        }

        if (levelButtonUnlockedSprites == null || levelButtonUnlockedSprites.Length != levelButtons.Length)
        {
            levelButtonUnlockedSprites = new Sprite[levelButtons.Length];

            for (int i = 0; i < levelButtons.Length; i++)
            {
                Image image = levelButtons[i] != null ? levelButtons[i].GetComponent<Image>() : null;
                levelButtonUnlockedSprites[i] = image != null ? image.sprite : null;
            }
        }

        if (levelButtonLocked == null || levelButtonLocked.Length != levelButtons.Length)
        {
            levelButtonLocked = new bool[levelButtons.Length];
        }

        for (int i = 0; i < levelButtons.Length; i++)
        {
            Button button = levelButtons[i];

            if (button == null)
            {
                continue;
            }

            bool sceneExists = levelSceneNames != null &&
                i < levelSceneNames.Length &&
                !string.IsNullOrEmpty(levelSceneNames[i]) &&
                Application.CanStreamedLevelBeLoaded(levelSceneNames[i]);

            bool available = sceneExists && IsLevelUnlocked(i);

            levelButtonLocked[i] = !available;
            SetButtonAvailabilityVisual(button, levelButtonUnlockedSprites[i], available);
        }
    }

    private void SetButtonAvailabilityVisual(Button button, Sprite unlockedSprite, bool available)
    {
        Image image = button.GetComponent<Image>();

        if (image != null)
        {
            image.sprite = available ? unlockedSprite : lockedIcon;
        }

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
        {
            label.gameObject.SetActive(available);
        }
    }

    private bool IsLevelUnlocked(int levelIndex)
    {
        if (!enableLevelLocking)
        {
            return true;
        }

        int highestUnlocked = PlayerPrefs.GetInt(LevelUnlockedPrefKey, defaultUnlockedLevel);
        return levelIndex < highestUnlocked;
    }

    public void OnPlayButtonPressed()
    {
        if (isTransitioning || isSceneLoading)
        {
            return;
        }

        if (settingPanelToggle != null && settingPanelToggle.IsOpen)
        {
            settingPanelToggle.Close();
        }

        OpenLevelSelectPanel();
    }

    public void OnSettingButtonPressed()
    {
        if (isTransitioning || isSceneLoading || settingPanelToggle == null)
        {
            return;
        }

        if (isLevelSelectOpen)
        {
            CloseLevelSelectPanel();
            settingPanelToggle.Open();
            return;
        }

        settingPanelToggle.Toggle();
    }

    public void OnBackButtonPressed()
    {
        CloseLevelSelectPanel();
    }

    public void OpenSettingPanel()
    {
        settingPanelToggle?.Open();
    }

    public void CloseSettingPanel()
    {
        settingPanelToggle?.Close();
    }

    public void OpenLevelSelectPanel()
    {
        if (isTransitioning || isSceneLoading || isLevelSelectOpen || levelSelectPanel == null)
        {
            return;
        }

        isTransitioning = true;
        isLevelSelectOpen = true;

        SetMainButtonsInteractable(false);
        SetLevelButtonsInteractable(false);

        levelSelectTween?.Kill();
        levelSelectPanel.SetActive(true);
        levelSelectCanvasGroup.blocksRaycasts = true;
        levelSelectCanvasGroup.interactable = false;

        levelSelectRect.localScale = Vector3.one * startScale;
        levelSelectCanvasGroup.alpha = 0f;
        levelSelectRect.anchoredPosition = levelSelectHomePosition + new Vector2(0f, verticalOffset);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(levelSelectRect.DOScale(Vector3.one, openDuration).SetEase(Ease.OutBack));
        sequence.Join(levelSelectRect.DOAnchorPos(levelSelectHomePosition, openDuration).SetEase(Ease.OutCubic));
        sequence.Join(levelSelectCanvasGroup.DOFade(1f, openDuration));
        sequence.OnComplete(() =>
        {
            levelSelectCanvasGroup.interactable = true;
            SetLevelButtonsInteractable(true);
            isTransitioning = false;
        });

        levelSelectTween = sequence;
    }

    public void CloseLevelSelectPanel()
    {
        if (isTransitioning || !isLevelSelectOpen || levelSelectPanel == null)
        {
            return;
        }

        isTransitioning = true;
        isLevelSelectOpen = false;

        levelSelectTween?.Kill();
        levelSelectCanvasGroup.interactable = false;
        SetLevelButtonsInteractable(false);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(levelSelectRect.DOScale(Vector3.one * startScale, closeDuration).SetEase(Ease.InCubic));
        sequence.Join(levelSelectRect.DOAnchorPos(levelSelectHomePosition + new Vector2(0f, verticalOffset), closeDuration).SetEase(Ease.InCubic));
        sequence.Join(levelSelectCanvasGroup.DOFade(0f, closeDuration));
        sequence.OnComplete(() =>
        {
            levelSelectPanel.SetActive(false);
            levelSelectCanvasGroup.blocksRaycasts = false;
            SetMainButtonsInteractable(true);
            isTransitioning = false;
        });

        levelSelectTween = sequence;
    }

    private void SetLevelSelectImmediate(bool open)
    {
        isLevelSelectOpen = open;

        if (levelSelectPanel == null || levelSelectRect == null)
        {
            return;
        }

        levelSelectRect.localScale = open ? Vector3.one : Vector3.one * startScale;
        levelSelectRect.anchoredPosition = open ? levelSelectHomePosition : levelSelectHomePosition + new Vector2(0f, verticalOffset);

        if (levelSelectCanvasGroup != null)
        {
            levelSelectCanvasGroup.alpha = open ? 1f : 0f;
            levelSelectCanvasGroup.interactable = open;
            levelSelectCanvasGroup.blocksRaycasts = open;
        }

        levelSelectPanel.SetActive(open);
    }

    private void SetMainButtonsInteractable(bool interactable)
    {
        if (mainButtonsCanvasGroup != null)
        {
            mainButtonsCanvasGroup.interactable = interactable;
            mainButtonsCanvasGroup.blocksRaycasts = interactable;
            mainButtonsCanvasGroup.alpha = interactable ? 1f : 0.6f;
        }
    }

    private void SetLevelButtonsInteractable(bool interactable)
    {
        if (levelButtons == null)
        {
            return;
        }

        foreach (Button button in levelButtons)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        if (backButton != null)
        {
            backButton.interactable = interactable;
        }
    }

    private void OnLevelButtonPressed(int levelIndex)
    {
        if (levelButtonLocked != null && levelIndex >= 0 && levelIndex < levelButtonLocked.Length && levelButtonLocked[levelIndex])
        {
            PlayLockedFeedback(levelIndex);
            return;
        }

        LoadLevel(levelIndex);
    }

    private void PlayLockedFeedback(int levelIndex)
    {
        if (levelButtons == null || levelIndex < 0 || levelIndex >= levelButtons.Length || levelButtons[levelIndex] == null)
        {
            return;
        }

        Transform buttonTransform = levelButtons[levelIndex].transform;
        buttonTransform.DOKill();
        buttonTransform.localRotation = Quaternion.identity;
        buttonTransform.DOShakeRotation(0.35f, new Vector3(0f, 0f, 12f), vibrato: 20, randomness: 90f, fadeOut: true)
            .SetUpdate(true);
    }

    public void LoadLevel(int levelIndex)
    {
        if (levelSceneNames == null || levelIndex < 0 || levelIndex >= levelSceneNames.Length)
        {
            Debug.LogError($"[MainMenuUIController] LoadLevel index {levelIndex} is out of range.");
            return;
        }

        LoadLevelBySceneName(levelSceneNames[levelIndex]);
    }

    public void LoadLevelBySceneName(string sceneName)
    {
        if (isSceneLoading)
        {
            return;
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[MainMenuUIController] LoadLevelBySceneName called with an empty scene name.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"Scene '{sceneName}' is not included in Build Settings.");
            return;
        }

        isSceneLoading = true;
        SetMainButtonsInteractable(false);
        SetLevelButtonsInteractable(false);

        bool isLevel1 = levelSceneNames != null && levelSceneNames.Length > 0 && sceneName == levelSceneNames[0];

        if (playCutSceneBeforeLevel1 && isLevel1)
        {
            // Routed through LoadingScene on the way to CutScene too (not just CutScene ->
            // Level1) so both hops look like the same "loading data" screen: Level1Button ->
            // LoadingScene -> CutScene -> LoadingScene -> Level1.
            if (Application.CanStreamedLevelBeLoaded(cutSceneName) && Application.CanStreamedLevelBeLoaded(loadingSceneName))
            {
                SceneLoadData.NextSceneName = cutSceneName;
                SceneManager.LoadSceneAsync(loadingSceneName);
                return;
            }

            Debug.LogError($"[MainMenuUIController] CutScene scene '{cutSceneName}' or LoadingScene '{loadingSceneName}' is not included in Build Settings — loading '{sceneName}' directly instead.");
        }

        if (useLoadingScene)
        {
            // Loaded async so entering LoadingScene itself doesn't hitch the main thread —
            // a synchronous LoadScene here would block for a frame while LoadingScene's own
            // assets (zombie preview, UI) load, defeating the point of a loading screen.
            SceneLoadData.NextSceneName = sceneName;
            SceneManager.LoadSceneAsync(loadingSceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
