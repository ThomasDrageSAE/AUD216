using UnityEngine;

/*
 * MinigunController.cs
 *
 * Purpose: The minigun in the player's hands - a barrel that has to be brought up to speed
 *          before a single round leaves it, and brought back down when the trigger goes.
 * Used by: The Minigun weapon under Player/FirstPersonCharacter/WeaponParent, selected
 *          through a WeaponItem like every other weapon. FirstPersonController calls Fire()
 *          every frame the fire button is held (FirstPersonController.Update), exactly as it
 *          does for the guns.
 *
 * Key Features:
 * - A four-state spin: Idle -> SpinningUp -> Firing -> SpinningDown. Holding the trigger
 *   winds the barrel up over spinUpSeconds and only then starts putting rounds out;
 *   releasing it winds down over spinDownSeconds. A re-press during the wind-down resumes
 *   from wherever the barrel actually is rather than re-paying the whole wind-up
 * - Fire() is overridden whole rather than extending the base: the base's Fire fires on the
 *   frame it is called, and the gate it fires on - raised, loaded, off cooldown - is exactly
 *   the gate the spin replaces. Everything the base's shot DOES (round spent, recoil,
 *   reticle, muzzle flash, shoot sound, camera-line raycast, impact, damage) is reproduced
 *   in FireOneRound below
 * - The spin loop is a LOOPING source, so it is the one sound in this class that can outlive
 *   the weapon. Every path that stops the barrel funnels through BeginSpinDown or
 *   HardStopSpin, and both stop it - see "Stopping the loop" below
 *
 * Audio: five clips on this weapon's own profile, played by the WeaponAudioManager on three
 *   dedicated sources - a wind-up whose LENGTH is the wind-up, a spin loop, a shooting startup
 *   that hands over sample-accurately to a shooting loop, and a shutdown. No per-round shot:
 *   the shooting loop is the gunfire. This controller decides WHEN (it owns the barrel and
 *   the trigger), the manager decides what plays.
 * - WeaponAudioManager: StartSpinWindup, BeginShooting, EndShooting, StopMinigunAudio
 */
public class MinigunController : WeaponController
{
    /// <summary>
    /// The minigun is hip-fired down the crosshair, so it needs one. Overriding this rather
    /// than adding a third weaponName.Contains test to the base: the base's SMG/Revolver
    /// strings are why a new weapon could not have a crosshair without editing the base.
    /// </summary>
    protected override bool ShouldShowReticle => true;

    [Header("Minigun Spin")]
    [Tooltip("The rotating barrel cluster - the FBX's SK_Wep_Minigun_02_Barrel_01. Optional: " +
             "left empty the gun still fires and sounds right, nothing turns")]
    [SerializeField] private Transform barrel;

    [Tooltip("FALLBACK wind-up, in seconds, for a profile with no wind-up clip. When the " +
             "profile has one, its length is the wind-up and this is ignored: the owner's " +
             "rule is that the first round leaves exactly when the wind-up clip ends")]
    [Min(0f)] [SerializeField] private float spinUpSeconds = 0.7f;

    [Tooltip("FALLBACK wind-down, in seconds, for a release with no shutdown clip playing - " +
             "a profile without one, or a trigger let go during the wind-up. When the shutdown " +
             "clip plays, its length is the wind-down and this is ignored: the barrels stop " +
             "turning exactly when the sound does")]
    [Min(0f)] [SerializeField] private float spinDownSeconds = 1f;

    [Tooltip("Barrel revolutions per minute at full speed. Scaled by how far the spin has " +
             "wound up, so the mesh and the sound describe the same barrel")]
    [Min(0f)] [SerializeField] private float maxBarrelRpm = 1200f;

    [Tooltip("The shortest wind-up a tap can produce, in seconds. A press shorter than this " +
             "keeps winding up until it has run this long, THEN winds down - so the tiniest " +
             "click still hears the wind-up begin and the wind-down end, instead of two frames " +
             "of each. Zero means a press is honoured to the frame, which at 100+ fps is a " +
             "click. Running dry, reloading and lowering the weapon are never held back")]
    [Min(0f)] [SerializeField] private float minimumTapSeconds = 0.15f;

    [Tooltip("Axis the barrels turn about, in the BARREL'S OWN space. Leave at zero to derive " +
             "it from the fire point - the direction from the barrel to the muzzle IS the " +
             "barrel axis. Set it only for a rig where that is not true")]
    [SerializeField] private Vector3 barrelSpinAxis = Vector3.zero;

