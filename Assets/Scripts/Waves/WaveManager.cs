using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/*
 * WaveManager.cs
 * 
 * Purpose: Controls zombie wave spawning and management during night cycles
 * Used by: Game progression system, zombie spawning, night cycle events
 * 
 * Key Features:
 * - Week/Night/Wave hierarchy system
 * - Time-based wave spawning
 * - Dynamic zombie difficulty scaling
 * - Objective system integration
 * - UI status updates
 * - Night cycle synchronization
 * 
 * Wave System Structure:
 * - Weeks contain multiple nights
 * - Nights contain multiple waves
 * - Waves control:
 *   - Zombie count and types
 *   - Spawn timing and locations
 *   - Difficulty multipliers
 * 
 * Performance Considerations:
 * - Coroutine-based wave management
 * - Efficient zombie spawning
 * - Smart state tracking
 * - Event-driven updates
 * 
 * Dependencies:
 * - DayNightCycle system
 * - ObjectiveManager
 * - UIAudioManager
 * - AnimationEventHandler
 * - Requires configured wave data
 * - TextMeshPro for UI elements
 * 
 * Debug Features:
 * - Detailed logging
 * - State monitoring
 * - Wave progression tracking
 * - Zombie count verification
 */

public class WaveManager : MonoBehaviour
{
    /// <summary>
    /// Represents a single wave of zombies with specific properties and behaviors
    /// </summary>
    [System.Serializable]
    public class Wave
    {
        [Tooltip("Name of this wave for organization")]
        public string waveName = "Wave";
        
        [Tooltip("Total number of zombies that will spawn in this wave")]
        public int numberOfZombies = 5;
        
        [Tooltip("Time between individual zombie spawns within the wave")]
        public float spawnDelay = 2f;
        
        [Tooltip("Delay from night start until this wave begins (in seconds)")]
        public float timeToStart = 0f;
        
        [Tooltip("Array of different zombie types that can spawn in this wave")]
        public GameObject[] zombiePrefabs;
        
        [Tooltip("Modifies zombie movement speed (1 = normal, 2 = double speed)")]
        public float zombieSpeedMultiplier = 1f;
        
        [Tooltip("Modifies zombie health (1 = normal, 2 = double health)")]
        public float healthMultiplier = 1f;
        
        [Tooltip("Possible locations where zombies can spawn")]
        public Transform[] waveSpawnPoints;
        
        [Header("Wave Messages")]
        [Tooltip("Optional message to display when this wave starts spawning. Leave empty for no message.")]
        public string waveStartMessage = "";
        
        [Tooltip("If true, message shows before enemies spawn. If false, message shows when first enemy spawns.")]
        public bool showStartMessageBeforeSpawn = false;
        
        [Tooltip("Time offset (in seconds) before showing wave start message. If showStartMessageBeforeSpawn is true, this is the delay before spawning starts. If false, this is offset from when first zombie spawns.")]
        public float waveStartMessageOffset = 0f;
        
        [Tooltip("Optional message to display when all zombies in this wave are killed. Leave empty for no message.")]
        public string waveCompleteMessage = "";
        
        [Tooltip("Time offset (in seconds) before showing wave complete message. Negative values show message earlier, positive values show message later. Message shows when wave completes + this offset.")]
        public float waveCompleteMessageOffset = 0f;
        
        [Header("Wave Timing")]
        [Tooltip("If true, this wave will only start after the previous wave has completed. If false, wave starts based on timeToStart.")]
        public bool waitForPreviousWave = false;
    }

    /// <summary>
    /// Represents a single night of waves with progressive difficulty
    /// </summary>
    [System.Serializable]
    public class Night
    {
        [Tooltip("Identifier for this night")]
        public string nightName = "Night";
        
        [Tooltip("Sequential waves that occur during this night")]
        public Wave[] waves;
        
        [Tooltip("Editor-only: Controls expansion in inspector")]
        public bool isExpanded;
    }

