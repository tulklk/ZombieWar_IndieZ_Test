using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A gate blocking the corridor between two wave zones. Closed = a solid Collider blocks
/// the Player (and, if a NavMeshObstacle is assigned, carves zombie NavMesh paths too);
/// Open = the Collider disables after disableDelayAfterOpen so an open/close animation
/// has time to finish before the path is actually walkable. NavMesh is never rebaked —
/// obstacle carving is the only supported way to change zombie pathing at runtime.
/// </summary>
public class WaveBarrier : MonoBehaviour
{
    [Header("Visual & Collision")]
    [SerializeField] private GameObject barrierVisual;
    [SerializeField] private Collider blockingCollider;
    [SerializeField] private NavMeshObstacle navMeshObstacle;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string openTriggerName = "Open";
    [SerializeField] private string closeTriggerName = "Close";

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    [Header("Settings")]
    [SerializeField] private bool startClosed = true;
    [SerializeField] private float disableDelayAfterOpen = 0.5f;

    private int openTriggerHash;
    private int closeTriggerHash;
    private Coroutine openRoutine;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (!string.IsNullOrEmpty(openTriggerName))
        {
            openTriggerHash = Animator.StringToHash(openTriggerName);
        }

        if (!string.IsNullOrEmpty(closeTriggerName))
        {
            closeTriggerHash = Animator.StringToHash(closeTriggerName);
        }

        // Applied instantly (no animation/audio/coroutine) — this establishes the scene's
        // starting state, not a runtime open/close event.
        IsOpen = !startClosed;

        if (barrierVisual != null)
        {
            barrierVisual.SetActive(true);
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = startClosed;
        }

        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = startClosed;
        }
    }

    public void SetBarrierState(bool isOpen)
    {
        if (isOpen)
        {
            OpenBarrier();
        }
        else
        {
            CloseBarrier();
        }
    }

    public void CloseBarrier()
    {
        if (openRoutine != null)
        {
            StopCoroutine(openRoutine);
            openRoutine = null;
        }

        IsOpen = false;

        if (barrierVisual != null)
        {
            barrierVisual.SetActive(true);
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = true;
        }

        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = true;
        }

        if (animator != null && !string.IsNullOrEmpty(closeTriggerName))
        {
            animator.SetTrigger(closeTriggerHash);
        }

        PlayClip(closeClip);
    }

    public void OpenBarrier()
    {
        if (openRoutine != null)
        {
            StopCoroutine(openRoutine);
        }

        IsOpen = true;

        if (animator != null && !string.IsNullOrEmpty(openTriggerName))
        {
            animator.SetTrigger(openTriggerHash);
        }

        PlayClip(openClip);

        if (navMeshObstacle != null)
        {
            navMeshObstacle.enabled = false;
        }

        openRoutine = StartCoroutine(DisableCollisionAfterDelay());
    }

    private IEnumerator DisableCollisionAfterDelay()
    {
        // Real-time so a paused/slowed game doesn't stall the gate mid-open.
        yield return new WaitForSecondsRealtime(disableDelayAfterOpen);

        if (blockingCollider != null)
        {
            blockingCollider.enabled = false;
        }

        openRoutine = null;
    }

    private void PlayClip(AudioClip clip)
    {
        if (audioSource == null || clip == null)
        {
            return;
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (sfxEnabled)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = blockingCollider != null ? blockingCollider : GetComponent<Collider>();

        if (col == null)
        {
            return;
        }

        bool open = Application.isPlaying ? IsOpen : !startClosed;
        Gizmos.color = open ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
    }
}
