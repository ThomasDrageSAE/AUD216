using UnityEngine;

public class FirstTimeInteractionTracker : MonoBehaviour
{
    public static FirstTimeInteractionTracker Instance { get; private set; }

    [Header("Power and Lights")]
    [SerializeField] private bool hasElectricalBoxBeenUsed;
    [SerializeField] private bool hasLightsBeenTurnedOnFirstTime;
    [SerializeField] private bool hasMixingDeskBeenUsedFirstTime;

    // Firestick progress is counted by ObjectiveManager.IncrementFirestickProgress,
    // driven by FirestickInteraction. This class used to keep a second, parallel
    // count that nothing ever incremented.

    [Header("Fishing")]
    [SerializeField] private bool hasFishingRodBeenPickedUp;
    [SerializeField] private bool hasFirstFishBeenCaught;

    [Header("Objective IDs")]
    [SerializeField] private string electricalBoxObjectiveId = ObjectiveConstants.FIX_STUDIO_POWER;
    [SerializeField] private string lightsObjectiveId = ObjectiveConstants.TURN_ON_LIGHTS;
    [SerializeField] private string mixingDeskObjectiveId = ObjectiveConstants.TEST_MIXING_DESK;
    [SerializeField] private string fishingRodObjectiveId = ObjectiveConstants.PICKUP_FISHING_ROD;
    [SerializeField] private string catchFishObjectiveId = ObjectiveConstants.CATCH_FISH;

    [Header("Tools")]
    [SerializeField] private bool hasAxeBeenPickedUp;
    [SerializeField] private string axeObjectiveId = ObjectiveConstants.PICKUP_AXE;

    [Header("Campfire")]
    [SerializeField] private bool hasCampfireBeenLit;
    [SerializeField] private string campfireObjectiveId = ObjectiveConstants.LIGHT_CAMPFIRE;

    [Header("Day 3 (cooking and the well)")]
    [SerializeField] private bool hasCookedAFish;
    [SerializeField] private bool hasMadeWellOffering;
    [SerializeField] private string cookFishObjectiveId = ObjectiveConstants.COOK_A_FISH;
    [SerializeField] private string wellOfferingObjectiveId = ObjectiveConstants.MAKE_WELL_OFFERING;

    private ActionHandler actionHandler;
    private bool hasCompletedElectricalObjective = false;
    private bool hasCompletedLightsObjective = false;
    private bool hasCompletedMixingDeskObjective = false;
    private bool hasCompletedFishingRodObjective = false;
    private bool hasCompletedFirstFishObjective = false;
    private bool hasCompletedAxeObjective = false;
    private bool hasCompletedCampfireObjective = false;
    private bool hasCompletedCookFishObjective = false;
    private bool hasCompletedWellOfferingObjective = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            // Single-scene game: deliberately NOT DontDestroyOnLoad (see ObjectiveManager).
            // This tracker holds "player already did this" flags, which MUST reset on a
            // restart or the fresh run auto-completes its first objectives.
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        actionHandler = FindFirstObjectByType<ActionHandler>();
        if (actionHandler == null)
        {
            Debug.LogError("No ActionHandler found in the scene!");
        }

