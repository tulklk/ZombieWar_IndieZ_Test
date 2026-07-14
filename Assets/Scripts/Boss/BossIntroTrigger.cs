using UnityEngine;

/// <summary>
/// Thin trigger wrapper — all it does is call bossFightManager.StartBossIntro() the first
/// (and, with triggerOnce, only) time the Player enters. No cutscene/camera/UI logic lives
/// here at all; that's entirely BossFightManager's job.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BossIntroTrigger : MonoBehaviour
{
    [SerializeField] private BossFightManager bossFightManager;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool triggerOnce = true;

    private bool hasFired;
    private BoxCollider triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<BoxCollider>();

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasFired && triggerOnce)
        {
            return;
        }

        if (!other.CompareTag(playerTag))
        {
            return;
        }

        if (bossFightManager == null)
        {
            Debug.LogWarning("[BossIntroTrigger] bossFightManager not assigned — cannot start the boss intro.", this);
            return;
        }

        hasFired = true;

        if (triggerOnce && triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }

        bossFightManager.StartBossIntro();
    }
}
