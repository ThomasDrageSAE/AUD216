using UnityEngine;
using TMPro;
using UnityEngine.UI;

/*
 * WorldSpaceObjectiveMarker.cs
 * 
 * Purpose: Manages 3D world-space markers for objectives and points of interest
 * Used by: Navigation system, objective tracking
 * 
 * Key Features:
 * - Dynamic distance display
 * - Camera-facing billboarding
 * - Visual effects (pulse, glow)
 * - Configurable height offset
 * 
 * Performance Considerations:
 * - Interval-based updates
 * - Efficient distance calculations
 * - Smart component caching
 * - Proper cleanup on destroy
 * 
 * Dependencies:
 * - TextMeshPro for distance display
 * - Unity UI system
 * - Requires player and camera references
 * - Visual effect prefabs
 */
public class WorldSpaceObjectiveMarker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private Image icon;
    [SerializeField] private GameObject pulseEffectA;
    [SerializeField] private GameObject pulseEffectB;
    [SerializeField] private GameObject glowEffect;
    
    [Header("Position Settings")]
    [Tooltip("Height offset above the target")]
    [SerializeField] private float heightOffset = 2f;
    
    [Header("Update Settings")]
    [SerializeField] private float updateInterval = 0.2f;


    private Transform playerTransform;
    private Transform targetTransform;
    private Camera mainCamera;
    private float nextUpdateTime;
    private bool isInitialized;

    public Transform TargetTransform => targetTransform;
    public Sprite MarkerIcon => icon?.sprite;

    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("Main Camera not found!");
            gameObject.SetActive(false);
            return;
        }

        playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (playerTransform == null)
        {
            Debug.LogError("Player not found! Ensure player has 'Player' tag.");
            gameObject.SetActive(false);
            return;
        }

        // Validate required components
        if (distanceText == null)
        {
            Debug.LogError("Distance Text reference missing on WorldSpaceObjectiveMarker!");
            gameObject.SetActive(false);
            return;
        }

        if (icon == null)
        {
            Debug.LogError("Icon reference missing on WorldSpaceObjectiveMarker!");
            gameObject.SetActive(false);
            return;
        }
    }

    public void Initialize(Transform target, Sprite markerIcon)
    {
        if (target == null)
        {
            Debug.LogError("Attempted to initialize marker with null target!");
            gameObject.SetActive(false);
            return;
        }

        targetTransform = target;
        if (icon != null && markerIcon != null)
        {
            icon.sprite = markerIcon;
        }
        isInitialized = true;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!isInitialized || targetTransform == null || playerTransform == null || 
            mainCamera == null || Time.time < nextUpdateTime)
        {
            return;
        }

        nextUpdateTime = Time.time + updateInterval;
        UpdateMarkerPosition();
        UpdateDistance();
    }

    private void UpdateMarkerPosition()
    {
        if (targetTransform == null || mainCamera == null || playerTransform == null)
            return;

        // Get the target's world position
        Vector3 targetPosition = targetTransform.position;
        
        // Add configurable offset above the target
        targetPosition.y += heightOffset;

        // Set the marker's world position directly
        transform.position = targetPosition;

        // Make the marker face the camera
        transform.LookAt(mainCamera.transform);
        transform.Rotate(0, 180, 0); // Flip to face camera
    }

    private void UpdateDistance()
    {
        if (targetTransform == null || playerTransform == null || distanceText == null)
            return;

        float distance = Vector3.Distance(playerTransform.position, targetTransform.position);
        distanceText.text = $"{distance:F0}m";
    }

    public void SetPulseActive(bool active)
    {
        if (pulseEffectA) pulseEffectA.SetActive(active);
        if (pulseEffectB) pulseEffectB.SetActive(active);
    }

    public void SetGlowActive(bool active)
    {
        if (glowEffect) glowEffect.SetActive(active);
    }

    private void OnDestroy()
    {
        // Clean up references
        targetTransform = null;
        playerTransform = null;
        mainCamera = null;
    }
} 