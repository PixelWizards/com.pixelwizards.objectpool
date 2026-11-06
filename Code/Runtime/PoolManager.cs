using System;
using System.Collections.Generic;
using UnityEngine;
using MegaCrush.ObjectPool.Interfaces;
using UnityEngine.SceneManagement;

#if UNITY_6000_0_OR_NEWER
using PoolObjectId = UnityEngine.EntityId;
#else
using PoolObjectId = System.Int32;
#endif

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Simple pooling system for GameObjects
    /// </summary>
    public class PoolManager : MonoBehaviour
    {
		[SerializeField] private bool logPoolFlushOnSceneLoad = true;

        private static readonly Dictionary<string, PoolObjects> objectsMap = new();          // key: poolName
        private static readonly Dictionary<PoolObjectId, string> cachedObjectNames = new();           // prefabID -> prefabName
        private static readonly Dictionary<PoolObjectId, string> instanceToPoolName = new();          // instanceID -> poolName
        // prefabID -> pool (prevents same-name prefab collisions)
        private static readonly Dictionary<PoolObjectId, PoolObjects> poolsByPrefabId = new();

        // Fixed expansion step policy (clamped)
        private const int kMinExpansionStep = 4;
        private const int kMaxExpansionStep = 32;

        // Singleton instance used only for driving Update-based warmup.
        private static PoolManager _instance;

        // Simple queued expansion job.
        private class ExpansionJob
        {
            public PoolObjects pool;
            public PoolObjectSetting settings;
            public int remaining;
        }

        // domain reload cleanup
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            objectsMap.Clear();
            cachedObjectNames.Clear();
            instanceToPoolName.Clear();
            poolsByPrefabId.Clear();

            s_expansionQueue.Clear();

            _instance = null;
            IsWarming = false;
        }

		private void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
		}

		private void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			// Only nuke on full scene loads (not additive).
			if (mode != LoadSceneMode.Single)
				return;

			NukeAllPools(scene);
		}

        /// <summary>Discard previous-scene instances, preserving warmup performed by new-scene Awake methods.</summary>
        private static void NukeAllPools(Scene loadedScene)
        {
            var retainedIds = new HashSet<PoolObjectId>();
            int discarded = 0;
            foreach (var pool in objectsMap.Values)
            {
                if (pool?.instances == null) continue;
                for (int i = pool.instances.Count - 1; i >= 0; i--)
                {
                    var instance = pool.instances[i];
                    if (instance && instance.scene == loadedScene)
                    {
                        retainedIds.Add(GetObjectId(instance));
                        continue;
                    }
                    if (instance) UnityEngine.Object.Destroy(instance);
                    pool.instances.RemoveAt(i);
                    discarded++;
                }
                pool.currentIndex = 0;
            }
            var staleIds = new List<PoolObjectId>();
            foreach (var id in instanceToPoolName.Keys)
                if (!retainedIds.Contains(id)) staleIds.Add(id);
            foreach (var id in staleIds) instanceToPoolName.Remove(id);

            int jobs = s_expansionQueue.Count;
            for (int i = 0; i < jobs; i++)
            {
                var job = s_expansionQueue.Dequeue();
                if (job.pool?.instances != null && job.pool.instances.Count > 0)
                    s_expansionQueue.Enqueue(job);
            }
            IsWarming = s_expansionQueue.Count > 0;
            // Keep prefab registrations: EnsurePoolCapacity measures actual surviving instances.
            if (discarded > 0 && (_instance == null || _instance.logPoolFlushOnSceneLoad))
                Debug.Log($"[PoolManager] Previous-scene cleanup: '{loadedScene.name}' (discarded={discarded}).");
        }

        // Queue of expansion jobs that will be processed over multiple frames.
        private static readonly Queue<ExpansionJob> s_expansionQueue = new();

        // How many instances we’re allowed to Instantiate per frame during time-sliced expansion.
        [SerializeField] private int maxInstantiatesPerFrame = 8;

        private static void EnsureInstance()
        {
            if (_instance) return;

            // Do not auto-create while not playing.
            if (!Application.isPlaying)
                return;

            var existing = FindFirstObjectByType<PoolManager>();
            if (existing) { _instance = existing; return; }

            var go = new GameObject("[PoolManager]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PoolManager>();
        }

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

        private static int ComputeExpansionStep(PoolObjectSetting settings)
        {
            // Treat settings.count as a "runtime growth step hint", not a "target pool size".
            int hint = settings != null ? settings.count : 0;
            return Mathf.Clamp(hint <= 0 ? kMinExpansionStep : hint, kMinExpansionStep, kMaxExpansionStep);
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

        /// <summary>Number of surviving instances for this prefab, including borrowed instances.</summary>
        public static int GetPoolCapacity(GameObject prefab)
        {
            if (!prefab || !poolsByPrefabId.TryGetValue(GetObjectId(prefab), out var pool) || pool?.instances == null) return 0;
            int count = 0;
            foreach (var instance in pool.instances) if (instance) count++;
            return count;
        }

        /// <summary>Warm a complete plan without spawning. Budget is shared across all prefabs in this call.
        /// Time budget is checked between instances: a single expensive Instantiate cannot be interrupted.
        /// Caller owns iteration/cancellation; existing capacity survives cancellation and can be reused.</summary>
        public static System.Collections.IEnumerator PrewarmPoolsAsync(
            IEnumerable<KeyValuePair<GameObject, int>> requests, int instancesPerFrame = 8,
            float millisecondsPerFrame = 2f, Action<float> onProgress = null)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            var plan = new Dictionary<GameObject, int>();
            foreach (var request in requests)
            {
                if (!request.Key || request.Value <= 0) continue;
                plan.TryGetValue(request.Key, out int previous);
                plan[request.Key] = Mathf.Max(previous, request.Value);
            }
            long total = 0;
            foreach (var request in plan) total += Math.Max(0, request.Value - GetPoolCapacity(request.Key));
            onProgress?.Invoke(total == 0 ? 1f : 0f);
            if (total == 0) yield break;
            EnsureInstance();
            // Give an authored loading screen a frame before the first construction work.
            yield return null;
            int budget = Mathf.Max(1, instancesPerFrame);
            int madeThisFrame = 0;
            long completed = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var request in plan)
            {
                if (!request.Key) throw new InvalidOperationException("A prewarm prefab was destroyed during preparation.");
                int capacity = GetPoolCapacity(request.Key);
                while (capacity < request.Value)
                {
                    if (madeThisFrame >= budget || (madeThisFrame > 0 && millisecondsPerFrame > 0f && clock.Elapsed.TotalMilliseconds >= millisecondsPerFrame))
                    {
                        yield return null;
                        madeThisFrame = 0;
                        clock.Restart();
                    }
                    EnsurePoolCapacity(request.Key, capacity + 1);
                    int actual = GetPoolCapacity(request.Key);
                    if (actual <= capacity) throw new InvalidOperationException($"Pool warmup made no progress for '{request.Key.name}'.");
                    completed += actual - capacity;
                    capacity = actual;
                    madeThisFrame++;
                    onProgress?.Invoke(Mathf.Min(1f, (float)completed / total));
                }
            }
            onProgress?.Invoke(1f);
        }

        /// <summary>Ensure a total capacity without repeatedly adding the requested count.</summary>
        public static void EnsurePoolCapacity(GameObject prefab, int capacity)
        {
            if (!prefab || capacity <= 0) return;
            EnsureInstance();
            int existing = 0;
            if (poolsByPrefabId.TryGetValue(GetObjectId(prefab), out var pool) && pool != null)
                foreach (var instance in pool.instances)
                    if (instance) existing++;
            if (existing < capacity)
                AddNewObjectPool(new PoolObjectSetting { prefab = prefab, count = capacity - existing });
        }

        /// <summary>True for a currently borrowed instance, independent of its name.</summary>
        public static bool IsBorrowedInstance(GameObject instance) =>
            instance && instanceToPoolName.ContainsKey(GetObjectId(instance));

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
        /// NOTE: poolObject.count is interpreted as "add this many instances".
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

            PoolObjectId prefabId = GetObjectId(poolObject.prefab);

            PoolObjects pool;
            if (expandExistingPool)
            {
                if (!objectsMap.TryGetValue(poolName, out pool) || pool == null)
                {
                    Debug.LogError($"PoolManager: Couldn't find existing pool '{poolName}' to expand.");
                    return;
                }

                // Ensure prefab->pool mapping exists
                if (!poolsByPrefabId.ContainsKey(prefabId))
                    poolsByPrefabId[prefabId] = pool;
            }
            else
            {
                pool = new PoolObjects
                {
                    settings = poolObject,
                    instances = new List<GameObject>(),
                    currentIndex = 0,
                    poolName = poolName
                };
                objectsMap[poolName] = pool;

                poolsByPrefabId[prefabId] = pool;
            }

            int toCreate = Mathf.Max(0, poolObject.count);
            if (toCreate <= 0)
                return;

            if (timeSliced)
            {
                // Ensure we have a driver instance, otherwise fall back to immediate expansion.
                EnsureInstance();

                if (!_instance)
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
                    remaining = toCreate,
                });
            }
            else
            {
                bool wasWarming = IsWarming;
                IsWarming = true;
                try { InternalCreateInstances(pool, poolObject, toCreate); }
                finally { IsWarming = wasWarming; }
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
        /// Hybrid expansion:
        /// - create a small number immediately so the requesting call can succeed this frame
        /// - queue the remainder for time-sliced expansion to avoid spikes
        /// </summary>
        private static void ExpandPoolHybrid(PoolObjects pool, PoolObjectSetting settings, int immediateCount, int queuedCount)
        {
            if (pool == null || settings == null) return;

            immediateCount = Mathf.Max(0, immediateCount);
            queuedCount = Mathf.Max(0, queuedCount);

            // Satisfy the requesting frame first (small, bounded cost)
            if (immediateCount > 0)
            {
                IsWarming = true;
                InternalCreateInstances(pool, settings, immediateCount);
                IsWarming = false;
            }

            // Then time-slice the rest (clone settings so we don't mutate pool.settings)
            if (queuedCount > 0)
            {
                CreatePoolObjects(new PoolObjectSetting
                {
                    name = settings.name,
                    parent = settings.parent,
                    prefab = settings.prefab,
                    count = queuedCount
                }, expandExistingPool: true, timeSliced: true);
            }
        }

        /// <summary>
        /// Get an instance by prefab reference. Expands if exhausted.
        /// </summary>
        public static GameObject GetInstance(GameObject prefab)
        {
            return GetInstanceInternal(prefab, activate: true, hasTransform: false, Vector3.zero, Quaternion.identity, null);
        }

        /// <summary>
        /// Get an instance by prefab reference, place it before activation, then activate it.
        /// Mirrors the important lifecycle behavior of Object.Instantiate(prefab, position, rotation, parent).
        /// </summary>
        public static GameObject GetInstance(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return GetInstanceInternal(prefab, activate: true, hasTransform: true, position, rotation, parent);
        }

        /// <summary>
        /// Get an inactive instance by prefab reference. Expands if exhausted.
        /// </summary>
        public static GameObject GetInactiveInstance(GameObject prefab)
        {
            return GetInstanceInternal(prefab, activate: false, hasTransform: false, Vector3.zero, Quaternion.identity, null);
        }

        /// <summary>
        /// Get an inactive instance by prefab reference and place it before returning.
        /// Useful for callers that need additional policy checks before activation.
        /// </summary>
        public static GameObject GetInactiveInstance(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return GetInstanceInternal(prefab, activate: false, hasTransform: true, position, rotation, parent);
        }

        private static GameObject GetInstanceInternal(
            GameObject prefab, bool activate, bool hasTransform,
            Vector3 position, Quaternion rotation, Transform parent)
        {
            if (!prefab)
                return null;

            PoolObjectId prefabId = GetObjectId(prefab);

            // Use prefab identity first (prevents name collisions)
            if (poolsByPrefabId.TryGetValue(prefabId, out var pool) && pool != null)
            {
                var instance = BorrowFromPool(pool, activate, hasTransform, position, rotation, parent); // may return null if all active
                if (!instance)
                {
                    var settings = pool.settings;

                    if (settings != null)
                    {
                        int step = ComputeExpansionStep(settings);
                        ExpandPoolHybrid(pool, settings, immediateCount: 1, queuedCount: step - 1);
                    }
                    else
                    {
                        // No settings: create a small default step (same policy)
                        var fallback = new PoolObjectSetting
                        {
                            name = GetObjectName(prefab),
                            parent = null,
                            prefab = prefab,
                            count = kMinExpansionStep
                        };
                        ExpandPoolHybrid(pool, fallback, immediateCount: 1, queuedCount: fallback.count - 1);
                    }

                    instance = BorrowFromPool(pool, activate, hasTransform, position, rotation, parent);
                    if (!instance)
                    {
                        Debug.LogError($"PoolManager: Failed to fetch instance for prefab '{prefab.name}' after expansion.");
                        return null;
                    }
                }

                return instance;
            }

            // No pool yet: create a pool for this prefab and retry.
            // Use a reasonable initial size so the first burst doesn't immediately exhaust.
            AddNewObjectPool(new PoolObjectSetting
            {
                name = GetObjectName(prefab),
                prefab = prefab,
                count = kMinExpansionStep
            });

            return GetInstanceInternal(prefab, activate, hasTransform, position, rotation, parent);
        }

        private static GameObject BorrowFromPool(
            PoolObjects pool,
            bool activate,
            bool hasTransform,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            if (pool == null)
                return null;

            var instance = hasTransform ? pool.GetInactiveInstance(position, rotation, parent) : pool.GetInactiveInstance();
            if (!instance) return null;
            instanceToPoolName[GetObjectId(instance)] = pool.poolName;
            if (activate) PoolObjects.ActivateInstance(instance);
            return instance;
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

            var instance = BorrowFromPool(pool, true, false, Vector3.zero, Quaternion.identity, null);
            if (!instance)
            {
                var settings = pool.settings;

                if (settings != null)
                {
                    int step = ComputeExpansionStep(settings);
                    ExpandPoolHybrid(pool, settings, immediateCount: 1, queuedCount: step - 1);
                }
                else if (prefabForFallbackExpansion != null)
                {
                    var fallback = new PoolObjectSetting
                    {
                        name = poolName,
                        prefab = prefabForFallbackExpansion,
                        count = kMinExpansionStep
                    };
                    ExpandPoolHybrid(pool, fallback, immediateCount: 1, queuedCount: fallback.count - 1);
                }
                else
                {
                    Debug.LogError($"PoolManager: Cannot expand pool '{poolName}' (missing settings).");
                    return null;
                }

                instance = BorrowFromPool(pool, true, false, Vector3.zero, Quaternion.identity, null);
                if (!instance)
                {
                    Debug.LogError($"PoolManager: Failed to fetch instance for '{poolName}' after expansion.");
                    return null;
                }
            }

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
			else
			{
				instance.transform.SetParent(null, true);
			}


            // Ensure agents are disabled before pooling
            if (instance.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                agent.enabled = false;

            instance.SetActive(false);

            // Clean mapping
            instanceToPoolName.Remove(GetObjectId(instance));
        }

        private static bool TryGetPoolForInstance(GameObject instance, out PoolObjects pool)
        {
            pool = null;

            if (instanceToPoolName.TryGetValue(GetObjectId(instance), out var poolName))
                return objectsMap.TryGetValue(poolName, out pool);

            // Fallback: best-effort prefix parse (in case mapping was lost)
            var name = instance.name;
            var underscore = name.IndexOf('_');
            var prefabKey = underscore > 0 ? name.Substring(0, underscore) : name;
            bool ok = objectsMap.TryGetValue(prefabKey, out pool);
            if (!ok)
                Debug.LogWarning($"PoolManager: instance->pool mapping missing and fallback failed for '{instance.name}'.");
            return ok;
        }

        private static string GetPoolName(PoolObjectSetting s)
        {
            if (s == null) return null;
            if (!string.IsNullOrEmpty(s.name)) return s.name;
            return GetObjectName(s.prefab);
        }

        private static PoolObjectId GetObjectId(UnityEngine.Object obj)
        {
#if UNITY_6000_0_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>Cached prefab name (avoids GC).</summary>
        public static string GetObjectName(GameObject prefab)
        {
            if (!prefab) return null;
            PoolObjectId id = GetObjectId(prefab);
            if (!cachedObjectNames.TryGetValue(id, out var name))
            {
                name = prefab.name;
                cachedObjectNames.Add(id, name);
            }
            return name;
        }
    }
}
