using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the level's timer + win/lose flow. Counts elapsed time from scene start into
/// timeCountText every frame. Shows WinPanel (with the final time) when
/// ZombieWaveManager.OnAllWavesCompletedEvent fires, or shows LosePanel when
/// PlayerHealth.OnDied fires — whichever happens first wins, isRunning guards the other
/// one out. Either outcome pauses gameplay (Time.timeScale = 0) until Restart/Main Menu.
/// </summary>
public class LevelResultManager : MonoBehaviour
{
    [Header("Timer")]
    [SerializeField] private TMP_Text timeCountText;

    [Header("Player")]
    [Tooltip("Auto-found via GameObject.FindGameObjectWithTag(\"Player\") if left empty.")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Win")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private CanvasGroup winCanvasGroup;
    [SerializeField] private TMP_Text winTimeText;
    [SerializeField] private TMP_Text killCountText;
    [SerializeField] private TMP_Text scoreText;
    [Tooltip("How long each of Kills/Time/Score takes to count up from 0 to its final value — they count up one after another, not all at once.")]
    [SerializeField] private float countUpDuration = 1f;
    [Tooltip("Pause between each stat finishing its count-up and the next one starting.")]
    [SerializeField] private float countUpGap = 0.2f;

    [Header("Lose")]
    [SerializeField] private GameObject losePanel;
    [SerializeField] private CanvasGroup loseCanvasGroup;
    [SerializeField] private TMP_Text loseTimeText;

    [Header("Boss (optional)")]
    [Tooltip("Hidden immediately if the Player dies while a Boss fight is in progress — otherwise its health bar lingers visible behind the Lose panel. Leave empty if this level has no Boss.")]
    [SerializeField] private BossHealthUI bossHealthUI;

    [Header("Show Animation")]
    [SerializeField] private float showFadeDuration = 0.35f;
    [SerializeField] private float showStartScale = 0.9f;

    [Header("Scenes")]
    [SerializeField] private string levelSceneName = "Level1";
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [Tooltip("0-based index matching MainMenuUIController's levelSceneNames array (Level1 = 0) — used to unlock the NEXT level when WinPanel's Next button is pressed.")]
    [SerializeField] private int levelIndex = 0;

    private float elapsedTime;
    private bool isRunning = true;
    private Coroutine showRoutine;
    private int totalKills;
    private int totalScore;

    private void Awake()
    {
        if (playerHealth == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                playerHealth = player.GetComponent<PlayerHealth>();
            }
        }

        if (winPanel != null)
        {
            winPanel.SetActive(false);
        }

        if (losePanel != null)
        {
            losePanel.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDied += HandlePlayerDied;
        }

        ZombieHealth.AnyZombieDied += HandleZombieDied;
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDied -= HandlePlayerDied;
        }

