using UnityEngine;

/*
 * AxeController.cs
 * 
 * Purpose: Controls melee axe weapon behavior and animations
 * Used by: Player weapon system, melee combat
 * 
 * Key Features:
 * - Melee attack animations
 * - Position transitions
 * - Attack collision detection
 * - Damage application
 * - Smooth state transitions
 * 
 * Combat Mechanics:
 * - Attack swing system
 * - Hit detection
 * - Damage zones
 * - Animation control
 * 
 * Performance Considerations:
 * - Optimized collision checks
 * - Efficient state management
 * - Smart animation transitions
 * - Position interpolation
 * 
 * Dependencies:
 * - Extends MeleeWeaponController
 * - Animation system
 * - Physics collision system
 * - Impact effect manager
 */

public class AxeController : MeleeWeaponController
{
    [Header("Attack Swing Settings")]
    [SerializeField] private Vector3 attackSwingPosition;
    [SerializeField] private Vector3 attackSwingRotation;
    [SerializeField] private float attackSwingSpeed = 15f;
    [SerializeField] private float attackReturnSpeed = 10f;
    
    private bool isPositionTransitioning;
    private bool isAttackingForward;
    // Pose the current transition leg started from; the swing lerps from here to the
    // target over normalised time rather than chasing the target from wherever it is now.
    private Vector3 legStartPosition;
    private Quaternion legStartRotation = Quaternion.identity;

    private Vector3 currentTargetPosition;
    private Vector3 currentTargetRotation;
    private float currentTransitionTime;

    protected override void Start()
    {
        base.Start();
        // swingDuration / swingAngle / swingSpeed used to be hard-coded here, silently
        // overriding the inspector every play session. They are authored on the component
        // now, so tuning the axe in the inspector actually does something.
        MoveToLoweredPosition();
    }

    /// <summary>
    /// The swing is rejected while the previous one is still moving the weapon, and the
    /// transition outlasts the fire rate - so without this the fire gate accepted a swing
    /// that PerformMeleeAttack would refuse, playing the sound with no damage.
    /// </summary>
    protected override bool CanAttack() => !isPositionTransitioning;

    protected override void PerformMeleeAttack()
    {
        // Only allow attacks when weapon is completely still in raised position
        // and not in ANY phase of the attack animation
        if (!isWeaponRaised || isPositionTransitioning) return;

        if (firePoint == null)
        {
            Debug.LogError($"[{name}] No firePoint assigned; cannot resolve a swing.", this);
            return;
        }

        isPositionTransitioning = true;
        isAttackingForward = true;
        currentTransitionTime = 0f;
        currentTargetPosition = attackSwingPosition;
        currentTargetRotation = attackSwingRotation;
        BeginTransitionLeg();

        Collider[] hitColliders = Physics.OverlapSphere(
            firePoint.position, 
            attackRange, 
            hitLayers
        );

        foreach (var hitCollider in hitColliders)
        {
            // Instead of using ClosestPoint, do a direct raycast to the collider's center
            Vector3 directionToTarget = (hitCollider.bounds.center - firePoint.position).normalized;

            RaycastHit hit;
            if (Physics.Raycast(firePoint.position, directionToTarget, out hit, attackRange, hitLayers))
            {
                // Apply damage if it's damageable
                if (DamageableUtil.TryGetDamageable(hit.collider, out IDamageable target))
                {
                    // Positional, like the guns: the swing knows where it landed.
                    DamageableUtil.Damage(target, damage, hit.point, hit.normal);
                }

                // Play impact effect at the actual point of impact
                if (BulletImpactManager.Instance != null)
                {
                    BulletImpactManager.Instance.PlayImpactEffect(hit);
                }
            }
        }
    }

    protected override void Update()
    {
        // Only handle attack animations when weapon is raised
        if (!isWeaponRaised || !isPositionTransitioning) 
        {
            base.Update();
            return;
        }

        currentTransitionTime += Time.deltaTime;
        // Guard: at 0 the leg never completes, isPositionTransitioning latches, and
        // CanAttack() refuses input for the rest of the session.
        float speed = Mathf.Max(0.01f, isAttackingForward ? attackSwingSpeed : attackReturnSpeed);

        // Normalised progress from a pose captured when this leg began, NOT a lerp from
        // the live transform toward the target. The old form asymptotically approached
        // the target at a rate that depended on frame time, so the swing took visibly
        // longer at low frame rates and never quite arrived - which is why the whole
        // transition outran the fire rate.
        float t = Mathf.Clamp01(currentTransitionTime * speed);

        transform.localPosition = Vector3.Lerp(legStartPosition, currentTargetPosition, t);
        transform.localRotation = Quaternion.Slerp(legStartRotation, Quaternion.Euler(currentTargetRotation), t);

        // Check if we've reached our target
        if (t >= 1f)
        {
            if (isAttackingForward)
            {
                // Start return to raised position
                isAttackingForward = false;
                currentTransitionTime = 0f;
                currentTargetPosition = raisedPosition;
                currentTargetRotation = raisedRotation;
                BeginTransitionLeg();
            }
            else
            {
                // Attack complete, ensure we're exactly at raised position
                isPositionTransitioning = false;
                transform.localPosition = raisedPosition;
                transform.localRotation = Quaternion.Euler(raisedRotation);
            }
        }
    }

    /// <summary>Captures the pose a transition leg starts from, so it can be lerped from.</summary>
    private void BeginTransitionLeg()
    {
        legStartPosition = transform.localPosition;
        legStartRotation = transform.localRotation;
    }

    public void MoveToLoweredPosition()
    {
        transform.localPosition = raisedPosition + loweredPositionOffset;
        transform.localRotation = Quaternion.Euler(raisedRotation + loweredRotationOffset);
        currentTargetPosition = raisedPosition + loweredPositionOffset;
        currentTargetRotation = raisedRotation + loweredRotationOffset;
    }

    public override void ToggleWeaponPosition()
    {
        base.ToggleWeaponPosition();

        // Lowering mid-swing used to strand isPositionTransitioning as true: Update()
        // returns early while the weapon is lowered, and that is the only code that
        // clears the flag, so PerformMeleeAttack's guard rejected every later swing for
        // the rest of the session - the axe kept playing its swing sound while dealing
        // no damage. Switching inventory slots during the ~0.7s transition did exactly
        // this.
        if (!isWeaponRaised)
        {
            isPositionTransitioning = false;
            isAttackingForward = false;
            currentTransitionTime = 0f;
        }
    }

    private void OnEnable()
    {
        // Just set target to raised position
        targetPosition = raisedPosition;
        targetRotation = Quaternion.Euler(raisedRotation);
        isWeaponRaised = true;
    }

    private void OnDisable()
    {
        // Just set target to lowered position
        targetPosition = raisedPosition + loweredPositionOffset;
        targetRotation = Quaternion.Euler(raisedRotation + loweredRotationOffset);
        isWeaponRaised = false;

        // Same latch as ToggleWeaponPosition: disabled mid-swing (death does this),
        // the frozen leg would resume from a stale pose and block attacks on re-enable.
        isPositionTransitioning = false;
        isAttackingForward = false;
        currentTransitionTime = 0f;
    }
} 