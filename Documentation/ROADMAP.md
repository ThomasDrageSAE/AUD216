# Roadmap — Top 10 Projected Updates

> Written 2026-08-18. A prioritised projection of the work the project owner will most
> likely want next, based on everything the audits found, what remains open, and the
> project's purpose: **a level handed to students who implement its sounds and music
> for assessment.** That lens ranks this list — player-facing completeness and the
> student authoring surface outrank internal elegance.
>
> Each item names the doc that already specifies it in detail. Nothing here is new
> analysis; this is the ordering.
>
> **Implementation instructions for all ten items live in
> [ROADMAP_TASKS.md](ROADMAP_TASKS.md)** — hand that file to the implementing AI.
>
> **STATUS (2026-08-18): all ten items are implemented and committed**
> (`b6e9ed7`…`af011c0`). The table below is preserved as the rationale and ordering
> record — its "why it's next" phrasing describes the state *before* implementation.
> What actually remains is the in-editor work: the ⏸ owner checkpoints listed in
> ROADMAP_TASKS.md's STATUS block (playthroughs, the music editor steps, the
> profile-id migration run, prefab re-saves, pickup-variation authoring).

| # | Update | Why it's next | Detail lives in |
|---|---|---|---|
| 1 | **Scene integrity: one `Player` tag, `PlayerStats` on the root, drop the extra capsule** | Three objects tagged `Player` make ~14 call sites non-deterministic — zombie damage, eating, radars can all silently break depending on which object Unity returns. Everything else sits on top of this. Editor work, ~30 min. | [IMPROVEMENT_PLAN_3](IMPROVEMENT_PLAN_3.md) §A1, A3 |
| 2 | **Restore the crossbow** (scene `ArrowPool`, continuous collision on the bolt, arrow lifetime) | A whole weapon is currently a prop that plays sounds and fires nothing — and it carries a rich sound-design surface (draw, release, flight, impact, stick) students can't score against until it works. | Plan-3 §A2, A6, D5 |
| 3 | **Complete the game loop: chest end screen + restart button** (plus the DDoL scene-reload fixes that must land first) | Players can currently neither finish the game (endScreen unwired) nor retry after death (game quits). A restart button requires the documented `sceneLoaded` re-acquisition work or the reloaded game is unplayable. | Plan-3 §A4 + CODE_STRUCTURE "scene reload" hazard |
| 4 | **Audio routing fixes**: fishing SFX off the UI bus, pooled impact sources, mixing-desk power-off, sparks A/V sync | The assessment is audio: student sounds are only as good as the routing lets them be. Fishing SFX currently can't be spatialised or mixed; impact beds audibly slide across the level; the desk ignores the power state. | Plan-3 §C1–C4 |
| 5 | **Bring the adaptive music alive**: run the stem generator, assign the assets, verify by ear, then author real stems; add the intensity fade band as the first upgrade | The flagship system is implemented, audited, and dormant. Two editor steps activate it; the audible acceptance checks have never run. The fade band (one number per stem) is the queued upgrade if stepped layer-adds feel mechanical. | [MUSIC_LAYERING_PLAN](MUSIC_LAYERING_PLAN.md) STATUS + §6.5 |
| 6 | **Weapon feel pass**: axe fire-gate honesty, respect inspector-authored swing values, framerate-correct recoil, weapon bob settling | Roughly every other axe swing plays its sound but deals no damage — the worst kind of bug for a sound-design showcase (the sound lies). All four items change game feel, so they need someone playing while tuning. | Plan-3 §B1, B2, B3, B5 |
| 7 | **Framerate-correct survival ticks, then a balance pass** | Stamina/hunger currently run ~25% slow at 30fps, so difficulty varies per machine. Fixing the tick maths changes effective balance, so re-tune hunger decay and sprint economy in the same session. | Plan-3 §B4 |
| 8 | **Enum-keyed audio profiles** (the long-deferred W6), with an editor migration script | The single biggest source of silent student errors: a typo'd string key ("Zombie " vs "Zombie") produces no sound and no warning. Enum keys turn that into a compile/inspector error. Needs the migration tool + dedicated in-editor verification time — do not attempt casually. | [IMPROVEMENT_PLAN_2](IMPROVEMENT_PLAN_2.md) W6 |
| 9 | **Student authoring guardrails**: profile-name validation (warn on enemy prefabs whose `enemyAudioProfileName` matches no profile, empty profile names, orphaned clips), building on the new Audio Progress Report | The progress report (Ctrl+G / Tools ▸ Audio ▸ Student Progress Report) shows *coverage*; validation catches *wiring mistakes*. Together they close the two ways student audio silently fails. | This doc; report shipped 2026-08-18 |
| 10 | **Dead-code sweep + prefab hygiene**: Plan-3's verified-dead list, re-save the weapon prefabs (orphaned fields, unassigned `firePoint`), real pickup-sound variations | A smaller, honest codebase is a teaching asset in itself — students read this code. Last on purpose: sweeps are safest when nothing above still needs the code being swept. | Plan-3 §E, A7, A8 |