    [Tooltip("Which way round the barrels turn. Off, they turn ANTI-CLOCKWISE seen from " +
             "behind the gun (the shooter's view) and clockwise seen from downrange; on, the " +
             "other way. Nothing else changes - flip it by eye if the turn reads wrong")]
    [SerializeField] private bool reverseSpin = false;

    /// <summary>
    /// The axis SpinBarrel actually turns about, in the barrel's own space. Resolved once in
    /// Start rather than worked out per frame: it reads two Transforms, and the answer cannot
    /// change while the gun is in one piece. Vector3.up is only the last-resort value - see
    /// ResolveSpinAxis for why up rather than forward.
    /// </summary>
    private Vector3 resolvedSpinAxis = Vector3.up;

    /// <summary>
    /// Where the barrel is between stopped (0) and up to speed (1). Drives both the mesh
    /// rotation and the "may a round leave the gun" test, so the two can never disagree.
    /// </summary>
    private float spinFraction;

    private enum SpinState { Idle, SpinningUp, Firing, SpinningDown }
    private SpinState spinState = SpinState.Idle;

    /// <summary>
    /// The frame Fire() last ran. FirstPersonController calls Fire() every frame the button
    /// is held and never calls it while the game is paused, the weapon is holstered or
    /// another weapon is selected - so this one number is "the trigger is down" AND every
    /// reason it does not count, which reading the mouse here directly would not give.
    /// </summary>
    private int lastFireFrame = -1;

    /// <summary>
    /// The wind-up in force for this press: the profile's wind-up clip length when there is
    /// one, spinUpSeconds when there is not. Read once per press in BeginSpinUp, because
    /// the clip is the manager's to know about and a student can swap it in play mode.
    /// </summary>
    private float windupSeconds;

    /// <summary>
    /// How long the barrels take from where they are to stopped, for this release. Derived
    /// so that they stop exactly when the sound does: the shutdown's full length after rounds
    /// had left the gun (the barrels are at 1); after a tap, the tail of the wind-down the
    /// manager actually started, spread over the fraction the barrels had reached; and
    /// spinDownSeconds when no clip is authored. The owner's two rules (2026-09-06): the
    /// barrels stop when the sound does, and a tap's wind-down is as long as its wind-up was.
    /// </summary>
    private float winddownSeconds;

    /// <summary>When this press's wind-up began, for the tap floor and for the tail length.</summary>
    private float windupStartedAt;

    private bool barrelWarningLogged;

    /// <summary>Degrees per second for one rpm - 360 degrees over 60 seconds.</summary>
    private const float RpmToDegreesPerSecond = 6f;

    /// <summary>
    /// The camera the shot is traced from. Resolved here the same way the base resolves its
    /// own, which is private to it - the same reason GrenadeController keeps a throwCamera.
    /// </summary>
    private Camera aimCamera;

    protected override void Start()
    {
        base.Start();

        aimCamera = GetComponentInParent<Camera>();
        if (aimCamera == null) aimCamera = Camera.main;

        ResolveSpinAxis();
    }

    /// <summary>
    /// The trigger is up whenever Fire() did not run this frame or last frame - one frame of
    /// slack because script execution order decides whether FirstPersonController.Update
    /// runs before or after this one, and a release must never be reported a frame early
    /// while the finger is still down.
    /// </summary>
    private bool TriggerHeld => lastFireFrame >= 0 && Time.frameCount - lastFireFrame <= 1;

    /// <summary>Whether the barrel is allowed to be turning at all right now.</summary>
    private bool CanSpin => isWeaponRaised && !IsReloading && CurrentAmmo > 0;

    private bool IsSpinning => spinState == SpinState.SpinningUp || spinState == SpinState.Firing;

    protected override void Update()
    {
        base.Update();

#if UNITY_EDITOR
        // A held trigger is the one input a script driving the editor cannot produce; this
        // stands in for FirstPersonController's per-frame Fire() call while a check runs.
        if (EditorHoldTrigger) Fire();
#endif

        // The one place a release is noticed. Out of ammo, mid-reload and holstered come
        // through the same test on purpose: they are all "the barrel may not be turning",
        // and giving each its own exit is how one of them ends up forgetting the loop.
        if (IsSpinning && !(TriggerHeld && CanSpin))
        {
            // The tap floor: a trigger let go within minimumTapSeconds of starting the wind-up
            // is treated as still down until that much has run. ONLY the trigger is held back
            // - CanSpin going false (dry, reload, lowered) stops the barrels at once as ever.
            bool releasedEarly = CanSpin && spinState == SpinState.SpinningUp
                                 && Time.time - windupStartedAt < minimumTapSeconds;

            if (!releasedEarly) BeginSpinDown();
        }

        AdvanceSpin();
        SpinBarrel();
    }

