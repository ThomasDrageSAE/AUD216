# Audio Integrity Pass — Work Order (parallel agents)

> Written 2026-09-08 at commit `0dc67c4`. **This is a work order for an implementing AI
> (Opus) running six agents in parallel under one integrator.** It is not a feature. It is
> the pass that makes the existing work true: the report lists what the code plays, every
> slot a student fills is one the game can play, and no audio state outlives the state that
> owns it. It came out of four read-only audits (audio state, the student authoring surface,
> runtime robustness, the documented backlog) run against `0dc67c4`; every line number below
> was read from the code, the scene and the prefabs at that commit, and every one of them
> should be **re-read before it is edited** — the last order's own claims were wrong five
> times and the agents caught all five by reading rather than trusting.
>
> The owner's four decisions (multichoice, 2026-09-08): **the full pass, all lanes**; under
> the pause menu and the death screen **audio keeps running by design — only the fades that
> get stuck are fixed** (so nothing here pauses the listener, stops loops on pause, or adds a
> stop-everything path — a fade frozen at partial volume is a bug, a fire that keeps crackling
> under the menu is not); **each music state ships with one empty loop stem** so the three
> states are counted (the end state is therefore **244**); and the enemy radar stays a
> separate ticket, not a seventh lane.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (Assessment, Audio, Enemies, Weapons and
> Editor rows), the ground rules in [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md), and the
> STATUS block of [ENEMY_ROLES_AND_FEEDBACK_TASKS.md](ENEMY_ROLES_AND_FEEDBACK_TASKS.md), whose
> agent shape this copies: **only the integrator drives the editor**, scene edits are menu
> items and Undo-recorded, the owner saves, prefab writes are logged with their revert. ⏸
> marks an owner checkpoint.

## What the audits found — read this before assigning anything

The report is the grading surface, and today it is wrong in both directions.

| The report today (224 rows) | Where it is true at `0dc67c4` |
|---|---|
| **Thirteen slots the code plays produce no row.** `AudioProgressScanner` (`Assets/Scripts/Assessment/`) records one row per array *element* (`WalkValue`, :458), so an `AudioClip[]` of size 0 is invisible, while a single `AudioClip` field records even when null. | `headshotSounds: []` on all five enemy profiles (scene :201387, :201419, :201451, :201485, :201517), played on every head hit at `ZombieAI.cs:624`. `dialogueLines: []` on the "Mushroom Eating" profile, played at `MushroomTripInteraction.cs:165`. |
| **The boss and the fishing rod are never scanned.** `Scan()` (:217) discovers managers with `FindObjectsByType<MonoBehaviour>` (:223), and `WalkValue` stops at every Unity-object reference (:454) except `MusicStateDefinition`, which it follows deliberately, once (:440). `BossZombieController` and `FishingRod` have **zero scene instances** (grep of both script guids in the `.unity`: 0); the boss is spawned by the boss wave, the rod on pickup. | `Boss - Rock Golem.prefab`: `entranceRoar`, `slamWindupRoar`, `slamImpactSound`, `summonBellow` (single, null) and `phaseCrackSounds: []` (:3394-3420). `FishingRod.prefab`: three `fishTiers` (Perch, Silver Bass, Golden Koi) each with `biteSound` and `catchSound` null (:326-353), played at `FishingRod.cs:419`. `ClipLabelOverrides` already carries `phaseCrackSounds → "Phase Cracks"` (:704), so the report was *expected* to list the boss and never has. |
| **Three slots the report offers are never played.** | `reloadStartSound` and `reloadEndSound` on `WeaponSoundProfile` (`WeaponAudioManager.cs:78,80`) are shown by the filter for every type that reloads (:148-180) and `PlayReloadEndSound` (:537) has **no caller in the project**; `PlayReloadStartSound` (:526) is called only by `CrossbowController.cs:63`. `WeaponController.ReloadRoutine` (:522-538) plays only `PlayReloadActionSound` (:533). A student who authors mag-out and mag-in for the revolver gets silence and a green tick. `interactionSounds[]` (`InteractionAudioManager.cs:80`, type `GeneralInteraction`) is drawn by `InteractionAudioManagerEditor.cs:81-83`, cleared by `PortedPropTools.cs:616`, and played nowhere; no scene profile uses the type (`interactionType: 2` appears 0 times). |
| **Two components carry clip fields nothing can reach.** | `FireSourceAudio.cs` and `CampfireAudio.cs` (`Audio/`) are on no scene object and no prefab (grep of `.unity` and `.prefab`: only the two `.cs` files mention them). Dead code with a `fireLoop` and an `igniteSound` a student could never find. |
| **Labels.** | River rows read `River Profiles ▸ [0] ▸ River` (the profile has no name field; `RiverAudioManagerEditor.cs:32` calls them "River Area 1/2"). The spawner heading reads `Random Sound Spawner  (Birds - Random Sound Spawner (6))` — `FinishGroups` (:325) appends the GameObject name, which repeats the type and carries Unity's `(6)`. |
| **The docs describe a report that no longer exists.** | `ROADMAP.md:46` ("three states per slot") and `:52-58` (BEEP, REPLACE, EMPTY) — every not-done state renders as one red MISSING since the owner's 2026-09-06 decision (`AudioProgressScanner.cs:61-63`, `AudioProgressReportUI.cs:153`, `AudioProgressReportWindow.cs:143`). |

