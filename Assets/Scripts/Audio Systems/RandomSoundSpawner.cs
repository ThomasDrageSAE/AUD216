using UnityEngine;
using System.Collections;

/*
 * RandomSoundSpawner.cs
 * 
 * Purpose: Spawns audio at random positions within a defined area during specified time periods
 * Used by: Environmental audio systems, ambient sound management
 * 
 * Key Features:
 * - Time-based sound activation
 * - Spatial randomization of sound positions
 * - Smooth audio transitions with fading
 * - Configurable timing and positioning
 * 
 * Performance Considerations:
 * - Uses coroutines for efficient timing
 * - Implements smooth volume transitions
 * - Validates configuration in editor
 * 
 * Dependencies:
 * - Requires AudioSource component
 * - Optional DayNightCycle integration
 * - Needs configured audio clips
 */

public class RandomSoundSpawner : MonoBehaviour
{
    [System.Serializable]
    public class TimeRange
    {
        [Tooltip("Time of day to start playing (0-1)")]
        [Range(0f, 1f)] public float startTime = 0f;
        
        [Tooltip("Time of day to stop playing (0-1)")]
        [Range(0f, 1f)] public float endTime = 1f;
    }

    [Header("Time Settings")]
    [Tooltip("When enabled, sounds will only play during specified time ranges")]
    [SerializeField] private bool useTimeRestriction = false;
    [SerializeField] private TimeRange[] activeTimeRanges;

    [Header("Position Settings")]
    [Tooltip("Maximum distance on the X-axis that the sound can spawn from the initial position")]
    [SerializeField] private float maxDistanceX = 10f;
    [Tooltip("Maximum distance on the Z-axis that the sound can spawn from the initial position")]
    [SerializeField] private float maxDistanceZ = 10f;
    [Tooltip("Maximum elevation on the Y-axis that the sound can spawn above the initial position")]
    [SerializeField] private float maxElevationY = 5f;

    [Header("Timing Settings")]
    [Tooltip("Minimum time interval between sound spawns")]
    [SerializeField] private float minTimeInterval = 5f;
    [Tooltip("Maximum time interval between sound spawns")]
    [SerializeField] private float maxTimeInterval = 15f;

    [Header("Audio Settings")]
    [Tooltip("Array of audio clips to be played randomly")]
    [SerializeField] private AudioClip[] audioClips;
    [Tooltip("If true, waits for the full clip to play before starting the next interval")]
    [SerializeField] private bool waitForFullClipPlay = true;
    [Tooltip("Maximum pitch variation applied to each sound (0 = no variation, 1 = maximum variation)")]
    [SerializeField] private float audioPitchVariation = 0.2f;
    [Tooltip("Fade in/out duration when starting/stopping sounds")]
    [SerializeField] private float fadeTime = 0.5f;

    private AudioSource audioSource;
    private Vector3 initialPosition;
    private DayNightCycle dayNightCycle;
    private Coroutine soundRoutine;
    private Coroutine fadeRoutine;
    private bool shouldBePlaying = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        initialPosition = transform.position;
        dayNightCycle = FindFirstObjectByType<DayNightCycle>();
    }

    private void Start()
    {
        if (!useTimeRestriction || IsTimeInRange())
        {
            StartSoundRoutine();
        }
    }

    private void Update()
    {
        if (!useTimeRestriction || activeTimeRanges == null || activeTimeRanges.Length == 0) return;

        bool isInRange = IsTimeInRange();
        
        if (isInRange && !shouldBePlaying)
        {
            StartSoundRoutine();
        }
        else if (!isInRange && shouldBePlaying)
        {
            StopSoundRoutine();
        }
    }

    private bool IsTimeInRange()
    {
        if (dayNightCycle == null || !useTimeRestriction || activeTimeRanges == null) return true;

        float currentTime = dayNightCycle.GetTimeOfDay();
        
        foreach (TimeRange range in activeTimeRanges)
        {
            if (range.startTime <= range.endTime)
            {
                // Simple case: start time is before end time
                if (currentTime >= range.startTime && currentTime <= range.endTime)
                    return true;
            }
            else
            {
                // Complex case: range crosses midnight
                if (currentTime >= range.startTime || currentTime <= range.endTime)
                    return true;
            }
        }
        
        return false;
    }

    private void StartSoundRoutine()
    {
        shouldBePlaying = true;
        if (soundRoutine == null)
        {
            soundRoutine = StartCoroutine(PlaySoundRoutine());
        }
    }

    private void StopSoundRoutine()
    {
        shouldBePlaying = false;
        if (soundRoutine != null)
        {
            StopCoroutine(soundRoutine);
            soundRoutine = null;
        }
        
        // Start fade out if sound is playing
        if (audioSource.isPlaying)
        {
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }
            fadeRoutine = StartCoroutine(FadeOut());
        }
    }

    private IEnumerator PlaySoundRoutine()
    {
        while (shouldBePlaying)
        {
            RepositionAndPlaySound();
            
            float waitTime = waitForFullClipPlay && audioSource.clip != null 
                ? audioSource.clip.length 
                : 0f;
                
            waitTime += Random.Range(minTimeInterval, maxTimeInterval);
            
            yield return new WaitForSeconds(waitTime);
        }
    }

    private void RepositionAndPlaySound()
    {
        // Randomly reposition
        float randomX = Random.Range(-maxDistanceX, maxDistanceX);
        float randomZ = Random.Range(-maxDistanceZ, maxDistanceZ);
        float randomY = Random.Range(0, maxElevationY);
        transform.position = initialPosition + new Vector3(randomX, randomY, randomZ);

        // Play random sound with pitch variation
        if (audioClips.Length > 0 && audioSource != null)
        {
            audioSource.clip = audioClips[Random.Range(0, audioClips.Length)];
            audioSource.pitch = 1f + Random.Range(-audioPitchVariation, audioPitchVariation);
            
            // Start fade in
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }
            fadeRoutine = StartCoroutine(FadeIn());
        }
    }

    private IEnumerator FadeIn()
    {
        audioSource.volume = 0f;
        audioSource.Play();
        
        float startTime = Time.time;
        float initialVolume = 0f;
        float targetVolume = 1f;
        
        while (Time.time < startTime + fadeTime)
        {
            float t = (Time.time - startTime) / fadeTime;
            audioSource.volume = Mathf.Lerp(initialVolume, targetVolume, t);
            yield return null;
        }
        
        audioSource.volume = targetVolume;
    }

    private IEnumerator FadeOut()
    {
        float startTime = Time.time;
        float initialVolume = audioSource.volume;
        
        while (Time.time < startTime + fadeTime)
        {
            float t = (Time.time - startTime) / fadeTime;
            audioSource.volume = Mathf.Lerp(initialVolume, 0f, t);
            yield return null;
        }
        
        audioSource.Stop();
        audioSource.volume = initialVolume;
    }

    private void OnValidate()
    {
        // Ensure minInterval is always less than or equal to maxInterval
        if (minTimeInterval > maxTimeInterval)
        {
            minTimeInterval = maxTimeInterval;
        }

        // Ensure AudioPitchVariation is within a reasonable range
        audioPitchVariation = Mathf.Clamp(audioPitchVariation, 0f, 1f);

        // Validate time ranges
        if (activeTimeRanges != null)
        {
            foreach (TimeRange range in activeTimeRanges)
            {
                range.startTime = Mathf.Clamp01(range.startTime);
                range.endTime = Mathf.Clamp01(range.endTime);
            }
        }
    }
}