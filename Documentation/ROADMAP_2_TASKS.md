# Roadmap 2 Tasks — Implementation Work Order

> Written 2026-08-18 at commit `13ce2e2`. **This document is a work order for an
> implementing AI.** It covers five of the ten items projected in conversation with the
> owner (picks 1, 4, 7, 8, 9 of that list — the student guide, grading upgrades, music
> authoring aids, mixer craft and creature expansion are not in scope here). Every
> fileID/guid/line number below was verified against the repo at `13ce2e2` by parallel
> research passes; **line numbers drift — re-grep before every YAML edit.**
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md), then the ground rules in
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md), then
> [ROADMAP_TASKS.md](ROADMAP_TASKS.md)'s STATUS block (what already shipped and why).
> Items marked ⏸ are **owner checkpoints** — stop and hand back; do not fake them.

## Global rules of engagement

1. `bash Documentation/tools/compile-check.sh` after every edit; both assemblies must
   pass. One task = one commit (Task 3 may be two), repo message style.
2. **Scene/prefab YAML edits**: line-targeted Python only — assert the exact current
   content of every touched line before replacing/inserting, then verify with
   `git diff` that only the intended lines changed. Never regenerate a block. Unity
   matches YAML keys by name, not order, but insert new keys in C# declaration order
   anyway (it is what a Unity re-save would produce).
3. ⚠ **Line endings**: several `.cs` files are CRLF (`PauseMenu.cs`,
   `SecretRockSwitch.cs`, `PickupBase.cs`, `PickupsAudioManager.cs` are known; there
   may be others). Python text-mode writes silently convert them to LF and bloat the
   diff. After editing any file, run `file <path>` and compare against
   `git show HEAD:<path> | file -`; restore CRLF byte-wise if it changed.
4. Before renaming/removing any serialized field or public method: grep
   `Assets/Scripts/Editor`, `Assets/Scripts/Testing`, and all `.unity`/`.prefab` files
   (string-bound `FindProperty`/reflection/UnityEvent `m_MethodName` break silently).
5. Compatibility contracts stand: `MusicManager.musicTracks` (reflection),
   `SetFightingState`/`SetIndoorState`/`ChangeState` (public), and the do-not-delete
   list (`Core.Pooling.ObjectPool`, `DayNightCycle.SmoothTransition`,
   `Audio/MusicController`). ⚠ **`Audio/MusicController` left that list on 2026-09-03**
   (unwired legacy; removed by AUDIO_MANAGER_TIDY_TASKS.md Task 4.1) — the other two
   stand.
6. This is a sound-design assessment project: **strings stay as the student-facing
   authoring surface**. Ids supplement them; never delete a name string field.

Useful identifiers (verified at `13ce2e2`):

