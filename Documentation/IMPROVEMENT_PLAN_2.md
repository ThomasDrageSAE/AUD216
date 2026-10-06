# Improvement Plan 2 — Objectives Audit & Project-Wide Follow-Up

> Audit date 2026-08-17 (fresh pass, after the 2026-07-06 audit's Phases 0–5 were
> committed and the terrain-layer recovery landed). Companion docs:
> [CODE_STRUCTURE.md](CODE_STRUCTURE.md) · [AUDIT_FINDINGS.md](AUDIT_FINDINGS.md) ·
> [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) · [TERRAIN.md](TERRAIN.md)
>
> **This document is written to be handed to an implementing AI (or human) as a
> work order.** Each item has a severity, exact locations, the defect, fix
> instructions, and an acceptance check. Line numbers were verified on commit
> `5968b41` and refer to the code **before** implementation.

## STATUS (2026-08-17): all six phases implemented

Every item below is done. `W6` was deferred here, then shipped 2026-08-18 as Roadmap Task 8 (`e0b05e8` normalised lookups, `b7ec905` identity enums + `Tools/Audio/Migrate Profile Keys To Enums`); only the caller-side string→id conversion remains gated on an in-editor migration run. Original commits, in order:
`9aa70e1` (2.1) · `a8bdb66` (2.2) · `7467d10` (2.3) · `f251080` (2.4) ·
`c132388` (2.5) · `de21b3f` (2.6).

Each commit was validated by compiling **both** `Assembly-CSharp` and
`Assembly-CSharp-Editor` with Unity 6000.0.62f1's bundled Roslyn against the
project's own reference set — the whole project builds clean. **Play-mode
acceptance checks (the "Accept:" lines below) have not been run and are the
remaining verification step.**

Deviations from the plan as written, and why:

