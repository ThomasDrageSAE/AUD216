using System.Collections.Generic;
using UnityEngine;

/*
 * GrenadeController.cs
 *
 * Purpose: The grenade in the player's hand - how many are left, and what a left click
 *          does with one.
 * Used by: Player/FirstPersonCharacter/WeaponParent/Grenade in the scene, selected through
 *          GrenadeItem like any other WeaponItem. FirstPersonController calls Fire() every
 *          frame the fire button is held, exactly as it does for the guns.
 *
 * Key Features:
 * - Fire() is a throw, not a shot: it is overridden whole rather than extending the base,
 *   because none of the base's shot - the raycast, the recoil, the muzzle flash, the shoot
 *   sound - applies to something that leaves the hand
 * - The thrower's own colliders are collected once and handed to each grenade, so a throw
 *   cannot bounce off the player making it
 * - The count is spent here and the item is consumed when it reaches zero, the pattern
 *   FoodItem uses
 *
 * Audio: exactly one assessed sound is played from this class - PlayThrowSound, on the
 * weapon's own profile. The blast is the grenade's to play, from where it lands.
 *
 * Dependencies:
 * - WeaponController (base), Grenade (the projectile), GrenadeItem (the inventory entry)
 * - WeaponAudioManager.PlayThrowSound
 */
public class GrenadeController : WeaponController
{
    /// <summary>
    /// Nothing to sight down, and this class inherits the base Update that reads the aim
    /// key - without this a right click would swing the can into a rifle pose.
    /// </summary>
    protected override bool CanAimDownSights => false;

    /// <summary>
    /// True, so the current-weapon panel prints a count beside the icon - but the number is
    /// grenades in hand, not a magazine. CurrentAmmo and MaxAmmo below redirect the panel
    /// onto this weapon's own count; the base's magazine is private, untouched and
    /// irrelevant here. There is still no reload: the base's TryReload returns immediately
    /// on a full magazine, and Fire below never spends one.
    /// </summary>
    public override bool UsesMagazine => true;

    /// <summary>Grenades in hand, for the HUD panel. Not a magazine.</summary>
    public override int CurrentAmmo => carriedGrenades;

    /// <summary>
    /// The most the hand can hold, for the HUD panel's "n / max" readout. Set by the pickup
    /// through AddGrenades, because the cap is authored on GrenadeItem - the thing found in
    /// the world - rather than on the weapon.
    /// </summary>
    public override int MaxAmmo => maxHeld > 0 ? maxHeld : carriedGrenades;

    [Header("Grenade")]
    [Tooltip("The projectile spawned on each throw - Assets/Inbox/Prefabs/Grenade.prefab")]
    [SerializeField] private Grenade grenadePrefab;

    [Tooltip("Metres per second along the camera's forward. The camera, not the weapon: " +
             "the throw follows where the player is looking, the way the guns trace their " +
             "shot down the centre line rather than out of the barrel")]
    [Min(0f)] [SerializeField] private float throwSpeed = 14f;

    [Tooltip("Metres per second added straight up, so the throw arcs instead of skimming " +
             "the floor when the player is looking level")]
    [Min(0f)] [SerializeField] private float throwLift = 2f;

    [Tooltip("Grenades in hand right now. Starts empty on purpose - the pickup fills it, " +
             "and a weapon nobody has collected should have nothing to throw")]
    [Min(0)] [SerializeField] private int carriedGrenades = 0;

    /// <summary>How many are left. Read by GrenadeItem to decide whether a pickup merges.</summary>
    public int CarriedGrenades => carriedGrenades;

    /// <summary>
    /// The cap the last pickup declared, remembered only so MaxAmmo can answer the HUD.
    /// Not serialized: it is GrenadeItem's number, and duplicating it in the inspector
    /// would give the same rule two authorities.
    /// </summary>
    private int maxHeld;

    /// <summary>
    /// The camera the throw is aimed along. Resolved the same way the base resolves its
    /// own aim camera, which is private to it.
    /// </summary>
    private Camera throwCamera;

    /// <summary>
    /// The thrower's colliders, collected once. A grenade spawns inside the player's own
    /// capsule, so every one of these has to be ignored or the throw rebounds off the
    /// person making it.
    /// </summary>
    private Collider[] throwerColliders;

    protected override void Start()
    {
        base.Start();

        throwCamera = GetComponentInParent<Camera>();
        if (throwCamera == null) throwCamera = Camera.main;

        CollectThrowerColliders();

        // The base's R reload is already a dead key here and needs no override: base.Start
        // fills its private magazine to maxAmmo, this class never spends it, and TryReload
        // returns immediately on a full magazine. Nothing about the grenade reloads, so
        // that is the behaviour wanted - it is recorded here because it is load-bearing
        // and invisible.
    }

