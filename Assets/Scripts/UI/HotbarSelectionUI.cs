using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/*
 * HotbarSelectionUI.cs
 *
 * Purpose: Puts the Synty yellow border around the hotbar slot holding whatever is in
 *          the player's hands, and around nothing when the hands are empty.
 * Used by: HUD_Hotbar_Minimal, alongside ItemInventoryUI (added by
 *          Tools/HUD/Add Hotbar Selection).
 *
 * The border is not new art. Every slot's Btn_HotbarItem_0N/Item already carries a
 * Selected child - SPR_HUD_Frame_Border_Grunge_Med tinted the demo's yellow - shipped
 * inactive by Synty and, until this script, referenced by nothing. Its sibling
 * Highlighted is the mouse-over fill and is deliberately left alone: hovering a slot is
 * not holding its item.
 *
 * "Equipped" means IN THE HANDS: a weapon the player has drawn (WeaponController
 * .IsWeaponRaised) or a raised fishing rod. Food and medicine are consumables - using
 * one eats it on the spot and nothing is ever held - so a consumable's slot never
 * lights. Exactly one slot is lit at a time, because only one thing can be in the hands.
 *
 * Two signals, because the two item kinds report differently:
 *   - Weapons announce themselves. WeaponController raises the static OnHudStateChanged
 *     on every raise and lower, and this listens the way CurrentWeaponHUD does.
 *   - The rod does not. FishingRodItem.UseItem only flips a private bool, so the rod is
 *     polled in Update - the one thing here that has to be asked.
 * Both funnel into ApplySelection, which compares before it writes: SetActive on a uGUI
 * object dirties the canvas, and these canvases already rebuild every frame at idle.
 *
 * No new serialized wiring. The slots come from ItemInventoryUI and the frame is found
 * by name underneath them, so the Synty prefab structure is the only contract - one less
 * set of references for a scene merge to lose.
 */
public class HotbarSelectionUI : MonoBehaviour
{
    /// <summary>The Synty child that draws the yellow border, one per slot.</summary>
    private const string SelectedChildName = "Selected";


    /// <summary>Nothing in the hands - no slot lit.</summary>
    private const int NoSlot = -1;

    private GameObject[] selectedFrames;
    private int selectedSlot = NoSlot;
    private bool wasRodRaised;

    private void OnEnable()
    {
        WeaponController.OnHudStateChanged += OnWeaponHudStateChanged;
    }

    private void OnDisable()
    {
        // The event is static and this is a single-scene game: a handler left subscribed
        // outlives the reload that destroyed its component and throws on the next raise.
        WeaponController.OnHudStateChanged -= OnWeaponHudStateChanged;
    }

    private void Start()
    {
        ItemInventoryUI hotbar = GetComponent<ItemInventoryUI>();
        if (hotbar == null || hotbar.ItemSlots == null)
        {
            Debug.LogError("HotbarSelectionUI: no ItemInventoryUI on this object - " +
                           "the hotbar slots are read from it. Disabling.", this);
            enabled = false;
            return;
        }

        IReadOnlyList<Image> slots = hotbar.ItemSlots;
        selectedFrames = new GameObject[slots.Count];

        // One warning for the lot, not one per slot: a hotbar built without the Synty
        // children would otherwise print five lines saying the same thing.
        List<int> missing = null;

        for (int i = 0; i < slots.Count; i++)
        {
            // The serialized slot is the Icon image; the frame is its sibling under Item.
            Transform icon = slots[i] != null ? slots[i].transform : null;
            Transform frame = icon != null && icon.parent != null
                ? icon.parent.Find(SelectedChildName)
                : null;

            if (frame == null)
            {
                (missing ??= new List<int>()).Add(i);
                continue;
            }

            selectedFrames[i] = frame.gameObject;

            // The BUTTON's Animator, not the frame. Every clip in Synty's
            // AC_Button_HotBar_Item_Minimal animates "Item/Selected . m_IsActive", so the
            // Animator owns that GameObject outright and rewrites it every frame - a
            // SetActive from here is overwritten before it is ever drawn. Measured
            // 2026-09-06: the border could be switched on by hand and was gone next frame.
            // The controller exposes Normal and Selected triggers for exactly this, so the
            // Animator is driven rather than fought.
            // Take the frame off the Animator entirely, rather than driving it.
            //
            // Synty's button uses Selectable.Transition.Animation, and EVERY clip in
            // AC_Button_HotBar_Item_Minimal animates "Item/Selected . m_IsActive". So the
            // Animator owns that GameObject: a SetActive from here is overwritten on its next
            // update, and driving the controller's own Selected trigger does not hold either,
            // because Selectable re-fires Normal from DoStateTransition whenever it
            // re-evaluates. Both were measured failing on 2026-09-06.
            //
            // Switching the transition to None and disabling the Animator hands the four child
            // objects to this script, which is the only way the border can persist. What is
            // lost is the button's hover/press animation - and this hotbar is never moused: the
            // game locks the cursor, and the slots are driven by the number keys. Restore it by
            // deleting these four lines; the border stops working at the same moment.
            Transform button = frame.parent != null ? frame.parent.parent : null;
            if (button != null)
            {
                Selectable selectable = button.GetComponent<Selectable>();
                if (selectable != null) selectable.transition = Selectable.Transition.None;

                Animator animator = button.GetComponent<Animator>();
                if (animator != null) animator.enabled = false;
            }

            // Only meaningful for a slot with no Animator, where this script does own the
            // object; harmless where the Animator will take it over on its first update.
            if (frame.gameObject.activeSelf) frame.gameObject.SetActive(false);
        }

        if (missing != null)
        {
            Debug.LogWarning($"HotbarSelectionUI: no '{SelectedChildName}' child under the Item " +
                             $"of hotbar slot(s) {string.Join(", ", missing)} - those slots " +
                             "cannot show the equipped border. Run Tools/HUD/Report HUD Setup.",
                             this);
        }

        // Seed from whatever is already in the hands. Nothing normally is at load, but
        // this component can be added to a live scene and the weapon event would not
        // fire again until the player lowered what they were already holding.
        wasRodRaised = IsAnyRodRaised();
        ApplySelection(ResolveSelectedSlot());
    }

