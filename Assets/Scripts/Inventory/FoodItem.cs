using UnityEngine;

public class FoodItem : Item
{
    [SerializeField] protected float nutritionValue = 15f;

    /// <summary>CampfireCooking reads this so cooked value scales with the raw fish.</summary>
    public float GetNutritionValue() => nutritionValue;

    /// <summary>
    /// Sets up a food item built at runtime (cooked fish). Deliberately NOT
    /// Fish.InitializeFish: that one awards XP and credits the catch objectives, and
    /// cooking an already-caught fish must not pay for it twice.
    /// </summary>
    public void InitializeFood(string name, string description, Sprite icon, float nutrition)
    {
        itemName = name;
        itemDescription = description;
        itemIcon = icon;
        nutritionValue = nutrition;
    }

    public override void UseItem()
    {
        //Debug.Log($"Using food item: {itemName}");
        
        PlayerStats playerStats = PlayerStats.Instance;
        if (playerStats != null)
        {
            playerStats.AddHunger(nutritionValue);
            
            // "Food Can" has a bespoke UIAudioManager one-shot; every other food was
            // silent. Fall back to the player's generic eating sound rather than adding
            // it everywhere, which would double up on the items that already have one.
            if (itemName == "Food Can")
            {
                if (UIAudioManager.Instance != null)
                {
                    UIAudioManager.Instance.PlayFoodCanEatenSound();
                }
            }
            else if (playerStats.AudioManager != null)
            {
                playerStats.AudioManager.PlayEatingSound();
            }
            
            if (Inventory.Instance != null)
            {
                int slot = Inventory.Instance.GetSlotForItem(this);
                //Debug.Log($"Found item in slot: {slot}");
                if (slot != -1)
                {
                    //Debug.Log("Removing food item from inventory");
                    Inventory.Instance.RemoveItem(slot);
                    Destroy(gameObject);
                }
            }
            //Debug.LogError("Could not find Inventory instance!");
        }
        //Debug.LogError("Could not find PlayerStats component!");
    }
} 