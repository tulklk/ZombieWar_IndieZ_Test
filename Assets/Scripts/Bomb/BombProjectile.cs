using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody))]
public class BombProjectile : MonoBehaviour
{
    [Header("Explosion VFX / SFX (optional)")]
    [SerializeField] private GameObject explosionVfxPrefab;
    [SerializeField] private float explosionVfxDestroyDelay = 5f;
    [SerializeField] private AudioClip explosionSfx;
    [SerializeField] private float explosionSfxVolume = 1f;

    [Header("Player Damage (optional, off by default)")]
    [SerializeField] private bool damagePlayer;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private int playerDamage = 20;

    [Header("Explosion Hook (optional, e.g. Cinemachine Impulse Source)")]
    [SerializeField] private UnityEvent onExplode;

    [Header("Editor Preview Only")]
    [SerializeField] private float gizmoExplosionRadiusPreview = 4f;

    private int damage;
    private float explosionRadius;
    private float fuseTime;
    private LayerMask zombieLayer;

    private bool isInitialized;
    private bool hasExploded;
    private float fuseTimer;

    private const int MaxOverlapResults = 32;
    private static readonly Collider[] OverlapResults = new Collider[MaxOverlapResults];

    private void Awake()
    {
        if (GetComponent<Collider>() == null)
        {
            SphereCollider fallbackCollider = gameObject.AddComponent<SphereCollider>();
            fallbackCollider.radius = 0.05f;
        }
    }

    /// <summary>
    /// Arms the bomb. The fuse only starts counting down once this is called
    /// (i.e. after the bomb has actually been thrown, not while held in the hand).
    /// </summary>
    public void Initialize(int bombDamage, float radius, float fuse, LayerMask targetZombieLayer)
    {
        damage = bombDamage;
        explosionRadius = radius;
        fuseTime = fuse;
        zombieLayer = targetZombieLayer;

        fuseTimer = 0f;
        hasExploded = false;
        isInitialized = true;
    }

    public void AddExplodedListener(UnityAction listener)
    {
        onExplode.AddListener(listener);
    }

    private void Update()
    {
        if (!isInitialized || hasExploded)
        {
            return;
        }

        fuseTimer += Time.deltaTime;

        if (fuseTimer >= fuseTime)
        {
            Explode();
        }
    }

    private void Explode()
    {
        if (hasExploded)
        {
            return;
        }

        hasExploded = true;

        DamageZombiesInRadius();

        if (damagePlayer)
        {
            DamagePlayerInRadius();
        }

        if (explosionVfxPrefab != null)
        {
            GameObject vfxInstance = Instantiate(explosionVfxPrefab, transform.position, Quaternion.identity);
            Destroy(vfxInstance, explosionVfxDestroyDelay);
        }

        if (explosionSfx != null)
        {
            AudioSource.PlayClipAtPoint(explosionSfx, transform.position, explosionSfxVolume);
        }

        onExplode?.Invoke();

        Destroy(gameObject);
    }

    private void DamageZombiesInRadius()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, explosionRadius, OverlapResults, zombieLayer, QueryTriggerInteraction.Collide);

        HashSet<ZombieHealth> damagedZombies = null;

        for (int i = 0; i < hitCount; i++)
        {
            ZombieHealth zombieHealth = OverlapResults[i].GetComponentInParent<ZombieHealth>();

            if (zombieHealth == null)
            {
                continue;
            }

            damagedZombies ??= new HashSet<ZombieHealth>();

            if (damagedZombies.Add(zombieHealth))
            {
                zombieHealth.TakeDamage(damage);
            }
        }
    }

    private void DamagePlayerInRadius()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, explosionRadius, OverlapResults, playerLayer, QueryTriggerInteraction.Collide);

        HashSet<PlayerHealth> damagedPlayers = null;

        for (int i = 0; i < hitCount; i++)
        {
            PlayerHealth playerHealth = OverlapResults[i].GetComponentInParent<PlayerHealth>();

            if (playerHealth == null)
            {
                continue;
            }

            damagedPlayers ??= new HashSet<PlayerHealth>();

            if (damagedPlayers.Add(playerHealth))
            {
                playerHealth.TakeDamage(playerDamage);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        float radius = isInitialized ? explosionRadius : gizmoExplosionRadiusPreview;

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
        Gizmos.DrawSphere(transform.position, radius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
