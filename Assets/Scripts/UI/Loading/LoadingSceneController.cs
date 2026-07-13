using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives LoadingScene: kicks off SceneManager.LoadSceneAsync(nextSceneName) with
/// allowSceneActivation held off, smooths the reported progress into a 0-1 display
/// value, updates the bar/text/zombie from it, and only activates the next scene once
/// the load is actually done, the bar has visually reached 100%, and minimumLoadingTime
/// has elapsed. Runs entirely off a single coroutine driven by unscaled time, so it
/// still animates correctly if something upstream sets Time.timeScale to 0.
/// </summary>
public class LoadingSceneController : MonoBehaviour
{
    [Header("Scene Loading")]
    [Tooltip("Used only when nothing routed us here via SceneLoadData (e.g. the app's very first cold boot straight into this scene) — LoadingScene's default destination.")]
    [SerializeField] private string defaultSceneName = "MainMenu";
    [SerializeField] private float minimumLoadingTime = 5f;
    [SerializeField] private float completionDelay = 0.5f;
    [SerializeField] private bool loadOnStart = true;

    [Header("Loading UI")]
    [SerializeField] private Image loadingFillImage;
    [SerializeField] private Slider loadingSlider;
    [SerializeField] private TMP_Text loadingText;

    [Header("Zombie Runner")]
    [SerializeField] private RectTransform zombieRect;
    [SerializeField] private RectTransform loadingBarLeftPoint;
    [SerializeField] private RectTransform loadingBarRightPoint;
    [SerializeField] private Animator zombieAnimator;
    [Tooltip("Name of the Animator parameter that drives the run cycle. This project's zombie controller uses a float \"MoveSpeed\" blend tree (0=Idle, 0.5=Walk, 1=Run) rather than a bool — set runningParameterIsBool accordingly.")]
    [SerializeField] private string runningParameterName = "MoveSpeed";
    [SerializeField] private bool runningParameterIsBool;
    [SerializeField] private float zombieVerticalOffset = 15f;
    [SerializeField] private bool enableZombieBob = true;
    [SerializeField] private float bobHeight = 3f;
    [SerializeField] private float bobSpeed = 6f;

    [Header("Progress")]
    [SerializeField] private float progressSmoothSpeed = 0.8f;
    [SerializeField] private bool showDebugLogs;

    private string nextSceneName;
    private AsyncOperation asyncLoad;
    private float displayedProgress;
    private float loadStartTime;
    private int lastDisplayedPercent = -1;
    private int runningParameterHash;
    private bool isLoading;
    private bool isCompleting;

    private void Awake()
    {
        // If MainMenuUIController (or the app's boot flow) routed us here through
        // SceneLoadData, that destination wins. Otherwise this is a cold boot straight
        // into LoadingScene (build index 0), so fall back to defaultSceneName (MainMenu).
        // Cleared immediately after reading so a later direct visit to LoadingScene
        // doesn't accidentally reuse a stale value.
        nextSceneName = !string.IsNullOrEmpty(SceneLoadData.NextSceneName)
            ? SceneLoadData.NextSceneName
            : defaultSceneName;
        SceneLoadData.NextSceneName = null;

        ValidateReferences();

        if (!string.IsNullOrEmpty(runningParameterName))
        {
            runningParameterHash = Animator.StringToHash(runningParameterName);
        }

        SetProgressImmediate(0f);
        SetZombieRunning(true);
    }

    private void Start()
    {
        if (loadOnStart)
        {
            StartLoading();
        }
    }

