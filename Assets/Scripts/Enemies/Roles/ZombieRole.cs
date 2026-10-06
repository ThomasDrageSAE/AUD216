using UnityEngine;
using UnityEngine.AI;

/*
 * ZombieRole.cs
 *
 * Purpose: The base every zombie role extends - runner, shield, spitter, screamer. A role
 *          sits BESIDE ZombieAI and layers one behaviour over it, exactly as
 *          BossZombieController does; it never duplicates movement, melee or death.
 *
 * Used by: ZombieRunner, ZombieScreamer, ZombieShield, ZombieSpitter, added to the enemy
 *          prefabs by Tools > Enemies > Add Enemy Roles.
 *
 * What a role gets from here: the components it always needs, the player, the profile key
 * its sounds are addressed by, a line-of-sight test, a way to speak, and - the part worth
 * reading - one way to borrow control of the zombie and one way to give it back.
 *
 * ⚠ Borrowed control MUST come back. ZombieAI.Die disables the NavMeshAgent, so a coroutine
 * that re-enables it afterwards gives a corpse its pathing back and it slides after the
 * player. Everything here releases through ReleaseControl(), which never switches on
 * something the zombie switched off itself.
 *
 * ⚠ One role per zombie. Two would fight over SetPhaseSpeedScale - a single float on
 * ZombieAI that the boss also uses for its phases. DisallowMultipleComponent stops the
 * obvious mistake; the check in Awake catches the subtle one (two DIFFERENT role types).
 *
 * Dependencies:
 * - ZombieAI (required), NavMeshAgent, an AudioSource on the zombie
 * - EnemyAudioManager for the role's clips; absent, the role runs and is silent
 */

[RequireComponent(typeof(ZombieAI))]
[DisallowMultipleComponent]
public abstract class ZombieRole : MonoBehaviour
{
    [Header("Role")]

    [Tooltip("Metres above the pivot the line-of-sight test looks from and at. Roughly eye " +
             "height on these models - a test from the feet is blocked by every kerb")]
    [SerializeField] protected float eyeHeight = 1.6f;

    protected ZombieAI Zombie { get; private set; }
    protected NavMeshAgent Agent { get; private set; }
    protected Transform Player { get; private set; }

    /// <summary>
    /// The source ZombieAI vocalises on, so a role's voice comes from the same throat.
    ///
    /// ⚠ Resolved on demand, NOT cached in Awake, and that is not fussiness: a prefab with
    /// no AudioSource gets one from ZombieAI.Start, and every Awake runs before every Start.
    /// Cached early, a role on such a prefab would hold null for the rest of its life and be
    /// silently, permanently mute - and silence is the one failure this project cannot see.
    /// Found by the agent writing the runner, 2026-09-07. Both roled prefabs happen to carry
    /// a source today, which is exactly how this would have gone unnoticed.
    /// </summary>
    protected AudioSource Voice
    {
        get
        {
            if (voice == null) voice = GetComponent<AudioSource>();
            return voice;
        }
    }

    private AudioSource voice;

    protected EnemyAudioManager EnemyAudio { get; private set; }

    /// <summary>The profile key this zombie's sounds are addressed by - ZombieAI owns the spelling.</summary>
    protected string AudioKey => Zombie != null ? Zombie.ProfileKey : null;

    /// <summary>True while this role is holding control it took from ZombieAI.</summary>
    protected bool HasControl { get; private set; }

    // Set only when WE disabled it, so ReleaseControl cannot switch on a ZombieAI that
    // something else - the boss's slam, the player's death - had switched off first.
    private bool disabledZombieAi;

    protected virtual void Awake()
    {
        Zombie = GetComponent<ZombieAI>();
        Agent = GetComponent<NavMeshAgent>();

        // A second role would silently fight this one over the phase speed scale, and the
        // loser would be whichever coroutine finished last. Louder to say so at startup.
        ZombieRole[] roles = GetComponents<ZombieRole>();
        if (roles.Length > 1)
        {
            Debug.LogError($"[{name}] carries {roles.Length} ZombieRole components " +
                           $"({string.Join(", ", System.Array.ConvertAll(roles, r => r.GetType().Name))}). " +
                           "A zombie may have at most one: they share ZombieAI's phase speed scale " +
                           "and would overwrite each other. Disabling this one.", this);
            enabled = false;
        }
    }

