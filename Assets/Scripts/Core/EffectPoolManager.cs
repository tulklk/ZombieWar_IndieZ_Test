using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Generic pool for one-shot VFX (muzzle flash, hit effects, blood splats, explosions).
/// Replaces Instantiate/Destroy churn without needing any change to the VFX prefabs
/// themselves or any scene wiring — the manager lazily creates itself on first use.
/// One UnityEngine.Pool.ObjectPool per distinct prefab, created on demand.
/// </summary>
public class EffectPoolManager : MonoBehaviour
{
    private static EffectPoolManager instance;

    public static EffectPoolManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject managerObject = new GameObject("EffectPoolManager");
                instance = managerObject.AddComponent<EffectPoolManager>();
                DontDestroyOnLoad(managerObject);
            }

            return instance;
        }
    }

    private const int DefaultCapacity = 8;
    private const int MaxSize = 40;

    private readonly Dictionary<ParticleSystem, ObjectPool<ParticleSystem>> pools =
        new Dictionary<ParticleSystem, ObjectPool<ParticleSystem>>();

    // Tracks which pool a live instance belongs to, so ReleaseEarly() and the delayed
    // auto-release can't both try to release the same instance twice.
    private readonly Dictionary<ParticleSystem, ObjectPool<ParticleSystem>> instanceOwners =
        new Dictionary<ParticleSystem, ObjectPool<ParticleSystem>>();

    /// <summary>Gets a pooled instance of prefab, plays it, and auto-releases it back after lifetime seconds.</summary>
    public ParticleSystem Spawn(ParticleSystem prefab, Vector3 position, Quaternion rotation, float lifetime, Transform parent = null)
    {
        if (prefab == null)
        {
            return null;
        }

        ObjectPool<ParticleSystem> pool = GetOrCreatePool(prefab);
        ParticleSystem pooledInstance = pool.Get();
        instanceOwners[pooledInstance] = pool;

        Transform instanceTransform = pooledInstance.transform;
        instanceTransform.SetParent(parent, false);
        instanceTransform.SetPositionAndRotation(position, rotation);

        pooledInstance.Clear(true);
        pooledInstance.Play(true);

        StartCoroutine(ReleaseAfterDelay(pooledInstance, lifetime));

        return pooledInstance;
    }

    /// <summary>
    /// Same as Spawn, but positions the instance using local position/rotation relative to
    /// parent (instead of world-space) — for effects that must follow a moving parent, like
    /// a muzzle flash attached to the gun's fire point.
    /// </summary>
    public ParticleSystem SpawnLocal(ParticleSystem prefab, Transform parent, Vector3 localPosition, Vector3 localEulerAngles, float lifetime)
    {
        if (prefab == null)
        {
            return null;
        }

        ObjectPool<ParticleSystem> pool = GetOrCreatePool(prefab);
        ParticleSystem pooledInstance = pool.Get();
        instanceOwners[pooledInstance] = pool;

        Transform instanceTransform = pooledInstance.transform;
        instanceTransform.SetParent(parent, false);
        instanceTransform.localPosition = localPosition;
        instanceTransform.localEulerAngles = localEulerAngles;

        pooledInstance.Clear(true);
        pooledInstance.Play(true);

        StartCoroutine(ReleaseAfterDelay(pooledInstance, lifetime));

        return pooledInstance;
    }

    /// <summary>Releases a still-active instance back to its pool immediately (e.g. player stopped aiming).</summary>
    public void ReleaseEarly(ParticleSystem instance)
    {
        if (instance == null)
        {
            return;
        }

        if (instanceOwners.TryGetValue(instance, out ObjectPool<ParticleSystem> pool))
        {
            instanceOwners.Remove(instance);
            pool.Release(instance);
        }
    }

    private IEnumerator ReleaseAfterDelay(ParticleSystem pooledInstance, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (pooledInstance != null && instanceOwners.TryGetValue(pooledInstance, out ObjectPool<ParticleSystem> pool))
        {
            instanceOwners.Remove(pooledInstance);
            pool.Release(pooledInstance);
        }
    }

    private ObjectPool<ParticleSystem> GetOrCreatePool(ParticleSystem prefab)
    {
        if (pools.TryGetValue(prefab, out ObjectPool<ParticleSystem> existingPool))
        {
            return existingPool;
        }

        ObjectPool<ParticleSystem> pool = new ObjectPool<ParticleSystem>(
            createFunc: () =>
            {
                // This manager survives scene loads (DontDestroyOnLoad); its pooled
                // instances must too, or a reload would leave the pool holding
                // references to destroyed objects.
                ParticleSystem created = Instantiate(prefab);
                DontDestroyOnLoad(created.gameObject);
                return created;
            },
            actionOnGet: pooledInstance => pooledInstance.gameObject.SetActive(true),
            actionOnRelease: pooledInstance =>
            {
                if (pooledInstance == null) return;

                pooledInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                pooledInstance.gameObject.SetActive(false);
            },
            actionOnDestroy: pooledInstance =>
            {
                if (pooledInstance != null)
                {
                    Destroy(pooledInstance.gameObject);
                }
            },
            collectionCheck: false,
            defaultCapacity: DefaultCapacity,
            maxSize: MaxSize);

        pools[prefab] = pool;
        return pool;
    }
}
