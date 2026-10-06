using UnityEngine;
using System.Collections;

public class InteractionSystemTMP : MonoBehaviour
{
    [Header("Interaction Settings")]
    public GameObject objectToInteractWith;
    public GameObject switchObject;
    public ActionHandler actionHandler;

    [SerializeField]
    private bool requiresPower = false;
    [SerializeField]
    private bool isLightSwitch = false;
    [SerializeField]
    private bool isMusicSystem = false;
    [SerializeField]
    private bool isPowerBox = false;

    private bool playerInRange = false;
    private bool isInteractable = true;
    private bool isInteracting = false;
    private bool hasShownPrompt = false;

    [Header("UI Settings")]
    [SerializeField] private string objectDisplayName = "Object";
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string customActionMessage = "interact";

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Light Switch";
    private InteractionAudioManager audioManager;
    private AudioSource audioSource;
    private bool isPlayingSparks = false;
    private bool isMusicPlaying = false;

    private InteractionPromptUI promptUI;

    private void Awake()
    {
        // Resolved here, not in Start: OnEnable subscribes to onPowerToggled and runs
        // BEFORE Start, so an un-wired instance silently never reacted to power changes.
        if (actionHandler == null)
        {
            actionHandler = FindFirstObjectByType<ActionHandler>();
        }
    }

