# Improvement Plan — 26T1 AUD216 Project 1

> Prioritized, phased plan derived from [AUDIT_FINDINGS.md](AUDIT_FINDINGS.md)
> (H-numbers below refer to that doc). Each phase is independently shippable;
> test in the `Level for Sound Design` scene after each one.

> **STATUS (2026-07-06): Phases 0–5 executed** — see git log for the per-phase
> commits. Notable deviations from the plan below, discovered during execution:
> `SyncAudioSources.cs` was live in the main scene (rescued to
> `Audio/MusicController.cs`, not deleted); `ObjectiveTarget`, `Core/ObjectPool`
> and `DayNightCycle.SmoothTransition` are live (kept); the crossfade base-class
> extraction and asmdefs were deliberately skipped (serialization/assembly-
> reference risk outweighs benefit for this project); dev tools are gated by a
> runtime editor/dev-build check rather than `#if` so scene references survive
> release builds.
>
> **UPDATE (2026-08-18):** the "remaining known issues" this block used to list are
> now resolved by [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md) — `DayNightCycle`'s
> no-op interval "smoothing" (W2), its runtime mutation of the shared sky/cloud
> materials (W1), and the per-frame `Debug.DrawLine` in the occlusion components
> (W5). The one item still deliberately untouched is the **enum-over-string profile
> keys** (W6): behaviour-affecting, and the string values live in the scene, so it
> needs dedicated in-editor verification time.

## Phase 0 — Safety net & hygiene (do first, ~1 session)

Nothing else should happen before the project is under version control.

1. **Init git + `.gitignore`.** Standard Unity ignore set (`Library/`, `Temp/`, `Logs/`,
   `obj/`, `bin/`, `UserSettings/`, `*.sln`, `*.csproj`, `Recordings/`, `.DS_Store`, `._*`).
   Commit the clean baseline.
2. **Delete the 13,858 `._*` AppleDouble files** (`find Assets -name "._*" -delete`,
   `.DS_Store`s). Also delete the 18 stale root `.sln`
   files — Unity regenerates the current one.
   > ⚠ **Correction (2026-08-17):** this item originally also listed
   > `_TerrainAutoUpgrade` for deletion. That was wrong — those folders held the
   > live `.terrainlayer` assets for every terrain, and deleting them destroyed
   > the terrain textures. They were recovered to
   > `…/Nature/Scenes/Terrains/TerrainLayers/`; see
   > [TERRAIN.md](TERRAIN.md) before deleting anything that looks generated.
3. **Quarantine, don't fix, legacy content:** `Assets/_Recovery/`, committed
   `Assets/Exports/*.json`, empty `Assets/Scripts/DayNightCycle/Editor/`.
4. **Relocate strays:** move `Assets/TriggerSound.cs` →
   `Assets/Scripts/Audio Systems/RandomTriggerAudioVariations.cs` (rename file to match
   the class; the `.meta` GUID keeps scene references intact as long as the `.meta`
   moves with it — do the move inside Unity or move both files together).

**Exit criteria:** clean `git status`, project opens and plays identically.

## Phase 1 — Audible bugs (highest value for a sound-design project)

These change what you *hear*; fix and A/B test each one.

1. **Weapon pitch randomization is a no-op (H3, H8).** Don't reset `pitch = 1f`
   immediately after `PlayOneShot`. Either leave the pitch set (it's re-randomized next
   shot anyway) or play through a dedicated/pooled source per shot. Same fix in
   `WeaponAudioManager` and `BulletImpactManager`.
2. **Music playlist scheduler (H1, H2).** Add a `bool nextClipScheduled` per track,
   set when `PlayScheduled` is issued, cleared when the clip actually changes; guard
   `track.source.clip != null`.
3. **Consumption SFX double-fire (H4, H5).** Make `PlayerStats.AddHealth` silent
   (callers own their SFX), or pass a `playSound: false` flag from `MedicineItem`/`Fish`.
   Result: medicine plays once; fish plays only the fish sound.
4. **`PlayRandomSound` delay bug (H6).** Delete the `Play()` after `PlayDelayed()`, add
   empty-list guard and `[RequireComponent(typeof(AudioSource))]`.
5. **Enemy audio request drops (H24).** Only remove served requests instead of
   `pendingRequests.Clear()`; add an early-out when the list is empty (also fixes the
   per-frame sort).
6. **Decide the fate of `AmbienceMusicManager` (H7).** Recommended: delete it (legacy,
   competing with `AmbienceManager`+`MusicManager`); fixing the Update-vs-coroutine
   fight is wasted effort otherwise.

## Phase 2 — Crash-proofing (null guards & logic bugs)

Mechanical, low-risk edits; batch them in a few commits.

