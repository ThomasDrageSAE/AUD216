# Performance Fixes — Work Order (parallel agents)

> Written 2026-09-04 at commit `365aca2`. **This is a work order for an implementing AI
> (Opus) running several agents in parallel.** It carries out the six recommendations the
> owner approved from [PERFORMANCE_FINDINGS.md](PERFORMANCE_FINDINGS.md), phase 3 of
> [PERFORMANCE_PROFILING_TASKS.md](PERFORMANCE_PROFILING_TASKS.md). Every path, line number,
> field name and value below was verified against the repo and the live editor at `365aca2`;
> line numbers drift — re-grep before editing.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (single-authority rules, the Unity CLI
> paragraph), the ground rules in [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md),
> [PROMPT_unity-profiling-via-cli.md](PROMPT_unity-profiling-via-cli.md) (every bridge and
> profiler gotcha — you will hit them), and the STATUS blocks of the two profiling documents
> above, which record how the last two parallel-agent orders actually ran. Items marked ⏸
> are owner checkpoints. Questions use the multichoice format from
> [README.md](README.md#asking-the-owner-questions).

## The owner's decisions, already made

Do not re-ask these.

| Decision | Answer |
|---|---|
| Target machine and budget | **The owner's desktop, 60 fps.** CPU submission is the lever; fill-rate findings are out of scope. |
| Order of work | **Fix the exception first, then re-capture**, then the rest, each re-measured. |
| Fixes approved | All six below. |
| Look changes approved | **Bloom resolved to 0.4 in the grade profile** (the `FirstPersonCharacter` override is deleted); **FXAA on the main camera**. The stray second directional light is **not** to be touched. |
| Singleton sweep scope | **All game code; skip `Assets/Scripts/Testing/`** (dev-only harness, 24 of the 55 sites). |
| Terrain | **Invisible changes only**: instancing on, two-sided shadows off. Detail distance stays 80. |

## Read this before you believe the numbers

**On the stated target the level already passes.** Live combat measured 113 fps against a
60 fps budget, and the configured 40-zombie wave extrapolates to roughly 80. Nothing here
rescues a frame rate. This order removes waste, fixes a latent crash, and makes an assessed
look deterministic. Write the report in those terms.

**The top finding's per-frame cost did not reproduce.** The findings document ranked a
caught `NullReferenceException` as 3.4 % of the main thread, firing every frame. When the
integrator went back to the editor: **zero exceptions across 884 frames** with the title
dismissed. What *is* verified is the stack trace (`DailyObjectiveManager.
AreTimeCriticalObjectivesComplete`, line 191, dereferencing `ObjectiveManager.Instance`)
and that it fires at startup in some sessions. The guard is a correctness fix. Do not claim
a frame-time saving for it unless you measure one. Two downstream findings rested on the
per-frame claim — "the day/night visuals never run" and "11,430 particles are never stopped"
— and are therefore **unverified**: Agent A's first job is to find out.

**Three of the four fix groups are settings, not code.** They go through `[MenuItem]` tools
the integrator runs; the owner saves the scene.

## Invariants — every phase, every agent

- **Audio behaviour is unchanged.** No file under `Assets/Scripts/Audio/`,
  `Assets/Scripts/Audio Systems/` or `Assets/Scripts/Assessment/` is edited. Before the
  first change and after the last, the integrator runs
  `AudioProgressScanner.BuildExportText(AudioProgressScanner.Scan())` through `eval` and
  diffs the two exports: **identical, every row**. `git diff --stat` at the end shows nothing
  under those folders.
- **No serialized field is renamed or removed.** Adding one is fine. Grep `.unity` and
  `.prefab` before touching any `[SerializeField]`.
- **Only the integrator drives the editor.** Agents A, B and C never run a `unity` command.
- **Scene and asset edits are menu items**, Undo-recorded where Unity allows it, idempotent,
  logging before/after values and `SAVE THE SCENE` when the scene is touched. The owner
  saves. Asset edits (`.asset` files) save themselves via `AssetDatabase.SaveAssets` and are
  **not undoable** — the tool must say so in its log; git is the way back.
- `bash Documentation/tools/compile-check.sh` after every edit, both assemblies ✅. Then
  `recompile` + poll `recompile_status` before the editor is used.
- ⚠ **CRLF**: edited `.cs` files are CRLF in the working copy. Edit byte-wise. New files
  are written CRLF. Check `file <path>`.
- ⚠ **Harness**: the shell mangles C# `$"…"` and `'{…}'` even in quoted heredocs. Write
  code and edit scripts with the Write tool, run with `eval_file --file`.
- Always `--project-path D:/GitHub/AUD216_Project1_Nature_Level` — two editors were running
  and the ports swapped between sessions.
- One commit per fix group, repo message style, the harness's attribution trailer. Push only
  when the owner says.

## Verified anchors

| Fix | Anchor | Value at `365aca2` |
|---|---|---|
| 1 | The verified stack trace | `DailyObjectiveManager.AreTimeCriticalObjectivesComplete () (at Assets/Scripts/Objectives/DailyObjectiveManager.cs:191)` ← `UpdateTimePause` (:265) ← `Update` (:218). Line 191: `if (!ObjectiveManager.Instance.IsObjectiveCompleted(objectiveId))`. The method guards `m_CurrentTimeCriticalObjective` and its array but not `Instance`. |
| 1 | What is *not* null | Verified live at t≈3 s: `ObjectiveManager.Instance` set, one live instance, `objectivesByDay` assigned, `m_CurrentTimeCriticalObjective` set, `timeCriticalObjectives.Length = 8`. `ObjectiveManager.completedObjectives` is initialised inline (`:42`). So the null is **transient at startup**, not structural. |
| 1 | Unguarded `ObjectiveManager.Instance.` sites | **55 total, 2 guarded.** Game code (31): `Objectives/DailyObjectiveManager.cs` :99 :101 :148 :191 :292 :299 :315 :330 :344 :386 :388 (:263 already guarded); `Interactions/FirstTimeInteractionTracker.cs` :79 :88 :97 :101 :105 :109 :113 :117 :169 :175 :181; `Interactions/CampfireInteraction.cs` :87; `Interactions/InteractionSystemTMP.cs` :121; `Inventory/Item.cs` :71; `Items/Fish.cs` :62 :76; `Objectives/ObjectiveTrigger.cs` :80 :82; `Waves/WaveManager.cs` :296 :733 :739 (:472 already guarded). Skipped by decision: `Testing/GameTesting.cs` (24 sites, plus `DailyObjectiveManager.Instance` at :301). |
| 1 | The shape already in use | `DailyObjectiveManager.cs:262–263`: `ObjectiveManager.Instance != null && ObjectiveManager.Instance.IsObjectiveCompleted(...)`; `WaveManager.cs:471–472` the same. Follow it. |
| 1 | The second reported site | `DayNightCycle.UpdateParticleSystems`, `Assets/Scripts/DayNightCycle/DayNightCycle.cs:459`: `for (int i = 0; i < particleStates.Length; i++)`. `particleStates` is assigned unconditionally in `InitializeParticleStates()` (:309), which is the **last statement of `Awake`** (:225). Reached from `UpdateVisuals` behind a 10 Hz gate (:410). **Not seen in the integrator's 884-frame window.** Whether it fires at all now is Agent A's to establish. |
| 2 | Terrains | Two, both active: `Terrain - Main area` (scene line 34878) and `Terrain - Footsteps area` (119931). Both `m_DrawInstanced: 0`, `m_ShadowCastingMode: 2` (TwoSided), `m_DetailObjectDistance: 80`, `m_HeightmapPixelError: 5`. Material resolves to URP's stock `TerrainLit`, so instancing is safe with no shader work. Measured cost: `Terrain.Heightmap.RenderStep1+2+3` **0.275 ms/frame, 4.2 % of the main thread** (S1). |
| 3 | URP asset | `Assets/Level for Sound Design/URP_PipelineAsset_Main.asset`, `m_ShadowDistance: 40`, `m_ShadowCascadeCount: 2`, `m_MainLightShadowmapResolution: 4096`. Shadow casters **717 idle / 972 combat**; `Draw Additional Lights Shadowmap` 0.001 ms everywhere (the studio spots are free); `Draw Main Light Shadowmap` 0.102–0.340 ms. Casters scale with area: (25/40)² ≈ 0.39 → **~280 at idle**. |
| 4 | The two Bloom overrides | `Assets/Level for Sound Design/Post Processing Profile.asset`: Bloom **0.4**, threshold 0.8, scatter 0.7 — beside Vignette 0.318, LiftGammaGain, WhiteBalance, ShadowsMidtonesHighlights, ColorAdjustments (the grade). `Assets/Level for Sound Design/FirstPersonCharacter Profile.asset`: Bloom **1.47**, threshold 0.9 — plus a ChromaticAberration at intensity 0 (not overridden). Both used by a **global** `Volume`, `m_IsGlobal: 1`, `priority: 0`, `weight: 1`: GameObjects `Post Processing` (component &401316778) and `FirstPersonCharacter` (&8460210341775917330). **The mushroom trip is not involved** — `Camera/MushroomTripEffect.cs` builds its own `Volume` and `VolumeProfile` at runtime (`EnsureVolume`, :281). |
| 5 | Camera AA | `UniversalAdditionalCameraData` on the first-person camera, scene line 276397: `m_Antialiasing: 0`, `m_AntialiasingQuality: 2`, `m_RenderPostProcessing: 1`. URP asset `m_MSAA: 1` (off) — leave it. |
| 6 | ZombieAI | `Assets/Scripts/Enemies/ZombieAI.cs`: `agent.SetDestination(playerTransform.position)` at :242 inside the chase branch (`distanceToPlayer <= detectionRange && > attackRange`), guarded only by `agent.isOnNavMesh` (:240); `distanceToPlayer` is already computed in `Update`. Fields: `[SerializeField] float maxHealth = 100f` (:48), `[SerializeField] Canvas worldSpaceCanvas` (:52), `[SerializeField] Slider healthSlider` (:53), `currentHealth` (:125, :476). `LateUpdate` (:611–618) does `worldSpaceCanvas.transform.LookAt(Camera.main.transform); Rotate(0,180,0)` every frame. `Die` disables the canvas (:531–532). `UpdateHealthUI` (:421–426) writes the slider. `BossZombieController` is a separate MonoBehaviour with no `SetDestination` and no `LateUpdate` — **out of scope**. |
| 6 | The cost | Per zombie per frame: world-space canvas **0.034–0.039 ms** (`PlayerUpdateCanvases` 0.2013 → 0.4824 ms for 8), NavMesh crowd 0.041, animator 0.025, script 0.013. `Crowd.Pathfinding` is 35 % of AI cost. Waves configure **40** in one wave (W1 N7) and **60** in one night (W2 N3). |
| — | Prefabs carrying the canvas | 11 under `Assets/Inbox/Prefabs/Enemies/`, each with 1 Canvas + 7 CanvasRenderers + 16 RectTransforms, `Animator` culling mode already `CullUpdateTransforms`. No prefab edit is needed — the change is in the script. |
| — | Keeping the player alive for a capture | `Assets/Scripts/Player/PlayerStats.cs`: `PlayerStats.Instance` (:120), private `float currentHealth` (:82), `[SerializeField] float maxHealth = 100f` (:20), `bool isDead` (:98), `TakeDamage(float)` (:457). Pin `currentHealth` to a large value by reflection on every CLI poll during the window. |
| — | The re-measure kit | `Assets/Scripts/Testing/PerformanceProbe.cs` (`PerformanceProbe.Begin(name, seconds, uncapped)`, `IsDone`, `Stamp`), `Assets/Scripts/Editor/ProfilerCaptureTools.cs` (`Tools/Profiling/Start Profiler Recording`, `Stop Recording And Export`, `Export Asset Inventory`), and `Documentation/tools/profiling_driver.py` (the phase-0 driver, committed as a starting point — hard-coded project path and scenario list). Captures land in the gitignored `Exports/Profiling/<stamp>/`. |

## Agent plan

```
Phase 1  A · B · C in parallel   disjoint files; B in a git worktree so its change lands LAST
Phase 2  Integrator (serial)     A lands → recompile → Capture 1 (the honest baseline)
                                 C's tools applied → ⏸ owner saves → Capture 2 (settings deltas)
                                 B merged → recompile → Capture 3 (zombie deltas)
                                 audio invariants, docs, commits
```

| Who | Owns (writes) | Reads | Editor |
|---|---|---|---|
| **A** — singleton guards | `Objectives/DailyObjectiveManager.cs`, `Interactions/FirstTimeInteractionTracker.cs`, `Interactions/CampfireInteraction.cs`, `Interactions/InteractionSystemTMP.cs`, `Inventory/Item.cs`, `Items/Fish.cs`, `Objectives/ObjectiveTrigger.cs`, `Waves/WaveManager.cs`, `DayNightCycle/DayNightCycle.cs` | `Objectives/ObjectiveManager.cs` (read only — single authority, do not edit) | no |
| **B** — zombie throttle + health bar | `Enemies/ZombieAI.cs` **only**, in a worktree | the enemy prefabs (read only) | no |
| **C** — settings tools | `Assets/Scripts/Editor/PerformanceFixTools.cs` (new, the only file) | the URP asset, both profiles, the scene (read only) | no |
| **Integrator** | this file's STATUS, `PERFORMANCE_FINDINGS.md` (before/after), `CODE_STRUCTURE.md`, the scene via C's tools | everything | **yes — the only one** |

No two agents share a file. A and B both touch enemy-adjacent behaviour but not the same
code: A's `WaveManager.cs` edits are three guard lines around `ObjectiveManager.Instance`;
B never opens `WaveManager.cs`.

---

## Agent A — guard the singleton, and treat the pattern

**Step 1 — measure before editing (through the integrator).** Ask the integrator to run,
once, before any code lands: enter play, take a console cursor immediately, read
`--since` after 120 frames, take another cursor, read again after a further ~600 frames.
Record for each window: distinct stack traces and counts. This settles whether the
exception is startup-only, and whether the `DayNightCycle` one fires at all. Put the
numbers in your report; they replace the findings document's per-frame claim.

**Step 2 — the verified site.** `DailyObjectiveManager.cs:191`. Use the project's own
shape: guard `ObjectiveManager.Instance != null` and, when it is null, return the value
that does not stall the game — `AreTimeCriticalObjectivesComplete` returning `true` on a
missing manager matches its existing "nothing to check → true" branch. No log here: it is
on the `Update` path.

**Step 3 — the sweep, 31 sites.** For each site in the anchor table:

- In an `Update`/`LateUpdate`/`FixedUpdate`/coroutine path: guard and early-return
  silently.
- In an event-driven path (an interaction, a pickup, a trigger, an objective completion):
  guard, early-return, and `Debug.LogWarning` once with the site name — these are rare and
  a missing manager there *is* worth a line in the console.
- Event subscription/unsubscription (`FirstTimeInteractionTracker.cs:79, :88`): guard
  both; an unsubscribe on a null instance is harmless but symmetric.
- Keep each guard on the dereferencing line's own `if`; do not restructure methods.

**Step 4 — `DayNightCycle.cs:459`.** Add `if (particleStates == null) return;` at the top
of `UpdateParticleSystems`. Then, **because `InitializeParticleStates` is the last statement
of `Awake`**, find what could throw before it (`CreateRuntimeMaterialInstances` at :210 does
a scene-wide `FindObjectsByType<Renderer>` and clones two materials; the three
`GetCurrent…` seeds at :221–:223 read serialized colour tables). If Step 1 shows this site
never fires, say so and leave the guard as defence.

**Step 5 — the pattern, in writing.** Add one paragraph to `ObjectiveManager.cs`'s header
comment (comment only — no code change in that file): "`Instance` can be null for the first
frames and after a reload; every caller guards it at the site, the way :262 and
`WaveManager:471` do." That is the rule students copy.

Compile-check. Report: the Step 1 numbers, the 31 sites with the guard shape used at each,
and whether the day/night particle scheduler's behaviour changes once it runs (if it was
throwing, particles will now start/stop by phase — a **visible** change the owner must see,
⏸).

