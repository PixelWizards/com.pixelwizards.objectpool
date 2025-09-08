using System;
using System.Collections.Generic;
using UnityEngine;
using MegaCrush.ObjectPool.Interfaces;

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Simple pooling system for GameObjects.
    /// </summary>
    public class PoolManager : MonoBehaviour
    {
        private static readonly Dictionary<string, PoolObjects> objectsMap = new();
        private static readonly Dictionary<int, string> cachedObjectNames = new();

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
            IsWarming = true;

            PoolObjects pool;
            if (expandExistingPool)
            {
                if (!objectsMap.TryGetValue(poolObject.prefab.name, out pool) || pool == null)
                {
                    Debug.LogError("PoolManager: Couldn't find existing pool to expand.");
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
                objectsMap[poolObject.prefab.name] = pool;
            }

            for (int i = 0; i < poolObject.count; ++i)
            {
                var instance = poolObject.parent
                    ? UnityEngine.Object.Instantiate(poolObject.prefab, poolObject.parent)
                    : UnityEngine.Object.Instantiate(poolObject.prefab);

                // NEW: if it has an agent, disable it until placement
                if (instance.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                    agent.enabled = false;
                
                instance.SetActive(false); // keep inactive during warmup

                // ★ Always enforce correct parenting semantics after instantiate
                if (poolObject.parent)
                {
                    if (instance.TryGetComponent(out RectTransform _))
                        instance.transform.SetParent(poolObject.parent, false); // UI: keep local values sane
                    else
                        instance.transform.SetParent(poolObject.parent, true); // non-UI: preserve world
                }

                // ★ (Optional) Normalize UI rect once on warmup to avoid prefab drift
                // if (instance.TryGetComponent(out RectTransform rt))
                //     ResetUIRect(rt);

                pool.instances.Add(instance);
            }

            IsWarming = false;
        }


        /// <summary>
        /// Get an instance by prefab reference. Expands if exhausted.
        /// </summary>
        public static GameObject GetInstance(GameObject prefab)
        {
            var name = GetObjectName(prefab);
            var instance = GetInstance(name);

            if (!instance)
            {
//                Debug.Log($"PoolManager: No inactive instances for '{name}', creating more.");
                var settings = GetObjectPoolSettings(prefab);
                if (settings != null)
                {
                    settings.count += 20; // growth step
                    CreatePoolObjects(settings, expandExistingPool: true);
                }
                else
                {
                    settings = new PoolObjectSetting
                    {
                        prefab = prefab,
                        count = 20
                    };
                    CreatePoolObjects(settings, expandExistingPool: false);
                }

                instance = GetInstance(name);
                if (instance == null)
                {
                    Debug.LogError($"PoolManager: Failed to fetch instance for '{name}' after expansion.");
                    return null;
                }
            }

            instance.name = $"{name}_{Guid.NewGuid()}";
            return instance; // return the one we actually fetched & named
        }

        /// <summary>
        /// Get an instance by pool name (prefab name).
        /// </summary>
        public static GameObject GetInstance(string name)
        {
            if (!objectsMap.TryGetValue(name, out var pool))
                return null;

            return pool.GetInstance(); // returns null if all are active
        }

        /// <summary>
        /// Return an instance to the pool.
        /// </summary>
        /// <remarks>
        /// In addition to disabling the instance, any <see cref="UnityEngine.AI.NavMeshAgent"/> on the instance
        /// is explicitly disabled to ensure safe re-checkout and placement by runtime systems.
        /// </remarks>
        public static void ReturnInstance(GameObject instance)
        {
            if (instance == null) return;

            foreach (var h in instance.GetComponentsInChildren<IPooledDespawnHandler>(true))
                h.OnReturnedToPool();

            // ★ Reparent back to the pool's configured parent, if any
            if (TryGetPoolForInstance(instance, out var pool) && pool?.settings?.parent)
            {
                // UI vs non-UI semantics
                if (instance.TryGetComponent(out RectTransform _))
                    instance.transform.SetParent(pool.settings.parent, false);
                else
                    instance.transform.SetParent(pool.settings.parent, true);
            }

            // NEW: ensure agents are disabled before pooling (prevents off-mesh agent activation on next checkout)
            if (instance.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                agent.enabled = false;

            instance.SetActive(false);
        }
        
        // ★ Helper: map an instance back to its pool by prefab name key.
        // Easiest robust way: look up by original prefab name prefix (you already rename on GetInstance).
        private static bool TryGetPoolForInstance(GameObject instance, out PoolObjects pool)
        {
            pool = null;
            // Your GetInstance renames to $"{prefabName}_{GUID}"
            // Extract prefix up to first '_' to recover prefabName.
            var name = instance.name;
            var underscore = name.IndexOf('_');
            var prefabKey = underscore > 0 ? name.Substring(0, underscore) : name;

            return objectsMap.TryGetValue(prefabKey, out pool);
        }


        private static PoolObjectSetting GetObjectPoolSettings(GameObject prefab)
        {
            foreach (var kvp in objectsMap)
            {
                if (kvp.Value.settings.prefab == prefab)
                    return kvp.Value.settings;
            }

            return null;
        }

        /// <summary>
        /// Cached prefab name (avoids GC).
        /// </summary>
        public static string GetObjectName(GameObject prefab)
        {
            if (!cachedObjectNames.TryGetValue(prefab.GetInstanceID(), out var name))
            {
                name = prefab.name;
                cachedObjectNames.Add(prefab.GetInstanceID(), name);
            }

            return name;
        }
    }
}
