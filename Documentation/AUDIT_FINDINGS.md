# Script Audit Findings — 26T1 AUD216 Project 1

> Full-codebase audit, 2026-07-06 (~140 runtime + 14 editor scripts read in full).
> Severity: **HIGH** = bug / likely runtime failure · **MEDIUM** = performance or
> robustness · **LOW** = style, consistency, dead code.
> Companion docs: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) · [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md)
>
> ⚠ **This is a historical record and is not kept in sync with the code.** Most of it
> has since been fixed — see the STATUS blocks in
> [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) and
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md) for what was actually done, and
> verify against the current code before acting on any finding below.

## HIGH — bugs and likely runtime failures

### Audio-audible bugs (most important for a sound-design project)

| # | Where | Problem |
|---|---|---|
| H1 | `MusicManager.cs:175-223` | `ScheduleNextClip` re-fires **every frame** during the 0.1 s end-of-clip window — each call issues another `PlayScheduled` and advances `currentClipIndex`, so playlist clips get skipped/stacked. Needs an "already scheduled" flag. |
| H2 | `MusicManager.cs:180` | `track.source.clip.length` with no null guard — a track with no clips NREs every frame. |
| H3 | `WeaponAudioManager.cs:170-198` | Pitch is randomized, `PlayOneShot` called, then `pitch = 1f` **on the next line**. Pitch affects the playing one-shot in real time, so the reset cancels the variation — weapon pitch randomization does nothing. |
| H4 | `MedicineItem.cs:35` + `PlayerStats.cs:471` | Medicine SFX **double-fires**: the item plays it and `AddHealth` plays it again. |
| H5 | `Fish.cs:99-103` | Eating a fish plays `PlayFishEatenSound` **and** the medicine sound (via `AddHealth`). Wrong sound stacking. |
| H6 | `PlayRandomSound.cs:57-58` | `PlayDelayed(...)` immediately followed by `Play()` — `Play` cancels the scheduled delay, so min/max delay never works and clips chain back-to-back. |
| H7 | `AmbienceMusicManager.cs:66-75 vs 133-169` | `Update` forces the current area's volume to target every frame while the crossfade coroutine is still fading the *old* area out — Update fights the fade; crossfade is broken. (Legacy system — candidate for deletion instead of fix.) |
| H8 | `BulletImpactManager.cs:200-203` | Single shared impact `AudioSource`: pitch set → `PlayOneShot` → `pitch = 1f` same frame (same bug as H3), and concurrent delayed impacts fight over one source's position — wrong spatialization. |

### Null-reference crashes waiting to happen

| # | Where | Problem |
|---|---|---|
| H9 | `FoodItem.cs:11`, `MedicineItem.cs:14,38`, `Fish.cs:35,95` | `FindGameObjectWithTag("Player").GetComponent<…>()` chained with no null check (and re-run on every use). |
| H10 | `EnemyAudioManager.cs:97,134` | Player lookup unguarded in Start; `ProcessAudioRequests` then dereferences the null `playerTransform` every frame. |
| H11 | `EnemyAudioManager.cs:179` | `PlayVocalization` indexes `vocalisationSounds` without a null/length check (the loop path at :306 does check). |
| H12 | `AudioOcclusion.cs:83-88` | `player.position` in Update with no guard — NRE every frame if no `AudioListener` found. |
| H13 | `PlayRandomSound.cs:52-58` | No empty/null list check; no `[RequireComponent]`, `myAudioSource` may be null. |
| H14 | `StartSoundAtRandomPointInClip.cs:13-16` | `audioSource` and `clip` both unguarded. |
| H15 | `ZombieAI.cs:180,276,284,405,411` | `animator` dereferenced without guard (only logged as missing at :126). |
| H16 | `ZombieAI.cs:252-255` | Attack coroutine uses `enemyAudioManager` but only `attackAudioSource` was null-checked. |
| H17 | `FirstPersonController.cs:267,283` | `PlayJumpSound` / `playerStats.UseStaminaForDoubleJump` unguarded (landing path *is* guarded). |
| H18 | `FishingRod.cs:137-211` | `EventManager.Instance.*` dereferenced throughout with no guard. |
| H19 | `CrossbowController.cs:55` | `ArrowPool.Instance` unguarded; also draw state never resets on mouse-release → next click fires instantly. |
| H20 | `StatsHelper.cs:62,66` | `reportUI.activeSelf` every frame, unguarded. |
| H21 | `AnimationEventHandler.cs:65` | `dayNightCycle.GetTimeOfDay()` in Start right after an unguarded `FindFirstObjectByType`. |
| H22 | `DebugDisplay.cs:41-45` | `debugPanel` unguarded, and duplicate-singleton branch has no `return` after `Destroy` so the doomed copy keeps running Awake. |
| H23 | `ObjectiveRadar.cs:54` | `WorldSpaceObjectiveManager.Instance` unguarded in LateUpdate. |

