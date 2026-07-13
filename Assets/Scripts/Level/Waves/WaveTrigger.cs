using UnityEngine;

/// <summary>
/// Player-only entrance trigger for one wave zone. Fires at most once (disables itself
/// immediately on the first valid entry) and only hands off to
/// ZombieWaveManager.TryStartWave — it never spawns zombies or touches its zone's barrier
/// directly. The project has no dedicated "Player" physics layer (the Player GameObject
/// sits on Default), so tag-checking is the correct approach here rather than a layer mask.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaveTrigger : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [SerializeField] private bool startEnabled = true;

    private Collider triggerCollider;
    private ZombieWaveManager waveManager;
    private int waveIndex = -1;
    private bool hasFired;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void Start()
    {
        if (!startEnabled)
        {
            DisableTrigger();
        }
    }

    public void Initialize(ZombieWaveManager manager, int index)
    {
        waveManager = manager;
        waveIndex = index;
    }

    public void EnableTrigger()
    {
        hasFired = false;

        if (triggerCollider != null)
        {
            triggerCollider.enabled = true;
        }
    }

    public void DisableTrigger()
    {
        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasFired || waveManager == null)
        {
            return;
        }

        if (!other.CompareTag(PlayerTag))
        {
            return;
        }

        hasFired = true;
        DisableTrigger();

        waveManager.TryStartWave(waveIndex);
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = triggerCollider != null ? triggerCollider : GetComponent<Collider>();

        if (col == null)
        {
            return;
        }

        Gizmos.color = hasFired ? new Color(0.5f, 0.5f, 0.5f, 0.35f) : new Color(0f, 0.8f, 1f, 0.35f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
    }
}
