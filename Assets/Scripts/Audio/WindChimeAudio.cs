using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The speaker in the wind chimes: it owns the AudioSource, the level and the rolloff, and
/// plays whatever loop it is handed. It does NOT own the clip.
///
/// The clip lives on <see cref="WindChimeAudioManager"/>, on the Audio Managers object with
/// every other manager (the owner's call, 2026-09-06), and is pushed in from there at startup.
/// That is deliberate and it is the reason there is no AudioClip field here: a serialized clip
/// on this component would be a SECOND assessed slot in the progress report, sitting empty on
/// the prop for ever while the student fills the one on the manager.
///
/// Everything that is a property of THIS chime rather than of the sound - how loud, how far it
/// carries, how long it fades up - stays here, so two sets of chimes in one level can share a
/// clip and still be balanced separately.
///
/// Sits beside <see cref="WindChimeSway"/> on the same object: this one is what the chimes
/// SOUND like, that one is what they LOOK like. They are deliberately not connected - the loop
/// runs at a steady level whatever the sway is doing, so what a student hears is their own clip
/// and not a gain curve this script invented.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class WindChimeAudio : MonoBehaviour
{
    [Header("Audio")]

    [Tooltip("Left empty, the AudioSource on this object is used.")]
    [SerializeField] private AudioSource loopAudioSource;

    /// <summary>
    /// Which bus the chimes come out of. Left empty they go straight to Master, which means
    /// the AMBIENCE fader does not touch them and neither does anything mixed into that bus -
    /// the setup tool wires AMBIENCE for exactly that reason. The level's other world sounds
    /// each have their own child group under it (Fire, River, Waterfall, Birds, Frogs); if the
    /// owner adds a Chimes group later, this is the one field to repoint.
    /// </summary>
    [Tooltip("The mixer bus. Empty means straight to Master, which bypasses the AMBIENCE fader.")]
    [SerializeField] private AudioMixerGroup outputMixerGroup;

    [Header("Volume")]

    [Range(0f, 1f)]
    [Tooltip("Level of the loop at minDistance.")]
    [SerializeField] private float volume = 0.7f;

    [Tooltip("Seconds to reach that level from silence when the level loads. A chime that " +
             "snaps on at full level announces the scene load; two seconds hides it.")]
    [Min(0f)]
    [SerializeField] private float fadeInDuration = 2f;

    [Header("Distance")]

    [Tooltip("Full volume closer than this.")]
    [Min(0.1f)]
    [SerializeField] private float minDistance = 2f;

    [Tooltip("Silent beyond this.")]
    [Min(0.2f)]
    [SerializeField] private float maxDistance = 25f;

    /// <summary>
    /// Where in the clip playback starts. A loop that always begins at sample zero gives the
    /// same first two seconds on every scene reload, which is exactly what a student
    /// auditioning their clip is listening past.
    /// </summary>
    [Tooltip("Start somewhere random in the loop rather than always at the beginning.")]
    [SerializeField] private bool startAtRandomOffset = true;

    private AudioClip loop;
    private float fadeStartTime;
    private bool fading;
    private bool started;

    /// <summary>The clip the manager handed over, or null before it has. Read by the report tool.</summary>
    public AudioClip Loop => loop;

    private void Awake()
    {
        if (loopAudioSource == null) loopAudioSource = GetComponent<AudioSource>();
        ApplySourceSettings();
    }

    /// <summary>
    /// Hands this chime its clip. Called from <see cref="WindChimeAudioManager"/> in Awake,
    /// which is always before any Start - so <see cref="Start"/> below can rely on it having
    /// happened, without an execution-order setting to remember.
    ///
    /// Safe to call again later: a student who drops a clip into the manager while the game is
    /// running hears it start, because this restarts playback once Start has been through.
    ///
    /// ⚠ The test is "has Start run", NOT "is it playing". Measured 2026-09-06: with an empty
    /// slot at load, Start finds no clip and plays nothing, so a later push found isPlaying
    /// false, set the clip and stopped - the chimes took the clip and stayed silent.
    /// </summary>
    public void SetLoop(AudioClip clip)
    {
        loop = clip;

        if (loopAudioSource == null) loopAudioSource = GetComponent<AudioSource>();
        if (loopAudioSource == null) return;

        loopAudioSource.clip = clip;

        if (!started) return;                 // Start has not run yet; it will play this clip

        loopAudioSource.Stop();
        PlayLoop();
    }

    private void Start()
    {
        started = true;
        PlayLoop();
    }

    private void PlayLoop()
    {
        if (loopAudioSource == null || loop == null) return;

        loopAudioSource.clip = loop;

        // AudioSource.time is in seconds and clamps to the clip, so a clip shorter than the
        // offset asked for cannot put playback past the end.
        if (startAtRandomOffset) loopAudioSource.time = Random.Range(0f, loop.length);

        loopAudioSource.volume = fadeInDuration > 0f ? 0f : volume;
        loopAudioSource.Play();

        fadeStartTime = Time.time;
        fading = fadeInDuration > 0f;
    }

    /// <summary>
    /// The fade only. Update rather than a coroutine so it stops costing anything the moment
    /// it finishes - <see cref="fading"/> goes false and this returns on its first line - and
    /// so a student scrubbing the volume in play mode sees it move.
    /// </summary>
    private void Update()
    {
        if (!fading || loopAudioSource == null) return;

        float t = (Time.time - fadeStartTime) / fadeInDuration;

        if (t >= 1f)
        {
            loopAudioSource.volume = volume;
            fading = false;
            return;
        }

        loopAudioSource.volume = Mathf.Lerp(0f, volume, t);
    }

    /// <summary>
    /// Applies the inspector's numbers in the editor, so dragging maxDistance shows its
    /// gizmo sphere move rather than needing a play to take effect.
    /// </summary>
    private void OnValidate()
    {
        if (maxDistance <= minDistance) maxDistance = minDistance + 0.1f;
        if (loopAudioSource == null) loopAudioSource = GetComponent<AudioSource>();

        ApplySourceSettings();

        // Only while playing: outside play mode there is nothing playing to re-level, and
        // writing volume here would fight the fade.
        if (Application.isPlaying && loopAudioSource != null && !fading)
        {
            loopAudioSource.volume = volume;
        }
    }

    private void ApplySourceSettings()
    {
        if (loopAudioSource == null) return;

        loopAudioSource.loop = true;
        loopAudioSource.playOnAwake = false;

        // 3D and linear, the same shape the campfires and the river use in this level, so a
        // student who has learned one rolloff has learned them all.
        loopAudioSource.spatialBlend = 1f;
        loopAudioSource.rolloffMode = AudioRolloffMode.Linear;
        loopAudioSource.minDistance = minDistance;
        loopAudioSource.maxDistance = maxDistance;

        // Chimes do not move, and neither does the tree; a doppler shift here would only ever
        // be the player's own walking speed bending the pitch.
        loopAudioSource.dopplerLevel = 0f;

        // Only when set: assigning null here would silently unroute a source somebody had
        // wired by hand in the inspector.
        if (outputMixerGroup != null) loopAudioSource.outputAudioMixerGroup = outputMixerGroup;
    }
}
