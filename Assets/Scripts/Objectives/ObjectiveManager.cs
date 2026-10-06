using UnityEngine;
using System.Collections.Generic;

/*
 * ObjectiveManager.cs
 * 
 * Purpose: Core manager for all objective functionality
 * Used by: Mission system, game progression
 * 
 * Key Features:
 * - Objective lifecycle management
 * - UI element creation/cleanup
 * - Completion tracking
 * - Prerequisite validation
 * - World marker integration
 * 
 * System Features:
 * - Dynamic objective creation
 * - Progress tracking
 * - Event notifications
 * - Auto-completion logic
 * - UI synchronization
 * 
 * Dependencies:
 * - ObjectiveUI2 prefab
 * - ObjectiveData system
 * - WorldSpaceObjectiveManager
 * - UIAudioManager
 * - FirstTimeInteractionTracker
 *
 * Instance lifetime - the rule every caller follows:
 * Instance is assigned in Awake, so it is null for the first frames of a session, for
 * the whole of any other component's Awake that runs before this one, and again after a
 * scene reload - this is a single-scene game with no DontDestroyOnLoad, so between the
 * old manager being destroyed and the new one waking the static points at a destroyed
 * object, which Unity reports as null. There is no "wait until ready" hook and there is
 * no script execution order that fixes it for every caller.
 *
 * Therefore every caller null-checks Instance at the point where it dereferences it -
 * not once at Start, not in a cached field. The shape to copy is
 * DailyObjectiveManager.UpdateTimePause (:272) and WaveManager.EndNight (:471):
 *
 *     if (ObjectiveManager.Instance != null && ObjectiveManager.Instance.Something(...))
 *
 * On an Update / LateUpdate / FixedUpdate / coroutine path, guard and return silently -
 * a per-frame log would drown the console. On an event-driven path (an interaction, a
 * pickup, a trigger, an objective completion) guard, return, and Debug.LogWarning once
 * naming the site: those are rare, and a lost completion is worth a console line.
 * ObjectiveManager.Instance?.Foo() is the same guard in short form and is fine where
 * there is nothing to report.
 */

