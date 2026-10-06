using UnityEngine;
using System.Collections;

/*
 * BulletImpactManager.cs
 * 
 * Purpose: Manages bullet impact effects and sounds across different surfaces
 * Used by: Weapon system, impact visualization
 * 
 * Key Features:
 * - Surface-specific particle effects
 * - Delayed sound based on distance
 * - Blood effects for enemy impacts
 * - Configurable effect variations
 * - Sound propagation simulation
 * 
 * Effect Management:
 * - Automatic effect cleanup
 * - Sound pooling system
 * - Dynamic effect spawning
 * - Surface type detection
 * 
 * Performance Considerations:
 * - Efficient effect instantiation
 * - Smart audio source reuse
 * - Optimized cleanup routines
 * - Distance-based culling
 * 
 * Dependencies:
 * - Requires configured effect prefabs
 * - Audio source prefab system
 * - Surface tag configuration
 * - Physics raycast system
 */
public class BulletImpactManager : MonoBehaviour
{
    public static BulletImpactManager Instance { get; private set; }

    [System.Serializable]
    public class ImpactEffect
    {
        [Tooltip("Tag of the surface this effect should play on (e.g., 'Metal', 'Wood', 'Concrete')")]
        public string surfaceTag;

        [Tooltip("Particle system that plays when bullet hits this surface type")]
        public ParticleSystem particleEffect;

        [Tooltip("Array of impact sounds - one will be randomly selected when bullet hits this surface")]
        public AudioClip[] impactSounds;

        [Tooltip("Volume level for impact sounds on this surface")]
        [Range(0f, 1f)]
        public float volume = 1f;

        [Tooltip("Minimum pitch variation for impact sounds (lower values = deeper sounds)")]
        [Range(0.8f, 1.2f)]
        public float minPitch = 0.9f;

        [Tooltip("Maximum pitch variation for impact sounds (higher values = higher pitched sounds)")]
        [Range(0.8f, 1.2f)]
        public float maxPitch = 1.1f;
    }

    [Header("Impact Effects")]
    [Tooltip("Array of impact effects for different surface types (metal, wood, etc.)")]
    [SerializeField] private ImpactEffect[] impactEffects;

    [Tooltip("How long particle effects should remain visible before being destroyed")]
    [SerializeField] private float effectDuration = 1f;
    
    [Header("Enemy Impact")]
    [Tooltip("Particle effect to spawn when bullet hits an organic enemy (blood)")]
    [SerializeField] private ParticleSystem bloodEffect;

    [Tooltip("Array of sounds to play when bullet hits an enemy")]
    [SerializeField] private AudioClip[] enemyImpactSounds;

    [Tooltip("Volume level for enemy impact sounds")]
    [SerializeField] [Range(0f, 1f)] private float enemyImpactVolume = 1f;

    [Tooltip("Minimum pitch variation for enemy impact sounds (lower values = deeper sounds)")]
    [SerializeField] [Range(0.8f, 1.2f)] private float enemyMinPitch = 0.9f;

    [Tooltip("Maximum pitch variation for enemy impact sounds (higher values = higher pitched sounds)")]
    [SerializeField] [Range(0.8f, 1.2f)] private float enemyMaxPitch = 1.1f;

    [Header("Sound Settings")]
    [Tooltip("Speed in meters per second - affects delay of impact sounds based on distance")]
    [SerializeField] private float speedOfSound = 343f;

    [Tooltip("Master volume multiplier for all impact sounds")]
    [SerializeField] [Range(0f, 2f)] private float masterVolume = 1f;

    [Tooltip("Prefab for the audio source that will play impact sounds")]
    [SerializeField] private AudioSource impactAudioSourcePrefab;

    // A pool, not a single source. One shared source had to be teleported to each new
    // impact point and repitched, which dragged any still-ringing previous one-shot
    // across the level with it - very audible under automatic fire, where the
    // distance delay guarantees overlap.
    private const int ImpactVoiceCount = 4;
    private AudioSource[] impactAudioSources;
    private int nextImpactVoice;

    private Transform playerTransform;

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

