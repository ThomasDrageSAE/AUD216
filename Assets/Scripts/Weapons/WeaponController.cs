using TMPro;
using UnityEngine;
using System.Collections;

/*
 * WeaponController.cs
 * 
 * Purpose: Base class for all weapon behavior and management
 * Used by: Player weapon system, specific weapon implementations
 * 
 * Key Features:
 * - Weapon positioning and movement
 * - Recoil system
 * - Weapon bob animation
 * - Fire rate control
 * - Damage system
 * - Camera shake integration
 * 
 * Movement Features:
 * - Smooth weapon transitions
 * - Position/rotation interpolation
 * - Configurable bob patterns
 * - Walking/running states
 * 
 * Performance Considerations:
 * - Efficient state management
 * - Optimized transform updates
 * - Smart component caching
 * - Minimal physics checks
 * 
 * Dependencies:
 * - ReticleController for crosshair
 * - BulletImpactManager for hit effects
 * - WeaponAudioManager for sounds
 * - CameraShakeController for recoil
 */

public class WeaponController : MonoBehaviour
{
    [Header("Weapon Properties")]
    [SerializeField] protected float fireRate = 0.5f;
    [SerializeField] protected float damage = 10f;
    [SerializeField] protected float range = 100f;
    [SerializeField] protected Transform firePoint;
    [SerializeField] protected ParticleSystem muzzleFlash;
    [SerializeField] protected WeaponAudioManager weaponAudioManager;
    [SerializeField] protected string weaponName;
    [Tooltip("Compile-checked identity for AUDIO lookups. When set, this wins over the " +
             "name above; the name stays as the fallback and still drives weapon detection.")]
    [SerializeField] protected WeaponId weaponId = WeaponId.Unassigned;

    /// <summary>
    /// The key handed to WeaponAudioManager. Audio only - weaponName still drives the
    /// SMG/Revolver detection below, which is weapon identity rather than a sound key.
    /// </summary>
    protected string WeaponAudioKey => AudioProfileIds.IsAssigned(weaponId)
        ? AudioProfileIds.NameOf(weaponId)
        : weaponName;

    /// <summary>
    /// The profile name this weapon looks up. Public so editor tooling can ask the scene
    /// which weapons are which instead of keeping its own list of names.
    /// </summary>
    public string ProfileName => WeaponAudioKey;

    /// <summary>
    /// Whether this weapon carries a magazine - an ammo count, an R reload and the HUD
    /// readout. True by default, so a new firearm gets one without being registered
    /// anywhere.
    /// </summary>
    /// <remarks>
    /// The axe and the crossbow override this to false. The crossbow is the reason this is
    /// not simply weaponType != Axe: it still reloads audibly, so it keeps its reload
    /// PROFILE, it just has no magazine to count. Those are two different questions, and
    /// collapsing them is what left the answer spelled as a list of names in the editor
    /// tool while the runtime matched the same names separately.
    /// </remarks>
    public virtual bool UsesMagazine => true;
    
    // Cached in Start - weaponName never changes at runtime, and the string
    // checks used to run per-access/per-shot (and NRE'd on an unset name).
    private bool showsReticle;
    private bool isRevolver;
    private bool isSMG;
    public bool ShowsReticle => showsReticle;

    /// <summary>
    /// Whether this weapon draws a crosshair. Read ONCE, in Start, into showsReticle above.
    /// </summary>
    /// <remarks>
    /// Overridable so a new weapon can answer for itself instead of being spelled into the
    /// name tests below - MinigunController returns true. Those two tests are why this is
    /// virtual: "does it have a crosshair" was answered by matching the weapon's NAME, so
    /// every weapon that wanted one had to be added to a string test in the base class,
    /// which is a list of the game's weapons living in the class they all inherit from.
    /// A subclass overriding this adds nothing to that list.
    /// </remarks>
    protected virtual bool ShouldShowReticle => isSMG || isRevolver;

