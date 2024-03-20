using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelWizards.ObjectPool
{
    /// <summary>
    /// Actual pool containing object instances of one prefab
    /// </summary>
    [Serializable]
    public class PoolObjects
    {
        public List<GameObject> instances;
        public int currentIndex;

        /// <summary>
        /// Retrieve an instance from the pool and set it active
        /// </summary>
        /// <returns>a single instance of the gameObject cached in this Object Pool</returns>
        public GameObject GetInstance()
        {
            var instance = instances[currentIndex];
            instance.SetActive(true);

            if (++currentIndex >= instances.Count)
            {
                currentIndex = 0;
            }

            return instance;
        }
    }
}