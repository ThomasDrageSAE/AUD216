using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;

public class PlayerAudioManager : MonoBehaviour
{
    public enum SoundType
    {
        Health,
        Stamina,
        Hunger
    }

    [System.Serializable]
    public class PlayerSoundProfile : IAudioSlotFilter
    {
        [Tooltip("Unique identifier for this sound profile")]
        public string profileName;
        
        [Tooltip("Type of player state that determines which audio properties are available")]
        public SoundType soundType;

        [Header("Audio Source")]
        [Tooltip("Optional: Specific AudioSource to use for this profile")]
        public AudioSource customAudioSource;
        [Tooltip("Audio Mixer Group for routing and effects processing")]
        public AudioMixerGroup mixerGroupOutput;

        // Health-specific properties
        [Header("Health Sounds")]
        [Tooltip("Played when taking damage")]
        public AudioClip[] damageSounds;
        
        [Header("Heartbeat")]
        [Tooltip("Played when health is critically low")]
        public AudioClip lowHealthLoop;
        [Tooltip("Dedicated AudioSource for heartbeat")]
        public AudioSource heartbeatAudioSource;
        [Tooltip("Health threshold below which the heartbeat sound starts playing")]
        [Range(0f, 100f)] public float playBelowThisHealth = 25f;
        [Range(0f, 1f)] public float damageVolume = 1f;
        [Range(0f, 1f)] public float lowHealthVolume = 0.7f;

        // Stamina-specific properties
        [Header("Stamina Sounds")]
        [Tooltip("Played when stamina is depleted")]
        public AudioClip staminaDepletedSound;
        [Tooltip("Played when stamina starts regenerating")]
        public AudioClip staminaRegeneratingSound;
        [Range(0f, 1f)] public float staminaVolume = 0.8f;

        // Hunger-specific properties
        [Header("Hunger Sounds")]
        [Tooltip("Played when hunger is critically low")]
        public AudioClip[] hungerSounds;
        [Tooltip("Played when eating/recovering hunger")]
        public AudioClip[] eatingSound;
        [Range(0f, 1f)] public float hungerVolume = 0.8f;
        [Range(0f, 1f)] public float eatingVolume = 1f;

        [Header("Hunger Loop")]
        [Tooltip("Played continuously while hunger stays in the warning band - the hunger twin of the heartbeat")]
        public AudioClip hungerLoop;
        [Tooltip("Dedicated AudioSource for the hunger loop. Leave empty and one is added at runtime; it is never the default source, which the heartbeat can claim")]
        public AudioSource hungerLoopAudioSource;
        [Tooltip("Hunger fraction below which the hunger loop starts playing. 0.25 = a quarter of a full belly, matching PlayerStats.hungerWarningFraction")]
        [Range(0f, 1f)] public float playBelowThisHungerFraction = 0.25f;
        [Range(0f, 1f)] public float hungerLoopVolume = 0.7f;

        // ---- Progress-report filter -------------------------------------------

        /// <summary>
        /// Every clip slot the class declares. A field outside this set is not a slot
        /// at all and is never hidden.
        /// </summary>
        private static readonly HashSet<string> AllClipFields = new HashSet<string>
        {
            nameof(damageSounds), nameof(lowHealthLoop),
            nameof(staminaDepletedSound), nameof(staminaRegeneratingSound),
            nameof(hungerSounds), nameof(eatingSound), nameof(hungerLoop),
        };

        /// <summary>
        /// The clip fields each sound type actually plays - the same split the custom
        /// inspector draws (PlayerAudioManagerEditor), so the progress report hides the
        /// rest. Every profile carries every field, and without this the Hunger entry
        /// listed a heartbeat loop and both stamina slots it will never play, and the
        /// Health and Stamina entries listed each other's: two thirds of the manager's
        /// rows were not work at all. Keep this and the inspector in step.
        /// </summary>
        public static IReadOnlyCollection<string> ClipFieldsFor(SoundType type)
        {
            switch (type)
            {
                case SoundType.Health: return HealthClips;
                case SoundType.Stamina: return StaminaClips;
                case SoundType.Hunger: return HungerClips;
                default: return AllClipFields;   // an unknown type hides nothing
            }
        }

        private static readonly string[] HealthClips = { nameof(damageSounds), nameof(lowHealthLoop) };
        private static readonly string[] StaminaClips = { nameof(staminaDepletedSound), nameof(staminaRegeneratingSound) };
        private static readonly string[] HungerClips = { nameof(hungerSounds), nameof(eatingSound), nameof(hungerLoop) };