    protected virtual void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) Player = player.transform;

        // Null-checked at every use rather than required: a scene without the manager is a
        // scene where the roles still work and simply make no sound.
        EnemyAudio = FindFirstObjectByType<EnemyAudioManager>();
    }

    protected virtual void OnDisable()
    {
        ReleaseControl();
    }

    // ---- Asking ---------------------------------------------------------------

    /// <summary>
    /// Whether this role may START something right now: the zombie is alive and driving
    /// itself, its agent is usable, and there is a player to do it to.
    ///
    /// Before a behaviour begins, and only then. Once control has been taken the question
    /// changes and so does the answer - use <see cref="StillHolding"/> after a yield.
    /// </summary>
    protected bool CanAct =>
        Zombie != null && !Zombie.IsDead() && Zombie.enabled &&
        Agent != null && Agent.enabled && Agent.isOnNavMesh &&
        Player != null;

    /// <summary>
    /// Whether a behaviour that has ALREADY taken control may carry on - the question to ask
    /// after every yield inside a wind-up, a scream or a spit.
    ///
    /// ⚠ Deliberately NOT CanAct, and this is the trap: TakeControl disables ZombieAI, and
    /// CanAct requires it to be enabled. Checking CanAct after a yield therefore abandons
    /// every borrowed behaviour on its first frame - the Demon would never finish a retch.
    /// Found while writing the spitter, 2026-09-07; the work order said "re-check CanAct
    /// after every yield" and was wrong.
    ///
    /// The agent's state is not asked about either: ZombieAI.Die disables the agent on
    /// purpose, and IsDead() is the honest way to hear about that.
    /// </summary>
    protected bool StillHolding =>
        isActiveAndEnabled && Zombie != null && !Zombie.IsDead() && Player != null;

    /// <summary>Ground distance to the player. Height is irrelevant to every role's ranges.</summary>
    protected float DistanceToPlayer
    {
        get
        {
            if (Player == null) return float.PositiveInfinity;
            Vector3 delta = Player.position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }
    }

    /// <summary>
    /// Whether the player is visible from here, within <paramref name="maxDistance"/>. Cast
    /// from eye height to eye height and ignoring other zombies, so a pack does not blind
    /// itself: a screamer behind three of its own should still see the player.
    /// </summary>
    protected bool HasLineOfSight(float maxDistance)
    {
        if (Player == null) return false;

        Vector3 from = transform.position + Vector3.up * eyeHeight;
        Vector3 to = Player.position + Vector3.up * eyeHeight;
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance > maxDistance) return false;
        if (distance < 0.01f) return true;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int mask = enemyLayer >= 0 ? ~(1 << enemyLayer) : ~0;

        // Triggers ignored: the level is full of interaction volumes and pickup spheres, and
        // none of them is cover.
        if (!Physics.Raycast(from, delta / distance, out RaycastHit hit, distance, mask,
                             QueryTriggerInteraction.Ignore))
        {
            return true;   // nothing in the way at all
        }

        // The player's own collider counts as arriving, not as being blocked.
        return hit.transform != null && hit.transform.root == Player.root;
    }

    // ---- Speaking -------------------------------------------------------------

    /// <summary>
    /// The role's own voice, from the zombie. Silent and harmless when the manager, the
    /// profile or the clip array is missing - a behaviour never depends on a clip being
    /// authored, because for most of this project's life they will not be.
    /// </summary>
    protected void Say(RoleSound which)
    {
        if (EnemyAudio == null || Voice == null) return;
        EnemyAudio.PlayRoleSound(AudioKey, which, Voice);
    }

    /// <summary>
    /// The same, at a place rather than at the zombie - the spit's impact lands metres away
    /// and must be heard there.
    /// </summary>
    protected void SayAt(RoleSound which, Vector3 position)
    {
        if (EnemyAudio == null) return;
        EnemyAudio.PlayRoleSoundAt(AudioKey, which, position);
    }

    // ---- Borrowing and returning control --------------------------------------

    /// <summary>
    /// Takes the zombie away from ZombieAI for a moment - a wind-up, a scream, a spit.
    /// The boss's pattern: disable the brain, stop the agent, and be the only thing moving
    /// it. Returns false when the zombie is in no state to be borrowed, in which case
    /// nothing was taken and nothing needs giving back.
    /// </summary>
    protected bool TakeControl()
    {
        if (!CanAct) return false;

        if (Zombie.enabled)
        {
            Zombie.enabled = false;
            disabledZombieAi = true;
        }

        if (Agent != null && Agent.enabled && Agent.isOnNavMesh) Agent.isStopped = true;

        HasControl = true;
        return true;
    }

    /// <summary>
    /// Gives it back. Safe to call twice, safe on a dead zombie, and safe when nothing was
    /// ever taken - which is why every path can end with it. Re-enables ZombieAI only if
    /// this role is what disabled it AND the zombie is still alive: Die() disables the agent
    /// on purpose, and a corpse whose pathing came back walks.
    /// </summary>
    protected void ReleaseControl()
    {
        bool alive = Zombie != null && !Zombie.IsDead();

        if (Zombie != null)
        {
            // The burst scale always goes back, alive or not: it is a multiplier the boss
            // and the roles share, and a corpse holding 2.2 would hand it to nothing.
            Zombie.SetPhaseSpeedScale(1f);

            if (disabledZombieAi && alive) Zombie.enabled = true;
        }

        disabledZombieAi = false;

        if (alive && Agent != null && Agent.enabled && Agent.isOnNavMesh) Agent.isStopped = false;

        HasControl = false;
    }

    /// <summary>Turns to face the player on the ground plane. What a tell is delivered to.</summary>
    protected void FacePlayer()
    {
        if (Player == null) return;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.LookRotation(toPlayer);
    }
}
