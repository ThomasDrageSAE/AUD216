# Documentation — 26T1 AUD216 Project 1

This folder lives outside `Assets/` on purpose — Unity does not import it.

## Start here (especially for AI coding sessions)

1. Read [CODE_STRUCTURE.md](CODE_STRUCTURE.md) — project map, audio architecture, the
   work history, the single-authority rules, and the open work.
2. Read the **ground rules** at the top of [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md)
   — serialized-field cautions, scene-wiring checks, the do-not-delete list.
3. After every edit, run `bash Documentation/tools/compile-check.sh` (details below).
4. Before deleting **anything** that looks generated or unused, read
   [TERRAIN.md](TERRAIN.md) — this project once lost all its terrain textures to a
   well-intentioned cleanup.
5. This is a **sound-design** project: audio-state bugs (double-fired sounds, loops that
   outlive their owner, music that never relaxes) outrank almost everything else.
6. When you need a decision from the owner, ask it as a **multichoice question with a
   recommended answer** — see [Asking the owner questions](#asking-the-owner-questions)
   below. Open-ended questions are not accepted.

## Asking the owner questions

The owner reviews AI sessions asynchronously and answers by picking an option. Every
question — in chat, in a ⏸ checkpoint written into a work order, in a commit message —
must be **multichoice, with the answers spelled out and one of them recommended**.
Never ask an open-ended question ("how would you like…?", "should I…?").

### Format

Prefer the `AskUserQuestion` tool when the session has it: the recommended option goes
**first** with the label suffix ` (Recommended)`, every option gets a one-line
`description` of what choosing it means, and related questions are batched into a single
call. When the tool is not available, use this exact plain-text shape:

```
**Q1. Port compile-check.sh to Windows, or keep it macOS-only?**
- **A.** Port it — detect the OS and look under `C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Data\`
  as well as `/Applications/Unity/Hub/Editor/` ← **recommended:** it is the project's
  only automated check and the owner's machine is Windows
- **B.** Keep it macOS-only and document the Windows limitation — zero risk, but no
  compile verification on this machine
- **C.** Replace it with a Unity batch-mode compile — slower (~1 min) but also catches
  scene-wiring breakage; needs a licence-activated editor on the machine

**Q2. …**
```

Rules:

| Rule | Why |
|---|---|
| 2–4 options per question, each with a one-line consequence or trade-off | The owner should be able to choose without re-deriving the analysis |
| Exactly **one** option marked recommended, with the reason | A recommendation without a reason cannot be disagreed with |
| Options are mutually exclusive unless the question says **pick any** | Letters must map to a single action |
| Number questions `Q1, Q2, …` and letter options `A, B, C, D` | The owner answers `Q1 A, Q2 C`; "Other / free text" is always implied |
| Do not ask what the repo, the code, `git log`, or the docs can answer | Questions cost the owner a round trip; facts cost the session a grep |
| Collect questions while working and ask them together at the end of the turn | One batched answer beats five interruptions |
| Interrupt mid-task only when the answer changes the work materially, and say that it is blocking | Everything that does not depend on the answer should already be done |

This convention is also summarised in the repo-root [`CLAUDE.md`](../CLAUDE.md), which
Claude Code loads automatically.

## Document index

| Doc | Status | Contents |
|---|---|---|
| [PROMPT_unity-profiling-via-cli.md](PROMPT_unity-profiling-via-cli.md) | **Reusable prompt** (2026-09-04) | Project-agnostic: paste into any Claude Code session with the Unity CLI bridge to profile a different game. The method and every gotcha from this project's run — the Profiler window requirement, counter resolution by name, the 5 s eval limit, cumulative console tails, settle time, `timeScale` checks, editor-vs-shipping memory, verifying stack traces before attributing cost |
| [AUDIO_HANDOUT_TASKS.md](AUDIO_HANDOUT_TASKS.md) | **Work order — done and verified 2026-09-10** (four agents, one integrator; self-test 11/11) — **read its STATUS block and "How to use it"**: the project has two audio states, the owner's authored sounds and routing and the student handout (every slot a beep, routing cleared), and three menu items move between them through a snapshot. The proof is a round trip whose export comes back identical |
| [AUDIO_INTEGRITY_TASKS.md](AUDIO_INTEGRITY_TASKS.md) | **Work order — done and verified 2026-09-09** (six agents, one integrator; report 224 → 244, probe nine of nine, zero errors) — **read its STATUS block** for the measured numbers, the two things the probe found, and the crossbow's missing profile | The pass that makes the existing work true: the report hides thirteen slots the code plays (headshots on all five enemies, the mushroom dialogue, the boss, the fishing rod) and offers three it never plays (both gun reload slots, a dead interaction slot); a crossfade freezes at half volume under the death screen; the boss's pitch tell is erased by its next vocalisation; the enemy no-repeat never worked; two projectiles can throw on contact. Every claim has a line number, every lane a disjoint file list, and the end state is a number: 224 → 244 |
| [ENEMY_ROLES_AND_FEEDBACK_TASKS.md](ENEMY_ROLES_AND_FEEDBACK_TASKS.md) | **Work order — done and verified 2026-09-07** (six agents, one integrator) — **read its STATUS block**: five of the order's own claims were wrong and the agents caught all five, and it carries the measured numbers plus the one thing left for the owner's ear | Two of the ten engagement recommendations the owner picked: zombie **roles** (Ghoul runner, Butcher front-facing shield that headshots bypass, Demon lobbed-spit projectile, Yokai screamer that rallies the pack) layered over `ZombieAI` the way the boss is, each with its own tell as an assessed slot on the enemy's profile; and **combat feedback** (reticle hitmarker, headshot ping, kill XP popping at the corpse, per-weapon screen kick replacing two hard-coded multipliers). Eight new slots, report 216 → 224. Phase 0 is the integrator's two seams in `ZombieAI` (a hit event and a damage-modifier hook); the enemy audio surface lands first so the roles compile against it; then five agents on disjoint files. Carries the owner's four decisions and the survey's numbers |
| [HUD_AND_BARREL_TASKS.md](HUD_AND_BARREL_TASKS.md) | **Work order — all four done, verified 2026-09-06** (written the same day at `bb816c1`) — **read its STATUS block**: three of the order's own claims were wrong, and the agent running each job caught its own | Four owner requests, all surveyed first: the minigun barrels spin like a propeller (one wrong axis - `Vector3.forward` on a Y-long cluster; fix by deriving the axis from the fire point), the hotbar's yellow equipped border (Synty's own `Selected` child already exists in every slot, disabled; nothing activates it), and the compass top-right (it already exists and draws top-centre - the job is to move it), and the grenade reskinned onto the owner's new `Assets/Grenade.fbx` with the pack's pineapple icon `ICON_SM_Wep_Grenade_01_BR`. Four agents on disjoint files, one integrator, numeric barrel verification through the CLI |
| [GRENADE_TASKS.md](GRENADE_TASKS.md) | **Work order — built and play-verified 2026-09-05**, scene saved by the owner; reskinned onto the owner's own `Grenade.fbx` 2026-09-06 | A throwable explosive: pipe-bomb pickup carried as a weapon with a count, thrown on the fire button, bounces on the students' own surface sounds, 3 s fuse, radius damage with line of sight, self-damage through the directional blood + a scaled camera shake + a generated ringing. Exactly **two new assessed audio slots** (Throw, Explosion — the report goes 104 → 106), the explosion played in 3D at the grenade. Three agents on disjoint files (gameplay, audio surface, setup tool), one integrator; carries the owner's four design decisions and every verified anchor |
| [PERFORMANCE_FIX_TASKS.md](PERFORMANCE_FIX_TASKS.md) | **Work order — not started** (written 2026-09-04 at `365aca2`, for Opus with parallel agents) | Phase 3 of the profiling run: the six owner-approved fixes. Three agents on disjoint files (singleton guards across 31 sites; the zombie repath throttle and health-bar gating, in a worktree so it lands last; a `Tools/Performance/` menu for terrain instancing, shadow distance 25, FXAA and the duplicate Bloom), one integrator driving the editor with a capture between every fix group. Carries the owner's decisions, the verified stack trace, and the warning that the exception's per-frame cost did not reproduce |
| [PERFORMANCE_FINDINGS.md](PERFORMANCE_FINDINGS.md) | **Findings — phase 2 done; phase 3 gated on the owner** (2026-09-04) | The ranked result of the profiling run: twelve findings, the leads, the audio observations, and the order phase 3 would work in. Leads with the four ways the capture itself was flawed — two scenarios were captured with the game paused behind a death screen, and the two "slowest" scenarios were warming up |
| [PERFORMANCE_PROFILING_TASKS.md](PERFORMANCE_PROFILING_TASKS.md) | **Work order — phases 0–2 done; phase 3 gated** (written 2026-09-04 at `a2a1e53`, run the same day) | Measure-then-recommend: the integrator captures eight play-mode scenarios through the Unity CLI bridge (a runtime `ProfilerRecorder` probe + `ProfilerDriver.SaveProfile` exports into the gitignored `Exports/Profiling/`), four read-only agents analyse CPU / rendering / memory / physics-AI-audio in parallel, the integrator ranks the findings and asks the owner; fixes are phase 3, gated per item. Carries the verified anchors (URP + quality settings, 11 realtime lights with 6 shadow casters, 5/1165 static objects, GPU Resident Drawer set but disabled, two per-frame NullReferenceExceptions) and the bridge's quirks |
| [AUDIO_MANAGER_TIDY_TASKS.md](AUDIO_MANAGER_TIDY_TASKS.md) | **Work order — implemented 2026-09-03; owner checkpoints ⏸ open** (four parallel agents + integrator; read its STATUS block first) | Six owner requests on the audio managers and the Ctrl+G progress report: shorter report labels ("Footsteps", "Ambience"), interaction profiles sorted alphabetically, Cooking loses collect/burn, `PlayerSoundProfile` gets a per-type slot filter (+ audit of the pattern), the legacy `MusicController` goes, `PickupsAudioManager` moves under Audio Managers. Answers the owner's "what is Music Controller / Pickups Audio Manager" questions up front. Agent file-ownership table, ⏸ checkpoints, expected report totals |
| [CODE_STRUCTURE.md](CODE_STRUCTURE.md) | **Live reference** (revised continuously; last 2026-09-03) | Project facts, folder map, audio architecture incl. the adaptive music system, wiring conventions, single-authority rules, gameplay→audio call map, editor tooling |
| [ROADMAP.md](ROADMAP.md) | **Rationale record — all 10 implemented** | The prioritised top-10 and why it was ordered that way, plus the Audio Progress Report — the student/teacher assessment popup (Ctrl+G in game, Tools ▸ Audio in the editor). For current state read ROADMAP_TASKS.md's STATUS block |
| [ROADMAP_2_TASKS.md](ROADMAP_2_TASKS.md) | **Work order — all 5 implemented; owner checkpoints ⏸ open** | Round-2 implementation instructions: player-audio authoring surface (C5–C7), audio event monitor, title screen + persisted volumes, caller-side profile ids, known-issues cleanup (A5 + open D-items). Verified anchors as of `13ce2e2` |
| [ROADMAP_TASKS.md](ROADMAP_TASKS.md) | **Work order — all 10 implemented, play-verified 2026-08-18** | The ten roadmap items as concrete implementation instructions. **Read its STATUS block first** — it records what was built, two deviations from the work order, and everything still waiting on the Unity editor |
| [MUSHROOM_TRIP.md](MUSHROOM_TRIP.md) | **Live reference** — ported 2026-09-01; setup completed 2026-09-02, clip authoring open | Props ported from a student submission (Liam Chapman, 26T2 AUD216): edible mushrooms with a post-processing "trip" and camera drift, and a stone well. Records why the visuals were rebuilt rather than copied, the owner setup steps, the camera-pivot reasoning, and why each prop carries its own AudioSource |
| [TERRAIN.md](TERRAIN.md) | **Live reference** | Terrain/terrain-layer setup, footstep surface mapping, asset-GUID reference rules for safe CLI edits, and the 2026-08 terrain-texture recovery record |
| [MUSIC_AUDIT_2_TASKS.md](MUSIC_AUDIT_2_TASKS.md) | **Implemented 2026-08-26; audit record** (now incl. Audit 3, 2026-09-02) | Fixes for the second music audit's four highest-impact findings: flag-derived state + priority `cannotOverride` (no more direct `ChangeState` from triggers), fade-not-cut on takeover cancel, audio-device-change recovery. Verified anchors as of `e6cee03` |
| [MUSIC_LAYERING_PLAN.md](MUSIC_LAYERING_PLAN.md) | **Live reference** — implemented, audited three times, **live in the scene since 2026-08-25** | Adaptive music design (vertical stems + horizontal re-sequencing), compatibility contract, both audit records, and authoring recommendations for the audio person |
| [IMPROVEMENT_PLAN_3.md](IMPROVEMENT_PLAN_3.md) | **Work order — mostly done** | Gameplay-systems audit (weapons, inventory/fishing, player, enemies). A1–A3/A6/A7-YAML, all §B, C1–C4, D2/D5/D8 and the §E sweep are committed; still open: A5, A7 `firePoint`s, A8, C5–C7, D1/D3/D4/D6/D7/D9–D12. ⚠ **treat all per-item text as historical** — five §E claims were wrong; see ROADMAP_TASKS.md's STATUS block |
| [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md) | **Done** (all phases implemented, play-verified) | Objectives-system deep dive; its **ground rules** section remains the standing editing contract for the repo |
| [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) | Historical (with corrections) | The original phased fix plan from the first audit; its STATUS block records what was actually done and deviations |
| [AUDIT_FINDINGS.md](AUDIT_FINDINGS.md) | Historical | The first audit's findings (2026-07-06). Mostly fixed since — verify against current code before acting on anything in it |

## Verifying a change without opening Unity

```bash
bash Documentation/tools/compile-check.sh
```

Compiles both `Assembly-CSharp` and `Assembly-CSharp-Editor` using the Roslyn
compiler and .NET runtime bundled with the installed Unity editor, against the
reference set in the project's own `.csproj` files (reconciled against what is
actually on disk, since those files go stale between editor sessions). Exits 0
when both build, otherwise prints the errors. Takes a few seconds.

**Platforms (since 2026-08-20):** runs on macOS and on Windows under Git Bash. It
locates Unity at `/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents` or
`C:\Program Files\Unity\Hub\Editor\<version>\Editor\Data` (override with
`UNITY_VERSION=…` or `UNITY_ROOT=…`), and finds Python as `py -3`, `python3` or
`python` — on Windows the bare `python3` is usually the Microsoft Store stub, which is
why it probes. It needs `Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj` in
the project root (gitignored; the Unity editor regenerates them when you open a
script), and **exits 1 with a ❌ when they are missing** — so a fresh clone that has
never been opened in Unity on the current machine cannot be compile-checked until it
has. A green result is the two ✅ lines, one per assembly.

This is the only automated check the project has — there are no unit tests. It
catches compile breakage only. It **cannot** catch:

- serialized field renames or removals that drop scene/inspector values,
- `UnityEvent` wiring that points at a renamed or deleted method,
- Editor scripts binding properties by string (`serializedObject.FindProperty("…")`)
  or reflection (`GetField("…")`) — these silently degrade rather than fail,
- anything about how the game actually plays or sounds.

Check those by hand: grep the `.unity`/`.prefab` files for the field or method
name before renaming or deleting it.
