using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;

/*
 * WeaponAudioManager.cs
 * 
 * Purpose: Handles all weapon-related sound effects.
 * Used by: WeaponController, CrossbowController, GrenadeController,
 *          MinigunController
 * 
 * Manages weapon sound profiles including shooting, reloading,
 * and state change sounds. Provides randomization and variation
 * in weapon sounds for more dynamic audio.
 * 
 * Performance Considerations:
 * - Uses pooled audio sources
 * - Implements sound variation system
 * - Optimizes audio clip loading
 * 
 * Dependencies:
 * - AudioMixer system
 * - Weapon system
 * - Requires configured weapon sound profiles
 */

public enum WeaponType
{
    Gun,
    Axe,
    // Appended, never inserted: weaponType serializes as an int, so renumbering would
    // silently retype every profile already authored in the scene.
    Grenade,
    Minigun
}

/// <summary>Which part of the spin wind-down a tap hears - see WeaponSoundProfile.spinWinddownTail.</summary>
public enum WinddownTail
{
    StartOfClip,
    EndOfClip
}

public class WeaponAudioManager : MonoBehaviour
{
    #region Classes
    [System.Serializable]
    public class WeaponSoundProfile : IAudioSlotFilter
    {
        [Header("Weapon Info")]
        [Tooltip("Unique identifier for this weapon's sound profile")]
        public string weaponName;

        [Tooltip("Compile-checked identity for this profile. Leave Unassigned until Tools/Audio/Migrate Profile Keys To Enums has been run; once set, this name also works as a spelling of the profile.")]
        public WeaponId weaponId;
        
        [Tooltip("Array of audio clips that will be randomly selected when the weapon fires")]
        public AudioClip[] shootSounds;
        [Tooltip("Volume level for shooting sounds")]
        [Range(0f, 1f)] public float shootVolume = 1f;
        [Tooltip("Minimum pitch variation for shooting sounds")]
        [Range(0.8f, 1.2f)] public float minPitchVariation = 0.95f;
        [Tooltip("Maximum pitch variation for shooting sounds")]
        [Range(0.8f, 1.2f)] public float maxPitchVariation = 1.05f;
        
        [Header("Weapon State Sounds")]
        [Tooltip("Sound played when weapon is raised")]
        public AudioClip raiseWeaponSound;
        [Tooltip("Sound played when weapon is lowered")]
        public AudioClip lowerWeaponSound;
        [Tooltip("Volume level for raise/lower weapon sounds")]
        [Range(0f, 1f)] public float toggleSoundVolume = 1f;
        [Tooltip("Amount of random pitch variation applied to raise/lower sounds")]
        [Range(0f, 0.2f)] public float togglePitchVariation = 0.1f;
        
        [Header("Reload Sounds")]
        [Tooltip("Sound played when reload begins")]
        public AudioClip reloadStartSound;
        [Tooltip("Sound played when reload completes")]
        public AudioClip reloadEndSound;
        [Tooltip("Array of sounds played during the reload sequence")]
        public AudioClip[] reloadActionSounds;
        [Tooltip("Volume level for all reload-related sounds")]
        [Range(0f, 1f)] public float reloadVolume = 1f;

        [Header("Grenade Sounds")]
        [Tooltip("Clips randomly selected as the grenade leaves the hand. Heard at the player, like a shot - the grenade is still in front of the camera when this fires")]
        public AudioClip[] throwSounds;
        [Tooltip("Volume level for throw sounds")]
        [Range(0f, 1f)] public float throwVolume = 1f;
        [Tooltip("Clips randomly selected when the grenade detonates. This is the one weapon sound in the level that plays AT THE GRENADE, in 3D: it arrives from wherever the grenade landed and falls off with distance, so author it to read across the level rather than to sit in front of the camera")]
        public AudioClip[] explosionSounds;
        [Tooltip("Volume level for explosion sounds")]
        [Range(0f, 1f)] public float explosionVolume = 1f;
        [Tooltip("Distance in metres at which the explosion has faded to nothing. Deliberately far larger than any gunshot: a blast the player cannot hear from across the level reads as a bug rather than as distance")]
        public float explosionMaxDistance = 80f;

