using System.Collections;
using UnityEngine;

/*
 * ZombieSpit.cs
 *
 * Purpose: The Demon's projectile - a lobbed glob of bile that splashes where it lands.
 *          Arrow.cs mirrored with the roles reversed: the enemy throws, the player is
 *          the thing that can be hit, and the damage goes through PlayerStats rather
 *          than IDamageable.
 *
 * Used by: ZombieSpitter (launches it), ZombieSpitPool (owns it). The prefab is built by
 *          Tools > Enemies > Add Enemy Roles.
 *
 * Key Features:
 * - Solved lob, not a tuned force (see the derivation below)
 * - Splash damage on impact, the player only
 * - The impact sound plays AT the impact, not at the Demon
 * - Pool integration, with a lifetime return for a spit that lands in the river
 *
 * What it is handed at launch, and why it goes looking for nothing:
 *   the pool it came from, the Demon that fired it, that Demon's audio manager and
 *   profile key, and where to land. A projectile that hunted for managers would pay for
 *   a FindFirstObjectByType every shot and would still be wrong the moment two spitters
 *   with different profiles were alive at once - the impact sound belongs to the enemy
 *   that fired it, so the enemy hands it over on the way out.
 *
 * ---------------------------------------------------------------------------------
 * The lob, solved rather than tuned
 * ---------------------------------------------------------------------------------
 * Take the launch point as the origin, let g = |Physics.gravity.y| (9.81 by default),
 * and split the flight into its vertical and horizontal halves - they share nothing but
 * the time of flight.
 *
 *   Vertical.    y(t) = v_y t - 1/2 g t^2. At the top of the arc the vertical speed is
 *                zero and the height there is the arc height h we asked for, so
 *                v_y^2 = 2 g h, giving
 *
 *                    v_y    = sqrt(2 g h)             the speed it leaves at, upward
 *                    t_up   = v_y / g = sqrt(2h / g)  time to the top
 *
 *                From the top it falls freely through h - dy, where dy is how far the
 *                TARGET sits above the launch point (negative here - the muzzle is at
 *                the Demon's eyes and the player's root is at their feet), so
 *
 *                    t_down = sqrt(2(h - dy) / g)
 *                    t      = t_up + t_down           the whole flight
 *
 *   Horizontal.  Nothing accelerates it sideways, so the speed is constant and the only
 *                thing left to choose is the one that makes it arrive in exactly t:
 *
 *                    v_h    = |horizontal displacement| / t
 *
 *   Launch velocity = horizontal direction * v_h + up * v_y.
 *
 * h is clamped to stay clear ABOVE the target (h > dy) or the square root has no real
 * answer: a lob whose peak is below where it lands is not a lob, it is a flat shot.
 *
 * Worked, for the number a reviewer can measure - a Demon 12 m from a stationary player,
 * muzzle 1.6 m up and 0.9 m forward, h = 2.5, g = 9.81, so dy = -1.6 and the horizontal
 * distance is 11.1 m:
 *      v_y = sqrt(49.05)          = 7.00 m/s
 *      t_up = 7.00 / 9.81         = 0.714 s
 *      t_down = sqrt(2*4.1/9.81)  = 0.914 s
 *      t = 1.63 s,  v_h = 11.1 / 1.63 = 6.82 m/s
 * The spit lands on the spot the player was standing on when the retch ended, 1.63 s
 * after it left. That is the whole design: the tell runs 0.8 s and the flight another
 * 1.6, so a player who keeps moving is a player who is not there any more. Tuning a
 * force until it "looked right" would land it wherever the tuning happened to land it,
 * and the tell would be a lie.
 *
 * Dependencies:
 * - ZombieSpitPool (recycling), Rigidbody + a non-trigger collider on the prefab
 * - PlayerStats.Instance for the damage (null for the first frames and after a reload)
 * - EnemyAudioManager.PlayRoleSoundAt for the splash, handed in at launch
 */
[RequireComponent(typeof(Rigidbody))]
public class ZombieSpit : MonoBehaviour
{
    [Header("Splash")]

    [Tooltip("Health taken off the player when the splash catches them. Flat, with no " +
             "falloff: the spit either lands on you or it does not, and a dodge that " +
             "only halves the damage teaches the player nothing about the tell")]
    [SerializeField] private float damage = 12f;

    [Tooltip("Metres from the impact point the splash reaches. Measured to the player's " +
             "root - the spit is aimed at their feet, so this is the whole miss margin " +
             "the player has to beat by moving")]
    [SerializeField] private float splashRadius = 1.6f;

    [Header("Flight")]

    [Tooltip("Metres the arc peaks above the launch point. This is what makes the spit " +
             "readable in the air and dodgeable on the ground: bigger = slower, higher, " +
             "easier to see coming and easier to walk out of")]
    [SerializeField] private float arcHeight = 2.5f;

