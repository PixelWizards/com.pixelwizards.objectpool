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
                // IMPORTANT: To avoid firing OnEnable during warmup:
                // keep the prefab ASSET inactive in the project. This SetActive(false)
                // ensures runtime state, but source asset active=true still fires once on instantiate.
                var instance = poolObject.parent
                    ? UnityEngine.Object.Instantiate(poolObject.prefab, poolObject.parent)
                    : UnityEngine.Object.Instantiate(poolObject.prefab);

                instance.SetActive(false); // ensure inactive post-instantiate

                // Preserve UI parenting when needed
                if (instance.TryGetComponent(out RectTransform _))
                    instance.transform.SetParent(poolObject.parent, false);
                else if (poolObject.parent && instance.transform.parent == null)
                    instance.transform.parent = poolObject.parent;

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

            if (instance == null)
            {
                Debug.Log($"PoolManager: No inactive instances for '{name}', creating more.");
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
        public static void ReturnInstance(GameObject instance)
        {
            if (instance == null) return;

            // Give components a chance to clean up before deactivation.
            foreach (var h in instance.GetComponentsInChildren<IPooledDespawnHandler>(true))
                h.OnReturnedToPool();

            instance.SetActive(false);
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