Audio state, ranked by what a student would hear.

| Defect | Where |
|---|---|
| **A crossfade frozen at partial volume.** `AmbienceManager.CrossFadeToState` advances on `Time.deltaTime` (:118). Die or pause mid-crossfade and two beds sit at half level for as long as the screen is up. The owner's policy keeps audio running under those screens, so the fade has to *finish* there. | `Area Management/AmbienceManager.cs:118` |
| Same shape, four more places: the dawn chorus swell, the occlusion filter sweep, the directional source sweep, the camera shake. | `Audio/DawnChorus.cs:99`, `Audio Systems/AudioOcclusion.cs:133,136`, `Audio Systems/DirectionalAudioSource.cs:169,170`, `Camera/CameraShakeController.cs:73` |
| **The boss's tell is erased.** `BossZombieController` deepens `voice.pitch` per phase (:203) — "the voice fraying is the tell" (:63). `AudioUtil.PlayRandomOneShot` (`Audio Systems/AudioUtil.cs:56`) writes `source.pitch = 1f` whenever no pitch range is passed, and `EnemyAudioManager` never passes one, on the same source (:146). The next idle vocalisation resets the voice. | `EnemyAudioManager.cs:289-309, :454-489` |
| **Footstep gain is written on the source, not the voice.** `audioSource.volume = profile.baseVolume` then `PlayOneShot(clip, 1f)` (:97, :104, :111). A jump at 1.0 retroactively raises a still-ringing grass step authored at 0.5. Everywhere else the project passes the profile volume per voice. | `Audio/FootstepAudioManager.cs:97-111` |
| **The no-repeat that never worked.** `PlayAttackSound`, `PlayDeathSound`, `PlayHeadshotSound` each declare `int lastIndex = -1` locally (:289, :299, :308); `GetRandomIndexNoRepeat` sees −1 every call. The profile already has the right pattern for the roles: `[NonSerialized] roleIndices` (:135) and `RoleIndex()` (:159). Searched every other manager: this is the only remaining instance. | `Audio/EnemyAudioManager.cs` |
| **Two runtime sources on Master.** `AudioRandomizer` adds its source in `Start` with `spatialBlend` but no `outputAudioMixerGroup` (:37); the grenade's detached ringing source sets clip, volume and blend but no group (:437), so the post-blast ring ignores the SFX fader and the volume slider. | `Audio Systems/AudioRandomizer.cs:37`, `Weapons/Grenade.cs:437` |
| **Two fallbacks that create an unrouted 2D voice.** `WaveManager.SpawnZombie` adds a bare `AudioSource` when the prefab lacks one (:565-570) and, because it runs before `ZombieAI.Start`, it wins over ZombieAI's own fallback (:260), which is equally bare. All eleven shipped enemy prefabs carry a root source on **Enemy vocalisations** and a child `Audio Source - Attack` on **Enemy attack** (verified per prefab), so this never fires today. Latent, and cheap. | `Waves/WaveManager.cs:565-570`, `Enemies/ZombieAI.cs:257-262` |
| **Two heartbeat latents recorded "for later" in `AUDIO_MANAGER_TIDY_TASKS.md`.** `currentHealth / profile.playBelowThisHealth` (:228) is NaN when the slider is 0; and both loop starters return on a null clip (:189, :332), so a loop whose clip is cleared while it plays is never told to stop. | `Audio/PlayerAudioManager.cs:189, :228, :332` |
| `OnZombieHit` is the only static event without a `[RuntimeInitializeOnLoadMethod] ResetStatics` (cf. `WeaponController.cs:561`, `Inventory/Item.cs`). Harmless while domain reload is on; a leak the day fast play-mode is turned on. | `Enemies/ZombieAI.cs:576` |

Runtime noise a student would see in the console, in ordinary play.

