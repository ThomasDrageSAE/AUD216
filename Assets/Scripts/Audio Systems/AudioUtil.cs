using UnityEngine;

/*
 * AudioUtil.cs
 *
 * Purpose: Shared helpers for randomized one-shot playback.
 * Used by: FootstepAudioManager, WeaponAudioManager, PlayerAudioManager,
 *          EnemyAudioManager, RandomTriggerAudioVariations, and any component
 *          that plays a random clip with pitch/volume variation.
 *
 * Replaces the copy-pasted "pick a random clip, randomize pitch, PlayOneShot"
 * snippet that previously lived in ~9 different scripts. Centralizing it also
 * fixes the recurring pitch bug in one place: pitch must stay set while the
 * one-shot plays (resetting it immediately after PlayOneShot cancels the
 * variation on the playing clip), so callers should give one-shots a source
 * that nothing else is playing on at the same time.
 */
public static class AudioUtil
{
    /// <summary>
    /// Returns a random index into a collection of the given length, avoiding
    /// lastIndex when the collection has more than one element.
    /// </summary>
    public static int GetRandomIndexNoRepeat(int length, int lastIndex)
    {
        if (length <= 1) return 0;

        // Pick from length-1 slots and skip over lastIndex - constant time,
        // no retry loop needed.
        int index = Random.Range(0, length - 1);
        if (index >= lastIndex && lastIndex >= 0) index++;
        return index;
    }

    /// <summary>
    /// Plays a random clip from the array as a one-shot with pitch and volume
    /// variation, avoiding an immediate repeat of lastIndex. Updates lastIndex
    /// to the clip chosen. Safe to call with a null/empty array (does nothing).
    /// The source's pitch is left at the randomized value so the playing clip
    /// keeps its variation; it is re-randomized on the next call.
    /// </summary>
    /// <param name="noisyLog">
    /// Marks the logged event as high-frequency, so the Ctrl+J monitor hides it. Set by
    /// footsteps, which otherwise fill every visible row within a couple of strides.
    /// </param>
    public static void PlayRandomOneShot(
        AudioSource source, AudioClip[] clips, ref int lastIndex,
        float volume = 1f, float minPitch = 1f, float maxPitch = 1f, bool noisyLog = false)
    {
        if (source == null || clips == null || clips.Length == 0) return;

        int index = GetRandomIndexNoRepeat(clips.Length, lastIndex);
        AudioClip clip = clips[index];
        if (clip == null) return;

        source.pitch = (minPitch == 1f && maxPitch == 1f) ? 1f : Random.Range(minPitch, maxPitch);
        source.PlayOneShot(clip, volume);
        lastIndex = index;

        AudioEventLog.Record(AudioEventKind.Play, source.gameObject.name, clip.name, noisyLog);
    }
}
