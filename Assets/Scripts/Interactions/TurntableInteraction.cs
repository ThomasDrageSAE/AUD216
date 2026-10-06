using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * TurntableInteraction.cs
 *
 * Purpose: The studio turntable. Press E at it to drop the needle; press E again to
 *          lift it. What plays is whatever the "Turntable" section of the
 *          InteractionAudioManager holds: the tracks, the playback mode, and the
 *          min/max gap between tracks.
 * Used by: the Turntable object in the studio (Tools > Interactions > Add Turntable
 *          Interaction wires it up).
 *
 * Repurposed from the Walkie Talkie (2026-09-03) - the same scheduling brain, now
 * spinning records: Once In Order plays the tracks first to last with the authored
 * gaps and then stops on its own; Random Continuous shuffles forever - never the same
 * clip twice in a row, nor, with three or more tracks, the previous one again.
 * Starting again always starts from the top: the needle goes back to the edge.
 *
 * 3D on purpose: the deck is a prop in the room, so the record keeps playing (and
 * fading with distance) when the player walks away. The profile's Mixer Group Output
 * routes it. Mode and gaps are re-read before every track, so tuning applies live.
 *
 * The Record child, when wired, spins at 33 rpm while a track is audible - the visual
 * that the needle is down.
 */
[DisallowMultipleComponent]
public class TurntableInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InteractionPromptUI promptUI;

    [Tooltip("Optional. Created on this object when empty - 3D, playing from the deck")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Optional. Spins at 33 rpm while a record plays (the add-tool wires the Record child)")]
    [SerializeField] private Transform record;

    [Header("Interaction Settings")]
    [SerializeField] private string objectDisplayName = "Turntable";
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string playMessage = "play record";
    [SerializeField] private string stopMessage = "stop record";
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("How close the player must get, in metres. Re-applied to the trigger on " +
             "Start, so it holds at this distance whatever scale the deck carries.")]
    [Min(0f)] [SerializeField] private float interactionReach = 2f;

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Turntable";

    [Tooltip("Compile-checked identity. When set, this wins over the name above")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    private const float RecordDegreesPerSecond = 200f;   // 33.3 rpm

    private bool playerInRange;
    private bool isPlaying;   // needle down: a track is audible or a gap is being waited out
    private InteractionAudioManager audioManager;
    private Coroutine spinning;

    // Scheduling state - reset every time the needle drops.
    private int nextTrackIndex;
    private int lastClipIndex = -1;
    private int previousClipIndex = -1;

    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    private void Start()
    {
        if (promptUI == null) promptUI = FindFirstObjectByType<InteractionPromptUI>();
        audioManager = FindFirstObjectByType<InteractionAudioManager>();

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }
        audioSource.spatialBlend = 1f;   // a deck in the room, not a sound in the head

        InteractionReach.Apply(gameObject, interactionReach);
        SeedPlayerInRange();
    }

    /// <summary>
    /// Picks up a player already inside the trigger at load - resizing a collider
    /// raises no OnTriggerEnter for someone standing within it.
    /// </summary>
    private void SeedPlayerInRange()
    {
        if (!InteractionReach.ContainsPlayer(gameObject)) return;

        playerInRange = true;
        ShowPrompt();
    }

    private void Update()
    {
        if (playerInRange && Input.GetKeyDown(interactKey))
        {
            if (isPlaying) StopRecord();
            else StartRecord();
            ShowPrompt();
        }

        if (record != null && isPlaying && audioSource != null && audioSource.isPlaying)
        {
            record.Rotate(0f, RecordDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }

    private void StartRecord()
    {
        isPlaying = true;

        // The needle goes back to the edge: every start is a fresh pass.
        nextTrackIndex = 0;
        lastClipIndex = -1;
        previousClipIndex = -1;

        spinning = StartCoroutine(SpinRoutine());
    }

    private void StopRecord()
    {
        isPlaying = false;

        if (spinning != null)
        {
            StopCoroutine(spinning);
            spinning = null;
        }

        if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
    }

    private IEnumerator SpinRoutine()
    {
        while (true)
        {
            if (!TryPlayTrack())
            {
                // Once In Order finished its pass - the record ends on its own.
                isPlaying = false;
                spinning = null;
                ShowPrompt();
                yield break;
            }

            // A track finishes before its gap starts, so the authored silence is the
            // silence the player actually hears, whatever length the tracks are.
            while (audioSource != null && audioSource.isPlaying) yield return null;

            // Fallback only while the profile is missing - a calm polling rate,
            // not a per-frame lookup. An authored gap of 0/0 is honoured above.
            float minGap = 2f;
            float maxGap = 2f;
            if (TryGetTurntableProfile(out var profile))
            {
                minGap = Mathf.Max(0f, profile.turntableMinGapSeconds);
                maxGap = Mathf.Max(minGap, profile.turntableMaxGapSeconds);
            }

            yield return new WaitForSeconds(Random.Range(minGap, maxGap));
        }
    }

    /// <summary>
    /// Plays the next track under the profile's playback mode. False only when a Once
    /// In Order pass has nothing left - the signal that the record has ended. A missing
    /// profile or empty array returns true and plays nothing: the loop keeps polling,
    /// so authoring tracks mid-game just works.
    /// </summary>
    private bool TryPlayTrack()
    {
        if (!TryGetTurntableProfile(out var profile)) return true;

        AudioClip[] clips = profile.turntableClips;
        if (clips == null || clips.Length == 0) return true;

        if (profile.turntablePlaybackMode == InteractionAudioManager.TrackPlaybackMode.OnceInOrder)
        {
            // Empty slots are skipped rather than stalling the record on them.
            while (nextTrackIndex < clips.Length && clips[nextTrackIndex] == null)
            {
                nextTrackIndex++;
            }

            if (nextTrackIndex >= clips.Length) return false;   // side over

            PlayClip(profile, clips[nextTrackIndex], $"track {nextTrackIndex + 1}/{clips.Length}");
            nextTrackIndex++;
            return true;
        }

        int pick = PickRandomIndex(clips);
        if (pick < 0) return true;

        PlayClip(profile, clips[pick], "track");
        previousClipIndex = lastClipIndex;
        lastClipIndex = pick;
        return true;
    }

    /// <summary>
    /// A random playable index under the no-repeat rules: never the track that just
    /// played (unless it is the only one), and with more than two playable tracks,
    /// never the one before that either.
    /// </summary>
    private int PickRandomIndex(AudioClip[] clips)
    {
        int playable = 0;
        foreach (AudioClip clip in clips)
        {
            if (clip != null) playable++;
        }
        if (playable == 0) return -1;

        List<int> candidates = new List<int>(clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            if (playable >= 2 && i == lastClipIndex) continue;
            if (playable > 2 && i == previousClipIndex) continue;
            candidates.Add(i);
        }

        // Unreachable while the rules above hold, but never let a logic slip silence
        // the deck forever.
        if (candidates.Count == 0) return lastClipIndex;

        return candidates[Random.Range(0, candidates.Count)];
    }

    private void PlayClip(InteractionAudioManager.InteractionSoundProfile profile,
                          AudioClip clip, string label)
    {
        if (profile.mixerGroupOutput != null)
        {
            audioSource.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        AudioEventLog.Record(AudioEventKind.Play, "Turntable", $"{label} '{clip.name}'");
        audioSource.clip = clip;
        audioSource.loop = false;
        audioSource.Play();
    }

    private bool TryGetTurntableProfile(out InteractionAudioManager.InteractionSoundProfile profile)
    {
        profile = null;
        if (audioManager == null) audioManager = FindFirstObjectByType<InteractionAudioManager>();
        return audioManager != null && audioManager.TryGetProfile(AudioProfileKey, out profile);
    }

    private void ShowPrompt()
    {
        if (promptUI == null || !playerInRange) return;

        promptUI.ShowPrompt(objectDisplayName, inputKey, isPlaying ? stopMessage : playMessage);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = true;
        ShowPrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;
        if (promptUI != null) promptUI.HidePrompt();
    }
}
