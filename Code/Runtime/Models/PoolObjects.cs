using System;
using System.Collections.Generic;
using UnityEngine;
using MegaCrush.ObjectPool.Interfaces;

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Holds instances for a single prefab pool.
    /// </summary>
    [Serializable]
    public class PoolObjects
    {
        public PoolObjectSetting settings;
        public List<GameObject> instances;
        public int currentIndex;
        
        public string poolName;

        /// <summary>
        /// Retrieve the next INACTIVE instance, activate it, and notify spawn handlers.
        /// Returns null if all instances are currently active.
        /// </summary>
        public GameObject GetInstance()
        {
            if (instances == null || instances.Count == 0)
                return null;

            // Find the next inactive object (ring buffer)
            for (int i = 0; i < instances.Count; i++)
            {
                int idx = (currentIndex + i) % instances.Count;
                var go = instances[idx];
                if (!go.activeSelf)
                {
                    currentIndex = (idx + 1) % instances.Count;

                    // Reattach to parent if needed
                    if (settings.parent)
                    {
                        if (go.TryGetComponent(out RectTransform _))
                            go.transform.SetParent(settings.parent, false);
                        else
                            go.transform.parent = settings.parent;
                    }

                    // Notify that this is a REAL spawn (not warmup)
                    foreach (var h in go.GetComponentsInChildren<IPooledSpawnHandler>(true))
                        h.OnSpawnedFromPool();

                    return go;
                }
            }

            // All active → let PoolManager expand at the higher level (GetInstance(prefab) path)
            return null;
        }
    }
}