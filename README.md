# com.pixelwizards.objectpool

Object pooling system for Unity.

Documentation is online:
https://www.megacrush.app/api/object-pool

No editor-facing functionality. 

Create new Object pools with:

```c#
GameObject prefab;

PoolManager.AddNewObjectPool(new PoolObjectSetting()
{
	count = 10,			// number of instances to init the pool with
	prefab = prefab,	// the prefab to use
});
```

Get an instance from the Pool:

```c#
var instance = PoolManager.GetInstance(prefab);
```

Return the Instance to the Pool:

```c#
PoolManager.ReturnInstance(thisObject);
```