        // Instantiate the audio sources from prefab (all settings/routing come from it)
        if (impactAudioSourcePrefab != null)
        {
            impactAudioSources = new AudioSource[ImpactVoiceCount];
            for (int i = 0; i < ImpactVoiceCount; i++)
            {
                impactAudioSources[i] = Instantiate(impactAudioSourcePrefab, transform);
                impactAudioSources[i].name = $"Impact Voice {i + 1}";
            }
        }
        else
        {
            Debug.LogError("Impact Audio Source Prefab not assigned!");
        }

        // Find player transform
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
    }

    public void PlayImpactEffect(RaycastHit hit)
    {
        // Spawn visual effects immediately
        if (hit.collider.CompareTag("Enemy"))
        {
            PlayEnemyEffect(hit);
            PlayDelayedImpactSound(enemyImpactSounds, hit.point, enemyImpactVolume, enemyMinPitch, enemyMaxPitch);
            return;
        }

        foreach (var effect in impactEffects)
        {
            if (hit.collider.CompareTag(effect.surfaceTag))
            {
                SpawnEffect(effect.particleEffect, hit);
                PlayDelayedImpactSound(effect.impactSounds, hit.point, effect.volume, effect.minPitch, effect.maxPitch);
                return;
            }
        }
    }

    /// <summary>
    /// Only ever reached from the CompareTag("Enemy") branch above, so the Rock test
    /// that used to live here could never pass - a GameObject has exactly one tag. Its
    /// rockEffect went with it: stone creatures already get rock particles and rock
    /// impact sounds from the surfaceTag loop, which is the better-sounding answer
    /// anyway. (WaveManager retags every spawned zombie "Enemy", so the Rock Golem -
    /// whose prefab root is tagged Rock - gets enemy impacts when spawned by a wave.)
    /// </summary>
    private void PlayEnemyEffect(RaycastHit hit)
    {
        if (bloodEffect != null)
        {
            SpawnEffect(bloodEffect, hit);
        }
    }

    private void SpawnEffect(ParticleSystem effectPrefab, RaycastHit hit)
    {
        ParticleSystem spawnedEffect = Instantiate(effectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        Destroy(spawnedEffect.gameObject, effectDuration);
    }

    private void PlayDelayedImpactSound(AudioClip[] sounds, Vector3 impactPoint, float baseVolume, float minPitch, float maxPitch)
    {
        if (sounds == null || sounds.Length == 0 || impactAudioSources == null || playerTransform == null) return;

        // Calculate distance from player to impact point
        float distance = Vector3.Distance(playerTransform.position, impactPoint);
        
        // Calculate delay based on distance and speed of sound
        float delay = distance / speedOfSound;

        // Calculate final volume
        float finalVolume = baseVolume * masterVolume;

        // Start coroutine to play sound after delay
        StartCoroutine(PlaySoundAfterDelay(sounds, impactPoint, finalVolume, minPitch, maxPitch, delay));
    }

    private System.Collections.IEnumerator PlaySoundAfterDelay(AudioClip[] sounds, Vector3 position, float volume, float minPitch, float maxPitch, float delay)
    {
        yield return new WaitForSeconds(delay);

        AudioClip randomSound = sounds[Random.Range(0, sounds.Length)];

        AudioSource source = TakeImpactVoice();
        if (source == null) yield break;

        // Pitch stays set while the one-shot plays — resetting it immediately
        // would cancel the variation on the playing clip. Each voice owns its own
        // pitch and position for the whole life of its one-shot.
        source.pitch = Random.Range(minPitch, maxPitch);
        source.transform.position = position;
        source.PlayOneShot(randomSound, volume);
    }

    /// <summary>
    /// Round-robin, preferring a voice that is not currently ringing so an overlapping
    /// impact does not steal a source that is still sounding at another position.
    /// </summary>
    private AudioSource TakeImpactVoice()
    {
        if (impactAudioSources == null || impactAudioSources.Length == 0) return null;

        for (int offset = 0; offset < impactAudioSources.Length; offset++)
        {
            int index = (nextImpactVoice + offset) % impactAudioSources.Length;
            AudioSource candidate = impactAudioSources[index];
            if (candidate != null && !candidate.isPlaying)
            {
                nextImpactVoice = (index + 1) % impactAudioSources.Length;
                return candidate;
            }
        }

        // All busy: take the next in rotation, which is the oldest.
        AudioSource fallback = impactAudioSources[nextImpactVoice];
        nextImpactVoice = (nextImpactVoice + 1) % impactAudioSources.Length;
        return fallback;
    }
} 