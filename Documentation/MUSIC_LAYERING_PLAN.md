# Music Layering Plan — Vertical + Horizontal Layering for MusicManager

> Feature plan, 2026-08-18. This document is a **work order for an implementing AI**
> (or human). It specifies an adaptive music system — vertical layering (synchronized
> stems driven by intensity) and horizontal re-sequencing (musically-timed state
> transitions) — built **in plain Unity, no audio middleware**, as an in-place
> extension of the existing `MusicManager`.
>
> Companion docs: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) ·
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md) (read its **ground rules** first —
> they all apply) · run `bash Documentation/tools/compile-check.sh` after every edit.

## STATUS (2026-08-18): implemented, M1–M6

All six phases are committed (`3bd46fa`, `8071666`, `281b4c5`, `a3861d3`, `6df09ff`,
and the M6 commit). Both assemblies compile after every phase.

**Two things must happen in the Unity editor before you can hear any of it:**

1. Run **`Tools/Music/Generate Test Stems`**. The generator is an editor script, so it
   could not be run from the CLI — until it runs, `Assets/Audio/TestStems/` and the
   three `MusicStateDefinition` assets do not exist.
   *(It forces the clips to PCM / Decompress On Load. Do not let them re-import as
   Vorbis: the encoder delay and transient pre-echo smear exactly the click transients
   and stem alignment the content exists to prove.)*
2. On the `MusicManager` component, assign the three generated assets to
   **State Definitions**, and set **Default Music Mixer Group** to the MUSIC group
   (otherwise stems fall back to that state's legacy track group, which is correct but
   worth checking). Optionally author **Transition Rules** — with none, every change
   uses the built-in `NextBar` default.

Until step 2 is done the system is dormant by design: every state has no asset, so all
three keep playing their legacy single-clip tracks exactly as before.

**Deviations from the plan as written:** one in behaviour — each stem gained an
optional `fadeRange` (Roadmap Task 5): with `fadeRange > 0` the stem's volume scales
*proportionally* across `[minIntensity, minIntensity + fadeRange]` instead of
switching at the threshold, and the hysteresis in §3.2 is bypassed for that stem
(a proportional target cannot flap). `fadeRange = 0` (the default) keeps the
threshold behaviour documented below. Details in §6.5. Otherwise none: M2 was
committed with the transition pipeline present but resolving to `Immediate` sync,
and M3 added rule resolution on top, rather than M2 shipping a throwaway simpler
path.

**Not verifiable without you:** every acceptance check in §6 is an *audible* one
(gapless loops, bar-aligned switches, threshold fades). They have not been run.

**The debug overlay key is `Ctrl+M` (show/hide), alias F8.** The intensity-floor
(`Ctrl+,` / `Ctrl+.`), cycle-state (`Ctrl+N`) and clear (`Ctrl+K`) keys were **removed at
the owner's request on 2026-09-01** — the overlay is now read-only apart from its
mouse-driven per-stem solo/mute buttons. Intensity can still be driven from script via
`MusicManager.SetIntensityOverride` / `ClearIntensityOverride` (unchanged public API), and
the overlay shows an override whenever one is set. F8 remains an alias because macOS
claims the F-keys for media transport and volume.

## Audit (2026-08-18): reviewed and hardened

The implementation was then audited — by me and by three independent adversarial
reviews — and the findings fixed across four commits. The bugs worth knowing about,
because they shape how the system behaves:

| Area | What was wrong |
|---|---|
| Re-entry | Requesting the state already playing restarted it from its intro, because `ScheduleEntry` begins by stopping everything. This fired on the *most common* combat pattern: a skirmish ending before the scheduled bar, where `SetFightingState(false)` asks for the state we never left. |
| Fade ownership | The legacy and layered fade coroutines both wrote the same AudioSource volumes with no mutual exclusion, and `CrossFadeToState` assigned `currentState` seconds after it started — so a superseded fade could stop a track a newer one had just raised, and overwrite the state a newer transition had established. |
| Stale transitions | A pending layered transition survived a legacy→legacy change and fired later against a state that had moved on. |
| Deferred state | `cannotOverride` and `previousNonFightingState` read `currentState`, which lags the target for the whole scheduled window — letting a forbidden override through and mis-remembering the state to restore after combat. Both now use `EffectiveState`. |
| Outro | Re-entering during an outro cut it dead; an outro could be scheduled on a state whose entry had not started; a cut point past the segment end cancelled the queued loop and left a bar of silence. |
| Stems | A slot absent from the current segment was treated as "always on"; gates restarted from zero on entry, so an `Immediate` transition swelled in over 1.5s. |
| Test content | The stinger hard-clipped in all three states, and the pad layer faded to silence **before** every loop point — a 1.4s hole that would read as a broken scheduler when the scheduler was fine. |

Two design notes that came out of it and are worth respecting:

- **Keep an (empty) legacy `MusicTrack` entry for every state named in a `cannotOverride`
  list.** That block-list lives only on the legacy tracks and the check needs an entry for
  both states involved, so deleting a migrated state's track silently disables the rule.
  The manager now warns about this at startup.