| Thing | Value |
|---|---|
| Placeholder clip | `Assets/Audio/Beep to replace.wav`, guid `680c856c8571b954ab0f5665caf936bf`; array-element form `- {fileID: 8300000, guid: 680c856c8571b954ab0f5665caf936bf, type: 3}` |
| "Audio Managers" GameObject (scene) | fileID `2049660006` |
| PlayerAudioManager component | `&2049660020`, script guid `dedb17be975bd4659a4b9366a8a776fb`, block starts scene line ~172627 |
| WeaponAudioManager component | `&2049660012`; `m_WeaponProfiles:` at ~172118, last profile key (`weaponType: 1`) at ~172169, then `m_WeaponAudioSource:` |
| FootstepAudioManager component | `&2049660011`; `surfaceProfiles:` at ~172045, water profile ends `baseVolume: 1` at ~172094, then `movementProfile:` |
| EnemyAudioManager component | `&2049660010`; five profiles at ~171930–172030 |
| InteractionAudioManager component | `&2049660018`; six profiles at ~172477–172581 |
| AudioMixer asset | guid `855e36c5d423f44d6a9571cbdf1b2686`; exposed params: `AmbienceVolume, EnemiesVolume, FootstepsVolume, ImpactsVolume, MusicVolume, SFXVolume, UIVolume, WeaponsVolume` (no `MasterVolume`, no `volume`) |
| Free hotkeys | Ctrl+J is unclaimed (used below). Taken: Ctrl+G/K/M/N/O/P, Alt+S/X/1–4, Shift+I/D, F8–F12, Escape, E, C, P, 0–5, `\` |

---

## Task 1 — Finish the player-audio authoring surface (closes Plan-3 C5, C6, C7)

Everything here is in `Assets/Scripts/Audio/PlayerAudioManager.cs`,
`Assets/Scripts/Player/PlayerStats.cs`, `Assets/Scripts/Inventory/FoodItem.cs`, plus
scene YAML. Facts you can rely on: PlayerStats already holds a wired serialized
`playerAudioManager` reference (scene `playerAudioManager: {fileID: 2049660020}`);
the scene's PlayerAudioManager has **only Health and Hunger profiles — no Stamina
profile exists**, so the two stamina methods currently no-op even if called.

### 1.1 `lowHealthVolume` becomes real (C5)

In `PlayLowHealthLoop` (PlayerAudioManager.cs ~line 180) the computed heartbeat volume
is applied raw: `sourceToUse.volume = volumeScale;`. Multiply by the profile's
authored ceiling: `sourceToUse.volume = volumeScale * profile.lowHealthVolume;`.
Scene values are Health `lowHealthVolume: 1` (no audible change today) — this makes
the inspector slider a real mix control for students.

### 1.2 Heartbeat ticks at 0.1s, not per-frame (C7)

Same coroutine: replace the `yield return null;` at the loop's end (~line 182) with
`yield return new WaitForSeconds(0.1f);`. The loop's exit conditions re-evaluate each
tick; nothing else changes. (Keep the loop's own volume writes — they are what 1.1
scales.)

### 1.3 Stamina edges (C6, half 1)

In `PlayerStats`:

- Add two private bools, e.g. `staminaDepletedNotified` and a computed resume
  threshold `maxStamina * SprintResumeFraction` (the existing const, 0.15f).
- **Depleted**: fires when `currentStamina` reaches 0 from above. Mutation sites:
  `HandleStamina` (~line 253, the clamp), `UseStaminaForDoubleJump` (~561),
  `UseStaminaForSlide` (~582). Route all three through one private
  `AfterStaminaChanged(float previous)` that compares previous vs current: on
  `previous > 0 && currentStamina <= 0` → `playerAudioManager?.PlayStaminaDepletedSound();
  staminaDepletedNotified = true;`.
- **Regenerating**: in the same helper, on upward crossing of the resume threshold
  (`previous < threshold && currentStamina >= threshold`) **only when
  `staminaDepletedNotified` is set** → `PlayStaminaRegeneratingSound();
  staminaDepletedNotified = false;`. Gating on the flag stops the regen sound firing
  every time stamina dips below 15 and recovers without ever hitting empty.
- `HandleStamina`'s early return at full stamina (~line 250) never reaches the clamp —
  that is fine; a threshold crossing can only happen while below max.

### 1.4 Hunger warning (C6, half 2)

There is **no hunger threshold anywhere in the project** — invent the minimal one:

- New serialized fields on `PlayerStats` (with tooltips):
  `[Range(0f,1f)] hungerWarningFraction = 0.25f` and
  `hungerWarningRepeatSeconds = 30f`.
- In `HandleHunger` (~lines 223-234): when `currentHunger` crosses **below**
  `maxHunger * hungerWarningFraction`, call `playerAudioManager?.PlayHungerSound()`
  and stamp the time; while it stays below, repeat every
  `hungerWarningRepeatSeconds`. Re-arm (clear the stamp) when hunger rises back above
  the threshold — the only riser is `AddHunger` (~541), so the re-arm check lives
  there or in `HandleHunger` on the first tick above threshold.
- Use `Time.time` for the repeat stamp and **seed it the same way the Task-7 stamps
  are seeded in `Start`** — `Time.time` survives scene reloads (see the restart-
  starvation fix in ROADMAP_TASKS' audit record; do not repeat that bug).

### 1.5 Eating fallback (C6, half 3) — do NOT double existing sounds

`UIAudioManager` already owns eating audio: `Fish.UseItem` → `PlayFishEatenSound`,
`MedicineItem` → `PlayMedicineUsedSound`, and `FoodItem.UseItem` →
`PlayFoodCanEatenSound` — but the FoodItem call is **gated on
`itemName == "Food Can"`** (FoodItem.cs ~19-22); any other FoodItem is silent today.
Wire `PlayEatingSound` as the fallback for exactly that silent path:

- `PlayerStats` gains `public PlayerAudioManager AudioManager => playerAudioManager;`.
- In `FoodItem.UseItem`, the non-"Food Can" branch calls
  `PlayerStats.Instance?.AudioManager?.PlayEatingSound();`.
- Do **not** call `PlayEatingSound` from Fish, MedicineItem, or the "Food Can" branch
  — each already has a bespoke UIAudioManager sound and would double (verified: the
  two paths use different AudioSources, so both would be audible).

### 1.6 Scene YAML — three profile insertions

All three seeded with the beep placeholder so they surface in the student progress
report as not-yet-done work. Re-grep every anchor first.

**(a) Stamina profile** — append a third entry to PlayerAudioManager's
`soundProfiles:` (component `&2049660020`), after the Hunger profile's last key
(`eatingVolume: 1`) and before the `  playerStats: {fileID: 1918631700}` line
(~172677). Keys in C# declaration order:

```yaml
  - profileName: Stamina
    soundType: 1
    customAudioSource: {fileID: 0}
    mixerGroupOutput: {fileID: 0}
    damageSounds: []
    lowHealthLoop: {fileID: 0}
    heartbeatAudioSource: {fileID: 0}
    playBelowThisHealth: 25
    damageVolume: 1
    lowHealthVolume: 0.7
    staminaDepletedSound: {fileID: 8300000, guid: 680c856c8571b954ab0f5665caf936bf, type: 3}
    staminaRegeneratingSound: {fileID: 8300000, guid: 680c856c8571b954ab0f5665caf936bf, type: 3}
    staminaVolume: 0.8
    hungerSounds: []
    eatingSound: []
    hungerVolume: 0.8
    eatingVolume: 1
