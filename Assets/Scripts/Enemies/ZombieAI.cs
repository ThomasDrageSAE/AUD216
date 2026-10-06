using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/*
 * ZombieAI.cs
 * 
 * Purpose: Controls zombie behavior, combat, and lifecycle
 * Used by: Enemy system, wave management
 * 
 * Key Features:
 * - Health and damage system
 * - Player tracking and pursuit
 * - Attack patterns
 * - Death handling
 * - UI health display
 * - XP rewards
 * 
 * AI Behaviors:
 * - Detection range checking
 * - Attack range management
 * - Movement control
 * - Animation state management
 * 
 * Performance Considerations:
 * - Efficient distance calculations
 * - Smart animation transitions
 * - Optimized UI updates
 * - Clean event handling
 * 
 * Dependencies:
 * - Requires Animator component
 * - ZombieManager integration
 * - PlayerStats for XP
 * - UI canvas system
 *
 * Two seams for the roles and the combat feedback, added 2026-09-07. Neither changes what
 * this class does; both let something else watch or have a say:
 * - OnZombieHit, raised once per landed hit, so the HUD can mark it without this class
 *   knowing the HUD exists.
 * - IZombieDamageModifier, asked once per positional hit, so a shield can take the sting
 *   out of a round without this class knowing what a shield is.
 */

/// <summary>
/// What one hit did. Handed to <see cref="ZombieAI.OnZombieHit"/> after the damage has
/// landed, so <see cref="Killed"/> is the truth and not a prediction.
/// </summary>
public readonly struct ZombieHit
{
    public ZombieHit(ZombieAI zombie, Vector3 point, bool headshot, float damage, bool killed, float xpReward)
    {
        Zombie = zombie;
        Point = point;
        Headshot = headshot;
        Damage = damage;
        Killed = killed;
        XpReward = xpReward;
    }

    /// <summary>The zombie that was hit. Alive or not - test <see cref="Killed"/>, not the object.</summary>
    public ZombieAI Zombie { get; }

    /// <summary>Where the hit landed in the world. For a hit with no position (a blast), the zombie's chest.</summary>
    public Vector3 Point { get; }

    public bool Headshot { get; }

    /// <summary>What actually landed, after any modifier and after the headshot multiplier.</summary>
    public float Damage { get; }

    /// <summary>True on the one hit that emptied the bar.</summary>
    public bool Killed { get; }

    /// <summary>The kill's XP, so a listener need not reach back into the zombie for it.</summary>
    public float XpReward { get; }
}

/// <summary>
/// A component on the same zombie that has a say in incoming damage - the shield role is
/// the reason this exists. Asked once per hit that arrives with a position, before the
/// headshot multiplier.
///
/// That includes a grenade's blast: it reaches a zombie through the same positional path a
/// bullet does, with the point the blast's line-of-sight ray struck. The work order guessed
/// otherwise; the code was read. A shield covers an arc, and a blast arrives from a
/// direction, so it is blocked like anything else coming from the front - flank it or aim
/// at the head.
/// </summary>
public interface IZombieDamageModifier
{
    /// <summary>Returns the damage that should land. <paramref name="headshot"/> is already decided.</summary>
    float Modify(float damage, Vector3 hitPoint, bool headshot);
}

public class ZombieAI : MonoBehaviour, IDamageable
{
    [Header("NPC Settings")]
    [SerializeField] private string npcName = "Zombie";
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    [Header("UI Elements")]
    [SerializeField] private Canvas worldSpaceCanvas;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TextMeshProUGUI healthText;
    [Tooltip("Metres within which a damaged zombie shows its health bar. Past it the " +
             "world-space canvas is switched off - it costs more per zombie per frame " +
             "than the AI does. Set to 0 to never gate by distance.")]
    [SerializeField] private float healthBarVisibleDistance = 25f;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 3f;
    [Tooltip("Set to 0 for infinite detection range")]
    [SerializeField] private float detectionRange = 0f;
    [Tooltip("Seconds between destination writes while chasing. The agent still steers " +
             "along the path it already has every frame - only the re-plan is rate " +
             "limited. Set to 0 to write the destination every frame, as it used to.")]
    [SerializeField] private float repathInterval = 0.25f;
    private float nextRepathTime;
    private Transform playerTransform;
    private NavMeshAgent agent;

