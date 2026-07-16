using UnityEngine;

/// <summary>
/// A single thrown Boss projectile: launched on a real ballistic arc (horizontal velocity
/// toward the target at `speed`, vertical launch velocity derived from `arcHeight` + the
/// scene's actual Physics.gravity), then left to real physics/collision to resolve — impact
/// is detected via OnCollisionEnter/OnTriggerEnter against damageLayer/collisionLayer, never a
/// precomputed landing time, so a Player who moves after the throw genuinely can dodge it.
/// Deals radius damage exactly once via Physics.OverlapSphereNonAlloc (no per-impact GC alloc)
/// against damageLayer only — the Boss itself is naturally never hit as long as its own layer
/// is excluded from damageLayer/collisionLayer on the prefab.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BossThrowableProjectile : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody projectileRigidbody;
    [SerializeField] private Collider projectileCollider;
    [SerializeField] private GameObject impactVFX;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip impactClip;

    [Header("Damage")]
    [Tooltip("Only colliders on this layer take area damage on impact — should be the Player's layer only.")]
    [SerializeField] private LayerMask damageLayer;
    [Tooltip("Solid surfaces (ground/environment) that also trigger impact but deal no damage themselves — must NOT include the Boss's own layer.")]
    [SerializeField] private LayerMask collisionLayer;

    [Header("Lifetime")]
    [SerializeField] private float maximumLifetime = 6f;

    private int damage;
    private float impactRadius;
    private bool hasImpacted;
    private bool hasResolved;
    private float spawnTime;

    private static readonly Collider[] OverlapResultsBuffer = new Collider[8];

    /// <summary>Fires exactly once, whether from a real impact or a lifetime timeout — lets the spawner (BigBossPhaseController) decrement its active-projectile count without polling.</summary>
    public event System.Action<BossThrowableProjectile> Resolved;

    private void Awake()
    {
        if (projectileRigidbody == null)
        {
            projectileRigidbody = GetComponent<Rigidbody>();
        }

        if (projectileCollider == null)
        {
            projectileCollider = GetComponent<Collider>();
        }
    }

    private void Update()
    {
        if (hasImpacted || hasResolved)
        {
            return;
        }

        if (Time.time - spawnTime >= maximumLifetime)
        {
            Resolve(0f);
        }
    }

    /// <summary>
    /// Launches toward targetPosition. Horizontal velocity direction/magnitude come straight
    /// from `speed`; vertical launch velocity is solved from `arcHeight` (v = sqrt(2 * g *
    /// arcHeight)) so the peak height is honored regardless of the project's gravity setting.
    /// Safe to call on a freshly-instantiated, still-disabled-nothing prefab instance.
    /// </summary>
    public void Launch(Vector3 startPosition, Vector3 targetPosition, float speed, float arcHeight, int projectileDamage, float radius)
    {
        damage = projectileDamage;
        impactRadius = Mathf.Max(radius, 0.1f);
        hasImpacted = false;
        hasResolved = false;
        spawnTime = Time.time;

        transform.position = startPosition;

        Vector3 horizontalDelta = targetPosition - startPosition;
        horizontalDelta.y = 0f;
        Vector3 horizontalDirection = horizontalDelta.sqrMagnitude > 0.01f ? horizontalDelta.normalized : transform.forward;

        float gravity = Mathf.Max(Mathf.Abs(Physics.gravity.y), 0.1f);
        float verticalVelocity = Mathf.Sqrt(2f * Mathf.Max(arcHeight, 0.5f) * gravity);

        if (projectileRigidbody != null)
        {
            projectileRigidbody.isKinematic = false;
            projectileRigidbody.useGravity = true;
            projectileRigidbody.velocity = horizontalDirection * Mathf.Max(speed, 0.5f) + Vector3.up * verticalVelocity;
        }

        if (projectileCollider != null)
        {
            projectileCollider.enabled = true;
        }

        transform.rotation = Quaternion.LookRotation(horizontalDirection);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryImpact(collision.collider, collision.GetContact(0).point);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryImpact(other, other.ClosestPoint(transform.position));
    }

    private void TryImpact(Collider other, Vector3 impactPoint)
    {
        if (hasImpacted || hasResolved || other == null)
        {
            return;
        }

        int otherLayerMask = 1 << other.gameObject.layer;

        if ((otherLayerMask & (damageLayer.value | collisionLayer.value)) == 0)
        {
            return;
        }

        Impact(impactPoint);
    }

    private void Impact(Vector3 impactPosition)
    {
        if (hasImpacted)
        {
            return;
        }

        hasImpacted = true;

        if (projectileRigidbody != null)
        {
            projectileRigidbody.velocity = Vector3.zero;
            projectileRigidbody.isKinematic = true;
        }

        if (projectileCollider != null)
        {
            projectileCollider.enabled = false;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(impactPosition, impactRadius, OverlapResultsBuffer, damageLayer, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = OverlapResultsBuffer[i];

            if (hitCollider == null)
            {
                continue;
            }

            PlayerHealth playerHealth = hitCollider.GetComponentInParent<PlayerHealth>();

            if (playerHealth != null && !playerHealth.IsDead)
            {
                playerHealth.TakeDamage(damage);
            }
        }

        HideVisuals();

        float tailDelay = 0.1f;

        if (impactVFX != null)
        {
            GameObject vfxInstance = Instantiate(impactVFX, impactPosition, Quaternion.identity);
            Destroy(vfxInstance, 3f);
        }

        bool sfxEnabled = AudioManager.Instance == null || AudioManager.Instance.SfxEnabled;

        if (audioSource != null && impactClip != null && sfxEnabled)
        {
            audioSource.PlayOneShot(impactClip);
            tailDelay = Mathf.Max(tailDelay, impactClip.length);
        }

        Resolve(tailDelay);
    }

    /// <summary>Disables renderers immediately (so the projectile visually vanishes right at impact) while the GameObject itself lingers just long enough for its impact AudioClip to finish before being destroyed.</summary>
    private void HideVisuals()
    {
        Renderer[] renderersToHide = GetComponentsInChildren<Renderer>();

        for (int i = 0; i < renderersToHide.Length; i++)
        {
            renderersToHide[i].enabled = false;
        }
    }

    private void Resolve(float destroyDelay)
    {
        if (hasResolved)
        {
            return;
        }

        hasResolved = true;
        Resolved?.Invoke(this);
        Destroy(gameObject, destroyDelay);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(impactRadius, 0.1f));
    }
}
