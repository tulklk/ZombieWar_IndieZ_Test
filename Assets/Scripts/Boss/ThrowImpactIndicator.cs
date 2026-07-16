using System.Collections;
using UnityEngine;

/// <summary>
/// Ground telegraph shown at a predicted throw-impact position, giving the Player a visible
/// warning for the whole windup duration before the projectile actually lands. Fades a flat
/// SpriteRenderer circle in (and/or plays a ParticleSystem — either is optional) rather than
/// snapping instantly to full opacity, so the warning reads as building urgency instead of a
/// jarring flash. Destroyed by its spawner (BigBossPhaseController) shortly after the throw.
/// </summary>
public class ThrowImpactIndicator : MonoBehaviour
{
    [SerializeField] private SpriteRenderer indicatorRenderer;
    [SerializeField] private ParticleSystem indicatorParticles;

    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (indicatorRenderer != null)
        {
            Color startColor = indicatorRenderer.color;
            startColor.a = 0f;
            indicatorRenderer.color = startColor;
        }
    }

    public void PlayFadeIn(float duration)
    {
        if (indicatorParticles != null)
        {
            indicatorParticles.Play();
        }

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
        }

        fadeRoutine = StartCoroutine(FadeInRoutine(Mathf.Max(duration, 0.05f)));
    }

    private IEnumerator FadeInRoutine(float duration)
    {
        if (indicatorRenderer == null)
        {
            yield break;
        }

        Color targetColor = indicatorRenderer.color;
        targetColor.a = Mathf.Max(targetColor.a, 0.6f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Color color = targetColor;
            color.a = Mathf.Lerp(0f, targetColor.a, Mathf.Clamp01(elapsed / duration));
            indicatorRenderer.color = color;
            yield return null;
        }

        indicatorRenderer.color = targetColor;
        fadeRoutine = null;
    }
}