| Item | Deviation |
|---|---|
| O7 | Was described below but missing from the phase table; implemented in 2.1. Scene check found no trigger setting `requiresObjectiveToBeActive: 0`, so the flag was deleted (the plan's second option) rather than changing `CompleteObjective`, which would have caused the tracker to pre-complete objectives that were never shown. |
| O13 | `Zombie` was not switched to `ObjectiveTargetLocator` — it needs no target component at all (nothing queries enemies through `GetTargetLocations`), so the dynamic `AddComponent` was dropped entirely. That leaves `ObjectiveTarget` with no C# users; it is trimmed and marked legacy in-file rather than deleted, because **one scene object still carries it** (`objectiveId: TestMixingDesk`). Editing that instance is not safe from the CLI. |
| W1 | Renderers are discovered once at `Awake` (inactive included) instead of two serialized `Renderer` fields: the cloud material is used by three prefabs with multiple instances, so two fields could not have covered it. Requires no inspector wiring. |
| O14 | Deleting the wave-completion fields also required removing `WaveManagerExporter`'s `animationSettings` block, which read them by name — otherwise the export would have silently degraded to empty values. |
| O9 | Additionally: a failed spawn (no spawn points, empty prefab slot, prefab without `ZombieAI`) is now counted as already-dead, and a `ZombieAI`-less instance is destroyed. Previously either case left a zombie total that could never reach zero, stalling the night forever. |
| O14 | `maxViewDistance` was deleted rather than implementing distance culling: hiding the marker with `SetActive(false)` would stop the `Update` that must re-show it, and the fix needs prefab knowledge to do properly. |

Still open after this round:

1. ~~Run the play-mode acceptance checks~~ — **done 2026-08-18, playthrough clean.**
2. ~~O13 scene migration~~ — **done 2026-08-18**. Migrating the component
   also fixed a live bug: `CompleteObjective` only cleans up markers through
   `ObjectiveTargetLocator`, so the `TestMixingDesk` marker (`showWorldMarker: 1`)
   used to hang in the world and on the compass for the rest of the session.
   `ObjectiveTarget.cs` is now deleted.
3. **W6** (enum-over-string profile keys) — deferred here by design; since shipped as Roadmap Task 8 (see the note above). The W6 text below is the original analysis, kept as the record.

## Ground rules for the implementer (read first)

1. **Never rename or remove a `[SerializeField]` field without checking scene
   wiring first** — the main scene (`Assets/Level for Sound Design.unity`) stores
   data by field name. Removing a field only loses that field's stored value
   (safe when the field is confirmed unused); *renaming* one silently drops data
   unless you add `[FormerlySerializedAs]`.
2. **Do not delete these classes** (flagged dead by the first audit but verified
   live): `Core.Pooling.ObjectPool`, `DayNightCycle.SmoothTransition`,
   `Audio/MusicController` (scene-wired). (`ObjectiveTarget` was on this list until
   phase 2.6 replaced it with `ObjectiveTargetLocator` and deleted it — do not
   resurrect it. ⚠ **`Audio/MusicController` came off this list on 2026-09-03**: the
   "scene-wired" claim was stale — nothing called it and no UnityEvent referenced it —
   and it was removed by AUDIO_MANAGER_TIDY_TASKS.md Task 4.1. The rest of the rule
   stands.)
3. Scene-wired UnityEvents in the main scene call only: `SetSFXVolume`,
   `SkipToDay2`, `SkipToDay3`, `Quit`. Prefabs wire only UI-extension state
   methods, `SetActive`, `SceneLoad`, `NextCamera`, `OnMenuStatusChange`,
   `LoadURL`. Any public method *not* on those lists and with no C# callers is
   genuinely dead.
4. Keep the Legacy Input Manager (`Input.GetKey…`) — do not migrate to the new
   Input System.
5. Never touch `.meta` files, `.terrainlayer` files, or anything under
   `…/Terrains/TerrainLayers/` (see TERRAIN.md for why).
6. Test in `Assets/Level for Sound Design.unity` after each phase. There are no
   automated tests; the acceptance checks below are manual/play-mode checks.
7. Work in the phase order given at the bottom; commit per phase like the
   previous rounds (`git log` shows the convention).

Severity: **P1** = player-visible/audible bug · **P2** = correctness/robustness
defect · **P3** = refactor/consolidation · **P4** = efficiency & dead code.

---

# Part A — Objectives system

The objectives stack is: `ObjectiveData`/`DailyObjectiveData` (ScriptableObjects)
→ `ObjectiveManager` (DDoL singleton: active/completed sets + per-objective UI) →
`DailyObjectiveManager` (DDoL: time-of-day scheduling, pauses the clock) →
`WaveManager` (owns the `ZombieDefence` objective) → completion sources
(`ObjectiveTrigger`, `FirstTimeInteractionTracker`, `FirestickInteraction`,
`Fish`) → presentation (`ObjectiveUI2`, `WorldSpaceObjectiveManager`/`Marker`,
`ObjectiveRadar`).

## O1 (P1) Auto-completed objectives fire the completion sound & animation twice

**Where:** `ObjectiveManager.cs:132` + `ObjectiveManager.cs:136–148` and
`FirstTimeInteractionTracker.cs:92–111`.

**Defect:** Two independent auto-complete paths run for the same event. When
`AddObjective` runs for an already-done objective (`PickupFishingRod`,
`CatchAFish`, `PickupAxe`, `LightCampfire`):
1. Line 132 fires `OnObjectiveAdded`; the tracker's `HandleObjectiveAdded`
   synchronously calls `CompleteObjective` (sound + animation + 1 s removal).
2. Lines 136–148 then *also* detect `shouldAutoComplete` and start
   `AutoCompleteObjectiveWithDelay(0.5f)`, which calls `CompleteObjective`
   again — the UI entry is still in `activeObjectives` (removal happens at
   1 s), so the completion **sound plays twice** and the animation restarts.

**Fix:** Keep exactly one path. Recommended: delete the auto-complete block in
`ObjectiveManager.AddObjective` (lines 136–148 plus the now-unused
`AutoCompleteObjectiveWithDelay` coroutine) and keep the tracker's event-driven
`HandleObjectiveAdded`, which is the single-responsibility owner of "was this
already done". While doing this, apply O2's un-nesting (the two edits touch the
same block). If a short delay before auto-completion is desired for readability,
move the 0.5 s delay into `HandleObjectiveAdded` instead.

**Accept:** With the fishing rod already collected, trigger the day's
`PickupFishingRod` objective → completion stinger plays exactly once; objective
card animates in, marks complete once, and leaves cleanly.

## O2 (P1) World markers are never created if FirstTimeInteractionTracker is absent

**Where:** `ObjectiveManager.cs:134–161`.

**Defect:** Marker creation (lines 149–160) is nested inside
`if (FirstTimeInteractionTracker.Instance != null)`. The tracker singleton has
nothing to do with world markers; if it is missing or destroyed, every
objective silently loses its world-space marker.

**Fix:** Restructure so marker creation runs whenever
`objectiveData.showWorldMarker` and the objective was *not* auto-completed —
independent of the tracker's existence:

```csharp
bool alreadyDone = /* tracker present && objective pre-completed (O1 path) */;
if (!alreadyDone && objectiveData.showWorldMarker && WorldSpaceObjectiveManager.Instance != null)
{
    // existing GetTargetLocations / CreateObjectiveMarkers code
}
```

**Accept:** Disable the tracker GameObject in the scene, start a day objective
with `showWorldMarker = true` → marker still appears.

