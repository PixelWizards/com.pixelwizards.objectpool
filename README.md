# com.pixelwizards.objectpool

Lightweight object pooling for Unity.

**Docs:** https://www.megacrush.app/api/object-pool
  
---  

## Quick start

    using MegaCrush.ObjectPool;  
    using UnityEngine;  
      
    public class Example : MonoBehaviour  
    {  
     [SerializeField] GameObject prefab;  
      void Awake() 
      { 
       // Create a pool (e.g., 10 preallocated) 
       PoolManager.AddNewObjectPool(new oolObjectSetting { prefab = prefab, count  = 10, });
             
       // (Optional) Prewarm more for smoother frames 
       // PoolManager.Prewarm(prefab, 20 /*extra*/, parent: null);
      }  
      void Spawn() 
      { 
       // Get an instance (returned INACTIVE by default) 
       var go = PoolManager.GetInstance(prefab);  
       // Position / setup, then activate 
       go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); 
       go.SetActive(true); 
      }  
      void Despawn(GameObject go) 
      { 
       // Return to pool (idempotent / safe) 
       PoolManager.ReturnInstance(go); 
      }
    }

## API cheatsheet

-   `AddNewObjectPool(PoolObjectSetting setting)`

    -   Creates a pool for `setting.prefab` with initial `setting.count`.

    -   (Package ≥ 1.2) Supports organizing under a parent transform for UI workflows.

-   `GetInstance(GameObject prefab)` → `GameObject`

    -   Retrieves an **inactive** instance from the pool (or grows as needed).

-   `ReturnInstance(GameObject instance)`

    -   Returns an instance to its pool (safe to call once; ignores destroyed objects).

-   _(Package ≥ 1.2, optional helpers)_

    -   `EnsurePool(GameObject prefab, int capacity)`

    -   `Prewarm(GameObject prefab, int count, Transform parent = null)`


----------

## Notes

-   Keep instances **inactive** until positioned/configured, then call `SetActive(true)`.

-   Pools expand gradually to avoid spikes.

-   Designed to work well with gameplay objects **and** UI elements.