        [Header("Minigun Sounds")]
        [Tooltip("Plays from the moment the trigger goes down. Its LENGTH is the wind-up: the barrels reach speed and the first round leaves exactly when this clip ends, so a longer clip is a slower gun. Letting go early stops it where it is; pressing again resumes it from wherever the barrels are")]
        [UnityEngine.Serialization.FormerlySerializedAs("spinUpSound")]
        public AudioClip spinWindupSound;
        [Tooltip("The barrels coasting to a stop after a trigger let go DURING the wind-up - a tap, before any round left the gun. N seconds of it play, where N is how long the wind-up ran (a 0.2 s press hears 0.2 s of wind-up and then 0.2 s of this), and the barrels stop turning exactly when it ends. Which N seconds is the setting below. A release after rounds have left the gun plays Shooting Shutdown instead, never this")]
        public AudioClip spinWinddownSound;
        [Tooltip("Which part of Spin Winddown a tap hears. START OF CLIP: its first N seconds, faded out over the last 50 ms - the loud part of a wind-down that decays to nothing, which is what a wind-down usually is. END OF CLIP: its last N seconds - only audible if the clip is authored to stay loud right up to the stop. Measured 2026-09-06 on the owner's clip: its last half second sits at -46 to -67 dBFS, so END OF CLIP was silence for every tap under half a second")]
        public WinddownTail spinWinddownTail = WinddownTail.StartOfClip;
        [Tooltip("The barrels at speed - loops underneath the shooting loop for as long as rounds are leaving the gun, and stops DEAD the instant the trigger is released. Must be a SEAMLESS loop: silence, a fade or a stray transient at either end is heard as a tick every time it wraps, and a minigun wraps it many times in a single burst")]
        public AudioClip spinLoopSound;
        [Tooltip("The first rounds. Plays the instant the wind-up ends, and the shooting loop takes over at the exact sample this clip finishes - so it must end where the loop begins")]
        public AudioClip shootingStartupSound;
        [Tooltip("Sustained fire. Starts exactly where the startup clip ends and runs until the trigger is released. Must be a SEAMLESS loop, for the same reason as the spin loop")]
        public AudioClip shootingLoopSound;
        [Tooltip("The gun stopping. Plays the instant the trigger is released after rounds have left the gun - both loops stop dead at that moment - and is cut short if the trigger goes down again. Its LENGTH is the wind-down: the barrels stop turning exactly when this clip ends. Not played when the trigger is let go during the wind-up: nothing fired, so nothing shuts down")]
        public AudioClip shootingShutdownSound;
        [Tooltip("Volume for the wind-up and the spin loop. The three shooting clips use Shoot Volume above")]
        [Range(0f, 1f)] public float spinVolume = 1f;

        [Header("Weapon Type")]
        [Tooltip("Type of weapon that determines which audio properties are available")]
        public WeaponType weaponType;

        [System.NonSerialized] public int lastShootIndex = -1;
        // The same no-repeat memory as the shots, one per grenade slot, so two throws or
        // two blasts in a row can never land on the same clip.
        [System.NonSerialized] public int lastThrowIndex = -1;
        [System.NonSerialized] public int lastExplosionIndex = -1;

        /// <summary>
        /// Keeps the slots a weapon type can never play out of the Ctrl+G progress report.
        /// A melee weapon has no magazine and a gun has nothing to detonate, so those
        /// clips are not missing audio - counting them left the report permanently short
        /// of 100% with nothing to author.
        /// </summary>
        /// <remarks>
        /// A switch on weaponType rather than on the name, so a second melee weapon or a
        /// second throwable is handled without editing this, and the next weapon type is
        /// one more case. The crossbow is deliberately NOT special-cased: it is a Gun to
        /// this enum and nocking an arrow is a reload worth hearing.
        ///
        /// Only the clip fields are listed. The volume and distance fields beside them are
        /// floats, and the scanner records AudioClip leaves only - hiding a float would
        /// change nothing in the report, so naming them here would only mislead.
        /// </remarks>
        public bool IsAudioSlotRelevant(string fieldName)
        {
            switch (weaponType)
            {
                case WeaponType.Gun:
                    // Nothing to throw, nothing to detonate, and no barrels to spin.
                    return fieldName != nameof(throwSounds)
                        && fieldName != nameof(explosionSounds)
                        && !IsMinigunOnlyField(fieldName);

                case WeaponType.Axe:
                    // No magazine either, on top of everything a gun cannot do.
                    return fieldName != nameof(reloadStartSound)
                        && fieldName != nameof(reloadEndSound)
                        && fieldName != nameof(reloadActionSounds)
                        && fieldName != nameof(throwSounds)
                        && fieldName != nameof(explosionSounds)
                        && !IsMinigunOnlyField(fieldName);

                case WeaponType.Grenade:
                    // Thrown rather than fired, and there is nothing to reload. Raise and
                    // lower are hidden too, which is the one place this filter departs from
                    // "the weapon can make this sound": a grenade IS raised and lowered in
                    // hand, so the two clips would play if authored. The owner chose two
                    // assessed slots for the grenade - Throw and Explosion - and the report
                    // is the list of work a student is marked on, so it lists two. Show them
                    // again by deleting the two raise/lower clauses below.
                    return fieldName != nameof(shootSounds)
                        && fieldName != nameof(raiseWeaponSound)
                        && fieldName != nameof(lowerWeaponSound)
                        && fieldName != nameof(reloadStartSound)
                        && fieldName != nameof(reloadEndSound)
                        && fieldName != nameof(reloadActionSounds)
                        && !IsMinigunOnlyField(fieldName);

                case WeaponType.Minigun:
                    // Raise, lower and the three reload clips as a Gun has them, plus the
                    // five this type exists for: wind-up, spin loop, shooting startup,
                    // shooting loop, shutdown. NOT shootSounds - since 2026-09-06 the
                    // minigun has no per-round shot, the shooting LOOP is the gunfire,
                    // and a slot the weapon never plays is a clip a student would be
                    // marked on and could never hear.
                    return fieldName != nameof(throwSounds)
                        && fieldName != nameof(explosionSounds)
                        && fieldName != nameof(shootSounds);

                default:
                    // An unrecognised type hides nothing: a row too many is a visible
                    // mistake, a hidden slot is a sound the report silently stops asking
                    // for.
                    return true;
            }
        }

