using UnityEngine;

/*
 * DirectionalAudioSource.cs
 * 
 * Purpose: Provides directional audio filtering based on listener position and occlusion
 * Used by: Audio sources that need directional characteristics (e.g., speakers, megaphones)
 * 
 * Key Features:
 * - Simulates directional audio emission patterns
 * - Handles audio occlusion through obstacles
 * - Provides smooth transitions for audio filtering
 * - Dynamic volume and frequency adjustments
 * 
 * Performance Considerations:
 * - Uses efficient raycasting for occlusion checks
 * - Implements smooth parameter interpolation
 * - Caches component references
 * 
 * Dependencies:
 * - Requires AudioSource component
 * - Uses AudioLowPassFilter for frequency control
 * - Needs AudioListener in scene
 */

[RequireComponent(typeof(AudioSource))]
public class DirectionalAudioSource : MonoBehaviour
{
    // Directionality Variables
    [Tooltip("Draw the occlusion ray in the Scene view for this source")]
    [SerializeField] private bool debugDrawRay = false;

    [Header("Directivity Controls")]
    [Tooltip("The angle (in degrees) within which the audio has no filtering applied.")]
    [Range(0, 180)]
    public float onAxisAngle = 50f;

    [Tooltip("The angle (in degrees) at which the audio has maximum filtering applied.")]
    [Range(0, 180)]
    public float offAxisAngle = 180f;

    [Header("Off-Axis Adjustments")]
    [Tooltip("The frequency of the low-pass filter when the listener is at the off-axis angle.")]
    [Range(10, 22000)]
    public float frequency = 1000f;

    [Tooltip("The volume reduction (in decibels) when the listener is at the off-axis angle.")]
    [Range(0, 80)]
    public float volumeReduction = 20f;

    private const float onAxisFrequency = 22000f;

    // Occlusion Variables
    [Header("Occlusion Settings")]
    public LayerMask occlusionLayers;

    [Tooltip("Max distance to check for occlusion.")]
    public float maxDistance = 50f;

    [Tooltip("Speed at which the cutoff frequency and volume changes.")]
    public float transitionSpeed = 8f;

    [Range(0f, 1f)]
    public float occludedVolume = 0.5f;

    [Tooltip("Frequency to apply when the audio is occluded.")]
    public float occludedFrequency = 10000f;

    // Private Variables
    private AudioSource audioSource;
    private AudioLowPassFilter lowPassFilter;
    private float baseVolume;
    private AudioListener audioListener;
    private Transform player;
    private float targetCutoffFrequency = 22000f;
    private float targetVolume = 1f;
    private float currentAngle;

    // Debug display is formatted by the custom editor, not here: these three used to
    // be pre-built strings reassigned EVERY frame, on EVERY directional source, for a
    // readout only the inspector consumes - and two of them were already dead, because
    // the editor re-formats those from the numeric properties below.
    // Read-Only Properties for Editor Access
    public float CurrentListenerAngle => currentAngle;
    public float CurrentFilterFrequencyValue => lowPassFilter != null ? lowPassFilter.cutoffFrequency : frequency;
    public float CurrentVolumeReductionValue
    {
        get
        {
            float t = Mathf.InverseLerp(onAxisAngle, offAxisAngle, currentAngle);
            return Mathf.Lerp(0, volumeReduction, t);
        }
    }

    // Public Read-Only Property for Player Angle

    private void Awake()
    {
        // Initialize AudioSource and AudioLowPassFilter
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogError("AudioSource component not found on " + gameObject.name);
            enabled = false;
            return;
        }

        baseVolume = audioSource.volume;

        lowPassFilter = GetComponent<AudioLowPassFilter>();
        if (lowPassFilter == null)
        {
            lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
        }
        lowPassFilter.enabled = true;

        // Set default occlusion layers to 'Occluder' if not set
        if (occlusionLayers == 0)
        {
            int occluderLayer = LayerMask.NameToLayer("Occluder");
            if (occluderLayer != -1)
            {
                occlusionLayers = 1 << occluderLayer;
            }
        }
    }

    private void Start()
    {
        FindAudioListener();
    }

    private void FindAudioListener()
    {
        // Find and assign the AudioListener in the scene
        audioListener = FindFirstObjectByType<AudioListener>();
        if (audioListener != null)
        {
            player = audioListener.transform;
        }
        else
        {
            Debug.LogError("No AudioListener found in the scene.");
            enabled = false;
        }
    }

    private void Update()
    {
        if (audioListener != null && lowPassFilter != null)
        {
            // Check for occlusion
            bool isOccluded = CheckOcclusion();

            if (isOccluded)
            {
                // Apply occlusion settings
                targetCutoffFrequency = occludedFrequency;
                targetVolume = occludedVolume * baseVolume;

            }
            else
            {
                // Apply directionality settings
                UpdateDirectionality();
            }

            // Smoothly interpolate cutoff frequency and volume (unscaled so the sweep settles under
            // pause/death; Mathf.Lerp clamps t, so an unclamped hitch lands on the target, never past it)
            lowPassFilter.cutoffFrequency = Mathf.Lerp(lowPassFilter.cutoffFrequency, targetCutoffFrequency, Time.unscaledDeltaTime * transitionSpeed);
            audioSource.volume = Mathf.Lerp(audioSource.volume, targetVolume, Time.unscaledDeltaTime * transitionSpeed);
        }
        else if (audioListener == null)
        {
            FindAudioListener();
        }
    }

    private bool CheckOcclusion()
    {
        // Raycast to detect occlusion
        RaycastHit hit;
        Vector3 direction = player.position - transform.position;
        float distanceToListener = direction.magnitude;
        float rayDistance = Mathf.Min(maxDistance, distanceToListener);
        bool isOccluded = Physics.Raycast(transform.position, direction.normalized, out hit, rayDistance, occlusionLayers);

        // Verify if the hit object is within occlusion layers
        bool isActuallyOccluded = isOccluded && ((1 << hit.collider.gameObject.layer) & occlusionLayers) != 0 && hit.distance < distanceToListener;

        // Debug ray in the Scene view
        if (debugDrawRay)
        {
            Debug.DrawLine(transform.position, player.position, isActuallyOccluded ? Color.red : Color.green);
        }

        return isActuallyOccluded;
    }

    private void UpdateDirectionality()
    {
        // Calculate angle to listener
        Vector3 directionToListener = player.position - transform.position;
        currentAngle = Vector3.Angle(transform.forward, directionToListener);

        float t = Mathf.InverseLerp(onAxisAngle, offAxisAngle, currentAngle);

        // Update target cutoff frequency based on angle
        targetCutoffFrequency = Mathf.Lerp(onAxisFrequency, frequency, t);

        // Update target volume based on angle
        float volumeReductionDB = Mathf.Lerp(0, volumeReduction, t);
        targetVolume = baseVolume * Mathf.Pow(10, -volumeReductionDB / 20);
    }

    private void OnValidate()
    {
        // Ensure offAxisAngle is not less than onAxisAngle
        if (offAxisAngle < onAxisAngle)
        {
            offAxisAngle = onAxisAngle;
        }

        // Clamp angles to valid ranges
        onAxisAngle = Mathf.Clamp(onAxisAngle, 0f, 180f);
        offAxisAngle = Mathf.Clamp(offAxisAngle, 0f, 180f);
    }
}
