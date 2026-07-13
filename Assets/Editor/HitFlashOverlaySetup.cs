using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wires HitFlashOverlay onto the existing "LowHealthOverlay" full-screen Image in the
/// active scene (previously unused — a plain red Image with alpha 0, no script driving
/// it) and points it at the Player's PlayerHealth. Safe to run more than once. Editor-
/// only, lives under Assets/Editor.
/// </summary>
public static class HitFlashOverlaySetup
{
    private const string OverlayName = "LowHealthOverlay";
    private const string VignetteSpritePath = "Assets/UI/Images/Material/HitFlashVignette.png";
    private const int VignetteTextureSize = 512;
    private const float VignetteBorderWidth = 0.12f;

    [MenuItem("Tools/Zombie War/Setup Hit Flash Overlay")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[HitFlashOverlaySetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Transform overlayTransform = FindInActiveScene(activeScene, OverlayName);

        if (overlayTransform == null)
        {
            Debug.LogError($"[HitFlashOverlaySetup] Could not find a GameObject named '{OverlayName}' in the active scene. Aborted.");
            return;
        }

        Image overlayImage = overlayTransform.GetComponent<Image>();

        if (overlayImage == null)
        {
            Debug.LogError($"[HitFlashOverlaySetup] '{OverlayName}' has no Image component. Aborted.");
            return;
        }

        overlayImage.sprite = GetOrCreateVignetteSprite();
        overlayImage.type = Image.Type.Simple;
        overlayImage.color = new Color(1f, 0f, 0f, 0f);
        EditorUtility.SetDirty(overlayImage);

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        PlayerHealth playerHealth = playerObject != null ? playerObject.GetComponent<PlayerHealth>() : null;

        if (playerHealth == null)
        {
            Debug.LogWarning("[HitFlashOverlaySetup] Could not find a Player with a PlayerHealth component — wiring the overlay anyway, assign the reference by hand afterward.");
        }

        // Removed and re-added (not reused) so tunable fields like flashAlpha always pick
        // up the script's current defaults instead of keeping a stale value from a
        // previous run — this tool gets re-run each time those numbers are retuned.
        HitFlashOverlay existingHitFlash = overlayTransform.GetComponent<HitFlashOverlay>();

        if (existingHitFlash != null)
        {
            Undo.DestroyObjectImmediate(existingHitFlash);
        }

        HitFlashOverlay hitFlash = Undo.AddComponent<HitFlashOverlay>(overlayTransform.gameObject);

        SerializedObject serializedOverlay = new SerializedObject(hitFlash);
        serializedOverlay.FindProperty("targetImage").objectReferenceValue = overlayImage;

        if (playerHealth != null)
        {
            serializedOverlay.FindProperty("playerHealth").objectReferenceValue = playerHealth;
        }

        serializedOverlay.ApplyModifiedProperties();

        EditorUtility.SetDirty(overlayTransform.gameObject);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = overlayTransform.gameObject;

        Debug.Log($"[HitFlashOverlaySetup] HitFlashOverlay wired on '{OverlayName}'. Test in Play Mode (take damage from a zombie), then save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Generates (once) a white, edge-only vignette sprite: fully transparent at the
    /// center, opaque toward all 4 screen edges (brightest at the corners, where two
    /// edges overlap) — HitFlashOverlay tints/fades this via the Image's own color
    /// rather than filling the whole screen with flat color.
    /// </summary>
    private static Sprite GetOrCreateVignetteSprite()
    {
        // Always regenerated (not cached) so re-running this tool after a tuning pass
        // (border width, falloff curve) refreshes the sprite instead of reusing a stale one.
        Texture2D texture = new Texture2D(VignetteTextureSize, VignetteTextureSize, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[VignetteTextureSize * VignetteTextureSize];

        for (int y = 0; y < VignetteTextureSize; y++)
        {
            float v = y / (float)(VignetteTextureSize - 1);

            for (int x = 0; x < VignetteTextureSize; x++)
            {
                float u = x / (float)(VignetteTextureSize - 1);

                float distanceToEdge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float t = Mathf.Clamp01(distanceToEdge / VignetteBorderWidth);
                float falloff = 1f - t;
                float alpha = falloff * falloff * falloff;

                pixels[y * VignetteTextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        byte[] pngData = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        string folder = System.IO.Path.GetDirectoryName(VignetteSpritePath)?.Replace('\\', '/');

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/'), System.IO.Path.GetFileName(folder));
        }

        System.IO.File.WriteAllBytes(VignetteSpritePath, pngData);
        AssetDatabase.ImportAsset(VignetteSpritePath);

        TextureImporter importer = AssetImporter.GetAtPath(VignetteSpritePath) as TextureImporter;

        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(VignetteSpritePath);
    }

    /// <summary>Searches every root GameObject's full hierarchy, including inactive objects.</summary>
    private static Transform FindInActiveScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
