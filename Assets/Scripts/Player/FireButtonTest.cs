using UnityEngine;
using UnityEngine.EventSystems;

public class FireButtonTest : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private WeaponController weaponController;

    private bool isHolding;

    private void Update()
    {
        if (isHolding && weaponController != null)
        {
            weaponController.TryShoot();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isHolding = true;

        if (weaponController != null)
        {
            weaponController.StartShooting();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHolding = false;

        if (weaponController != null)
        {
            weaponController.StopShooting();
        }
    }
}
