using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Actual pool containing object instances of one prefab
    /// </summary>
    [Serializable]
    public class PoolObjects
    {
        public PoolObjectSetting settings;
        public List<GameObject> instances;
        public int currentIndex;

        /// <summary>
        /// Retrieve an instance from the pool and set it active
        /// </summary>
        /// <returns>a single instance of the gameObject cached in this Object Pool</returns>
        public GameObject GetInstance()
        {
            var instance = instances[currentIndex];
            if (settings.parent)
            {
                // check if this is a UI element
                if (instance.TryGetComponent(out RectTransform uiTransform))
                {
                    instance.transform.SetParent(settings.parent, false);
                }
                else
                {
                    instance.transform.parent = settings.parent;    
                }
                
            }
            instance.SetActive(true);

            if (++currentIndex >= instances.Count)
            {
                currentIndex = 0;
            }

            return instance;
        }
    }
}