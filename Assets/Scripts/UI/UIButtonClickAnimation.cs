using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Tactile press/release feedback for any UI Button: a quick squash on press, then a
/// single smooth overshoot-and-settle back to normal size on release — a chunky,
/// mechanical "click" feel matching the game's metal/rusted button art. Runs on
/// unscaled time so it still plays while a menu has Time.timeScale at 0.
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonClickAnimation : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("Press")]
    [SerializeField] private float pressedScale = 0.92f;
    [SerializeField] private float pressDuration = 0.08f;

    [Header("Release")]
    [SerializeField] private float releaseDuration = 0.3f;
    [Tooltip("How far the scale overshoots past normal size before settling — DOTween's default OutBack overshoot is ~1.7.")]
    [SerializeField] private float releaseOvershoot = 1.3f;

    private RectTransform rectTransform;
    private Vector3 baseScale;
    private Tween activeTween;

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        baseScale = rectTransform.localScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        activeTween?.Kill();
        activeTween = rectTransform
            .DOScale(baseScale * pressedScale, pressDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        activeTween?.Kill();

        // A single DOScale eased with OutBack overshoots past baseScale and settles in
        // one continuous motion — smoother than snapping to baseScale and layering a
        // separate vibrato-based punch on top, which read as two disconnected steps.
        activeTween = rectTransform
            .DOScale(baseScale, releaseDuration)
            .SetEase(Ease.OutBack, releaseOvershoot)
            .SetUpdate(true);
    }

    private void OnDisable()
    {
        activeTween?.Kill();

        if (rectTransform != null)
        {
            rectTransform.localScale = baseScale;
        }
    }
}
