using UnityEngine;
using Core.Pooling;

/*
 * ArrowPool.cs
 * 
 * Purpose: Manages a pool of arrow projectiles for the crossbow weapon system.
 * Used by: CrossbowController, Arrow
 * 
 * Maintains a singleton instance that handles arrow instantiation and recycling.
 * Provides methods for getting new arrows and returning used ones to the pool.
 * 
 * Performance Considerations:
 * - Pre-instantiates arrows to avoid runtime allocation
 * - Handles proper cleanup of physics components
 * - Manages arrow lifecycle and state reset
 * 
 * Dependencies:
 * - Core.Pooling.ObjectPool<T>
 * - Arrow.cs (for projectile behavior)
 * - Requires arrow prefab with proper components
 */

public class ArrowPool : MonoBehaviour
{
    public static ArrowPool Instance { get; private set; }

    [SerializeField] private Arrow arrowPrefab;
    [SerializeField] private int initialPoolSize = 20;
    [SerializeField] private Transform poolContainer;

    private ObjectPool<Arrow> pool;

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
            poolContainer = new GameObject("Arrow Pool").transform;
            poolContainer.SetParent(transform);
        }

        // ⚠ ObjectPool's constructor returns early on a null prefab (ObjectPool.cs:38-42),
        // leaving an object whose Get() throws on the first shot. A crossbow whose pool has
        // not been wired should say so once and fire nothing, not fill the console with null
        // references - the guard ZombieSpitPool.cs:71-81 already carries.
        if (arrowPrefab == null)
        {
            Debug.LogError($"[{name}] ArrowPool has no arrow prefab - the crossbow will draw " +
                           "and sound correct but launch nothing. Assign Arrow Prefab on " +
                           "this component.", this);
            return;
        }

        pool = new ObjectPool<Arrow>(arrowPrefab, poolContainer, initialPoolSize);
    }

    /// <summary>
    /// An arrow, initialised and ready to be positioned and launched. Null when the pool
    /// has no prefab - see Initialize; the caller checks, as CrossbowController does.
    /// </summary>
    public Arrow GetArrow()
    {
        if (pool == null) return null;

        Arrow arrow = pool.Get();
        arrow.Initialize(this);
        return arrow;
    }

    public void ReturnArrow(Arrow arrow)
    {
        if (pool != null) pool.Return(arrow);
    }

    private void OnDestroy()
    {
        if (pool != null)
        {
            pool.ClearPool();
        }
    }
} 