    /// <summary>
    /// Called every frame the fire button is held. Overridden whole - the base's Fire fires
    /// immediately, and its gate (raised, loaded, off cooldown, automatic-or-clicked) is the
    /// one the spin replaces. Nothing here is a shot until the barrel is up to speed.
    /// </summary>
    public override void Fire()
    {
        // Stamped before every early-out: this is the trigger, not the shot, and a frame
        // where firing is refused is still a frame where the finger is down.
        lastFireFrame = Time.frameCount;

        if (!CanSpin) return;

        if (spinState == SpinState.Idle || spinState == SpinState.SpinningDown)
        {
            BeginSpinUp();
        }

        if (spinState != SpinState.Firing) return;
        if (Time.time < nextTimeToFire) return;

        FireOneRound();
    }

#if UNITY_EDITOR
    /// <summary>Editor-only: while true, Update calls Fire() every frame as if the button were down.</summary>
    public bool EditorHoldTrigger { get; set; }

    /// <summary>Editor-only: the audio-side wind-up in force, for a check to compare against the clip.</summary>
    public float EditorWindupSeconds => windupSeconds;

    /// <summary>Editor-only: the wind-down in force, for a check to compare against the clip.</summary>
    public float EditorWinddownSeconds => winddownSeconds;

    /// <summary>Editor-only: 0..1, where the barrels are.</summary>
    public float EditorSpinFraction => spinFraction;

    /// <summary>Editor-only: the state name, for a check's log.</summary>
    public string EditorSpinState => spinState.ToString();
#endif

    // ---- The spin ------------------------------------------------------------

    /// <summary>
    /// Works out which way the barrel cluster turns, instead of trusting a constant. This
    /// used to spin the barrel about Vector3.forward; the Synty rig's barrels run down the
    /// barrel object's local Y, so the whole cluster went end over end like a propeller. The
    /// direction from the barrel to the muzzle IS the barrel axis on any rig where the rounds
    /// leave down the middle of the cluster, so DERIVING it means the next FBX, rigged down
    /// some other axis, cannot bring the propeller back. barrelSpinAxis is the escape hatch
    /// for a rig where that assumption does not hold.
    ///
    /// Runs once, from Start, so the warning below is once-only by construction - no flag
    /// needed, and barrelWarningLogged stays reserved for the missing-barrel path.
    /// </summary>
    private void ResolveSpinAxis()
    {
        if (barrelSpinAxis.sqrMagnitude > 0f)
        {
            resolvedSpinAxis = barrelSpinAxis.normalized;
            Debug.Log($"[{name}] Barrel spin axis {resolvedSpinAxis} (barrel-local), set by hand.", this);
            return;
        }

        // Direction, not position, and converted into the barrel's own space because that is
        // the space Rotate(..., Space.Self) turns in. InverseTransformDirection ignores scale,
        // so the result stays a unit vector.
        if (barrel != null && firePoint != null)
        {
            Vector3 toMuzzle = firePoint.position - barrel.position;

            if (toMuzzle.sqrMagnitude > 0f)
            {
                resolvedSpinAxis = barrel.InverseTransformDirection(toMuzzle.normalized);
                Debug.Log($"[{name}] Barrel spin axis {resolvedSpinAxis} (barrel-local), " +
                          "derived from the barrel-to-muzzle direction.", this);
                return;
            }
        }

        // Up, not forward: a barrel cluster is modelled down its own length, and of the two
        // axes a rig is likely to use for "length" this is the one this project's minigun
        // actually uses. It is still a guess, hence the warning - the fix is to assign the
        // fire point, not to edit this line.
        resolvedSpinAxis = Vector3.up;
        Debug.LogWarning($"[{name}] Cannot derive the barrel spin axis (" +
                         (barrel == null ? "no Barrel" : "no Fire Point") +
                         $"); falling back to {resolvedSpinAxis} (barrel-local), which may " +
                         "turn the barrels the wrong way. Assign Fire Point (the model's " +
                         "Muzzle_Point_01) or set Barrel Spin Axis by hand.", this);
    }

