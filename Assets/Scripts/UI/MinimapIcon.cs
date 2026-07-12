using UnityEngine;

/// <summary>
/// Drop this on any world object that should show up on the minimap (a Zombie, an
/// objective, an exit...). It self-registers with MinimapController, which then keeps
/// "marker" positioned/rotated to match this object's world position every frame — the
/// same way the Player marker already works. Nothing on ZombieAI/PlayerHealth needs to
/// change to use this later.
/// </summary>
public class MinimapIcon : MonoBehaviour
{
    [SerializeField] private RectTransform marker;
    [SerializeField] private bool rotateWithTransform;

    public RectTransform Marker => marker;
    public bool RotateWithTransform => rotateWithTransform;
    public Transform WorldTransform => transform;

    private void OnEnable()
    {
        MinimapController.Instance?.RegisterIcon(this);
    }

    private void OnDisable()
    {
        MinimapController.Instance?.UnregisterIcon(this);
    }
}
