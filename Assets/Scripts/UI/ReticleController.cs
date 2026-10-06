using UnityEngine;

/*
 * ReticleController.cs
 * 
 * Purpose: Manages the player's reticle/crosshair behavior and animations
 * Used by: Player aiming system, weapon system
 * 
 * Key Features:
 * - Dynamic reticle spread based on movement
 * - Weapon fire response
 * - Smooth transitions
 * - Movement-based adjustments
 * 
 * Performance Considerations:
 * - Optimized animation handling
 * - Efficient state transitions
 * - Smart parameter caching
 * 
 * Dependencies:
 * - Requires Animator component
 * - FirstPersonController integration
 * - Animation system setup
 */
public class ReticleController : MonoBehaviour
{
    private static ReticleController Instance;

    [Header("References")]
    [Tooltip("The Animator component controlling the reticle animations")]
    [SerializeField] private Animator reticleAnimator;
    
    [Tooltip("The root GameObject containing all reticle elements")]
    [SerializeField] private GameObject reticleObject;

    [Tooltip("The marker CombatFeedback flashes on a landed hit. Must be a CHILD of the " +
             "reticle root above, so that Show(false) hides it with the rest of the " +
             "crosshair. Empty is fine - the flash is simply skipped")]
    [SerializeField] private RectTransform hitMarker;

    [Tooltip("Reference to the player's FirstPersonController for movement detection")]
    [SerializeField] private UnityStandardAssets.Characters.FirstPerson.FirstPersonController m_FirstPersonController;

    [Header("Movement Settings")]
    [Tooltip("Minimum player velocity required to trigger movement-based reticle spread")]
    [SerializeField, Range(0.1f, 2f)] private float movementThreshold = 0.1f;
    
    [Tooltip("How quickly the reticle adjusts its size when player movement state changes")]
#pragma warning disable 0414
    [SerializeField, Range(1f, 20f)] private float sizeChangeSpeed = 8f;
#pragma warning restore 0414

    [Header("Animation Settings")]
    [Tooltip("How quickly the reticle appears when weapon is raised (higher = faster)")]
    [SerializeField] private float raiseSpeed = 10f;
    
    [Tooltip("How quickly the reticle disappears when weapon is lowered (higher = faster)")]
    [SerializeField] private float lowerSpeed = 8f;

    [Header("Fire Response")]
    [Tooltip("How quickly the reticle expands when weapon is fired (higher = snappier)")]
    [SerializeField] private float fireSpreadSpeed = 20f;
    
    [Tooltip("How quickly the reticle returns to normal size after firing (higher = faster recovery)")]
    [SerializeField] private float fireRecoverSpeed = 15f;

    [Tooltip("Controls how gradually the reticle slows down as it approaches minimum size (higher = more gradual slowdown)")]
    [SerializeField, Range(1f, 5f)] private float recoveryEaseExponent = 2f;

    [Header("Reticle Size")]
    [Tooltip("Base size multiplier when reticle is visible but player is stationary (0.5 = half spread, 1.0 = full spread)")]
    [SerializeField, Range(0.01f, 1f)] private float minSpreadSize = 0.5f;

    [Tooltip("Maximum size multiplier when player is moving")]
    [SerializeField, Range(0.5f, 1.5f)] private float movementMaxSpreadSize = 0.9f;

    [Tooltip("Maximum size multiplier when weapon is fired")]
    [SerializeField, Range(0.5f, 1.5f)] private float fireMaxSpreadSize = 0.8f;

    private bool isFireSpread = false;
    private float fireSpreadValue = 0f;

    private float currentAimValue = 0f;
    private float targetAimValue = 0f;
    private static readonly int AimParameter = Animator.StringToHash("Aim");
    private CharacterController m_CharacterController;
    private bool isVisible = false;
    private bool isTransitioning = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (m_FirstPersonController != null)
        {
            m_CharacterController = m_FirstPersonController.GetComponent<CharacterController>();
        }