## O3 (P1) Enemy compass markers leak — frozen markers pile up where zombies died

**Where:** `Zombie.cs:38–47`, `Zombie.cs:51–57`, `ZombieAI.cs:441`,
`WorldSpaceObjectiveManager.cs:90–125`.

**Defect:** Every zombie creates a world marker keyed exactly `Enemy_<id>` via
the *singular* `CreateObjectiveMarker` (`Zombie.cs:46`). Nothing ever removes
it: `Zombie.OnDestroy` only unregisters from `ZombieManager`, and even if
`RemoveObjectiveMarker("Enemy_<id>")` were called, the no-target branch
(`WorldSpaceObjectiveManager.cs:110–112`) only removes keys matching
`StartsWith(objectiveId + "_")` — it never matches an **exact** key. Result:
each killed zombie leaves a marker GameObject frozen at its death position
(its `Update` early-outs on the destroyed target), plus a stale compass
indicator in `ObjectiveRadar`. Every night wave makes it worse.

**Fix (two parts, both required):**
1. In `WorldSpaceObjectiveManager.RemoveObjectiveMarker`, in the `target == null`
   branch, also remove an exact-key match:
   ```csharp
   if (activeMarkers.TryGetValue(objectiveId, out var exact))
   {
       if (exact != null) Destroy(exact.gameObject);
       activeMarkers.Remove(objectiveId);
   }
   ```
2. In `Zombie.OnDestroy`, remove the marker:
   ```csharp
   if (WorldSpaceObjectiveManager.Instance != null)
       WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker($"Enemy_{GetInstanceID()}");
   ```
   (Cache the `uniqueId` string built in `Start` in a field rather than
   rebuilding it, so the two always agree.)

**Accept:** Kill several wave zombies → no floating markers remain at death
spots; compass returns to empty; `WorldSpaceObjectiveManager.ActiveMarkers.Count`
returns to its pre-wave value.

## O4 (P2) Completed-objective UI is destroyed twice; the "Out" animation never plays

**Where:** `ObjectiveManager.cs:199–211` vs `ObjectiveUI2.cs:75–93`.

**Defect:** `CompleteObjective` schedules `RemoveObjectiveAfterDelay(1f)` which
destroys the UI GameObject at 1.0 s. `ObjectiveUI2.PlayCompletionAnimation`
*also* owns removal: inactive anim (1.0 s) → "Out" anim → destroy at 1.5 s. The
manager's destroy wins at 1.0 s, so the card's exit animation is always cut off.

