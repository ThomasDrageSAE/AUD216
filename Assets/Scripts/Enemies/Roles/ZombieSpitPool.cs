using UnityEngine;
using Core.Pooling;

/*
 * ZombieSpitPool.cs
 *
 * Purpose: Manages a pool of spit projectiles for the Demon's spitter role.
 * Used by: ZombieSpitter, ZombieSpit
 *
 * ArrowPool with the roles reversed - the same singleton, the same container, the same
 * Core.Pooling.ObjectPool<T>, because a wave night can have four Demons spitting every
 * four seconds and a projectile that is instantiated per shot is a projectile that
 * stutters the frame it is fired on.
 *
 * One per scene, made by Tools > Enemies > Add Enemy Roles under Game Management. No
 * DontDestroyOnLoad: this is a single-scene game and a reload is the restart path, so
 * the pool is rebuilt with everything else.
 *
 * Performance Considerations:
 * - Pre-instantiates spits to avoid runtime allocation
 * - Recycles rather than destroys; ObjectPool grows on demand if a night is busy
 * - The physics state is reset by ZombieSpit.Launch, not here
 *
 * Dependencies:
 * - Core.Pooling.ObjectPool<T>
 * - ZombieSpit.cs (for projectile behaviour)
 * - Requires a spit prefab with a Rigidbody, a non-trigger SphereCollider and ZombieSpit
 */

public class ZombieSpitPool : MonoBehaviour
{
    public static ZombieSpitPool Instance { get; private set; }

    [Tooltip("The projectile this pool hands out. Built and wired by " +
             "Tools > Enemies > Add Enemy Roles; without it the pool is inert and every " +
             "Demon falls back to biting")]
    [SerializeField] private ZombieSpit spitPrefab;

    [Tooltip("How many spits exist before the first one is fired. Four Demons at one " +
             "spit every four seconds, each in the air for under two, never need many - " +
             "the pool grows by itself if a night proves otherwise")]
    [SerializeField] private int initialPoolSize = 8;

    [Tooltip("Where parked spits are kept. Left empty, one is made as a child of this " +
             "object at startup, which is what the setup tool does")]
    [SerializeField] private Transform poolContainer;

    private ObjectPool<ZombieSpit> pool;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        if (poolContainer == null)
        {
            poolContainer = new GameObject("Zombie Spit Pool").transform;
            poolContainer.SetParent(transform);
        }

        // ⚠ Deliberately different from ArrowPool: ObjectPool's constructor returns early
        // on a null prefab, leaving an object whose Get() throws on the first shot. A
        // Demon whose pool has not been wired should bite, not fill the console with null
        // references, so the pool stays null and GetSpit says so once.
        if (spitPrefab == null)
        {
            Debug.LogError($"[{name}] ZombieSpitPool has no spit prefab - Demons will bite " +
                           "instead of spitting. Run Tools > Enemies > Add Enemy Roles to " +
                           "build and wire it.", this);
            return;
        }

        pool = new ObjectPool<ZombieSpit>(spitPrefab, poolContainer, initialPoolSize);
    }

    /// <summary>
    /// A spit, active and at wherever it was last parked. The caller positions it and
    /// calls <see cref="ZombieSpit.Launch"/> - unlike ArrowPool.GetArrow this does not
    /// initialise it, because a spit needs to know its shooter and its target and the
    /// pool has no business knowing either. Null when the pool has no prefab.
    /// </summary>
    public ZombieSpit GetSpit()
    {
        return pool != null ? pool.Get() : null;
    }

    public void ReturnSpit(ZombieSpit spit)
    {
        if (pool != null) pool.Return(spit);
    }

    private void OnDestroy()
    {
        if (pool != null)
        {
            pool.ClearPool();
        }

        // Cleared only if it is still us. Unity's fake-null would make a stale Instance
        // compare equal to null anyway, but a static left pointing at a destroyed object
        // across a scene reload is the kind of thing that reads as a leak later.
        if (Instance == this) Instance = null;
    }
}
