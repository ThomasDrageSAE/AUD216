using System.Collections;
using UnityEngine;

/*
 * ZombieSpitter.cs
 *
 * Purpose: The Demon's role - the zombie that hurts you from across the clearing. Sits
 *          beside ZombieAI, exactly as BossZombieController does, and adds one thing:
 *          a telegraphed, lobbed spit at a player who is neither on top of it nor out
 *          of reach.
 *
 * Used by: Demon.prefab, added by Tools > Enemies > Add Enemy Roles.
 *
 * The behaviour, in the order it happens:
 *
 *   further than spitRange   ZombieAI walks it in, untouched. Nothing here runs.
 *   preferredMin..spitRange  with line of sight and the cooldown up: stop, face the
 *                            player, retch (the tell), wait windupSeconds, lob one spit
 *                            at where the player is AT THAT MOMENT, give control back.
 *   closer than preferredMin ZombieAI closes and bites as it does today. A cornered
 *                            spitter is an ordinary zombie - it does not back away,
 *                            because a ranged enemy that kites in a level with this
 *                            much cover is an enemy the player can never reach.
 *
 * The wind-up IS the mechanic. The retch is 0.8 s and the spit's own flight is another
 * ~1.6 s at 12 m, so the player has well over two seconds between hearing it and being
 * hit, and the spit is aimed at the ground they were standing on when it left. Keep
 * moving and it lands behind you. That only works because ZombieSpit SOLVES its arc
 * rather than being pushed with a tuned force - see the derivation in that file.
 *
 * ⚠ Borrowed control comes back on every path, including death. ZombieAI.Die disables
 * the NavMeshAgent, so a coroutine that re-enabled it afterwards would give a corpse its
 * pathing back; ZombieRole.ReleaseControl is the only way out of here and it re-enables
 * nothing on a dead zombie. Every wait re-checks that the zombie is still alive.
 *
 * ⚠ Nothing here depends on a clip existing. With every role slot on the Demon's profile
 * empty - which is how the students receive this project - the Demon still stops, still
 * winds up and still spits. It is simply silent.
 *
 * Dependencies:
 * - ZombieRole (control, line of sight, the voice), ZombieAI (movement, melee, death)
 * - ZombieSpitPool.Instance for the projectile; missing, it logs once and bites instead
 */
public class ZombieSpitter : ZombieRole
{
    [Header("Spit - when")]

    [Tooltip("Metres inside which the Demon stops spitting and lets ZombieAI close and " +
             "bite. A spit at point-blank range cannot be dodged, and an enemy whose " +
             "only answer to being cornered is to keep spitting is not a fight")]
    [SerializeField] private float preferredMin = 7f;

    [Tooltip("Metres beyond which the Demon just walks in. Also the range the " +
             "line-of-sight ray is cast to, so a spit is never launched at a player " +
             "behind a rock")]
    [SerializeField] private float spitRange = 16f;

    [Tooltip("Seconds between spits. Long enough that the player is never fighting the " +
             "spit instead of the pack, and long enough for the last one to have landed " +
             "before the next retch starts")]
    [SerializeField] private float spitInterval = 4f;

    [Tooltip("Seconds the Demon stands still retching before the spit leaves. THIS IS " +
             "THE TELL - the player's whole warning is this sound and this pause, so " +
             "shortening it makes the spit unfair, not harder")]
    [SerializeField] private float windupSeconds = 0.8f;

    [Header("Spit - where from")]

    [Tooltip("Metres forward of the Demon's centre the spit appears, at the eye height " +
             "set above. Far enough out that the model's own colliders - which swing " +
             "through the muzzle during the walk cycle - are clear of it")]
    [SerializeField] private float muzzleForward = 0.9f;

    private Animator animator;

    private float nextSpitTime;
    private bool spitting;
    private bool warnedNoPool;

    protected override void Awake()
    {
        base.Awake();

        // ZombieAI finds its animator the same way, on this object rather than in the
        // children (Mixamo rigs put the Animator on the root).
        animator = GetComponent<Animator>();
    }

    protected override void Start()
    {
        base.Start();

        // Staggered, not synchronised. Four Demons spawned by the same wave tick would
        // otherwise retch in unison for the rest of the night, which reads as one enormous
        // enemy rather than four - and the player cannot tell which one to move away from.
        nextSpitTime = Time.time + Random.Range(0f, spitInterval);
    }

    protected override void OnDisable()
    {
        // Disabling a component does NOT stop its coroutines in Unity - this project
        // relies on that elsewhere (ZombieAI.CancelAttack: "its coroutines outlive the
        // disable") - so a wind-up left running here would carry on releasing control it
        // no longer holds and firing spits from a switched-off role.
        StopAllCoroutines();
        spitting = false;

        base.OnDisable();   // gives back anything that was borrowed
    }