    [Header("Headshots")]
    [Tooltip("Damage multiplier for a hit in the head band. Tunable per prefab - the " +
             "boss can take less than 2x so headshots do not trivialise it")]
    [Min(1f)] [SerializeField] private float headshotMultiplier = 2f;

    [Tooltip("Top fraction of the collider that counts as the head. The zombie is a " +
             "single capsule, so the top band IS the head")]
    [Range(0.05f, 0.5f)] [SerializeField] private float headHeightFraction = 0.25f;

    private Collider bodyCollider;

    [Header("Attack Settings")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1f;
    [SerializeField] private float attackDamage = 20f;
    private float lastAttackTime;
    private bool isAttacking;
    // Tracked so a re-entered attack replaces the previous sequence instead of stacking
    // another one that never terminates.
    private Coroutine attackRoutine;

    // Wave system modifiers
    private float speedMultiplier = 1f;

    // Boss phase modifier. A separate factor rather than sharing speedMultiplier:
    // the wave system writes that one, and whichever wrote last would win.
    private float phaseSpeedScale = 1f;
    public event System.Action OnZombieDeath;

    // Animator reference
    private Animator animator;
    private bool isDying = false;
    private ZombieManager zombieManager;

    [Header("XP Reward")]
    // Tripled 2026-09-04 at the owner's request, at the same time as the grant above was
    // wired up. Every prefab overrides this, so the default only applies to a zombie
    // created from scratch. See PERFORMANCE_FIX_TASKS.md for the resulting curve.
    [SerializeField] private float xpReward = 75f;  // Base XP reward for killing this zombie type
    public float XPReward => xpReward;

    [Header("Audio")]
    [SerializeField] private string enemyAudioProfileName; // Match this with profile name in EnemyAudioManager
    [Tooltip("Compile-checked identity. When set, this wins over the name above - which " +
             "stays as the fallback for anything not yet migrated.")]
    [SerializeField] private EnemyId enemyAudioProfileId = EnemyId.Unassigned;

    /// <summary>
    /// The key handed to EnemyAudioManager: the id when one is assigned, otherwise the
    /// authored string. Both resolve through the same lookup - an assigned id is
    /// registered there under its own name - so this only changes which spelling is
    /// used, never which profile answers.
    /// </summary>
    private string AudioProfileKey => AudioProfileIds.IsAssigned(enemyAudioProfileId)
        ? AudioProfileIds.NameOf(enemyAudioProfileId)
        : enemyAudioProfileName;
    private AudioSource audioSource;
    private AudioSource attackAudioSource;
    private EnemyAudioManager enemyAudioManager;

    // How long the death sound gets before the source starts fading out.
    private const float DeathSoundGrace = 2.5f;

    private void Start()
    {
        currentHealth = maxHealth;

        // Find player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }

        // Get the NavMeshAgent
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.speed = moveSpeed;
            agent.stoppingDistance = attackRange;
        }

        // A random phase offset, not zero - that offset is the whole point. A wave
        // spawns up to forty of these in a single frame, and seeding them all to the
        // same deadline would put every repath in the wave back on the same frame.
        nextRepathTime = Time.time + Random.value * repathInterval;

        // Setup collider if needed; cache it either way - IsHeadPoint runs per bullet.
        bodyCollider = GetComponent<Collider>();
        if (bodyCollider == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.5f;
            capsule.center = new Vector3(0, 1f, 0);
            bodyCollider = capsule;
        }

        // Get the animator component
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            // Every state (movement, attack, death) drives the animator; without
            // it Update would NRE every frame, so disable the AI outright.
            Debug.LogError("Animator component missing from " + gameObject.name + " - disabling ZombieAI.");
            enabled = false;
            return;
        }

        // Initialize UI
        InitializeHealthUI();
        InitializeNameUI();

