using UnityEngine;
using System.Collections;

/// <summary>
/// One rarity band of fish. Rarity is felt through the AUDIO: a rarer tier waits
/// longer, gives a shorter catch window, and can carry its own bite and catch clips.
/// </summary>
[System.Serializable]
public class FishTier
{
    public string tierName = "Fish";
    public string description = "A freshly caught fish.";

    [Tooltip("Relative chance of hooking this tier")]
    [Range(0.01f, 1f)] public float weight = 0.5f;

    public float nutritionValue = 25f;

    [Tooltip("Optional; falls back to the rod's Fish Icon")]
    public Sprite icon;

    [Tooltip("Scales the wait before a bite - rare fish take their time")]
    [Min(0.1f)] public float waitTimeMultiplier = 1f;

    [Tooltip("This tier's catch window - rare fish give you less of it")]
    [Min(0.2f)] public float catchWindowSeconds = 1.6f;

    [Tooltip("Optional per-tier bite plip; falls back to the Fishing profile's Bite Sound")]
    public AudioClip biteSound;

    [Tooltip("Optional per-tier catch fanfare; falls back to the Fishing profile's Caught Sound")]
    public AudioClip catchSound;
}

public class FishingRod : Item
{
    [Header("Position Settings")]
    [SerializeField] private Vector3 raisedPosition;
    [SerializeField] private Vector3 raisedRotation;
    [SerializeField] private Vector3 loweredPositionOffset;
    [SerializeField] private Vector3 loweredRotationOffset;
    
    [Header("Movement Speeds")]
    [Tooltip("How fast the fishing rod moves when being equipped")]
    [SerializeField] private float raiseSpeed = 8f;
    [Tooltip("How fast the fishing rod moves when being unequipped")]
    [SerializeField] private float lowerSpeed = 6f;

    [Header("Fishing Settings")]
    [SerializeField] private float minFishingTime = 5f;
    [SerializeField] private float maxFishingTime = 15f;

    [Header("Audio")]
    // The rod's one-shots (cast, bite, catch fanfare) are
    // authored on the InteractionAudioManager's "Fishing" profile - one section per
    // prop, beside every other interaction sound. Only the per-tier bite/catch
    // overrides stay on the tiers above. AUTHORING THE PROFILE'S BITE CLIP ARMS THE
    // CATCH WINDOW: with no bite clip anywhere, fishing lands automatically as it
    // always did, so an unauthored scene keeps working.
    [SerializeField] private string audioProfileName = "Fishing";