- **`AudioSettings.dspTime` and unscaled time are load-bearing**, and music sources are set
  to `priority = 0` because Unity virtualises the *quietest* voices first — which would
  otherwise be precisely the stems mid-fade.

## Audit 2 (2026-08-20): state logic, takeover cancel, device change

A second full read of `MusicManager`, `LayeredStatePlayer`, `MusicConductor`,
`MusicTransitionRule`, `MusicStateDefinition`, `MusicIntensityConfig` and the three
gameplay callers, traced against the scene data (both the Exploring and Fighting legacy
tracks author `cannotOverride: [Inside]`; five `TriggerManager` zones). The layered
pipeline itself held up - the rapid A→B→A, outro-then-cancel and supersede-by-a-third-state
paths all behave as their comments claim, and nothing in the intensity driver is wrong.
The defects are at the edges: who is allowed to decide the state, what happens to a
player that is audible while "pending", and what happens when the audio clock resets.

| # | Area | What is wrong | Status |
|---|---|---|---|
| 1 | State authority | `TriggerManager` calls raw `ChangeState(...)` right after `SetIndoorState(...)`; it ignores `isFighting`, so crossing any of the four outdoor ambience zones mid-fight drops Fighting → Exploring for the rest of the fight. The call exists because Exploring's `cannotOverride: [Inside]` means `UpdateMusicState` can never leave Inside on its own. | Fixed `647fe79` (2026-08-26) — Task A as specified; the trigger's raw `ChangeState` call is gone |
| 2 | State memory | `SetIndoorState` updates `previousNonFightingState` only when *not* fighting, so a fight that starts in the cave and ends outdoors restores **Inside** music outdoors. Root cause shared with #1: a remembered state beside a derived one. | Fixed `647fe79` (2026-08-26) — field deleted; state derived from the flags; `cannotOverride` blocks only while the blocker's condition holds |
| 3 | Takeover cancel | `CancelPendingTransition` and `BeginTransition`'s supersede call `StopEverything()` on the pending player, assuming it is silent. After a graceful takeover it is audible at a held fade level - spawn / kill-within-the-crossfade / spawn cuts the under-layer dead. | Fixed `06f138e` (2026-08-26) — `LayeredStatePlayer.AbandonEntry` fades an audible pending player, stops a silent one |
| 4 | Audio device change | No `AudioSettings.OnAudioConfigurationChanged` handler. A device change stops every source and restarts `dspTime`; the layered player waits forever on a `pendingStartDsp` that never comes and the legacy loop never reschedules a stopped source. Silence until the next state change. | Fixed `2e9908a` (2026-08-26) — `OnAudioConfigurationChanged` rebuilds the effective state with an immediate entry on the new clock |
| 5 | Legacy path | `ScheduleNextClip` assigns the next clip and `PlayScheduled`s on the **same** source still playing the current clip's last 0.1 s - a ~0.1 s hole at every clip change. Audible today (Exploring cycles five clips). | Open - not in the work order |
| 6 | Legacy path | The immediate branch of `ChangeState` starts *all* legacy tracks silently at startup; `CrossFadeToState` only restarts a track that is not playing, so the first Fighting entry fades in mid-clip at a random position. Silent tracks also burn voices and run the scheduling loop every frame. | Open |
| 7 | Entry timing | A zero-crossfade entry is scheduled with `stateFade = 0` and raised to 1 on the first `Update` *after* `entryDsp`, so the downbeat plays silent for up to a frame (worst at startup and on `Immediate` combat hits). Fix: `SetFadeImmediate(1)` at scheduling time when `crossfade ≤ 0.001` and not a takeover. | Fixed 2026-08-25 — as prescribed, in `BeginTransition`. The finding was promoted from cosmetic to clearly audible by the finding-8 startup lead: the 1 s entry now lands *inside* the scene-load stall, so the intro's head played silently under it (heard by the owner). The pre-entry `Tick` now applies full volumes to the scheduled sources before the sync point |
| 8 | Entry timing | The first scheduled entry can miss its `PlayScheduled` slot at scene load — the stem clips are still loading, so the audio starts **late, from the top of the clip**, while `currentStartDsp`/`currentEndDsp` keep the scheduled times. The first loop boundary then lands early by exactly the lateness; every later loop is exact. Found by the owner by ear while authoring real stems (2026-08-24). | Fixed 2026-08-24 — three layers: stems `LoadAudioData()` at player init; the session's **first** layered entry is scheduled 1 s out instead of the bare margin (owner-requested — the slot then cannot be missed, and `VerifyEntryStart`'s ±50 ms tolerance stops mattering); `VerifyEntryStart` re-anchors the segment window to the measured playhead if a start is missed anyway (the conductor follows via its per-frame `Rebase`) |
| 9 | Authored content | Every clip in `Assets/Audio/Music Loops/` is exactly 38.4 s = **16 bars at 100 BPM**, but the on-disk `MusicState_Exploring` segments say `lengthInBars: 8` (19.2 s). The two-bank player only reclaims the old bank ~1 s before the *following* boundary — a 19.2 s "tail" is not a tail, so the second half of each Exploring clip plays over the next segment for ~18 s, near-continuously in Random order. ⚠ Disk may be stale vs the editor (owner rarely saves — see the session memory / workflow note): if the in-editor value already says 16, this is only an unsaved project. | Fixed 2026-08-26 — the five loop segments set to 16 on the asset (clip durations verified from the WAV headers: all 38.400 s = exactly 16 bars at 100 BPM). Git was clean at the time, so the disk was current, not stale |
| 10 | Authored content | The Inside state is silent both ways: the on-disk `MusicState_Inside` asset has no stems in any segment, and the legacy Inside track's clip guid `f38f924f1762e4deda5f74fedc35c957` has **no matching asset in the repo and never has had one** (`git log -S` over all history finds nothing; the same dangling guid sits in `Chest.prefab` and `Random Container AudioSource.prefab`). Cave music has been silence since the baseline — masked until 2026-08-25 by the MUSIC bus sitting at −80 dB. Same stale-disk caveat: the in-editor asset may be authored but unsaved. | Open — owner: author (or save) the Inside stems, or assign a real clip to the legacy track; the chest/container prefab slots want a look too (found 2026-08-26) |
| - | Smaller | Stingers get no mixer group when `defaultMusicMixerGroup` is empty (stems fall back to the legacy track's group; stingers fall back to Master). An outro handover is followed by a full fade-in of the incoming state, undoing the clean entry an outro is for. A takeover replays the intro of a state that never left. | Open - authoring notes until fixed |

Design consequence worth keeping: **music state is a pure function of `isFighting` and
`isIndoors`.** `cannotOverride` is a priority between two simultaneously-true conditions,
never a one-way door, and gameplay never calls `ChangeState` directly (it stays public
only for `Start` and the debug overlay).

## Decisions already made (do not re-litigate)

These were settled with the project owner:

1. **Intensity driver:** computed from gameplay (zombie count, low player health,
   night approaching) **plus** an explicit override API for triggers/scripted moments.
2. **Transition sync:** authored **per transition** — `Immediate`, `NextBeat`,
   `NextBar`, or `EndOfSegment`; default `NextBar`.
3. **Authoring:** ScriptableObject assets per music state.
4. **Integration:** **extend `MusicManager` in place.** Same component, same
   GameObject, same scene wiring, same public API. No parallel manager, no rename.
5. **Vertical model:** **threshold layers** — each stem has a `minIntensity` plus
   fade-in/out times; rising intensity adds stems, falling removes them. (Since
   Task 5 a stem may also set `fadeRange > 0` for proportional volume across a band
   instead of a hard threshold — see §6.5.)
6. **Transition features:** synced crossfade, **stingers**, and **transition
   segments** (outro of old state / intro of new state). Exit markers are out of scope.
7. **Verification:** an editor tool **generates synthetic test stems** (distinct
   pitch per layer, audible bar accents) plus a **debug overlay**; the owner replaces
   them with real stems later. The system must be verifiable end-to-end without real
   music existing.
8. **State identity:** keep the existing `MusicState` enum (`Exploring`, `Fighting`,
   `Inside`). Each enum value maps to one content asset.

## Glossary (name things exactly like this)

| Term | Meaning |
|---|---|
| **State** | The horizontal dimension: *which* music plays (`MusicState` enum). |
| **Segment** | A musical section inside a state: intro, one or more loop sections, outro. The unit of horizontal scheduling. |
| **Stem** (layer) | One synchronized audio file inside a segment (drums, bass, pads…). The unit of vertical mixing. All stems of a segment start sample-locked and differ only in volume. |
| **Intensity** | A global 0–1 value; each stem is audible when smoothed intensity ≥ its `minIntensity` (or scales across `minIntensity…minIntensity+fadeRange` when a fade band is set — §6.5). |
| **Conductor** | The DSP-clock musical timeline: BPM, beats/bar, bar counting, "when is the next beat/bar/segment-end" queries. |
| **Sync point** | The DSP time at which a horizontal transition executes. |
| **Stinger** | A one-shot flourish played over a transition, aligned so its accent lands on the sync point. |

---

# 1. Current system anatomy and the compatibility contract

`Assets/Scripts/Audio/MusicManager.cs` (~350 lines at planning time; ~915 after
implementation) had: three states, one
`MusicTrack` per state (an array of whole-song clips), gapless clip chaining via
`PlayScheduled` polled from `Update()`, and a wall-clock crossfade between states.
One `AudioSource` per track, created in `Awake`.

**The following external surface MUST keep working unchanged** (verified callers):

| Caller | Call |
|---|---|
| `ZombieManager.UpdateMusicState` | `musicManager.SetFightingState(bool)` |
| `TriggerManager` (enter/exit handlers) | `SetIndoorState(bool)` and `ChangeState(MusicManager.MusicState, …)` — `ChangeState` must stay **public** |
| `LocationTrigger` (enter/exit) | `SetIndoorState(bool)` |
| `StatsHelper` (music section) | **Reflection** on the private field literally named `musicTracks` of type `MusicManager.MusicTrack[]` — do not rename, retype, or delete that field, `MusicTrack`, `MusicState`, or `PlaybackMode` |

(Line numbers removed — they drift; locate callers by method name.)

Scene data on the component (main scene, GameObject 2049660006): Exploring and
Fighting each have 5 clips routed to the MUSIC mixer group
(guid `855e36c5…`, fileID `8426258057616649091`); **Inside has 1 clip and NO mixer
group** (it currently bypasses the MUSIC bus — fix noted in §7 migration);
`cannotOverride: [Inside]` on Exploring and Fighting; `crossFadeDuration: 2`;
`startingState: Exploring`.

**Fighting state restore logic** (`previousNonFightingState`), the
`cannotOverride` check in `UpdateMusicState()`, and the indoor/fighting flags are
behaviourally correct — preserve them exactly; the new system changes *what plays*,
not *which state wins*.

**Legacy fallback is a requirement, not an option:** a state that has no
`MusicStateDefinition` asset assigned must keep playing through the existing
`MusicTrack` path, untouched. This is what makes extend-in-place safe: the owner
migrates one state at a time as stems get authored, and the scene keeps working at
every point in between.

---

# 2. Data model

New folder `Assets/Scripts/Audio/Music/` for all new types (they are still part of
the same assembly; the folder is organisational). Content assets live in
`Assets/Audio/Music States/` (create it).

## 2.1 `MusicStateDefinition` (ScriptableObject)

```csharp
[CreateAssetMenu(fileName = "MusicState", menuName = "Game/Music/Music State")]
public class MusicStateDefinition : ScriptableObject
{
    [Tooltip("Which MusicManager state this asset provides content for")]
    public MusicManager.MusicState state;

    [Header("Musical timing — applies to every segment in this state")]
    [Min(20f)] public float bpm = 120f;
    [Min(1)]  public int beatsPerBar = 4;

    [Header("Segments")]
    [Tooltip("Optional. Played once when this state is entered via a transition whose rule enables intros")]
    public MusicSegment introSegment;              // optional: null/empty = none
    [Tooltip("The main body. At least one required")]
    public MusicSegment[] loopSegments;
    public MusicManager.PlaybackMode loopOrder = MusicManager.PlaybackMode.Sequential;
    [Tooltip("Optional. Played once on the way OUT of this state when the transition rule enables outros")]
    public MusicSegment outroSegment;              // optional
}
```

## 2.2 `MusicSegment` and `MusicStem` (`[System.Serializable]` classes, same file as the SO or a sibling file)

```csharp
[System.Serializable]
public class MusicSegment
{
    public string segmentName;
    [Tooltip("Musical length. Scheduling uses bars*beatsPerBar*(60/bpm), NOT clip length, so clips may carry a reverb tail past the loop point")]
    [Min(1)] public int lengthInBars = 8;
    public MusicStem[] stems;
}

[System.Serializable]
public class MusicStem
{
    public string stemName;                         // "Drums", "Bass", "Pads", "Lead"
    public AudioClip clip;
    [Tooltip("This stem is audible when smoothed intensity >= this value. 0 = always on")]
    [Range(0f, 1f)] public float minIntensity = 0f;
    [Tooltip("0 = hard threshold. > 0 = volume scales proportionally from minIntensity to minIntensity + fadeRange")]
    [Range(0f, 1f)] public float fadeRange = 0f;    // added in Roadmap Task 5 - see section 6.5
    [Range(0f, 1f)] public float volume = 1f;
    [Min(0.05f)] public float fadeInTime  = 1.5f;
    [Min(0.05f)] public float fadeOutTime = 2.5f;
}
```

A segment's **musical length in seconds** = `lengthInBars * beatsPerBar * 60 / bpm`.
Validation (§5.1) warns when a clip is *shorter* than the musical length (gap) and
allows clips up to ~2s *longer* (tail). Empty-clip stems are skipped with a warning.

## 2.3 `MusicTransitionRule` (`[System.Serializable]`, authored on the manager)

A flat, ordered, first-match-wins list on `MusicManager` (scene-level wiring stays on
the component per the hybrid nature of extend-in-place):

```csharp
public enum TransitionSync { Immediate, NextBeat, NextBar, EndOfSegment }

[System.Serializable]
public class MusicTransitionRule
{
    [Tooltip("Match any source state")] public bool fromAny = true;
    public MusicManager.MusicState from;             // used when fromAny == false
    [Tooltip("Match any destination state")] public bool toAny = false;
    public MusicManager.MusicState to;               // used when toAny == false

    public TransitionSync sync = TransitionSync.NextBar;
    [Min(0f)] public float crossfadeDuration = 1.0f;

    [Header("Optional flourishes")]
    public AudioClip stinger;
    [Tooltip("Seconds of the stinger BEFORE its accent. The stinger is scheduled at syncPoint - leadIn so the accent lands on the boundary")]
    [Min(0f)] public float stingerLeadIn = 0f;
    [Range(0f, 1f)] public float stingerVolume = 1f;

    [Tooltip("Play the outgoing state's outroSegment before the switch (sync point then targets the outro's end)")]
    public bool playOutroOfCurrent = false;
    [Tooltip("Enter the new state via its introSegment before its loops")]
    public bool playIntroOfNext = true;
}
```

Resolution: `FindRule(from, to)` scans the array in order, returns the first rule
whose from/to match (wildcards allowed); if none matches, use a built-in default
(`NextBar`, crossfade = existing `crossFadeDuration` field, intro enabled, no
stinger, no outro).

---

# 3. Runtime architecture (extend in place)

`MusicManager` remains **the only MonoBehaviour and the only scene object**. New
logic goes into plain C# helper classes in separate files under
`Assets/Scripts/Audio/Music/`, owned and driven by `MusicManager` — this honours
extend-in-place (zero scene rewiring, one entry point) without producing a
1,500-line file.

```
MusicManager (MonoBehaviour, existing — the facade)
 ├─ legacy path: musicTracks + existing methods, UNCHANGED, used for any state
 │    with no MusicStateDefinition assigned
 ├─ new serialized fields:
 │    MusicStateDefinition[] stateDefinitions;      // 0..3 assets
 │    MusicTransitionRule[]  transitionRules;
 │    AudioMixerGroup        defaultMusicMixerGroup; // fallback routing for stems+stingers
 │    MusicIntensityConfig   intensityConfig;        // §4.2, [Serializable] block
 ├─ MusicConductor   conductor;      // plain class: DSP musical clock
 ├─ LayeredStatePlayer[] players;    // plain class: one per state-with-asset
 └─ StingerPlayer    stingerPlayer;  // one dedicated AudioSource
```

## 3.1 `MusicConductor` (plain class)

Owns the musical grid of the **currently playing** layered state:

```csharp
public class MusicConductor
{
    public void   Begin(double dspStartTime, float bpm, int beatsPerBar);
    public double BeatDuration { get; }        // 60/bpm
    public double BarDuration  { get; }        // BeatDuration * beatsPerBar
    public double NextBeat(double afterDsp);   // earliest beat boundary >= afterDsp + margin
    public double NextBar(double afterDsp);
    public int    CurrentBar(double dsp);      // for the debug overlay
    public int    CurrentBeatInBar(double dsp);
}
```

All boundary maths is pure arithmetic on `AudioSettings.dspTime` from the anchor —
never poll `AudioSource.time` for musical decisions. Use a **scheduling margin** of
0.05s: a boundary closer than that is skipped for the next one (PlayScheduled needs
runway). Each state runs on **its own grid**: at a transition, the sync point is
computed on the *outgoing* state's grid, and the incoming state's conductor anchor
is set to exactly that sync point (states may have different BPMs).

## 3.2 `LayeredStatePlayer` (plain class) — one per state that has an asset

Responsibilities: create and own the AudioSources for its stems, schedule segments
gaplessly, apply vertical mixing.

- **Source pool:** per stem slot, **two** AudioSources (A/B, double-buffered).
  Segment *N* plays on bank A; segment *N+1* is `PlayScheduled` on bank B at the
  musical end of *N*; bank A is left running so a baked reverb tail can ring past
  the loop point, then stopped one segment later. All sources: `loop = false`,
  `playOnAwake = false`, `spatialBlend = 0`, routed to the state's legacy track
  `outputMixerGroup` if assigned, else `defaultMusicMixerGroup`. Create the largest
  stem count × 2 sources once at init (`AddComponent<AudioSource>` on the manager's
  GameObject, same as the legacy path does).
- **Sample-locked start:** every stem of a segment is started with the **same**
  `PlayScheduled(dspTime)` value. After that only volumes change.
- **Segment chaining:** a `Tick()` called from `MusicManager.Update()` checks: if
  `nextSegmentScheduled == false` and `dspTime >= currentSegmentEnd - lookahead`
  (lookahead = 1.0s), pick the next loop segment (Sequential/Random reusing the
  legacy `avoidRepeat` logic) and schedule it. This replaces clip-end polling with
  DSP arithmetic — the current `Update()` loop stays for legacy tracks only.
- **Vertical mixing:** each frame, per stem:
  `targetVol = (smoothedIntensity >= stem.minIntensity - hysteresis(stem)) ? stem.volume * stateFade : 0`
  approached at a rate derived from `fadeInTime`/`fadeOutTime`
  (`vol += dt / fadeTime` toward target, clamped — linear fades are fine).
  **Hysteresis:** a stem that is currently audible only drops out when intensity
  falls below `minIntensity - 0.05`, so hovering at a threshold cannot flap.
  (Threshold stems only. A stem with `fadeRange > 0` — §6.5 — takes a proportional
  target `clamp01((intensity − minIntensity) / fadeRange)` instead and skips
  hysteresis entirely; a continuous target cannot flap.)
  `stateFade` is the horizontal crossfade multiplier (0→1 on entry, 1→0 on exit)
  applied on top of the per-stem result — one multiplier per player, not per stem.
- **Fades use `Time.unscaledDeltaTime`.** The pause menu sets `timeScale = 0` while
  DSP time keeps running; scaled-time fades would freeze mid-crossfade. (The legacy
  crossfade has this bug; do not copy it.)
- Public: `ScheduleEntry(double dspTime, bool viaIntro)`,
  `ScheduleExitOutro(double dspTime)` (returns outro end time),
  `BeginFadeOut(float duration)` / `BeginFadeIn(float duration)`,
  `StopAllAtDsp(double dspTime)` (for cancelling a scheduled-but-not-started entry:
  `AudioSource.Stop()` on a scheduled source cancels it), `bool IsAudible`.

## 3.3 Horizontal transition sequencing (in `MusicManager`)

Keep the existing *decision* pipeline (`SetFightingState` / `SetIndoorState` /
`UpdateMusicState` / `cannotOverride` / `previousNonFightingState`) exactly as-is.
Where the code currently reaches `ChangeState(newState)`, the layered path instead
calls `RequestStateChange(newState)`:

```
RequestStateChange(target):
  if target == currentState and no pending transition: return
  rule = FindRule(currentState, target)
  if a pending transition exists and its scheduled entry has NOT started yet:
      cancel it (Stop scheduled sources of the incoming player; clear pending)
  if pending exists and HAS started: treat currentState = that state and re-request
  syncPoint = rule.sync switch:
      Immediate    -> dspNow + margin
      NextBeat     -> conductor.NextBeat(dspNow)
      NextBar      -> conductor.NextBar(dspNow)
      EndOfSegment -> current player's currentSegmentEnd
  if rule.playOutroOfCurrent and outgoing has an outro:
      outroEnd = outgoingPlayer.ScheduleExitOutro(syncPoint)   // outro starts at syncPoint
      entryPoint = outroEnd
  else entryPoint = syncPoint
  incomingPlayer.ScheduleEntry(entryPoint, viaIntro: rule.playIntroOfNext)
  at entryPoint (checked from Update): outgoing.BeginFadeOut(rule.crossfadeDuration),
      incoming.BeginFadeIn(rule.crossfadeDuration), conductor.Begin(entryPoint, newState)
  if rule.stinger: stingerPlayer.PlayScheduled(entryPoint - rule.stingerLeadIn)
      (if that time is already past, play immediately)
  currentState = target when the entry actually starts (keep a pendingState until then)
```

Edge cases that MUST be handled:

- **Superseded requests** (enter combat, kill everything before the bar arrives):
  newest request wins; a not-yet-started scheduled entry is cancelled with
  `Stop()`; if the target equals the currently-playing state again, just clear the
  pending transition.
- **Mixed legacy/layered:** transitions between a legacy state and a layered state
  use the legacy crossfade path (`Immediate` sync, `crossFadeDuration`); a legacy
  state has no grid, so when *leaving* legacy the sync is always `Immediate`.
  Layered→layered uses the full pipeline.
- **Intro/outro are non-loop segments:** after an intro finishes, the player chains
  into `loopSegments[0]` (Sequential) or a random one, scheduled off the intro's
  musical end. An outro chains into silence (player fades/stops at outro end).
- **`EndOfSegment` + `playOutroOfCurrent`** both set: the outro starts at the
  current segment's end, then the new state enters at the outro's end.
- **Startup:** `Start()` keeps calling `ChangeState(startingState, true)`; for a
  layered starting state this means `ScheduleEntry(dspNow + 0.2, viaIntro: true)`
  with no fade.

# 4. Intensity

## 4.1 Sources and combination

```csharp
[System.Serializable]
public class MusicIntensityConfig
{
    [Header("Combat (dominant driver)")]
    public int zombiesForFullIntensity = 5;     // activeZombies / this, clamped 0..1
    [Header("Low health")]
    public float lowHealthThreshold = 30f;      // below this, contributes…
    [Range(0f,1f)] public float lowHealthContribution = 0.4f;
    [Header("Night approach (via DayNightCycle)")]
    [Range(0f,1f)] public float duskStartContribution = 0.25f; // ramps 0→this from duskStart→nightTime
    [Header("Response")]
    public float attackSeconds = 1.0f;          // smoothing when intensity rises
    public float releaseSeconds = 6.0f;         // smoothing when it falls
}
```

- `rawIntensity = max(combat, lowHealth, nightRamp, scriptedOverride)` — **max, not
  sum**: the loudest reason wins, values stay in 0–1, and layers never over-trigger.
- Combat needs a count: `ZombieManager.GetActiveZombieCount()` (shipped — it prunes
  destroyed entries before counting). `MusicManager` finds `ZombieManager`,
  `PlayerStats` and `DayNightCycle` with `FindFirstObjectByType` in `Start()` and
  tolerates any of them being null (that source contributes 0).
- `smoothedIntensity` moves toward `rawIntensity` at `1/attackSeconds` up,
  `1/releaseSeconds` down, using **unscaled** delta time. Stems consume only the
  smoothed value.
- Poll gameplay sources at 4 Hz (0.25s interval), not every frame.

## 4.2 Override API (new public surface)

```csharp
public void SetIntensityOverride(float value01);   // scripted floor; -1-free API: use Clear to remove
public void ClearIntensityOverride();
public float GetCurrentIntensity();                // smoothed, for overlay/other systems
```

The override is a *floor* entering the `max()` above — gameplay can push intensity
higher than the override but not below it. This is the hook zone triggers or
scripted moments use.

# 5. Editor tooling

## 5.1 Asset validation

`OnValidate()` on `MusicStateDefinition` plus a `ValidateContent()` called by the
manager at init, logging **warnings** (never throwing):

- every loop segment has ≥1 stem with a clip; stems within a segment whose clip
  lengths differ from each other by > 0.01s; clip shorter than musical length
  (audible gap) or longer than musical length + 2s (probably wrong `lengthInBars`);
  `state` values duplicated across two assigned assets; segment with every stem's
  `minIntensity > 0` (silence at intensity 0 — flag, may be intended).

## 5.2 Test-stem generator (editor-only, `Assets/Scripts/Editor/`)

Menu item **`Tools/Music/Generate Test Stems`**. Pure C# WAV synthesis (no
AudioClip API needed — write 44.1kHz 16-bit PCM WAV files with a `BinaryWriter`,
then `AssetDatabase.ImportAsset`), into `Assets/Audio/TestStems/`:

