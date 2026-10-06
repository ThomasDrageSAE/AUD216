using UnityEngine;

public class WeaponItem : Item
{
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private string weaponName;
    [Tooltip("Picture for the bottom-right current-weapon panel. Tools/Weapons/Create " +
             "Current Weapon Panel seeds it with the Synty *_Side view of the hotbar " +
             "icon; any sprite works. Empty = the panel shows the hotbar icon")]
    [SerializeField] private Sprite hudIcon;

    protected override void OnPickup()
    {
        base.OnPickup();  // Call base class OnPickup first

        // Check if this is the axe and track its pickup
        if (weaponName == "Axe" && FirstTimeInteractionTracker.Instance != null)
        {
            FirstTimeInteractionTracker.Instance.OnAxePickup();
        }
    }

    public WeaponController GetWeaponController()
    {
        return weaponController;
    }

    /// <summary>
    /// Picture for the current-weapon panel: the panel icon when one is authored,
    /// otherwise the same icon the hotbar shows, so a new weapon is never a blank panel.
    /// </summary>
    public Sprite GetHudIcon()
    {
        return hudIcon != null ? hudIcon : GetItemIcon();
    }

    public override string GetItemName()
    {
        return weaponName;
    }

    public void ForceToLowered()
    {
        if (weaponController != null && weaponController.IsWeaponRaised())
        {
            weaponController.ToggleWeaponPosition();
        }
    }
} 