using UnityEngine;
using UnityEngine.EventSystems;

public class FireButtonTest : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private PlayerAnimationController animationController;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (animationController != null)
        {
            animationController.SetShooting(true);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (animationController != null)
        {
            animationController.SetShooting(false);
        }
    }
}