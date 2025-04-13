using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaCrush.ObjectPool
{
    /// <summary>
    /// Simple Pooling system for GameObjects.
    /// It instantiates multiple instances of predefined prefabs 
    /// and keeps them in a scene.
    ///
    /// The Pool Manager does not need to be instantiated in the scene, all
    /// methods are static and can be accessed without a reference, like so:
    ///
    /// PoolManager.GetInstance("prefabName");
    /// </summary>
    public class PoolManager : MonoBehaviour
    {
        private static Dictionary<string, PoolObjects> objectsMap = new Dictionary<string, PoolObjects>();
        private static Dictionary<int, string> cachedObjectNames = new Dictionary<int, string>();

        /// <summary>
        /// Create a new pool and prep it 
        /// </summary>
        /// <param name="thisObject"></param>
        public static void AddNewObjectPool( PoolObjectSetting thisObject)
        {
            CreatePoolObjects(thisObject);
        }

        /// <summary>
        /// Creates a new PoolObject which will hold instances of the prefab.
        /// </summary>
        /// <param name="poolObject">Settings of the pooled prefab</param>
        private static void CreatePoolObjects(PoolObjectSetting poolObject, bool expandExistingPool = false)
        {
            var pool = new PoolObjects();
            if (expandExistingPool)
            {
                Debug.Log("expanding pool...");
                pool = objectsMap[poolObject.prefab.name];
                if (pool == null)
                {
                    Debug.LogError("Couldn't find existing pool to expand");
                }
            }
            else
            {
                Debug.Log("create new pool...");
                pool = new PoolObjects
                {
                    settings =  poolObject,
                    instances = new List<GameObject>(),
                    currentIndex = 0
                };
    
            }
            
            for (var i = 0; i < poolObject.count; ++i)
            {
                var instance = poolObject.parent ? Instantiate(poolObject.prefab, poolObject.parent) : Instantiate(poolObject.prefab);
                if (instance.TryGetComponent(out RectTransform rectTransform))
                {
                    instance.transform.SetParent(poolObject.parent,false);
                }
                else
                {
                    if (poolObject.parent && instance.transform.parent == null)
                    {
                        instance.transform.parent = poolObject.parent;
                    }  
                }
                
                instance.SetActive(false);

                pool.instances.Add(instance);
            }

            objectsMap[poolObject.prefab.name] = pool;
        }

        /// <summary>
        /// Returns pooled GameObject. If it does not exists in the pool,
        /// it creates a new pool for it.
        /// </summary>
        /// <param name="prefab">Which prefab (template) to pool</param>
        /// <returns>Pooled GameObject</returns>
        public static GameObject GetInstance(GameObject prefab)
        {
            var name = GetObjectName(prefab);
            var instance = GetInstance(name);
            // give this instance a unique name

            var guid = Guid.NewGuid();
            if (instance == null)
            {
                Debug.Log("NO objects in pool, creating more!");
                var settings = GetObjectPoolSettings(prefab);
                if (settings != null)
                {
                    // expand existing pool
                    settings.count += 20;
                    // create more objects for the pool
                    CreatePoolObjects(settings, true);
                }
                else
                {
                    // couldn't find the existing pool, create a new one
                    settings = new PoolObjectSetting()
                    {
                        prefab = prefab,
                        count = 20,
                    };
                    // create more objects for the pool
                    CreatePoolObjects(settings, false);
                }

                
                // and then get an instance
                if (instance == null)
                {
                    instance = GetInstance(prefab);
                }
                instance.name = name + "_" + guid.ToString();
                return GetInstance(name);
            }

            instance.name = name + "_" + guid.ToString();
            return instance;
        }

        private static PoolObjectSetting GetObjectPoolSettings(GameObject prefab)
        {
            foreach (var existingPools in objectsMap)
            {
                if (existingPools.Value.settings.prefab == prefab)
                {
                    return existingPools.Value.settings;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns pooled object by its name.
        /// </summary>
        /// <param name="name">Name of the GameObject</param>
        /// <returns>Pooled GameObject</returns>
        public static GameObject GetInstance(string name)
        {
            PoolObjects pool;
            if (!objectsMap.TryGetValue(name, out pool))
            {
                return null;
            }
            return pool.GetInstance();
        }

        /// <summary>
        /// Return object to the pool.
        /// </summary>
        /// <param name="instance">GameObject to return</param>
        public static void ReturnInstance(GameObject instance)
        {
            instance.SetActive(false);
        }

        /// <summary>
        /// Gets name of the prefab. 
        /// Directly accessing prefab.name creates a memory garbage in Unity,
        /// so we cached the names and access them by prefab Ids.
        /// </summary>
        /// <param name="prefab">The prefab</param>
        /// <returns>Prefab's name</returns>
        public static string GetObjectName(GameObject prefab)
        {
            string name;
            if (!cachedObjectNames.TryGetValue(prefab.GetInstanceID(), out name))
            {
                name = prefab.name;

                cachedObjectNames.Add(prefab.GetInstanceID(), name);
            }

            return name;
        }
    }
}