**Fix:** Make `ObjectiveUI2` the sole owner of its destruction. In
`ObjectiveManager.CompleteObjective`, replace the destroy-coroutine with
immediate bookkeeping: `activeObjectives.Remove(objectiveId)` right after
`PlayCompletionAnimation()` (the completed flag already lives in
`completedObjectives`). Delete `RemoveObjectiveAfterDelay`. Check `HideObjective`
still destroys directly (that path is fine — it's for hiding, not completing).

**Accept:** Complete any objective → the card plays Inactive, then the Out
animation fully, then disappears. No `MissingReferenceException` in the console.

## O5 (P2) Two competing day counters can desync the whole progression

**Where:** `AnimationEventHandler.cs:28,100–105,274–288` vs
`DayNightCycle.cs:42,304–309`; consumed at `DailyObjectiveManager.cs:204`
(reads AnimationEventHandler) and `WaveManager.cs:209–212` (reads DayNightCycle).

**Defect:** `DayNightCycle.dayNumber` increments when time crosses
`dayTransitionTime` and fires `OnDayChanged`. `AnimationEventHandler.currentDay`
is a *separate* counter incremented by its own night→dawn detection
(`Update`, lines 100–105). The two use different detection logic, so time
jumps (`SetTimeOfDay` from `WaveManager.cs:710`, `ObjectiveTrigger`, cheats)
can advance one and not the other — and `DailyObjectiveManager` and
`WaveManager` each follow a different counter. `GameTesting.SkipToDay`
(GameTesting.cs:~400–421) already has to manually sync both, which is the
design telling you it's wrong.

**Fix:** Single source of truth = `DayNightCycle`.
1. In `AnimationEventHandler`: delete `currentDay` increments (lines 100–105
   become only announcement scheduling); subscribe to
   `dayNightCycle.OnDayChanged` in `Start` (unsubscribe in `OnDestroy`) and set
   an internal `pendingAnnouncement` flag + day value from the event. Keep
   `GetCurrentDay()` but return the value received from the event (seed it from
   `dayNightCycle.GetCurrentDay()` in `Start`). Keep `SetCurrentDay` as a
   forwarding no-op or delete after updating GameTesting.
2. In `GameTesting.SkipToDay…`: remove the manual `SetCurrentDay` call (the
   `OnDayChanged` from `SetDayNumber` now covers it) but keep
   `ShowDayAnnouncementNow`.
3. `DailyObjectiveManager.Update` line 204: read
   `dayNightCycle.GetCurrentDay()` instead of the animation handler.

**Accept:** Use the scene-wired `SkipToDay2` button → day announcement shows
"DAY 2" once, day-2 objectives appear, and `WaveManager` UI shows Week 1 /
Night 2. Let a full night pass naturally → same consistency.

## O6 (P2) Objective state only exists when its UI can be built

**Where:** `ObjectiveManager.cs:126–162`.

**Defect:** `activeObjectives.Add` happens *inside*
`if (objectiveUIPrefab != null && objectivesContainer != null)`. If the prefab
fails to load from Resources or no Canvas exists (both only warn at Awake),
every `AddObjective` silently no-ops: triggers won't complete, prerequisites
never satisfy, the day never advances past the 0.85 pause. Gameplay state must
not depend on UI availability.

**Fix:** Track state unconditionally, treat UI as optional:
```csharp
ObjectiveUI2 objectiveUI = null;
if (objectiveUIPrefab != null && objectivesContainer != null) { /* instantiate */ }
activeObjectives.Add(objectiveId, objectiveUI);   // may be null
OnObjectiveAdded?.Invoke(objectiveId);
```
Then null-guard the UI uses: `CompleteObjective` →
`activeObjectives[objectiveId]?.PlayCompletionAnimation()`; `HideObjective` →
guard the `Destroy`. (Dictionary value type stays `ObjectiveUI2`; null values
are fine.)

**Accept:** Temporarily clear the prefab reference in the inspector → objectives
still activate/complete (log-visible, `IsObjectiveCompleted` true), no NREs;
restore the reference afterwards.

## O7 (P2) `ObjectiveTrigger.requiresObjectiveToBeActive = false` does nothing

**Where:** `ObjectiveTrigger.cs:79–92` + `ObjectiveManager.cs:176–178`.

**Defect:** The flag promises "complete even if not yet shown", but
`CompleteObjective` early-outs unless the objective is in `activeObjectives` —
so with the flag false and the objective inactive, the call completes nothing,
yet the trigger still sets `hasTriggered = true` and (by default) destroys
itself. The one-shot is consumed with zero effect, and the objective can never
be completed by that trigger again.

**Fix:** In `CompleteObjective`, handle the not-active case: if
`HasObjective(objectiveId)` and not active, still `completedObjectives.Add(objectiveId)`
(so prerequisites/`IsObjectiveCompleted` work) and skip the UI/audio path.
Alternatively — simpler and honest — delete the flag and its check from
`ObjectiveTrigger` if no trigger in the scene sets it false (check the scene:
search `requiresObjectiveToBeActive: 0` in the .unity file before choosing).
Also remove the `try/catch` around the completion call (lines 89–118): it can
only mask real bugs; nothing in `CompleteObjective` throws in normal operation.

**Accept:** A trigger with the flag false completes an unshown objective →
`IsObjectiveCompleted` returns true and any prerequisite chain unlocks.

## O8 (P2) Three time-pause writers fight every frame; setting speed un-pauses time

**Where:** `DailyObjectiveManager.cs:217–251` and `DayNightCycle.cs:773–781`.

**Defect:** Each `Update`, up to three blocks issue `PauseTime()`/`ResumeTime()`
independently (time-critical objective, the 0.85 all-day-objectives gate, the
unconditional 0.95 gate) — last writer wins, which currently *happens* to work
but is one reorder away from breaking. Worse, `SetTimeSpeedMultiplier`
(`DayNightCycle.cs:773`) clamps the multiplier to ≥ 0.1 and then always sets
`m_IsTimePaused = false` — so any code "resetting speed to 1" (e.g.
`DailyObjectiveManager.cs:213`, `ObjectiveTrigger.cs:106`) silently cancels an
active pause.

**Fix:**
1. `DayNightCycle.SetTimeSpeedMultiplier`: delete the un-pause side effect
   (remove lines `if (_multiplier > 0) { m_IsTimePaused = false; }`). Pause is
   controlled only by `PauseTime`/`ResumeTime`.
2. `DailyObjectiveManager.Update`: compute one verdict, apply once:
   ```csharp
   bool shouldPause =
       (m_CurrentTimeCriticalObjective != null
           && currentTime >= m_CurrentTimeCriticalObjective.pauseTimeOfDay
           && !AreTimeCriticalObjectivesComplete())
       || (currentTime >= TIME_PAUSE_THRESHOLD && !AreAllDayObjectivesCompleted())
       || (currentTime >= LATE_TIME_PAUSE_THRESHOLD && night-defence not cleared*);
   if (shouldPause) dayNightCycle.PauseTime(); else dayNightCycle.ResumeTime();
   ```
   \*The 0.95 gate exists so zombies spawn before dawn; its release is
   `WaveManager.HandleZombieDeath` calling `ResumeTime` + `SetTimeOfDay(0.1)`
   (WaveManager.cs:708–710). Express that: pause at ≥0.95 only while
   `!ObjectiveManager.Instance.IsObjectiveCompleted(ObjectiveConstants.ZOMBIE_DEFENCE)`,
   falling back to `true` (pause) when the manager is missing.

**Accept:** Play through an evening: time pauses at 0.85 with objectives
open, resumes when they're done, pauses again at 0.95, and resumes to dawn when
the last zombie dies. An `ObjectiveTrigger` that sets time speed during the
0.85 pause no longer breaks the pause.

## O9 (P2) Wave-completion tracking miscounts when waves overlap

**Where:** `WaveManager.cs:179,413,663–684`.

**Defect:** `zombiesAliveInCurrentWave` is a single counter: `StartWave`
(line 413) resets it to the new wave's size while earlier waves' zombies are
still alive, and `HandleZombieDeath` (line 666) decrements it for *any* zombie
death. `ManageNightWaves` starts the next time-based wave as soon as spawning
finishes (`isWaveActive` false), so overlap is the normal case — meaning
`completedWaves` entries (which gate `waitForPreviousWave` waves and the
per-wave complete messages) are recorded early or late, not when that wave's
zombies are actually dead.

**Fix:** Attribute deaths per wave. Give `SpawnZombie`/`SpawnWaveZombies` the
wave index, keep `Dictionary<int, int> aliveByWave`, and subscribe with a
closure:
```csharp
int idx = currentWaveIndex;                       // capture per spawn
zombieAI.OnZombieDeath += () => HandleZombieDeath(idx);
```
`HandleZombieDeath(int waveIdx)` decrements `aliveByWave[waveIdx]` and marks
`completedWaves.Add(waveIdx)` when it hits zero **and** that wave's spawning has
finished (track a `HashSet<int> finishedSpawning` set at the end of
`SpawnWaveZombies`). Keep the existing `zombiesAlive` night-total logic
unchanged — it drives the objective and is correct.

**Accept:** Configure a test night where wave 2 has `timeToStart` before wave 1
can be cleared and wave 3 has `waitForPreviousWave = true` → wave 3 starts only
after wave 2's own zombies are dead; complete-messages fire per wave at the
right moments.

## O10 (P2) Night teardown leaks when zombies survive past dawn

**Where:** `WaveManager.cs:492–526` (early return at 494–497), monitor loop at
`WaveManager.cs:355–359`.

**Defect:** If time crosses back below the spawn window while zombies are
still alive (possible via cheats, `ObjectiveTrigger` time-sets, or future
balance changes), the monitor flips `isNightStarted = false` and calls
`EndNight()` — which immediately returns because `zombiesAlive > 0`. Coroutines
keep running: `SpawnWaveZombies` continues spawning into the day, and the
surviving zombies' `HandleZombieDeath` subscriptions decrement the *next*
night's counters, letting `ZombieDefence` complete prematurely.

**Fix:** Make `EndNight` unconditional about teardown. Remove the early
return; always stop both coroutines and clear wave state. Decide the surviving
zombies policy explicitly — simplest consistent rule: leave them alive in the
world but disconnect them from wave accounting (their handler is a lambda/named
method that checks a night-generation counter: increment `int nightGeneration`
in `StartNight`, capture it in the death subscription, and ignore deaths from a
stale generation). If O9's per-wave closure is implemented, add the generation
check there.

**Accept:** Start a night, use GameTesting to set time to mid-day with zombies
alive → no further spawns occur, and the next night's "Zombies Remaining"
count is exact from start to finish.

## O11 (P3) Firestick progress: one live system, one dead duplicate, no reset

**Where:** live: `ObjectiveManager.cs:47–52,266–282` (called from
`FirestickInteraction.cs:108`). Dead: `FirstTimeInteractionTracker.cs:155–165`
(`IncrementSwampFiresticks`/`IncrementGrasslandsFiresticks` — zero callers) and
its firestick fields/completion block (lines 13–16, 189–199).

**Fix:**
1. Delete the tracker's firestick methods, its firestick serialized fields, and
   the two firestick clauses in `CheckAndCompleteObjectives` (scene will drop
   the stored ints — that's fine, they're runtime counters).
2. In `ObjectiveManager`, reset progress when the objective (re)activates: in
   `AddObjective`, after the objective is tracked, add
   `if (firestickProgress.ContainsKey(objectiveId)) firestickProgress[objectiveId] = 0;`.
3. Optional (nice-to-have, P4): replace the two hardcoded dicts with a
   `requiredCount` int on `ObjectiveData` (default 1) and a generic
   `IncrementObjectiveProgress(string id)`; `Fish`'s `fishRequirements`
   (Fish.cs:11–17) is the same pattern and can ride the same mechanism. Only do
   this if you also update the two `ObjectiveData` assets' inspector values.

**Accept:** Light 3 swamp firesticks → `LightFiresticks` completes exactly once;
re-adding the objective (day skip) starts its count at 0 again.

## O12 (P3) Target registration is per-instance, unregistration is whole-list

**Where:** `WorldSpaceObjectiveManager.cs:143–150` vs `184–190`;
callers `ObjectiveTargetLocator.cs:68–78`, `ObjectiveTarget.cs:64–70`.

**Defect:** `RegisterTarget` appends one Transform to a list;
`UnregisterTarget` removes the *entire list*. With multi-target objectives
(firesticks: several locators share one `targetId`), disabling or destroying a
single locator wipes every target's registration — a later `AddObjective` for
that id finds no targets and creates no markers.

**Fix:** Change signature to `UnregisterTarget(string targetId, Transform target)`;
remove just that transform (`list.Remove(target)`, delete the key when the list
empties). Update both callers to pass `transform`. In `GetTargetLocations`,
prune destroyed entries (`targets.RemoveAll(t => t == null)`) before returning.

**Accept:** With two firestick locators for one objective, disable one in the
inspector, then activate the objective → the other firestick still gets a
marker.

## O13 (P3) `ObjectiveTarget` vs `ObjectiveTargetLocator` — merge the duplicates

**Where:** `Assets/Scripts/Objectives/ObjectiveTarget.cs` (used by `Zombie.cs:38`)
and `Assets/Scripts/Objectives/ObjectiveTargetLocator.cs` (scene-placed; used by
`ObjectiveManager.CompleteObjective:184`).

**Defect:** Two components do the same registration job with different cleanup
behavior (`Locator` also removes markers on disable; `Target` doesn't). Zombie
uses `Target` *plus* its own singular marker call, duplicating registration.

**Fix (after O3 and O12 land):** Fold `ObjectiveTarget`'s used surface (the
`ObjectiveId` setter for dynamic assignment) into `ObjectiveTargetLocator`
(add a public `SetTargetId(string)` that registers), switch `Zombie.cs` to it,
then delete `ObjectiveTarget` — **but first** grep the scene for
`ObjectiveTarget` component usages (`m_Script` GUID of ObjectiveTarget.cs.meta)
and migrate any scene-placed instances to `ObjectiveTargetLocator` in the
editor. If scene migration is not practical this round, do the code merge and
leave `ObjectiveTarget` as a thin subclass marked `[System.Obsolete]`.

**Accept:** Zombies still show compass icons while alive (and none after death,
per O3); scene-placed targets still produce markers.

## O14 (P4) Objectives dead code & micro-efficiency

Safe deletions (verified: no C# callers, no scene/prefab UnityEvent wiring):

| Item | Where |
|---|---|
| `SetMarkerPulse` / `SetMarkerGlow` | `WorldSpaceObjectiveManager.cs:127–141` |
| `CheckObjectives()` | `DailyObjectiveManager.cs:394–408` |
| `ZOMBIE_DEFENSE_ANNOUNCE_TIME` const | `DailyObjectiveManager.cs:36` |
| `SetZombieDefenseActive()` | `WaveManager.cs:736–740` |
| Empty `HandleNightStateChanged` + its subscribe/unsubscribe | `WaveManager.cs:214,256–266,272,280` |
| `autoStartWaves` (always true, hidden) — inline as `true` | `WaveManager.cs:139,292` |
| Unused serialized fields: `waveCompletedAnimator`, `waveCompletedBoolName`, `waveCompletedDisplayTime`, `animatedUIElement`, `delayTime`, `waveCompletionUI` (+ the `#pragma warning disable 0414`) | `WaveManager.cs:50,155–172` |
| `ResetAllTracking()` — keep only if you want a debug reset; otherwise delete | `FirstTimeInteractionTracker.cs:235–255` |
| `maxViewDistance` — either implement distance culling in `Update` (`SetActive(dist < maxViewDistance)`) or delete the field + pragma | `WorldSpaceObjectiveMarker.cs:44–46` |

Micro-efficiency (do in the same pass):
- `ObjectiveManager.HasObjective`/`GetObjectiveData` (`ObjectiveManager.cs:246–254`)
  do linear `List.Find` per call; build a `Dictionary<string, ObjectiveData>` in
  `Awake` (skip null list entries defensively) and use it for both.
- `ObjectiveRadar.LateUpdate` (`ObjectiveRadar.cs:66`) allocates a `List<string>`
  every frame; hoist it to a reused field (`removeBuffer.Clear()`).
- `ObjectiveManager.CompleteObjective:184` does a scene-wide
  `FindObjectsByType` + LINQ per completion — acceptable frequency, but if O12's
  registry lands, resolve locators from `targetLocations` instead.

---

# Part B — Project-wide findings

## W1 (P1) DayNightCycle permanently dirties shared material assets

**Where:** `DayNightCycle.cs:187–190` (reads), `339–352` (writes to
`skydomeMaterial`/`cloudMaterial`).

**Defect:** `Material.SetColor` on a serialized material reference edits the
**asset**. Every play session rewrites
`…/Misc/Skydome_Day.mat` and `…/Misc/PolygonNature_Clouds.mat` (they show as
modified in `git status` right now — do not commit those diffs; revert them
when this fix lands).

**Fix:** In `Awake`, clone both materials and use the clones:
```csharp
if (skydomeMaterial != null) skydomeMaterial = new Material(skydomeMaterial);
if (cloudMaterial   != null) cloudMaterial   = new Material(cloudMaterial);
```
The clone must also be *assigned to the renderers that use it* — find the
skydome/cloud renderers (they are on the sky dome objects in the scene; add two
`[SerializeField] Renderer` fields, wire them in the inspector, and set
`renderer.sharedMaterial = clonedMaterial` in `Awake`; log a warning if unwired).
Destroy the clones in `OnDestroy`. Afterwards run the game and confirm
`git status` shows both `.mat` files clean; then `git checkout --` the current
stale diffs.

**Accept:** Enter and exit play mode → `git status` shows no modified `.mat`
files; sky/cloud colors still animate through the cycle.

## W2 (P2) DayNightCycle's visual "interpolation" is a no-op step

**Where:** `DayNightCycle.cs:324–328`.

**Defect:** `t = (Time.time - lastUpdateTime) / UPDATE_INTERVAL` is computed
only once `Time.time >= lastUpdateTime + UPDATE_INTERVAL`, so `t ≥ 1` always;
after `Clamp01`, every `Color.Lerp(current, target, 1f)` is a snap. The code's
smoothing is illusory — visuals step at 10 Hz (the first audit's "no-op
smoothing" note).

**Fix:** Pick one honest behavior:
- *Simple:* drop the fake lerp — assign targets directly (targets are already
  smooth functions of `timeOfDay`); keep the 10 Hz interval.
- *Smooth (recommended):* keep per-interval target computation but apply an
  exponential approach each frame in `Update`:
  `current = Color.Lerp(current, target, 1f - Mathf.Exp(-k * Time.deltaTime))`
  with `k ≈ 8`. Move only the *target* computation behind the interval gate.

Also (same file, same pass): cache the shader property IDs
(`_ColorTop`, `_ColorBottom`, `_Color`, `_EmissionColor`) as
`static readonly int` via `Shader.PropertyToID`.

**Accept:** Watch a dawn transition at 60 fps — no visible 10 Hz stepping in
sky color; no per-frame string-based `SetColor` calls remain.

## W3 (P2) DayNightCycle dead weight & misleading API

**Where / fixes** (single cleanup pass, mind ground-rule 1 for serialized fields):
- `shouldPauseTime` (`DayNightCycle.cs:156,271`) — never set anywhere; delete
  both the field and the check.
- The time-critical duplicate: `timeCriticalObjectives`,
  `m_CurrentTimeCriticalObjective`, `UpdateCurrentTimeCriticalObjective()`
  (`DayNightCycle.cs:149–151,248,253–267`) — computed once, never read;
  the real system lives in `DailyObjectiveManager`. Delete all three.
- `objectiveManager` / `zombieManager` fields (`DayNightCycle.cs:146–147,238–246`)
  — assigned, never used. Delete fields + the `Find` fallbacks.
- `IsNight()` (`DayNightCycle.cs:737–749`) mutates `lastNightState` inside a
  getter and duplicates the `isNighttime` property — reduce to
  `return isNighttime;` and delete `lastNightState`.
- `dayLength`/`nightLength` (`DayNightCycle.cs:39–40`) are private constants in
  disguise — either `[SerializeField]` them intentionally or make them `const`.

**Accept:** Project compiles; day/night behavior unchanged in a full cycle;
no inspector errors on the DayNightCycle object (removed fields simply vanish).

## W4 (P2) EventManager: duplicate methods, per-call scene searches, unguarded singletons

**Where:** `Assets/Scripts/UI/EventManager.cs:53–64` vs `85–96` (identical
`PerformAction`/`TriggerAction`), `FindFirstObjectByType<ActionHandler>` in
both, bare `UIManager.Instance.…` at 69, 81, 101.

**Fix:** Keep `PerformAction`, delete `TriggerAction` after grepping scene +
code for callers (code grep shows none for either besides UI wiring — check
`m_MethodName: TriggerAction` / `PerformAction` in the .unity file; neither
appears in the current wiring list, but re-verify). Cache the `ActionHandler`
in `Start` with a lazy re-find fallback. Null-guard `UIManager.Instance` (log
a warning instead of NRE when the UI scene object is missing).

**Accept:** Electrical box, mixing desk, light switches still work; no NRE
when UIManager is temporarily disabled.

## W5 (P2) Occlusion debug rays always on

**Where:** `Assets/Scripts/Audio Systems/AudioOcclusion.cs:112`,
`Assets/Scripts/Audio Systems/DirectionalAudioSource.cs:193`.

**Fix:** Add `[SerializeField] private bool debugDrawRay = false;` to each and
wrap the `Debug.DrawLine` call (`if (debugDrawRay) …`). Editor-only cost, but
it's per-source-per-frame noise and pollutes the scene view during sound
design work — which is this project's whole point.

**Accept:** Rays gone by default; tick the box on one source to see its ray.

## W6 (P3) String-keyed audio profiles (deferred, behavior-affecting)

Carried over from the first audit's STATUS block: every audio manager keys
profiles by free-text strings ("grass", enemy names, area names) while enums
exist. **Do not attempt this without in-editor time**: the inspector-entered
strings live in the scene, and a mismatched rename silently kills a sound. If
tackled: add enum fields alongside strings, migrate via a one-off editor script
that maps existing strings → enums and logs unmapped ones, then remove string
lookups. Otherwise leave as-is; the surface-name contract for footsteps is
documented in TERRAIN.md ("grass", "sand", "rock", "snow", "swamp", "wood",
"water", "default").

## W7 (P4) Small robustness/cleanup items

| Item | Where | Fix |
|---|---|---|
| `Fish.isFirstFish` is per-instance but means "first ever" | `Fish.cs:7,42–46` | Delete the field; the tracker's `hasFirstFishBeenCaught` already dedups — call `OnFirstFishCaught()` unconditionally. |
| `FirestickInteraction` hardcodes `KeyCode.E` while `inputKey` is a display string | `FirestickInteraction.cs:12,60` | Either derive the display from a serialized `KeyCode interactKey = KeyCode.E` used in both places, or leave and delete nothing — just make them agree. |
| Objective ID strings duplicated across `ObjectiveConstants`, tracker serialized fields, `ObjectiveManager` firestick dict, `Fish` dict, `FirestickInteraction.Awake` | various | When touching these files for the fixes above, route every literal through `ObjectiveConstants` (add the missing ids: `FixStudioPower`, `TurnOnLights`, `TestMixingDesk`, `LightFiresticks`, `LightFiresticksGrasslands`, `PickupAxe`, `LightCampfire`, `CatchTwoFishes`…`CatchFiveFishes`). Keep the tracker's serialized overrides working by defaulting them to the constants. |
| `WaveManager.UpdateUI` rebuilds four strings on every spawn/death | `WaveManager.cs:716–729` | Fine at current scale; only touch if already editing (cache last values, skip unchanged). |
| `MusicZoneManager` uses `StopAllCoroutines` on enter/exit | `Area Management/MusicZoneManager.cs:38,48` | Self-contained component, currently safe; replace with a tracked fade coroutine only if the class grows another coroutine. |

---

# Suggested implementation order

| Phase | Items | Theme | Risk |
|---|---|---|---|
| 2.1 | O1, O2, O4, O6 | ObjectiveManager correctness (one file cluster, interlocking edits — do together) | Low |
| 2.2 | O3, O12, O13 | Marker/target lifecycle | Low-medium (scene check for O13) |
| 2.3 | O5, O8 | Time & day arbitration | Medium — test a full in-game day |
| 2.4 | O9, O10 | WaveManager wave accounting | Medium — needs a configured test night |
| 2.5 | W1, W2, W3 | DayNightCycle hygiene | Low (W1 needs 2 inspector wirings) |
| 2.6 | O11, O14, W4, W5, W7 | Dead code, duplication, polish | Low |
| — | W6 | Enum keys — only with dedicated in-editor verification time | High |

Each phase: implement → enter play mode in `Level for Sound Design` → run the
relevant acceptance checks → commit with a message naming the phase (e.g.
"Objective completion single-path + UI ownership (Plan2 2.1)").
