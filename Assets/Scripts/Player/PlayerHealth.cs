using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int currentHealth;

    [Header("UI")]
    [SerializeField] private Image healthFillImage;

    [Header("Hit Effect")]
    [SerializeField] private Renderer[] playerRenderers;
    [SerializeField] private Color hitColor = Color.red;
    [SerializeField] private float flashDuration = 0.12f;

    [Header("Death")]
    [SerializeField] private PlayerAnimationController animationController;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private WeaponController weaponController;

    private Color[] originalColors;
    private bool isDead;
    private MaterialPropertyBlock propertyBlock;

    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    public bool IsDead => isDead;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    /// <summary>Fired whenever health changes, passing (currentHealth, maxHealth) as floats.</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>Fired once per TakeDamage call (not on Heal) — one hit, one event.</summary>
    public event Action<int> OnDamaged;

    private void Awake()
    {
        currentHealth = maxHealth;

        if (playerRenderers == null || playerRenderers.Length == 0 || System.Array.TrueForAll(playerRenderers, r => r == null))
        {
            playerRenderers = GetComponentsInChildren<Renderer>(true);
        }

        CacheOriginalColors();
        UpdateHealthUI();

        if (animationController == null)
        {
            animationController = GetComponent<PlayerAnimationController>();
        }

        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }

        if (weaponController == null)
        {
            weaponController = GetComponent<WeaponController>();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            TakeDamage(10);
        }
    }

    private const float DamageNumberHeight = 1.6f;

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        DamageNumberSpawner.Spawn(transform.position + Vector3.up * DamageNumberHeight, damage, Color.red);

        UpdateHealthUI();
        StartCoroutine(FlashHitEffect());
        OnDamaged?.Invoke(damage);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (isDead) return;

        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        UpdateHealthUI();
    }

    private void UpdateHealthUI()
    {
        if (healthFillImage != null)
        {
            healthFillImage.fillAmount = (float)currentHealth / maxHealth;
        }

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void CacheOriginalColors()
    {
        if (playerRenderers == null || playerRenderers.Length == 0) return;

        originalColors = new Color[playerRenderers.Length];

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            // sharedMaterial (not .material) so reading the starting color doesn't itself
            // clone a per-renderer material instance.
            originalColors[i] = playerRenderers[i].sharedMaterial != null
                ? playerRenderers[i].sharedMaterial.color
                : Color.white;
        }
    }

    private IEnumerator FlashHitEffect()
    {
        if (playerRenderers == null || playerRenderers.Length == 0) yield break;

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            SetRendererColor(playerRenderers[i], hitColor);
        }

        yield return new WaitForSeconds(flashDuration);

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            SetRendererColor(playerRenderers[i], originalColors[i]);
        }
    }

    // MaterialPropertyBlock overrides the color per-renderer at draw time without cloning
    // the shared Material — keeps GPU instancing/batching intact across many hit flashes.
    private void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;

        propertyBlock ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorPropertyId, color);
        renderer.SetPropertyBlock(propertyBlock);
    }

    private void Die()
    {
        isDead = true;
        Debug.Log("Player died");

        animationController?.PlayDeath();

        if (playerMovement != null)
        {
            playerMovement.enabled = false;
        }

        if (weaponController != null)
        {
            weaponController.HideWeaponModels();
            weaponController.enabled = false;
        }
    }
}