    [Header("Weapon Bob")]
    [Tooltip("Horizontal movement curve for weapon bobbing")]
    [SerializeField] private AnimationCurve bobCurveX;
    
    [Tooltip("Vertical movement curve for weapon bobbing")]
    [SerializeField] private AnimationCurve bobCurveY;
    
    [Tooltip("Amount of weapon bob while walking")]
    [SerializeField] private float bobAmountWalk = 0.05f;
    
    [Tooltip("Amount of weapon bob while running")]
    [SerializeField] private float bobAmountRun = 0.1f;
    
    [Tooltip("Speed of the weapon bob motion")]
    [SerializeField] private float bobSpeed = 10f;

    [Tooltip("Tilt rotation curve for weapon bobbing")]
    [SerializeField] private AnimationCurve bobCurveRotation;

    [Tooltip("Maximum rotation angle while walking")]
    [SerializeField] private float bobRotationAmountWalk = 2f;

    [Tooltip("Maximum rotation angle while running")]
    [SerializeField] private float bobRotationAmountRun = 4f;

    [Header("Weapon Recoil")]
    [Tooltip("How far back the weapon moves when fired")]
    [SerializeField] private float kickbackDistance = 0.1f;
    
    [Tooltip("How far up the weapon rotates when fired")]
    [SerializeField] private float recoilRotation = 10f;
    
    [Tooltip("How quickly the weapon returns to its original position")]
    [SerializeField] protected float returnSpeed = 10f;
    

    [Header("Weapon Position Toggle")]
    [Tooltip("Position when weapon is raised")]
    [SerializeField] protected Vector3 raisedPosition = new Vector3(0.2f, -0.2f, 0.4f);

    [Tooltip("Rotation when weapon is raised (in degrees)")]
    [SerializeField] protected Vector3 raisedRotation = new Vector3(0f, 0f, 0f);

    [Tooltip("Position offset when weapon is lowered (relative to raised position)")]
    [SerializeField] protected Vector3 loweredPositionOffset = new Vector3(0.4f, -0.6f, 0.2f);

    [Tooltip("Rotation offset when weapon is lowered (in degrees)")]
    [SerializeField] protected Vector3 loweredRotationOffset = new Vector3(-60f, 0f, 15f);

    [Tooltip("How fast the weapon moves when raising")]
    [SerializeField] protected float raiseSpeed = 8f;

    [Tooltip("How fast the weapon moves when lowering")]
    [SerializeField] protected float lowerSpeed = 6f;

    [Tooltip("If true, weapon will fire continuously while mouse button is held")]
    [SerializeField] private bool isAutomatic = false;

    [Header("Ammo")]
    [Tooltip("Rounds per magazine. Firing empty triggers a reload")]
    [Min(1)] [SerializeField] private int maxAmmo = 12;
    [Tooltip("Seconds the reload takes. The reload sound is authored on the Weapon Audio " +
             "Manager profile (Reload Action Sounds), not here")]
    [Min(0f)] [SerializeField] private float reloadTime = 1.5f;
    [SerializeField] private KeyCode reloadKey = KeyCode.R;
    [Tooltip("Optional HUD readout. Shows \"7 / 12\" while raised, and \"Reloading...\" during a reload")]
    [SerializeField] private TextMeshProUGUI ammoText;

    [Header("Aim Down Sights")]
    [Tooltip("Sighted weapons only. The axe and crossbow disable this in code - they " +
             "inherit this Update and would otherwise swing into a gun pose on right-click")]
    [SerializeField] private bool aimDownSightsEnabled = true;
    [SerializeField] private KeyCode aimKey = KeyCode.Mouse1;
    [Tooltip("Hold the key to aim. Off = press to toggle")]
    [SerializeField] private bool holdToAim = true;
    [SerializeField] private bool allowAimWhileReloading = false;
    [Tooltip("Weapon pose while aiming, replacing Raised Position/Rotation")]
    [SerializeField] private Vector3 aimPosition = new Vector3(0f, -0.15f, 0.25f);
    [SerializeField] private Vector3 aimRotation = Vector3.zero;
    [Tooltip("Weapon bob is scaled by this while aiming - steadier sights")]
    [Range(0f, 1f)] [SerializeField] private float aimBobMultiplier = 0.25f;
    [Tooltip("Kickback and camera shake are scaled by this while aiming")]
    [Range(0f, 1f)] [SerializeField] private float aimRecoilMultiplier = 0.65f;

