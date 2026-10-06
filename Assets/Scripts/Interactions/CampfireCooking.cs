using System.Collections;
using UnityEngine;

/*
 * CampfireCooking.cs
 *
 * Purpose: Cook the fish you catch. A lit campfire with a raw fish in the inventory
 *          offers "cook fish": the fish goes on, a sizzle loop runs, a chime says done -
 *          and a fish left on past that burns. Cooked fish restores far more hunger;
 *          charred fish barely any.
 * Used by: the campfire; added beside CampfireInteraction with
 *          Tools > Interactions > Add Campfire Cooking.
 *
 * The timing is carried entirely by audio: the sizzle is the progress bar and the chime
 * is the "now". A fish left on past the chime still burns, but silently - the collect and
 * burn one-shots were removed 2026-09-03, leaving the Cooking profile the two slots that
 * carry the timing; the burn is a status message. No cooking UI.
 *
 * Raw fish are Fish components (built by the rod); cooked and charred output are plain
 * FoodItems via InitializeFood - deliberately NOT Fish.InitializeFish, which awards XP
 * and credits the catch objectives, and cooking must not pay for the same fish twice.
 *
 * Shares the campfire's trigger but owns its own AudioSource, so the sizzle never
 * fights the fire's own crackle loop. E is safe to share with CampfireInteraction:
 * that one only listens while the fire is UNLIT, this one only while it is lit.
 */
[RequireComponent(typeof(CampfireInteraction))]
[DisallowMultipleComponent]
public class CampfireCooking : MonoBehaviour
{
    private enum CookState { Idle, Cooking, Done, Burnt }

    [Header("Timing")]
    [Tooltip("Seconds of sizzle before the fish is ready")]
    [Min(1f)] [SerializeField] private float cookSeconds = 8f;

    [Tooltip("Seconds after the done-chime before an uncollected fish burns")]
    [Min(1f)] [SerializeField] private float burnAfterSeconds = 12f;

    [Header("Output")]
    [SerializeField] private string cookedName = "Cooked Fish";
    [SerializeField] private string cookedDescription = "Cooked over the campfire. Restores plenty of hunger.";

    [Tooltip("Cooked value = the RAW fish's nutrition x this, so a rare catch stays " +
             "worth cooking - a flat number made a Golden Koi WORSE cooked than raw")]
    [Min(1f)] [SerializeField] private float cookedNutritionMultiplier = 1.8f;

    [SerializeField] private string charredName = "Charred Fish";
    [SerializeField] private string charredDescription = "Left on too long. Barely food.";
    [Min(0f)] [SerializeField] private float charredNutrition = 8f;

    [Header("Audio")]
    // The clips (sizzle loop, done chime) live on the manager's "Cooking" profile -
    // one section per prop, like the flashlight - so students author them beside every
    // other interaction sound. This component only knows the profile key.
    [SerializeField] private string audioProfileName = "Cooking";

    [Tooltip("Compile-checked identity. When set, this wins over the name above")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    [Header("Messages")]
    // On-screen status through the fishing-message channel. The audio is still the
    // intended progress bar, but before the clips are authored the cook was fish
    // in -> 8 seconds of absolute silence with no prompt -> silent burn, which reads
    // as the fish being eaten by a bug. Feedback must not depend on authoring.
    [SerializeField] private string cookingMessage = "Cooking fish...";
    [SerializeField] private string readyMessage = "Fish ready!  Press E at the campfire to collect";
    [SerializeField] private string burntMessage = "The fish burnt...";

    private CampfireInteraction campfire;
    private InteractionPromptUI promptUI;
    private AudioSource cookSource;
    private InteractionAudioManager audioManager;

    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    private CookState state = CookState.Idle;
    private float stateEndsAt;
    private float rawNutrition;     // of the fish on the fire, captured when it goes on
    private Sprite cookingIcon;     // carried from the consumed raw fish
    private bool playerInRange;
    private bool showingCookPrompt;

    private void Start()
    {
        campfire = GetComponent<CampfireInteraction>();
        promptUI = FindFirstObjectByType<InteractionPromptUI>();

        // Its own source: the fire's crackle already owns the campfire's main one.
        cookSource = gameObject.AddComponent<AudioSource>();
        cookSource.playOnAwake = false;
        cookSource.spatialBlend = 1f;   // the pan is ON the fire
    }

    private void Update()
    {
        // The offer has to follow the world, not the trigger: lighting the fire while
        // standing at it, or catching a first fish in range, used to surface nothing
        // until the player walked out and back in. Only OUR prompt is ever hidden here
        // - hiding unconditionally could stomp CampfireInteraction's 'light fire'.
        if (playerInRange && state == CookState.Idle)
        {
            bool offering = campfire.IsLit && HasRawFish();
            if (offering && !showingCookPrompt)
            {
                RefreshPrompt();
            }
            else if (!offering && showingCookPrompt)
            {
                showingCookPrompt = false;
                if (promptUI != null) promptUI.HidePrompt();
            }
        }

        switch (state)
        {
            case CookState.Cooking when Time.time >= stateEndsAt:
                state = CookState.Done;
                stateEndsAt = Time.time + burnAfterSeconds;
                if (cookSource.isPlaying) cookSource.Stop();
                PlayCookingOneShot(p => p.doneChime, "done chime");
                ShowStatus(readyMessage);   // visible even if the player stepped away
                RefreshPrompt();
                break;

            case CookState.Done when Time.time >= stateEndsAt:
                state = CookState.Burnt;
                // No burn sound by design (removed 2026-09-03) - the message carries it.
                StartCoroutine(ShowStatusBriefly(burntMessage, 4f));
                RefreshPrompt();
                break;
        }

        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            HandleInteract();
        }
    }

