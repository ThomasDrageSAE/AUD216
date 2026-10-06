using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;

/*
 * InteractionAudioManager.cs
 * 
 * Purpose: Manages audio for interactive objects in the game world
 * Used by: Interactive objects, switches, doors, mechanisms
 * 
 * Key Features:
 * - Per-interaction sound profiles
 * - Volume control
 * - Mixer routing
 * - Sound variation
 * 
 * Performance Considerations:
 * - Efficient audio source pooling
 * - Smart profile lookup
 * - Optimized sound triggering
 * 
 * Dependencies:
 * - AudioMixer system
 * - Interaction system
 * - Requires configured interaction profiles
 */

[DisallowMultipleComponent]
public class InteractionAudioManager : MonoBehaviour
{
    public enum InteractionType
    {
        SimpleInteraction,
        ElectricalBox,
        // KEPT DELIBERATELY, THOUGH NOTHING USES IT. This enum serializes as an INT, and
        // removing member 2 would renumber Firesticks (3) through Turntable (11) - silently
        // retyping every authored profile in the scene. Its one clip field,
        // interactionSounds[], was deleted 2026-09-08: nothing in the project ever played it,
        // and no profile has ever carried this type (`interactionType: 2` appears 0 times in
        // the .unity and 0 times in every .prefab). A GeneralInteraction profile therefore
        // shows no clip slots at all - see GeneralClips below.
        GeneralInteraction,
        Firesticks,
        Campfire,
        MixingDesk,
        // Appended only (serialized as ints, like InteractionId - reordering would
        // repoint every authored profile). Added 2026-09-03: one section per prop.
        Flashlight,
        Cooking,
        Fishing,
        WalkieTalkie,   // briefly renamed Turntable on 2026-09-03; restored the same day
        Mushroom,
        Turntable       // 2026-09-03: replaces the tape recorder
    }

    /// <summary>How the walkie talkie and the turntable schedule their clips.</summary>
    public enum TrackPlaybackMode
    {
        // Serialized as ints - append only, and RandomContinuous stays 0 so every
        // profile authored before the mode existed keeps its behaviour.
        RandomContinuous,   // forever, random order; never the same clip twice in a
                            // row, nor (with three or more) the previous one either
        OnceInOrder         // first to last, one pass, same gaps; then it ends
    }

    [System.Serializable]
    public class InteractionSoundProfile : IAudioSlotFilter
    {
        [Tooltip("Unique identifier for this interaction - must match the name referenced in interaction scripts")]
        public string interactionName;

        [Tooltip("Compile-checked identity for this profile. Leave Unassigned until Tools/Audio/Migrate Profile Keys To Enums has been run; once set, this name also works as a spelling of the profile.")]
        public InteractionId interactionId;
        
        [Tooltip("Type of interaction that determines which audio properties are available")]
        public InteractionType interactionType;

        // Simple Interaction
        [Tooltip("Single audio clip to play for this interaction")]
        public AudioClip interactionSound;
        [Tooltip("Optional: Specific AudioSource to use for this interaction")]
        public AudioSource customAudioSource;

        // Fire-based (Firesticks & Campfire)
        [Tooltip("Array of ignition sounds - one will be randomly selected when the fire is lit")]
        public AudioClip[] igniteSounds;
        [Tooltip("Continuous loop sound that plays while the fire is burning")]
        public AudioClip fireLoop;
        [Tooltip("Time in seconds to wait after ignition before starting the continuous fire loop")]
        [Range(0f, 5f)] public float fireLoopStartDelay = 1f;

        // Electrical Box
        [Tooltip("Sound played when toggling the electrical box lever")]
        public AudioClip leverSound;

        [Tooltip("Looping sound for electrical sparks")]
        public AudioClip sparksSound;

        [Tooltip("Audio Mixer Group for routing and effects processing - determines output path")]
        public AudioMixerGroup mixerGroupOutput;

        // Mixing Desk
        [Tooltip("Audio clip for the left speaker")]
        public AudioClip leftSpeakerAudio;
        [Tooltip("Audio clip for the right speaker")]
        public AudioClip rightSpeakerAudio;
        [Tooltip("AudioSource component for the left speaker")]
        public AudioSource leftSpeakerSource;
        [Tooltip("AudioSource component for the right speaker")]
        public AudioSource rightSpeakerSource;