        ZombieHealth.AnyZombieDied -= HandleZombieDied;
    }

    /// <summary>Tallies every zombie kill for the run, regardless of type — each type's own ZombieData.scoreValue is what makes a Tank worth more than a base Zombie.</summary>
    private void HandleZombieDied(ZombieHealth zombieHealth)
    {
        totalKills++;
        totalScore += zombieHealth.ScoreValue;
    }

    private void Update()
    {
        if (!isRunning)
        {
            return;
        }

        elapsedTime += Time.deltaTime;
        UpdateTimeText(timeCountText, elapsedTime);
    }

    private static void UpdateTimeText(TMP_Text text, float seconds)
    {
        if (text == null)
        {
            return;
        }

        int minutes = Mathf.FloorToInt(seconds / 60f);
        int wholeSeconds = Mathf.FloorToInt(seconds % 60f);
        text.text = $"{minutes:00}:{wholeSeconds:00}";
    }

    /// <summary>Wired to ZombieWaveManager.OnAllWavesCompletedEvent.</summary>
    public void ShowWinPanel()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;
        AudioManager.Instance?.StopMusic();

        ShowPanelAnimated(winPanel, winCanvasGroup, onIntroComplete: () => StartCoroutine(PlayWinStatsSequence()));

        Time.timeScale = 0f;
    }

    /// <summary>Runs only after WinPanel's intro (Victory, then Cup, then buttons) has fully played — Kills, then Time, then Score count up one at a time, never simultaneously.</summary>
    private IEnumerator PlayWinStatsSequence()
    {
        yield return CountUpInt(killCountText, totalKills, countUpDuration);
        yield return new WaitForSecondsRealtime(countUpGap);

        yield return CountUpTime(winTimeText, elapsedTime, countUpDuration);
        yield return new WaitForSecondsRealtime(countUpGap);

        yield return CountUpInt(scoreText, totalScore, countUpDuration);
    }

    private static IEnumerator CountUpInt(TMP_Text text, int targetValue, float duration)
    {
        if (text == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            text.text = targetValue.ToString();
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            text.text = Mathf.RoundToInt(Mathf.Lerp(0, targetValue, t)).ToString();
            yield return null;
        }

        text.text = targetValue.ToString();
    }

    private static IEnumerator CountUpTime(TMP_Text text, float targetSeconds, float duration)
    {
        if (text == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            UpdateTimeText(text, targetSeconds);
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            UpdateTimeText(text, Mathf.Lerp(0f, targetSeconds, t));
            yield return null;
        }

        UpdateTimeText(text, targetSeconds);
    }

    private void HandlePlayerDied()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;
        AudioManager.Instance?.StopMusic();
        bossHealthUI?.Hide();
        UpdateTimeText(loseTimeText, elapsedTime);
        ShowPanelAnimated(losePanel, loseCanvasGroup);

        Time.timeScale = 0f;
    }

    /// <summary>
    /// Fades + scale-pops the panel in instead of a hard SetActive pop. Runs on unscaled
    /// time so it still plays out visually even though Time.timeScale is set to 0 right
    /// after this is called (the whole point of the pause). If the panel has a
    /// PanelIntroAnimator, onIntroComplete fires once its whole title+stagger sequence has
    /// finished; with no PanelIntroAnimator, onIntroComplete fires immediately instead of
    /// never firing at all.
    /// </summary>
    private void ShowPanelAnimated(GameObject panel, CanvasGroup canvasGroup, System.Action onIntroComplete = null)
    {
        if (panel == null)
        {
            return;
        }

        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
        }

        panel.SetActive(true);
        showRoutine = StartCoroutine(FadeInPanel(panel.transform as RectTransform, canvasGroup));

        PanelIntroAnimator introAnimator = panel.GetComponent<PanelIntroAnimator>();

        if (introAnimator != null)
        {
            if (onIntroComplete != null)
            {
                void HandleIntroCompleted()
                {
                    introAnimator.IntroCompleted -= HandleIntroCompleted;
                    onIntroComplete();
                }

                introAnimator.IntroCompleted += HandleIntroCompleted;
            }

            introAnimator.PlayIntro();
        }
        else
        {
            onIntroComplete?.Invoke();
        }
    }

    private IEnumerator FadeInPanel(RectTransform rect, CanvasGroup canvasGroup)
    {
        if (rect != null)
        {
            rect.localScale = Vector3.one * showStartScale;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        float elapsed = 0f;

        while (elapsed < showFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / showFadeDuration);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = t;
            }

            if (rect != null)
            {
                rect.localScale = Vector3.one * Mathf.Lerp(showStartScale, 1f, t);
            }

            yield return null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        if (rect != null)
        {
            rect.localScale = Vector3.one;
        }

        showRoutine = null;
    }

    public void RestartLevel()
    {
        AudioManager.Instance?.PlayMusicFromStart();
        Time.timeScale = 1f;
        SceneManager.LoadScene(levelSceneName);
    }

    public void ReturnToMainMenu()
    {
        AudioManager.Instance?.PlayMusicFromStart();
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>Wire this to WinPanel's Next button: unlocks the next level, then returns to MainMenu with LevelSelectPanel already open.</summary>
    public void GoToNextLevel()
    {
        UnlockNextLevel();

        AudioManager.Instance?.PlayMusicFromStart();
        SceneLoadData.OpenLevelSelectOnLoad = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void UnlockNextLevel()
    {
        const int DefaultUnlockedLevel = 1;

        int highestUnlocked = PlayerPrefs.GetInt(MainMenuUIController.LevelUnlockedPrefKey, DefaultUnlockedLevel);
        int newHighestUnlocked = Mathf.Max(highestUnlocked, levelIndex + 2);

        PlayerPrefs.SetInt(MainMenuUIController.LevelUnlockedPrefKey, newHighestUnlocked);
        PlayerPrefs.Save();
    }
}
