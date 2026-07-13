using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the top-down minimap: frames MiniMapCamera (fixed whole-map, or following the
/// player) and keeps UI markers in sync with world positions. Uses
/// Camera.WorldToViewportPoint so marker placement is correct for either camera mode
/// without separate math paths. See MinimapIcon.cs to register more markers later.
/// </summary>
public class MinimapController : MonoBehaviour
{
    public static MinimapController Instance { get; private set; }

    [Header("Targets")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Camera minimapCamera;

    [Header("UI")]
    [SerializeField] private RectTransform playerMarker;
    [SerializeField] private RectTransform minimapRect;

    [Header("Camera Mode")]
    [Tooltip("On: camera follows the player. Off: camera stays fixed, framed over mapRoot's bounds.")]
    [SerializeField] private bool followPlayer;
    [SerializeField] private float followHeight = 40f;
    [Tooltip("Used directly when followPlayer is on, or as a fallback when mapRoot isn't assigned.")]
    [SerializeField] private float orthographicSize = 20f;
    [Tooltip("Extra world-space margin added around the map bounds in fixed (whole-map) mode.")]
    [SerializeField] private float worldPadding = 5f;

    [Header("Marker")]
    [SerializeField] private bool rotateMarkerWithPlayer = true;
    [SerializeField] private bool clampMarkerInsideMap = true;

    [Header("Performance")]
    [Tooltip("How many times per second MiniMapCamera actually re-renders. Marker UI still updates every frame — only the background map render is throttled.")]
    [SerializeField] private float cameraRefreshRate = 12f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private readonly List<MinimapIcon> icons = new List<MinimapIcon>();
    private bool cameraFramed;
    private float nextCameraRenderTime;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        ConfigureCameraPerformance();
        FrameCameraOverMap();
    }

    /// <summary>
    /// One-time cheap-camera setup: no HDR/MSAA/depth texture, and only the layers the
    /// minimap actually needs to show — keeps this second camera from paying for shadows,
    /// particles, bullets or world-space UI it doesn't render anything useful for.
    /// </summary>
    private void ConfigureCameraPerformance()
    {
        if (minimapCamera == null)
        {
            return;
        }

        minimapCamera.allowHDR = false;
        minimapCamera.allowMSAA = false;
        minimapCamera.depthTextureMode = DepthTextureMode.None;
        minimapCamera.useOcclusionCulling = false;

        // The project doesn't have dedicated layers for Player/Bullet/Particle/Bomb — map
        // props (buildings, roads...) and gameplay objects both live on Default, so we
        // can't fully separate "map" from "gameplay clutter" without a bigger layer
        // restructure. This still drops Zombie and UI, which is a real, safe win; Player/
        // bullets/particles keep rendering here today (they show as UI markers on top,
        // via MinimapIcon, but the 3D camera still sees them too until layers are split).
        int zombieLayer = LayerMask.NameToLayer("Zombie");
        int uiLayer = LayerMask.NameToLayer("UI");

        int mask = ~0;

        if (zombieLayer >= 0) mask &= ~(1 << zombieLayer);
        if (uiLayer >= 0) mask &= ~(1 << uiLayer);

        minimapCamera.cullingMask = mask;
    }

    private void LateUpdate()
    {
        if (minimapCamera == null || player == null)
        {
            return;
        }

        if (followPlayer)
        {
            Vector3 camPosition = player.position;
            camPosition.y = followHeight;
            minimapCamera.transform.position = camPosition;
        }
        else if (!cameraFramed)
        {
            FrameCameraOverMap();
        }

        // Throttle the actual GPU render — the map barely changes frame to frame, so
        // redrawing it at full game framerate is wasted work on mobile.
        if (cameraRefreshRate <= 0f)
        {
            minimapCamera.enabled = true;
        }
        else if (Time.time >= nextCameraRenderTime)
        {
            minimapCamera.enabled = true;
            nextCameraRenderTime = Time.time + (1f / cameraRefreshRate);
        }
        else
        {
            minimapCamera.enabled = false;
        }

        UpdateMarker(playerMarker, player, rotateMarkerWithPlayer);

        for (int i = 0; i < icons.Count; i++)
        {
            MinimapIcon icon = icons[i];

            if (icon == null || icon.Marker == null)
            {
                continue;
            }

            UpdateMarker(icon.Marker, icon.WorldTransform, icon.RotateWithTransform);
        }
    }

    /// <summary>Call again if the map is resized/expanded at runtime and uses fixed whole-map mode.</summary>
    public void RefreshCameraFraming()
    {
        cameraFramed = false;
        FrameCameraOverMap();
    }

    private void FrameCameraOverMap()
    {
        if (minimapCamera == null)
        {
            return;
        }

        minimapCamera.orthographic = true;

        if (!followPlayer && mapRoot != null)
        {
            Bounds bounds = MiniMapBoundsProvider.CalculateBounds(mapRoot);

            Vector3 position = bounds.center;
            position.y = followHeight;
            minimapCamera.transform.position = position;

            float halfExtent = Mathf.Max(bounds.extents.x, bounds.extents.z) + worldPadding;
            minimapCamera.orthographicSize = Mathf.Max(halfExtent, 1f);

            Log($"Framed whole map. center={bounds.center}, size={bounds.size}, orthographicSize={minimapCamera.orthographicSize}");
        }
        else
        {
            minimapCamera.orthographicSize = Mathf.Max(orthographicSize, 1f);

            if (player != null)
            {
                Vector3 position = player.position;
                position.y = followHeight;
                minimapCamera.transform.position = position;
            }

            Log($"Using manual orthographicSize={minimapCamera.orthographicSize} (followPlayer={followPlayer}, mapRoot assigned={mapRoot != null}).");
        }

        cameraFramed = true;
    }

    private void UpdateMarker(RectTransform marker, Transform worldTarget, bool rotate)
    {
        if (marker == null || minimapRect == null || worldTarget == null)
        {
            return;
        }

        Vector3 viewportPosition = minimapCamera.WorldToViewportPoint(worldTarget.position);

        float rectWidth = minimapRect.rect.width;
        float rectHeight = minimapRect.rect.height;

        float localX = (viewportPosition.x - 0.5f) * rectWidth;
        float localY = (viewportPosition.y - 0.5f) * rectHeight;

        if (clampMarkerInsideMap)
        {
            localX = Mathf.Clamp(localX, -rectWidth * 0.5f, rectWidth * 0.5f);
            localY = Mathf.Clamp(localY, -rectHeight * 0.5f, rectHeight * 0.5f);
        }

        marker.anchoredPosition = new Vector2(localX, localY);

        if (rotate)
        {
            marker.localEulerAngles = new Vector3(0f, 0f, -worldTarget.eulerAngles.y);
        }
    }

    public void RegisterIcon(MinimapIcon icon)
    {
        if (icon != null && !icons.Contains(icon))
        {
            icons.Add(icon);
            Log($"Registered icon: {icon.name}");
        }
    }

    public void UnregisterIcon(MinimapIcon icon)
    {
        icons.Remove(icon);
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[MinimapController] {message}");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
