using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Report-only mobile performance audit for Android. Scans project assets and every
/// loaded scene for the usual mobile perf offenders (oversized/Read-Write textures,
/// mipmapped UI sprites, Always Animate animators, realtime shadowed lights, heavy
/// particle systems, non-instanced materials, missing scripts, etc.) and lists them
/// grouped by severity. Nothing here mutates the project except the two explicit
/// "Fix" buttons, which only touch import settings that are safe by construction
/// (disabling mipmaps on UI sprites, enabling GPU Instancing on eligible materials).
/// </summary>
public class MobilePerformanceAuditWindow : EditorWindow
{
    private enum Severity { P0, P1, P2 }

    private class Finding
    {
        public Severity Severity;
        public string Category;
        public string Message;
        public UnityEngine.Object Target;
        public Action AutoFix;
        public string AutoFixLabel;
    }

    private const int MaxRecommendedTextureSize = 2048;
    private const int MaxRecommendedParticles = 500;
    private const int MinimapRenderTextureWarningSize = 512;
    private const long LargeAudioClipBytes = 1024 * 1024; // 1 MB

    private readonly List<Finding> findings = new List<Finding>();
    private Vector2 scrollPosition;
    private bool hasScanned;

    [MenuItem("Tools/Zombie War/Mobile Performance Audit")]
    public static void ShowWindow()
    {
        MobilePerformanceAuditWindow window = GetWindow<MobilePerformanceAuditWindow>("Mobile Perf Audit");
        window.minSize = new Vector2(560, 400);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rescan Project + Open Scenes", GUILayout.Height(28)))
            {
                RunScan();
            }

            GUI.enabled = hasScanned && findings.Count > 0;
            if (GUILayout.Button("Copy Report To Console", GUILayout.Height(28), GUILayout.Width(180)))
            {
                Debug.Log(BuildTextReport());
            }
            GUI.enabled = true;
        }

        if (!hasScanned)
        {
            EditorGUILayout.HelpBox("Report-only audit. Click \"Rescan\" to check the project against the mobile performance guidelines. Only two buttons in this window ever modify assets (UI sprite mipmaps, GPU Instancing) — everything else is informational.", MessageType.Info);
            return;
        }

        int p0 = findings.Count(f => f.Severity == Severity.P0);
        int p1 = findings.Count(f => f.Severity == Severity.P1);
        int p2 = findings.Count(f => f.Severity == Severity.P2);

        EditorGUILayout.LabelField($"P0 (critical): {p0}    P1 (high impact): {p1}    P2 (additional): {p2}", EditorStyles.boldLabel);

        if (findings.Count == 0)
        {
            EditorGUILayout.HelpBox("No issues found by the checks in this tool.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawGroup(Severity.P0, "P0 — Critical");
        DrawGroup(Severity.P1, "P1 — High Impact");
        DrawGroup(Severity.P2, "P2 — Additional");

        EditorGUILayout.EndScrollView();
    }

    private void DrawGroup(Severity severity, string title)
    {
        List<Finding> group = findings.Where(f => f.Severity == severity).ToList();
        if (group.Count == 0)
        {
            return;
        }

        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        foreach (Finding finding in group)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"[{finding.Category}] {finding.Message}", EditorStyles.wordWrappedLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = finding.Target != null;
                    if (GUILayout.Button("Select", GUILayout.Width(70)))
                    {
                        Selection.activeObject = finding.Target;
                        EditorGUIUtility.PingObject(finding.Target);
                    }
                    GUI.enabled = true;

                    if (finding.AutoFix != null && GUILayout.Button(finding.AutoFixLabel, GUILayout.Width(220)))
                    {
                        finding.AutoFix.Invoke();
                        RunScan();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        EditorGUILayout.Space();
    }

    private void RunScan()
    {
        findings.Clear();
        hasScanned = true;

        ScanTextures();
        ScanAudioClips();
        ScanMaterials();
        ScanSceneObjects();

        findings.Sort((a, b) => a.Severity.CompareTo(b.Severity));
    }

    // ---------------------------------------------------------------- Textures

    private void ScanTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsProjectAsset(path))
            {
                continue;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (importer.maxTextureSize > MaxRecommendedTextureSize)
            {
                Add(Severity.P2, "Texture", $"'{path}' has Max Size {importer.maxTextureSize} (> {MaxRecommendedTextureSize}). Consider lowering per-platform (Android) override.", texture);
            }

            if (importer.isReadable)
            {
                Add(Severity.P1, "Texture", $"'{path}' has Read/Write Enabled — doubles memory for this texture. Only keep it on if code accesses pixels at runtime.", texture);
            }

            if (importer.textureType == TextureImporterType.Sprite && importer.mipmapEnabled)
            {
                string localPath = path;
                Add(Severity.P1, "Texture", $"'{path}' is a UI Sprite with mipmaps enabled — mipmaps are wasted memory for 2D/UI sprites.", texture,
                    () => DisableSpriteMipmaps(localPath), "Fix: Disable Mipmaps");
            }
        }
    }

