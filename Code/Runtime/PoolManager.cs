using System;
using System.Collections.Generic;
using UnityEngine;
using MegaCrush.ObjectPool.Interfaces;

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Simple pooling system for GameObjects, now with named pools.
    /// </summary>
    public class PoolManager : MonoBehaviour
    {
        private static readonly Dictionary<string, PoolObjects> objectsMap = new();          // key: poolName
        private static readonly Dictionary<int, string> cachedObjectNames = new();           // prefabID -> prefabName
        private static readonly Dictionary<int, string> instanceToPoolName = new();          // instanceID -> poolName

        /// <summary>
        /// True while the pool is instantiating (warmup/expansion).
        /// Helpful as a guard if any legacy logic still runs in OnEnable.
        /// </summary>
        public static bool IsWarming { get; private set; }

        /// <summary>
        /// Register a new pool and pre-instantiate its objects.
        /// </summary>
        public static void AddNewObjectPool(PoolObjectSetting thisObject)
        {
            CreatePoolObjects(thisObject, expandExistingPool: false);
        }

        /// <summary>
        /// Create (or expand) a pool's instances.
        /// </summary>
        private static void CreatePoolObjects(PoolObjectSetting poolObject, bool expandExistingPool = false)
        {
            if (poolObject == null || poolObject.prefab == null)
            {
                Debug.LogError("PoolManager: Invalid PoolObjectSetting (null or missing prefab).");
                return;
            }

            string poolName = GetPoolName(poolObject);
            if (string.IsNullOrEmpty(poolName))
            {
                Debug.LogError("PoolManager: Pool name could not be resolved.");
                return;
            }

            IsWarming = true;

            PoolObjects pool;
            if (expandExistingPool)
            {
                if (!objectsMap.TryGetValue(poolName, out pool) || pool == null)
                {
                    Debug.LogError($"PoolManager: Couldn't find existing pool '{poolName}' to expand.");
                    IsWarming = false;
                    return;
                }
            }
            else
            {
                pool = new PoolObjects
                {
                    settings = poolObject,
                    instances = new List<GameObject>(),
                    currentIndex = 0
                };
                objectsMap[poolName] = pool;
            }

            int toCreate = Mathf.Max(0, poolObject.count);
            for (int i = 0; i < toCreate; ++i)
            {
                // Do NOT touch components on the prefab asset; only on the instance.
                var instance = poolObject.parent
                    ? UnityEngine.Object.Instantiate(poolObject.prefab, poolObject.parent)
                    : UnityEngine.Object.Instantiate(poolObject.prefab);

                // Safety: disable agent until placement
                if (instance.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var thisAgent))
                    thisAgent.enabled = false;

                instance.SetActive(false); // keep inactive during warmup

                // Re-apply parenting semantics (UI vs non-UI) if needed
                if (poolObject.parent)
                {
                    if (instance.TryGetComponent(out RectTransform _))
                        instance.transform.SetParent(poolObject.parent, false); // UI: keep local sane
                    else
                        instance.transform.SetParent(poolObject.parent, true);  // non-UI: preserve world
                }

                pool.instances.Add(instance);
            }

            IsWarming = false;
        }

        /// <summary>
        /// Get an instance by prefab reference. Expands if exhausted.
        /// </summary>
        public static GameObject GetInstance(GameObject prefab)
        {
            if (!prefab)
                return null;

            string poolName = GetObjectName(prefab);       // defaults to prefab.name
            return GetInstanceByNameInternal(poolName, prefab);
        }

        /// <summary>
        /// Get an instance by pool name. Expands if exhausted (requires that pool was created).
        /// </summary>
        public static GameObject GetInstance(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            // We don't have the prefab reference here. If expansion is needed, we expand from existing settings.
            return GetInstanceByNameInternal(name, /*prefab*/ null);
        }

        private static GameObject GetInstanceByNameInternal(string poolName, GameObject prefabForFallbackExpansion)
        {
            if (!objectsMap.TryGetValue(poolName, out var pool) || pool == null)
                return null;

            var instance = pool.GetInstance(); // may return null if all active
            if (!instance)
            {
                // Expand using current settings clone
                var settings = pool.settings;
                if (settings != null)
                {
                    // growth step
                    settings.count = Mathf.Max(1, Mathf.Max(settings.count, 1) * 2);
                    CreatePoolObjects(settings, expandExistingPool: true);
                }
                else if (prefabForFallbackExpansion != null)
                {
                    // Fallback if somehow settings went missing
                    var s = new PoolObjectSetting
                    {
                        name = poolName,
                        prefab = prefabForFallbackExpansion,
                        count = 20
                    };
                    CreatePoolObjects(s, expandExistingPool: false);
                }
                else
                {
                    Debug.LogError($"PoolManager: Cannot expand pool '{poolName}' (missing settings).");
                    return null;
                }

                instance = pool.GetInstance();
                if (!instance)
                {
                    Debug.LogError($"PoolManager: Failed to fetch instance for '{poolName}' after expansion.");
                    return null;
                }
            }

            // Track mapping so ReturnInstance can find the right pool without parsing names
            instanceToPoolName[instance.GetInstanceID()] = poolName;

            // Keep a readable name (purely cosmetic)
            instance.name = $"{poolName}_{Guid.NewGuid()}";
            return instance;
        }

        /// <summary>
        /// Return an instance to the pool.
        /// </summary>
        public static void ReturnInstance(GameObject instance)
        {
            if (!instance) return;

            foreach (var h in instance.GetComponentsInChildren<IPooledDespawnHandler>(true))
                h.OnReturnedToPool();

            // Reparent back to the pool's configured parent, if any
            if (TryGetPoolForInstance(instance, out var pool) && pool?.settings?.parent)
            {
                if (instance.TryGetComponent(out RectTransform _))
                    instance.transform.SetParent(pool.settings.parent, false);
                else
                    instance.transform.SetParent(pool.settings.parent, true);
            }

            // Ensure agents are disabled before pooling
            if (instance.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                agent.enabled = false;

            instance.SetActive(false);

            // Clean mapping
            instanceToPoolName.Remove(instance.GetInstanceID());
        }

        private static bool TryGetPoolForInstance(GameObject instance, out PoolObjects pool)
        {
            pool = null;

            if (instanceToPoolName.TryGetValue(instance.GetInstanceID(), out var poolName))
                return objectsMap.TryGetValue(poolName, out pool);

            // Fallback: best-effort prefix parse (in case mapping was lost)
            var name = instance.name;
            var underscore = name.IndexOf('_');
            var prefabKey = underscore > 0 ? name.Substring(0, underscore) : name;
            return objectsMap.TryGetValue(prefabKey, out pool);
        }

        private static PoolObjectSetting GetObjectPoolSettingsByPrefab(GameObject prefab)
        {
            foreach (var kvp in objectsMap)
            {
                if (kvp.Value.settings != null && kvp.Value.settings.prefab == prefab)
                    return kvp.Value.settings;
            }
            return null;
        }

        private static string GetPoolName(PoolObjectSetting s)
        {
            if (s == null) return null;
            if (!string.IsNullOrEmpty(s.name)) return s.name;
            return GetObjectName(s.prefab);
        }

        /// <summary>Cached prefab name (avoids GC).</summary>
        public static string GetObjectName(GameObject prefab)
        {
            if (!prefab) return null;
            int id = prefab.GetInstanceID();
            if (!cachedObjectNames.TryGetValue(id, out var name))
            {
                name = prefab.name;
                cachedObjectNames.Add(id, name);
            }
            return name;
        }
    }
}