1. **Guard the Player lookups (H9, H10, H12):** cache
   `GameObject.FindGameObjectWithTag("Player")` once, null-check, log a clear error.
   Applies to `FoodItem`, `MedicineItem`, `Fish`, `EnemyAudioManager`, `AudioOcclusion`.
2. **Guard optional references (H11, H13–H18, H20–H23):** clip arrays, `animator`,
   `enemyAudioManager`, `EventManager.Instance`, `ArrowPool.Instance`, `reportUI`,
   `debugPanel`, `dayNightCycle`, `WorldSpaceObjectiveManager.Instance`. Pattern:
   null-check once in `Awake`/`Start`, disable the component with a descriptive
   warning if a hard dependency is missing.
3. **Fix `StopAllCoroutines()` (H25):** track the specific coroutines
   (`Coroutine slideRoutine`, etc.) in `FirstPersonController` and stop only those.
4. **Crossbow draw state (H19):** reset `isDrawing` on mouse-up.
5. **Equip pipeline (H26):** have `SelectInventorySlot` call
   `Inventory.SetSelectedItem`; then delete `FishingSpot` (superseded by `FishingZone`).
6. **`DailyObjectiveManager` duplicate block (H27):** delete lines 281-292 (or fix to
   actually iterate `nightObjectives` if night-only activation was intended — check
   design intent first).
7. **Singleton `return` after `Destroy(gameObject)`** in `WorldSpaceObjectiveManager`,
   `DebugDisplay`, `ReticleController`, `EventManager`.
8. **`AudioOcclusion` layer mask (MEDIUM):** don't overwrite the inspector mask; if
   auto-detecting, guard `NameToLayer("Occluder") == -1`. Restore authored volume
   instead of hardcoded `1f`.

## Phase 3 — Dead code removal

Before deleting any MonoBehaviour, grep scene/prefab YAML for its script GUID
(`grep -rl <guid> Assets --include="*.unity" --include="*.prefab"`) — "no code
references" doesn't prove "not scene-wired".

Delete (per the inventory in AUDIT_FINDINGS.md): `FilterManager`, `TerrainChecker`,
`SoundClip`, `UISounds` nested class, `MusicController2`, `SyncAudioSources` +
`StopMusicTrigger` (the whole "Possibly Unused Scripts" folder), `AnimatedObjectiveUI`
+ `UIPoolManager` (+ `Core/ObjectPool` if `ArrowPool` is migrated or kept as the sole
user — check first), `ObjectiveMarker`, `ObjectiveTarget`, `FishingSpot`, one of the
two slider attributes, dead methods in `DayNightCycle` / `DailyObjectiveManager` /
`MedicineItem` / `ActionHandler` / `HUDManager` / `InteractionAudioManager`, unused
serialized fields, and the commented-out log noise. Expect roughly **15–20 fewer
files** and materially less confusion about which system is "live".

## Phase 4 — Consolidation & architecture (the real quality win)

1. **One random-playback utility.** A single static helper +
   one serializable type replaces ~9 copies:
   ```csharp
   [Serializable] public class RandomizedClipSet {
       public AudioClip[] clips;
       public Vector2 volumeRange = new(0.8f, 1f);
       public Vector2 pitchRange  = new(0.95f, 1.05f);
   }
   public static class AudioUtil {
       // no-repeat pick + pitch/volume variation on a dedicated source
       public static void PlayRandom(AudioSource src, RandomizedClipSet set, ref int lastIndex) { … }
   }
   ```
   Migrate managers one at a time (Footstep → Weapon → Player → Enemy → Interaction →
   the small trigger components). This also fixes the pitch-on-shared-source class of
   bug in exactly one place, and gives footsteps the pitch variation they're currently
   missing.
2. **One crossfade-by-state base.** `AmbienceManager` and `MusicManager` share
   near-identical init/crossfade code — extract a common `CrossfadeAudioBank` base or
   composition helper.
3. **Enums / constants over strings.** Route surface types, weapon names, enemy names,
   interaction names through the enums that already exist (`WeaponType`, `EnemyType`,
   `InteractionType`) or a central constants class; kill `weaponName.Contains("SMG")`
   by adding a `WeaponClass` enum field. Extract the duplicated tag→surface switch in
   `SurfaceDetector` into one method using `CompareTag`.
4. **Event-driven instead of per-frame polling:** `FirstTimeInteractionTracker`
   (subscribe to `ActionHandler.onPowerToggled`), `ZombieManager`→radar
   (register/unregister events instead of `EnemyRadar`'s tag scan + whole-scene
   rescan, H28), `DailyObjectiveManager` (hook `OnDayChanged`/objective-completed
   events), `UIManager` prompt positioning only while visible (H29).