| Defect | Where |
|---|---|
| `collision.GetContact(0)` with no `contactCount` guard, on a `ContinuousDynamic` body (`Crossbow_Bolt.prefab`, `Grenade.prefab` `m_CollisionDetection: 2`), where a sweep hit can report zero contacts. `ZombieSpit.cs:288-289` has the correct guard; these two do not. | `Weapons/Arrow.cs:86`, `Weapons/Grenade.cs:205` |
| The bolt is never told to ignore its shooter. `Grenade.Launch` (:154-165) and `ZombieSpit.IgnoreTheShooter` (:254) both do; the bolt spawns against the player's capsule on layer Default vs Player with an all-ones collision matrix, sticks, and vanishes ten seconds later. | `Weapons/CrossbowController.cs:88-96` |
| `ArrowPool.GetArrow` dereferences a `pool` that is null when the prefab is unassigned — `ObjectPool`'s constructor returns early (`ObjectPool.cs:38-42`). `ZombieSpitPool.cs:63-71` documents and guards this exact flaw. | `Weapons/ArrowPool.cs:60` |
| `ZombieAI` writes `agent.isStopped` / `agent.speed` and reads `agent.isOnNavMesh` with no null check at :322-323 and :337, unlike :310, :347, :359, and none of its `isStopped` writes check `isOnNavMesh` except :742. `BossZombieController.cs:239` and `ZombieRole.cs:227,254` guard both. | `Enemies/ZombieAI.cs:310-359` |
| `CameraShakeController` treats the camera's Awake-time `localPosition` as the rest pose (:41, :71, :77) while `FirstPersonController` writes the same property for head bob and slide (:349, :360, :626-683). Firing while sliding snaps the camera to standing height for the shake. | `Camera/CameraShakeController.cs` |
| `AudioRandomizer` reads `Camera.main.transform` inside a `while (true)` coroutine with no null check (:47). | `Audio Systems/AudioRandomizer.cs:47` |

**Verified clean, so nobody re-audits it:** no `PlayClipAtPoint` anywhere; every other runtime-created source sets its mixer group (`UIAudioManager`, `FootstepAudioManager`, `RiverAudioManager`, `AmbienceManager`, `MusicManager`, `LayeredStatePlayer`, `WindChimeAudio`, `HeartbeatLayer`, `DawnChorus`, both detached-host paths); all music timing is DSP clock plus unscaled time; `MixerDip` is restored from `OnDestroy` in all three users; the minigun's teardown is covered three ways; every subscribe has its unsubscribe; `PlayerStats.Instance` is null-checked at all 14 dereferences; no LINQ or allocation in any gameplay `Update`; `SFXVolumeControl` clamps and defaults its PlayerPrefs; no `UnityEditor` outside `#if UNITY_EDITOR`; all four hand-drawn inspectors draw exactly the fields their filters show — **the "oldest trap" has no remaining instance.**

## Invariants

- **Nothing that plays today changes level, timing or clip.** The footstep fix keeps the same
  loudness (the product `source.volume × volumeScale` is unchanged); the reload fix adds two
  calls and moves none; the pitch fix restores what the boss set and touches no other source.
  The export diff before/after is **additions and relabels only**; any removed row is a defect.
- **`AudioUtil` is shared and is not edited.** Its reset-to-1 pitch is load-bearing for the
  weapon and footstep sources, which are shared between randomised and plain calls. The fix for
  the boss lives in `EnemyAudioManager`, the only caller that owns a pitch it did not set.
- **The owner's policy: audio runs under pause and death.** No `AudioListener.pause`, no
  per-manager stop on pause, no new stop paths. Fades and lerps use unscaled time so they
  *complete* under a frozen `timeScale`; loops keep looping.
- **Enum members are never removed or reordered.** `InteractionType` values 3-11 are serialized
  as ints on the scene's profiles; deleting `GeneralInteraction` (2) would renumber every one of
  them. The dead *field* goes, the member stays with a comment.
- **A serialized field is removed only after the grep** (`.unity`, `.prefab`,
  `Assets/Scripts/Editor`, `Assets/Scripts/Testing`), and the order records the grep result.
- Singletons null-checked at the dereference; no `DontDestroyOnLoad`; Legacy Input only; no
  `.asmdef`; no `.meta` edits by hand (script deletions go through the editor so the metas go
  with them); CRLF preserved (`file` before/after); `bash Documentation/tools/compile-check.sh`
  after every edit.
- Scene changes through `[MenuItem]` tools, Undo-recorded, **the owner saves**. Prefab and asset
  writes are not undoable: the tool says so in its log, `git checkout --` is the revert.

## Phase 0 — the baseline (integrator, before any agent starts)

No seams this time: the lanes below share no file. Phase 0 is evidence. Open the project, run
`Tools ▸ Audio ▸ Student Progress Report`, export, and keep the file as **the 224-row
baseline**. Take a console cursor. Note the SMG profile's `reloadStartSound`/`reloadEndSound`
(null today) so the reload measurement later has something to assign. Commit nothing.

## Agent plan

```
Phase 0  Integrator                     baseline export (224), console cursor
Phase 1  A · B · C · D · E · F          in parallel — disjoint files, each compile-checks alone
Phase 2  Integrator                     land A, B, C, D, E, F in that order → B's tool → probe
                                        → ⏸ owner saves → export diff → docs → commits
```

