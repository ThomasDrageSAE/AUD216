using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

/*
 * BossZombieController.cs
 *
 * Purpose: Everything that makes the boss a boss. Sits beside ZombieAI on the boss
 *          zombie and layers five behaviours over it, each with audio as the surface:
 *
 *   1. Presence  - an entrance roar, and a floor under the music intensity for as long
 *                  as the boss lives, so every stem is up for the whole fight.
 *   2. Phases    - at authored health fractions the boss cracks (a stinger), speeds up,
 *                  and its voice deepens/rises via vocal pitch. The player HEARS how the
 *                  fight is going without reading a bar.
 *   3. Slam      - a telegraphed area attack: wind-up bellow (the tell), then an impact
 *                  with distance-scaled camera shake and damage in a ring. Dodged with
 *                  the ears first.
 *   4. Summons   - a periodic bellow that raises minions near the boss. They register
 *                  with ZombieManager themselves, so the music intensity climbs for free.
 *   5. Full stop - on death: the boss's own summons collapse, the mix dips into an
 *                  optional silence snapshot for a beat before relaxing, and a reward
 *                  item drops at the corpse.
 *
 * Used by: the boss zombie prefab; added with Tools > Enemies > Add Boss Controller.
 *
 * All clips here are authorable slots: serialized AudioClip fields, so they appear in
 * the Ctrl+G report, and every play is recorded for the Ctrl+J monitor. Anything left
 * empty simply does not sound - the behaviour still runs.
 *
 * ZombieAI still owns movement, melee and death; this component never duplicates those.
 * The slam borrows control briefly (ZombieAI disabled, agent stopped) and returns it.
 */
[RequireComponent(typeof(ZombieAI))]
[DisallowMultipleComponent]
public class BossZombieController : MonoBehaviour
{
    [Header("Presence")]
    [Tooltip("Roar played once when the boss appears")]
    [SerializeField] private AudioClip entranceRoar;
    [Range(0f, 1f)] [SerializeField] private float entranceRoarVolume = 1f;

    [Tooltip("Floor under the music intensity while the boss lives - 1 keeps every stem " +
             "up for the whole fight. Cleared on death")]
    [Range(0f, 1f)] [SerializeField] private float musicIntensityFloor = 1f;

    [Header("Phases")]
    [Tooltip("Health fractions that trigger a phase change, highest first (0.66 = at " +
             "two-thirds health)")]
    [SerializeField] private float[] phaseHealthFractions = { 0.66f, 0.33f };

    [Tooltip("Armour-crack stinger per phase change (index 0 = first crack). Fewer clips " +
             "than phases reuses the last one")]
    [SerializeField] private AudioClip[] phaseCrackSounds;
    [Range(0f, 1f)] [SerializeField] private float phaseCrackVolume = 1f;

    [Tooltip("Movement speed factor per phase change, applied cumulatively on top of the " +
             "wave multiplier (1.15 then 1.15 again = 1.32x)")]
    [SerializeField] private float phaseSpeedScalePerPhase = 1.15f;

    [Tooltip("Vocal pitch factor per phase change - the voice fraying is the tell")]
    [SerializeField] private float phasePitchScalePerPhase = 1.08f;

    [Header("Ground slam")]
    [Tooltip("Seconds between slam attempts. 0 disables the slam")]
    [Min(0f)] [SerializeField] private float slamIntervalSeconds = 12f;

    [Tooltip("The slam only triggers with the player inside this range, metres")]
    [Min(1f)] [SerializeField] private float slamTriggerRange = 10f;

    [Tooltip("Seconds between the wind-up bellow and the impact - the dodge window")]
    [Min(0.2f)] [SerializeField] private float slamWindupSeconds = 1.2f;

    [Tooltip("Damage radius of the impact, metres")]
    [Min(0.5f)] [SerializeField] private float slamRadius = 6f;

    [Min(0f)] [SerializeField] private float slamDamage = 30f;

    [Tooltip("The tell. Authoring this is what makes the slam dodgeable by ear")]
    [SerializeField] private AudioClip slamWindupRoar;
    [SerializeField] private AudioClip slamImpactSound;
    [Range(0f, 1f)] [SerializeField] private float slamVolume = 1f;

    [Tooltip("Camera shake at the impact point; it falls off with distance out to Shake Range")]
    [Min(0f)] [SerializeField] private float slamShakeMagnitude = 0.5f;
    [Min(1f)] [SerializeField] private float slamShakeRange = 25f;

    [Header("Summoning")]
    [Tooltip("Seconds between summoning bellows. 0 disables summoning")]
    [Min(0f)] [SerializeField] private float summonIntervalSeconds = 20f;

    [Tooltip("The standard zombie prefab to raise. Minions register with ZombieManager " +
             "themselves, which also raises the music intensity")]
    [SerializeField] private GameObject minionPrefab;

    [Min(1)] [SerializeField] private int summonCount = 2;

    [Tooltip("Minions the boss may have alive at once; a bellow tops back up to this")]
    [Min(1)] [SerializeField] private int maxSummonedAlive = 4;

