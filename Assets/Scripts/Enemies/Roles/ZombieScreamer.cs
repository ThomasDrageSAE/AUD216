using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * ZombieScreamer.cs
 *
 * Purpose: The Yokai's role - the enemy that makes the OTHERS dangerous. It is the weakest
 *          thing in the level (100 hp, walk 3) and it never gets faster itself for long;
 *          what it costs the player is the pack. On sight it plants itself, screams, and
 *          every zombie within earshot speeds up for five seconds.
 *
 * Used by: Yokai.prefab. At most one ZombieRole per zombie - see ZombieRole's header.
 *
 * Why the rally is a speed burst and not a wake-up: detectionRange is 0 on every prefab,
 * which ZombieAI reads as "always chasing". There is nothing asleep to wake, so the scream
 * buys the audible thing instead - the pack the player could already hear now arrives
 * sooner, and the scream is the second they had to decide about that.
 *
 * ⚠ The boss is excluded from every rally, and that is not a tuning choice.
 *   BossZombieController drives the same single phaseSpeedScale float for its phases
 *   (Mathf.Pow(1.15, phasesTriggered)); a screamer writing 1.5 across it would cancel the
 *   boss's escalation or make it permanent, depending on nothing but which coroutine
 *   finished last.
 *
 * ⚠ A pack of Yokai must not become a siren. Rallying a screamer silences it for a whole
 *   screamInterval (NotifyRallied), or two in earshot answer each other every 1.4 seconds
 *   for the rest of the night, and a tell that never stops has stopped being a tell.
 *
 * ⚠ CanAct is false BY CONSTRUCTION while this role holds control - it tests Zombie.enabled,
 *   which TakeControl has just cleared. Inside a borrow the question is only StillAlive.
 */
// No RequireComponent or DisallowMultipleComponent here: ZombieRole carries both and Unity
// honours them on a subclass. A second copy would only be a second place to keep in step.
public class ZombieScreamer : ZombieRole
{
    [Header("Scream")]

    [Tooltip("How close the player has to be, in metres, before the Yokai screams. It calls " +
             "on SIGHT, so this is really how far ahead of the pack the player is warned")]
    [Min(1f)] [SerializeField] private float screamRange = 18f;

    [Tooltip("Seconds before the same Yokai may scream again. Long: the scream is an event " +
             "the player reacts to, and one every few seconds is weather")]
    [Min(1f)] [SerializeField] private float screamInterval = 12f;

    [Tooltip("Seconds the Yokai stands still and screams before the pack speeds up. The " +
             "window the player gets to kill it and stop the rally - killed inside it, " +
             "nothing is rallied")]
    [Min(0.1f)] [SerializeField] private float screamSeconds = 1.4f;

    [Header("Rally")]

    [Tooltip("Metres around the screamer that hear the call. Everything tagged Enemy inside " +
             "it speeds up - the boss excepted, which owns its own phase speed")]
    [Min(1f)] [SerializeField] private float rallyRadius = 20f;

    [Tooltip("Speed multiplier handed to every rallied zombie, on top of the wave's own " +
             "multiplier. Modest on purpose: the scream should change the timing of the " +
             "fight, not teleport the pack into the player's face")]
    [Min(1f)] [SerializeField] private float rallySpeedScale = 1.5f;

    [Tooltip("Seconds the rally lasts before every zombie goes back to its own speed. It " +
             "ending is what makes killing the Yokai first worth doing")]
    [Min(0.1f)] [SerializeField] private float rallySeconds = 5f;

    [Header("Rally effect")]

    [Tooltip("Optional ring spawned at the screamer's feet as it calls, so the rally is " +
             "visible to a player who is looking the wrong way. Empty: the rally still runs")]
    [SerializeField] private GameObject rallyRing;

    [Tooltip("Seconds before the spawned ring is destroyed. Give the particle system longer " +
             "than its own duration or it disappears mid-flare")]
    [Min(0.1f)] [SerializeField] private float rallyRingLifetime = 3f;

    // A quarter second, the rate BossZombieController watches its own health at: fast enough
    // that "on first sight" reads as immediate, slow enough that eight Yokai are not eight
    // line-of-sight raycasts every frame.
    private const float SightPollSeconds = 0.25f;

    // The rally's own poll. Only fine enough to notice a rallied zombie dying while it is
    // still worth noticing.
    private const float RallyPollSeconds = 0.25f;

    private Animator animator;