| Agent | Owns (creates or edits) — nobody else opens these | Reads | Editor? |
|---|---|---|---|
| **A** — the scanner reaches the prefabs | `Assessment/AudioProgressScanner.cs`, `Assessment/AssessedPrefabRegistry.cs` (new), `Audio/RiverAudioManager.cs` (one string field), `Editor/RiverAudioManagerEditor.cs` (its label) | the two report views, `BossZombieController`, `FishingRod` | no |
| **B** — the slot data and the dead slot | `Editor/AudioSlotSurfaceTools.cs` (new), `Audio/InteractionAudioManager.cs`, `Editor/InteractionAudioManagerEditor.cs`, `Editor/PortedPropTools.cs` | A's registry field names (fixed below), `EnemyRoleSetupTools` for tool shape | **no** — writes the tool, the integrator runs it |
| **C** — fades, routing, levels | `Area Management/AmbienceManager.cs`, `Audio/DawnChorus.cs`, `Audio Systems/AudioOcclusion.cs`, `Audio Systems/DirectionalAudioSource.cs`, `Audio Systems/AudioRandomizer.cs`, `Audio/FootstepAudioManager.cs`, `Audio/PlayerAudioManager.cs`; confirms `FireSourceAudio.cs` + `CampfireAudio.cs` are unreferenced (the integrator deletes them) | `AudioUtil` (read only) | no |
| **D** — enemy audio and ZombieAI | `Audio/EnemyAudioManager.cs`, `Enemies/ZombieAI.cs`, `Waves/WaveManager.cs` (lines 565-570 only) | `BossZombieController`, `ZombieRole`, `AudioUtil` | no |
| **E** — weapons and projectiles | `Weapons/WeaponController.cs`, `Weapons/CrossbowController.cs`, `Weapons/Arrow.cs`, `Weapons/ArrowPool.cs`, `Weapons/Grenade.cs`, `Audio/WeaponAudioManager.cs` (one accessor + two log lines), `Camera/CameraShakeController.cs` | `ZombieSpit`, `ZombieSpitPool`, `MinigunController`, `FirstPersonController` | no |
| **F** — the probe | `Testing/AudioIntegrityProbe.cs` (new, `#if UNITY_EDITOR`) | everything the others declare, fixed below; `Testing/EnemyRoleProbe.cs` for shape | **no** — writes it, the integrator runs it |

Names are fixed in this document so agents compile against each other without meeting:
`AssessedPrefabRegistry` with `public GameObject[] prefabs`; `EnemySoundProfile.lastAttackIndex`,
`lastDeathIndex`, `lastHeadshotIndex`; `EnemyAudioManager.ConfigureFallbackVoice(string, AudioSource)`;
`WeaponAudioManager.OutputGroup`; the menu items `Tools/Audio/Report Slot Surface` (1500) and
`Tools/Audio/Fix Slot Surface` (1501). An agent that must deviate says so in its report; the
integrator reconciles before landing.

## Agent A — the scanner reaches the prefabs

**Outcome.** The boss's five slots and the rod's six rows appear in the report — in the editor
window, in the Ctrl+G popup before the boss wave, and in the popup with the boss alive — with
**the same total in all three**.

1. `Assessment/AssessedPrefabRegistry.cs`: a `MonoBehaviour` with `[Tooltip] public GameObject[]
   prefabs` and a header comment saying what it is for (components that exist only as prefabs
   until play spawns them). B's tool places it on the Audio Managers object.
2. In `Scan()`, after the scene behaviours: `FindFirstObjectByType<AssessedPrefabRegistry>(
   FindObjectsInactive.Include)`; for each non-null prefab, `GetComponentsInChildren<MonoBehaviour>
   (true)`, and walk each with `WalkObject` under a group named the way scene groups are (type
   name in words) with the prefab name where scene groups carry the GameObject name. Follow the
   `MusicStateDefinition` precedent (:440-449): `scannedAssets` de-dup, `AddGroup` only if
   `Total > 0`.
3. **No double count.** A type reached through the registry is *skipped* when met as a scene
   instance — the boss during its wave, the rod after pickup. Record the registry's component
   types first, then filter the scene pass. F's probe proves the three totals match.