**Not yours:** anything under `Testing/`, `ObjectiveManager.cs` itself, the audio folders.

## Agent B — zombie repath throttle and health-bar gating

Work in a **git worktree** (`isolation: worktree`) so your edit does not reach the main
working tree until the integrator merges it after Capture 2.

In `Enemies/ZombieAI.cs`, and nowhere else:

**Repath throttle.**
- Add `[SerializeField, Tooltip(...)] private float repathInterval = 0.25f;` beside the
  other tuning fields, and a private `float nextRepathTime`.
- In `Start`, seed `nextRepathTime = Time.time + Random.value * repathInterval` so forty
  zombies spawned in one wave never repath on the same frame.
- At :242, replace the unconditional call with: if `Time.time >= nextRepathTime`, set
  `nextRepathTime = Time.time + repathInterval` and call `agent.SetDestination(...)`.
  **Leave `agent.isStopped`, `agent.speed`, the animator bool and the attack/stop distance
  test exactly as they are, per frame.** Only the destination write is throttled; the
  agent still steers along its current corridor every frame and `autoRepath` still re-plans
  on invalidation.
- `repathInterval = 0` must mean "every frame" (the old behaviour) — the integrator uses
  that for the before/after capture. Guard the division accordingly.

**Health-bar gating.**
- Add `[SerializeField] private float healthBarVisibleDistance = 25f;`.
- The rule: the canvas is enabled only when `currentHealth < maxHealth` **and**
  `distanceToPlayer <= healthBarVisibleDistance`. Evaluate it once per `Update` where
  `distanceToPlayer` is already known (it is computed before :232); write
  `worldSpaceCanvas.enabled` only when the value changes (compare-before-write — a Canvas
  toggle is itself a rebuild).