        // Subscribe to objective added event
        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.OnObjectiveAdded += HandleObjectiveAdded;
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from objective added event
        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.OnObjectiveAdded -= HandleObjectiveAdded;
        }
    }

    private void HandleObjectiveAdded(string objectiveId)
    {
        // Event-driven (ObjectiveManager raises OnObjectiveAdded), so a null Instance
        // here means the manager was destroyed mid-dispatch. Rare, and it silently
        // loses a retroactive completion, so it is worth a console line.
        if (ObjectiveManager.Instance == null)
        {
            Debug.LogWarning("[FirstTimeInteractionTracker] HandleObjectiveAdded: no ObjectiveManager - " +
                "pre-completed objective '" + objectiveId + "' was not re-completed.");
            return;
        }

        // Check if any pre-completed objectives are being added
        if (objectiveId == fishingRodObjectiveId && hasFishingRodBeenPickedUp)
        {
            ObjectiveManager.Instance.CompleteObjective(fishingRodObjectiveId);
        }
        else if (objectiveId == catchFishObjectiveId && hasFirstFishBeenCaught)
        {
            ObjectiveManager.Instance.CompleteObjective(catchFishObjectiveId);
        }
        else if (objectiveId == axeObjectiveId && hasAxeBeenPickedUp)
        {
            ObjectiveManager.Instance.CompleteObjective(axeObjectiveId);
        }
        else if (objectiveId == campfireObjectiveId && hasCampfireBeenLit)
        {
            ObjectiveManager.Instance.CompleteObjective(campfireObjectiveId);
        }
        else if (objectiveId == cookFishObjectiveId && hasCookedAFish)
        {
            ObjectiveManager.Instance.CompleteObjective(cookFishObjectiveId);
        }
        else if (objectiveId == wellOfferingObjectiveId && hasMadeWellOffering)
        {
            ObjectiveManager.Instance.CompleteObjective(wellOfferingObjectiveId);
        }
    }

    private float nextPollTime;
    private const float PollInterval = 0.5f;

    private void Update()
    {
        if (actionHandler == null) return;

        // These are one-time flags; once all three objectives are completed
        // there is nothing left to poll for.
        if (hasCompletedElectricalObjective && hasCompletedLightsObjective && hasCompletedMixingDeskObjective)
            return;

        if (Time.time < nextPollTime) return;
        nextPollTime = Time.time + PollInterval;

        hasElectricalBoxBeenUsed = actionHandler.HasElectricalBoxBeenUsed;
        hasLightsBeenTurnedOnFirstTime = actionHandler.HasLightsBeenTurnedOnFirstTime;
        hasMixingDeskBeenUsedFirstTime = actionHandler.HasMixingDeskBeenUsedFirstTime;

        CheckAndCompleteObjectives();
    }

    public void OnFishingRodPickup()
    {
        hasFishingRodBeenPickedUp = true;
        if (!hasCompletedFishingRodObjective)
        {
            ObjectiveManager.Instance?.CompleteObjective(fishingRodObjectiveId);
            hasCompletedFishingRodObjective = true;
        }
    }

    public void OnFirstFishCaught()
    {
        hasFirstFishBeenCaught = true;
        if (!hasCompletedFirstFishObjective)
        {
            ObjectiveManager.Instance?.CompleteObjective(catchFishObjectiveId);
            hasCompletedFirstFishObjective = true;
        }
    }

    private void CheckAndCompleteObjectives()
    {
        if (ObjectiveManager.Instance == null) return;

        if (hasElectricalBoxBeenUsed && !hasCompletedElectricalObjective)
        {
            ObjectiveManager.Instance.CompleteObjective(electricalBoxObjectiveId);
            hasCompletedElectricalObjective = true;
        }

        if (hasLightsBeenTurnedOnFirstTime && !hasCompletedLightsObjective)
        {
            ObjectiveManager.Instance.CompleteObjective(lightsObjectiveId);
            hasCompletedLightsObjective = true;
        }

        if (hasMixingDeskBeenUsedFirstTime && !hasCompletedMixingDeskObjective)
        {
            ObjectiveManager.Instance.CompleteObjective(mixingDeskObjectiveId);
            hasCompletedMixingDeskObjective = true;
        }
    }

    public void OnAxePickup()
    {
        hasAxeBeenPickedUp = true;
        if (!hasCompletedAxeObjective)
        {
            ObjectiveManager.Instance?.CompleteObjective(axeObjectiveId);
            hasCompletedAxeObjective = true;
        }
    }

    /// <summary>
    /// Cooked (not charred) fish collected. Latched so a cook done BEFORE day 3
    /// assigns the objective still counts when it appears - the same retroactive
    /// completion every other first here gets, and the firestick lesson in event form.
    /// </summary>
    public void OnFishCooked()
    {
        hasCookedAFish = true;
        if (!hasCompletedCookFishObjective)
        {
            ObjectiveManager.Instance?.CompleteObjective(cookFishObjectiveId);
            hasCompletedCookFishObjective = true;
        }
    }

    /// <summary>The well took an offering. Same latch-and-retro-complete contract.</summary>
    public void OnWellOfferingMade()
    {
        hasMadeWellOffering = true;
        if (!hasCompletedWellOfferingObjective)
        {
            ObjectiveManager.Instance?.CompleteObjective(wellOfferingObjectiveId);
            hasCompletedWellOfferingObjective = true;
        }
    }

    public void OnCampfireLit()
    {
        hasCampfireBeenLit = true;
        if (!hasCompletedCampfireObjective)
        {
            ObjectiveManager.Instance?.CompleteObjective(campfireObjectiveId);
            hasCompletedCampfireObjective = true;
        }
    }
}