    private int currentAmmo;
    private bool isReloading;
    private bool isAiming;

    [Header("Raycast Settings")]
    // Protected, not private: a weapon that overrides Fire has to trace its own shot, and
    // a private mask forces it to serialize a duplicate that the owner then has to keep in
    // step by hand. One authored value, one place to change it.
    [SerializeField] protected LayerMask shootableLayerMask;

    /// <summary>
    /// The camera the shot is traced from, so hits land on the crosshair. Resolved in
    /// Start; see the aiming note in Fire.
    /// </summary>
    private Camera aimCamera;

    [Header("Camera Shake")]
    [Tooltip("Duration of camera shake when firing")]
    [SerializeField] private float m_ShakeDuration = 0.2f;

    [Tooltip("Intensity of camera shake")]
    [SerializeField] private float m_ShakeMagnitude = 0.05f;

    [Tooltip("How rough/noisy the camera shake is")]
    [SerializeField] private float m_ShakeRoughness = 10f;

    [Tooltip("Direction bias of the camera shake")]
    [SerializeField] private Vector3 m_ShakeDirection = new Vector3(0.1f, 0.3f, 0.1f);

    protected float bobTimer;
    protected float nextTimeToFire;
    protected bool isWalking;
    protected bool isRunning;
    protected bool isInRecoil;
    protected bool isWeaponRaised = false;
    protected Vector3 targetPosition;
    protected Quaternion targetRotation;
    protected bool isInitialized = false;

    protected virtual void Start()
    {
        isSMG = !string.IsNullOrEmpty(weaponName) && weaponName.Contains("SMG");
        isRevolver = !string.IsNullOrEmpty(weaponName) && weaponName.Contains("Revolver");

        // After the two name tests, which ShouldShowReticle's default reads.
        showsReticle = ShouldShowReticle;

        // The weapon hangs under the first-person camera, so this normally resolves to
        // the parent it is already attached to; Camera.main is the fallback for a weapon
        // spawned outside that hierarchy.
        aimCamera = GetComponentInParent<Camera>();
        if (aimCamera == null) aimCamera = Camera.main;

        targetPosition = transform.localPosition;
        targetRotation = transform.localRotation;

        // Initialize as lowered if not already initialized
        if (!isInitialized)
        {
            InitializeWeapon();
        }

        // After InitializeWeapon, which decides isWeaponRaised - the readout hides itself
        // while the weapon is down.
        currentAmmo = maxAmmo;
        UpdateAmmoUI();
    }