- In `Start`, the canvas starts **disabled** (full health).
- `LateUpdate` billboards only when `worldSpaceCanvas != null && worldSpaceCanvas.enabled`.
- `Die` already disables it (:531); keep that. `UpdateHealthUI` keeps writing the slider —
  a hidden canvas is cheap to update.
- `healthBarVisibleDistance = 0` must mean "never gate by distance" (the old behaviour
  apart from the full-health rule), for the before/after capture.

Zombies never regain health, so once hit within range a bar stays until the zombie leaves
range or dies — say that in your report; it is the visible behaviour the owner will judge
(⏸).

Compile-check in the worktree. Report the diff, the two new fields with their defaults,
and the exact lines changed. **Not yours:** `BossZombieController.cs`, the prefabs,
`EnemyAudioManager` (the vocalisation coroutine is audio — do not touch, do not move).

## Agent C — the settings tools

One new file, `Assets/Scripts/Editor/PerformanceFixTools.cs`, project header-comment style
(see `DistributionResetTools.cs` and `AudioManagerTidyTools.cs` for the house shape),
menu root `Tools/Performance/`. Every item is idempotent, logs the before and after value
of everything it touches, and never asks a dialog (the CLI runs it headless).

1. **`Report Current Settings`** — read-only. Logs: for each `Terrain` in the scene,
   `drawInstanced`, `shadowCastingMode`, `detailObjectDistance`; the URP asset's
   `shadowDistance`, `mainLightShadowmapResolution`; the main camera's
   `UniversalAdditionalCameraData.antialiasing`; for both profiles, whether a `Bloom`
   override exists and its `intensity.value`. The integrator runs this before and after
   everything.
