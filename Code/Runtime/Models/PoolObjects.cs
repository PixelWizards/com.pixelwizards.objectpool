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

			// Ring buffer scan for the next usable inactive object.
			// We also purge destroyed references as we encounter them.
			int count = instances.Count;

			for (int scan = 0; scan < count; scan++)
			{
				if (instances.Count == 0)
					return null;

				int idx = currentIndex % instances.Count;
				var go = instances[idx];

				// Advance index for next probe now (keeps ring behavior stable even on removals)
				currentIndex = (idx + 1) % instances.Count;

				// CRITICAL: Unity fake-null check FIRST.
				if (!go)
				{
					// Remove dead entry; keep scanning.
					instances.RemoveAt(idx);

					// currentIndex already points to "next" relative to old list;
					// after removal, it should step back one slot to avoid skipping.
					if (instances.Count > 0)
						currentIndex = Mathf.Clamp(currentIndex - 1, 0, instances.Count - 1);
					else
						currentIndex = 0;

					// We reduced the list, also reduce our scan budget accordingly.
					count--;
					scan--;
					continue;
				}

				// Only now is it safe to touch activeSelf / transform.
				if (go.activeSelf)
					continue;

				// Reattach to parent if needed, BUT only if parent still exists.
				if (settings != null && settings.parent)
				{
					if (go.TryGetComponent(out RectTransform _))
						go.transform.SetParent(settings.parent, false);
					else
						go.transform.SetParent(settings.parent, true);
				}

				// Notify that this is a REAL spawn (not warmup)
				foreach (var h in go.GetComponentsInChildren<IPooledSpawnHandler>(true))
					h.OnSpawnedFromPool();

				return go;
			}

			return null; // all active
		}

    }
}