    [Tooltip("Metres the arc must peak above the TARGET when the target is uphill of the " +
             "Demon. Without a floor here the solve has no real answer and the spit " +
             "would be fired flat at a player standing on a rock")]
    [SerializeField] private float minArcClearance = 0.5f;

    [Tooltip("Seconds a spit may stay in flight before it returns to the pool by itself. " +
             "Without this, a spit that lands in the river and never collides with " +
             "anything the physics layer cares about is never recycled")]
    [SerializeField] private float lifetime = 6f;

    [Tooltip("Metres around the muzzle whose OTHER zombies the spit is fired straight " +
             "through. The Demon's line-of-sight test deliberately ignores the Enemy " +
             "layer so a pack does not blind itself, so a spit that stopped dead on a " +
             "packmate's shoulder would spend the tell on nothing. 0 = collide with the " +
             "pack (the shooter itself is always ignored)")]
    [SerializeField] private float packClearRadius = 2.5f;

    [Header("Impact")]

    [Tooltip("Spawned at the impact point, facing back along the surface normal. The setup " +
             "tool wires FX_Impact_Water_01 - a splash reads right for a spit, and measured " +
             "2026-09-07 it is the ONLY non-looping impact in the pack (2 emitters, both " +
             "one-shot). FX_ShardMagic_Explosion_01 was the first choice and has 11 emitters " +
             "all looping over 4 s, so it sprays for as long as it is left alive rather than " +
             "bursting once. Empty = no effect, the spit still lands")]
    [SerializeField] private GameObject impactFx;

    [Tooltip("Seconds before the impact effect is destroyed. Long enough for the " +
             "particle system to finish, short enough that a night of spitting does not " +
             "leave a field of dead effects")]
    [SerializeField] private float impactFxLifetime = 2.5f;

    private Rigidbody rb;
    private Collider spitCollider;

    // Handed over at launch; see the header for why none of these is looked up here.
    private ZombieSpitPool pool;
    private Transform shooter;
    private EnemyAudioManager enemyAudio;
    private string audioKey;

