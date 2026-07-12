using UnityEngine;

/// <summary>
/// Full-screen desaturate + red vignette warning when PlayerHealth drops to/below a threshold.
/// Built-in Render Pipeline post-process (Camera.OnRenderImage), so it must live on the camera
/// that renders the game view. Reads state only from PlayerHealth.OnHealthChanged - no polling.
/// </summary>
[RequireComponent(typeof(Camera))]
public class LowHealthScreenEffect : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Effect Shader")]
    [SerializeField] private Shader effectShader;

    [Header("Thresholds")]
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.1f;

    [Header("Look")]
    [SerializeField, Range(0f, 1f)] private float targetSaturationLoss = 0.95f;
    [SerializeField, Range(0f, 1f)] private float targetVignetteIntensity = 0.35f;
    [SerializeField] private Color vignetteColor = new Color(0.3f, 0f, 0f, 1f);

    [Header("Transition")]
    [SerializeField, Range(0.4f, 0.7f)] private float transitionDuration = 0.5f;

    private Material effectMaterial;
    private float blend;
    private bool isLowHealth;

    private static readonly int SaturationLossId = Shader.PropertyToID("_SaturationLoss");
    private static readonly int VignetteIntensityId = Shader.PropertyToID("_VignetteIntensity");
    private static readonly int VignetteColorId = Shader.PropertyToID("_VignetteColor");

    private void OnEnable()
    {
        if (playerHealth == null) return;

        playerHealth.OnHealthChanged += HandleHealthChanged;

        // Sync immediately with current health in case Awake already ran before we subscribed.
        HandleHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
    }

    private void OnDisable()
    {
        if (playerHealth == null) return;

        playerHealth.OnHealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        if (maxHealth <= 0f) return;

        float healthPercent = currentHealth / maxHealth;
        isLowHealth = currentHealth <= 0f || healthPercent <= lowHealthThreshold;
    }

    private void Update()
    {
        float target = isLowHealth ? 1f : 0f;
        float step = transitionDuration > 0f ? Time.deltaTime / transitionDuration : 1f;
        blend = Mathf.MoveTowards(blend, target, step);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (effectShader == null || blend <= 0.001f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (effectMaterial == null)
        {
            effectMaterial = new Material(effectShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        effectMaterial.SetFloat(SaturationLossId, blend * targetSaturationLoss);
        effectMaterial.SetFloat(VignetteIntensityId, blend * targetVignetteIntensity);
        effectMaterial.SetColor(VignetteColorId, vignetteColor);

        Graphics.Blit(source, destination, effectMaterial);
    }

    private void OnDestroy()
    {
        if (effectMaterial != null)
        {
            Destroy(effectMaterial);
        }
    }
}
