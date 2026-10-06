using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * WalkieTalkieItem.cs
 *
 * Purpose: A carried walkie talkie. While it sits in the inventory it crackles to
 *          life at random intervals; dropped, it goes silent until picked up again.
 * Used by: the Walkie Talkie Pickup prefab (Tools > Items > Create Walkie Talkie Pickup).
 *
 * AUDIO lives on the InteractionAudioManager's "Walkie Talkie" profile: the chatter
 * clips, the min/max seconds of silence between bursts, and the playback mode -
 * Random Continuous (forever; never the same clip twice in a row, nor, with three or
 * more clips, the previous one again) or Once In Order (first to last, one pass,
 * then silence; an interrupted line replays on the next pickup, a finished sequence
 * stays finished until the scene reloads). Authored there like every other
 * interaction sound; listed in Ctrl+G, red in Ctrl+J while the profile is missing.
 * Gap and mode are re-read from the profile before every burst, so tuning them in
 * the inspector applies live, mid-game.
 *
 * 2D by design: the radio is on the player's belt, not out in the world, so it plays
 * flat the way the UI does. The profile's Mixer Group Output routes it.
 *
 * The chatter runs on THIS object. Item.OnPickup only hides renderers and colliders -
 * the GameObject stays active - so the coroutine survives being carried, and stopping
 * on drop can cut a burst off mid-word, which is exactly what dropping a radio sounds
 * like. Bursts play through the source's clip (not PlayOneShot) for that reason:
 * PlayOneShot voices cannot be stopped.
 */
public class WalkieTalkieItem : Item
{
    [Header("Audio")]
    [SerializeField] private string audioProfileName = "Walkie Talkie";

    [Tooltip("Compile-checked identity. When set, this wins over the name above")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    [Tooltip("Seconds after pickup before the first burst - the radio crackling to life")]
    [Min(0f)] [SerializeField] private float firstBurstDelay = 1.5f;

    private AudioSource speaker;
    private InteractionAudioManager audioManager;
    private Coroutine chatter;

    // Scheduling state, per item lifetime (a scene reload resets everything).
    private int nextSequenceIndex;            // OnceInOrder: the next unplayed clip
    private bool lastBurstWasSequence;        // so an interrupted line can be re-queued
    private int lastClipIndex = -1;           // RandomContinuous: the no-repeat window
    private int previousClipIndex = -1;

    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    protected override void OnPickup()
    {
        base.OnPickup();
        StartChatter();
    }

    public override void Drop(Vector3 position)
    {
        StopChatter();
        base.Drop(position);
    }

    private void OnDestroy()
    {
        StopChatter();
    }

    private void StartChatter()
    {
        if (chatter != null) return;

        EnsureSpeaker();
        chatter = StartCoroutine(ChatterLoop());
    }

    private void StopChatter()
    {
        if (chatter != null)
        {
            StopCoroutine(chatter);
            chatter = null;
        }

        if (speaker != null && speaker.isPlaying)
        {
            // A sequence line cut off mid-word was not heard - queue it again for
            // the next pickup rather than losing it from the one-pass story.
            if (lastBurstWasSequence && nextSequenceIndex > 0) nextSequenceIndex--;

            speaker.Stop();
        }
    }

    private IEnumerator ChatterLoop()
    {
        yield return new WaitForSeconds(firstBurstDelay);

        while (true)
        {
            if (!TryPlayBurst()) yield break;   // once-in-order sequence complete

            // A burst finishes before its gap starts, so the authored silence is the
            // silence the player actually hears, whatever length the clips are.
            while (speaker != null && speaker.isPlaying) yield return null;

            float minGap = 8f;
            float maxGap = 20f;
            if (TryGetWalkieProfile(out var profile))
            {
                minGap = Mathf.Max(0f, profile.walkieMinGapSeconds);
                maxGap = Mathf.Max(minGap, profile.walkieMaxGapSeconds);
            }

            yield return new WaitForSeconds(Random.Range(minGap, maxGap));
        }
    }

    /// <summary>
    /// Plays the next burst under the profile's playback mode. False only when a
    /// Once In Order sequence has nothing left - the signal to end the loop. A
    /// missing profile or empty array returns true and plays nothing: the loop keeps
    /// polling, so authoring clips mid-game just works.
    /// </summary>
    private bool TryPlayBurst()
    {
        lastBurstWasSequence = false;

        if (!TryGetWalkieProfile(out var profile)) return true;

        AudioClip[] clips = profile.walkieClips;
        if (clips == null || clips.Length == 0) return true;

        if (profile.walkiePlaybackMode == InteractionAudioManager.TrackPlaybackMode.OnceInOrder)
        {
            // Empty slots are skipped rather than stalling the sequence on them.
            while (nextSequenceIndex < clips.Length && clips[nextSequenceIndex] == null)
            {
                nextSequenceIndex++;
            }

            if (nextSequenceIndex >= clips.Length) return false;   // one pass done

            PlayClip(profile, clips[nextSequenceIndex],
                     $"sequence {nextSequenceIndex + 1}/{clips.Length}");
            nextSequenceIndex++;
            lastBurstWasSequence = true;
            return true;
        }

        int pick = PickRandomIndex(clips);
        if (pick < 0) return true;

        PlayClip(profile, clips[pick], "chatter");
        previousClipIndex = lastClipIndex;
        lastClipIndex = pick;
        return true;
    }

    /// <summary>
    /// A random playable index under the no-repeat rules: never the clip that just
    /// played (unless it is the only one), and with more than two playable clips,
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

        // Unreachable while the rules above hold, but never let a logic slip
        // silence the radio forever.
        if (candidates.Count == 0) return lastClipIndex;

        return candidates[Random.Range(0, candidates.Count)];
    }

    private void PlayClip(InteractionAudioManager.InteractionSoundProfile profile,
                          AudioClip clip, string label)
    {
        if (profile.mixerGroupOutput != null)
        {
            speaker.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        AudioEventLog.Record(AudioEventKind.Play, "Walkie Talkie", $"{label} '{clip.name}'");
        speaker.clip = clip;
        speaker.loop = false;
        speaker.Play();
    }

    private void EnsureSpeaker()
    {
        if (speaker != null) return;

        speaker = GetComponent<AudioSource>();
        if (speaker == null) speaker = gameObject.AddComponent<AudioSource>();
        speaker.playOnAwake = false;
        speaker.spatialBlend = 0f;   // on the belt - always right at the ear
    }

    private bool TryGetWalkieProfile(out InteractionAudioManager.InteractionSoundProfile profile)
    {
        profile = null;
        if (audioManager == null) audioManager = FindFirstObjectByType<InteractionAudioManager>();
        return audioManager != null && audioManager.TryGetProfile(AudioProfileKey, out profile);
    }
}