    private void ValidateReferences()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogError("[LoadingSceneController] nextSceneName is empty — there is nothing to load into.");
        }

        if (loadingText == null)
        {
            Debug.LogWarning("[LoadingSceneController] loadingText is not assigned — the percentage label won't update, but loading will still proceed.");
        }

        if (loadingFillImage == null && loadingSlider == null)
        {
            Debug.LogWarning("[LoadingSceneController] Neither loadingFillImage nor loadingSlider is assigned — the bar won't visually fill, but loading will still proceed.");
        }

        if (zombieRect == null)
        {
            Debug.LogWarning("[LoadingSceneController] zombieRect is not assigned — the zombie runner won't move, but loading will still proceed.");
        }
        else if (loadingBarLeftPoint == null || loadingBarRightPoint == null)
        {
            Debug.LogWarning("[LoadingSceneController] loadingBarLeftPoint/loadingBarRightPoint is not assigned — the zombie runner can't be positioned, but loading will still proceed.");
        }

        if (zombieRect != null && zombieAnimator == null)
        {
            Debug.LogWarning("[LoadingSceneController] zombieAnimator is not assigned — the zombie will move but won't play a run animation.");
        }
    }

    /// <summary>Call to (re)start the load. No-op if a load is already in progress.</summary>
    public void StartLoading()
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        StartCoroutine(LoadSceneRoutine());
    }

    private IEnumerator LoadSceneRoutine()
    {
        loadStartTime = Time.unscaledTime;

        AsyncOperation operation = SceneManager.LoadSceneAsync(nextSceneName);

        if (operation == null)
        {
            Debug.LogError($"[LoadingSceneController] Scene '{nextSceneName}' could not be loaded. Make sure it is added and enabled in File > Build Settings > Scenes In Build.");
            isLoading = false;
            yield break;
        }

        asyncLoad = operation;
        asyncLoad.allowSceneActivation = false;

        // asyncLoad.progress reaches 0.9 almost instantly for a small scene in the Editor,
        // so targetProgress hits 1 within a few frames. Capping the display speed to
        // "at most a full 0-1 sweep across minimumLoadingTime" keeps the counter climbing
        // steadily for the whole wait instead of racing to 100% and then sitting idle.
        // If the real load is slower than minimumLoadingTime, targetProgress itself is
        // the limiting factor (MoveTowards can't run ahead of it), so this never shows
        // progress the scene hasn't actually reached.
        float paceCap = 1f / Mathf.Max(minimumLoadingTime, 0.01f);
        float effectiveSmoothSpeed = Mathf.Min(progressSmoothSpeed, paceCap);

        while (!isCompleting)
        {
            float targetProgress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            displayedProgress = Mathf.MoveTowards(displayedProgress, targetProgress, effectiveSmoothSpeed * Time.unscaledDeltaTime);

            UpdateProgressVisuals(displayedProgress);

            bool sceneReady = asyncLoad.progress >= 0.9f;
            bool minimumTimeElapsed = Time.unscaledTime - loadStartTime >= minimumLoadingTime;
            bool visuallyComplete = displayedProgress >= 0.999f;

            if (sceneReady && minimumTimeElapsed && visuallyComplete)
            {
                isCompleting = true;
            }
            else
            {
                yield return null;
            }
        }

        displayedProgress = 1f;
        UpdateProgressVisuals(1f);
        SetZombieRunning(false);

        if (showDebugLogs)
        {
            Debug.Log($"[LoadingSceneController] Load of '{nextSceneName}' complete at {Time.unscaledTime - loadStartTime:0.00}s. Activating after a {completionDelay:0.00}s delay.");
        }

        yield return new WaitForSecondsRealtime(completionDelay);

        asyncLoad.allowSceneActivation = true;
    }

    private void UpdateProgressVisuals(float progress)
    {
        if (loadingFillImage != null)
        {
            loadingFillImage.fillAmount = progress;
        }

        if (loadingSlider != null)
        {
            loadingSlider.value = progress;
        }

        int percent = Mathf.RoundToInt(progress * 100f);

        if (loadingText != null && percent != lastDisplayedPercent)
        {
            loadingText.text = percent + "%";
            lastDisplayedPercent = percent;
        }

        UpdateZombiePosition(progress);
    }

    private void UpdateZombiePosition(float progress)
    {
        if (zombieRect == null || loadingBarLeftPoint == null || loadingBarRightPoint == null)
        {
            return;
        }

        Vector3 startPosition = loadingBarLeftPoint.position;
        Vector3 endPosition = loadingBarRightPoint.position;
        Vector3 zombiePosition = Vector3.Lerp(startPosition, endPosition, progress);

        // Bob only while still moving — a stopped zombie sitting flush at the end of
        // the bar reads as "arrived", a stopped zombie still bobbing reads as "stuck".
        float bobOffset = (enableZombieBob && progress < 1f)
            ? Mathf.Sin(Time.unscaledTime * bobSpeed) * bobHeight
            : 0f;

        zombiePosition.y += zombieVerticalOffset + bobOffset;

        zombieRect.position = zombiePosition;
    }

    private void SetZombieRunning(bool isRunning)
    {
        if (zombieAnimator == null)
        {
            return;
        }

        if (runningParameterIsBool)
        {
            zombieAnimator.SetBool(runningParameterHash, isRunning);
        }
        else
        {
            zombieAnimator.SetFloat(runningParameterHash, isRunning ? 1f : 0f);
        }
    }

    private void SetProgressImmediate(float progress)
    {
        displayedProgress = Mathf.Clamp01(progress);
        UpdateProgressVisuals(displayedProgress);
    }
}