    void Start()
    {

        if (promptUI == null)
        {
            promptUI = FindFirstObjectByType<InteractionPromptUI>();
        }

        // Get audio components
        audioManager = FindFirstObjectByType<InteractionAudioManager>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (audioManager != null)
        {
            // For electrical box, override the profile name
            if (isPowerBox)
            {
                audioProfileName = AudioProfileIds.NameOf(InteractionId.ElectricalBox);
            }
            
            audioManager.RegisterAudioSource(audioProfileName, audioSource);
            
            // Start sparks if this is the electrical box
            if (isPowerBox)
            {
 //               Debug.Log($"Starting electrical box sounds. Power is {(actionHandler?.IsPowerOn == true ? "ON" : "OFF")}"); // Debug log
                audioSource.loop = true;
                audioSource.spatialBlend = 1f; // 3D sound
                audioManager.PlayElectricalBoxSound(audioProfileName, audioSource, false);
                isPlayingSparks = true;
            }
        }

        // For mixing desk, override the profile name
        if (isMusicSystem)
        {
            audioProfileName = AudioProfileIds.NameOf(InteractionId.MixingDeskMusic);
        }

        UpdateInteractability();
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            if (actionHandler != null && !actionHandler.IsAnimating)
            {
                GameObject actualSwitchObject = switchObject != null ? switchObject : objectToInteractWith;
                
                if (isLightSwitch || !requiresPower || (requiresPower && actionHandler.IsPowerOn))
                {
                    isInteracting = true;
                    string action = GetActionCommand();
                    
                    // Handle mixing desk
                    if (isMusicSystem && audioManager != null)
                    {
                        audioProfileName = AudioProfileIds.NameOf(InteractionId.MixingDeskMusic);
                        isMusicPlaying = !isMusicPlaying;
                        audioManager.PlayMixingDeskAudio(audioProfileName, isMusicPlaying);

                        // Record that the desk has been used. The only assignment used to
                        // live in ActionHandler.ToggleMusic, whose body is commented out,
                        // so this flag could never become true.
                        if (actionHandler != null) actionHandler.MarkMixingDeskUsed();

                        // Complete the objective and remove world marker
                        if (ObjectiveManager.Instance != null)
                        {
                            ObjectiveManager.Instance.CompleteObjective(ObjectiveConstants.TEST_MIXING_DESK);

                            // Add this line to remove the world space marker
                            if (WorldSpaceObjectiveManager.Instance != null)
                            {
                                WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker(ObjectiveConstants.TEST_MIXING_DESK);
                            }
                        }
                    }
                    // Handle electrical box sounds. Only the lever is keyed to the press;
                    // the sparks audio is driven from onPowerToggled so it lands with the
                    // particles, which ActionHandler switches at the end of its 0.5s lever
                    // tween. Doing it here left audio and visuals half a second apart.
                    if (isPowerBox && audioManager != null)
                    {
                        StartCoroutine(PlayLeverSoundDelayed(0.1f));
                    }
                    // Handle light switch sounds
                    else if (isLightSwitch && audioManager != null)
                    {
                        audioManager.PlaySimpleInteraction(audioProfileName, audioSource);
                    }

                    EventManager.Instance.PerformAction(action, objectToInteractWith, actualSwitchObject);
                    isInteracting = false;
                }
                else
                {
                    EventManager.Instance.ShowInteractionPrompt(InteractionMessages.GetMessage("PowerRequired"));
                }
            }
            else
            {
                Debug.Log("Action is currently animating or ActionHandler is not assigned. Please wait.");
            }
        }
    }

    private string GetActionCommand()
    {
        if (isPowerBox)
            return "turn on the power";
        if (isLightSwitch)
            return "turn on the lights";
        if (isMusicSystem)
            return "play music";
            
        return "interact"; // Default fallback
    }

    public void UpdateInteractability()
    {
        isInteractable = !requiresPower || (requiresPower && actionHandler != null && actionHandler.IsPowerOn) || isLightSwitch || isMusicSystem;
        
        if (playerInRange && !isInteracting)
        {
            if (isInteractable)
            {
                ShowPrompt();
            }
            else
            {
                EventManager.Instance.HideInteractionPrompt();
            }
        }
    }

    private void ShowPrompt()
    {
        if (isPowerBox && hasShownPrompt)
            return;

        string actionMessage = customActionMessage;
        if (isPowerBox)
        {
            hasShownPrompt = true;
        }

        if (promptUI != null)
        {
            promptUI.ShowPrompt(objectDisplayName, inputKey, actionMessage);
        }
    }

    // Add this method to update the prompt when the power state changes
    private void UpdatePrompt()
    {
        if (playerInRange)
        {
            ShowPrompt();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            hasShownPrompt = false; // Reset the flag when the player enters the trigger
            UpdateInteractability();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            hasShownPrompt = false;
            
            if (promptUI != null)
            {
                promptUI.HidePrompt();
            }
        }
    }

    private void OnEnable()
    {
        if (actionHandler != null)
        {
            actionHandler.onPowerToggled.AddListener(OnPowerStateChanged);
        }
    }

    private void OnDisable()
    {
        // Stop sparks sound when disabled
        if (isPowerBox && isPlayingSparks && audioSource != null)
        {
            audioSource.Stop();
        }

        if (actionHandler != null)
        {
            actionHandler.onPowerToggled.RemoveListener(OnPowerStateChanged);
        }
    }

    // Modify the OnPowerStateChanged method to not update the prompt for the power box
    private void OnPowerStateChanged(bool isPowerOn)
    {
        // Sparks audio follows the power state itself, so it changes with the particles.
        if (isPowerBox && audioManager != null)
        {
            if (isPowerOn)
            {
                audioSource.Stop();
                isPlayingSparks = false;
            }
            else if (!isPlayingSparks)
            {
                StartCoroutine(PlaySparksAfterDelay(0f));
            }
        }

        // The speakers are wired to the power: cutting it must silence them, and
        // isMusicPlaying has to follow or the next press toggles the wrong way.
        if (isMusicSystem && !isPowerOn && isMusicPlaying && audioManager != null)
        {
            audioManager.PlayMixingDeskAudio(audioProfileName, false);
            isMusicPlaying = false;
        }

        UpdateInteractability();
        if (!isPowerBox)
        {
            UpdatePrompt();
        }
    }

    private IEnumerator PlaySparksAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        audioSource.loop = true;
        audioManager.PlayElectricalBoxSound(audioProfileName, audioSource, false);
        isPlayingSparks = true;
    }

    private IEnumerator PlayLeverSoundDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        audioManager.PlayElectricalBoxSound(audioProfileName, audioSource, true);
    }
}