    [Tooltip("Metres around the boss where minions rise")]
    [Min(1f)] [SerializeField] private float summonRadius = 4f;

    [SerializeField] private AudioClip summonBellow;
    [Range(0f, 1f)] [SerializeField] private float summonBellowVolume = 1f;

    [Header("Death payoff")]
    [Tooltip("Optional mixer snapshot for the beat of silence after the kill. Leave " +
             "empty to skip the dip entirely")]
    [SerializeField] private AudioMixerSnapshot deathSilenceSnapshot;

    [Tooltip("Snapshot to relax back into - normally the mixer's default")]
    [SerializeField] private AudioMixerSnapshot normalSnapshot;

    [Tooltip("Seconds the silence holds before the mix relaxes back in")]
    [Min(0f)] [SerializeField] private float silenceSeconds = 1.5f;

    [Tooltip("Seconds for the mix to come back after the silence")]
    [Min(0.1f)] [SerializeField] private float silenceReleaseSeconds = 2.5f;

    [Tooltip("A DEACTIVATED scene item dropped at the corpse - the guaranteed reward")]
    [SerializeField] private Item rewardItem;

    private ZombieAI zombieAI;
    private NavMeshAgent agent;
    private Animator animator;
    private AudioSource voice;
    private MusicManager musicManager;
    private Transform playerTransform;

    private readonly List<ZombieAI> summoned = new List<ZombieAI>();
    private int phasesTriggered;
    private bool slamInProgress;
    private bool dead;
    private readonly MixerDip mixDip = new MixerDip();

    private void Start()
    {
        zombieAI = GetComponent<ZombieAI>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        musicManager = FindFirstObjectByType<MusicManager>();

        // The same source ZombieAI vocalises through, so phase pitch changes reach the
        // manager-driven vocal loop as well as the one-shots here.
        voice = GetComponent<AudioSource>();
        if (voice == null)
        {
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;

        zombieAI.OnZombieDeath += OnBossDeath;

        // 1. Presence: the fight owns the mix from the first frame.
        if (musicManager != null && musicIntensityFloor > 0f)
        {
            musicManager.SetIntensityOverride(musicIntensityFloor);
        }

        PlayVoice(entranceRoar, entranceRoarVolume, "entrance roar");

        StartCoroutine(PhaseWatch());
        if (slamIntervalSeconds > 0f) StartCoroutine(SlamLoop());
        if (summonIntervalSeconds > 0f) StartCoroutine(SummonLoop());
    }

    private void OnDestroy()
    {
        if (zombieAI != null) zombieAI.OnZombieDeath -= OnBossDeath;

        // Never leave the fight's marks on the mix - whatever way the boss leaves the
        // scene (death, night teardown, scene unload), the floor lifts and a dip that
        // was mid-flight resolves. Snapshot state lives on the mixer ASSET and would
        // otherwise survive into the next run.
        if (musicManager != null) musicManager.ClearIntensityOverride();
        mixDip.Restore(silenceReleaseSeconds);
    }

    // ---- 2. Phases -----------------------------------------------------------

    private IEnumerator PhaseWatch()
    {
        while (!dead)
        {
            if (phaseHealthFractions != null &&
                phasesTriggered < phaseHealthFractions.Length &&
                zombieAI.HealthFraction <= phaseHealthFractions[phasesTriggered])
            {
                phasesTriggered++;

                // The crack is the announcement; speed and voice are the lasting change.
                AudioClip crack = PickCrack();
                PlayVoice(crack, phaseCrackVolume, $"phase {phasesTriggered + 1} crack");

                zombieAI.SetPhaseSpeedScale(Mathf.Pow(phaseSpeedScalePerPhase, phasesTriggered));
                if (voice != null) voice.pitch = Mathf.Pow(phasePitchScalePerPhase, phasesTriggered);
            }

            yield return new WaitForSeconds(0.25f);
        }
    }

    private AudioClip PickCrack()
    {
        if (phaseCrackSounds == null || phaseCrackSounds.Length == 0) return null;
        return phaseCrackSounds[Mathf.Min(phasesTriggered - 1, phaseCrackSounds.Length - 1)];
    }

    // ---- 3. Ground slam ------------------------------------------------------

    private IEnumerator SlamLoop()
    {
        while (!dead)
        {
            yield return new WaitForSeconds(slamIntervalSeconds * Random.Range(0.8f, 1.2f));

            if (dead || zombieAI.IsDead() || playerTransform == null) continue;
            if (Vector3.Distance(transform.position, playerTransform.position) > slamTriggerRange) continue;

            yield return SlamRoutine();
        }
    }

    private IEnumerator SlamRoutine()
    {
        slamInProgress = true;

        // Borrow control: the boss plants itself for the wind-up. ZombieAI's Update is
        // paused rather than fought over.
        zombieAI.enabled = false;
        zombieAI.CancelAttack();   // its coroutines outlive the disable
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        if (animator != null)
        {
            animator.SetBool("IsMoving", false);
            animator.SetTrigger("Attack");
        }

        // The tell. Everything between here and the impact is the dodge window.
        PlayVoice(slamWindupRoar, slamVolume, "slam wind-up");

        yield return new WaitForSeconds(slamWindupSeconds);

        if (!dead && !zombieAI.IsDead())
        {
            PlayVoice(slamImpactSound, slamVolume, "slam impact");

            if (playerTransform != null)
            {
                float distance = Vector3.Distance(transform.position, playerTransform.position);

                // The shake reaches further than the damage, falling off with distance,
                // so a near miss still lands physically.
                if (CameraShakeController.Instance != null && distance < slamShakeRange)
                {
                    float falloff = 1f - distance / slamShakeRange;
                    CameraShakeController.Instance.ShakeCamera(
                        0.45f, slamShakeMagnitude * falloff, 12f, new Vector3(0.1f, 0.4f, 0.1f));
                }

                if (distance <= slamRadius)
                {
                    // With the boss as the source, so the edge-of-screen smear points at it.
                    PlayerStats stats = PlayerStats.Instance;
                    if (stats != null)
                    {
                        stats.TakeDamage(slamDamage, transform);
                    }
                    else
                    {
                        playerTransform.GetComponentInChildren<IDamageable>()?.TakeDamage(slamDamage);
                    }
                }
            }
        }

        if (!dead && !zombieAI.IsDead()) zombieAI.enabled = true;
        slamInProgress = false;
    }

    // ---- 4. Summoning --------------------------------------------------------

    private IEnumerator SummonLoop()
    {
        if (minionPrefab == null)
        {
            Debug.LogWarning("[BossZombieController] No minion prefab assigned; the boss " +
                             "will not summon.", this);
            yield break;
        }

        while (!dead)
        {
            yield return new WaitForSeconds(summonIntervalSeconds * Random.Range(0.85f, 1.15f));

            if (dead || zombieAI.IsDead() || slamInProgress) continue;

            summoned.RemoveAll(minion => minion == null || minion.IsDead());
            int room = maxSummonedAlive - summoned.Count;
            if (room <= 0) continue;

            PlayVoice(summonBellow, summonBellowVolume, "summon bellow");

            int raising = Mathf.Min(summonCount, room);
            for (int i = 0; i < raising; i++)
            {
                SpawnMinion();
            }
        }
    }

    private void SpawnMinion()
    {
        // On the NavMesh near the boss, so a minion never rises inside a rock and
        // stands there doing nothing.
        Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(summonRadius * 0.5f, summonRadius);
        Vector3 candidate = transform.position + new Vector3(ring.x, 0f, ring.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, summonRadius, NavMesh.AllAreas))
        {
            candidate = hit.position;
        }

        GameObject minion = Instantiate(minionPrefab, candidate, Quaternion.identity);
        ZombieAI minionAI = minion.GetComponent<ZombieAI>();
        if (minionAI != null) summoned.Add(minionAI);
        // Minions register with ZombieManager in their own Start, which also feeds the
        // music intensity - the fight audibly thickens with each bellow.
    }