4. Labels: `ClipLabelOverrides` entries so the boss reads "Entrance Roar", "Summon Bellow", "Slam
   Windup Roar", "Slam Impact" (check what `TrimCategory` already strips before adding any);
   the fish rows label by `tierName` through `DescribeElement` (:601) — verify `IdentifierRank`
   ranks it, and add it if not. River: give `RiverProfile` a `public string areaName` (default
   from the editor's "River Area N" so existing data reads the same) and let `DescribeElement`
   find it; the editor draws it. Spawner heading: strip a trailing ` (n)` and a GameObject name
   that repeats the type in `FinishGroups` (:325).
5. Build safety: prefab assets referenced from a scene component ship in a build, and the walk
   is the same reflection walk as today — no `UnityEditor` in the runtime path.

**Done when:** an edit-mode scan of the scene as it is (registry absent) still reads exactly 224
with an identical export, and a scan with a registry holding the two prefabs adds exactly the
rows this document counts (5 boss once `phaseCrackSounds` is sized, 6 fish), nothing else.

## Agent B — the slot data and the dead slot

**Outcome.** Every slot the code plays has a row, and no row exists for a slot nothing plays.

`Editor/AudioSlotSurfaceTools.cs`, two menu items in the shape of `EnemyRoleSetupTools`
(report first, read-only; then fix; both log **assets written** and **scene fields set**
separately):

1. `headshotSounds` → size 1 on every `EnemySoundProfile` whose array is empty (find profiles
   by name, never by scene line). Five today.
2. `dialogueLines` → size 1 on the "Mushroom Eating" `InteractionSoundProfile`.
3. `phaseCrackSounds` → size 1 on `Boss - Rock Golem.prefab` — a **prefab write**, through
   `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` / `UnloadPrefabContents`, logged
   with its revert.
4. Add `AssessedPrefabRegistry` to the Audio Managers object if absent and fill it with
   `Assets/Inbox/Prefabs/Enemies/Boss - Rock Golem.prefab` and `Assets/Inbox/Prefabs/FishingRod.prefab`
   (found by `AssetDatabase.FindAssets("t:Prefab")` + the component, not by a hard-coded path —
   the crossbow's `CrossBow`/`Crossbow` lesson).
5. **One empty stem in each `MusicState_*.asset` loop segment** (`Assets/Music States/`,
   `stems: []` in all three today), so the three music states show as MISSING rows. Owner's
   decision 2026-09-08: the stems were emptied deliberately for the handout (`21bfca0`) and they
   **count**. These are **asset writes** (ScriptableObjects), logged with their revert; the stem's
   other fields take the defaults the `MusicStem` type declares, and `LayeredStatePlayer` must
   treat a stem with a null clip exactly as it treats no stem — B reads it and says so.
6. Idempotent: a second run reports "nothing to do".

The dead slot, in the three files B owns: delete the `interactionSounds` field
(`InteractionAudioManager.cs:80`), its `nameof` at :174 and the `GeneralClips` entry (:213 — the
`GeneralInteraction` case returns an empty array), the inspector case (:81-83) and the
`ClearArray` line (`PortedPropTools.cs:616`). **Keep the `GeneralInteraction` enum member** with
a comment saying why (serialized ints). Grep recorded here: `interactionType: 2` — 0 in
`.unity` and `.prefab`; `interactionSounds` — appears only as `[]` in scene profiles, which Unity
drops on the next save.

**Done when:** the report after `Fix Slot Surface` reads 224 + 5 + 1 + 5 + 6 + 3 = **244**, every
new row MISSING, and the export diff against the baseline is additions and relabels only.

## Agent C — fades, routing, levels

Each is one file, most are one line. The policy is *finish under a frozen clock*, not *stop*.

1. `AmbienceManager.cs:118` → `Time.unscaledDeltaTime`. The crossfade completes and commits
   `currentState` under the pause menu and the death screen.
2. `DawnChorus.cs:99`, `AudioOcclusion.cs:133,136`, `DirectionalAudioSource.cs:169,170` → unscaled.
   (Check each lerp's `t` is still clamped by `Mathf.Lerp`; unscaled delta is not clamped by
   `maximumDeltaTime`, so a long hitch must not overshoot.)
3. `AudioRandomizer.cs`: a `[SerializeField] AudioMixerGroup output` applied to the source it
   creates (:37) when set, and a null guard on `Camera.main` (:47) that skips the interval rather
   than throws. Its only user is `Random Container AudioSource.prefab`, not in the main scene —
   latent, but the next student who drags it in gets a source on Master.
4. `FootstepAudioManager.cs:97,104,111`: leave `audioSource.volume` at 1 and pass
   `profile.baseVolume` / `jumpVolume` / `landVolume` as `PlayRandomOneShot`'s `volume`
   argument. Identical loudness per voice; no retroactive gain on a ringing step. State in the
   report that the product is unchanged.
5. `PlayerAudioManager.cs:228`: guard the division (a threshold of 0 means *never*; no NaN).
   `:189` and `:332`: if the loop is playing and the clip is now null, **stop it**, then return.
6. Confirm, with the grep in the report, that `FireSourceAudio.cs` and `CampfireAudio.cs` are
   referenced by no `.unity`, `.prefab` or `.cs` outside themselves. The integrator deletes them
   through the editor (`AssetDatabase.DeleteAsset`) so the `.meta` files go with them.

**Done when:** compile green; every changed line is a `Time.*` swap, a group assignment, a
volume-argument move, a guard, or a stop — nothing else moved.

## Agent D — enemy audio and ZombieAI

1. **No-repeat.** On `EnemySoundProfile`, beside `roleIndices` (:135): `[System.NonSerialized]
   public int lastAttackIndex = -1, lastDeathIndex = -1, lastHeadshotIndex = -1;` and the three
   methods (:289, :299, :308) pass them by `ref`. Per profile, as the roles are — every zombie of
   a type shares one memory, which is the memory that matters to the ear.
2. **The boss keeps its pitch.** In every method of `EnemyAudioManager` that plays through a
   zombie's own source without a pitch range (`PlayAttackSound`, `PlayDeathSound`,
   `PlayHeadshotSound`, `PlayRoleSound`, `VocalizationLoop`): read `source.pitch` before
   `PlayRandomOneShot` and write it back after. `AudioUtil` is not touched (see Invariants).
3. **The fallback source is routed.** `ZombieAI.cs:257-262`: when it has to add a source, ask the
   manager to configure it — `EnemyAudioManager.ConfigureFallbackVoice(string key, AudioSource s)`
   sets `outputAudioMixerGroup` from the profile's `vocalisationMixerGroup` (the detached-host
   path at :373-379 already knows it), `spatialBlend` 1, `playOnAwake` false. Delete
   `WaveManager.cs:565-570` — it runs before `ZombieAI.Start` and produces the one source that
   path would then never configure. Five lines, nothing else in that file.
