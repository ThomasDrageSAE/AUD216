# Roadmap Tasks — Implementation Work Order

> Written 2026-08-18. **This document is a work order for an implementing AI.** It turns
> the ten items in [ROADMAP.md](ROADMAP.md) into concrete, ordered instructions. Every
> fileID/guid quoted below was verified against the repo at commit `4828402`.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (orientation + single-authority
> rules) and the **ground rules** in [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md).
> Items marked ⏸ are **owner checkpoints** — stop and hand back; do not fake them.

## Global rules of engagement

1. `bash Documentation/tools/compile-check.sh` after every edit; commit per task with
   the repo's established message style. One task = one commit (Task 3 may be two).
2. **Scene/prefab YAML edits** are allowed and expected, but only line-targeted:
   use a Python script that asserts the exact current content of each line before
   replacing it (see commits `86ec28c`, `3c1c15b` for the pattern), then confirm with
   `git diff` that **only the intended lines changed**. Never regenerate a block.
3. Before renaming/removing any serialized field or public method: grep
   `Assets/Scripts/Editor`, `Assets/Scripts/Testing`, and the `.unity`/`.prefab` files
   for the name (string-bound `FindProperty`/reflection/UnityEvents break silently).
4. The compatibility contracts stand: `MusicManager.musicTracks` (reflection),
   `SetFightingState`/`SetIndoorState`/`ChangeState`, and the do-not-delete list
   (`Audio/MusicController`, `Core/ObjectPool`, `DayNightCycle.SmoothTransition`).
5. This is a sound-design assessment project: when in doubt, protect audio behaviour.

Useful identifiers (verified):

| Thing | Value |
|---|---|
| Placeholder clip | `Assets/Audio/Beep to replace.wav`, guid `680c856c8571b954ab0f5665caf936bf` |
| `Arrow` component inside `Crossbow_Bolt.prefab` | `{fileID: 1618442984709496322, guid: e2dd78ed311348b4da9cdc8657441337, type: 3}` |
| `ArrowPool.cs` script guid | `502348440a4874e0cbf40f14c1178178` |
| MUSIC mixer group | `{fileID: 8426258057616649091, guid: 855e36c5d423f44d6a9571cbdf1b2686, type: 2}` |
| SFX mixer group | `{fileID: -402529107390267425, guid: 855e36c5d423f44d6a9571cbdf1b2686, type: 2}` |
| MusicManager host GameObject (scene) | fileID `2049660006` |
| `Player` root / `PlayerStats` child / `Pickup - Food` | scene objects tagged `Player` (the child block starts at scene line ~163832; re-grep, lines drift) |

---

## Task 1 — Scene integrity: one `Player` tag, robust PlayerStats resolution

**Goal:** exactly one object tagged `Player`; every consumer finds `PlayerStats`
deterministically; the redundant root capsule stops double-firing triggers.

**Do NOT move the `PlayerStats` component to the root.** It holds many serialized
references (bars, animators, effect roots) and a YAML component-move is where scene
corruption comes from. Fix it in code instead:

1. **Code:** give `PlayerStats` a guarded singleton: `public static PlayerStats
   Instance { get; private set; }`, set in `Awake` (`if (Instance != null && Instance != this) { Debug.LogWarning(...); }
   Instance = this;`), cleared in `OnDestroy` (`if (Instance == this) Instance = null;`).
   No `DontDestroyOnLoad`.
2. **Code:** convert the PlayerStats-seeking call sites to `PlayerStats.Instance`
   (null-tolerated as today): `FoodItem.cs:11`, `MedicineItem.cs:14,26`,
   `Fish.cs:34,96`. In `ZombieAI` (`Start` caches `playerTransform` from the tag;
   `DealDamage` does `playerTransform.GetComponent<IDamageable>()`): keep the tag
   lookup for the transform, but resolve damage as
   `IDamageable target = PlayerStats.Instance as IDamageable ?? playerTransform.GetComponentInChildren<IDamageable>();`
   — the interface implementation lives on the child.
