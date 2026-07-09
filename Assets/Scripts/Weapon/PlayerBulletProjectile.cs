using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerBulletProjectile : MonoBehaviour
{
    [Header("Runtime Data")]
    [SerializeField] private int damage = 10;
    [SerializeField] private float speed = 25f;
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private ParticleSystem hitEffectPrefab;

    private Rigidbody rb;
    private bool hasHit;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void Initialize(Vector3 direction, int bulletDamage, float bulletSpeed, float bulletLifeTime, ParticleSystem hitEffect)
    {
        damage = bulletDamage;
        speed = bulletSpeed;
        lifeTime = bulletLifeTime;
        hitEffectPrefab = hitEffect;

        direction.y = 0f;
        direction.Normalize();

        transform.rotation = Quaternion.LookRotation(direction);

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        rb.useGravity = false;
        rb.velocity = direction * speed;

        Destroy(gameObject, lifeTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasHit)
        {
            return;
        }

        ZombieHealth zombieHealth = other.GetComponentInParent<ZombieHealth>();

        if (zombieHealth == null)
        {
            return;
        }

        hasHit = true;

        zombieHealth.TakeDamage(damage);

        if (hitEffectPrefab != null)
        {
            Instantiate(
                hitEffectPrefab,
                transform.position,
                Quaternion.identity
            );
        }

        Destroy(gameObject);
    }
}