        /// <summary>A clip slot this profile's type never plays is not outstanding work.</summary>
        public bool IsAudioSlotRelevant(string fieldName)
        {
            if (!AllClipFields.Contains(fieldName)) return true;

            foreach (string relevant in ClipFieldsFor(soundType))
            {
                if (relevant == fieldName) return true;
            }
            return false;
        }
    }

    [SerializeField] private PlayerSoundProfile[] soundProfiles;
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private AudioSource defaultAudioSource;

    private Coroutine lowHealthCoroutine;
    private bool isLowHealthPlaying;

    private Coroutine hungerLoopCoroutine;
    private bool isHungerLoopPlaying;

    // Added on first use when the Hunger profile authors no source of its own. NOT
    // defaultAudioSource: a loop takes ownership of a source's clip, and the heartbeat
    // falls back to that one, so sharing it would let whichever loop started second cut
    // the other off.
    private AudioSource hungerLoopFallbackSource;

    private void Awake()
    {
        InitializeAudioSources();
    }

    private void InitializeAudioSources()
    {
        if (defaultAudioSource == null)
        {
            defaultAudioSource = gameObject.AddComponent<AudioSource>();
            defaultAudioSource.playOnAwake = false;
        }

        if (soundProfiles == null) return;

        foreach (var profile in soundProfiles)
        {
            if (profile.customAudioSource != null)
            {
                profile.customAudioSource.playOnAwake = false;
                if (profile.mixerGroupOutput != null)
                {
                    profile.customAudioSource.outputAudioMixerGroup = profile.mixerGroupOutput;
                }
            }
        }
    }

    private int lastDamageIndex = -1;
    private int lastHungerIndex = -1;
    private int lastEatingIndex = -1;

    public void PlayDamageSound()
    {
        var profile = GetProfileByType(SoundType.Health);
        if (profile == null || profile.damageSounds == null || profile.damageSounds.Length == 0) return;

        AudioSource sourceToUse = profile.customAudioSource != null ? profile.customAudioSource : defaultAudioSource;
        AudioUtil.PlayRandomOneShot(sourceToUse, profile.damageSounds, ref lastDamageIndex, profile.damageVolume);
    }

    /// <summary>
    /// Re-evaluates whether the heartbeat should be playing.
    /// </summary>
    /// <param name="isLowHealth">
    /// Advisory only, deliberately not used as the condition. The heartbeat threshold is
    /// authored per-profile (playBelowThisHealth) so it can be mixed independently of
    /// PlayerStats.lowHealthThreshold, which drives the vignette. Changing this to honour
    /// the flag would move when the heartbeat starts, which is a mix decision.
    /// </param>
    public void UpdateLowHealthState(bool isLowHealth)
    {
        var profile = GetProfileByType(SoundType.Health);
        if (profile == null || profile.lowHealthLoop == null)
        {
            // A loop whose clip is cleared while it beats is still beating: tell it to stop.
            if (isLowHealthPlaying) StopLowHealthLoop();
            return;
        }

        // Get current health from PlayerStats
        float currentHealth = playerStats != null ? playerStats.GetCurrentHealth() : 0f;
        bool shouldPlayHeartbeat = currentHealth <= profile.playBelowThisHealth;

        if (shouldPlayHeartbeat && !isLowHealthPlaying)
        {
            StartLowHealthLoop(profile);
        }
        else if (!shouldPlayHeartbeat && isLowHealthPlaying)
        {
            StopLowHealthLoop();
        }
    }

    private void StartLowHealthLoop(PlayerSoundProfile profile)
    {
        if (lowHealthCoroutine != null)
        {
            StopCoroutine(lowHealthCoroutine);
        }
        lowHealthCoroutine = StartCoroutine(PlayLowHealthLoop(profile));
    }

    private IEnumerator PlayLowHealthLoop(PlayerSoundProfile profile)
    {
        AudioSource sourceToUse = profile.heartbeatAudioSource != null ? profile.heartbeatAudioSource : defaultAudioSource;
        sourceToUse.clip = profile.lowHealthLoop;
        sourceToUse.loop = true;
        isLowHealthPlaying = true;
        
        sourceToUse.Play();

        while (isLowHealthPlaying && playerStats != null)
        {
            float currentHealth = playerStats.GetCurrentHealth();
            
            // Calculate health percentage relative to the threshold. Guarded because the
            // slider reaches 0 - a threshold of 0 means "never beat", and a bare divide
            // would hand the mixer a NaN volume. Same guard the hunger loop uses below.
            float threshold = Mathf.Max(0.0001f, profile.playBelowThisHealth);
            float healthPercentage = (currentHealth / threshold) * 100f;
            
            // Map volume based on health percentage
            float minVolume = 0.02f;
            float maxVolume = 1f;
            float volumeScale;

            if (healthPercentage <= 10f) // Below 10% of threshold health
            {
                volumeScale = maxVolume;
            }
            else
            {
                // Map health percentage (10-100) to volume (1-0.02)
                float t = (healthPercentage - 10f) / 90f; // Normalize to 0-1 range
                volumeScale = Mathf.Lerp(maxVolume, minVolume, t);
            }
            
            // Apply the volume, scaled by the profile's authored ceiling. That
            // slider is surfaced in the inspector and holds a scene value, but the
            // computed curve used to be written raw - so it did nothing.
            sourceToUse.volume = volumeScale * profile.lowHealthVolume;

            // 10Hz, not per-frame: this value only changes with health.
            yield return new WaitForSeconds(0.1f);
        }

        sourceToUse.Stop();
        isLowHealthPlaying = false;
    }

