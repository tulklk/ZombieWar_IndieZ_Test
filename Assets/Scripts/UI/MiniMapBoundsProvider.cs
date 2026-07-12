using UnityEngine;

/// <summary>
/// Computes the world-space bounds of a map by encapsulating every Renderer under a root
/// Transform. Used by MinimapController to frame MiniMapCamera over the whole map
/// automatically, so it keeps working after the map is resized/expanded.
/// </summary>
public class MiniMapBoundsProvider : MonoBehaviour
{
    [SerializeField] private bool includeInactiveRenderers = false;

    public Bounds GetBounds()
    {
        return CalculateBounds(transform, includeInactiveRenderers);
    }

    public static Bounds CalculateBounds(Transform root, bool includeInactive = false)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive);

        if (renderers.Length == 0)
        {
            return new Bounds(root.position, Vector3.one * 10f);
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private void OnDrawGizmosSelected()
    {
        Bounds bounds = GetBounds();
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}
