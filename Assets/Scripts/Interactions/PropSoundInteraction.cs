using UnityEngine;

/*
 * PropSoundInteraction.cs
 *
 * Purpose: The plain case - walk up to a prop, press E, hear a sound. Nothing else.
 * Used by: the stone well; anything else that just needs to be audible on interaction.
 *
 * InteractionSystemTMP is the other general interactable, but it is built around the
 * power grid (light switches, the electrical box, the mixing desk) and drags ActionHandler
 * in with it. A prop that only makes a noise does not want any of that.
 *
 * The sound is authored on InteractionAudioManager like every other interaction, so it
 * shows up in the Ctrl+G progress report and the Ctrl+J event monitor, and a missing
 * profile is reported there by name rather than failing silently.
 */

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class PropSoundInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InteractionPromptUI promptUI;
    [Tooltip("Optional. The source the sound plays from; one is added at runtime if empty")]
    [SerializeField] private AudioSource audioSource;

    [Header("Interaction Settings")]
    [SerializeField] private string objectDisplayName = "Well";
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string actionMessage = "listen";
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("Seconds before it can be used again. Stops a held key machine-gunning the clip")]
    [Min(0f)] [SerializeField] private float cooldown = 1.5f;

    [Tooltip("How close the player must get, in metres. Re-applied to the trigger on " +
             "Start, so it holds at this distance whatever scale the instance carries.")]
    [Min(0f)] [SerializeField] private float interactionReach = 1.5f;

    [Tooltip("Use once only, then go quiet (the prompt stops appearing)")]
    [SerializeField] private bool singleUse = false;

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Well";
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

    private bool playerInRange;
    private bool used;
    private float nextUseTime;
    private InteractionAudioManager audioManager;

    private void Start()
    {
        RefreshReach();

        if (promptUI == null) promptUI = FindFirstObjectByType<InteractionPromptUI>();
        audioManager = FindFirstObjectByType<InteractionAudioManager>();

        // PlaySimpleInteraction dereferences the source it is handed whenever the profile
        // has no custom one of its own, so this prop always carries a usable source.
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;   // a world object, not a UI sound
        }

        // After promptUI is resolved, so a prompt can actually be shown.
        SeedPlayerInRange();
    }

    /// <summary>
    /// Re-derives the trigger radius from Interaction Reach. Call it again if this prop
    /// is rescaled or reparented at runtime: the radius is stored in local units, so it
    /// would otherwise keep the world size it had at load.
    /// </summary>
    public void RefreshReach()
    {
        InteractionReach.Apply(gameObject, interactionReach);
    }

    /// <summary>
    /// Picks up a player who is already inside the trigger at load. Resizing a collider
    /// raises no OnTriggerEnter for something already within it, so without this the
    /// prompt would not appear until they stepped out of range and back in.
    /// </summary>
    private void SeedPlayerInRange()
    {
        if (used || !InteractionReach.ContainsPlayer(gameObject)) return;

        playerInRange = true;

        if (promptUI != null)
        {
            promptUI.ShowPrompt(objectDisplayName, inputKey, actionMessage);
        }
    }

    private void Update()
    {
        if (!playerInRange || used) return;
        if (Time.time < nextUseTime) return;
        if (!Input.GetKeyDown(interactKey)) return;

        Interact();
    }

    private void Interact()
    {
        nextUseTime = Time.time + cooldown;

        if (audioManager != null)
        {
            audioManager.PlaySimpleInteraction(AudioProfileKey, audioSource);
        }

        if (!singleUse) return;

        used = true;
        if (promptUI != null) promptUI.HidePrompt();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || used) return;

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

        if (promptUI != null) promptUI.HidePrompt();
    }
}
