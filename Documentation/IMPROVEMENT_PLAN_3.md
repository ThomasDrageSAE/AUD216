# Improvement Plan 3 — Gameplay Systems Audit

> Audit date 2026-08-18, covering the subsystems the first two audits never read:
> weapons/combat, inventory/items/pickups/fishing, player/controller, and
> enemies/interactions. Companion docs: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) ·
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md) · [TERRAIN.md](TERRAIN.md)
>
> Read the **ground rules** at the top of [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md)
> before starting — they still apply (serialized-field cautions, scene-wiring checks,
> `bash Documentation/tools/compile-check.sh` after every edit).

## What is already done

The code-only, high-confidence fixes from this audit are **implemented and committed**
(commits `d68cc67`, `875c0b3`, `5055c0a`, `421096b`). In short: combat music now stops
after the last kill; zombie death sounds are no longer faded out under themselves;
corpses no longer grunt, attack, or fight the NavMesh; dying actually removes player
control and stops the heartbeat; rejected slides no longer drain stamina; sprinting no
longer oscillates at zero; the axe can no longer be permanently disabled; pickups can no
longer be collected twice; fishing with a full inventory no longer farms objectives; and
the campfire prompt no longer sticks. `Zombie.cs` was deleted (attached to nothing).

**Update 2026-08-18 — most of the rest shipped too**, as Roadmap Tasks 1–10
(`b6e9ed7`…`af011c0`; record in [ROADMAP_TASKS.md](ROADMAP_TASKS.md)'s STATUS block):
A1/A2/A3/A6 and A7's YAML half, all of §B (B1–B5), C1–C4, D2/D5/D8, and the §E sweep.
A4 shipped in a different form (a code-drawn `EndGameScreen`; the scene's `endScreen`
reference is now an optional extra). **Round 2 (`45a3535`…`646bbff`) then closed C5, C6,
C7, A5 and every remaining D item** (D1/D3/D4/D6/D7/D9/D10/D11/D12 — see
[ROADMAP_2_TASKS.md](ROADMAP_2_TASKS.md)'s STATUS block, which records that D6 needed no
taxonomy change and W7 was already fixed). **Still genuinely open:** A7's `firePoint`s +
prefab re-save, and A8 (pickup variations) — both editor jobs. The per-item text below predates all of this — treat every
§A–§D item as historical analysis and check the STATUS block before acting on one.
Line numbers are from the audit and pre-date the fixes, so re-locate by symbol rather
than trusting them exactly.

---

# A. Scene and asset fixes (require the Unity editor)

These cannot be done safely from the CLI. **A1 and A2 are the two most valuable items in
this document.**

## A1 (P1) Three GameObjects are tagged `Player`

**Where:** `Assets/Level for Sound Design.unity` — the `Player` root, its child
`PlayerStats`, and the world pickup `Pickup - Food`.

**Defect:** `GameObject.FindGameObjectWithTag("Player")` returns an *unspecified* one of
the three, and **14 call sites depend on it** (`ZombieAI`, `FoodItem`, `MedicineItem`,
`Fish`, `EnemyAudioManager`, `BulletImpactManager`, `ObjectiveRadar`,
`WorldSpaceObjectiveMarker`, `SecretRockSwitch`, …). Only the child carries `PlayerStats`.
Depending on which one Unity returns: zombies deal no damage (`GetComponent<IDamageable>()`
returns null), eating food does nothing *and does not consume the item*, and the
impact-sound distance model measures from a static food can. `Pickup - Food` being tagged
`Player` also means it satisfies `PickupBase`'s own `CompareTag("Player")` test.

It currently works, which means Unity happens to return the right object — but the order
is not contractual and can change between sessions, builds and Unity versions.

**Fix:** In the editor: set `Pickup - Food` to `Untagged`. Then make exactly one object
carry the tag — preferably by moving the `PlayerStats` **component** onto the `Player`
root and untagging the now-empty child. Re-point the serialized references into
`PlayerStats` (health/stamina/hunger bars, animators, effect roots) rather than
recreating them, or the authored values are lost. Doing this also lets
`PlayerStats.DisablePlayerControls` go back to a local `GetComponents` call.

**Accept:** exactly one active object is tagged `Player`; zombies damage you; eating a
food can restores hunger and consumes the item.

## A2 (P1) The crossbow can never fire — no `ArrowPool` exists

**Where:** `CrossbowController.Fire()` requires `ArrowPool.Instance`; the `ArrowPool`
script GUID `502348440a4874e0cbf40f14c1178178` appears in **zero** scenes and prefabs
(verified independently).

**Defect:** `Instance` is always null. Draw and shoot sounds and the muzzle flash all
play, then it logs and returns. No bolt is ever spawned, so `Arrow.cs` and `ArrowPool.cs`
are dead at runtime and the crossbow is a prop.

**Fix:** Add a GameObject to the scene with an `ArrowPool` component and assign
`arrowPrefab` = `Crossbow_Bolt.prefab` (guid `e2dd78ed311348b4da9cdc8657441337`). Fix
**A6** at the same time or the bolts will pass through everything.

**Accept:** firing the crossbow spawns a bolt that travels, sticks and damages enemies.

## A3 (P2) The player has two colliders, so triggers fire twice

**Where:** the `Player` root carries both a `CharacterController` and an enabled
non-trigger `CapsuleCollider`.

**Defect:** Trigger callbacks are dispatched per collider pair, so one walk-through can
deliver `OnTriggerEnter` twice. Code guards were added to `Item` and `PickupBase` this
round, but the duplicate dispatch still reaches `FishingZone` (double enter/exit) and any
future trigger.

**Fix:** Remove the redundant `CapsuleCollider`, or move it to a child that is not tagged
`Player`. Keep the code guards regardless.

## A4 (P2) The game has no visible ending

**Where:** `ChestPickup` — its serialized `endScreen` is `{fileID: 0}`, and this is the
only code path that shows an end screen.

**Fix:** assign the end-screen GameObject on the single `ChestPickup` in the scene.

## A5 (P2) The sprint/slide FOV kick does nothing

**Where:** the scene's `m_FovKick.IncreaseCurve` has an empty `m_Curve: []`.
`FOVKick.CheckStatus` only rejects a *null* curve, and an empty `AnimationCurve.Evaluate()`
returns 0, so `FOVKickUp` adds zero for a full second while still running a coroutine.

**Fix:** author keyframes (0,0) → (1,1) on `IncreaseCurve` in the inspector.

## A6 (P2) Crossbow bolts will tunnel

**Where:** `Crossbow_Bolt.prefab` — Rigidbody `m_CollisionDetection: 0` (Discrete), capsule
radius 0.03, launched with `ForceMode.Impulse` at `arrowForce` 100 (~100 m/s ≈ 2 m per
physics step).

**Fix:** set Collision Detection to `Continuous Dynamic`, and reconcile the speed — the
code default is 20, the scene says 100, and the crossbow prefab holds an orphaned
`arrowSpeed: 30`.

## A7 (P3) Weapon prefabs carry orphaned data and an unassigned `firePoint`

**Where:** `CrossBow.prefab` still serializes `arrowSpeed`, `arrowDropoff`, `reloadTime`,
`reloadEffect`, `reloadSound` — none of which exist on the current script. Both weapon
prefabs have `firePoint: {fileID: 0}`.

**Defect:** the scene instances have `firePoint` wired so the game works, but
instantiating either prefab fresh NREs on fire.

**Fix:** re-save both prefabs in the editor to strip the stale keys, assign `firePoint`,
and add null guards to the three fire paths.

## A8 (P3) Pickup sound variation is inert

**Where:** `PickupsAudioManager.pickupSounds` holds **four identical clips** at pitch 1,
and `chestPickupSound` is the same clip again.

**Fix (sound design):** author real variations, or delete the cycling machinery. Note
`PlaySound` mutates pitch on a shared source, which repitches any one-shot still ringing —
fix that if variations are added.

---

# B. Needs a play-test or tuning decision

## B1 (P1) Roughly every other axe swing deals no damage

**Where:** `AxeController.PerformMeleeAttack` bails while `isPositionTransitioning`, but
`MeleeWeaponController.Fire()` only gates on `isSwinging` and `nextTimeToFire`.

**Defect:** With the scene values the position transition takes ≈0.73 s at 60 fps against
a 0.5 s fire gate, so a swing is *accepted* — playing the swing sound and recoil — and
then silently does nothing. The gap widens at lower frame rates because the transition
lerps from the live transform with `Time.deltaTime`.

**Fix:** gate `Fire()` on the same state the attack gate uses — add a virtual `CanAttack()`
that `AxeController` overrides to `!isPositionTransitioning`, so a rejected swing plays no
sound at all. Drive the transition off normalized time
(`Vector3.Lerp(startPos, targetPos, t)` with `t = currentTransitionTime / duration`
captured at swing start) rather than lerping from the current transform.

**Why not done:** changing the swing cadence changes how the axe feels and sounds; it
needs someone swinging it.

## B2 (P2) `AxeController.Start()` overwrites three inspector values

`swingDuration = 0.5f; swingAngle = 80f; swingSpeed = 15f;` hard-code over `[SerializeField]`
fields whose scene values are 0.48 / 90 / 5. Tuning the axe in the inspector does nothing,
and the inspector lies about actual behaviour.

**Fix:** delete those three lines and set the intended values on the scene component.
**Why not done:** deleting them makes the scene values take effect, which changes the
axe's feel — a deliberate re-tune, not a silent fix.

## B3 (P2) Recoil magnitude depends on frame rate

`WeaponController.ApplyRecoil()` performs **one** `Vector3.Lerp(current, target, Time.deltaTime * kickbackSpeed)`
and then hands over to the recovery, so the kick is whatever fraction one frame happened to
be — 33 % at 60 fps, 67 % at 30 fps, 14 % at 144 fps. The kickback pose is never reached.

**Fix:** snap to the kickback pose on fire and let the existing recovery interpolate; make
the recovery framerate-correct with `1f - Mathf.Exp(-returnSpeed * Time.deltaTime)`.

## B4 (P2) Stat regen and decay depend on frame rate

`PlayerStats` fires its tick when `Time.time >= last + interval` but always credits exactly
`interval` seconds, then sets `last = currentTime`, discarding the overshoot. At 30 fps the
0.05 s stamina tick fires every 0.0667 s and still credits 0.05 s — regen, sprint drain and
hunger decay all run at ~75 % speed. Sprint duration and starvation time therefore differ
per machine.

**Fix:** use the real elapsed time (`float dt = currentTime - lastStaminaUpdate;`) or
advance with `lastStaminaUpdate += interval`. **Why not done:** this re-balances survival
pacing and wants a play-test.

## B5 (P2) The weapon never settles to its neutral pose

`FirstPersonController` calls `UpdateWeaponBob(m_IsWalking, !m_IsWalking, speed)`, and
`m_IsWalking` means "not sprinting" — so it is `true` while standing still and
`isWalking || isRunning` is a tautology. The "not moving" branch is unreachable: when you
stop, `bobTimer` freezes mid-cycle and the weapon rests permanently off-centre.

**Fix:** pass a real movement flag (`m_Input.sqrMagnitude > 0 && isGrounded`), or branch on
`speed > 0.01f` inside `UpdateWeaponBob`. Rename the parameters while you are there.

---

# C. Audio and sound design

This is a sound-design project, so these carry more weight than their severity suggests.

| # | Sev | Where | Issue and fix |
|---|---|---|---|
| C1 | P2 | `BulletImpactManager` | One shared `AudioSource` is *moved* to each new impact point and repitched while previous one-shots are still playing, so the impact bed audibly slides across the level during automatic fire (SMG fireRate 0.1 with delays up to ~0.22 s guarantees overlap). Pool 4–8 sources round-robin, or spawn a short-lived source per impact. |
| C2 | P2 | `FishingRod` → `UIAudioManager.PlayOneShot` | The cast and catch sounds route through `buttonAudioSetup`, which is forced to 2D and the **UI Button** mixer bus, ignoring the authored volume. Diegetic SFX on the UI bus cannot be spatialised or mixed separately. Add a dedicated `AudioSourceSetup` with its own mixer group. |
| C3 | P2 | `InteractionSystemTMP` / `ActionHandler` | Mixing-desk music keeps playing after the power is cut — `ActionHandler.ToggleMusic()`'s body is entirely commented out and the `UpdateMusicState()` call is commented out too. `isMusicPlaying` never resets, so the next press stops instead of starting. Drive the speakers from the power-toggled event. |
| C4 | P2 | `InteractionSystemTMP` vs `ActionHandler` | Electrical-box sparks audio is **0.5 s out of sync** with the visuals: the sound is driven from the key press, the particles from the lever tween's `OnComplete`. Drive both from `onPowerToggled`. |
| C5 | P3 | `PlayerAudioManager` | `lowHealthVolume` is serialized, range-decorated, surfaced in the custom inspector and holds a scene value — but the heartbeat coroutine computes its own volume and never reads it. A silently ignored mix control. Multiply the computed curve by it, or delete the field. |
| C6 | P3 | `PlayerAudioManager` | `PlayHungerSound`, `PlayEatingSound`, `PlayStaminaDepletedSound`, `PlayStaminaRegeneratingSound` have **zero callers**, yet the scene authors clips into the Hunger profile. Either wire them (a hunger threshold in `HandleHunger`, a stamina-hits-zero edge in `HandleStamina`) or delete them and the profile fields. Also note the Health profile's `lowHealthLoop` and `damageSounds[0]` are both the placeholder `Assets/Audio/Beep to replace.wav`. |
| C7 | P4 | `PlayerAudioManager` | The heartbeat loop rewrites `source.volume` **every frame** for a value that only changes with health. Drive it from `UpdateLowHealthState` or tick at 0.1 s. |

---

# D. Remaining correctness items

| # | Sev | Where | Issue and fix |
|---|---|---|---|
| D1 | P2 | `PauseMenu` vs `MouseLook` | Two owners fight over the cursor. Pausing and unpausing with Escape leaves the OS cursor visible over the game until you left-click, while mouse-look still rotates. Make `PauseMenu` the sole authority: drop `UpdateCursorLock()` from `MouseLook.LookRotation` and set the lock only on pause transitions. |
| D2 | P2 | `ActionHandler` + `ElectricalSparksController` | Each power toggle is handled twice — once by a direct call, once via `onPowerToggled`. `DeactivateSparks` also schedules an un-owned `DOVirtual.DelayedCall` that is never killed, so toggling off-then-on within 1 s lets the stale call switch the sparks back off. Remove the direct call, keep the event, and store/kill the tween. |
| D3 | P2 | `FishingZone` | (a) `UpdatePrompt()` only runs when the rod *reference* changes, which happens once, so the prompt never reflects raise/lower. (b) The `Update` path never calls `SetFishingSpotMultiplier`, so a zone's quality is silently dropped when the rod is equipped after entering — every zone's tuning is inert on first use. (c) Overlapping zones: exit from one cancels the other's cast and resets its multiplier. |
| D4 | P2 | `FishingRod.MoveToLoweredPosition` | Returns on `isTransitioning` **before** reaching `StopFishing()`, while `FishingRodItem.ForceToLowered()` sets its own `isRaised = false` unconditionally — press a weapon slot during the 0.33–0.5 s transition and you keep fishing with the rod visually stowed, with the next slot press toggling the wrong way. |
| D5 | P2 | `Arrow` / `Core.Pooling.ObjectPool` | An arrow that never collides is never returned, and `ObjectPool.Get()` instantiates when empty, so the pool grows without bound. Add a max-flight-time coroutine in `Initialize`, and a duplicate guard in `ObjectPool.Return` (it will happily enqueue the same instance twice, handing one arrow to two callers). Latent only because of A2. |
| D6 | P2 | `BulletImpactManager` | The `rockEffect` branch is unreachable: `PlayEnemyEffect` is only called inside `if (CompareTag("Enemy"))`, and a GameObject has one tag, so `CompareTag("Rock")` inside it is always false. `Boss - Rock Golem` is tagged `Rock`, so it never gets enemy impact sounds at all. Decide the taxonomy — test `Rock` before `Enemy`, or use an explicit surface component. |
| D7 | P3 | `WeaponController` / `AxeController` / `Arrow` | Three different ways to resolve a hit target (`hit.transform`, `hit.collider`, `collision.gameObject`). These diverge for any damageable with a Rigidbody or a child collider. Nothing is broken today because every damageable is co-located, but the first compound collider will make one weapon register hits and another not, silently. Add one shared `TryGetDamageable(Collider, out IDamageable)` using `GetComponentInParent`. |
| D8 | P3 | `ActionHandler` / `FirstTimeInteractionTracker` | `hasMixingDeskBeenUsedFirstTime` is only assigned inside the commented-out `ToggleMusic()`, so it is permanently false and the tracker's intended early-out never fires — it polls every 0.5 s for the whole session. |
| D9 | P3 | `FirestickInteraction` | `playerInRange` is only set when `promptUI != null`, conflating UI availability with interaction state: with no prompt UI in the scene, all five firesticks become permanently un-lightable and the objective unreachable. Set the state unconditionally; null-check only the `ShowPrompt`/`HidePrompt` calls. |
| D10 | P3 | `InteractionSystemTMP` | `OnEnable` subscribes to `onPowerToggled` before `Start` can resolve `actionHandler`, so an un-wired instance silently never reacts to power changes. Resolve in `Awake`. |
| D11 | P3 | `ZombieAI` / `WaveManager` | Nothing checks `agent.isOnNavMesh`, and `SpawnZombie` instantiates at the raw spawn-point position with no `NavMesh.SamplePosition` snap. A spawn point that has drifted off the baked mesh gives a frozen zombie that spams the console every frame. |
| D12 | P3 | `SecretRockSwitch` | Every locked-interaction keypress starts another `HidePromptAfterDelay`; spamming E stacks them and they fight over the prompt text. Track and stop the previous handle. |

---

# E. Verified-dead code

> **DONE — swept in Roadmap Task 10 (commit `af011c0`).** Do not work from this list
> again: re-verification found **five of its claims wrong**, most importantly that
> `ActionHandler.ToggleMusic` is alive and that `WeaponItem.weaponName` must be kept.
> The corrections, and the items deliberately kept, are recorded in the STATUS block of
> [ROADMAP_TASKS.md](ROADMAP_TASKS.md). The text below is preserved as the historical
> record of what was proposed.

All confirmed with a whole-`Assets` grep for C# callers **and** for GUID / `m_MethodName`
references in scenes and prefabs. **Items marked ⚠ are `[SerializeField]` and hold scene
values — record them before deleting.**

- `MeleeWeaponController.PerformMeleeAttack` (base body) and ⚠`swingRadius`, ⚠`swingSpeed`,
  ⚠`attackArc` — `AxeController` is the only subclass and overrides the method; no bare
  `MeleeWeaponController` exists anywhere.
- `AxeController.MoveToRaisedPosition()`, `ArrowPool.PrewarmPool`, `ObjectPool.PrewarmPool`,
  ⚠`Arrow.hitEffect`.
- `WeaponController`: the `startRaised` parameter (never read — the body hard-codes false),
  `weaponOriginalPosition/Rotation`, `targetKickbackPosition/Rotation` (should be locals),
  the shadowed `currentBobAmount` field, and the `#pragma warning disable 0414` whose named
  field does not exist anywhere in the project — it is currently masking real warnings.
- `Inventory`: `DebugInventoryState()`, `GetEquippedItem()` + `selectedItemIndex` /
  `SetSelectedItem` (the whole selected-slot mechanism is write-only), and two `for` loops
  whose entire bodies are commented-out `Debug.Log`s. Keep `HasEmptySlot()` — the fishing
  fix now uses it.
- `WeaponItem.UseItem()` and `FishingRod.UseItem()` — unreachable; weapon selection routes
  around them. `FishingRod`'s `if (!isFishing)` mid-coroutine branch and `FishingZone`'s
  `|| !playerInRange` are both unreachable conditions.
- `PickupBase.ScoreValue`, `OnPickupCollected` (zero subscribers), `ResetPickup()`, and
  `IPickupable` — an entire unconsumed abstraction. ⚠`scoreValue` is serialized as 1 on five
  objects.
- ⚠`FishingZone.spotQualityDescription`, ⚠`fishingStartTime` (0.3), ⚠`fishingEndTime` (0.7) —
  a designed-but-unimplemented time-of-day fishing window, already `#pragma`-suppressed.
- `ZombieManager.AreZombiesActive()` / `LogZombieCount()`; `ZombieAI.healthMultiplier`
  (write-only) and `isPositionLocked` (written in four places, never read).
- `ActionHandler.PerformActionLogic()`, its empty `OnDestroy()`, and `ToggleMusic()`
  (body commented out — see C3 before deleting).
- `EnemyAudioManager`: `PlayVocalization` / `RequestVocalization` / the 10-source audio pool
  and its per-frame `ProcessAudioRequests()` over a queue that is always empty. **Keep
  `StopVocalizationLoop` — it is now called on zombie death.**
- `InteractionAudioManager.ResetFirestick` / `PlayInteractionSound`; `UIManager.DisplayPrompt()`;
  `SecretRockSwitch.initialRotation` and ⚠`promptUI`; `FirstTimeInteractionTracker.objectiveManager`
  and its eight `Has*()` accessors; `PlayerStats.GetCurrentHunger/GetCurrentLevel/GetCurrentXP/GetXPToNextLevel`;
  `FirstPersonController.m_AudioSource` (+ its `[RequireComponent(typeof(AudioSource))]`),
  `m_YRotation`, and ⚠`m_CameraShakeController`.
- ⚠`WeaponItem.weaponName` — overrides `GetItemName()` but is **empty for 3 of 4 weapons**,
  so diagnostics print blanks. Only the Axe uses it, via a string compare in `OnPickup`.
  Migrate to `itemName` plus an explicit flag; do not delete before migrating the value.
- `Assets/Inbox/Prefabs/Fish.prefab` — referenced by nothing; `FishingRod` builds its fish
  at runtime with `new GameObject("Fish")`, so caught fish are invisible, collider-less
  objects at world origin. Either instantiate the prefab (which also gets inspector-tunable
  XP and heal values) or delete it.

⚠ **Careful with `FirstPersonController.m_CameraShakeController`:** it is serialized and
wired in the scene but never read, and the fallback that runs when it is null does
`AddComponent<CameraShakeController>()` on the camera — whose `Awake` calls
`Destroy(gameObject)` on the **Main Camera** if another instance already exists. Delete the
field *and* the fallback together.

---

# Suggested order

| Phase | Items | Why first |
|---|---|---|
| 3.1 | A1, A3 | The tag collision is the highest-impact latent bug in the project and makes several other systems non-deterministic. |
| 3.2 | A2, A6, D5 | Restores the crossbow, which is currently a non-functional weapon. |
| 3.3 | C1–C4 | The audio defects that a sound-design submission is actually marked on. |
| 3.4 | B1, B2, B5 | Axe feel and weapon pose — needs someone playing. |
| 3.5 | A4, A5, A7, A8, D1–D4, D6 | Remaining polish and correctness. |
| 3.6 | B3, B4 | Framerate-dependent tuning; re-balance and play-test. |
| 3.7 | E | Dead-code sweep, once nothing above still needs it. |
