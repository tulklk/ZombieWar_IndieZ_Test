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

    public bool IsDead => isDead;

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

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void UpdateHealthUI()
    {
        if (healthFillImage != null)
        {
            healthFillImage.fillAmount = (float)currentHealth / maxHealth;
        }
    }

    private void CacheOriginalColors()
    {
        if (playerRenderers == null || playerRenderers.Length == 0) return;

        originalColors = new Color[playerRenderers.Length];

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            originalColors[i] = playerRenderers[i].material.color;
        }
    }

    private IEnumerator FlashHitEffect()
    {
        if (playerRenderers == null || playerRenderers.Length == 0) yield break;

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            playerRenderers[i].material.color = hitColor;
        }

        yield return new WaitForSeconds(flashDuration);

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            playerRenderers[i].material.color = originalColors[i];
        }
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