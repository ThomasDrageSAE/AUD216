using UnityEngine;
using UnityEngine.Audio;

/*
 * MushroomTripInteraction.cs
 *
 * Purpose: An edible mushroom. Shows the standard E prompt in range, and on use plays its
 *          interaction sound and hands a trip to MushroomTripEffect.
 * Used by: dropped onto any mushroom prop in the scene (the Synty Nature pack ships six,
 *          under Inbox/Low Poly Models/Nature/Prefabs/Plants). Needs a trigger collider.
 *
 * Ported from a student submission (Liam Chapman, 26T2 AUD216). Reworked to this project's
 * conventions: the prompt goes through InteractionPromptUI rather than a TMP field wired
 * per mushroom, the sound goes through InteractionAudioManager so it appears in the
 * Ctrl+G progress report and the Ctrl+J event monitor, and the trip itself is owned by a
 * separate object so the mushroom can be consumed immediately instead of being kept alive
 * for the whole trip to protect its own coroutine.
 */

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class MushroomTripInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InteractionPromptUI promptUI;
    [Tooltip("Optional. The source the eat sound plays from; falls back to the manager's own")]
    [SerializeField] private AudioSource audioSource;

    [Header("Interaction Settings")]
    [SerializeField] private string objectDisplayName = "Mushroom";
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string actionMessage = "eat";
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Mushroom Eating";
    [Tooltip("Compile-checked identity. When set, this wins over the name above - which " +
             "stays as the fallback for anything not yet migrated.")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    [Header("Dialogue")]
    // The lines themselves live in the "Mushroom Eating" profile's Dialogue Lines
    // array (one section per prop on the InteractionAudioManager); one is chosen at
    // random per bite. Only the delivery tuning stays here.
    [Tooltip("Seconds between eating and the dialogue line")]
    [Min(0f)] [SerializeField] private float dialogueDelay = 3f;

    [Range(0f, 1f)] [SerializeField] private float dialogueVolume = 1f;

    [Header("Trip")]
    [SerializeField] private MushroomTripSettings trip = new MushroomTripSettings();

    [Tooltip("How much of the hunger meter eating one puts back, as a fraction of the full " +
             "meter. 0.15 is the owner's number (2026-09-06): a mushroom is food, just not much " +
             "of it, and the trip is the price.")]
    [Range(0f, 1f)] [SerializeField] private float hungerRestoredFraction = 0.15f;

    [Tooltip("Remove the mushroom once eaten. Uncheck while tuning the trip so it can be " +
             "used again without replaying the scene")]
    [SerializeField] private bool consumeOnUse = true;

    [Tooltip("How close the player must get, in metres. Re-applied to the trigger on " +
             "Start, so it holds at this distance whatever scale the instance carries.")]
    [Min(0f)] [SerializeField] private float interactionReach = 1.2f;

    [Tooltip("Seconds the eaten mushroom lingers, invisible, so its own eat sound can " +
             "finish. Destroying it outright would cut the clip off mid-play")]
    [Min(0f)] [SerializeField] private float destroyDelay = 3f;

    /// <summary>
    /// The key handed to InteractionAudioManager: the id when one is assigned, otherwise
    /// the authored string. Both resolve through the same lookup.
    /// </summary>
    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    private bool playerInRange;
    private InteractionAudioManager audioManager;

    private void Start()
    {
        RefreshReach();

        if (promptUI == null) promptUI = FindFirstObjectByType<InteractionPromptUI>();
        audioManager = FindFirstObjectByType<InteractionAudioManager>();

        // PlaySimpleInteraction dereferences the source it is handed whenever the profile
        // has no custom one of its own, so the mushroom always carries a usable source.
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
        if (!InteractionReach.ContainsPlayer(gameObject)) return;

        playerInRange = true;

        if (promptUI != null)
        {
            promptUI.ShowPrompt(objectDisplayName, inputKey, actionMessage);
        }
    }

    private void Update()
    {
        if (playerInRange && Input.GetKeyDown(interactKey))
        {
            Consume();
        }
    }

    private void Consume()
    {
        // One trip at a time. Without this a second mushroom would restart the fades and
        // silently reset the timer, which reads as the first one having done nothing.
        MushroomTripEffect effect = MushroomTripEffect.Get();
        if (effect.IsTripping) return;

        if (audioManager != null)
        {
            // Routed through the manager (not a bare PlayOneShot) so a missing profile
            // shows as a red row in the Ctrl+J monitor naming the key to author.
            audioManager.PlaySimpleInteraction(AudioProfileKey, audioSource);
        }

        effect.BeginTrip(trip);

        // It is food. Null-checked at the dereference: PlayerStats.Instance is null for the
        // first frames and after a reload, like every singleton in this level.
        PlayerStats stats = PlayerStats.Instance;
        if (stats != null && hungerRestoredFraction > 0f) stats.AddHunger(stats.MaxHunger * hungerRestoredFraction);

        // The line plays on the EFFECT, not on this object: an eaten mushroom destroys
        // itself after destroyDelay, which is about when the dialogue would be due, and
        // a coroutine dies with its host. The lines come off the profile's Dialogue
        // Lines array, authored beside the eat sound.
        if (audioManager != null && audioManager.TryGetProfile(AudioProfileKey, out var profile) &&
            profile.dialogueLines != null && profile.dialogueLines.Length > 0)
        {
            AudioClip line = profile.dialogueLines[Random.Range(0, profile.dialogueLines.Length)];
            AudioMixerGroup output = profile.mixerGroupOutput != null
                ? profile.mixerGroupOutput
                : (audioSource != null ? audioSource.outputAudioMixerGroup : null);
            effect.PlayDialogue(line, dialogueDelay, dialogueVolume, output);
        }

        if (promptUI != null) promptUI.HidePrompt();
        playerInRange = false;

        if (consumeOnUse) BeginConsumeTeardown();
    }

    /// <summary>
    /// Takes the mushroom out of the world immediately but keeps the GameObject alive
    /// briefly: the eat sound is playing on this object's own AudioSource, and destroying
    /// it outright would cut the clip off in the same frame it started.
    /// </summary>
    private void BeginConsumeTeardown()
    {
        enabled = false;   // no second prompt or second bite during the wait

        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }

        Destroy(gameObject, destroyDelay);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

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