```

**(b) Crossbow weapon profile** — first add `Crossbow` to `WeaponId` in
`Assets/Scripts/Audio Systems/AudioProfileIds.cs` (append after `Axe` — **never
reorder or renumber existing members**; the enum is `Unassigned=0, SMG, Revolver,
Axe`, so Crossbow becomes 4). Then insert a fourth entry into `m_WeaponProfiles`
after the Axe profile's `    weaponType: 1` (~172169), before `  m_WeaponAudioSource:`.
Use the same 15 keys as the existing entries (weaponName, shootSounds, shootVolume,
minPitchVariation 0.95, maxPitchVariation 1.05, raiseWeaponSound, lowerWeaponSound,
toggleSoundVolume 1, togglePitchVariation 0, reloadStartSound, reloadEndSound,
reloadActionSounds, reloadVolume, weaponType) with: `weaponName: Crossbow`,
`shootSounds:` one beep element, `shootVolume: 1`, `raiseWeaponSound`/
`lowerWeaponSound: {fileID: 8300000, guid: 853820e2b0f624005bcc0116cbce99eb, type: 3}`
(the toggle clip the other three share), **`reloadStartSound:` the beep** (this is the
crossbow's draw sound — `CrossbowController.Fire` calls `PlayReloadStartSound`),
`reloadEndSound: {fileID: 0}`, `reloadActionSounds: []`, `reloadVolume: 1`,
`weaponType: 0`. This un-silences the crossbow (it has never made a sound) and clears
the standing validator finding.

**(c) `default` footstep profile** — insert after the water profile's
`    baseVolume: 1` (~172094), before `  movementProfile:`:

```yaml
  - surfaceType: default
    footstepSounds:
    - {fileID: 8300000, guid: 680c856c8571b954ab0f5665caf936bf, type: 3}
    baseVolume: 1
```

**Verify:** compile clean; `git diff` shows only the intended lines; the wiring
validator's two standing content findings (Crossbow, default) should no longer fire —
confirm by reading `AudioWiringValidator.Validate()`'s checks against the new data,
not by editing the validator.

---

## Task 2 — Real-time audio event monitor (prediction #4)

Two new files under `Assets/Scripts/Assessment/` (namespace-less, like their
siblings), plus small `Record` hooks. Purpose: students see *which* audio events fire
and *what* answered them, live.

### 2.1 `AudioEventLog.cs` — static ring buffer

- `public static class AudioEventLog` with a fixed-capacity ring (128 entries):
  `struct Entry { float time; string source; string detail; EventKind kind; }`,
  `enum EventKind { Play, Miss, State }`.
- `public static void Record(EventKind kind, string source, string detail)` —
  timestamps with `Time.unscaledTime`, writes into the ring. Recording is
  unconditional (cheap) so events that happened *before* the overlay is opened are
  visible. Expose an enumeration of entries newest-first for the overlay.
- Clear the ring on scene load the same way `AudioProfileKeys` clears its warn-once
  memory (`RuntimeInitializeOnLoadMethod(SubsystemRegistration)` + a
  `SceneManager.sceneLoaded` handler — copy that pattern, it exists for a reason:
  the load-method alone fires once per play session, not per in-game restart).

### 2.2 Hooks (the MUST list — keep them one line each)

1. `AudioProfileKeys.TryGetProfile` — on hit: `Record(Play, managerName, normalized)`;
   on miss: `Record(Miss, managerName, normalized)`. **This single hook covers every
   keyed lookup in all four profile managers, including misses the warn-once
   suppression no longer logs.**
2. `AudioUtil.PlayRandomOneShot` — after a clip is chosen and played:
   `Record(Play, source.gameObject.name, clip.name)`. Covers the actual playback for
   enemy/weapon/footstep/player sounds.
3. `UIAudioManager.PlaySound` (the private helper ~159) — when it actually plays:
   `Record(Play, "UIAudioManager", clip.name)`.
4. `MusicManager.ChangeState` (public, a compatibility-contract method — **add the
   Record line inside the body; do not change the signature**):
   `Record(State, "Music", <old> -> <new>)`.

Nice-to-have (only if trivial): `InteractionAudioManager`'s public play methods and
`BulletImpactManager`'s delayed impact play. Do not instrument `LayeredStatePlayer`'s
per-frame gate updates — that would flood the ring.

### 2.3 `AudioEventMonitor.cs` — the overlay

- Copy `AudioProgressReportUI`'s self-install pattern exactly
  (`RuntimeInitializeOnLoadMethod(AfterSceneLoad)`, `FindFirstObjectByType`
  idempotence guard, hidden host GameObject, `DontDestroyOnLoad`,
  `[DisallowMultipleComponent]`). **Not dev-gated** — students use builds, same as
  the progress report.
- Toggle: **Ctrl+J** (verified unclaimed). Same chord-detection code as Ctrl+G.
- OnGUI: right-hand column (the progress report owns the centre), ~420px wide,
  last ~16 entries newest-first: age in seconds, source, detail. Colour by kind:
  Play = white, **Miss = the report's red `#ff6b6b`** (a miss is the thing students
  most need to see), State = cyan. Reuse the report's translucent-box style.