4. **Agent guards.** `ZombieAI.cs:322-323` and `:337` get the `agent != null` the neighbouring
   branches have; every `isStopped` write in the file gets `&& agent.isOnNavMesh`, the way :742
   and `ZombieRole.cs:227,254` do. No behaviour change on-mesh.
5. `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] ResetStatics`
   nulling `OnZombieHit`, beside :576, as `WeaponController.cs:561-562`.

**Done when:** compile green; `git diff --stat` shows the three files and nothing else.

## Agent E — weapons and projectiles

1. **Reload start and end play.** `WeaponController.ReloadRoutine` (:522-538): call
   `PlayReloadStartSound(WeaponAudioKey)` at the top, keep `PlayReloadActionSound` exactly where
   it is (:533), and call `PlayReloadEndSound(WeaponAudioKey)` after the `reloadTime` wait and
   before the magazine refills. The minigun reloads through this routine (`MinigunController.cs:155`
   reads `IsReloading`) so it inherits both. Read the filter (`WeaponAudioManager.cs:148-180`)
   and list, in the report, every `WeaponType` that shows the two slots; each must now have a
   path that plays them — the crossbow already plays Start at `CrossbowController.cs:63`, so
   give it End at the end of its cock if the filter shows End for it. Add
   `AudioEventLog.Record` to `PlayReloadStartSound` and `PlayReloadEndSound` (:526-546 — today
   they `PlayOneShot` without a log line) so the Ctrl+J monitor and F's probe can see them.
2. **Contacts.** `Arrow.cs:86` and `Grenade.cs:205`: `collision.contactCount > 0 ?
   collision.GetContact(0).point : transform.position`, normal likewise, as `ZombieSpit.cs:288-289`.
3. **The bolt ignores its shooter.** `CrossbowController.cs:88-96`: collect the player's colliders
   and `Physics.IgnoreCollision` each enabled one against the bolt, the way `Grenade.Launch`
   (:154-165) does, with the same skip for disabled colliders and the reason in the comment.
4. **The pool survives a null prefab.** `ArrowPool.cs:60`: the guard and the one-line log from
   `ZombieSpitPool.cs:63-71`.
5. **The ring is on the bus.** `Grenade.cs:437`: `source.outputAudioMixerGroup = audio.OutputGroup`
   where `audio` is the `WeaponAudioManager` already passed to `Launch` (:146), and
   `WeaponAudioManager` gains `public AudioMixerGroup OutputGroup` returning what its detached-host
   path uses at :616. One accessor; nothing else in that file beyond the two log lines above.
6. **The shake rides on the controller's pose.** `CameraShakeController.cs`: `elapsed` on
   unscaled time (:73) so a shake finishes under the death screen; and shake as an additive
   offset — remember the offset applied last frame, subtract it, add this frame's — instead of
   snapping to the Awake-time `localPosition` (:41, :71, :77). Firing while sliding stays at
   slide height.

**Done when:** compile green; the reload report lists the types; `git diff` shows no line moved
that this list does not name.

## Agent F — the probe

`Testing/AudioIntegrityProbe.cs`, `#if UNITY_EDITOR`, the shape of `EnemyRoleProbe`
(`KeepPlayerAlive()`, short settles, a `Report` string the CLI polls, numbers not impressions).
It assigns generated clips (`AudioClip.Create`, a second of tone) where a measurement needs a
clip the profile lacks, and restores the profile afterwards. Measurements:

1. **The crossfade finishes under pause.** Trigger an ambience state change, set `timeScale = 0`
   at 0.3 s, wait 3 s realtime, read both track volumes: old at 0, new at its target. Restore.
2. **The boss keeps its pitch.** Spawn `Boss - Rock Golem`, force a phase (reflection, as
   `EnemyRoleProbe` reaches `phaseSpeedScale`), read `voice.pitch` = P; `PlayAttackSound` through
   its source; read again = P.
3. **Footstep gain.** After `PlayJumpSound`, the footstep source's `volume` is 1.
4. **No-repeat.** Two generated clips on a profile's `attackSounds`; 50 calls; count consecutive
   repeats in `AudioEventLog` = 0.
