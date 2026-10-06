using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Which of the four combat roles this enemy plays, and so which of the six role slots its
/// profile is asked to fill. None owns none of them: the boss, and any enemy carrying no
/// role component - see EnemySoundProfile.IsAudioSlotRelevant.
/// </summary>
/// <remarks>
/// Appended, never inserted or reordered: enemyRole serializes as an int, so renumbering
/// would silently retype every profile already authored in the scene. Same rule as
/// WeaponType.
/// </remarks>
public enum EnemyRole
{
    None,
    Runner,
    Shield,
    Spitter,
    Screamer
}

/// <summary>
/// One role sound, as the role components ask for it. At file scope beside EnemyRole - the
/// way WinddownTail is - so a role can say RoleSound.Lunge without naming the manager the
/// clip will end up playing through.
/// </summary>
public enum RoleSound
{
    Lunge,       // Runner: the growl before the sprint
    Block,       // Shield: the clang of a round taken on the front
    SpitWindup,  // Spitter: the retch before the spit
    Spit,        // Spitter: the launch
    SpitImpact,  // Spitter: the splash where it lands, away from the zombie
    Scream       // Screamer: the call that rallies the pack
}

[DisallowMultipleComponent]
public class EnemyAudioManager : MonoBehaviour
{
    public enum EnemyType
    {
        StandardEnemy,
        Boss
    }

    [System.Serializable]
    public class VocalizationSettings
    {
        [Tooltip("Individual vocalisation sounds")]
        public AudioClip[] vocalisationSounds;
    }

    [System.Serializable]
    public class EnemySoundProfile : IAudioSlotFilter
    {
        [Tooltip("Unique identifier for this enemy - must match the name referenced in enemy scripts")]
        public string enemyName;

        [Tooltip("Compile-checked identity for this profile. Leave Unassigned until Tools/Audio/Migrate Profile Keys To Enums has been run; once set, this name also works as a spelling of the profile.")]
        public EnemyId enemyId;
        
        [Tooltip("Type of enemy that determines base volume levels")]
        public EnemyType enemyType;

        [Header("Sounds")]
        [Tooltip("Random vocalisation sounds played while enemy is active")]
        public VocalizationSettings generalVocalisations;
        [Tooltip("Sounds played when enemy attacks - one will be randomly selected")]
        public AudioClip[] attackSounds;
        [Tooltip("Sounds played when enemy dies - one will be randomly selected")]
        public AudioClip[] deathSounds;
        [Tooltip("Sounds played when this enemy takes a HEADSHOT - one will be randomly selected")]
        public AudioClip[] headshotSounds;

        [Header("Role")]
        [Tooltip("Which role's slots this enemy is asked for. None hides all six; the boss is None. This field decides which SOUNDS the profile shows and the report asks for - what makes the enemy behave that way is the role component on its prefab")]
        public EnemyRole enemyRole = EnemyRole.None;

        [Header("Role Sounds")]
        [Tooltip("Runner: the growl before the sprint burst. A TELL - it plays before the zombie moves, so the length of the clip is the warning the player gets")]
        public AudioClip[] lungeSounds;
        [Tooltip("Shield: the clang of a round taken on the front. Plays on the hit, once per shot the shield eats")]
        public AudioClip[] blockSounds;
        [Tooltip("Spitter: the retch before the spit. A TELL - this is the time the player has to move")]
        public AudioClip[] spitWindupSounds;
        [Tooltip("Spitter: the launch, heard at the zombie")]
        public AudioClip[] spitSounds;
        [Tooltip("Spitter: the splash where the spit lands. The one enemy sound in the level that plays AWAY from the enemy, in 3D at the impact point - author it to read from across the level rather than to sit up close")]
        public AudioClip[] spitImpactSounds;
        [Tooltip("Screamer: the call that rallies the pack. A TELL - the pack speeds up when it ends")]
        public AudioClip[] screamSounds;
        [Tooltip("Volume multiplier shared by all six role sounds (0-1)")]
        [Range(0f, 1f)] public float roleVolume = 1f;