5. **Gate the dev tools:** wrap `GameTesting`, `StatsHelper`, `Teleporter`,
   `DebugDisplay`, `ToggleGameObjects`, the `P` damage key in
   `#if UNITY_EDITOR || DEVELOPMENT_BUILD`; replace the private-field reflection in
   `GameTesting`/`StatsHelper` with `internal` accessors or public read-only APIs
   (fixes the already-broken `m_WeaponProfiles` reflection).
6. **Singleton & lifetime cleanup:** pick one pattern; align DontDestroyOnLoad usage
   (either everything session-persistent or everything per-scene — for a single-scene
   project, per-scene is simpler); one Escape-key owner (`PauseMenu`), one quit path,
   one volume path (`SFXVolumeControl` param naming vs `SettingsMenu`).

## Phase 5 — Polish & tooling (optional, as time allows)

- **Editor fixes:** wrap the stale foldout arrays with a length re-check in
  `OnInspectorGUI` (copy the pattern from `FootstepAudioManagerEditor`); add
  `Undo.RecordObject` in `AudioOcclusionEditor` / `MeleeWeaponControllerEditor`; point
  the exporters at a folder outside `Assets/` and use `JsonUtility`/`EditorJsonUtility`
  instead of hand-rolled StringBuilder JSON; delete redundant `UIAudioManagerEditor`.
- **Naming pass:** rename `compass` → `CompassUI`, make filenames match classes
  (`InteractionSystemTMP`), pick one field convention (`[SerializeField] private`,
  no `m_`), one namespace scheme (either none or `Game.*` everywhere — note renaming
  classes breaks scene references only if the file GUID changes; renaming the class
  *and* file together inside Unity is safe).
- **`DayNightCycle`:** either implement real interpolation (lerp toward target each
  frame, refresh targets on the interval) or drop the pretend-smoothing; use
  `MaterialPropertyBlock`/instanced materials instead of mutating shared assets.
- **Asmdefs:** `Game.Runtime` + `Game.Editor` to cut recompile times and stop editor
  code leaking into builds.
- **`ToggleEmission` material leak:** cache instances like `ToggleMeshRenderers` does.
- **Magic-number sweep:** hoist tuning literals (heartbeat mapping, radar constants,
  spawn time 0.868f, arrow damage) into serialized fields or named constants.

## Suggested improvements beyond fixes (sound-design value-adds)

These build on the cleanup and directly improve the audio deliverable:

1. **Pooled one-shot audio player.** A small `AudioOneShotPool` (N pre-created
   sources parented to a manager) so every one-shot gets its own source: correct pitch
   variation, correct 3D position for impacts, no cut-offs. `BulletImpactManager`,
   `AudioRandomizer`, and the weapon managers all become clients.
2. **Footstep pitch/volume variation** via the shared `RandomizedClipSet` — currently
   footsteps are the only major system with zero variation (very audible repetition).
3. **Wire up or remove the occlusion arbiter.** `AudioOcclusion` and
   `DirectionalAudioSource` both fight over the same `AudioLowPassFilter` if ever
   combined; `FilterManager` was meant to arbitrate. Either route both through it
   (min-of-cutoffs wins) or delete it and document that the two components are
   mutually exclusive per source.
4. **Mixer snapshots for game states.** The mixer already has a snapshot; drive
   Exploring/Fighting/Inside/Paused via `TransitionToSnapshots` instead of (or on top
   of) per-source volume lerps — cheaper and easier to mix.
5. **Surface data as ScriptableObjects.** Replace string-keyed
   `SurfaceAudioProfile[]` with a `SurfaceType` enum + per-surface ScriptableObject
   (clips, volume, pitch range) shared by footsteps *and* bullet impacts — one place
   to author per-surface sound.
6. **A `DebugAudio` toggle** that gates the per-frame `Debug.DrawLine` calls in the
   occlusion components and surfaces current mixer values — useful for the assignment
   write-up and profiling.

## Suggested order of work

| Phase | Effort | Risk | Payoff |
|---|---|---|---|
| 0 Hygiene + git | ~1 hr | none | safety net, sane project |
| 1 Audible bugs | ~2-3 hrs | low | the mix actually behaves as designed |
| 2 Crash-proofing | ~2-3 hrs | low | no NRE spam, stable play sessions |
| 3 Dead code | ~2 hrs | low (with GUID grep) | ~15-20 fewer files, clarity |
| 4 Consolidation | ~1-2 days | medium | one audio utility, event-driven flow |
| 5 Polish | as available | low-medium | editor QoL, naming, asmdefs |

Phases 0–2 are safe to do immediately. Phase 3+ should be one commit per deletion or
refactor so anything scene-breaking is trivially revertible.
