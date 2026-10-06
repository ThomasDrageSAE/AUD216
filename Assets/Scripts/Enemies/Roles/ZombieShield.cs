using UnityEngine;

/*
 * ZombieShield.cs
 *
 * Purpose: The Butcher's shield. A hit that arrives inside a 120 degree arc centred on the
 *          zombie's forward is taken on the shield - it clangs, it throws sparks, and it
 *          pays three tenths of what it was worth. Everything outside that arc pays in
 *          full. A headshot is never blocked. So: flank it, or shoot it in the head. That
 *          one sentence is the whole mechanic, and the only Butcher that is a problem is
 *          one the player keeps shooting in the chest from the front.
 *
 * Used by: Butcher.prefab, added by Tools > Enemies > Add Enemy Roles - which also retunes
 *          the prefab (health 100 -> 150, moveSpeed 3.5 -> 3.0; a heavy walks like one).
 *          Neither the prefab nor the retune is this script's business.
 *
 * How it is asked: ZombieAI.TakeDamage(float, Vector3) finds this through
 * IZombieDamageModifier and calls Modify ONCE per positional hit, with the RAW damage,
 * BEFORE the headshot multiplier - and applies that multiplier to whatever comes back. The
 * order is the reason the headshot case returns the damage untouched instead of trying to
 * divide anything out.
 *
 * ⚠ A grenade's blast is offered here like everything else. DamageableUtil.Damage routes
 * every zombie hit through the positional overload, and the blast passes the point its own
 * line-of-sight ray struck - so a blast that lands in front of the Butcher is blocked. There
 * is no special case for it and none is wanted: a shield covers an arc, and a blast arrives
 * from a direction like anything else. Flank it, or aim at the head.
 *
 * ⚠ Modify runs PER BULLET. A held minigun trigger is twenty calls a second per zombie in
 * the beam, so nothing on the way to the arc test searches, allocates or builds a string -
 * the transform is cached in Awake and the rest is arithmetic on two Vector3s.
 *
 * Dependencies:
 * - ZombieRole (the base) for the zombie, the voice and the audio profile key. It also
 *   carries the RequireComponent(ZombieAI) and DisallowMultipleComponent this class needs;
 *   Unity honours both through inheritance, so they are not repeated here.
 * - ZombieAI's IZombieDamageModifier seam, the only caller of Modify.
 * - Nothing else is required. No Block clip means a silent block; no sparks prefab means an
 *   invisible one. The shield still blocks - behaviour never waits on authoring.
 */
public class ZombieShield : ZombieRole, IZombieDamageModifier
{
    [Header("Shield")]

    [Tooltip("How wide the shield's cover is, in degrees, centred on the zombie's forward. " +
             "120 means anything within 60 degrees either side of straight ahead is taken " +
             "on the shield and the rest of the circle is bare flank and back")]
    [Range(0f, 360f)] [SerializeField] private float frontArcDegrees = 120f;

    [Tooltip("What a hit inside the arc is worth after the shield takes it. 0.3 = the " +
             "Butcher feels three tenths of it. 1 turns the block off without removing " +
             "the component; 0 makes the front invulnerable, which no weapon can then solve")]
    [Range(0f, 1f)] [SerializeField] private float frontDamageFraction = 0.3f;

    [Header("Block Feedback")]

    [Tooltip("Sparks spawned where the round struck, thrown back the way it came. " +
             "FX_Impact_Metal_01 from the pack; left empty here and wired by " +
             "Tools > Enemies > Add Enemy Roles. Empty is fine - the block is silent " +
             "about its visuals and still blocks")]
    [SerializeField] private GameObject blockSparks;

    [Tooltip("Seconds the sparks instance lives. ⚠ FX_Impact_Metal_01 is a LOOPING system " +
             "(both its emitters, 1 s duration, play on awake), so this is how long the " +
             "sparks SPRAY, not merely how long to wait before tidying up. Much longer and " +
             "the Butcher wears a welder's fountain")]
    [Min(0f)] [SerializeField] private float blockSparksLifetime = 0.5f;

    [Tooltip("Shortest gap between two spark spawns, in seconds. Every spawn is an " +
             "instantiated particle system and the minigun lands twenty hits a second, so " +
             "0 means twenty instances a second. 0.1 still reads as a continuous shower. " +
             "The clang is NOT throttled - it plays on every blocked round")]
    [Min(0f)] [SerializeField] private float blockSparksInterval = 0.1f;

