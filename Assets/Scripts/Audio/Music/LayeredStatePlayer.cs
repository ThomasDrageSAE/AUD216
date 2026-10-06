using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/*
 * LayeredStatePlayer.cs
 *
 * Purpose: Plays one adaptive music state - schedules its segments gaplessly
 * (horizontal) and mixes its stems by intensity (vertical).
 * Used by: MusicManager
 *
 * Source layout: two banks of AudioSources per stem slot. Segment N plays on one bank
 * while segment N+1 is PlayScheduled on the other at N's musical end. The finished bank
 * is left running rather than stopped, so a clip longer than its musical length rings
 * its reverb tail past the loop point; it is only reclaimed a whole segment later.
 *
 * All stems of a segment are started with the SAME PlayScheduled value, so they are
 * sample-locked for as long as they play. After that only volumes ever change.
 *
 * Fade state lives per stem SLOT, not per bank, so a layer keeps its gate across a
 * segment change (slot index is layer identity - see MusicStateDefinition).
 *
 * All fades run on unscaled time: the pause menu sets timeScale to 0 while the DSP
 * clock keeps running, and a scaled fade would freeze mid-crossfade.
 */

public class LayeredStatePlayer
{
    /// <summary>How far ahead of a segment boundary the next segment is scheduled.</summary>
    private const double ScheduleLookahead = 1.0;

    /// <summary>
    /// A stem already audible only drops out once intensity falls this far below its
    /// threshold, so hovering exactly on a threshold cannot flap the layer on and off.
    /// </summary>
    private const float Hysteresis = 0.05f;

    private readonly MusicStateDefinition definition;
    private readonly AudioSource[,] banks;          // [bank, stemSlot]
    private readonly MusicSegment[] bankSegment = new MusicSegment[2];
    private readonly bool[] bankScheduled = new bool[2];
    private readonly List<MusicSegment> usableLoops = new List<MusicSegment>();

    private readonly float[] stemGate;              // 0-1 smoothed gate, per slot
    private readonly bool[] stemAudible;            // hysteresis latch, per slot
    private readonly bool[] debugMute;              // overlay auditioning aids, per slot
    private readonly bool[] debugSolo;

    private int currentBank;
    private MusicSegment currentSegment;
    private double currentStartDsp;
    private double currentEndDsp;
    private bool currentIsIntro;

    private bool hasPending;
    private int pendingBank;
    private MusicSegment pendingSegment;
    private double pendingStartDsp;
    private bool pendingIsIntro;

    private bool stopAfterCurrent;
    private int loopCursor = -1;
    private bool primeGates;
    private bool verifyEntryStart;

    private float stateFade;
    private float stateFadeTarget;
    private float stateFadeRate = 1f;
    private bool isFadingOut;

    public MusicStateDefinition Definition => definition;
    public bool IsActive { get; private set; }
    public bool IsAudible => IsActive || stateFade > 0.001f;
    public double CurrentSegmentEndDsp => currentEndDsp;

    /// <summary>
    /// DSP time the playing segment began. Segments are whole bars, so this is always a
    /// bar boundary and is what the conductor re-anchors its grid to.
    /// </summary>
    public double CurrentSegmentStartDsp => currentStartDsp;

    /// <summary>True once an outro has been scheduled and no further loops will follow.</summary>
    public bool IsExiting => stopAfterCurrent;

    /// <summary>
    /// True only once this state's audio has actually begun. IsActive is set as soon as an
    /// entry is *scheduled*, which may be a bar in the future, so anything that cuts or
    /// retires playing audio must test this instead.
    /// </summary>
    public bool IsPlaying => IsActive && AudioSettings.dspTime >= currentStartDsp;

    /// <summary>DSP time this state finishes when it is exiting (the end of its outro).</summary>
    public double ExitEndDsp => hasPending && pendingSegment != null
        ? pendingStartDsp + definition.GetSegmentDuration(pendingSegment)
        : currentEndDsp;
    public MusicSegment CurrentSegment => currentSegment;
    public float StateFade => stateFade;
    public int StemSlotCount => stemGate.Length;