    /// <summary>
    /// Represents a week of nights with escalating challenges
    /// </summary>
    [System.Serializable]
    public class Week
    {
        [Tooltip("Identifier for this week")]
        public string weekName = "Week";
        
        [Tooltip("Five nights of progressive difficulty")]
        public Night[] nights = new Night[5];
        
        [Tooltip("Editor-only: Controls expansion in inspector")]
        public bool isExpanded;
    }

    [Header("Wave Settings")]
    [Tooltip("Weeks of waves configuration")]
    [SerializeField] private Week[] weeks;

    [Header("UI Elements")]
    [Tooltip("Text displaying current week number")]
    [SerializeField] private TextMeshProUGUI weekText;
    [Tooltip("Text displaying current night number")]
    [SerializeField] private TextMeshProUGUI nightText;
    [Tooltip("Text displaying current wave number")]
    [SerializeField] private TextMeshProUGUI waveText;
    [Tooltip("Text displaying number of zombies remaining")]
    [SerializeField] private TextMeshProUGUI zombieCountText;

    [Header("Day/Night Cycle")]
    [Tooltip("Reference to the DayNightCycle component")]
    [SerializeField] private DayNightCycle dayNightCycle;

    [Header("Night End Settings")]
    [Tooltip("Time of day to set when zombies are cleared (0.1 = early dawn)")]
    [SerializeField, Range(0f, 1f)] private float zombieDefenseTimeTillDawn = 0.1f;

    private int currentWeekIndex = 0;
    private int currentNightIndex = 0;
    private int currentWaveIndex = -1;
    private int zombiesAlive = 0;
    private int zombiesSpawned = 0;
    // Zombies still alive per wave index. Waves overlap (a time-based wave starts as
    // soon as the previous one finishes *spawning*), so a single "current wave"
    // counter attributed deaths to whichever wave happened to be spawning.
    private readonly Dictionary<int, int> aliveByWave = new Dictionary<int, int>();
    private readonly HashSet<int> finishedSpawningWaves = new HashSet<int>();
    // Bumped whenever a night starts or is torn down, so death events from zombies
    // belonging to an earlier night cannot decrement this night's counters.
    private int nightGeneration = 0;
    private bool isWaveActive = false;
    private Wave currentWave;
    private bool isNightStarted = false;
    private float nightTimer = 0f;
    private List<int> pendingWaves = new List<int>();
    private HashSet<int> completedWaves = new HashSet<int>(); // Track which waves have completed
    private Coroutine waveManagementCoroutine;
    private Coroutine currentWaveSpawningCoroutine;
    private Coroutine monitorCoroutine;
    private bool hasZombieDefenseObjective = false;
    private int totalZombiesForNight = 0; // Track total zombies across all waves
    private const float ZOMBIE_SPAWN_START_TIME = 0.868f;

    /// <summary>
    /// Initializes wave system and subscribes to necessary events
    /// </summary>
    private void Start()
    {
        if (dayNightCycle == null)
        {
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();
            if (dayNightCycle == null)
            {
                //Debug.LogError("No DayNightCycle found in the scene!");
                return;
            }
        }

        // Subscribe to day changes
        dayNightCycle.OnDayChanged += HandleDayChanged;

        // Initialize week/night based on current day
        UpdateWeekAndNightFromDay(dayNightCycle.GetCurrentDay());

        if (weeks == null || weeks.Length == 0)
        {
           // Debug.LogError("No weeks configured in WaveManager!");
            return;
        }

        StartMonitoring();
    }