5. **Reload.** Generated clips on the SMG's `reloadStartSound`/`reloadEndSound`; reload once;
   `AudioEventLog` shows Start, Action, End in that order, End no earlier than `reloadTime`.
6. **Projectiles.** 30 bolts at a zombie 6 m away and 10 grenades bounced off a wall: zero
   exceptions since the cursor; no bolt parented under the player; bolts return to the pool.
7. **Report parity.** `AudioProgressScanner.Scan().Total` in edit mode, in play before the boss,
   and in play with the boss alive: three equal numbers, and the expected one.
8. **Heartbeat at 0.** `playBelowThisHealth = 0` on the profile, health to 1: no NaN on any source
   volume, no error.
9. Console errors since the Phase 0 cursor: **0**.

## Integrator — sequence and verification

1. Phase 0. Baseline export (224), console cursor.
2. Land **A**. Compile. Scan: still 224 (no registry yet), identical export.
3. Land **B**. Compile. `Report Slot Surface`, then `Fix Slot Surface`, then the report: **244**,
   every new row MISSING, diff = additions and relabels. Run `Fix Slot Surface` again: "nothing
   to do".
4. Land **C**. Delete `FireSourceAudio.cs` and `CampfireAudio.cs` through the editor. Compile.
5. Land **D**, then **E**, compiling after each. Land **F**.
6. Run the probe through the CLI; every number in the log. Then a regression night: a wave
   spawns, registers, clears; music climbs and falls; the boss wave's report total equals the
   edit-mode total; console clean.
7. ⏸ **Owner saves the scene.** (The boss prefab write is already on disk.)
8. ⏸ **Owner listens** — nothing here can: a revolver reload now has three sounds in order,
   the footsteps sound exactly as before, a paused crossfade is finished when the menu closes,
   the boss's voice stays frayed between phases.
9. Docs: `ROADMAP.md` :46 and :52-58 rewritten for MISSING; `CODE_STRUCTURE.md` Assessment row
   (the registry, prefab-only components, 224 → 244), Audio row (the no-repeat, the pitch
   rule, the reload path, the footstep rule), Enemies row (the guards, the routed fallback),
   Weapons row (contacts, the bolt, the pool, the ring), Editor row (the two tools, the prefab
   write), Testing row (the probe); the work history; this file's STATUS; `README.md`'s index
   row; and the "Noted for later" items in `AUDIO_MANAGER_TIDY_TASKS.md` marked done.
10. Commits: A; B (code); B's tool run — the boss prefab and the three music states; C (incl.
    the two deletions); D; E; F;
    the owner-saved scene; docs. **Push only when the owner says.**

## Known risks, so nobody rediscovers them

- **The scanner's boundary is deliberate.** `WalkValue` stops at Unity objects (:454) because
  following materials, prefabs or textures would drag the asset graph into the report. The
  registry is the *only* door through it, and it lists prefabs by hand for that reason.
- **Prefab-only plus scene-instance = double count** unless the scene pass skips registry types.
  F's parity check exists for this.
- **Enum ints.** Deleting or reordering an `InteractionType` member silently retypes every
  authored interaction profile. The member stays.
- **`AudioUtil.PlayRandomOneShot` resets pitch on purpose** for shared sources. Fixing the boss
  there would give the revolver a randomised reload pitch. Fix the caller.
- **Unscaled deltas are not clamped.** After a long hitch `Time.unscaledDeltaTime` can be
  seconds; every converted lerp must still be a `Mathf.Lerp` with `t` clamped, and every
  converted accumulator must be compared, not multiplied into a position.
- **`WaveManager` is sensitive.** D's edit is five lines of deletion. Registration, `OnZombieDeath`,
  multipliers and the boss wave are untouched, and the regression night proves it.
- **Prefab writes are asset writes.** One prefab (the boss) and three ScriptableObjects (the
  music states) this time. The tool logs the diff and the revert.

## Noted, not in this order

- `UI/EnemyRadar.cs` rebuilds every indicator on any count change (:83-126 — 40 UI objects per
  spawn on a 40-zombie wave) and gates on count alone (:42-52), so a death and a spawn in the
  same 0.25 s window leave the new zombie without an indicator. Not audio; a one-line ticket:
  pool the indicators as `MinimapView` does, and key the refresh on identity.
- The Demon's one-spit window, the SMG reload take outlasting `reloadTime`, and the SMG's
  reduced kick — all ⏸ owner's ear, all already recorded in
  `ENEMY_ROLES_AND_FEEDBACK_TASKS.md` and `MUSIC_AUDIT_2_TASKS.md`.
- 25 % of the report is bullet impacts (56 of 224 rows). A weighting question for the assessor,
  not a code one.
- "Choul" for Ghoul, in the profile, the prefab and the enemy id. Known, documented at
  `AudioWiringValidator.cs:19`, and renaming a profile key is a student-facing change the owner
  makes, not an agent.