- A one-line footer: "Ctrl+J to close · misses in red are lookups that found no
  profile".

**Verify:** compile clean. Grep that `ChangeState`'s signature is untouched. The
monitor must not throw when the ring is empty.

---

## Task 3 — Title screen, resume, persisted volumes (prediction #7)

Research facts to rely on: the build has exactly **one** scene (index 0). The pause
menu (`PauseMenu.pauseMenuUI` → the `' UI Canvas'` GameObject — note the name's
leading space) is **already a full settings panel** with 8 authored sliders bound via
8 `SFXVolumeControl` instances to the mixer's 8 exposed parameters. What is missing:
nothing persists (**zero `PlayerPrefs` in the whole project**), there is **no
Resume button** (Escape is the only way out), and there is no title screen.

### 3.1 `TitleScreen.cs` — code-driven, same-scene

New `Assets/Scripts/UI/TitleScreen.cs`, IMGUI in `EndGameScreen`'s style (reuse its
panel/dim/style constants as a starting point):

- Self-install via `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` **guarded by a
  static `hasShownThisSession` flag** so it appears on application launch only — not
  after an in-game Restart (statics survive scene reloads; here that is the feature).
- `public static bool IsShowing { get; private set; }` — the same gate pattern
  `EndGameScreen` uses.
- On show: `Time.timeScale = 0f`, cursor free/visible, disable the
  `FirstPersonController` (exactly as `EndGameScreen.Show` does — its Update ignores
  timeScale and re-locks the cursor every frame; this is a known trap, see the audit
  record). Ambience/music keep playing behind the title — desirable.
- Content: game title, one line of controls help (WASD / Shift sprint / C slide /
  E interact / Escape pause / Ctrl+G progress report / Ctrl+J audio monitor), a
  **Play** button and a **Quit** button (copy `EndGameScreen.Quit`'s
  editor/standalone split). Play: re-enable the controller, `timeScale = 1`, lock
  cursor, clear `IsShowing`, destroy the host. Optionally
  `UIAudioManager.Instance?.PlayMenuToggle(false)` on Play.
- `OnDestroy` clears `IsShowing` (same leak the audit fixed on EndGameScreen).
- **PauseMenu.Update must also early-return on `TitleScreen.IsShowing`** — add it
  beside the existing `EndGameScreen.IsShowing` guard, or Escape resumes the world
  under the title. ⚠ `PauseMenu.cs` is CRLF.

### 3.2 Resume affordance on the pause menu

Do not build new uGUI objects in YAML (a Button hierarchy is ~5 objects — too much
hand-authored YAML risk). Instead: `PauseMenu` gains an `OnGUI` that, while
`isPaused`, draws a single **Resume** button in `EndGameScreen`'s button style
(140×38, centred near the bottom, `GUI.depth` above the canvas) that calls the
existing public `ResumeGame()`. Escape still works; this just makes the menu
self-explanatory. ⏸ If the owner prefers an authored uGUI button instead, that is an
editor job — note it and move on.

### 3.3 Volume persistence — inside `SFXVolumeControl`

All 8 sliders run through `SFXVolumeControl` (per-instance `sfxVolumeParameter`
string), so persistence belongs there, not in a new manager:

- In `SetSFXVolume(float sliderValue)`: after the mixer write,
  `PlayerPrefs.SetFloat("AudioVolume_" + sfxVolumeParameter, sliderValue);`.
- In `Start()`: if `PlayerPrefs.HasKey(...)`, read it, apply via the existing
  dB-conversion path, and set the slider with `SetValueWithoutNotify` (the scene
  serializes a doubled binding — inspector event + `AddListener` — so a notifying
  set would write the mixer twice; harmless but sloppy). Else keep the current
  behaviour (read the mixer). **Apply in `Start`, not `Awake`** — mixer writes from
  `Awake` are clobbered by snapshot initialisation (known Unity behaviour).
