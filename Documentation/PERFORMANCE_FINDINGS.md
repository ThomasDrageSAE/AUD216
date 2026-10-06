# Performance Findings — Nature Level

> Phase 2 of [PERFORMANCE_PROFILING_TASKS.md](PERFORMANCE_PROFILING_TASKS.md). Capture stamp
> `20260904_1349`, commit `263aa90`, Unity 6000.0.72f1, URP 17.0.4.
> Measured on a Ryzen 9 5950X / RTX 3080 Ti at **1280×720**. A student laptop at 1080p renders
> 2.25× the pixels on integrated graphics, so **every number here is optimistic** and findings
> are ranked by *share of the frame*, never by milliseconds.
>
> Four agents analysed the captures in parallel. Their full workings are in
> `Exports/Profiling/20260904_1349/findings_{A,B,C,D}.md` (gitignored). This document is the
> merge; where two agents disagreed, the disagreement is recorded rather than smoothed over.
>
> **Nothing in the game has been changed.** Phase 3 is gated on the questions at the end.

---

## 0. Read this first: the capture was flawed, and the headline ranking was wrong

All four agents independently found problems with the phase-0 data. Three of them matter
enough to change what phase 3 should do.

**The player died during two captures.** `EndGameScreen.Show` sets `Time.timeScale = 0`, and
the "YOU DIED" panel is visible in `screen_S4_swamp.png` and `screen_S6_combat.png`. Neither
scenario has any `Physics.Simulate` marker at all. In **S6 combat, 78 % of the window is a
frozen death screen** — the eight zombies stopped moving 3.2 seconds in. Live combat is
**8.85 ms / 113 fps**, not the 7.96 ms / 126 fps published in the STATUS table. S4, the only
terrain-detail scenario, is equally paused.

**The two "slowest" scenarios were not slow.** S6 and S8 were each the first capture of a
fresh play session. S8's frame time *falls* from 8.6 ms to 6.2 ms across its own window while
particles ramp 1,584 → 11,419. Its last-300-frame mean is **6.40 ms against idle's 6.73** —
the tree view is not slower than standing still, it was still warming up.

**My "two exceptions every frame, in every scenario" was wrong on both counts.** It is **one**
logged exception per frame, and only in **four** of the eight scenarios. The console tails I
saved are cumulative for the play session, not the capture window, so S5/S6/S8 inherited a
stale count of 400.

Smaller, but worth knowing: the profiler flushes ~1 MB every 3.7 s, which accounts for two or
three of the six worst frames in every capture; and `screen_S3_waterfall.png` is a shot of the
inside of a rock, so the waterfall scenario measured the wrong thing.

**Added 2026-09-04, before phase 3:** re-measuring with console cursors found **zero game
exceptions** in 1,668 frames across two windows, and `DayNightCycle.particleStates` assigned.
Finding #1's *rate* and findings 1a and 1b are withdrawn in the table below. The lesson is the
same one as the rest of this section: the console tail is cumulative for the play session, so
every "fires every frame" claim in phase 1 was built on a stale read.

**What survives all of that:** the idle, studio, night and tree-view captures are sound, the
per-frame counters are sound, and the marker attribution is sound. The corrected picture is
below.

---

## 1. Headline

Corrected. S6 is the live-combat figure derived by Agent D; S8 is its settled last-300 mean.

| scenario | fps | frame ms | GPU ms | main ms | batches | SetPass | shadow casters | notes |
|---|---|---|---|---|---|---|---|---|
| S3 waterfall | 228 | 4.4 | 2.2 | 3.0 | 544 | 71 | 186 | camera faced a rock |
| S4 swamp | 195 | 5.1 | 2.9 | 3.7 | 1146 | 101 | 331 | **paused** |
| S2 studio | 166 | 6.0 | 2.7 | 4.4 | 1408 | 121 | 131 | |
| S5 night | 164 | 6.1 | 2.9 | 4.3 | 1482 | 129 | 474 | |
| S1 spawn idle | 153 | 6.6 | 3.0 | 4.9 | 2137 | 130 | 717 | the real baseline |
| S8 tree view | ~156 | 6.4 | ~2.8 | 5.6 | 1634 | 129 | 640 | settled value |
| **S6 combat, 8 zombies** | **113** | **8.9** | 3.6 | **6.8** | 2379 | 176 | **972** | live frames only |

