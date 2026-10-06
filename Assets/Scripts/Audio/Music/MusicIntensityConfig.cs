using UnityEngine;

/*
 * MusicIntensityConfig.cs
 *
 * Purpose: Drives the vertical dimension - a single smoothed 0-1 "intensity" that
 * decides which stems are audible.
 * Used by: MusicManager
 *
 * Sources are combined with MAX, not sum: the loudest reason wins, the result stays
 * in 0-1 by construction, and layers cannot over-trigger by stacking mild reasons.
 * A scripted override acts as a floor, so gameplay can push above it but not below.
 *
 * Gameplay sources are polled at PollInterval rather than every frame; the smoothing
 * runs per frame on UNSCALED time (the pause menu sets timeScale to 0).
 */

[System.Serializable]
public class MusicIntensityConfig
{
    [Header("Combat (dominant driver)")]
    [Tooltip("Active zombie count that corresponds to full combat intensity")]
    [Min(1)] public int zombiesForFullIntensity = 5;

    [Header("Low health")]
    [Tooltip("Player health at or below this contributes tension")]
    public float lowHealthThreshold = 30f;

    [Tooltip("Intensity contributed while the player is below the health threshold")]
    [Range(0f, 1f)] public float lowHealthContribution = 0.4f;

    [Header("Night approach (DayNightCycle)")]
    [Tooltip("Intensity ramped in from dusk start to full night")]
    [Range(0f, 1f)] public float duskStartContribution = 0.25f;

    [Header("Response")]
    [Tooltip("Seconds to approach a RISING intensity. Short = responsive")]
    [Min(0.05f)] public float attackSeconds = 1.0f;

    [Tooltip("Seconds to approach a FALLING intensity. Long = music relaxes gently")]
    [Min(0.05f)] public float releaseSeconds = 6.0f;
}

/// <summary>
/// Runtime evaluation of <see cref="MusicIntensityConfig"/>. Plain class, owned and
/// ticked by MusicManager. Every gameplay source is optional: a missing one contributes
/// zero rather than disabling the system.
/// </summary>
public class MusicIntensityDriver
{
    private const float PollInterval = 0.25f;

    private readonly MusicIntensityConfig config;

    private ZombieManager zombieManager;
    private PlayerStats playerStats;
    private DayNightCycle dayNightCycle;

    private float nextPollTime;
    private float overrideValue = -1f;

    public float Raw { get; private set; }
    public float Smoothed { get; private set; }

    // Individual contributions, exposed for the debug overlay.
    public float CombatContribution { get; private set; }
    public float HealthContribution { get; private set; }
    public float NightContribution { get; private set; }
    public float OverrideValue => overrideValue;
    public bool HasOverride => overrideValue >= 0f;
    public int ActiveZombies { get; private set; }

    public MusicIntensityDriver(MusicIntensityConfig config)
    {
        this.config = config ?? new MusicIntensityConfig();
    }

    public void ResolveSources()
    {
        ResolveMissingSources();
    }

    /// <summary>
    /// Re-acquires any source that is currently missing. Run on each poll rather than once
    /// at startup: a source created later (additive load, a respawned player) would
    /// otherwise be null forever and contribute zero silently, which is hard to diagnose.
    /// </summary>
    private void ResolveMissingSources()
    {
        if (zombieManager == null) zombieManager = Object.FindFirstObjectByType<ZombieManager>();
        if (playerStats == null) playerStats = Object.FindFirstObjectByType<PlayerStats>();
        if (dayNightCycle == null) dayNightCycle = Object.FindFirstObjectByType<DayNightCycle>();
    }

    /// <summary>Sets a floor on intensity. Gameplay can exceed it, nothing can go below it.</summary>
    public void SetOverride(float value01)
    {
        overrideValue = Mathf.Clamp01(value01);
        Raw = EvaluateRaw();   // don't wait up to a poll interval to take effect
    }

    public void ClearOverride()
    {
        overrideValue = -1f;
        Raw = EvaluateRaw();
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (Time.unscaledTime >= nextPollTime)
        {
            nextPollTime = Time.unscaledTime + PollInterval;
            ResolveMissingSources();
            Raw = EvaluateRaw();
        }

        // Attack/release smoothing. Rising is quick so combat lands promptly; falling is
        // slow so the music does not snap back the instant the last zombie drops.
        float seconds = Raw > Smoothed ? config.attackSeconds : config.releaseSeconds;
        float step = unscaledDeltaTime / Mathf.Max(0.05f, seconds);
        Smoothed = Mathf.MoveTowards(Smoothed, Raw, step);
    }

    private float EvaluateRaw()
    {
        CombatContribution = 0f;
        HealthContribution = 0f;
        NightContribution = 0f;
        ActiveZombies = 0;

        if (zombieManager != null)
        {
            ActiveZombies = zombieManager.GetActiveZombieCount();
            CombatContribution = Mathf.Clamp01(ActiveZombies / (float)Mathf.Max(1, config.zombiesForFullIntensity));
        }

        if (playerStats != null && playerStats.GetCurrentHealth() <= config.lowHealthThreshold)
        {
            HealthContribution = config.lowHealthContribution;
        }

        if (dayNightCycle != null && config.duskStartContribution > 0f)
        {
            float now = dayNightCycle.GetTimeOfDay();
            float duskStart = dayNightCycle.GetDuskStartTime();
            float night = dayNightCycle.GetNightTime();
            float dawnStart = dayNightCycle.GetDawnStartTime();

            if (now < dawnStart)
            {
                // Past midnight. timeOfDay has wrapped through 1.0 back to 0, but it is
                // still night until dawn - testing only "now >= duskStart" collapsed the
                // night term to zero for the darkest part of the night.
                NightContribution = config.duskStartContribution;
            }
            else if (now >= duskStart && night > duskStart)
            {
                float ramp = Mathf.Clamp01((now - duskStart) / (night - duskStart));
                NightContribution = ramp * config.duskStartContribution;
            }
        }

        float value = Mathf.Max(CombatContribution, Mathf.Max(HealthContribution, NightContribution));
        if (HasOverride) value = Mathf.Max(value, overrideValue);
        return Mathf.Clamp01(value);
    }
}