    private void HandleInteract()
    {
        switch (state)
        {
            case CookState.Idle when campfire.IsLit:
                TryStartCooking();
                break;

            case CookState.Done:
                Collect(cookedName, cookedDescription, rawNutrition * cookedNutritionMultiplier,
                    countsAsCooked: true);
                break;

            case CookState.Burnt:
                // Charring is NOT cooking - the objective stays open.
                Collect(charredName, charredDescription, charredNutrition, countsAsCooked: false);
                break;
        }
    }

    private void TryStartCooking()
    {
        if (Inventory.Instance == null) return;

        // The first RAW fish. Cooked/charred output are plain FoodItems, so `is Fish`
        // is exactly the raw test - nothing can be cooked twice.
        Item[] items = Inventory.Instance.GetAllItems();
        for (int slot = 0; slot < items.Length; slot++)
        {
            if (items[slot] is Fish rawFish)
            {
                cookingIcon = rawFish.GetItemIcon();
                rawNutrition = rawFish.GetNutritionValue();
                Inventory.Instance.RemoveItem(slot);
                Destroy(rawFish.gameObject);

                state = CookState.Cooking;
                stateEndsAt = Time.time + cookSeconds;

                ShowStatus(cookingMessage);

                if (TryGetCookingProfile(out var profile) && profile.sizzleLoop != null)
                {
                    cookSource.clip = profile.sizzleLoop;
                    cookSource.loop = true;
                    cookSource.volume = 1f;
                    cookSource.Play();
                    AudioEventLog.Record(AudioEventKind.Play, "Campfire",
                        $"sizzle '{profile.sizzleLoop.name}'");
                }

                RefreshPrompt();
                return;
            }
        }
    }

    private void Collect(string itemName, string description, float nutrition,
                         bool countsAsCooked)
    {
        if (Inventory.Instance == null) return;

        if (!Inventory.Instance.HasEmptySlot())
        {
            // Stay collectable; the fish waits (a burnt one cannot burn further).
            if (promptUI != null) promptUI.ShowPrompt(itemName, "E", "inventory full!");
            return;
        }

        GameObject foodObject = new GameObject(itemName);
        FoodItem food = foodObject.AddComponent<FoodItem>();
        food.InitializeFood(itemName, description, cookingIcon, nutrition);

        if (!Inventory.Instance.AddItem(food))
        {
            Destroy(foodObject);
            return;
        }

        // Collecting is silent by design (the collect one-shot was removed 2026-09-03).
        HideStatus();

        // Through the tracker, so a fish cooked BEFORE day 3 assigns the objective
        // still counts when it appears (retroactive completion, like every other
        // first). Direct completion is only the no-tracker fallback.
        if (countsAsCooked)
        {
            if (FirstTimeInteractionTracker.Instance != null)
            {
                FirstTimeInteractionTracker.Instance.OnFishCooked();
            }
            else
            {
                ObjectiveManager.Instance?.CompleteObjective(ObjectiveConstants.COOK_A_FISH);
            }
        }

        state = CookState.Idle;
        cookingIcon = null;
        RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        if (!playerInRange || promptUI == null) return;

        switch (state)
        {
            case CookState.Idle when campfire.IsLit && HasRawFish():
                promptUI.ShowPrompt("Campfire", "E", "cook fish");
                showingCookPrompt = true;
                break;

            case CookState.Done:
                promptUI.ShowPrompt(cookedName, "E", "collect");
                break;

            case CookState.Burnt:
                promptUI.ShowPrompt(charredName, "E", "collect");
                break;

            case CookState.Cooking:
                promptUI.HidePrompt();   // the sizzle is the progress bar
                showingCookPrompt = false;
                break;
        }
    }

    private void ShowStatus(string message)
    {
        if (EventManager.Instance != null && !string.IsNullOrEmpty(message))
        {
            EventManager.Instance.ShowFishingPrompt(message);
        }
    }

    private void HideStatus()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.HideInteractionPrompt();
        }
    }

    /// <summary>The burnt notice should not sit on screen forever if never collected.</summary>
    private IEnumerator ShowStatusBriefly(string message, float seconds)
    {
        ShowStatus(message);
        yield return new WaitForSeconds(seconds);

        // Only clear our own message - a newer state may have replaced it.
        if (state == CookState.Burnt) HideStatus();
    }

    private static bool HasRawFish()
    {
        if (Inventory.Instance == null) return false;

        foreach (Item item in Inventory.Instance.GetAllItems())
        {
            if (item is Fish) return true;
        }

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = true;
        RefreshPrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;
        // CampfireInteraction hides the prompt on exit too; both run, one wins, same result.
    }

    /// <summary>
    /// Fetches the "Cooking" profile, routing the cook source through its mixer group
    /// when one is set. A missing profile logs the same red Ctrl+J row as any other
    /// unresolved interaction key.
    /// </summary>
    private bool TryGetCookingProfile(out InteractionAudioManager.InteractionSoundProfile profile)
    {
        profile = null;
        if (audioManager == null) audioManager = FindFirstObjectByType<InteractionAudioManager>();
        if (audioManager == null || !audioManager.TryGetProfile(AudioProfileKey, out profile)) return false;

        if (profile.mixerGroupOutput != null && cookSource != null)
        {
            cookSource.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        return true;
    }

    private void PlayCookingOneShot(System.Func<InteractionAudioManager.InteractionSoundProfile, AudioClip> pick,
                                    string label)
    {
        if (!TryGetCookingProfile(out var profile)) return;

        AudioClip clip = pick(profile);
        if (clip == null) return;

        AudioEventLog.Record(AudioEventKind.Play, "Campfire", $"{label} '{clip.name}'");
        cookSource.PlayOneShot(clip, 1f);
    }
}
