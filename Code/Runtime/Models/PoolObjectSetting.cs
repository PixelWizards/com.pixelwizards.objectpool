using System;
using UnityEngine;

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Which prefab we want to pool and how many copies.
    /// </summary>
    [Serializable]
    public class PoolObjectSetting
    {
        [Tooltip("Optional explicit pool name. If empty, falls back to prefab.name")]
        public string name;

        [Tooltip("Create the pooled instances under this parent (optional)")]
        public Transform parent;

        [Tooltip("Prefab to pool")]
        public GameObject prefab;

        [Tooltip("Initial pool size")]
        public int count = 10;
    }
}