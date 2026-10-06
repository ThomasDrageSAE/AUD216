using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * AudioEventLog.cs
 *
 * Purpose: A small fixed-size record of audio events as they happen - which profile a
 *          lookup resolved to, which clip actually played, when the music state moved,
 *          and crucially which lookups found nothing.
 * Used by: AudioEventMonitor (the Ctrl+J overlay). Written to by AudioProfileKeys,
 *          AudioUtil, UIAudioManager and MusicManager.
 *
 * Assessment context: students implementing sound need to see WHEN an event fires and
 * WHAT answered it. A silent moment is ambiguous - no event, or an event whose profile
 * name did not match? The Miss rows answer that directly, and they keep answering it
 * after AudioProfileKeys' warn-once has gone quiet for a key it already reported.
 *
 * Recording is unconditional and cheap (a struct into a ring buffer) so the overlay can
 * show events that happened BEFORE it was opened. The ring is cleared on scene load as
 * well as at startup: RuntimeInitializeOnLoadMethod alone fires once per play session,
 * not per SceneManager.LoadScene, so an in-game restart would otherwise show the
 * previous run's events.
 */

public enum AudioEventKind
{
    /// <summary>A profile resolved, or a clip actually played.</summary>
    Play,
    /// <summary>A keyed lookup found no profile - the sound cannot play.</summary>
    Miss,
    /// <summary>A music state change.</summary>
    State
}

public struct AudioEventEntry
{
    public float Time;
    public AudioEventKind Kind;
    public string Source;
    public string Detail;

    /// <summary>
    /// A high-frequency event the monitor hides, because listing it buries everything
    /// else. Footsteps are the only such source today: they fire several times a second
    /// and would fill all 16 visible rows within a couple of strides. Only successful
    /// plays are marked - a footstep MISS is rare and diagnostic, so it still shows.
    /// </summary>
    public bool Noisy;
}

public static class AudioEventLog
{
    public const int Capacity = 128;

    private static readonly AudioEventEntry[] entries = new AudioEventEntry[Capacity];
    private static int nextSlot;
    private static int count;

    public static int Count => count;

    public static void Record(AudioEventKind kind, string source, string detail, bool noisy = false)
    {
        entries[nextSlot] = new AudioEventEntry
        {
            // Unscaled: the end screen and pause menu freeze Time.time, and an event
            // logged while paused should still read as "just now".
            Time = UnityEngine.Time.unscaledTime,
            Kind = kind,
            Source = source,
            Detail = detail,
            Noisy = noisy
        };

        nextSlot = (nextSlot + 1) % Capacity;
        if (count < Capacity) count++;
    }

    /// <summary>The most recent entries, newest first.</summary>
    public static IEnumerable<AudioEventEntry> Recent(int max)
    {
        int wanted = Mathf.Min(max, count);
        for (int i = 1; i <= wanted; i++)
        {
            yield return entries[(nextSlot - i + Capacity) % Capacity];
        }
    }

    public static void Clear()
    {
        nextSlot = 0;
        count = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        Clear();

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Clear();
    }
}
