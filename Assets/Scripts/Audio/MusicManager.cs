using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[DisallowMultipleComponent]
public class MusicManager : MonoBehaviour
{
    public enum MusicState
    {
        Exploring,
        Fighting,
        Inside
    }

    public enum PlaybackMode
    {
        Random,         // Randomly select next clip
        Sequential     // Play clips in order
    }

    [System.Serializable]
    public class MusicTrack
    {
        [Tooltip("A descriptive name for this music track in the inspector")]
        public string trackName;

        [Tooltip("The game state this music track is associated with")]
        public MusicState state;

        [Tooltip("Array of music clips that will be played for this state. All clips should be the same length")]
        public AudioClip[] musicClips;

        [Tooltip("Volume level for this track (0.0 to 1.0)")]
        [Range(0f, 1f)]
        public float volume = 1.0f;

        [Tooltip("The Audio Mixer Group this track should output to")]
        public AudioMixerGroup outputMixerGroup;

        [Tooltip("If true, the track will continue playing clips. If false, it will stop after playing through the clips once")]
        public bool loop = true;

        [Tooltip("Sequential: Play clips in order as listed\nRandom: Randomly select the next clip to play")]
        public PlaybackMode playbackMode = PlaybackMode.Sequential;

        [Tooltip("When using Random mode, avoid playing the same clip twice in a row (only applies when there are 3 or more clips)")]
        public bool avoidRepeatInRandom = true;

        [Tooltip("States this track must wait behind while THEIR condition still holds. E.g. " +
                 "Fighting listing Inside means: indoors AND zombies alive -> Inside keeps " +
                 "playing; leave the cave -> Fighting starts. A block never outlives the " +
                 "condition that caused it")]
        public MusicState[] cannotOverride;

        [HideInInspector] public AudioSource source;

        // The idle half of a two-source pair. Assigning the next clip to the source
        // still playing STOPS it - cutting up to 0.1s of tail and leaving an equal gap
        // before the scheduled start - so boundaries schedule on this one and swap.
        [HideInInspector] public AudioSource nextSource;
        [HideInInspector] public int currentClipIndex = 0;
        [HideInInspector] public int lastPlayedClipIndex = -1;
        [HideInInspector] public bool nextClipScheduled = false;
        [HideInInspector] public double scheduledClipStartTime;
    }

    [Tooltip("Array of music tracks, each defining the music for a different game state")]
    public MusicTrack[] musicTracks;

    [Tooltip("Duration in seconds for crossfading between different music states")]
    public float crossFadeDuration = 2.0f;

    [Tooltip("The music state to play when the game starts")]
    public MusicState startingState = MusicState.Exploring;

    [Header("Layered music (optional)")]
    [Tooltip("Adaptive content, one asset per state. A state with no asset here keeps using " +
             "its legacy single-clip track above, so states can be migrated one at a time")]
    [SerializeField] private MusicStateDefinition[] stateDefinitions;

    [Tooltip("Horizontal transition rules, first match wins. Unmatched transitions use a " +
             "NextBar default with the crossfade duration above")]
    [SerializeField] private MusicTransitionRule[] transitionRules;

    [Tooltip("Mixer group for layered stems and stingers. Falls back to the legacy track's " +
             "group for that state when left empty")]
    [SerializeField] private AudioMixerGroup defaultMusicMixerGroup;

    [Tooltip("How gameplay drives the vertical layering intensity")]
    [SerializeField] private MusicIntensityConfig intensityConfig = new MusicIntensityConfig();

    [Tooltip("Adds the Ctrl+M (alias F8) music debug overlay - available in builds, like " +
             "the other assessment overlays")]
    [SerializeField] private bool enableDebugOverlay = true;

    private MusicState currentState;
    private Coroutine fadeCoroutine;
    private bool isIndoors = false;
    private bool isFighting = false;

    // ---- Layered music runtime ----------------------------------------------
    private readonly Dictionary<MusicState, LayeredStatePlayer> layeredPlayers =
        new Dictionary<MusicState, LayeredStatePlayer>();
    private readonly MusicConductor conductor = new MusicConductor();
    private MusicIntensityDriver intensityDriver;
    private Coroutine legacyFadeRoutine;

    /// <summary>Runway given to the session's first layered entry - see RequestStateChange.</summary>
    private const double StartupEntryLeadSeconds = 1.0;
    private bool startupEntryScheduled;

    // False once every state is layered: the per-frame walk over the legacy tracks in
    // Update then has nothing it could ever do. Decided once in Awake.
    private bool hasLegacyTracks;

