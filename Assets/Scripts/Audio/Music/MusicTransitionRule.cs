using UnityEngine;

/*
 * MusicTransitionRule.cs
 *
 * Purpose: Authoring data for ONE horizontal transition (state -> state).
 * Used by: MusicManager
 *
 * Rules live in an ordered, first-match-wins list on MusicManager. Wildcards let a
 * single rule cover "anything -> Fighting". When nothing matches, MusicManager falls
 * back to a built-in default (NextBar, its serialized crossFadeDuration, intro on,
 * no stinger, no outro).
 */

public enum TransitionSync
{
    [Tooltip("Switch as soon as possible")]
    Immediate,

    [Tooltip("Wait for the next beat boundary")]
    NextBeat,

    [Tooltip("Wait for the next bar line (musical default)")]
    NextBar,

    [Tooltip("Wait until the current segment finishes")]
    EndOfSegment
}

[System.Serializable]
public class MusicTransitionRule
{
    [Tooltip("Descriptive name for the inspector, e.g. 'Any -> Fighting'")]
    public string ruleName = "Transition";

    [Header("Match")]
    [Tooltip("Match a transition FROM any state")]
    public bool fromAny = true;

    [Tooltip("Source state, used when 'From Any' is off")]
    public MusicManager.MusicState from = MusicManager.MusicState.Exploring;

    [Tooltip("Match a transition TO any state")]
    public bool toAny = false;

    [Tooltip("Destination state, used when 'To Any' is off")]
    public MusicManager.MusicState to = MusicManager.MusicState.Fighting;

    [Header("Timing")]
    [Tooltip("When the switch is allowed to happen")]
    public TransitionSync sync = TransitionSync.NextBar;

    [Tooltip("Seconds to crossfade the outgoing and incoming music")]
    [Min(0f)] public float crossfadeDuration = 1.0f;

    [Header("Optional flourishes")]
    [Tooltip("One-shot played over the transition. Optional")]
    public AudioClip stinger;

    [Tooltip("Seconds of the stinger BEFORE its accent. The stinger is scheduled at " +
             "(sync point - lead in) so its accent lands exactly on the boundary")]
    [Min(0f)] public float stingerLeadIn = 0f;

    [Range(0f, 1f)] public float stingerVolume = 1f;

    [Header("Transition segments")]
    [Tooltip("Play the outgoing state's outro segment before handing over. " +
             "The incoming state then enters at the end of that outro")]
    public bool playOutroOfCurrent = false;

    [Tooltip("Enter the new state through its intro segment before its loops")]
    public bool playIntroOfNext = true;

    public bool Matches(MusicManager.MusicState fromState, MusicManager.MusicState toState)
    {
        if (!fromAny && from != fromState) return false;
        if (!toAny && to != toState) return false;
        return true;
    }

    public string Describe()
    {
        string f = fromAny ? "Any" : from.ToString();
        string t = toAny ? "Any" : to.ToString();
        return $"{f} -> {t} [{sync}]";
    }
}