- `PlayerPrefs.Save()` in `OnDisable` (one per control is fine; `Save` is idempotent).

### 3.4 Dead menu-code sweep (verify-then-delete, rule 4 applies)

Re-verify zero callers (C#, YAML `m_MethodName`, Editor/Testing greps) then remove:

- `Assets/Scripts/UI/SettingsMenu.cs` — dead (0 scene refs) and wrong: sets a
  non-exposed lowercase `"volume"` parameter with no dB conversion.
- `Assets/Scripts/UI/MainMenu.cs` — dead (0 scene refs); its `PlayGame()` does
  `LoadScene(buildIndex + 1)`, which **throws** in this one-scene build.
- `Assets/Scripts/UI/EndGame.cs` — misleading orphan: `MainMenu()` actually reloads
  the current scene; zero callers. It IS attached in the scene (component fileID
  `1387532401` on the `EventSystem` GameObject, block near scene line ~108381) — remove
  the component block AND its entry in the GameObject's `m_Component` list, following
  the established component-removal YAML protocol (see ROADMAP_TASKS Task 1 for the
  pattern). Delete the file + its `.meta`.
- `Assets/Scripts/Audio/AudioMixerController.cs` — grep for callers first. If (as
  research indicates) nothing calls any of its methods: its `SetMasterVolume` targets
  a parameter (`MasterVolume`) that is **not exposed on the mixer** and fails
  silently. If zero callers, delete file + scene component (fileID `175922378` on
  `Game Management / Audio Mixer Control`); if anything calls it, fix the master
  method to log a warning instead and record the finding.

**Verify:** compile clean; launch flow reads: title → Play → game; Escape → pause
(sliders + Resume + QUIT) → Resume; die/win → end screen → Restart (no title). ⏸
Owner: confirm the title's wording/keys line, and that slider positions survive a
full quit-and-relaunch.

---

## Task 4 — Caller-side profile ids (prediction #8; finishes Roadmap Task 8)

The four id enums exist, the alias machinery is live and audited, the migration tool
exists — but **no id is serialized anywhere yet** (the scene was last saved before
the fields existed; verified: zero `enemyId|weaponId|interactionId|surfaceId` keys in
the scene, zero in prefabs). The owner has not run the migration tool, and this task
supersedes that checkpoint: writing the ids by YAML is the same operation, done
deterministically.

**Do Task 1 first** — it adds `WeaponId.Crossbow` and the Crossbow profile this task
assigns an id to.

### 4.1 Stage 0 — write the ids into the scene (YAML)

Insert the id key **immediately after each profile's name line** (matches C#
declaration order). Canonical mapping (from `AudioProfileIds.Canonical` — letters and
digits, lowercased):

- EnemyAudioManager profiles (~171930–172030): `Butcher`→`enemyId: 1`,
  `Yokai`→2, `Demon`→3, `Choul`→4, `Rock Golem`→5.
- WeaponAudioManager profiles (~172119–): `SMG`→`weaponId: 1`, `Revolver`→2,
  `Axe`→3, `Crossbow` (added by Task 1) →4.
- InteractionAudioManager profiles (~172477–172581): `Firestick`→`interactionId: 1`,
  `Campfire`→2, `Cave Door`→3, `Electrical Box`→4, `Light Switch`→5,
  `Mixing Desk Music`→6.
- FootstepAudioManager profiles (~172046–): `wood`→`surfaceId: 1`, `grass`→2,
  `sand`→3, `rock`→4, `snow`→5, `swamp`→6, `water`→7, `default` (added by Task 1)→8.

Indentation: profile keys are 4 spaces under the `- ` entry (copy the neighbouring
lines exactly).

### 4.2 Stage 1 — ZombieAI

- Add `[SerializeField] private EnemyId enemyAudioProfileId = EnemyId.Unassigned;`
  directly under the existing `enemyAudioProfileName` declaration (~ZombieAI.cs:87),
  tooltip: "Compile-checked identity; when set, this wins over the string above."
- Add a private resolver:
  `private string AudioProfileKey => AudioProfileIds.IsAssigned(enemyAudioProfileId)
  ? AudioProfileIds.NameOf(enemyAudioProfileId) : enemyAudioProfileName;`
- Replace `enemyAudioProfileName` with `AudioProfileKey` at the four call sites
  (~163 StartVocalizationLoop, ~263 GetAttackSoundDelay, ~266 PlayAttackSound,
  ~443 PlayDeathSound). **The string field stays.**