    public LayeredStatePlayer(MusicStateDefinition definition, GameObject host, AudioMixerGroup mixerGroup)
    {
        this.definition = definition;

        int slots = Mathf.Max(1, definition.MaxStemCount);
        banks = new AudioSource[2, slots];
        stemGate = new float[slots];
        stemAudible = new bool[slots];
        debugMute = new bool[slots];
        debugSolo = new bool[slots];

        for (int bank = 0; bank < 2; bank++)
        {
            for (int slot = 0; slot < slots; slot++)
            {
                AudioSource source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;              // segment chaining is scheduled by hand
                source.volume = 0f;
                source.spatialBlend = 0f;         // music is 2D
                source.ignoreListenerPause = false;
                // Unity virtualises the QUIETEST real voices first, which is exactly the
                // fading stems this system depends on. 0 = highest priority.
                source.priority = 0;
                if (mixerGroup != null) source.outputAudioMixerGroup = mixerGroup;
                banks[bank, slot] = source;
            }
        }

        if (definition.loopSegments != null)
        {
            foreach (MusicSegment segment in definition.loopSegments)
            {
                if (segment != null && segment.HasContent) usableLoops.Add(segment);
            }
        }

        // Kick the clip loads now rather than at the first PlayScheduled. An entry is
        // scheduled only ScheduleMargin ahead, and a clip whose data is still loading
        // misses that slot and starts late from the top of the clip - the defect
        // VerifyEntryStart exists to catch. A no-op for clips the importer preloaded.
        foreach (MusicSegment segment in definition.EnumerateSegments())
        {
            for (int slot = 0; slot < segment.StemCount; slot++)
            {
                MusicStem stem = segment.stems[slot];
                if (stem != null && stem.clip != null &&
                    stem.clip.loadState == AudioDataLoadState.Unloaded)
                {
                    stem.clip.LoadAudioData();
                }
            }
        }
    }

    // ---- Entry / exit --------------------------------------------------------

    /// <summary>Schedules this state to begin at an exact DSP time.</summary>
    public void ScheduleEntry(double dspTime, bool viaIntro)
    {
        bool useIntro = viaIntro && definition.HasIntro;

        if (IsPlaying)
        {
            // Graceful takeover. This state is still audible - typically fading out after
            // a departure the game just changed its mind about (a skirmish ending seconds
            // after it began). StopEverything here would click and leave silence until
            // dspTime; instead the playing segment is retired exactly at the entry point
            // and the new first segment rides in on the free bank as a normal pending
            // handover. Gate and fade state carry across, so the music stays continuous.
            PromoteIfDue(AudioSettings.dspTime);
            CancelPending();

            MusicSegment takeoverFirst = useIntro ? definition.introSegment : PickFirstLoop();
            if (takeoverFirst == null) return;

            // Retire the audible segment at the handover. When the entry lands past this
            // segment's natural end, it simply ends there - the brief quiet gap sits
            // inside an already-fading state and is benign, while shortening the entry
            // time would break the bar alignment the manager promised.
            if (dspTime < currentEndDsp - 0.001)
            {
                SetBankScheduledEnd(currentBank, dspTime);
            }

            int freeBank = 1 - currentBank;
            ScheduleSegmentOnBank(takeoverFirst, freeBank, dspTime);

            pendingBank = freeBank;
            pendingSegment = takeoverFirst;
            pendingStartDsp = dspTime;
            pendingIsIntro = useIntro;
            hasPending = true;

            stopAfterCurrent = false;
            IsActive = true;

            // Hold the fade where it is; the manager ramps it back to full at the entry
            // point. Clearing isFadingOut matters: the release-at-zero path would
            // otherwise stop everything - including the entry just scheduled.
            isFadingOut = false;
            stateFadeTarget = stateFade;
            return;
        }

        StopEverything();

        MusicSegment first = useIntro ? definition.introSegment : PickFirstLoop();
        if (first == null) return;

        currentIsIntro = useIntro;
        currentBank = 0;
        currentSegment = first;
        currentStartDsp = dspTime;
        currentEndDsp = dspTime + definition.GetSegmentDuration(first);
        stopAfterCurrent = false;
        IsActive = true;

        // Snap the gates to their steady state on the first tick instead of ramping from
        // zero. Otherwise an Immediate entry (0.05s of lead) becomes audible with every
        // layer at zero and swells in over fadeInTime, so combat music that is supposed
        // to hit instantly arrives over a second and a half.
        primeGates = true;

        // A fresh entry starts from silence, so nothing would reveal a missed schedule;
        // the first Ticks confirm the audio really began where the bookkeeping says.
        verifyEntryStart = true;

        ScheduleSegmentOnBank(first, currentBank, dspTime);
    }