    // ---- 5. The full stop ----------------------------------------------------

    private void OnBossDeath()
    {
        if (dead) return;
        dead = true;

        StopAllCoroutines();

        // The boss's own summons do not outlive it - the sudden quiet after the death
        // vocal is the point, and a leftover minion chewing on the player would ruin it.
        summoned.RemoveAll(minion => minion == null || minion.IsDead());
        foreach (ZombieAI minion in summoned)
        {
            minion.TakeDamage(float.MaxValue);
        }
        summoned.Clear();

        // The floor lifts; the intensity then relaxes at the driver's own release rate.
        if (musicManager != null) musicManager.ClearIntensityOverride();

        if (rewardItem != null)
        {
            rewardItem.gameObject.SetActive(true);
            // Same 1.2m drop height convention as the player's own drops.
            rewardItem.Drop(transform.position + Vector3.up * 1.2f);
        }

        // ZombieAI's own death path is already playing the death vocal; the dip starts
        // just after it lands so the silence frames it rather than swallowing it.
        if (deathSilenceSnapshot != null)
        {
            StartCoroutine(DeathQuietRoutine());
        }
    }

    private IEnumerator DeathQuietRoutine()
    {
        yield return new WaitForSeconds(1.2f);   // let the death vocal speak first

        AudioEventLog.Record(AudioEventKind.Play, "Boss", "death silence dip");
        mixDip.Dip(deathSilenceSnapshot, normalSnapshot, 0.15f);

        yield return new WaitForSeconds(silenceSeconds);

        mixDip.Restore(silenceReleaseSeconds);
    }

    // ---- Shared --------------------------------------------------------------

    private void PlayVoice(AudioClip clip, float volume, string label)
    {
        if (clip == null || voice == null) return;

        AudioEventLog.Record(AudioEventKind.Play, "Boss", $"{label} '{clip.name}'");
        voice.PlayOneShot(clip, volume);
    }
}
