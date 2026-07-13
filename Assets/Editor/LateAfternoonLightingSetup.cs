using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click setup for the "post-apocalyptic late afternoon" lighting look on Level1:
/// duplicates the Sunless_BlueSky_01 skybox (never touches the original), retargets the
/// scene's Directional Light to a low warm sun, and tunes ambient/fog to match.
/// Editor-only — lives under Assets/Editor so it is stripped from player builds.
/// </summary>
public static class LateAfternoonLightingSetup
{
    private const string SourceSkyboxPath = "Assets/EmaceArt/NecroPOLY Dark Corners/Sky/Sunless_BlueSky_01.mat";
    private const string TargetSkyboxFolder = "Assets/Materials/Environment";
    private const string TargetSkyboxName = "Sunless_BlueSky_01_LateAfternoon";
    private const string TargetSkyboxPath = TargetSkyboxFolder + "/" + TargetSkyboxName + ".mat";

    private static readonly Color SkyboxTint = new Color32(145, 158, 176, 255);
    private static readonly Color SunColor = new Color32(255, 180, 106, 255);
    private static readonly Color FogColor = new Color32(38, 49, 61, 255);

    private const float SkyboxExposure = 0.62f;
    private static readonly Vector3 SunEulerAngles = new Vector3(18f, -35f, 0f);
    private const float SunIntensity = 0.72f;
    private const float SunIndirectMultiplier = 0.75f;
    private const float SunShadowStrength = 0.78f;
    private const float SunShadowBias = 0.05f;
    private const float SunShadowNormalBias = 0.4f;
    private const float SunShadowNearPlane = 0.2f;
    private const float AmbientIntensity = 0.62f;
    private const float ReflectionIntensity = 0.4f;
    private const float FogDensity = 0.006f;

    [MenuItem("Tools/Zombie War/Apply Late Afternoon Lighting")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[LateAfternoonLightingSetup] Skipped — changes made in Play Mode are discarded on Stop. Exit Play Mode first.");
            return;
        }

        Light sun = FindMainDirectionalLight();

        if (sun == null)
        {
            Debug.LogError("[LateAfternoonLightingSetup] No Directional Light found in the active scene. Aborted — nothing was changed.");
            return;
        }

        Material skyboxMaterial = GetOrCreateLateAfternoonSkybox();

        if (skyboxMaterial == null)
        {
            Debug.LogError("[LateAfternoonLightingSetup] Could not load or create the Late Afternoon skybox material. Aborted — nothing was changed.");
            return;
        }

        LogBackup(sun);

        ApplySkybox(skyboxMaterial, sun);
        ApplyDirectionalLight(sun);
        ApplyEnvironment();
        ApplyFog();

        DynamicGI.UpdateEnvironment();

        // DynamicGI.UpdateEnvironment() only refreshes the live in-memory ambient/reflection
        // data for this Editor session — reopening the scene later can still show a stale
        // cached look for a moment before it catches up. A full bake writes the environment
        // lighting (ambient probe + reflection cubemap) to disk so it's correct immediately
        // every time this scene is opened, not just right after this tool runs.
        bool bakeSucceeded = Lightmapping.Bake();

        if (!bakeSucceeded)
        {
            Debug.LogWarning("[LateAfternoonLightingSetup] Lightmapping.Bake() did not complete successfully — you may need to run Window > Rendering > Lighting > Generate Lighting manually.");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("[LateAfternoonLightingSetup] Late Afternoon lighting applied and baked. Review it in Game View / Device Simulator, then save the scene (Ctrl+S) yourself when you're happy with it.");
    }

    private static Light FindMainDirectionalLight()
    {
        if (RenderSettings.sun != null)
        {
            return RenderSettings.sun;
        }

        Light[] lights = Object.FindObjectsOfType<Light>();

        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }

        return null;
    }

    private static Material GetOrCreateLateAfternoonSkybox()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(TargetSkyboxPath);

        if (existing != null)
        {
            return existing;
        }

        Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceSkyboxPath);

        if (source == null)
        {
            Debug.LogError($"[LateAfternoonLightingSetup] Source skybox not found at {SourceSkyboxPath}");
            return null;
        }

        EnsureFolder("Assets/Materials");
        EnsureFolder(TargetSkyboxFolder);

        if (!AssetDatabase.CopyAsset(SourceSkyboxPath, TargetSkyboxPath))
        {
            Debug.LogError("[LateAfternoonLightingSetup] AssetDatabase.CopyAsset failed while duplicating the skybox material.");
            return null;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return AssetDatabase.LoadAssetAtPath<Material>(TargetSkyboxPath);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void LogBackup(Light sun)
    {
        string previousSkybox = RenderSettings.skybox != null ? RenderSettings.skybox.name : "None";

        Debug.Log(
            "[LateAfternoonLightingSetup] Backup before changes — " +
            $"Skybox: {previousSkybox}, " +
            $"Sun Rotation: {sun.transform.eulerAngles}, " +
            $"Sun Color: #{ColorUtility.ToHtmlStringRGB(sun.color)}, " +
            $"Sun Intensity: {sun.intensity}, " +
            $"Ambient Intensity: {RenderSettings.ambientIntensity}, " +
            $"Fog Enabled: {RenderSettings.fog}");
    }

    private static void ApplySkybox(Material skyboxMaterial, Light sun)
    {
        Undo.RecordObject(skyboxMaterial, "Apply Late Afternoon Lighting");

        if (skyboxMaterial.HasProperty("_Tint"))
        {
            skyboxMaterial.SetColor("_Tint", SkyboxTint);
        }

        if (skyboxMaterial.HasProperty("_Exposure"))
        {
            skyboxMaterial.SetFloat("_Exposure", SkyboxExposure);
        }

        // _Rotation is left as-authored: this is a 6-sided cubemap (no single "sun glow"
        // face), so there is no one correct value here — nudge it manually in the
        // Inspector while watching Game View if the horizon doesn't read right.

        EditorUtility.SetDirty(skyboxMaterial);

        Object renderSettings = GetRenderSettingsObject();

        if (renderSettings != null)
        {
            Undo.RecordObject(renderSettings, "Apply Late Afternoon Lighting");
        }

        RenderSettings.skybox = skyboxMaterial;
        RenderSettings.sun = sun;
    }

    private static void ApplyDirectionalLight(Light sun)
    {
        Undo.RecordObject(sun.transform, "Apply Late Afternoon Lighting");
        Undo.RecordObject(sun, "Apply Late Afternoon Lighting");

        sun.transform.rotation = Quaternion.Euler(SunEulerAngles);
        sun.color = SunColor;
        sun.intensity = SunIntensity;
        sun.bounceIntensity = SunIndirectMultiplier;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = SunShadowStrength;
        sun.shadowBias = SunShadowBias;
        sun.shadowNormalBias = SunShadowNormalBias;
        sun.shadowNearPlane = SunShadowNearPlane;

        if (sun.gameObject.name == "Directional Light")
        {
            Undo.RecordObject(sun.gameObject, "Apply Late Afternoon Lighting");
            sun.gameObject.name = "LateAfternoon_Sun";
        }
    }

    private static void ApplyEnvironment()
    {
        Object renderSettings = GetRenderSettingsObject();

        if (renderSettings != null)
        {
            Undo.RecordObject(renderSettings, "Apply Late Afternoon Lighting");
        }

        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = AmbientIntensity;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = ReflectionIntensity;
        RenderSettings.reflectionBounces = 1;
    }

    private static void ApplyFog()
    {
        Object renderSettings = GetRenderSettingsObject();

        if (renderSettings != null)
        {
            Undo.RecordObject(renderSettings, "Apply Late Afternoon Lighting");
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = FogDensity;
    }

    /// <summary>
    /// RenderSettings has no public Object handle, but the Lighting window itself uses
    /// this internal accessor to make ambient/fog/skybox changes undoable — same trick here.
    /// Falls back to no-op (still applies the values, just without Undo) if it's ever removed.
    /// </summary>
    private static Object GetRenderSettingsObject()
    {
        MethodInfo method = typeof(RenderSettings).GetMethod(
            "GetRenderSettings",
            BindingFlags.NonPublic | BindingFlags.Static);

        return method?.Invoke(null, null) as Object;
    }
}