- **Per state** (Exploring=C major/110 BPM, Fighting=A minor/140 BPM,
  Inside=G major/90 BPM — different BPMs on purpose, to exercise cross-grid
  transitions), generate **two 8-bar loop segments (A and B)**, a **2-bar intro**,
  a **2-bar outro**, each with **4 stems**:
  1. *Pulse* (`minIntensity 0`) — a short click every beat, **accented and
     pitched up on beat 1 of each bar** (this is how bar alignment is verified by ear),
  2. *Bass* (`minIntensity 0.25`) — root-note sine, one note per bar,
  3. *Pads* (`minIntensity 0.5`) — sustained triad, distinct waveform (e.g. two
     detuned sines),
  4. *Lead* (`minIntensity 0.75`) — arpeggio, highest pitch.
  Segment B transposes up a fourth so segment changes are obvious. Each state uses
  a different octave/root so state changes are unmistakable.
- One **stinger** per state: a 1-beat riser into an accent, `stingerLeadIn` = one
  beat at that state's BPM (encode the value in the filename).
- Also generate the three `MusicStateDefinition` assets pre-wired to these files
  (via `AssetDatabase`/`ScriptableObject.CreateInstance`) and a `README.md` in the
  folder stating everything is generated and replaceable.

## 5.3 Debug overlay + debug keys