        // Zombies spawn at full health and never heal, so there is nothing for the bar
        // to say yet. Update switches the canvas on the first time one is hurt within
        // reading distance.
        if (worldSpaceCanvas != null)
            worldSpaceCanvas.enabled = false;

        // Register with ZombieManager
        zombieManager = FindFirstObjectByType<ZombieManager>();
        if (zombieManager != null)
        {
            zombieManager.RegisterZombie(gameObject);
        }

        // Setup audio
        audioSource = GetComponent<AudioSource>();
        bool addedFallbackVoice = false;
        if (audioSource == null)
        {
            // Every shipped enemy prefab carries a root source, so this is the latent path.
            // A bare AddComponent is 2D and routed straight to Master; the manager gives it
            // the profile's routing below, before anything is ever played through it.
            audioSource = gameObject.AddComponent<AudioSource>();
            addedFallbackVoice = true;
        }

        // Get the attack audio source from child
        Transform attackAudioChild = transform.Find("Audio Source - Attack");
        if (attackAudioChild != null)
        {
            attackAudioSource = attackAudioChild.GetComponent<AudioSource>();
        }

        enemyAudioManager = FindFirstObjectByType<EnemyAudioManager>();
        if (enemyAudioManager != null)
        {
            // Mixer group, 3D blend and play-on-awake for a voice this component had to
            // invent. The manager owns the profile, so it owns the routing.
            if (addedFallbackVoice)
            {
                enemyAudioManager.ConfigureFallbackVoice(AudioProfileKey, audioSource);
            }

            // Start the looping ambient sound
            enemyAudioManager.StartVocalizationLoop(AudioProfileKey, audioSource);
        }
    }

    private void Update()
    {
        // Check for death first, before any other state checks
        if (currentHealth <= 0 && !isDying)
        {
            Die();
            return;
        }

        if (isDying)
        {
            // The agent is disabled in Die(); touching it here logged an error every
            // frame once the corpse sank off the NavMesh.
            return;
        }

        if (playerTransform != null)
        {
            // Get current animation state
            var currentState = animator.GetCurrentAnimatorStateInfo(0);
            bool isInAttackAnimation = currentState.IsName("Attack");

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Above the attack-animation return below, deliberately: a zombie locked in
            // its swing still needs its bar gated, and this is the one place per frame
            // where the distance is already paid for.
            UpdateHealthBarVisibility(distanceToPlayer);

            // If we're in attack animation, force position lock
            if (isInAttackAnimation)
            {
                // isStopped logs an error on an agent that is not on the NavMesh, so every
                // write in this file tests both - the way Die() below already does, and
                // ZombieRole.TakeControl/ReleaseControl and BossZombieController. On the
                // mesh, which is where a zombie spends its life, nothing here changes.
                if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
                animator.SetBool("IsMoving", false);
                return;
            }

            // If we're not in attack animation, ensure we can move
            isAttacking = false;  // Make sure we clear the attack flag

            if (detectionRange == 0 || distanceToPlayer <= detectionRange)
            {
                if (distanceToPlayer > attackRange)
                {
                    // The neighbouring branches have always null-checked the agent; this
                    // one did not, and a zombie prefab without a NavMeshAgent NRE'd here
                    // every frame it was out of attack range.
                    if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
                    if (agent != null) agent.speed = moveSpeed * speedMultiplier * phaseSpeedScale;
                    // Throttled, not skipped: the agent keeps steering along the path
                    // it already has every frame, and its own autoRepath re-plans the
                    // moment that path is invalidated. Only the destination write is
                    // rate limited, and a walking zombie cannot gain on a quarter second
                    // of stale target. Forty of them writing one every frame was most of
                    // what Crowd.Pathfinding cost.
                    // isOnNavMesh is tested second so the native call is only paid on
                    // repath frames - a zombie spawned off the baked mesh would otherwise
                    // spam an error every frame for the rest of its life.
                    // An interval of 0 is tested outright rather than left to the
                    // deadline: the value can be lowered at runtime on a zombie whose
                    // deadline is still in the future, and 0 has to mean every frame
                    // from that instant, not from the next one.
                    if (agent != null && (repathInterval <= 0f || Time.time >= nextRepathTime)
                        && agent.isOnNavMesh)
                    {
                        nextRepathTime = Time.time + repathInterval;
                        agent.SetDestination(playerTransform.position);
                    }
                    animator.SetBool("IsMoving", true);
                }
                else
                {
                    animator.SetBool("IsMoving", false);
                    if (agent != null && agent.isOnNavMesh) agent.isStopped = true;

                    // Look at player for attack
                    Vector3 directionToPlayer = (playerTransform.position - transform.position).normalized;
                    directionToPlayer.y = 0;
                    transform.rotation = Quaternion.LookRotation(directionToPlayer);
                    StartAttack();
                }
            }
            else
            {
                animator.SetBool("IsMoving", false);
                if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
            }
        }
    }

    private void StartAttack()
    {
        if (animator != null && !isAttacking && Time.time >= lastAttackTime + attackCooldown)
        {
            // Ensure movement is stopped
            animator.SetBool("IsMoving", false);
            isAttacking = true;
            lastAttackTime = Time.time;
            
            // Set attack parameters
            animator.SetTrigger("Attack");
            animator.SetBool("IsAttacking", true);
            
            if (attackAudioSource != null && enemyAudioManager != null)
            {
                StartCoroutine(PlayDelayedAttackSound());
            }
            
            // Update() clears isAttacking on any frame outside the attack animation, so
            // StartAttack re-enters every cooldown. Without replacing the old sequence,
            // a zombie in melee contact accrued one permanently-running coroutine per
            // second, each polling the animator and distance every frame.
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
            }
            attackRoutine = StartCoroutine(AttackSequence());
        }
    }

    private IEnumerator PlayDelayedAttackSound()
    {
        yield return new WaitForSeconds(enemyAudioManager.GetAttackSoundDelay(AudioProfileKey));
        if (attackAudioSource != null)
        {
            enemyAudioManager.PlayAttackSound(AudioProfileKey, attackAudioSource);
        }
    }

    private IEnumerator AttackSequence()
    {
        while (true)
        {
            // Check if we should stop attacking
            if (playerTransform == null)
            {
                break;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > attackRange)
            {
                break;
            }

            // Wait until damage point in animation
            if (animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") && 
                animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.5f &&
                Time.time >= lastAttackTime + attackCooldown)
            {
                DealDamage();
                lastAttackTime = Time.time;
                
                // Wait until near end of current attack
                while (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.9f)
                {
                    // Check distance even during animation
                    distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
                    if (distanceToPlayer > attackRange)
                    {
                        EndAttack();
                        yield break;
                    }
                    yield return null;
                }
            }

            yield return null;
        }

        EndAttack();
    }

    /// <summary>
    /// Stops the melee sequence outright. BossZombieController calls this when the
    /// slam borrows control: disabling this component does NOT stop its coroutines,
    /// so the attack loop would keep polling the animator - which the slam is now
    /// driving - and land a melee hit inside the wind-up.
    /// </summary>
    public void CancelAttack()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }

        EndAttack();
    }

    private void EndAttack()
    {
        attackRoutine = null;
        isAttacking = false;
        animator.SetBool("IsAttacking", false);
    }

    private void DealDamage()
    {
        if (playerTransform != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer <= attackRange)
            {
                // PlayerStats (the IDamageable) lives on a CHILD of the tagged Player
                // root, so GetComponent on the root transform found nothing and zombies
                // dealt no damage at all. Prefer the singleton - and hand it this zombie,
                // so the edge-of-screen smear points back at the attacker; fall back to a
                // child search so a differently-arranged player rig still takes hits.
                PlayerStats stats = PlayerStats.Instance;
                if (stats != null)
                {
                    stats.TakeDamage(attackDamage, transform);
                }
                else
                {
                    IDamageable playerDamageable = playerTransform.GetComponentInChildren<IDamageable>();
                    if (playerDamageable != null)
                    {
                        playerDamageable.TakeDamage(attackDamage);
                    }
                }
            }
        }
    }

    private void InitializeNameUI()
    {
        if (nameText != null)
        {
            nameText.text = npcName;
        }
    }

    private void InitializeHealthUI()
    {
        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = 1f;
            healthSlider.value = 1f;
        }

        UpdateHealthUI();
    }

    private void UpdateHealthUI()
    {
        if (healthSlider != null)
        {
            healthSlider.value = currentHealth / maxHealth;
        }

        if (healthText != null)
        {
            healthText.text = $"{Mathf.Ceil(currentHealth)}";
        }
    }

    /// <summary>
    /// Decides whether the world-space health bar is worth drawing at all. The canvas is
    /// the most expensive thing on a zombie - measured, more per frame than this script
    /// and the animator together - and a full-health bar tells the player nothing, so it
    /// stays off until something has actually hurt the zombie and the player is close
    /// enough to read it. Called once per Update with the distance already computed there.
    /// </summary>
    private void UpdateHealthBarVisibility(float distanceToPlayer)
    {
        if (worldSpaceCanvas == null) return;

        // 0 means "no distance gate"; the full-health rule still applies either way.
        bool withinReadingDistance = healthBarVisibleDistance <= 0f
                                     || distanceToPlayer <= healthBarVisibleDistance;
        bool shouldShow = currentHealth < maxHealth && withinReadingDistance;

        // Compare before writing: toggling a Canvas is itself a rebuild, so writing the
        // same value every frame would cost more than leaving the bar on ever did.
        if (worldSpaceCanvas.enabled != shouldShow)
        {
            worldSpaceCanvas.enabled = shouldShow;
        }
    }

    /// <summary>
    /// True when a world-space hit lands in the head band - the top Head Height
    /// Fraction of the collider's WORLD bounds, so the test stays correct at any
    /// prefab or instance scale.
    /// </summary>
    public bool IsHeadPoint(Vector3 worldPoint)
    {
        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
        if (bodyCollider == null) return false;

        Bounds bounds = bodyCollider.bounds;
        return worldPoint.y >= bounds.max.y - bounds.size.y * headHeightFraction;
    }

    /// <summary>
    /// Raised once for every hit that lands on any zombie, after the damage has been
    /// applied. Static, so a listener subscribes once instead of hunting down every zombie
    /// as it spawns - the same reason WeaponController.OnHudStateChanged is static. A
    /// static event outlives a scene reload, so every subscriber unsubscribes in OnDisable.
    /// </summary>
    public static event System.Action<ZombieHit> OnZombieHit;

    /// <summary>
    /// Clears the static event before the first scene loads, as WeaponController does for
    /// OnHudStateChanged (:561-562) and Item does for OnDropped. Domain reload does this
    /// for free today; the day fast enter-play-mode is turned on, it does not, and a
    /// subscriber from the previous run would be invoked with a destroyed listener.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnZombieHit = null;

    /// <summary>
    /// The profile key this zombie's sounds are addressed by. Public so a role component
    /// beside this one can play its own clips through the same profile the zombie already
    /// uses, rather than carrying a second spelling of the same name.
    /// </summary>
    public string ProfileKey => AudioProfileKey;

    /// <summary>
    /// True while the melee swing is running. A role reads this to keep out of the way -
    /// CancelAttack() is for interrupting a swing, not for asking whether there is one.
    /// </summary>
    public bool IsAttacking => isAttacking;

    /// <summary>
    /// Positional damage: a hit in the head band pays headshotMultiplier times the
    /// damage and plays the profile's headshot sound. A deliberate OVERLOAD rather
    /// than a change to IDamageable, so PlayerStats and DamageableObject are
    /// untouched; callers without a hit position keep using the flat version.
    ///
    /// The one place a modifier gets a say. Order matters and is the shield's rule: the
    /// modifier sees the RAW damage and knows whether it is a headshot, so it can wave a
    /// headshot through, and the multiplier is applied to whatever it returns - a blocked
    /// hit that is somehow also a headshot pays double the blocked amount, not double the
    /// raw one.
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitPoint)
    {
        if (isDying) return;

        bool headshot = IsHeadPoint(hitPoint);
        float landing = damage;

        // TryGetComponent, not GetComponent: this runs per bullet, and the minigun fires
        // twenty a second. It allocates nothing when there is no modifier, which is four
        // zombies out of five.
        if (TryGetComponent(out IZombieDamageModifier modifier))
        {
            landing = modifier.Modify(landing, hitPoint, headshot);
        }

        if (headshot)
        {
            AudioEventLog.Record(AudioEventKind.Play, "Headshot", npcName);

            if (enemyAudioManager != null)
            {
                enemyAudioManager.PlayHeadshotSound(AudioProfileKey, audioSource);
            }

            landing *= headshotMultiplier;
        }

        ApplyDamage(landing);
        RaiseHit(hitPoint, headshot, landing);
    }

    /// <summary>
    /// The bare IDamageable path. Deliberately raises NO hit event and asks no modifier.
    ///
    /// Measured 2026-09-07: every weapon in this game reaches a zombie through
    /// DamageableUtil.Damage, which routes ZombieAI to the POSITIONAL overload above -
    /// bullets, arrows, the axe and the grenade's blast alike. The only caller left on this
    /// one is BossZombieController collapsing its own summons with float.MaxValue when the
    /// boss dies. Reporting those as hits would flash the player's hitmarker a dozen times
    /// for kills they did not make, which is why the event lives upstairs and not here.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (isDying) return;

        ApplyDamage(damage);
    }

    /// <summary>
    /// Where the health actually moves. Both entry points funnel through here so that
    /// neither can raise the hit event twice - the positional overload used to call the
    /// flat one, and after both learned to report, that would have been two hits for one
    /// bullet.
    /// </summary>
    private void ApplyDamage(float damage)
    {
        currentHealth = Mathf.Max(0, currentHealth - damage);
        UpdateHealthUI();

        if (currentHealth <= 0 && !isDying)
        {
            Die();
        }
    }

    /// <summary>
    /// Reports the hit. Called AFTER the damage has landed, so isDying is the answer to
    /// "did this one kill it" rather than a guess. A listener that throws must not take the
    /// zombie down with it - a broken HUD is not worth a dead enemy standing still.
    /// </summary>
    private void RaiseHit(Vector3 point, bool headshot, float damage)
    {
        if (OnZombieHit == null) return;

        try
        {
            OnZombieHit.Invoke(new ZombieHit(this, point, headshot, damage, isDying, xpReward));
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    /// <summary>Roughly chest height on these models, for a hit that has no position of its own.</summary>
    private const float ChestHeight = 1.6f;

    private void Die()
    {
        isDying = true;
        isAttacking = false;

        // The kill XP. xpReward has been authored per prefab since the beginning (25 for a
        // Yokai, up to 1500 for a boss) and exposed as XPReward - but nothing in the project
        // ever read it, so killing a zombie granted nothing at all. That, not the size of the
        // numbers, is why XP appeared not to move. Granted here rather than from a listener
        // because Die() is the one place that runs exactly once: both callers test !isDying
        // and this sets it first.
        if (PlayerStats.Instance != null)
        {
            // The overload with a position, so the toast can pop at the corpse rather than
            // at the bottom of the screen. Still granted exactly once, from here.
            PlayerStats.Instance.AddXP(xpReward, transform.position + Vector3.up * ChestHeight);
        }

        // Stop the attack loop and any pending delayed attack sound before the death
        // coroutines start. Without this a zombie killed mid-swing still landed its
        // hit and still shouted from the corpse.
        StopAllCoroutines();
        attackRoutine = null;

        // Notify any listeners
        OnZombieDeath?.Invoke();

        // A zombie that dies normally is never unregistered by OnDestroy (it guards on
        // !isDying), so ZombieManager's list stayed non-empty and combat music never
        // switched back off after the last kill.
        if (zombieManager != null)
        {
            zombieManager.UnregisterZombie(gameObject);
        }

        // The idle vocalisation loop runs on the audio manager, so StopAllCoroutines
        // above does not reach it. Left running, the corpse keeps grunting.
        if (enemyAudioManager != null)
        {
            enemyAudioManager.StopVocalizationLoop(audioSource);
        }

        if (attackAudioSource != null)
        {
            attackAudioSource.Stop();
        }

        // Stop movement and disable collider. The agent has to be disabled, not merely
        // stopped: DeathSequence drives the transform two units below the mesh, which
        // an enabled agent fights and then logs errors about once it is off the NavMesh.
        if (agent != null)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }
        if (GetComponent<Collider>() != null)
            GetComponent<Collider>().enabled = false;

        // Hide UI elements
        if (worldSpaceCanvas != null)
            worldSpaceCanvas.enabled = false;

        // Play death animation and sound
        animator.SetBool("IsDying", true);

        if (enemyAudioManager != null)
        {
            enemyAudioManager.PlayDeathSound(AudioProfileKey, audioSource);
        }

        // Start coroutine to handle death sequence
        StartCoroutine(DeathSequence());
        StartCoroutine(FadeOutAudio());
    }

    private IEnumerator DeathSequence()
    {
        // Wait until we're in the death animation
        while (!animator.GetCurrentAnimatorStateInfo(0).IsName("Dying"))
        {
            yield return null;
        }
        
        // Wait for death animation to complete
        while (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.95f)
        {
            yield return null;
        }
        
        // Pause animation
        animator.speed = 0;
        
        // Wait before starting sink animation
        yield return new WaitForSeconds(4f);
        
        // Sink into ground
        float sinkDuration = 1.5f;
        float elapsedTime = 0f;
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos - new Vector3(0, 2f, 0); // Sink 2 units down
        
        while (elapsedTime < sinkDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / sinkDuration;
            transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
        
        // Destroy the game object
        Destroy(gameObject);
    }

    private IEnumerator FadeOutAudio()
    {
        // Let the death sound play before ducking the source. This fade previously
        // started on the same frame the death one-shot was fired on the same
        // AudioSource, so every death sound was faded out under itself and then cut
        // off by the Stop() at the end.
        yield return new WaitForSeconds(DeathSoundGrace);

        if (audioSource != null)
        {
            float startVolume = audioSource.volume;
            float elapsedTime = 0f;
            float fadeDuration = 2f;

            while (elapsedTime < fadeDuration)
            {
                elapsedTime += Time.deltaTime;
                float newVolume = Mathf.Lerp(startVolume, 0f, elapsedTime / fadeDuration);
                audioSource.volume = newVolume;
                yield return null;
            }

            audioSource.Stop();
        }
    }

    private void LateUpdate()
    {
        // No point aiming a canvas nobody is looking at. Update owns the enabled flag;
        // this only follows it.
        if (worldSpaceCanvas != null && worldSpaceCanvas.enabled && Camera.main != null)
        {
            worldSpaceCanvas.transform.LookAt(Camera.main.transform);
            worldSpaceCanvas.transform.Rotate(0, 180, 0);
        }
    }

    // Wave system methods
    public void SetSpeedMultiplier(float multiplier)
    {
        speedMultiplier = multiplier;
    }

    /// <summary>Extra speed factor for boss phases; stacks with the wave multiplier.</summary>
    public void SetPhaseSpeedScale(float scale)
    {
        phaseSpeedScale = Mathf.Max(0.1f, scale);
    }

    /// <summary>Remaining health, 0..1. BossZombieController reads it to drive phases.</summary>
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    public void SetHealthMultiplier(float multiplier)
    {
        maxHealth *= multiplier;
        currentHealth = maxHealth;
        UpdateHealthUI();
    }

    public bool IsDead()
    {
        return isDying || currentHealth <= 0;
    }

    private void OnDestroy()
    {
        // Covers zombies destroyed without dying (night teardown, scene unload).
        // UnregisterZombie is idempotent, so running after Die() has already
        // unregistered is harmless.
        if (zombieManager != null)
        {
            zombieManager.UnregisterZombie(gameObject);
        }
    }
}