**Sequencing notes:** 1 → 2 → 3 are ordered by dependency (restart needs the reload
fixes; the crossbow's sounds matter only once hits register). 4 and 5 are independent
and can interleave. 6 and 7 want play-test sessions. 8–10 are quality-of-authoring
work best done between student cohorts rather than mid-assessment.

## The Audio Progress Report (shipped with this roadmap)

The student/teacher progress popup requested alongside this roadmap is implemented:

- **In game:** `Ctrl+G` toggles the popup (self-installing, works in builds, not
  dev-gated — it is an assessment tool). Shows overall and per-manager completion
  bars, **two states per slot - implemented, or MISSING in red** (the owner's call,
  2026-09-06; the three not-done classifications still exist underneath for the
  validator and the counts, but every view shows one word), a "missing only" filter, and an **Export for grading** button (timestamped text file
  in `<project>/Exports`, or `persistentDataPath` in a build).
- **In the editor:** `Tools ▸ Audio ▸ Student Progress Report` — same scanner, no Play
  mode needed, refreshes on focus, export reveals the file.
- **Detection (revised 2026-09-03):** everything shipped in `Assets/Audio/Demo sounds`
  counts as not done. A clip whose name contains "beep" (`Beep to replace.wav`,
  `Beep to replace - footsteps.wav` and student copies of them) or that matches the
  generated music test-stem naming, any other clip from that folder (the walkie-talkie
  chatter, the demo heartbeat) — by asset path in the editor, by "demo" in the name in a
  build, so keep one of those words in a new placeholder's file name — and an empty slot
  all read the same: **MISSING**. In the editor, anything under `Assets/Audio/TestStems`
  is caught by path even if renamed. Since 2026-09-09 the report also reaches components
  that exist only as prefabs (the boss, the fishing rod) through `AssessedPrefabRegistry`,
  and every assessed array carries at least one element, because an empty array is no
  row at all (`AUDIO_INTEGRITY_TASKS.md`).
- **Presentation (owner, 2026-09-03):** headings are the component name in words with
  no GameObject suffix ("Ambience Manager", not "AmbienceManager (Audio Managers)"),
  the footstep manager is "Footsteps" and sits first because students start there,
  placeholder rows carry no clip name, and the leading category segment of a label
  ("Enemy Profiles", "Surface Profiles"...) is dropped once a profile name follows it.
  Clip labels also lose their "Sound / Sounds / Clip / Clips / Audio" tail, with an
  override table for the readings a bare strip gets wrong ("Footsteps", "Landings",
  "Vocalisations", "Chatter", "Calls"): the reader knows the report is about sounds.
  Profile entries are capitalised for reading ("Wood", not the authored key "wood").
  All of it is display-only — no serialized field is renamed.
- **Section order (owner, 2026-09-03):** alphabetical, except that Footsteps, Waterfall
  and the River Audio Manager are pinned to the top in that order (students work
  top-down and start with footsteps) and the Bullet Impact Manager sits last, under the
  Weapon Audio Manager it belongs with. Two lists in the scanner, `GroupsFirst` and
  `GroupsLast`.
- **The one AudioSource exception:** the report otherwise reads serialized *fields*, never
  `AudioSource.clip`. The **Waterfall** section is the exception, because its clip is
  dropped straight onto the AudioSource with no manager or profile in between and is
  assessed all the same. `DirectAudioSources` in the scanner is the table to extend if
  another such source is ever authored. ⚠ `Tools/Distribution/Reset Audio Clips For
  Handout` deliberately never touches AudioSources, so a waterfall clip is **not** cleared
  by a handout reset — clear that one by hand.
- **Scope:** every serialized `AudioClip` field on project components in the scene —
  the audio managers, the scene-wired ambient components, interactables, fishing —
  plus the `MusicStateDefinition` assets (each reported as its own group). It reads
  *fields*, never `AudioSource.clip` at runtime, so the in-game and editor views
  always agree and manager-assigned sources are not double-counted.
- Code: `Assets/Scripts/Assessment/` + `Assets/Scripts/Editor/AudioProgressReportWindow.cs`.