    private void Update()
    {
        if (spitting) return;

        // CanAct is the right question HERE, before anything is borrowed: alive, driving
        // itself, on the NavMesh, with a player to aim at.
        if (!CanAct) return;

        // Never interrupt a bite. CancelAttack() is for interrupting a swing, not for
        // asking whether there is one - ZombieAI.IsAttacking is the question.
        if (Zombie.IsAttacking) return;

        if (Time.time < nextSpitTime) return;

        float distance = DistanceToPlayer;
        if (distance < preferredMin || distance > spitRange) return;

        // Read at the dereference, never cached: the pool is a scene object and this is a
        // single-scene game where a reload is the restart path.
        if (ZombieSpitPool.Instance == null)
        {
            WarnMissingPool();

            // Pushed forward so a scene with no pool costs one static comparison every
            // four seconds rather than one every frame, and so a pool created later - by
            // the setup tool, mid-session - starts working on its own.
            nextSpitTime = Time.time + spitInterval;
            return;
        }

        // Last because it is the expensive one.
        if (!HasLineOfSight(spitRange)) return;

        StartCoroutine(SpitRoutine());
    }

    /// <summary>
    /// Stop, retch, lob, hand back. The only path that borrows control, and every exit
    /// from it - finished, interrupted, dead - goes through ReleaseControl.
    /// </summary>
    private IEnumerator SpitRoutine()
    {
        spitting = true;

        // Started here rather than after the launch, so an interrupted wind-up costs the
        // same as a completed one. A Demon that was interrupted at 0.7 s and could then
        // retry on the very next frame would machine-gun its tell.
        nextSpitTime = Time.time + spitInterval;

        if (!TakeControl())
        {
            spitting = false;
            yield break;
        }

        // The animator has no idle state: a stopped zombie otherwise keeps playing Walk
        // on the spot. ZombieAI does exactly this whenever it stands still (ZombieAI:311),
        // and takes the flag back over itself the moment control is returned.
        if (animator != null) animator.SetBool("IsMoving", false);

        FacePlayer();
        Say(RoleSound.SpitWindup);

        float elapsed = 0f;
        while (elapsed < windupSeconds)
        {
            // Re-checked after EVERY yield. A Demon can be shot dead mid-retch, and
            // ZombieAI.Die runs whether or not its own Update is disabled, so this is not
            // a theoretical case - it is what a headshot during the tell looks like.
            if (!StillHolding) break;

            // Tracking during the wind-up is what makes the tell legible: it is looking at
            // you. It does not make the spit harder to dodge - the aim point is fixed at
            // the instant of launch, and the dodge happens during the flight.
            FacePlayer();

            elapsed += Time.deltaTime;
            yield return null;
        }

        // ...and once more after the loop's last yield, before anything is fired.
        if (StillHolding)
        {
            FacePlayer();
            LaunchSpit();
        }

        ReleaseControl();
        spitting = false;
    }

    private void LaunchSpit()
    {
        // At the dereference again: 0.8 s of wind-up is long enough for a scene to be
        // reloaded out from under this coroutine.
        ZombieSpitPool pool = ZombieSpitPool.Instance;
        if (pool == null)
        {
            WarnMissingPool();
            return;
        }

        ZombieSpit spit = pool.GetSpit();
        if (spit == null)
        {
            // A pool with no prefab. It has already logged its own error; this Demon just
            // goes back to biting.
            WarnMissingPool();
            return;
        }

        // Eye height and forward: clear of the Demon's own colliders on the way out, and
        // high enough that the arc is not launched out of the ground. ZombieSpit ignores
        // the shooter's colliders as well - the offset alone is not proof against a walk
        // cycle swinging an arm through the muzzle.
        Vector3 muzzle = transform.position + Vector3.up * eyeHeight + transform.forward * muzzleForward;

        // The player's position AT THIS MOMENT, and never updated afterwards. This one
        // line is the dodge.
        Vector3 target = Player.position;

        Vector3 aim = target - muzzle;
        Quaternion facing = aim.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(aim)
            : transform.rotation;

        spit.transform.SetPositionAndRotation(muzzle, facing);
        spit.Launch(pool, transform, EnemyAudio, AudioKey, target);

        Say(RoleSound.Spit);
    }

    /// <summary>
    /// Once per Demon, not once per attempt. A missing pool must never spam the console
    /// every four seconds for the rest of the night - the warning is for the person who
    /// forgot to run the setup tool, and they only need telling once.
    /// </summary>
    private void WarnMissingPool()
    {
        if (warnedNoPool) return;
        warnedNoPool = true;

        Debug.LogWarning($"[{name}] ZombieSpitter found no usable ZombieSpitPool - this " +
                         "Demon will close and bite instead of spitting. Add one with " +
                         "Tools > Enemies > Add Enemy Roles. Logged once per Demon.", this);
    }
}
