using UnityEngine;

public class BombButton : MonoBehaviour
{
    [SerializeField] private PlayerBombController playerBombController;

    public void OnBombButtonPressed()
    {
        if (playerBombController != null)
        {
            playerBombController.TryThrowBomb();
        }
    }
}
