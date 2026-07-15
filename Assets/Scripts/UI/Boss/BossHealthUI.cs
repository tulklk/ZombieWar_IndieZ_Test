using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BossHealthPanel: binds to a ZombieHealth once (via Bind, called by BossFightManager) and
/// updates purely from its HealthChanged/Died events — never polls/finds the boss per frame.
/// The fill bar itself smooths toward the latest value in Update (MoveTowards, not Lerp, so
/// it settles exactly rather than asymptoting forever), and the value text is only rewritten
/// when the displayed integers actually change (no per-frame string allocation).
/// </summary>
public class BossHealthUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image healthFill;
    [SerializeField] private TMP_Text bossNameText;
    [SerializeField] private TMP_Text healthValueText;
    [SerializeField] private TMP_Text bossPhaseText;
    [SerializeField] private float fillSmoothSpeed = 5f;
    [SerializeField] private Gradient healthGradient;
    [Tooltip("WaveHUD (Phase/Zombies-remaining counter) — hidden the moment this panel shows, since it'd otherwise overlap the same top area of the screen and no longer means anything once the Boss fight has started. Left hidden even after this panel hides (the level is ending either way at that point). Leave empty to skip this entirely.")]
    [SerializeField] private GameObject waveHudRoot;

    [Header("Fade")]
    [SerializeField] private float fadeDuration = 0.35f;

    [Header("Boss Phase (optional)")]
    [SerializeField] private bool enableBossPhases = false;
    [SerializeField, Range(0f, 1f)] private float phase2Threshold = 0.5f;

    private ZombieHealth boundBossHealth;
    private float targetFillAmount = 1f;
    private int lastDisplayedCurrent = -1;
    private int lastDisplayedMax = -1;
    private int currentPhase = 1;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        // Deliberately does NOT call root.SetActive(false) here — root starts inactive in
        // the scene already (set once by BossFightSetup), and since Awake() only runs the
        // FIRST time an object is activated, deactivating root from within its own Awake()
        // would fire as a synchronous side effect of Show()'s root.SetActive(true) call,
        // flipping it back off before Show()'s own FadeTo(1f, ...) could ever run.
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    private void Update()
    {
        if (healthFill == null || Mathf.Approximately(healthFill.fillAmount, targetFillAmount))
        {
            return;
        }

        healthFill.fillAmount = Mathf.MoveTowards(healthFill.fillAmount, targetFillAmount, Time.unscaledDeltaTime * fillSmoothSpeed);

        if (healthGradient != null)
        {
            healthFill.color = healthGradient.Evaluate(healthFill.fillAmount);
        }
    }

    /// <summary>Call once when the boss is assigned (e.g. BossFightManager.Awake) — safe to call again if the boss reference ever changes.</summary>
    public void Bind(ZombieHealth bossHealth, string bossDisplayName)
    {
        Unbind();

        boundBossHealth = bossHealth;

        if (boundBossHealth != null)
        {
            boundBossHealth.HealthChanged += HandleHealthChanged;
            boundBossHealth.Died += HandleBossDied;
        }

        if (bossNameText != null)
        {
            bossNameText.text = string.IsNullOrEmpty(bossDisplayName) ? "BOSS" : bossDisplayName.ToUpperInvariant();
        }

        currentPhase = 1;
        UpdatePhaseText();

        if (boundBossHealth != null)
        {
            ApplyHealthValues(boundBossHealth.CurrentHealth, boundBossHealth.MaxHealth, instant: true);
        }
    }

    public void Show()
    {
        if (root == null)
        {
            Debug.LogWarning("[BossHealthUI] root not assigned — boss fight continues without a visible health bar.");
            return;
        }

        root.SetActive(true);
        FadeTo(1f, null);

        if (waveHudRoot != null)
        {
            waveHudRoot.SetActive(false);
        }
    }

    public void Hide()
    {
        // Already inactive (never shown yet, or already hidden by an earlier Hide() call)
        // means already hidden — nothing to fade, and starting a coroutine on an inactive
        // GameObject would just log a warning and no-op anyway.
        if (root == null || !root.activeInHierarchy)
        {
            return;
        }

        FadeTo(0f, () => root.SetActive(false));
    }

    private void HandleHealthChanged(int current, int max)
    {
        ApplyHealthValues(current, max, instant: false);
    }

    private void HandleBossDied(ZombieHealth _)
    {
        Hide();
    }

    private void ApplyHealthValues(int current, int max, bool instant)
    {
        if (max <= 0)
        {
            max = 1;
        }

        targetFillAmount = (float)current / max;

        if (instant && healthFill != null)
        {
            healthFill.fillAmount = targetFillAmount;

            if (healthGradient != null)
            {
                healthFill.color = healthGradient.Evaluate(targetFillAmount);
            }
        }

        if (current != lastDisplayedCurrent || max != lastDisplayedMax)
        {
            lastDisplayedCurrent = current;
            lastDisplayedMax = max;

            if (healthValueText != null)
            {
                healthValueText.text = $"{current} / {max}";
            }
        }

        if (enableBossPhases)
        {
            int newPhase = ((float)current / max) <= phase2Threshold ? 2 : 1;

            if (newPhase != currentPhase)
            {
                currentPhase = newPhase;
                UpdatePhaseText();
            }
        }
    }

    private void UpdatePhaseText()
    {
        if (bossPhaseText == null)
        {
            return;
        }

        if (!enableBossPhases)
        {
            bossPhaseText.gameObject.SetActive(false);
            return;
        }

        bossPhaseText.gameObject.SetActive(true);
        bossPhaseText.text = $"PHASE {currentPhase}";
    }

    private void FadeTo(float target, Action onComplete)
    {
        if (canvasGroup == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        fadeRoutine = StartCoroutine(FadeRoutine(target, onComplete));
    }

    private IEnumerator FadeRoutine(float target, Action onComplete)
    {
        float start = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        canvasGroup.alpha = target;
        fadeRoutine = null;
        onComplete?.Invoke();
    }

    private void Unbind()
    {
        if (boundBossHealth != null)
        {
            boundBossHealth.HealthChanged -= HandleHealthChanged;
            boundBossHealth.Died -= HandleBossDied;
            boundBossHealth = null;
        }
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void OnDestroy()
    {
        Unbind();
    }
}
