using UnityEngine;
using UnityEngine.Audio;

/*
 * HeartbeatLayer.cs
 *
 * Purpose: The foreground of being nearly dead - a heartbeat loop that rises and
 *          quickens as health falls, with an optional low-health mixer snapshot that
 *          softly closes the world down around it.
 * Used by: the scene; added with Tools > Audio > Add Heartbeat Layer.
 *
 * The adaptive music already carries a low-health TERM (MusicIntensityDriver), but that
 * is background: it raises stems. This is the body: 2D, in the player's chest, faster
 * and louder the closer to zero they are.
 *
 * Enter/exit use different thresholds (hysteresis) so health hovering on the line does
 * not flap the heart on and off. Death releases everything quickly - the death screen
 * should not have a heartbeat under it.
 *
 * The snapshot pair carries the same guard as the mushroom trip and the boss dip:
 * snapshot state lives on the mixer ASSET and survives scene reloads, so no exit path
 * may leave the low-health mix latched.
 */
[DisallowMultipleComponent]
public class HeartbeatLayer : MonoBehaviour
{
    [Tooltip("A LOOPING heartbeat clip. One or two beats per bar of clip works best - " +
             "the rate change comes from pitch")]
    [SerializeField] private AudioClip heartbeatLoop;

    [Tooltip("Mixer group for the heartbeat (SFX)")]
    [SerializeField] private AudioMixerGroup output;

    [Tooltip("The heart starts below this health fraction...")]
    [Range(0.05f, 0.9f)] [SerializeField] private float startBelowFraction = 0.35f;

    [Tooltip("...and stops above this one. The gap is hysteresis")]
    [Range(0.1f, 0.95f)] [SerializeField] private float stopAboveFraction = 0.45f;

    [Tooltip("Volume at the threshold -> at zero health")]
    [Range(0f, 1f)] [SerializeField] private float minVolume = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float maxVolume = 0.9f;

    [Tooltip("Pitch at the threshold -> at zero health (the quickening)")]
    [Min(0.5f)] [SerializeField] private float minPitch = 1f;
    [Min(0.5f)] [SerializeField] private float maxPitch = 1.35f;

    [Tooltip("Seconds for the heartbeat to fade in/out at the threshold")]
    [Min(0.05f)] [SerializeField] private float fadeSeconds = 1.5f;

    [Header("Low-health mix (optional)")]
    [Tooltip("Snapshot blended to while the heartbeat runs - a gentle lowpass/duck. " +
             "Leave empty to skip")]
    [SerializeField] private AudioMixerSnapshot lowHealthSnapshot;

    [Tooltip("Snapshot to return to - normally the mixer's default")]
    [SerializeField] private AudioMixerSnapshot normalSnapshot;

    [Min(0.1f)] [SerializeField] private float snapshotSeconds = 1.5f;

    private AudioSource source;
    private bool active;        // the hysteresis latch
    private readonly MixerDip mixDip = new MixerDip();
    private float level;        // 0..1 fade driver

    private void Update()
    {
        PlayerStats stats = PlayerStats.Instance;
        if (stats == null) return;

        float max = stats.GetMaxHealth();
        float fraction = max > 0f ? Mathf.Clamp01(stats.GetCurrentHealth() / max) : 1f;
        bool dead = stats.IsDead();

        // Hysteresis: below start turns it on, above stop turns it off, the gap holds.
        // The stop line is clamped above the start line - authored inverted, the gap
        // would become the exact flapping it exists to prevent.
        float stopLine = Mathf.Max(stopAboveFraction, startBelowFraction + 0.02f);
        if (!active && !dead && fraction <= startBelowFraction) SetActive(true);
        else if (active && (dead || fraction >= stopLine)) SetActive(false);

        // Death releases fast - the death screen should not have a heartbeat under it.
        float step = Time.deltaTime / (dead ? 0.4f : fadeSeconds);
        level = Mathf.MoveTowards(level, active ? 1f : 0f, step);

        if (source == null) return;

        if (level <= 0.001f)
        {
            if (source.isPlaying) source.Stop();
            return;
        }

        if (!source.isPlaying && heartbeatLoop != null)
        {
            source.clip = heartbeatLoop;
            source.Play();
            AudioEventLog.Record(AudioEventKind.Play, "Heartbeat", heartbeatLoop.name);
        }

        // Danger, 0 at the threshold -> 1 at zero health, drives level and rate.
        float danger = startBelowFraction > 0f
            ? Mathf.Clamp01(1f - fraction / startBelowFraction)
            : 1f;

        source.volume = Mathf.Lerp(minVolume, maxVolume, danger) * level;
        source.pitch = Mathf.Lerp(minPitch, maxPitch, danger);
    }

    private void SetActive(bool value)
    {
        active = value;
        EnsureSource();

        if (value) mixDip.Dip(lowHealthSnapshot, normalSnapshot, snapshotSeconds);
        else mixDip.Restore(snapshotSeconds);
    }

    private void EnsureSource()
    {
        if (source != null) return;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;   // in the chest, not in the world
        if (output != null) source.outputAudioMixerGroup = output;
    }

    private void OnDestroy()
    {
        // Mixer state survives scene reloads; dying while low must not leave the world
        // lowpassed into the next run.
        mixDip.Restore(0.5f);
    }
}