    // Two sources, used alternately: assigning a clip to a playing source stops it, so a
    // single source meant each stinger cut off the previous one still ringing out.
    private AudioSource[] stingerSources;
    private int nextStingerSource;

    // The stinger belonging to the transition currently pending - the only one a
    // supersede is allowed to cancel. Earlier stingers ringing out are left alone.
    private AudioSource lastStingerSource;

    private void Awake()
    {
        // Seed before anything can transition. The field would otherwise default to
        // Exploring, so with a different startingState the first transition of the
        // session would be resolved from a state that never played - and a rule authored
        // "from: Exploring" would match at startup regardless.
        currentState = startingState;

        InitializeAudioSources();
        InitializeLayeredMusic();

        hasLegacyTracks = musicTracks.Any(t => !IsLayered(t.state));
    }

    private void OnEnable()
    {
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
    }

    private void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    /// <summary>
    /// Unity has just rebuilt the audio system (output device changed, or AudioSettings.Reset):
    /// every AudioSource is stopped and AudioSettings.dspTime has restarted. Every scheduled
    /// segment, the pending transition and the conductor's anchor now refer to a clock that no
    /// longer exists, so nothing can be resumed - the state the flags ask for is rebuilt from
    /// scratch with an immediate entry.
    /// </summary>
    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        MusicState resume = EffectiveState;

        StopMusicFades();
        pending.active = false;
        lastStingerSource = null;
        conductor.Stop();

        // The rebuilt audio system is churning exactly like a fresh scene load, so the
        // restart entry gets the full startup runway again - the bare margin is the
        // late-start defect the lead exists to avoid.
        startupEntryScheduled = false;

        foreach (LayeredStatePlayer player in layeredPlayers.Values) player.StopEverything();

        foreach (MusicTrack track in musicTracks)
        {
            if (track.source == null) continue;
            StopTrackSources(track);
            track.nextClipScheduled = false;   // the latch is waiting on the old clock
        }

        Debug.Log($"[MusicManager] Audio configuration changed (device changed: {deviceWasChanged}); " +
                  $"restarting {resume}.", this);

