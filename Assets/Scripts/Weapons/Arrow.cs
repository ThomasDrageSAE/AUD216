using UnityEngine;
using System.Collections;

/*
 * Arrow.cs
 * 
 * Purpose: Controls arrow projectile behavior and physics
 * Used by: Crossbow weapon system, projectile management
 * 
 * Key Features:
 * - Projectile physics
 * - Collision detection
 * - Damage application
 * - Pool integration
 * - Impact effects
 * 
 * Physics Features:
 * - Trajectory calculation
 * - Velocity management
 * - Hit detection
 * - Surface interaction
 * 
 * Performance Considerations:
 * - Object pooling integration
 * - Efficient physics checks
 * - Smart cleanup
 * - Resource management
 * 
 * Dependencies:
 * - ArrowPool system
 * - Physics system
 * - Impact effect system
 * - Damage system
 */
public class Arrow : MonoBehaviour
{
    [SerializeField] private float damage = 50f;
    [SerializeField] private float stickDuration = 10f;

    [Tooltip("Seconds an arrow may stay in flight before it returns to the pool by itself. " +
             "Without this, an arrow that never collides (fired off the map) is never " +
             "recycled and the pool instantiates a replacement every shot")]
    [SerializeField] private float maxFlightTime = 8f;

    private bool hasHit = false;
    private Rigidbody rb;
    private Collider arrowCollider;
    private ArrowPool pool;

    public void Initialize(ArrowPool pool)
    {
        this.pool = pool;
        hasHit = false;

        // Cancelled by ReturnToPool's StopAllCoroutines on a normal impact.
        StartCoroutine(ReturnAfterMaxFlightTime());
        
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (arrowCollider == null) arrowCollider = GetComponent<Collider>();
        
        // Reset state
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        if (arrowCollider != null)
        {
            arrowCollider.enabled = true;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!hasHit)
        {
            hasHit = true;
            
            // Handle damage
            if (DamageableUtil.TryGetDamageable(collision.collider, out IDamageable damageable))
            {
                // Same positional rule as the guns: the arrow knows where it landed.
                // contactCount is 0 only in the degenerate case where the pair separated in
                // the same step - which this body can produce, being ContinuousDynamic
                // (Crossbow_Bolt.prefab) where a sweep hit can report no contacts at all.
                // The same guard ZombieSpit.cs:288-289 carries.
                Vector3 point = collision.contactCount > 0
                    ? collision.GetContact(0).point : transform.position;
                Vector3 normal = collision.contactCount > 0
                    ? collision.GetContact(0).normal : Vector3.up;

                DamageableUtil.Damage(damageable, damage, point, normal);

                ReturnToPool();
            }
            else
            {
                StickToSurface(collision);
            }
        }
    }

    private void StickToSurface(Collision collision)
    {
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (arrowCollider != null)
        {
            arrowCollider.enabled = false;
        }

        StartCoroutine(CleanupArrow());
    }

    private IEnumerator CleanupArrow()
    {
        yield return new WaitForSeconds(stickDuration);
        ReturnToPool();
    }

    private IEnumerator ReturnAfterMaxFlightTime()
    {
        yield return new WaitForSeconds(maxFlightTime);

        // Only relevant while still in flight; a stuck arrow is on CleanupArrow's clock.
        if (!hasHit) ReturnToPool();
    }

    private void ReturnToPool()
    {
        StopAllCoroutines();
        if (pool != null)
        {
            pool.ReturnArrow(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }
} 