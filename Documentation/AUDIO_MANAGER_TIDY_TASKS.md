# Audio Manager Tidy — Implementation Work Order (parallel agents)

> Written 2026-09-03 at commit `746ab91`. **This document is a work order for an
> implementing AI (Opus) running several agents in parallel.** It covers six owner
> requests about the audio managers and the Ctrl+G **AUDIO IMPLEMENTATION PROGRESS**
> report, plus two questions the owner asked, answered below from the code. Every path,
> line number and count below was verified against the repo and the LIVE editor scene at
> `746ab91`; **line numbers drift — re-grep before every edit.**
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (project map, single-authority
> rules, the Unity CLI section), then the ground rules in
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md). Items marked ⏸ are **owner
> checkpoints** — stop and hand back; do not fake them. Questions for the owner go in the
> multichoice format from [README.md](README.md#asking-the-owner-questions), one
> recommended option each, batched at the end of the agent's report.

## The owner's two questions, answered

**Q: What is the "Music Controller" section with the Toggle Sound for?**
`Assets/Scripts/Audio/MusicController.cs` is a legacy component that starts and stops two
AudioSources in sync ("Speaker Left" and "Speaker Right", the mixing-desk speakers) and can
play a `toggleSound` while doing so. It sits on the GameObject
`Game Management/Start Speaker Playback - Ready to Delete?` (two components: Transform +
MusicController). Nothing calls it: the only code reference is a commented-out line in
`Assets/Scripts/Interactions/ActionHandler.cs:222`, and the saved scene contains **no**
UnityEvent wiring to `ToggleMusic` / `PlayMusic` / `StopMusic` (grep of the `.unity` for
those method names returns nothing; the script guid `850db8f8d9d4148988922f665006b5ba`
appears once — the component itself). The speaker playback it used to drive is now the
`InteractionAudioManager` **Mixing Desk** profile (Left/Right Speaker Audio) and the
Turntable. The "do not delete — scene-wired" note for `MusicController` in
CODE_STRUCTURE / IMPROVEMENT_PLAN_2 dates from the first audit (2026-07) and is stale.
**Task 4.1 removes it.**

**Q: What is the Pickups Audio Manager? It is not under Audio Managers.**
`Assets/Scripts/Pickups/PickupsAudioManager.cs` is a real, live manager for the **world
pickup** system — the skull pickups: `ChestPickup` (×1 in the scene) and `StandardPickup`
(×4) call `PlayChestPickupSound()` / `PlayPickupSound()` on it. It is a different system
from inventory items (axe, food, medicine...), whose pickup/drop sounds live on
`UIAudioManager` (`Item Pickup`, `Item Drop`). The component lives on
`Game Management/Pickups/Skull Pickups/Pickups Audio Manager`, which is why it is not in
the `Audio Managers` GameObject. Its two slots show in the report as
`Pickup Sounds ▸ [0..3] ▸ Clip` and `Chest Pickup Sound ▸ Clip` because its `PickupSound`
entries have no name field. **Task 4.2 moves it under Audio Managers** (with a question
about merging it into `UIAudioManager` instead).

## Global rules of engagement

1. `bash Documentation/tools/compile-check.sh` after every edit; both assemblies must
   pass. Then `unity command recompile` and poll `recompile_status` before trusting an
   in-editor check (the CLI is set up: `unity status` lists the owner's editor; see the
   Unity CLI section of CODE_STRUCTURE.md).
2. **Scene edits are menu items, never hand edits of the `.unity` file.** The scene on
   disk is routinely older than the editor's memory. Write an idempotent `[MenuItem]`,
   run it through the CLI (`unity command menu --path "Tools/Audio/…"`), verify with an
   `eval`, and hand the save to the owner (⏸). Log `SAVE THE SCENE` from every such tool.
3. ⚠ **Line endings**: most `.cs` files are CRLF in the working copy (`core.autocrlf=true`).
   Edit byte-wise (read → replace → write back with the same endings) and check
   `file <path>` afterwards.
4. ⚠ **Harness gotcha**: the Bash tool's shell wrapper mangles C# `$"…"` interpolated
   strings and `'{…}'` sequences even inside quoted heredocs. Put edit scripts and C#
   snippets that contain them into a file with the Write tool, then run the file.
5. Before removing any serialized field or public method: grep `Assets/Scripts/Editor`,
   `Assets/Scripts/Testing`, and every `.unity`/`.prefab` (string-bound `FindProperty`,
   reflection, `m_MethodName` break silently). Removing a field only loses its stored
   value; renaming one silently drops data — this work order removes two fields and
   renames none.
6. Display only in the progress report: **never rename a serialized field to change how
   the report reads.** Strings stay the student-facing authoring surface.
7. **Parallel-agent safety**: the file-ownership table below is a contract. An agent
   edits only the files it owns, creates only the new files listed for it, and does not
   touch `Documentation/` — the integrator writes all docs at the end. If an agent finds
   it needs a file another agent owns, it stops and reports instead.
8. One task = one commit, repo message style (`git log` shows it), with the
   `Co-Authored-By` / `Claude-Session` trailer the harness supplies. Push only when the
   owner says so.

### Verified anchors (at `746ab91`)

| Thing | Value |
|---|---|
| Report code | `Assets/Scripts/Assessment/AudioProgressScanner.cs` (scanner; `TrimCategory`, `DescribeElement`, `Prettify`, `ClassifyClip`), `AudioProgressReportUI.cs` (Ctrl+G), `Assets/Scripts/Editor/AudioProgressReportWindow.cs` (`Tools/Audio/Student Progress Report`) |
| Report totals now (handout scene) | `0/118 implemented, placeholder 72, empty 46` — regenerate with `unity command eval 'return AudioProgressScanner.BuildExportText(AudioProgressScanner.Scan());'` |
| Interaction profiles (scene order, name = type) | Firestick=Firesticks, Campfire=Campfire, Cave Door=SimpleInteraction, Electrical Box=ElectricalBox, Light Switch=SimpleInteraction, Mixing Desk Music=MixingDesk, Mushroom Eating=Mushroom, Well=SimpleInteraction, Well Splash=SimpleInteraction, Flashlight=Flashlight, Cooking=Cooking, Fishing=Fishing, Walkie Talkie=WalkieTalkie, Turntable=Turntable (array `interactionProfiles`, 14 entries) |
| Interaction profile lookup | `InteractionAudioManager.TryGetProfile(string)` → `AudioProfileKeys.TryGetProfile(profileLookup, …)` (line ~296): by name / id, **array order is cosmetic** — confirm nothing indexes `interactionProfiles[i]` by position before sorting |
| Cooking collect/burn | fields `InteractionSoundProfile.collectSound` / `burnSound` (InteractionAudioManager.cs ~123–125, also in `AllClipFields` + `CookingClips`); inspector lines InteractionAudioManagerEditor.cs ~120–121; `PortedPropTools.cs` ~613–614 (`ClearObject(entry, "collectSound"/"burnSound")`); callers `CampfireCooking.cs` ~129 (`PlayCookingOneShot(p => p.burnSound, "burnt")`) and ~220 (`… p.collectSound, "collected"`) |
| Player profiles | `PlayerAudioManager.PlayerSoundProfile` (one class, `soundType` enum Health / Stamina / Hunger); playback: Health → `damageSounds`, `lowHealthLoop`; Stamina → `staminaDepletedSound`, `staminaRegeneratingSound`; Hunger → `hungerSounds`, `eatingSound` (lines ~103–246). `PlayerAudioManagerEditor.cs` ~113–135 already draws only those per type |
| Existing filter pattern | `IAudioSlotFilter` (AudioProgressScanner.cs); implementations: `WeaponSoundProfile` (axe reloads) and `InteractionSoundProfile.ClipFieldsFor` (added `746ab91`, mirrors the inspector; both files carry cross-reference comments) |
| MusicController | script guid `850db8f8d9d4148988922f665006b5ba`; scene object `Game Management/Start Speaker Playback - Ready to Delete?`; `audioSource1` = `Speaker Right`, `audioSource2` = `Speaker Left` (these AudioSources belong to the mixing-desk speakers and STAY) |
| PickupsAudioManager | `Game Management/Pickups/Skull Pickups/Pickups Audio Manager`; callers `Pickups/ChestPickup.cs:56`, `Pickups/StandardPickup.cs:5`; listed in `DistributionResetTools.BeepManagers` |
| "Audio Managers" GameObject | scene root `Audio Managers` (holds every other manager incl. `PlayerAudioManager`) |
| Placeholders | `Assets/Audio/Demo sounds/` (`AudioProgressScanner.DemoSoundsFolder`): the two beeps + walkie/heartbeat demo clips |

## Agent plan — who owns what

Run agents A–D **in parallel**; their file sets are disjoint. The integrator runs after all
four have reported. Agent D has an owner checkpoint in the middle (a scene save) and a
second phase after it.

| Agent | Task | Owns (may edit) | Creates | Must not touch |
|---|---|---|---|---|
| **A** | 1 — report labels | `Assets/Scripts/Assessment/AudioProgressScanner.cs` | — | the renderers, any manager, docs |
| **B** | 2 — interaction manager | `Assets/Scripts/Audio/InteractionAudioManager.cs`, `Assets/Scripts/Editor/InteractionAudioManagerEditor.cs`, `Assets/Scripts/Interactions/CampfireCooking.cs`, `Assets/Scripts/Editor/PortedPropTools.cs` | `Assets/Scripts/Editor/InteractionProfileTools.cs` | the scanner, docs |
| **C** | 3 — player audio filter + audit | `Assets/Scripts/Audio/PlayerAudioManager.cs`, `Assets/Scripts/Editor/PlayerAudioManagerEditor.cs`; plus any manager the audit (3.3) proves needs a filter — **list them in the report first**, and never `InteractionAudioManager` (B owns it) | — | the scanner, docs |
| **D** | 4 — legacy / misplaced managers | `Assets/Scripts/Audio/MusicController.cs` (delete, phase 2), `Assets/Scripts/Interactions/ActionHandler.cs` (one comment line) | `Assets/Scripts/Editor/AudioManagerTidyTools.cs` | managers, the scanner, docs |
| **Integrator** | verify + docs + commits | `Documentation/*.md` only | — | code (except reverting a broken agent change after telling the owner) |

Each agent's report must contain: files changed, the compile-check and recompile results,
the report rows it affects (before → after, from the regenerated export), and its owner
questions in the multichoice format.

---

## Task 1 (Agent A) — Report labels: drop "Sound / Sounds / Clip / Clips / Audio"

The owner wants `BEEP  wood ▸ Footsteps` rather than `BEEP  wood ▸ Footstep Sounds`, and
`EMPTY  Outside ▸ Ambience` rather than `… Ambience Clip`: the reader knows these are
sounds. Display only (rule 6).

### 1.1 Rule

In `AudioProgressScanner`, where a field name becomes a label (`WalkObject` →
`Prettify(field.Name)`), add a clip-field label step:

1. Prettify as now.
2. Strip one trailing word from `Sound`, `Sounds`, `Clip`, `Clips`, `Audio`, and the pair
   `Audio Clip` (so `River Audio Clip` → `River`, `Left Speaker Audio` → `Left Speaker`).
3. If nothing is left (a field literally named `clip`), **drop that path segment
   entirely** — `Chest Pickup Sound ▸ Clip` → `Chest Pickup`, `Pickup Sounds ▸ [2] ▸ Clip`
   → `Pickups ▸ [2]`.
4. Then apply an **override table** keyed on the real field name for the readings a bare
   strip gets wrong. Seed it with:

   | field | label | why |
   |---|---|---|
   | `footstepSounds` | Footsteps | the owner's example |
   | `jumpSounds` | Jumps | |
   | `landSounds` | Landings | "Lands" is not a noun |
   | `attackSounds` | Attacks | |
   | `deathSounds` | Deaths | |
   | `vocalisationSounds` | Vocalisations | |
   | `impactSounds` | Impacts | |
   | `enemyImpactSounds` | Enemy Impacts | |
   | `shootSounds` | Shots | |
   | `reloadActionSounds` | Reload Actions | |
   | `igniteSounds` | Ignition | |
   | `damageSounds` | Damage | uncountable |
   | `hungerSounds` | Hunger Warnings | "Hunger ▸ Hunger" would read twice |
   | `eatingSound` | Eating | |
   | `musicClips` | Music | |
   | `dawnChorusClips` | Dawn Chorus | |
   | `walkieClips` | Chatter | |
   | `turntableClips` | Tracks | |
   | `pickupSounds` | Pickups | |
   | `audioClips` | Calls | the two `RandomSoundSpawner`s are birds and frogs |

   Keep `Loop` (`Fire Loop`, `Sizzle Loop`, `Low Health Loop`, `Heartbeat Loop`) — a loop
   is information. Keep `Chime`, `Stinger`.
5. `TrimCategory` (drop the leading category once a profile name follows) and the
   "[index] keeps its category" rule are **unchanged**. Group titles are unchanged.

Put the table in one static dictionary next to `Prettify` with a comment saying it is
display-only and that the owner tunes it (⏸ 1.3). Unit-test by hand: regenerate the export
and diff against the pre-change export (save both under the scratchpad).

### 1.2 Expected rows (spot checks)

| before | after |
|---|---|
| `wood ▸ Footstep Sounds ▸ [0]` | `wood ▸ Footsteps ▸ [0]` |
| `Outside ▸ Ambience Clip` | `Outside ▸ Ambience` |
| `Butcher ▸ General Vocalisations ▸ Vocalisation Sounds ▸ [0]` | `Butcher ▸ General Vocalisations ▸ Vocalisations ▸ [0]` |
| `SMG ▸ Reload Action Sounds ▸ [0]` | `SMG ▸ Reload Actions ▸ [0]` |
| `Fishing ▸ Cast Sound` | `Fishing ▸ Cast` |
| `Health ▸ Low Health Loop` | unchanged |
| `Pickup Sounds ▸ [0] ▸ Clip` | `Pickups ▸ [0]` |
| `River Profiles ▸ [0] ▸ River Audio Clip` | `River Profiles ▸ [0] ▸ River` |

Counts must not change: still `0/118 · placeholder 72 · empty 46` before Tasks 2–3 land.

### 1.3 ⏸ Owner checkpoint

Hand the owner the full regenerated export (the label column only) and ask, multichoice,
whether any override reads wrong. Apply their answer as table edits — nothing else.

---

## Task 2 (Agent B) — Interaction Audio Manager: alphabetical order, no cooking collect/burn

### 2.1 Sort the interaction profiles alphabetically

New `Assets/Scripts/Editor/InteractionProfileTools.cs`, menu item
`Tools/Audio/Sort Interaction Profiles`:

- Finds the scene's `InteractionAudioManager`, takes `interactionProfiles` as a
  `SerializedProperty`, and reorders it by `interactionName` (`OrdinalIgnoreCase`, trimmed)
  with `MoveArrayElement` — Undo-recorded, idempotent (a second run changes nothing),
  `MarkSceneDirty`, logs the resulting order and `SAVE THE SCENE`.
- Why data order and not an inspector-only sort: the inspector, the Ctrl+G report and the
  export all follow array order, so sorting the data fixes all three at once. Lookups are
  by name/id (`TryGetProfile` → `AudioProfileKeys.TryGetProfile(profileLookup, …)`), so
  order is cosmetic — **confirm** by grepping for positional access
  (`interactionProfiles\[`) and that `profileLookup` is rebuilt from the array on
  Awake/enable; report what you found.
- Expected order: Campfire, Cave Door, Cooking, Electrical Box, Firestick, Fishing,
  Flashlight, Light Switch, Mixing Desk Music, Mushroom Eating, Turntable, Walkie Talkie,
  Well, Well Splash.
- Run it through the CLI, verify with the regenerated export (the Interaction Audio Manager
  section now starts with Campfire and ends with Well Splash), then ⏸ **owner saves the
  scene**.

### 2.2 Remove `collectSound` and `burnSound` from the Cooking interaction

The owner wants the Cooking interaction to carry only Sizzle Loop and Done Chime. Remove the
two fields entirely (rule 5: a field removal loses only stored values; both slots are empty
in the scene):

1. `InteractionSoundProfile`: delete the two fields and their tooltips; delete them from
   `AllClipFields` and `CookingClips`.
2. `InteractionAudioManagerEditor.cs`: delete the two `PropertyField` lines in the Cooking
   case.
3. `PortedPropTools.cs`: delete the two `ClearObject(entry, …)` lines (~613–614).
4. `CampfireCooking.cs`: delete the `PlayCookingOneShot(p => p.burnSound, "burnt")` call
   (~129) and the `… p.collectSound, "collected"` call (~220). The burnt state, its status
   message, and collecting the food all stay; they simply make no sound. If
   `PlayCookingOneShot` then has a parameter or branch only those two used, tidy it.
5. Grep the whole repo (`collectSound|burnSound|Collect Sound|Burn Sound`) — the only
   remaining hits should be the `.unity` (stored values, harmless; Unity drops them on the
   next save) and docs (integrator).

Verify: compile clean; report shows `Cooking ▸ Sizzle Loop` and `Cooking ▸ Done Chime`
only; total drops 118 → 116.

---

## Task 3 (Agent C) — Player Audio Manager: per-type filter, and the pattern behind it

### 3.1 The bug the owner saw

`PlayerSoundProfile` is one class with every field; each entry's `soundType` decides which
fields the manager reads. The inspector already hides the rest (per-type `case`s in
`PlayerAudioManagerEditor`), but the progress report walks every serialized field, so the
**Hunger** entry listed `Stamina Depleted Sound`, `Stamina Regenerating Sound` and
`Low Health Loop` — slots the code never plays for Hunger. Same pattern as the interaction
profiles, fixed the same way.

Implement `IAudioSlotFilter` on `PlayerSoundProfile`, keyed on `soundType`, mirroring the
inspector exactly:

| `soundType` | clip fields that apply |
|---|---|
| Health | `damageSounds`, `lowHealthLoop` |
| Stamina | `staminaDepletedSound`, `staminaRegeneratingSound` |
| Hunger | `hungerSounds`, `eatingSound` |

Follow the `InteractionSoundProfile` implementation (`AllClipFields` + `ClipFieldsFor` +
`IsAudioSlotRelevant`; an unknown type hides nothing) and add the cross-reference comment
in both the profile and the editor's `switch`.

Expected report: Health 2 rows (`Damage ▸ [0]` BEEP, `Low Health Loop` REPLACE), Stamina 2
rows, Hunger 2 rows — 6 instead of 12; total −6.

### 3.2 "Hunger ▸ Low Health Loop should be High Hunger"

After 3.1 the Hunger entry no longer shows a loop at all, and that is correct: **there is
no hunger loop in the code.** `lowHealthLoop` is the Health heartbeat only; hunger has
one-shot `hungerSounds`, played when hunger is critically low (`PlayHungerSound`, threshold
authored in ROADMAP_2 Task 1.4). Ask the owner:

**Q. Should hunger get a looping warning like the heartbeat?**
- **A.** No — keep the one-shot hunger warnings; a second loop would compete with the
  heartbeat ← **recommended:** it is what the code and the inspector already say
- **B.** Yes — add a `hungerLoop` slot to the Hunger type with the same start/stop logic as
  the heartbeat (new field, new playback, new editor row, filter entry, reset-tool coverage
  comes free)

Do not implement B without the answer.

### 3.3 Audit the rest of the managers for the same pattern

For every `[Serializable]` profile class the report walks, answer: does every clip field
apply to every entry, or does an enum / mode / type field decide? Candidates:
`EnemyAudioManager.EnemySoundProfile`, `WaveAudioManager.WaveAudioProfile`,
`FootstepAudioManager` profiles, `RiverProfile`, `AmbienceManager`'s tracks,
`PickupsAudioManager.PickupSound`, `FishingRod` tiers, `WeaponSoundProfile` (already
filtered for the axe — confirm nothing else). Evidence is the manager's own playback code,
as in 3.1 — never a guess. Put the table in your report; implement a filter only where a
field is provably unread for a type. **Do not restructure any profile class** (serialized
data, student-facing names).

---

## Task 4 (Agent D) — Legacy and misplaced managers

New `Assets/Scripts/Editor/AudioManagerTidyTools.cs` with two menu items. Both are scene
edits (rule 2).

### 4.1 Remove the legacy Music Controller — two phases around a save

**Phase a — `Tools/Audio/Remove Legacy Music Controller`:**
- Find the `MusicController` component. Log which AudioSources it pointed at
  (`Speaker Right`, `Speaker Left`) — **those sources stay**; confirm they are the ones the
  Mixing Desk interaction profile uses (`leftSpeakerSource` / `rightSpeakerSource`, or the
  scene objects the speakers sit on) and say so in the log.
- `Undo.DestroyObjectImmediate` the component. If its host
  (`Game Management/Start Speaker Playback - Ready to Delete?`) then has only a Transform
  and no children, destroy the GameObject too. Mark the scene dirty, log `SAVE THE SCENE`.
- Run it through the CLI; verify with an `eval` that no `MusicController` remains and the
  two speaker AudioSources still exist and still play through the mixing desk profile.
- ⏸ **Owner saves the scene.** Do not start phase b until the saved scene on disk has zero
  hits for the script guid `850db8f8d9d4148988922f665006b5ba` (a class deleted while a
  saved scene still references it becomes a "missing script" component).

**Phase b — after the save:**
- Delete `Assets/Scripts/Audio/MusicController.cs` **and its `.meta`** (the one exception
  to the never-touch-meta rule: deleting a script means deleting its meta, or Unity
  recreates an empty asset).
- Delete the commented-out `MusicController2` line in `ActionHandler.cs:222`.
- Compile clean; report has no `Music Controller` group; the docs' do-not-delete lists are
  the integrator's job (tell them).

### 4.2 Put the Pickups Audio Manager with the other managers

`Tools/Audio/Move Pickups Audio Manager`: reparent the `Pickups Audio Manager` GameObject
(currently under `Game Management/Pickups/Skull Pickups/`) to the `Audio Managers` root
object, as its last child, keeping its world transform irrelevant (it is a pure manager;
use `SetParent(parent, false)`). Undo-recorded, idempotent, dirty + `SAVE THE SCENE`.
Verify the singleton still resolves (`PickupsAudioManager.Instance` in play mode, or the
callers' `Instance?.` path) and that `DistributionResetTools.BeepManagers` still finds it
(it finds by type, so it should). ⏸ owner saves (can be the same save as 4.1a).

Ask the owner, multichoice:

**Q. Keep the world-pickup sounds as their own manager, or fold them into UI Audio Manager?**
- **A.** Keep `PickupsAudioManager`, now under Audio Managers ← **recommended:** it is a
  live system (1 chest + 4 skull pickups), pitch-varied and sequenced, and folding it
  means moving five slots and two callers for no gain to students
- **B.** Fold its `Pickup Sounds` + `Chest Pickup Sound` into `UIAudioManager` next to
  `Item Pickup` / `Item Drop`, delete the class, repoint `ChestPickup` / `StandardPickup`
  — one manager fewer, but a scene-data migration and a second reset-tool edit
- **C.** Keep it, and also give `PickupSound` a `name` string so the report reads
  `Skull ▸ …` instead of `Pickups ▸ [0]` (a new serialized field the owner then authors)

---

## Integrator — after A, B, C and D(a) have reported

1. `bash Documentation/tools/compile-check.sh`, `unity command recompile`, wait for
   `completed`, then regenerate the export and check: Interaction section alphabetical
   (Campfire … Well Splash); Cooking has two rows; Hunger/Stamina/Health two rows each;
   labels per Task 1.2; `Music Controller` group gone once D(b) is in; totals =
   118 − 2 (Task 2) − 6 (Task 3) − 1 (Task 4.1, the Toggle Sound) − any 3.3 filters =
   **109 before 3.3**. Report the exact numbers.
2. Open `Tools/Audio/Student Progress Report` through the CLI (`menu`) and the in-game
   popup (play mode → `AudioOverlays.Toggle(AudioOverlay.ProgressReport)` via `eval`,
   `capture_game_view --source screen`) and eyeball both.
3. Docs, all in one commit: `CODE_STRUCTURE.md` — Assessment row (label rule + override
   table location), the `Interaction Audio Manager` line ("Cooking (sizzle/chime/collect/
   burn)" → sizzle/chime), Player row (filter), the three new menu items under Editor
   tooling, the do-not-delete list (drop `Audio/MusicController`, with the date and why),
   folder map (`Pickups Audio Manager` location), work-history row;
   `IMPROVEMENT_PLAN_2.md` ground rule 2 and `ROADMAP_2_TASKS.md` rule 5 — add a dated
   note that `MusicController` was removed in this work order (historical docs: annotate,
   don't rewrite); `ROADMAP.md` presentation bullet (labels); `README.md` index row for
   this file with its status.
4. Commit sequence: A (labels) → B (interaction sort + cooking fields) → C (player filter
   [+ audit filters]) → D(a) tool + D(b) deletion (after the ⏸) → integrator docs. Push
   only when the owner says.
5. Owner checkpoints to hand back, in order: 1.3 label overrides · 2.1 save · 3.2 hunger
   loop question · 4.1a/4.2 save (one save) · 4.2 pickups question.

## STATUS

**Implemented 2026-09-03** by four parallel agents plus an integrator, commits `e69e393`
(Task 1), `866a9f5` (Task 2), `44e28da` (Task 3), `83a3cb7` (Task 4a) and this doc pass.
Compile-check green throughout; the live editor recompiled clean.

**The report went from 0/118 (placeholder 72, empty 46) to 0/109 (placeholder 72, empty
37)** — verified from the regenerated export against a baseline taken before any change:
−2 Cooking, −6 Player, −1 Music Controller. All eight of Task 1.2's expected label rows
match exactly.

### Deviations from this work order

1. **Agents did code only; the integrator drove the editor.** The work order gave each
   agent its own menu-item runs and verification. A second Unity editor (another project)
   was running, and the live editor is a single shared resource — four agents calling
   `recompile` and menu items concurrently is not safe. All Unity CLI work was done
   serially by the integrator instead, with `--project-path` passed explicitly so the
   right editor is targeted. File-ownership (rule 7) was unchanged and held: no two
   agents touched the same file.
2. **Task 1's clip-field test is wider than the brief said.** "Shorten only an AudioClip
   field" cannot produce two of this order's own expected rows (`Chest Pickup` and
   `Pickups ▸ [0]`), because `PickupSound` holds the clip one level down. It is now "any
   field the walk can reach a clip through", with the same `UnityEngine.Object` boundary
   `WalkValue` draws. Every field in the project ending in a strip word was enumerated:
   only those two differ from the narrow reading, and every profile-container field ends
   in `Profiles`, so none are affected.
3. **Task 3.3 found no fourth filter case.** Nineteen `[Serializable]` classes were
   audited against their managers' playback code. In every one except the three now
   filtered (weapon, interaction, player), an enum decides *when* a clip plays, not
   *which fields exist*. Nothing else was changed.
4. **The −6/−7 discrepancy resolved to −6.** An agent counted 13 player rows from the
   scene *on disk* and proposed a total of 108. The live editor had 12, so 109 stands.
   The disk copy was stale, as CLAUDE.md warns.

### Still open

- ⏸ **The owner must save the scene.** Three scene edits are in editor memory only: the
  profiles sorted, the `MusicController` component and its host object gone, the
  `Pickups Audio Manager` reparented under `Audio Managers`.
- ✅ **Task 4.1 phase (b) — done.** The owner saved, the gate was met (zero hits for guid
  `850db8f8d9d4148988922f665006b5ba` in the saved scene), and `MusicController.cs`, its
  `.meta` and both commented-out `musicController2` blocks in `ActionHandler.cs` are gone.
  `ActionHandler.ToggleMusic` survives — it is still called for the "play music" action —
  but now only refuses the action while the power is off; the desk's audio has been
  `InteractionSystemTMP` → `InteractionAudioManager.PlayMixingDeskAudio` all along.
  `Assets/_Recovery/0.unity` (archived, not in the build settings) still holds the guid.
### Owner's answers, same day (second round — three more parallel agents)

- **Checkpoint 1.3 — fix both label problems.** `staminaDepletedSound` → "Depleted" and
  `staminaRegeneratingSound` → "Regenerating" (the row already says "Stamina"), plus
  `headshotSounds` → "Headshots" and `phaseCrackSounds` → "Phase Cracks" to match their
  plural siblings. Commit `a621465`.
- **Checkpoint 3.2 — yes, add a hunger loop.** Built as the heartbeat's twin: a Hunger
  Loop section on the Hunger profile (clip, its own AudioSource, a fraction threshold
  defaulting to the same 0.25 as `hungerWarningFraction`, and a volume), the matching
  start/stop quartet on the manager, and `PlayerStats` driving it from the band it
  already tracks — stopping on the frame the player eats out of it, and in `Die` beside
  the heartbeat. It never falls back to the shared AudioSource: a loop owns its source's
  clip, so the heartbeat would cut it off. The one-shot warnings are unchanged. One new
  report row, `Hunger ▸ Hunger Loop`, empty until a clip is authored. Commit `7cd9952`.
- **`WaveAudioManager` — remove it.** It turned out to be deader than the music
  controller: **absent from every scene and prefab**, so its eight slots were never shown
  to students at all, its ten playback methods had no callers, and even the debug-report
  block that reflected its profile out was itself never called. Deleted with its `.meta`,
  its entry in the handout reset tool, and that unreachable report block. The archived
  recovery scene is clean of its guid, unlike the music controller's. Commit `a442e5b`.
- **Checkpoint 4.2 (pickups) — answered by deleting the feature.** Shown where the four
  skulls and the chest actually were (and that their parent object was switched off, so
  none of them could ever be collected), the owner deleted them from the scene.
  `Tools/Audio/Remove Pickups Audio System` — which replaced the "Move Pickups Audio
  Manager" tidy written a few hours earlier — then took the manager out. Report 110 → 105.
  ✅ **Phase (b) done 2026-09-04**: the owner saved, the gate was met (zero hits for all five
  script guids in the saved scene), and the whole `Assets/Scripts/Pickups/` folder went,
  with `EndGameScreen.ShowVictory` and the reset tool's entry. The orphaned `Assets/Inbox/Prefabs/Chest.prefab`
  went too, on the owner's say-so: nothing referenced it, and it already carried two dead
  script guids of its own before `ParticleBurst` became a third. One leftover remains, and
  it is the same one `MusicController` left: the archived `_Recovery/0.unity` references
  four of the deleted classes. `AudioManagerTidyTools`' two removal menu items are now permanent
  no-ops — the classes they hunt for are gone — so that file is a candidate for deletion in
  its own right.

  Two owner decisions on it: **delete all five scripts** (`PickupBase`, `StandardPickup`,
  `ChestPickup`, `PickupsAudioManager`, `ParticleBurst` — nothing else in the project uses
  any of them), and **leave the game with no win condition**. `ChestPickup` was the only
  caller of `EndGameScreen.ShowVictory()`, so that method goes with it in the same commit:
  it cannot compile without its caller removed at the same time, and its copy ("You found
  the chest and made it out") describes an object that no longer exists. Restoring an
  ending later is three lines against the `Show` helper that `ShowDeath` already uses.
  Death is now the only ending.

Report after the second round: **0/110, placeholder 72, empty 38.**

### Noted for later, not acted on

- ✅ **Done 2026-09-09** (`AUDIO_INTEGRITY_TASKS.md`, Agent C): `playBelowThisHealth` had a latent divide-by-zero in the heartbeat's volume ramp when
  the slider is dragged to 0 (NaN volume, silent source, an error per frame). The hunger
  loop guards its own equivalent; the heartbeat was out of scope and still has it.
- ✅ **Done 2026-09-09** (same pass): if a loop's clip was cleared *while it was playing* (only possible by editing the
  inspector in play mode), the null guard means the loop is never told to stop. Both
  loops behave this way; the hunger loop mirrors the heartbeat rather than diverging.
- The in-game Ctrl+G popup was **not** screenshotted: that needs play mode, and the scene
  edits were unsaved. The editor window (`Tools ▸ Audio ▸ Student Progress Report`) was
  opened and rendered clean. Worth an eyeball after the save.