A new `MusicDebugOverlay : MonoBehaviour` on the MusicManager GameObject, gated
exactly like the project's other dev tools
(`if (!Application.isEditor && !Debug.isDebugBuild) { enabled = false; }` — see
`DebugDisplay.cs`), toggled with **Ctrl+M** (F8 remains an alias — the primary keys moved to Ctrl combos, see the STATUS note at the top), `OnGUI`-based like `DebugDisplay`:

- current state / pending state, active rule and sync type, **countdown to the sync
  point in seconds and beats**;
- conductor bar:beat readout that visibly ticks (verifies the grid against the
  audible beat-1 accent of the test stems);
- raw vs smoothed intensity, per-source contributions, override value;
- one horizontal volume bar per stem of the audible state(s), labelled, showing the
  live source volume;
- debug keys (same gating): **F9/F10** intensity override −/+ 0.1, **F11** cycle
  Exploring→Fighting→Inside request, **F12** clear override.
  ⚠ **Superseded:** these four keys (and their `Ctrl+,`/`Ctrl+.`/`Ctrl+N`/`Ctrl+K`
  primaries) were removed on 2026-09-01 at the owner's request. Only `Ctrl+M`/F8 remains.
  See the STATUS block at the top of this document.

# 6. Phased implementation (commit per phase, compile-check every phase)

