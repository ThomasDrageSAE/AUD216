using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

/*
 * MeleeWeaponController.cs
 * 
 * Purpose: Base class for all melee weapon behavior and management
 * Used by: Axe, sword, and other melee weapon implementations
 * 
 * Key Features:
 * - Melee attack patterns
 * - Swing animations
 * - Hit detection
 * - Damage zones
 * - Visual effects
 * 
 * Combat Mechanics:
 * - Configurable swing arcs
 * - Attack timing
 * - Damage application
 * - Impact effects
 * - Trail rendering
 * 
 * Performance Considerations:
 * - Efficient collision checks
 * - Optimized animation curves
 * - Smart effect management
 * - Coroutine-based timing
 * 
 * Dependencies:
 * - Extends WeaponController
 * - Animation system
 * - Trail renderer
 * - Particle system
 * - Editor tools for setup
 */

public class MeleeWeaponController : WeaponController
{
    /// <summary>
    /// A melee weapon has no sights. This class calls base.Update, which handles the aim
    /// key, so without this an axe would swing into the firearm aim pose on right-click.
    /// </summary>
    protected override bool CanAimDownSights => false;

    /// <summary>No magazine: no ammo count, no R reload, no HUD readout.</summary>
    public override bool UsesMagazine => false;

    [Header("Melee Settings")]
    [SerializeField] protected float swingDuration = 0.5f;
    [SerializeField] protected LayerMask hitLayers;
    [SerializeField] protected float swingAngle = 90f;
    [SerializeField] protected float attackRange = 2f;
    [SerializeField] protected AnimationCurve swingCurve;
    [SerializeField] protected Transform weaponModel;
    [SerializeField] protected ParticleSystem swingEffect;
    [SerializeField] protected TrailRenderer swingTrail;
    
    protected bool isSwinging = false;
    protected float swingStartTime;
    protected Vector3 originalModelRotation;

    public void SetAttackRotationFromCurrentTransform()
    {
        if (weaponModel != null)
        {
            originalModelRotation = weaponModel.localEulerAngles;
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }
    }

    protected override void Start()
    {
        base.Start();
        if (weaponModel != null)
            originalModelRotation = weaponModel.localEulerAngles;
    }

    /// <summary>
    /// Whether a swing may start. Subclasses that drive their own attack animation
    /// override this so a swing the attack itself will reject never gets accepted here -
    /// otherwise the weapon plays its swing sound and recoil and then silently does no
    /// damage, which makes the sound a lie.
    /// </summary>
    protected virtual bool CanAttack() => true;

    public override void Fire()
    {
        if (!isWeaponRaised) return;
        if (!CanAttack()) return;

        if (!isSwinging && Time.time >= nextTimeToFire)
        {
            isSwinging = true;
            swingStartTime = Time.time;
            nextTimeToFire = Time.time + fireRate;

            if (weaponAudioManager != null)
                weaponAudioManager.PlayShootSound(WeaponAudioKey);

            StartCoroutine(PerformSwing());
        }
    }

    /// <summary>
    /// The damage-dealing half of a swing. AxeController is the only implementation -
    /// there is no generic melee shape worth guessing at, so a subclass that forgets to
    /// override this swings and hits nothing, and says so rather than failing quietly.
    /// </summary>
    protected virtual void PerformMeleeAttack()
    {
        Debug.LogWarning($"{GetType().Name} has no melee attack implementation - " +
                         "the swing animates but deals no damage.", this);
    }

    private IEnumerator PerformSwing()
    {
        if (swingTrail != null)
            swingTrail.enabled = true;
            
        if (swingEffect != null)
            swingEffect.Play();

        // Recoil first: PerformMeleeAttack captures the pose its swing leg starts
        // from, and capturing it before the kick meant the next frame's interpolation
        // overwrote the kick entirely - the melee recoil was invisible.
        ApplyRecoil();

        PerformMeleeAttack();
        yield return new WaitForSeconds(swingDuration);
        
        if (swingTrail != null)
            swingTrail.enabled = false;
            
        isSwinging = false;
    }

    protected override void Update()
    {
        base.Update();
        
        if (isSwinging && weaponModel != null)
        {
            float swingProgress = (Time.time - swingStartTime) / swingDuration;
            float curveValue = swingCurve.Evaluate(swingProgress);
            
            Vector3 newRotation = originalModelRotation;
            newRotation.y += swingAngle * curveValue;
            weaponModel.localEulerAngles = newRotation;
        }
    }
} 