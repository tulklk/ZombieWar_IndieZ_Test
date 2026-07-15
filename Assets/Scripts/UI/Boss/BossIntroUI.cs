using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// BossIntroPanel: fades in + slides in from the left, BossNameText punches in from a
/// slightly oversized scale, holds (BossFightManager controls the hold duration — this
/// class doesn't have an opinion on how long to stay up), then fades back out on Hide().
/// Pure Coroutine + Time.unscaledDeltaTime (no DOTween — not installed in this project).
/// All text content is passed in via ShowBossInfo(...) from whatever's actually on the
/// boss's ZombieAI/ZombieHealth/ZombieData — never hard-coded here.
/// </summary>
public class BossIntroUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TMP_Text bossNameText;
    [SerializeField] private TMP_Text bossTypeText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text damageText;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text attackRangeText;
    [SerializeField] private TMP_Text warningText;

    [Header("Animation")]
    [SerializeField] private float slideDistance = 300f;
    [SerializeField] private float fadeInDuration = 0.4f;
    [SerializeField] private float fadeOutDuration = 0.35f;
    [SerializeField] private float nameStartScale = 1.2f;

    private Coroutine activeRoutine;
    private Vector2 restingAnchoredPosition;
    private bool hasCachedRestingPosition;

    private void Awake()
    {
        if (panelRect != null && !hasCachedRestingPosition)
        {
            restingAnchoredPosition = panelRect.anchoredPosition;
            hasCachedRestingPosition = true;
        }

        // Deliberately does NOT call root.SetActive(false) here — root starts inactive in
        // the scene already (set once by BossFightSetup), and since Awake() only runs the
        // FIRST time an object is activated, deactivating root from within its own Awake()
        // would fire as a synchronous side effect of ShowBossInfo()'s root.SetActive(true)
        // call, flipping it back off before the very next line (StartCoroutine) could run.
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    public void ShowBossInfo(string bossName, string bossType, int maxHealth, int damage, float speed, float attackRange)
    {
        if (root == null || canvasGroup == null)
        {
            Debug.LogWarning("[BossIntroUI] root/canvasGroup not assigned — the boss fight continues, just without a visible intro panel.");
            return;
        }

        SetText(bossNameText, string.IsNullOrEmpty(bossName) ? "BOSS ZOMBIE" : bossName.ToUpperInvariant());
        SetText(bossTypeText, bossType);
        SetText(healthText, $"HP: {maxHealth}");
        SetText(damageText, $"DAMAGE: {damage}");
        SetText(speedText, $"SPEED: {speed:0.0}");
        SetText(attackRangeText, $"ATTACK RANGE: {attackRange:0.0}");
        SetText(warningText, "WARNING:\nELIMINATE THE BOSS");

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
        }

        root.SetActive(true);
        activeRoutine = StartCoroutine(ShowRoutine());
    }

    public void Hide()
    {
        // Already inactive (e.g. the intro reveal ended long ago and combat is already
        // underway) means already hidden — nothing to fade, and starting a coroutine on an
        // inactive GameObject would just log a warning and no-op anyway.
        if (root == null || canvasGroup == null || !root.activeInHierarchy)
        {
            return;
        }

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
        }

        activeRoutine = StartCoroutine(FadeOutRoutine());
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }

    private IEnumerator ShowRoutine()
    {
        Vector2 offscreenPosition = restingAnchoredPosition + new Vector2(-slideDistance, 0f);

        if (panelRect != null)
        {
            panelRect.anchoredPosition = offscreenPosition;
        }

        if (bossNameText != null)
        {
            bossNameText.rectTransform.localScale = Vector3.one * nameStartScale;
        }

        canvasGroup.alpha = 0f;
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOutCubic(Mathf.Clamp01(elapsed / fadeInDuration));

            canvasGroup.alpha = t;

            if (panelRect != null)
            {
                panelRect.anchoredPosition = Vector2.Lerp(offscreenPosition, restingAnchoredPosition, t);
            }

            if (bossNameText != null)
            {
                bossNameText.rectTransform.localScale = Vector3.one * Mathf.Lerp(nameStartScale, 1f, t);
            }

            yield return null;
        }

        canvasGroup.alpha = 1f;

        if (panelRect != null)
        {
            panelRect.anchoredPosition = restingAnchoredPosition;
        }

        if (bossNameText != null)
        {
            bossNameText.rectTransform.localScale = Vector3.one;
        }

        activeRoutine = null;
    }

    private IEnumerator FadeOutRoutine()
    {
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / fadeOutDuration));
            yield return null;
        }

        canvasGroup.alpha = 0f;

        if (root != null)
        {
            root.SetActive(false);
        }

        activeRoutine = null;
    }

    private static float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }
}
