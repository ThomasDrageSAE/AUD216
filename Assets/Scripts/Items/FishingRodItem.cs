using UnityEngine;

public class FishingRodItem : Item
{
    [SerializeField] private GameObject fishingRodPrefab;
    [SerializeField] private Transform weaponParent;
    private FishingRod activeFishingRod;
    private bool isRaised = false;
    public bool IsRaised => isRaised;
    
    protected override void Awake()
    {
        base.Awake();  // Call the base class Awake first
        
        // Ensure the item properties are set
        if (string.IsNullOrEmpty(itemName))
        {
            itemName = "Fishing Rod";
        }
        if (itemIcon == null)
        {
            Debug.LogError($"No icon assigned for {itemName} in FishingRodItem!");
        }
        if (string.IsNullOrEmpty(itemDescription))
        {
            itemDescription = "Used for catching fish";
        }
    }
    
    public override void UseItem()
    {
        // First time use - instantiate the fishing rod
        if (activeFishingRod == null && weaponParent != null)
        {
            GameObject instance = Instantiate(fishingRodPrefab, weaponParent);
            activeFishingRod = instance.GetComponent<FishingRod>();
            if (activeFishingRod == null)
            {
                //Debug.LogError("FishingRod component not found on instantiated prefab");
                return;
            }
        }
        else if (weaponParent == null)
        {
            //Debug.LogError("WeaponParent reference not set in inspector for FishingRodItem");
            return;
        }

        // Don't allow toggling while transitioning
        if (activeFishingRod.IsTransitioning())
        {
            //Debug.Log("Cannot toggle fishing rod while it's transitioning");
            return;
        }

        // Toggle raised state
        isRaised = !isRaised;
        //Debug.Log($"Toggling fishing rod. IsRaised: {isRaised}");
        
        if (isRaised)
        {
            LowerOtherItems();
            activeFishingRod?.MoveToRaisedPosition();
        }
        else
        {
            activeFishingRod?.MoveToLoweredPosition();
        }
    }

    private void LowerOtherItems()
    {
        if (weaponParent == null) return;

        // Lower weapons
        foreach (WeaponItem weapon in weaponParent.GetComponentsInChildren<WeaponItem>())
        {
            var controller = weapon.GetWeaponController();
            if (controller != null && controller.IsWeaponRaised())
            {
                controller.ToggleWeaponPosition();
            }
        }

        // Lower other fishing rods
        foreach (FishingRod rod in weaponParent.GetComponentsInChildren<FishingRod>())
        {
            if (rod != activeFishingRod)
            {
                rod.MoveToLoweredPosition();
            }
        }
    }

    protected override void OnPickup()
    {
        base.OnPickup();
        isRaised = false;
        activeFishingRod?.MoveToLoweredPosition();

        // Add this line to track fishing rod pickup
        if (FirstTimeInteractionTracker.Instance != null)
        {
            FirstTimeInteractionTracker.Instance.OnFishingRodPickup();
        }
    }

    public void ForceToLowered()
    {
        isRaised = false;
        activeFishingRod?.MoveToLoweredPosition();
    }

    public FishingRod GetFishingRodController()
    {
        return activeFishingRod;
    }
} 