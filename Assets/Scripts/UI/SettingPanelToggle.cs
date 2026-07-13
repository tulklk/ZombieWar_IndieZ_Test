using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Animates SettingPanel open/closed with a pop scale + fade. The panel GameObject
/// stays active at all times — visibility/interactivity is driven by a CanvasGroup
/// (alpha + interactable + blocksRaycasts) so it never intercepts clicks on PlayBtn
/// underneath while closed, and can still be tweened smoothly on open/close.
///
/// Does NOT wire its own Button.onClick — MainMenuUIController owns SettingBtn's
/// click so it can coordinate mutual exclusion with LevelSelectPanel (only one of
/// the two panels may be open at a time) before calling Open()/Close() here.
/// </summary>
[RequireComponent(typeof(Button))]
public class SettingPanelToggle : MonoBehaviour
{
    [SerializeField] private RectTransform settingPanel;
    [SerializeField] private CanvasGroup settingPanelCanvasGroup;
    [SerializeField] private float animationDuration = 0.25f;
    [SerializeField] private Vector3 hiddenScale = new Vector3(0.85f, 0.85f, 1f);

    private bool isOpen;
    private Tween activeTween;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (settingPanelCanvasGroup == null && settingPanel != null)
        {
            settingPanelCanvasGroup = settingPanel.GetComponent<CanvasGroup>();
        }

        SetImmediateState(isOpen: false);
    }

    public void Toggle()
    {
        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        if (isOpen || settingPanel == null)
        {
            return;
        }

        isOpen = true;
        activeTween?.Kill();

        settingPanel.gameObject.SetActive(true);

        if (settingPanelCanvasGroup != null)
        {
            settingPanelCanvasGroup.interactable = true;
            settingPanelCanvasGroup.blocksRaycasts = true;
        }

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(settingPanel.DOScale(Vector3.one, animationDuration).SetEase(Ease.OutBack));

        if (settingPanelCanvasGroup != null)
        {
            sequence.Join(settingPanelCanvasGroup.DOFade(1f, animationDuration));
        }

        activeTween = sequence;
    }

    public void Close()
    {
        if (!isOpen || settingPanel == null)
        {
            return;
        }

        isOpen = false;
        activeTween?.Kill();

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(settingPanel.DOScale(hiddenScale, animationDuration).SetEase(Ease.InBack));

        if (settingPanelCanvasGroup != null)
        {
            sequence.Join(settingPanelCanvasGroup.DOFade(0f, animationDuration));
        }

        sequence.OnComplete(() =>
        {
            if (settingPanelCanvasGroup != null)
            {
                settingPanelCanvasGroup.interactable = false;
                settingPanelCanvasGroup.blocksRaycasts = false;
            }
        });

        activeTween = sequence;
    }

    private void SetImmediateState(bool isOpen)
    {
        this.isOpen = isOpen;

        if (settingPanel == null)
        {
            return;
        }

        settingPanel.gameObject.SetActive(true);
        settingPanel.localScale = isOpen ? Vector3.one : hiddenScale;

        if (settingPanelCanvasGroup != null)
        {
            settingPanelCanvasGroup.alpha = isOpen ? 1f : 0f;
            settingPanelCanvasGroup.interactable = isOpen;
            settingPanelCanvasGroup.blocksRaycasts = isOpen;
        }
    }
}