        // Flashlight - one profile carries both toggles
        [Tooltip("The beam clicking on")]
        public AudioClip toggleOnSound;
        [Tooltip("The beam clicking off")]
        public AudioClip toggleOffSound;

        // Cooking (campfire) - the timing is carried by audio: the sizzle is the
        // progress bar, the chime the 'now'. The collect and burn one-shots were removed
        // 2026-09-03 at the owner's request; the burn is still a state and a message.
        [Tooltip("LOOPING sizzle while the fish cooks - the audible progress bar")]
        public AudioClip sizzleLoop;
        [Tooltip("The 'it's ready' chime")]
        public AudioClip doneChime;

        // Fishing - the rod's one-shots (per-tier bite/catch overrides live on the rod)
        [Tooltip("Cast - the moment fishing starts")]
        public AudioClip castSound;
        [Tooltip("The bobber plip when a fish bites. AUTHORING THIS ARMS the catch window: " +
                 "without a bite clip anywhere, the fish lands automatically")]
        public AudioClip biteSound;
        [Tooltip("The reward fanfare once the fish is actually in the inventory")]
        public AudioClip caughtSound;

        // Mushroom - the eat sound uses Interaction Sound above; these are the reaction
        [Tooltip("One plays a few seconds after eating - chosen at random per bite")]
        public AudioClip[] dialogueLines;

        // Walkie Talkie (restored 2026-09-03 - the turntable replaced the TAPE, not this)
        [Tooltip("The chatter bursts, scheduled by Walkie Playback Mode below")]
        public AudioClip[] walkieClips;
        [Tooltip("Random Continuous: keeps playing forever in random order - never the " +
                 "same clip twice in a row, and with three or more clips never the " +
                 "previous one again either. Once In Order: plays first to last with " +
                 "the same gaps, then goes silent")]
        public TrackPlaybackMode walkiePlaybackMode = TrackPlaybackMode.RandomContinuous;
        [Tooltip("Shortest silence between chatter bursts, in seconds")]
        [Min(0f)] public float walkieMinGapSeconds = 8f;
        [Tooltip("Longest silence between chatter bursts, in seconds")]
        [Min(0f)] public float walkieMaxGapSeconds = 20f;

        // Turntable (2026-09-03: replaces the TAPE recorder as the studio's played
        // prop. Deliberately NO FormerlySerializedAs pointing at the walkie names:
        // these fields briefly carried it, and Add Missing Audio Profiles moves any
        // content that remap shuffled into these slots back home to the walkie row.)
        [Tooltip("The record's tracks, scheduled by Turntable Playback Mode below")]
        public AudioClip[] turntableClips;
        [Tooltip("Random Continuous: keeps playing forever in random order - never the " +
                 "same track twice in a row, and with three or more tracks never the " +
                 "previous one again either. Once In Order: plays first to last with " +
                 "the same gaps, then the record ends on its own")]
        public TrackPlaybackMode turntablePlaybackMode = TrackPlaybackMode.RandomContinuous;
        [Tooltip("Shortest silence between tracks, in seconds")]
        [Min(0f)] public float turntableMinGapSeconds = 8f;
        [Tooltip("Longest silence between tracks, in seconds")]
        [Min(0f)] public float turntableMaxGapSeconds = 20f;

        // ---- Progress-report filter -------------------------------------------

        /// <summary>
        /// Every clip slot the class declares. A field outside this set is not a slot
        /// at all and is never hidden.
        /// </summary>
        private static readonly HashSet<string> AllClipFields = new HashSet<string>
        {
            nameof(interactionSound),
            nameof(igniteSounds), nameof(fireLoop),
            nameof(leverSound), nameof(sparksSound),
            nameof(leftSpeakerAudio), nameof(rightSpeakerAudio),
            nameof(toggleOnSound), nameof(toggleOffSound),
            nameof(sizzleLoop), nameof(doneChime),
            nameof(castSound), nameof(biteSound), nameof(caughtSound),
            nameof(dialogueLines), nameof(walkieClips), nameof(turntableClips),
        };