        // With currentState == target the landing takes the no-outgoing path, so the
        // immediate re-entry does not fade out the very state it is resuming.
        currentState = resume;
        ChangeState(resume, immediate: true);
    }

    /// <summary>
    /// Builds a player for every state that has a content asset. States without one are
    /// left entirely to the legacy path above, so content can be migrated one state at a
    /// time without the scene ever losing its music.
    /// </summary>
    private void InitializeLayeredMusic()
    {
        AudioMixerGroup stingerFallbackGroup = defaultMusicMixerGroup;

        intensityDriver = new MusicIntensityDriver(intensityConfig);

        // Before the null-asset return: the cannotOverride carrier check concerns the
        // legacy tracks and matters whether or not any layered content is assigned.
        WarnAboutOrphanedOverrideRules();

        if (stateDefinitions == null) return;

        foreach (MusicStateDefinition definition in stateDefinitions)
        {
            if (definition == null) continue;

            if (layeredPlayers.ContainsKey(definition.state))
            {
                Debug.LogWarning($"[MusicManager] Two music state assets both target {definition.state}; " +
                                 $"ignoring '{definition.name}'.", this);
                continue;
            }

            if (!definition.HasLoops)
            {
                Debug.LogWarning($"[MusicManager] Music state asset '{definition.name}' has no usable loop " +
                                 $"segments; {definition.state} will keep using its legacy track.", this);
                continue;
            }

            definition.ValidateContent(message => Debug.LogWarning($"[MusicManager] {message}", this));

            AudioMixerGroup group = defaultMusicMixerGroup != null
                ? defaultMusicMixerGroup
                : GetLegacyMixerGroup(definition.state);

            // The stingers below inherit the first resolved group, so they stay on the
            // music bus even when defaultMusicMixerGroup is left empty - unrouted they
            // would ignore the music volume slider and every snapshot.
            if (stingerFallbackGroup == null) stingerFallbackGroup = group;

            layeredPlayers[definition.state] = new LayeredStatePlayer(definition, gameObject, group);
        }

        if (layeredPlayers.Count > 0)
        {
            stingerSources = new AudioSource[2];
            for (int i = 0; i < stingerSources.Length; i++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = 1f;
                source.priority = 0;
                if (stingerFallbackGroup != null) source.outputAudioMixerGroup = stingerFallbackGroup;
                stingerSources[i] = source;
            }
        }

        // No editor/dev gate: the overlay is an assessment tool like Ctrl+G and Ctrl+J,
        // and gating it here hid its pause-menu button in the standalone build.
        if (layeredPlayers.Count > 0 && enableDebugOverlay &&
            GetComponent<MusicDebugOverlay>() == null)
        {
            gameObject.AddComponent<MusicDebugOverlay>().Initialize(this);
        }
    }

    /// <summary>
    /// cannotOverride is authored only on the legacy MusicTrack entries, and the check that
    /// reads it needs a track for BOTH states involved. Deleting a migrated state's
    /// now-redundant track therefore disables those rules silently, which is a nasty trap
    /// during a one-state-at-a-time migration.
    /// </summary>
    private void WarnAboutOrphanedOverrideRules()
    {
        if (musicTracks == null) return;

        foreach (MusicTrack track in musicTracks)
        {
            if (track?.cannotOverride == null) continue;

            foreach (MusicState blocked in track.cannotOverride)
            {
                bool hasCarrier = musicTracks.Any(t => t != null && t.state == blocked);
                if (!hasCarrier)
                {
                    Debug.LogWarning($"[MusicManager] '{track.trackName}' lists {blocked} in " +
                                     "cannotOverride, but no music track exists for that state, so the " +
                                     "rule is ignored. Keep an (empty) track entry for every state that " +
                                     "appears in a cannotOverride list.", this);
                }
            }
        }
    }

    private AudioMixerGroup GetLegacyMixerGroup(MusicState state)
    {
        MusicTrack track = musicTracks?.FirstOrDefault(t => t != null && t.state == state);
        return track != null ? track.outputMixerGroup : null;
    }

    /// <summary>True when this state is driven by a layered content asset.</summary>
    public bool IsLayered(MusicState state) => layeredPlayers.ContainsKey(state);

    private void Start()
    {
        // Resolved here rather than in Awake so the gameplay singletons have been created.
        // Any of them may be absent; that source simply contributes nothing.
        intensityDriver?.ResolveSources();

        ChangeState(startingState, true);
    }

    public void SetIndoorState(bool indoor)
    {
        isIndoors = indoor;
        UpdateMusicState();
    }

    public void SetFightingState(bool fighting)
    {
        isFighting = fighting;
        UpdateMusicState();
    }

    /// <summary>The state the flags ask for. Fighting outranks Inside outranks Exploring.</summary>
    private MusicState DesiredState =>
        isFighting ? MusicState.Fighting : (isIndoors ? MusicState.Inside : MusicState.Exploring);

    /// <summary>
    /// True while the game still has a reason to be in <paramref name="state"/>. Exploring is
    /// the default, held only when nothing else is.
    /// </summary>
    private bool ConditionHolds(MusicState state) => state switch
    {
        MusicState.Fighting => isFighting,
        MusicState.Inside => isIndoors,
        _ => !isFighting && !isIndoors
    };

    /// <summary>
    /// Single decision point for gameplay-driven music. cannotOverride (authored on the legacy
    /// MusicTrack entries) is a PRIORITY rule between two simultaneously-true conditions: the
    /// blocked state waits only while the blocking state's condition still holds. Once that
    /// condition clears, the block clears with it - so no rule can ever trap the music.
    /// </summary>
    private void UpdateMusicState()
    {
        MusicState newState = DesiredState;
        MusicState effective = EffectiveState;
        if (newState == effective) return;

        var currentTrack = musicTracks.FirstOrDefault(t => t.state == effective);
        var newTrack = musicTracks.FirstOrDefault(t => t.state == newState);

        bool blocked = currentTrack != null && newTrack != null &&
                       newTrack.cannotOverride != null &&
                       newTrack.cannotOverride.Contains(effective) &&
                       ConditionHolds(effective);
        if (blocked) return;

        ChangeState(newState);
    }

    private void InitializeAudioSources()
    {
        foreach (var track in musicTracks)
        {
            track.source = CreateLegacySource(track);
            track.nextSource = CreateLegacySource(track);

            if (track.musicClips != null && track.musicClips.Length > 0)
            {
                track.source.clip = track.musicClips[0];
            }
        }
    }

    private AudioSource CreateLegacySource(MusicTrack track)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.loop = false;   // looping is scheduled by hand
        source.volume = 0f;
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        if (track.outputMixerGroup != null)
        {
            source.outputAudioMixerGroup = track.outputMixerGroup;
        }

        return source;
    }

    /// <summary>Stops both of a legacy track's sources - the carrier and the one that may still be ringing out.</summary>
    private static void StopTrackSources(MusicTrack track)
    {
        if (track.source != null) track.source.Stop();
        if (track.nextSource != null) track.nextSource.Stop();
    }

    private void Update()
    {
        float unscaledDelta = Time.unscaledDeltaTime;
        double dspNow = AudioSettings.dspTime;

        if (intensityDriver != null) intensityDriver.Tick(unscaledDelta);
        float intensity = intensityDriver != null ? intensityDriver.Smoothed : 0f;

        UpdatePendingTransition(dspNow);

        foreach (LayeredStatePlayer player in layeredPlayers.Values)
        {
            player.Tick(dspNow, intensity, unscaledDelta);
        }

        // Keep the grid anchored to the audible segment rather than to the moment the
        // state was entered, so a segment that started late after a hitch does not leave
        // transition sync points pointing at a bar line nobody can hear.
        if (conductor.IsRunning &&
            layeredPlayers.TryGetValue(currentState, out LayeredStatePlayer activePlayer) &&
            activePlayer.IsActive)
        {
            conductor.Rebase(activePlayer.CurrentSegmentStartDsp);
        }

        // Check for tracks that need to schedule their next clip
        if (!hasLegacyTracks) return;

        foreach (var track in musicTracks)
        {
            // A layered state's legacy track is never started; skip it entirely.
            if (IsLayered(track.state)) continue;

            if (track.source == null || track.source.clip == null) continue;

            // Once the scheduled clip has actually started, allow scheduling again.
            if (track.nextClipScheduled && AudioSettings.dspTime >= track.scheduledClipStartTime)
            {
                track.nextClipScheduled = false;
            }

            // The end-of-clip window is ~0.1s wide; without the latch this would
            // re-schedule (and advance the clip index) every frame inside it.
            if (!track.nextClipScheduled && track.source.isPlaying &&
                track.source.time >= track.source.clip.length - 0.1f)
            {
                ScheduleNextClip(track);
            }
        }
    }

    private void ScheduleNextClip(MusicTrack track)
    {
        if (track.musicClips == null || track.musicClips.Length == 0) return;
        if (!track.loop && track.currentClipIndex >= track.musicClips.Length - 1) return;

        int nextClipIndex;
        if (track.playbackMode == PlaybackMode.Sequential)
        {
            nextClipIndex = (track.currentClipIndex + 1) % track.musicClips.Length;
        }
        else // Random mode
        {
            if (track.musicClips.Length == 1)
            {
                nextClipIndex = 0;
            }
            else if (track.musicClips.Length == 2)
            {
                nextClipIndex = track.currentClipIndex == 0 ? 1 : 0;
            }
            else
            {
                do
                {
                    nextClipIndex = Random.Range(0, track.musicClips.Length);
                } while (track.avoidRepeatInRandom && nextClipIndex == track.currentClipIndex);
            }
        }

        // Calculate exact time to schedule the next clip
        double nextStartTime = AudioSettings.dspTime + (track.source.clip.length - track.source.time);

        // On the OTHER source. The finishing one rings out its last fraction of a
        // second untouched and goes quiet by itself (loop is off); every volume write
        // and stop from here on goes to the new carrier via the swap.
        AudioSource finishing = track.source;
        AudioSource next = track.nextSource;

        track.lastPlayedClipIndex = track.currentClipIndex;
        track.currentClipIndex = nextClipIndex;

        next.clip = track.musicClips[nextClipIndex];
        next.volume = finishing.volume;
        next.PlayScheduled(nextStartTime);

        track.source = next;
        track.nextSource = finishing;
        track.nextClipScheduled = true;
        track.scheduledClipStartTime = nextStartTime;
    }

    private void StartTrack(MusicTrack track)
    {
        if (track.musicClips == null || track.musicClips.Length == 0) return;
        
        track.currentClipIndex = 0;
        track.lastPlayedClipIndex = -1;
        track.nextClipScheduled = false;
        if (track.nextSource != null) track.nextSource.Stop();
        track.source.clip = track.musicClips[0];
        track.source.Play();
    }

    /// <summary>
    /// Raw transition. Bypasses isFighting / isIndoors / cannotOverride - gameplay must go
    /// through SetFightingState / SetIndoorState, which are the only authorities on music
    /// state. Called only by Start; it stays public because the compatibility contract
    /// requires it (CODE_STRUCTURE - adaptive music). The debug overlay's state-cycle key
    /// that used to call it was removed on 2026-09-01.
    /// </summary>
    public void ChangeState(MusicState newState, bool immediate = false)
    {
        // Compared against the state we are heading for, not the one we are leaving:
        // during a scheduled transition currentState still names the outgoing state.
        if (EffectiveState == newState && !immediate) return;

        AudioEventLog.Record(AudioEventKind.State, "Music", $"{EffectiveState} -> {newState}");

        // Either side layered -> the adaptive pipeline owns this transition. Also when a
        // layered transition is already in flight: taking the legacy branch would leave it
        // armed to fire later against a state that has since moved on.
        if (pending.active || IsLayered(newState) || IsLayered(currentState))
        {
            RequestStateChange(newState, immediate);
            return;
        }

        StopMusicFades();

        // Assigned here rather than when a coroutine finishes. CrossFadeToState used to
        // write it seconds later, so a superseded fade could overwrite the state a newer
        // transition had already established.
        currentState = newState;

        if (immediate)
        {
            SetStateVolumes(newState);

            // Start playing all tracks (they'll be muted if not active). Layered states
            // own their own sources and must not have their legacy track started.
            foreach (var track in musicTracks)
            {
                if (IsLayered(track.state)) continue;
                if (!track.source.isPlaying)
                    track.source.Play();
            }
        }
        else
        {
            fadeCoroutine = StartCoroutine(CrossFadeToState(newState));
        }
    }

    /// <summary>
    /// The state the music is heading for. Everything that reasons about "where we are"
    /// must use this: currentState only advances when a scheduled transition lands, so
    /// during the wait it still names the state being left.
    /// </summary>
    private MusicState EffectiveState => pending.active ? pending.target : currentState;

    /// <summary>
    /// Stops both fade coroutines. They write the same AudioSource volumes, so leaving
    /// either running while the other starts let them fight - and the loser's completion
    /// block could stop a track the winner had just brought up.
    /// </summary>
    private void StopMusicFades()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }

        if (legacyFadeRoutine != null)
        {
            StopCoroutine(legacyFadeRoutine);
            legacyFadeRoutine = null;
        }
    }

    private void SetStateVolumes(MusicState state)
    {
        foreach (var track in musicTracks)
        {
            if (track.state == state)
            {
                track.source.volume = track.volume;
                if (!track.source.isPlaying)
                    StartTrack(track);
            }
            else
            {
                track.source.volume = 0f;
                StopTrackSources(track);  // Stop tracks that aren't being used
            }
        }
    }

    private IEnumerator CrossFadeToState(MusicState newState)
    {
        float elapsedTime = 0f;
        float[] startVolumes = new float[musicTracks.Length];
        float[] targetVolumes = new float[musicTracks.Length];
        
        // Start the new track if it's not playing
        var newTrack = musicTracks.FirstOrDefault(t => t.state == newState);
        if (newTrack != null && !newTrack.source.isPlaying)
        {
            StartTrack(newTrack);
        }
        
        for (int i = 0; i < musicTracks.Length; i++)
        {
            startVolumes[i] = musicTracks[i].source.volume;
            targetVolumes[i] = musicTracks[i].state == newState ? musicTracks[i].volume : 0f;
        }

        while (elapsedTime < crossFadeDuration)
        {
            // Unscaled: the pause menu sets timeScale to 0, and a scaled fade would
            // freeze the crossfade halfway through.
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / crossFadeDuration;

            for (int i = 0; i < musicTracks.Length; i++)
            {
                musicTracks[i].source.volume = Mathf.Lerp(startVolumes[i], targetVolumes[i], t);
            }

            yield return null;
        }

        // Stop all tracks except the current one
        foreach (var track in musicTracks)
        {
            if (track.state != newState)
            {
                StopTrackSources(track);
            }
            else
            {
                track.source.volume = track.volume;
            }
        }
    }

    // ---- Layered transition pipeline ----------------------------------------

    private struct PendingTransition
    {
        public bool active;
        public MusicState target;
        public double entryDsp;
        public float crossfade;
        public bool playIntro;
        public string ruleName;
    }

    private PendingTransition pending;

    /// <summary>
    /// Routes a state change through the adaptive pipeline. The switch is scheduled at a
    /// musical sync point rather than applied immediately, so the caller's timing does not
    /// have to be musical.
    /// </summary>
    private void RequestStateChange(MusicState target, bool immediate)
    {
        double dspNow = AudioSettings.dspTime;

        // Settle a transition whose sync point has already passed, so the rule below is
        // resolved against the state we are really in rather than the one we are leaving.
        UpdatePendingTransition(dspNow);

        if (immediate)
        {
            // The bare margin is enough mid-session, but the session's FIRST layered
            // entry lands while the scene is still churning through load work, and a
            // PlayScheduled slot missed there starts the audio late from the top of the
            // clip - cutting the first loop short (audit-2 finding 8; VerifyEntryStart
            // then recovers only to within its measurement tolerance). A second of
            // runway cannot be missed, and silence while the scene is still appearing
            // is inaudible.
            double lead = startupEntryScheduled
                ? MusicConductor.ScheduleMargin
                : StartupEntryLeadSeconds;
            startupEntryScheduled = true;

            BeginTransition(target, dspNow + lead, 0f,
                playIntro: true, ruleName: "Immediate");
            return;
        }

        // The same transition is already on its way; leave its sync point where it is
        // rather than pushing it back on every repeated request. Checked FIRST, because
        // the self-target branch below must never cancel a pending entry for the very
        // state being asked for - that stranded the player scheduled but permanently
        // muted, looping in silence with no path back.
        if (pending.active && pending.target == target) return;

        layeredPlayers.TryGetValue(currentState, out LayeredStatePlayer outgoing);

        // Already in the requested state: drop any pending departure and stay put.
        // Re-entering would call ScheduleEntry on the state that is currently audible,
        // and that begins by stopping it - an audible dropout and a restart from the
        // intro. This is the common "combat ended before the scheduled bar arrived"
        // path, where SetFightingState(false) asks for the state we never left.
        if (currentState == target)
        {
            // Changed our mind about leaving while the outro is already playing: let it
            // finish and carry on looping. Re-entering would cut the outro dead and leave
            // silence until its original end.
            if (outgoing != null && outgoing.IsExiting) outgoing.ResumeLooping();

            CancelPendingTransition();
            return;
        }

        bool outgoingIsExiting = outgoing != null && outgoing.IsExiting;

        MusicTransitionRule rule = FindRule(currentState, target);
        double syncPoint = ResolveSyncPoint(rule.sync, dspNow);
        double entryDsp = syncPoint;

        if (outgoingIsExiting)
        {
            // An outro is already running from a superseded request; the new state can
            // only come in once it has finished.
            entryDsp = System.Math.Max(entryDsp, outgoing.ExitEndDsp);
        }
        else if (rule.playOutroOfCurrent && outgoing != null && outgoing.IsPlaying &&
                 outgoing.Definition.HasOutro && currentState != target)
        {
            // The outro replaces the rest of the current segment, and the incoming state
            // waits for it to finish.
            entryDsp = outgoing.ScheduleExitOutro(syncPoint);
        }

        BeginTransition(target, entryDsp, rule.crossfadeDuration, rule.playIntroOfNext, rule.Describe());

        // Aligned to the handover, which is the sync point unless an outro moved it.
        ScheduleStinger(rule, entryDsp);
    }

    /// <summary>
    /// Schedules a transition stinger so its accent lands on the handover. The clip starts
    /// stingerLeadIn seconds early; if that moment has already passed, it fires now.
    /// </summary>
    private void ScheduleStinger(MusicTransitionRule rule, double alignDsp)
    {
        if (rule.stinger == null || stingerSources == null) return;

        AudioSource source = stingerSources[nextStingerSource];
        nextStingerSource = (nextStingerSource + 1) % stingerSources.Length;

        source.clip = rule.stinger;
        source.volume = rule.stingerVolume;

        double startAt = alignDsp - rule.stingerLeadIn;
        double earliest = AudioSettings.dspTime + MusicConductor.ScheduleMargin;
        if (startAt < earliest) startAt = earliest;

        source.PlayScheduled(startAt);
        lastStingerSource = source;
    }

    /// <summary>
    /// Fade used when a pending entry is abandoned: the transition's own crossfade, or the
    /// serialized default when that transition was instantaneous (a 0 here would reintroduce
    /// the cut this exists to prevent).
    /// </summary>
    private float AbandonFadeSeconds(float transitionCrossfade) =>
        transitionCrossfade > 0.001f ? transitionCrossfade : Mathf.Max(0.25f, crossFadeDuration);

    /// <summary>
    /// Abandons a scheduled transition, along with any stinger that belonged to it. An
    /// incoming player that has not made a sound is stopped (cancelling its scheduled
    /// audio); one that is audible - a takeover on a state still fading out - fades out
    /// instead of being cut. The state that is currently audible is left playing untouched.
    /// </summary>
    private void CancelPendingTransition()
    {
        if (!pending.active) return;

        if (pending.target != currentState &&
            layeredPlayers.TryGetValue(pending.target, out LayeredStatePlayer incoming))
        {
            incoming.AbandonEntry(AbandonFadeSeconds(pending.crossfade));
        }

        pending.active = false;
        StopPendingStinger();
    }

    /// <summary>Cancels only the stinger tied to the pending transition, if any.</summary>
    private void StopPendingStinger()
    {
        if (lastStingerSource != null) lastStingerSource.Stop();
        lastStingerSource = null;
    }

    /// <summary>First matching rule, or a NextBar default using the serialized crossfade.</summary>
    private MusicTransitionRule FindRule(MusicState from, MusicState to)
    {
        if (transitionRules != null)
        {
            foreach (MusicTransitionRule rule in transitionRules)
            {
                if (rule != null && rule.Matches(from, to)) return rule;
            }
        }

        fallbackRule.crossfadeDuration = crossFadeDuration;
        return fallbackRule;
    }

    private readonly MusicTransitionRule fallbackRule = new MusicTransitionRule
    {
        ruleName = "Default",
        fromAny = true,
        toAny = true,
        sync = TransitionSync.NextBar,
        playIntroOfNext = true,
        playOutroOfCurrent = false
    };

    /// <summary>
    /// Translates a sync rule into the DSP time the switch should happen. Resolved on the
    /// OUTGOING state's grid; a state with no grid (a legacy track) can only go immediately.
    /// </summary>
    private double ResolveSyncPoint(TransitionSync sync, double dspNow)
    {
        double immediate = dspNow + MusicConductor.ScheduleMargin;
        if (!conductor.IsRunning) return immediate;

        switch (sync)
        {
            case TransitionSync.NextBeat:
                return conductor.NextBeat(dspNow);

            case TransitionSync.NextBar:
                return conductor.NextBar(dspNow);

            case TransitionSync.EndOfSegment:
                if (layeredPlayers.TryGetValue(currentState, out LayeredStatePlayer player) && player.IsActive)
                {
                    double end = player.CurrentSegmentEndDsp;
                    // A boundary that is already upon us is unusable; fall back to the next bar.
                    if (end > immediate) return end;
                }
                return conductor.NextBar(dspNow);

            default:
                return immediate;
        }
    }

    private void BeginTransition(MusicState target, double entryDsp, float crossfade, bool playIntro, string ruleName)
    {
        // A transition that has not started yet is superseded, not queued: the newest
        // request always wins. A superseded player that never made a sound is stopped
        // (cancelling its PlayScheduled); one that is audible - a takeover held at a
        // fade level - fades out over the abandoned transition's own crossfade instead
        // of being cut dead. Read pending.crossfade BEFORE the assignments below
        // overwrite it with the new transition's value.
        if (pending.active && pending.target != target &&
            layeredPlayers.TryGetValue(pending.target, out LayeredStatePlayer superseded))
        {
            superseded.AbandonEntry(AbandonFadeSeconds(pending.crossfade));

            // Drop only the stinger that belonged to the transition just superseded. A
            // stinger ringing out over a boundary that has already passed is legitimate
            // and must not be truncated by the next state change.
            StopPendingStinger();
        }

        pending.active = true;
        pending.target = target;
        pending.entryDsp = entryDsp;
        pending.crossfade = crossfade;
        pending.playIntro = playIntro;
        pending.ruleName = ruleName;

        // Schedule the incoming audio now, so it starts exactly on the sync point.
        if (layeredPlayers.TryGetValue(target, out LayeredStatePlayer incoming))
        {
            // A player that is still audible (fading out after a departure the game just
            // reversed) is taken over gracefully by ScheduleEntry and must NOT be zeroed
            // here - it holds its current level and ramps back up at the entry point.
            bool takeover = incoming.IsPlaying;
            incoming.ScheduleEntry(entryDsp, playIntro);

            if (!takeover)
            {
                // A crossfaded entry starts silent and ramps once the transition lands.
                // A ZERO-crossfade entry must instead be audible from its first sample:
                // raising the fade at landing (the first Update at or after entryDsp) is
                // fine mid-session, but at scene load the first frame can stall past the
                // entry point, and the sources then play at volume zero under the stall
                // - eating the head of the intro (audit-2 finding 7, promoted from
                // cosmetic by the startup lead). With the fade set now, the pre-entry
                // Tick applies full volumes to the scheduled sources, so the audio
                // thread starts them audible whatever the main thread is doing.
                incoming.SetFadeImmediate(crossfade <= 0.001f ? 1f : 0f);
            }
        }
    }

    /// <summary>
    /// Applies a scheduled transition once its sync point arrives. Fades start here, not
    /// when the change was requested, so the crossfade is centred on the musical boundary.
    /// </summary>
    private void UpdatePendingTransition(double dspNow)
    {
        if (!pending.active || dspNow < pending.entryDsp) return;

        MusicState target = pending.target;
        float crossfade = pending.crossfade;
        double entryDsp = pending.entryDsp;
        pending.active = false;

        // The stinger is no longer cancellable: its transition just landed, and from here
        // it is legitimately ringing out over the boundary.
        lastStingerSource = null;

        // Outgoing. (A legacy -> legacy change never reaches this pipeline, so the only
        // legacy source to retire here is handled with the incoming case below.)
        if (currentState != target &&
            layeredPlayers.TryGetValue(currentState, out LayeredStatePlayer outgoing))
        {
            outgoing.BeginFadeOut(crossfade);
        }

        if (layeredPlayers.TryGetValue(target, out LayeredStatePlayer incoming))
        {
            if (crossfade <= 0.001f) incoming.SetFadeImmediate(1f);
            else incoming.BeginFadeIn(crossfade);

            conductor.Begin(entryDsp, incoming.Definition.bpm, incoming.Definition.beatsPerBar);

            // Any legacy track still audible must give way to the layered state.
            if (!IsLayered(currentState)) StartLegacyFade(null, crossfade);
        }
        else
        {
            // Layered -> legacy: bring the legacy track up as the layered one fades out.
            StartLegacyFade(target, crossfade);
            conductor.Stop();
        }

        currentState = target;
    }

    private void StartLegacyFade(MusicState? target, float duration)
    {
        StopMusicFades();
        legacyFadeRoutine = StartCoroutine(LegacyFadeRoutine(target, duration));
    }

    /// <summary>
    /// Fades the non-layered tracks toward <paramref name="target"/> (or all to silence
    /// when null). Separate from CrossFadeToState because it must leave layered states,
    /// which own their own sources, completely alone.
    /// </summary>
    private IEnumerator LegacyFadeRoutine(MusicState? target, float duration)
    {
        MusicTrack[] tracks = musicTracks
            .Where(t => t != null && t.source != null && !IsLayered(t.state))
            .ToArray();

        float[] startVolumes = new float[tracks.Length];
        float[] endVolumes = new float[tracks.Length];

        for (int i = 0; i < tracks.Length; i++)
        {
            startVolumes[i] = tracks[i].source.volume;
            bool isTarget = target.HasValue && tracks[i].state == target.Value;
            endVolumes[i] = isTarget ? tracks[i].volume : 0f;

            if (isTarget && !tracks[i].source.isPlaying) StartTrack(tracks[i]);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < tracks.Length; i++)
            {
                tracks[i].source.volume = Mathf.Lerp(startVolumes[i], endVolumes[i], t);
            }
            yield return null;
        }

        for (int i = 0; i < tracks.Length; i++)
        {
            tracks[i].source.volume = endVolumes[i];
            if (endVolumes[i] <= 0f) StopTrackSources(tracks[i]);
        }

        legacyFadeRoutine = null;
    }

    // ---- Public intensity API ------------------------------------------------

    /// <summary>
    /// Sets a floor on the music intensity, for scripted moments and zone triggers.
    /// Gameplay can still push above it; nothing can pull below it until it is cleared.
    /// </summary>
    public void SetIntensityOverride(float value01) => intensityDriver?.SetOverride(value01);

    public void ClearIntensityOverride() => intensityDriver?.ClearOverride();

    /// <summary>Smoothed 0-1 intensity currently driving the vertical layering.</summary>
    public float GetCurrentIntensity() => intensityDriver != null ? intensityDriver.Smoothed : 0f;

    // ---- Readouts for the debug overlay --------------------------------------

    public MusicState CurrentState => currentState;
    public bool HasPendingTransition => pending.active;
    public MusicState PendingState => pending.target;
    public double PendingEntryDsp => pending.entryDsp;
    public string PendingRuleName => pending.ruleName;
    public MusicConductor Conductor => conductor;
    public MusicIntensityDriver IntensityDriver => intensityDriver;
    public IReadOnlyDictionary<MusicState, LayeredStatePlayer> LayeredPlayers => layeredPlayers;
} 