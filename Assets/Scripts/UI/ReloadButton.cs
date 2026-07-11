using UnityEngine;

public class ReloadButton : MonoBehaviour
{
    [SerializeField] private WeaponController weaponController;

    public void OnReloadButtonPressed()
    {
        if (weaponController != null)
        {
            weaponController.TryReload();
        }
    }
}