- Prefab YAML — `enemyAudioProfileName` is the **last key** of the ZombieAI block in
  all five wave prefabs (next line is a `--- !u!82` document marker). Insert
  `enemyAudioProfileId: <n>` directly after it:
  `Butcher.prefab` ~2941 →1 · `Choul.prefab` ~2287 →4 · `Demon.prefab` ~1254 →3 ·
  `Yokai.prefab` ~1017 →2 · `Boss - Rock Golem.prefab` ~3303 →5.
  (Careful: Choul's `npcName` is "Ghoul" — the audio profile name/id is Choul.)
  Leave the six non-wave enemy prefabs alone (they reference profiles that do not
  exist; that is future content, not this task).

### 4.3 Stage 2 — weapons (audio key only; identity logic untouched)

- `WeaponController` gains
  `[SerializeField] protected WeaponId weaponId = WeaponId.Unassigned;` under
  `weaponName` (~:46), plus
  `protected string WeaponAudioKey => AudioProfileIds.IsAssigned(weaponId)
  ? AudioProfileIds.NameOf(weaponId) : weaponName;`
- Swap `weaponName` → `WeaponAudioKey` in the **audio calls only**: WeaponController
  ~217/299/306, CrossbowController ~54/65, MeleeWeaponController ~95.
- **Do NOT touch** the `isSMG`/`isRevolver` `Contains` detection (~141-143) or
  `WeaponItem.weaponName` — those are behaviour/identity, not audio keys, and are
  covered by rule 4's grep discipline.
- Scene YAML — insert `weaponId: <n>` after the `weaponName:` line on the four scene
  weapon components: SMG (~252167) →1, Revolver (~188806) →2, Axe (AxeController,
  ~167748) →3, Crossbow (CrossbowController, ~250430) →4. (The three empty
  `weaponName:` lines at ~18349/31201/81005 are `WeaponItem`s — different class, do
  not touch.)

### 4.4 Stage 3 — interactions

- `SecretRockSwitch`, `FirestickInteraction`, `CampfireInteraction`: each gains
  `[SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;`
  under its `audioProfileName`, plus the same resolver pattern; use it everywhere the
  string was passed to `InteractionAudioManager`. ⚠ `SecretRockSwitch.cs` is CRLF.
- `InteractionSystemTMP` overwrites its string at runtime with constants — replace
  those magic strings with compile-checked ids:
  `audioProfileName = AudioProfileIds.NameOf(InteractionId.ElectricalBox);` (~:62)
  and `...NameOf(InteractionId.MixingDeskMusic);` (both sites, ~:81 and ~:103).
- Scene YAML — insert `audioProfileId: <n>` after each `audioProfileName:` line:
  five FirestickInteraction instances (~62597, 69761, 111773, 118008, 172887) →1;
  CampfireInteraction (~48067) →2; SecretRockSwitch (~236670) →3. Note the
  SecretRockSwitch block does NOT end at that key (`inputKey` etc. follow) — assert
  surrounding content.
- **Footsteps are deliberately not converted**: the surface pipeline is inherently
  string-typed (`SurfaceDetector` returns tag/terrain-derived strings). The scene
  `surfaceId`s from Stage 0 still buy typo protection via the alias registration.
  Record this rationale in the STATUS block.

### 4.5 Validator extension

Add a check to `AudioWiringValidator`: for every profile where **both** a name and an
id are set, warn when `AudioProfileIds.Canonical(name)` differs from
`Canonical(NameOf(id))` — a drifted pair means the string and the id now answer to
different profiles. Also mark the Task 8B owner checkpoint superseded in
ROADMAP_TASKS' STATUS (the migration tool remains for future re-runs).

**Verify:** compile clean; diff shows only inserted id lines + the code changes. ⏸
Owner: one playthrough with the console open — every sound domain (enemy, weapon,
interaction, footsteps) must behave identically; the ids only change which *key*
reaches the same lookup.

---

## Task 5 — Known-issues cleanup (prediction #9; closes Plan-3 A5, D-items, W7)

Re-verified 2026-08-18: **D2, D5, D8 are already fixed, as are the DayNightCycle
material/smoothing issues and the occlusion debug rays** (despite older notes —
IMPROVEMENT_PLAN.md's update block is correct). What follows is everything genuinely
open. For each D-item, read its row in IMPROVEMENT_PLAN_3.md §D first — the fixes
below name the verified current anchors; the plan rows carry the fuller rationale.

1. **A5 — FOV kick silently does nothing.** Scene authors `m_UseFovKick: 1` but
   `IncreaseCurve` has `m_Curve: []` (~scene 251231-251233), and an empty curve
   evaluates to 0. Code fallback in the vendored
   `Assets/Inbox/Standard Assets/Utility/FOVKick.cs`: in `Setup`/`CheckStatus`
   (~:18-38), extend the null guard to
   `if (IncreaseCurve == null || IncreaseCurve.length == 0) IncreaseCurve =
   AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);` (replace the throw for the empty case;
   keep throwing on null camera). Comment why (empty curve = silent no-op for a full
   second of coroutine).
2. **D1 — cursor-lock authority.** `MouseLook.InternalLockUpdate`
   (`Assets/Inbox/Standard Assets/.../MouseLook.cs` ~:75-95) reacts to Escape and
   left-click on its own, fighting PauseMenu (which owns Escape) and the end/title
   screens. Remove the Escape/Mouse0 reactions from `InternalLockUpdate`; keep
   `SetCursorLock` and the state-application so `FirstPersonController`'s existing
   per-frame `SetCursorLock(!paused)` drive (~FPC:170/175) remains the single
   in-game authority, with PauseMenu/EndGameScreen/TitleScreen owning their overlays.
3. **D3 — FishingZone prompt/multiplier gating** (~FishingZone.cs:43-48, 90,
   101-123): follow the plan row — refresh the prompt on rod *state* changes, not
   just reference changes; apply `SetFishingSpotMultiplier` when the rod is acquired
   while already inside the zone; per the plan's fix line.
4. **D4 — FishingRod lower-while-transitioning** (~FishingRod.cs:70-79): the
   `if (isTransitioning) return;` runs **before** `StopFishing()`, so lowering
   mid-transition abandons an active fishing session; `FishingRodItem.ForceToLowered`
   (~:107-111) then desyncs `isRaised`. Reorder so `StopFishing()` runs regardless,
   per the plan row.
5. **D6 — unreachable Rock branch** (~BulletImpactManager.cs:148-150, 168): the
   caller proves the tag is `Enemy` before `PlayEnemyEffect`, so its inner
   `CompareTag("Rock")` never passes; delete the branch and the now-dead `rockEffect`
   field (~:76) — ⚠ serialized: record the scene value in the commit message; the
   orphaned YAML key drops on next scene save.
6. **D7 — one IDamageable resolver.** Three divergent lookups:
   `WeaponController.cs:233` (`hit.transform`), `AxeController.cs:104`
   (`hit.collider`), `Arrow.cs:83` (`collision.gameObject`). Add one static helper
   (suggested: `IDamageableUtil.From(Component hitContext)` in
   `Assets/Scripts/Weapons/`, checking `GetComponent` then
   `GetComponentInParent<IDamageable>()`) and use it at all three sites. Behaviour
   note: the parent fallback is a deliberate small improvement — a child collider on
   a damageable root now takes damage from all three weapons consistently.
7. **D9 + W7 — FirestickInteraction**: `playerInRange` is conflated with
   `promptUI != null` (~:79, :88 — a missing prompt makes the firestick
   *uninteractable*, not just promptless); decouple per the plan row. Fold in W7:
   it hardcodes `KeyCode.E` while displaying a separate `inputKey` string — derive
   the display from the serialized `interactKey`.
8. **D10 — InteractionSystemTMP** resolves `actionHandler` in `Start` (~:39-43) while
   `OnEnable` (~:232-236) subscribes only if it is already set — first-enable never
   subscribes. Move resolution to `Awake` per the plan row.
9. **D11 — NavMesh guards**: `WaveManager` spawn (~:548) without
   `NavMesh.SamplePosition`; `ZombieAI.SetDestination` (~:208) without an
   `agent.isOnNavMesh` check (the only existing check is on the death path ~:428).
   Add both guards per the plan row; a failed sample skips the spawn with one warning.
10. **D12 — SecretRockSwitch prompt coroutine** (~:110): the handle from
    `StartCoroutine(HidePromptAfterDelay(2f))` is discarded; track and stop-then-start
    it exactly like the class's own `rotationCoroutine`/`doorCoroutine` pattern
    (~:146-148, :168-172). ⚠ CRLF file.
11. **NEW — DirectionalAudioSource allocates 2-3 interpolated strings per source per
    frame** (~:164-165, :209, :221-222 in
    `Assets/Scripts/Audio Systems/DirectionalAudioSource.cs`) for display fields whose
    only consumer is the custom editor — and the editor (~DirectionalAudioSourceEditor.cs:32-36)
    already re-formats two of them from numeric accessors. Replace the string fields
    with numeric ones (add a numeric angle accessor), format in the editor only.
    Rule 4 applies: the editor binds these by name.
12. **Stale message**: `FirstPersonController.cs` ~:162 warns about
    "PlayerMovementAudioManager", a class that does not exist (the field is a
    `FootstepAudioManager`). Correct the message text.

**Verify:** compile clean after each numbered item (they are independent — commit as
one task, but keep the diff reviewable). ⏸ Final owner playthrough: fishing feel
(D3/D4), a firestick with no promptUI still lights, zombies spawn on the NavMesh,
sprint now visibly kicks the FOV.

---

## Suggested commit sequence

`Task 1` → `Task 2` → `Task 3` (may split: title+resume / persistence+dead-code) →
`Task 4` → `Task 5`. Task 4 hard-depends on Task 1 (WeaponId.Crossbow + profile).
Owner checkpoints fall after 3 (launch-flow approval), 4 (sound-domain playthrough)
and 5 (final pass). Record deviations in this file's own STATUS block when done, in
the ROADMAP_TASKS.md style.

---

## STATUS — implementation record

> Updated 2026-08-18. All five tasks implemented and committed; each was compile-verified
> on **both** assemblies with `Documentation/tools/compile-check.sh` before its commit,
> and every `.cs` file was checked for line-ending drift against `HEAD`. **Nothing has
> been run inside Unity** — the ⏸ checkpoints below are open.

| Task | Commit | Notes |
|---|---|---|
| 1 — player-audio authoring surface | `45a3535` | as written |
| 2 — audio event monitor (Ctrl+J) | `5dac677` | as written |
| 3a — title screen, Resume, persistence | `b88e05d` | as written (Resume drawn in IMGUI, the work order's stated default) |
| 3b — dead menu-code sweep | `9ffbc28` | **deviates on one of four — see below** |
| 4 — caller-side profile ids | `33b5df3` | as written; supersedes the Task 8B checkpoint in ROADMAP_TASKS |
| 5 — known-issues cleanup | `646bbff` | two items were already fixed — see below |

### Deviation: AudioMixerController was kept, not deleted

Task 3.4 listed it for deletion "if nothing calls it". Nothing calls it — but
`StatsHelper` declares a `[SerializeField] private AudioMixerController` field, so
deleting the class would have meant editing a Testing script and a second scene object
for no functional gain. The work order's own fallback was taken instead: its real defect
is fixed, so `SetVolume` now checks `AudioMixer.SetFloat`'s return value and warns.
That matters because the method fails **silently** for a parameter the mixer does not
expose, and `"MasterVolume"` is not exposed here (only the eight bus volumes are).

The `StatsHelper` field is itself dead (it reports CS0169 and the scene authors it as
`{fileID: 0}`). A future sweep can remove class, field and scene component together.

The other three went as written: `SettingsMenu` and `MainMenu` were pure file deletions
(zero references; `MainMenu.PlayGame()` would have thrown in this one-scene build), and
`EndGame` had its scene component and `m_Component` entry removed alongside the file.

### Task 5: two items needed no work

- **W7** (FirestickInteraction's hardcoded `KeyCode.E` versus its display string) was
  already fixed in an earlier round — `interactKey` is parsed from `inputKey`.
- **D6** needed no taxonomy decision. The plan row warns that `Boss - Rock Golem` is
  tagged `Rock` and so "never gets enemy impact sounds at all", but `WaveManager` assigns
  `zombie.tag = "Enemy"` to everything it spawns, so a wave-spawned golem does get them.
  Only the unreachable branch and its redundant `rockEffect` field were removed.

### Things found while implementing, worth knowing

- **The Stamina sound profile did not exist.** `PlayerAudioManager` had only Health and
  Hunger profiles, so `PlayStaminaDepletedSound`/`PlayStaminaRegeneratingSound` would
  have no-opped even once called. Task 1 adds it.
- **`FirestickInteraction`'s serialized default was `"Firesticks"`** (plural) against a
  profile named `"Firestick"`. Every scene instance overrides it, so it never bit — but a
  new firestick dropped in with the default would have been silent. Its assigned
  `InteractionId` now resolves to the correct spelling regardless.
- **Nothing in the project used `PlayerPrefs`** before Task 3; volume changes were lost
  on every quit.
- Every authored profile name and its new id canonicalise identically, so Task 4's new
  drift check reports nothing on the current scene — it is there for future edits.

### Still open — owner checkpoints

- ⏸ **Launch flow**: confirm the title screen's wording and the controls line, and that
  slider positions survive a full quit-and-relaunch (this is the first PlayerPrefs use,
  so it is worth one explicit check).
- ⏸ **Sound domains after the id conversion**: one console-open playthrough hitting
  enemies, each weapon, a firestick, the campfire, the cave door, the mixing desk and a
  few surfaces. The ids only change which *key* reaches the same lookup, so anything that
  changes is a bug — Ctrl+J now shows exactly which key each lookup used.
- ⏸ **Feel of the Task 5 fixes**: fishing raise/lower and overlapping zones (D3/D4),
  a firestick lights with no prompt UI in the scene (D9), zombies spawn on the NavMesh
  (D11), and sprinting now visibly kicks the FOV (A5) — that last one has been inert
  since the scene was authored, so it will look new.
- ⏸ Unchanged from before: Task 5 of ROADMAP_TASKS (run `Tools/Music/Generate Test
  Stems`, assign the `MusicStateDefinition` assets) and its Task 10 prefab/authoring
  decisions.
- The orphaned `rockEffect:` key stays in the scene YAML until the next editor save,
  along with the other swept keys. Harmless — Unity drops unknown keys on load.