| Phase | Deliverable | Acceptance (with generated test stems) |
|---|---|---|
| **M1** | Data model (`MusicStateDefinition`, segment/stem/rule types), validation, new serialized fields on `MusicManager` (all dormant), test-stem generator, generated assets | Compiles; generator produces importable WAVs + 3 wired assets; validation warnings fire on a deliberately broken asset; legacy music completely unaffected |
| **M2** | `MusicConductor` + `LayeredStatePlayer` playing ONE state (assign only Exploring's asset): gapless loop-segment chaining with double-buffered stems; vertical mixing from a manually set intensity (temporary inspector slider or the F9/F10 keys); debug overlay | Loop point is gapless and bar-aligned (beat-1 accent never stutters); raising intensity fades stems in at their thresholds in order Pulse→Bass→Pads→Lead; lowering removes them with release fades; hysteresis: parking intensity exactly at 0.5 does not flap Pads |
| **M3** | Horizontal transitions layered↔layered and layered↔legacy: `RequestStateChange`, rule matching, sync-point maths, crossfade, cancellation | With all 3 assets assigned: F11 with `NextBar` — the new state's beat-1 lands exactly where the countdown hits 0 and the old state's accent grid never overlaps audibly; spamming F11 before the sync point never double-plays (superseded requests cancel cleanly); unassigning one asset still transitions via legacy fallback |
| **M4** | Stingers + intro/outro transition segments | Combat entry rule with a stinger: accent lands on the sync point; Exploring→Fighting with `playIntroOfNext`: intro plays once then loops continue on-grid; Fighting→Exploring with `playOutroOfCurrent`: outro completes before Exploring enters; overlay countdown targets the outro end |
| **M5** | Gameplay intensity: `ZombieManager.GetActiveZombieCount()`, health + dusk contributions, smoothing, override API wired to overlay keys | Spawn a wave (SkipToDay/GameTesting): intensity climbs with zombie count and layers build; killing zombies releases slowly (releaseSeconds); dropping player health below threshold holds a floor; override keys beat all of it |
| **M6** | Migration + polish: assign the MUSIC mixer group to stem routing defaults, fix the Inside legacy track's missing `outputMixerGroup` (scene edit: set it to the MUSIC group, guid `855e36c5…` fileID `8426258057616649091`, matching the other two tracks), docs update (CODE_STRUCTURE music section + this file's status block), memory of what's test content vs real | Full playthrough: day/night/combat cycle produces musical, bar-aligned changes; `git status` clean of unintended churn; compile-check green |

