using System;
using UnityEngine;

namespace PixelWizards.ObjectPool
{
    /// <summary>
    /// Which prefab we want to pool and how many copies
    /// </summary>
    [Serializable]
    public class PoolObjectSetting
    {
        public GameObject prefab;
        public int count;
    }
}