    // Left at 0 so the first sighting screams at once rather than after a silent interval.
    private float nextScreamTime;

    // Every zombie this screamer currently owes a restore to. Kept as a list and not only as
    // live coroutines because the coroutines die with this component and the debt does not -
    // see OnDisable.
    private readonly List<ZombieAI> rallied = new List<ZombieAI>();

    protected override void Awake()
    {
        base.Awake();   // caches ZombieAI/agent/voice, and disables this role if there are two

        animator = GetComponent<Animator>();
    }

    protected override void Start()
    {
        base.Start();   // finds the player and the audio manager

        StartCoroutine(ScreamWatch());
    }

    protected override void OnDisable()
    {
        base.OnDisable();   // gives back anything THIS zombie borrowed

        // And here, anything it borrowed from everybody else. A screamer killed three
        // seconds into its own five-second rally takes its restore coroutines with it -
        // Unity stops them when the object goes - and the pack would stay at 1.5x for the
        // rest of the night with nothing left alive that knows to put it back. Every path
        // that borrows returns it, including the path where the borrower dies.
        for (int i = 0; i < rallied.Count; i++)
        {
            if (rallied[i] != null) rallied[i].SetPhaseSpeedScale(1f);
        }

        rallied.Clear();
    }

    /// <summary>
    /// Polls for a reason to scream: off cooldown, able to act, the player close enough and
    /// actually visible. The first pass that passes all four is the "on first line of sight"
    /// scream; every one after it is gated by screamInterval, which NotifyRallied can also
    /// push out from underneath this loop.
    /// </summary>
    private IEnumerator ScreamWatch()
    {
        while (StillHolding)
        {
            yield return new WaitForSeconds(SightPollSeconds);

            if (Time.time < nextScreamTime) continue;
            if (!CanAct) continue;
            if (DistanceToPlayer > screamRange) continue;
            if (!HasLineOfSight(screamRange)) continue;

            yield return ScreamRoutine();

            // Measured from the end of the scream rather than its start, so screamSeconds
            // cannot eat into the gap between two calls.
            nextScreamTime = Time.time + screamInterval;
        }
    }

    private IEnumerator ScreamRoutine()
    {
        // Borrowed the boss's way: ZombieAI paused rather than fought with, agent stopped.
        // A refusal means nothing was taken and there is nothing to give back.
        if (!TakeControl()) yield break;

        // ZombieAI's melee coroutine outlives the disable above - disabling a component does
        // not stop the coroutines it started - so a Yokai screaming in the player's face
        // would otherwise still bite them mid-scream, out of an animator this role is driving.
        Zombie.CancelAttack();

        StandStill();
        FacePlayer();   // a scream is delivered TO someone; one addressed to a rock is a bug you can see
        Say(RoleSound.Scream);
        SpawnRallyRing();

        yield return new WaitForSeconds(screamSeconds);

        // Read before the release, because ReleaseControl is about to make the zombie look
        // alive-and-driving again whether it is alive or not.
        bool survivedTheCall = StillHolding;
        ReleaseControl();

        // A screamer killed during the hold rallies nobody, and that is the whole point of
        // holding: screamSeconds is the window the player has to stop the pack speeding up.
        // Paying the rally out anyway would make the tell decorative.
        if (!survivedTheCall) yield break;

        Rally();
    }

