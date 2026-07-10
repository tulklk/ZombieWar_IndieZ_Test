using DG.Tweening;
using TMPro;
using UnityEngine;

public static class DamageNumberSpawner
{
    private const float FontSize = 3f;
    private const float FloatDistance = 0.6f;
    private const float PopDuration = 0.15f;
    private const float MoveDuration = 0.7f;
    private const float FadeDuration = 0.3f;

    public static void Spawn(Vector3 worldPosition, int amount, Color color)
    {
        GameObject numberObject = new GameObject("DamageNumber");
        numberObject.transform.position = worldPosition;

        if (Camera.main != null)
        {
            numberObject.transform.forward = Camera.main.transform.forward;
        }

        TextMeshPro label = numberObject.AddComponent<TextMeshPro>();
        label.text = "-" + amount;
        label.color = color;
        label.fontSize = FontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;

        numberObject.transform.localScale = Vector3.zero;

        Sequence sequence = DOTween.Sequence();
        sequence.Insert(0f, numberObject.transform.DOScale(1f, PopDuration).SetEase(Ease.OutBack));
        sequence.Insert(0f, numberObject.transform.DOMoveY(worldPosition.y + FloatDistance, MoveDuration).SetEase(Ease.OutCubic));
        sequence.Insert(MoveDuration - FadeDuration, DOTween.To(() => label.color, c => label.color = c, new Color(color.r, color.g, color.b, 0f), FadeDuration));
        sequence.OnComplete(() => Object.Destroy(numberObject));
    }
}
