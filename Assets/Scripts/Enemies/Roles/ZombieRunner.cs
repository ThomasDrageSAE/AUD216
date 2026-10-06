using System.Collections;
using UnityEngine;

/*
 * ZombieRunner.cs
 *
 * Purpose: The Ghoul's role - a burst, not a sprint. Its authored moveSpeed of 7 made it
 *          permanently faster than the player, which reads as nothing at all: a constant is
 *          not a threat, it is a condition. Tools > Enemies > Add Enemy Roles drops the walk
 *          to 4.5 and this component spends the difference in two-second lunges that are
 *          ANNOUNCED - a growl, half a second of standing still, then the sprint.
 *
 * Used by: Choul.prefab (the Ghoul; the prefab is spelt Choul and the audio profile with it,
 *          the npcName is "Ghoul"). At most one ZombieRole per zombie - see ZombieRole.
 *
 * The tell is the whole design. Say(Lunge) from a zombie that is already moving is a sound
 * effect; the same growl from one that has planted itself is a warning the player can act
 * on, which is the thing this project is marked on. tellSeconds is therefore a dodge window
 * and not a delay, and shortening it much below 0.4 s makes the growl decorative.
 *
 * ⚠ Order of operations, twice over:
 *   - ReleaseControl() puts the phase speed scale back to 1 (ZombieRole's rule: the burst
 *     scale always goes back, alive or dead). The burst is therefore set AFTER control is
 *     handed back, never before, or the line that starts the sprint would cancel it.
 *   - ZombieAI has to be DRIVING for the burst to happen at all - agent.speed is only
 *     written in its own chase tick. This role stops the zombie for the tell and the pant
 *     and is a passenger for the sprint itself; it never moves the zombie.
 *
 * ⚠ CanAct is false BY CONSTRUCTION while this role holds control: it tests Zombie.enabled,
 *   which TakeControl has just cleared. Inside a borrow the question is only StillAlive.
 */
// No RequireComponent or DisallowMultipleComponent here: ZombieRole carries both and Unity
// honours them on a subclass. A second copy would only be a second place to keep in step.
public class ZombieRunner : ZombieRole
{
    [Header("Lunge timing")]

    [Tooltip("Average seconds between lunges. The pacing dial: long enough that the growl is " +
             "an event rather than a texture, short enough that a chase across the camp " +
             "contains one")]
    [Min(0.5f)] [SerializeField] private float lungeInterval = 6f;

    [Tooltip("Random slop either side of the interval, seconds. Stops a pack of Ghouls " +
             "lunging in unison and stops the player counting the gap between them")]
    [Min(0f)] [SerializeField] private float lungeIntervalJitter = 1.5f;

    [Tooltip("How close the player has to be, in metres, for a lunge to be worth spending. " +
             "Further out the Ghoul simply walks in - a burst nobody sees start is wasted")]
    [Min(1f)] [SerializeField] private float lungeRange = 14f;

    [Tooltip("Seconds the Ghoul stands still after the growl before it moves. THE dodge " +
             "window - the growl only warns if the player has time to answer it")]
    [Min(0f)] [SerializeField] private float tellSeconds = 0.5f;

    [Tooltip("Seconds the burst lasts. Long enough to close a gap the player thought was " +
             "safe, short enough that they can walk away from the Ghoul again afterwards")]
    [Min(0.1f)] [SerializeField] private float lungeSeconds = 2f;

    [Tooltip("Speed multiplier while the burst runs, on top of the wave's own multiplier. " +
             "This is where the prefab's old permanent speed of 7 went")]
    [Min(1f)] [SerializeField] private float lungeSpeedScale = 2.2f;

    [Tooltip("Seconds the Ghoul stands still when the burst ends - the pant. The recovery is " +
             "what makes a lunge a gamble for the zombie instead of free distance")]
    [Min(0f)] [SerializeField] private float recoverSeconds = 0.8f;

    [Header("Launch effect")]

    [Tooltip("Kicked up at the feet on the frame the burst starts, so the sprint is seen as " +
             "well as heard - FX_Dust_Small_01. Empty: the lunge still happens, invisibly")]
    [SerializeField] private GameObject dustPuff;

    [Tooltip("Seconds before the spawned puff is destroyed. Give the particle system longer " +
             "than its own duration or the cloud vanishes while it is still in the air")]
    [Min(0.1f)] [SerializeField] private float dustPuffLifetime = 2f;

    private Animator animator;

    protected override void Awake()
    {
        base.Awake();   // caches ZombieAI/agent/voice, and disables this role if there are two

        animator = GetComponent<Animator>();
    }

