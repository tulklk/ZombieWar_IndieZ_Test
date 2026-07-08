using System.Collections;
using UnityEngine;

public class ZombieHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 50;
    [SerializeField] private int currentHealth;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private ZombieAI zombieAI;
    [SerializeField] private Collider zombieCollider;

    [Header("Hit Effect")]
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float flashDuration = 0.1f;

    [Header("Death")]
    [SerializeField] private float destroyDelay = 2.5f;

    private Color[] originalColors;
    private bool isDead;

    private static readonly int DieBackHash = Animator.StringToHash("DieBack");
    private static readonly int DieForwardHash = Animator.StringToHash("DieForward");

    private void Awake()
    {
        currentHealth = maxHealth;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (zombieAI == null)
        {
            zombieAI = GetComponent<ZombieAI>();
        }

        if (zombieCollider == null)
        {
            zombieCollider = GetComponent<Collider>();
        }

        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<Renderer>();
        }

        CacheOriginalColors();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            TakeDamage(10);
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead)
        {
            return;
        }

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        StartCoroutine(FlashHitEffect());

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void CacheOriginalColors()
    {
        originalColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                originalColors[i] = renderers[i].material.color;
            }
        }
    }

    private IEnumerator FlashHitEffect()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].material.color = hitColor;
            }
        }

        yield return new WaitForSeconds(flashDuration);

        if (isDead)
        {
            yield break;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].material.color = originalColors[i];
            }
        }
    }

    private void Die()
    {
        isDead = true;

        if (zombieAI != null)
        {
            zombieAI.SetDead();
        }

        if (zombieCollider != null)
        {
            zombieCollider.enabled = false;
        }

        if (animator != null)
        {
            bool fallBack = Random.value > 0.5f;

            if (fallBack)
            {
                animator.SetTrigger(DieBackHash);
            }
            else
            {
                animator.SetTrigger(DieForwardHash);
            }
        }

        Debug.Log("Zombie died");

        Destroy(gameObject, destroyDelay);
    }
}