    // Cached because Modify is the one method in this project a held minigun trigger calls
    // twenty times a second per zombie: Component.transform is a native lookup, and there is
    // no reason to pay for it per round when a zombie's transform never changes.
    private Transform cachedTransform;

    // Time.time of the last spark, for the interval above. A float compare per blocked hit.
    private float lastSparksTime = float.NegativeInfinity;

    protected override void Awake()
    {
        base.Awake();

        cachedTransform = transform;
    }

    /// <summary>
    /// The shield's say in one incoming hit. Returns the damage that should land.
    ///
    /// ⚠ Called per bullet, from ZombieAI.TakeDamage(float, Vector3), with the RAW damage
    /// and before the headshot multiplier. Whatever this returns is what the multiplier is
    /// then applied to.
    ///
    /// ⚠ Called with no regard for this component's state - a hit can arrive on the frame
    /// the zombie dies, or on a role the base disabled for being the second one on the
    /// prefab. Every one of those cases returns the damage untouched rather than throwing,
    /// and nothing on any path here touches the NavMeshAgent, ZombieAI.enabled or the phase
    /// speed scale, so no hit can hand a corpse its pathing back.
    /// </summary>
    public float Modify(float damage, Vector3 hitPoint, bool headshot)
    {
        // The owner's rule, and the one that teaches the fight: the head is above the
        // shield's line, so a headshot is never blocked. Returned raw - ZombieAI multiplies
        // it by headshotMultiplier immediately after, so a headshot from the front pays its
        // full double and the shield may as well not be there.
        if (headshot) return damage;

        // A role that has been switched off is not a shield. Deliberately isActiveAndEnabled
        // and not CanAct: CanAct asks whether the role may ACT (agent on the NavMesh, a
        // player to act on), and being shot is not acting. A Butcher standing somewhere its
        // agent cannot path still has a shield strapped to its arm.
        if (!isActiveAndEnabled) return damage;

        // Belt and braces - ZombieAI already returns early once isDying is set, so this only
        // catches a future caller. A corpse does not raise its shield, and more to the point
        // must not clang and spark after its death sound.
        if (Zombie == null || Zombie.IsDead()) return damage;

        Vector3 toHit = hitPoint - cachedTransform.position;
        toHit.y = 0f;   // the shield covers a bearing, not an elevation

        // A hit with no horizontal bearing at all - a blast whose line-of-sight ray resolved
        // onto the zombie's own axis - did not come through the shield, because it came from
        // no direction in particular. Guarded rather than left to Vector3.Angle, which
        // answers 0 for a degenerate vector and would silently call it a frontal hit; and
        // LookRotation below would log an error on it.
        float sqrDistance = toHit.sqrMagnitude;
        if (sqrDistance < 0.0001f) return damage;

        // Vector3.Angle rather than a precomputed cosine: it allocates nothing, and a
        // reviewer asking "is 61 degrees blocked?" can read the answer straight off the line
        // instead of trusting a cached trig constant to have been rebuilt after a retune.
        if (Vector3.Angle(cachedTransform.forward, toHit) > frontArcDegrees * 0.5f)
        {
            return damage;   // flank or back: from here the Butcher is an ordinary zombie
        }

        // Silent when the profile has no Block clips - the manager returns on an empty array.
        Say(RoleSound.Block);

        SpawnSparks(hitPoint, toHit / Mathf.Sqrt(sqrDistance));

        return damage * frontDamageFraction;
    }

    /// <summary>
    /// Sparks at the hit, turned to face back along <paramref name="outward"/> - the
    /// direction the round came FROM, so they spray towards the shooter the way
    /// BulletImpactManager aims its impacts down the surface normal.
    /// </summary>
    private void SpawnSparks(Vector3 hitPoint, Vector3 outward)
    {
        if (blockSparks == null) return;

        if (Time.time - lastSparksTime < blockSparksInterval) return;
        lastSparksTime = Time.time;

        // Instantiate + Destroy after a lifetime, exactly as Grenade.cs spawns its blast
        // effect. Nothing is pooled: a block is not a per-frame event even under a minigun,
        // and the interval above is what keeps the count down.
        GameObject sparks = Instantiate(blockSparks, hitPoint, Quaternion.LookRotation(outward));
        Destroy(sparks, blockSparksLifetime);
    }
}