    /// <summary>
    /// Stops the heartbeat immediately. Needed on death: health is 0 then, so
    /// UpdateLowHealthState would re-derive "should be beating" and keep it running.
    /// </summary>
    public void StopLowHealthLoop()
    {
        if (lowHealthCoroutine != null)
        {
            StopCoroutine(lowHealthCoroutine);
            lowHealthCoroutine = null;
        }

        var profile = GetProfileByType(SoundType.Health);
        if (profile != null)
        {
            AudioSource sourceToUse = profile.heartbeatAudioSource != null ? profile.heartbeatAudioSource : defaultAudioSource;
            sourceToUse.Stop();
        }
        isLowHealthPlaying = false;
    }

    public void PlayStaminaDepletedSound()
    {
        var profile = GetProfileByType(SoundType.Stamina);
        if (profile == null || profile.staminaDepletedSound == null) return;

        AudioSource sourceToUse = profile.customAudioSource != null ? profile.customAudioSource : defaultAudioSource;
        sourceToUse.PlayOneShot(profile.staminaDepletedSound, profile.staminaVolume);
    }

    public void PlayStaminaRegeneratingSound()
    {
        var profile = GetProfileByType(SoundType.Stamina);
        if (profile == null || profile.staminaRegeneratingSound == null) return;

        AudioSource sourceToUse = profile.customAudioSource != null ? profile.customAudioSource : defaultAudioSource;
        sourceToUse.PlayOneShot(profile.staminaRegeneratingSound, profile.staminaVolume);
    }

    public void PlayHungerSound()
    {
        var profile = GetProfileByType(SoundType.Hunger);
        if (profile == null || profile.hungerSounds == null || profile.hungerSounds.Length == 0) return;

        AudioSource sourceToUse = profile.customAudioSource != null ? profile.customAudioSource : defaultAudioSource;
        AudioUtil.PlayRandomOneShot(sourceToUse, profile.hungerSounds, ref lastHungerIndex, profile.hungerVolume);
    }

    public void PlayEatingSound()
    {
        var profile = GetProfileByType(SoundType.Hunger);
        if (profile == null || profile.eatingSound == null || profile.eatingSound.Length == 0) return;

        AudioSource sourceToUse = profile.customAudioSource != null ? profile.customAudioSource : defaultAudioSource;
        AudioUtil.PlayRandomOneShot(sourceToUse, profile.eatingSound, ref lastEatingIndex, profile.eatingVolume);
    }

    /// <summary>
    /// Re-evaluates whether the sustained hunger warning should be playing - the hunger
    /// twin of UpdateLowHealthState, and deliberately independent of it: a hurt, starving
    /// player hears both at once, on separate sources.
    /// </summary>
    /// <param name="isHungerLow">
    /// Advisory only, deliberately not used as the condition - the same arrangement the
    /// heartbeat uses. The loop's threshold is authored per-profile
    /// (playBelowThisHungerFraction) so it can be mixed independently of
    /// PlayerStats.hungerWarningFraction, which paces the one-shot hunger warnings.
    /// PlayerStats calls this on every hunger tick and whenever the player eats, so the
    /// profile threshold is free to differ from the one-shots' band.
    /// </param>
    public void UpdateHungerLoopState(bool isHungerLow)
    {
        var profile = GetProfileByType(SoundType.Hunger);
        if (profile == null || profile.hungerLoop == null)
        {
            // A loop whose clip is cleared while it plays is still playing: tell it to stop.
            if (isHungerLoopPlaying) StopHungerLoop();
            return;
        }

        // Read hunger as a fraction of a full belly - the unit PlayerStats already states
        // its own warning band in, and the one a profile threshold of 0.25 means.
        float hungerFraction = playerStats != null ? playerStats.GetHungerFraction() : 1f;
        bool shouldPlayHungerLoop = hungerFraction <= profile.playBelowThisHungerFraction;

        if (shouldPlayHungerLoop && !isHungerLoopPlaying)
        {
            StartHungerLoop(profile);
        }
        else if (!shouldPlayHungerLoop && isHungerLoopPlaying)
        {
            StopHungerLoop();
        }
    }