    private void UpdateWeekAndNightFromDay(int day)
    {
        // Convert from 1-based day to 0-based index
        int dayIndex = day - 1;
        
        // Calculate week and night indices
        currentWeekIndex = dayIndex / 5;  // Integer division for week number
        currentNightIndex = dayIndex % 5;  // Remainder for night within week
        
       // Debug.Log($"[WaveManager] Day {day} converted to: Week {currentWeekIndex + 1}, Night {currentNightIndex + 1}");
      //  Debug.Log($"[WaveManager] Raw values - dayIndex: {dayIndex}, weekIndex: {currentWeekIndex}, nightIndex: {currentNightIndex}");
        
        // Validate indices
        if (currentWeekIndex >= weeks.Length)
        {
            Debug.LogError($"[WaveManager] Week index {currentWeekIndex} is out of range! Max weeks: {weeks.Length}");
        }
        else if (currentNightIndex >= weeks[currentWeekIndex].nights.Length)
        {
            Debug.LogError($"[WaveManager] Night index {currentNightIndex} is out of range! Max nights in week {currentWeekIndex + 1}: {weeks[currentWeekIndex].nights.Length}");
        }
        
        UpdateUI();
    }

    private void HandleDayChanged(int newDay)
    {
       // Debug.Log($"[WaveManager] Day changed to: {newDay}");
        UpdateWeekAndNightFromDay(newDay);
    }

    private void OnDestroy()
    {
        if (dayNightCycle != null)
        {
            dayNightCycle.OnDayChanged -= HandleDayChanged;
        }
    }

    private void OnDisable()
    {
        if (monitorCoroutine != null)
        {
            StopCoroutine(monitorCoroutine);
            monitorCoroutine = null;
        }
    }

    private void StartMonitoring()
    {
        if (monitorCoroutine == null)
        {
            monitorCoroutine = StartCoroutine(MonitorDayNightCycle());
        }
    }

    /// <summary>
    /// Monitors day/night cycle to trigger wave events at appropriate times
    /// Manages zombie defense objective states
    /// </summary>
    private IEnumerator MonitorDayNightCycle()
    {
        yield return new WaitForSeconds(1f);
        
        while (true)
        {
            if (dayNightCycle == null)
            {
                Debug.LogError("DayNightCycle reference lost!");
                yield break;
            }

            float currentTime = dayNightCycle.GetTimeOfDay();
            
            if (currentTime >= ZOMBIE_SPAWN_START_TIME && !isNightStarted)
            {
                isNightStarted = true;
                StartNight();  // Initialize waves first

                // Only add the objective if waves were successfully initialized
                if (zombiesAlive > 0 && pendingWaves.Count > 0)
                {
                    if (ObjectiveManager.Instance != null)
                    {
                        ObjectiveManager.Instance.AddObjective(ObjectiveConstants.ZOMBIE_DEFENCE);
                        hasZombieDefenseObjective = true;
//                        Debug.Log($"[ZombieDefense] Added objective for night. Zombies: {zombiesAlive}");
                    }
                    
                    // Show first wave start message immediately when objective is posted if configured
                    // This reduces the delay between objective appearing and message showing
                    Night currentNight = GetCurrentNight();
                    if (currentNight != null && currentNight.waves != null && currentNight.waves.Length > 0)
                    {
                        Wave firstWave = currentNight.waves[0];
                        if (!string.IsNullOrEmpty(firstWave.waveStartMessage) &&
                            firstWave.showStartMessageBeforeSpawn &&
                            AnimationEventHandler.Instance != null)
                        {
                            AnimationEventHandler.Instance.ShowCustomAnnouncement(firstWave.waveStartMessage);
                        }
                    }
                }
            }
            else if (currentTime < ZOMBIE_SPAWN_START_TIME && isNightStarted)
            {
                isNightStarted = false;
                EndNight();
            }
            
            yield return new WaitForSeconds(0.5f);
        }
    }