        /// <summary>
        /// The clip fields each interaction type actually plays - the same split the
        /// custom inspector draws (InteractionAudioManagerEditor), so the progress report
        /// hides the rest. Every profile carries every field, and without this a Firestick
        /// listed a bite sound and a sizzle loop it will never play: 218 rows for the
        /// manager, of which most were not work at all. Keep this and the inspector in step.
        /// </summary>
        public static IReadOnlyCollection<string> ClipFieldsFor(InteractionType type)
        {
            switch (type)
            {
                case InteractionType.SimpleInteraction: return SimpleClips;
                case InteractionType.ElectricalBox: return ElectricalBoxClips;
                case InteractionType.GeneralInteraction: return GeneralClips;
                case InteractionType.Firesticks:
                case InteractionType.Campfire: return FireClips;
                case InteractionType.MixingDesk: return MixingDeskClips;
                case InteractionType.Flashlight: return FlashlightClips;
                case InteractionType.Cooking: return CookingClips;
                case InteractionType.Fishing: return FishingClips;
                case InteractionType.Mushroom: return MushroomClips;
                case InteractionType.WalkieTalkie: return WalkieClips;
                case InteractionType.Turntable: return TurntableClips;
                default: return AllClipFields;   // an unknown type hides nothing
            }
        }

        private static readonly string[] SimpleClips = { nameof(interactionSound) };
        private static readonly string[] ElectricalBoxClips = { nameof(leverSound), nameof(sparksSound) };
        /// <summary>
        /// Empty on purpose. GeneralInteraction's only clip field, interactionSounds[], was
        /// deleted 2026-09-08 because nothing in the project played it, so the type now
        /// offers no slots and a profile of this type is graded on nothing. The enum member
        /// stays because it serializes as an int - see InteractionType above.
        /// </summary>
        private static readonly string[] GeneralClips = new string[0];
        private static readonly string[] FireClips = { nameof(igniteSounds), nameof(fireLoop) };
        private static readonly string[] MixingDeskClips = { nameof(leftSpeakerAudio), nameof(rightSpeakerAudio) };
        private static readonly string[] FlashlightClips = { nameof(toggleOnSound), nameof(toggleOffSound) };
        private static readonly string[] CookingClips = { nameof(sizzleLoop), nameof(doneChime) };
        private static readonly string[] FishingClips = { nameof(castSound), nameof(biteSound), nameof(caughtSound) };
        private static readonly string[] MushroomClips = { nameof(interactionSound), nameof(dialogueLines) };
        private static readonly string[] WalkieClips = { nameof(walkieClips) };
        private static readonly string[] TurntableClips = { nameof(turntableClips) };

        /// <summary>A clip slot this profile's type never plays is not outstanding work.</summary>
        public bool IsAudioSlotRelevant(string fieldName)
        {
            if (!AllClipFields.Contains(fieldName)) return true;

            foreach (string relevant in ClipFieldsFor(interactionType))
            {
                if (relevant == fieldName) return true;
            }
            return false;
        }
    }

    [SerializeField] private InteractionSoundProfile[] interactionProfiles;
    
    private Dictionary<string, InteractionSoundProfile> profileLookup;
    private Dictionary<string, List<AudioSource>> profileAudioSources;

    private void Awake()
    {
        InitializeAudioSystem();
    }

    private void InitializeAudioSystem()
    {
        profileLookup = AudioProfileKeys.CreateLookup<InteractionSoundProfile>();
        profileAudioSources = AudioProfileKeys.CreateLookup<List<AudioSource>>();

        if (interactionProfiles == null) return;

        foreach (var profile in interactionProfiles)
        {
            if (profile == null) continue;

            int before = profileLookup.Count;
            AudioProfileKeys.AddProfile(profileLookup, profile.interactionName, profile, this, "InteractionAudioManager");

            // Skipped as blank or duplicate - no source list to pair with it.
            if (profileLookup.Count == before) continue;

            profileAudioSources[AudioProfileKeys.Normalize(profile.interactionName)] =
                new List<AudioSource>();
        }

        // Second pass: aliases only for profiles the lookup actually serves - a
        // duplicate that pass 1 rejected must not smuggle itself back in via its id
        // (nor bind another profile's source list to its alias).
        foreach (var profile in interactionProfiles)
        {
            if (profile == null) continue;
            if (!AudioProfileKeys.Owns(profileLookup, profile.interactionName, profile)) continue;
            if (!profileAudioSources.TryGetValue(AudioProfileKeys.Normalize(profile.interactionName),
                    out List<AudioSource> sources)) continue;

            AudioProfileKeys.AddIdAlias(profileLookup, profile.interactionId, profile);
            AudioProfileKeys.AddIdAlias(profileAudioSources, profile.interactionId, sources);
        }

        // Sources are configured as they arrive via RegisterAudioSource - the
        // lists above are empty at this point.
    }

