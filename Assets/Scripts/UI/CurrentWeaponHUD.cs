using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/*
 * CurrentWeaponHUD.cs
 *
 * Purpose: The bottom-right "current weapon" panel - the raised weapon's picture and the
 *          rounds left in its magazine, in the style of the Synty Apocalypse HUD demo.
 * Used by: "Current Weapon" under Canvas - Interface/HUD/ScreenSpace/Bottom Right, an
 *          instance of the pack's HUD_CurrentWeapon_Rack01 prefab (the rusty box; the
 *          owner tried the Minimal variant and came back to it, 2026-09-03). Built and
 *          wired by Tools/Weapons/Create Current Weapon Panel (WeaponSetupTools).
 *
 * The weapon stays the authority on its own state: WeaponController raises
 * OnHudStateChanged on every raise/lower/shot/reload and this panel only repaints from
 * the weapon's public properties. It never polls.
 *
 * Visibility is a CanvasGroup fade rather than SetActive, so an animated parent (the
 * HUD Manager slides its corners in) can still move a hidden panel, and so a panel that
 * was hidden mid-tween cannot be left half-shown. Nothing raised = nothing shown, which
 * covers empty hands and the fishing rod.
 *
 * The picture comes from the WeaponItem that owns the raised weapon (GetHudIcon: the
 * side-on Synty icon when authored, else the hotbar icon), found through the Inventory -
 * a weapon can only be raised from an inventory slot, so it is always there.
 */
public class CurrentWeaponHUD : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Fades the whole panel. Added to this object if left empty")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("The weapon picture (Icon_CurrentWeapon in the Synty prefab)")]
    [SerializeField] private Image icon;
    [Tooltip("Rounds left in the magazine (Label_AmmoCount in the Synty prefab). " +
             "Blank for the axe and crossbow, which have no magazine to count")]
    [SerializeField] private TextMeshProUGUI ammoLabel;
    [Tooltip("Optional second label: \"/ <magazine size>\" (Label_AmmoTotal in the Synty " +
             "Minimal prefab). Blank alongside the count for weapons without a magazine")]
    [SerializeField] private TextMeshProUGUI ammoTotalLabel;

    [Header("Behaviour")]
    [Tooltip("Seconds for the show/hide fade. 0 = instant")]
    [Min(0f)] [SerializeField] private float fadeDuration = 0.15f;
    [Tooltip("Label opacity while the weapon is reloading - the count is stale until " +
             "the reload lands, and this is the only cue the panel gives")]
    [Range(0f, 1f)] [SerializeField] private float reloadingLabelAlpha = 0.35f;

    private WeaponController shownWeapon;
    private Tween fadeTween;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Never eat a click meant for the world, visible or not.
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        WeaponController.OnHudStateChanged += OnWeaponStateChanged;
    }

    private void OnDisable()
    {
        WeaponController.OnHudStateChanged -= OnWeaponStateChanged;
        fadeTween?.Kill();
        fadeTween = null;
    }

    private void Start()
    {
        // The weapons repaint from their own Start, which may have run before this
        // subscribed. Nothing is normally up at load, but a raised one is cheap to find
        // and a missed one would leave the panel blank until the next raise.
        foreach (WeaponController weapon in FindObjectsByType<WeaponController>(FindObjectsSortMode.None))
        {
            if (weapon.IsWeaponRaised())
            {
                Show(weapon);
                return;
            }
        }
    }

    private void OnWeaponStateChanged(WeaponController weapon)
    {
        if (weapon == null) return;

        if (weapon.IsWeaponRaised())
        {
            Show(weapon);
            return;
        }

        // A lowered weapon that is not the one on the panel changes nothing. The guns
        // share this panel, and a holstered gun finishing its reload must not hide the
        // weapon the player is actually holding. Switching weapons lowers the old one
        // BEFORE raising the new one, so this branch also covers the swap cleanly.
        if (weapon == shownWeapon) Hide();
    }

    private void Show(WeaponController weapon)
    {
        if (weapon != shownWeapon)
        {
            shownWeapon = weapon;

            if (icon != null)
            {
                Sprite picture = FindIcon(weapon);
                icon.sprite = picture;
                icon.enabled = picture != null;
            }
        }

        if (ammoLabel != null)
        {
            if (weapon.UsesMagazine)
            {
                ammoLabel.text = weapon.CurrentAmmo.ToString();
                ammoLabel.alpha = weapon.IsReloading ? reloadingLabelAlpha : 1f;
            }
            else
            {
                ammoLabel.text = string.Empty;
            }
        }

        if (ammoTotalLabel != null)
        {
            ammoTotalLabel.text = weapon.UsesMagazine ? "/ " + weapon.MaxAmmo : string.Empty;
        }

        FadeTo(1f);
    }

    private void Hide()
    {
        shownWeapon = null;
        FadeTo(0f);
    }

    private void FadeTo(float alpha)
    {
        fadeTween?.Kill();
        fadeTween = null;

        if (fadeDuration <= 0f)
        {
            canvasGroup.alpha = alpha;
            return;
        }

        // Unscaled: the pause menu freezes time, and a panel caught mid-fade would
        // otherwise sit half-shown behind it.
        fadeTween = canvasGroup.DOFade(alpha, fadeDuration).SetUpdate(true);
    }

    private static Sprite FindIcon(WeaponController weapon)
    {
        Inventory inventory = Inventory.Instance;
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        if (inventory == null) return null;

        foreach (Item item in inventory.GetAllItems())
        {
            if (item is WeaponItem weaponItem && weaponItem.GetWeaponController() == weapon)
            {
                return weaponItem.GetHudIcon();
            }
        }

        return null;
    }
}
