# Code Structure — 26T1 AUD216 Project 1

> Originally generated from a full script audit on 2026-07-06; **revised 2026-08-18
> (post-roadmap-tasks)** to match the code after three fix/feature rounds and the ten
> ROADMAP_TASKS items. This is the primary orientation
> document — an AI session should read this first, then the ground rules in
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md), and run
> `bash Documentation/tools/compile-check.sh` after every edit.

## Project facts

| | |
|---|---|
| Unity version | 6000.0.72f1 (Unity 6; upgraded from 6000.0.62f1 on 2026-08-25) |
| Render pipeline | URP 17.3.0, Linear color space |
| Input | Legacy Input Manager only (`Input.GetKey`/`GetMouseButton`) |
| Main scene | `Assets/Level for Sound Design.unity` |
| Audio mixer | `Assets/Inbox/AudioMixer.mixer` |
| Scripts | 142 runtime + 19 editor `.cs` files (161 total, counted 2026-09-02), all in monolithic `Assembly-CSharp` (no `.asmdef`s) |
| Version control | git (baseline `21b5b64`, 2026-07-06; 110+ commits since, one per work item) |
| Verification | `Documentation/tools/compile-check.sh` compiles both assemblies via Unity's bundled Roslyn — the project's only automated check. It cannot see scene wiring or hear audio. |
| Terrain | Two terrains in the main scene; layers in `…/Nature/Scenes/Terrains/TerrainLayers/` — see [TERRAIN.md](TERRAIN.md) |
| Adaptive music | Vertical + horizontal layering built into `MusicManager` — see [MUSIC_LAYERING_PLAN.md](MUSIC_LAYERING_PLAN.md); **live since 2026-08-25** (three `MusicStateDefinition`s assigned, MUSIC bus unmuted). Known content issues: Audit-2 findings 9–10 |
| Art/content | Synty low-poly packs (educational license), mostly under `Assets/Inbox/` and `Assets/PolygonNature/` |
| Ported audio | The only clips here not the owner's are two reload takes in `Assets/Audio/Weapons/` (`SMGReload_SFX.wav`, `Revolver_Reload_SFX.wav`, both 96 kHz 24-bit mono) — **Liam Chapman, 26T2 AUD216**, brought over 2026-09-01 because this project recorded no reload audio at all. Credit him if this is graded or ships, or replace them. ⚠ His provenance is undocumented. A third take, `RevolverReload_SFX.mp3`, was dropped: Unity's importer reported it truncated (8,704 bytes short), and at 44.1 kHz 192 kbps it did not match the two recordings. |

The project is a small first-person survival game (zombies, waves, day/night cycle,
fishing, objectives) used as a vehicle for **sound design** — the gameplay is provided;
the audio content and its implementation are the deliverable. That priority shapes every
judgement call in the codebase: audio-state bugs (double-fired sounds, loops that outlive
their owner, music that never relaxes) outrank almost everything else.

## Work history (what has already been done — don't re-fix)

