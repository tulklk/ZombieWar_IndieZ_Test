using UnityEngine;
using UnityEngine.EventSystems;

public class BombButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private PlayerBombController playerBombController;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (playerBombController != null)
        {
            playerBombController.BeginAim();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (playerBombController != null)
        {
            playerBombController.TryThrowBomb();
        }
    }

    private void OnDisable()
    {
        if (playerBombController != null)
        {
            playerBombController.CancelAim();
        }
    }
}