    private void StartHungerLoop(PlayerSoundProfile profile)
    {
        if (hungerLoopCoroutine != null)
        {
            StopCoroutine(hungerLoopCoroutine);
        }
        hungerLoopCoroutine = StartCoroutine(PlayHungerLoop(profile));
    }

    private IEnumerator PlayHungerLoop(PlayerSoundProfile profile)
    {
        AudioSource sourceToUse = ResolveHungerLoopSource(profile, true);
        sourceToUse.clip = profile.hungerLoop;
        sourceToUse.loop = true;
        isHungerLoopPlaying = true;

        sourceToUse.Play();

        while (isHungerLoopPlaying && playerStats != null)
        {
            // How far through the warning band the player is, the same reading the
            // heartbeat takes of health against its own threshold: 100 = just crossed in,
            // 0 = an empty belly. Guarded because the slider reaches 0, and a bare divide
            // would hand the mixer a NaN volume.
            float threshold = Mathf.Max(0.0001f, profile.playBelowThisHungerFraction);
            float hungerPercentage = (playerStats.GetHungerFraction() / threshold) * 100f;

            float minVolume = 0.02f;
            float maxVolume = 1f;
            float volumeScale;

            if (hungerPercentage <= 10f) // all but starved
            {
                volumeScale = maxVolume;
            }
            else
            {
                // Map hunger percentage (10-100) to volume (1-0.02)
                float t = (hungerPercentage - 10f) / 90f;
                volumeScale = Mathf.Lerp(maxVolume, minVolume, t);
            }

            // Scaled by the profile's authored ceiling, as the heartbeat's curve is.
            sourceToUse.volume = volumeScale * profile.hungerLoopVolume;

            // 10Hz, not per-frame: this value only changes with hunger.
            yield return new WaitForSeconds(0.1f);
        }

        sourceToUse.Stop();
        isHungerLoopPlaying = false;
    }

    /// <summary>
    /// Stops the hunger loop immediately. Needed on death for the reason the heartbeat is:
    /// starving to death leaves hunger at 0, so UpdateHungerLoopState would re-derive
    /// "should be looping" and keep it running - at full volume - through the whole death
    /// sequence.
    /// </summary>
    public void StopHungerLoop()
    {
        if (hungerLoopCoroutine != null)
        {
            StopCoroutine(hungerLoopCoroutine);
            hungerLoopCoroutine = null;
        }

        var profile = GetProfileByType(SoundType.Hunger);
        if (profile != null)
        {
            AudioSource sourceToUse = ResolveHungerLoopSource(profile, false);
            if (sourceToUse != null) sourceToUse.Stop();
        }
        isHungerLoopPlaying = false;
    }

    /// <summary>
    /// The hunger loop's own AudioSource: the authored one, else a spare added here on
    /// first use so dropping in a clip is all the authoring the loop needs. Deliberately
    /// never defaultAudioSource - the heartbeat falls back to that, and two loops on one
    /// source means the second Play() cuts the first off. The two warnings overlap by
    /// design.
    /// </summary>
    private AudioSource ResolveHungerLoopSource(PlayerSoundProfile profile, bool createIfMissing)
    {
        if (profile.hungerLoopAudioSource != null) return profile.hungerLoopAudioSource;
        if (hungerLoopFallbackSource != null || !createIfMissing) return hungerLoopFallbackSource;

        hungerLoopFallbackSource = gameObject.AddComponent<AudioSource>();
        hungerLoopFallbackSource.playOnAwake = false;
        if (profile.mixerGroupOutput != null)
        {
            hungerLoopFallbackSource.outputAudioMixerGroup = profile.mixerGroupOutput;
        }
        return hungerLoopFallbackSource;
    }

    private PlayerSoundProfile GetProfileByType(SoundType type)
    {
        if (soundProfiles == null) return null;
        
        foreach (var profile in soundProfiles)
        {
            if (profile.soundType == type)
                return profile;
        }
        return null;
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        if (soundProfiles != null)
        {
            foreach (var profile in soundProfiles)
            {
                if (profile != null && string.IsNullOrEmpty(profile.profileName))
                {
                    profile.profileName = profile.soundType.ToString();
                }
            }
        }
    }
    #endif
} 