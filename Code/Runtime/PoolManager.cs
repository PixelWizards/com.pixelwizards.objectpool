using System;
using System.Collections.Generic;
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
    
    /// <summary>
    /// Actual pool containing object instances of one prefab
    /// </summary>
    [Serializable]
    public class PoolObjects
    {
        public List<GameObject> instances;
        public int currentIndex;

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

    /// <summary>
    /// Simple Pooling system for GameObjects.
    /// It instantiates multiple instances of predefined prefabs 
    /// and keeps them in a scene.
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
        /// Creates a new PoolObject which will hold instances of the prefab
        /// </summary>
        /// <param name="poolObject">Settings of the pooled prefab</param>
        private static void CreatePoolObjects(PoolObjectSetting poolObject)
        {
            var pool = new PoolObjects
            {
                instances = new List<GameObject>(poolObject.count),
                currentIndex = 0
            };

            for (int i = 0; i < poolObject.count; ++i)
            {
                var instance = Instantiate(poolObject.prefab);

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
                // add more objects if we ran out
                var poolObjSet = new PoolObjectSetting()
                {
                    prefab = prefab,
                    count = 20
                };

                CreatePoolObjects(poolObjSet);

                instance.name = name + "_" + guid.ToString();
                return GetInstance(name);
            }

            instance.name = name + "_" + guid.ToString();
            return instance;
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