        // Start with reticle deactivated
        if (reticleObject != null)
        {
            reticleObject.SetActive(false);
        }
    }

    private void Update()
    {
        UpdateReticleAnimation();
        
        if (isVisible && !isTransitioning)
        {
            UpdateReticleSpread();
        }
    }

    private void UpdateReticleAnimation()
    {
        if (reticleAnimator == null) return;

        if (isVisible)
        {
            // Handle fire spread
            if (isFireSpread)
            {
                // Quick expansion to fire max size
                fireSpreadValue = Mathf.Lerp(fireSpreadValue, 1f, Time.deltaTime * fireSpreadSpeed);
                if (fireSpreadValue > 0.95f)
                {
                    isFireSpread = false;
                }
            }
            else
            {
                // Recovery with configurable ease-out
                float recoveryT = Time.deltaTime * fireRecoverSpeed;
                float baseSpeed = 1f + fireSpreadValue * 0.2f;
                float easeOut = Mathf.Pow(fireSpreadValue, 1f / recoveryEaseExponent);
                fireSpreadValue = Mathf.Lerp(fireSpreadValue, 0f, recoveryT * baseSpeed * easeOut);
            }

            // Calculate fire spread contribution
            float fireSpread = Mathf.Lerp(currentAimValue, fireMaxSpreadSize, fireSpreadValue);
            
            // Use the larger of movement spread or fire spread
            float finalAimValue = Mathf.Max(currentAimValue, fireSpread);
            reticleAnimator.SetFloat(AimParameter, finalAimValue);

            // Normal raise animation
            currentAimValue = Mathf.Lerp(currentAimValue, targetAimValue, Time.deltaTime * raiseSpeed);
            if (Mathf.Abs(currentAimValue - targetAimValue) < 0.01f)
            {
                isTransitioning = false;
            }
        }
        else
        {
            // Lower animation
            currentAimValue = Mathf.MoveTowards(currentAimValue, 0f, Time.deltaTime * lowerSpeed);
            if (currentAimValue == 0f && isTransitioning)
            {
                isTransitioning = false;
                reticleObject?.SetActive(false);
            }
            reticleAnimator.SetFloat(AimParameter, currentAimValue);
        }
    }

    private void UpdateReticleSpread()
    {
        if (m_CharacterController == null) return;

        float currentVelocity = m_CharacterController.velocity.magnitude;
        targetAimValue = currentVelocity > movementThreshold ? movementMaxSpreadSize : minSpreadSize;
    }

    public static void Show(bool show)
    {
        if (Instance != null)
        {
            Instance.isVisible = show;
            Instance.isTransitioning = true;
            
            if (show)
            {
                Instance.reticleObject?.SetActive(true);
                Instance.targetAimValue = Instance.minSpreadSize;
            }
            else
            {
                Instance.targetAimValue = 0f;
            }
        }
    }

    /// <summary>
    /// The reticle's hitmarker, for CombatFeedback to flash. A wired [SerializeField]
    /// rather than a Find by name: a name lookup would silently pick up whatever the object
    /// is renamed to next, and a hitmarker that quietly stops appearing is a bug nobody
    /// reports. Null until the setup tool wires it, and null in a scene that has no reticle
    /// yet - so every caller checks at the dereference.
    ///
    /// This class does NOT drive the marker. It only owns the reference, because the marker
    /// hangs under reticleObject and is therefore already covered by Show(false).
    /// </summary>
    public static RectTransform HitMarker => Instance != null ? Instance.hitMarker : null;

    public static void OnWeaponFired()
    {
        if (Instance != null && Instance.isVisible)
        {
            Instance.isFireSpread = true;
            Instance.fireSpreadValue = 0f;  // Reset to ensure full animation
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (reticleAnimator == null)
        {
            Debug.LogWarning("ReticleController: Animator reference is missing!");
        }
        if (m_FirstPersonController == null)
        {
            Debug.LogWarning("ReticleController: FirstPersonController reference is missing!");
        }
        // Not cosmetic: Show(false) hides the crosshair by deactivating reticleObject, and a
        // marker parented anywhere else would stay on screen with no crosshair around it.
        if (hitMarker != null && reticleObject != null && !hitMarker.IsChildOf(reticleObject.transform))
        {
            Debug.LogWarning("ReticleController: the hit marker is not under the reticle root, so hiding the reticle will leave it visible!", this);
        }
    }
#endif
} 