    /// <summary>
    /// Key-addressed profile lookup, public since the 2026-09-03 consolidation:
    /// feature components (cooking, fishing, flashlight, walkie talkie, mushroom
    /// dialogue) fetch their profile and drive their own playback - loops, delays,
    /// random picks - that the fixed Play* entry points below cannot express. Misses
    /// are logged the same as every other lookup, so a missing profile still shows
    /// red in Ctrl+J.
    /// </summary>
    public bool TryGetProfile(string interactionName, out InteractionSoundProfile profile)
    {
        return AudioProfileKeys.TryGetProfile(profileLookup, interactionName, out profile,
            this, "InteractionAudioManager");
    }

    /// <summary>
    /// Id-addressed lookup for callers once they stop passing strings. Resolves through the
    /// same dictionary, where an assigned id is registered under its own name.
    /// </summary>
    public bool TryGetProfile(InteractionId interactionId, out InteractionSoundProfile profile)
    {
        // Unassigned is "no id", not a name - looking it up would warn about a
        // profile called 'Unassigned' that was never supposed to exist.
        if (!AudioProfileIds.IsAssigned(interactionId)) { profile = null; return false; }
        return TryGetProfile(AudioProfileIds.NameOf(interactionId), out profile);
    }

    private List<AudioSource> GetSources(string interactionName)
    {
        return profileAudioSources[AudioProfileKeys.Normalize(interactionName)];
    }

    public void RegisterAudioSource(string profileName, AudioSource source)
    {
        if (!TryGetProfile(profileName, out InteractionSoundProfile profile))
            return;

        if (!GetSources(profileName).Contains(source))
        {
            GetSources(profileName).Add(source);
            ConfigureAudioSource(source); // Apply standard firestick audio settings
        }
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        //source.spatialBlend = 1f;
        //source.rolloffMode = AudioRolloffMode.Linear;
        //source.minDistance = 1f;
        //source.maxDistance = 8f;
        source.priority = 128;
    }