    [Tooltip("Compile-checked identity. When set, this wins over the name above")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    [Header("Bite (audible-first catching)")]
    [Tooltip("Seconds after the bite sound in which releasing the button lands the fish")]
    [Min(0.2f)] [SerializeField] private float catchWindowSeconds = 1.6f;

    [Header("Fish Settings")]
    [SerializeField] private string fishName = "Fresh Fish";
    [SerializeField] private string fishDescription = "A freshly caught fish. Can be eaten to restore hunger.";
    [SerializeField] private Sprite fishIcon;
    [SerializeField] private float fishNutritionValue = 25f;

    [Header("UI Messages")]
    [SerializeField] private string startFishingMessage = "Fishing...";
    [SerializeField] private string stopFishingMessage = "Stopped fishing";
    [SerializeField] private string fishCaughtMessage = "Fish caught!";
    [SerializeField] private string inventoryFullMessage = "Inventory is full!";
    [SerializeField] private string fishEscapedMessage = "It got away...";

    [Header("Fish tiers (optional)")]
    [Tooltip("Weighted rarity bands. EMPTY = the single-fish behaviour above. Seed " +
             "sensible defaults with Tools > Items > Seed Fish Tiers.")]
    [SerializeField] private FishTier[] fishTiers;

    private bool isRaised = false;
    private bool isFishing = false;
    private bool biteWindowOpen = false;
    private FishTier currentTier;   // picked at cast; null = legacy single fish
    private bool isTransitioning = false;
    private Coroutine fishingCoroutine;

    private float m_FishingSpotMultiplier = 1f;

    private InteractionAudioManager audioManager;
    private AudioSource fishingSource;

    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    public void SetFishingSpotMultiplier(float _multiplier)
    {
        m_FishingSpotMultiplier = Mathf.Max(0.1f, _multiplier);
    }

    private void Start()
    {
        // Ensure we start in lowered position
        transform.localPosition = raisedPosition + loweredPositionOffset;
        transform.localRotation = Quaternion.Euler(raisedRotation + loweredRotationOffset);

        // Its own 2D source: the rod is in the player's hands, not out in the world.
        // The profile's Mixer Group Output routes it when one is set.
        fishingSource = GetComponent<AudioSource>();
        if (fishingSource == null) fishingSource = gameObject.AddComponent<AudioSource>();
        fishingSource.playOnAwake = false;
        fishingSource.spatialBlend = 0f;

        if (itemIcon == null)
        {
            //Debug.LogError($"No icon assigned for {itemName} in FishingRodItem!");
        }
    }

    public void MoveToRaisedPosition()
    {
        if (isTransitioning) return;
        
       // Debug.Log("Moving fishing rod to raised position");
        isRaised = true;
        isTransitioning = true;
        StartCoroutine(TransitionPosition(true));
    }

    public void MoveToLoweredPosition()
    {
        // Stopping comes FIRST and unconditionally. The early-out below used to sit
        // above it, so lowering during the ~0.3-0.5s raise transition returned before
        // StopFishing - leaving the player fishing with the rod visually stowed, and
        // FishingRodItem.ForceToLowered had already set its own isRaised = false, so the
        // next slot press toggled the wrong way.
        StopFishing(abandonBite: true);

        if (isTransitioning) return;

        //Debug.Log("Moving fishing rod to lowered position");
        isRaised = false;
        isTransitioning = true;
        StartCoroutine(TransitionPosition(false));
    }

    public bool IsRaised()
    {
        return isRaised;
    }

    public bool IsTransitioning()
    {
        return isTransitioning;
    }

    private IEnumerator TransitionPosition(bool raising)
    {
        Vector3 targetPos = raising ? raisedPosition : raisedPosition + loweredPositionOffset;
        Vector3 targetRot = raising ? raisedRotation : raisedRotation + loweredRotationOffset;
        
        float elapsedTime = 0f;
        Vector3 startPos = transform.localPosition;
        Quaternion startRot = transform.localRotation;
        
        // Use different speeds for raising and lowering
        float currentSpeed = raising ? raiseSpeed : lowerSpeed;
        
        while (elapsedTime < 1f)
        {
            elapsedTime += Time.deltaTime * currentSpeed;
            transform.localPosition = Vector3.Lerp(startPos, targetPos, elapsedTime);
            transform.localRotation = Quaternion.Lerp(startRot, Quaternion.Euler(targetRot), elapsedTime);
            yield return null;
        }

        // Ensure we reach the exact target position/rotation
        transform.localPosition = targetPos;
        transform.localRotation = Quaternion.Euler(targetRot);
        
        isTransitioning = false;
       // Debug.Log($"Transition complete. IsRaised: {isRaised}");
    }

    public void StartFishing()
    {
        if (!isFishing && isRaised)
        {
            isFishing = true;
            currentTier = PickTier();
            if (TryGetFishingProfile(out var castProfile))
            {
                PlayFishingClip(castProfile.castSound, "cast");
            }
            fishingCoroutine = StartCoroutine(FishingProcess());
            ShowFishingPrompt(startFishingMessage);
        }
    }

    public void StopFishing(bool abandonBite = false)
    {
        if (!isFishing) return;

        // Releasing the button inside the bite window IS the catch. Anything that
        // stops fishing for another reason - lowering the rod, leaving the zone -
        // abandons the bite instead of landing it.
        bool landed = biteWindowOpen && !abandonBite;

        biteWindowOpen = false;
        isFishing = false;

        if (fishingCoroutine != null)
        {
            StopCoroutine(fishingCoroutine);
            fishingCoroutine = null;
        }

        if (landed)
        {
            LandFish();
            return;
        }

        ShowFishingPrompt(stopFishingMessage);

        // Hide the message after a short delay
        StartCoroutine(HidePromptAfterDelay(1.5f));
    }

    private IEnumerator HidePromptAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideInteractionPrompt();
    }

