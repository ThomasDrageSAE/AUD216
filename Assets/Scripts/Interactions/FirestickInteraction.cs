using UnityEngine;

public class FirestickInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ParticleSystem fireParticleSystem;
    [SerializeField] private AudioSource firestickAudioSource;
    [SerializeField] private InteractionPromptUI promptUI;

    [Header("Interaction Settings")]
    [SerializeField] private string objectDisplayName = "Firesticks";
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string actionMessage = "light fire";
    [SerializeField] private bool isSwampFirestick = false;

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Firestick";
    [Tooltip("Compile-checked identity. When set, this wins over the name above - which " +
             "stays as the fallback for anything not yet migrated.")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    /// <summary>
    /// The key handed to InteractionAudioManager: the id when one is assigned, otherwise
    /// the authored string. Both resolve through the same lookup.
    /// </summary>
    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    private bool playerInRange = false;
    private bool isLit = false;

    // One firestick per E press. Two sticks stand close enough that a player between
    // them is inside BOTH triggers, and every FirestickInteraction polls the same
    // GetKeyDown - so one press lit both at once. Whichever Update runs first claims
    // the frame; the neighbour waits for the next press. Static, reset on load like
    // the project's other reset-on-load statics.
    private static int lightClaimFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lightClaimFrame = -1;

    /// <summary>
    /// World-state readouts for ObjectiveManager, which syncs its firestick counters
    /// to reality when an objective is assigned instead of assuming zero progress.
    /// ObjectiveId is computed, not the cached field, so it is safe before Awake.
    /// </summary>
    public bool IsLit => isLit;

    public string ObjectiveId => isSwampFirestick
        ? ObjectiveConstants.LIGHT_FIRESTICKS_SWAMP
        : ObjectiveConstants.LIGHT_FIRESTICKS_GRASSLANDS;
    private ParticleSystem.EmissionModule emission;
    private string objectiveId;
    private KeyCode interactKey = KeyCode.E;
    private WorldSpaceObjectiveManager worldSpaceManager;
    private ObjectiveManager objectiveManager;
    private InteractionAudioManager audioManager;

    private void Awake()
    {
        // Cache the objective ID
        objectiveId = isSwampFirestick
            ? ObjectiveConstants.LIGHT_FIRESTICKS_SWAMP
            : ObjectiveConstants.LIGHT_FIRESTICKS_GRASSLANDS;

        // Keep the prompt and the key that actually works in sync. The displayed
        // string is the source of truth; labels that aren't key names fall back to E.
        if (!System.Enum.TryParse(inputKey, true, out interactKey))
        {
            interactKey = KeyCode.E;
        }


        if (fireParticleSystem != null)
        {
            emission = fireParticleSystem.emission;
            emission.enabled = false;
            fireParticleSystem.Stop();
        }
    }

    private void Start()
    {
        // Cache references
        if (promptUI == null)
            promptUI = FindFirstObjectByType<InteractionPromptUI>();
            
        audioManager = FindFirstObjectByType<InteractionAudioManager>();
        
        // Add this firestick's AudioSource to the manager's profile
        if (audioManager != null && firestickAudioSource != null)
        {
            audioManager.RegisterAudioSource(AudioProfileKey, firestickAudioSource);
        }

        worldSpaceManager = WorldSpaceObjectiveManager.Instance;
        objectiveManager = ObjectiveManager.Instance;
    }

    private void Update()
    {
        if (playerInRange && !isLit && Input.GetKeyDown(interactKey))
        {
            // Another stick already took this press.
            if (Time.frameCount == lightClaimFrame) return;

            LightFirestick();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || isLit) return;

        // playerInRange is interaction state, NOT UI availability. Gating it on promptUI
        // meant a scene with no prompt UI made all five firesticks permanently
        // un-lightable and the objective unreachable.
        playerInRange = true;

        if (promptUI != null)
        {
            promptUI.ShowPrompt(objectDisplayName, inputKey, actionMessage);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;

        if (promptUI != null)
        {
            promptUI.HidePrompt();
        }
    }

    private void LightFirestick()
    {
        if (fireParticleSystem == null || isLit) return;

        // Claimed only on an ACTUAL light, so a broken stick cannot eat the press.
        lightClaimFrame = Time.frameCount;

        // Light the fire
        emission.enabled = true;
        fireParticleSystem.Play();
        
        // Play audio through the InteractionAudioManager
        if (audioManager != null)
        {
            audioManager.IgniteFirestick(AudioProfileKey, firestickAudioSource);
        }
        
        isLit = true;

        // Handle objectives
        if (worldSpaceManager != null)
        {
            worldSpaceManager.RemoveObjectiveMarker(objectiveId, transform);
        }

        if (objectiveManager != null)
        {
            objectiveManager.IncrementFirestickProgress(objectiveId);
        }

        if (promptUI != null)
        {
            // Standing between two sticks: hand the prompt to the neighbour still
            // waiting rather than blanking it while E still works.
            FirestickInteraction next = FindOtherUnlitInRange();
            if (next != null)
            {
                promptUI.ShowPrompt(next.objectDisplayName, next.inputKey, next.actionMessage);
            }
            else
            {
                promptUI.HidePrompt();
            }
        }
    }

    /// <summary>Another unlit firestick whose trigger the player is also inside, or null.</summary>
    private FirestickInteraction FindOtherUnlitInRange()
    {
        foreach (FirestickInteraction stick in FindObjectsByType<FirestickInteraction>(FindObjectsSortMode.None))
        {
            if (stick != this && !stick.isLit && stick.playerInRange) return stick;
        }

        return null;
    }
} 