3. **Transform-seeking sites stay on the tag** (now unique): `ObjectiveRadar`,
   `WorldSpaceObjectiveMarker`, `SecretRockSwitch`, `BulletImpactManager`,
   `EnemyAudioManager` (x3) — verify each wants the *root* transform (they all
   measure distance/direction; the root is correct).
4. **Scene YAML:** change `m_TagString: Player` → `m_TagString: Untagged` on the
   `PlayerStats` child GameObject and on `Pickup - Food`. Two one-line edits; assert
   the neighbouring `m_Name:` lines first so you edit the right blocks.
5. **Scene YAML:** remove the redundant `CapsuleCollider` from the `Player` root:
   delete its component block **and** its entry in the root GameObject's
   `m_Component:` list. The `CharacterController` remains and is the player's
   collider. Verify with `git diff --stat` (one file) and by grepping that the
   deleted fileID appears nowhere else in the scene.

**Verify:** compile clean; grep shows exactly one `m_TagString: Player` in the scene;
`grep -rn 'FindGameObjectWithTag("Player")' Assets/Scripts` shows only
transform-seeking sites. ⏸ Owner play check: zombies damage the player; food/medicine/
fish consume and apply; pickup sound fires once per pickup.

## Task 2 — Restore the crossbow

1. **Scene YAML — add the `ArrowPool` component** to the MusicManager host GameObject
   (fileID `2049660006`): append a new `MonoBehaviour` block with a fresh unique
   fileID (pick a random 9–10 digit number, grep the scene to confirm it is unused),
   `m_Script: {fileID: 11500000, guid: 502348440a4874e0cbf40f14c1178178, type: 3}`,
   `m_GameObject: {fileID: 2049660006}`, and the one property that matters:
   `arrowPrefab: {fileID: 1618442984709496322, guid: e2dd78ed311348b4da9cdc8657441337, type: 3}`.
   Add the matching `- component: {fileID: <new id>}` entry to GameObject
   `2049660006`'s `m_Component:` list. Omitted fields (`initialPoolSize`,
   `poolContainer`) fall back to code defaults — first check `ArrowPool` tolerates a
   null `poolContainer` (create one under itself if not; add that guard).
2. **Prefab YAML:** in `Crossbow_Bolt.prefab`, set the Rigidbody's
   `m_CollisionDetection: 0` → `2` (Continuous Dynamic). A 3 cm collider at high
   speed tunnels through everything otherwise.
3. **Scene YAML:** the scene `CrossbowController`'s `arrowForce: 100` → `25`
   (~25 m/s; the code default is 20, the old prefab intent was 30).
4. **Code (`Arrow.cs`):** add a max-flight-time return: in `Initialize`, start a
   coroutine (`WaitForSeconds(maxFlightTime)`, serialized default 8f) that calls the
   existing `ReturnToPool()`; the existing `StopAllCoroutines()` in `ReturnToPool`
   cancels it on normal impact. An arrow fired into the void currently never returns
   and the pool grows forever.
5. **Code (`Core/ObjectPool.cs`):** guard `Return` against double-enqueue (a
   `HashSet<T>` of pooled items or an `isPooled` flag) — enqueueing the same instance
   twice hands one arrow to two callers.
6. **Code:** null-guard `firePoint` in the three fire paths (`WeaponController`,
   `AxeController`, `CrossbowController`): log an error and return instead of NRE.

**Verify:** compile clean; scene diff shows exactly the new block + one component
entry + the force line. ⏸ Owner play check: bolts fly, stick, damage, and the pool
count stays bounded (fire 30 bolts off the map edge; no runaway instantiation).

## Task 3 — Complete the game loop: end screen, death screen, restart

**Design decision (made — don't relitigate):** this is a **single-scene game** (only
`Level for Sound Design` is in Build Settings), so the DontDestroyOnLoad pattern on
the gameplay singletons is pure liability: on reload, the *new* scene copies destroy
themselves in favour of stale survivors holding dead references. The fix is to stop
persisting, not to patch each survivor.

