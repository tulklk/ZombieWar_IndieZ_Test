using UnityEngine;

/// <summary>
/// Simple pause/resume flow: PausePanel is shown/hidden via Time.timeScale (same mechanism
/// Win/Lose already use), and Player input is locked the same way BossFightManager locks it
/// during the boss intro (WeaponController.SetActionLocked/PlayerBombController.SetInputLocked
/// — movement freezes as a side effect of the weapon lock, no separate flag needed). Refuses
/// to open once the level has already ended (LevelResultManager.IsRunning is false), so the
/// Pause button can't awkwardly reopen over an already-showing Win/Lose panel.
/// </summary>
public class PauseManager : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private CanvasGroup pauseCanvasGroup;

    [Header("Player Lock")]
    [Tooltip("Auto-found via the \"Player\" tag if left empty.")]
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private PlayerBombController bombController;

    [Header("Level Result (optional — blocks Pause once Win/Lose has already shown)")]
    [SerializeField] private LevelResultManager levelResultManager;

    public bool IsPaused { get; private set; }

    private void Awake()
    {
        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        if (weaponController == null || bombController == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                if (weaponController == null)
                {
                    weaponController = player.GetComponent<WeaponController>();
                }

                if (bombController == null)
                {
                    bombController = player.GetComponent<PlayerBombController>();
                }
            }
        }
    }

    /// <summary>Wired to the HUD Pause button.</summary>
    public void TogglePause()
    {
        if (IsPaused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        if (levelResultManager != null && !levelResultManager.IsRunning)
        {
            return;
        }

        IsPaused = true;
        Time.timeScale = 0f;
        SetPlayerInputLocked(true);

        if (pausePanel != null)
        {
            pausePanel.SetActive(true);
        }

        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.alpha = 1f;
        }
    }

    /// <summary>Wired to PausePanel's Resume button.</summary>
    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        Time.timeScale = 1f;
        SetPlayerInputLocked(false);

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }
    }

    private void SetPlayerInputLocked(bool locked)
    {
        weaponController?.SetActionLocked(locked);
        bombController?.SetInputLocked(locked);
    }

    /// <summary>Wired to PausePanel's Restart button — reuses LevelResultManager's own scene-name-aware restart instead of duplicating that logic.</summary>
    public void RestartLevel()
    {
        Time.timeScale = 1f;

        if (levelResultManager != null)
        {
            levelResultManager.RestartLevel();
        }
    }

    /// <summary>Wired to PausePanel's Main Menu button.</summary>
    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;

        if (levelResultManager != null)
        {
            levelResultManager.ReturnToMainMenu();
        }
    }

    private void OnDestroy()
    {
        if (IsPaused)
        {
            Time.timeScale = 1f;
        }
    }
}
