using UnityEngine;

/// <summary>
/// The wind chimes' clip, on the Audio Managers object with every other manager.
///
/// One assessed slot: a single seamless loop, no start and no end. That is what a chime in a
/// tree is - it does not begin and it does not finish, it is either audible or it is not - and
/// a student who drops in a clip with a hard attack will hear the seam every time it wraps,
/// which is the lesson.
///
/// It holds the clip and nothing else. Everything that belongs to a particular set of chimes -
/// how loud, how far it carries, how long it fades up - lives on <see cref="WindChimeAudio"/>
/// out at the prop, so two sets in one level can share this clip and still be balanced apart.
/// The clip is pushed out in Awake, which always runs before any Start, so the chimes can play
/// in their own Start without an execution-order setting to remember.
///
/// ⚠ NO custom inspector, on purpose. The hand-drawn audio-manager inspectors in
/// Assets/Scripts/Editor are the trap that shipped the grenade with an empty section and left
/// the SMG's reload slots unfillable for months: a field they do not explicitly draw is a field
/// nobody can put a clip into. Unity's default inspector draws this one, so the field cannot go
/// missing. Nothing registers it with the progress report either - the scanner walks serialized
/// AudioClip fields on project components, so the field below IS the registration.
/// </summary>
public class WindChimeAudioManager : MonoBehaviour
{
    [Header("Wind Chimes")]

    /// <summary>
    /// The assessed slot. Left empty the chimes hang silently, which is the right state for a
    /// level handed to students - a missing clip should be a quiet prop, not a warning.
    /// </summary>
    [Tooltip("The wind-chime loop. One seamless clip - no start, no end.")]
    [SerializeField] private AudioClip windChimeLoop;

    /// <summary>
    /// Which chimes get it. Left empty, every set in the scene is found at startup - the
    /// normal case, and the one that survives the owner dragging the prop somewhere else in
    /// the hierarchy, which is the likeliest thing to happen to it.
    /// </summary>
    [Tooltip("Left empty, every WindChimeAudio in the scene is used.")]
    [SerializeField] private WindChimeAudio[] chimes;

    /// <summary>The clip a student has dropped in, or null. Read by the setup tool's report.</summary>
    public AudioClip Loop => windChimeLoop;

    private void Awake()
    {
        Distribute();
    }

    /// <summary>
    /// Pushes the clip out to every chime. Public so the setup tool and a student scrubbing
    /// the field in play mode can both re-apply it without a scene reload.
    /// </summary>
    public void Distribute()
    {
        WindChimeAudio[] targets = chimes != null && chimes.Length > 0
            ? chimes
            : FindObjectsByType<WindChimeAudio>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (targets == null) return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null) targets[i].SetLoop(windChimeLoop);
        }
    }

    /// <summary>
    /// So dropping a clip in during play mode is heard immediately, the way the other managers
    /// in this project behave when a student is auditioning.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying) Distribute();
    }
}