    /// <summary>
    /// Starts, or resumes, the wind-up. Resumes from wherever spinFraction already is: a
    /// player who lets go for a moment has a barrel that is still turning, and charging them
    /// the full wind-up again would read as the gun having stopped dead. The wind-up CLIP
    /// resumes from the same place - the manager starts it at the matching offset - so what
    /// is heard and what the barrels are doing agree.
    ///
    /// The wind-up's length is the clip's length. That is the owner's rule (2026-09-06): the
    /// first round leaves exactly when the wind-up clip ends, so a student who authors a
    /// longer clip has authored a slower gun. spinUpSeconds is only for a profile with no clip.
    /// </summary>
    private void BeginSpinUp()
    {
        // Already at speed - a re-press caught before the wind-down had taken anything off.
        // Straight to firing; the manager cuts a shutdown still playing.
        if (spinFraction >= 1f)
        {
            BeginFiring();
            return;
        }

        windupSeconds = spinUpSeconds;

        if (weaponAudioManager != null)
        {
            float clipLength = weaponAudioManager.StartSpinWindup(WeaponAudioKey, spinFraction);
            if (clipLength > 0f) windupSeconds = clipLength;
        }

        // Backdated by however far the barrels already were, so the tap floor and the tail
        // measure the wind-up as heard - a resume from 0.7 has 0.7 of it behind it already.
        windupStartedAt = Time.time - spinFraction * windupSeconds;
        spinState = SpinState.SpinningUp;
    }

    /// <summary>
    /// The barrels are at speed: rounds may leave, and the gunfire starts - startup, spin
    /// loop, and the shooting loop scheduled for the sample the startup ends. Reached from
    /// AdvanceSpin when the wind-up completes, and from BeginSpinUp on a re-press that
    /// caught the barrels still at speed.
    /// </summary>
    private void BeginFiring()
    {
        spinState = SpinState.Firing;
        spinFraction = 1f;

        if (weaponAudioManager != null) weaponAudioManager.BeginShooting(WeaponAudioKey);
    }

    /// <summary>
    /// Hands the barrel over to the wind-down and ends the gunfire. Every ordinary way the
    /// spin can end - trigger released, weapon lowered, magazine empty, reload started -
    /// arrives here, so the audio has exactly one ordinary stop to get right. Which ending
    /// plays is the manager's decision from two facts this controller owns: whether rounds
    /// were actually leaving the gun (the shooting shutdown) or the trigger was let go
    /// during the wind-up (the spin wind-down, from the offset matching the barrels).
    /// </summary>
    private void BeginSpinDown()
    {
        bool roundsWereLeaving = spinState == SpinState.Firing;
        float fractionAtRelease = Mathf.Clamp01(spinFraction);

        spinState = SpinState.SpinningDown;
        winddownSeconds = spinDownSeconds;

        if (weaponAudioManager != null && fractionAtRelease > 0f)
        {
            // The tail the tap hears is as long as the wind-up it heard: the barrel fraction
            // times the wind-up length, which is the wind-up's run time for a press from rest.
            float tail = fractionAtRelease * windupSeconds;
            float played = weaponAudioManager.EndShooting(WeaponAudioKey, roundsWereLeaving, tail);

            // The barrels have `fractionAtRelease` left to lose and `played` seconds to lose
            // it in, and AdvanceSpin loses 1/winddownSeconds per second - so this is the
            // number that lands them on zero as the sound ends. After a burst the fraction
            // is 1 and this is simply the shutdown's length.
            if (played > 0f) winddownSeconds = played / fractionAtRelease;
        }
    }

    private void AdvanceSpin()
    {
        switch (spinState)
        {
            case SpinState.SpinningUp:
                // windupSeconds, not spinUpSeconds: the clip's length when there is one.
                spinFraction = windupSeconds <= 0f
                    ? 1f
                    : Mathf.Min(1f, spinFraction + Time.deltaTime / windupSeconds);
                if (spinFraction >= 1f) BeginFiring();
                break;

            case SpinState.Firing:
                spinFraction = 1f;
                break;

            case SpinState.SpinningDown:
                // winddownSeconds, not spinDownSeconds: the shutdown clip's length when
                // one is playing, so the mesh and the sound stop together.
                spinFraction = winddownSeconds <= 0f
                    ? 0f
                    : Mathf.Max(0f, spinFraction - Time.deltaTime / winddownSeconds);
                if (spinFraction <= 0f) spinState = SpinState.Idle;
                break;
        }
    }

    /// <summary>
    /// Turns the barrel mesh at whatever fraction of full speed the spin has reached, about
    /// the axis the cluster is modelled down - resolvedSpinAxis, derived in Start rather than
    /// hard-coded, because the wrong constant here is a propeller and not a minigun.
    /// </summary>
    private void SpinBarrel()
    {
        if (spinFraction <= 0f) return;

        if (barrel == null)
        {
            if (!barrelWarningLogged)
            {
                barrelWarningLogged = true;
                Debug.LogWarning($"[{name}] No barrel Transform assigned - the minigun fires " +
                                 "and sounds correct, but nothing turns. Drag the model's " +
                                 "SK_Wep_Minigun_02_Barrel_01 into Barrel.", this);
            }
            return;
        }

        float degrees = spinFraction * maxBarrelRpm * RpmToDegreesPerSecond * Time.deltaTime;

        // The derived axis points down the barrels toward the muzzle, and Unity turns
        // anti-clockwise about an axis that points away from the eye - so a positive angle is
        // anti-clockwise seen from behind the gun. Which way a minigun "should" turn is a
        // matter of the eye, not of the maths, so it is a checkbox rather than a sign here.
        if (reverseSpin) degrees = -degrees;

        barrel.Rotate(resolvedSpinAxis, degrees, Space.Self);
    }