    /// <summary>
    /// Begins a night of waves, calculating total zombies and initializing wave tracking
    /// </summary>
    private void StartNight()
    {
        Night currentNight = GetCurrentNight();
        if (currentNight == null || currentNight.waves == null || currentNight.waves.Length == 0)
        {
            return;
        }

        totalZombiesForNight = 0;
        foreach (Wave wave in currentNight.waves)
        {
            totalZombiesForNight += wave.numberOfZombies;
        }
        zombiesAlive = totalZombiesForNight;

        nightGeneration++;
        nightTimer = 0f;
        pendingWaves.Clear();
        completedWaves.Clear(); // Reset completed waves for new night
        aliveByWave.Clear();
        finishedSpawningWaves.Clear();

        for (int i = 0; i < currentNight.waves.Length; i++)
        {
            pendingWaves.Add(i);
        }

        UpdateUI();
        waveManagementCoroutine = StartCoroutine(ManageNightWaves());
    }

    /// <summary>
    /// Resolves the configured night for the current week/night indices, or null if
    /// either index is out of range.
    /// </summary>
    private Night GetCurrentNight()
    {
        if (weeks == null || currentWeekIndex < 0 || currentWeekIndex >= weeks.Length)
        {
            return null;
        }

        Week currentWeek = weeks[currentWeekIndex];
        if (currentWeek == null || currentWeek.nights == null ||
            currentNightIndex < 0 || currentNightIndex >= currentWeek.nights.Length)
        {
            return null;
        }

        return currentWeek.nights[currentNightIndex];
    }

    private void StartWave(Wave wave, int waveIndex)
    {
        currentWaveIndex = waveIndex;
        currentWave = wave;
        zombiesSpawned = 0;
        aliveByWave[waveIndex] = wave.numberOfZombies;
        UpdateUI();

        // Wave start message is now shown earlier in ManageNightWaves() when wave is determined to start
        // This gives the animation more time to become visible before spawning begins

        currentWaveSpawningCoroutine = StartCoroutine(SpawnWaveZombies(wave, waveIndex, nightGeneration));
    }

    /// <summary>
    /// Manages the progression of waves throughout the night
    /// Controls wave timing and sequencing
    /// </summary>
    private IEnumerator ManageNightWaves()
    {
        Night currentNight = GetCurrentNight();
        if (currentNight == null || currentNight.waves == null)
        {
            yield break;
        }

        while (isNightStarted && pendingWaves.Count > 0)
        {
            nightTimer += Time.deltaTime;

            // Only check for new waves if we're not currently spawning
            if (!isWaveActive)
            {
                for (int i = pendingWaves.Count - 1; i >= 0; i--)
                {
                    int waveIndex = pendingWaves[i];
                    Wave wave = currentNight.waves[waveIndex];

                    bool canStartWave = false;

                    if (wave.waitForPreviousWave)
                    {
                        // If waiting for previous wave, check if previous wave is completed
                        if (waveIndex == 0)
                        {
                            // First wave can't wait for previous, so use time-based start
                            canStartWave = nightTimer >= wave.timeToStart;
                        }
                        else
                        {
                            // Check if previous wave (waveIndex - 1) is completed
                            canStartWave = completedWaves.Contains(waveIndex - 1);
                        }
                    }
                    else
                    {
                        // Use time-based start (original behavior)
                        canStartWave = nightTimer >= wave.timeToStart;
                    }

                    if (canStartWave)
                    {
                        // Show wave start message early if configured (before wave actually starts)
                        // Skip for first wave if it was already shown when objective was posted (within first 0.1s of night)
                        bool shouldShowMessage = !string.IsNullOrEmpty(wave.waveStartMessage) && 
                                                wave.showStartMessageBeforeSpawn &&
                                                !(waveIndex == 0 && nightTimer < 0.1f); // First wave message already shown
                        
                        if (shouldShowMessage)
                        {
                            if (AnimationEventHandler.Instance != null)
                            {
                                AnimationEventHandler.Instance.ShowCustomAnnouncement(wave.waveStartMessage);
                            }
                        }
                        
                        StartWave(wave, waveIndex);
                        pendingWaves.RemoveAt(i);
                        break; // Only start one wave at a time
                    }
                }
            }

            yield return null;
        }
    }