    /// <summary>
    /// Walks up to the player root - whatever carries the CharacterController - and takes
    /// every collider under it, the CharacterController included (it is one).
    /// </summary>
    private void CollectThrowerColliders()
    {
        CharacterController character = GetComponentInParent<CharacterController>();
        Transform root = character != null ? character.transform : transform.root;

        // Inactive included: a holstered weapon's collider is still the thrower's, and a
        // grenade that clipped one would stop dead in mid-air.
        List<Collider> colliders = new List<Collider>(root.GetComponentsInChildren<Collider>(true));
        throwerColliders = colliders.ToArray();
    }

    /// <summary>
    /// Adds picked-up grenades to the hand, capped. Returns how many were actually taken,
    /// so a pickup can tell whether it was worth consuming.
    /// </summary>
    public int AddGrenades(int amount, int maxHeld)
    {
        // Kept so MaxAmmo can answer the HUD panel. The cap lives on GrenadeItem, so the
        // weapon only learns it when something is actually picked up.
        this.maxHeld = Mathf.Max(0, maxHeld);

        int before = carriedGrenades;
        carriedGrenades = Mathf.Clamp(carriedGrenades + Mathf.Max(0, amount), 0, this.maxHeld);

        if (carriedGrenades != before) RaiseHudStateChanged();
        return carriedGrenades - before;
    }

    /// <summary>
    /// Throws one. Overridden whole rather than calling base: the base Fire is a hitscan
    /// shot - it spends a magazine this weapon does not have, raycasts damage down the
    /// camera, flashes a muzzle and plays the SHOOT sound, which for a grenade would be a
    /// third assessed audio slot going off at the player's ear instead of at the blast.
    /// </summary>
    public override void Fire()
    {
        if (!isWeaponRaised) return;

        // FirstPersonController calls this every frame the button is held, so the press is
        // the gate - one grenade per click, never a stream while the finger is down.
        if (!Input.GetMouseButtonDown(0)) return;
        if (Time.time < nextTimeToFire) return;
        if (carriedGrenades <= 0) return;

        ThrowOne();
    }

    /// <summary>
    /// Everything a throw does, with none of the deciding. Split out of Fire so the throw
    /// can be driven without a mouse: Fire's gate is four lines of input state that only a
    /// human at the keyboard can satisfy, and a feature nobody can exercise from a script
    /// is a feature nobody checks. Callers own the gate; this owns the consequences.
    /// </summary>
    public void ThrowOne()
    {
        if (carriedGrenades <= 0) return;

        if (grenadePrefab == null)
        {
            Debug.LogError($"[{name}] No grenadePrefab assigned; cannot throw.", this);
            return;
        }

        Transform spawn = firePoint != null ? firePoint : transform;
        Transform aim = throwCamera != null ? throwCamera.transform : spawn;

        Grenade grenade = Instantiate(grenadePrefab, spawn.position, spawn.rotation);
        grenade.Launch(aim.forward * throwSpeed + Vector3.up * throwLift,
                       throwerColliders,
                       weaponAudioManager,
                       WeaponAudioKey);

        if (weaponAudioManager != null)
        {
            weaponAudioManager.PlayThrowSound(WeaponAudioKey);
        }

        carriedGrenades--;
        nextTimeToFire = Time.time + fireRate;
        RaiseHudStateChanged();

        // No ApplyRecoil: the only shake this weapon authors is the blast's, and a kick at
        // the throw would read as the grenade going off in the player's hand.

        if (carriedGrenades > 0) return;

        // Out of grenades: the hand comes down and the slot empties, the same way eating
        // the last of a food item empties its slot (FoodItem.UseItem).
        if (isWeaponRaised) ToggleWeaponPosition();
        ConsumeOwningItem();
    }

    /// <summary>
    /// Finds the inventory entry that owns this controller and has it remove itself. The
    /// item knows the controller, not the other way round, so this is the same lookup
    /// CurrentWeaponHUD uses to find a weapon's icon.
    /// </summary>
    private void ConsumeOwningItem()
    {
        Inventory inventory = Inventory.Instance;
        if (inventory == null) return;

        foreach (Item item in inventory.GetAllItems())
        {
            if (item is GrenadeItem grenadeItem && grenadeItem.GetWeaponController() == this)
            {
                grenadeItem.ConsumeFromInventory();
                return;
            }
        }
    }
}