**The frame is CPU-bound everywhere, about 2:1.** The GPU idles roughly half of every frame in
all seven scenarios. Render *submission on the main thread* is 38–51 % of the frame; the game's
own update logic is 23–31 %. That inverts on a laptop, which is why the GPU findings still
matter.

---

## 2. Ranked findings

Ranked by share of the frame in the worst scenario each affects. Full evidence in the agents'
files; the agent's own reference is in brackets.

| # | Finding | Evidence | Recommendation | Expected gain | Risk | Effort | Touches audio? |
|---|---|---|---|---|---|---|---|
| **1** | ~~**One caught NullReferenceException per frame…**~~ **RATE DISPROVEN 2026-09-04.** Re-measured with console *cursors* over two windows in one session: **0 game exceptions in 551 startup frames and 0 in 1,117 playing frames**. The phase-1 figures below came from four scenarios in a single session, read from a *cumulative* console tail. The stack trace is real and the code path can throw; it does not throw today. The fix is defensive, and no frame-time saving may be claimed for it. [A1, B6 — superseded] | The exception trio (`StackTraceUtility.Extract…`, `LogStringToConsole`, `CallLogCallback`) is **0.224 ms/f = 3.4 % of the main thread** (S1). `Update.ScriptRunBehaviourUpdate` is **0.469 ms/f where it fires and 0.191 ms where it does not** — the exception is 59 % of it. GC: **31,045 → 12,700 B/frame**, i.e. 18,345 B/f = **2.7 MB/s** of pure logging garbage. Sites: `DailyObjectiveManager.cs:232`, `DayNightCycle.cs:459`. | Find what throws **earlier in each `Awake`** — both fields are assigned as the last statement of `Awake`, so `Awake` never finished. One play-mode entry with Error Pause identifies it. Fix that, *then* add defensive guards. **Do not just null-guard the two lines** — that hides the bug and leaves the systems below dead. | −3.4 % of the main thread, −2.7 MB/s of GC, **and three separate symptoms disappear** (see below) | Low — restores intended behaviour. The day/night ambient lerp becomes live again, a **visual** change to eyeball | S | **No** |
| **1a** | ~~*Symptom of #1:* the day/night sky and ambient transitions never run.~~ **DISPROVEN 2026-09-04.** Measured live: `DayNightCycle.particleStates` is **assigned, length 39**, so `UpdateParticleSystems` does not throw and nothing after it is skipped. The transitions run. | Live reflection read at the end of a play session, `Exports/Profiling/20260904_fix/step1_exception_rate.json` | None. Withdrawn. | — | — | — | No |
| **1b** | ~~*Symptom of #1:* ~11,430 particles stay alive permanently because the scheduler that stops them throws.~~ **CAUSE DISPROVEN 2026-09-04** — the scheduler is not broken (see 1a). The *count* is real: `alive_particles` 11,425–11,441 in every live scenario, 2.6–2.7 ms/frame of worker-thread job work. It is simply what the level emits, not a bug. | `alive_particles` · `frames_*.csv`; `particleStates` assigned · `step1_exception_rate.json` | None here. If the particle budget is ever worth attacking it is a content decision, not a fix — and on the owner's desktop target the job cost never reaches the main thread | — | — | — | No |
| **1c** | *Symptom of #1:* the "idle has more particles than combat" puzzle was never real — S6's count was frozen by the pause and S8's was still ramping. | — | — | Removes a false finding | — | — | No |
| **2** | **The GPU Resident Drawer is switched on and doing nothing, because the renderer is Forward.** The frame is CPU-submission-bound, which is exactly what it would help. [B1, B2] | `m_GPUResidentDrawerMode: 1` with `m_RenderingMode: 0`; URP logs `Disabled due to … not supporting Forward+`. `RenderLoop.ScheduleDraw` 1.45–2.04 ms + `Submit` 1.40–2.08 ms = **~50 % of the main thread**. `m_GPUResidentDrawerEnableOcclusionCullingInCameras` is already `1`, so GPU occlusion culling comes free with the switch | `URP_Renderer_Main.asset` → `m_RenderingMode` **Forward → Forward+** | **Estimated, not measured:** a 25–50 % cut in the submit pair is **0.7–2.0 ms of a 6.5–8.9 ms frame (11–26 %)**. Must be verified by re-capturing S1, S2, S8 | **Medium** — Forward+ changes the lighting path to clustered. Should look equal or better; on weak integrated graphics the clustering pass has its own fixed cost. One-line revert | S | No |
| **3** | **Terrain instancing is off, costing 4.2 % of the main thread for nothing**, and both terrains cast two-sided shadows. [B4] | `Terrain.Heightmap.RenderStep1+2+3` = **0.275 ms/f (4.2 %)** in S1; `CullAllTerrains` a further 4.2–6.0 %. The material is URP's stock TerrainLit, so instancing is safe. `m_ShadowCastingMode: 2` (TwoSided) on a heightfield | Both terrains: `m_DrawInstanced` **0 → 1**; `m_ShadowCastingMode` **2 → 1**; `m_DetailObjectDistance` **80 → 40**; `m_SplatMapDistance` **1000 → 80** | ~4 % of the main thread; two-sided → on halves terrain shadow rasterisation for **free, with no visual change**; detail patches 598 → ~150 per frame | Low for the first two. Detail 40 makes grass pop in closer; basemap 80 is a visible quality step at distance | S, via a menu item | No |
| **4** | **Shadow distance 40 m produces 717 casters at idle and 972 in combat — and cutting it makes shadows _sharper_.** All the measurable cost is the main directional light; the five studio spots cost **0.001 ms**. [B3] | Casters 717 (S1) / 972 (S6). `Draw Main Light Shadowmap` 0.102–0.340 ms; `Draw Additional Lights Shadowmap` **0.001 ms everywhere** | `URP_PipelineAsset_Main.asset` → `m_ShadowDistance` **40 → 25**. Optional laptop lever: `m_MainLightShadowmapResolution` **4096 → 2048** | Casters scale with area: **717 → ~280, i.e. −20 % of all S1 draw calls**; main shadow pass 0.284 → ~0.11 ms. **Cascade texel density improves 1.6×, so shadows get crisper** | Low — only shadows past 25 m vanish, and the canopy and fog hide them | S | No |
| **5** | **Every uGUI canvas rebuilds every single frame while the player stands still**, and each zombie's world-space health bar multiplies it. Per zombie the canvas costs more than its AI. [A2, A3, D2] | `UGUI.Rendering.UpdateBatches` present in **300 of 300 idle frames**, 0.192 ms/f (2.94 %) → **0.474 ms/f (6.16 %)** in combat, with `Canvas.RenderSubBatch` 13,500 calls / 300 frames. Per zombie: canvas **0.0387 ms** vs `ZombieAI.Update` 0.0356 ms | Find what dirties the HUD canvas at idle (candidates: `compass.cs:32`, `ReticleController.cs:109`, `ObjectiveRadar.cs:52` — all write unconditionally) and compare-before-write. For zombies: show the bar only within a distance window **and** below full health, and billboard a parent transform | 1–3 % at idle, 3–5 % in combat; **1.37 ms ≈ 20 % of the combat frame at 40 zombies** | Low for the HUD; the zombie change is visible (bars vanish at range / at full health) | M | No |
| **6** | **A stray second directional light is a per-pixel additional light on every object in the level**, and it is what pushes the studio over the 4-light limit. [B9] | `Light (directional) - shadows`, intensity 0.4, active, shadows off. URP picks the brighter day/night sun as main, so this becomes an *additional* light with infinite range | Set it inactive, or fold the fill into `RenderSettings.ambientLight`, which `DayNightCycle` already drives | One per-pixel light off **every** shading pass in the level; the studio drops 5 → 4 additional lights and the per-object light popping stops | **Visual** — the scene gets slightly darker unless ambient is raised. Owner call | S | No |
| **7** | **A zombie costs ~0.10 ms of main thread, and the waves are configured for 40 in one wave and 60 in one night. The capture measured 8.** [D3, D4, A3] | Per zombie: NavMesh crowd 0.041, canvas 0.034, animator 0.025, script ~0.013. `agent.SetDestination` runs **once per zombie per frame**, unthrottled — `Crowd.Pathfinding` is 35 % of all AI cost. Wave data: **40** in one wave (W1 N7), **60** across one night (W2 N3) | Throttle `SetDestination` to ~4 Hz with a per-zombie random phase offset, so 40 agents never repath on one frame. Keep steering and attack logic per-frame | At the 6.81 ms live combat frame: 20 zombies = **34 %**, 40 = **68 %**, 60 = **more than the entire main thread**. Throttling saves ~8 % at 40 | Low — the path goes up to 250 ms stale on a 1.5 m/s shambler; steering stays per-frame | S | No |
| **8** | **Bloom is configured twice at conflicting intensities, and the game ships with no anti-aliasing.** The seven "active" post overrides I flagged in the work order are all at **neutral values** and cost nothing. [B7] | `ScreenSpaceLensFlare.intensity = 0`, `PaniniProjection.distance = 0` — neither pass is scheduled. Bloom is **0.4** in `Post Processing Profile` and **1.47** in `FirstPersonCharacter Profile`, both global, both weight 1, both priority 0 — **which wins is undefined**. `m_Antialiasing: 0` and MSAA off | Decide the Bloom intensity once and delete the duplicate. Consider FXAA — one cheap pass, and this low-poly foliage aliases badly | Deleting the duplicate costs nothing and removes real ambiguity in an assessed look. Bloom at quarter downscale would save ~0.5–1.5 ms on integrated graphics | **Look** change on a project whose look is assessed | S | No |
| **9** | **Unity's default audio import is Decompress On Load + Vorbis, and every shipped clip carries it untouched.** Right for a 90 ms beep, wrong for every ambience the students are about to add — and there are ~104 slots waiting. [C1, C10] | All six demo clips: `loadType: 0, compressionFormat: 1, platformSettingOverrides: {}` — the untouched default. The six Standard Assets footstep clips are worse: `CompressedInMemory + PCM + preload`, which saves nothing and forces them into RAM at load | Put Agent C's paragraph (in `findings_C.md` §3, quoted below) into the student guide, and fix the six Standard Assets clips so the project contains no bad example | Prevents the predictable failure: one 3-minute stereo ambience on the default is ≈35 MB of RAM plus a load-time decode spike. Ten of them is the whole level's texture budget again | None — import settings do not change which clip plays or when | S | **No** |
| **10** | **~40 MB of the scene's texture budget is five oversized textures**, and two project-authored UI icons are uncompressed at near-1024. [C7, C8] | Five reachable textures over 2048 (two 4096² atlases, three 4096×2048 skybox maps) = 10.67 MB est each; 16 reachable ≥2048 are **64 %** of the scene's texture budget. `Walkie Talkie Icon.png` 817² RGBA32 = 2.55 MB, `Torch Inventory Icon.png` 585² = 1.31 MB | Max Size **2048** on the five; Compressed + Max Size **256** on the two icons | ~44 MB, importer fields only, no code and no scene edit | Low — check the skybox for banding and the HUD icons for crispness | S | No |
| **11** | **Three memory alarms are false, and one of them is mine.** 941 MB of texture and 945 MB of managed heap are editor numbers, and the three "system is running out of memory" errors trace to the profiler's own buffers. [C5, C6, C9] | The scene reaches **156 of 1317 textures** (~100–140 MB) — verified by a guid walk whose control (6 of 12 audio clips reachable) matches `audioclip_count` exactly. `gc_reserved_bytes` is **945,659,904 in every frame of every scenario** and never responds to the game. `total_used_bytes` rises 200–333 MB per window while `texture_bytes` moves 0 bytes; the eight `.raw` exports are 55–90 MB each | **Do not report these as shipping problems.** One Development Build with Autoconnect Profiler, idle 20 s, settles all three | Stops a ~900 MB false alarm from driving the wrong work | None | S (one build) | No |
| **12** | **Static batching flags and an occlusion bake are the wrong levers — I asked for both in the work order and the evidence says no.** [B8] | The SRP Batcher already collapses **2137 batches into 130 SetPass, a 94 % reduction**. Marking props `BatchingStatic` would duplicate mesh data and make culling *coarser* (a combined batch culls as one unit). An Umbra bake returns little on a 100 m open landscape and goes stale whenever a student moves a rock | **Do neither.** Take the same benefit from Forward+ (#2), which brings per-frame GPU occlusion culling with no bake asset and no static flags, and which works on moving zombies too | Avoids a day of work with a negative expected return | None | None — a decision | No |

---

## 3. Leads — no number behind them, deliberately unranked

- **What dirties the HUD canvas at idle.** `compass.cs:32`, `ReticleController.cs:109` and
  `ObjectiveRadar.cs:52` all write unconditionally in `LateUpdate`. Any one would explain
  finding #5's idle half. Needs Deep Profile or a targeted marker.
- **12,700 B/frame of GC remains unattributed** after the exception is removed, and part of it
  is my own probe (`new FrameTiming[1]` every frame, ~15 `ToString` per row). One editor pass
  with GC.Alloc call stacks would name it.
- **94 of 655 materials use legacy particle shaders** that bypass the SRP Batcher, costing
  34–76 non-batched shader binds per frame. Re-authoring them onto URP/Particles/Unlit is a
  look change on ambient FX and needs eyeballing.
- **LOD cross-fade is unmeasured** — the player never moved during any capture, so no LOD
  transition ever happened.
- **`Animators.Update` still costs 0.30 ms/frame while the game is paused.** Unity's floor, or
  something re-dirtying the graph.
- **Every NavMeshAgent has the same avoidance priority**, so RVO never short-circuits. Invisible
  at 8 agents, and there is no 20- or 40-agent capture to establish the curve.
- **1009 of 2744 meshes have Read/Write enabled** and nothing in the project reads mesh data at
  runtime. Only ~2.4 MB in the reachable set; the risk is MeshCollider cooking.
- **Three water materials resolve to `Hidden/InternalErrorShader`.** Unreachable from the scene,
  harmless today, magenta the moment anyone drags one in.
- **776 MB of imported-but-unused Synty library** is the main reason the editor sits at 3.1 GB.
  Costs nothing in a build. Whether the handout ships it is an owner call, not a perf finding.

---

## 4. Observations on audio — reported, never recommended

Per the work order, nothing here proposes a change to playback behaviour.

- **The measured audio cost is a floor, not the shipping cost.** `playing_audio_sources` is
  **0.0** in seven of eight captures — the handout's clips are placeholder or empty, so almost
  nothing played. What the engine spends on near-silence: `UpdateAudio` **1.2–1.5 % of the main
  thread**, and `Audio.Thread` 1.6–3.2 % of wall clock on its own thread (which never counts
  against the frame).
- **Two-thirds of that audio thread is a fixed effect chain running on silence** — `VUFader`,
  `SFX Reverb`, `MasterDSP`. It is per-DSP-block, so filling the clips in does not grow it;
  adding sends, groups and filters does.
- **What it becomes with real clips is genuinely unknown.** 46 sources today; a 40-zombie wave
  adds 80 more plus 40 vocalisation coroutines, against a **60-real-voice ceiling** — so
  virtualisation will engage during a big wave, which is a *mix* outcome as much as a
  performance one. The project also loads the **Oculus spatializer** on a non-VR project, with
  zero sources currently opting in.
- **`AudioOcclusion` raycasts every frame with no distance or audibility gate.** One instance is
  0.12 % of the frame. If a student put it on all 46 sources — which is natural, since it is
  self-configuring — that is **0.30–0.53 ms/frame, 4–8 % of the combat frame**, plus 46 lowpass
  filters in the DSP graph. The behaviour is authored and stays; the note is only that the cost
  is per-instance-per-frame with nothing to amortise it.
- **`Fixed Timestep` must not be lowered** even though physics does nothing: the footstep
  trigger runs off `FixedUpdate`, so the rate is authored sound design.

---

## 5. What phase 3 would do, in order

If every recommendation were approved, in the order that maximises value per risk:

1. **Fix the exception** (#1). One bug, four symptoms, zero risk, and it unblocks measuring
   everything else honestly.
2. **Re-capture** S1, S4, S6 and S8 properly — player kept alive, 45 s of settle time, and
   `timeScale` checked at both ends. Without this the combat numbers stay estimates.
3. **The free settings wins**: terrain instancing and two-sided shadows (#3), shadow distance
   (#4), the duplicate Bloom override (#8).
4. **Forward+** (#2) — the biggest single lever, and the one that most needs measuring.
5. **The canvas work** (#5) and the zombie throttle (#7).
6. **Assets and guidance**: the audio-import paragraph (#9) and the texture caps (#10).

---

## Phase 3 results — measured, 2026-09-04

Three captures on the corrected protocol: 45 s settle, player pinned alive, `timeScale` and
`isDead` checked at both ends of every window, console read with a **cursor**. All twelve
windows valid; **0 game exceptions in-window** in every one. Uncapped, 1280x720, desktop
target. Capture stamp `20260904_fix`.

### Headline

| capture | scenario | fps | main ms | GPU ms | batches | draw calls | shadow casters |
|---|---|---|---|---|---|---|---|
| C1 guards only | idle | 150 | 4.77 | 3.09 | 2160 | 2247 | 717 |
| **C2 + settings** | idle | **170** | **3.88** | 3.30 | **662** | **731** | **223** |
| C1 guards only | 8 zombies | 94 | 6.99 | 3.46 | 1924 | 2009 | 650 |
| **C2 + settings** | 8 zombies | **121** | **5.71** | 3.42 | **668** | **740** | **226** |
| C3 old zombie behaviour | **40 zombies** | 75 | 10.61 | 4.82 | 866 | — | 289 |
| **C3 + zombie fixes** | **40 zombies** | **115** | **6.43** | 3.85 | 974 | — | 248 |
| C1 to C2 | tree view | 168 to 173 | 4.08 to 3.90 | 3.05 to 2.84 | 1556 to 1101 | — | 555 to 405 |

### What each fix actually did

| fix | measured | vs. predicted |
|---|---|---|
| **Singleton guards** | No frame-time change, and none claimed. 0 exceptions before or after. | The finding's 3.4 % was **wrong** — see the withdrawal above. |
| **Terrain instancing + single-sided shadows** | Part of a **69 % batch drop** and **67 % fewer draw calls** at idle. Larger than predicted: instancing removes the per-patch heightmap draws as well as the CPU build. | Predicted ~4 % of the main thread. Under-called. |
| **Shadow distance 40 to 25** | Casters **717 to 223** at idle (−69 %), 650 to 226 in combat. | Predicted ~280. Slightly better. |
| **Duplicate Bloom** | No measurable cost either way — it was a correctness fix. The level now has exactly one Bloom. | As expected. |
| **FXAA** | Idle GPU 3.09 to 3.30 ms (**+0.21 ms**), well inside budget on this target. | As expected: one cheap pass. |
| **Zombie throttle + health bars** | At 40 zombies, main thread **10.61 to 6.43 ms (−39 %)**, **75 to 115 fps**. At 8, 5.25 to 4.59 ms (−13 %). | The decisive fix. |

### The correction that matters most

The findings document said the level "already passes comfortably" on the desktop target.
**It did not.** At the 40-zombie wave the waves are configured for, C1 measured **56 fps at 44
zombies and 75 fps at 40** — below or barely at the 60 fps budget. The zombie work was
described as "genuinely optional"; it was the only fix that addressed the one scenario that
failed. After all six, 40 zombies runs at **115 fps**.

### Caveats on these numbers

- **GPU time rose 40 % in the C1-to-C2 combat-40 pair** while triangles rose 27 %. That pair
  is not like-for-like: zombies move, so the camera saw a denser view in the second run. The
  C3 before/after pair, taken in the same session, is the trustworthy zombie comparison.
- C1's combat-40 run actually spawned **44** enemies (the ring placement overshot); C3's
  spawned exactly 40. Compare C3 to C3.
- Applying terrain instancing caused Unity to rewrite `Assets/Inbox/Tidy/New Terrain.asset`,
  a 7.5 MB binary `TerrainData` (116 bytes smaller). **Left uncommitted** for the owner —
  this project's terrain rules make a silent 7.5 MB binary churn the wrong call for an agent
  to make alone.

### Forward+ and the texture caps, actioned 2026-09-04

Both were listed above as "think twice about". The owner asked for both to be actioned, so
both were applied and measured. **One is kept, one is reverted.**

**Forward+ is a regression on this project — reverted.** Applied to `URP_Renderer_Main`
(`m_RenderingMode` Forward → Forward+), captured, and rolled back. Same machine, same day,
uncapped:

| scenario | before | with Forward+ | after reverting |
|---|---|---|---|
| **40 zombies** | **115 fps** | **88 fps** | **115 fps** |
| tree view | 173 | 144 | 162 |
| idle | 170 | 138 | 141 |

The 40-zombie figure is the trustworthy one: three points, same session, and it returns
*exactly* to 115 on revert. The clustered lighting pass costs more than the GPU Resident
Drawer wins back at this scene's scale, so the drawer and its occlusion culling stay switched
on in the URP asset and stay inert. **Phase 1's estimate of an 11–26 % gain was wrong**; it
was labelled an estimate, and this is what measuring it produced. `Tools/Performance/Apply
Forward Plus` is kept, with the numbers in its summary comment, because "switch to Forward+"
is an obvious suggestion that will come round again.

(Idle did not fully recover — 141 against 170. Idle is the noisy scenario across editor
sessions, as Agent A's session-drift lead already flagged; the same-session 40-zombie triple
is the evidence to trust.)

**The texture caps are kept: 87 MB, no frame-time cost.** `texture_bytes` 960 → 873 MB, held
after the Forward+ revert. Seven files, import settings only:

| file | was | now |
|---|---|---|
| `FishingRod_Texture_01_A` | 4096×4096 DXT1 | 2048×2048 DXT1 |
| `PolygonHorror_Texture_01_A` | 4096×4096 DXT1 | 2048×2048 DXT1 |
| `Skybox_Diff`, `Skybox_No_Snow_Diff` | 4096×2048 BC7 | 2048×1024 BC7 |
| `Skybox_Normal` | 4096×2048 DXT5 | 2048×1024 DXT5 |
| `Walkie Talkie Icon` | 817×817 **RGBA32** | 256×256 DXT5 |
| `Torch Inventory Icon` | 585×585 **RGBA32** | 256×256 DXT5 |

⚠ Two things worth knowing for next time. The effective cap is the `DefaultTexturePlatform`
entry in `platformSettings`, which is what `TextureImporter.maxTextureSize` reads and writes —
the `maxTextureSize` near the top of a `.meta` is a legacy field and setting it does nothing.
And the two icons were uncompressed *not* because compression was off (it was set to
Compressed) but because 817 and 585 are not multiples of 4, so Unity silently fell back to
RGBA32; capping them at 256 made them a compressible size as a side effect.

## 6. STATUS

Phases 1 and 2 complete. **Phase 3 applied and measured 2026-09-04** — see the section
above. Outstanding: the owner saves the scene, looks at the before/after screenshots, plays
a combat check, and decides on the terrain asset and the startup-log question.
