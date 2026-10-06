# Music Audit 2 Tasks — Implementation Work Order

> Written 2026-08-20 at commit `e6cee03`. **This document is a work order for an
> implementing AI.** It fixes the four highest-impact findings of the second adaptive-music
> audit (recorded in [MUSIC_LAYERING_PLAN.md](MUSIC_LAYERING_PLAN.md) → "Audit 2"). Every
> line number below was verified at `e6cee03`; **line numbers drift — re-grep before every
> edit.** Items marked ⏸ are **owner checkpoints** — stop and hand back; do not fake them.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (audio architecture → adaptive music),
> the ground rules in [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md), and the design +
> first audit in [MUSIC_LAYERING_PLAN.md](MUSIC_LAYERING_PLAN.md). The system is still
> **dormant in the scene** (`stateDefinitions: []`), so Tasks B and C can only be heard
> after the owner runs `Tools/Music/Generate Test Stems` and assigns the assets; Task A is
> audible on the legacy path today.

## Rules of engagement

1. `bash Documentation/tools/compile-check.sh` after every edit; both assemblies must
   print ✅. On this (Windows) machine the script needs the editor-generated `.csproj`
   files — if it prints ❌ *missing*, open the project in Unity once, open any script so
   the IDE integration regenerates them, and re-run. Do not treat a missing-csproj exit
   as a pass.
2. One task = one commit, repo message style (`git log` shows the convention).
3. ⚠ **Line endings**: `MusicManager.cs`, `LayeredStatePlayer.cs` and `TriggerManager.cs`
   are all **CRLF**. After editing, `file <path>` must still say CRLF; compare with
   `git show HEAD:<path> | file -` and restore byte-wise if it changed.
4. **No scene or prefab edits in this work order.** Every fix is in C#. The scene's
   `cannotOverride: [Inside]` entries on the Exploring and Fighting tracks stay as they
   are — Task A changes what they *mean*, not the data.
5. Compatibility contracts stand (CODE_STRUCTURE → adaptive music): `MusicManager.musicTracks`
   is read by reflection in `StatsHelper`; `SetFightingState`, `SetIndoorState` and public
   `ChangeState` keep their names and signatures. `ChangeState` stays public (the debug
   overlay and `Start` use it).
6. Everything that reasons about "where the music is" uses `EffectiveState`, never
   `currentState` — see the property's doc comment at `MusicManager.cs:483-488`.
7. Questions for the owner, if any arise, are **multichoice with a recommended answer**
   (README → "Asking the owner questions"). Decisions already taken are listed at the
   bottom; do not re-open them.

Useful anchors (verified at `e6cee03`):

| Thing | Location |
|---|---|
| `previousNonFightingState` (field + uses) | `MusicManager.cs:93, 232, 243, 260, 269, 445` |
| `SetIndoorState` / `SetFightingState` / `UpdateMusicState` | `MusicManager.cs:236-309` |
| `ChangeState` | `MusicManager.cs:432-481` |
| `CancelPendingTransition` | `MusicManager.cs:688-700` (the `StopEverything` at `:695`) |
| `BeginTransition` supersede block | `MusicManager.cs:769-778` (the `StopEverything` at `:772`) |
| `UpdatePendingTransition` | `MusicManager.cs:804-843` |
| `LayeredStatePlayer.ResumeLooping` (model for Task B) | `LayeredStatePlayer.cs:277-289` |
| `LayeredStatePlayer.IsPlaying` | `LayeredStatePlayer.cs:89` |
| `TriggerManager.OnTriggerEnter` music block | `Area Management/TriggerManager.cs:29-43` |
| Scene: 5 `TriggerManager` zones, `targetState` 0, 3, 2, 1, 0 (Outside, Campfire, Cave, Swamp, Outside) | script guid `1b51b4ca192da4f959941ab34f4f837e` |
| Scene: `cannotOverride: 02000000` (= `[Inside]`) on **both** the Exploring and Fighting tracks | `Level for Sound Design.unity` ~172245 and ~172262 |

