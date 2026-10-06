using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/*
 * GameTesting.cs
 * 
 * Purpose: Development tool for testing game mechanics and systems.
 * Used by: Development team during testing
 * 
 * Provides shortcuts and utilities for testing various game systems,
 * including inventory, objectives, and player stats. Should be disabled
 * in production builds.
 * 
 * Features:
 * - Quick item addition
 * - Objective completion
 * - Stats modification
 * - Time control
 * 
 * Dependencies:
 * - Inventory system
 * - ObjectiveManager
 * - PlayerStats
 * - DayNightCycle
 */

public class GameTesting : MonoBehaviour
{
    [Header("Test Items")]
    [Tooltip("List of items to add to the player's inventory for testing purposes")]
    [SerializeField] private Item[] testItems;

    [Header("Test Objectives")]
    [Tooltip("List of objective IDs to mark as completed for testing purposes")]
    [SerializeField] private string[] testObjectiveIds;
    
    [Space(10)]
    [Tooltip("Delay between completing each objective (in seconds)")]
    [SerializeField] private float objectiveCompletionDelay = 0.5f;

    [Header("Item Shortcut Settings")]
    [Tooltip("Modifier key required to be held down for adding items (e.g., Left Shift)")]
    [SerializeField] private KeyCode itemModifierKey = KeyCode.LeftShift;
    
    [Tooltip("Key to press while holding the modifier to add items (e.g., I)")]
    [SerializeField] private KeyCode itemTriggerKey = KeyCode.I;

    [Header("Objective Shortcut Settings")]
    [Tooltip("Modifier key required to be held down for completing objectives (e.g., Left Control)")]
    [SerializeField] private KeyCode objectiveModifierKey = KeyCode.LeftControl;

    [Tooltip("Key to press while holding the modifier to complete objectives (e.g., O)")]
    [SerializeField] private KeyCode objectiveTriggerKey = KeyCode.O;

    // The secondary-objective (Shift+P) and XP (Shift+X) shortcuts were removed at the
    // owner's request (2026-09-01), along with the fields, coroutine flag and PlayerStats
    // reference that existed only to serve them.

    private Inventory playerInventory;
    private bool isCompletingObjectives = false;

    [Header("Time Acceleration")]
    [Tooltip("Whether completing objectives should also accelerate time")]
    [SerializeField] private bool accelerateTimeWithObjectives = true;
    
    [Tooltip("How much faster time should pass when accelerated")]
    [SerializeField] private float timeAccelerationMultiplier = 10f;

    // Day skipping is UI-buttons only (SkipToDay2/SkipToDay3 on the pause canvas).
    // The old Shift+D hotkey collided with sprint-strafing right, and its
    // "settings must be open" gate fell through to the skip whenever its root
    // reference was unassigned - which it was.

    [Header("Day 2 Skip Auto-Complete")]
    [Tooltip("Objective IDs to auto-complete when skipping directly to Day 2")]
    [SerializeField] private string[] day2AutoCompleteObjectives = new[] { "PickupFishingRod", "CatchAFish" };

    private DayNightCycle dayNightCycle;
    private bool isTimeAccelerated = false;