| When | Round | Record |
|---|---|---|
| 2026-07-06 | Full audit + fix phases 0–5 (audible bugs, crash-proofing, dead code, consolidation, editor polish) | [AUDIT_FINDINGS.md](AUDIT_FINDINGS.md) *(historical)*, [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) |
| 2026-08-17 | Terrain-layer recovery (hygiene pass had deleted all 34 `.terrainlayer` assets) | [TERRAIN.md](TERRAIN.md) |
| 2026-08-17 | Objectives-system deep dive, phases 2.1–2.6, **all implemented and play-verified** | [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md) |
| 2026-08-18 | Gameplay-systems audit (weapons, inventory/fishing, player, enemies); code fixes committed, **scene/asset work remains** | [IMPROVEMENT_PLAN_3.md](IMPROVEMENT_PLAN_3.md) |
| 2026-08-18 | Adaptive music feature (M1–M6) + three audit passes over it | [MUSIC_LAYERING_PLAN.md](MUSIC_LAYERING_PLAN.md) |
| 2026-09-03 | Bottom-right current-weapon HUD panel (Synty `HUD_CurrentWeapon_Rack01`, the rusty box from the pack's FPS demo; the Minimal `GunAmmo` variant was tried the same day and reverted as a worse style match): `CurrentWeaponHUD` + `WeaponController.OnHudStateChanged` + `WeaponItem.hudIcon`; built in-editor by `Tools/Weapons/Create Current Weapon Panel` | this file (Editor tooling) |
| 2026-09-03 | Directional hit feedback: `PlayerStats.TakeDamage(amount, attacker)` + `DamageDirectionIndicator` rotate the existing Synty edge-of-screen smear/hit-lines toward the zombie or boss that landed the hit (the pack demo's behaviour); source-less damage keeps the old top-edge pose | this file (UI row, integration map) |
| 2026-09-03 | Audio progress report reworked for the handout: everything in `Assets/Audio/Demo sounds` counts as not done (BEEP for beeps, REPLACE for the other demo clips), spaced headings without the GameObject suffix, Footsteps first, placeholder rows without clip names, category prefixes dropped, profile entries named by their type/tag fields; `InteractionSoundProfile` filters the clip slots its type never plays (218 rows → 25); the handout reset gives footsteps their own beep instead of emptying them | this file (Assessment row, overlays, tooling) |
| 2026-09-03 | Audio-manager tidy (work order, four parallel agents): report clip labels shortened, interaction profiles sorted alphabetically, Cooking lost collect/burn, `PlayerSoundProfile` slot filter, legacy `MusicController` removed from the scene, `PickupsAudioManager` moved under `Audio Managers`. Report 118 → 109 slots | [AUDIO_MANAGER_TIDY_TASKS.md](AUDIO_MANAGER_TIDY_TASKS.md) |
| 2026-09-03 | Owner's answers to the tidy checkpoints: four label overrides (`Stamina ▸ Depleted`, plural headshots/phase cracks), a **hunger warning loop** as the heartbeat's twin, and `WaveAudioManager` deleted — a class absent from every scene and prefab, whose slots students never saw. Report 109 → 110 slots | [AUDIO_MANAGER_TIDY_TASKS.md](AUDIO_MANAGER_TIDY_TASKS.md) |
| 2026-09-03 | Owner deleted the skull/chest collectathon; its audio manager removed from the scene and its classes queued for deletion after the save. ⚠ That chest was the only `ShowVictory()` caller. Report 110 → 105 slots | this file (`Pickups/` row) |
| 2026-09-03 | Report presentation, round three: footstep surfaces capitalised, hand-placed section order (Footsteps ▸ Waterfall ▸ River on top, Bullet Impacts last), and a **Waterfall** section reading the waterfall `AudioSource`'s own clip — the assessed slot students fill directly, previously invisible to the report. 105 → 106 slots | this file (Assessment row) |
| 2026-09-04 | Pickups phase (b): the owner saved, and the five scripts under `Assets/Scripts/Pickups/` were deleted with the folder, along with `EndGameScreen.ShowVictory` and the reset tool's entry. No win condition remains, by the owner's decision | this file (integration map) |
| 2026-09-04 | Profiling run (work order + 4 analysis agents): a runtime `ProfilerRecorder` probe and `Tools/Profiling/` capture menu, seven scenarios, ranked findings. Four of the captures turned out flawed (two taken behind a death screen with `timeScale` 0, two during warm-up) — the corrections are the most useful part | `PERFORMANCE_PROFILING_TASKS.md`, `PERFORMANCE_FINDINGS.md` |
| 2026-09-04 | Performance fixes (work order + 3 parallel agents): `ObjectiveManager.Instance` guarded at every game-code dereference; `Tools/Performance/` applying terrain instancing, single-sided terrain shadows, shadow distance 25, FXAA, and the duplicate Bloom removed; zombie repath throttled to 4 Hz with a phase offset and health bars gated on damage + 25 m. **40 zombies: 75 → 115 fps. Idle: 150 → 170 fps, draw calls −67 %** | `PERFORMANCE_FIX_TASKS.md` |
| 2026-09-04 | **XP was never granted for a kill.** `xpReward` was authored on all 11 enemy prefabs and exposed as `XPReward`, but nothing read it — so tripling the numbers alone would have multiplied zero. The grant is now in `ZombieAI.Die`, values tripled (Yokai 25→75, bosses 500→1500), and completing an objective pays `objectiveXpFraction` (0.1) of the **current** level's cost rather than a flat number | this file (Player + Objectives rows) |
| 2026-09-04 | **Forward+ measured and reverted** — a regression here, not the estimated 11-26 % win: 40 zombies 115 → 88 → 115 fps on revert. The GPU Resident Drawer stays on in the URP asset and stays inert. Texture caps kept: 7 files, **87 MB** off texture memory, no frame cost | `PERFORMANCE_FINDINGS.md` |
| 2026-09-05 | **Grenade** (work order + 3 parallel agents): pipe-bomb pickup carried with a count, thrown on fire, bounces on the bullet-impact surface sounds, 3 s fuse, 5 m blast with line of sight, self-damage + shake + a generated ringing. Two new assessed audio slots (report 198 -> 200). `WeaponController.CurrentAmmo`/`MaxAmmo` became virtual and gained a protected HUD raiser; `Item` gained `TryMergeIntoInventory` | this file (Weapons, Inventory, Audio rows) |
| 2026-09-05 | Grenade audio slots were invisible in the inspector: `WeaponAudioManagerEditor` hand-draws each profile's fields and had no `case WeaponType.Grenade`, so the section rendered empty. Added the branch (Throw, Explosion, pitch, max distance) | this file (Editor row) |
| 2026-09-06 | **Minigun** (3 parallel agents): 0.7 s spin-up before the first round, spinning barrel mesh, 200-round belt, crosshair. **Three new assessed slots** (spin up / loop / down) on top of the gun set - report 200 -> 209. Four additive base changes: `ShouldShowReticle` virtual (the rule was hard-coded to weapon NAMES), `shootableLayerMask` protected, `TrySpendRound()` protected | this file (Weapons, Audio, Editor rows) |
| 2026-09-06 | The SMG's and Revolver's six reload slots had **no inspector field at all** - the `PropertyField`s were commented out while the report asked for them regardless. Restored for Gun and Minigun. Second instance of the hand-drawn-inspector trap in two days | this file (Editor row) |
| 2026-09-06 | Minigun barrels spun like a propeller: the controller rotated about `Vector3.forward` while the mesh's barrels run along local **Y**. The axis is now **derived from the fire point** (barrel to muzzle IS the barrel axis), with a hand override and a reverse checkbox. Verified numerically, not by eye | this file (Weapons row) |
| 2026-09-06 | Hotbar shows Synty's yellow **equipped** border. The `Selected` child existed in every slot, disabled, and nothing referenced it. ⚠ Took three attempts: the Button's **Animation transition** owns that child in every clip | this file (UI row) |
| 2026-09-06 | Compass moved top-right. It already existed and drew top-centre; the job was to move it. NOT into the `Top Right` region - that is at scale 1.1 and overhangs the canvas top | this file (Editor row) |
| 2026-09-06 | Grenade reskinned onto the owner's `Assets/Grenade.fbx` with `ICON_SM_Wep_Grenade_01_BR`; the tin can was a stand-in from when no grenade mesh existed. Physics and the `Grenade` component untouched - a throw that still detonates is the proof | this file (Weapons row) |
| 2026-09-06 | **Wind chimes** hang in the birch 14.5 m from the spawn: one looping clip slot for students (report 209 -> **210**), and a procedural sway. The slot is on the **Audio Managers** object with every other manager (the owner's call) and the prop keeps only its own level and rolloff. NOT in the nearer willow - a willow is a curtain of fronds and the prop was invisible inside it at 1.6 m, photographed before moving | this file (Audio, Animations, Editor rows) |
| 2026-09-06 | Minigun **pointed at the floor**: the FBX's barrel runs down local -Y and its grip sits at +Z, so identity aimed it down, in the hand and on the pickup. Rotation derived from the model's own locators, not guessed. The muzzle locator is turned back downrange separately - the flash and tracers fire along it | this file (Weapons row) |
| 2026-09-06 | Minigun belt 200 -> **2000** rounds at the owner's request, and the round counter re-fitted: a 90 px box at 64 pt WRAPPED on four digits and showed '20'. No weapon in this level had ever had four digits | this file (UI, Weapons rows) |
| 2026-09-06 | **Damage dust** instead of a destruction system (scoped, questioned, declined as too ambitious): the 40 breakable props puff at every hit, more as they weaken, and throw a cloud the size of their own bounds on the frame they vanish. Two world-space ParticleSystems for the whole level, `Emit()` only, nothing instantiated per shot. Every weapon now goes through `DamageableUtil.Damage` with the hit point | this file (Weapons, Editor rows) |
| 2026-09-06 | **Minigun audio is five clips, not one per round**: a wind-up whose LENGTH is the wind-up (the first round leaves when it ends), a spin loop, a shooting startup handing over sample-accurately to a shooting loop, and a shutdown. The owner's clips wired; Shots and Spin Down slots gone; report 210 -> **211** (10 minigun slots). Verified frame by frame with `Testing/MinigunAudioProbe` | this file (Audio, Weapons, Editor rows) |
| 2026-09-06 | Minigun **spin wind-down**, a sixth clip: a tap used to cut to silence. Now a trigger let go during the wind-up plays `MinigunSpinWindDown` from the offset matching how far the barrels got, and they stop when it ends (0.41 -> wind-down from 1.27 s of 2.15, both ended 0.89 s later). Report 211 -> **212** | this file (Audio row) |
| 2026-09-06 | Minigun **tap rule**: a tap hears the wind-down's LAST N seconds where N is how long the wind-up ran (0.2 s press -> 0.2 s of wind-up, then the final 0.2 s of wind-down), barrels down over the same N; and a **0.15 s floor** so a two-frame click is not two frames of each. Measured 0.207 / 0.208 / 0.215 s, and 0.153 / 0.153 / 0.300 s for the click | this file (Weapons, Audio rows) |
| 2026-09-06 | **Tap silence was the clip, not the code**: `MinigunSpinWindDown` measures -46 to -67 dBFS over its last half second, so the owner's 'last N seconds' rule played its fade-out. A tap now plays the wind-down's FIRST N seconds, faded over 50 ms at the cut; `spinWinddownTail` on the profile switches back to the end for a clip authored to stay loud | this file (Audio row) |
| 2026-09-06 | Compass strip **back top-centre** (the owner's original ask was misread: keep the long one, ADD a round one). Round compass top-right built from the pack's `HUD_Minimap_Dial_Minimal` with its demo map and sample icons stripped, driven by `CircularCompass`; N verified against world north numerically (dot 1.0000 at heading 48.8). The owner's `minigunIcon` / `minigunGraphic` PNGs imported as sprites and wired | this file (UI, Editor rows) |
| 2026-09-06 | **Minimap** top-right - the owner's 'compass' meant this. The pack's `HUD_Minimap_Dial_LightsWires01` with a live top-down camera in its mask (north up, view cone turns, enemies and objectives as the pack's icons on the same metres-to-pixels scale as the map). `CircularCompass` retired. Checked: marker at -heading, an enemy 20 m east drawn 94.3 px east as computed, the texture genuinely rendering. **Costs ~1.1 ms a frame** at every-3rd-frame rendering; `renderEveryNthFrame` is the knob | this file (UI, Editor rows) |
| 2026-09-06 | Interaction prompt (`HUD_Interact_ContextSensitive_Object`, every 'Press E to …') moved out from under the minimap to its left, placed by the union of its DRAWN graphics rather than its mostly-empty 600 px root. ⚠ Its action label resizes to its text and grows towards the map, so the gap is set against the longest string the code shows ('inventory full!' clears by 18 px) | this file (Editor row) |
| 2026-09-06 | **XP toast** wrapped off its strip: the objective award is a tenth of the level threshold (50.625, 75.9375) printed raw into a fixed 220 px prefab. Now rounded (`+76 XP`), NoWrap, and the strip grows to the text (`FitXpStripToText`) | this file (Player row) |
| 2026-09-06 | **Drop is a double-tap** of the slot's number key (0.6 s, unscaled), replacing Shift+number - Shift is also sprint. Title screen says `1-5 twice drop`. Verified: two taps 1.2 s apart select, two in a frame drop the item 0.8 m ahead | this file (controls table) |
| 2026-09-06 | **Mushrooms feed**: eating one restores 15 % of the hunger meter (`hungerRestoredFraction` on `MushroomTripInteraction`, through a new `PlayerStats.MaxHunger`). 50 -> 65 measured | this file (Interactions row) |
| 2026-09-06 | Progress report says **MISSING** in red for every slot not implemented, in the Ctrl+G overlay, the editor window and the export - EMPTY / BEEP / REPLACE are gone from what students see (the scanner keeps the distinction for the validator). Report at 216 slots, 42 missing; the minigun's six and the wind chime are in it | this file (Assessment row) |
| 2026-09-06 | **IMGUI overlays scale with the screen**: the progress report, event monitor and music debug panels were a fixed 2x - 1120 px is most of a 1370 px Free Aspect view and a quarter of a 4K one. `AudioOverlays.ResolutionScale` makes them the same fraction of any screen (reference height 1370, the view they were tuned on) | this file (Assessment row) |
| 2026-09-06 | **Settings menu background at every aspect**: its layers were sized by sizeDelta/localScale hacks that landed the striped vignette sprite at exactly the canvas width on 16:9, so its rounded corners showed at 4K. `Tools/HUD/Stretch Settings Background` gives them stretch anchors with 20 % overflow; measured -768..4608 on a 3840 canvas and -500..3001 on a 2501 one | this file (Editor row) |
| 2026-09-07 | Work order written for **enemy roles and combat feedback** (`ENEMY_ROLES_AND_FEEDBACK_TASKS.md`): four roles as `ZombieRole` components beside `ZombieAI`, six enemy slots + two UI slots (216 -> 224), a `ZombieAI.OnZombieHit` event and `IZombieDamageModifier` hook as the integrator's Phase 0. Not started | that file |
| 2026-09-07 | **Enemy roles**: Ghoul runs (growl, 0.5 s still, 2.2x for 2 s, then a pant), Butcher blocks (120 deg front arc at 30 %, headshots bypass), Demon spits (a retch, then a lobbed projectile solved from the parabola), Yokai screams (1.4 s call, 20 m rally at 1.5x for 5 s, boss excluded). Six agents in parallel over one integrator; six new assessed slots. **Measured in play**: shield 3.00 / 10.00 / 20.00 for a 10-damage hit, runner's scale 2.20 held 2.06 s then 1.00, screamer's neighbour 1.50 with the **boss at 1.00**, spit landing 1.67 m from the player's launch position for exactly 12.0 | this file (Enemies, Enemies/Roles, Audio, Editor rows) |
| 2026-09-07 | **Combat feedback**: a hitmarker on the reticle (white / red on a headshot), two UI confirmation slots, kill XP popping at the corpse, and the screen kick authored **per weapon** - `ApplyRecoil`'s two hard-coded branches meant the minigun, crossbow, axe and grenade all shook by one default nobody chose. Report 218 -> **224** (the two UI slots are single fields and registered themselves; only the six enemy ARRAYS needed sizing). **Measured**: marker white/1.25 on a hit and red/1.50 on a headshot, corpse toast within a pixel of the projected corpse in x, six weapons with six distinct shakes. ⚠ The SMG was authored at magnitude 0.28, so its old effective kick was 0.196 and is now 0.035 - a real change in feel, needing an ear | this file (UI, Player, Weapons, Assessment rows) |
| 2026-09-07 | Four things the work order got wrong, each caught by an agent reading the code: `CanAct` is false inside a borrow (it would have abandoned every tell on its first frame); `ZombieRole` cached the AudioSource before `ZombieAI` creates it (silent mute); a grenade's blast DOES reach the damage modifier; and an empty `AudioClip[]` records **no** report row, so the eight new slots needed sizing to 1 or would never have appeared | this file; the work order's STATUS |
| 2026-09-07 | ⚠ Noted while verifying, **pre-existing and left alone**: `ZombieAI` has four `agent.isStopped` calls guarded only against null, never against `isOnNavMesh`, so a zombie whose agent has gone off-mesh logs an error. `BossZombieController` and every `ZombieRole` guard theirs | this file (Enemies row) |
| 2026-09-08 | Work order written for the **audio integrity pass** (`AUDIO_INTEGRITY_TASKS.md`), from four read-only audits of `0dc67c4`. The report is wrong both ways: **thirteen played slots have no row** (five `headshotSounds: []`, the mushroom `dialogueLines`, the boss's five - `BossZombieController` has no scene instance so the scanner never meets it - and the rod's three tiers x two), and **three offered slots never play** (`PlayReloadEndSound` has no caller, `PlayReloadStartSound` only the crossbow's, `interactionSounds` nothing). Fifteen audio-state findings, the worst a `Time.deltaTime` crossfade that freezes under the death screen and `AudioUtil` resetting the boss's phase pitch. Owner's decisions: audio keeps running under pause and death, only stuck fades are fixed; the three music states get one empty loop stem each so they count (224 → **244**); the enemy radar's rebuild-per-spawn stays a separate ticket | this file (Assessment row, once implemented) |
| 2026-09-09 | **Audio integrity pass** (`AUDIO_INTEGRITY_TASKS.md`): six agents on disjoint files, one integrator. The report went **224 -> 244** - thirteen played slots had no row, three offered slots never played - and the export diff is additions and relabels only. Fifteen audio-state findings fixed; the probe's nine measurements all PASS; a regression night spawned 8, registered 8, intensity 0 -> 1.00, cleared to 0 / 0.00; zero console errors | this file (Assessment, Audio, Audio Systems, Area Management, Enemies, Weapons, Waves, Camera, Testing, Editor rows) |
| 2026-09-09 | What the order got wrong, and what the first probe run found: the music states had **no loop segment at all** (only intro and outro), so the tool creates the segment too and each asset now logs a true one-time `OnValidate` warning until a clip is authored; the SMG's and revolver's reload-action clips are **dangling references** (removed in `5f1f18d`), so every reload logged `PlayOneShot(null)` - the action method now treats a null element as the MISSING slot it is; and the probe itself first picked an unused Fort Golem and compared the boss's id alias `RockGolem` to the name `Rock Golem` | this file; the order's STATUS |
| 2026-09-09 | ⚠ Noted, **not fixed**: the crossbow has **no `WeaponAudioManager` profile** (five exist: SMG, Revolver, Axe, Grenade, Minigun), so its reload start, reload end and shot all log a Miss and it contributes zero report rows - the one weapon a student cannot be marked on. A data fix (one profile entry, type Gun) that moves the count; the owner chose on 2026-09-09 to leave it for a later order. Also ⏸: the SMG reload take (2.16 s) outlasts `reloadTime` (1.5 s), so End now lands inside the Action take | this file (Weapons row) |
| 2026-09-10 | **Handout mode** (`AUDIO_HANDOUT_TASKS.md`): four agents on disjoint files, one integrator. The report's walk hands back a reference per slot and per routing reference; a state asset; three tools (snapshot, switch, reinstate) and a self-test. **Measured**: the walk finds 244 slots (230 scene, 11 prefab, 3 asset) and 122 routing references (97 scene, 25 prefab); the switch beeps 197, footstep-beeps 33, empties 14, clears 66 routing references that were set; Handout reads **0/244**; Reinstate reads **174/244** with an export that differs in **0 lines** and 22 files byte-identical; self-test **11/11** | this file (Assessment, Editor rows) |
| 2026-09-10 | What the first self-test run found: three `Mushroom` siblings share a hierarchy path so the routing sweep's order changed per run (sibling indices break the tie now); ten prefabs no tool had saved under Unity 6 were reserialised on the first round trip - format only, committed as such; and git's CRLF checkout vs Unity's LF writes made every re-saved file look different until the comparison normalised line endings | this file; the order's STATUS |

**Open work, in case a session is looking for it (updated 2026-09-02):** every roadmap,
port and engagement-wave feature is implemented, scene-wired and shipped in a playable
Windows build (`Builds/EMBERWATCH/`, gitignored). The adaptive music is **live** (since
2026-08-25). What remains is **authoring and placement**, not code: the clip slots the
Ctrl+G report lists as empty/placeholder (reloads, dialogue, the tape, well splash,
dawn chorus, heartbeat, flashlight, cooking, fishing, walkie talkie, turntable, fish tiers, boss
set), running Add Turntable Interaction, and Plan-3's genuinely
open items (A4's optional `endScreen`, A5 FOV curve,
A7 `firePoint`s, A8 pickup variations, C5–C7, D1/D3/D4/D6/D7/D9–D12), plus the ⏸
play/listen checks recorded in the work orders.

## Folder map (`Assets/Scripts/`)

| Folder | Purpose | Key classes |
|---|---|---|
| `Audio/` | Per-domain audio managers | `FootstepAudioManager`, `EnemyAudioManager`, `WeaponAudioManager`, `PlayerAudioManager`, `UIAudioManager`, `MusicManager` (adaptive — see below), `InteractionAudioManager`, `RiverAudioManager`, `AudioMixerController`, `FireSourceAudio`/`CampfireAudio`, **`WindChimeAudioManager`** + **`WindChimeAudio`** | **The minigun's six clips** (`spinWindupSound`, `spinWinddownSound`, `spinLoopSound`, `shootingStartupSound`, `shootingLoopSound`, `shootingShutdownSound`) play on **three dedicated AudioSources per profile** - spin loop, shooting loop, and a *transition* source that carries wind-up → startup → shutdown in turn - through four calls the controller makes: `StartSpinWindup(name, fraction)` (plays the wind-up from the barrel's current offset and **returns the clip length, which the controller uses as its wind-up time**), `BeginShooting` (spin loop and startup now, shooting loop `PlayScheduled` for the exact sample the startup ends - `ExactLength` from the sample count, because a float `.length` is samples short and a seam between two clips of gunfire is heard at 1 ms), `EndShooting(name, roundsWereLeaving, winddownTailSeconds)` (both loops stop dead; then the **shooting shutdown** from its start if rounds had left the gun, or **`winddownTailSeconds` of the spin wind-down** after a tap — its FIRST N seconds by default (`WinddownTail.StartOfClip`, faded over the last 50 ms from the manager's `Update` and stopped, because a hard `SetScheduledEndTime` cut clicks) or its last N (`EndOfClip`); ⚠ the owner's clip is -46 to -67 dBFS over its final half second, which is why 'the end part' was silence for every tap under half a second - the controller passes how long the wind-up ran, `spinFraction x windupSeconds`, so a 0.2 s press hears 0.2 s of wind-up and then the final 0.2 s of wind-down; **returns the seconds of sound it started**, and the controller sets `winddownSeconds = played / fraction` so the barrels lose what they have left in exactly that time. Measured 2.17 s barrels vs 2.19 s sound on the shutdown, 0.215 vs 0.229 on a 0.2 s tap), `StopMinigunAudio` (everything, no shutdown - death and teardown). ⚠ The transition clips are `Play()` on their own source, never `PlayOneShot`: a PlayOneShot voice cannot be stopped on its own or started part-way through, and both are required (a re-press cuts the shutdown and resumes the wind-up from the barrel's fraction). The old `spinUpSound` is `[FormerlySerializedAs]` onto `spinWindupSound`, so the scene's assignment survived; `spinDownSound` is gone. `WeaponSoundProfile` carries `throwSounds` and `explosionSounds` for `WeaponType.Grenade`, and `PlayExplosionSound(name, position)` plays them on a **detached, spatialised** source at the blast - the grenade is destroyed the same frame, so a one-shot on its own source would be cut off. WARNING: the Grenade profile hides raise/lower from the report even though a grenade is raised and lowered in hand: the owner chose two assessed slots, and the report is the list of work a student is marked on. **Enemy roles carry six assessed slots** on `EnemySoundProfile` — `lungeSounds`, `blockSounds`, `spitWindupSounds`, `spitSounds`, `spitImpactSounds`, `screamSounds` — played by `PlayRoleSound(name, RoleSound, source)` at the zombie and `PlayRoleSoundAt(name, RoleSound, position)` detached and spatialised where a spit lands (`PlayExplosionSound`'s shape, for the same reason: the thing that made the sound is gone by the time it is heard). The profile now implements **`IAudioSlotFilter`**, keyed on its `enemyRole`, so a Butcher is never marked on a scream it cannot make. ⚠ **A slot is the tell, not the decoration**: the growl, the clang, the retch and the call are what a player reads the fight by, so an empty one costs the mechanic its warning, not just its polish. ⚠ Found while adding them, and fixed: **`headshotSounds` had no line at all in the hand-drawn inspector** — a sound `ZombieAI` plays on every head hit, which the report even carries a label for, and which no student could ever have filled in (the fourth time this trap has bitten this project). ⚠ Found and **not** fixed, out of scope: `PlayAttackSound` / `PlayDeathSound` / `PlayHeadshotSound` each declare their no-repeat index as a **local** reset on every call, so "never the same clip twice running" has never worked for enemies; the role slots keep theirs on the profile and the comment names the older bug. **Wind chimes** are split the way `RiverAudioManager` is: **`WindChimeAudioManager`** on the Audio Managers object holds the ONE assessed clip (the owner's call - every slot a student fills is in one inspector), and **`WindChimeAudio`** out at the prop owns the AudioSource, the level, the rolloff and the fade, so two sets could share a clip and still be balanced apart. The manager pushes the clip in `Awake`, which always precedes any `Start`, so the chime plays in its own `Start` with no execution-order setting to remember. ⚠ **`WindChimeAudio` deliberately has no AudioClip field** - one there would be a second assessed slot, sitting empty on the prop for ever. ⚠ `SetLoop` restarts on "has `Start` run", **not** on "is it playing": measured 2026-09-06, a clip pushed in later found `isPlaying` false (the slot was empty at load, so `Start` played nothing), took the clip and stayed silent. Neither component needs a custom inspector or any registration - the default inspector draws the field, and the report scanner walks serialized AudioClip fields, so the field IS the registration. **2026-09-09 (audio integrity pass)**: `EnemyAudioManager`'s no-repeat for attacks / deaths / headshots is per-profile memory (`lastAttackIndex` etc., beside `roleIndices`) - it was a local reset on every call and had never worked (measured: 50 plays, 0 consecutive repeats); every enemy one-shot goes through `PlayKeepingSourcePitch`, which puts the source's pitch back after `AudioUtil.PlayRandomOneShot` so the boss's per-phase pitch survives its next vocalisation (measured 1.080 -> 1.080). ⚠ **`AudioUtil` is not the place for that fix**: its reset-to-1 is load-bearing for the weapon and footstep sources, which are played both randomised and plain. `ConfigureFallbackVoice` routes a source a zombie had to invent. `WeaponAudioManager`: `ReloadRoutine` plays Start, Action, End (End had **no caller anywhere**, Start only the crossbow's - students authored both for nothing); a null reload-action element is a MISSING slot, not a `PlayOneShot(null)` warning per reload - the SMG's and revolver's action clips are dangling references since the owner's 2026-09-03 commit. `FootstepAudioManager` passes the profile volume per voice instead of writing the shared source's volume (same product; that source is also `UIAudioManager`'s diegetic one, so a non-1 footstep level was rescaling UI blips). `PlayerAudioManager` guards the heartbeat threshold at 0 and stops a loop whose clip is cleared mid-play. `FireSourceAudio` / `CampfireAudio` deleted: on no scene object or prefab, by name or by guid.
| `Audio/Music/` | Adaptive music internals (plain C# helpers owned by `MusicManager`) | `MusicStateDefinition` (SO: segments/stems), `MusicTransitionRule`, `MusicConductor` (DSP clock), `LayeredStatePlayer` (double-banked stem playback), `MusicIntensityConfig`+`MusicIntensityDriver`, `MusicDebugOverlay` |
| `Audio Systems/` | Reusable audio components | `AudioUtil` (shared random-one-shot helper used by 4 managers + `RandomTriggerAudioVariations`), `AudioProfileKeys` (forgiving, loud profile-name lookups), `AudioProfileIds` (compile-checked profile identities), `MixerDip` (the snapshot-dip-that-must-never-latch pattern, shared by the trip, the boss death silence and the heartbeat — Restore only acts when its own dip is live, so one feature's teardown cannot stomp another's snapshot), `AudioOcclusion`, `DirectionalAudioSource`, `AudioRandomizer`, `PlayRandomSound`, `RandomSoundSpawner`, `RandomTriggerAudioVariations`, `StartSoundAtRandomPointInClip` 2026-09-09: `AudioOcclusion` and `DirectionalAudioSource` lerp on **unscaled** time so a sweep finishes under the pause menu (the owner's policy: audio keeps running under pause and death, only stuck fades are bugs); `AudioRandomizer` takes a mixer group and skips its interval when `Camera.main` is null. |
| `Area Management/` | Zone-based ambience/music switching | `AmbienceManager`, `MusicZoneManager` (self-contained single-clip zones), `TriggerManager`, `LocationTrigger` 2026-09-09: `AmbienceManager.CrossFadeToState` advances on unscaled time, so a crossfade caught by `timeScale = 0` finishes there instead of sitting at half volume under the death screen (measured: old bed 0.000, new bed at target, 3.5 s into a frozen clock). |
| `Footsteps/` | Surface detection for footsteps | `SurfaceDetector`, `TerrainTextureDetector` (layer-index → surface map, see [TERRAIN.md](TERRAIN.md)) |
| `Character/` | Player movement | `FirstPersonController` (~600 lines), `SimpleCameraController` (debug) |
| `Enemies/` seams | Two additions to `ZombieAI` (2026-09-07) so nothing else has to reach into it | `ZombieHit` + `static event OnZombieHit`, `interface IZombieDamageModifier` | Damage funnels through one private `ApplyDamage`, because the positional overload used to call the flat one and both reporting would be two hits per bullet. ⚠ **`OnZombieHit` is raised only from the POSITIONAL path.** Every weapon reaches a zombie through `DamageableUtil.Damage`, which routes to that overload — bullets, arrows, the axe **and the grenade's blast**. The flat `TakeDamage(float)`'s one remaining caller on a zombie is `BossZombieController` collapsing its own summons with `float.MaxValue`; reporting those would flash the player's hitmarker for kills they did not make. ⚠ For the same reason **a blast IS offered to `IZombieDamageModifier`** and is blocked by a shield's front arc like anything else — the work order claimed otherwise and was corrected by reading `DamageableUtil`. |
| `Player/` | Player state | `PlayerStats` (health/hunger/stamina/XP; `TakeDamage(amount)` for IDamageable callers and `TakeDamage(amount, attacker)` for enemies, which aims the edge-of-screen hit smear at the attacker through `DamageDirectionIndicator`). ⚠ Lives on a **child** GameObject of the Player, not the root — see Plan-3 A1 | **Kill XP pops at the corpse** (`AddXP(float, Vector3)`, 2026-09-07): the same `HUD_XPLog` toast, placed by projecting the world point and converting with `RectTransformUtility.ScreenPointToLocalPointInRectangle` (camera argument **null** — the canvas is Screen Space Overlay), then subtracting the toast's own anchor origin, because `local` is measured from the parent's pivot and `anchoredPosition` from its anchor. A corpse behind the camera falls back to the bottom toast rather than drawing at a mirrored point; objectives and fish keep the bottom unchanged. ⚠ The overload could not be a body-only fill: the grant, the level-up loop and the bar all live in `AddXP(float)`, so a nullable field is set around that one call and consumed by the toast — duplicating the grant is how XP gets paid twice. **XP model:** level 1→2 costs `baseXpToLevelUp x firstLevelXpFraction` (50 by default — deliberately cheap); level N→N+1 costs `baseXpToLevelUp x 1.5^(N-1)`, from the level rather than compounding, so the first-level discount does not propagate. `AwardObjectiveXP()` grants a **share of the current threshold**, not a flat amount, so an objective is worth the same fraction of a level at level 1 and level 10.
| `Weapons/` | Ranged + melee weapons. Firearms carry a magazine, an `R` reload and aim-down-sights (ported 2026-09-01 from Liam Chapman's submission); every weapon raises the static `WeaponController.OnHudStateChanged` on raise/lower/shot/reload and exposes `CurrentAmmo`/`MaxAmmo`/`IsReloading` for the current-weapon panel (the weapon stays the only authority on its magazine); melee and the crossbow override `CanAimDownSights` to false because they inherit the base `Update`, and **`UsesMagazine`** to false so they get no ammo count, `R` reload or HUD readout — the crossbow is why that is a separate question from `weaponType`, since it still reloads audibly but has nothing to count. `WeaponSetupTools` asks the scene through `UsesMagazine`/`ProfileName` rather than keeping its own list of weapon names | `WeaponController` (base) → `MeleeWeaponController` → `AxeController`; `CrossbowController` (fires since Task 2; ⚠ still silent — no `"Crossbow"` audio profile exists, see the integration map), `Arrow`/`ArrowPool`, `BulletImpactManager`, `DamageableObject`, `IDamageable`, **`DamageableUtil.Damage`**, **`DamageDustManager`** | **Grenade**: `GrenadeController : WeaponController` (in hand, holds the count) throws a `Grenade` (the projectile prefab). WARNING: the blast resolves the **player explicitly through `PlayerStats.Instance`, not through the overlap query** - `PlayerStats` sits on a *child* of the player while the only body collider is the `CharacterController` on the *parent*, and `DamageableUtil` only ever walks **up**. `ZombieAI` works around the same wall with `GetComponentInChildren`. Left to the overlap, a grenade at the player's feet did nothing. `Fire` decides; `ThrowOne` acts, split so a throw can be driven from a script. `xpReward` is granted in `ZombieAI.Die`, so blast kills pay XP with no extra code. **Damage with a point goes through `DamageableUtil.Damage(target, amount, hitPoint, hitNormal)`** - all five weapons (guns, minigun, arrow, axe, grenade) call it, and it alone knows which types take a position: `ZombieAI` (head band pays double) and `DamageableObject` (dust where the round went in); everything else gets the flat `IDamageable.TakeDamage`. Until 2026-09-06 each weapon carried its own copy of the zombie-or-not branch; a third positional type would have meant a third branch in five places. `hitNormal` is the direction dust leaves the surface - the raycast normal, the contact normal, or for a grenade the direction AWAY from the blast. **`DamageableObject`** still simply vanishes at zero health (a destruction system was scoped and declined); what it does now is call **`DamageDustManager`** - `Puff` on every hit, sized by damage/maxHealth AND by how little health is left, so a cracked wall sheds more than a fresh one, and `Cloud` from its renderer bounds **before** `Destroy`, dense enough on that first frame to hide the vanish. ⚠ The manager is two world-space ParticleSystems built in code at Awake, `Emit()`-only, with the pack's two round-particle materials: additive for the puff, **alpha-blended for the cloud, because additive can only brighten and never covers anything**. Colour is by the prop's tag, the same tags `BulletImpactManager` keys its sounds on. Scene-lifetime singleton, null-checked at every dereference. **Minigun**: `MinigunController : WeaponController` - a spin state machine (Idle / SpinningUp / Firing / SpinningDown) where `Fire()` being called means *the trigger is down this frame* and release is inferred from the frame it was last called, so a pause or a weapon swap counts as a release. WARNING: the spin LOOP must stop on every exit - holstered, out of ammo, reloading, disabled, destroyed, and **the player dying** (`PlayerStats.DisablePlayerControls` sets `enabled = false`, which kills `Update`, so `OnDisable` is the only thing that catches it). ⚠ **The screen kick is per weapon, not branched on** (2026-09-07): `ApplyRecoil` used to multiply the shake by 1.5 for a revolver and 0.7 for an SMG, which left the minigun, crossbow, axe and grenade all shaking by one default nobody chose. The `m_ShakeMagnitude` / `m_ShakeDuration` / `m_ShakeRoughness` fields already existed and did nothing; now they are the only thing, written per weapon by `Tools/HUD/Add Combat Feedback`. `ShouldShowReticle` is virtual because the rule used to be `weaponName.Contains("SMG")`; `shootableLayerMask` is protected and `TrySpendRound()` exists so a subclass with its own `Fire` need not duplicate the mask or the magazine. **Since 2026-09-06 the wind-up is the wind-up CLIP's length and the wind-down is the shutdown CLIP's length** (`windupSeconds` / `winddownSeconds`, read from `StartSpinWindup`'s and `EndShooting`'s returns per press and release; `spinUpSeconds` / `spinDownSeconds` are only the fallbacks for a profile with no clip, or a release during the wind-up when no shutdown plays), `BeginFiring` is the one place rounds are allowed and the gunfire starts, `FireOneRound` plays **no per-round sound** (the shooting loop is the gunfire), and `BeginSpinDown` tells the manager whether rounds had actually been leaving, because a trigger let go during the wind-up gets a silent spin-down by the owner's decision. **`minimumTapSeconds`** (0.15) is the tap floor: a trigger let go sooner keeps winding up until that much has run, then winds down - only the trigger is held back, never dry/reload/lower - because at 100+ fps a click is two frames of wind-up and two of wind-down, which is the silence the owner reported. `EditorHoldTrigger` is the editor-only seam that drives `Fire()` from `Update` so `Testing/MinigunAudioProbe` can hold, release and re-press on a timeline. ⚠ **`MinigunController` derives its barrel spin axis** from `barrel.InverseTransformDirection(firePoint - barrel)` in `Start`, because the mesh's barrels run along local **Y** and the original `Vector3.forward` turned the cluster end over end. `barrelSpinAxis` overrides it, `reverseSpin` flips the direction by eye. Unity's positive rotation is clockwise when the axis points **at** the eye, and this axis points downrange, so positive reads anti-clockwise from behind the gun. `EditorForceSpin` is an editor-only seam: a held trigger is the one input a CLI-driven check cannot produce, so the spin is verified geometrically (the axis of rotation is the one line that does not move). ⚠ **The minigun MESH is turned `Euler(90, 180, 0)` inside its controller** (`MinigunSetupTools.ModelRotation`), because the FBX is authored barrel-down-local-**-Y** with its grip, trigger and button at **+Z**: left at identity the gun points at the floor, which is how it shipped and what the owner reported. Both facts were read off the model's own locators - the muzzle is at (0, -0.0113, 0) - rather than found by turning it until it looked right, and the check is that the model-to-muzzle direction in the CONTROLLER's space comes out (0, 0, 1) with the grip at negative Y. The muzzle locator is a bone inside the model, so it inherits that turn: it is rotated back to the controller's own orientation, because the flash and the tracers are parented to it and fire along its forward. Nothing AIMS by it - `Shoot` traces from the camera. 2026-09-09: `Arrow` and `Grenade` guard `contactCount` before `GetContact(0)` - a continuous-dynamic body can report none, and the spit already guarded it; the bolt ignores the shooter's colliders as the grenade does (it spawned inside the capsule and stuck there); `ArrowPool` survives a null prefab; the grenade's ring is on the weapon bus via `WeaponAudioManager.OutputGroup`. Measured: 30 bolts and 10 grenades, 0 exceptions, 0 bolts under the player, all bolts back in the pool.
| `Enemies/` | Zombies, and since 2026-09-07 their **roles** | `ZombieAI` (NavMesh brain; registers itself with the manager and cleans up audio/agent on death), `ZombieManager` (tracks actives, drives combat music + music intensity), `Roles/` (`ZombieRole` base → `ZombieRunner`, `ZombieShield`, `ZombieSpitter`, `ZombieScreamer`, plus `ZombieSpit`/`ZombieSpitPool`), `BossZombieController` (layers presence/phases/slam/summons/death-payoff over `ZombieAI` on the boss — every behaviour an authorable clip slot, a music-intensity floor held while it lives, an optional mixer-snapshot silence dip on death) | ⚠ `xpReward` is granted in `Die()` and **only** there — it is the one path that runs exactly once (both callers test `!isDying`, and `Die` sets it first). Do not also grant from `OnZombieDeath`; three listeners already subscribe to it. `repathInterval` (0.25 s, 0 = every frame) and `healthBarVisibleDistance` (25 m, 0 = no distance gate) are the two performance knobs, both per-prefab. 2026-09-09: every `agent.isStopped` write in `ZombieAI` checks `isOnNavMesh` as well as null (two branches had no null check at all), `OnZombieHit` has its `ResetStatics` like its two siblings, and the fallback AudioSource is routed through the manager.
| `Enemies/Roles/` | One behaviour layered over `ZombieAI`, the way the boss already is: **Ghoul runs** (a growl, 0.5 s standing still, then 2.2× for 2 s, then a pant), **Butcher blocks** (a 120° front arc takes 30 % damage; behind and headshots land whole), **Demon spits** (a retch, then a lobbed projectile at where the player was), **Yokai screams** (1.4 s call, then every zombie within 20 m runs 1.5× for 5 s). None of them adds an animation: the three-state Mixamo controller has Walk / Attack / Dying and no idle, so a role only ever sets `IsMoving` false, and only while it holds control | `ZombieRole` (the base), `ZombieRunner`, `ZombieShield`, `ZombieSpitter` + `ZombieSpit` + `ZombieSpitPool`, `ZombieScreamer` | ⚠ **Borrowed control must come back.** `ZombieAI.Die` disables the agent, so a coroutine that re-enables it afterwards hands a corpse its pathing and it slides after the player. Everything releases through `ZombieRole.ReleaseControl()`, which never switches on what the zombie switched off itself. ⚠ **`CanAct` is the wrong question after a yield** — `TakeControl()` disables `ZombieAI` and `CanAct` requires it enabled, so re-checking it inside a borrow abandons every tell on its first frame (the work order said to do exactly that; two agents hit it independently). Use **`StillHolding`**. ⚠ **One role per zombie**: they share `ZombieAI.SetPhaseSpeedScale`, a single float the **boss also uses for its phases** — which is why the screamer's rally sweep skips anything carrying a `BossZombieController`, and why a role must never touch `SetSpeedMultiplier` (WaveManager owns that, per wave). ⚠ `ZombieRole.Voice` resolves **on demand, never cached in Awake**: `ZombieAI.Start` is what adds an AudioSource to a prefab lacking one, and every Awake precedes every Start — cached early, such a zombie would be silently mute for ever. |
| `Waves/` | Night-wave spawning | `WaveManager` (spawning + ZombieDefence objective + UI; per-wave death attribution via `aliveByWave` + `nightGeneration`) 2026-09-09: the bare `AddComponent<AudioSource>` fallback in `SpawnZombie` is gone - it ran before `ZombieAI.Start` and won, unrouted and 2D. |
| `Inventory/` + `Items/` | 5-slot inventory, usable items | `Inventory` (singleton), `Item` base → `FoodItem`/`MedicineItem`/`WeaponItem`/`Fish`; `FishingRod`, `FishingRodItem`, `FishingZone` | `Item.TryMergeIntoInventory` (virtual, `false` by default) is the **only stacking in the game**: `Inventory.AddItem` is first-empty-slot and nothing else. `GrenadeItem` overrides it so a second grenade merges into the count instead of taking a slot; returning `true` means *handled*, so the override owns the pickup sound and the destroy, and a full player walks past a pickup that stays put.
| `Interactions/` | E-key interactables | `InteractionSystemTMP` (file renamed to match class), `ActionHandler` (power/lights hub), `ElectricalSparksController`, `SecretRockSwitch`, `FirestickInteraction`, `CampfireInteraction`, `FirstTimeInteractionTracker`, `InteractionMessages`, `ToggleMeshRenderers` |
| `Objectives/` | Quest system | `ObjectiveManager`, `DailyObjectiveManager` (scene-lifetime singletons — deliberately not persisted, see the reload note below), `ObjectiveData`/`DailyObjectiveData` (ScriptableObjects), `ObjectiveConstants` (all objective id literals), `ObjectiveTrigger`, `ObjectiveTargetLocator` (the only target component) | ⚠ **`ObjectiveManager.Instance` is null for the first frames and after a reload** (single-scene, no `DontDestroyOnLoad`), and there is no wait-until-ready hook — so every caller null-checks **at the dereference**, silently on `Update`/coroutine paths and with one `LogWarning` on event paths. The header of `ObjectiveManager.cs` states the rule; `DailyObjectiveManager:272` and `WaveManager:471` are the shape to copy.
| `UI/` | HUD, menus, radar, prompts | `UIManager`, `HUDManager`, `CurrentWeaponHUD` (bottom-right current-weapon panel: the raised weapon's side-on picture and rounds left — plus `/ magazine` when the prefab has a second label — repainted from `WeaponController.OnHudStateChanged`, blank count for the axe/crossbow, faded out when nothing is raised; ⚠ its `Label_AmmoCount` is a **90 px box at 64 pt**, which fits three digits and WRAPS on four — the minigun's 2000-round belt showed as '20' until `Tools/HUD/Fit Ammo Readout` turned TMP auto-sizing on (32..64 pt) and wrapping off, so every other weapon still draws at exactly 64), `DamageDirectionIndicator` (2026-09-03: swings the Synty edge-of-screen hit smear `FX_Damage_EdgeofScreen` around the screen centre toward the attacker and tracks it while the flash shows; `PlayerStats` adds it to the damage-effect root at runtime, so the scene needs no edit — add it by hand only to author its fields), `EventManager`, `WorldSpaceObjectiveManager`/`WorldSpaceObjectiveMarker`, `ObjectiveUI2` (owns its own teardown), `EnemyRadar`, `ObjectiveRadar`, `Compass`, `PauseMenu`, `TitleScreen`/`EndGameScreen` (runtime-installed IMGUI bookends), `CanvasButtonStyle` (shared IMGUI-matches-canvas button styling; `PauseMenu` also **`CombatFeedback`** listens to the static `ZombieAI.OnZombieHit` and flashes a hitmarker on the reticle — white on a hit, red and larger on a headshot — with a 2D confirmation to the player from `UIAudioManager` (`hitMarker`, `headshotPing`), deliberately a different sound from the zombie's own 3D `headshotSounds`: one is the gun telling you, the other is the thing you shot. It **unsubscribes in `OnDisable`**, because a static event outlives a scene reload and this level's restart path is a reload. The marker is a child of `ReticleController.reticleObject`, exposed as `public static RectTransform HitMarker`, so `Show(false)` already hides it — `ReticleController.OnValidate` warns if a tool ever wires one that is not a child. **`MinimapView`** drives the round top-right minimap: a second, orthographic camera 80 m above the player looking straight down into a 256 px RenderTexture, **yawed by the heading so the player's forward is the top of the picture** (`rotateWithPlayer`, the owner's call 2026-09-06; off = north up and the cone turns instead) - `Euler(90, heading, 0)` is pitch-down first with the camera's up on north, then a yaw about world Y that swings it onto forward; the icon offsets are turned anti-clockwise by the same heading (`(sin h, cos h)` lands on `(0, 1)`), checked with a marker 20 m ahead drawing at (0, 94.3) and one 20 m right at (94.3, 0). The RenderTexture made at runtime and shown by a RawImage inside the dial's circular mask; `viewRadius` (35 m) is both the camera's orthographic size and the metres-to-pixels scale the icons are placed on, so an enemy 20 m east is drawn 20 m-worth of pixels east of the centre on the same scale the ground is drawn at. Enemies by the `Enemy` tag four times a second exactly as `EnemyRadar`, objectives from `WorldSpaceObjectiveManager.ActiveMarkers` exactly as `ObjectiveRadar`, both as the pack's own icon prefabs, clamped to the rim. The player marker's view cone turns by MINUS the heading (UI is anti-clockwise positive; a compass bearing is clockwise), verified in play. ⚠ **A render costs ~3 ms** (terrain, trees and props from above), so the camera is only enabled every `renderEveryNthFrame` (3) - a disabled camera keeps its last picture; the camera hides Player / Enemy / UI / TriggerBoxes layers, renders no shadows, post or AA. The strip compass is back under `Top ` at its original (0.5, 1) / (0, -106.7). **`HotbarSelectionUI`** lights Synty's `Selected` frame on the slot whose item is **in the hands** - a raised `WeaponController` or a raised `FishingRod`; consumables never equip. It rides the static `WeaponController.OnHudStateChanged` and polls only the rod, which has no event. ⚠ **It disables each slot Button's `Selectable.Transition.Animation` and its `Animator`**, because every clip in `AC_Button_HotBar_Item_Minimal` animates `Item/Selected . m_IsActive` - so the Animator owns that object and overwrites any `SetActive` on its next update. Driving the controller's own `Selected` trigger does not hold either: `Selectable` re-fires `Normal` from `DoStateTransition`. Both were measured failing. The cost is the button's hover animation, which an FPS with a locked cursor never shows.
hosts the CONTRIBUTIONS acknowledgements popup - entries are authored on its
inspector list), `MenuManager`, `SettingsMenu`, `SFXVolumeControl`, `ReticleController`, `DebugDisplay`, and more |
| `DayNightCycle/` | Time-of-day driver | `DayNightCycle` — sun/sky/ambient, **sole owner of the day number** (`OnDayChanged`), clones its sky/cloud materials at Awake so the `.mat` assets are never written |
| `Animations/` | Announcement bridge + procedural motion | `AnimationEventHandler` (day/death/cleared announcements; follows `DayNightCycle.OnDayChanged` — no counter of its own), `AnimationEventRelay`, `WindChimeSway` | `WindChimeSway` is sines, not physics: a hanging thing is a pendulum, so the cluster and each tube get two of them at periods that do not divide into each other, plus a much slower gust envelope. ⚠ The **swing axis is measured, not assumed** - each child's hang direction is the vector from its pivot to the middle of its own mesh, and it swings about the two axes perpendicular to that. This project has been bitten once already by an assumed axis (the minigun's propeller), and the same reasoning is why the ROOT swings about the WORLD horizontals instead: what makes a hanging thing hang is gravity, not the model's conventions. Deliberately NOT wired to `WindChimeAudio` - modulating the loop's gain with the gust would sound better and would also mean a student could no longer hear their own clip at the level they authored it. |
| `Camera/` | Screen shake, screen effects | `CameraShakeController` (singleton), `MushroomTripEffect` (runtime post-processing Volume + camera-drift pivot — see [MUSHROOM_TRIP.md](MUSHROOM_TRIP.md)) 2026-09-09: `CameraShakeController` is an additive offset over whatever pose `FirstPersonController` set this frame, on unscaled time - it snapped to the Awake-time `localPosition`, so firing while sliding stood the camera up for the shake. |
| `Core/` | Generic pool | `Core.Pooling.ObjectPool<T>` (used by `ArrowPool`) |
| `Lighting/`, `Lighting fade/` | Trigger-driven light/ambient fades | `TriggerLightIntensity`, `AmbientLightController`, `AmbientFadeTrigger` |
| `River/` | Spline for river audio | `Spline`, `SplinePointGizmos`, `ToggleGameObjects` |
| `Attributes/` | Inspector attributes | `RangeFloat`, `RangeSliderAttribute` |
| `Testing/` | Dev cheats/diagnostics (gated by `Application.isEditor \|\| Debug.isDebugBuild` at runtime) | `GameTesting` (day-skip etc.), `StatsHelper` (audio wiring report), `Teleporter`, `RotateObject`, `Instructions`, `PerformanceProbe`, **`MinigunAudioProbe`** (`#if UNITY_EDITOR`; a coroutine that holds / releases / re-presses the minigun through `EditorHoldTrigger` and logs every 50 ms which clips are playing and where - the only way to check a 0.74 s wind-up from a bridge whose round trip is a second), **`EnemyRoleProbe`** (`#if UNITY_EDITOR`; spawns one roled prefab in front of the player and measures what the work order only claimed - the runner's phase-speed scale and how long it holds, the shield's damage from three angles, the screamer's rally on a neighbour and on the boss, and the spit's landing point and damage. ⚠ Two traps it was built around: the subject can kill the player, and the death screen sets `timeScale` to 0, which freezes every `WaitForSeconds` in the probe - hence `KeepPlayerAlive()`; and that healing hides the very damage the spit test is watching for, so `DamageAbsorbed` is read BEFORE the top-up. `settleSeconds` is short because a Demon walks 4 m/s and its spit window closes at 7 m) **`AudioIntegrityProbe`** (`#if UNITY_EDITOR`, 2026-09-09; nine measurements, one grep-able line each, `[1]`..`[9]` PASS/FAIL/SKIPPED with the numbers: the crossfade under a frozen clock, the boss's pitch across an attack sound, the footstep source holding a marker level, fifty no-repeat calls, reload Start -> End against `reloadTime`, thirty bolts and ten grenades, report parity edit / play / boss, the heartbeat at threshold 0, and the console error count. Each measurement is its own coroutine stepped by hand so one exception is that line's FAIL and the next still runs; everything it changes is restored inline and on destroy. ⚠ It picks its subjects from the scene's registry and from prefabs whose profile the manager knows - `Inbox/Prefabs/Enemies` holds six zombies no wave spawns and no profile names, and the first alphabetically is one of them). |
| `Assessment/` | Student grading tools (NOT dev-gated — available in builds) | ⚠ **The report records one row per ARRAY ELEMENT.** An empty `AudioClip[]` therefore produces **no row at all** — which is why `headshotSounds: []` on all five enemy profiles has never once been listed despite being played on every head hit, and why a tool that adds an assessed array slot must size it to 1 (one null element = one MISSING row). A single `AudioClip` field records whether or not it is null. `AudioProgressScanner` (classifies every serialized AudioClip slot: implemented / placeholder (a beep or generated test stem) / demo (any other clip from `Assets/Audio/Demo sounds` — by asset path in the editor, by "demo" in the name in a build) / empty — **but every report shows the three not-done states as one word, MISSING, in red** (the owner's call 2026-09-06; `AudioProgressReport.Missing` is the count) — presentation since 2026-09-03: spaced headings with no GameObject suffix unless a type repeats, "Footsteps" first, no clip name on placeholder rows, the leading category segment dropped once a profile name follows it, and **clip labels shortened** — one trailing whole word off (`Audio Clip`, `Sound`, `Sounds`, `Clip`, `Clips`, `Audio`), then a display-only override table keyed on the real field name has the last word (`footstepSounds` → "Footsteps", `walkieClips` → "Chatter", `audioClips` → "Calls"); a label that strips to nothing drops its whole path segment, so `Chest Pickup Sound ▸ Clip` reads `Chest Pickup`. `Loop`/`Chime`/`Stinger` stay — a loop is information. The label test is "any field the walk reaches a clip through", not "the field is an AudioClip", because `PickupSound` holds the clip one level down; profile entries read with a capital ("Wood", not the authored lookup key "wood"); section order is alphabetical apart from the ones the owner placed by hand — `GroupsFirst` puts **Footsteps, Waterfall, River Audio Manager** on top because students work top-down, `GroupsLast` drops **Bullet Impact Manager** below the Weapon Audio Manager it belongs with. ⚠ The **Waterfall** section is the one place the report reads an `AudioSource`'s own `clip` rather than a serialized field: see `DirectAudioSources`, a short table of assessed sources a student fills in **by hand** with no manager in between, matched on GameObject name. Never list a source a manager fills at runtime there, or it is counted twice — a class can implement `IAudioSlotFilter` to hide slots that do not apply to a given instance, which is how the axe's reload slots stay out of the report), `AudioProgressReportUI` (self-installing Ctrl+G popup + grading export; ⚠ drawn through `GUI.matrix` at `AudioOverlays.ResolutionScale(2)` = 2 x Screen.height / 1370, so it is the same fraction of the screen at 4K as on the Free Aspect view it was tuned on — IMGUI draws in screen pixels and a fixed scale was a third the size at 4K; the event monitor and music debug overlay use the same rule), `AudioWiringValidator` (name-mismatch / uncovered-surface / clipless-profile / id-drift checks; dev-gated on load, plus a **Validate wiring** button in the editor window), `AudioEventLog` + `AudioEventMonitor` (**Ctrl+J** live audio-event overlay: what fired, what answered it, and misses in red — footstep *plays* are marked `Noisy` and hidden, footstep misses still show), `AudioOverlays` (which overlay is on screen; see the single-authority table); editor twin in `Editor/AudioProgressReportWindow` (`Tools ▸ Audio`) **Since 2026-09-09 the scanner also reaches prefab-only components**: `AssessedPrefabRegistry` (a scene component listing prefabs by hand - the boss and the fishing rod today, placed by `Tools/Audio/Fix Slot Surface`) is the one door through the Unity-object boundary; a type met through the registry is skipped on the scene pass so a boss alive in play mode is not counted twice, and the total is the same in edit mode, in play, and with the boss alive (**measured 244 / 244 / 244**). The report went **224 -> 244**: five headshot slots, the mushroom dialogue, the boss's five, the rod's six (three tiers x bite/catch, labelled by `tierName`) and one loop stem per music state. Spawner headings drop the repeated type and Unity's `(n)`; river rows read by `RiverProfile.areaName`. **Handout mode (2026-09-10)**: the scanner's walk also hands back a *reference* per slot (`AudioSlotRef`: host, holding object + field or list + index, `Set()`) and per mixer-group reference (`AudioRoutingRef`: every `AudioMixerGroup` field the walk meets, every scene AudioSource's output group, every AudioSource under the registry prefabs and the prefabs an editor tool names) through `WalkSlots(clipSink, routingSink, extraPrefabRoots)`. One walk, two clients: `Scan()` records rows, `WalkSlots` layers a sink on the same walk - the rows are not optional there, because the registry gating depends on the counts. Proof the refactor moved nothing: the export differs from the previous one in **0 lines**. `AudioHandoutState.asset` (`Assets/Audio/Handout/`) says Authored or Handout and names the snapshot Reinstate reads. ⚠ Same-named siblings share a hierarchy path (three `Mushroom`s under one parent), so the routing sweep sorts by sibling indices, root to leaf, or the order changes per run. |
| `Utilities/` | Small helpers | `DeactivateOnStart`, `TimedActivator` |
| `Editor/` | Custom inspectors + tools | one editor per audio manager, `RangeSliderDrawer`, `WaveManagerExporter`, `ObjectiveManagerExporter` (both export to `<project>/Exports/`), `MusicTestStemGenerator` (`Tools/Music/Generate Test Stems`), `AudioProfileEnumMigration` (`Tools/Audio/Migrate Profile Keys To Enums`), `AudioProgressReportWindow` (`Tools/Audio/Student Progress Report`), `MinigunSetupTools` also carries **`Tools/Weapons/Wire Minigun Icons`** (imports the owner's `Assets/minigunIcon.png` and `minigunGraphic.png` as UI sprites through `TextureImporter` — an asset change, the sanctioned way to rewrite a .meta — and wires them onto the minigun `WeaponItem`) and **`Tools/Weapons/Wire Minigun Clips`** (fills EMPTY minigun slots from the owner's five clips under `Assets/Audio/Weapons/Minigun` by exact name, never overwrites) | `PerformanceFixTools` (`Tools/Performance/` — a read-only settings report plus four idempotent fixes; asset edits are **not** undoable, git is the revert) and `ProfilerCaptureTools` (`Tools/Profiling/` — drives the Profiler around a capture window; ⚠ it must open the Profiler **window** first or `ProfilerDriver` records nothing and `firstFrameIndex` stays -1). ⚠ **The audio-manager inspectors are hand-drawn** — each one lists its profile's fields explicitly, and `WeaponAudioManagerEditor` switches on `WeaponType`. A profile that gains a field, or a weapon type with no `case`, renders as an **empty section with nothing to drag a clip into**, which is how the grenade shipped for an hour. Adding a slot means three places, not one: the field on the profile, a `PropertyField` in the inspector, and the filter in `IsAudioSlotRelevant` that decides whether the report asks for it. WARNING (again): the hand-drawn inspector trap has now bitten **twice** - the grenade shipped with an empty section, and the SMG/Revolver reload slots had been unfillable since the beginning. `WeaponSetupTools`, `GrenadeSetupTools`, `MinigunSetupTools`, `EnemyRoleSetupTools` (`Tools/Enemies/` — report + add the four roles: prefab components, the Ghoul/Butcher retunes, the VFX, the profiles' `enemyRole`, the six report rows, and the spit prefab, material and pool) and `CombatFeedbackSetupTools` (`Tools/HUD/` — the listener, the hitmarker under the reticle, and the per-weapon shake numbers). ⚠ Both split their logs into **assets written** (prefabs and materials, straight to disk, `Ctrl+Z` will not bring them back — `git checkout --` is the way back) and **scene fields set** (Undo-recorded, the owner saves). ⚠ **Find weapons by type and id, never by path**: the crossbow's GameObject is named `CrossBow` while its `weaponName` is `Crossbow`, so a hard-coded path compiles, runs, reports success on the others and silently leaves that one on the class defaults. `HudSetupTools` (`Tools/HUD/` — also `Move Compass Top Centre`, the inverse of `Move Compass Top Right`, and `Add Minimap`, which instantiates the lights-and-wires dial as a PLAIN instance - not a prefab instance, because its demo map and eleven sample icons are deleted and a RawImage added, and a prefab instance carrying that many overrides is one a re-import fights - creates `Player/Minimap Camera` with its URP data set through `GetUniversalAdditionalCameraData()`, and wires `MinimapView`; `MinimapMargin` 30 is the corner gap; and `Stretch Settings Background` (honest stretch anchors for the pause menu's fill and striped vignette, `SettingsOverflow` 0.2 past every edge so the vignette's rounded corners stay off-screen at any aspect), `Move Interaction Prompt Beside Minimap`, which reparents the prompt widget from the `Top Right` region to `ScreenSpace` and places the union of its **drawn** graphics `PromptGap` px left of the map — ⚠ measured on the editor's placeholder text, and the action label grows towards the map with longer text, so `PromptGap` is 120 to keep 'inventory full!' clear), `DamageDustSetupTools` (`Tools/Level/` — report, add, and **Reset … To Defaults**; ⚠ **a serialized field keeps the value it had when its component was created**, so retuning a default in the `.cs` changes nothing in a scene that already has the component - the first dust retune was invisible until the component was removed and re-added, which is what Reset does). `WindChimeSetupTools` (`Tools/Level/` — report + hang the chimes, idempotent, and it never writes the clip field, so re-running cannot wipe a student's work; it also **positions them only on the run that creates them**, because placement is the owner's, and wires the AudioSource to the **AMBIENCE** bus — a fresh AudioSource goes straight to Master where the ambience fader cannot reach it, which 30 sources in this scene already do). ⚠ **A constant that changes after a build has already run does not reach the scene**: every `Build …` here skips the parts it finds, by design, so `MinigunSetupTools` also carries `Tools/Weapons/Fix Minigun Orientation and Ammo`, which applies the two constants to the minigun that is already in the level rather than rebuilding it and losing the audio profile's clips. ⚠ `Renderer.bounds` is a cached world AABB that is **stale right after instantiating in edit mode** and includes ParticleSystemRenderers - measuring a weapon that way reports ~18 m of muzzle-flash tracer instead of ~0.9 m of gun. Measure from `Mesh.bounds` instead (`MinigunSetupTools.TryGetWorldBounds`). **`AudioSlotSurfaceTools`** (`Tools/Audio/Report Slot Surface` read-only, `Fix Slot Surface`; 2026-09-09): sizes every empty assessed array to one null element (an empty array is no report row), places `AssessedPrefabRegistry` with the prefab-only components found by type, and gives each music state a loop segment with one empty stem - the three `MusicState_*.asset` writes and the boss prefab write are logged with their `git checkout --` revert; the scene fields are one Undo step. Idempotent. `RiverAudioManagerEditor` seeds `areaName` ("River Area N") the first time it draws. **`AudioHandoutTools`** (`Tools/Audio/Handout/` - `Report Handout Surface` read-only; `Snapshot Authored Audio` writes a dated JSON of every slot and routing reference with two keys - a GlobalObjectId and the report's group + label - and the asset's guid + local id, never overwrites, **refused in Handout mode**; `Switch To Handout` refuses without a snapshot matching the live state, then applies the owner's rule: Footsteps manager → the footsteps beep, MusicManager and the music states → null, everything else → the beep, every routing reference → null; `Reinstate Authored Audio` puts it all back and fails unless the report's counts equal the snapshot's). Scene fields are one Undo step and **the owner saves**; prefab and asset writes go straight to disk, logged with `git checkout --`, and each prefab is re-read to prove the write persisted. **`AudioHandoutSelfTest`** (`Round-Trip Self-Test`): snapshot → switch → reinstate inside one Undo group, eleven checks, measured 2026-09-10: switch changes 213 slots + 66 routing refs, report 0/244 in Handout, export back byte-identical, routing list identical, 22 files identical on disk. **`AudioHandoutBanner`**: one line at the top of the six hand-drawn manager editors and the report window; a warning box in Handout mode, nothing in Authored. ⚠ git checks prefabs out CRLF and Unity writes LF, so compare files with line endings normalised; and a prefab saved for the first time under Unity 6 is reserialised once (`m_audioClip` → `m_Resource`, dead keys dropped) - commit that and re-run.

Dead-code candidates that were audited and **kept because they are live** — do not
delete: `Core/ObjectPool`, `DayNightCycle.SmoothTransition`. (`Audio/MusicController`
was on this list until 2026-09-03. Its "scene-wired" note dated from the 2026-07 audit
and was stale: nothing called it — no code caller, no UnityEvent in the scene, one guid
hit which was the component itself — and the mixing-desk speakers it started are driven
by the `InteractionAudioManager` **Mixing Desk** profile. `Tools/Audio/Remove Legacy
Music Controller` took the component out; the owner saved, and the class, its `.meta` and
the commented-out `musicController2` blocks in `ActionHandler` went with it. That is the
two-phase shape to copy for any scene-referenced class: component out, owner saves, class
deleted — the reverse order leaves a "missing script" in the saved scene.
`Assets/_Recovery/0.unity`, an archived copy outside the build settings, still holds the
guid and will show a missing script if anyone opens it.) The remaining verified-dead members are listed in
[IMPROVEMENT_PLAN_3.md](IMPROVEMENT_PLAN_3.md) §E — **swept in Roadmap Task 10**; do not work from §E's list again (five of its claims were wrong — see ROADMAP_TASKS.md's STATUS block).

## Audio architecture

### Mixer bus layout (`Assets/Inbox/AudioMixer.mixer`)

```
Master
├── AMBIENCE (Birds, Frogs, River, Waterfall, Fire, …)
├── MUSIC (Speakers, …)
├── SFX (Lightswitch, Rock Door, Electrical Box, …)
├── FOOTSTEPS
├── ENEMIES (Enemy vocalisations, Enemy attack)
├── WEAPONS
├── IMPACTS
├── UI (UI Buttons, UI Menu, UI Pickup, UI Notification, UI Consumption,
│        UI Day, UI LevelUp)
├── PlayerStats (Heartbeat)
└── AUX Reverb (send target) — plus one Snapshot
```

Exposed params used by code: `MasterVolume`, `MusicVolume`, `AmbienceVolume`,
`SFXVolume`, `UIVolume` (via `AudioMixerController`), plus `volume` used separately by
`SettingsMenu` (inconsistent — two different volume paths).

### Manager pattern

Every audio domain follows the same shape:

```
[SerializeField] Profile[] profiles       // clips + volume (+ sometimes pitch) per key
Dictionary<string, Profile> lookup        // built in Awake, keyed by free-text name
Play*(...)                                // picks a random clip, optional pitch/volume
                                          // variation, routes to a mixer group
```

- **String keys everywhere**: `enemyName`, `weaponName`, `interactionName`, surface
  names ("grass", "wood", …). Callers still pass strings; the migration to ids is
  half-done and deliberately gated (Roadmap Task 8).
- `Audio Systems/AudioProfileKeys` is the single place those keys are handled. All four
  managers build their lookup through it, so keys are **trimmed and case-insensitive**,
  blank and duplicate names are reported at init, and a missed lookup logs **one**
  warning naming the requested key and every key that does exist. Route any new
  string-keyed audio lookup through it rather than calling `Dictionary.TryGetValue`.
- `Audio Systems/AudioProfileIds` declares `EnemyId` / `WeaponId` / `InteractionId` /
  `SurfaceId` — one member per authored profile, each starting at `Unassigned = 0`.
  **These are not the same as `EnemyType` / `WeaponType` / `InteractionType`**, which are
  behaviour categories (several profiles share one value, and `InteractionType` decides
  which playback path runs) and must not be repurposed as keys. Assigning an id also
  registers the id's own name as a spelling of that profile, so a mistyped string field
  no longer silences the sound. `Tools ▸ Audio ▸ Migrate Profile Keys To Enums` fills the
  ids in and logs whatever it cannot match.
- **The callers are converted** (Roadmap-2 Task 4): every profile in the scene and the
  five wave prefabs carries an id, and `ZombieAI`, the weapon controllers and the
  interaction scripts resolve **id-first with the authored string as fallback**, via a
  small `AudioProfileKey`/`WeaponAudioKey` property on each. Footsteps stay string-keyed
  by design — `SurfaceDetector` derives surface names from tags and terrain layers at
  runtime, so there is no caller to convert. Two deliberate fences: `weaponName` still
  drives `WeaponController`'s SMG/Revolver detection (identity, not a sound key), and
  `InteractionSystemTMP` has no id field because it overwrites its own name in `Start` —
  its three magic strings are now `AudioProfileIds.NameOf(...)` calls instead.
- The random-clip + variation snippet is consolidated into
  `Audio Systems/AudioUtil` for its users (Enemy/Weapon/Footstep/Player managers and
  `RandomTriggerAudioVariations`); a few standalone components (`AudioRandomizer`,
  `PlayRandomSound`, …) still carry their own copies.

### Ambience / music state flow

```
Trigger volumes (TriggerManager, LocationTrigger)      MusicZoneManager (self-contained
        │                                               single-clip fade zones)
        ▼
AmbienceManager  ──crossfade──▶ per-state ambience beds (Outside/Swamp/Cave/Campfire)
MusicManager     ──scheduled──▶ Exploring / Fighting / Inside  (adaptive, see below)
        ▲
ZombieManager.SetFightingState(bool)  (combat music)
```

### Adaptive music (vertical + horizontal layering) — newest feature

`MusicManager` supports layered music alongside its original single-clip tracks.
Implementation in `Assets/Scripts/Audio/Music/`; full design, audit record and
authoring recommendations in [MUSIC_LAYERING_PLAN.md](MUSIC_LAYERING_PLAN.md).

```
MusicManager (the only component; same GameObject, same scene wiring)
├── legacy path  ── musicTracks[]  (any state with no content asset)
└── layered path
    ├── MusicStateDefinition (asset per state) ── segments ── stems
    ├── MusicConductor        musical clock on AudioSettings.dspTime
    ├── LayeredStatePlayer    one per layered state; 2 source banks per stem slot
    ├── MusicIntensityDriver  zombie count / low health / dusk ⇒ 0-1 intensity
    └── MusicDebugOverlay     Ctrl+M (F8 alias), dev-gated
```

- **Vertical**: each stem declares a `minIntensity`; the smoothed intensity gates it in
  and out with its own fade times (equal-power curves), 0.05 hysteresis, and per-stem
  solo/mute in the overlay for auditioning.
- **Horizontal**: state changes are *scheduled*, not applied. A `MusicTransitionRule`
  picks the sync point (`Immediate`/`NextBeat`/`NextBar`/`EndOfSegment`) and may add a
  stinger and intro/outro segments. Re-entering a still-audible state is a graceful
  takeover, never a restart.
- **Migration is per-state**: a state with no `MusicStateDefinition` assigned keeps
  playing its legacy track. ⚠ Keep an (empty) legacy `MusicTrack` entry for any state
  named in a `cannotOverride` list — that rule lives only on the legacy entries and the
  manager warns when a carrier is missing.
- **Compatibility contract**: `StatsHelper` reflects on the field literally named
  `musicTracks`; `ZombieManager`/`TriggerManager`/`LocationTrigger` call
  `SetFightingState`/`SetIndoorState`/public `ChangeState`. None of these names may
  change.
- Test content: `Tools/Music/Generate Test Stems` → `Assets/Audio/TestStems/`
  (placeholder tones, forced to PCM import — never let them re-import as Vorbis).

⚠ Two rules the whole design depends on: **musical timing reads
`AudioSettings.dspTime`, never `AudioSource.time`** (a buffered position that drifts and
stalls when paused), and **every music fade uses unscaled time** (the pause menu sets
`timeScale = 0` while the DSP clock keeps running). Music sources are `priority = 0`
because Unity virtualises the *quietest* voices first — exactly the stems mid-fade.

**Voice budget:** `stems × 2 banks` AudioSources per layered state
(3 states × 4 stems = 24, plus legacy tracks and two stinger sources). The project
allows 60 real voices (`ProjectSettings/AudioManager.asset`); check that number before
blaming dropouts on the scheduler if many more layers are authored.

### How things find each other (wiring conventions — inconsistent)

- **Serialized references**: `WeaponAudioManager`, `PlayerAudioManager`, `FootstepAudioManager` on the player.
- **`FindFirstObjectByType` at Start**: `EnemyAudioManager`, `InteractionAudioManager`, `MusicManager`, `DayNightCycle` consumers.
- **Singletons (`Instance`)**: `PlayerStats` (guarded, added Task 1), `UIAudioManager`, `BulletImpactManager`, `ArrowPool`, `Inventory`, `ObjectiveManager`, `DailyObjectiveManager`, `UIManager`, `AnimationEventHandler`, `EventManager`, `WorldSpaceObjectiveManager`, `CameraShakeController`, `ReticleController` (static), `FirstTimeInteractionTracker`. **None persists across scene loads** except `AudioProgressReportUI` — see the reload note below.
- **Tag/name lookups**: `"Player"` tag (exactly **one** object carries it since Task 1 —
  the `Player` root; consumers that need `PlayerStats` use `PlayerStats.Instance`
  instead of tag lookups), `"Enemy"` tag/layer (radar), `"Occluder"` layer.
- **Scene reload is now a supported, shipping path** (Task 3): `EndGameScreen`'s
  Restart button calls `SceneManager.LoadScene(activeScene.buildIndex)` after victory
  (`ChestPickup`) or death (`PlayerStats`). The old stale-reference hazard was solved
  by **removing `DontDestroyOnLoad` from every gameplay singleton** — this is a
  single-scene game, so each manager dies and re-`Awake`s with the scene, and each
  carries a "deliberately NOT DontDestroyOnLoad" comment. Do **not** re-add DDoL or
  write `sceneLoaded` re-acquisition handlers; that per-survivor patching approach was
  considered and rejected (recorded in ROADMAP_TASKS.md Task 3). The only survivor is
  `AudioProgressReportUI` (the assessment popup), which owns no per-scene references.
  Statics that must reset on reload do so explicitly: `Fish.ResetAllFishCounts()` in
  `DailyObjectiveManager.Awake`, `PauseMenu.Awake` resets `isPaused`/`timeScale`.

### Single-authority rules (established by the audits — don't reintroduce duplicates)

| Concern | Sole owner | Notes |
|---|---|---|
| Current day number | `DayNightCycle.dayNumber` | Broadcast via `OnDayChanged`. `AnimationEventHandler` and `WaveManager` subscribe; `DailyObjectiveManager` reads `GetCurrentDay()`. Never add a second counter. |
| Pausing day/night time | `DailyObjectiveManager.UpdateTimePause` | One verdict per frame. The pause menu is separate (`Time.timeScale`). `SetTimeSpeedMultiplier` does **not** unpause. |
| "Player already did this" auto-completion | `FirstTimeInteractionTracker` (via `ObjectiveManager.OnObjectiveAdded`) | `ObjectiveManager` must not auto-complete as well, or the completion sound double-fires. |
| Objective card teardown | `ObjectiveUI2` | The manager only drops its dictionary entry. |
| Firestick counting | `ObjectiveManager.IncrementFirestickProgress` | Reset when the objective is (re)shown. |
| Per-wave zombie counts | `WaveManager.aliveByWave` + `nightGeneration` | Deaths are attributed to the wave that spawned them; stale-night deaths are ignored. |
| Zombie manager (un)registration | `ZombieAI` (`Start` registers, `Die`/`OnDestroy` unregister — idempotent) | This is what turns combat music off after the last kill. |
| Sky/cloud material colours | `DayNightCycle` runtime clones | Never drive the serialized material assets directly — it rewrites the `.mat` files on disk. |
| Music state and musical time | `MusicManager` (+ `MusicConductor`) | State is a pure function of `isFighting`/`isIndoors` (+ `cannotOverride` as a priority rule between them) — nothing remembers a previous state, and gameplay never calls `ChangeState` directly. The layered pipeline only decides *when* the change lands. Don't add a second scheduler. |
| Which audio overlay is visible | `AudioOverlays.Current` | One value for all three (Ctrl+G report, Ctrl+J monitor, Ctrl+M music), so showing one hides the others and they cannot overlap. Both routes — the Ctrl shortcuts and the pause-menu buttons — call `AudioOverlays.Toggle`; never add a local `visible` bool back. `MusicDebugAvailable` gates the music button, which is dev-only and needs layered content. |
| Where a gun's shot goes | The **camera's** centre line (`WeaponController.Fire`) | Never `firePoint.forward`. `ApplyRecoil` snaps the weapon to a nose-up kickback pose *before* the trace, so aiming along the weapon put every shot `recoilRotation` degrees (10°) above the crosshair, compounding on automatic fire. `firePoint` is cosmetic — it positions the muzzle flash. Melee and the crossbow have their own `Fire` overrides and are unaffected. |
| Cursor lock | ⚠ contested — `PauseMenu` and `MouseLook` both write it (Plan-3 D1, unfixed) | If touching either, make `PauseMenu` the sole authority. |

## Audio integration map (every gameplay → sound call site)

> Line numbers below are from the original 2026-07 audit and have drifted — locate call
> sites by file + method name, not line. The ⚠ double-fire notes from the original
> audit are gone: `AddHealth` is silent since fix phase 1.

| Trigger | Call site | Audio call |
|---|---|---|
| Fire ranged weapon | `WeaponController.Fire` | `WeaponAudioManager.PlayShootSound(weaponName)` — consumes a round; firing empty starts a reload instead |
| Reload (R, or firing empty) | `WeaponController.ReloadRoutine` | `WeaponAudioManager.PlayReloadActionSound(weaponName)` — authored on the weapon profile's `reloadActionSounds`, **not** as a clip on the weapon |
| Raise / lower weapon | `WeaponController.ToggleWeaponPosition` | `PlayWeaponRaiseSound` / `PlayWeaponLowerSound` |
| Melee swing | `MeleeWeaponController.Fire` | `PlayShootSound` |
| Crossbow draw / release | `CrossbowController` | `PlayReloadStartSound` / `PlayShootSound` — a `Crossbow` profile now exists (beep-seeded), so the draw and shot are audible for the first time |
| Bullet impact (distance-delayed) | `BulletImpactManager.PlaySoundAfterDelay` | 4-voice round-robin (`TakeImpactVoice` prefers idle voices), so overlapping impacts no longer relocate a still-playing source (was Plan-3 C1) |
| Zombie ambient loop | `ZombieAI.Start` | `EnemyAudioManager.StartVocalizationLoop` (stopped again in `Die`) |
| Zombie attack (delayed) | `ZombieAI.AttackSequence` | `GetAttackSoundDelay` + `PlayAttackSound` |
| Zombie death | `ZombieAI.Die` | `PlayDeathSound` (the source's fade-out waits for it) |
| Combat music on/off | `ZombieManager.UpdateMusicState` | `MusicManager.SetFightingState` |
| Music intensity | `MusicIntensityDriver` polling | zombie count / health / dusk ⇒ stem layers |
| Wave cleared (anim event) | `WaveManager.PlayClearedSound` | `UIAudioManager.PlayAllEnemiesCleared` |
| Player damaged | `PlayerStats.TakeDamage` (`ZombieAI.DealDamage` / `BossZombieController` slam pass their transform, so the Synty edge-of-screen smear and hit lines swing toward the attacker) | `PlayerAudioManager.PlayDamageSound` |
| High-hunger loop (2026-09-03) | `PlayerStats.UpdateHungerLoop` (driven from the hunger band it already tracks; stops on the frame the player eats out of it, and in `Die` beside the heartbeat — starving leaves hunger at 0, the loudest point of the ramp) | `PlayerAudioManager.UpdateHungerLoopState` (its own clip, source, fraction threshold and volume on the **Hunger** profile; never falls back to the shared source, because a loop owns its source's clip and the heartbeat would cut it off) |
| Low-health heartbeat | `PlayerStats.UpdateLowHealthEffect` | `PlayerAudioManager.UpdateLowHealthState` (threshold authored on the audio profile, deliberately separate from the vignette threshold; `Die` stops the loop) |
| Jump / double-jump / land | `FirstPersonController` | `FootstepAudioManager.PlayJumpSound` / `PlayLandSound` |
| Footstep (surface-typed) | `FirstPersonController.PlayFootstepSound` | `PlayFootstep(surfaceType)` via `SurfaceDetector` — all eight surfaces are covered, including the `default` fallback |
| Item pickup | `Item.OnTriggerEnter` (guarded against double-collect) | `UIAudioManager.PlayPickupSound` |
| Eat food can / fish / medicine | `FoodItem` / `Fish` / `MedicineItem` `.UseItem` | `PlayFoodCanEatenSound` / `PlayFishEatenSound` / `PlayMedicineUsedSound` — each fires once, only on success |
| Fishing start / catch | `FishingRod` | `UIAudioManager.PlayOneShot(...)` — catch sound only when the fish is actually landed; routed via `diegeticAudioSetup` to the SFX bus (was Plan-3 C2) |
| ~~Standard / chest pickup~~ | ~~`StandardPickup` / `ChestPickup`~~ | **Gone 2026-09-04.** The owner deleted the four skulls and the chest, then the whole `Assets/Scripts/Pickups/` folder followed: `PickupBase`, `StandardPickup`, `ChestPickup`, `PickupsAudioManager`, `ParticleBurst`. `EndGameScreen.ShowVictory` went with them — the chest was its only caller — so **the game has no win condition**; death is the only ending, and restoring one is three lines against `EndGameScreen.Show`. `Assets/Inbox/Prefabs/Chest.prefab` was deleted with it on the owner's say-so — nothing referenced it, and it already carried two dead script references of its own before `ParticleBurst` became a third. One leftover remains: `Assets/_Recovery/0.unity` (archived, outside the build settings) references four of the deleted classes |
| Electrical box / mixing desk / switches | `InteractionSystemTMP`, `SecretRockSwitch` | `InteractionAudioManager.*` — desk silences on power-off, sparks follow `onPowerToggled`, pending spark-deactivation tween killed on state change (was Plan-3 C3/C4) |
| Firestick / campfire ignite | `FirestickInteraction` / `CampfireInteraction` | `IgniteFirestick` |
| Eat a mushroom | `MushroomTripInteraction.Consume` | `InteractionAudioManager.PlaySimpleInteraction("Mushroom", …)`, then `MushroomTripEffect` blends an optional mixer snapshot for the trip — see [MUSHROOM_TRIP.md](MUSHROOM_TRIP.md) |
| Sound-only prop (the well) | `PropSoundInteraction.Interact` | `InteractionAudioManager.PlaySimpleInteraction("Well", …)` — reusable for any prop that just needs to be audible on `E` |
| Objective completed | `ObjectiveManager.CompleteObjective` | `UIAudioManager.PlayObjectiveCompleted` |
| Level up (anim event) | `LevelUpAnimationEvents` | `UIAudioManager.PlayLevelUpSound` |
| Headshot (top 25% of a zombie's collider, 2x damage; guns + crossbow, tunable per prefab) | `ZombieAI.TakeDamage(damage, hitPoint)` via `WeaponController.Fire` / `Arrow` | `EnemyAudioManager.PlayHeadshotSound` (`headshotSounds` on the profile — new authorable slot) |
| UI toggle / menus | `UIToggleButton`, `PauseMenu`, `MenuManager` | `UIAudioManager.PlayButtonToggle` etc. |
| Volume sliders | `SFXVolumeControl`, `AudioMixerController`, `SettingsMenu` | `AudioMixer.SetFloat` |

Ambient/environmental sound is scene-wired (no code call site): `AmbienceManager`,
`MusicZoneManager`, `AudioRandomizer`, `RandomSoundSpawner`, `RiverAudioManager`
(follows the player along a `Spline`), `AudioOcclusion` / `DirectionalAudioSource`
(per-source raycast filtering, debug ray behind an opt-in toggle),
`FireSourceAudio`/`CampfireAudio` loops.

## Keyboard shortcuts (as of 2026-09-01)

Gameplay keys are read by `FirstPersonController` / `WeaponController` / the interaction
scripts; the rest are listed with the component that owns them.

| Key | Does | In builds? | Owner |
|---|---|---|---|
| WASD / mouse | Move, look | Yes | `FirstPersonController` |
| Shift | Sprint (needs stamina) | Yes | `FirstPersonController` |
| Space | Jump / double jump | Yes | `FirstPersonController` |
| C | Slide | Yes | `FirstPersonController` (`slideKey`) |
| E | Interact | Yes | `InteractionSystemTMP`, `CampfireInteraction`, … |
| 1–5 | Select inventory slot; same slot again lowers a raised weapon; consumables are used | Yes | `FirstPersonController` |
| 1–5 twice (within 0.6 s) | Drop that slot's item 0.8m in front of the player (1.2m off the ground), where it becomes a pickup again (a raised weapon/rod is lowered first; `UIAudioManager.itemDrop` is its sound slot). Replaced Shift+1–5 on 2026-09-06 because Shift is also sprint; `slotDoubleTapSeconds` on `FirstPersonController`, unscaled time so the pause menu cannot stretch a tap | Yes | `FirstPersonController`, `Item.Drop` |
| Left click | Fire (hold = automatic; crossbow draws on hold, fires on release); hold to fish in a fishing zone — and once a bite clip is authored on the rod, the fish lands only if the button is RELEASED within the catch window of *hearing* the bite (no visual cue; miss it and the fish escapes) | Yes | `WeaponController`, `FishingZone`, `FishingRod` |
| Escape | Toggle the pause/settings menu | Yes | `PauseMenu` |
| **Ctrl+G** | Audio progress report + **Export** | **Yes** | `AudioProgressReportUI` |
| **Ctrl+J** | Audio event monitor | **Yes** | `AudioEventMonitor` |
| Ctrl+M (alias F8) | Music debug overlay (read-only; per-stem S/M buttons) | **Yes** (un-gated 2026-09-02 — it is an assessment tool like Ctrl+G/J, and the dev gate hid its pause-menu button in builds) | `MusicDebugOverlay` |
| Shift+M | Audio wiring report (Escape closes) | Editor/dev | `StatsHelper` |
| Shift+I | Add test items | Editor/dev | `GameTesting` |
| Shift+O | Complete test objectives | Editor/dev | `GameTesting` |
| 0 | Toggle the river helper objects | Editor/dev | `ToggleGameObjects` |

⚠ **The dev-tool keys are serialized, and the scene overrides the script defaults** — every
`GameTesting`/`StatsHelper` binding is `LeftShift` in the scene even though the fields
default to Alt/Ctrl. Read the scene, not the field initialiser. `DebugDisplay` (backslash)
is **not in the scene**, so that key does nothing.

Removed on 2026-09-01 at the owner's request (`038a6c7`): Shift+P (objective set 2),
Shift+X (add XP), P (self-damage), Ctrl+N (cycle music state), Ctrl+K (clear intensity
floor), Ctrl+, / Ctrl+. (intensity floor), and their F9–F12 aliases.

### The three audio overlays

All three are IMGUI, drawn at 200% (the owner's display is high-resolution), and mutually
exclusive through `AudioOverlays` — see the single-authority table above. Each carries an
**X** in its top-right corner (`AudioOverlays.DrawCloseButton`), and each can also be
toggled from buttons on the pause menu, which `PauseMenu` stacks above the authored
`Button - SKIP TO DAY 2`. Nothing about those buttons is hard-coded, so re-authoring the
canvas moves them; renaming that button hides them with a warning.

The matching itself lives in **`CanvasButtonStyle`** (`UI/`), shared by `PauseMenu` and
`TitleScreen`. It reads the authored button's own font (`SairaCondensed-SemiBold`, via
`TMP_FontAsset.sourceFontFile`), its background sprite and its resting tint at runtime,
and builds an IMGUI `GUIStyle` from them — the scene's sprite is Unity's built-in
`UISprite` drawn 9-sliced, which IMGUI reproduces exactly by borrowing the texture and
its border widths, so the rounded corners are a copy rather than an approximation. It
finds buttons through `FindButtons`, which fills a name-keyed dictionary in **one** pass — the sweep behind it (`Resources.FindObjectsOfTypeAll`) walks every loaded `RectTransform`, so `PauseMenu` asks for both of its buttons together rather than twice, and the sweep is skipped entirely when the preferred root already answered. It deliberately does **not** set `fontSize`: `PauseMenu` sizes labels from the anchor's
height and shrinks them to fit, `TitleScreen` picks a fixed size. A scene without that
button falls back to a flat white fill rather than failing.

**The overlays defer to the title and end screens.** `AudioOverlays.Toggle` refuses to
*open* while `TitleScreen.IsShowing` or `EndGameScreen.IsShowing`: those draw at
`GUI.depth = -1000` and the overlays set no depth, so one opened underneath is unreadable
and unclickable, which reads as a broken shortcut. Closing is always allowed, so an
overlay can never be stranded.

**Slots that cannot apply are not counted.** The scanner is a generic reflection walk, so it
would otherwise report the axe's three reload clips as empty forever — a melee weapon has no
magazine, and a permanently red row trains the reader to ignore red. `WeaponSoundProfile`
implements `IAudioSlotFilter` and hides `reloadStartSound`, `reloadEndSound` and
`reloadActionSounds` when `weaponType` is `Axe`. It is keyed on the enum, not the name, so a
second melee weapon needs no edit; the **crossbow is deliberately excluded from the
exclusion** — it is a `Gun` to that enum, and nocking an arrow is a reload worth hearing.
`InteractionSoundProfile` does the same (2026-09-03): every interaction profile carries every
clip field, so a Firestick used to list a bite sound and a sizzle loop it will never play —
218 rows for the manager. `PlayerSoundProfile` is the third (2026-09-03): its three entries
share one class and `soundType` decides which fields the manager reads, so the Hunger entry
listed both stamina slots and the heartbeat loop it will never play (12 rows → 6; 7 since
the hunger loop was added, which the filter keeps under Hunger alone). This is
the pattern to reach for whenever the report shows a slot the code cannot play; the audit
behind it found no fourth case — in every other profile class an enum decides *when* a clip
plays, not *which fields exist*. `ClipFieldsFor(interactionType)` mirrors the per-type split the
custom inspector draws (change both together), and the section is down to the 25 slots that
are real work. A profile whose only clip array is empty (the Turntable) has no rows at all —
the wiring validator is what flags that one.

The progress report is centred with an opaque dark-grey panel (the default `GUI.skin.box`
is translucent and the level showed through it); the event monitor and music overlay are
pinned top-right. Its rows read `● BEEP  Grass ▸ Footsteps ▸ [0]`, `● REPLACE
Walkie Talkie ▸ Walkie Clips ▸ [0]`, `○ EMPTY  Outside ▸ Ambience Clip` and, once a real
clip is in, `✓ Outside ▸ Ambience Clip — Forest_Day.wav`: only implemented rows name
their clip. Everything in `Assets/Audio/Demo sounds` (the two beeps, the walkie-talkie
chatter, the demo heartbeat) is a placeholder; keep "beep" or "demo" in any new
placeholder's file name so a build, which cannot read asset paths, still catches it. **Footstep plays are hidden from the event monitor** — they fire several
times a second and logged twice per step; `AudioEventEntry.Noisy` marks them, set through
opt-in parameters on `AudioUtil.PlayRandomOneShot` and `AudioProfileKeys.TryGetProfile`.
Footstep *misses* are deliberately still shown.

## Editor tooling

- One custom inspector per audio manager (foldout-based profile editing); the
  stale-foldout and missing-Undo issues from the first audit were fixed in phase 5.
- `WaveManagerExporter` / `ObjectiveManagerExporter` hand-build JSON into
  `<project>/Exports/` (outside `Assets/`).
- `MusicTestStemGenerator` — `Tools/Music/Generate Test Stems`; synthesises the layered
  placeholder music content and its `MusicStateDefinition` assets (see the music plan).
- `WeaponSetupTools` — `Tools/Weapons/Create Current Weapon Panel` (2026-09-03: builds the
  bottom-right current-weapon panel from the Synty Apocalypse HUD pack's
  `HUD_CurrentWeapon_Rack01` prefab — the rusty box with the side-on weapon picture and the
  rounds left, as in the pack's own FPS demo; the owner tried the Minimal `GunAmmo` variant
  on 2026-09-03 and came back to the box as the better style match — under
  `Canvas - Interface/HUD/ScreenSpace/Bottom Right` (a corner container it creates to match
  the others), wires `CurrentWeaponHUD`, and fills each weapon pickup's `hudIcon` with the
  `_Side` view of its hotbar icon (an authored icon is kept; prefab path and icon variant
  are two constants, `PanelPrefabPath` / `HudIconSuffix`). Re-runnable: a panel built from
  this prefab is re-wired, one built from another Synty variant is replaced),
  `Tools/Weapons/Create Ammo Readout` (adds the older TEXT readout to
  `Canvas - Stats` and points every firearm's `ammoText` at it — ⚠ that canvas is
  **disabled** in the scene, so the text never shows; the panel is the readout the player
  actually sees) and `Tools/Weapons/Seed Reload Sounds` (fills the SMG/Revolver `reloadActionSounds` with the
  ported reload takes in `Assets/Audio/Weapons/`, falling back to `Beep to replace.wav` if a
  clip is missing, and never overwriting authored audio; it also raises `reloadVolume` from
  `0` to `1` on the firearms — the profiles predate that field, so they serialized `0` and
  would have reloaded **silently** whatever clip was assigned). Both are menu items rather
  than direct scene edits because the `.unity` file on disk is routinely older than what is
  open in the editor.
- **Unity CLI + MCP (2026-09-03):** the official Unity CLI (`unity`, installed at
  `%LOCALAPPDATA%\Unity\bin`, `unity self-update` to update) manages editors, builds and
  tests from the terminal, and hosts the official MCP server. The project carries
  `com.unity.pipeline` (the editor-side bridge; experimental) and a checked-in
  `.mcp.json` starting `unity mcp --project-path <this project>`, so Claude Code
  sessions opened in this repo can drive the LIVE editor (inspect scenes, run menu
  items, evaluate C#) instead of relying only on headless batch runs. `unity status`
  lists connectable editors; `unity command` / `unity command eval` execute against
  one. The agent skill lives at `~/.claude/skills/unity-cli`. Remove the package +
  `.mcp.json` before a student handout if the cohort should not ship with them.
- `PortedPropTools` — `Tools/Interactions/…`; builds the mushroom and well prefabs and
  keeps the scene's interaction audio profiles current. Since the 2026-09-03
  consolidation the model is **one profile section per prop**: `Flashlight` (both
  toggles), `Cooking` (sizzle/chime — the collect and burn one-shots were removed 2026-09-03 at the owner's request; the fish still burns and is still collected, silently), `Fishing` (cast/bite/tension/
  escape/catch — per-tier overrides stay on the rod), `Mushroom Eating` (eat sound +
  dialogue lines), `Walkie Talkie` (chatter + playback mode + min/max gap),
  `Turntable` (tracks + playback mode + min/max gap — it replaced the tape recorder
  entirely), `Well`, `Well Splash`. `Add Missing Audio Profiles` removes the retired
  `Tape` rows, merges the old `Flashlight On/Off` pair, aligns ids and types, and
  seeds the Fishing profile with the two clips that used to live on the rod prefab;
  `Remove Placed Tape Recorders` clears any recorders still standing;
  `Remove Crossbow Weapon Profile` drops the unused crossbow row from the
  WeaponAudioManager until that weapon ships in a later revision. Feature components fetch their profile through the now-public
  `InteractionAudioManager.TryGetProfile(string, …)` and drive their own loops/delays.
  See [MUSHROOM_TRIP.md](MUSHROOM_TRIP.md).
- `LevelFeatureTools` — `Tools/Audio/Add Dawn Chorus` (sunrise payoff component: chorus
  swell + stinger + XP), `Tools/Interactions/Add Well Offering` (targeted prefab edit
  adding `WellOffering` to the well: dropped items are swallowed, splash after a depth
  delay, returned — sometimes upgraded — at dawn) and
  `Tools/Enemies/Add Boss Controller` (adds `BossZombieController` to whichever prefab
  the EnemyAudioManager's Boss-type profile names, via a targeted prefab edit),
  `Tools/Audio/Add Heartbeat Layer` (low-health heartbeat + optional lowpass snapshot),
  `Tools/Items/Create Flashlight Pickup` (toggleable torch; no battery - both toggle
  clips live on the manager's single `Flashlight` profile),
  `Tools/Interactions/Add Turntable Interaction` (the studio deck: E toggles the
  record; tracks, playback mode - once-in-order plays the record through and stops,
  random-continuous shuffles under no-repeat rules - and the min/max gap between
  tracks all author on the manager's `Turntable` profile; 3D from the deck, the
  Record child spins at 33 rpm while playing; it replaced the tape recorder),
  `Tools/Items/Create Walkie Talkie Pickup` (SM_Prop_Radio_01 in the standard pickup
  sphere; while carried it plays chatter from the `Walkie Talkie` profile with an
  authorable min/max silence between bursts and a playback mode; 2D, silent when
  dropped; bakes a transparent icon unless a PNG sits at
  `Assets/Walkie Talkie/Walkie Talkie Icon.png`),
  `Tools/Interactions/Add Campfire Cooking` (cook raw fish: sizzle loop as the
  progress bar, done-chime, burn-if-forgotten; cooked output is a plain `FoodItem` so
  catch objectives are never paid twice) and `Tools/Items/Seed Fish Tiers` (weighted
  rarity bands on the rod prefab - rarer fish wait longer, give a shorter catch window,
  and can carry their own bite/catch clips; an empty array keeps the single-fish path)
  and `Tools/Objectives/Add Day 3 Cooking And Offering Objectives` (creates the two
  `ObjectiveData` assets, registers them with `ObjectiveManager.allObjectives`, and adds
  day-3 entries to the `DailyObjectiveData` asset the scene references).
- `DistributionResetTools` — `Tools/Distribution/Reset Audio Clips For Handout`; readies
  the level for a new student cohort. Every `AudioClip` field on the audio manager
  components becomes `Beep to replace.wav` (arrays keep their sizes, every element
  beeped); `FootstepAudioManager` gets its own `Beep to replace - footsteps.wav` in every
  footstep / jump / land slot (2026-09-03 — it used to be emptied to zero slots, but the
  footsteps are the first thing students replace, so they are audible placeholders and
  BEEP rows like everything else). `AudioSource` components are never touched — only `AudioClip` fields on the
  managers, recognised by their serialized type, never an AudioSource's clip (Resource)
  slot. Also covers the FEATURE components carrying authorable clips (rod, mushroom
  dialogue, dawn chorus, heartbeat, flashlight, cooking, boss, sound props) in the scene
  AND inside their prefabs, and empties the `MusicStateDefinition` stem assets, so the
  whole Ctrl+G surface resets. ⚠ **Maintenance:** the TYPE lists and PREFAB paths inside
  the tool must be extended when a new audio manager, clip-carrying component or prefab
  is added; clip fields *within* a listed type are discovered automatically by walking
  the serialized object. Scene edits undoable; prefab/asset edits are not (git is the
  way back). SAVE THE SCENE afterwards.
- `AudioProfileEnumMigration` — `Tools/Audio/Migrate Profile Keys To Enums`; fills in the
  profile `*Id` fields by matching authored names, never overwrites a hand-set id, never
  edits a name, and logs everything it could not match. One-way and safe to re-run.
- `AudioProgressReportWindow` — `Tools/Audio/Student Progress Report`; the grading view,
  with a **Validate wiring** button running `AudioWiringValidator`. Same scanner and the
  same BEEP / REPLACE / EMPTY rows as the Ctrl+G popup.
- `DistributionResetTools` and `WeaponSetupTools` load the beep from
  `AudioProgressScanner.DemoSoundsFolder` (`Assets/Audio/Demo sounds/`), where the
  placeholders have lived since 2026-09-03.
- `InteractionProfileTools` — `Tools/Audio/Sort Interaction Profiles`; reorders the
  manager's `interactionProfiles` array by name, which is what the inspector, the Ctrl+G
  report and its export all read. Safe because array order is cosmetic: the runtime only
  iterates, `profileLookup` is rebuilt from the array by name on Awake, and
  `TryGetProfile` resolves by name or id. Stable ties, idempotent, Undo-recorded.
- `AudioManagerTidyTools` — `Tools/Audio/Remove Legacy Music Controller` (drops the
  unwired component, and its host object when that carried nothing else; logs which
  speaker AudioSources it pointed at and cross-checks them against the Mixing Desk
  profile, touching neither) and `Tools/Audio/Remove Pickups Audio System` (takes the
  pickups manager and its GameObject out, now that the skulls and chest it played for are
  gone — it replaced a "Move Pickups Audio Manager" tidy from earlier the same day). Both
  find their type **by name through reflection**, so deleting those classes afterwards
  leaves this file compiling, and both are the phase (a) of a two-phase removal: the
  component goes, the owner saves, then the class is deleted. Idempotent and
  Undo-recorded.
- ⚠ Editors bind by hardcoded property-name strings (no `nameof()`), and
  `WaveManagerExporter`/`GameTesting` use `FindProperty`/reflection by name — **renaming
  a serialized field compiles clean and breaks these silently.** Grep
  `Assets/Scripts/Editor` and `Assets/Scripts/Testing` for the old name whenever you
  rename one, and grep the `.unity`/`.prefab` files too (UnityEvents call methods by
  name).