Rules of engagement, restated: never rename/remove `musicTracks`, `MusicTrack`,
`MusicState`, `PlaybackMode`, `SetFightingState`, `SetIndoorState`, or public
`ChangeState`; keep the legacy path executing for asset-less states; all new fades
on unscaled time; no new packages, no middleware, no `.asmdef`s; scene edits only
the two listed (add overlay component is NOT needed as a scene edit — add it from
`MusicManager.Awake()` via `gameObject.AddComponent` when dev-gated, to keep scene
churn zero); commit per phase with the established message style.

# 6.5 Vertical layering — recommendations for the audio author

Written after the third audit (2026-08-18). The guiding rule: **every improvement here
must cost the audio person at most one number per stem, or nothing at all.**

## Implemented (no authoring change needed)

- **Equal-power fades.** All stem gates and state crossfades are shaped with
  `sin(x·π/2)` instead of a linear gain ramp. Linear fades dip audibly in the middle
  of a crossfade; equal-power keeps perceived loudness steady. This is what middleware
  does by default. If a fade sounds different after updating, this is why — it is
  smoother, not broken.
- **Solo / mute per stem in the overlay.** Each stem row in the `Ctrl+M` overlay has
  `S` and `M` toggle buttons (solo wins: any solo silences all non-soloed stems).
  This is the fastest way to audition one layer's threshold and fade in isolation
  while the game runs. Dev-only; the flags do nothing in a release build unless the
  overlay set them.

