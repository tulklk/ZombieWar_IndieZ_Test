using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen red flash — one pulse per PlayerHealth.OnDamaged, so N hits blink N times.
/// Pure alpha fade of a fixed red color only, no desaturation — kept deliberately separate
/// from LowHealthScreenEffect (which handles the persistent low-health gray/vignette look).
/// </summary>
[RequireComponent(typeof(Image))]
public class HitFlashOverlay : MonoBehaviour
{
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Image targetImage;

    [Header("Flash")]
    [SerializeField, Range(0f, 1f)] private float flashAlpha = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.25f;

    [Header("Low Health Handoff")]
    [Tooltip("At or below this health fraction, LowHealthScreenEffect's persistent vignette already covers it — skip flashing so the two effects don't stack.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.1f;

    private Coroutine flashCoroutine;

    private void Awake()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        SetAlpha(0f);
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDamaged += HandleDamaged;
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDamaged -= HandleDamaged;
        }
    }

    private void HandleDamaged(int damage)
    {
        if (playerHealth != null && playerHealth.MaxHealth > 0f)
        {
            float healthFraction = playerHealth.CurrentHealth / playerHealth.MaxHealth;

            if (healthFraction <= lowHealthThreshold)
            {
                return;
            }
        }

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    // Restarting (not overlapping) on every hit is what makes rapid hits read as a
    // distinct blink per attack instead of one smeared, ever-brightening glow.
    private IEnumerator FlashRoutine()
    {
        SetAlpha(flashAlpha);

        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(flashAlpha, 0f, elapsed / fadeOutDuration));
            yield return null;
        }

        SetAlpha(0f);
        flashCoroutine = null;
    }

    private void SetAlpha(float alpha)
    {
        if (targetImage == null)
        {
            return;
        }

        Color color = targetImage.color;
        color.r = 1f;
        color.g = 0f;
        color.b = 0f;
        color.a = alpha;
        targetImage.color = color;
    }
}