    protected virtual void Update()
    {
        if (Input.GetKeyDown(reloadKey))
        {
            TryReload();
        }

        HandleAimInput();

        // Handle weapon position toggling and recoil recovery
        if (isInRecoil)
        {
            // Recovery goes to the pose the weapon should currently hold, which is the
            // aim pose while sighted rather than the hip pose.
            Vector3 recoveryPosition = CurrentBasePosition;
            Quaternion recoveryRotation = CurrentBaseRotation;

            // Framerate-independent approach: an exponential decay, so the recovery takes
            // the same wall-clock time regardless of frame rate.
            float recovery = 1f - Mathf.Exp(-returnSpeed * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, recoveryPosition, recovery);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, recoveryRotation, recovery);

            if (Vector3.Distance(transform.localPosition, recoveryPosition) < 0.001f)
            {
                isInRecoil = false;
                transform.localPosition = recoveryPosition;
                transform.localRotation = recoveryRotation;
                
                // Reset targets after recoil
                targetPosition = recoveryPosition;
                targetRotation = recoveryRotation;
            }
        }
        else
        {
            // Handle weapon position toggling when not in recoil
            float currentToggleSpeed = isWeaponRaised ? raiseSpeed : lowerSpeed;
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, Time.deltaTime * currentToggleSpeed);
            transform.localRotation = Quaternion.Lerp(transform.localRotation, targetRotation, Time.deltaTime * currentToggleSpeed);
        }
    }

    public virtual void Fire()
    {
        if (!isWeaponRaised) return;
        if (isReloading) return;

        if (currentAmmo <= 0)
        {
            // Dry: start the reload rather than doing nothing, so an empty magazine is
            // never a dead trigger.
            TryReload();
            return;
        }

        if (Time.time >= nextTimeToFire)
        {
            if (!isAutomatic && !Input.GetMouseButtonDown(0)) return;

            currentAmmo--;
            UpdateAmmoUI();

            nextTimeToFire = Time.time + fireRate;

            ApplyRecoil();

            // Add reticle feedback
            if (ShowsReticle)
            {
                ReticleController.OnWeaponFired();
            }

            if (muzzleFlash != null)
                muzzleFlash.Play();
            
            if (weaponAudioManager != null)
                weaponAudioManager.PlayShootSound(WeaponAudioKey);

            // Traced down the CAMERA's centre line, where the crosshair is - never along
            // the weapon. ApplyRecoil above snaps the weapon to its kickback pose, which
            // is recoilRotation degrees nose-up, so tracing firePoint.forward put every
            // shot that far above the crosshair; on the SMG's automatic fire the pose has
            // not recovered between shots, so the error compounded. The weapon's raised
            // pose and its model orientation fed in the same way. The muzzle stays purely
            // cosmetic - it positions the flash, not the shot.
            Transform aim = aimCamera != null ? aimCamera.transform : firePoint;
            if (aim == null)
            {
                Debug.LogError($"[{name}] No aim camera and no firePoint; cannot trace a shot.", this);
                return;
            }

            RaycastHit hit;
            if (Physics.Raycast(aim.position, aim.forward, out hit, range, shootableLayerMask))
            {
                if (BulletImpactManager.Instance != null)
                {
                    BulletImpactManager.Instance.PlayImpactEffect(hit);
                }

                if (DamageableUtil.TryGetDamageable(hit.collider, out IDamageable target))
                {
                    // Positional: zombies pay double on the head band, props dust where hit.
                    DamageableUtil.Damage(target, damage, hit.point, hit.normal);
                }
            }
        }
    }

    protected virtual void ApplyRecoil()
    {
        isInRecoil = true;

        // Sighted weapons kick less, and the kick is measured from whichever pose the
        // weapon is holding - otherwise firing while aimed would snap it back to the hip.
        float recoilScale = isAiming ? aimRecoilMultiplier : 1f;

        Vector3 kickbackPosition = CurrentBasePosition - new Vector3(0, 0, kickbackDistance * recoilScale);
        Quaternion kickbackRotation =
            CurrentBaseRotation * Quaternion.Euler(new Vector3(-recoilRotation * recoilScale, 0, 0));

        // Snap to the kickback pose, then let the recovery in Update interpolate back.
        // This used to be a single Lerp weighted by one frame's deltaTime, so the kick
        // reached only a fraction of its authored distance - about a third at 60fps, two
        // thirds at 30, a seventh at 144 - and the pose was never actually reached.
        transform.localPosition = kickbackPosition;
        transform.localRotation = kickbackRotation;

        // Apply camera shake
        if (CameraShakeController.Instance != null)
        {
            // The kick is AUTHORED, not branched on. Two hard-coded multipliers used to sit
            // here - the revolver x1.5 magnitude and x1.2 duration, the SMG x0.7 and x0.8 -
            // which meant the minigun, crossbow, axe and grenade fired the bare defaults
            // because nobody had written them a branch. The numbers now live on each
            // weapon's m_ShakeMagnitude / m_ShakeDuration / m_ShakeRoughness, in the
            // inspector, where a designer can change one and hear it on the next shot.
            //
            // isSMG / isRevolver stay: they are still the reticle rule (ShouldShowReticle),
            // which is an identity question and not a kick one.
            float shakeMagnitude = m_ShakeMagnitude;

            if (isAiming) shakeMagnitude *= aimRecoilMultiplier;

            CameraShakeController.Instance.ShakeCamera(
                m_ShakeDuration,
                shakeMagnitude,
                m_ShakeRoughness,
                m_ShakeDirection
            );
        }
    }

    public virtual void ToggleWeaponPosition()
    {
        isWeaponRaised = !isWeaponRaised;
        
        // Restore reticle visibility control
        if (ShowsReticle)
        {
            ReticleController.Show(isWeaponRaised);
        }
        
        // Lowering the weapon drops the sights; the pose helpers then resolve to the
        // holstered pose for both branches below.
        if (!isWeaponRaised) isAiming = false;

        SnapTargetsToBasePose();

        if (weaponAudioManager != null)
        {
            if (isWeaponRaised) weaponAudioManager.PlayWeaponRaiseSound(WeaponAudioKey);
            else weaponAudioManager.PlayWeaponLowerSound(WeaponAudioKey);
        }

        // The readout follows the weapon, so nothing outside has to show or hide it.
        UpdateAmmoUI(isReloading);
    }

    public void InitializeWeapon()
    {
        if (!isInitialized)
        {
            isInitialized = true;
            isWeaponRaised = false;  // Always start lowered
            
            // Set initial position and rotation to lowered state
            transform.localPosition = raisedPosition + loweredPositionOffset;
            transform.localRotation = Quaternion.Euler(raisedRotation + loweredRotationOffset);
            targetPosition = transform.localPosition;
            targetRotation = transform.localRotation;

            UpdateAmmoUI();   // hides the readout: the weapon starts holstered
        }
    }

    public bool IsWeaponRaised()
    {
        return isWeaponRaised;
    }

    // ---- Aim down sights -----------------------------------------------------

    /// <summary>
    /// The pose the weapon should hold right now, before bob or recoil: lowered, aimed, or
    /// at the hip. Everything that needs a "resting" pose goes through these, so adding the
    /// aim state did not mean teaching each of them about it separately.
    /// </summary>
    private Vector3 CurrentBasePosition =>
        !isWeaponRaised ? raisedPosition + loweredPositionOffset
                        : (isAiming ? aimPosition : raisedPosition);

    private Quaternion CurrentBaseRotation =>
        !isWeaponRaised ? Quaternion.Euler(raisedRotation + loweredRotationOffset)
                        : Quaternion.Euler(isAiming ? aimRotation : raisedRotation);

    /// <summary>True while the player is sighted. Read by the bob and recoil scaling.</summary>
    public bool IsAiming => isAiming;

    /// <summary>
    /// Whether this weapon has sights at all. Overridden to false by the melee and
    /// crossbow controllers, which inherit this Update and have no aim pose authored.
    /// </summary>
    protected virtual bool CanAimDownSights => aimDownSightsEnabled;

    private void HandleAimInput()
    {
        bool canAim = CanAimDownSights && isWeaponRaised &&
                      (!isReloading || allowAimWhileReloading);

        if (!canAim)
        {
            // Drop the sights whenever aiming stops being legal, so lowering the weapon
            // mid-aim cannot strand it in the aim pose.
            if (isAiming)
            {
                isAiming = false;
                if (!isInRecoil) SnapTargetsToBasePose();
            }
            return;
        }

        bool wasAiming = isAiming;

        if (holdToAim) isAiming = Input.GetKey(aimKey);
        else if (Input.GetKeyDown(aimKey)) isAiming = !isAiming;

        // Only on the transition: the bob owns the target pose while moving, and
        // rewriting it every frame here would cancel the bob out.
        if (isAiming != wasAiming && !isInRecoil) SnapTargetsToBasePose();
    }

    private void SnapTargetsToBasePose()
    {
        targetPosition = CurrentBasePosition;
        targetRotation = CurrentBaseRotation;
    }

    // ---- Reloading -----------------------------------------------------------

    /// <summary>Starts a reload if one is possible. Safe to call on a full magazine.</summary>
    public void TryReload()
    {
        if (isReloading || !isWeaponRaised) return;
        if (currentAmmo >= maxAmmo) return;

        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;

        if (!allowAimWhileReloading) isAiming = false;

        // Through the manager, not a clip on the weapon: the reload is then authored on
        // the Weapon Audio Manager profile like every other weapon sound, and a missing
        // one shows up in the Ctrl+G report and the Ctrl+J monitor.
        //
        // Three sounds, in the order a reload happens: Start is the magazine coming out,
        // the action sounds are the hands working, and End - below, after the wait - is
        // the weapon ready again. The report has offered Reload Start and Reload End since
        // the profile was written; until 2026-09-08 nothing in the project played either,
        // so a student could author both and hear silence.
        if (weaponAudioManager != null)
        {
            weaponAudioManager.PlayReloadStartSound(WeaponAudioKey);
            weaponAudioManager.PlayReloadActionSound(WeaponAudioKey);
        }

        UpdateAmmoUI(reloading: true);

        yield return new WaitForSeconds(reloadTime);

        // Before the magazine refills, so End marks the moment the weapon can fire again
        // rather than a moment after it.
        if (weaponAudioManager != null)
        {
            weaponAudioManager.PlayReloadEndSound(WeaponAudioKey);
        }

        currentAmmo = maxAmmo;
        isReloading = false;

        // Repaint only while raised. The readout is SHARED between the guns, and a
        // reload finishing on a weapon that was lowered mid-reload would otherwise hide
        // the readout the currently-raised weapon is using. A lowered weapon repaints
        // anyway when it next comes up (ToggleWeaponPosition).
        if (isWeaponRaised) UpdateAmmoUI();
    }

    // ---- Ammo readout --------------------------------------------------------

    /// <summary>
    /// Raised whenever this weapon's HUD-relevant state changes: raised or lowered, a
    /// round fired, a reload starting or landing. The bottom-right current-weapon panel
    /// (CurrentWeaponHUD) listens and reads the properties below; the weapon stays the
    /// only authority on its own magazine. Static, so it is reset on load like the
    /// project's other reset-on-load statics.
    /// </summary>
    public static event System.Action<WeaponController> OnHudStateChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnHudStateChanged = null;

    /// <summary>
    /// Rounds left in the magazine. Meaningless when UsesMagazine is false.
    /// Virtual so a weapon that counts something other than a magazine can put its own
    /// number in the HUD panel: GrenadeController reports grenades in hand. The backing
    /// field stays private - a subclass reports a count, it does not reach into this one.
    /// </summary>
    public virtual int CurrentAmmo => currentAmmo;

    /// <summary>Rounds in a full magazine. Virtual for the same reason as CurrentAmmo.</summary>
    public virtual int MaxAmmo => maxAmmo;

    /// <summary>True from the reload starting until the magazine refills.</summary>
    public bool IsReloading => isReloading;

    /// <summary>
    /// Repaints the HUD readout, hiding it whenever the weapon is down, and tells the
    /// current-weapon panel. Called by the weapon itself on every state change, so
    /// nothing outside has to remember to.
    /// </summary>
    /// <summary>
    /// Tells the current-weapon panel to re-read this weapon. UpdateAmmoUI raises it for
    /// anything with a magazine; a weapon whose count is its own (the grenade) has nothing
    /// to update here and still needs the panel refreshed, and a subclass cannot raise a
    /// static event declared in its base.
    /// </summary>
    protected void RaiseHudStateChanged()
    {
        OnHudStateChanged?.Invoke(this);
    }

    /// <summary>
    /// Spends one round and refreshes the readout, for a subclass that replaces Fire and so
    /// cannot reach the private magazine. Returns false when the magazine is empty, which is
    /// the caller's cue to stop firing.
    ///
    /// Without this a subclass has to keep its own count, and then the base's magazine never
    /// empties: R does nothing, the fire-empty reload never triggers, and the legacy ammo
    /// readout sits at full forever. MinigunController is the reason this exists.
    /// </summary>
    protected bool TrySpendRound()
    {
        if (currentAmmo <= 0) return false;

        currentAmmo--;
        UpdateAmmoUI();
        return true;
    }

    private void UpdateAmmoUI(bool reloading = false)
    {
        // Before the text readout's early-out: the panel listens whether or not a
        // legacy text readout is wired.
        OnHudStateChanged?.Invoke(this);

        if (ammoText == null) return;

        if (!isWeaponRaised)
        {
            ammoText.gameObject.SetActive(false);
            return;
        }

        ammoText.gameObject.SetActive(true);
        ammoText.text = reloading ? "Reloading..." : $"{currentAmmo} / {maxAmmo}";
    }

    /// <summary>
    /// Drives the idle/movement bob.
    /// </summary>
    /// <param name="isMoving">
    /// Whether the player is actually moving. The callers used to pass
    /// (m_IsWalking, !m_IsWalking), where m_IsWalking means "not sprinting" and is true
    /// while standing still - so "walking || running" was a tautology, the not-moving
    /// branch was unreachable, and the weapon froze off-centre wherever the bob curve
    /// happened to stop.
    /// </param>
    /// <param name="isSprinting">Whether the player is sprinting (selects the larger bob).</param>
    /// <param name="speed">Current planar movement speed.</param>
    public virtual void UpdateWeaponBob(bool isMoving, bool isSprinting, float speed)
    {
        if (!isWeaponRaised || isInRecoil) return;

        isWalking = isMoving && !isSprinting;
        isRunning = isMoving && isSprinting;

        if (isMoving)
        {
            // Update bob timer
            bobTimer += Time.deltaTime * bobSpeed * speed;

            // Sighted, the weapon steadies: the same bob scaled down rather than a second
            // curve to author.
            float bobScale = isAiming ? aimBobMultiplier : 1f;

            // Calculate bob amounts based on movement state
            float currentBobAmount = (isRunning ? bobAmountRun : bobAmountWalk) * bobScale;
            float currentRotationAmount = (isRunning ? bobRotationAmountRun : bobRotationAmountWalk) * bobScale;

            // Calculate position offset
            float horizontalBob = bobCurveX.Evaluate(bobTimer) * currentBobAmount;
            float verticalBob = bobCurveY.Evaluate(bobTimer) * currentBobAmount;
            Vector3 bobOffset = new Vector3(horizontalBob, verticalBob, 0f);

            // Calculate rotation offset
            float rotationBob = bobCurveRotation.Evaluate(bobTimer) * currentRotationAmount;
            Quaternion bobRotation = Quaternion.Euler(0f, 0f, rotationBob);

            // Apply bob to target position and rotation, around whichever pose the weapon
            // is holding - hip or aimed.
            if (!isInRecoil)
            {
                targetPosition = CurrentBasePosition + bobOffset;
                targetRotation = CurrentBaseRotation * bobRotation;
            }

            // Reset bob timer to prevent floating point errors
            if (bobTimer > 1000f)
            {
                bobTimer = 0f;
            }
        }
        else
        {
            // Return to normal position when not moving
            bobTimer = 0f;
            if (!isInRecoil) SnapTargetsToBasePose();
        }
    }
} 