        /// <summary>The five clips only a minigun plays. One list, so the three other types hide the same five.</summary>
        private static bool IsMinigunOnlyField(string fieldName)
        {
            return fieldName == nameof(spinWindupSound)
                || fieldName == nameof(spinWinddownSound)
                || fieldName == nameof(spinLoopSound)
                || fieldName == nameof(shootingStartupSound)
                || fieldName == nameof(shootingLoopSound)
                || fieldName == nameof(shootingShutdownSound);
        }
    }
    #endregion

    #region Private Fields
    [SerializeField] private WeaponSoundProfile[] m_WeaponProfiles;
    [SerializeField] private AudioSource m_WeaponAudioSource;
    [SerializeField] private AudioMixerGroup m_AudioMixerGroup;
    
    private Dictionary<string, WeaponSoundProfile> m_ProfileLookup;
    private Dictionary<string, AudioSource> m_WeaponAudioSources;

    // One dedicated looping source per weapon that actually spins, added on first
    // use. Keyed on the RESOLVED profile's own name rather than on whatever the
    // caller spelled, so a start addressed by id name and a stop addressed by the
    // authored name land on the same source.
    private Dictionary<string, AudioSource> m_SpinLoopSources;

    // Two more, same arrangement, added 2026-09-06 when the shots became a loop: the
    // shooting loop's source, and a "transition" source that plays the wind-up, the
    // startup and the shutdown in turn. Three sources because three things overlap - a
    // minigun winds up, then fires two loops at once, then shuts down - and because the
    // transition clips have to be STOPPABLE and RESUMABLE from an offset, which PlayOneShot
    // voices are not: Stop() on a PlayOneShot source kills every voice on it, and there is
    // no way to start one part-way through.
    private Dictionary<string, AudioSource> m_ShootLoopSources;
    private Dictionary<string, AudioSource> m_TransitionSources;

    // A tail that has to END before its clip does: the wind-down's first N seconds. Held
    // here and driven from Update - faded over the last TailFadeSeconds, then stopped -
    // because a bare SetScheduledEndTime is a hard cut, and a hard cut in the middle of a
    // wind-down is a click. Cleared by anything that takes the transition source over.
    private AudioSource m_TailSource;
    private double m_TailEndDsp;
    private float m_TailVolume;
    private const float TailFadeSeconds = 0.05f;
    #endregion

    #region Public Properties
    public AudioSource WeaponAudioSource
    {
        get => m_WeaponAudioSource;
        set => m_WeaponAudioSource = value;
    }