    protected override void Start()
    {
        base.Start();   // finds the player and the audio manager

        StartCoroutine(LungeLoop());
    }

    /// <summary>
    /// The pacing. Waits out an interval, then asks four questions - alive and driving
    /// itself, not mid-swing, close enough, actually visible - and only then spends a lunge.
    /// A failed question costs the whole next interval rather than retrying at once: a Ghoul
    /// that lunges on the frame it rounds a corner gives no tell worth the name.
    /// </summary>
    private IEnumerator LungeLoop()
    {
        while (StillHolding)
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f,
                lungeInterval + Random.Range(-lungeIntervalJitter, lungeIntervalJitter)));

            if (!CanAct) continue;

            // Asked, not cancelled. CancelAttack is for interrupting a swing this role has
            // decided to take over; here the swing simply outranks the lunge, because a bite
            // that is already landing is damage ZombieAI has earned.
            if (Zombie.IsAttacking) continue;

            if (DistanceToPlayer > lungeRange) continue;
            if (!HasLineOfSight(lungeRange)) continue;

            yield return LungeRoutine();
        }
    }

    private IEnumerator LungeRoutine()
    {
        // ---- The tell ------------------------------------------------------------
        // Borrowed the boss's way: ZombieAI paused rather than fought with, agent stopped.
        // A refusal means nothing was taken and there is nothing to give back.
        if (!TakeControl()) yield break;

        // ZombieAI's melee coroutine outlives the disable above - disabling a component does
        // not stop the coroutines it started - and left running it keeps polling the animator
        // this role is now driving and lands a bite inside the wind-up. IsAttacking can
        // already read false while that sequence is still alive, so the check in the loop
        // above does not make this redundant.
        Zombie.CancelAttack();

        StandStill();
        FacePlayer();
        Say(RoleSound.Lunge);

        yield return new WaitForSeconds(tellSeconds);

        // Re-checked after the wait, not only before it: half a second stood still in the
        // open is long enough to be killed in, and ZombieAI.Die disables the agent on
        // purpose. ReleaseControl is what keeps a corpse from getting its pathing back.
        if (!StillHolding)
        {
            ReleaseControl();
            yield break;
        }

        // ---- The burst -----------------------------------------------------------
        // Control back FIRST, then the scale. ReleaseControl resets the scale to 1, so these
        // two lines the other way round are a lunge that never happens.
        ReleaseControl();
        Zombie.SetPhaseSpeedScale(lungeSpeedScale);
        SpawnDustPuff();

        yield return new WaitForSeconds(lungeSeconds);

        if (Zombie == null) yield break;

        // Unconditionally, alive or dead: phaseSpeedScale is one float shared with the boss's
        // phases and the screamer's rally, and a corpse left holding 2.2 hands it to nothing.
        Zombie.SetPhaseSpeedScale(1f);

        // Control is already given back here, so this is the CanAct case rather than the
        // StillHolding one - the pant is only worth taking the zombie back for if it is in a
        // state to be taken back at all. Plus the enabled test CanAct does not make.
        if (!enabled || !CanAct) yield break;

        // ---- The pant ------------------------------------------------------------
        // Not over a bite. A Ghoul whose lunge closed the gap is already standing still and
        // already in the Attack state; borrowing it now would cancel the hit the lunge just
        // bought and pause the swing half-way through - the recovery is meant to cost the
        // zombie its distance, not its damage.
        if (recoverSeconds > 0f && !Zombie.IsAttacking && TakeControl())
        {
            StandStill();

            yield return new WaitForSeconds(recoverSeconds);

            // Safe on a corpse by design: it re-enables nothing the zombie switched off
            // itself, which is why every path in this routine can end with it.
            ReleaseControl();
        }
    }

    /// <summary>
    /// The only animator write in this class, and only ever while control is held. The
    /// controller has Walk, Attack and Dying and no idle state, so with ZombieAI paused -
    /// and nothing else clearing IsMoving - a Ghoul standing still would walk on the spot
    /// through its own tell. Guarded rather than merely documented: an animator write from a
    /// role that does not hold the wheel is a fight with ZombieAI's own Update.
    /// </summary>
    private void StandStill()
    {
        if (!HasControl || animator == null) return;

        animator.SetBool("IsMoving", false);
    }

    private void SpawnDustPuff()
    {
        if (dustPuff == null) return;

        // At the pivot, which on these models sits between the feet - where a launch throws
        // dirt. Unrotated: a puff has no facing, and matching the zombie's would only lean
        // the same cloud.
        GameObject puff = Instantiate(dustPuff, transform.position, Quaternion.identity);
        Destroy(puff, dustPuffLifetime);
    }
}
