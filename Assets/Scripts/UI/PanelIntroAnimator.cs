using System.Collections;
using UnityEngine;

/// <summary>
/// Staggered reveal for a result panel's title + supporting elements (buttons, a trophy
/// icon, etc.): the title either punches in from an oversized scale (stamp effect, e.g.
/// LosePanel's bloody GAME OVER art) or slides straight down from above (e.g. WinPanel's
/// VICTORY banner), then each staggerElement slides up + fades in one after another. Runs
/// on unscaled time so it still plays out even though the panel is shown right as gameplay
/// pauses (Time.timeScale = 0). Purely optional — LevelResultManager just no-ops if a given
/// panel doesn't have this component.
/// </summary>
public class PanelIntroAnimator : MonoBehaviour
{
    public enum TitleAnimationStyle
    {
        PunchScale,
        SlideFromTop
    }

    [SerializeField] private RectTransform titleRect;
    [Tooltip("Any RectTransform to reveal in a staggered sequence after the title — buttons, a trophy icon, etc.")]
    [SerializeField] private RectTransform[] staggerElements;

    [Header("Title")]
    [SerializeField] private TitleAnimationStyle titleAnimationStyle = TitleAnimationStyle.PunchScale;
    [Tooltip("PunchScale only: how oversized the title starts before settling to normal size.")]
    [SerializeField] private float titleStartScale = 1.6f;
    [Tooltip("SlideFromTop only: how far above its resting position the title starts.")]
    [SerializeField] private float titleSlideDistance = 250f;
    [SerializeField] private float titleDuration = 0.4f;

    [Header("Stagger Elements (slide up + fade)")]
    [SerializeField] private float elementSlideDistance = 60f;
    [SerializeField] private float elementDuration = 0.3f;
    [SerializeField] private float staggerDelay = 0.12f;
    [SerializeField] private float elementsStartDelay = 0.15f;

    private Coroutine introRoutine;

    public void PlayIntro()
    {
        if (introRoutine != null)
        {
            StopCoroutine(introRoutine);
        }

        introRoutine = StartCoroutine(IntroRoutine());
    }

    private IEnumerator IntroRoutine()
    {
        if (titleRect != null)
        {
            if (titleAnimationStyle == TitleAnimationStyle.SlideFromTop)
            {
                yield return SlideAndFadeIn(titleRect, titleSlideDistance, titleDuration, fromAbove: true);
            }
            else
            {
                yield return PunchScale(titleRect, titleStartScale, titleDuration);
            }
        }

        if (staggerElements != null && staggerElements.Length > 0)
        {
            yield return new WaitForSecondsRealtime(elementsStartDelay);

            for (int i = 0; i < staggerElements.Length; i++)
            {
                if (staggerElements[i] == null)
                {
                    continue;
                }

                StartCoroutine(SlideAndFadeIn(staggerElements[i], elementSlideDistance, elementDuration, fromAbove: false));
                yield return new WaitForSecondsRealtime(staggerDelay);
            }
        }

        introRoutine = null;
    }

    private static IEnumerator PunchScale(RectTransform rect, float fromScale, float duration)
    {
        rect.localScale = Vector3.one * fromScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
            rect.localScale = Vector3.one * Mathf.LerpUnclamped(fromScale, 1f, t);
            yield return null;
        }

        rect.localScale = Vector3.one;
    }

    /// <summary>fromAbove flips the slide direction: buttons/icons slide up into place from below, while a SlideFromTop title instead drops down from above.</summary>
    private static IEnumerator SlideAndFadeIn(RectTransform rect, float distance, float duration, bool fromAbove)
    {
        CanvasGroup group = rect.GetComponent<CanvasGroup>();

        if (group == null)
        {
            group = rect.gameObject.AddComponent<CanvasGroup>();
        }

        Vector2 targetPosition = rect.anchoredPosition;
        Vector2 startPosition = targetPosition + new Vector2(0f, fromAbove ? distance : -distance);
        rect.anchoredPosition = startPosition;
        group.alpha = 0f;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOutCubic(Mathf.Clamp01(elapsed / duration));
            rect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, t);
            group.alpha = t;
            yield return null;
        }

        rect.anchoredPosition = targetPosition;
        group.alpha = 1f;
    }

    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.70158f;
        const float c3 = overshoot + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + overshoot * Mathf.Pow(t - 1f, 2f);
    }

    private static float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }
}