    /// <summary>
    /// Everything tagged Enemy inside rallyRadius gets the burst, this screamer included.
    /// </summary>
    /// <remarks>
    /// FindGameObjectsWithTag allocates, and is used anyway: this runs once per scream - the
    /// rarest event in the game at one per zombie per twelve seconds - and the alternative,
    /// an OverlapSphere on the Enemy layer, still has to filter by tag and component
    /// afterwards. WaveManager is what sets the tag, on the frame it spawns each zombie.
    ///
    /// Bursts do not stack: phaseSpeedScale is a single float, so a Ghoul rallied mid-lunge
    /// ends up at whichever of 2.2 and 1.5 was written last, and whichever restore lands
    /// first ends both. The worst case is a burst that finishes early - never a zombie stuck
    /// fast - which is the direction to fail in.
    /// </remarks>
    private void Rally()
    {
        // The screamer speeds up with the pack it called. Applied directly rather than left
        // to meet itself in the sweep below: that sweep is by TAG, and WaveManager is what
        // sets the tag on the frame it spawns a zombie - a Yokai dropped into the scene by
        // hand, or by an editor probe, carries none and would otherwise rally nobody at all.
        ApplyRally(Zombie);

        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        float radiusSquared = rallyRadius * rallyRadius;

        foreach (GameObject enemy in enemies)
        {
            if (enemy == null) continue;

            // Ground distance, like every other range in a role: a zombie on the riverbank
            // below is as rallied as one on the path.
            Vector3 delta = enemy.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > radiusSquared) continue;

            ZombieAI other = enemy.GetComponent<ZombieAI>();
            if (other == null || other.IsDead()) continue;

            // The boss is never rallied - it drives this same float for its phases, and a
            // 1.5 written over Mathf.Pow(1.15, phase) either cancels the escalation the
            // player is listening for or freezes it there. See this file's header.
            if (enemy.GetComponent<BossZombieController>() != null) continue;

            // Told, not asked: a rallied screamer stays quiet for a full interval, so two
            // Yokai in earshot cannot answer each other into a siren. Never this one - it is
            // in its own sweep, and silencing itself would only overwrite the cooldown
            // ScreamWatch is about to set from the end of this scream.
            ZombieScreamer neighbour = enemy.GetComponent<ZombieScreamer>();
            if (neighbour != null && neighbour != this) neighbour.NotifyRallied();

            ApplyRally(other);
        }
    }

    /// <summary>
    /// One zombie's share of a rally: the burst now, and the promise to take it back. The
    /// Contains guard is what keeps that promise down to exactly one coroutine per zombie -
    /// a second would restore the scale when the FIRST five seconds were up and cut the
    /// rally short for everything that heard the second scream.
    /// </summary>
    private void ApplyRally(ZombieAI zombie)
    {
        if (zombie == null || zombie.IsDead()) return;

        zombie.SetPhaseSpeedScale(rallySpeedScale);

        if (rallied.Contains(zombie)) return;

        rallied.Add(zombie);
        StartCoroutine(RestoreAfterRally(zombie));
    }

    /// <summary>
    /// Hands one rallied zombie its own speed back after rallySeconds, or the moment it dies.
    /// </summary>
    private IEnumerator RestoreAfterRally(ZombieAI zombie)
    {
        float remaining = rallySeconds;

        // Polled rather than one flat wait so that a zombie killed at second two is let go
        // at second two. Nothing downstream depends on the exact moment; what matters is
        // that the loop cannot be stuck holding a reference to something that has died.
        while (remaining > 0f && zombie != null && !zombie.IsDead())
        {
            yield return new WaitForSeconds(RallyPollSeconds);
            remaining -= RallyPollSeconds;
        }

        // Put back whether it lived or not. phaseSpeedScale is one float shared with the
        // boss's phases and the Ghoul's lunge, and a corpse left holding 1.5 hands it to
        // nothing while this component still believes the debt is outstanding.
        if (zombie != null) zombie.SetPhaseSpeedScale(1f);

        rallied.Remove(zombie);
    }

    /// <summary>
    /// Called by another screamer's rally, on the screamers it has just sped up. Silences
    /// this one for a full screamInterval: without it two Yokai in earshot answer each
    /// other's screams every screamSeconds for ever, the pack never drops back to its own
    /// speed, and the one sound the player is meant to react to becomes the background.
    /// </summary>
    public void NotifyRallied()
    {
        // Max, not assignment: being rallied may only ever push the next scream further out,
        // never pull a cooldown this screamer is already serving forward.
        nextScreamTime = Mathf.Max(nextScreamTime, Time.time + screamInterval);
    }

    /// <summary>
    /// The only animator write in this class, and only ever while control is held. The
    /// controller has Walk, Attack and Dying and no idle state, so with ZombieAI paused -
    /// and nothing else clearing IsMoving - a screaming Yokai would walk on the spot for the
    /// length of its own tell. Guarded rather than merely documented: an animator write from
    /// a role that does not hold the wheel is a fight with ZombieAI's own Update.
    /// </summary>
    private void StandStill()
    {
        if (!HasControl || animator == null) return;

        animator.SetBool("IsMoving", false);
    }

    private void SpawnRallyRing()
    {
        if (rallyRing == null) return;

        // At the pivot, which on these models sits between the feet, and unrotated: a ring
        // spreading from the screamer has no facing to match.
        GameObject ring = Instantiate(rallyRing, transform.position, Quaternion.identity);
        Destroy(ring, rallyRingLifetime);
    }
}