    private bool hasHit;
    private bool returned;      // the double-return guard: see ReturnToPool
    private bool warnedNoCollider;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        spitCollider = GetComponent<Collider>();
    }

    /// <summary>
    /// Fires this spit from where it currently stands at <paramref name="targetPoint"/>.
    /// One entry point rather than the arrow's Initialize-then-fire pair, because a spit
    /// that had been handed out but not aimed would fall on the Demon's own head.
    /// </summary>
    /// <param name="owner">The pool to go back to. Null returns are destroyed instead.</param>
    /// <param name="firedBy">The Demon - the damage indicator points at it, and its own
    /// colliders are the first thing this spit is told to ignore.</param>
    /// <param name="audio">The manager that owns the splash sound; may be null.</param>
    /// <param name="profileKey">Which enemy profile the splash is addressed to.</param>
    /// <param name="targetPoint">Where it should land - the player's position NOW.</param>
    public void Launch(ZombieSpitPool owner, Transform firedBy, EnemyAudioManager audio,
                       string profileKey, Vector3 targetPoint)
    {
        pool = owner;
        shooter = firedBy;
        enemyAudio = audio;
        audioKey = profileKey;

        hasHit = false;
        returned = false;

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (spitCollider == null) spitCollider = GetComponent<Collider>();

        if (spitCollider != null)
        {
            spitCollider.enabled = true;
            IgnoreTheShooter();
        }
        else if (!warnedNoCollider)
        {
            // It will still fly and still time out, but it can never land on anything.
            warnedNoCollider = true;
            Debug.LogWarning($"[{name}] ZombieSpit has no Collider - it will fly through " +
                             "the level and return to the pool on its lifetime instead of " +
                             "splashing. The prefab needs a small NON-TRIGGER SphereCollider.",
                             this);
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = SolveLobVelocity(transform.position, targetPoint,
                                                 arcHeight, minArcClearance);
        }

        // Cancelled by ReturnToPool's StopAllCoroutines on a normal impact.
        StopAllCoroutines();
        StartCoroutine(ReturnAfterLifetime());
    }

    /// <summary>
    /// The velocity that carries something from <paramref name="from"/> to
    /// <paramref name="to"/> under gravity, peaking <paramref name="arc"/> metres above
    /// the start. Static and public so the integrator's probe can check a landing point
    /// without spawning anything. The derivation is in the file header.
    /// </summary>
    public static Vector3 SolveLobVelocity(Vector3 from, Vector3 to, float arc, float clearance)
    {
        // A project with gravity switched off would hand back NaN, and every spit would
        // vanish. The arc is still what was asked for, so solve it against real gravity.
        float g = Mathf.Abs(Physics.gravity.y);
        if (g < 0.01f) g = 9.81f;

        Vector3 delta = to - from;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float dy = delta.y;

        // The peak must clear the target, not just the launch point: a Demon at the foot
        // of a slope firing at a player above it needs a taller arc than the authored one.
        float h = Mathf.Max(arc, dy + Mathf.Max(clearance, 0.01f), 0.01f);

        float vUp = Mathf.Sqrt(2f * g * h);
        float time = Mathf.Sqrt(2f * h / g) + Mathf.Sqrt(2f * (h - dy) / g);

        Vector3 velocity = Vector3.up * vUp;
        if (time > 0.0001f) velocity += flat / time;   // flat / time IS direction * v_h

        return velocity;
    }

    /// <summary>
    /// Keeps the spit off the thing that fired it, and off that thing's neighbours.
    /// </summary>
    /// <remarks>
    /// The muzzle already sits at eye height and forward of the Demon, which is most of
    /// the answer, but a walk cycle swings the model through that point and the arms are
    /// separate colliders - so the shooter's colliders are ignored explicitly, always.
    ///
    /// Other zombies are a design choice, not an accident: ZombieRole.HasLineOfSight
    /// ignores the whole Enemy layer so that a pack does not blind itself, so a Demon
    /// behind three of its own WILL fire, and a spit that stopped on the first shoulder
    /// would make the retch a promise the game never keeps. Within packClearRadius the
    /// spit is fired through them; past that a zombie in the way blocks it and it
    /// splashes there, which is honest - and harmless, because the splash only ever
    /// damages the player. There is no friendly fire in either direction.
    ///
    /// Unity resets an ignore pair when either collider is deactivated, and the pool
    /// deactivates every spit it takes back, so these pairs cannot leak into the next
    /// Demon's shot.
    /// </remarks>
    private void IgnoreTheShooter()
    {
        if (shooter != null)
        {
            foreach (Collider own in shooter.GetComponentsInChildren<Collider>())
            {
                if (own != null && own.enabled) Physics.IgnoreCollision(spitCollider, own, true);
            }
        }

        if (packClearRadius <= 0f) return;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0) return;

        // One query per shot, and a Demon shoots every four seconds.
        Collider[] pack = Physics.OverlapSphere(transform.position, packClearRadius,
                                                1 << enemyLayer, QueryTriggerInteraction.Ignore);
        foreach (Collider neighbour in pack)
        {
            if (neighbour != null && neighbour.enabled && neighbour != spitCollider)
            {
                Physics.IgnoreCollision(spitCollider, neighbour, true);
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasHit) return;
        hasHit = true;

        // Same positional rule as the arrow: the spit knows where it landed. contactCount
        // is 0 only in the degenerate case where the pair separated in the same step.
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.up;

        Splash(point, normal);
        ReturnToPool();
    }

    private void Splash(Vector3 point, Vector3 normal)
    {
        // ⚠ Null for the first frames of the scene and after a reload - single-scene game,
        // no DontDestroyOnLoad - so it is read at the dereference and never cached.
        PlayerStats player = PlayerStats.Instance;
        if (player != null)
        {
            // The ROOT, not the capsule's middle. The spit is aimed at the player's feet,
            // so measuring to their chest would spend a metre of the 1.6 on the player's
            // own height and turn a direct hit into a graze. PlayerStats lives on a CHILD
            // of the player, which is why the root is taken from it rather than the other
            // way round (the same wall the grenade documents at Grenade.cs:278).
            Transform playerRoot = player.transform.root;
            if (Vector3.Distance(point, playerRoot.position) <= splashRadius)
            {
                // The Transform overload, so the edge-of-screen blood smear swings round
                // to the Demon and the player learns WHERE the spit came from. Passing
                // null here would leave them hurt and none the wiser.
                player.TakeDamage(damage, shooter);
            }
        }

        // At the impact, not at the Demon: by now the Demon is somewhere else, and this
        // spit is about to be deactivated, so a one-shot on its own source would be cut
        // off mid-splash. PlayRoleSoundAt spawns a throwaway 3D host that outlives us.
        if (enemyAudio != null)
        {
            enemyAudio.PlayRoleSoundAt(audioKey, RoleSound.SpitImpact, point);
        }

        if (impactFx != null)
        {
            Quaternion facing = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            GameObject effect = Instantiate(impactFx, point, facing);
            Destroy(effect, impactFxLifetime);
        }
    }

    private IEnumerator ReturnAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);

        // A spit that already splashed is long gone; this is the one that did not.
        if (!hasHit) ReturnToPool();
    }

    /// <summary>
    /// Back to the pool, once. Both the impact and the lifetime can call this in the same
    /// frame; without the guard the same spit is queued twice and two Demons are later
    /// handed the same object. (ObjectPool.Return guards this as well - two locks on one
    /// door, because the failure is silent and only shows up as a spit that vanishes.)
    /// </summary>
    private void ReturnToPool()
    {
        if (returned) return;
        returned = true;

        StopAllCoroutines();

        if (pool != null)
        {
            pool.ReturnSpit(this);
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