        [Header("Enemy Audio Setup")]
        [Tooltip("The enemy prefab this profile is for")]
        public GameObject enemyPrefab;
        [Tooltip("Minimum vocal delay in seconds")]
        public float minVocalDelay = 2f;
        [Tooltip("Maximum vocal delay in seconds")]
        public float maxVocalDelay = 6f;
        [Tooltip("Volume multiplier for vocalisations (0-1)")]
        [Range(0f, 1f)] public float vocalisationVolume = 1f;
        [Tooltip("Volume multiplier for attack sounds (0-1)")]
        [Range(0f, 1f)] public float attackVolume = 1f;
        [Tooltip("Volume multiplier for the death sound (0-1)")]
        [Range(0f, 1f)] public float deathVolume = 1f;
        [Tooltip("Volume multiplier for headshot sounds (0-1)")]
        [Range(0f, 1f)] public float headshotVolume = 1f;
        [HideInInspector]
        [Tooltip("The AudioSource for vocalisation and death sounds")]
        public AudioSource vocalisationAudioSource;
        [HideInInspector]
        [Tooltip("The AudioSource for attack sounds")]
        public AudioSource attackAudioSource;
        [HideInInspector]
        public AudioMixerGroup vocalisationMixerGroup;
        [HideInInspector]
        public AudioMixerGroup attackMixerGroup;

        [Header("Timing")]
        [Tooltip("Delay in seconds before playing attack sound to align to the appropriate moment in the animation")]
        [Range(0f, 1f)] public float attackSoundDelay = 0.2f;

        // The no-repeat memory for the role slots, one entry per RoleSound. It lives on the
        // PROFILE, the way WeaponSoundProfile keeps lastShootIndex and its two grenade
        // indices: the older Play* methods below each declare a local lastIndex, which is
        // -1 again on the next call and therefore remembers nothing, so two lunges in a row
        // can land on the same clip. One array rather than six fields because RoleSound
        // already numbers the slots - a seventh role sound is an enum member and a case in
        // RoleClips, not a seventh field for somebody to forget.
        [System.NonSerialized] private int[] roleIndices;

        // The same memory for the three older slots. Separate fields rather than a second
        // array because nothing numbers them the way RoleSound numbers the role slots.
        // On the PROFILE, so every zombie of a type shares one memory - which is the
        // memory that matters to the ear: two Yokai attacking together should not both
        // pick the same growl because each was asked in isolation.
        [System.NonSerialized] public int lastAttackIndex = -1, lastDeathIndex = -1, lastHeadshotIndex = -1;

        /// <summary>
        /// The clip array behind one role sound - null or empty when the student has not
        /// authored that slot, which every caller treats as silence.
        /// </summary>
        public AudioClip[] RoleClips(RoleSound which)
        {
            switch (which)
            {
                case RoleSound.Lunge:      return lungeSounds;
                case RoleSound.Block:      return blockSounds;
                case RoleSound.SpitWindup: return spitWindupSounds;
                case RoleSound.Spit:       return spitSounds;
                case RoleSound.SpitImpact: return spitImpactSounds;
                case RoleSound.Scream:     return screamSounds;
                default:                   return null;
            }
        }

        /// <summary>
        /// The no-repeat index for one role slot, by reference so AudioUtil.PlayRandomOneShot
        /// can write the clip it picked straight back into it.
        /// </summary>
        public ref int RoleIndex(RoleSound which)
        {
            if (roleIndices == null)
            {
                roleIndices = new int[System.Enum.GetValues(typeof(RoleSound)).Length];
                // -1 is "nothing played yet"; a fresh array of zeros would ban clip 0 from
                // being the first one heard.
                for (int i = 0; i < roleIndices.Length; i++) roleIndices[i] = -1;
            }
            return ref roleIndices[(int)which];
        }