    private void Update()
    {
        // The rod is the one item with no state event of its own. The poll is the five
        // inventory slots and a bool; the write is the part that costs, and it is gated
        // on the answer actually having changed.
        bool rodRaised = IsAnyRodRaised();
        if (rodRaised == wasRodRaised) return;

        wasRodRaised = rodRaised;
        ApplySelection(ResolveSelectedSlot());
    }

    /// <summary>
    /// Weapons report their own state. Raising is unambiguous - the walk below finds the
    /// weapon that just came up and lights its slot. Lowering is not: this event also
    /// fires for a holstered weapon finishing a reload, and blanking the border on every
    /// such event would clear the slot of the weapon actually in the hands. So a lower
    /// only counts when it came from the weapon whose slot is lit - the same rule
    /// CurrentWeaponHUD follows, and the one that makes a swap read correctly, since a
    /// swap lowers the old weapon before raising the new one.
    /// </summary>
    private void OnWeaponHudStateChanged(WeaponController weapon)
    {
        if (weapon == null) return;
        if (!weapon.IsWeaponRaised() && !OwnsSelectedSlot(weapon)) return;

        // Recompute rather than assume: raising the rod lowers the weapon AFTER the rod
        // has already marked itself raised, so the answer to a lower may be "the rod".
        ApplySelection(ResolveSelectedSlot());
    }

    /// <summary>True when the lit slot is this weapon's - i.e. it is the one in hand.</summary>
    private bool OwnsSelectedSlot(WeaponController weapon)
    {
        if (selectedSlot == NoSlot) return false;

        Item[] items = AllItems();
        if (items == null || selectedSlot >= items.Length) return false;

        return items[selectedSlot] is WeaponItem weaponItem
               && weaponItem.GetWeaponController() == weapon;
    }

    /// <summary>
    /// The single slot whose item is in the hands, or NoSlot for empty hands. First match
    /// wins because only one thing can be held: selecting a weapon force-lowers the rod
    /// and raising the rod lowers the weapons, so a second match would be a bug elsewhere.
    /// </summary>
    private int ResolveSelectedSlot()
    {
        Item[] items = AllItems();
        if (items == null) return NoSlot;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is WeaponItem weaponItem)
            {
                WeaponController weapon = weaponItem.GetWeaponController();
                if (weapon != null && weapon.IsWeaponRaised()) return i;
            }
            else if (items[i] is FishingRodItem rodItem && rodItem.IsRaised)
            {
                return i;
            }

            // Anything else is a consumable, and a consumable is never held: FoodItem
            // and MedicineItem apply their effect and remove themselves.
        }

        return NoSlot;
    }

    /// <summary>Whether the rod is up, the one state that has to be polled.</summary>
    private static bool IsAnyRodRaised()
    {
        Item[] items = AllItems();
        if (items == null) return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is FishingRodItem rodItem && rodItem.IsRaised) return true;
        }

        return false;
    }

    /// <summary>
    /// The inventory's slots, or null when there is no inventory yet. Guarded here rather
    /// than cached in Start: Instance is null for the first frames of a session and again
    /// across a scene reload (ObjectiveManager's header spells out why), and this runs on
    /// an Update path, so it returns quietly instead of logging.
    /// </summary>
    private static Item[] AllItems()
    {
        Inventory inventory = Inventory.Instance;
        return inventory != null ? inventory.GetAllItems() : null;
    }

    /// <summary>
    /// Lights one slot and only one. Returns without touching a thing when the answer has
    /// not changed - SetActive on a uGUI object is a canvas rebuild, and this is called
    /// from Update.
    /// </summary>
    private void ApplySelection(int slot)
    {
        if (selectedFrames == null) return;

        // Every frame is driven from the wanted slot, rather than trusting selectedSlot as a
        // cache and returning early when it already matches.
        //
        // Measured 2026-09-06: with the early-out, selectedSlot could sit at 0 while every
        // frame was inactive, and from then on the border could never appear - the one value
        // the cache existed to protect was the one that had drifted. Anything that switches a
        // frame off behind this script's back (a prefab revert, a scene reload part-way
        // through, a hand edit in the inspector) wedges it permanently.
        //
        // This costs nothing: SetFrameActive still compares before it writes, so a repaint
        // with no real change still issues zero SetActive calls, and SetActive is the only
        // part that would have cost a canvas rebuild.
        selectedSlot = slot;

        for (int i = 0; i < selectedFrames.Length; i++)
        {
            SetFrameActive(i, i == slot);
        }
    }

    private void SetFrameActive(int slot, bool active)
    {
        if (slot < 0 || slot >= selectedFrames.Length) return;

        // Compare before writing: SetActive on a uGUI object is a canvas rebuild, and these
        // canvases already rebuild every frame at idle without help.
        GameObject frame = selectedFrames[slot];
        if (frame != null && frame.activeSelf != active) frame.SetActive(active);
    }
}
