using UnityEngine;

/*
 * MusicConductor.cs
 *
 * Purpose: The musical clock for whichever layered state is currently playing.
 * Used by: MusicManager (to resolve transition sync points), MusicDebugOverlay
 *
 * Everything here is pure arithmetic on AudioSettings.dspTime measured from an anchor
 * set when a state entered. Musical decisions must NEVER be taken from
 * AudioSource.time: that is a buffered playback position, it drifts, and it stalls
 * while the game is paused.
 *
 * Each state runs on its own grid. On a transition the sync point is computed on the
 * OUTGOING state's grid, and the incoming state's anchor is then set to exactly that
 * point, so two states with different BPMs hand over cleanly.
 */

public class MusicConductor
{
    /// <summary>
    /// Boundaries closer than this are skipped in favour of the next one. PlayScheduled
    /// needs runway: scheduling into the immediate future produces a late or dropped start.
    /// </summary>
    public const double ScheduleMargin = 0.05;

    private double anchorDsp;
    private double beatDuration = 0.5;
    private int beatsPerBar = 4;

    public bool IsRunning { get; private set; }

    public double AnchorDsp => anchorDsp;
    public double BeatDuration => beatDuration;
    public double BarDuration => beatDuration * beatsPerBar;
    public int BeatsPerBar => beatsPerBar;
    public float Bpm => (float)(60.0 / beatDuration);

    public void Begin(double dspStartTime, float bpm, int beatsPerBar)
    {
        anchorDsp = dspStartTime;
        beatDuration = 60.0 / Mathf.Max(20f, bpm);
        this.beatsPerBar = Mathf.Max(1, beatsPerBar);
        IsRunning = true;
    }

    /// <summary>
    /// Moves the grid onto a known bar boundary without changing tempo or meter. Segment
    /// starts are bar-aligned by construction, so re-anchoring to the playing segment
    /// keeps the grid locked to what is actually audible even if a frame hitch made a
    /// segment start late.
    /// </summary>
    public void Rebase(double barBoundaryDsp)
    {
        if (!IsRunning) return;
        anchorDsp = barBoundaryDsp;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    /// <summary>Earliest grid boundary at or after <paramref name="afterDsp"/> + margin.</summary>
    public double NextBoundary(double afterDsp, double interval)
    {
        double earliest = afterDsp + ScheduleMargin;
        if (!IsRunning || interval <= 0.0) return earliest;

        double elapsed = earliest - anchorDsp;
        double steps = System.Math.Ceiling(elapsed / interval);

        // An anchor in the future would otherwise return a boundary BEFORE earliest,
        // which PlayScheduled treats as "now" - breaking the contract this method exists
        // to provide.
        if (steps < 0.0) return earliest;

        return anchorDsp + steps * interval;
    }

    public double NextBeat(double afterDsp) => NextBoundary(afterDsp, beatDuration);

    public double NextBar(double afterDsp) => NextBoundary(afterDsp, BarDuration);

    public int CurrentBar(double dsp)
    {
        if (!IsRunning) return 0;
        return Mathf.Max(0, (int)System.Math.Floor((dsp - anchorDsp) / BarDuration));
    }

    /// <summary>1-based beat within the current bar, for a readable bar:beat display.</summary>
    public int CurrentBeatInBar(double dsp)
    {
        if (!IsRunning) return 1;
        double elapsed = dsp - anchorDsp;
        if (elapsed < 0.0) return 1;
        int beat = (int)System.Math.Floor(elapsed / beatDuration);
        return (beat % beatsPerBar) + 1;
    }
}