    /// <summary>
    /// Cuts the current segment at <paramref name="dspTime"/> and plays the outro there.
    /// Returns the DSP time the outro ends, which is when the next state should enter.
    /// </summary>
    public double ScheduleExitOutro(double dspTime)
    {
        // IsPlaying, not IsActive: a state whose entry is merely scheduled has no audio to
        // exit, and cutting it would set an end time earlier than its own start.
        if (!definition.HasOutro || !IsPlaying) return dspTime;

        // A segment that has already handed over must be promoted first, or the bank
        // indices below refer to audio that is no longer current.
        PromoteIfDue(AudioSettings.dspTime);

        // The outro replaces the REST of the current segment. If the cut point is already
        // at or past that segment's end, cancelling the queued next loop would leave a
        // hole, so the outro simply follows on instead.
        if (dspTime > currentEndDsp) dspTime = currentEndDsp;

        CancelPending();

        // Only truncate when genuinely cutting mid-segment; at an exact boundary this
        // would clip the reverb tail the two-bank design exists to preserve.
        if (dspTime < currentEndDsp - 0.001) SetBankScheduledEnd(currentBank, dspTime);

        int bank = 1 - currentBank;
        ScheduleSegmentOnBank(definition.outroSegment, bank, dspTime);

        pendingBank = bank;
        pendingSegment = definition.outroSegment;
        pendingStartDsp = dspTime;
        pendingIsIntro = false;
        hasPending = true;
        stopAfterCurrent = true;

        return dspTime + definition.GetSegmentDuration(definition.outroSegment);
    }

    public void BeginFadeIn(float duration)
    {
        isFadingOut = false;
        stateFadeTarget = 1f;
        stateFadeRate = duration <= 0.001f ? float.MaxValue : 1f / duration;
    }

    public void BeginFadeOut(float duration)
    {
        isFadingOut = true;
        stateFadeTarget = 0f;
        stateFadeRate = duration <= 0.001f ? float.MaxValue : 1f / duration;
    }

