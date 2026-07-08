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

    private Color[] originalColors;
    private bool isDead;

    private void Awake()
    {
        currentHealth = maxHealth;
        CacheOriginalColors();
        UpdateHealthUI();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            TakeDamage(10);
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

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
    }
}