    private static void DisableSpriteMipmaps(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.mipmapEnabled = false;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- Audio

    private void ScanAudioClips()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsProjectAsset(path))
            {
                continue;
            }

            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                continue;
            }

            System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length < LargeAudioClipBytes)
            {
                continue;
            }

            AudioImporterSampleSettings settings = importer.GetOverrideSampleSettings("Android");
            if (!importer.ContainsSampleSettingsOverride("Android"))
            {
                settings = importer.defaultSampleSettings;
            }

            if (settings.loadType == AudioClipLoadType.DecompressOnLoad)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Add(Severity.P1, "Audio", $"'{path}' ({fileInfo.Length / 1024}KB) uses Decompress On Load — large clips should use Streaming or Compressed In Memory on Android.", clip);
            }
        }
    }

    // ---------------------------------------------------------------- Materials

    private void ScanMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsProjectAsset(path))
            {
                continue;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
            {
                continue;
            }

            if (material.enableInstancing)
            {
                continue;
            }

            // Standard/Mobile shaders support GPU Instancing; a material not opted in
            // silently loses batching whenever more than one instance of it is drawn.
            if (material.shader.name.Contains("Standard") || material.shader.name.StartsWith("Mobile/") || material.shader.name.StartsWith("Legacy Shaders/"))
            {
                string localPath = path;
                Add(Severity.P2, "Material", $"'{path}' (shader: {material.shader.name}) does not have GPU Instancing enabled.", material,
                    () => EnableInstancing(localPath), "Fix: Enable GPU Instancing");
            }
        }
    }

    private static void EnableInstancing(string path)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            return;
        }

        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------- Scene objects

    private void ScanSceneObjects()
    {
        int directionalLightCount = 0;

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                ScanMissingScripts(root);
                ScanAnimators(root);
                ScanSkinnedMeshRenderers(root);
                ScanParticleSystems(root);
                ScanCameras(root);
                ScanRigidbodiesAndColliders(root);
                directionalLightCount += CountDirectionalLights(root);
            }
        }

        if (directionalLightCount > 1)
        {
            Add(Severity.P1, "Lighting", $"{directionalLightCount} Directional Lights found across loaded scenes — mobile Built-in RP should have exactly one, extras multiply per-pixel lighting cost.", null);
        }

        foreach (Light light in FindAllComponents<Light>())
        {
            if (light.type != LightType.Directional && light.lightmapBakeType == LightmapBakeType.Realtime && light.shadows != LightShadows.None)
            {
                Add(Severity.P1, "Lighting", $"Realtime point/spot light '{light.name}' casts shadows — realtime shadows from non-directional lights are expensive on mobile tiled GPUs.", light);
            }
        }
    }

    private void ScanMissingScripts(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missingCount > 0)
            {
                Add(Severity.P0, "Missing Script", $"'{GetPath(t)}' has {missingCount} missing script reference(s).", t.gameObject);
            }
        }
    }

    private void ScanAnimators(GameObject root)
    {
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        {
            if (animator.cullingMode == AnimatorCullingMode.AlwaysAnimate)
            {
                Add(Severity.P1, "Animation", $"Animator on '{GetPath(animator.transform)}' uses Always Animate — evaluates even fully offscreen. Prefer Cull Update Transforms unless root motion off-screen is required.", animator);
            }
        }
    }

    private void ScanSkinnedMeshRenderers(GameObject root)
    {
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.updateWhenOffscreen)
            {
                Add(Severity.P2, "Animation", $"SkinnedMeshRenderer on '{GetPath(renderer.transform)}' has Update When Offscreen enabled — forces bounds recompute every frame even off-camera.", renderer);
            }
        }
    }

    private void ScanParticleSystems(GameObject root)
    {
        foreach (ParticleSystem particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particleSystem.main;
            if (main.maxParticles > MaxRecommendedParticles)
            {
                Add(Severity.P2, "VFX", $"ParticleSystem '{GetPath(particleSystem.transform)}' has Max Particles = {main.maxParticles} (> {MaxRecommendedParticles}).", particleSystem);
            }
        }
    }

    private void ScanCameras(GameObject root)
    {
        foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
        {
            bool isMain = camera.CompareTag("MainCamera");

            if (!isMain && (camera.allowHDR || camera.allowMSAA))
            {
                Add(Severity.P2, "Camera", $"Secondary camera '{GetPath(camera.transform)}' has HDR={camera.allowHDR}, MSAA={camera.allowMSAA} — usually unnecessary for minimap/UI/overlay cameras.", camera);
            }

            if (camera.targetTexture != null && Mathf.Max(camera.targetTexture.width, camera.targetTexture.height) > MinimapRenderTextureWarningSize)
            {
                Add(Severity.P2, "Camera", $"Camera '{GetPath(camera.transform)}' renders to a {camera.targetTexture.width}x{camera.targetTexture.height} RenderTexture — oversized for a minimap/UI target.", camera);
            }
        }
    }

    private void ScanRigidbodiesAndColliders(GameObject root)
    {
        foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rigidbody.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic || rigidbody.collisionDetectionMode == CollisionDetectionMode.Continuous)
            {
                Add(Severity.P2, "Physics", $"Rigidbody on '{GetPath(rigidbody.transform)}' uses {rigidbody.collisionDetectionMode} collision detection — expensive; only needed for fast-moving objects that must not tunnel through thin colliders.", rigidbody);
            }

            MeshCollider meshCollider = rigidbody.GetComponent<MeshCollider>();
            if (meshCollider != null && !meshCollider.convex && !rigidbody.isKinematic)
            {
                Add(Severity.P0, "Physics", $"'{GetPath(rigidbody.transform)}' has a non-kinematic Rigidbody with a non-convex MeshCollider — unsupported by PhysX and will throw/silently fail to collide correctly.", meshCollider);
            }
        }
    }

    private int CountDirectionalLights(GameObject root)
    {
        int count = 0;
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.type == LightType.Directional)
            {
                count++;
            }
        }
        return count;
    }

    private IEnumerable<T> FindAllComponents<T>() where T : Component
    {
        List<T> results = new List<T>();
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                results.AddRange(root.GetComponentsInChildren<T>(true));
            }
        }
        return results;
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    /// <summary>
    /// Only project-owned assets ship in the build and matter for device memory/perf —
    /// Packages/ (including Editor-only tooling like Device Simulator overlays) is excluded.
    /// </summary>
    private static bool IsProjectAsset(string path)
    {
        return path.StartsWith("Assets/", StringComparison.Ordinal);
    }

    private void Add(Severity severity, string category, string message, UnityEngine.Object target, Action autoFix = null, string autoFixLabel = null)
    {
        findings.Add(new Finding
        {
            Severity = severity,
            Category = category,
            Message = message,
            Target = target,
            AutoFix = autoFix,
            AutoFixLabel = autoFixLabel
        });
    }

    private string BuildTextReport()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Mobile Performance Audit — {findings.Count} findings ({DateTime.Now:yyyy-MM-dd HH:mm})");

        foreach (Severity severity in new[] { Severity.P0, Severity.P1, Severity.P2 })
        {
            foreach (Finding finding in findings.Where(f => f.Severity == severity))
            {
                sb.AppendLine($"[{severity}] [{finding.Category}] {finding.Message}");
            }
        }

        return sb.ToString();
    }
}
