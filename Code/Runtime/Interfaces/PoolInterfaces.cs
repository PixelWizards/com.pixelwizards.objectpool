
namespace MegaCrush.ObjectPool.Interfaces
{
    /// <summary>
    /// Invoked by the pool immediately after an instance is ACTIVATED and handed out.
    /// Use this for SFX/VFX or any "spawn-time" setup you do not want to run during warmup.
    /// </summary>
    public interface IPooledSpawnHandler
    {
        void OnSpawnedFromPool();
    }

    /// <summary>
    /// Invoked by the pool immediately before an instance is DEACTIVATED and returned.
    /// Use this to stop loops, clear state, and unsubscribe events.
    /// </summary>
    public interface IPooledDespawnHandler
    {
        void OnReturnedToPool();
    }
}