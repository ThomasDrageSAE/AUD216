using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

public class FishingZone : MonoBehaviour
{
    [Header("Fishing Quality Settings")]
    [Tooltip("Multiplier for fishing time (0.5 = half time, 2 = double time)")]
    [SerializeField, Range(0.1f, 2f)] private float fishingTimeMultiplier = 1f;
    
    [Header("UI Settings")]
    [SerializeField] private string objectDisplayName = "Fishing Spot";
    [SerializeField] private string inputKey = "LMB";
    [SerializeField] private string actionMessage = "hold to fish";
    [SerializeField] private InteractionPromptUI promptUI;

    private bool playerInRange = false;
    private FishingRod activeFishingRod = null;
    private FirstPersonController playerController = null;
    private bool lastRodRaised = false;

    // Which zone currently owns the rod. Overlapping trigger volumes used to fight each
    // other: leaving one cancelled the other's cast and reset its multiplier. A single
    // owner rather than a stack - stepping out of the inner of two nested zones leaves
    // the outer one's multiplier unapplied until re-entry, which is the rarer wrong.
    // Safe across scene reloads: a destroyed reference compares false against `this`.
    private static FishingZone activeZone;

    private void Start()
    {
        if (promptUI == null)
        {
            promptUI = FindFirstObjectByType<InteractionPromptUI>();
            if (promptUI == null)
            {
                Debug.LogError("InteractionPromptUI not found. Please assign it in the inspector.");
            }
        }
    }

    private void Update()
    {
        if (playerInRange)
        {
            // Check for fishing rod state changes
            if (playerController != null)
            {
                foreach (var item in playerController.GetInventory().GetAllItems())
                {
                    if (item is FishingRodItem fishingRodItem)
                    {
                        FishingRod newRod = fishingRodItem.GetFishingRodController();
                        if (newRod != activeFishingRod)
                        {
                            activeFishingRod = newRod;

                            // The rod can be equipped AFTER entering the zone, in which
                            // case OnTriggerEnter never saw it and this spot's quality
                            // tuning was silently dropped on first use.
                            if (activeFishingRod != null)
                            {
                                activeFishingRod.SetFishingSpotMultiplier(fishingTimeMultiplier);
                                activeZone = this;
                            }

                            UpdatePrompt();
                        }

                        // The prompt has to follow raise/lower too. Refreshing only when
                        // the rod REFERENCE changes meant it updated once and then never
                        // again, so it never reflected the rod being put away.
                        bool rodRaised = activeFishingRod != null && activeFishingRod.IsRaised();
                        if (rodRaised != lastRodRaised)
                        {
                            lastRodRaised = rodRaised;
                            UpdatePrompt();
                        }
                        break;
                    }
                }
            }

            if (activeFishingRod != null && activeFishingRod.IsRaised())
            {
                if (Input.GetMouseButtonDown(0))
                {
                    if (!activeFishingRod.IsFishing())
                    {
                        activeFishingRod.StartFishing();
                    }
                }
                else if (Input.GetMouseButtonUp(0))
                {
                    if (activeFishingRod.IsFishing())
                    {
                        activeFishingRod.StopFishing();
                    }
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            playerController = other.GetComponent<FirstPersonController>();
            if (playerController != null)
            {
                foreach (var item in playerController.GetInventory().GetAllItems())
                {
                    if (item is FishingRodItem fishingRodItem)
                    {
                        FishingRod rod = fishingRodItem.GetFishingRodController();
                        if (rod != null)
                        {
                            // Apply the fishing spot's quality settings to the rod
                            rod.SetFishingSpotMultiplier(fishingTimeMultiplier);
                            activeFishingRod = rod;
                            activeZone = this;
                        }
                        break;
                    }
                }
            }
            UpdatePrompt();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Only tear the rod down if THIS zone is the one that owns it.
            if (activeZone == this)
            {
                if (activeFishingRod != null)
                {
                    if (activeFishingRod.IsFishing())
                    {
                        // Walking out of the zone is not a catch, whatever the bite
                        // window happens to be doing.
                        activeFishingRod.StopFishing(abandonBite: true);
                    }
                    // Reset the rod's multiplier when leaving the zone
                    activeFishingRod.SetFishingSpotMultiplier(1f);
                }

                activeZone = null;
            }

            playerInRange = false;
            activeFishingRod = null;
            playerController = null;
            lastRodRaised = false;
            
            if (promptUI != null)
            {
                promptUI.HidePrompt();
            }
        }
    }

    private void UpdatePrompt()
    {
        if (!playerInRange || promptUI == null)
        {
            return;
        }

        if (activeFishingRod != null && activeFishingRod.IsRaised())
        {
            promptUI.ShowPrompt(objectDisplayName, inputKey, actionMessage);
        }
        else
        {
            promptUI.HidePrompt();
        }
    }
} 