    private void EndNight()
    {
        // Only reset hasZombieDefenseObjective if the objective was completed
        if (ObjectiveManager.Instance != null &&
            ObjectiveManager.Instance.IsObjectiveCompleted(ObjectiveConstants.ZOMBIE_DEFENCE))
        {
            hasZombieDefenseObjective = false;
        }

        ResetNightState();
        UpdateUI();
    }

    /// <summary>
    /// Tears the night down unconditionally. This used to bail out while zombies were
    /// still alive, which left the spawn coroutines running into the next day and let
    /// leftover zombies decrement the following night's counters.
    /// </summary>
    private void ResetNightState()
    {
        if (currentWaveSpawningCoroutine != null)
        {
            StopCoroutine(currentWaveSpawningCoroutine);
            currentWaveSpawningCoroutine = null;
        }

        if (waveManagementCoroutine != null)
        {
            StopCoroutine(waveManagementCoroutine);
            waveManagementCoroutine = null;
        }

        // Anything still alive belongs to a night that is over.
        nightGeneration++;

        // Clear wave state
        currentWaveIndex = -1;
        currentWave = null;
        isWaveActive = false;
        zombiesAlive = 0;
        zombiesSpawned = 0;
        pendingWaves.Clear();
        completedWaves.Clear();
        aliveByWave.Clear();
        finishedSpawningWaves.Clear();
        nightTimer = 0f;
    }

    /// <summary>
    /// Handles individual zombie spawning with configured modifiers
    /// Applies wave-specific settings to each zombie
    /// </summary>
    /// <returns>True if a zombie that can actually die was spawned.</returns>
    private bool SpawnZombie(Wave wave, int waveIndex, int generation)
    {
        if (wave == null)
        {
            return false;
        }

        if (wave.waveSpawnPoints == null || wave.waveSpawnPoints.Length == 0)
        {
            Debug.LogError($"[WaveManager] Wave '{wave.waveName}' has no spawn points assigned!", this);
            return false;
        }

        if (wave.zombiePrefabs == null || wave.zombiePrefabs.Length == 0)
        {
            Debug.LogError($"[WaveManager] Wave '{wave.waveName}' has no zombie prefabs assigned!", this);
            return false;
        }

        Transform spawnPoint = wave.waveSpawnPoints[Random.Range(0, wave.waveSpawnPoints.Length)];
        GameObject zombiePrefab = wave.zombiePrefabs[Random.Range(0, wave.zombiePrefabs.Length)];
        if (spawnPoint == null || zombiePrefab == null)
        {
            Debug.LogError($"[WaveManager] Wave '{wave.waveName}' has an empty spawn point or prefab slot!", this);
            return false;
        }

        // Snap to the baked NavMesh: a spawn point that has drifted off it produces a
        // frozen zombie that never reaches the player and errors every frame.
        Vector3 spawnPosition = spawnPoint.position;
        if (UnityEngine.AI.NavMesh.SamplePosition(spawnPosition, out UnityEngine.AI.NavMeshHit navHit,
                                                  4f, UnityEngine.AI.NavMesh.AllAreas))
        {
            spawnPosition = navHit.position;
        }
        else
        {
            Debug.LogWarning($"[WaveManager] Spawn point '{spawnPoint.name}' is not on the " +
                             "NavMesh; skipping this spawn.", spawnPoint);
            return false;
        }

        GameObject zombie = Instantiate(zombiePrefab, spawnPosition, spawnPoint.rotation);
        zombie.tag = "Enemy";

        ZombieAI zombieAI = zombie.GetComponent<ZombieAI>();
        if (zombieAI == null)
        {
            // Without a ZombieAI it can never report a death, so leaving it alive would
            // stall the night forever.
            Debug.LogError($"[WaveManager] Zombie prefab '{zombiePrefab.name}' is missing ZombieAI; discarding this spawn.", this);
            Destroy(zombie);
            return false;
        }

        zombieAI.SetSpeedMultiplier(wave.zombieSpeedMultiplier);
        zombieAI.SetHealthMultiplier(wave.healthMultiplier);
        zombieAI.OnZombieDeath += () => HandleZombieRemoved(wave, waveIndex, generation);
        return true;
    }