2. **`Apply Terrain Instancing And Shadow Mode`** — scene. For every `Terrain` found with
   `FindObjectsByType<Terrain>(FindObjectsInactive.Include, ...)`: `Undo.RecordObject`,
   then `drawInstanced = true`, `shadowCastingMode = ShadowCastingMode.On`. Do **not**
   touch `detailObjectDistance` (owner's decision). Mark the scene dirty; log
   `SAVE THE SCENE`. Nothing to do → say so.
3. **`Apply Shadow Distance 25`** — asset. `GraphicsSettings.defaultRenderPipeline as
   UniversalRenderPipelineAsset` (null → refuse with a clear log); `Undo.RecordObject`;
   `shadowDistance = 25f`; `EditorUtility.SetDirty`; `AssetDatabase.SaveAssets`. Log that
   asset edits are saved immediately and git is the revert. Do not touch the shadowmap
   resolution or cascades.
4. **`Apply FXAA On Main Camera`** — scene. Find the camera (`Camera.main`, falling back to
   the first `Camera` on a GameObject named `FirstPersonCharacter` — verify the name in the
   scene before relying on it); `GetUniversalAdditionalCameraData()`; `Undo.RecordObject`
   on that component; `antialiasing = AntialiasingMode.FastApproximateAntialiasing`. Leave
   `antialiasingQuality`. Mark dirty, `SAVE THE SCENE`.
5. **`Resolve Duplicate Bloom`** — assets. Load both profiles by their exact paths from the
   anchor table as `VolumeProfile`. Refuse (log, do nothing) if either is missing, or if the
   grade profile's `Bloom` is absent. Ensure the grade profile's `Bloom.intensity` override
   is **0.4** (it already is — log "unchanged" if so). In the `FirstPersonCharacter` profile:
   if a `Bloom` component exists, `profile.Remove<Bloom>()` — this destroys the sub-asset,
   so log loudly that it is **not undoable**; then `EditorUtility.SetDirty` both and
   `AssetDatabase.SaveAssets`. Leave the profile's zero-intensity ChromaticAberration and
   the `Volume` component alone. Idempotent: no `Bloom` in the FPC profile → "nothing to
   do".

All items work with the two editors running (they run inside whichever editor executes
them; the integrator addresses it with `--project-path`). Compile-check. Report the menu
paths and the exact fields each writes.

**Not yours:** running any of it. The integrator does, in order, with captures between.

---

## Integrator — sequence, measurement, invariants

**Before anything:** export the audio report (`eval` → `AudioProgressScanner.BuildExportText(AudioProgressScanner.Scan())`) to `Exports/Profiling/<stamp>/audio_before.txt`. Run Agent A's Step 1 measurement and hand A the numbers.

**Capture protocol** (from `PERFORMANCE_PROFILING_TASKS.md` §0.2, corrected by its STATUS):
title dismissed by reflection, one round trip, then **≥45 s of settle time**, then the
window. Read `stats_*_end.json` → `timeScale` must be `1` and `PlayerStats.isDead` false
at both ends, or the capture is discarded. Screenshot every window and look at it. Three
scenarios each time: **S1** idle at spawn, **S8** the tree view (facing the waterfall from
45 m), **S6** combat with the player pinned alive — 8 zombies instantiated directly from
the wave prefabs (the phase-0 `drive3.py` method: `WaveManager.StartNight` spawns nothing
from a cold start), and a second run at **40**. Uncapped only; "capped" is not capped in
the editor.

1. **A lands.** Compile-check, `recompile`, poll. **Capture 1** — the honest baseline, with
   the guards in. This replaces `20260904_1349` as the reference; note in its README that
   the exception guard is the only code difference.
2. **C's tools land.** Compile-check, `recompile`. Run `Report Current Settings`, then the
   four apply items in order 2 → 3 → 4 → 5, then `Report` again; keep both logs. Take a
   screenshot at the S1 spot before and after items 4 and 5 for the owner (FXAA and Bloom
   are look changes). **⏸ Owner saves the scene** (terrain + camera are scene edits; shadow
   distance and Bloom are already saved as assets). Verify the saved `.unity` carries
   `m_DrawInstanced: 1` on both terrains and `m_Antialiasing: 1` on the camera. **Capture 2.**
   Report per scenario: batches, SetPass, shadow casters, `Terrain.Heightmap.RenderStep*`
   ms, main-thread ms, GPU ms — before/after. Expect casters ~717 → ~280 at S1 and the
   terrain block to fall by ~0.25 ms; expect Bloom/FXAA to be invisible in the CPU numbers.
3. **B merges.** `git merge` the worktree branch (one file; conflicts are impossible if A
   stayed out of `ZombieAI.cs`). Compile-check, `recompile`. **Capture 3**, S6 at 8 and 40
   only, twice each: with `repathInterval = 0` and `healthBarVisibleDistance = 0` set by
   reflection on every spawned zombie (the old behaviour), and with the defaults. Report
   `Crowd.Pathfinding`, `PostLateUpdate.PlayerUpdateCanvases`, `UGUI.Rendering.UpdateBatches`
   and main-thread ms, per count. Expect the canvas marker to fall to near its no-zombie
   baseline (~0.20 ms) and pathfinding to fall by most of its 0.10 ms at 8.
4. **Audio invariants.** Export the report again → `audio_after.txt`; `diff` must be empty.
   `git diff --stat 365aca2..HEAD` shows nothing under `Audio*/` or `Assessment/`.
5. **Docs.** `PERFORMANCE_FINDINGS.md`: a "Phase 3 results" section with the three
   before/after tables and, for each of the twelve findings, *held / did not hold / not
   tested*. `CODE_STRUCTURE.md`: work-history rows (one per fix group), an Editor-tooling
   bullet for `PerformanceFixTools`, the Enemies row updated with the two new fields, and
   the singleton-guard rule under single-authority. This file's STATUS.
6. **Commits**, in this order, each with the measured number in the message: A's guards;
   C's tool + the owner-saved scene + the two assets; B's zombie change; docs. Push only
   when the owner says.

**⏸ Owner checkpoints, collected for the end:** (a) save the scene after step 2; (b) look
at the before/after screenshots for FXAA and Bloom; (c) a play check of combat — zombies
still reach and attack, health bars appear on first hit within 25 m; (d) if Agent A found
the day/night particle scheduler had been throwing, a look at dusk and night for particles
now starting and stopping.

## Questions for the owner, if they arise

The six decisions above are made. Ask only if something in the capture contradicts them —
for instance, if Capture 2 shows the FXAA pass costing more than 0.3 ms on the desktop
target, or if the day/night scheduler turns out to change the look at night materially.
Multichoice, recommended option first.

## STATUS

**Done and measured, 2026-09-04.** Capture stamp `20260904_fix`. Three agents ran in
parallel (B in a worktree), one integrator drove the editor. Results are in
[PERFORMANCE_FINDINGS.md](PERFORMANCE_FINDINGS.md) under "Phase 3 results".

**Headline:** at the 40-zombie wave the level went from **75 fps to 115**, main thread
−39 %. At idle, **150 to 170 fps**, draw calls −67 %, shadow casters 717 to 223.

### Four corrections to this work order, found while running it

1. **"31 unguarded sites" was wrong.** 19 of them were already dominated by an enclosing
   null check that the flat grep could not see. **12 needed a guard**, covered by 8 new
   statements. Five of the nine allotted files needed no edit at all. A doubled null check
   in a student-facing project is worse than none.
2. **The exception does not fire.** 0 in 551 startup frames, 0 in 1,117 playing frames, read
   with cursors. `DayNightCycle.particleStates` is assigned (length 39). Findings 1a and 1b
   are struck; the guards claim no frame-time saving.
3. **"The level already passes at 60 fps" was wrong.** C1 measured **56 fps at 44 zombies**
   and 75 at 40. The zombie fix, called "genuinely optional", was the only one that fixed the
   only failing scenario.
4. **The capture driver had a format bug** that silently failed every probe call after the
   first, so the whole of Capture 1 was run twice. Anything driving `PerformanceProbe.Begin`
   from a formatted string should assert on its return value, not just log it.

### Deviations

- Capture 1's combat-40 run spawned **44** enemies, not 40 (ring placement overshoot). C3
  spawned exactly 40; the C3 before/after pair is the trustworthy zombie comparison.
- The C1-to-C2 combat-40 GPU delta (+40 %) is not like-for-like — zombies move, so the camera
  saw a denser view. Same-session C3 pairs are the ones to trust.
- Applying terrain instancing made Unity rewrite a 7.5 MB binary `TerrainData`
  (`Assets/Inbox/Tidy/New Terrain.asset`). **Left uncommitted** for the owner.

### Owner checkpoints, outstanding

1. **Save the scene** — terrain instancing and FXAA are live in the editor only.
2. **Look at the before/after screenshots** in the capture folder (FXAA and Bloom are look
   changes).
3. **Play a combat check** — zombies still reach and attack; a health bar appears only after
   a zombie is damaged and only within 25 m.
4. **The 7.5 MB terrain asset**: commit it, or revert it and re-test.
5. **Agent A's question**: should `DailyObjectiveManager` log once at startup when
   `ObjectiveManager.Instance` is missing? The guards turn a loud failure into a silent one —
   with no manager the day now runs quietly past two objective gates instead of throwing.
