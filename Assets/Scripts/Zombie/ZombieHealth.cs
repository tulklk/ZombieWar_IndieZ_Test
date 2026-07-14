using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ZombieHealth : MonoBehaviour
{
    [Header("Data (optional — overrides the health/hit-effect/blood/death fields below if assigned)")]
    [Tooltip("Assign a ZombieData asset (Assets/ScriptableObjects/Zombies) to drive this zombie's stats from one shared, reusable asset instead of editing them inline per prefab. Usually the same asset assigned on this GameObject's ZombieAI.")]
    [SerializeField] private ZombieData zombieData;

    [Header("Health")]
    [SerializeField] private int maxHealth = 50;
    [SerializeField] private int currentHealth;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private ZombieAI zombieAI;
    [SerializeField] private Collider zombieCollider;

    [Header("Health Bar")]
    [SerializeField] private bool showHealthBar = true;
    [SerializeField] private float healthBarHeightPadding = 0.25f;
    [SerializeField] private float fallbackHealthBarHeight = 2.2f;

    private GameObject healthBarRoot;
    private Image healthBarFillImage;

    [Header("Hit Effect")]
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color hitColor = new Color(1f, 0.25f, 0f, 1f);
    [SerializeField] private Color bloodStainColor = new Color(0.5f, 0f, 0f, 1f);
    [SerializeField] private float flashDuration = 0.3f;

    [Header("Blood VFX")]
    [SerializeField] private ParticleSystem floorSplatPrefab;
    [SerializeField] private ParticleSystem directionalSplatPrefab;
    [SerializeField] private LayerMask groundLayerMask;

    [Header("Death")]
    [SerializeField] private float destroyDelay = 2.5f;

    [Header("Score")]
    [SerializeField] private int scoreValue = 10;

    private Color[] originalColors;
    private bool isDead;
    private bool countedAsActive;
    private MaterialPropertyBlock propertyBlock;

    private static readonly int DieBackHash = Animator.StringToHash("DieBack");
    private static readonly int DieForwardHash = Animator.StringToHash("DieForward");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    /// <summary>
    /// Live zombie count, kept in sync from Awake/OnDestroy instead of scanning the scene
    /// with GameObject.FindGameObjectsWithTag("Zombie") every spawn attempt.
    /// </summary>
    public static int ActiveCount { get; private set; }

    /// <summary>Added to the run's score total when this zombie dies — set from ZombieData if assigned.</summary>
    public int ScoreValue => scoreValue;

    /// <summary>Fires exactly once, from Die(), before the GameObject is scheduled for destruction — subscribers (e.g. ZombieWaveManager) should unsubscribe once handled.</summary>
    public event System.Action<ZombieHealth> Died;

    /// <summary>Instance-wide version of Died — lets LevelResultManager tally kills/score for the run without depending on ZombieWaveManager. Never unsubscribed per-instance since it's static; LevelResultManager unsubscribes itself in OnDisable instead.</summary>
    public static event System.Action<ZombieHealth> AnyZombieDied;

    private void Awake()
    {
        if (zombieData != null)
        {
            ApplyZombieData(zombieData);
        }

        currentHealth = maxHealth;
        ActiveCount++;
        countedAsActive = true;

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

        if (showHealthBar)
        {
            CreateHealthBar();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            TakeDamage(10);
        }
    }

    private void ApplyZombieData(ZombieData data)
    {
        maxHealth = data.maxHealth;
        hitColor = data.hitColor;
        bloodStainColor = data.bloodStainColor;
        flashDuration = data.flashDuration;
        destroyDelay = data.destroyDelay;
        scoreValue = data.scoreValue;

        if (data.floorSplatPrefab != null)
        {
            floorSplatPrefab = data.floorSplatPrefab;
        }

        if (data.directionalSplatPrefab != null)
        {
            directionalSplatPrefab = data.directionalSplatPrefab;
        }

        if (data.groundLayerMask.value != 0)
        {
            groundLayerMask = data.groundLayerMask;
        }
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, transform.position, Vector3.up);
    }

    public void TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (isDead)
        {
            return;
        }

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        DamageNumberSpawner.Spawn(hitPoint, damage, Color.red);

        UpdateHealthBar();
        StainHitBodyPart(hitPoint);
        StartCoroutine(FlashHitEffect());
        SpawnBloodVfx(hitPoint, hitNormal);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private const float BloodVfxDestroyDelay = 5f;

    private const float SurfaceOffset = 0.03f;

    private const float FloorSplatFlightDuration = 0.25f;
    private const float FloorSplatArcHeight = 0.4f;

    private void SpawnBloodVfx(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (directionalSplatPrefab != null)
        {
            EffectPoolManager.Instance.Spawn(
                directionalSplatPrefab,
                hitPoint + hitNormal * SurfaceOffset,
                Quaternion.LookRotation(hitNormal),
                BloodVfxDestroyDelay);
        }

        SpawnFloorSplat(hitPoint);
    }

    private void SpawnFloorSplat(Vector3 hitPoint)
    {
        if (floorSplatPrefab == null)
        {
            return;
        }

        Vector3 floorPosition = hitPoint;
        Vector3 floorNormal = Vector3.up;

        if (Physics.Raycast(hitPoint + Vector3.up * 0.5f, Vector3.down, out RaycastHit groundHit, 5f, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            floorPosition = groundHit.point;
            floorNormal = groundHit.normal;
        }
        else
        {
            floorPosition.y = transform.position.y;
        }

        ParticleSystem floorSplat = EffectPoolManager.Instance.Spawn(
            floorSplatPrefab, hitPoint, Quaternion.identity, BloodVfxDestroyDelay);

        StartCoroutine(FlyFloorSplatToGround(floorSplat.transform, hitPoint, floorPosition + floorNormal * SurfaceOffset));
    }

    private IEnumerator FlyFloorSplatToGround(Transform splatTransform, Vector3 startPosition, Vector3 endPosition)
    {
        float elapsed = 0f;

        while (elapsed < FloorSplatFlightDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / FloorSplatFlightDuration);
            float arc = Mathf.Sin(t * Mathf.PI) * FloorSplatArcHeight;

            splatTransform.position = Vector3.Lerp(startPosition, endPosition, t) + Vector3.up * arc;

            yield return null;
        }

        splatTransform.position = endPosition;
    }

    private void CreateHealthBar()
    {
        healthBarRoot = new GameObject("HealthBarCanvas");
        healthBarRoot.transform.SetParent(transform, false);
        healthBarRoot.transform.localPosition = Vector3.up * CalculateHealthBarHeight();

        Canvas canvas = healthBarRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(100f, 14f);
        canvasRect.localScale = Vector3.one * 0.01f;

        healthBarRoot.AddComponent<Billboard>();

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform));
        backgroundObject.transform.SetParent(healthBarRoot.transform, false);
        Image backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.sprite = GetWhiteSprite();
        backgroundImage.color = new Color(0f, 0f, 0f, 0.6f);

        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform));
        fillObject.transform.SetParent(healthBarRoot.transform, false);
        healthBarFillImage = fillObject.AddComponent<Image>();
        healthBarFillImage.sprite = GetWhiteSprite();
        healthBarFillImage.color = Color.red;
        healthBarFillImage.type = Image.Type.Filled;
        healthBarFillImage.fillMethod = Image.FillMethod.Horizontal;
        healthBarFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        healthBarFillImage.fillAmount = 1f;

        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
    }

    private static Sprite whiteSprite;

    private static Sprite GetWhiteSprite()
    {
        if (whiteSprite == null)
        {
            Texture2D texture = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        return whiteSprite;
    }

    private float CalculateHealthBarHeight()
    {
        if (renderers == null || renderers.Length == 0)
        {
            return fallbackHealthBarHeight;
        }

        bool hasBounds = false;
        Bounds combinedBounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (!hasBounds)
        {
            return fallbackHealthBarHeight;
        }

        return (combinedBounds.max.y - transform.position.y) + healthBarHeightPadding;
    }

    private void UpdateHealthBar()
    {
        if (healthBarFillImage != null)
        {
            healthBarFillImage.fillAmount = (float)currentHealth / maxHealth;
        }
    }

    private void CacheOriginalColors()
    {
        originalColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                // sharedMaterial (not .material) so reading the starting color doesn't
                // itself clone a per-renderer material instance.
                originalColors[i] = renderers[i].sharedMaterial != null
                    ? renderers[i].sharedMaterial.color
                    : Color.white;
            }
        }
    }

    private void StainHitBodyPart(Vector3 hitPoint)
    {
        int hitIndex = FindClosestRendererIndex(hitPoint);

        if (hitIndex < 0)
        {
            return;
        }

        // Permanently update the cached "original" color so this part stays blood-stained
        // even after the next whole-body flash reverts everything back to originalColors.
        originalColors[hitIndex] = bloodStainColor;
        SetRendererColor(renderers[hitIndex], bloodStainColor);
    }

    private IEnumerator FlashHitEffect()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                SetRendererColor(renderers[i], hitColor);
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
                SetRendererColor(renderers[i], originalColors[i]);
            }
        }
    }

    // MaterialPropertyBlock overrides the color per-renderer at draw time without cloning
    // the shared Material — keeps GPU instancing/batching intact across many zombie hits.
    private void SetRendererColor(Renderer renderer, Color color)
    {
        propertyBlock ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorPropertyId, color);
        renderer.SetPropertyBlock(propertyBlock);
    }

    private int FindClosestRendererIndex(Vector3 hitPoint)
    {
        int closestIndex = -1;
        float closestDistanceSqr = float.MaxValue;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            Vector3 closestPoint = renderers[i].bounds.ClosestPoint(hitPoint);
            float distanceSqr = (closestPoint - hitPoint).sqrMagnitude;

            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private void Die()
    {
        isDead = true;
        Died?.Invoke(this);
        AnyZombieDied?.Invoke(this);

        if (healthBarRoot != null)
        {
            healthBarRoot.SetActive(false);
        }

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

    private void OnDestroy()
    {
        if (countedAsActive)
        {
            ActiveCount--;
            countedAsActive = false;
        }
    }
}