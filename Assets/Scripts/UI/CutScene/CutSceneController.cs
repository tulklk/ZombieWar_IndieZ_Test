using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the story-intro CutScene: fades through cutSceneSprites in order (subtitle
/// types out character-by-character via TMP's maxVisibleCharacters; optional Ken Burns
/// pan/scale on the image itself, off by default), advancing on a timer (autoAdvance)
/// and/or player input (Skip button, tap-anywhere via InputBlocker — no separate Next
/// button), then hands off to nextSceneName — optionally through LoadingScene — the same
/// way MainMenuUIController does. One Image/CanvasGroup is reused for every slide
/// (sprites are swapped, never Instantiated), and all animation runs via Coroutine +
/// Time.unscaledDeltaTime so it keeps playing correctly even if something upstream sets
/// Time.timeScale to 0.
/// </summary>
public class CutSceneController : MonoBehaviour
{
    [Header("CutScene Images")]
    [SerializeField] private Sprite[] cutSceneSprites;

    [Header("UI References")]
    [SerializeField] private Image cutSceneImage;
    [SerializeField] private CanvasGroup cutSceneCanvasGroup;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private Image[] progressDots;
    [SerializeField] private GameObject inputBlocker;

    [Header("Timing")]
    [SerializeField] private float imageDisplayDuration = 4f;
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float finalImageDelay = 1f;
    [SerializeField] private bool autoAdvance = true;

    [Header("Image Motion")]
    [SerializeField] private bool enableKenBurnsEffect = true;
    [SerializeField] private float startScale = 1f;
    [SerializeField] private float endScale = 1.06f;
    [SerializeField] private float panAmount = 20f;

    [Header("Subtitle")]
    [SerializeField] private string[] subtitles;
    [SerializeField] private bool showSubtitles = false;
    [SerializeField] private float typewriterCharactersPerSecond = 30f;

    [Header("Scene Transition")]
    [SerializeField] private string nextSceneName = "Level1";
    [SerializeField] private bool useLoadingScene = false;
    [SerializeField] private string loadingSceneName = "LoadingScene";

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pageChangeClip;
    [SerializeField] private AudioClip skipClip;
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField] private bool stopMusicWhenFinished = true;

    [Header("Input")]
    [SerializeField] private bool tapAnywhereToContinue = true;
    [SerializeField] private float inputCooldown = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private static readonly Color DotUpcomingColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color DotCurrentColor = new Color(1f, 0.55f, 0.15f, 1f);
    private static readonly Color DotWatchedColor = new Color(1f, 0.92f, 0.6f, 1f);

    private RectTransform cutSceneImageRect;
    private Coroutine cutSceneRoutine;
    private int currentIndex;
    private bool isTransitioning;
    private bool isFinished;
    private bool advanceRequested;
    private float lastInputTime = -999f;

    private RectTransform subtitleRootRect;
    private Coroutine subtitleAnimRoutine;

    private void Awake()
    {
        if (cutSceneImage != null)
        {
            cutSceneImageRect = cutSceneImage.rectTransform;
        }

        if (subtitleText != null)
        {
            subtitleRootRect = subtitleText.transform.parent as RectTransform;
        }

        // If MainMenuUIController (or anything else) routed us here through SceneLoadData,
        // that destination overrides the Inspector default. Cleared immediately after
        // reading so a later direct visit to CutScene doesn't reuse a stale value.
        if (!string.IsNullOrEmpty(SceneLoadData.NextSceneName))
        {
            nextSceneName = SceneLoadData.NextSceneName;
            SceneLoadData.NextSceneName = null;
        }

        WireButtons();
    }

    private void Start()
    {
        if (cutSceneSprites == null || cutSceneSprites.Length == 0)
        {
            Debug.LogError("CutSceneController: No cutscene sprites have been assigned.");
            return;
        }

        if (cutSceneSprites.Length < 6)
        {
            Debug.LogWarning($"[CutSceneController] Only {cutSceneSprites.Length} cutscene sprite(s) assigned (expected 6) — playing with what's available.");
        }

        WarmSubtitleFontAtlas();
        PlayBackgroundMusic();
        StartCutScene();
    }

    /// <summary>
    /// The default TMP font (LiberationSans SDF) uses a Dynamic atlas with no Vietnamese
    /// diacritics baked in, so the first time each new accented character appears it gets
    /// rasterized into the atlas on the main thread — right as a subtitle changes, i.e.
    /// exactly during a transition. Pre-adding every character used across all subtitles
    /// once at Start (before the first slide plays) moves that one-time cost up front
    /// instead of spreading small hitches across every image change.
    /// </summary>
    private void WarmSubtitleFontAtlas()
    {
        if (subtitleText == null || subtitleText.font == null || subtitles == null)
        {
            return;
        }

        string allCharacters = string.Empty;

        for (int i = 0; i < subtitles.Length; i++)
        {
            if (!string.IsNullOrEmpty(subtitles[i]))
            {
                allCharacters += subtitles[i];
            }
        }

        if (allCharacters.Length > 0)
        {
            subtitleText.font.TryAddCharacters(allCharacters);
        }
    }

    private void WireButtons()
    {
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(NextImage);
        }

        if (skipButton != null)
        {
            skipButton.onClick.AddListener(SkipCutScene);
        }

        if (inputBlocker != null)
        {
            Button blockerButton = inputBlocker.GetComponent<Button>();

            if (blockerButton != null)
            {
                blockerButton.onClick.AddListener(NextImage);
            }

            inputBlocker.SetActive(tapAnywhereToContinue);
        }
    }

    public void StartCutScene()
    {
        if (cutSceneRoutine != null || isFinished || cutSceneSprites == null || cutSceneSprites.Length == 0)
        {
            return;
        }

        cutSceneRoutine = StartCoroutine(PlayCutSceneSequenceRoutine());
    }

    private IEnumerator PlayCutSceneSequenceRoutine()
    {
        currentIndex = 0;

        while (currentIndex < cutSceneSprites.Length)
        {
            yield return PlayCurrentImageRoutine();
            yield return TransitionToNextImage();
        }

        cutSceneRoutine = null;
        FinishCutScene();
    }

    private IEnumerator PlayCurrentImageRoutine()
    {
        SetCurrentImage(currentIndex);
        UpdateSubtitle(currentIndex);
        UpdateProgressDots(currentIndex);
        ResetImageTransform();

        if (showDebugLogs)
        {
            Debug.Log($"[CutSceneController] Showing image {currentIndex + 1}/{cutSceneSprites.Length}.");
        }

        isTransitioning = true;
        yield return FadeCanvasGroup(0f, 1f, fadeInDuration);
        isTransitioning = false;

        advanceRequested = false;
        float elapsed = 0f;
        bool panLeftToRight = currentIndex % 2 == 0;

        while (!advanceRequested && (!autoAdvance || elapsed < imageDisplayDuration))
        {
            elapsed += Time.unscaledDeltaTime;
            ApplyKenBurns(elapsed, panLeftToRight);
            yield return null;
        }
    }

    private IEnumerator TransitionToNextImage()
    {
        isTransitioning = true;
        yield return FadeCanvasGroup(1f, 0f, fadeOutDuration);
        isTransitioning = false;
        currentIndex++;
    }

    private void ApplyKenBurns(float elapsed, bool panLeftToRight)
    {
        if (!enableKenBurnsEffect || cutSceneImageRect == null)
        {
            return;
        }

        float t = Mathf.Clamp01(elapsed / Mathf.Max(imageDisplayDuration, 0.01f));
        float eased = EaseInOutCubic(t);
        float scale = Mathf.Lerp(startScale, endScale, eased);
        float fromX = panLeftToRight ? -panAmount : panAmount;
        float toX = panLeftToRight ? panAmount : -panAmount;
        float x = Mathf.Lerp(fromX, toX, eased);

        cutSceneImageRect.localScale = new Vector3(scale, scale, 1f);
        cutSceneImageRect.anchoredPosition = new Vector2(x, 0f);
    }

    private void ResetImageTransform()
    {
        if (cutSceneImageRect == null)
        {
            return;
        }

        cutSceneImageRect.localScale = Vector3.one * startScale;
        cutSceneImageRect.anchoredPosition = Vector2.zero;
    }

    private void SetCurrentImage(int index)
    {
        if (cutSceneImage == null || cutSceneSprites == null || index < 0 || index >= cutSceneSprites.Length)
        {
            return;
        }

        cutSceneImage.sprite = cutSceneSprites[index];
    }

    private void UpdateSubtitle(int index)
    {
        if (subtitleText == null)
        {
            return;
        }

        // subtitleText's parent is the background bar (Image can't share a GameObject with
        // TextMeshProUGUI — both are Graphic components) — toggle that so the bar hides
        // along with the label instead of leaving an empty bar on screen.
        GameObject subtitleRoot = subtitleRootRect != null ? subtitleRootRect.gameObject : subtitleText.gameObject;

        bool hasLine = showSubtitles && subtitles != null && index >= 0 && index < subtitles.Length && !string.IsNullOrEmpty(subtitles[index]);

        if (!hasLine)
        {
            subtitleRoot.SetActive(false);
            return;
        }

        subtitleRoot.SetActive(true);

        if (subtitleAnimRoutine != null)
        {
            StopCoroutine(subtitleAnimRoutine);
        }

        subtitleAnimRoutine = StartCoroutine(TypewriterSubtitleRoutine(subtitles[index]));
    }

    /// <summary>Reveals the line one character at a time via TMP's maxVisibleCharacters (cheap — no re-layout per character) instead of fading/sliding the whole sentence in at once.</summary>
    private IEnumerator TypewriterSubtitleRoutine(string line)
    {
        subtitleText.text = line;
        subtitleText.ForceMeshUpdate();
        subtitleText.maxVisibleCharacters = 0;

        int totalCharacters = subtitleText.textInfo.characterCount;
        float secondsPerCharacter = 1f / Mathf.Max(typewriterCharactersPerSecond, 1f);
        float elapsed = 0f;

        while (subtitleText.maxVisibleCharacters < totalCharacters)
        {
            elapsed += Time.unscaledDeltaTime;
            subtitleText.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(elapsed / secondsPerCharacter), 0, totalCharacters);
            yield return null;
        }

        subtitleAnimRoutine = null;
    }

    private void UpdateProgressDots(int index)
    {
        if (progressDots == null)
        {
            return;
        }

        for (int i = 0; i < progressDots.Length; i++)
        {
            Image dot = progressDots[i];

            if (dot == null)
            {
                continue;
            }

            if (i == index)
            {
                dot.color = DotCurrentColor;
            }
            else if (i < index)
            {
                dot.color = DotWatchedColor;
            }
            else
            {
                dot.color = DotUpcomingColor;
            }
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (nextButton != null)
        {
            nextButton.interactable = interactable;
        }

        if (skipButton != null)
        {
            skipButton.interactable = interactable;
        }
    }

    /// <summary>Wired to InputBlocker (tap-anywhere) and, if assigned, a NextButton. Ignored mid-fade so a single press/tap never double-advances.</summary>
    public void NextImage()
    {
        if (isFinished || isTransitioning)
        {
            return;
        }

        if (Time.unscaledTime - lastInputTime < inputCooldown)
        {
            return;
        }

        lastInputTime = Time.unscaledTime;
        PlaySfx(pageChangeClip);
        advanceRequested = true;
    }

    /// <summary>Wired to SkipButton. Unlike NextImage, this works even mid-fade — skipping should always be possible.</summary>
    public void SkipCutScene()
    {
        if (isFinished)
        {
            return;
        }

        if (Time.unscaledTime - lastInputTime < inputCooldown)
        {
            return;
        }

        lastInputTime = Time.unscaledTime;
        isFinished = true;

        if (cutSceneRoutine != null)
        {
            StopCoroutine(cutSceneRoutine);
            cutSceneRoutine = null;
        }

        SetButtonsInteractable(false);

        if (inputBlocker != null)
        {
            inputBlocker.SetActive(false);
        }

        PlaySfx(skipClip);

        if (showDebugLogs)
        {
            Debug.Log("[CutSceneController] Skipped.");
        }

        StartCoroutine(SkipRoutine());
    }

    private IEnumerator SkipRoutine()
    {
        if (cutSceneCanvasGroup != null)
        {
            yield return FadeCanvasGroup(cutSceneCanvasGroup.alpha, 0f, fadeOutDuration);
        }

        LoadNextScene();
    }

    public void FinishCutScene()
    {
        if (isFinished)
        {
            return;
        }

        isFinished = true;

        if (cutSceneRoutine != null)
        {
            StopCoroutine(cutSceneRoutine);
            cutSceneRoutine = null;
        }

        SetButtonsInteractable(false);

        if (inputBlocker != null)
        {
            inputBlocker.SetActive(false);
        }

        if (showDebugLogs)
        {
            Debug.Log("[CutSceneController] Finished — loading next scene.");
        }

        StartCoroutine(FinishRoutine());
    }

    private IEnumerator FinishRoutine()
    {
        yield return new WaitForSecondsRealtime(finalImageDelay);
        LoadNextScene();
    }

    private void LoadNextScene()
    {
        if (stopMusicWhenFinished && audioSource != null)
        {
            audioSource.Stop();
        }

        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogError("[CutSceneController] nextSceneName is empty — nothing to load.");
            return;
        }

        if (useLoadingScene)
        {
            if (!Application.CanStreamedLevelBeLoaded(loadingSceneName))
            {
                Debug.LogError($"[CutSceneController] LoadingScene '{loadingSceneName}' is not included in Build Settings — staying on this scene instead of loading a blank one.");
                return;
            }

            SceneLoadData.NextSceneName = nextSceneName;
            SceneManager.LoadSceneAsync(loadingSceneName);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogError($"CutSceneController: Scene '{nextSceneName}' is not included in Build Settings.");
            return;
        }

        SceneManager.LoadSceneAsync(nextSceneName);
    }

    private IEnumerator FadeCanvasGroup(float from, float to, float duration)
    {
        if (cutSceneCanvasGroup == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            cutSceneCanvasGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / duration));
            cutSceneCanvasGroup.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        cutSceneCanvasGroup.alpha = to;
    }

    private float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    private void PlayBackgroundMusic()
    {
        if (audioSource == null || backgroundMusic == null)
        {
            return;
        }

        audioSource.clip = backgroundMusic;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.Play();
    }

    private void PlaySfx(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        audioSource.PlayOneShot(clip);
    }
}