    public void IgniteFirestick(string profileName, AudioSource source)
    {
        if (!TryGetProfile(profileName, out InteractionSoundProfile profile))
            return;

        if (profile.interactionType != InteractionType.Firesticks && 
            profile.interactionType != InteractionType.Campfire)
            return;

        if (!GetSources(profileName).Contains(source))
            return;

        // Set the output mixer group
        if (profile.mixerGroupOutput != null)
        {
            source.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        // Play random ignite sound
        if (profile.igniteSounds != null && profile.igniteSounds.Length > 0)
        {
            AudioClip randomIgniteSound = profile.igniteSounds[Random.Range(0, profile.igniteSounds.Length)];
            source.PlayOneShot(randomIgniteSound, 1f);
        }

        // Start fire loop after delay
        if (profile.fireLoop != null)
        {
            StartCoroutine(StartFireLoopDelayed(source, profile));
        }
    }

    private IEnumerator StartFireLoopDelayed(AudioSource source, InteractionSoundProfile profile)
    {
        yield return new WaitForSeconds(profile.fireLoopStartDelay);
        
        // Set the output mixer group
        if (profile.mixerGroupOutput != null)
        {
            source.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        source.clip = profile.fireLoop;
        source.time = Random.Range(0f, profile.fireLoop.length);
        source.loop = true;
        source.Play();
    }

    public void PlaySimpleInteraction(string profileName, AudioSource defaultSource)
    {
        if (!TryGetProfile(profileName, out InteractionSoundProfile profile))
            return;

        // Mushroom profiles are simple-plus: the same single eat clip, with the
        // dialogue lines riding alongside for the component to fetch itself.
        if (profile.interactionType != InteractionType.SimpleInteraction &&
            profile.interactionType != InteractionType.Mushroom)
            return;

        if (profile.interactionSound != null)
        {
            AudioSource sourceToUse = profile.customAudioSource != null ? profile.customAudioSource : defaultSource;
            
            // Set the output mixer group
            if (profile.mixerGroupOutput != null)
            {
                sourceToUse.outputAudioMixerGroup = profile.mixerGroupOutput;
            }

            sourceToUse.PlayOneShot(profile.interactionSound, 1f);
        }
    }

    public void PlayElectricalBoxSound(string profileName, AudioSource source, bool isLeverSound)
    {
        if (!TryGetProfile(profileName, out InteractionSoundProfile profile))
            return;

        if (profile.interactionType != InteractionType.ElectricalBox)
            return;

        // Set the output mixer group
        if (profile.mixerGroupOutput != null)
        {
            source.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        if (isLeverSound)
        {
            if (profile.leverSound != null)
            {
                source.PlayOneShot(profile.leverSound, 1f);
            }
        }
        else
        {
            if (profile.sparksSound != null)
            {
                source.clip = profile.sparksSound;
                source.loop = true;
                source.Play();
            }
        }
    }

    public void PlayMixingDeskAudio(string profileName, bool play)
    {
        if (!TryGetProfile(profileName, out InteractionSoundProfile profile))
            return;

        if (profile.interactionType != InteractionType.MixingDesk)
        {
            Debug.LogWarning($"Profile {profileName} is not a MixingDesk type");
            return;
        }

        // Validate required components with non-fatal logging
        if (profile.leftSpeakerAudio == null)
            Debug.LogWarning($"[{profileName}] Left speaker audio clip is missing");
        if (profile.rightSpeakerAudio == null)
            Debug.LogWarning($"[{profileName}] Right speaker audio clip is missing");
        if (profile.leftSpeakerSource == null)
            Debug.LogWarning($"[{profileName}] Left speaker AudioSource is missing");
        if (profile.rightSpeakerSource == null)
            Debug.LogWarning($"[{profileName}] Right speaker AudioSource is missing");

        if (profile.leftSpeakerAudio == null || profile.rightSpeakerAudio == null ||
            profile.leftSpeakerSource == null || profile.rightSpeakerSource == null)
        {
            return;
        }

        if (play)
        {
            // Set the output mixer group for both sources
            if (profile.mixerGroupOutput != null)
            {
                profile.leftSpeakerSource.outputAudioMixerGroup = profile.mixerGroupOutput;
                profile.rightSpeakerSource.outputAudioMixerGroup = profile.mixerGroupOutput;
            }

            // Configure both sources
            profile.leftSpeakerSource.clip = profile.leftSpeakerAudio;
            profile.rightSpeakerSource.clip = profile.rightSpeakerAudio;
            
            // Ensure both sources are at the start
            profile.leftSpeakerSource.time = 0;
            profile.rightSpeakerSource.time = 0;

            // Schedule both sources to start on the next audio frame for sample accuracy
            double startTime = AudioSettings.dspTime + 0.1; // Small delay to ensure scheduling
            profile.leftSpeakerSource.PlayScheduled(startTime);
            profile.rightSpeakerSource.PlayScheduled(startTime);
        }
        else
        {
            profile.leftSpeakerSource.Stop();
            profile.rightSpeakerSource.Stop();
        }
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure interaction profiles have unique names
        if (interactionProfiles != null)
        {
            // Matches the runtime lookup: names differing only by case or padding collide.
            HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var profile in interactionProfiles)
            {
                if (profile == null) continue;

                string name = AudioProfileKeys.Normalize(profile.interactionName);
                if (string.IsNullOrEmpty(name)) continue;

                if (!names.Add(name))
                {
                    Debug.LogWarning($"Duplicate interaction profile name found: {name}");
                }
            }
        }
    }

    [ContextMenu("Debug Interaction Profiles")]
    private void DebugInteractionProfiles()
    {
        if (interactionProfiles == null)
        {
            Debug.Log("No interaction profiles assigned.");
            return;
        }

        foreach (var profile in interactionProfiles)
        {
            if (profile == null) continue;
            
            Debug.Log($"Interaction: {profile.interactionName}\n" +
                     $"Fire Loop: {(profile.fireLoop != null ? profile.fireLoop.name : "none")}");
        }
    }
    #endif
} 