    /// <summary>
    /// The bus every weapon sound lands on, for a source this manager did not create - the
    /// grenade's ringing tail, which lives on its own throwaway host so it can outlive the
    /// grenade. Exactly what the detached explosion host uses in PlayExplosionSound, so a
    /// sound routed through here cannot end up on a different fader from the blast it
    /// belongs to. A source left on Master ignores the SFX fader and the volume slider.
    /// </summary>
    public AudioMixerGroup OutputGroup => m_WeaponAudioSource != null
        ? m_WeaponAudioSource.outputAudioMixerGroup
        : m_AudioMixerGroup;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        InitializeAudioSources();
    }

    private void OnValidate()
    {
        // Ensure weapon profiles have unique names
        if (m_WeaponProfiles != null)
        {
            // Matches the runtime lookup: names differing only by case or padding collide.
            HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var profile in m_WeaponProfiles)
            {
                if (profile == null) continue;

                string name = AudioProfileKeys.Normalize(profile.weaponName);
                if (string.IsNullOrEmpty(name)) continue;

                if (!names.Add(name))
                {
                    Debug.LogWarning($"Duplicate weapon profile name found: {name}");
                }
            }
        }
    }

    /// <summary>
    /// A loop outlives the frame that started it, so it needs stopping by something
    /// other than itself. Every other weapon sound is a one-shot that ends on its own;
    /// the spin does not. Disabling or destroying this manager - which is what a scene
    /// reload does to it, and a reload is this single-scene game's restart path - must
    /// not leave a minigun spinning underneath the next run.
    /// </summary>
    private void OnDisable()
    {
        StopAllMinigunAudio();
    }
    #endregion

    #region Private Methods
    private void InitializeAudioSources()
    {
        // Initialize the main weapon audio source if not set
        if (m_WeaponAudioSource == null)
        {
            m_WeaponAudioSource = gameObject.AddComponent<AudioSource>();
            m_WeaponAudioSource.playOnAwake = false;
            m_WeaponAudioSource.spatialBlend = 0f; // Full 2D as these are player's weapons
        }

        if (m_AudioMixerGroup != null)
        {
            m_WeaponAudioSource.outputAudioMixerGroup = m_AudioMixerGroup;
        }

        m_ProfileLookup = AudioProfileKeys.CreateLookup<WeaponSoundProfile>();
        m_WeaponAudioSources = AudioProfileKeys.CreateLookup<AudioSource>();

        // Empty, not populated: a spin source is added the first time a weapon actually
        // spins, so the four weapons that never will do not each carry a silent one.
        m_SpinLoopSources = AudioProfileKeys.CreateLookup<AudioSource>();
        m_ShootLoopSources = AudioProfileKeys.CreateLookup<AudioSource>();
        m_TransitionSources = AudioProfileKeys.CreateLookup<AudioSource>();

        if (m_WeaponProfiles == null) return;

        foreach (var profile in m_WeaponProfiles)
        {
            if (profile == null) continue;

            int before = m_ProfileLookup.Count;
            AudioProfileKeys.AddProfile(m_ProfileLookup, profile.weaponName, profile, this, "WeaponAudioManager");

            // Skipped as blank or duplicate - don't give it a voice of its own.
            if (m_ProfileLookup.Count == before) continue;

            // Create dedicated audio source for each weapon
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            
            if (m_AudioMixerGroup != null)
            {
                source.outputAudioMixerGroup = m_AudioMixerGroup;
            }
            
            m_WeaponAudioSources[AudioProfileKeys.Normalize(profile.weaponName)] = source;
        }

        // Second pass: aliases only for profiles the lookup actually serves - a
        // duplicate that pass 1 rejected must not smuggle itself back in via its id
        // (nor bind another weapon's dedicated AudioSource to its alias).
        foreach (var profile in m_WeaponProfiles)
        {
            if (profile == null) continue;
            if (!AudioProfileKeys.Owns(m_ProfileLookup, profile.weaponName, profile)) continue;
            if (!m_WeaponAudioSources.TryGetValue(AudioProfileKeys.Normalize(profile.weaponName),
                    out AudioSource source)) continue;

            AudioProfileKeys.AddIdAlias(m_ProfileLookup, profile.weaponId, profile);
            AudioProfileKeys.AddIdAlias(m_WeaponAudioSources, profile.weaponId, source);
        }
    }

    private bool TryGetProfile(string weaponName, out WeaponSoundProfile profile)
    {
        return AudioProfileKeys.TryGetProfile(m_ProfileLookup, weaponName, out profile,
            this, "WeaponAudioManager");
    }

    /// <summary>
    /// Id-addressed lookup for callers once they stop passing strings. Resolves through the
    /// same dictionary, where an assigned id is registered under its own name.
    /// </summary>
    public bool TryGetProfile(WeaponId weaponId, out WeaponSoundProfile profile)
    {
        // Unassigned is "no id", not a name - looking it up would warn about a
        // profile called 'Unassigned' that was never supposed to exist.
        if (!AudioProfileIds.IsAssigned(weaponId)) { profile = null; return false; }
        return TryGetProfile(AudioProfileIds.NameOf(weaponId), out profile);
    }

    /// <summary>
    /// One of the minigun's three dedicated AudioSources, added on first use - the hunger
    /// loop's arrangement in PlayerAudioManager, three per spinning weapon instead of one
    /// per manager.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the weapon's shoot source, for two reasons that both bite on this
    /// weapon specifically:
    ///  - PlayShootSound re-randomises that source's pitch on every shot, and pitch is a
    ///    property of the SOURCE, not of the voice. Anything sustained on it would warble.
    ///  - a loop takes ownership of a source's clip, so stopping it would cut off whatever
    ///    else that source happened to be playing.
    /// The key is the resolved profile's authored name, so every spelling that reaches the
    /// same profile also reaches the same source. Null before Awake has run.
    /// </remarks>
    private AudioSource ResolveMinigunSource(Dictionary<string, AudioSource> table,
                                             WeaponSoundProfile profile, bool createIfMissing)
    {
        if (table == null) return null;

        string key = AudioProfileKeys.Normalize(profile.weaponName);
        if (string.IsNullOrEmpty(key)) return null;

        if (table.TryGetValue(key, out AudioSource source) && source != null) return source;
        if (!createIfMissing) return null;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D, like every other weapon sound at the player
        source.outputAudioMixerGroup = m_AudioMixerGroup != null
            ? m_AudioMixerGroup
            : (m_WeaponAudioSource != null ? m_WeaponAudioSource.outputAudioMixerGroup : null);
        table[key] = source;
        return source;
    }

    /// <summary>
    /// Silences everything a minigun plays, without needing a profile lookup to succeed
    /// first - the one stop path that still works when the profiles have been renamed or
    /// emptied underneath a running loop.
    /// </summary>
    private void StopAllMinigunAudio()
    {
        ClearTailEnd();
        StopAll(m_SpinLoopSources);
        StopAll(m_ShootLoopSources);
        StopAll(m_TransitionSources);
    }

    private static void StopAll(Dictionary<string, AudioSource> table)
    {
        if (table == null) return;
        foreach (AudioSource source in table.Values)
        {
            if (source != null) source.Stop();
        }
    }

    private static void StopIfAny(Dictionary<string, AudioSource> table, WeaponSoundProfile profile)
    {
        if (table == null) return;
        string key = AudioProfileKeys.Normalize(profile.weaponName);
        if (!string.IsNullOrEmpty(key) && table.TryGetValue(key, out AudioSource source) && source != null)
        {
            source.Stop();
        }
    }

    /// <summary>
    /// Fades and stops a wind-down tail that is due to end before its clip does. Runs only
    /// while one is pending; a null m_TailSource costs a compare per frame and nothing else.
    /// </summary>
    private void Update()
    {
        if (m_TailSource == null) return;

        double remaining = m_TailEndDsp - AudioSettings.dspTime;

        if (remaining <= 0d || !m_TailSource.isPlaying)
        {
            m_TailSource.Stop();
            m_TailSource.volume = m_TailVolume;
            m_TailSource = null;
            return;
        }

        if (remaining < TailFadeSeconds)
        {
            m_TailSource.volume = m_TailVolume * (float)(remaining / TailFadeSeconds);
        }
    }

    /// <summary>
    /// Forgets a pending tail end, restoring the source's authored volume. Called by every
    /// path that puts something new on the transition source, so a wind-down's scheduled
    /// end cannot cut short the wind-up that replaced it.
    /// </summary>
    private void ClearTailEnd()
    {
        if (m_TailSource != null) m_TailSource.volume = m_TailVolume;
        m_TailSource = null;
    }

    /// <summary>
    /// A clip's length from its sample count, for scheduling. AudioClip.length is a float
    /// and a float second is about 60 ns short at 44.1 kHz - inaudible once, but this is
    /// the number the shooting loop is scheduled against, and a handoff a few samples early
    /// is a click every burst.
    /// </summary>
    private static double ExactLength(AudioClip clip) => (double)clip.samples / clip.frequency;
    #endregion

    #region Public Methods
    public void PlayShootSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        // Use the weapon's dedicated source so the pitch change can't affect
        // other sounds playing on the shared source.
        if (!m_WeaponAudioSources.TryGetValue(AudioProfileKeys.Normalize(weaponName), out AudioSource source))
        {
            source = m_WeaponAudioSource;
        }

        AudioUtil.PlayRandomOneShot(source, profile.shootSounds, ref profile.lastShootIndex,
            profile.shootVolume, profile.minPitchVariation, profile.maxPitchVariation);
    }

    public void PlayWeaponRaiseSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        if (profile.raiseWeaponSound != null)
        {
            m_WeaponAudioSource.pitch = 1f + Random.Range(-profile.togglePitchVariation, profile.togglePitchVariation);
            m_WeaponAudioSource.PlayOneShot(profile.raiseWeaponSound, profile.toggleSoundVolume);
        }
    }

    public void PlayWeaponLowerSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        if (profile.lowerWeaponSound != null)
        {
            m_WeaponAudioSource.pitch = 1f + Random.Range(-profile.togglePitchVariation, profile.togglePitchVariation);
            m_WeaponAudioSource.PlayOneShot(profile.lowerWeaponSound, profile.toggleSoundVolume);
        }
    }

    public void PlayReloadStartSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        if (profile.reloadStartSound != null)
        {
            m_WeaponAudioSource.pitch = 1f;
            m_WeaponAudioSource.PlayOneShot(profile.reloadStartSound, profile.reloadVolume);
            AudioEventLog.Record(AudioEventKind.Play, m_WeaponAudioSource.gameObject.name,
                                 profile.reloadStartSound.name);
        }
    }

    public void PlayReloadEndSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        if (profile.reloadEndSound != null)
        {
            m_WeaponAudioSource.pitch = 1f;
            m_WeaponAudioSource.PlayOneShot(profile.reloadEndSound, profile.reloadVolume);
            AudioEventLog.Record(AudioEventKind.Play, m_WeaponAudioSource.gameObject.name,
                                 profile.reloadEndSound.name);
        }
    }

    public void PlayReloadActionSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        if (profile.reloadActionSounds != null && profile.reloadActionSounds.Length > 0)
        {
            AudioClip randomReloadSound = profile.reloadActionSounds[Random.Range(0, profile.reloadActionSounds.Length)];
            // A null element is a MISSING row the student has not filled yet (the SMG's and
            // the revolver's are dangling references today), not a clip: PlayOneShot(null)
            // logs a warning on every reload. AudioUtil.PlayRandomOneShot makes the same check.
            if (randomReloadSound == null) return;
            m_WeaponAudioSource.pitch = 1f;
            m_WeaponAudioSource.PlayOneShot(randomReloadSound, profile.reloadVolume);
        }
    }

    /// <summary>
    /// The throw, heard at the player exactly like a shot: the grenade is still in hand
    /// when this fires, so it takes the same per-weapon 2D source PlayShootSound uses.
    /// </summary>
    public void PlayThrowSound(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        // Use the weapon's dedicated source so the pitch change can't affect
        // other sounds playing on the shared source.
        if (!m_WeaponAudioSources.TryGetValue(AudioProfileKeys.Normalize(weaponName), out AudioSource source))
        {
            source = m_WeaponAudioSource;
        }

        AudioUtil.PlayRandomOneShot(source, profile.throwSounds, ref profile.lastThrowIndex,
            profile.throwVolume, profile.minPitchVariation, profile.maxPitchVariation);
    }

    /// <summary>
    /// The detonation, played AT the blast point in 3D - the only weapon sound in the
    /// level that does not come from the camera, and the reason the grenade is worth
    /// authoring: it arrives from where the grenade landed and falls off with distance.
    /// </summary>
    /// <remarks>
    /// Deliberately DETACHED onto a throwaway GameObject rather than played on the
    /// grenade's own source: the grenade is destroyed on the same frame it detonates, and
    /// a one-shot on a source that dies with its object is cut off mid-blast. The
    /// temporary object outlives the clip and then deletes itself.
    ///
    /// It borrows the weapon source's mixer group so the explosion lands on the same bus
    /// as every other weapon sound - a blast routed around the mixer would ignore the
    /// levels and effects authored on that bus and sit oddly loud against the guns.
    /// </remarks>
    public void PlayExplosionSound(string weaponName, Vector3 position)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;
        if (profile.explosionSounds == null || profile.explosionSounds.Length == 0) return;

        // The clip is picked here rather than inside AudioUtil.PlayRandomOneShot because
        // its length decides how long the temporary object has to live; the no-repeat rule
        // itself is still AudioUtil's.
        int index = AudioUtil.GetRandomIndexNoRepeat(profile.explosionSounds.Length, profile.lastExplosionIndex);
        AudioClip clip = profile.explosionSounds[index];
        if (clip == null) return;
        profile.lastExplosionIndex = index;

        GameObject host = new GameObject("Explosion Audio");
        host.transform.position = position;

        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;                            // fully 3D - the whole point
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 5f;                             // full volume inside the blast radius
        source.maxDistance = profile.explosionMaxDistance;
        source.outputAudioMixerGroup = m_WeaponAudioSource != null
            ? m_WeaponAudioSource.outputAudioMixerGroup
            : m_AudioMixerGroup;

        // Same pitch rule AudioUtil applies to every other one-shot: two blasts in a row
        // should not be bit-identical.
        source.pitch = (profile.minPitchVariation == 1f && profile.maxPitchVariation == 1f)
            ? 1f
            : Random.Range(profile.minPitchVariation, profile.maxPitchVariation);

        source.PlayOneShot(clip, profile.explosionVolume);
        AudioEventLog.Record(AudioEventKind.Play, host.name, clip.name);

        // Pitched DOWN, a clip takes longer than its nominal length to finish, so the
        // lifetime is divided by the pitch - a flat clip.length would clip the tail.
        Destroy(host, clip.length / Mathf.Max(source.pitch, 0.01f) + 0.1f);
    }

    // ---- The minigun: five clips, three sources, one order ----------------------------
    //
    // trigger down   StartSpinWindup   the wind-up clip, from the barrel's current speed
    // wind-up ends   BeginShooting     spin loop now; startup now; shooting loop scheduled
    //                                  for the exact sample the startup ends
    // trigger up     EndShooting       both loops stop DEAD; then the shutdown if rounds had
    //                                  left the gun, or the LAST N seconds of the spin
    //                                  wind-down if they had not - N being how long the
    //                                  wind-up ran
    // died / torn    StopMinigunAudio  everything stops, no shutdown
    //
    // The owner's design, 2026-09-06. The controller decides WHEN (it owns the barrel and
    // the trigger); this decides WHAT plays and on which source.

    /// <summary>
    /// Starts, or resumes, the wind-up. <paramref name="fromFraction"/> is how far the
    /// barrels have already spun up (0..1), and the clip starts from the matching offset:
    /// a barrel still turning from a moment ago does not get the whole wind-up again.
    /// Cuts a shutdown that is still playing on the same source - a re-press mid-shutdown
    /// is the gun starting again, and it should sound like it.
    /// </summary>
    /// <returns>The wind-up clip's length in seconds - the controller's wind-up time - or
    /// 0 when no clip is authored, in which case the controller falls back to its own.</returns>
    public float StartSpinWindup(string weaponName, float fromFraction)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return 0f;

        AudioClip clip = profile.spinWindupSound;
        if (clip == null) return 0f;

        AudioSource source = ResolveMinigunSource(m_TransitionSources, profile, true);
        if (source == null) return 0f;

        ClearTailEnd();
        source.Stop();
        source.clip = clip;
        source.loop = false;
        source.pitch = 1f;
        source.volume = profile.spinVolume;

        // Not past the end: AudioSource.time at or beyond the clip length is an error, and
        // a fraction of exactly 1 never reaches here (the controller goes straight to
        // firing), so the clamp is for rounding only.
        source.time = Mathf.Min(Mathf.Clamp01(fromFraction) * clip.length, Mathf.Max(0f, clip.length - 0.01f));
        source.Play();

        AudioEventLog.Record(AudioEventKind.Play, source.gameObject.name, clip.name);
        return clip.length;
    }

    /// <summary>
    /// The wind-up has ended and rounds are leaving the gun. The spin loop and the startup
    /// begin together, one audio buffer from now, and the shooting loop is scheduled for
    /// the exact sample the startup ends - PlayScheduled on the DSP clock, not Play on the
    /// next frame, because a frame is 16 ms and the seam between two clips of gunfire is
    /// heard at 1 ms.
    /// </summary>
    public void BeginShooting(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        // Slightly ahead of the DSP clock, so "now" is a time the mixer has not yet
        // rendered: a schedule in the past plays late and unpredictably.
        double start = AudioSettings.dspTime + 0.02;

        AudioSource spin = ResolveMinigunSource(m_SpinLoopSources, profile, true);
        if (spin != null && profile.spinLoopSound != null)
        {
            if (spin.isPlaying && spin.clip == profile.spinLoopSound)
            {
                spin.volume = profile.spinVolume;      // already running: leave the playhead alone
            }
            else
            {
                spin.Stop();
                spin.clip = profile.spinLoopSound;
                spin.loop = true;
                spin.pitch = 1f;
                spin.volume = profile.spinVolume;
                spin.time = 0f;
                spin.PlayScheduled(start);
                AudioEventLog.Record(AudioEventKind.Play, spin.gameObject.name, profile.spinLoopSound.name);
            }
        }

        double loopStart = start;

        AudioSource transition = ResolveMinigunSource(m_TransitionSources, profile, true);
        if (transition != null)
        {
            // Whatever is on it - the wind-up's last few milliseconds, or nothing - ends
            // here: the startup IS the moment the wind-up is over.
            ClearTailEnd();
            transition.Stop();

            if (profile.shootingStartupSound != null)
            {
                transition.clip = profile.shootingStartupSound;
                transition.loop = false;
                transition.pitch = 1f;
                transition.volume = profile.shootVolume;
                transition.time = 0f;
                transition.PlayScheduled(start);
                loopStart = start + ExactLength(profile.shootingStartupSound);
                AudioEventLog.Record(AudioEventKind.Play, transition.gameObject.name, profile.shootingStartupSound.name);
            }
        }

        AudioSource shoot = ResolveMinigunSource(m_ShootLoopSources, profile, true);
        if (shoot != null && profile.shootingLoopSound != null)
        {
            shoot.Stop();
            shoot.clip = profile.shootingLoopSound;
            shoot.loop = true;
            shoot.pitch = 1f;
            shoot.volume = profile.shootVolume;
            shoot.time = 0f;
            shoot.PlayScheduled(loopStart);
            AudioEventLog.Record(AudioEventKind.Play, shoot.gameObject.name, profile.shootingLoopSound.name);
        }
    }

    /// <summary>
    /// The trigger has come up - or the magazine ran dry, or the weapon was lowered, or a
    /// reload began; the controller funnels all four here. Both loops stop dead. Then one of
    /// two endings: the shooting shutdown, from its start, when rounds had actually been
    /// leaving the gun; or the LAST <paramref name="winddownTailSeconds"/> of the spin
    /// wind-down when the trigger was let go during the wind-up - a tap. The owner's rule
    /// (2026-09-06): a press that heard 0.2 s of wind-up hears the final 0.2 s of wind-down,
    /// so the sound is as short as the press was. Safe on a weapon that never started -
    /// every Stop here is a no-op on a silent source.
    /// </summary>
    /// <param name="winddownTailSeconds">For the tap only: how much of the wind-down's end
    /// to play. The controller passes how long the wind-up ran.</param>
    /// <returns>How many seconds of sound were started - the full shutdown, or the tail of
    /// the wind-down (less than asked for only if the clip is shorter than the tail). The
    /// controller winds the barrels down over exactly this, so mesh and sound stop together.
    /// 0 when nothing was played, in which case the controller falls back to its own.</returns>
    public float EndShooting(string weaponName, bool roundsWereLeaving, float winddownTailSeconds)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return 0f;

        StopIfAny(m_SpinLoopSources, profile);
        StopIfAny(m_ShootLoopSources, profile);

        // A wind-up let go early, or a startup still in its first tenth of a second.
        ClearTailEnd();
        StopIfAny(m_TransitionSources, profile);

        AudioClip clip = roundsWereLeaving ? profile.shootingShutdownSound : profile.spinWinddownSound;
        if (clip == null) return 0f;

        AudioSource transition = ResolveMinigunSource(m_TransitionSources, profile, true);
        if (transition == null) return 0f;

        float volume = roundsWereLeaving ? profile.shootVolume : profile.spinVolume;
        transition.clip = clip;
        transition.loop = false;
        transition.pitch = 1f;
        transition.volume = volume;

        if (roundsWereLeaving)
        {
            // The shutdown, whole, from its start. It ends by itself.
            transition.time = 0f;
            transition.Play();
            AudioEventLog.Record(AudioEventKind.Play, transition.gameObject.name, clip.name);
            return clip.length;
        }

        // The wind-down: N seconds of it, N being how long the wind-up ran. Which N seconds
        // is the profile's call - see spinWinddownTail. Clamped to the clip, and short of the
        // end because AudioSource.time at the clip length is an error.
        float tail = Mathf.Clamp(winddownTailSeconds, 0.01f, clip.length);
        bool fromStart = profile.spinWinddownTail == WinddownTail.StartOfClip;
        float offset = fromStart ? 0f : Mathf.Clamp(clip.length - tail, 0f, Mathf.Max(0f, clip.length - 0.01f));

        transition.time = offset;
        transition.Play();
        AudioEventLog.Record(AudioEventKind.Play, transition.gameObject.name, clip.name);

        float played = fromStart ? tail : clip.length - offset;

        // From the start, the clip would carry on past N: Update fades it out over the last
        // 50 ms and stops it. (From the end it stops by itself, at the same moment.)
        if (fromStart && played < clip.length - 0.001f)
        {
            m_TailSource = transition;
            m_TailVolume = volume;
            m_TailEndDsp = AudioSettings.dspTime + played;
        }

        return played;
    }

    /// <summary>
    /// Everything the minigun plays, stopped now, with no shutdown: for the player dying,
    /// the weapon being destroyed, and the other paths where any sound after this point is
    /// the bug. Never creates a source - nothing has ever played on a weapon that has none.
    /// </summary>
    public void StopMinigunAudio(string weaponName)
    {
        if (!TryGetProfile(weaponName, out WeaponSoundProfile profile)) return;

        ClearTailEnd();
        StopIfAny(m_SpinLoopSources, profile);
        StopIfAny(m_ShootLoopSources, profile);
        StopIfAny(m_TransitionSources, profile);
    }
    #endregion

    #if UNITY_EDITOR
    [ContextMenu("Debug Weapon Profiles")]
    private void DebugWeaponProfiles()
    {
        if (m_WeaponProfiles == null)
        {
            Debug.Log("No weapon profiles assigned.");
            return;
        }

        foreach (var profile in m_WeaponProfiles)
        {
            if (profile == null) continue;
            
            Debug.Log($"Weapon: {profile.weaponName}\n" +
                     $"Shoot Sounds: {(profile.shootSounds?.Length ?? 0)} clips\n" +
                     $"Reload Sounds: {(profile.reloadActionSounds?.Length ?? 0)} clips");
        }
    }
    #endif
}