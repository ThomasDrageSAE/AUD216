using UnityEngine;
using System.Collections.Generic;
using System.Collections;

/*
 * DailyObjectiveManager.cs
 * 
 * Purpose: Manages daily objective progression and timing
 * Used by: Game progression, mission system
 * 
 * Key Features:
 * - Time-based objective activation
 * - Day/night cycle integration
 * - Prerequisite checking
 * - Time-critical objectives
 * - Wave system integration
 * 
 * Time Management:
 * - Objective timing thresholds
 * - Day progression tracking
 * - Time pausing system
 * - Dusk transition handling
 * 
 * Dependencies:
 * - DayNightCycle system (also the authority for the current day number)
 * - ObjectiveManager
 * - WaveManager
 * - DailyObjectiveData
 */

public class DailyObjectiveManager : MonoBehaviour
{
    private const float TIME_PAUSE_THRESHOLD = 0.85f;
    private const float LATE_TIME_PAUSE_THRESHOLD = 0.95f;

    public static DailyObjectiveManager Instance { get; private set; }

    [SerializeField] private DailyObjectiveData dailyObjectives;
    [SerializeField] private DayNightCycle dayNightCycle;
    [SerializeField] private WaveManager waveManager;

    private Dictionary<int, List<DailyObjective>> objectivesByDay;
    private HashSet<string> activeObjectives = new HashSet<string>();
    private HashSet<string> m_DayObjectives = new HashSet<string>();
    private HashSet<string> m_NightObjectives = new HashSet<string>();
    private int currentDay = 1;
    private int lastDay = 1;
    private bool m_HasCompletedDayObjectives = false;

    [Header("Time Critical Objectives")]
    [SerializeField] private TimeCriticalObjective[] timeCriticalObjectives;
    private TimeCriticalObjective m_CurrentTimeCriticalObjective;

    /// <summary>
    /// So the diagnostic in Update is one line in the console, not one per frame.
    /// </summary>
    private bool loggedMissingObjectivesByDay;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Single-scene game: deliberately NOT DontDestroyOnLoad. Persisting across
            // a reload of the same scene left this instance alive holding references to
            // destroyed per-scene objects while the fresh copy destroyed itself, which
            // made restarting unplayable. The Instance guard still prevents duplicates.
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Fish counts live in a static dictionary, which survives a scene reload. Without
        // this a restart could inherit progress toward a catch-N-fish objective.
        Fish.ResetAllFishCounts();