public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private List<ObjectiveData> allObjectives;
    [SerializeField] private ObjectiveUI2 objectiveUIPrefab;
    [SerializeField] private Transform objectivesContainer;

    private Dictionary<string, ObjectiveUI2> activeObjectives = new Dictionary<string, ObjectiveUI2>();
    private HashSet<string> completedObjectives = new HashSet<string>();
    private HashSet<string> hiddenObjectives = new HashSet<string>();

    // objectiveId -> data, built once from allObjectives so lookups aren't linear scans
    private readonly Dictionary<string, ObjectiveData> objectiveLookup = new Dictionary<string, ObjectiveData>();

    // Add these to track firestick progress
    private Dictionary<string, int> firestickProgress = new Dictionary<string, int>();
    private readonly Dictionary<string, int> requiredFiresticks = new Dictionary<string, int>()
    {
        { ObjectiveConstants.LIGHT_FIRESTICKS_GRASSLANDS, 2 },
        { ObjectiveConstants.LIGHT_FIRESTICKS_SWAMP, 3 }
    };

    public delegate void ObjectiveAddedHandler(string objectiveId);
    public event ObjectiveAddedHandler OnObjectiveAdded;

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

        BuildObjectiveLookup();

        // Load prefab if not assigned
        if (objectiveUIPrefab == null)
        {
            objectiveUIPrefab = Resources.Load<ObjectiveUI2>("Prefabs/UI/ObjectiveUI");
            if (objectiveUIPrefab == null)
            {
                Debug.LogWarning("ObjectiveUI Prefab not found in Resources folder. Some UI features will be disabled.");
            }
        }

        // Create container if not assigned
        if (objectivesContainer == null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                objectivesContainer = new GameObject("ObjectivesContainer").transform;
                objectivesContainer.SetParent(canvas.transform, false);
            }
            else
            {
                Debug.LogWarning("No Canvas found in scene. Some UI features will be disabled.");
            }
        }
    }

    private void Start()
    {
        // Initialize progress counters
        firestickProgress[ObjectiveConstants.LIGHT_FIRESTICKS_GRASSLANDS] = 0;
        firestickProgress[ObjectiveConstants.LIGHT_FIRESTICKS_SWAMP] = 0;
    }

    private void BuildObjectiveLookup()
    {
        objectiveLookup.Clear();
        if (allObjectives == null) return;

        foreach (ObjectiveData objective in allObjectives)
        {
            if (objective == null || string.IsNullOrEmpty(objective.objectiveId)) continue;

            if (objectiveLookup.ContainsKey(objective.objectiveId))
            {
                Debug.LogWarning($"[Objective] Duplicate objective id '{objective.objectiveId}' in allObjectives; keeping the first.", this);
                continue;
            }

            objectiveLookup.Add(objective.objectiveId, objective);
        }
    }

    public void AddObjective(string objectiveId)
    {
        if (!HasObjective(objectiveId))
        {
            Debug.LogWarning($"[Objective] Tried to add unknown objective: {objectiveId}");
            return;
        }

        // Re-adding a previously completed objective makes it pending again.
        completedObjectives.Remove(objectiveId);

        if (activeObjectives.ContainsKey(objectiveId))
        {
            return;
        }

        ObjectiveData objectiveData = GetObjectiveData(objectiveId);
        if (objectiveData == null)
        {
            return;
        }

        // Counter objectives sync to the WORLD when shown, not to zero. A player who
        // sprints to a nearby firestick and lights it before this objective is assigned
        // must keep that progress - a lit stick cannot be lit again, so the old reset
        // made the objective permanently incompletable. A re-shown objective gets the
        // same treatment: sticks still burning still count.
        if (firestickProgress.ContainsKey(objectiveId))
        {
            firestickProgress[objectiveId] = CountLitFiresticks(objectiveId);
        }

        // The UI is optional. Objective state has to exist even when no prefab or
        // canvas is available, or triggers and prerequisites silently stop working.
        ObjectiveUI2 objectiveUI = null;
        if (objectiveUIPrefab != null && objectivesContainer != null)
        {
            objectiveUI = Instantiate(objectiveUIPrefab, objectivesContainer);
            objectiveUI.gameObject.SetActive(true);
            objectiveUI.Initialize(objectiveData);
        }

        activeObjectives.Add(objectiveId, objectiveUI);

        // Listeners may complete the objective synchronously — FirstTimeInteractionTracker
        // does exactly that for things the player already did — so the completed check
        // below decides whether this objective still needs markers.
        OnObjectiveAdded?.Invoke(objectiveId);

        // The world may already satisfy a counter objective (every stick lit before it
        // was assigned) - complete it now, the same way the tracker above completes
        // already-done interactions synchronously.
        if (!IsObjectiveCompleted(objectiveId) &&
            firestickProgress.TryGetValue(objectiveId, out int litAlready) &&
            requiredFiresticks.TryGetValue(objectiveId, out int required) &&
            litAlready >= required)
        {
            CompleteObjective(objectiveId);
        }

        if (!IsObjectiveCompleted(objectiveId) &&
            objectiveData.showWorldMarker &&
            WorldSpaceObjectiveManager.Instance != null)
        {
            List<Transform> targets = WorldSpaceObjectiveManager.Instance.GetTargetLocations(objectiveId);

            // No marker on a stick that is already burning: it cannot be interacted
            // with again, and an arrow pointing at it reads as a broken objective.
            targets?.RemoveAll(target =>
                target != null &&
                target.GetComponentInParent<FirestickInteraction>() is FirestickInteraction stick &&
                stick.IsLit);

            if (targets != null && targets.Count > 0)
            {
                WorldSpaceObjectiveManager.Instance.CreateObjectiveMarkers(
                    objectiveId,
                    targets,
                    objectiveData.markerIcon
                );
            }
        }
    }

    public void CompleteObjective(string objectiveId)
    {
        if (!activeObjectives.TryGetValue(objectiveId, out ObjectiveUI2 objectiveUI))
        {
            return;
        }

        completedObjectives.Add(objectiveId);

        // XP for the objective. Completing one granted nothing before 2026-09-04; the
        // amount is a share of the current level's cost, decided by PlayerStats, so the
        // reward keeps pace with the curve instead of fading out by level 8.
        if (PlayerStats.Instance != null)
        {
            PlayerStats.Instance.AwardObjectiveXP();
        }

        // ObjectiveUI2 owns its own teardown (inactive -> out -> destroy). The manager
        // only drops its reference; destroying it here as well cut the exit animation off.
        activeObjectives.Remove(objectiveId);
        if (objectiveUI != null)
        {
            objectiveUI.PlayCompletionAnimation();
        }

        // Find and handle all ObjectiveTargetLocators for this objective
        var targetLocators = ObjectiveTargetLocator.FindLocatorsForTarget(objectiveId);
        foreach (var locator in targetLocators)
        {
            if (locator != null)
            {
                locator.enabled = false;
            }
        }

        // Play completion sound
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayObjectiveCompleted();
        }
    }

    public void HideObjective(string objectiveId)
    {
        if (activeObjectives.TryGetValue(objectiveId, out ObjectiveUI2 objectiveUI))
        {
            if (objectiveUI != null)
            {
                Destroy(objectiveUI.gameObject);
            }
            activeObjectives.Remove(objectiveId);
            hiddenObjectives.Add(objectiveId);
        }
    }

    public void ShowObjective(string objectiveId)
    {
        ObjectiveData objectiveData = GetObjectiveData(objectiveId);
        if (objectiveData == null) return;

        // Check prerequisites
        if (objectiveData.prerequisites != null && objectiveData.prerequisites.Length > 0)
        {
            foreach (ObjectiveData prerequisite in objectiveData.prerequisites)
            {
                if (prerequisite == null) continue;
                
                if (!IsObjectiveCompleted(prerequisite.objectiveId))
                {
                    return;
                }
            }
        }

        hiddenObjectives.Remove(objectiveId);
        AddObjective(objectiveId);
    }

    public bool HasObjective(string objectiveId)
    {
        return !string.IsNullOrEmpty(objectiveId) && objectiveLookup.ContainsKey(objectiveId);
    }

    public ObjectiveData GetObjectiveData(string objectiveId)
    {
        if (string.IsNullOrEmpty(objectiveId)) return null;

        objectiveLookup.TryGetValue(objectiveId, out ObjectiveData objectiveData);
        return objectiveData;
    }

    public bool IsObjectiveActive(string objectiveId)
    {
        return activeObjectives.ContainsKey(objectiveId) && !completedObjectives.Contains(objectiveId);
    }

    public bool IsObjectiveCompleted(string objectiveId)
    {
        return completedObjectives.Contains(objectiveId);
    }

    /// <summary>
    /// Lit firesticks belonging to this objective, counted from the world - the single
    /// truth the counters sync to whenever the objective is (re)shown.
    /// </summary>
    private static int CountLitFiresticks(string objectiveId)
    {
        int lit = 0;

        foreach (FirestickInteraction firestick in FindObjectsByType<FirestickInteraction>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (firestick.IsLit && firestick.ObjectiveId == objectiveId) lit++;
        }

        return lit;
    }

    public void IncrementFirestickProgress(string objectiveId)
    {
        if (firestickProgress.ContainsKey(objectiveId))
        {
            firestickProgress[objectiveId]++;
            
            if (firestickProgress[objectiveId] >= requiredFiresticks[objectiveId])
            {
                CompleteObjective(objectiveId);
                
                if (WorldSpaceObjectiveManager.Instance != null)
                {
                    WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker(objectiveId);
                }
            }
        }
    }
}