        /// <summary>
        /// Keeps a role's slots off the profiles of enemies that cannot play them, so the
        /// Ctrl+G report never asks a student for a sound the enemy never makes. A Butcher
        /// blocks; it does not spit, and a permanently red Spit row is work that can never
        /// be done - which is exactly the training-the-reader-to-ignore-red problem the
        /// filter exists for.
        /// </summary>
        /// <remarks>
        /// A switch on the FIELD NAME with default true, where
        /// WeaponSoundProfile.IsAudioSlotRelevant switches on the weapon type: only these
        /// six of an enemy profile's slots are conditional, so naming the six is shorter
        /// than listing what each role does NOT hide, and - the point - a slot added later
        /// shows up by itself. A hidden slot is a sound the report silently stops asking
        /// for; a row too many is a visible mistake.
        ///
        /// EnemyRole.None owns nothing, which is how the Rock Golem, and any enemy that
        /// has not been given a role, keeps exactly the slots it had before this existed.
        /// Nothing else on the profile is touched: every slot that showed yesterday shows
        /// today.
        ///
        /// Only the clip fields are listed. roleVolume beside them is a float and the
        /// scanner records AudioClip leaves only, so naming it here would change nothing
        /// in the report and would only mislead.
        /// </remarks>
        public bool IsAudioSlotRelevant(string fieldName)
        {
            switch (fieldName)
            {
                case nameof(lungeSounds):      return enemyRole == EnemyRole.Runner;
                case nameof(blockSounds):      return enemyRole == EnemyRole.Shield;

                // The spitter's three are one sound in three parts - the retch, the launch
                // and the splash - so they appear and disappear together.
                case nameof(spitWindupSounds):
                case nameof(spitSounds):
                case nameof(spitImpactSounds): return enemyRole == EnemyRole.Spitter;

                case nameof(screamSounds):     return enemyRole == EnemyRole.Screamer;

                default:                       return true;
            }
        }
    }

    [SerializeField] private EnemySoundProfile[] enemyProfiles;
    private Dictionary<string, EnemySoundProfile> profileLookup;
    private Dictionary<AudioSource, Coroutine> activeVocalizations = new Dictionary<AudioSource, Coroutine>();

    [HideInInspector]
    [SerializeField] private float maxHearingDistance = 30f;

    private Transform playerTransform;