### Logic bugs

| # | Where | Problem |
|---|---|---|
| H24 | `EnemyAudioManager.cs:151` | `pendingRequests.Clear()` after the pool loop silently **discards** requests that couldn't be served this frame. |
| H25 | `FirstPersonController.cs:372,500` | Blanket `StopAllCoroutines()` kills *every* coroutine on the controller (jump bob, stand-up lerp, FOV kick) → stuck crouch height / broken transitions. |
| H26 | Equip pipeline | `FirstPersonController.SelectInventorySlot` never calls `Inventory.SetSelectedItem`, so `GetEquippedItem()` always returns nothing → `FishingSpot.cs:68` can never find the rod. `FishingSpot` is dead in practice (superseded by `FishingZone`). |
| H27 | `DailyObjectiveManager.cs:281-292` | Fetches `nightObjectives` from the dictionary then loops over `regularObjectives` — the whole block is a broken duplicate of :267-278. |
| H28 | `EnemyRadar.cs:32,91` | `FindGameObjectsWithTag("Enemy")` every LateUpdate, and `RefreshEnemies` scans **every GameObject in the scene** (`FindObjectsByType<GameObject>`); detection uses *tag* in one place and *layer* in the other, so a mismatch makes the full rescan run every frame. |
| H29 | `UIManager.cs:272-287` | Update runs `GetComponent<RectTransform>()` + full panel-transform rebuild every frame, prompt visible or not. |

### Project hygiene (HIGH)

- **13,858 macOS `._*` AppleDouble junk files** under `Assets/` (Unity imports them as noise; searches/UX suffer).
- **No version control**: not a git repo, no `.gitignore`; 18 stale `.sln` files at the project root from prior trimesters.
- Editor exporters write generated JSON **into `Assets/Exports/`** (`WaveManagerExporter.cs:43-47`, `ObjectiveManagerExporter.cs:43-47`), and those artifacts are committed.
- `AudioOcclusionEditor.cs:19-31` mutates the target directly with no `Undo.RecordObject` and no `serializedObject.Update()` — inspector edits aren't undoable and can be lost.

## MEDIUM — performance and robustness

**Per-frame waste / polling that should be event-driven**
- `EnemyAudioManager.cs:126-136` — sorts `pendingRequests` (with a distance delegate) every frame even when empty.
- `AmbienceMusicManager.cs:66` — `System.Array.Find` per frame for values that only change on area transitions; also `GameObject.Find` naming contracts (`"{Area}Ambience"`).
- `RiverAudioManager.cs:102-176` — per-frame spline projection + SmoothDamp for every in-range river; `UnPause()` used to start never-played sources; `OnEnable` restarts playback.
- `FirstTimeInteractionTracker.cs:113-123` — polls three `ActionHandler` bools every frame for a one-time event (an `onPowerToggled` event already exists).
- `DailyObjectiveManager.cs:201-293` — full objective scan per frame.
- `FishingZone.cs:41-81` — per-frame inventory scan + input poll.
- `DebugDisplay.cs:72-81` — builds an interpolated multi-line string every frame while visible; `WorldSpaceObjectiveMarker.cs:147` allocates a distance string every 0.2 s.
- `ObjectiveRadar` / `EnemyRadar` — `GetComponent` per indicator per frame; `ObjectiveRadar.cs:66` allocates a `List<string>` every LateUpdate.
- `Spline.cs:66-107` — `OnDrawGizmos` reallocates the point array and rebuilds the whole BSP tree on every editor repaint.
- `WeaponController.cs:52,257,262` — `weaponName.Contains("SMG")` string checks per shot / per property access (also NRE if name unset).

