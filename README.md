# com.pixelwizards.objectpool

Object pooling system for Unity.

Documentation is online:
https://www.megacrush.app/api/object-pool

No editor-facing functionality.

Create new Object pools with:

GameObject prefab;

PoolManager.AddNewObjectPool(new PoolObjectSetting()
{
	count = 10,
	prefab = prefab,
});

Get an instance from the Pool:

var instance = PoolManager.GetInstance(prefab);

Return the Instance to the Pool:

PoolManager.ReturnInstance(thisObject);