## How to author stems well with the current model (no code changes)

- **Give every state one `minIntensity: 0` bed stem.** The system warns if a segment is
  silent at zero intensity; a constant bed also masks the moment other layers gate in.
- **Space thresholds unevenly, low-heavy**: e.g. 0 / 0.2 / 0.45 / 0.75 rather than
  even quarters. The intensity spends most of its life low (a few zombies, dusk), so
  even spacing leaves the top layer almost never heard.
- **Fade-in short, fade-out long** (the defaults, 1.5s/2.5s, encode this). Layers
  arriving feel intentional; layers leaving should be unnoticeable.
- **Author stems at full mix level and balance with the per-stem `volume`** rather than
  baking level differences into the files — you can then rebalance in the inspector
  while listening, without re-exporting audio.
- **Remember the hysteresis**: a stem drops out 0.05 *below* its threshold. Don't
  author two stems within 0.05 of each other expecting them to trade places.

## Also implemented

- **Intensity fade band** (`fadeRange` per stem, default 0). Above zero, the stem's
  level tracks intensity *proportionally* across
  `[minIntensity, minIntensity + fadeRange]` instead of switching on at a threshold —
  the one-number equivalent of an RTPC curve, for layers that should swell rather than
  arrive. `fadeRange 0` keeps the original gating behaviour exactly, so existing
  content is unaffected. The fade times still bound how fast the level may move, so a
  sudden intensity jump glides instead of zippering, and the validator warns when a
  band extends past intensity 1 (its top could never be reached).

  *Authoring tip:* bands suit sustained layers (pads, drones); leave rhythmic layers
  (drums, arpeggios) gated, since a half-volume drum kit reads as a mistake rather than
  as tension.