    /// <summary>
    /// Manages the spawning sequence for a specific wave
    /// Controls timing and progression of zombie spawns
    /// </summary>
    private IEnumerator SpawnWaveZombies(Wave wave, int waveIndex, int generation)
    {
        isWaveActive = true;
        int zombiesToSpawn = wave.numberOfZombies;

        // If message should show before spawn, wait for animation visibility + offset before spawning
        if (!string.IsNullOrEmpty(wave.waveStartMessage) && wave.showStartMessageBeforeSpawn)
        {
            // Wait for animation to become visible (minimum delay to ensure message is seen)
            // This compensates for the animation delay - adjust if animation timing changes
            const float animationVisibilityDelay = 1.0f;
            yield return new WaitForSeconds(animationVisibilityDelay);

            // Wait for additional offset before starting to spawn
            if (wave.waveStartMessageOffset > 0f)
            {
                yield return new WaitForSeconds(wave.waveStartMessageOffset);
            }
        }

        for (int i = 0; i < zombiesToSpawn; i++)
        {
            bool spawned = SpawnZombie(wave, waveIndex, generation);
            zombiesSpawned++;
            UpdateUI();

            if (!spawned)
            {
                // Count a failed spawn as already dead, otherwise the night's zombie
                // total can never reach zero and the objective never completes.
                HandleZombieRemoved(wave, waveIndex, generation);
            }

            // Trigger wave start message when first zombie spawns (if not shown before spawn)
            if (i == 0 && !string.IsNullOrEmpty(wave.waveStartMessage) && !wave.showStartMessageBeforeSpawn)
            {
                StartCoroutine(ShowWaveStartMessageWithOffset(wave));
            }

            if (i < zombiesToSpawn - 1)  // Don't wait after spawning the last zombie
            {
                yield return new WaitForSeconds(wave.spawnDelay);
            }
        }

        isWaveActive = false;
        finishedSpawningWaves.Add(waveIndex);

        // A wave whose zombies all died while it was still spawning (or one configured
        // with no zombies at all) completes here.
        CheckWaveCompleted(wave, waveIndex);
        CheckNightCompleted();
    }

    /// <summary>
    /// Shows wave start message with configured time offset
    /// </summary>
    /// <param name="applyOffset">If false, shows message immediately (offset already applied in calling code)</param>
    private IEnumerator ShowWaveStartMessageWithOffset(Wave wave)
    {
        if (wave.waveStartMessageOffset != 0f)
        {
            yield return new WaitForSeconds(wave.waveStartMessageOffset);
        }

        if (AnimationEventHandler.Instance != null && !string.IsNullOrEmpty(wave.waveStartMessage))
        {
            AnimationEventHandler.Instance.ShowCustomAnnouncement(wave.waveStartMessage);
        }
    }

    /// <summary>
    /// Shows wave complete message with configured time offset. Takes the wave it
    /// belongs to: by the time the offset elapses another wave may be current.
    /// </summary>
    private IEnumerator ShowWaveCompleteMessageWithOffset(Wave wave)
    {
        if (wave.waveCompleteMessageOffset != 0f)
        {
            yield return new WaitForSeconds(wave.waveCompleteMessageOffset);
        }

        if (AnimationEventHandler.Instance != null && !string.IsNullOrEmpty(wave.waveCompleteMessage))
        {
            AnimationEventHandler.Instance.ShowCustomAnnouncement(wave.waveCompleteMessage);
        }
    }

