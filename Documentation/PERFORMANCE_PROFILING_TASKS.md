# Level Performance Profiling — Work Order (parallel agents)

> Written 2026-09-04 at commit `a2a1e53`. **This document is a work order for an
> implementing AI (Opus) running several agents in parallel.** The ask: use the Unity API
> through the Claude Code Unity CLI bridge, with the Unity Profiler, to find out what can
> be improved about the level's performance. It is a **measure-then-recommend** order:
> phases 0–2 change nothing in the game; fixes are phase 3, one at a time, each gated on
> the owner ticking it. Every number, path and line below was verified against the repo
> and the LIVE editor at `a2a1e53`; line numbers drift — re-grep before citing.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (project map, single-authority rules,
> the **Unity CLI + MCP** paragraph under Editor tooling), the ground rules in
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md), and
> [AUDIO_MANAGER_TIDY_TASKS.md](AUDIO_MANAGER_TIDY_TASKS.md)'s STATUS block — it records
> how the previous parallel-agent order actually ran, including the deviation that
> **only the integrator may touch the editor**. That deviation is a rule here. Items marked
> ⏸ are owner checkpoints; questions go in the multichoice format from
> [README.md](README.md#asking-the-owner-questions).

## Why this needs care

This is a **sound-design assessment project**. Performance work that changes when a sound
fires, how many voices play, or how a manager finds its profile is a regression here even
if the frame rate doubles. Two invariants hold across every phase:

- **Audio behaviour is unchanged.** No file under `Assets/Scripts/Audio/`,
  `Assets/Scripts/Audio Systems/` or `Assets/Scripts/Assessment/` is edited by this order,
  and the Ctrl+G progress report reads the same totals after any fix as before it
  (`0/104 · placeholder 73 · empty 31` at `a2a1e53` — regenerate rather than trust).
- **The scene is never saved by an agent.** Scene and settings edits are menu items the
  integrator runs; the owner saves.

The owner's machine is a Ryzen 9 5950X with an RTX 3080 Ti. Absolute frame times here are
optimistic for a student laptop, so findings are stated as *relative* costs (what fraction
of the frame, what multiple of the cheapest scenario) and ranked by that, not by "it's
already 200 fps".

## What the bridge offers (verified at `a2a1e53`)

Always pass `--project-path D:/GitHub/AUD216_Project1_Nature_Level`: two editors were
running when this was written (port 7800 is this project, 7801 is another), and an
unqualified `unity command` may pick the wrong one.

| Command | Verified shape / quirk |
|---|---|
| `get_performance_stats` | Read-only, any mode. Returns `render{drawCalls,batches,setPassCalls,triangles,vertices}`, `memory{totalAllocatedBytes,totalReservedBytes,monoUsedBytes,monoHeapBytes}`, `frameTiming{available,cpuFrameTimeMs,gpuFrameTimeMs,cpuMainThreadFrameTimeMs}`. Edit-mode sample: 104 draw calls / 12 batches / 62 SetPass / 35.7k tris; 818 MB allocated, **695 MB Mono heap**; `frameTiming.available = true`. One snapshot, not a series — the probe below gives the series. |
| `editor_play` / `editor_stop` / `editor_pause` | Enter/leave play mode. On entry the `TitleScreen` pauses time (`Time.timeScale = 0`) and disables the player controller; dismiss it via reflection (`FindFirstObjectByType<TitleScreen>()` → private `Play()`), then **wait one CLI round-trip** before touching the player — `FirstPersonController.Start` runs on the next frame and nulls its state. |
| `eval` / `eval_file` | Synchronous C# on the main thread with a **5 s server-side limit** (`Main thread operation timed out after 5000ms`). Nothing that waits for frames goes inside one eval; the probe records over frames and the eval only starts/reads it. Types in namespaces need the full name (`UnityStandardAssets.Characters.FirstPerson.FirstPersonController`) or a reflection lookup. A `return` at the end works; the "Unreachable code" warning is noise. |
| `console --tail N --level error --since <cursor>` | The scene currently logs **two NullReferenceExceptions per frame in play mode** (below). Count them over a capture with `--since`; an exception per frame is itself a cost. |
| `capture_game_view --source screen` | Play mode only. The PNG comes back under the JSON key **`base64`**; `--save_path` is remapped under `Assets/` and imports the file — decode inline instead. `screenshot` writes to the gitignored `Temp/pipeline-screenshots/`. |
| `recompile` + `recompile_status` | Required after adding the probe scripts; poll until `completed`. |
| `audit` / `audit_status` | Project Auditor. **Unavailable now**: `audit_status` → `"Project Auditor is not installed"`. Adding the package is a project change → owner question Q3 below. If approved: `package_add --name com.unity.project-auditor` (confirm the id and a Unity 6 version with `package_search` first), `package_status` until `completed`, then `audit --categories ... --output ...`, poll `audit_status`, read the CSV. Run it `--detach`ed; it is long. |
| `get_quality_settings` / `get_graphics_settings` / `get_import_settings --asset <path>` | Read-only. The `set_*` twins require `confirm=true` and are **forbidden in phases 0–2**. |
| `set_autotick --enable true` | Only if frames stop advancing while the editor is unfocused (check the probe's frame count grows). Turn it off afterwards. |
| Connection resets | `Connection reset by server` happens under load; retry the same call up to three times before treating it as a failure. |
| Destructive evals | The harness classifier blocked an eval that deleted a GameObject. Anything that changes the scene or an asset is a **menu item**, not an eval. Read-only evals are fine. |

Profiler API from an editor eval (verified): `UnityEditorInternal.ProfilerDriver.enabled`
(false by default), `.deepProfiling` (false — leave it; it forces a domain reload),
`.firstFrameIndex` / `.lastFrameIndex`, and **`ProfilerDriver.SaveProfile(path)` exists** —
a `.raw` the owner opens in the Profiler window. `UnityEngine.Profiling.Profiler.enabled`
is false by default. `Unity.Profiling.ProfilerRecorder` (package `com.unity.profiling.core`
1.0.3 is resolved) works at runtime for per-frame series; enumerate what this editor
exposes with `ProfilerRecorderHandle.GetAvailable()` before hard-coding stat names.

### Verified anchors

| Thing | Value at `a2a1e53` |
|---|---|
| Unity / pipeline | 6000.0.72f1, URP 17.0.4 (`Assets/Level for Sound Design/URP_PipelineAsset_Main.asset`), renderer `URP_Renderer_Main`: **Forward** (not Forward+), depth priming off, **no renderer features** |
| URP asset | renderScale 1, MSAA 1 (off), HDR on, shadow distance 40, 2 cascades, main-light shadows **4096**, additional lights **PerPixel** (per-object limit 4), additional-light shadows on at **2048**, soft shadows on, SRP Batcher on, dynamic batching off, LOD cross-fade on, `gpuResidentDrawerMode = InstancedDrawing` — but the console says **`GPUResidentDrawer Disabled due to some configured Universal Renderers not supporting Forward+`**: the setting is on and doing nothing |
| Quality | level HIGH of LOW/MEDIUM/HIGH/ULTRA, **vSync 1**, `Application.targetFrameRate = -1`, `QualitySettings.shadowDistance` 40, lodBias 1, pixelLightCount 8; `ProjectSettings`: `gcIncremental 1`, `runInBackground 1` |
| Lights | 11, **all realtime**; 6 cast shadows (5 soft, 1 hard): the DayNightCycle directional, four studio spots (`Spot Light - Key/Fill Main/Fill behind console/Main light in studio`, the fill has range 120.6), `Spot Light - Speakers` (hard). No lightmaps, no light probes data, **no occlusion culling bake** (umbra data 0) |
| Static batching | **5 of 1165** objects carry any static flag |
| Geometry | 1375 MeshRenderers, **302 LODGroups** (the Synty trees/props are prefabs, not terrain trees), 101 ParticleSystems (63 in the scene file, the rest inside prefab instances), 7 Canvases, 2 post-processing Volumes |
| Terrains | two, both active: `Terrain - Main area` (100 × 133 × 100 m, heightmap 513) and `Terrain - Footsteps area`. Pixel error 5, **`drawInstanced = false`**, treeDistance 2000 (0 terrain trees — trees are prefabs), detail distance 80 / density 1 with 10 detail prototypes, basemap distance 1000, shadow casting on |
| Post-processing | `DefaultVolumeProfile`: SplitToning, ScreenSpaceLensFlare, PaniniProjection, ColorCurves, ColorAdjustments, LiftGammaGain, WhiteBalance all active (no Bloom/AO/DoF in it); plus the mushroom-trip Volume (`Camera/MushroomTripEffect.cs`, weight-blended vignette/bloom — `MUSHROOM_TRIP.md`) |
| Enemies | 11 prefabs under `Assets/Inbox/Prefabs/Enemies/`, all with `NavMeshAgent`; `WaveManager` (`Assets/Scripts/Waves/WaveManager.cs`) **Instantiates (:563) and Destroys (:578)** zombies — `Core.Pooling.ObjectPool` is used only by `ArrowPool`. `Boss - Fort Golem.prefab`: 11 SkinnedMeshRenderers, no LODGroup. `ZombieAI.cs:242` calls `agent.SetDestination(player)` — confirm the cadence (every Update?) |
| Audio | 46 AudioSources in the scene, 1 `AudioOcclusion` (the Waterfall; one `Physics.Raycast` per frame in `Update`, `Audio Systems/AudioOcclusion.cs`), footstep raycasts in `FirstPersonController.FixedUpdate` |
| Per-frame exceptions | `DailyObjectiveManager.Update` → `DailyObjectiveManager.cs:232` (`objectivesByDay.TryGetValue(...)` region) and `DayNightCycle.UpdateParticleSystems` → `DayNightCycle.cs:459` (`particleStates[i]`), both `NullReferenceException`, both **every frame** of play mode |
| Per-frame code, static scan | the only bodies with more than one suspect call: the four IMGUI overlays (`MusicDebugOverlay`, `AudioProgressReportUI`, `AudioProgressReportWindow`, `TitleScreen` — string building per `OnGUI`, dev/assessment tools), `EnemyRadar.LateUpdate` (throttled by `CountCheckInterval` — verified, not per-frame), `AudioOcclusion.Update` (raycast per frame), `ZombieAI.LateUpdate` (`Camera.main` twice per zombie; cached by Unity since 2020.2, so cheap). `DayNightCycle.UpdateVisuals` drives `RenderSettings.ambient*` and two materials' colours every frame (:431–:452). Nothing calls `FindObjectOfType` per frame. This is a lead list, **not** findings — the profiler decides |
| Memory at rest (editor) | 818 MB allocated / 1361 MB reserved / **695 MB Mono** — editor-inflated; the play-mode number is what matters |
| Scene anchors for stations | GameObjects named `Player`, `Studio Room`, `Waterfall`, `Well`, `Mushrooms`, `Swamp_Water`, `SwampAmbie…`, `CampfireMusic`, five `Spawn Poi…` objects |
| Existing tooling | `Documentation/tools/compile-check.sh` (both assemblies); `Exports/` is gitignored — captures go there; `Assets/Scripts/Testing/` holds dev-only helpers (`GameTesting`, `StatsHelper`) — the probe belongs beside them |

## Global rules of engagement

1. **Only the integrator drives the editor.** Analysis agents get files under
   `Exports/Profiling/<stamp>/` and read-only access to the repo. No agent other than the
   integrator runs any `unity` command. (Two editors, one bridge, a 5 s main-thread budget:
   concurrent drivers corrupt each other's captures.)
2. **Phases 0–2 change no game behaviour.** The only files created are the two capture
   helpers and the findings document. No `set_*` settings command, no scene edit, no
   asset edit, no prefab edit.
3. **Leave the editor as found** after every capture: play mode stopped,
   `ProfilerDriver.enabled = false`, `Profiler.enabled = false`, `Time.timeScale = 1`,
   `set_autotick` off if it was turned on, no probe objects left in the scene (the probe
   lives on a hidden `DontDestroyOnLoad` host that play mode discards).
4. `bash Documentation/tools/compile-check.sh` after every code edit; both assemblies ✅.
   Then `recompile` + poll before the editor is used.
5. ⚠ **CRLF**: `.cs` files in the working copy are CRLF. Edit byte-wise; write new files
   CRLF. Check `file <path>`.
6. ⚠ **Harness gotcha**: the Bash tool's shell wrapper mangles C# `$"…"` strings and
   `'{…}'` sequences even inside quoted heredocs. Write scripts and C# with the Write tool,
   then run them.
7. **Evidence standard**: every finding names the scenario, the metric, the number, and
   the capture file it came from. A code-reading suspicion without a profiler number is a
   "lead", listed separately, never ranked.
8. **Do not rename, remove or reorder any serialized field** anywhere — the scene stores
   data by field name and the audio profiles are the student authoring surface.
9. One commit per phase (phase 3: one per fix), repo message style, with the harness's
   attribution trailer. Push only when the owner says.

## Agent plan — who does what, in what order

```
Phase 0  Integrator (alone, owns the editor)   capture: probe scripts → scenarios → Exports/Profiling/<stamp>/
Phase 1  Agents A · B · C · D (parallel)        analyse: read captures + code, write findings (no editor)
Phase 2  Integrator                              consolidate, rank, ask the owner   ⏸
Phase 3  one agent per approved fix (parallel   fix, re-measure, prove audio unchanged  ⏸ per scene edit
         where files are disjoint)
```

| Who | Phase | Reads | Writes | Editor |
|---|---|---|---|---|
| **Integrator** | 0, 2 | everything | `Assets/Scripts/Testing/PerformanceProbe.cs`, `Assets/Scripts/Editor/ProfilerCaptureTools.cs`, `Exports/Profiling/<stamp>/*`, `Documentation/PERFORMANCE_FINDINGS.md`, this file's STATUS | **yes — the only one** |
| **A** CPU & scripts | 1 | `markers_*.csv`, `frames_*.csv`, `console_*.txt`, `Assets/Scripts/**` | `Exports/Profiling/<stamp>/findings_A.md` | no |
| **B** Rendering & GPU | 1 | `frames_*.csv`, `stats_*.json`, `screens_*.png`, the URP/quality/terrain/light anchors, `Assets/Level for Sound Design/*.asset` | `findings_B.md` | no |
| **C** Memory, GC & assets | 1 | `frames_*.csv` (GC.Alloc, memory series), `assets_inventory.csv`, `audit_*.csv` if present, import settings | `findings_C.md` | no |
| **D** Physics, AI & audio load | 1 | `markers_*.csv`, `console_*.txt`, `Assets/Scripts/Enemies/**`, `Waves/**`, `Audio Systems/**`, `Character/**` | `findings_D.md` | no |

A–D run at the same time; their outputs are separate files, so nothing conflicts. Each
one's report must end with its findings table in the format of §Phase 2 so the
integrator can merge without rewriting.

---

## Phase 0 (Integrator) — capture

### 0.1 Two small helpers, committed with the findings

**`Assets/Scripts/Testing/PerformanceProbe.cs`** — runtime, dev-only, no scene wiring.
Installed on demand from an eval (`PerformanceProbe.Begin("<scenario>", seconds)`) onto a
hidden `DontDestroyOnLoad` host; ends itself. Records **per frame** into
`Exports/Profiling/<stamp>/frames_<scenario>.csv`:

- frame index, `Time.unscaledDeltaTime`, `FrameTimingManager` CPU / GPU / main-thread
  times (call `CaptureFrameTimings()` then `GetLatestTimings`; it is available here —
  `get_performance_stats` reports `available: true`);
- `ProfilerRecorder` series — start from `ProfilerRecorderHandle.GetAvailable()` and
  record, where present: Main Thread, Render Thread, Batches Count, SetPass Calls Count,
  Draw Calls Count, Triangles Count, Shadow Casters Count, Visible Skinned Meshes Count,
  GC.Alloc (count and bytes; needs `ProfilerRecorderOptions.SumAllSamplesInFrame`),
  GC Used Memory, Total Used Memory, Audio-category stats (whatever the enumeration lists
  — `Audio` DSP/voice counts vary by version), Physics.Contacts / Active Rigidbodies,
  NavMesh-related if any;
- `AudioSettings.GetDSPBufferSize()` once, plus the count of AudioSources with
  `isPlaying` and `Profiler`-independent `AudioSource` counts sampled every 30 frames;
- `Profiler.GetTotalAllocatedMemoryLong()` / `GetMonoUsedSizeLong()` every 30 frames.

It also writes `stats_<scenario>.json` at start and end (a copy of what
`get_performance_stats` returns, taken in-process) and must be harmless when the folder
cannot be created (log once, record nothing).

**`Assets/Scripts/Editor/ProfilerCaptureTools.cs`** — editor menu items, non-destructive:

- `Tools/Profiling/Start Profiler Recording`: `ProfilerDriver.ClearAllFrames()`,
  `ProfilerDriver.profileEditor = false`, `ProfilerDriver.enabled = true`,
  `Profiler.enabled = true`. Logs the first frame index.
- `Tools/Profiling/Stop Recording And Export`: `ProfilerDriver.enabled = false`, then
  `ProfilerDriver.SaveProfile("Exports/Profiling/<stamp>/profile_<scenario>.raw")` (the
  owner opens it in Window ▸ Analysis ▸ Profiler), then walks the captured frames with
  `ProfilerDriver.GetRawFrameDataView(frame, threadIndex 0)` and writes
  `markers_<scenario>.csv`: for every marker name, frames seen, total ms, mean ms/frame,
  max ms, and the share of the main thread — top 60 by total, plus every marker whose
  name contains `Audio`, `Physics`, `Nav`, `Animator`, `Particle`, `Terrain`, `Shadow`,
  `Culling`, `GC`, `Exception`, regardless of rank. The scenario name comes from a static
  string the eval sets (`ProfilerCaptureTools.Scenario = "..."`).

Both menu items log what they wrote. Neither touches the scene. Compile-check, then
`recompile`, then poll.

### 0.2 Scenario protocol

The capture stamp is `yyyyMMdd_HHmm`; one folder per run; a `README.txt` in it lists the
scenarios, durations, machine (`SystemInfo`), quality level, vSync, and the git commit.

Every scenario: enter play mode → dismiss the title (reflection) → **wait one round
trip** → set the state → `Start Profiler Recording` → `PerformanceProbe.Begin(name, 20)`
→ wait ~25 s of wall clock (eval `PerformanceProbe.IsDone` until true, sleeping between
CLI calls — never inside an eval) → `Stop Recording And Export` → `console --since`
count of exceptions during the window → `capture_game_view --source screen` decoded to
`screens_<scenario>.png` → `editor_stop` → confirm `dirty == false` and
`ProfilerDriver.enabled == false`.

Because `vSync = 1`, every scenario is captured **twice**: once as authored, once with
`QualitySettings.vSyncCount = 0` and `Application.targetFrameRate = -1` set **from the
probe at runtime only** (play-mode state, discarded on stop — not a settings write) so
the uncapped frame time is visible. Say in `README.txt` which is which.

| # | Scenario | How to set it up (read-only evals; player moves are `CharacterController.enabled=false; transform.position=…; enabled=true`) |
|---|---|---|
| S1 | **Spawn, idle** | as loaded, nothing raised, stand still 20 s — the baseline |
| S2 | **Studio room** | teleport to the `Studio Room` object's position, face into the room (the four shadowed spots + speakers live here) |
| S3 | **Waterfall / river** | teleport 3 m from `Waterfall`, facing it (occlusion raycast, water, particles) |
| S4 | **Swamp** | teleport near `Swamp_Water` (detail grass, the ambience zone) |
| S5 | **Night** | S1 position; set the DayNightCycle to night by whatever field or method drives its clock (read `DayNightCycle.cs` — `UpdateTime` at ~:355; the scene wires `SkipToDay2`/`SkipToDay3` UnityEvents somewhere: grep `SkipToDay`), so the shadowed directional light and the studio spots are at their heaviest |
| S6 | **Combat** | spawn the wave: find `WaveManager`'s spawn path (the `Instantiate` at :563 sits in a method — call it, or its public trigger, via reflection) until ~8–10 zombies are alive; stand at spawn; 20 s. Note the alive count in `README.txt`. Also capture **S6b: same, boss** — spawn `Boss - Fort Golem` (11 skinned meshes) |
| S7 | **Mushroom trip** | trigger `MushroomTripInteraction` (see `MUSHROOM_TRIP.md` for the entry point) so the trip Volume blends in; 20 s |
| S8 | **Hard look** | S1 with the camera turned to face the densest tree cluster (pick the direction with the highest `Triangles Count` in a quick 4-way sweep) |

Skip a scenario only if its entry point cannot be found in ten minutes; write down why.

### 0.3 Also from phase 0

- `console_<scenario>.txt`: the error/exception tail for each window and a count per
  distinct message. The two per-frame NREs should show up as ~20 × fps entries each.
- `assets_inventory.csv` (editor eval, `AssetDatabase`): every Texture2D (path, size,
  format, mipmaps, max size, compression), every Mesh (verts, read/write enabled), every
  AudioClip (path, length, load type, compression format, preload, quality), every
  Material's shader name. Read-only; one pass; this is what Project Auditor would give if
  it were installed.
- `Q3` answered → `audit_*.csv` per category (Performance, Assets, Code, Settings).
- `git log -1`, the quality level, and both terrain settings pasted into `README.txt`.

Phase 0 commit: the two helpers + this file's STATUS updated with the stamp. Captures are
gitignored; list their sizes in the commit message.

---

## Phase 1 — four analysis agents, in parallel, no editor

Each agent writes `Exports/Profiling/<stamp>/findings_<letter>.md` with: (1) the numbers it
relied on, quoted; (2) a findings table in the §Phase 2 format; (3) a separate **leads**
list (suspicions without a number); (4) questions for the owner in multichoice format.
Nothing else is written. The integrator copies the tables into the findings document.

### Agent A — CPU main thread and scripts

From `markers_*.csv` and `frames_*.csv`: what the main thread spends its time on per
scenario (top 15 markers, share of frame), how `PlayerLoop` splits between Update /
FixedUpdate / LateUpdate / Animators / Rendering submit / Audio. Cross-reference every
project-script marker to its `Update` body. Specifically answer:

- the cost of the **two per-frame NullReferenceExceptions** (they show as
  `Exception`/`Debug.LogException` markers and as GC.Alloc) — and, from the code, what is
  null (`DailyObjectiveManager.cs:232`, `DayNightCycle.cs:459`); the fix is a one-line
  guard each, but it is phase 3;
- whether `DayNightCycle.UpdateVisuals`'s per-frame material and `RenderSettings` writes
  register at all;
- `ZombieAI` per-zombie cost in S6 (NavMesh `SetDestination` cadence at `:242`,
  animator, the `LateUpdate` canvas billboard) and how it scales from S1 → S6 → S6b;
- the IMGUI overlays: are any drawing when hidden? (`OnGUI` runs even when a panel is
  off unless the component guards it — check `AudioProgressReportUI`, `MusicDebugOverlay`,
  `AudioEventMonitor`, `TitleScreen`, `PauseMenu`, `EndGameScreen`.)
- FixedUpdate rate vs physics load (footstep raycasts, `AudioOcclusion`).

### Agent B — rendering and GPU

From `frames_*.csv` (batches, SetPass, triangles, shadow casters, GPU frame time),
`stats_*.json`, the screenshots, and the anchors: where the GPU time goes and what the
draw-call count is made of. Specifically answer, with numbers:

- **shadows**: six realtime shadow casters, 4096 main / 2048 additional maps, soft, 40 m
  distance, 2 cascades — measure S1 vs S5 vs S2; recommend per-light changes (which spots
  need shadows at all, resolution per light, `shadowDistance`, cascade count) with the
  expected saving;
- **the GPU Resident Drawer contradiction**: it is set to InstancedDrawing but disabled
  because the renderer is Forward. Two candidate fixes: switch `URP_Renderer_Main` to
  **Forward+** (enables GRD and lifts the per-object light limit; check the per-object
  limit 4 is being hit in the studio) or set the drawer mode to Disabled to stop the
  warning. Recommend one with the reasoning; the test is a re-capture of S2 and S8;
- **static batching**: 5 of 1165 objects flagged static, no occlusion bake, no lightmaps
  — estimate what flagging the Synty props/trees `BatchingStatic` (not `LightmapStatic`,
  which would change nothing without a bake) and an occlusion bake would do to SetPass /
  batches in S8; note LOD cross-fade is on (it costs a second draw per fading LOD);
- **terrain**: `drawInstanced = false` on both terrains (turn on: free), pixel error 5 on
  a 100 m map, detail distance 80 with 10 prototypes, basemap 1000 — measure S4;
- **lights**: additional lights PerPixel with limit 4, HDR on, MSAA off; whether the
  `Spot Light - Fill behind console` (range 120.6) leaks over the whole studio;
- **post-processing**: the seven active overrides in `DefaultVolumeProfile` (lens flare
  and Panini are the ones with a cost) and the mushroom-trip Volume in S7;
- **particles**: 101 systems — which are alive at rest (`ParticleSystem.isPlaying`) and
  what they cost in S1 vs S3.

Every recommendation names the asset/field it would change and what the owner would see.

### Agent C — memory, GC and assets

From `frames_*.csv` (GC.Alloc per frame, GC Used, Total Used), `assets_inventory.csv`,
and `audit_*.csv` if present:

- **GC allocations per frame** in S1 and S6: which markers allocate (Agent A's marker
  table has the `GC.Alloc` column), and whether any steady per-frame allocation exists
  outside the dev overlays;
- the **Mono heap** in play mode vs the 695 MB seen in edit mode, and what dominates
  Total Used (textures? meshes? audio?);
- **audio import settings** — this matters most for the assessment: for every AudioClip
  (the 6 shipped demo clips today, the students' clips tomorrow) report load type
  (`DecompressOnLoad` for short one-shots, `Streaming` for the long ambiences/music),
  compression, preload, and flag anything a student would get wrong by default; write the
  one-paragraph guidance the student guide should carry;
- **textures**: anything over 2048, uncompressed, or with mipmaps off on a world texture;
  **meshes**: read/write enabled where nothing reads them;
- the two terrains' data assets (heightmap 513, 10 detail prototypes) and the Synty
  material count (how many distinct shaders/materials → SetPass).

### Agent D — physics, AI and audio load

From `markers_*.csv` (Physics, NavMesh, Animator, Audio markers), `console_*.txt`, and
the code:

- **AI**: NavMeshAgent count per scenario, `SetDestination` cadence (`ZombieAI.cs:242`),
  path recalculation cost, `Instantiate`/`Destroy` in `WaveManager` (:563/:578) vs the
  existing `Core.Pooling.ObjectPool` (used only by `ArrowPool`) — measure the spike on
  spawn in S6;
- **animation**: 11 skinned meshes on the boss with no LODGroup; skinned-mesh update cost
  per zombie; whether `Animator.cullingMode` is set;
- **physics**: colliders (105 box / 34 mesh / 8 capsule), one Rigidbody, footstep and
  occlusion raycasts, the fixed timestep;
- **audio load**: 46 AudioSources, how many playing per scenario, DSP buffer size, the
  Audio-category profiler markers, virtual/real voice counts if exposed, and whether any
  looping source plays inaudibly far from the listener (a cost with no sound — but
  **report it, do not change it**: which sources loop where is an authored sound-design
  decision);
- the exceptions: confirm the two per-frame NREs are the only recurring ones, and whether
  they appear in every scenario.

---

## Phase 2 (Integrator) — consolidate and ask

Write `Documentation/PERFORMANCE_FINDINGS.md`:

1. **Headline** — per scenario, one line: mean / p95 frame time (capped and uncapped),
   main-thread ms, GPU ms, batches, SetPass, triangles, GC.Alloc per frame, exceptions per
   frame. A small table, from `frames_*.csv`.
2. **Ranked findings** — one table, every row from A–D, sorted by *estimated fraction of
   the frame in the worst scenario it affects*:

   | # | Finding | Evidence (scenario · metric · number · file) | Recommendation | Expected gain | Risk | Effort | Touches audio? |

   "Touches audio?" is yes if the fix edits anything under `Audio*/`, changes when a clip
   plays, or alters an AudioSource's settings. Those rows are **not** offered as phase-3
   fixes by this order; they are handed back as observations.
3. **Leads** — everything without a number, kept apart.
4. **Owner questions**, multichoice, batched — at least these:

   **Q1. What is the target machine and frame budget?** A. the owner's desktop, 60 fps
   with vSync (what students see in the lab?) — B. a typical student laptop with integrated
   graphics, 30 fps ← **recommended** as the *design* target, with the desktop as the
   *measurement* platform — C. no budget: fix only what is clearly wasteful.

   **Q2. Forward+ or drawer off?** A. switch `URP_Renderer_Main` to Forward+ (GRD works,
   more lights per object; re-measure) ← recommended if Agent B's S2/S8 numbers support
   it — B. set `gpuResidentDrawerMode` to Disabled (silence the warning, no other change)
   — C. leave as is.

   **Q3. Install Project Auditor?** A. yes, `com.unity.project-auditor`, and strip it
   before the handout like `com.unity.pipeline` ← recommended — B. no, the asset inventory
   eval covers it.

   **Q4. Which phase-3 fixes proceed?** pick any: the two NRE guards (zero risk) · terrain
   `drawInstanced` on · shadow-map sizes / which lights cast · static flags on the Synty
   props · zombie pooling · `SetDestination` throttling · occlusion bake · post-processing
   trims — each with the number from the table beside it.

5. ⏸ **Stop here.** Phase 3 starts only with answers to Q4.

Phase 2 commit: the findings document + README index row + this file's STATUS.

---

## Phase 3 (gated) — fixes, one agent each

For every fix the owner ticked:

- one agent, one branch of files, disjoint from every other running fix (the integrator
  assigns; a fix touching a file another fix needs waits);
- code fixes (the NRE guards, `SetDestination` cadence, pooling) are ordinary edits with
  compile-check; **scene, prefab, terrain and settings fixes are menu items** the
  integrator runs (`Tools/Profiling/Apply …`), Undo-recorded, logging what changed and
  `SAVE THE SCENE` — the owner saves; `set_quality_settings` / `set_graphics_settings`
  with `confirm=true` only after the owner ticked that exact change;
- **re-measure**: the integrator re-runs the affected scenario with the same protocol
  and appends before/after to the findings table. A fix with no measurable gain is
  reverted, not kept "because it should help";
- **prove audio unchanged**: regenerate the Ctrl+G export before and after (totals and
  every row identical), run `Validate wiring` in the report window (no new findings), and
  confirm no file under `Audio*/` or `Assessment/` changed (`git diff --stat`);
- one commit per fix, the number in the message.

## Deliverables

- `Assets/Scripts/Testing/PerformanceProbe.cs`, `Assets/Scripts/Editor/ProfilerCaptureTools.cs`
- `Exports/Profiling/<stamp>/` (gitignored): `README.txt`, `frames_*.csv`, `markers_*.csv`,
  `profile_*.raw`, `stats_*.json`, `console_*.txt`, `screens_*.png`, `assets_inventory.csv`,
  `findings_A..D.md`, `audit_*.csv` (if Q3 = A)
- `Documentation/PERFORMANCE_FINDINGS.md` — the ranked table and the questions
- CODE_STRUCTURE.md: a work-history row and an Editor-tooling bullet for the two helpers
  (integrator, phase 2)

## STATUS

**Phase 0 complete, 2026-09-04, capture stamp `20260904_1349`** (536 MB, gitignored).
Seven scenarios captured, plus a duplicate of the baseline. Phase 1 next.

### Deviations from this order, and why

1. **`ProfilerDriver` needed a Profiler window.** With none open, `ProfilerDriver.enabled
   = true` records nothing and `firstFrameIndex` stays `-1`; five scenarios exported an
   empty marker table before this was spotted. `StartRecording` now opens the window
   (unfocused) first. **The order's claim that `SaveProfile` "just works" headlessly was
   wrong** - the `.raw` files only appeared after that fix.
2. **A second marker export was added.** The driver's table only covers the last ~300
   frames its ring buffer holds. `PerformanceProbe` now also samples every timing marker
   in PlayerLoop/Ai/Animation/Audio/Particles/Physics/GC/Gui with `ProfilerRecorder` over
   the **whole** window, into `markers_recorder_*.csv` (679 markers sampled, ~314 with
   time). No window needed, so it works unattended. Both files are kept: the driver's has
   the hierarchy and call counts, the recorder's has the full window.
3. **The counters are resolved by name**, not `(category, name)` - `ProfilerCategory.Any`
   does not exist, and guessing a category yields a silently invalid recorder.
4. **Scenarios were grouped into three play sessions**, not one each. No scenario depends
   on a fresh load, and it saved ~10 editor restarts.
5. **"Capped" is not capped.** The editor game view does not vsync here, so
   `S1_spawn_idle` and `S1_spawn_idle_uncapped` are within 1% of each other. Every number
   should be read as uncapped.
6. **Combat was spawned directly.** `WaveManager.StartNight` was invoked first and spawned
   nothing - it depends on day/night state the harness never reaches. Eight zombies were
   instantiated instead from the prefabs the waves are configured with, ringed 9 m around
   the player and snapped to the NavMesh: same prefabs, same `ZombieAI`, same agents, only
   the wave *scheduling* bypassed.
7. **Not captured**: S6b (the boss in isolation) and S7 (the mushroom trip) - the trip's
   entry point was not wired up in the time available. Both are gaps in the evidence and
   are called out in `README.txt`.

### The headline, before any analysis

Means over each window with warm-up dropped, on a 5950X / RTX 3080 Ti. Read these as
*relative* - a student laptop is the target machine, which is owner question Q1.

| scenario | fps | frame ms (p95) | GPU ms | main ms | batches | SetPass | shadow casters | triangles | GC B/frame |
|---|---|---|---|---|---|---|---|---|---|
| S1 spawn idle | 153 | 6.6 (7.8) | 3.0 | 4.9 | 2137 | 130 | 717 | 1.42 M | 34.6 k |
| S2 studio | 166 | 6.0 (7.0) | 2.7 | 4.4 | 1408 | 121 | 131 | 1.09 M | 33.5 k |
| S3 waterfall | 228 | 4.4 (5.6) | 2.2 | 3.0 | 544 | 71 | 186 | 0.32 M | 32.9 k |
| S4 swamp | 195 | 5.1 (6.0) | 2.9 | 3.7 | 1146 | 101 | 331 | 0.63 M | 34.7 k |
| S5 night | 164 | 6.1 (8.3) | 2.9 | 4.3 | 1482 | 129 | 474 | 1.23 M | 15.6 k |
| **S6 combat (8 zombies)** | **126** | **8.0 (9.9)** | 3.7 | **6.1** | 2379 | 176 | **972** | 1.59 M | 23.3 k |
| **S8 tree view** | **124** | **8.0 (10.7)** | **3.8** | 5.6 | 1634 | 129 | 640 | 1.15 M | 19.6 k |

Facts that jump out of the capture and belong to phase 1 to explain:

- **~11,400 live particles in every static scenario, including idle**, falling to ~6,800
  in combat. `ParticleSystem.GeometryJob` alone is **14% of the frame** in S6.
- **717 shadow casters at idle, 972 in combat**, against 6 realtime shadow-casting lights.
- **~33 kB of GC allocation every frame** at rest. Nothing in the game should be
  allocating while the player stands still.
- **941 MB of texture memory, 3.1 GB total** in the editor.
- **Zero AudioSources playing in every scenario** - the handout scene's clips are all
  placeholder or empty, so the audio cost measured here is a floor, not the shipping cost.
- The two per-frame `NullReferenceException`s saturate the console: 400 of 400 entries in
  every scenario, and they are inside every number above.

### Phase 1 and 2 complete, 2026-09-04

Four agents analysed the captures in parallel (`findings_{A,B,C,D}.md`, gitignored) and the
merge is [PERFORMANCE_FINDINGS.md](PERFORMANCE_FINDINGS.md). **Nothing in the game changed.**

**All four agents independently found the phase-0 capture flawed**, and the corrections
matter more than any single finding:

1. **The player died during two captures.** `EndGameScreen.Show` sets `Time.timeScale = 0`;
   the "YOU DIED" panel is in `screen_S4_swamp.png` and `screen_S6_combat.png`, and neither
   scenario has a `Physics.Simulate` marker. **78 % of the S6 combat window is a frozen death
   screen** — live combat is 8.85 ms / 113 fps, not the 7.96 / 126 in the table above. S4, the
   only terrain-detail scenario, is equally paused. The protocol should have checked
   `timeScale` at both ends of every window; the probe already writes it to `stats_*.json`,
   and nobody looked.
2. **The two "slowest" scenarios were warming up.** S6 and S8 were each the first capture of a
   fresh play session. S8's frame time falls 8.6 → 6.2 ms across its own window while
   particles ramp 1,584 → 11,419; its settled mean is **6.40 ms against idle's 6.73**. The
   tree view is not slower than standing still. Scenarios need ~45 s of settle time.
3. **"Two exceptions every frame, in every scenario" was wrong on both counts** — it is one
   per frame, in four of eight scenarios. The console tails are cumulative for the play
   session, not the window, so S5/S6/S8 inherited a stale count of 400.
4. Smaller: the profiler flushes ~1 MB every 3.7 s, which is 2–3 of the six worst frames in
   every capture; and `screen_S3_waterfall.png` shows the inside of a rock, so the waterfall
   scenario measured the wrong thing.

**The top finding is one bug with four symptoms.** A single caught `NullReferenceException`
per frame costs 3.4 % of the main thread — more than every `Update` method in the game
combined — and 59 % of the idle GC. It also silently kills the day/night ambient and colour
lerps, and leaves ~11,430 particles alive permanently because the scheduler that stops them
throws before its first loop iteration. Both sites (`DailyObjectiveManager.cs:232`,
`DayNightCycle.cs:459`) are fields assigned as the last statement of an `Awake` that never
finished.

**Three memory alarms in the phase-0 STATUS were false**, including one of mine: 941 MB of
texture and 945 MB of managed heap are editor numbers (the scene reaches 156 of 1317
textures), and the "system is running out of memory" errors trace to the profiler's own
buffers.

**Two work-order assumptions were wrong and are withdrawn**: static batching flags and an
occlusion bake are the wrong levers here (the SRP Batcher already gets a 94 % reduction, and a
bake would make culling coarser and rot in a handout); and the "seven active post-processing
overrides" are all at neutral values and cost nothing — the real post finding is that Bloom is
configured twice at conflicting intensities.

### Still open

- ⏸ **Phase 2's owner questions**, put to the owner with the findings document.
- Phase 3 is gated on those answers. The recommended order is in
  [PERFORMANCE_FINDINGS.md](PERFORMANCE_FINDINGS.md) §5: fix the exception, re-capture
  properly, take the free settings wins, then Forward+.