- `Item.OnDropped` and `WeaponController.OnHudStateChanged` already reset on load; `OnZombieHit`
  joins them here.

## STATUS

**Done and verified 2026-09-09.** Six agents in parallel under one integrator, nine commits
after the order's own, both tools run against the live editor, the probe run three times
(the first run found two things, below), and a regression night. The report reads **244**.

### Measured, not asserted

| Check | Result |
|---|---|
| Report | **224 -> 244**; the export diff against the 2026-09-07 baseline is additions and relabels only (the seven "removed" lines are the overall line, two group headers, two river relabels, two spawner relabels). Implemented unchanged at 174; MISSING 50 -> 70. |
| Parity | `Scan().Total` edit mode **244**, play before the boss **244**, play with the boss alive **244**. |
| Crossfade under `timeScale = 0` | Outside -> Swamp, frozen at 0.3 s, read 3.5 s later: old bed **0.000**, new bed at target (gap **0.000**). |
| Boss pitch | Phase 1 forced to **1.080**; after `PlayAttackSound` through the root source: **1.080**. |
| Footstep gain | A 0.375 marker on the shared source held through a jump and a landing; source rests at **1.000**. |
| No-repeat | Butcher `attackSounds`, two clips, 50 plays: **0** consecutive repeats (a dead memory averages ~25). |
| Reload | SMG: Start at **+0.00**, End at **+1.50**, `reloadTime` 1.50. Action is not logged (its method records nothing). |
| Projectiles | 30 bolts through `CrossbowController.Fire` and 10 grenades: **0** exceptions, **0** bolts under the player, **0** resting within 1.2 m, **0** still out of the pool after 11.6 s. |
| Heartbeat at threshold 0 | 1 hp, loop running: **0** of 70 AudioSources NaN or Inf. |
| Regression night | Clock forced to 0.875: **8** spawned, **8** registered, music intensity **0.00 -> 1.00**; all killed: count **0**, intensity **0.54 -> 0.00**; play-mode report **244**. |
| Console | **0** errors in every run. New warnings: exactly three, one per music state, from `MusicStateDefinition.OnValidate` - true until a clip is authored. |

### What the order got wrong, and what the probe found

| Claim | What is actually true |
|---|---|
| "one empty stem in each music state's loop segment (`stems: []`)" | The states had **no loop segment at all** - `loopSegments: []`, only intro and outro carried `stems: []`. The tool creates the segment (named Loop) and its stem (Stem 1). `LayeredStatePlayer` treats a stem with a null clip exactly as no stem (verified site by site by Agent B); `MusicManager`'s "legacy track" warning is gated on the same `HasLoops` and fired before too. |
| (not in the order) | The SMG's and revolver's `reloadActionSounds[0]` are **dangling references** since `5f1f18d`, so every reload of either gun logged `PlayOneShot(null)`. The report already listed both as MISSING; the method now treats a null element as one. Found by the probe's first run. |
| (the probe's own) | It picked the first prefab with a boss controller - none has one except the Rock Golem, so it took the first prefab with a `ZombieAI`, an unused Fort Golem - and compared the boss's id alias `RockGolem` to the authored `Rock Golem`. It now prefers the registry's boss and matches the alias the way the manager does. |

### Already done by the integrator

The project was opened, the baseline taken from the 2026-09-07 export (same scene, same
code), both dead scripts deleted through the editor, `Fix Slot Surface` run (and run again:
"nothing to do"), the river names seeded, the probe run to nine PASS, and the night run.
Commits: the order; A; B; B's asset writes; C; D; E; F; the probe-run fixes; docs.

### ⏸ What still needs the owner

1. ✅ **The scene is saved** - through the bridge at the owner's choice (2026-09-09) and
   committed as `449b452`: 32 lines in, 22 out, exactly the nine fields (five headshot
   arrays, the dialogue array, the registry and its two prefabs, the river names) and no
   reserialisation noise. The boss prefab and the three music states were already on disk.
2. ⏸ **Listen.** A revolver or SMG reload now has three sounds in order once its slots are
   authored; the footsteps must sound exactly as before; a crossfade caught by the pause
   menu is finished when the menu closes; the boss's voice stays frayed between phases.

### Follow-ups, none blocking

- **The crossbow has no `WeaponAudioManager` profile** - five exist (SMG, Revolver, Axe,
  Grenade, Minigun) and nothing is named Crossbow, so its shot, reload start and reload end
  all log a Miss and it contributes **zero report rows**. One profile entry of type Gun
  would give students the slots; it moves the count. **Owner, 2026-09-09: leave it for a
  later order.**
- The SMG reload take (2.16 s) outlasts `reloadTime` (1.5 s), so Reload End now lands ~0.66 s
  inside the Action take. Authoring, already recorded in `MUSIC_AUDIT_2_TASKS.md`.
- `EnemyRadar` rebuilds every indicator per spawn and can miss a new zombie. Separate ticket
  by the owner's decision.
