# Enemy Roles and Combat Feedback — Work Order (parallel agents)

> Written 2026-09-07 at commit `2fbf9b4`. **This is a work order for an implementing AI
> (Opus) running six agents in parallel under one integrator.** Two of the ten engagement
> recommendations the owner picked:
>
> 6. **Zombie roles, not just skins.** Ghoul = runner, Butcher = shield, Demon = spitter,
>    Yokai = screamer, each telegraphed by its own sound. The Rock Golem boss keeps its
>    `BossZombieController` and is not touched.
> 7. **Combat feedback.** A hitmarker on the reticle, a headshot ping, kill XP popping at the
>    corpse, and a screen kick authored per weapon instead of two hard-coded multipliers.
>
> The owner's four decisions (multichoice, 2026-09-07): the role mapping above; the shield is
> **front-facing damage reduction that headshots bypass**; the spitter fires **a lobbed
> projectile you can dodge**; kill XP pops **at the corpse, on the HUD**.
>
> Every path, field, line and number below was read from the code and the scene at `2fbf9b4`.
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (Enemies, Weapons, Audio, Assessment and
> Editor rows — especially the **hand-drawn inspector warning** in the Editor row, which this
> order walks straight into), the ground rules in [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md),
> and the STATUS blocks of [HUD_AND_BARREL_TASKS.md](HUD_AND_BARREL_TASKS.md) and
> [GRENADE_TASKS.md](GRENADE_TASKS.md), whose agent shape this copies: **only the integrator
> drives the editor**, scene and prefab edits are menu items, the owner saves. ⏸ marks an
> owner checkpoint.

## What the survey found — read this before assigning anything