**Robustness / correctness**
- `AudioOcclusion.cs:80` — overwrites the inspector layer mask with `1 << NameToLayer("Occluder")`; if the layer doesn't exist that's `1 << -1` (garbage mask). `:105-112` also force volume to `1f`, discarding authored source volume.
- `AudioRandomizer.cs:47-62` — `Camera.main` inside the coroutine loop (tag scan + NRE risk); reuses one source with `Play()` so overlapping timers cut sounds off.
- `RandomSoundSpawner.cs` — nullable `audioClips` unguarded (`:185`); shared `fadeRoutine` used by both FadeIn and FadeOut can interleave; repositions its own GameObject.
- `InteractionAudioManager.cs:201-215` — untracked fire-loop coroutine survives `ResetFirestick`/destroy.
- `EnemyAudioManager.cs:275-325` — `while(true)` vocalization coroutines rely on destroy checks to exit; leak if `StopVocalizationLoop` is missed.
- `TerrainTextureDetector.cs:9` — `terrain` unguarded; alphamap coords unclamped (can throw at terrain edges).
- `ActionHandler.cs:165` — `renderer.material` in `ToggleEmission` leaks a new material instance per renderer per toggle (`ToggleMeshRenderers` does it correctly — caches).
- `ZombieAI.cs:478` — `SetHealthMultiplier` compounds `maxHealth` on repeat calls; `XPReward` is never awarded to the player (dead feature).
- `DayNightCycle.cs:324-374` — the "smooth interpolation" clamps `t` to 1 every time it runs — smoothing is a no-op. `:339,761,837` mutate **shared material assets** at runtime (edits persist after Play mode in-editor).
- `PlayerStats.cs:178` — debug self-damage key (`P`) ships in builds. Testing scripts (`GameTesting`, `StatsHelper`, `Teleporter`, `ToggleGameObjects`) are not `#if UNITY_EDITOR`/dev-build gated and poll input in production.
- `GameTesting.cs:453-476`, `StatsHelper.cs` — reflection into other classes' private fields (`BindingFlags.NonPublic`); already drifted once (`StatsHelper.cs:429` reflects `weaponProfiles`, real field is `m_WeaponProfiles` → returns null).
- Singleton duplicate branches missing `return` after `Destroy(gameObject)`: `WorldSpaceObjectiveManager.cs:48`, `DebugDisplay.cs:41`, `ReticleController.cs:93`, `EventManager.cs:47`.
- Lifetime mismatch: DontDestroyOnLoad `ObjectiveManager` ↔ per-scene `WorldSpaceObjectiveManager` (stale refs after scene reload). Marker creation is also gated behind `FirstTimeInteractionTracker.Instance != null` (`ObjectiveManager.cs:149-160`).
- Three competing Escape handlers: `PauseMenu`, `MenuManager`, `StatsHelper`.
- `PickupBase.cs:40` — static event `OnPickupCollected` leaks subscribers for process lifetime.
- Editor: stale foldout-state arrays in `EnemyAudioManagerEditor`, `InteractionAudioManagerEditor`, `PlayerAudioManagerEditor`, `WeaponAudioManagerEditor` → IndexOutOfRange under Undo/multi-select; `MeleeWeaponControllerEditor` button mutates target without Undo; exporters hand-roll JSON with culture-sensitive float formatting and partial escaping.

## LOW — style, consistency, dead code

**Dead / duplicate code inventory** (safe-delete candidates, verify scene refs first):