        InitializeObjectives();
    }

    private void Start()
    {
        if (dayNightCycle == null)
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();

        if (waveManager == null)
            waveManager = FindFirstObjectByType<WaveManager>();

        // Give a small delay to allow all targets to register
        StartCoroutine(DelayedStart());
    }

    private IEnumerator DelayedStart()
    {
        // Wait for next frame to allow all objects to initialize
        yield return new WaitForEndOfFrame();

        // Every ObjectiveManager.Instance use in this class now guards and returns quietly,
        // which is right on an Update path but costs the loud symptom: with no manager at
        // all, the day would run silently past both objective gates and simply stall near
        // dusk, with nothing in the console to say why. This is that diagnostic, put back
        // once, off the per-frame path, where a coroutine already waits a frame for
        // everything to wake up.
        if (ObjectiveManager.Instance == null)
        {
            Debug.LogError("[DailyObjectiveManager] No ObjectiveManager in the scene. Objectives " +
                           "cannot be shown, completed or gated, and the day will stall near dusk " +
                           "without further errors - the per-frame checks degrade quietly by design.",
                           this);
        }
        
        // Hide all daily objectives at start
        if (ObjectiveManager.Instance != null && dailyObjectives != null && dailyObjectives.objectives != null)
        {
            foreach (var objective in dailyObjectives.objectives)
            {
                if (ObjectiveManager.Instance.HasObjective(objective.objectiveId))
                {
                    ObjectiveManager.Instance.HideObjective(objective.objectiveId);
                }
            }
        }

        InitializeDayNightObjectives();
        UpdateTimeCriticalObjective();
    }

    private void InitializeObjectives()
    {
        objectivesByDay = new Dictionary<int, List<DailyObjective>>();
        
        if (dailyObjectives == null || dailyObjectives.objectives == null)
        {
          //  Debug.LogWarning("No daily objectives data assigned!");
            return;
        }

        // Group objectives by day
        foreach (var objective in dailyObjectives.objectives)
        {
            if (!objectivesByDay.ContainsKey(objective.dayNumber))
            {
                objectivesByDay[objective.dayNumber] = new List<DailyObjective>();
            }
            objectivesByDay[objective.dayNumber].Add(objective);
        }

        // Sort objectives by time for each day
        foreach (var dayObjectives in objectivesByDay.Values)
        {
            dayObjectives.Sort((a, b) => a.timeToAppear.CompareTo(b.timeToAppear));
        }
    }

    private void InitializeDayNightObjectives()
    {
        m_DayObjectives.Clear();
        m_NightObjectives.Clear();

        if (dailyObjectives == null || dailyObjectives.objectives == null) return;

        // ObjectiveManager.Instance is null for the first frames and after a reload
        // (see that class's header). Reached from Update on a day change, so no log:
        // the next day change re-runs this once the manager exists.
        if (ObjectiveManager.Instance == null) return;

        foreach (var objective in dailyObjectives.objectives)
        {
            if (objective.dayNumber == currentDay)
            {
                ObjectiveData objectiveData = ObjectiveManager.Instance.GetObjectiveData(objective.objectiveId);
                if (objectiveData != null)
                {
                    if (objectiveData.isNightObjective)
                    {
                        m_NightObjectives.Add(objective.objectiveId);
                    }
                    else
                    {
                        m_DayObjectives.Add(objective.objectiveId);
                    }
                }
            }
        }
    }

    private void UpdateTimeCriticalObjective()
    {
        m_CurrentTimeCriticalObjective = null;
        if (timeCriticalObjectives != null)
        {
            foreach (var timeCritical in timeCriticalObjectives)
            {
                if (timeCritical.dayNumber == currentDay)
                {
                    m_CurrentTimeCriticalObjective = timeCritical;
                    break;
                }
            }
        }
    }

    private bool AreTimeCriticalObjectivesComplete()
    {
        if (m_CurrentTimeCriticalObjective == null || 
            m_CurrentTimeCriticalObjective.requiredObjectiveIds == null || 
            m_CurrentTimeCriticalObjective.requiredObjectiveIds.Length == 0)
        {
            return true;
        }

        // With no ObjectiveManager there is nothing that can be checked, so give the
        // same answer as the "nothing to check" branch above rather than stalling the
        // clock. This runs every frame via Update -> UpdateTimePause, so no log.
        if (ObjectiveManager.Instance == null) return true;

        foreach (string objectiveId in m_CurrentTimeCriticalObjective.requiredObjectiveIds)
        {
            if (!ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                return false;
            }
        }

        return true;
    }

    private void Update()
    {
        if (dayNightCycle == null) return;

        float currentTime = dayNightCycle.GetTimeOfDay();
        currentDay = dayNightCycle.GetCurrentDay();

        // Check if day has changed
        if (currentDay != lastDay)
        {
            ResetDailyObjectives();
            InitializeDayNightObjectives();
            m_HasCompletedDayObjectives = false;
            lastDay = currentDay;
            dayNightCycle.SetTimeSpeedMultiplier(1f);
            UpdateTimeCriticalObjective();
        }

        UpdateTimePause(currentTime);

        // Check for objective completion and trigger dusk transition
        if (!m_HasCompletedDayObjectives)
        {
            bool completed = AreDayObjectivesCompleted();
            if (completed)
            {
                m_HasCompletedDayObjectives = true;
                TriggerDuskTransition();
            }
        }

        // Handle regular objectives.
        //
        // Guarded because this DOES throw. Measured 2026-09-06: 30+ NullReferenceExceptions
        // per play session from this exact line, objectivesByDay null. It is assigned as the
        // first statement of InitializeObjectives, which is the last statement of Awake - so
        // a null here means Awake did not finish, and the manager is running half-built for
        // the rest of the session.
        //
        // The guard stops the per-frame exception storm; it does not fix the cause, and the
        // log below is what will name it. Note the sibling guards added 2026-09-04 covered
        // ObjectiveManager.Instance, a different null on a different line - this one was
        // missed, and a sweep that greps for one symbol will keep missing it.
        if (objectivesByDay == null)
        {
            if (!loggedMissingObjectivesByDay)
            {
                loggedMissingObjectivesByDay = true;
                Debug.LogError("[DailyObjectiveManager] objectivesByDay is null in Update, so no " +
                               "regular objective can ever appear. It is assigned at the end of " +
                               "Awake, so Awake did not complete - check what threw before " +
                               "InitializeObjectives on this object.", this);
            }
            return;
        }

        if (objectivesByDay.TryGetValue(currentDay, out var regularObjectives))
        {
            foreach (var objective in regularObjectives)
            {
                if (!activeObjectives.Contains(objective.objectiveId) && 
                    currentTime >= objective.timeToAppear &&
                    objective.objectiveId != ObjectiveConstants.ZOMBIE_DEFENCE)
                {
                    AddObjective(objective);
                }
            }
        }

        // Note: the zombie defense objective is activated by WaveManager, not here.
    }

    /// <summary>
    /// Decides in one place whether the clock should be running. Three independent
    /// pause/resume writers used to run every frame here, and whichever executed last
    /// won the argument. This manager is the only system that pauses day/night time
    /// (the pause menu uses Time.timeScale), so a single verdict is authoritative and
    /// also self-heals a pause left set by something else.
    /// </summary>
    private void UpdateTimePause(float currentTime)
    {
        bool dayObjectivesDone = AreAllDayObjectivesCompleted();

        // The late gate holds the night open until the wave defence is cleared;
        // WaveManager resumes time and jumps to dawn when the last zombie dies.
        // With no ObjectiveManager present, hold rather than skip the night.
        bool zombieDefenceCleared = ObjectiveManager.Instance != null &&
            ObjectiveManager.Instance.IsObjectiveCompleted(ObjectiveConstants.ZOMBIE_DEFENCE);

        bool shouldPause =
            (m_CurrentTimeCriticalObjective != null &&
             currentTime >= m_CurrentTimeCriticalObjective.pauseTimeOfDay &&
             !AreTimeCriticalObjectivesComplete())
            || (currentTime >= TIME_PAUSE_THRESHOLD && !dayObjectivesDone)
            || (currentTime >= LATE_TIME_PAUSE_THRESHOLD && !zombieDefenceCleared);

        if (shouldPause)
        {
            dayNightCycle.PauseTime();
        }
        else
        {
            dayNightCycle.ResumeTime();
        }

        // Once the day's objectives are cleared, the evening runs at normal speed.
        if (currentTime >= TIME_PAUSE_THRESHOLD && dayObjectivesDone)
        {
            dayNightCycle.SetTimeSpeedMultiplier(1f);
        }
    }

    private void AddObjective(DailyObjective objective)
    {
        if (ObjectiveManager.Instance != null)
        {
            ObjectiveData objectiveData = ObjectiveManager.Instance.GetObjectiveData(objective.objectiveId);
            if (objectiveData != null && !ArePrerequisitesMet(objectiveData))
            {
                return;
            }

            Fish.ResetObjectiveFishCount(objective.objectiveId);
            ObjectiveManager.Instance.ShowObjective(objective.objectiveId);
            activeObjectives.Add(objective.objectiveId);
        }
    }

    private bool ArePrerequisitesMet(ObjectiveData objectiveData)
    {
        if (objectiveData.prerequisites == null || objectiveData.prerequisites.Length == 0)
        {
            return true;
        }

        // The only caller (AddObjective) already null-checks Instance; guarded here too
        // so the helper is safe on its own. Same "nothing to check" answer as above.
        if (ObjectiveManager.Instance == null) return true;

        foreach (ObjectiveData prerequisite in objectiveData.prerequisites)
        {
            if (prerequisite == null) continue;
            
            if (!ObjectiveManager.Instance.IsObjectiveCompleted(prerequisite.objectiveId))
            {
                return false;
            }
        }

        return true;
    }

    public bool AreAllDayObjectivesCompleted()
    {
        if (m_DayObjectives.Count == 0) return true;

        // No manager to ask -> same answer as "nothing to check". Update path, no log.
        if (ObjectiveManager.Instance == null) return true;

        foreach (string objectiveId in m_DayObjectives)
        {
            if (!ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                return false;
            }
        }
        return true;
    }

    private bool AreDayObjectivesCompleted()
    {
        if (m_DayObjectives.Count == 0) return false;

        // No manager to ask -> same answer as "nothing to check", which also keeps the
        // one-shot dusk transition from firing on missing data. Update path, no log.
        if (ObjectiveManager.Instance == null) return false;

        foreach (string objectiveId in m_DayObjectives)
        {
            if (!ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                return false;
            }
        }
        
        return true;
    }

    private void TriggerDuskTransition()
    {
        if (dayNightCycle != null)
        {
            float currentTime = dayNightCycle.GetTimeOfDay();
            float duskStartTime = dayNightCycle.GetDuskStartTime();
            
            if (duskStartTime > currentTime)
            {
                dayNightCycle.SetTimeOfDay(duskStartTime);
            }
            else
            {
                dayNightCycle.ResumeTime();
                dayNightCycle.SetTimeSpeedMultiplier(1f);
            }
        }
        else
        {
            Debug.LogWarning("DayNightCycle reference is missing!");
        }
    }

    public void ResetDailyObjectives()
    {
        activeObjectives.Clear();
        m_HasCompletedDayObjectives = false;
        Fish.ResetAllFishCounts();

        // Ensure zombie defense objective is properly reset
        if (ObjectiveManager.Instance != null)
        {
            // Only hide and reset if it was active
            if (ObjectiveManager.Instance.IsObjectiveActive(ObjectiveConstants.ZOMBIE_DEFENCE))
            {
                ObjectiveManager.Instance.HideObjective(ObjectiveConstants.ZOMBIE_DEFENCE);
                activeObjectives.Remove(ObjectiveConstants.ZOMBIE_DEFENCE);
             //   Debug.Log("[ZombieDefense] Objective reset for new day");
            }
            
            // Always reset WaveManager state
            if (waveManager != null)
            {
                waveManager.ResetZombieDefenseState();
            }
        }
    }

}