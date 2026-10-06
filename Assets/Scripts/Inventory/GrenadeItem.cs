using UnityEngine;

/*
 * GrenadeItem.cs
 *
 * Purpose: The grenade's inventory entry - one slot, a count, and the only stacking pickup
 *          in the game.
 * Used by: Game Management/Pickups/Pickup - Grenade (and its siblings), built by
 *          Tools/Weapons/Build Grenade. Selected from the hotbar like any other WeaponItem.
 *
 * Key Features:
 * - The first grenade found is an ordinary pickup: it takes a slot and fills the hand
 * - Every one after that MERGES into that slot through Item.TryMergeIntoInventory, so a
 *   player who finds four grenades still has one grenade slot and four grenades
 * - A player already carrying the maximum walks straight through a pickup and leaves it on
 *   the ground for later, rather than wasting it or filling a second slot
 * - When the last grenade is thrown, the entry removes itself from the inventory and is
 *   destroyed - the FoodItem consumption pattern
 *
 * The count itself lives on GrenadeController, not here: the controller is what spends it
 * on a throw, and one authority for it is the point.
 *
 * Dependencies:
 * - WeaponItem (base), Item.TryMergeIntoInventory (the merge hook), Inventory
 * - GrenadeController (holds the count), UIAudioManager (the shared pickup sound)
 */
public class GrenadeItem : WeaponItem
{
    [Header("Grenade")]
    [Tooltip("Most grenades the player may carry at once. A pickup found while already at " +
             "this count is left where it is")]
    [Min(1)] [SerializeField] private int maxCarried = 3;

    [Tooltip("Grenades this pickup is worth, capped by Max Carried - so the first one " +
             "found fills the hand, and a later one tops it back up")]
    [Min(1)] [SerializeField] private int grenadesPerPickup = 3;

    /// <summary>
    /// Latched before this pickup is destroyed by a merge. The player carries both a
    /// CharacterController and a CapsuleCollider, so one walk-through delivers
    /// OnTriggerEnter twice, and Destroy does not take effect until the end of the frame -
    /// without this the second callback merges the same pickup again.
    /// </summary>
    private bool merged;

    /// <summary>
    /// The "hands full" message says itself once. The pickup deliberately stays collectable,
    /// so the player walking in and out of it would otherwise fill the console.
    /// </summary>
    private bool loggedFull;

    /// <summary>
    /// One pickup pays out once, ever. Dropping the item (Shift+slot) puts this same
    /// GameObject back in the world as an ordinary pickup, so without the latch a player
    /// holding one grenade could drop it and walk back onto it for a free refill.
    /// </summary>
    private bool granted;

    private GrenadeController Controller => GetWeaponController() as GrenadeController;

    protected override void OnPickup()
    {
        base.OnPickup();

        GrenadeController controller = Controller;
        if (controller == null)
        {
            Debug.LogError($"[{name}] Weapon Controller is not a GrenadeController; " +
                           "the grenades cannot be counted.", this);
            return;
        }

        if (granted) return;

        granted = true;
        controller.AddGrenades(grenadesPerPickup, maxCarried);
    }

    /// <summary>
    /// The merge, run before the inventory is asked for a slot.
    /// </summary>
    /// <returns>
    /// False only when the player is not carrying grenades yet, which lets the ordinary
    /// pickup path take a slot. True in both of the other cases, because both mean "this
    /// must not become a second grenade slot": either it folded into the one the player
    /// already has, or their hands are full and it stays on the ground.
    /// </returns>
    protected override bool TryMergeIntoInventory(Inventory inventory)
    {
        if (merged) return true;
        if (inventory == null) return false;

        GrenadeItem carried = FindCarriedGrenade(inventory);
        if (carried == null) return false;

        GrenadeController controller = carried.Controller;
        if (controller == null) return false;

        if (controller.CarriedGrenades >= carried.maxCarried)
        {
            // Full hands walk through it. Nothing is marked collected, so the renderers and
            // the trigger stay on and the pickup is still sitting there once the player has
            // thrown a couple - which is a better answer than silently binning a grenade.
            if (!loggedFull)
            {
                loggedFull = true;
                Debug.Log($"[{name}] Already carrying {controller.CarriedGrenades} grenades " +
                          $"(max {carried.maxCarried}); leaving this one on the ground.", this);
            }

            return true;
        }

        merged = true;
        granted = true;
        controller.AddGrenades(grenadesPerPickup, carried.maxCarried);

        // The same pickup sound the ordinary path plays, because to the player this WAS an
        // ordinary pickup - it just did not need a slot.
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayPickupSound();
        }

        Destroy(gameObject);
        return true;
    }

    /// <summary>The grenade entry already in the inventory, if there is one.</summary>
    private GrenadeItem FindCarriedGrenade(Inventory inventory)
    {
        foreach (Item item in inventory.GetAllItems())
        {
            if (item is GrenadeItem grenadeItem && grenadeItem != this) return grenadeItem;
        }

        return null;
    }

    /// <summary>
    /// Called by GrenadeController once the last grenade has been thrown: the slot empties
    /// and the entry goes, exactly as a food item does when it is eaten.
    /// </summary>
    public void ConsumeFromInventory()
    {
        Inventory inventory = Inventory.Instance;
        if (inventory != null)
        {
            int slot = inventory.GetSlotForItem(this);
            if (slot != -1) inventory.RemoveItem(slot);
        }

        Destroy(gameObject);
    }
}
