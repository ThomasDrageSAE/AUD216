using System.Collections.Generic;
using UnityEngine;

/*
 * MusicStateDefinition.cs
 *
 * Purpose: Authoring asset for one adaptive music state (the horizontal dimension).
 * Used by: MusicManager / LayeredStatePlayer
 *
 * A state owns a musical grid (bpm + beatsPerBar) and a set of segments:
 *   - an optional intro, played once on entry,
 *   - one or more loop segments, played continuously,
 *   - an optional outro, played once on the way out.
 *
 * Each segment holds stems: synchronized clips that start sample-locked and differ
 * only in volume. Stems are gated by intensity (the vertical dimension).
 *
 * Authoring convention: every segment in a state should declare the same stem layers
 * in the same order (Drums, Bass, Pads, Lead...). Slot index is treated as layer
 * identity, so the gate/fade state survives a segment change. ValidateContent warns
 * when segments disagree.
 *
 * Scheduling uses the MUSICAL length (lengthInBars), never clip length, so a clip may
 * carry a reverb tail past the loop point.
 */

[System.Serializable]
public class MusicStem
{
    [Tooltip("Layer name, for the inspector and the debug overlay (e.g. Drums, Bass, Pads, Lead)")]
    public string stemName = "Stem";

    public AudioClip clip;

    [Tooltip("This stem is audible once smoothed intensity reaches this value. 0 = always audible")]
    [Range(0f, 1f)] public float minIntensity = 0f;

    [Tooltip("0 = switch on at minIntensity (with the fade times below).\n" +
             "Above 0 = the stem's level tracks intensity proportionally across " +
             "[minIntensity, minIntensity + fadeRange], so it swells in rather than " +
             "switching on. The one-number equivalent of an RTPC curve")]
    [Range(0f, 1f)] public float fadeRange = 0f;

    [Tooltip("Authored level for this stem")]
    [Range(0f, 1f)] public float volume = 1f;

    [Tooltip("Seconds to fade in when intensity rises past minIntensity")]
    [Min(0.05f)] public float fadeInTime = 1.5f;

    [Tooltip("Seconds to fade out when intensity falls back below minIntensity")]
    [Min(0.05f)] public float fadeOutTime = 2.5f;
}

[System.Serializable]
public class MusicSegment
{
    [Tooltip("Descriptive name for the inspector and debug overlay")]
    public string segmentName = "Segment";

    [Tooltip("Musical length in bars. Scheduling uses this, NOT clip length, so clips may ring past the loop point")]
    [Min(1)] public int lengthInBars = 8;

    [Tooltip("Synchronized layers. All stems start sample-locked; only their volumes differ")]
    public MusicStem[] stems;

    /// <summary>True when this segment has at least one usable stem.</summary>
    public bool HasContent
    {
        get
        {
            if (stems == null) return false;
            for (int i = 0; i < stems.Length; i++)
            {
                if (stems[i] != null && stems[i].clip != null) return true;
            }
            return false;
        }
    }

    public int StemCount => stems != null ? stems.Length : 0;
}

[CreateAssetMenu(fileName = "MusicState", menuName = "Game/Music/Music State")]
public class MusicStateDefinition : ScriptableObject
{
    [Tooltip("Which MusicManager state this asset provides content for")]
    public MusicManager.MusicState state = MusicManager.MusicState.Exploring;

    [Header("Musical timing (applies to every segment in this state)")]
    [Tooltip("Beats per minute. Drives every scheduling decision for this state")]
    [Min(20f)] public float bpm = 120f;

    [Tooltip("Beats per bar. 4 = common time")]
    [Min(1)] public int beatsPerBar = 4;

    [Header("Segments")]
    [Tooltip("Optional. Played once on entry when the transition rule enables intros")]
    public MusicSegment introSegment;

    [Tooltip("The main body of the state. At least one segment is required")]
    public MusicSegment[] loopSegments;

    [Tooltip("How the loop segments are ordered")]
    public MusicManager.PlaybackMode loopOrder = MusicManager.PlaybackMode.Sequential;

    [Tooltip("When using Random order, avoid repeating the same segment twice in a row")]
    public bool avoidRepeatInRandom = true;

    [Tooltip("Optional. Played once on the way OUT when the transition rule enables outros")]
    public MusicSegment outroSegment;

    // ---- Musical maths -------------------------------------------------------

    public double BeatDuration => 60.0 / Mathf.Max(20f, bpm);
    public double BarDuration => BeatDuration * Mathf.Max(1, beatsPerBar);

    /// <summary>Musical duration of a segment in seconds. Independent of clip length.</summary>
    public double GetSegmentDuration(MusicSegment segment)
    {
        if (segment == null) return 0.0;
        return Mathf.Max(1, segment.lengthInBars) * BarDuration;
    }

