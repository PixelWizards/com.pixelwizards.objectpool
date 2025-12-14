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
		// NEW: prefabID -> pool (prevents same-name prefab collisions)
		private static readonly Dictionary<int, PoolObjects> poolsByPrefabId = new();

        // Singleton instance used only for driving Update-based warmup.
        private static PoolManager _instance;

        // Simple queued expansion job.
        private class ExpansionJob
        {
            public PoolObjects pool;
            public PoolObjectSetting settings;
            public int remaining;
        }

        // Queue of expansion jobs that will be processed over multiple frames.
        private static readonly Queue<ExpansionJob> s_expansionQueue = new();

        // How many instances we’re allowed to Instantiate per frame during time-sliced expansion.
        private int maxInstantiatesPerFrame = 8;

		/// <summary>
		/// Global budget for how many pooled instances can be instantiated per frame
		/// during time-sliced expansion.
		/// </summary>
		public static int MaxInstantiatesPerFrame
		{
			get => _instance != null ? Mathf.Max(1, _instance.maxInstantiatesPerFrame) : 8;
			set
			{
				if (_instance != null)
					_instance.maxInstantiatesPerFrame = Mathf.Max(1, value);
			}
		}

        /// <summary>
        /// True while the pool is instantiating (warmup/expansion).
        /// Helpful as a guard if any legacy logic still runs in OnEnable.
        /// </summary>
        public static bool IsWarming { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Only one PoolManager should be active. Extra instances can be safely destroyed.
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void Update()
        {
            if (s_expansionQueue.Count == 0)
                return;

            IsWarming = true;

            int budget = MaxInstantiatesPerFrame;

            while (budget > 0 && s_expansionQueue.Count > 0)
            {
                var job = s_expansionQueue.Peek();
                if (job.pool == null || job.settings == null || job.remaining <= 0)
                {
                    s_expansionQueue.Dequeue();
                    continue;
                }

                int toSpawn = Mathf.Min(job.remaining, budget);
                InternalCreateInstances(job.pool, job.settings, toSpawn);

                job.remaining -= toSpawn;
                budget -= toSpawn;

                if (job.remaining <= 0)
                    s_expansionQueue.Dequeue();
            }

            if (s_expansionQueue.Count == 0)
                IsWarming = false;
        }

        /// <summary>
        /// Register a new pool and pre-instantiate its objects.
        /// </summary>
        public static void AddNewObjectPool(PoolObjectSetting thisObject)
        {
            bool expandExistingPool = false;

            // check if we have an existing pool before creating a new one
            if (thisObject != null && thisObject.prefab)
            {
                string poolName = GetPoolName(thisObject);
                if (!string.IsNullOrEmpty(poolName) && objectsMap.ContainsKey(poolName))
                    expandExistingPool = true;
            }

            // Initial setup/warmup remains synchronous (no time slicing).
            CreatePoolObjects(thisObject, expandExistingPool, timeSliced: false);
        }

        /// <summary>
        /// Create (or expand) a pool's instances.
        /// </summary>
        private static void CreatePoolObjects(PoolObjectSetting poolObject, bool expandExistingPool = false, bool timeSliced = false)
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

			int prefabId = poolObject.prefab.GetInstanceID(); // NEW

			PoolObjects pool;
			if (expandExistingPool)
			{
				if (!objectsMap.TryGetValue(poolName, out pool) || pool == null)
				{
					Debug.LogError($"PoolManager: Couldn't find existing pool '{poolName}' to expand.");
					return;
				}

				// NEW: ensure prefab->pool mapping exists
				if (!poolsByPrefabId.ContainsKey(prefabId))
					poolsByPrefabId[prefabId] = pool;
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

				// NEW
				poolsByPrefabId[prefabId] = pool;
			}

            int toCreate = Mathf.Max(0, poolObject.count);
            if (toCreate <= 0)
                return;

            if (timeSliced)
            {
                // If we don't have a driver instance, fall back to immediate expansion.
                if (_instance == null)
                {
                    Debug.LogWarning("PoolManager: No PoolManager instance in scene; falling back to immediate expansion.");
                    IsWarming = true;
                    InternalCreateInstances(pool, poolObject, toCreate);
                    IsWarming = false;
                    return;
                }

                IsWarming = true;
                s_expansionQueue.Enqueue(new ExpansionJob
                {
                    pool = pool,
                    settings = poolObject,
                    remaining = toCreate
                });
            }
            else
            {
                IsWarming = true;
                InternalCreateInstances(pool, poolObject, toCreate);
                IsWarming = false;
            }
        }

        /// <summary>
        /// Actually instantiates instances and appends them to the pool.
        /// </summary>
        private static void InternalCreateInstances(PoolObjects pool, PoolObjectSetting poolObject, int count)
        {
            for (int i = 0; i < count; ++i)
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
        }

        /// <summary>
        /// Get an instance by prefab reference. Expands if exhausted.
        /// </summary>
		public static GameObject GetInstance(GameObject prefab)
		{
			if (!prefab)
				return null;

			int prefabId = prefab.GetInstanceID();

			// NEW: use prefab identity first (prevents name collisions)
			if (poolsByPrefabId.TryGetValue(prefabId, out var pool) && pool != null)
			{
				var instance = pool.GetInstance(); // may return null if all active
				if (!instance)
				{
					// Expand using current settings clone
					var settings = pool.settings;
					if (settings != null)
					{
						settings.count = Mathf.Max(1, Mathf.Max(settings.count, 1) * 2);
						CreatePoolObjects(settings, expandExistingPool: true, timeSliced: true);
					}
					else
					{
						// Shouldn't happen, but safe fallback
						var s = new PoolObjectSetting
						{
							name = GetObjectName(prefab),
							prefab = prefab,
							count = 20
						};
						CreatePoolObjects(s, expandExistingPool: false, timeSliced: true);
					}

					instance = pool.GetInstance();
					if (!instance)
					{
						Debug.LogError($"PoolManager: Failed to fetch instance for prefab '{prefab.name}' after expansion.");
						return null;
					}
				}

				// Map instance->poolName for ReturnInstance (still fine)
				string poolName = GetObjectName(prefab);
				instanceToPoolName[instance.GetInstanceID()] = poolName;

				// Cosmetic
				instance.name = $"{poolName}_{Guid.NewGuid()}";
				return instance;
			}

			// No pool yet: create a pool for this prefab and retry
			AddNewObjectPool(new PoolObjectSetting
			{
				name = GetObjectName(prefab),
				prefab = prefab,
				count = 1
			});

			return GetInstance(prefab);
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

                    // Runtime expansions are time-sliced to avoid frame spikes.
                    CreatePoolObjects(settings, expandExistingPool: true, timeSliced: true);
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
                    CreatePoolObjects(s, expandExistingPool: false, timeSliced: true);
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
