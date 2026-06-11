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

        /// <summary>
        /// Retrieve the next INACTIVE instance, activate it, and notify spawn handlers.
        /// Returns null if all instances are currently active.
        /// </summary>
        public GameObject GetInstance()
        {
            var go = GetInactiveInstance();
            if (!go)
                return null;

            ActivateInstance(go);
            return go;
        }

        /// <summary>
        /// Retrieve the next INACTIVE instance, move it to the requested transform, activate it,
        /// and notify spawn handlers. This mirrors the useful lifecycle behavior of
        /// Object.Instantiate(prefab, position, rotation, parent): placement happens before activation.
        /// </summary>
        public GameObject GetInstance(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var go = GetInactiveInstance(position, rotation, parent);
            if (!go)
                return null;

            ActivateInstance(go);
            return go;
        }

        /// <summary>
        /// Retrieve the next INACTIVE instance without activating it.
        /// Returns null if all instances are currently active.
        /// </summary>
        public GameObject GetInactiveInstance()
        {
            var go = AcquireInactiveInstance();
            if (!go)
                return null;

            ApplyDefaultParent(go);
            return go;
        }

        /// <summary>
        /// Retrieve the next INACTIVE instance and move it to the requested transform without activating it.
        /// This is useful when a caller needs to run additional placement or policy checks before activation.
        /// </summary>
        public GameObject GetInactiveInstance(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var go = AcquireInactiveInstance();
            if (!go)
                return null;

            ApplyTransform(go, position, rotation, parent);
            return go;
        }

        private GameObject AcquireInactiveInstance()
        {
            if (instances == null || instances.Count == 0)
                return null;

            // Find the next inactive object (ring buffer)
            for (int i = 0; i < instances.Count; i++)
            {
                int idx = (currentIndex + i) % instances.Count;
                var go = instances[idx];
                if (go != null && !go.activeSelf)
                {
                    currentIndex = (idx + 1) % instances.Count;
                    return go;
                }
            }

            // All active → let PoolManager expand at the higher level.
            return null;
        }

        private void ApplyDefaultParent(GameObject go)
        {
            if (!go || settings == null || !settings.parent)
                return;

            if (go.TryGetComponent(out RectTransform _))
                go.transform.SetParent(settings.parent, false);
            else
                go.transform.parent = settings.parent;
        }

        private void ApplyTransform(GameObject go, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (!go)
                return;

            var targetParent = parent ? parent : settings?.parent;

            if (targetParent)
            {
                // Match Instantiate(prefab, position, rotation, parent) semantics: world position/rotation
                // are applied before activation while still parenting the instance under the requested parent.
                go.transform.SetParent(targetParent, true);
            }
            else
            {
                go.transform.SetParent(null, true);
            }

            go.transform.SetPositionAndRotation(position, rotation);
        }

        private static void ActivateInstance(GameObject go)
        {
            if (!go)
                return;

            go.SetActive(true);

            // Notify that this is a REAL spawn (not warmup)
            foreach (var h in go.GetComponentsInChildren<IPooledSpawnHandler>(true))
                h.OnSpawnedFromPool();
        }
    }
}
