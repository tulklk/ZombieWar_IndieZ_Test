using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Pooled floating damage numbers — every bullet hit used to Instantiate + Destroy a brand
/// new GameObject/TextMeshPro; under sustained fire this was the single hottest allocation
/// site in the game. Now reuses a small pool instead.
/// </summary>
public static class DamageNumberSpawner
{
    private const float FontSize = 3f;
    private const float FloatDistance = 0.6f;
    private const float PopDuration = 0.15f;
    private const float MoveDuration = 0.7f;
    private const float FadeDuration = 0.3f;

    private static ObjectPool<TextMeshPro> pool;
    private static Camera cachedCamera;

    private static ObjectPool<TextMeshPro> Pool => pool ??= new ObjectPool<TextMeshPro>(
        createFunc: CreateLabel,
        actionOnGet: label => label.gameObject.SetActive(true),
        actionOnRelease: label =>
        {
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }
        },
        actionOnDestroy: label =>
        {
            if (label != null)
            {
                Object.Destroy(label.gameObject);
            }
        },
        collectionCheck: false,
        defaultCapacity: 16,
        maxSize: 64);

    private static TextMeshPro CreateLabel()
    {
        GameObject numberObject = new GameObject("DamageNumber");
        Object.DontDestroyOnLoad(numberObject);

        TextMeshPro label = numberObject.AddComponent<TextMeshPro>();
        label.fontSize = FontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;

        return label;
    }

    public static void Spawn(Vector3 worldPosition, int amount, Color color)
    {
        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
        }

        TextMeshPro label = Pool.Get();
        Transform labelTransform = label.transform;

        labelTransform.position = worldPosition;
        labelTransform.localScale = Vector3.zero;

        if (cachedCamera != null)
        {
            labelTransform.forward = cachedCamera.transform.forward;
        }

        label.text = "-" + amount;
        label.color = color;

        Sequence sequence = DOTween.Sequence();
        sequence.Insert(0f, labelTransform.DOScale(1f, PopDuration).SetEase(Ease.OutBack));
        sequence.Insert(0f, labelTransform.DOMoveY(worldPosition.y + FloatDistance, MoveDuration).SetEase(Ease.OutCubic));
        sequence.Insert(MoveDuration - FadeDuration, DOTween.To(() => label.color, c => label.color = c, new Color(color.r, color.g, color.b, 0f), FadeDuration));
        sequence.OnComplete(() => Pool.Release(label));
    }
}
