using UnityEngine;

public class CampfireInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ParticleSystem campfireParticleSystem;
    [SerializeField] private GameObject campfireLight;
    [SerializeField] private AudioSource campfireAudioSource;
    [SerializeField] private InteractionPromptUI promptUI;

    [Header("Interaction Settings")]
    [SerializeField] private string objectDisplayName = "Campfire";
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string actionMessage = "light fire";

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Campfire";
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
    private bool isCampfireLit = false;

    /// <summary>Read by CampfireCooking - cooking needs a fire.</summary>
    public bool IsLit => isCampfireLit;
    private InteractionAudioManager audioManager;

    void Start()
    {
        if (promptUI == null)
            promptUI = FindFirstObjectByType<InteractionPromptUI>();

        audioManager = FindFirstObjectByType<InteractionAudioManager>();
        
        if (audioManager != null && campfireAudioSource != null)
        {
            audioManager.RegisterAudioSource(AudioProfileKey, campfireAudioSource);
        }

        if (campfireParticleSystem != null)
            campfireParticleSystem.Stop();

        if (campfireLight != null)
            campfireLight.SetActive(false);
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E) && !isCampfireLit)
        {
            LightCampfire();
        }
    }

    private void LightCampfire()
    {
        if (campfireParticleSystem != null)
            campfireParticleSystem.Play();

        if (campfireLight != null)
            campfireLight.SetActive(true);

        if (audioManager != null)
        {
            audioManager.IgniteFirestick(AudioProfileKey, campfireAudioSource);
        }

        isCampfireLit = true;
        
        // Notify tracker that campfire has been lit
        if (FirstTimeInteractionTracker.Instance != null)
        {
            FirstTimeInteractionTracker.Instance.OnCampfireLit();
        }
        
        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.CompleteObjective(ObjectiveConstants.LIGHT_CAMPFIRE);

            if (WorldSpaceObjectiveManager.Instance != null)
            {
                WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker(ObjectiveConstants.LIGHT_CAMPFIRE, transform);
            }
        }

        // Hide the prompt we actually showed. This used to call through EventManager to
        // UIManager, which drives a different panel entirely, so the "press E to light
        // fire" prompt stayed on screen until the player walked out of the trigger.
        if (promptUI != null)
        {
            promptUI.HidePrompt();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !isCampfireLit)
        {
            playerInRange = true;
            
            if (promptUI != null)
            {
                promptUI.ShowPrompt(objectDisplayName, inputKey, actionMessage);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (promptUI != null)
            {
                promptUI.HidePrompt();
            }
        }
    }
}