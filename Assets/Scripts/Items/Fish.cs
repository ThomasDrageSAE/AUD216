using UnityEngine;
using System.Collections.Generic;

public class Fish : FoodItem
{
    private static Dictionary<string, int> fishCaughtPerObjective = new Dictionary<string, int>();
    [SerializeField] private float xpReward = 15f;
    [SerializeField] private float healthRestoreAmount = 10f;

    private static readonly Dictionary<string, int> fishRequirements = new Dictionary<string, int>
    {
        { ObjectiveConstants.CATCH_TWO_FISH, 2 },
        { ObjectiveConstants.CATCH_THREE_FISH, 3 },
        { ObjectiveConstants.CATCH_FOUR_FISH, 4 },
        { ObjectiveConstants.CATCH_FIVE_FISH, 5 }
    };

    public void InitializeFish(string name, string description, Sprite icon, float nutrition)
    {
        if (string.IsNullOrEmpty(name) || icon == null)
        {
            return;
        }

        itemName = name;
        itemDescription = description;
        itemIcon = icon;
        nutritionValue = nutrition;

        // Check active objectives and increment their counters
        CheckFishObjectives();

        // Award XP when fish is caught
        PlayerStats playerStats = PlayerStats.Instance;
        if (playerStats != null)
        {
            playerStats.AddXP(xpReward);
        }

        // The tracker dedups this itself; the old per-instance "isFirstFish" flag meant
        // "first fish for this Fish object", which is every fish.
        if (FirstTimeInteractionTracker.Instance != null)
        {
            FirstTimeInteractionTracker.Instance.OnFirstFishCaught();
        }
    }

    private void CheckFishObjectives()
    {
        if (ObjectiveManager.Instance != null)
        {
            foreach (var requirement in fishRequirements)
            {
                CheckAndIncrementObjective(requirement.Key, requirement.Value);
            }
        }
    }

    private void CheckAndIncrementObjective(string objectiveId, int requiredFish)
    {
        // The only caller (CheckFishObjectives) already null-checks Instance and returns
        // silently, so this guard is unreachable today; it is here so the helper is safe
        // on its own. Silent for the same reason the caller is.
        if (ObjectiveManager.Instance == null) return;

        // Only process if the objective is currently active
        if (ObjectiveManager.Instance.IsObjectiveActive(objectiveId))
        {
            // Initialize counter if needed
            if (!fishCaughtPerObjective.ContainsKey(objectiveId))
            {
                fishCaughtPerObjective[objectiveId] = 0;
            }

            // Increment fish count for this objective
            fishCaughtPerObjective[objectiveId]++;

            // Complete objective if target reached
            if (fishCaughtPerObjective[objectiveId] >= requiredFish)
            {
                ObjectiveManager.Instance.CompleteObjective(objectiveId);
            }
        }
    }

    // Reset method for when objectives become active or day changes
    public static void ResetObjectiveFishCount(string objectiveId)
    {
        fishCaughtPerObjective.Remove(objectiveId);
    }

    // Reset all counters (useful for day changes)
    public static void ResetAllFishCounts()
    {
        fishCaughtPerObjective.Clear();
    }

    public override void UseItem()
    {
        PlayerStats playerStats = PlayerStats.Instance;
        if (playerStats != null)
        {
            playerStats.AddHunger(nutritionValue);
            playerStats.AddHealth(healthRestoreAmount);
            
            if (UIAudioManager.Instance != null)
            {
                UIAudioManager.Instance.PlayFishEatenSound();
            }
            
            if (Inventory.Instance != null)
            {
                int slot = Inventory.Instance.GetSlotForItem(this);
                if (slot != -1)
                {
                    Inventory.Instance.RemoveItem(slot);
                    Destroy(gameObject);
                }
            }
        }
    }
} 