    public void SetFadeImmediate(float value)
    {
        isFadingOut = false;
        stateFade = stateFadeTarget = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Abandons a scheduled exit so the state carries on looping. Used when the game
    /// changes its mind about leaving while an exit is in flight - re-entering through
    /// ScheduleEntry would cut audible music dead.
    ///
    /// If the outro has not started yet, it is dropped and the playing segment's natural
    /// end is restored (SetScheduledEndTime can move an end back out), so nothing of the
    /// exit is ever heard. If the outro is already playing, it is left to finish and the
    /// loops chain on after it.
    /// </summary>
    public void ResumeLooping()
    {
        if (!stopAfterCurrent) return;
        stopAfterCurrent = false;

        PromoteIfDue(AudioSettings.dspTime);

        if (hasPending)
        {
            CancelPending();
            SetBankScheduledEnd(currentBank, currentEndDsp);
        }
    }

    /// <summary>
    /// Abandons a scheduled entry. A player that is not yet audible is stopped outright,
    /// which cancels its PlayScheduled. A player that IS audible - a takeover scheduled on a
    /// state still fading out - keeps playing: the scheduled takeover segment is dropped, the
    /// playing segment's natural end is restored, and it resumes fading out over
    /// <paramref name="fadeOutSeconds"/> instead of being cut.
    /// </summary>
    public void AbandonEntry(float fadeOutSeconds)
    {
        if (!IsPlaying)
        {
            StopEverything();
            return;
        }

        PromoteIfDue(AudioSettings.dspTime);

        if (hasPending)
        {
            CancelPending();
            SetBankScheduledEnd(currentBank, currentEndDsp);
        }

        BeginFadeOut(fadeOutSeconds);
    }

    /// <summary>Cancels a scheduled-but-not-yet-started segment (Stop cancels PlayScheduled).</summary>
    public void CancelPending()
    {
        if (!hasPending) return;
        StopBank(pendingBank);
        hasPending = false;
        pendingSegment = null;
        pendingIsIntro = false;
    }

    public void StopEverything()
    {
        CancelPending();
        StopBank(0);
        StopBank(1);
        IsActive = false;
        currentSegment = null;
        currentIsIntro = false;
        stopAfterCurrent = false;
        loopCursor = -1;
        primeGates = false;
        verifyEntryStart = false;
        isFadingOut = false;
        stateFade = stateFadeTarget = 0f;

        for (int i = 0; i < stemGate.Length; i++)
        {
            stemGate[i] = 0f;
            stemAudible[i] = false;
        }
    }

    // ---- Per-frame -----------------------------------------------------------

    public void Tick(double dspNow, float intensity, float unscaledDeltaTime)
    {
        PromoteIfDue(dspNow);

        // Before the scheduling block below, so the next segment is booked against the
        // corrected end time, not the one the missed schedule invalidated.
        VerifyEntryStart(dspNow);

        if (IsActive && !hasPending && !stopAfterCurrent && dspNow >= currentEndDsp - ScheduleLookahead)
        {
            MusicSegment next = PickNextLoop();
            if (next != null)
            {
                double nextStart = currentEndDsp;

                // If a frame hitch or load spike swallowed the boundary entirely,
                // scheduling in the past would make the next segment start immediately
                // and, for a long enough stall, burst through several segments trying to
                // catch up. Start from now instead and let the grid re-anchor.
                if (nextStart < dspNow) nextStart = dspNow + MusicConductor.ScheduleMargin;

                pendingBank = 1 - currentBank;
                pendingSegment = next;
                pendingStartDsp = nextStart;
                pendingIsIntro = false;
                hasPending = true;
                ScheduleSegmentOnBank(next, pendingBank, pendingStartDsp);
            }
        }

        if (IsActive && stopAfterCurrent && !hasPending && dspNow >= currentEndDsp)
        {
            IsActive = false;
        }

        UpdateStateFade(unscaledDeltaTime);
        UpdateStemGates(intensity, unscaledDeltaTime);
        ApplyVolumes();
    }

    /// <summary>
    /// Adopts a scheduled segment once its start time has passed. Split out of Tick so
    /// anything that reasons about "the current bank" can settle a handover first - the
    /// audio thread starts the segment at its scheduled time, which may be up to a frame
    /// before this runs.
    /// </summary>
    private void PromoteIfDue(double dspNow)
    {
        if (!hasPending || dspNow < pendingStartDsp) return;

        // The scheduled segment has started; it is now the current one and the bank it
        // replaced is left ringing out.
        currentBank = pendingBank;
        currentSegment = pendingSegment;
        currentStartDsp = pendingStartDsp;
        currentEndDsp = pendingStartDsp + definition.GetSegmentDuration(pendingSegment);
        currentIsIntro = pendingIsIntro;   // a takeover entry may arrive via the intro
        hasPending = false;
        pendingSegment = null;
        pendingIsIntro = false;
    }

    /// <summary>
    /// A fresh entry is scheduled only ScheduleMargin ahead, and at scene load the stem
    /// clips may still be loading - PlayScheduled then misses its slot and the audio
    /// starts LATE, from the top of the clip, while currentStartDsp still records the
    /// time that was asked for. Every boundary derived from it (above all the first
    /// loop) would land early by the lateness. Once the audio is genuinely running,
    /// measure the playhead and shift the segment window onto it; the manager re-anchors
    /// the conductor from CurrentSegmentStartDsp, so the grid follows.
    /// </summary>
    private void VerifyEntryStart(double dspNow)
    {
        if (!verifyEntryStart) return;

        // A boundary already booked would not follow the shift; too late to correct.
        if (!IsActive || hasPending)
        {
            verifyEntryStart = false;
            return;
        }

        if (dspNow < currentStartDsp) return;

        for (int slot = 0; slot < stemGate.Length; slot++)
        {
            AudioSource source = banks[currentBank, slot];
            if (source.clip == null) continue;

            // Mid-load the playhead cannot be measured (timeSamples sits at 0, which
            // reads as "started this instant" and would under-correct). Keep waiting;
            // the source starts the moment the data lands.
            if (source.clip.loadState == AudioDataLoadState.Loading) return;

            double actual = (double)source.timeSamples / source.clip.frequency;
            double late = (dspNow - currentStartDsp) - actual;

            // The mixer quantises starts to its buffer (~20ms); anything past the
            // schedule margin means the start really was missed.
            if (late > MusicConductor.ScheduleMargin)
            {
                currentStartDsp += late;
                currentEndDsp += late;
            }

            verifyEntryStart = false;
            return;
        }

        verifyEntryStart = false;   // no stem carries a clip; nothing to measure
    }

    private void UpdateStateFade(float unscaledDeltaTime)
    {
        if (isFadingOut)
        {
            // Release explicitly rather than relying on crossing zero: a fade-out asked
            // for while already silent would otherwise leave the player looping forever
            // at zero volume, holding its clips.
            stateFade = Mathf.MoveTowards(stateFade, 0f, stateFadeRate * unscaledDeltaTime);
            if (stateFade <= 0.0001f) StopEverything();
            return;
        }

        if (Mathf.Approximately(stateFade, stateFadeTarget)) return;
        stateFade = Mathf.MoveTowards(stateFade, stateFadeTarget, stateFadeRate * unscaledDeltaTime);
    }

    private void UpdateStemGates(float intensity, float unscaledDeltaTime)
    {
        MusicSegment config = currentSegment ?? bankSegment[currentBank];

        // Nothing loaded (stopped, or faded fully out). Leave the gates alone: with no
        // config every slot would look like an always-on layer and climb toward full
        // while the state is silent, so the next entry would start mid-fade.
        if (config == null) return;

        for (int slot = 0; slot < stemGate.Length; slot++)
        {
            MusicStem stem = (slot < config.StemCount) ? config.stems[slot] : null;
            bool slotExists = stem != null && stem.clip != null;

            float minIntensity = slotExists ? stem.minIntensity : 0f;
            float fadeRange = slotExists ? stem.fadeRange : 0f;
            bool banded = slotExists && fadeRange > 0.001f;

            float threshold = stemAudible[slot] ? minIntensity - Hysteresis : minIntensity;

            // A slot this segment does not use is absent, not "always on". Treating it as
            // minIntensity 0 drove its gate to full, which both pushed the other bank's
            // still-ringing tail for that layer up to full level and left the hysteresis
            // latch set, so a high-threshold layer came back audible on the next segment.
            bool shouldBeAudible = slotExists && intensity >= threshold;
            stemAudible[slot] = shouldBeAudible;

            float target;
            if (banded)
            {
                // Level tracks intensity across the band, so the layer swells instead of
                // switching on. Hysteresis is irrelevant here: the response is already
                // continuous, so there is no threshold to chatter across.
                target = Mathf.Clamp01((intensity - minIntensity) / fadeRange);
            }
            else
            {
                target = shouldBeAudible ? 1f : 0f;
            }

            if (primeGates)
            {
                stemGate[slot] = target;
                continue;
            }

            // The fade times still bound how fast the gate may move, so a sudden
            // intensity jump inside a band glides rather than zippering.
            float fadeTime = !slotExists
                ? 1.5f
                : (target > stemGate[slot] ? stem.fadeInTime : stem.fadeOutTime);

            stemGate[slot] = Mathf.MoveTowards(
                stemGate[slot],
                target,
                unscaledDeltaTime / Mathf.Max(0.05f, fadeTime));
        }

        primeGates = false;
    }

    /// <summary>
    /// Equal-power law for fades. A linear gain ramp dips audibly in the middle of a
    /// crossfade (the halfway point sums well below full loudness); shaping both sides
    /// with sin(x*pi/2) keeps perceived level steady. This is what middleware does by
    /// default and costs the author nothing.
    /// </summary>
    private static float EqualPower(float linear01)
    {
        return Mathf.Sin(Mathf.Clamp01(linear01) * (Mathf.PI * 0.5f));
    }

    private void ApplyVolumes()
    {
        float fadeCurve = EqualPower(stateFade);

        // Solo/mute are debug-overlay aids for auditioning the mix; solo wins over mute
        // conflicts the usual way: any solo active silences every non-soloed stem.
        bool anySolo = false;
        for (int slot = 0; slot < stemGate.Length; slot++)
        {
            if (debugSolo[slot]) { anySolo = true; break; }
        }

        for (int bank = 0; bank < 2; bank++)
        {
            MusicSegment segment = bankSegment[bank];
            if (segment == null) continue;

            for (int slot = 0; slot < stemGate.Length; slot++)
            {
                AudioSource source = banks[bank, slot];
                if (source.clip == null) continue;

                bool silenced = debugMute[slot] || (anySolo && !debugSolo[slot]);
                if (silenced)
                {
                    source.volume = 0f;
                    continue;
                }

                float authored = (slot < segment.StemCount && segment.stems[slot] != null)
                    ? segment.stems[slot].volume
                    : 0f;

                source.volume = EqualPower(stemGate[slot]) * authored * fadeCurve;
            }
        }
    }

    // ---- Scheduling helpers --------------------------------------------------

    private void ScheduleSegmentOnBank(MusicSegment segment, int bank, double dspTime)
    {
        bankSegment[bank] = segment;
        bankScheduled[bank] = false;

        for (int slot = 0; slot < stemGate.Length; slot++)
        {
            AudioSource source = banks[bank, slot];
            bool hasClip = slot < segment.StemCount
                           && segment.stems[slot] != null
                           && segment.stems[slot].clip != null;

            if (hasClip)
            {
                source.Stop();                       // releases any tail still on this bank
                source.clip = segment.stems[slot].clip;
                source.volume = 0f;                  // ApplyVolumes sets the real level before it starts
                source.PlayScheduled(dspTime);
                bankScheduled[bank] = true;
            }
            else
            {
                source.Stop();
                source.clip = null;
            }
        }
    }

    private void StopBank(int bank)
    {
        for (int slot = 0; slot < stemGate.Length; slot++)
        {
            AudioSource source = banks[bank, slot];
            source.Stop();
            source.volume = 0f;
        }
        bankSegment[bank] = null;
        bankScheduled[bank] = false;
    }

    private void SetBankScheduledEnd(int bank, double dspTime)
    {
        if (!bankScheduled[bank]) return;

        for (int slot = 0; slot < stemGate.Length; slot++)
        {
            AudioSource source = banks[bank, slot];

            // isPlaying: a stem clip shorter than its musical length has already stopped
            // by now, and SetScheduledEndTime on a source with no active schedule logs a
            // Unity error per stem per transition.
            if (source.clip != null && source.isPlaying) source.SetScheduledEndTime(dspTime);
        }
    }

    private MusicSegment PickFirstLoop()
    {
        if (usableLoops.Count == 0) return null;

        if (definition.loopOrder == MusicManager.PlaybackMode.Random && usableLoops.Count > 1)
        {
            loopCursor = Random.Range(0, usableLoops.Count);
        }
        else
        {
            loopCursor = 0;
        }
        return usableLoops[loopCursor];
    }

    private MusicSegment PickNextLoop()
    {
        if (usableLoops.Count == 0) return null;

        // Coming out of the intro, hand over to the body using the configured order.
        if (currentIsIntro) return PickFirstLoop();

        if (definition.loopOrder == MusicManager.PlaybackMode.Sequential)
        {
            loopCursor = (loopCursor + 1) % usableLoops.Count;
        }
        else if (usableLoops.Count == 1)
        {
            loopCursor = 0;
        }
        else if (usableLoops.Count == 2)
        {
            // With two segments, avoiding a repeat is forced alternation; without it,
            // honour the flag and actually pick at random.
            loopCursor = definition.avoidRepeatInRandom
                ? 1 - Mathf.Max(0, loopCursor)
                : Random.Range(0, 2);
        }
        else
        {
            int next;
            do
            {
                next = Random.Range(0, usableLoops.Count);
            }
            while (definition.avoidRepeatInRandom && next == loopCursor);
            loopCursor = next;
        }

        return usableLoops[loopCursor];
    }

    // ---- Debug readouts ------------------------------------------------------

    public string GetSegmentName()
    {
        if (currentSegment == null) return "-";
        return currentIsIntro ? currentSegment.segmentName + " (intro)" : currentSegment.segmentName;
    }

    public string GetStemName(int slot)
    {
        MusicSegment config = currentSegment ?? bankSegment[currentBank];
        if (config != null && slot < config.StemCount && config.stems[slot] != null)
        {
            return config.stems[slot].stemName;
        }
        return $"Slot {slot}";
    }

    public float GetStemGate(int slot) => slot < stemGate.Length ? stemGate[slot] : 0f;

    public bool GetDebugMute(int slot) => slot < debugMute.Length && debugMute[slot];
    public bool GetDebugSolo(int slot) => slot < debugSolo.Length && debugSolo[slot];

    public void SetDebugMute(int slot, bool value)
    {
        if (slot < debugMute.Length) debugMute[slot] = value;
    }

    public void SetDebugSolo(int slot, bool value)
    {
        if (slot < debugSolo.Length) debugSolo[slot] = value;
    }

    public float GetStemMinIntensity(int slot)
    {
        MusicSegment config = currentSegment ?? bankSegment[currentBank];
        if (config != null && slot < config.StemCount && config.stems[slot] != null)
        {
            return config.stems[slot].minIntensity;
        }
        return 0f;
    }
}