| Fact | Where it is true at `2fbf9b4` |
|---|---|
| **Five zombie prefabs, one animator.** | `Assets/Inbox/Prefabs/Enemies/{Yokai,Butcher,Demon,Choul,Boss - Rock Golem}.prefab`. All share `Mixamo Animation Controller.controller` with states **Walk, Attack, Dying** and parameters `Speed`, `IsMoving`, `IsAttacking`, trigger `Attack`. **There is no scream, spit, lunge or block animation and none is to be made** — roles are carried by movement, timing, VFX and audio, which is the subject of this course. The prefab file is `Choul.prefab`; its `npcName` is "Ghoul" and its audio profile key is **`Choul`** / `EnemyId` 4 — spell it the way the profile does. |
| **Authored stats** (ZombieAI) | Yokai hp 100 speed 3 dmg 10 xp 75 (profile `Yokai`/2). Butcher hp 100 speed 3.5 dmg 15 xp 105 (`Butcher`/1). Demon hp 100 speed 4 dmg 15 xp 135 (`Demon`/3). Ghoul hp 150 **speed 7** dmg 15 xp 225 (`Choul`/4). Boss hp 1000 speed 4 dmg 30 range 4 xp 1500 (`Rock Golem`/5). All: attackRange 2, attackCooldown 1, `NavMeshAgent` speed 1.5 on the prefab but **overwritten every chase tick** by `agent.speed = moveSpeed * speedMultiplier * phaseSpeedScale` (`ZombieAI.cs:265`). |
| **The layering pattern** | `BossZombieController` (`[RequireComponent(typeof(ZombieAI))]`, `[DisallowMultipleComponent]`) sits beside `ZombieAI` and never duplicates movement, melee or death. Its slam "borrows control briefly (ZombieAI disabled, agent stopped) and returns it". Its clips are **serialized `AudioClip` fields on the controller**, which the report scanner picks up unaided. Every role here is the same shape. |
| **What a role can call on `ZombieAI`** | `SetSpeedMultiplier(float)` (:698 — **WaveManager owns this one**, per wave, `WaveManager.cs:582`; a role must not touch it), `SetPhaseSpeedScale(float)` (:704, floored at 0.1 — the boss's phase hook; the boss is the only user, so the four roles may use it for their own bursts), `CancelAttack()` (:395), `IsHeadPoint(Vector3)` (:503), `HealthFraction`, `IsDead()`, `XPReward`, event `OnZombieDeath`. `detectionRange` is 0 on every prefab, which `ZombieAI.cs:251` reads as **always chasing** — a screamer's "rally" is therefore a speed burst, not a wake-up. |
| **The damage path** | Every weapon → `DamageableUtil.Damage(target, amount, hitPoint, hitNormal)` → `ZombieAI.TakeDamage(float, Vector3)` (:518): head band (`IsHeadPoint`: top `headHeightFraction` = 0.25 of the body collider) pays `headshotMultiplier` (2) and plays the profile's **`headshotSounds`** on the zombie; then `TakeDamage(float)` (:536) → `Die()` (:549) → `PlayerStats.Instance.AddXP(xpReward)` → `OnZombieDeath` → `ZombieManager.UnregisterZombie`. **There is no hit event** anyone else can listen to, and no hook to modify damage before it lands — both are added in Phase 0. |
| **Enemy audio surface** | `EnemyAudioManager.EnemySoundProfile`: `enemyName`, `enemyType` (`EnemyType`, `Boss` is what `LevelFeatureTools.FindBossPrefab` keys on), `enemyPrefab`, `vocalisationSounds` (nested, looped by `VocalizationLoop` :230), `attackSounds`, `deathSounds`, `headshotSounds`, volumes, mixer groups. Play methods: `PlayAttackSound / PlayDeathSound / PlayHeadshotSound(string enemyName, AudioSource)`. Five profiles: Butcher, Yokai, Demon, Choul, Rock Golem. ⚠ **`EnemyAudioManagerEditor.cs` is hand-drawn** (`PropertyField` per field, :68–100) and `EnemySoundProfile` has **no `IAudioSlotFilter`** — a new slot is three places: the field, the inspector line, the filter that hides it on profiles whose enemy has no such role. |
| **The kick** | `WeaponController.ApplyRecoil` (:362): per-weapon serialized `m_ShakeDuration` (0.2), `m_ShakeMagnitude` (0.05), `m_ShakeRoughness` (10), `m_ShakeDirection`, then **two hard-coded branches** (:392–401): `isRevolver` × 1.5 magnitude / × 1.2 duration, `isSMG` × 0.7 / × 0.8, then `isAiming` × `aimRecoilMultiplier`, then `CameraShakeController.Instance.ShakeCamera(duration, magnitude, roughness, direction)`. The minigun, crossbow, axe and grenade get the bare defaults. `MinigunController.FireOneRound` calls `ApplyRecoil()` twenty times a second. |
| **The reticle** | `ReticleController` (`UI/`): `static Instance`, `static Show(bool)`, `static OnWeaponFired()` (spread), `reticleAnimator`, `reticleObject` (the root of the crosshair graphics), `isVisible`. **No hit state.** |
| **UI audio** | `UIAudioManager` (public `AudioClip` fields + `Play…()` per slot; `PlayOneShot(clip, volume)`; slots today: menu/button toggles, objectiveCompleted, allEnemiesCleared, itemPickup, itemDrop, fishEaten, foodCanEaten, medicineUsed, dayNumberSound, levelUpSound). **No hit or headshot slot.** Its inspector is hand-drawn too (`UIAudioManagerEditor.cs` — check before assuming; if it draws every field explicitly, the two new slots need lines). |
| **The XP toast** | `PlayerStats.AddXP(float)` → `ShowXPGainAnimation` instantiates `HUD_XPLog.prefab` under `xpGainParent` (`HUD/ScreenSpace/Bottom`), text `+N XP`, `FitXpStripToText`, destroyed after `xpGainAnimationDuration`. Anchored (0.5, 0) at y 438 - it always rises from the bottom. |
| **Player damage with a source** | `PlayerStats.TakeDamage(float, Transform attacker)` aims `DamageDirectionIndicator` at the attacker; `TakeDamage(float)` does not. Zombies use the former (`ZombieAI.cs:427`). A projectile must too. |
| **Projectiles** | `Arrow` (`Weapons/`): Rigidbody, `OnCollisionEnter` → `DamageableUtil` → `ReturnToPool()`, pooled by `ArrowPool` on `Core.Pooling.ObjectPool<T>(prefab, container, capacity)` with `Get()` / `Return(obj)`. The spit copies this shape with the roles reversed. |
| **VFX in the pack for free** | `FX_Impact_Metal_01` (sparks - the shield's clang), `FX_ShardMagic_Explosion_01` and `FX_Impact_Water_01` (a spit landing), `FX_Dust_Small_01` (a runner's lunge). All under `Assets/Inbox/Low Poly Models/Nature/Particles/Prefabs/`. `DamageDustManager.Puff` already exists for a burst of coloured dust at a point if a prefab is more than is wanted. |
| **Layers and tags** | Enemy layer 9, Player layer 7; `WaveManager.SpawnZombie` (:563) `Instantiate`s the prefab and sets `tag = "Enemy"`, then `SetSpeedMultiplier` / `SetHealthMultiplier` from the wave. Roles ride along on the prefab; **WaveManager is not edited**. |
| **The report** | 216 slots, 42 missing, at the last export. This order adds **8 assessed slots** (six enemy, two UI) → **224**. The Ctrl+G export diff before/after must be additions only. |

## Invariants

- **Nothing that plays today changes.** Existing clip slots, their fields and their playback
  paths are untouched; the export diff shows only new rows. Audio-state bugs outrank everything.
- **New slots live where the students already look:** enemy role sounds on the enemy's profile
  in `EnemyAudioManager` (the owner's wind-chime ruling: every slot a student fills is in one
  inspector), the two feedback sounds on `UIAudioManager`. Not on the role components.
- **The three-places rule** for every new slot: field, hand-drawn inspector line, filter.
  Verified by the report showing the slot — on the right profiles only — before the agent
  reports done.
- `ZombieAI`, `WaveManager`, `ZombieManager` semantics unchanged: registration, `OnZombieDeath`,
  the music-intensity climb, XP granted exactly once in `Die()`, wave multipliers applied by
  WaveManager alone.
- Singletons null-checked **at the dereference**; no `DontDestroyOnLoad`; Legacy Input only;
  no `.asmdef`; no `.meta` edits by hand; CRLF preserved (`file` before/after); run
  `bash Documentation/tools/compile-check.sh` after every edit.
- Scene changes through `[MenuItem]` tools, Undo-recorded, **the owner saves**. Prefab and
  asset writes are not undoable: the tool says so in its log, `git checkout --` is the revert.

## Phase 0 — the integrator's seams (before any agent starts)

Three small edits to shared files, so that no agent has to touch a file another agent owns.
Compile-checked and committed alone.

**`ZombieAI.cs`** — two hooks, both additive:

```csharp
/// <summary>What one hit did. Raised from TakeDamage(float, Vector3) for every hit that
/// lands, after the damage is applied; killed is true on the hit that emptied the bar.</summary>
public readonly struct ZombieHit
{
    public readonly ZombieAI Zombie; public readonly Vector3 Point; public readonly bool Headshot;
    public readonly float Damage; public readonly bool Killed; public readonly float XpReward;
    ...
}
public static event System.Action<ZombieHit> OnZombieHit;

/// <summary>A component on the same zombie that has a say in incoming damage - the shield.
/// Asked once per hit, before the headshot multiplier is applied; return the damage to land.</summary>
public interface IZombieDamageModifier { float Modify(float damage, Vector3 hitPoint, bool headshot); }
```

`TakeDamage(float, Vector3)` becomes: compute `headshot = IsHeadPoint(hitPoint)`; query
`TryGetComponent<IZombieDamageModifier>` and let it modify `damage`; apply the headshot
multiplier and sound exactly as today; apply the damage; then raise `OnZombieHit` with
`Killed = isDying` read *after* it lands.

> **CORRECTED 2026-09-07 during Phase 0, by reading `DamageableUtil` rather than trusting this
> document.** The draft said the flat `TakeDamage(float)` was the blast path and should also
> raise the event. Both halves were wrong. `DamageableUtil.Damage` routes **every** ZombieAI
> hit to the positional overload — bullets, arrows, the axe *and the grenade's blast*, which
> passes the point its line-of-sight ray struck. So: **a blast IS offered to the modifier**,
> and a shield blocks it like anything else from its front arc (flank it, or aim at the head);
> and the flat overload raises **no** event, because its only remaining caller on a zombie is
> `BossZombieController` collapsing its own summons with `float.MaxValue` — reporting those
> would flash the player's hitmarker for a dozen kills they did not make. `Die()` unchanged.

**`PlayerStats.cs`** — one overload, a stub for Agent D to fill: `public void AddXP(float
amount, Vector3 worldPoint) => AddXP(amount);` with a `TODO(D)` comment. `ZombieAI.Die`
switches to `AddXP(xpReward, transform.position + Vector3.up * 1.6f)`. XP still granted once.

**`Enemies/Roles/ZombieRole.cs`** — the abstract base every role extends (~60 lines):
`[RequireComponent(typeof(ZombieAI))]`; caches `ZombieAI`, `NavMeshAgent`, the zombie's
`AudioSource` (the one `ZombieAI` vocalises on — find it the way `ZombieAI.Start` does),
`EnemyAudioManager` (`FindFirstObjectByType`, null-checked at use), `Transform Player`
(tag `Player`); `protected string AudioKey` = the zombie's profile key (read it through a
new one-line `public string AudioProfileKey => ...` exposed on `ZombieAI` in this same
phase - it is private today); `protected bool CanAct` = alive, `ZombieAI.enabled`, agent on
the NavMesh, player present; `protected bool HasLineOfSight(float maxDistance)` (a raycast
from eye height that ignores the Enemy layer); `protected void Say(RoleSound which)` →
`enemyAudioManager.PlayRoleSound(AudioKey, which, source)` (Agent E's method) with an
`AudioEventLog.Record` so the Ctrl+J monitor shows it. `OnDisable` releases anything the role
borrowed (phase scale back to 1, agent un-stopped) — the boss's rule: **every path that
borrows control returns it.**

Phase 0 ends with a green compile and the report unchanged at 216.

## Agent plan

```
Phase 0  Integrator                       seams above, commit
Phase 1  E (enemy audio surface)          lands first — A, B, C compile against its enum and method
Phase 2  A · B · C · D · F in parallel    disjoint files; each compile-checks with E present
Phase 3  Integrator                       land A, B, C, D, F in that order → tools → play checks
                                          → ⏸ owner saves → export diff → docs → commits
```

| Agent | Owns (creates or edits) — nobody else opens these | Reads | Editor? |
|---|---|---|---|
| **E** — audio surface | `Audio/EnemyAudioManager.cs`, `Editor/EnemyAudioManagerEditor.cs` | the scanner, `AudioUtil` | no |
| **A** — runner + screamer | `Enemies/Roles/ZombieRunner.cs`, `Enemies/Roles/ZombieScreamer.cs` (new) | `ZombieRole`, `ZombieAI`, `BossZombieController` for shape | no |
| **B** — shield | `Enemies/Roles/ZombieShield.cs` (new) | as A | no |
| **C** — spitter | `Enemies/Roles/ZombieSpitter.cs`, `Enemies/Roles/ZombieSpit.cs`, `Enemies/Roles/ZombieSpitPool.cs` (new) | `Arrow`, `ArrowPool`, `Grenade` (splash + LOS), `PlayerStats` | no |
| **D** — combat feedback | `UI/CombatFeedback.cs` (new), `UI/ReticleController.cs`, `Audio/UIAudioManager.cs`, `Editor/UIAudioManagerEditor.cs` (if hand-drawn), `Player/PlayerStats.cs` (the overload body only), `Weapons/WeaponController.cs` (the shake block only) | `ZombieAI.OnZombieHit`, `CameraShakeController` | no |
| **F** — setup tools | `Editor/EnemyRoleSetupTools.cs`, `Editor/CombatFeedbackSetupTools.cs` (new) | every field name the others declare, fixed below | **no** — writes the tool, the integrator runs it |

Field, enum and method names are fixed in this document so that agents compile against each
other without meeting. If an agent needs to deviate, it says so in its report and the
integrator reconciles before landing.

## Agent E — the six enemy slots, three places each

On `EnemySoundProfile`:

```csharp
public enum EnemyRole { None, Runner, Shield, Spitter, Screamer }
public enum RoleSound { Lunge, Block, SpitWindup, Spit, SpitImpact, Scream }

[Header("Role")]
[Tooltip("Which role's slots this enemy shows. None hides all six; the boss is None.")]
public EnemyRole enemyRole = EnemyRole.None;
[Header("Role Sounds")]
public AudioClip[] lungeSounds;      // Runner: the growl before the sprint burst - the tell
public AudioClip[] blockSounds;      // Shield: the clang of a round taken on the front
public AudioClip[] spitWindupSounds; // Spitter: the retch before the spit - the tell
public AudioClip[] spitSounds;       // Spitter: the launch
public AudioClip[] spitImpactSounds; // Spitter: the splash where it lands (3D, at the impact)
public AudioClip[] screamSounds;     // Screamer: the call that rallies the pack - the tell
[Range(0f, 1f)] public float roleVolume = 1f;
```

`IAudioSlotFilter` on the profile: role fields are relevant only when `enemyRole` owns them
(`lungeSounds` ↔ Runner, `blockSounds` ↔ Shield, the three spit slots ↔ Spitter,
`screamSounds` ↔ Screamer); everything else unchanged. Model it on
`WeaponSoundProfile.IsAudioSlotRelevant` and say in the remark why a role's slot is hidden on
the other profiles: a student is not marked on a sound the enemy cannot make.

`public void PlayRoleSound(string enemyName, RoleSound which, AudioSource source)` and
`public void PlayRoleSoundAt(string enemyName, RoleSound which, Vector3 position)` (the spit
impact lands away from the zombie — a detached, spatialised one-shot the way
`WeaponAudioManager.PlayExplosionSound` does it). Both through `AudioUtil.PlayRandomOneShot`
with a per-slot no-repeat index, both recorded to `AudioEventLog`.

The inspector: six `PropertyField` lines under a "Role" header, the `enemyRole` field above
them, and a `HelpBox` per role saying what each slot is the tell for. Labels in the scanner's
override table if the default stripping reads badly (`spitWindupSounds` → "Spit Wind-up").

Report check before reporting done: profiles with `enemyRole = None` show no new rows; a
profile switched to `Spitter` shows exactly three. The integrator sets the roles with F's tool;
E only proves the filter.

## Agent A — the runner and the screamer

**`ZombieRunner : ZombieRole`** (the Ghoul). The Ghoul's authored speed 7 becomes a *burst*,
not a walk: F's tool drops `moveSpeed` to 4.5. Every `lungeInterval` (6 s, jittered ±1.5)
while chasing, within `lungeRange` (14 m) with line of sight: `Say(Lunge)` and stop for
`tellSeconds` (0.5 — the growl IS the warning), then `SetPhaseSpeedScale(lungeSpeedScale)`
(2.2) for `lungeSeconds` (2.0), a `FX_Dust_Small_01` puff at the feet on launch, then scale
back to 1 and a `recoverSeconds` (0.8) stop — the pant. Never during `ZombieAI`'s own attack
(read `IsAttacking` through a one-line getter Phase 0 adds if none exists; `CancelAttack` is
for interrupting, not for asking). Fields: the five numbers above, `dustPuff` prefab, all
tooltipped with why.

**`ZombieScreamer : ZombieRole`** (the Yokai). On first line of sight within `screamRange`
(18 m) and then every `screamInterval` (12 s): borrow control the boss's way (`ZombieAI.enabled
= false`, agent stopped, face the player), `Say(Scream)` and hold for `screamSeconds` (1.4),
return control, then **rally**: every `GameObject` tagged `Enemy` within `rallyRadius` (20 m)
that has a `ZombieAI`, is not dead, and has **no `BossZombieController`** (the boss owns its
own phase scale) gets `SetPhaseSpeedScale(rallySpeedScale)` (1.5) for `rallySeconds` (5), then
back to 1 — including the screamer itself. Restore through one coroutine per rallied zombie
that also bails if the zombie dies, so a corpse is never left holding a scale. Optional
`rallyRing` VFX at the screamer's feet. A screamer that is rallied by another screamer does not
scream in reply for `screamInterval` — a pack of Yokai must not become a siren.

Both roles: `OnDisable` returns everything (the base already does); a role on a zombie whose
profile has `enemyRole = None` still runs and is simply silent (the manager returns on an
empty array) — behaviour never depends on a clip being authored.

## Agent B — the shield

**`ZombieShield : ZombieRole, IZombieDamageModifier`** (the Butcher).
`Modify(damage, hitPoint, headshot)`: if `headshot`, return `damage` (the owner's rule -
headshots bypass). Otherwise `Vector3 toHit = hitPoint - transform.position; toHit.y = 0;`
if `Vector3.Angle(transform.forward, toHit) <= frontArcDegrees / 2` (arc 120), the hit is on
the shield: play `Say(Block)`, spawn `blockSparks` (`FX_Impact_Metal_01`) at `hitPoint` facing
back along `toHit`, and return `damage * frontDamageFraction` (0.3). Else return `damage`.
⚠ **A grenade's blast DOES reach this modifier** and is blocked like any other hit from the
front arc - see the correction under Phase 0. There is no special case for it, and this class's
header should say why there is not: a shield covers an arc, and a blast arrives from a
direction.

F's tool retunes the Butcher: hp 100 → 150, moveSpeed 3.5 → 3.0 (a heavy walks like one).
Optional and flagged only: parent a pack shield prop to the left hand. Not this order.

Check: from the front, 10 damage lands as 3.0 (`HealthFraction` 1.0 → 0.98); from behind as
10; a head hit from the front as 20. Numbers, not impressions.

## Agent C — the spitter

**`ZombieSpitter : ZombieRole`** (the Demon). Keeps its distance: while the player is between
`preferredMin` (7 m) and `spitRange` (16 m) with line of sight and the spit is off cooldown
(`spitInterval` 4 s), it stops (agent stopped), faces the player, `Say(SpitWindup)` for
`windupSeconds` (0.8 — the tell), then fires. Closer than `preferredMin` it lets `ZombieAI`
close and bite as today (a cornered spitter is an ordinary zombie); further than `spitRange`
it walks in. Control borrowed the boss's way and returned in every path including death.

**`ZombieSpit`** — the projectile, `Arrow` mirrored: Rigidbody (useGravity), a small trigger-
less sphere collider on the **Default** layer, launched with the velocity that lobs it onto
the player's *current* position with `arcHeight` (2.5 m) — solve the parabola, do not tune a
force; the player who keeps moving is the one who dodges. `OnCollisionEnter` anywhere: splash
`splashRadius` (1.6 m): if the player root is within it, `PlayerStats.Instance.TakeDamage
(damage, spitterTransform)` so the hit smear points at the Demon; `PlayRoleSoundAt(SpitImpact,
point)`; `impactFx` (`FX_ShardMagic_Explosion_01`); `ReturnToPool()`. A `lifetime` (6 s)
return for a spit that lands in the river. Damage 12.

**`ZombieSpitPool`** — one per scene, made by F's tool under `Game Management`, wrapping
`ObjectPool<ZombieSpit>` exactly as `ArrowPool` does; `static Instance`, null-checked by the
spitter at the dereference (a missing pool logs once and the Demon reverts to biting).

Check: a Demon 12 m from a stationary player lands a spit within `splashRadius` of the player's
position at launch (measure the landing point); the player's health drops by 12; the direction
indicator aims at the Demon; a spit fired at a player who then moves 3 m misses.

## Agent D — combat feedback

**`CombatFeedback`** (`UI/`, scene-lifetime, added by F's tool under `Game Management`):
subscribes to `ZombieAI.OnZombieHit` in `OnEnable`, unsubscribes in `OnDisable`.

- **Hitmarker.** A uGUI `Image` child of `ReticleController.reticleObject` (F's tool adds it
  from the pack's `HUD_Reticle_CrosshairCircle` parts — four ticks, or the `SPR_HUD_DialReticle_
  Sml_Grungy` sprite — white, alpha 0), that `CombatFeedback` flashes on every hit: alpha to 1,
  scale 1.25 → 1, over `hitFlashSeconds` (0.12). Headshots flash it **red** and larger
  (1.5). The reticle's own `Show(false)` hides it with the rest (it is a child).
  `ReticleController` gets one accessor for the marker so `CombatFeedback` never searches by
  name: `public static RectTransform HitMarker`, wired by the tool.
- **Sounds.** `UIAudioManager`: `public AudioClip hitMarker;` `public AudioClip headshotPing;`
  with `PlayHitMarker()` / `PlayHeadshotPing()` on the existing UI source (2D, at the player -
  the zombie's own `headshotSounds` stay 3D at the zombie; the ping is the *player's*
  confirmation and the two are different sounds). If `UIAudioManagerEditor.cs` draws fields by
  hand, two lines there. Two new assessed slots, in the UI Audio Manager section.
- **Kill XP at the corpse.** Fill Phase 0's `PlayerStats.AddXP(float amount, Vector3
  worldPoint)`: the same `HUD_XPLog` prefab, instantiated under `xpGainParent`, positioned at
  `Camera.main.WorldToScreenPoint(worldPoint)` converted into the parent's space
  (`RectTransformUtility.ScreenPointToLocalPointInRectangle` — the canvas is Screen Space
  Overlay, reference 3840 × 2160, match 1), clamped inside the screen with a margin, then
  driven up `popRiseReferencePx` (120) over its life by a small coroutine on the instance (the
  prefab's animator plays its In state as today). A corpse behind the camera (`z < 0`) falls
  back to the bottom toast. The bottom toast keeps objectives and fish untouched.
- **Kick per weapon.** In `WeaponController.ApplyRecoil`, **delete the two hard-coded branches**
  (:392–401, `isRevolver` and `isSMG`) - the per-weapon `m_ShakeMagnitude / Duration /
  Roughness` fields already exist and F's tool writes the values that the branches used to
  fake, plus real ones for the weapons that never had any: Revolver 0.075 / 0.24 / 10,
  SMG 0.035 / 0.16 / 12, Minigun 0.022 / 0.08 / 16 (twenty a second - small and short or it is
  nausea), Crossbow 0.02 / 0.12 / 8, Axe 0.06 / 0.18 / 6 (the swing), Grenade 0 (its blast
  shake is its own, `Grenade.cs:345`). `isAiming` scaling stays. The `isSMG` / `isRevolver`
  flags stay for the reticle rule they still serve.

Check: each of the four events (hit, headshot, kill, kill-by-headshot) flashes the marker in
the right colour, plays the right slot, and a kill pops `+N XP` at the corpse's screen point
while the bottom toast stays quiet; the five weapons report five different shake magnitudes
through a one-line editor seam.

## Agent F — the tools that put it in the level

**`Tools/Enemies/Report Enemy Roles`** — read-only: for each of the five prefabs, its stats,
which role component it carries, its profile's `enemyRole`, and whether the spit pool and
`CombatFeedback` exist. **`Tools/Enemies/Add Enemy Roles`** — idempotent, and for every step
that writes a **prefab asset** it says ⚠ not undoable, `git checkout --` is the revert:

1. `Choul.prefab` += `ZombieRunner`; `moveSpeed` 7 → 4.5. `Butcher.prefab` += `ZombieShield`;
   `maxHealth` 100 → 150, `moveSpeed` 3.5 → 3.0. `Demon.prefab` += `ZombieSpitter`.
   `Yokai.prefab` += `ZombieScreamer`. Through `PrefabUtility.LoadPrefabContents` /
   `SaveAsPrefabAsset` / `UnloadPrefabContents` — never on an instance. The boss is not
   touched; the tool refuses if asked.
2. VFX references on each role from the pack paths in the survey table.
3. `EnemyAudioManager` profiles: `enemyRole` = Runner on `Choul`, Shield on `Butcher`, Spitter
   on `Demon`, Screamer on `Yokai`, None on `Rock Golem`. Slots left **empty** - they are the
   students'.
4. The spit projectile prefab, built and saved to `Assets/Inbox/Prefabs/Enemies/ZombieSpit.prefab`
   (a scaled `SM_Rock_Small_01` or a primitive sphere, a green URP/Lit material written beside
   it, Rigidbody, SphereCollider, `ZombieSpit`), and `Game Management/Zombie Spit Pool`.

**`Tools/HUD/Add Combat Feedback`** — `Game Management/Combat Feedback` with `CombatFeedback`;
the hitmarker image under the reticle, wired to `ReticleController.HitMarker`; the per-weapon
shake values from Agent D's table written onto the five `WeaponController`s in the scene
(`SerializedObject`, Undo-recorded). Report first, then apply, both logging every number.

## Integrator — sequence and verification

1. Phase 0 as written. Compile. Ctrl+G export → keep as the baseline (216 slots).
2. Land **E**. Compile. Report: no new rows yet (every profile is `None`).
3. Land **A, B, C, D, F** in that order, compile-checking after each. Run `Report Enemy Roles`,
   then `Add Enemy Roles`, then `Add Combat Feedback`, then both reports again; keep the logs.
4. Report: **224 slots**, the eight new ones MISSING, on the right profiles only (Runner slot on
   Choul, three on Demon, and so on). Export → `diff` against the baseline is **additions only**.
5. Play checks through the CLI, one prefab at a time, spawned 12 m from the player by a small
   editor-only probe (the shape of `Testing/MinigunAudioProbe` - a coroutine that logs every
   100 ms, because the bridge's round trip is a second): the runner's speed scale reads 2.2 for
   2.0 s after its lunge then 1; the screamer's scream sets 1.5 on a second zombie for 5 s and
   never on the boss; the shield's front / back / head numbers; the spit's landing point and
   the player's health; the hitmarker's alpha and colour on each of the four events; the corpse
   XP toast's screen position within 40 px of the corpse's projected point; five distinct shake
   magnitudes. **Numbers in the log, not impressions.** Nothing here can hear a clip - that is
   the owner's check.
6. Regression: a wave night still spawns, registers, and clears (`GetActiveZombieCount` back
   to 0); the music intensity still climbs and falls; `OnZombieDeath` still fires once per
   zombie; console clean of errors and of per-frame warnings.
7. ⏸ **Owner saves the scene** (and the prefab writes are already on disk). ⏸ **Owner plays a
   night**: does each role *read* - the growl before the sprint, the scream before the pack
   comes, the retch before the spit, the clang on the Butcher's front? Are the four tells long
   enough to act on? Is the minigun's kick tolerable for a hundred seconds?
8. Docs: `CODE_STRUCTURE.md` Enemies row (the four roles, the two `ZombieAI` hooks and the rule
   about who owns which speed scale), Audio row (six slots, the filter), UI row (`CombatFeedback`,
   the hitmarker, the corpse toast), Weapons row (the branches are gone; the kick is per weapon),
   Editor row (the three tools, the prefab-write warning), Assessment row (216 → 224); the work
   history; this file's STATUS; `README.md`'s index row.
9. Commits: Phase 0; E; A; B; C; D; F; the prefabs + spit prefab + material; the owner-saved
   scene; docs. Push only when the owner says.

## Known risks, so nobody rediscovers them

- **`SetPhaseSpeedScale` is shared.** The boss uses it for phases; the runner and the screamer
  use it for bursts. They never meet (the boss is `None` and is excluded from rallies), but two
  *roles* on one zombie would fight over it — `[DisallowMultipleComponent]` on the base and a
  Start-time check that a zombie carries at most one `ZombieRole`.
- **Borrowed control must be returned on death.** `ZombieAI.Die` disables the agent; a role's
  coroutine that re-enables it afterwards resurrects a corpse's pathing. Every borrow checks
  `IsDead()` before returning control, and `OnDisable` releases without re-enabling anything
  the zombie has switched off itself.
- **The animator has no idle.** A stopped zombie plays `Walk` at `Speed` 0 or the last frame;
  set `IsMoving` false while stopped, as `ZombieAI` does at :243. It reads as standing.
- **Prefab writes are asset writes.** Five prefabs and one new one. The tool logs the diff it
  is about to make and the revert command.
- **Hand-drawn inspectors, twice over.** `EnemyAudioManagerEditor` and possibly
  `UIAudioManagerEditor`. The grenade shipped for an hour with an empty section; the reload
  slots were unfillable for months. The report is the proof, not the code.

## STATUS

**Done and verified 2026-09-07.** Six agents in parallel under one integrator, ten commits,
both tools run against the live editor and every claim measured rather than asserted. The
report went **218 -> 224** and the export diff is six added rows and nothing else changed.

### Measured, not asserted

| Check | Result |
|---|---|
| Shield, 10 damage | **3.00** from the front, **10.00** from behind, **20.00** on the head. Exactly the design. |
| Runner's burst | Phase speed scale peaks **2.20**, holds **2.06 s**, returns to **1.00**. |
| Screamer's rally | Neighbour **1.50**, **boss 1.00** (excluded, as designed), screamer itself 1.50; all settle to 1.00. |
| Spitter | Spit lands **1.67 m** from where the player stood at launch, for **exactly 12.0** damage, once. |
| Hitmarker | Hit: white, alpha **1.00**, scale **1.25**. Headshot: red (1.00, 0.25, 0.20), scale **1.50**. Settles to alpha 0. |
| Corpse XP toast | Corpse projected to screen (1468, 336); toast centre (1468, **459**) - x within a pixel, y the deliberate upward drift. |
| Per-weapon kick | Six weapons, six different shakes: SMG 0.035, Grenade 0, Revolver 0.075, Axe 0.06, CrossBow 0.02, Minigun 0.022. |
| Report | 224 slots, 174 implemented, 50 missing. Six new MISSING rows on the right profiles; **the boss gained none**. |
| Console | Zero errors across every play run. |

⏸ **What still needs the owner**, because no probe can hear anything: play a night and judge
whether the four tells are long enough to act on, and whether the SMG's kick is right - it was
authored at magnitude **0.28** and is now **0.035**, so its old effective kick (0.28 x the
deleted 0.7 branch = 0.196) was five times what it is now. That is a real change in feel, not
just writing down what the branch faked, and it is the one number here nobody has judged by ear.

### What the order got wrong, and who caught it### What the order got wrong, and who caught it

| Claim | What is actually true |
|---|---|
| "Re-check `CanAct` after every yield" | **`CanAct` is false for the whole of any borrowed behaviour** - `TakeControl()` disables `ZombieAI` and `CanAct` requires it enabled. Taken literally, every tell would abandon itself on its first frame and no role would ever fire. Agents A and C found it independently; the base now carries `StillHolding`. |
| "The flat `TakeDamage(float)` is the blast path; a shield never sees a blast" | **`DamageableUtil` routes every zombie hit to the POSITIONAL overload**, blast included. So a blast is offered to the modifier and is blocked from the front arc like anything else; and the flat path raises no hit event, because its only remaining caller is the boss collapsing its own summons - which would have flashed the player's hitmarker for kills they did not make. |
| "A profile switched to Spitter shows three new rows" | Only after its arrays are **sized to 1**. `AudioProgressScanner` records one row per array ELEMENT, so an empty `AudioClip[]` shows nothing - which is why `headshotSounds: []` on all five profiles has never once appeared despite being played on every head hit. Agent E found it; Agent F's tool sizes the six. |
| "`ZombieRole` caches the AudioSource in `Awake`" (implied by the base's design) | `ZombieAI.Start` is what **adds** a source to a prefab lacking one, and every Awake precedes every Start. A role on such a prefab would have been permanently, silently mute. Resolved on demand now. |
| "the five `WeaponController`s" | **Six**, all authored with ids 1-6. And the crossbow's GameObject is named `CrossBow` while its `weaponName` is `Crossbow`, so the tool finds weapons by type and id - a path would have silently missed it. |

Agent E also fixed a **fourth** instance of this project's oldest trap, in the file it was
editing: `headshotSounds` had no line at all in the hand-drawn enemy inspector - played on
every head hit, carrying a report label, and impossible for a student to fill in.

### Already done by the integrator

The project was opened, both reports run, `Add Enemy Roles` and `Add Combat Feedback` applied,
the scene saved, and the probe run against all four roled prefabs. The prefab and asset writes
are committed, so `git checkout --` is still the way back from any of them.

### Known open questions, none blocking

- The Demon crosses its 16 m -> 7 m spit window in about 2.3 s at `moveSpeed` 4, so a Demon
  walking at a stationary player gets roughly **one** spit before it is inside `preferredMin`
  and biting. If it should read as a ranged enemy more of the time, the knobs are
  `preferredMin` up or the Demon's `moveSpeed` down. A decision, not a defect.
- `rallyRing` is left empty: the pack has no ring-shaped one-shot, and the scream is carried
  by sound, which is the point.
- Three of the four pack VFX are **looping** systems (`FX_Impact_Metal_01`, `FX_Dust_Small_01`,
  `FX_ShardMagic_Explosion_01`), so their lifetime field is how long they spray.
  `BulletImpactManager` already lives with this; the spit uses `FX_Impact_Water_01`, the only
  non-looping impact in the pack.
- Not fixed, out of scope, worth a ticket: the enemy manager's no-repeat memory for attacks,
  deaths and headshots is a **local** variable reset on every call, so it has never worked.