---

## Task A — Music state is derived from the flags, never remembered (findings 1 + 2)

### The defect

Two bugs with one root cause: `MusicManager` keeps a *second* source of truth for
"where is the player" (`previousNonFightingState`) when `isIndoors` already is one, and
`TriggerManager` pokes the raw `ChangeState` to paper over a `cannotOverride` rule that
would otherwise trap the music.

- **Finding 1.** `TriggerManager.cs:37-41` calls `musicManager.ChangeState(Inside|Exploring)`
  immediately after `SetIndoorState(...)`. `ChangeState` ignores `isFighting`, so crossing
  any of the four outdoor ambience zones mid-fight drops `Fighting → Exploring` for the
  rest of the fight (nothing re-requests Fighting until a *new* zombie registers).
  The explicit call is there because the scene authors `cannotOverride: [Inside]` on
  **Exploring**, so `UpdateMusicState` can never take the player *out* of Inside by itself.
- **Finding 2.** `SetIndoorState` (`:240-244`) updates `previousNonFightingState` only
  when **not** fighting — the one case where it does not matter. Fight starts in the cave
  (blocked, music stays Inside, `previous = Inside`) → player runs outside while fighting
  (`previous` not updated) → last zombie dies → `ChangeState(Inside)`: cave music plays
  outdoors until the next zone crossing.

### The fix