    private void ShowFishingPrompt(string message)
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.ShowFishingPrompt(message);
        }
    }

    private void HideInteractionPrompt()
    {
        if (EventManager.Instance != null)
        {
            EventManager.Instance.HideInteractionPrompt();
        }
    }

    private IEnumerator FishingProcess()
    {
        // Apply the spot multiplier to the fishing time
        float baseTime = Random.Range(minFishingTime, maxFishingTime);
        float adjustedFishingTime = baseTime * m_FishingSpotMultiplier;
        if (currentTier != null) adjustedFishingTime *= currentTier.waitTimeMultiplier;
        float elapsedTime = 0f;

        while (elapsedTime < adjustedFishingTime)
        {
            if (!isFishing)
            {
                ShowFishingPrompt(stopFishingMessage);
                StartCoroutine(HidePromptAfterDelay(1.5f));
                yield break;
            }
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // ---- The bite ------------------------------------------------------
        // Armed by authoring the bite clip (now on the manager's Fishing profile);
        // without one the fish lands as it always did, so an unauthored scene
        // keeps working.
        TryGetFishingProfile(out var fishingProfile);
        AudioClip biteClip = currentTier != null && currentTier.biteSound != null
            ? currentTier.biteSound
            : fishingProfile?.biteSound;
        float window = currentTier != null ? currentTier.catchWindowSeconds : catchWindowSeconds;

        if (biteClip != null)
        {
            biteWindowOpen = true;

            PlayFishingClip(biteClip, "bite");

            // Releasing inside this window stops the coroutine via StopFishing,
            // which lands the fish. Reaching the end means the window was missed.
            float held = 0f;
            while (held < window)
            {
                held += Time.deltaTime;
                yield return null;
            }

            biteWindowOpen = false;
            isFishing = false;
            fishingCoroutine = null;

            ShowFishingPrompt(fishEscapedMessage);
            StartCoroutine(HidePromptAfterDelay(1.5f));
            yield break;
        }

        LandFish();
    }

    /// <summary>Weighted tier roll. Null when no tiers are authored - the legacy path.</summary>
    private FishTier PickTier()
    {
        if (fishTiers == null || fishTiers.Length == 0) return null;

        float total = 0f;
        foreach (FishTier tier in fishTiers)
        {
            if (tier != null) total += Mathf.Max(0.01f, tier.weight);
        }

        float roll = Random.value * total;
        foreach (FishTier tier in fishTiers)
        {
            if (tier == null) continue;
            roll -= Mathf.Max(0.01f, tier.weight);
            if (roll <= 0f) return tier;
        }

        return fishTiers[fishTiers.Length - 1];
    }

    /// <summary>
    /// Fetches the "Fishing" profile, routing the rod's source through its mixer
    /// group when one is set. A missing profile logs the same red Ctrl+J row as any
    /// other unresolved interaction key.
    /// </summary>
    private bool TryGetFishingProfile(out InteractionAudioManager.InteractionSoundProfile profile)
    {
        profile = null;
        if (audioManager == null) audioManager = FindFirstObjectByType<InteractionAudioManager>();
        if (audioManager == null || !audioManager.TryGetProfile(AudioProfileKey, out profile)) return false;

        if (profile.mixerGroupOutput != null && fishingSource != null)
        {
            fishingSource.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        return true;
    }

    private void PlayFishingClip(AudioClip clip, string label)
    {
        if (clip == null || fishingSource == null) return;

        AudioEventLog.Record(AudioEventKind.Play, "Fishing", $"{label} '{clip.name}'");
        fishingSource.PlayOneShot(clip, 1f);
    }

    /// <summary>
    /// Puts the fish in the inventory (or says why not). Shared by the automatic
    /// landing and by a release inside the bite window.
    /// </summary>
    private void LandFish()
    {
        // Add fish to inventory
        if (Inventory.Instance != null)
        {
            // Check for room *before* building the fish. InitializeFish awards XP,
            // credits every active catch-N-fish objective and fires the first-fish
            // tracker, all of which used to stick even when the fish was immediately
            // destroyed for want of a slot - so fishing with a full inventory farmed
            // objectives without ever landing a fish.
            if (!Inventory.Instance.HasEmptySlot())
            {
                ShowFishingPrompt(inventoryFullMessage);
            }
            else
            {
                string caughtName = currentTier != null ? currentTier.tierName : fishName;
                string caughtDescription = currentTier != null ? currentTier.description : fishDescription;
                Sprite caughtIcon = currentTier != null && currentTier.icon != null ? currentTier.icon : fishIcon;

                // InitializeFish refuses a null icon by design (the hotbar needs one),
                // but it refuses SILENTLY - and the blank fish still went into the
                // inventory as a nameless, valueless item. Void the catch loudly instead.
                if (caughtIcon == null)
                {
                    Debug.LogWarning("[FishingRod] No icon for the caught fish (the tier's " +
                                     "and the rod's Fish Icon are both empty) - the catch is " +
                                     "voided rather than adding a blank item.", this);
                    ShowFishingPrompt(fishEscapedMessage);
                    isFishing = false;
                    StartCoroutine(LandedPromptTimeout());
                    return;
                }
                float caughtNutrition = currentTier != null ? currentTier.nutritionValue : fishNutritionValue;

                GameObject fishObj = new GameObject("Fish");
                Fish fishItem = fishObj.AddComponent<Fish>();
                fishItem.InitializeFish(caughtName, caughtDescription, caughtIcon, caughtNutrition);

                if (!Inventory.Instance.AddItem(fishItem))
                {
                    Destroy(fishObj);
                    ShowFishingPrompt(inventoryFullMessage);
                }
                else
                {
                    // Reward sound only once the fish is actually the player's.
                    TryGetFishingProfile(out var landedProfile);
                    AudioClip fanfare = currentTier != null && currentTier.catchSound != null
                        ? currentTier.catchSound
                        : landedProfile?.caughtSound;
                    PlayFishingClip(fanfare, "caught");
                    ShowFishingPrompt(fishCaughtMessage);
                }
            }
        }

        isFishing = false;
        StartCoroutine(LandedPromptTimeout());
    }

    private IEnumerator LandedPromptTimeout()
    {
        yield return new WaitForSeconds(2f);
        if (!isFishing) // Only hide if we haven't started fishing again
        {
            HideInteractionPrompt();
        }
    }

    public bool IsFishing()
    {
        return isFishing;
    }
} 