| Item | Evidence |
|---|---|
| `FilterManager.cs` | No callers; both occlusion components write their own lowpass filter. |
| `TerrainChecker.cs` | Unused duplicate of `TerrainTextureDetector`. |
| `SoundClip.cs` | Type referenced nowhere. |
| `UIAudioManager.cs:15-83` nested `UISounds` | ~70-line duplicate of the manager's own fields, never instantiated. |
| `MusicController2.cs` | Only referenced by commented-out code. |
| `SyncAudioSources.cs` (class `MusicController`) + `StopMusicTrigger.cs` | In "Possibly Unused Scripts", unreferenced; StopMusicTrigger also has an overlapping-coroutine fade bug. |
| `AnimatedObjectiveUI.cs` → `UIPoolManager.cs` → `Core/ObjectPool.cs` | Chain of unused: ObjectiveManager instantiates `ObjectiveUI2` directly. |
| `ObjectiveMarker.cs`, `ObjectiveTarget.cs` | Superseded by `WorldSpaceObjectiveMarker` / `ObjectiveTargetLocator`. |
| `FishingSpot.cs` | Non-functional (see H26); `FishingZone` is the live path. |
| `MinMaxSliderAttribute.cs` ≈ `RangeSliderAttribute.cs` | Near-identical attributes. |
| `DayNightCycle.cs` | Dead methods: `UpdateSkyColors`, `UpdateCloudColors`, `UpdateParticleSystemGroup`, `LogTimePhase`; dead time-critical-objective fields (live logic is in `DailyObjectiveManager`). |
| `DailyObjectiveManager.cs:425-438` | Private `PauseTime`/`ResumeTime` never called. |
| `MedicineItem.cs:59-88` | `RemoveAfterEffects`/`RemoveFromInventory` never invoked. |
| `ActionHandler.cs:311,368,373` | `PlaySound` never called; empty subscribe/unsubscribe stubs. |
| `HUDManager.cs:67-91` | Pool fields + fully commented-out `InitializePools`. |
| `InteractionAudioManager.cs:117-130,160-167` | Loop over just-created empty dict (configures nothing); mostly commented-out method body. |
| `UIAudioManagerEditor.cs` | Redundant wrapper around `DrawDefaultInspector`. |
| `Assets/_Recovery/0.unity` | Crash-recovery leftover. |
| Unused serialized fields | `ReticleController.sizeChangeSpeed`, `TriggerLightIntensity.startIntensity`, `WorldSpaceObjectiveMarker.maxViewDistance`, `CameraShakeController.m_FirstPersonController`, `Spline.moveSpeed/smoothMovement/smoothTime`, `ObjectiveUI2.backgroundImage`, `InteractionPromptUI.backgroundImage`, `ObjectiveData.targetTag`. |

**Naming / convention inconsistencies**
- Filename ≠ class name: `TriggerSound.cs` (`RandomTriggerAudioVariations`), `InteractionSystem.cs` (`InteractionSystemTMP`), `SyncAudioSources.cs` (`MusicController`).
- Lowercase class `compass`; `EndGame.MainMenu()` reloads the scene (name lies).
- Only `FootstepAudioManager` uses `namespace Audio`; everything else is global namespace. Only `WeaponAudioManager` uses `m_` prefixes. Public fields vs `[SerializeField] private` varies file-by-file.
- `CampfireAudio` never sets `isCampfire = true`, so `OnValidate` and `InitializeAudioSource` fight over min/max distance. `FireSourceAudio.Reset()` collides with Unity's editor `Reset` message.
- String-typed keys throughout despite existing enums; `switch` on `hit.collider.tag` instead of `CompareTag`; the tag→surface switch is duplicated verbatim twice in `SurfaceDetector.cs:37-42,73-78`.
- `ObjectiveConstants` defines 3 ids but call sites still use raw string literals ("PickupFishingRod", "LightCampfire", …).
- Magic numbers: cutoff constants 20000/22000 Hz, −80 dB floor, 0.1 s scheduling thresholds, heartbeat mapping (`PlayerStats:151-163`), radar constants 58f/0.322f, `WaveManager` `0.868f` spawn time, hardcoded `Arrow.damage = 50` (crossbow damage never reaches the arrow).
- Duplicated "do-while random no-repeat" in `TriggerSound.cs:52`, `MusicManager.cs:209`, `EnemyAudioManager.cs:312`.
- Unconditional `Debug.DrawLine` every frame (`AudioOcclusion.cs:94`, `DirectionalAudioSource.cs:193`); heavy commented-out log noise across `Inventory`, `WaveManager`, `MusicManager`, `SurfaceDetector`.
- UI overlaps: quit logic in 3 places; volume in 2 places with different mixer params (`volume` vs `SFXVolume`); prompt UI duplicated (`UIManager` vs `InteractionPromptUI`); compass math copy-pasted 3× (`compass`, `EnemyRadar`, `ObjectiveRadar`); day counting duplicated (`AnimationEventHandler` vs `DayNightCycle`).
- Editor scripts bind by string literals everywhere (no `nameof()`); `DirectionalAudioSourceEditor` shows runtime values without `RequiresConstantRepaint`.

**Hygiene (non-HIGH)**
- `Assets/Inbox/` is an unsorted dumping ground (full third-party packs, a second scene copy, `.DS_Store`s); `Inbox/Tidy/` contains auto-generated terrain-upgrade folders and a corrupted-filename screenshot.
- Empty folder `Assets/Scripts/DayNightCycle/Editor/`.
- No `.asmdef` files — everything recompiles into `Assembly-CSharp` on any change.
- Input handler is legacy-only; fine, but noted for future package expectations.