Music state becomes a pure function of `isFighting` + `isIndoors`, and a `cannotOverride`
entry only blocks **while the blocking state's own condition still holds** — i.e. it
expresses priority between two conditions that are true at the same time ("indoors AND
fighting → Inside wins"), never a one-way door.

**A.1 `MusicManager.cs` — delete the memory.** Remove the field at `:93` and every use:
`:232` (in `Start`), the block at `:240-244` (in `SetIndoorState`), `:255-261` (in
`SetFightingState`), and `:442-446` (in `ChangeState`).

**A.2 Rewrite the two setters and the resolver** (`:236-309`) to exactly this shape:

```csharp
public void SetIndoorState(bool indoor)
{
    isIndoors = indoor;
    UpdateMusicState();
}

public void SetFightingState(bool fighting)
{
    isFighting = fighting;
    UpdateMusicState();
}

/// <summary>The state the flags ask for. Fighting outranks Inside outranks Exploring.</summary>
private MusicState DesiredState =>
    isFighting ? MusicState.Fighting : (isIndoors ? MusicState.Inside : MusicState.Exploring);

/// <summary>
/// True while the game still has a reason to be in <paramref name="state"/>. Exploring is
/// the default, held only when nothing else is.
/// </summary>
private bool ConditionHolds(MusicState state) => state switch
{
    MusicState.Fighting => isFighting,
    MusicState.Inside => isIndoors,
    _ => !isFighting && !isIndoors
};

/// <summary>
/// Single decision point for gameplay-driven music. cannotOverride (authored on the legacy
/// MusicTrack entries) is a PRIORITY rule between two simultaneously-true conditions: the
/// blocked state waits only while the blocking state's condition still holds. Once that
/// condition clears, the block clears with it - so no rule can ever trap the music.
/// </summary>
private void UpdateMusicState()
{
    MusicState newState = DesiredState;
    MusicState effective = EffectiveState;
    if (newState == effective) return;

    var currentTrack = musicTracks.FirstOrDefault(t => t.state == effective);
    var newTrack = musicTracks.FirstOrDefault(t => t.state == newState);

    bool blocked = currentTrack != null && newTrack != null &&
                   newTrack.cannotOverride != null &&
                   newTrack.cannotOverride.Contains(effective) &&
                   ConditionHolds(effective);
    if (blocked) return;

    ChangeState(newState);
}
```

Keep `EffectiveState` exactly as it is. Keep the `AudioEventLog.Record` line in
`ChangeState`. `Start()` still calls `ChangeState(startingState, true)` — `startingState`
is only the seed; the first flag change corrects it.

**A.3 `ChangeState` doc comment.** Add above `:432`:

```csharp
/// <summary>
/// Raw transition. Bypasses isFighting / isIndoors / cannotOverride - gameplay must go
/// through SetFightingState / SetIndoorState, which are the only authorities on music
/// state. Public only for Start and the debug overlay (whose Ctrl+N cycle is therefore
/// snapped back by the next flag change, which is the intended behaviour of a debug key).
/// </summary>
```

**A.4 Update the `cannotOverride` tooltip** (`:51`) so the authoring surface says what the
rule now means:

```
"States this track must wait behind while THEIR condition still holds. E.g. Fighting
listing Inside means: indoors AND zombies alive → Inside keeps playing; leave the cave →
Fighting starts. A block never outlives the condition that caused it."
```

**A.5 `TriggerManager.cs:29-43`** — replace the whole `if (musicManager != null) { … }`
block with:

```csharp
if (musicManager != null)
{
    musicManager.SetIndoorState(ShouldUseIndoorMusic);
}
```

`LocationTrigger` already does only this; leave it alone.

**A.6 Docs.** In `CODE_STRUCTURE.md`'s single-authority table, change the
"Music state and musical time" row's notes to: *"State is a pure function of
`isFighting`/`isIndoors` (+ `cannotOverride` as a priority rule between them) — nothing
remembers a previous state, and gameplay never calls `ChangeState` directly. The layered
pipeline only decides *when* the change lands. Don't add a second scheduler."* In
`MUSIC_LAYERING_PLAN.md` → "Audit 2", mark findings 1 and 2 fixed with the commit hash.

### Why this cannot trap the music (reason it through before committing)

Blocking needs `ConditionHolds(effective)` and `DesiredState != effective`. Exploring
holds only when neither flag is set — and then `DesiredState` *is* Exploring. So a block
can only ever occur between Fighting and Inside while **both** flags are true, which is
precisely the priority case the rule exists for. Trace these against the scene data
(both Exploring and Fighting list `[Inside]`):

| Sequence | Expected music |
|---|---|
| Outdoors, zombie spawns, cross the Swamp zone, zombie still alive | Fighting throughout (was: Exploring after the zone) |
| In cave, zombie spawns | Inside (blocked, as authored) |
| …run out of the cave, zombie alive | Fighting (block cleared with `isIndoors`) |
| …kill it outdoors | Exploring (was: Inside) |
| Walk in and out of the cave, no zombies | Inside / Exploring at each trigger — the path the deleted explicit `ChangeState` used to carry |
| Fighting pending (NextBar not yet landed), enter cave | Fighting lands; `SetIndoorState(true)` is a no-op because `EffectiveState` is already Fighting |

**Verify:** compile clean; `file` says CRLF on both `.cs` files; `git diff` shows no
scene change. ⏸ Owner play check: the table above, with `Ctrl+J` open — every row's
`Music` state line must match the expected column, and there must be **no** `Music`
line when crossing an outdoor zone mid-fight.

---

## Task B — Abandoning a takeover fades, never cuts (finding 3)

### The defect

`CancelPendingTransition` (`MusicManager.cs:695`) and the supersede block in
`BeginTransition` (`:772`) call `StopEverything()` on the pending target's player on the
assumption that a pending player is silent. After a **graceful takeover**
(`LayeredStatePlayer.ScheduleEntry`, `:144-185`) it is not: the player is audible,
holding its fade level, waiting for its entry bar. Wave combat produces the failing
sequence routinely: zombie spawns (Fighting lands, Exploring starts its 2 s fade-out) →
dies inside that crossfade (Exploring takeover pending, fade held at ~0.6) → another
spawns → `RequestStateChange(Fighting)` takes the `currentState == target` branch →
`CancelPendingTransition` → Exploring cut from 0.6 to silence in one buffer.

### The fix

**B.1 `LayeredStatePlayer.cs` — add, next to `ResumeLooping` (`:277-289`), which it mirrors:**

```csharp
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
```

Notes for the implementer: `SetBankScheduledEnd` already skips sources that are not
playing, so a takeover whose entry lay past the segment's natural end (the "benign gap"
case in `ScheduleEntry`'s comment) is safe — the player simply chains its next loop under
the fade-out and `UpdateStateFade` releases it at zero. `BeginFadeOut` sets `isFadingOut`,
so the release-at-zero path in `UpdateStateFade` (`:384-392`) will `StopEverything` when
the fade completes, exactly as a normal departure does.

**B.2 `MusicManager.cs` — a helper and two call-site changes.**

```csharp
/// <summary>
/// Fade used when a pending entry is abandoned: the transition's own crossfade, or the
/// serialized default when that transition was instantaneous (a 0 here would reintroduce
/// the cut this exists to prevent).
/// </summary>
private float AbandonFadeSeconds(float transitionCrossfade) =>
    transitionCrossfade > 0.001f ? transitionCrossfade : Mathf.Max(0.25f, crossFadeDuration);
```

- `CancelPendingTransition` (`:692-696`): `incoming.StopEverything();` →
  `incoming.AbandonEntry(AbandonFadeSeconds(pending.crossfade));`
- `BeginTransition` supersede block (`:769-773`): `superseded.StopEverything();` →
  `superseded.AbandonEntry(AbandonFadeSeconds(pending.crossfade));`
  This reads `pending.crossfade` **before** the assignments at `:780-785` overwrite it —
  keep it above them.

Update both surrounding comments: they currently say "Stopping the incoming player
cancels its PlayScheduled", which is now only the silent half of the story.

**Verify:** compile clean; CRLF preserved on both files. ⏸ Owner listen check (needs the
generated test stems assigned): during a night wave with the `Ctrl+M` overlay open, when
the first zombie's fight music lands, kill it within ~2 s while a second is spawning. The
**Exploring** row's fade value must ramp down to 0 over the crossfade and its stem rows
fade with it; before this fix the row blanks and the under-layer drops out in one frame
(audible as a click/hole under the combat stems).

---

## Task C — Recover from an audio-device change (finding 4)

### The defect

Nothing subscribes to `AudioSettings.OnAudioConfigurationChanged`. When the output device
changes (headphones in/out, a Bluetooth device connecting — routine on the owner's
Windows machine) or `AudioSettings.Reset` runs, Unity rebuilds the audio system: every
`AudioSource` stops and `AudioSettings.dspTime` restarts. Both music paths then die
permanently:

- **Layered**: `hasPending` is true with a `pendingStartDsp` that is now far in the
  future, so `PromoteIfDue` never fires and `Tick` (`LayeredStatePlayer.cs:328`) never
  schedules again; the conductor's anchor points at a clock that no longer exists.
- **Legacy**: the `Update` loop (`MusicManager.cs:358-378`) only reschedules while
  `source.isPlaying`, and the `nextClipScheduled` latch waits for a DSP time that will
  not come for hours.

Silence until the next state change — which in a quiet stretch of Exploring may be minutes.

### The fix

**C.1 `MusicManager.cs` — subscribe for the component's enabled lifetime.** Add after
`Awake` (`:111-121`):

```csharp
private void OnEnable()
{
    AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
}

private void OnDisable()
{
    AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
}

/// <summary>
/// Unity has just rebuilt the audio system (output device changed, or AudioSettings.Reset):
/// every AudioSource is stopped and AudioSettings.dspTime has restarted. Every scheduled
/// segment, the pending transition and the conductor's anchor now refer to a clock that no
/// longer exists, so nothing can be resumed - the state the flags ask for is rebuilt from
/// scratch with an immediate entry.
/// </summary>
private void OnAudioConfigurationChanged(bool deviceWasChanged)
{
    MusicState resume = EffectiveState;

    StopMusicFades();
    pending.active = false;
    lastStingerSource = null;
    conductor.Stop();

    foreach (LayeredStatePlayer player in layeredPlayers.Values) player.StopEverything();

    foreach (MusicTrack track in musicTracks)
    {
        if (track.source == null) continue;
        track.source.Stop();
        track.nextClipScheduled = false;   // the latch is waiting on the old clock
    }

    Debug.Log($"[MusicManager] Audio configuration changed (device changed: {deviceWasChanged}); " +
              $"restarting {resume}.", this);

    currentState = resume;              // so the immediate re-entry has no "outgoing"
    ChangeState(resume, immediate: true);
}
```

Why `currentState = resume` before the call: `ChangeState(…, immediate)` routes a layered
state through `BeginTransition`, and `UpdatePendingTransition` would otherwise treat the
state we are *resuming* as the outgoing one and fade it out the moment it lands. With
`currentState == target` the landing takes the no-outgoing path (`:819`), `conductor.Begin`
re-anchors on the new clock, and the legacy branch of `ChangeState` does its usual
`SetStateVolumes` restart.

Two things this deliberately does not do: it does not try to resume mid-segment (the old
DSP positions are meaningless on the new clock), and it does not special-case
`deviceWasChanged == false` (an explicit `AudioSettings.Reset` needs the identical
treatment).

Known cosmetic consequence, accepted: the immediate re-entry plays the state's intro if
it has one (the immediate path passes `playIntro: true`). If the owner finds that
jarring, `RequestStateChange`'s immediate branch can gain a `playIntro` parameter — not
in scope here.

**Verify:** compile clean; CRLF preserved. ⏸ Owner check, either way works:
(a) in play mode, unplug/plug headphones or switch the Windows default output device;
(b) in play mode, change **Edit ▸ Project Settings ▸ Audio ▸ DSP Buffer Size** (this
calls `AudioSettings.Reset`). Music must resume in the same state within a frame, one
`[MusicManager] Audio configuration changed` line in the console, and — with test stems
assigned — the `Ctrl+M` bar:beat counter running again. Before the fix the music stays
silent until the next state change.

---

## Suggested commit sequence

1. `Music state derives from the flags; TriggerManager stops calling ChangeState` (Task A)
2. `Abandoning a takeover fades the player instead of cutting it` (Task B)
3. `Restart music after an audio-device change` (Task C)
4. `Record the Audit-2 fixes` — STATUS block below + the two doc edits in A.6.

## Explicitly out of scope (recorded in MUSIC_LAYERING_PLAN → Audit 2, not to be done here)

Findings 5–7 and the smaller items: the legacy path's single-source clip gap and silent
start-all-tracks behaviour, the zero-crossfade first-frame cut, stinger mixer-group
fallback, fade-in after an outro handover, intro on takeover. Do not fold them into the
three commits above, even where the code is adjacent.

## Decisions already taken (owner may veto, implementer must not)

| Decision | Why |
|---|---|
| `cannotOverride` becomes a *priority* rule (blocks only while the blocker's condition holds) rather than editing the scene to remove `Inside` from Exploring's list | Keeps the student-facing authoring data untouched, makes the rule impossible to author into a trap, and matches what the authored data evidently meant ("no combat music in the cave") |
| `previousNonFightingState` is deleted, not repaired | A remembered state and a derived state will disagree again the next time someone adds a flag; `isIndoors` is already the truth |
| Abandon-fade uses the abandoned transition's own crossfade, falling back to the serialized default (min 0.25 s) | Predictable for the author; a zero here would re-create the cut |
| Device-change recovery re-enters immediately rather than re-syncing to a bar | There is no bar to sync to — the clock that defined it is gone |

## STATUS — implementation record (2026-08-26)

All three tasks implemented as specified, one commit each, at the code state after
`967bf86` (which had already fixed findings 7+8 and upgraded Unity to 6000.0.72f1 —
anchors in this document had drifted accordingly; everything was re-located by name):

| Task | Commit | Notes |
|---|---|---|
| A — state derived from flags | `647fe79` | Exactly the specified shape. `previousNonFightingState` deleted (grep confirmed no other user, incl. overlay/StatsHelper). No scene change; both `cannotOverride: [Inside]` entries untouched. |
| B — AbandonEntry fades | `06f138e` | As specified; the supersede site reads `pending.crossfade` before the overwrite. |
| C — device-change recovery | `2e9908a` | As specified. Interacts fine with the finding-8 startup lead: the 1 s lead is consumed at scene start, so recovery re-enters with the bare margin. |

**Compile result:** the compile-check script remains blocked on this machine — the
editor-generated `.csproj` files have never existed (open any script from Unity once, or
Preferences ▸ External Tools ▸ Regenerate project files). The owner explicitly waived the
script check for this machine's commits on 2026-08-25 (recorded in `f19eb13`); Unity
compiles the edits on next editor focus and refuses Play on errors.

**Line endings:** `file` reports CRLF on `MusicManager.cs`, `LayeredStatePlayer.cs` and
`TriggerManager.cs` after every edit.

**⏸ Owner checks still open:** Task A's six-row trace table (with Ctrl+J open — no `Music`
line when crossing an outdoor zone mid-fight); Task B's kill-within-the-crossfade listen
check (Ctrl+M — the Exploring rows must ramp down, not blank); Task C's device-switch
check (music resumes in-state within a frame, one console line, bar:beat ticking).

---

## Audit 3 (2026-09-02) — full read of the layered system + the week's features

A line-by-line pass over MusicManager, LayeredStatePlayer, MusicConductor,
MusicStateDefinition, MusicIntensityConfig/Driver, MusicDebugOverlay, MusicController,
MusicZoneManager, MushroomTripEffect and the weapon reload port. The layered core came
through clean — the takeover/abandon/resume web, the conductor grid maths,
VerifyEntryStart and the intensity night-wrap were all checked and confirmed. Eight
findings, all fixed the same day:

| # | Finding | Fix |
|---|---|---|
| 1 | The shared ammo readout was hidden when a reload finished on a weapon lowered mid-reload | `ReloadRoutine` repaints only while raised |
| 2 | **Audio state:** a trip cut short by death (scene reload) left the trip mixer snapshot latched — snapshot state lives on the mixer asset and survives reloads | `MushroomTripEffect.OnDestroy` transitions back to `normalSnapshot` when destroyed mid-trip |
| 3 | Legacy track boundaries assigned the next clip to the playing source, stopping it ≤ 0.1 s early and leaving an equal gap (dormant — all three states are layered — but live during any future migration) | Each legacy track now carries two sources; boundaries schedule on the idle one and the roles swap |
| 4 | `MusicZoneManager` NRE'd on trigger entry when its AudioSource was missing, and held a muted voice from scene load | Null guards in both trigger handlers; playback starts on entry and stops after fading out |
| 5 | A device-change restart reused the bare 50 ms schedule margin instead of the 1 s startup runway, under exactly the churn the runway exists for | `OnAudioConfigurationChanged` resets `startupEntryScheduled` |
| 6 | Stinger sources fell back to **no** mixer group when `defaultMusicMixerGroup` was empty (players fall back to the legacy track's group), escaping the music bus | Stingers inherit the first resolved player group |
| 7 | ⏸ **Owner, open:** the SMG reload take (2.16 s) outlasts the 1.5 s default `reloadTime`, so rapid reloads can overlap takes | Authoring: tune each gun's `Reload Time` to roughly match its takes |
| 8 | `MusicManager.Update` walked the legacy tracks every frame with every state layered | Skipped via `hasLegacyTracks`, decided once in Awake |
| 9 | MusicDebugOverlay and EndGameScreen each allocated 1×1 textures for flat fills | Both now tint `Texture2D.whiteTexture`, like TitleScreen |

Verified with `compile-check.sh` (unblocked 2026-09-01): both assemblies, exit 0.