    /// <summary>
    /// Processes zombie death events and checks for wave/night completion
    /// Updates UI and manages objective completion
    /// </summary>
    private void HandleZombieRemoved(Wave wave, int waveIndex, int generation)
    {
        // Zombies left over from a night that already ended must not decrement this
        // night's counters, or the objective completes early.
        if (generation != nightGeneration) return;

        zombiesAlive = Mathf.Max(0, zombiesAlive - 1);

        if (aliveByWave.TryGetValue(waveIndex, out int remainingInWave))
        {
            aliveByWave[waveIndex] = Mathf.Max(0, remainingInWave - 1);
        }

        UpdateUI();

        CheckWaveCompleted(wave, waveIndex);
        CheckNightCompleted();
    }

    /// <summary>
    /// Marks a wave complete once it has finished spawning and its own zombies are all
    /// dead. Deaths are attributed per wave, so overlapping waves can no longer close
    /// each other out.
    /// </summary>
    private void CheckWaveCompleted(Wave wave, int waveIndex)
    {
        if (waveIndex < 0 || completedWaves.Contains(waveIndex)) return;
        if (!finishedSpawningWaves.Contains(waveIndex)) return;
        if (aliveByWave.TryGetValue(waveIndex, out int remaining) && remaining > 0) return;

        completedWaves.Add(waveIndex);

        // Trigger wave complete message if set (with offset)
        if (wave != null && !string.IsNullOrEmpty(wave.waveCompleteMessage))
        {
            StartCoroutine(ShowWaveCompleteMessageWithOffset(wave));
        }
    }

    /// <summary>
    /// Completes the zombie defence objective and rolls the clock to dawn once every
    /// wave has spawned and every zombie is dead.
    /// </summary>
    private void CheckNightCompleted()
    {
        if (!hasZombieDefenseObjective) return;
        if (zombiesAlive > 0 || pendingWaves.Count > 0 || isWaveActive) return;

        if (ObjectiveManager.Instance == null ||
            ObjectiveManager.Instance.IsObjectiveCompleted(ObjectiveConstants.ZOMBIE_DEFENCE))
        {
            return;
        }

        AnimationEventHandler.Instance?.ShowEnemiesClearedAnnouncement();
        ObjectiveManager.Instance.CompleteObjective(ObjectiveConstants.ZOMBIE_DEFENCE);
        hasZombieDefenseObjective = false;

        // Resume time when enemies are cleared
        if (dayNightCycle != null)
        {
            dayNightCycle.ResumeTime();
            dayNightCycle.SetTimeSpeedMultiplier(1f);
            dayNightCycle.SetTimeOfDay(zombieDefenseTimeTillDawn);
        }
    }

    private void UpdateUI()
    {
        if (weekText != null)
            weekText.text = $"Week {currentWeekIndex + 1}";
        
        if (nightText != null)
            nightText.text = $"Night {currentNightIndex + 1}";
        
        if (waveText != null)
            waveText.text = $"Wave {currentWaveIndex + 1}";
        
        if (zombieCountText != null)
            zombieCountText.text = $"Zombies Remaining: {zombiesAlive} of {totalZombiesForNight}";
    }

    private void OnEnable()
    {
        StartMonitoring();
    }

    // Add these public methods to expose debug information
    public int GetTotalZombiesForNight() => totalZombiesForNight;
    public int GetZombiesAlive() => zombiesAlive;
    public int GetPendingWavesCount() => pendingWaves.Count;
    public bool IsWaveActive() => isWaveActive;
    public int GetCurrentWaveIndex() => currentWaveIndex;
    public bool IsNightStarted() => isNightStarted;
    public bool HasZombieDefenseObjective() => hasZombieDefenseObjective;

    // Add this method to be called by the animation event
    public void PlayClearedSound()
    {
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayAllEnemiesCleared();
        }
    }

    public void ResetZombieDefenseState()
    {
        hasZombieDefenseObjective = false;
        isNightStarted = false;
        ResetNightState();
        UpdateUI();
    }
}