1. **Remove `DontDestroyOnLoad`** (and any `transform.SetParent(null)` done only for
   its sake) from: `ObjectiveManager`, `DailyObjectiveManager`, `UIManager`,
   `AnimationEventHandler`, `FirstTimeInteractionTracker`, `UIAudioManager`,
   `PickupsAudioManager`. **Keep it** in `AudioProgressReportUI` (stateless,
   rescans). Keep each class's `Instance == null` guard — after a reload the old
   instance is destroyed, Unity's fake-null makes the guard pass, and the fresh scene
   copy takes over with fresh state and fresh references. That is the entire fix.
2. **Audit statics for reload-safety** (grep `static` in `Assets/Scripts`): anything
   holding scene state must be cleared on reload or fake-null-safe. Known items:
   `Fish.fishCaughtPerObjective` — call `Fish.ResetAllFishCounts()` from
   `DailyObjectiveManager.Awake`; check `ReticleController`'s static and
   `CameraShakeController.Instance` follow the fake-null-safe guard pattern; note
   `PickupBase.OnPickupCollected` is a static event with zero subscribers (Task 10
   removes it).
3. **New `EndGameScreen`** (`Assets/Scripts/UI/EndGameScreen.cs`): a code-driven
   IMGUI screen in the style of the project's overlays — no scene UI authoring, no
   wiring. Static `ShowVictory()` / `ShowDeath()` create a hidden host object;
   full-screen dim + title ("YOU ESCAPED!" / "YOU DIED") + two buttons:
   **Restart** → `Time.timeScale = 1; Cursor.lockState = CursorLockMode.None;`
   then `SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex)`;
   **Quit** → `Application.Quit()` (with `EditorApplication.isPlaying = false` under
   `#if UNITY_EDITOR`). Unlock and show the cursor while the screen is up.
4. **Wire it:** `ChestPickup.Collect` → `EndGameScreen.ShowVictory()` (keep the
   serialized `endScreen` GameObject as an optional extra to activate if the owner
   later builds real UI). `PlayerStats`: replace `QuitGameSequence`'s
   `Application.Quit()` with `EndGameScreen.ShowDeath()` after the existing
   `deathDelay` — keep the death effects and control-disable exactly as they are.
5. Keep `EndGame.cs` untouched (it is scene-present but unwired; Task 10 may revisit).

**Verify:** compile clean. ⏸ Owner play check: die → death screen → Restart → fresh
run with objectives, day counter, music and tracker all reset; collect the chest →
victory screen; Quit works in a build.

## Task 4 — Audio routing (the assessment surface)