    public bool HasIntro => introSegment != null && introSegment.HasContent;
    public bool HasOutro => outroSegment != null && outroSegment.HasContent;

    public bool HasLoops
    {
        get
        {
            if (loopSegments == null) return false;
            for (int i = 0; i < loopSegments.Length; i++)
            {
                if (loopSegments[i] != null && loopSegments[i].HasContent) return true;
            }
            return false;
        }
    }

    /// <summary>Largest stem count across all segments — how many voice slots to allocate.</summary>
    public int MaxStemCount
    {
        get
        {
            int max = 0;
            foreach (MusicSegment segment in EnumerateSegments())
            {
                if (segment.StemCount > max) max = segment.StemCount;
            }
            return max;
        }
    }

    public IEnumerable<MusicSegment> EnumerateSegments()
    {
        if (HasIntro) yield return introSegment;
        if (loopSegments != null)
        {
            for (int i = 0; i < loopSegments.Length; i++)
            {
                if (loopSegments[i] != null && loopSegments[i].HasContent) yield return loopSegments[i];
            }
        }
        if (HasOutro) yield return outroSegment;
    }

    // ---- Validation ----------------------------------------------------------

    /// <summary>
    /// Reports authoring problems as warnings. Never throws: bad content should degrade,
    /// not crash a play session.
    /// </summary>
    public void ValidateContent(System.Action<string> report)
    {
        if (report == null) return;

        if (!HasLoops)
        {
            report($"'{name}' ({state}) has no usable loop segments; the state cannot play.");
            return;
        }

        int canonicalStemCount = -1;
        string canonicalSegmentName = null;

        foreach (MusicSegment segment in EnumerateSegments())
        {
            double musicalLength = GetSegmentDuration(segment);

            if (canonicalStemCount < 0)
            {
                canonicalStemCount = segment.StemCount;
                canonicalSegmentName = segment.segmentName;
            }
            else if (segment.StemCount != canonicalStemCount)
            {
                report($"'{name}' segment '{segment.segmentName}' has {segment.StemCount} stems but " +
                       $"'{canonicalSegmentName}' has {canonicalStemCount}. Stem slot index is treated as " +
                       "layer identity, so segments should declare the same layers in the same order.");
            }

            float firstClipLength = -1f;
            bool anyAudibleAtZero = false;

            for (int i = 0; i < segment.StemCount; i++)
            {
                MusicStem stem = segment.stems[i];
                if (stem == null || stem.clip == null)
                {
                    report($"'{name}' segment '{segment.segmentName}' stem {i} has no clip and will be skipped.");
                    continue;
                }

                if (stem.minIntensity <= 0f) anyAudibleAtZero = true;

                if (stem.fadeRange > 0f && stem.minIntensity + stem.fadeRange > 1f)
                {
                    report($"'{name}' segment '{segment.segmentName}' stem '{stem.stemName}' has a fade " +
                           $"band ending at {stem.minIntensity + stem.fadeRange:F2}, above the maximum " +
                           "intensity of 1. The top of the band can never be reached, so the layer will " +
                           "never reach full level.");
                }

                float clipLength = stem.clip.length;
                if (firstClipLength < 0f) firstClipLength = clipLength;
                else if (Mathf.Abs(clipLength - firstClipLength) > 0.01f)
                {
                    report($"'{name}' segment '{segment.segmentName}' stem '{stem.stemName}' is {clipLength:F3}s " +
                           $"but the first stem is {firstClipLength:F3}s. Stems must be the same length to stay locked.");
                }

                if (clipLength < musicalLength - 0.01f)
                {
                    report($"'{name}' segment '{segment.segmentName}' stem '{stem.stemName}' is {clipLength:F3}s, " +
                           $"shorter than its musical length of {musicalLength:F3}s ({segment.lengthInBars} bars at " +
                           $"{bpm} BPM). There will be an audible gap.");
                }
                else if (clipLength > musicalLength + 2.0f)
                {
                    report($"'{name}' segment '{segment.segmentName}' stem '{stem.stemName}' is {clipLength:F3}s, " +
                           $"more than 2s longer than its musical length of {musicalLength:F3}s. " +
                           "Check lengthInBars/BPM (a long reverb tail is fine).");
                }
            }

            if (!anyAudibleAtZero && segment.StemCount > 0)
            {
                report($"'{name}' segment '{segment.segmentName}' has no stem with minIntensity 0, " +
                       "so it is silent at zero intensity. Intended?");
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (loopSegments == null || loopSegments.Length == 0) return;
        ValidateContent(message => Debug.LogWarning($"[MusicStateDefinition] {message}", this));
    }
#endif
}
