using UnityEngine;

/*
 * CrossbowController.cs
 * 
 * Purpose: Handles crossbow weapon mechanics and arrow firing.
 * Used by: PlayerWeaponSystem, Arrow
 * 
 * Controls crossbow firing mechanics, including drawing, aiming, and releasing arrows.
 * Manages weapon states and animations, integrating with the arrow pooling system
 * for efficient projectile management.
 * 
 * Performance Considerations:
 * - Uses pooled arrows instead of instantiation
 * - Optimizes physics calculations for arrows
 * - Manages weapon state transitions
 * 
 * Dependencies:
 * - ArrowPool for projectile management
 * - WeaponController base class
 * - WeaponAudioManager for sound effects
 */

public class CrossbowController : WeaponController
{
    /// <summary>
    /// The crossbow has no aim pose authored, and this class calls base.Update, which
    /// handles the aim key. Flip this if sights are ever added for it.
    /// </summary>
    protected override bool CanAimDownSights => false;

    /// <summary>No magazine: no ammo count, no R reload, no HUD readout.</summary>
    public override bool UsesMagazine => false;

    [Header("Crossbow Settings")]
    [SerializeField] private float arrowForce = 20f;
    [SerializeField] private float drawTime = 1f;
    private bool isDrawing = false;
    private float drawStartTime;

    /// <summary>
    /// The shooter's colliders, collected once, exactly as GrenadeController collects the
    /// thrower's. The bolt spawns inside the player's own capsule, so every one of these
    /// has to be ignored or the shot sticks in the person who fired it.
    /// </summary>
    private Collider[] shooterColliders;

    protected override void Start()
    {
        base.Start();

        CollectShooterColliders();
    }

    /// <summary>
    /// Walks up to the player root - whatever carries the CharacterController - and takes
    /// every collider under it, the CharacterController included (it is one).
    /// </summary>
    /// <remarks>
    /// Inactive included, the same as GrenadeController.CollectThrowerColliders: a
    /// holstered weapon's collider is still the shooter's. Whether each one is actually
    /// ignored is decided at the shot, because Physics.IgnoreCollision errors on a
    /// disabled collider.
    /// </remarks>
    private void CollectShooterColliders()
    {
        CharacterController character = GetComponentInParent<CharacterController>();
        Transform root = character != null ? character.transform : transform.root;

        shooterColliders = root.GetComponentsInChildren<Collider>(true);
    }

    protected override void Update()
    {
        base.Update();

        // Releasing the mouse mid-draw cancels the shot; without this the stale
        // isDrawing state made the next click fire instantly.
        if (isDrawing && !Input.GetMouseButton(0))
        {
            isDrawing = false;
        }
    }

    public override void Fire()
    {
        if (!isWeaponRaised) return;

        if (!isDrawing && Time.time >= nextTimeToFire)
        {
            isDrawing = true;
            drawStartTime = Time.time;
            
            if (weaponAudioManager != null)
                weaponAudioManager.PlayReloadStartSound(WeaponAudioKey);
        }
        else if (isDrawing && Time.time >= drawStartTime + drawTime)
        {
            nextTimeToFire = Time.time + fireRate;
            isDrawing = false;

            if (muzzleFlash != null)
                muzzleFlash.Play();

            if (weaponAudioManager != null)
            {
                // The cock is over: the string is at the latch and the bolt is seated. The
                // crossbow has no magazine, so this - not WeaponController.ReloadRoutine,
                // which it never runs - is where its Reload End belongs, closing the pair
                // that Reload Start opened when the draw began above.
                weaponAudioManager.PlayReloadEndSound(WeaponAudioKey);
                weaponAudioManager.PlayShootSound(WeaponAudioKey);
            }

            if (ArrowPool.Instance == null)
            {
                Debug.LogError("CrossbowController: no ArrowPool in scene - cannot fire.", this);
                return;
            }

            if (firePoint == null)
            {
                Debug.LogError($"[{name}] No firePoint assigned; cannot launch a bolt.", this);
                return;
            }

            Arrow arrow = ArrowPool.Instance.GetArrow();
            if (arrow == null)
            {
                // The pool says so once, in its own log; a bolt-less crossbow must not
                // throw here every draw on top of it.
                return;
            }

            arrow.transform.position = firePoint.position;
            arrow.transform.rotation = firePoint.rotation;

            IgnoreTheShooter(arrow);

            Rigidbody rb = arrow.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(firePoint.forward * arrowForce, ForceMode.Impulse);
            }

            ApplyRecoil();
        }
    }

    /// <summary>
    /// Keeps the bolt off the person who fired it.
    /// </summary>
    /// <remarks>
    /// The bolt leaves the fire point, which sits inside the player's capsule, and the
    /// capsule is on layer Player against the bolt's Default with an all-ones collision
    /// matrix - so without this the first thing every shot hits is the shooter. It sticks
    /// there and vanishes ten seconds later, which reads as the crossbow not firing at all.
    /// Grenade.Launch (Grenade.cs:154-165) and ZombieSpit.IgnoreTheShooter both do this;
    /// the bolt was the one projectile that did not.
    ///
    /// Physics.IgnoreCollision logs an error for a collider that is disabled or on an
    /// inactive object, so both are checked rather than caught - the same skip as
    /// Grenade.Launch, over the same inactive-included list.
    ///
    /// Unity resets an ignore pair when either collider is deactivated, and the pool
    /// deactivates every bolt it takes back, so these pairs cannot leak into the next shot.
    /// </remarks>
    private void IgnoreTheShooter(Arrow arrow)
    {
        if (shooterColliders == null) return;

        Collider boltCollider = arrow.GetComponent<Collider>();
        if (boltCollider == null) return;

        foreach (Collider own in shooterColliders)
        {
            if (own == null || own == boltCollider) continue;
            if (!own.enabled || !own.gameObject.activeInHierarchy) continue;

            Physics.IgnoreCollision(boltCollider, own);
        }
    }
} 