1. **C1 — impact sources stop sliding:** in `BulletImpactManager`, replace the single
   `impactAudioSource` with a pool of **4** sources created in `Awake` on the same
   GameObject (copy the original's mixer group / spatial settings). Round-robin
   selection, preferring a source that is not `isPlaying`; position + pitch are set
   per impact on the chosen source. Keep the distance-delay math untouched.
2. **C2 — fishing SFX off the UI bus:** add to `UIAudioManager`:
   `[SerializeField] private AudioMixerGroup fishingMixerGroup;` and a
   runtime-created `fishingSource` (Awake: `AddComponent<AudioSource>`,
   `playOnAwake=false`, `spatialBlend=0`, routed to `fishingMixerGroup` when set,
   else fall back to the button setup so nothing breaks unwired). Route
   `PlayOneShot(...)` (its only two callers are the fishing sounds) through it.
   **Scene YAML:** add the property line to the scene `UIAudioManager` block:
   `fishingMixerGroup: {fileID: -402529107390267425, guid: 855e36c5d423f44d6a9571cbdf1b2686, type: 2}` (the SFX group).
3. **C3 + D8 — the mixing desk respects power:** in `InteractionSystemTMP`'s existing
   `onPowerToggled` handler, when power goes **off** and `isMusicPlaying`, stop the
   desk (`PlayMixingDeskAudio(profileName, false)`) and set `isMusicPlaying = false`.
   Add `ActionHandler.MarkMixingDeskUsed()` setting the
   `hasMixingDeskBeenUsedFirstTime` backing state, called where the desk actually
   toggles (`InteractionSystemTMP`, the mixing-desk branch); replace the two
   hard-coded `"TestMixingDesk"` literals with `ObjectiveConstants.TEST_MIXING_DESK`.
4. **C4 + D2 — sparks in sync, handled once:** move the sparks *audio*
   (stop / delayed start) out of the keypress path in `InteractionSystemTMP` into the
   `onPowerToggled` handler, so audio and particles share the lever tween's
   completion. In `ActionHandler.TogglePower`, delete the **direct**
   `ToggleElectricalSparks()` call (the event drives `ElectricalSparksController`
   already); in `ElectricalSparksController`, store the `DOVirtual.DelayedCall` tween
   and `.Kill()` it in `ActivateSparks` and `OnDestroy` (the stale call currently
   switches sparks off if power is retoggled within a second).

**Verify:** compile clean. ⏸ Owner listen check: SMG sweep — impacts stay put;
fishing SFX now ride the SFX bus (mixer meters); desk dies with the power; sparks
sound and particles change together.

## Task 5 — Adaptive music: fade band + activation

1. **Code — intensity fade band** (the §6.5.1 upgrade): add to `MusicStem`:
   `[Tooltip("0 = gate at minIntensity (current behaviour). >0 = level tracks intensity proportionally across [minIntensity, minIntensity+fadeRange]")] [Range(0f, 1f)] public float fadeRange = 0f;`
   In `LayeredStatePlayer.UpdateStemGates`, when `fadeRange > 0.001f` the slot's gate
   **target** becomes `Mathf.Clamp01((intensity - stem.minIntensity) / stem.fadeRange)`
   instead of the 0/1 threshold (hysteresis does not apply — the band is already
   continuous), still approached through the existing `MoveTowards` with the fade
   times so it cannot zipper. `primeGates` snaps to the band value. Equal-power
   shaping in `ApplyVolumes` stays as-is on top.
2. Update `MUSIC_LAYERING_PLAN.md` §6.5 (move the band from "worth adding later" to
   implemented) and the `MusicStateDefinition` validator: warn if
   `minIntensity + fadeRange > 1` (unreachable top of the band).
3. ⏸ **Owner-only activation checklist** (audible, cannot be done from the CLI): run
   `Tools/Music/Generate Test Stems`; on `MusicManager` assign the three assets to
   *State Definitions* and set *Default Music Mixer Group* to MUSIC; play — verify
   gapless loops, bar-aligned transitions (watch the Ctrl+M overlay), threshold and
   band fades; then author real stems per §6.5.

## Task 6 — Weapon feel pass ⏸ (implement, then owner tunes)

1. **B1 — the axe stops lying:** add `protected virtual bool CanAttack() => true;` to
   `MeleeWeaponController`, checked in `Fire()` **before** the swing sound and
   recoil; `AxeController` overrides it with `!isPositionTransitioning`. A rejected
   swing now makes no sound at all. Then make the transition framerate-independent:
   capture `startPos`/`startRot` when the transition leg begins, drive
   `t = clamp01(currentTransitionTime * speed)` and `Lerp(start, target, t)` —
   never lerp from the live transform with a `deltaTime` factor.
2. **B2 — the inspector stops lying:** delete the three `Start()` overrides in
   `AxeController` (`swingDuration = 0.5f; swingAngle = 80f; swingSpeed = 15f;`) and
   write those values into the scene axe via YAML (`swingDuration: 0.48` → `0.5`,
   `swingAngle: 90` → `80`, `swingSpeed: 5` → `15`) so behaviour is preserved and
   the inspector becomes the source of truth.
3. **B3 — framerate-correct recoil:** in `WeaponController.ApplyRecoil`, snap the
   transform to the kickback pose (no single-frame lerp), and make the recovery in
   `Update` use `1f - Mathf.Exp(-returnSpeed * Time.deltaTime)`.
4. **B5 — the weapon settles:** change `UpdateWeaponBob(bool isMoving, float speed)`
   — the caller passes `m_Input.sqrMagnitude > 0.01f && m_CharacterController.isGrounded`
   — and route the currently-unreachable "return to neutral, reset bobTimer" branch
   off `!isMoving`. Delete the misnamed walking/running parameters.

**Verify:** compile clean. ⏸ Owner feel pass: axe cadence and damage agree with what
you hear; recoil feels identical at different frame caps; weapon returns to centre
when you stop.

## Task 7 — Framerate-correct survival ticks ⏸ (implement, then owner balances)

In `PlayerStats`, every interval tick (stamina, hunger — grep `lastStaminaUpdate`,
`lastHungerUpdate` and siblings) currently credits exactly `interval` seconds while
real elapsed time is longer at low frame rates. Change each to
`float dt = currentTime - lastX; lastX = currentTime;` and multiply the rates by
`dt`, or advance with `lastX += interval` in a while-loop. Behaviour at high frame
rate is unchanged; low frame rates stop running ~25 % slow.
⏸ Owner: a balance pass afterwards (hunger decay and sprint economy now run at true
rates on every machine).

## Task 8 — Audio keys: validate now, enums carefully (W6)

**Stage A (do fully):** in `EnemyAudioManager`, `WeaponAudioManager`,
`InteractionAudioManager`, `FootstepAudioManager`: normalise the profile lookup —
build the dictionary with `key.Trim()` and `StringComparer.OrdinalIgnoreCase`, warn
on duplicate or empty keys at init, and when a `Play*` lookup misses, log **one**
warning naming the requested key and the available keys (rate-limit with a
`HashSet<string>` of already-warned keys — a missing footstep surface fires every
step). This alone eliminates the silent-typo failure mode for students.

**Stage B (start, but gated):** add enum fields **alongside** the strings —
`EnemyType`/`WeaponType`/`InteractionType` already exist (verify their members cover
the scene's authored strings; extend if not). Profiles gain
`public EnemyType enemyType;` etc. with the string kept; lookup prefers the enum when
set. Write an editor tool `Tools/Audio/Migrate Profile Keys To Enums` that maps each
profile's string to the enum by normalised name, sets it, and **logs every unmapped
string without touching it**. Do **not** remove the string fields or convert the
*callers* in this round. ⏸ Owner: run the migration in-editor, play-verify every
sound domain, and only then schedule the caller-side enum conversion.

## Task 9 — Authoring guardrails: the wiring validator

New `Assets/Scripts/Assessment/AudioWiringValidator.cs` — a static class with one
`public static List<string> Validate()` returning human-readable problems, invoked
from (a) a `RuntimeInitializeOnLoadMethod` gated by
`Application.isEditor || Debug.isDebugBuild` that logs each as a warning, and (b) a
**"Validate wiring"** button + results section in `AudioProgressReportWindow`.

Checks (all read-only, tolerate every manager being absent):

1. Every zombie prefab referenced by `WaveManager`'s weeks/nights/waves has a
   `ZombieAI.enemyAudioProfileName` that matches an `EnemyAudioManager` profile
   (normalised comparison), and vice-versa list profiles no prefab uses.
2. Every scene `WeaponController.weaponName` matches a `WeaponAudioManager` profile.
3. `FootstepAudioManager.surfaceProfiles` covers the full surface vocabulary:
   `grass, sand, rock, snow, swamp, wood, water, default` (the outputs of
   `TerrainTextureDetector.MapTextureToCategory` + `SurfaceDetector`'s tags).
4. Duplicate or empty profile keys anywhere; profiles whose every clip slot is empty.

**Verify:** compile clean; in the editor the validator reports the *current* known
gaps (there will be some — that is it working) without throwing.

## Task 10 — Dead-code sweep + prefab hygiene (last, on purpose)

1. Work through [IMPROVEMENT_PLAN_3.md](IMPROVEMENT_PLAN_3.md) **§E** exactly as
   written there, honouring its ⚠ serialized-field markers (record scene values
   before deleting) and these updates since it was written: keep `HasEmptySlot()`
   (the fishing fix uses it), keep `StopVocalizationLoop` (zombie death uses it),
   `ObjectiveTarget.cs` is already gone, and `EnemyAudioManager`'s dead
   vocalisation-request subsystem (`PlayVocalization`/`RequestVocalization`/the
   10-source pool + its per-frame `ProcessAudioRequests`) goes too. Re-grep every
   candidate for callers *and* scene/prefab wiring before deleting — Task 3 and 4
   may have added consumers.
2. **Prefab YAML:** strip the orphaned keys from `CrossBow.prefab`
   (`arrowPrefab`, `arrowSpeed`, `arrowDropoff`, `reloadTime`, `reloadEffect`,
   `reloadSound` — fields that no longer exist on `CrossbowController`); line
   deletions only, verify the diff.
3. ⏸ Owner (editor): re-save both weapon prefabs, assign their `firePoint`s, and
   decide the pickup-sound variations (`PickupsAudioManager` currently cycles four
   identical clips — author real variations or simplify to one; Plan-3 A8).

**Verify:** compile clean; `git diff --stat` per deletion batch; the game still
plays a full day/night/wave cycle. ⏸ Final owner playthrough of everything.

---

## Suggested commit sequence

`Task 1` → `Task 2` → `Task 3` (may split: singleton de-persist / end screens) →
`Task 4` → `Task 5.1-5.2` → `Task 6` → `Task 7` → `Task 8A` → `Task 8B (tool only)` →
`Task 9` → `Task 10`. Owner checkpoints fall naturally after 1–4 (one combined
playthrough is fine), after 5 (music activation), 6–7 (feel/balance), 8B (migration),
and 10 (final pass). Record deviations in this file's own STATUS block when done.

---

## STATUS — implementation record

> Updated 2026-08-18. Every task below was compile-verified with
> `Documentation/tools/compile-check.sh` on **both** assemblies before its commit.
> Nothing here has been run inside Unity — the ⏸ checkpoints are all still open.

| Task | Commit | Notes |
|---|---|---|
| 1 — one `Player` tag | `b6e9ed7` | as written |
| 2 — restore the crossbow | `37c086a` | as written |
| 3 — restartable game loop | `a79f6d0` | as written; single commit, not split |
| 4 — audio routing | `4295702` | fishing re-route used a `diegeticAudioSetup` (full `AudioSourceSetup`) instead of the specified bare `fishingMixerGroup` field — same effect, more student-tunable |
| 5 — vertical-layer fade band | `b462e2e` | as written |
| 6 — weapon feel | `c6f7710` | bob parameters renamed (`isMoving`/`isSprinting`) rather than deleted as 6.4 worded it |
| 7 — framerate-correct stats | `bfe8ea0` | as written |
| 8A — forgiving profile keys | `e0b05e8` | as written, plus a shared `AudioProfileKeys` helper so the four managers share one implementation |
| 8B — profile ids + migration tool | `b7ec905` | **deviates — see below** |
| 9 — wiring validator | `30563e3` | as written, plus a fifth check |
| 10 — dead-code sweep + prefab hygiene | `6b422e0` + `af011c0` | **Plan-3 §E was wrong in five places — see below** |

### Deviation: Task 8B does not reuse the existing enums

The work order assumed `EnemyType` / `WeaponType` / `InteractionType` could become the
lookup keys. They cannot, and the scene proves it:

- `SMG` and `Revolver` are both `WeaponType.Gun`;
- `Cave Door` and `Light Switch` are both `InteractionType.SimpleInteraction`;
- five enemy profiles share two `EnemyType` members;
- footsteps have no enum at all.

They are behaviour categories, not identities — and `InteractionType` additionally
decides which playback path runs (`IgniteFirestick`, `PlaySimpleInteraction`,
`PlayElectricalBoxSound` and `PlayMixingDeskAudio` all branch on it). Keying on them
would collapse distinct profiles *and* break that branching.

So `AudioProfileIds.cs` declares **new, separate** identity enums — `EnemyId`,
`WeaponId`, `InteractionId`, `SurfaceId` — one member per authored profile, each
starting at `Unassigned = 0`. The behavioural enums are untouched. Everything else the
task asked for holds: strings stay, callers are unconverted, the migration tool is
one-way and logs what it cannot map.

One addition worth knowing: assigning an id registers the id's own name as an extra
spelling of that profile, so the id starts protecting against typos immediately instead
of lying dormant until the callers are converted.

### Addition: Task 9 has a fifth check

The four checks in the work order are implemented as written. A fifth cross-checks the
interaction scripts' `audioProfileName` strings against the interaction profiles — the
same failure mode as the zombie and weapon checks, and the same one-line fix.

### Two real gaps the validator already finds

Both were confirmed statically against the scene, before ever running it:

1. **The crossbow has never been able to make a sound.** `CrossbowController` is
   authored with `weaponName: Crossbow`, and `WeaponAudioManager` has only `SMG`,
   `Revolver` and `Axe`. Both `PlayReloadStartSound` and `PlayShootSound` miss.
2. **No footstep profile covers `"default"`** — what `SurfaceDetector` returns for every
   untagged, non-terrain collider, and for a failed ground raycast.

Both are content decisions (someone has to author or assign the clips), so they are
reported rather than papered over. Adding an empty profile would only trade one finding
for another.

### Plan-3 §E corrections found while sweeping

Every candidate was re-verified with three parallel search passes before deletion —
C# callers, YAML serialized values, and UnityEvent `m_MethodName` bindings. Five of §E's
claims did not survive that check:

1. **`ActionHandler.ToggleMusic` is ALIVE.** §E says its body is commented out. Only the
   part *after* the power-off guard is; the guard itself still shows the player the
   "power is off" prompt, and `HandleAction` calls it for `"play music"` / `"stop music"`.
   Kept.
2. **`Inventory.SetSelectedItem` has a live caller** (`FirstPersonController`), so it
   could not be deleted alone. But what it stores was only ever read by the uncalled
   `GetEquippedItem` — a closed write-only loop. All three went, along with the call site
   and the comment promising "other systems" that do not exist.
3. **`WeaponItem.weaponName` is ALIVE and was not touched.** §E lists it for migration;
   in fact the Axe's value drives a real `OnAxePickup()` call. The actionable item is the
   inverse of deletion: the Revolver, SMG and Crossbow pickups have *empty* names, so the
   diagnostics that print `GetItemName()` print blanks for three weapons out of four.
4. **`FirstTimeInteractionTracker` has seven `Has*()` accessors, not eight.** The eighth
   belongs to `ActionHandler`, is differently named, and is live.
5. **`MeleeWeaponController.PerformMeleeAttack` could not simply be deleted.** The body
   is unreachable, but the signature is the dispatch point `PerformSwing` calls and the
   thing `AxeController` overrides. The body now warns instead of silently doing nothing,
   so a future melee subclass that forgets to override finds out immediately.

Deliberately kept against §E's advice: **`FishingRod`'s `if (!isFishing)` guard inside
the fishing coroutine.** It is unreachable only because `StopFishing` happens to stop the
coroutine in the same call. That is a property of today's implementation, not of the
design, and the guard costs nothing.

Two knock-on deletions §E did not list, both orphaned by the deletions above:
`ObjectPool.defaultCapacity` (read only by `PrewarmPool`) and `WeaponController`'s
`#pragma warning disable 0414`, which named a field that exists nowhere in the project,
had no matching `restore`, and was therefore masking a real unused-field warning for the
whole file. That field — `kickbackSpeed` — had lost its last reader in Task 6, and went
with it. A compile with the suppression lifted now reports no CS0414 anywhere.

### Post-implementation audit (2026-08-18)

An adversarial review pass over all ten tasks (three parallel code reviews + a
string-binding sweep + this documentation pass) found and fixed, in two commits after
the meta-file commit:

- **Restart starved the player** — the Task 7 elapsed-time stamps defaulted to 0 while
  `Time.time` survives a scene reload, so the first tick after a restart billed the
  whole previous session (instant death after ~200s of prior play, then a death loop).
- **The victory screen was unclickable** — nothing disabled `FirstPersonController` on
  the victory path and its Update re-locks the cursor every frame regardless of
  timeScale. Also: Escape resumed the world under the end screen, the death delay
  stalled if paused, and `IsShowing` could latch.
- **The axe ran at a third of its authored cadence** — Task 6 changed
  `attackSwingSpeed`/`attackReturnSpeed` semantics (rate → 1/duration) without
  migrating the scene's values; re-authored 3/0.8 → 5/3, plus a zero-guard and an
  OnDisable latch clear, and melee recoil now applies before the swing leg captures
  its start pose (it was being overwritten one frame later).
- **Rejected duplicate profiles could return via their id alias** in all four audio
  managers (in the weapon manager through another weapon's AudioSource); aliases now
  require `AudioProfileKeys.Owns()`. `NameOf` no longer indexes names by enum value
  (silent cross-wiring on any future non-contiguous enum), and the warn-once miss
  diagnostics reset on scene load, not just per play session.
- **The diegetic audio setup stomped scene-wired sources to 2D** and its documented
  button-bus fallback was unreachable; the validator gained per-type interaction
  checks (the scene's Mixing Desk profile passes the any-clip test while being
  permanently silent — now reported) and skips `InteractionSystemTMP`, whose
  serialized profile name is overwritten in Start.
- Sweep orphans removed (`isLit`, `AdjustTextSize`), `ObjectPool`'s double-return
  guard registered before `SetActive(false)`, the dead `GetComponent` fallback for
  `PlayerStats` now uses the singleton, and the Unity-generated `.meta` files for the
  nine new scripts are committed (their GUIDs must not regenerate per-checkout).

**Known, deliberately deferred** (needs authoring or a design call, not a code fix):
two scene weapons author `bobAmountWalk == bobAmountRun` so sprint bob is invisible on
them; the enemy "profile not used" check counts only WaveManager spawns; the migration
tool sees loaded scenes only (fine — all managers are scene objects); arrow reuse
correctness relies on the pool deactivating instances (commented at both ends); and
the orphaned YAML keys from swept fields drop off automatically on the owner's next
scene save.

### Owner checkpoints

- ✅ **2026-08-18: owner playthrough completed with no errors detected** — this closes
  the combined Tasks 1–4 playthrough and serves as the working-order verification of
  Tasks 6–7 and the audit fixes. (Feel/balance numbers remain open to tuning whenever
  the owner wants — the axe cadence values re-authored by the audit, 5/3, are a
  starting point, not a verdict.)
- ⏸ Task 5: run `Tools/Music/Generate Test Stems`, assign the `MusicStateDefinition`
  assets, verify transitions. The layered music is still dormant — a normal playthrough
  does not exercise it.
- ✅ Task 8B: **superseded by ROADMAP_2_TASKS Task 4** (2026-08-18). The ids were written
  into the scene and the five wave prefabs deterministically by YAML, and the enemy,
  weapon and interaction callers now resolve id-first with the string as fallback. The
  migration tool remains for future re-runs and for anything authored later. Footsteps
  keep string keys by design — `SurfaceDetector` derives surface names from tags and
  terrain layers at runtime, so there is no caller to convert; their `surfaceId`s still
  buy typo protection through the alias registration. ⏸ still needs one console-open
  playthrough per sound domain.
- ⏸ Task 10: re-save both weapon prefabs and assign their `firePoint`s; decide the
  pickup-sound variations (Plan-3 A8); decide `Fish.prefab` (Plan-3 §E last bullet).
  Re-saving the scene will also drop the orphaned YAML keys from swept fields.
- Content gaps the validator reports until someone authors them: a `"Crossbow"` weapon
  profile (the crossbow is silent without it) and a `"default"` footstep profile.
