using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Adds BigBossPhaseController to the existing 'BigZombieBoss' (built earlier by
/// BossFightSetup), creates its ThrowPoint/GroundCheck/ProjectileDetectionOrigin/Phase2VFX
/// children, wires every reference it can resolve automatically, and creates placeholder
/// throw-projectile + impact-indicator prefabs if none exist yet. Safe to run more than once —
/// every object/wire-up is find-or-create; only a freshly created element's placeholder
/// position/visuals are ever touched. Does NOT touch the Animator Controller (Phase 2 reuses
/// the existing Roar/Attack triggers — see the setup report for why). Editor-only.
/// </summary>
public static class BossPhaseSetup
{
    private const string UndoLabel = "Setup Boss Phase 2";
    private const string PrefabFolder = "Assets/Prefabs/Boss";
    private const string ProjectilePrefabPath = PrefabFolder + "/BossThrowProjectile.prefab";
    private const string IndicatorPrefabPath = PrefabFolder + "/ThrowImpactIndicator.prefab";

    [MenuItem("Tools/Zombie War/Setup Boss Phase 2")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[BossPhaseSetup] Skipped — run this outside Play Mode.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        Transform bossTransform = FindInActiveScene(activeScene, "BigZombieBoss");

        if (bossTransform == null)
        {
            Debug.LogError("[BossPhaseSetup] Could not find 'BigZombieBoss' in the active scene. Run Tools > Zombie War > Setup Boss Fight first. Aborted.");
            return;
        }

        GameObject bossObject = bossTransform.gameObject;

        BigBossPhaseController phaseController = bossObject.GetComponent<BigBossPhaseController>();

        if (phaseController == null)
        {
            phaseController = Undo.AddComponent<BigBossPhaseController>(bossObject);
        }

        // ---- Child anchor points --------------------------------------------------------
        Transform throwPoint = GetOrCreateChild(bossTransform, "ThrowPoint", out bool throwPointIsNew);

        if (throwPointIsNew)
        {
            throwPoint.localPosition = new Vector3(0.4f, 1.6f, 0.6f);
        }

        Transform groundCheck = GetOrCreateChild(bossTransform, "GroundCheck", out bool groundCheckIsNew);

        if (groundCheckIsNew)
        {
            groundCheck.localPosition = Vector3.zero;
        }

        Transform detectionOrigin = GetOrCreateChild(bossTransform, "ProjectileDetectionOrigin", out bool detectionOriginIsNew);

        if (detectionOriginIsNew)
        {
            detectionOrigin.localPosition = new Vector3(0f, 2f, 0.3f);
        }

        Transform phase2VfxTransform = GetOrCreateChild(bossTransform, "Phase2VFX", out bool phase2VfxIsNew);
        EnsurePhase2VfxParticles(phase2VfxTransform.gameObject, phase2VfxIsNew);

        if (phase2VfxIsNew)
        {
            phase2VfxTransform.localPosition = new Vector3(0f, 1f, 0f);
            phase2VfxTransform.gameObject.SetActive(false);
        }

        // ---- References already present on/around the Boss ------------------------------
        ZombieHealth bossHealth = bossObject.GetComponent<ZombieHealth>();
        ZombieAI bossAI = bossObject.GetComponent<ZombieAI>();
        NavMeshAgent navMeshAgent = bossObject.GetComponent<NavMeshAgent>();
        Animator bossAnimator = bossObject.GetComponentInChildren<Animator>(true);
        Renderer[] bossRenderers = bossObject.GetComponentsInChildren<Renderer>(true);
        AudioSource bossAudioSource = bossObject.GetComponent<AudioSource>();

        Transform playerTransform = FindInActiveScene(activeScene, "Player");
        PlayerHealth playerHealth = playerTransform != null ? playerTransform.GetComponent<PlayerHealth>() : null;

        Transform bossHealthPanelTransform = FindInActiveScene(activeScene, "BossHealthPanel");
        BossHealthUI bossHealthUI = bossHealthPanelTransform != null ? bossHealthPanelTransform.GetComponent<BossHealthUI>() : null;

        Transform bossFightManagerTransform = FindInActiveScene(activeScene, "BossFightManager");
        BossFightManager bossFightManager = bossFightManagerTransform != null ? bossFightManagerTransform.GetComponent<BossFightManager>() : null;

        Transform playerFollowTransform = FindInActiveScene(activeScene, "CM_PlayerFollow");
        CinemachineImpulseSource transitionImpulse = playerFollowTransform != null ? playerFollowTransform.GetComponent<CinemachineImpulseSource>() : null;

        Transform gameCanvasTransform = FindInActiveScene(activeScene, "GameCanvas");
        CanvasGroup screenFlashCanvasGroup = gameCanvasTransform != null ? BuildRoarScreenFlash(gameCanvasTransform) : null;

        if (gameCanvasTransform == null)
        {
            Debug.LogWarning("[BossPhaseSetup] Could not find 'GameCanvas' in the active scene — skipping the Roar screen-flash UI (everything else still works without it).");
        }

        // ---- Placeholder prefabs ----------------------------------------------------------
        GameObject projectilePrefab = EnsureProjectilePrefab();
        GameObject indicatorPrefab = EnsureIndicatorPrefab();

        // ---- Wire BigBossPhaseController --------------------------------------------------
        SerializedObject serializedController = new SerializedObject(phaseController);
        SetReference(serializedController, "bossHealth", bossHealth);
        SetReference(serializedController, "bossAI", bossAI);
        SetReference(serializedController, "navMeshAgent", navMeshAgent);
        SetReference(serializedController, "animator", bossAnimator);
        SetReference(serializedController, "player", playerTransform);
        SetReference(serializedController, "playerHealth", playerHealth);
        SetReference(serializedController, "bossHealthUI", bossHealthUI);
        SetReference(serializedController, "bossFightManager", bossFightManager);
        SetReference(serializedController, "throwPoint", throwPoint);
        SetReference(serializedController, "projectileDetectionOrigin", detectionOrigin);
        SetReference(serializedController, "throwableProjectilePrefab", projectilePrefab);
        SetReference(serializedController, "impactIndicatorPrefab", indicatorPrefab);
        SetReference(serializedController, "phaseTwoVFX", phase2VfxTransform.gameObject);
        SetReference(serializedController, "bossAudioSource", bossAudioSource);
        SetReference(serializedController, "transitionImpulse", transitionImpulse);
        SetReference(serializedController, "screenFlashCanvasGroup", screenFlashCanvasGroup);

        SerializedProperty renderersProperty = serializedController.FindProperty("bossRenderers");

        if (renderersProperty != null)
        {
            renderersProperty.arraySize = bossRenderers.Length;

            for (int i = 0; i < bossRenderers.Length; i++)
            {
                renderersProperty.GetArrayElementAtIndex(i).objectReferenceValue = bossRenderers[i];
            }
        }

        serializedController.ApplyModifiedProperties();

        // ---- Wire BossFightManager.phaseController -----------------------------------------
        if (bossFightManager != null)
        {
            SerializedObject serializedManager = new SerializedObject(bossFightManager);
            SetReference(serializedManager, "phaseController", phaseController);
            serializedManager.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[BossPhaseSetup] Could not find 'BossFightManager' in the active scene — wire its 'Phase Controller' field to this Boss's BigBossPhaseController by hand.");
        }

        if (transitionImpulse == null)
        {
            Debug.LogWarning("[BossPhaseSetup] CM_PlayerFollow has no CinemachineImpulseSource — the Phase 2 transition camera shake will silently no-op until one is added there (BossCameraController's roar shake already relies on the same component).");
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = bossObject;

        Debug.Log("[BossPhaseSetup] BigBossPhaseController added/updated on 'BigZombieBoss' (+ ThrowPoint/GroundCheck/ProjectileDetectionOrigin/Phase2VFX children, placeholder throw-projectile + impact-indicator prefabs under 'Assets/Prefabs/Boss'). " +
            "playerLayer/damageLayer, groundLayer/collisionLayer and obstacleLayerMask are left unassigned (Nothing) — assign them by hand to this project's actual Player/Ground/Wall layers before testing Throw, or it will silently never register a hit. " +
            "Also review: SFX clips (phaseTransitionClip/roarClip/throwClip/projectileImpactClip), throwTriggerName (currently reuses 'Attack' — see report), and ThrowPoint's exact local position.");
    }

    /// <summary>Full-screen red-tinted flash, punched in/out by BigBossPhaseController at the exact Roar moment — find-or-create, only styled the first time.</summary>
    private static CanvasGroup BuildRoarScreenFlash(Transform gameCanvasTransform)
    {
        Transform existing = FindImmediateChild(gameCanvasTransform, "PhaseTwoScreenFlash");
        bool isNew = existing == null;

        GameObject flashObject;

        if (!isNew)
        {
            flashObject = existing.gameObject;
        }
        else
        {
            flashObject = new GameObject("PhaseTwoScreenFlash", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(flashObject, UndoLabel);
            flashObject.transform.SetParent(gameCanvasTransform, false);
        }

        RectTransform rect = flashObject.GetComponent<RectTransform>();

        if (isNew)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        Image image = flashObject.GetComponent<Image>();

        if (image == null)
        {
            image = Undo.AddComponent<Image>(flashObject);
            image.color = new Color(1f, 0.15f, 0.05f, 0.4f);
            image.raycastTarget = false;
        }

        CanvasGroup canvasGroup = flashObject.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = Undo.AddComponent<CanvasGroup>(flashObject);
        }

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // Sits above the regular HUD so the flash actually reads over gameplay — but the
        // Phase Announcement panel (if built later at the same GameCanvas level) should still
        // be placed after this one so its text isn't ever hidden behind the flash.
        flashObject.transform.SetAsLastSibling();

        if (isNew)
        {
            flashObject.SetActive(false);
        }

        return canvasGroup;
    }

    private static void EnsurePhase2VfxParticles(GameObject phase2VfxObject, bool isNew)
    {
        ParticleSystem particles = phase2VfxObject.GetComponent<ParticleSystem>();

        if (particles == null)
        {
            particles = Undo.AddComponent<ParticleSystem>(phase2VfxObject);
        }

        if (!isNew)
        {
            return;
        }

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.startLifetime = 0.6f;
        main.startSpeed = 0.8f;
        main.startSize = 0.25f;
        main.startColor = new Color(1f, 0.3f, 0.15f, 0.85f);
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 20f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1f;

        ParticleSystemRenderer particleRenderer = phase2VfxObject.GetComponent<ParticleSystemRenderer>();

        if (particleRenderer != null)
        {
            Material particleMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");

            if (particleMaterial != null)
            {
                particleRenderer.sharedMaterial = particleMaterial;
            }
        }
    }

    private static GameObject EnsureProjectilePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);

        if (existing != null)
        {
            return existing;
        }

        EnsureFolder(PrefabFolder);

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        temp.name = "BossThrowProjectile";
        temp.transform.localScale = Vector3.one * 0.6f;

        SphereCollider sphereCollider = temp.GetComponent<SphereCollider>();
        sphereCollider.isTrigger = false;

        Rigidbody rigidbody = temp.AddComponent<Rigidbody>();
        rigidbody.useGravity = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        AudioSource audioSource = temp.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        MeshRenderer meshRenderer = temp.GetComponent<MeshRenderer>();

        if (meshRenderer != null)
        {
            Material debrisMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.3f, 0.28f) };
            AssetDatabase.CreateAsset(debrisMaterial, PrefabFolder + "/BossThrowProjectileMaterial.mat");
            meshRenderer.sharedMaterial = debrisMaterial;
        }

        BossThrowableProjectile projectile = temp.AddComponent<BossThrowableProjectile>();

        SerializedObject serializedProjectile = new SerializedObject(projectile);
        SetReference(serializedProjectile, "projectileRigidbody", rigidbody);
        SetReference(serializedProjectile, "projectileCollider", sphereCollider);
        SetReference(serializedProjectile, "audioSource", audioSource);
        serializedProjectile.ApplyModifiedProperties();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, ProjectilePrefabPath);
        Object.DestroyImmediate(temp);

        Debug.Log($"[BossPhaseSetup] Created a placeholder throw-projectile prefab at '{ProjectilePrefabPath}' (a plain gray sphere, sized as a debris chunk) — swap its mesh/material for a proper broken-concrete/barrel/boulder model when available; BossThrowableProjectile's wiring keeps working either way.");

        return prefab;
    }

    private static GameObject EnsureIndicatorPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(IndicatorPrefabPath);

        if (existing != null)
        {
            return existing;
        }

        EnsureFolder(PrefabFolder);

        GameObject temp = new GameObject("ThrowImpactIndicator");
        temp.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        temp.transform.localScale = Vector3.one * 2.5f;

        SpriteRenderer spriteRenderer = temp.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        spriteRenderer.color = new Color(1f, 0.25f, 0.1f, 0f);

        ThrowImpactIndicator indicator = temp.AddComponent<ThrowImpactIndicator>();

        SerializedObject serializedIndicator = new SerializedObject(indicator);
        SetReference(serializedIndicator, "indicatorRenderer", spriteRenderer);
        serializedIndicator.ApplyModifiedProperties();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, IndicatorPrefabPath);
        Object.DestroyImmediate(temp);

        Debug.Log($"[BossPhaseSetup] Created a placeholder impact-indicator prefab at '{IndicatorPrefabPath}' (a flat, built-in circle sprite tinted red-orange) — swap for a proper radial-gradient ground decal sprite later if you have one.");

        return prefab;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
        string folderName = System.IO.Path.GetFileName(path);

        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void SetReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            property.objectReferenceValue = value;
        }
        else
        {
            Debug.LogWarning($"[BossPhaseSetup] Property '{propertyName}' not found on {serializedObject.targetObject?.GetType().Name}.");
        }
    }

    private static Transform GetOrCreateChild(Transform parent, string name, out bool isNew)
    {
        Transform existing = FindImmediateChild(parent, name);

        if (existing != null)
        {
            isNew = false;
            return existing;
        }

        GameObject childObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(childObject, UndoLabel);
        childObject.transform.SetParent(parent, false);
        isNew = true;
        return childObject.transform;
    }

    private static Transform FindImmediateChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

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