    private void Awake()
    {
        InitializeAudioSystem();
        AutoAssignAudioSources();
    }

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }
        else
        {
            Debug.LogWarning("EnemyAudioManager: no GameObject tagged 'Player' found; enemy " +
                             "vocalisations stay silent until one appears (each loop retries).", this);
        }
    }

    private void InitializeAudioSystem()
    {
        profileLookup = AudioProfileKeys.CreateLookup<EnemySoundProfile>();

        if (enemyProfiles == null) return;

        foreach (var profile in enemyProfiles)
        {
            if (profile == null) continue;
            AudioProfileKeys.AddProfile(profileLookup, profile.enemyName, profile, this, "EnemyAudioManager");
        }

        // Second pass: aliases only for profiles the lookup actually serves - a
        // duplicate that pass 1 rejected must not smuggle itself back in via its id.
        foreach (var profile in enemyProfiles)
        {
            if (profile == null) continue;
            if (!AudioProfileKeys.Owns(profileLookup, profile.enemyName, profile)) continue;
            AudioProfileKeys.AddIdAlias(profileLookup, profile.enemyId, profile);
        }
    }

    private bool TryGetProfile(string enemyName, out EnemySoundProfile profile)
    {
        return AudioProfileKeys.TryGetProfile(profileLookup, enemyName, out profile,
            this, "EnemyAudioManager");
    }

    /// <summary>
    /// Id-addressed lookup for callers once they stop passing strings. Resolves through the
    /// same dictionary, where an assigned id is registered under its own name.
    /// </summary>
    public bool TryGetProfile(EnemyId enemyId, out EnemySoundProfile profile)
    {
        // Unassigned is "no id", not a name - looking it up would warn about a
        // profile called 'Unassigned' that was never supposed to exist.
        if (!AudioProfileIds.IsAssigned(enemyId)) { profile = null; return false; }
        return TryGetProfile(AudioProfileIds.NameOf(enemyId), out profile);
    }

    /// <summary>
    /// AudioUtil.PlayRandomOneShot, with the source's own pitch put back afterwards. Every
    /// enemy one-shot in this class goes through here.
    /// </summary>
    /// <remarks>
    /// AudioUtil writes source.pitch = 1 whenever no pitch range is passed (AudioUtil.cs:56),
    /// and an enemy profile authors no pitch range - so on most zombies this restores 1 to 1
    /// and changes nothing. The exception is the boss: BossZombieController deepens
    /// voice.pitch once per phase (BossZombieController.cs:203) on the same root AudioSource
    /// these methods play through - "the voice fraying is the tell" - and before this, the
    /// next idle vocalisation, headshot or death cry reset the voice to 1 and erased it.
    ///
    /// The restore lives here rather than in AudioUtil because AudioUtil is shared: the
    /// weapon and footstep sources are played both randomised and plain, and their
    /// reset-to-1 is load-bearing (fixing it there would give the revolver a randomised
    /// reload pitch). This is the only caller that owns a pitch it did not set.
    ///
    /// Restoring AFTER PlayOneShot is deliberate, not a race: a one-shot follows its
    /// source's pitch for as long as it plays, so the clip is heard at the boss's phase
    /// pitch rather than at 1.
    /// </remarks>
    private static void PlayKeepingSourcePitch(AudioSource source, AudioClip[] clips,
                                               ref int lastIndex, float volume)
    {
        if (source == null) return;   // AudioUtil would no-op anyway; this is for the read below

        float pitch = source.pitch;
        AudioUtil.PlayRandomOneShot(source, clips, ref lastIndex, volume);
        source.pitch = pitch;
    }

    public void PlayAttackSound(string enemyName, AudioSource source)
    {
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return;

        PlayKeepingSourcePitch(source, profile.attackSounds, ref profile.lastAttackIndex, profile.attackVolume);
    }

    public void PlayDeathSound(string enemyName, AudioSource source)
    {
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return;

        // Play random death sound if available
        PlayKeepingSourcePitch(source, profile.deathSounds, ref profile.lastDeathIndex, profile.deathVolume);
    }

    public void PlayHeadshotSound(string enemyName, AudioSource source)
    {
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return;

        PlayKeepingSourcePitch(source, profile.headshotSounds, ref profile.lastHeadshotIndex, profile.headshotVolume);
    }

    /// <summary>
    /// A role's own sound, played on the zombie's source: the growl before a runner's
    /// sprint, the clang off a shield, the retch before a spit, the scream that rallies
    /// the pack. The role decides WHEN; the profile decides what, and at what level.
    /// </summary>
    /// <remarks>
    /// Silence, never an exception, when anything is missing - no profile for the key, no
    /// clips authored into the slot yet, or a zombie with no AudioSource: TryGetProfile
    /// returns false (and logs one Miss row for the Ctrl+J monitor), and
    /// AudioUtil.PlayRandomOneShot returns on a null source or an empty array. The roles
    /// run whether or not a student has filled the slots, so a role that threw on an empty
    /// array would make authoring order a gameplay bug. AudioUtil records the Play row.
    /// </remarks>
    public void PlayRoleSound(string enemyName, RoleSound which, AudioSource source)
    {
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return;

        ref int lastIndex = ref profile.RoleIndex(which);
        PlayKeepingSourcePitch(source, profile.RoleClips(which), ref lastIndex, profile.roleVolume);
    }

    /// <summary>
    /// The same role sound, but AT a world position rather than on the enemy - the
    /// spitter's impact, which lands wherever the spit lands and not where the Demon is
    /// standing by the time it gets there.
    /// </summary>
    /// <remarks>
    /// Built the way WeaponAudioManager.PlayExplosionSound builds the grenade's blast, and
    /// for the same two reasons: the sound must be fully 3D at a point nothing owns, and
    /// the projectile is returned to its pool on the frame it lands, so a one-shot on the
    /// spit's own source would be cut off. The throwaway host outlives the clip and then
    /// deletes itself.
    ///
    /// The clip is picked here rather than inside AudioUtil.PlayRandomOneShot because its
    /// length decides how long the host has to live; the no-repeat rule itself is still
    /// AudioUtil's, and shares the profile's per-slot memory with PlayRoleSound.
    ///
    /// It borrows the profile's vocalisation mixer group so the splash lands on the ENEMIES
    /// bus with every other zombie sound - a sound routed around the mixer ignores the
    /// levels and effects authored on that bus and sits oddly loud against the pack.
    /// maxHearingDistance is the same range the vocalisation loop already gates on, so a
    /// spit landing carries exactly as far as everything else the enemy does.
    /// </remarks>
    public void PlayRoleSoundAt(string enemyName, RoleSound which, Vector3 position)
    {
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return;

        AudioClip[] clips = profile.RoleClips(which);
        if (clips == null || clips.Length == 0) return;

        ref int lastIndex = ref profile.RoleIndex(which);
        int index = AudioUtil.GetRandomIndexNoRepeat(clips.Length, lastIndex);
        AudioClip clip = clips[index];
        if (clip == null) return;
        lastIndex = index;

        GameObject host = new GameObject("Enemy Role Audio");
        host.transform.position = position;

        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;                       // fully 3D - the whole point
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 2f;                        // full level within the splash
        source.maxDistance = maxHearingDistance;
        source.outputAudioMixerGroup = profile.vocalisationMixerGroup != null
            ? profile.vocalisationMixerGroup
            : profile.attackMixerGroup;

        source.PlayOneShot(clip, profile.roleVolume);
        AudioEventLog.Record(AudioEventKind.Play, host.name, clip.name);

        // Pitch is left at 1 - an enemy profile authors no pitch range, unlike a weapon -
        // so the clip's own length is how long the host is needed, plus a little slack.
        Destroy(host, clip.length + 0.1f);
    }

    public float GetAttackSoundDelay(string enemyName)
    {
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return 0f;
        
        return profile.attackSoundDelay;
    }

    private void OnDestroy()
    {
        // Clean up all active coroutines
        foreach (var coroutine in activeVocalizations.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        activeVocalizations.Clear();
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure enemy profiles have unique names
        if (enemyProfiles != null)
        {
            // Matches the runtime lookup: names differing only by case or padding collide.
            HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var profile in enemyProfiles)
            {
                if (profile == null) continue;

                string name = AudioProfileKeys.Normalize(profile.enemyName);
                if (string.IsNullOrEmpty(name)) continue;

                if (!names.Add(name))
                {
                    Debug.LogWarning($"Duplicate enemy profile name found: {name}");
                }
            }
        }
    }
    #endif

    // New: Coroutine to continuously play vocalizations for an enemy
    public void StartVocalizationLoop(string enemyName, AudioSource source)
    {
        if (source == null) return;
        // Stop any existing loop for this source first
        if (activeVocalizations.TryGetValue(source, out var existing) && existing != null)
        {
            StopCoroutine(existing);
        }
        var coroutine = StartCoroutine(VocalizationLoop(enemyName, source));
        activeVocalizations[source] = coroutine;
    }

    private IEnumerator VocalizationLoop(string enemyName, AudioSource source)
    {
        if (source == null) yield break;
        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            yield break;
        int lastIndex = -1;
        while (true)
        {
            // Exit if the AudioSource or its GameObject was destroyed
            if (source == null || source.Equals(null) || source.gameObject == null)
            {
                break;
            }

            // Ensure playerTransform is valid
            if (playerTransform == null || playerTransform.Equals(null))
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    playerTransform = player.transform;
            }

            // If we still don't have a player, wait briefly and retry
            if (playerTransform == null || playerTransform.Equals(null))
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // Only play if the enemy is within audible range
            float distanceToPlayer = Vector3.Distance(source.transform.position, playerTransform.position);
            if (distanceToPlayer > maxHearingDistance)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            if (profile.generalVocalisations != null)
            {
                PlayKeepingSourcePitch(source, profile.generalVocalisations.vocalisationSounds,
                    ref lastIndex, profile.vocalisationVolume);
            }
            // Wait for a random duration between the min and max vocal delays
            float waitTime = Random.Range(profile.minVocalDelay, profile.maxVocalDelay);
            yield return new WaitForSeconds(waitTime);
        }

        // Clean up tracking if we exit the loop
        if (activeVocalizations.ContainsKey(source))
        {
            activeVocalizations.Remove(source);
        }
    }

    public void StopVocalizationLoop(AudioSource source)
    {
        if (source == null) return;
        if (activeVocalizations.TryGetValue(source, out var coroutine) && coroutine != null)
        {
            StopCoroutine(coroutine);
        }
        activeVocalizations.Remove(source);
    }

    // Auto-assign the audio source references based on the enemyPrefab.
    private void AutoAssignAudioSources()
    {
        if (enemyProfiles != null)
        {
            foreach (var profile in enemyProfiles)
            {
                if (profile == null) continue;

                if (profile.enemyPrefab != null)
                {
                    // Assign the parent's AudioSource for vocalisation and death sounds
                    if (profile.vocalisationAudioSource == null)
                    {
                        profile.vocalisationAudioSource = profile.enemyPrefab.GetComponent<AudioSource>();
                    }
                    // Assign the child's AudioSource (named "Audio Source - Attack") for attack sounds
                    if (profile.attackAudioSource == null)
                    {
                        Transform attackChild = profile.enemyPrefab.transform.Find("Audio Source - Attack");
                        if (attackChild != null)
                        {
                            profile.attackAudioSource = attackChild.GetComponent<AudioSource>();
                        }
                    }

                    // Ensure the output mixer groups are properly assigned
                    if (profile.vocalisationAudioSource != null && profile.vocalisationMixerGroup != null)
                        profile.vocalisationAudioSource.outputAudioMixerGroup = profile.vocalisationMixerGroup;

                    if (profile.attackAudioSource != null && profile.attackMixerGroup != null)
                        profile.attackAudioSource.outputAudioMixerGroup = profile.attackMixerGroup;
                }
            }
        }
    }

    /// <summary>
    /// Routes and shapes a voice a zombie had to create for itself, so a fallback source is
    /// never the one unmixed 2D voice in the level.
    /// </summary>
    /// <remarks>
    /// ZombieAI adds an AudioSource in Start when its prefab carries none. All eleven shipped
    /// enemy prefabs carry a root source already, so this is a latent path - but a bare
    /// AddComponent gives Unity's defaults: no mixer group (straight to Master, deaf to the
    /// SFX fader and the volume slider), spatialBlend 0 (heard in the middle of the player's
    /// head from anywhere), playOnAwake on. The three writes below are what the prefab's own
    /// source already has, and the mixer group is picked exactly the way PlayRoleSoundAt
    /// picks it for its detached host.
    ///
    /// Safe on a missing profile: the blend and the play-on-awake flag are still corrected,
    /// only the routing is skipped, and TryGetProfile logs the one Miss row for the monitor.
    /// </remarks>
    public void ConfigureFallbackVoice(string enemyName, AudioSource source)
    {
        if (source == null) return;

        source.playOnAwake = false;
        source.spatialBlend = 1f;   // fully 3D, like every enemy prefab's own source

        if (!TryGetProfile(enemyName, out EnemySoundProfile profile))
            return;

        source.outputAudioMixerGroup = profile.vocalisationMixerGroup != null
            ? profile.vocalisationMixerGroup
            : profile.attackMixerGroup;
    }

    // Public methods to get the correct mixer group for an enemy
    public AudioMixerGroup GetVocalisationMixerGroup(string enemyName)
    {
        if (TryGetProfile(enemyName, out EnemySoundProfile profile))
            return profile.vocalisationMixerGroup;
        return null;
    }

    public AudioMixerGroup GetAttackMixerGroup(string enemyName)
    {
        if (TryGetProfile(enemyName, out EnemySoundProfile profile))
            return profile.attackMixerGroup;
        return null;
    }
} 