## Worth adding later — in this order (each is small; none is needed now)

1. **Bar-quantised layer entries** (one optional bool per stem). Defer a gate *opening*
   to the next bar line so layers join musically instead of whenever a zombie spawns.
   Gate *closing* should stay immediate-with-fade (leaving late sounds wrong).
   Only worth it if the timed fades feel unmusical in practice — try authoring longer
   `fadeInTime`s first, which achieves most of the same effect for free.
2. **dB trim instead of linear volume** — only if mixing in linear units becomes
   annoying. A `[Range(-24, 0)] float trimDb` converted once at init. Cosmetic.

**Deliberately rejected** (complexity outweighs value at this project's scale):
per-stem EQ/filter automation, sidechain-style ducking between stems, intensity-driven
tempo changes, and per-stem sync offsets. Everything above keeps the authoring surface
at "clips + a few numbers per stem", which is the point of the design.

# 7. Out of scope (explicitly)

- `MusicZoneManager` single-clip trigger zones — untouched, they layer on top.
- `AmbienceManager` — separate system, untouched (a future round could give it the
  same stem treatment; do not attempt it here).
- Exit markers (transition points mid-segment other than beat/bar boundaries).
- Tempo-synced *ambience* or SFX quantisation.
- Real music content — the owner authors real stems and replaces the generated
  ones; the data model must make that a pure content swap (new clips + bpm/bars on
  the assets, no code changes).