    private void Awake()
    {
        // Dev cheat tool - disable outside the editor and development builds.
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            enabled = false;
        }
    }

    private void Start()
    {
        // Find the FirstPersonController and get its Inventory component
        FirstPersonController fpsController = FindFirstObjectByType<FirstPersonController>();
        if (fpsController != null)
        {
            playerInventory = fpsController.GetInventory();
            if (playerInventory == null)
            {
                Debug.LogError("Inventory component not found on FirstPersonController!");
            }
        }
        else
        {
            Debug.LogError("FirstPersonController not found in scene!");
        }

        // Find DayNightCycle
        dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        if (dayNightCycle == null)
        {
            Debug.LogWarning("DayNightCycle not found in scene! Time acceleration will be disabled.");
        }
    }

    private void Update()
    {
        // Check for items shortcut
        if (Input.GetKey(itemModifierKey) && Input.GetKeyDown(itemTriggerKey))
        {
            AddTestItemsToInventory();
        }

        // Check for objectives shortcut
        if (Input.GetKey(objectiveModifierKey) && Input.GetKeyDown(objectiveTriggerKey) && !isCompletingObjectives)
        {
            StartCoroutine(CompleteTestObjectivesSequentially(testObjectiveIds));
        }
    }

    private void AddTestItemsToInventory()
    {
        if (playerInventory == null)
        {
            Debug.LogError("Cannot add items: Player inventory not found!");
            return;
        }

//        Debug.Log("Adding test items to inventory...");
        foreach (var item in testItems)
        {
            if (item != null)
            {
                bool added = playerInventory.AddItem(item);
                if (added)
                {
//                    Debug.Log($"Successfully added {item.GetItemName()} to inventory.");
                }
                else
                {
                    Debug.LogWarning($"Failed to add {item.GetItemName()} to inventory. Inventory might be full.");
                }
            }
            else
            {
                Debug.LogWarning("Null item found in testItems array!");
            }
        }
    }

    private IEnumerator CompleteTestObjectivesSequentially(string[] objectiveIds)
    {
        if (ObjectiveManager.Instance == null)
        {
            Debug.LogError("Cannot complete objectives: ObjectiveManager not found!");
            yield break;
        }

        isCompletingObjectives = true;

        // Accelerate time if enabled
        if (accelerateTimeWithObjectives && dayNightCycle != null && !isTimeAccelerated)
        {
            dayNightCycle.SetTimeSpeedMultiplier(timeAccelerationMultiplier);
            isTimeAccelerated = true;
 //           Debug.Log($"[TimeAcceleration] Time speed increased to {timeAccelerationMultiplier}x");
        }

        foreach (var objectiveId in objectiveIds)
        {
            if (!string.IsNullOrEmpty(objectiveId))
            {
                try
                {
                    ObjectiveManager.Instance.CompleteObjective(objectiveId);

                    // Remove world space marker for the completed objective
                    if (WorldSpaceObjectiveManager.Instance != null)
                    {
                        WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker(objectiveId);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to complete objective {objectiveId}: {e.Message}");
                }

                yield return new WaitForSeconds(objectiveCompletionDelay);
            }
            else
            {
                Debug.LogWarning("Empty objective ID found in testObjectiveIds array!");
            }
        }

        // Reset time acceleration
        if (accelerateTimeWithObjectives && dayNightCycle != null && isTimeAccelerated)
        {
            dayNightCycle.SetTimeSpeedMultiplier(1f);
            isTimeAccelerated = false;
//            Debug.Log("[TimeAcceleration] Time speed reset to normal");
        }

        isCompletingObjectives = false;
    }

    // Optional: Methods to change shortcuts at runtime
    public void SetItemShortcut(KeyCode newModifier, KeyCode newTrigger)
    {
        itemModifierKey = newModifier;
        itemTriggerKey = newTrigger;
        Debug.Log($"Item testing shortcut changed to: {itemModifierKey} + {itemTriggerKey}");
    }

    public void SetObjectiveShortcut(KeyCode newModifier, KeyCode newTrigger)
    {
        objectiveModifierKey = newModifier;
        objectiveTriggerKey = newTrigger;
        Debug.Log($"Objective testing shortcut changed to: {objectiveModifierKey} + {objectiveTriggerKey}");
    }

    // Add method to toggle time acceleration
    public void SetTimeAcceleration(bool enabled)
    {
        accelerateTimeWithObjectives = enabled;
        Debug.Log($"Time acceleration with objectives {(enabled ? "enabled" : "disabled")}");
    }

    // Add method to change time acceleration multiplier
    public void SetTimeAccelerationMultiplier(float multiplier)
    {
        timeAccelerationMultiplier = Mathf.Max(1f, multiplier);
        Debug.Log($"Time acceleration multiplier set to {timeAccelerationMultiplier}x");
    }

    /// <summary>
    /// Public method to skip to Day 2 (can be called from UI buttons)
    /// </summary>
    public void SkipToDay2()
    {
        SkipToDay(2);
    }

    /// <summary>
    /// Public method to skip to Day 3 (can be called from UI buttons)
    /// </summary>
    public void SkipToDay3()
    {
        SkipToDay(3);
    }

    /// <summary>
    /// Skips to the specified day, resetting objectives and wave state
    /// </summary>
    /// <param name="day">The day number to skip to</param>
    private void SkipToDay(int day)
    {
        if (dayNightCycle == null)
        {
            Debug.LogError("Cannot skip day: DayNightCycle not found!");
            return;
        }

        Debug.Log($"[GameTesting] Skipping to Day {day}");

        // Reset time to start of day (dawn) before setting day number
        // This ensures systems initialize correctly for the new day
        dayNightCycle.SetTimeOfDay(dayNightCycle.GetDawnStartTime());
        dayNightCycle.ResumeTime();
        dayNightCycle.SetTimeSpeedMultiplier(1f);

        // Set day in DayNightCycle (this will trigger OnDayChanged event)
        // This will automatically trigger:
        // - WaveManager.UpdateWeekAndNightFromDay() via OnDayChanged
        // - AnimationEventHandler picks up the new day via OnDayChanged
        // - DailyObjectiveManager will detect day change in Update() and reset
        dayNightCycle.SetDayNumber(day);

        // Show the day announcement UI for Day 2 and beyond. Must run after
        // SetDayNumber so the announcement reads the day we just moved to.
        if (day >= 2 && AnimationEventHandler.Instance != null)
        {
            AnimationEventHandler.Instance.ShowDayAnnouncementNow();
        }

        // Force reset objectives immediately (DailyObjectiveManager will also reset in Update, but this ensures it happens)
        if (DailyObjectiveManager.Instance != null)
        {
            DailyObjectiveManager.Instance.ResetDailyObjectives();
        }

        // Reset wave manager state
        WaveManager waveManager = FindFirstObjectByType<WaveManager>();
        if (waveManager != null)
        {
            waveManager.ResetZombieDefenseState();
        }

        // Auto-complete previous days' objectives and first objectives of target day when skipping
        if (day >= 2)
        {
            StartCoroutine(AutoCompleteDaySkipObjectives(day));
        }

        Debug.Log($"[GameTesting] Successfully skipped to Day {day}. Time reset to dawn. All systems initialized.");
    }

    /// <summary>
    /// Auto-completes previous days' objectives and first objectives of target day when skipping
    /// </summary>
    /// <param name="targetDay">The day number we're skipping to</param>
    private IEnumerator AutoCompleteDaySkipObjectives(int targetDay)
    {
        // Wait a frame to ensure DailyObjectiveManager has processed the day change
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        if (DailyObjectiveManager.Instance == null || ObjectiveManager.Instance == null)
        {
            Debug.LogWarning("[GameTesting] Cannot auto-complete objectives: Managers not found");
            yield break;
        }

        // Get DailyObjectiveManager's daily objectives data
        DailyObjectiveManager dailyManager = DailyObjectiveManager.Instance;
        System.Reflection.FieldInfo field = typeof(DailyObjectiveManager).GetField("dailyObjectives", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (field == null)
        {
            Debug.LogWarning("[GameTesting] Could not access dailyObjectives field");
            yield break;
        }

        DailyObjectiveData dailyObjectives = field.GetValue(dailyManager) as DailyObjectiveData;
        
        if (dailyObjectives == null || dailyObjectives.objectives == null)
        {
            Debug.LogWarning("[GameTesting] No daily objectives data found");
            yield break;
        }

        // Get access to DailyObjectiveManager's activeObjectives HashSet
        System.Reflection.FieldInfo activeObjectivesField = typeof(DailyObjectiveManager).GetField("activeObjectives", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        HashSet<string> activeObjectives = null;
        if (activeObjectivesField != null)
        {
            activeObjectives = activeObjectivesField.GetValue(dailyManager) as HashSet<string>;
        }

        // First, auto-complete all Day 1 objectives
        List<DailyObjective> day1Objectives = dailyObjectives.objectives
            .Where(obj => obj.dayNumber == 1)
            .OrderBy(obj => obj.timeToAppear)
            .ToList();

        if (day1Objectives.Count > 0)
        {
            Debug.Log($"[GameTesting] Auto-completing {day1Objectives.Count} Day 1 objectives");
            
            // First pass: Complete any Day 1 objectives that are already active immediately
            foreach (DailyObjective dailyObjective in day1Objectives)
            {
                string objectiveId = dailyObjective.objectiveId;
                if (ObjectiveManager.Instance.HasObjective(objectiveId) && 
                    ObjectiveManager.Instance.IsObjectiveActive(objectiveId))
                {
                    ObjectiveManager.Instance.CompleteObjective(objectiveId);
                    Debug.Log($"[GameTesting] Immediately completed Day 1 objective (was already active): {objectiveId}");
                    if (activeObjectives != null)
                    {
                        activeObjectives.Add(objectiveId);
                    }
                }
            }
            
            yield return new WaitForSeconds(0.1f); // Brief pause for UI updates
            
            // Second pass: Handle any Day 1 objectives that weren't already active
            // Set time to when Day 1 objectives should appear (use the latest one to ensure all appear)
            float latestDay1Time = day1Objectives.Max(obj => obj.timeToAppear);
            if (dayNightCycle != null)
            {
                dayNightCycle.SetTimeOfDay(latestDay1Time);
                yield return new WaitForSeconds(0.1f); // Wait for DailyObjectiveManager to process the time change
            }
            
            // Complete any remaining Day 1 objectives that weren't already active
            yield return StartCoroutine(CompleteObjectivesList(day1Objectives, activeObjectives, "Day 1"));
        }

        // Auto-complete all objectives from previous days (days before target day)
        for (int previousDay = 2; previousDay < targetDay; previousDay++)
        {
            List<DailyObjective> previousDayObjectives = dailyObjectives.objectives
                .Where(obj => obj.dayNumber == previousDay)
                .OrderBy(obj => obj.timeToAppear)
                .ToList();

            if (previousDayObjectives.Count > 0)
            {
                Debug.Log($"[GameTesting] Auto-completing {previousDayObjectives.Count} Day {previousDay} objectives");
                
                // First pass: Complete any objectives that are already active immediately
                foreach (DailyObjective dailyObjective in previousDayObjectives)
                {
                    string objectiveId = dailyObjective.objectiveId;
                    if (ObjectiveManager.Instance.HasObjective(objectiveId) && 
                        ObjectiveManager.Instance.IsObjectiveActive(objectiveId))
                    {
                        ObjectiveManager.Instance.CompleteObjective(objectiveId);
                        Debug.Log($"[GameTesting] Immediately completed Day {previousDay} objective (was already active): {objectiveId}");
                        if (activeObjectives != null)
                        {
                            activeObjectives.Add(objectiveId);
                        }
                    }
                }
                
                yield return new WaitForSeconds(0.1f); // Brief pause for UI updates
                
                // Second pass: Handle any objectives that weren't already active
                float latestPreviousDayTime = previousDayObjectives.Max(obj => obj.timeToAppear);
                if (dayNightCycle != null)
                {
                    dayNightCycle.SetTimeOfDay(latestPreviousDayTime);
                    yield return new WaitForSeconds(0.1f);
                }
                
                // Complete any remaining objectives that weren't already active
                yield return StartCoroutine(CompleteObjectivesList(previousDayObjectives, activeObjectives, $"Day {previousDay}"));
            }
        }

        // Only auto-complete target day objectives for Day 2
        // Day 3+ objectives should not be auto-completed
        if (targetDay == 2)
        {
            // Get Day 2 objectives and sort by timeToAppear
            List<DailyObjective> targetDayObjectives = dailyObjectives.objectives
                .Where(obj => obj.dayNumber == targetDay)
                .OrderBy(obj => obj.timeToAppear)
                .ToList();

            if (targetDayObjectives.Count == 0)
            {
                Debug.LogWarning($"[GameTesting] No Day {targetDay} objectives found");
                yield break;
            }

            // Set time to when the first target day objective should appear so DailyObjectiveManager shows it immediately
            if (targetDayObjectives.Count > 0 && dayNightCycle != null)
            {
                float firstObjectiveTime = targetDayObjectives[0].timeToAppear;
                dayNightCycle.SetTimeOfDay(firstObjectiveTime);
                yield return new WaitForSeconds(0.1f); // Wait for DailyObjectiveManager to process the time change
            }

            // Ensure critical Day 2 objectives are completed when skipping (fishing rod + first fish)
            if (day2AutoCompleteObjectives != null && day2AutoCompleteObjectives.Length > 0)
            {
                yield return StartCoroutine(CompleteSpecificObjectives(day2AutoCompleteObjectives, activeObjectives, $"Day {targetDay} (auto list)"));
            }

            // Complete the first 2 Day 2 objectives
            int objectivesToComplete = Mathf.Min(2, targetDayObjectives.Count);
            List<DailyObjective> firstTargetDayObjectives = targetDayObjectives.Take(objectivesToComplete).ToList();
            
            Debug.Log($"[GameTesting] Auto-completing first {objectivesToComplete} Day {targetDay} objective(s)");
            yield return StartCoroutine(CompleteObjectivesList(firstTargetDayObjectives, activeObjectives, $"Day {targetDay}"));
        }
        else
        {
            // For Day 3+, just set time to dawn so objectives appear naturally
            if (dayNightCycle != null)
            {
                dayNightCycle.SetTimeOfDay(dayNightCycle.GetDawnStartTime());
                yield return new WaitForSeconds(0.1f);
            }
            Debug.Log($"[GameTesting] Skipped to Day {targetDay}. Previous days' objectives completed. Day {targetDay} objectives will appear naturally.");
        }
    }

    /// <summary>
    /// Helper method to complete a list of objectives
    /// </summary>
    private IEnumerator CompleteObjectivesList(List<DailyObjective> objectives, HashSet<string> activeObjectives, string dayLabel)
    {
        foreach (DailyObjective dailyObjective in objectives)
        {
            string objectiveId = dailyObjective.objectiveId;
            
            if (!ObjectiveManager.Instance.HasObjective(objectiveId))
            {
                Debug.LogWarning($"[GameTesting] Objective {objectiveId} not found in ObjectiveManager");
                continue;
            }

            // Skip if already completed
            if (ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                Debug.Log($"[GameTesting] {dayLabel} objective {objectiveId} was already completed");
                continue;
            }

            // If objective is already active, complete it immediately without waiting
            if (ObjectiveManager.Instance.IsObjectiveActive(objectiveId))
            {
                ObjectiveManager.Instance.CompleteObjective(objectiveId);
                Debug.Log($"[GameTesting] Auto-completed {dayLabel} objective (was already active): {objectiveId}");
                yield return new WaitForSeconds(0.05f); // Very short delay for UI update
                continue;
            }

            // Objective is not active yet, so show it and wait for it to become active
            ObjectiveManager.Instance.ShowObjective(objectiveId);
            
            // Mark as active in DailyObjectiveManager to prevent re-adding
            if (activeObjectives != null)
            {
                activeObjectives.Add(objectiveId);
            }
            
            // Wait for objective to become active in ObjectiveManager (with shorter timeout for responsiveness)
            float timeout = 1.5f;
            float elapsed = 0f;
            while (!ObjectiveManager.Instance.IsObjectiveActive(objectiveId) && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            // Complete the objective if it's now active
            if (ObjectiveManager.Instance.IsObjectiveActive(objectiveId))
            {
                ObjectiveManager.Instance.CompleteObjective(objectiveId);
                Debug.Log($"[GameTesting] Auto-completed {dayLabel} objective: {objectiveId}");
            }
            else if (ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                // Already completed (maybe by auto-completion logic)
                Debug.Log($"[GameTesting] {dayLabel} objective {objectiveId} was already completed");
            }
            else
            {
                Debug.LogWarning($"[GameTesting] Objective {objectiveId} did not become active within timeout. IsActive: {ObjectiveManager.Instance.IsObjectiveActive(objectiveId)}, IsCompleted: {ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId)}");
            }

            yield return new WaitForSeconds(0.1f); // Shorter delay between objectives for better responsiveness
        }
    }

    /// <summary>
    /// Completes specific objectives immediately (used for critical day-skip objectives)
    /// </summary>
    private IEnumerator CompleteSpecificObjectives(IEnumerable<string> objectiveIds, HashSet<string> activeObjectives, string label)
    {
        foreach (var objectiveId in objectiveIds)
        {
            if (string.IsNullOrWhiteSpace(objectiveId))
            {
                Debug.LogWarning($"[GameTesting] {label} contains an empty objective id");
                continue;
            }

            if (!ObjectiveManager.Instance.HasObjective(objectiveId))
            {
                Debug.LogWarning($"[GameTesting] Objective {objectiveId} not found in ObjectiveManager");
                continue;
            }

            if (ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                continue;
            }

            ObjectiveManager.Instance.ShowObjective(objectiveId);
            if (activeObjectives != null)
            {
                activeObjectives.Add(objectiveId);
            }

            // Small delay to allow UI/manager to register the objective as active
            yield return new WaitForSeconds(0.05f);

            if (!ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))
            {
                ObjectiveManager.Instance.CompleteObjective(objectiveId);
                if (WorldSpaceObjectiveManager.Instance != null)
                {
                    WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker(objectiveId);
                }
            }

            yield return new WaitForSeconds(0.05f);
        }
    }
} 