    // ---- Stopping the loop ---------------------------------------------------

    /// <summary>
    /// Lowering the weapon stops the barrel. The wind-down sound still plays - it is what
    /// putting a spinning gun away sounds like, and the manager owns the source, so it
    /// survives this object being switched away from.
    /// </summary>
    public override void ToggleWeaponPosition()
    {
        base.ToggleWeaponPosition();

        if (!isWeaponRaised) BeginSpinDown();
    }

    /// <summary>
    /// The last line of defence for the loop, and the only one that runs when the player
    /// dies: PlayerStats.DisablePlayerControls sets enabled = false on every
    /// WeaponController, which stops Update and therefore stops the release detection above.
    /// Also covers the weapon being deactivated, the component being disabled by hand, and
    /// the scene being torn down. No wind-down sound here - a falling whine after the player
    /// is dead is the same bug as the loop, one second shorter.
    /// </summary>
    private void OnDisable()
    {
        HardStopSpin();
    }

    /// <summary>
    /// Redundant with OnDisable for an active object, which is the point: a destroyed
    /// weapon is the one case where being wrong leaves a whirr playing with nothing left to
    /// stop it. Both calls are idempotent.
    /// </summary>
    private void OnDestroy()
    {
        HardStopSpin();
    }

    private void HardStopSpin()
    {
        spinState = SpinState.Idle;
        spinFraction = 0f;

        // Unconditional - no test for whether this weapon believes it started a loop. This
        // is the last chance to silence a looping source, and StopMinigunAudio on sources
        // that are not playing is a no-op.
        if (weaponAudioManager != null) weaponAudioManager.StopMinigunAudio(WeaponAudioKey);
    }

    // ---- The shot ------------------------------------------------------------

    /// <summary>
    /// One round: everything the base's Fire does once it has decided to shoot, minus the
    /// deciding. Kept in step with WeaponController.Fire by hand - if that shot changes,
    /// this is the other place it lives.
    /// </summary>
    private void FireOneRound()
    {
        if (!TrySpendRound()) return;

        nextTimeToFire = Time.time + fireRate;

        ApplyRecoil();

        if (ShowsReticle) ReticleController.OnWeaponFired();

        if (muzzleFlash != null) muzzleFlash.Play();

        // No per-round sound. The gunfire is the shooting LOOP, started by BeginFiring and
        // running until BeginSpinDown - one clip per round at 20 rounds a second was the
        // model this weapon was built with, and the owner replaced it (2026-09-06).

        // Down the CAMERA's centre line, where the crosshair is, never along the weapon -
        // ApplyRecoil above has just snapped the gun into a nose-up pose, and on a weapon
        // firing this fast that pose never recovers between rounds. See the base's Fire.
        Transform aim = aimCamera != null ? aimCamera.transform : firePoint;
        if (aim == null)
        {
            Debug.LogError($"[{name}] No aim camera and no firePoint; cannot trace a shot.", this);
            return;
        }

        RaycastHit hit;
        if (!Physics.Raycast(aim.position, aim.forward, out hit, range, shootableLayerMask)) return;

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

#if UNITY_EDITOR
    // ---- Editor test seam ----------------------------------------------------

    /// <summary>
    /// Editor-only: forces the barrel to full speed for scripted checks. It exists because a
    /// HELD trigger is the one thing a CLI-driven check cannot produce - the spin only winds
    /// up if Fire() runs on consecutive frames, and a script driving the editor from outside
    /// gets one call in, then loses the frame. This puts the state machine where the trigger
    /// would have put it, without going through BeginSpinUp, so a geometry check never starts
    /// the looping spin sound.
    ///
    /// Update's release detection winds it straight back down on the next frame (that is the
    /// correct behaviour - no trigger is held), so a check that wants the barrel still
    /// turning calls this again each time it samples the rotation.
    /// </summary>
    public void EditorForceSpin(float fraction)
    {
        spinFraction = Mathf.Clamp01(fraction);
        spinState = spinFraction > 0f ? SpinState.Firing : SpinState.Idle;
    }
#endif

}
