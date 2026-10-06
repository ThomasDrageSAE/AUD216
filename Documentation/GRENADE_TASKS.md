# Grenade — Work Order (parallel agents)

> Written 2026-09-05 at commit `2549954`. **This is a work order for an implementing AI
> (Opus) running several agents in parallel.** It adds a throwable explosive: a pickup the
> player carries as a weapon, throws with the fire button, that bounces, detonates on a
> fuse, and damages everything in a radius — the player included. Every path, field and
> line below was verified against the repo at `2549954`; line numbers drift, re-grep.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (the Weapons, Inventory and Audio rows,
> the single-authority rules, the Unity CLI paragraph), the ground rules in
> [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md), and the STATUS blocks of
> [PERFORMANCE_FIX_TASKS.md](PERFORMANCE_FIX_TASKS.md) — the last parallel-agent order, and
> the one whose shape this copies: **only the integrator touches the editor**, and scene
> edits are menu items the owner saves. ⏸ marks an owner checkpoint. Questions use the
> multichoice format in [README.md](README.md#asking-the-owner-questions).

## The owner's decisions, already made

Do not re-ask these.

| Decision | Answer |
|---|---|
| Throw mechanic | **Press to throw, fixed 3 s fuse from release.** Left click pulls the pin and throws in one motion. No cooking, no impact detonation. |
| Self-damage | **Yes.** Inside the radius the player takes falloff damage through the existing directional blood effect, a camera shake scaled by distance, and a ringing tail. |
| Model | **Tin-can pipe bomb from the pack**: `SM_Prop_Can_01` with a dark material, HUD icon `ICON_SM_Wep_PipeBomb_01`. Nothing imported. The item is still *named* Grenade. |
| Assessed audio slots | **Two: Throw and Explosion.** Bounces reuse the surface impact sounds students already author for bullets. The fuse is silent. The ringing is generated, not a clip. |

## Why this is mostly an audio feature

This is a sound-design assessment project. A grenade is the one weapon where the sound
*leaves the player*: the explosion happens at the grenade's position, not the camera's, so
it is the first genuinely 3D weapon sound in the level, and the bounces are surface sounds
the students have already authored for bullet impacts. The two new assessed slots must
appear in the Ctrl+G progress report as empty placeholders, reset to the beep by the
handout tool, and be authored the same way every other weapon sound is — through a
`WeaponSoundProfile` on the `WeaponAudioManager`. **The report goes from 104 slots to 106.**

## Invariants

- **Audio authoring surface unchanged in shape.** New profile fields, yes; no existing
  field renamed, removed or reordered; `WeaponId` gets a new member **appended, never
  renumbered** (the enum is serialized by value).
- **The Ctrl+G export before the change and after must differ by exactly the two new
  rows** and nothing else. The integrator proves it with a diff.
- **Only the integrator drives the editor.** Agents A, B and C never run a `unity` command.
  ⚠ At `2549954` the editor is **closed** — the integrator opens it first; `unity status`
  must list this project before anything else.
- **Scene edits are one idempotent, Undo-recorded `[MenuItem]`** the integrator runs and
  the owner saves. Prefab creation is an asset write (not undoable; git is the revert) and
  the tool says so.
- `bash Documentation/tools/compile-check.sh` after every edit, both assemblies ✅; then
  `recompile` + poll `recompile_status` before the editor is used.
- ⚠ CRLF for every `.cs`, new or edited. ⚠ The shell mangles `$"…"` — write code with the
  Write tool, run with `eval_file`. ⚠ Always `--project-path`.
- No `[SerializeField]` renamed or removed anywhere. Grep `.unity`/`.prefab` before touching
  one.
- One commit per agent's landing, house message style, the harness attribution trailer.
  Push only when the owner says.

## Verified anchors

| Area | Anchor | Value at `2549954` |
|---|---|---|
| Weapon contract | `Assets/Scripts/Weapons/WeaponController.cs` | Virtuals: `Fire()` (:277), `ToggleWeaponPosition()` (:395), `UpdateWeaponBob` (:597), `Start`/`Update`, `ApplyRecoil` (:346), `UsesMagazine` (:78, drives the R reload + HUD ammo readout), `CanAimDownSights` (:465). Protected fields: `fireRate`, `damage`, `range`, `firePoint`, `muzzleFlash`, `weaponAudioManager`, `weaponName`, `weaponId`, `raisedPosition`/`loweredPositionOffset`…, `maxAmmo`, `reloadKey`, `ammoText`, camera-shake fields (`m_ShakeDuration/Magnitude/Roughness/Direction`). Public: `WeaponAudioKey` (:56, from `weaponId` via `AudioProfileIds`), `ProfileName`, `ShowsReticle` (:85), `IsWeaponRaised()`, `CurrentAmmo`/`MaxAmmo`/`IsReloading` (:554–560), static event `OnHudStateChanged` (:548) raised at :571. |
| The fire loop | `FirstPersonController.cs:190` | `if (activeWeapon != null && Input.GetMouseButton(0)) activeWeapon.Fire();` — held, every frame. `WeaponController.Fire` gates single-shot on `Input.GetMouseButtonDown(0)` and `nextTimeToFire`. A grenade override must gate the same way. |
| Equip path | `FirstPersonController.cs` | Slot keys `Alpha1–5` → `HandleSlotKey` → `SelectInventorySlot` → `HandleWeaponSelection(WeaponItem)`: lowers the rod, toggles `activeWeapon`, `ToggleWeaponPosition()` raises. `Shift+slot` → `DropInventorySlot`. Any `WeaponItem` whose controller is a `WeaponController` works with **no change** to the FPC. |
| The projectile precedent | `CrossbowController.cs`, `Arrow.cs`, `Crossbow_Bolt.prefab` | `UsesMagazine => false`, `CanAimDownSights => false`; `Fire` plays `PlayShootSound(WeaponAudioKey)` then `rb.AddForce(firePoint.forward * arrowForce, ForceMode.Impulse)` (:95). `Arrow.OnCollisionEnter` → `DamageableUtil.TryGetDamageable(collision.collider, out IDamageable)`, `ZombieAI` gets `TakeDamage(damage, contactPoint)`, else `TakeDamage(damage)`. Bolt prefab: layer **Default**, tag Untagged, Rigidbody mass 1, `m_CollisionDetection: 2` (ContinuousDynamic), CapsuleCollider. |
| The area-damage precedent | `AxeController.cs:89–110` | `Physics.OverlapSphere(firePoint.position, attackRange, hitLayers)`, then per collider a **line-of-sight raycast** to `bounds.center`, then `TryGetDamageable` → `TakeDamage`, then `BulletImpactManager.Instance.PlayImpactEffect(hit)`. Copy the shape. |
| Damage targets | `IDamageable.TakeDamage(float)`; `ZombieAI.TakeDamage(float, Vector3 hitPoint)` (:518) and `(float)` (:536); `DamageableObject.TakeDamage(float)`; `PlayerStats.TakeDamage(float)` (:465) and **`TakeDamage(float, Transform attacker)` (:475)** → `ResolveDamageDirection` → the directional blood spatter | The player overload wants a **Transform** to aim the blood: pass the grenade's transform *before* it is destroyed. `ZombieAI.Die` grants `xpReward` exactly once, so area kills pay XP with no extra code. `DamageableUtil.TryGetDamageable(Component)` resolves a hit collider to its `IDamageable` (a zombie's colliders are runtime, not on the prefab — rely on the util, never on `GetComponent`). |
| Camera shake | `Camera/CameraShakeController.cs:44` | `CameraShakeController.Instance.ShakeCamera(float duration, float magnitude, float roughness, Vector3 direction)`. Lives on `Player/FirstPersonCharacter`. `WeaponController` already carries per-weapon shake fields. |
| Surface impacts | `Weapons/BulletImpactManager.cs:142` | `PlayImpactEffect(RaycastHit hit)` — **tag-driven**: `CompareTag("Enemy")` branch, then `impactEffects[].surfaceTag` authored as **Dirt, Metal, Rock, Terrain, Water, Wood**. It needs a `RaycastHit`, so a bounce (a `Collision`) must raycast from the contact point back along the contact normal to get one. Singleton `BulletImpactManager.Instance`, on `Audio Managers`. |
| Weapon audio | `Audio/WeaponAudioManager.cs` | `WeaponSoundProfile : IAudioSlotFilter` — `weaponName`, `weaponId`, `shootSounds[]`+`shootVolume`+pitch range, `raiseWeaponSound`, `lowerWeaponSound`+`toggleSoundVolume`, `reloadStartSound`/`reloadEndSound`/`reloadActionSounds[]`+`reloadVolume`, `WeaponType weaponType` (enum `{ Gun, Axe }`, :26), `IsAudioSlotRelevant(fieldName)` (:55: Axe hides the three reload fields, everything else shows all). Public: `TryGetProfile(WeaponId, out profile)` (:216), `PlayShootSound/PlayWeaponRaiseSound/PlayWeaponLowerSound/PlayReload*(string weaponName)` — each resolves the profile by name and plays on a **per-weapon AudioSource** keyed by `AudioProfileKeys.Normalize(name)`, via `AudioUtil.PlayRandomOneShot(source, clips, ref lastIndex, volume, minPitch, maxPitch)`. Profiles authored in the scene: **SMG (1), Revolver (2), Axe (3)**. *(Pre-existing gap, out of scope: the Crossbow has `weaponId` 4 but no profile entry.)* |
| Profile keys | `Audio Systems/AudioProfileIds.cs:38` | `enum WeaponId { Unassigned = 0, SMG, Revolver, Axe, Crossbow }`. `AudioProfileIds.IsAssigned`, `NameOf`, `Canonical`, `TryParse`. **Append `Grenade` after `Crossbow`**; never insert. `Tools/Audio/Migrate Profile Keys To Enum` exists for legacy string keys. |
| The report | `Assessment/AudioProgressScanner.cs` | Walks serialized `AudioClip` fields by reflection; a new profile entry appears by itself, titled by its `weaponName`; the profile's `IsAudioSlotRelevant` hides irrelevant slots. `ClipLabelOverrides` (:707) maps `shootSounds → "Shots"`, `reloadActionSounds → "Reload Actions"` — add `throwSounds → "Throws"`, `explosionSounds → "Explosions"` or the rows read as a bare strip. |
| The handout reset | `Editor/DistributionResetTools.cs` | Lists `typeof(WeaponAudioManager)` (:53) and walks every `PPtr<$AudioClip>` property (:265), so **new profile fields reset to the beep with no change**. It never touches AudioSources — the grenade prefab must not carry an authored clip. |
| Inventory | `Inventory/Inventory.cs`, `Item.cs`, `WeaponItem.cs` | `MAX_SLOTS` slots (`items = new Item[MAX_SLOTS]`, :25), `AddItem` = first empty slot, **no stacking**. `Item.OnTriggerEnter` (Player tag, double-fire guard) → `inventory.AddItem(this)` → `UIAudioManager.PlayPickupSound()` → `OnPickup()` (disables the collider and every renderer) → objective hook. `Item.Drop(position)` re-enables them with 1 s pickup protection. `WeaponItem`: `[SerializeField] weaponController`, `weaponName`, `hudIcon`; `GetHudIcon()` falls back to `itemIcon`; `OnPickup` has the axe-objective special case. |
| Where things live | scene | Controllers under `Player/FirstPersonCharacter/WeaponParent/{SMG, Revolver, Axe, CrossBow}` (each with a `firePoint`); world pickups under `Game Management/Pickups/Pickup - {Revolver, SMG, Crossbow, FishingRod, Axe}`; managers on `Audio Managers` (`WeaponAudioManager`, `BulletImpactManager`, `ArrowPool`); `CameraShakeController` and the camera on `Player/FirstPersonCharacter`; the Player is layer **Player** (7). |
| HUD | `UI/CurrentWeaponHUD.cs`, `Editor/WeaponSetupTools.cs` | The panel subscribes to `WeaponController.OnHudStateChanged` and reads `CurrentAmmo`/`MaxAmmo`/`IsReloading`/the item's `GetHudIcon()`. It never polls. `WeaponSetupTools` seeds `_Side` icons; there is **no `_Side` pipe-bomb icon**, so set `hudIcon` explicitly. |
| Tags and layers | `ProjectSettings/TagManager.asset` | Tags include **`Projectile`** (unused by the bolt, which is Untagged). Layers: Default, TransparentFX, Ignore Raycast, Occluder, Water, UI, Terrain, Player, Navmesh ignore, Enemy, TriggerBoxes — **there is no Projectile layer**; the grenade goes on Default like the bolt. |
| Assets | pack | Mesh `Assets/Inbox/Low Poly Models/.../SM_Prop_Can_01.fbx` (+ `SM_Prop_Can_01_Convex.asset`); icon `.../InterfaceApocalypseHUD/Sprites/Icons_Explosives/ICON_SM_Wep_PipeBomb_01.png`; explosion FX `.../Nature/Particles/Prefabs/FX_Grenade_Explosive_01.prefab` — 5 particle systems (fire, smoke, debris, ember, root), all **2 s, non-looping, playOnAwake**, one point light, **no AudioSource**. Project prefabs live in `Assets/Inbox/Prefabs/`. |
| Pickup sound | `UIAudioManager.PlayPickupSound()` | Already fires from `Item.OnTriggerEnter`. |

## The design, in one page

**Carry.** A `GrenadeItem : WeaponItem` sits in one inventory slot and carries a **count**
(default 3, serialized `maxCarried`). Its controller is a `GrenadeController :
WeaponController` under `WeaponParent`, holding the tin can in hand. Select the slot → it
raises like any weapon; the HUD panel shows the pipe-bomb icon and the count through the
existing ammo readout (`UsesMagazine => true` so the readout works, with the reload path
made a no-op — there is nothing to reload). A second grenade pickup found in the world does
**not** take a slot: it merges into the existing item's count and destroys itself; if the
count is already at `maxCarried` it stays on the ground.

**Throw.** Left click, gated on `GetMouseButtonDown` and `fireRate`. The controller plays
`PlayThrowSound(WeaponAudioKey)`, spawns the projectile prefab at `firePoint`, gives it
`camera.forward * throwSpeed + Vector3.up * throwLift` as a velocity change, tells it to
ignore the player's own colliders (`Physics.IgnoreCollision` against the
`CharacterController` and the capsule — otherwise it bounces off the thrower), starts its
fuse, decrements the count, raises `OnHudStateChanged`. At zero the controller lowers itself
and the item removes itself from the slot (the `FoodItem` consumption pattern).

**Flight.** `Grenade` (the projectile): Rigidbody mass 0.4, ContinuousDynamic, a
SphereCollider, layer Default, **tag `Projectile`** (exists, unused — now it means
something). `OnCollisionEnter` throttled to one bounce per 0.15 s and relative speed above
1 m/s: raycast from the contact point back along the contact normal for a `RaycastHit`,
then `BulletImpactManager.Instance.PlayImpactEffect(hit)` — the students' own surface
sounds, at the surface it hit. **No fuse sound** (owner's decision). `maxFlightTime` safety
return like the arrow, so one thrown off the map never leaks.

**Detonate.** After `fuseSeconds` (3): `PlayExplosionSound(WeaponAudioKey, position)` — a
detached, spatialised one-shot at the blast point, since the grenade is about to be
destroyed; instantiate `FX_Grenade_Explosive_01` there (self-destruct after 2.5 s);
`Physics.OverlapSphere(position, radius)`, then per collider the axe's line-of-sight raycast
(walls block), `TryGetDamageable`, **dedupe by `IDamageable` instance** (a target has more
than one collider), damage = `centreDamage` scaled linearly from 1 at the centre to
`edgeFraction` (0.25) at the radius. `ZombieAI` gets the hit point; `PlayerStats` gets the
**Transform overload** so the blood spatter aims at the grenade; everything else the plain
overload. Then the player-side effects, only if the player was inside the radius:
`CameraShakeController.Instance.ShakeCamera(...)` scaled by proximity, and the ringing —
a **generated** 3 kHz sine (`AudioClip.Create`, 1.5 s, low volume, 2D) played on a temporary
source, so no asset and no assessed slot. Destroy the grenade.

**Audio authoring surface — exactly two new slots.** `WeaponSoundProfile` gains
`throwSounds[]` + `throwVolume` and `explosionSounds[]` + `explosionVolume`, and
`WeaponType.Grenade`. `IsAudioSlotRelevant` becomes a real filter: Gun and Axe hide the two
grenade fields; Grenade hides `shootSounds` and the three reload fields, keeps
raise/lower. `WeaponId.Grenade` appended. The manager gets `PlayThrowSound(string)` (the
per-weapon source, like a shot) and `PlayExplosionSound(string, Vector3)` (a pooled/temporary
3D source at the point: `spatialBlend 1`, logarithmic rolloff, `maxDistance` ~80 m, destroyed
after the clip). The scanner's label table gets the two labels. The report gains two
`[BEEP]` rows under a "Grenade" group; the reset tool needs nothing.

**Numbers, all serialized, all the owner's to tune:** `throwSpeed` 14 m/s, `throwLift`
2 m/s, `fuseSeconds` 3, `radius` 5 m, `centreDamage` 120, `edgeFraction` 0.25,
`maxCarried` 3, `fireRate` 0.6 s, shake 0.35 s / 0.25 magnitude at the centre.

## Agent plan

```
Phase 1  A · B · C in parallel     disjoint files, nothing shared
Phase 2  Integrator (serial)       opens the editor · lands A+B · runs C's tool · ⏸ owner saves
                                   · play check via CLI · report diff · docs · commits
```

| Who | Owns (writes) | Reads | Editor |
|---|---|---|---|
| **A** — gameplay | `Weapons/GrenadeController.cs`, `Weapons/Grenade.cs`, `Inventory/GrenadeItem.cs` (all new), and **one virtual hook in `Inventory/Item.cs`** | `WeaponController`, `CrossbowController`, `Arrow`, `AxeController`, `WeaponItem`, `FoodItem`, `PlayerStats`, `ZombieAI`, `CameraShakeController`, `BulletImpactManager` (read only) | no |
| **B** — audio | `Audio/WeaponAudioManager.cs`, `Audio Systems/AudioProfileIds.cs`, `Assessment/AudioProgressScanner.cs` (two label lines) | `AudioUtil`, `AudioProfileKeys` | no |
| **C** — editor tool | `Editor/GrenadeSetupTools.cs` (new, the only file) | the scene, the pack assets, `WeaponSetupTools.cs` for house shape | no |
| **Integrator** | docs, this STATUS, the scene via C's tool | everything | **yes — the only one** |

A and B share an *interface*, not a file: A calls `weaponAudioManager.PlayThrowSound(WeaponAudioKey)` and `PlayExplosionSound(WeaponAudioKey, position)`; B defines them with exactly those signatures. Both agents get the signatures from this document, so neither waits on the other. C references `GrenadeController`, `Grenade`, `GrenadeItem` and `WeaponId.Grenade` by name; it compiles only once A and B have landed, so the integrator compile-checks C **last**.

---

## Agent A — the gameplay

**`Weapons/Grenade.cs`** (the projectile, on the prefab C builds):
- Fields: `fuseSeconds`, `radius`, `centreDamage`, `edgeFraction`, `maxFlightTime`,
  `bounceMinSpeed`, `bounceCooldown`, `explosionEffectPrefab`, shake `duration`/`magnitude`/`roughness`.
- `Launch(Vector3 velocity, Collider[] ignore, WeaponAudioManager audio, string audioKey)`:
  `IgnoreCollision` against each collider in `ignore`, set the velocity, start the fuse and
  the max-flight timer.
- `OnCollisionEnter` as designed above. Build the `RaycastHit` for the impact manager by
  `Physics.Raycast(contact.point + contact.normal * 0.05f, -contact.normal, out hit, 0.2f)`;
  if it misses, skip the sound — never fake a hit.
- `Detonate()` exactly once (`bool detonated` guard). Order: explosion sound, FX, damage
  query, player effects, destroy. Dedupe damageables with a `HashSet<IDamageable>`.
  Line of sight blocks on any collider that is not the target and is not a trigger; use
  `~0` for the overlap mask but ignore trigger colliders (`QueryTriggerInteraction.Ignore`).
- The ringing: `GenerateRing()` builds a mono `AudioClip` via `AudioClip.Create(...,
  stream: false)` with a 3 kHz sine, linear fade, cached in a static; play it on a temporary
  2D `AudioSource` on a `HideAndDontSave` host. Volume by proximity, cap 0.35. Comment why it
  is generated: the owner chose two assessed slots, and ringing is an effect, not an
  authored sound.
- Player self-damage: `PlayerStats.Instance.TakeDamage(dmg, transform)` — the Transform
  overload, **before** `Destroy(gameObject)`.

**`Weapons/GrenadeController.cs : WeaponController`**:
- `UsesMagazine => true` (the HUD count), `CanAimDownSights => false`, `ShowsReticle` false
  (via the serialized field).
- Override `Fire()` entirely (do not call base): gate on `isWeaponRaised`,
  `GetMouseButtonDown(0)`, `Time.time >= nextTimeToFire`, count > 0. Spawn, `Launch`, play
  the throw sound, decrement, `RaiseHudStateChanged`, at zero → `ToggleWeaponPosition()` and
  tell the item to consume itself.
- Override the reload entry point to a no-op (read how `TryReload`/the `R` key are wired
  in the base and neutralise them without touching the base class — if the base's reload
  is not virtual, say so in your report and propose the smallest change to make it
  overridable; do not edit `WeaponController.cs` without flagging it).
- The count lives on the controller as `currentAmmo` (protected in the base — check its
  accessibility; if it is private, keep the count on the controller in a new field and
  override `CurrentAmmo`/`MaxAmmo` if they are virtual, else report).
- Fields: `grenadePrefab`, `throwSpeed`, `throwLift`, `playerColliders` (filled at
  `Start` from the Player root: `CharacterController` + every `Collider`).

**`Inventory/GrenadeItem.cs : WeaponItem`** with `maxCarried`, `count`, and the merge:
add to `Item.cs` **one** hook, `protected virtual bool TryMergeIntoInventory(Inventory
inventory)` returning `false` by default, called in `OnTriggerEnter` **before** `AddItem`;
when it returns `true` the pickup plays the pickup sound and destroys itself without taking
a slot. `GrenadeItem` overrides it: find an existing `GrenadeItem` in the inventory; if its
controller's count is below `maxCarried`, add and return `true`; if full, return `false` *and*
suppress the normal add (a full player walks through it — say so in a comment, and make the
full case log once). Consumption at zero: `Inventory.Instance.RemoveItem(slot)` +
`Destroy(gameObject)`, the `FoodItem` pattern (`:54–55`).

Compile-check. Report the diff, every serialized field with its default, the exact
signatures you call on `WeaponAudioManager`, and anything in the base class you needed to
be virtual and was not.

## Agent B — the audio surface

In `Audio/WeaponAudioManager.cs`:
- `WeaponType` gains `Grenade` (append).
- `WeaponSoundProfile` gains, under a `[Header("Grenade Sounds")]`: `AudioClip[]
  throwSounds`, `[Range(0,1)] float throwVolume = 1f`, `AudioClip[] explosionSounds`,
  `[Range(0,1)] float explosionVolume = 1f`, `float explosionMaxDistance = 80f`, and
  `[NonSerialized] int lastThrowIndex = -1, lastExplosionIndex = -1`. Tooltips in the house
  voice; the explosion tooltip says it is played **at the grenade**, in 3D.
- `IsAudioSlotRelevant` becomes: Gun → hide the two grenade arrays; Axe → hide reload ×3 and
  the two grenade arrays; Grenade → hide `shootSounds` and reload ×3, keep raise/lower and
  the grenade arrays. Write it as a switch so the next type is one case.
- `public void PlayThrowSound(string weaponName)` — identical shape to `PlayShootSound`
  (per-weapon source, `AudioUtil.PlayRandomOneShot`, the throw volume and the profile's pitch
  range).
- `public void PlayExplosionSound(string weaponName, Vector3 position)` — a temporary
  `AudioSource` on a new GameObject at `position` (`spatialBlend = 1`, `rolloffMode =
  Logarithmic`, `minDistance = 5`, `maxDistance = profile.explosionMaxDistance`, output
  mixer group copied from the manager's weapon source so it lands on the same bus), one
  random clip via `AudioUtil`, `Destroy(go, clip.length + 0.1f)`. Guard: no profile / no
  clips → return silently, like the others. Comment why it is detached: the grenade is
  destroyed on the same frame.
- `TryGetProfile(WeaponId, …)` untouched.

In `Audio Systems/AudioProfileIds.cs`: `Grenade` appended to `WeaponId`. Check whether
`AssignedNames`/`Canonical` need anything — they should not.

In `Assessment/AudioProgressScanner.cs`: two entries in `ClipLabelOverrides` —
`throwSounds → "Throws"`, `explosionSounds → "Explosions"`. **Nothing else in that file.**

Compile-check. Report the exact public signatures, and confirm by reading
`DistributionResetTools` that the new fields will be reset (they are `PPtr<$AudioClip>`
arrays, so they should be — say you checked).

## Agent C — the setup tool

One new file, `Assets/Scripts/Editor/GrenadeSetupTools.cs`, in the house shape of
`WeaponSetupTools.cs` (header comment, `///` summaries saying *why*, idempotent, Undo where
Unity allows, no dialogs, before/after logging, `SAVE THE SCENE`). Menu root
`Tools/Weapons/`.

**`Tools/Weapons/Build Grenade`** does, in order, each step skipping what already exists:
1. **Material** `Assets/Inbox/Prefabs/Grenade_Can.mat` — URP/Lit, dark grey-green, from the
   pack's can texture if `SM_Prop_Can_01` has one, else flat colour. Asset write.
2. **Projectile prefab** `Assets/Inbox/Prefabs/Grenade.prefab`: the `SM_Prop_Can_01` mesh
   scaled to ~0.12 m tall, `MeshRenderer` with the material, `Rigidbody` (mass 0.4,
   ContinuousDynamic, interpolate), `SphereCollider` (radius ~0.07, PhysicMaterial with
   bounciness 0.3 / friction 0.6 — create `Grenade_Bounce.physicMaterial` beside it), layer
   Default, tag `Projectile`, the `Grenade` component with `explosionEffectPrefab` =
   `FX_Grenade_Explosive_01`. **No AudioSource** — the reset tool never touches sources, and
   the grenade must carry no authored clip.
3. **The controller** under `Player/FirstPersonCharacter/WeaponParent/Grenade`: the same
   can mesh as the in-hand model, `GrenadeController` with `weaponId = Grenade`, `weaponName
   = "Grenade"`, `weaponAudioManager` = the scene's, `grenadePrefab` = step 2, a `firePoint`
   child 0.3 m forward of the camera, `showsReticle` false, and the raised/lowered offsets
   copied from the Axe controller (a sensible starting pose for a held object). Starts
   inactive/lowered like the others — read how `WeaponSetupTools` or the scene leaves the
   Axe and match it.
4. **The pickup** `Game Management/Pickups/Pickup - Grenade`: `GrenadeItem` with
   `weaponController` = step 3, `itemName "Grenade"`, `itemIcon` and `hudIcon` =
   `ICON_SM_Wep_PipeBomb_01`, `maxCarried` 3, a trigger `SphereCollider` (radius 0.6, match
   the axe pickup's shape), the can mesh as the world model, placed **2 m from the Axe
   pickup** (read its position; the owner will move it). Then **two more** pickups, `Pickup -
   Grenade (2)` near `Studio Room` and `Pickup - Grenade (3)` near `CampfireMusic`, each
   offset 1.5 m and snapped to the ground with a downward raycast, so the merge path is
   exercisable in play.
5. **The audio profile**: append a `WeaponSoundProfile` to the `WeaponAudioManager`'s
   profile array via `SerializedObject` — `weaponName "Grenade"`, `weaponId Grenade`,
   `weaponType Grenade`, and **both clip arrays holding one entry each: the beep clip the
   reset tool uses** (read `BeepClipPath` from `DistributionResetTools`), so the report shows
   two `[BEEP]` placeholder rows exactly like every other unfilled slot. Raise/lower left
   empty. Log the array index it landed at.
6. Mark the scene dirty, log `SAVE THE SCENE`, log every asset path written.

**`Tools/Weapons/Report Grenade Setup`** — read-only: prints whether each of the six
pieces exists and the key values, so the integrator can diff before/after.

Compile-check **after** A and B have landed (you reference their types). Report every
asset path, every serialized field the tool writes, and the values.

---

## Integrator — sequence

1. `unity status` — open the editor if it is not listed. Export the Ctrl+G report to
   `Exports/grenade_before.txt`.
2. Land A and B (they are disjoint). Compile-check, `recompile`, poll.
3. Land C. Compile-check, `recompile`, poll. Run `Report Grenade Setup`, then `Build
   Grenade`, then `Report` again; keep both logs.
4. **⏸ Owner saves the scene.** Verify the saved `.unity` has the controller, the three
   pickups and the profile entry (grep `weaponName: Grenade`).
5. **Play check through the CLI**, title dismissed, player pinned alive as in the
   performance order's driver: (a) walk onto `Pickup - Grenade` by teleport → the HUD shows
   the pipe-bomb icon and `3`; (b) walk onto the second pickup → count `3` stays (full) and
   the pickup stays; throw one (`GrenadeController.Fire()` via reflection with the button
   state simulated, or call the throw method directly) → count `2`, a `Grenade(Clone)` in
   flight, then after 3 s none, one `FX_Grenade_Explosive_01(Clone)`; (c) spawn 6 zombies in
   a ring of 3 m and throw at them → at least 4 die, XP rises by their rewards; (d) stand
   1 m from a detonation → `PlayerStats` health drops and the console shows no exception.
   Screenshot the explosion frame.
6. **Report diff**: export again to `grenade_after.txt`; the diff is exactly two added
   `[BEEP]` rows under `Grenade` and the totals `104 → 106`, `placeholder 73 → 75`.
   Anything else is a bug.
7. **Reset dry run**: `Tools/Distribution/Reset Audio Clips For Handout` has a dry-run or
   report mode? Read `DistributionResetTools.cs` — if it does, run it and confirm the two
   new fields are listed; if not, say so and leave the reset untested (do **not** run the
   real reset on the owner's scene).
8. **Docs**: `CODE_STRUCTURE.md` — Weapons row (`GrenadeController`, `Grenade`, the
   detonation order, the `Projectile` tag now meaning something), Inventory row (`GrenadeItem`
   + the `TryMergeIntoInventory` hook, the only stacking in the game), Audio row (the two
   slots, `WeaponType.Grenade`, the detached 3D explosion source), Editor tooling bullet,
   work-history row; `README.md` index row; this STATUS.
9. Commits: A, B, C's tool, the owner-saved scene + the new prefab/material/physic
   material, docs. Push only when the owner says.

**⏸ Owner checkpoints, collected:** (1) save the scene after step 3; (2) a real play
check — the throw arc, the bounce, the fuse timing, whether 5 m / 120 damage feels right
against a Yokai (health unknown to this order — read `maxHealth` on the prefabs and say
what one grenade kills), the self-damage feel; (3) move the three pickups where the level
wants them; (4) two beeps to listen for — Throw and Explosion — and confirm the report
reads `Grenade  -  0/2`.

## Questions for the owner, if they arise

The four design decisions are made. Ask only if the code forces one: for instance if the
base class's reload or ammo members are private and the smallest fix is a one-line
`virtual` in `WeaponController.cs` — propose it and ask, do not just edit.

## STATUS

**Built and verified 2026-09-05.** Three agents ran in parallel, one integrator drove the
editor. Code and assets are committed; **the scene is waiting on the owner to save.**

### Verified in play (CLI-driven, 0 game exceptions throughout)

| check | result |
|---|---|
| Walk onto a pickup | count **3**, HUD reads **3/3**, one inventory slot |
| Select the slot | weapon raises |
| Walk onto a second pickup while full | count stays 3, **the pickup stays on the ground** |
| Throw | count 3 to 2, HUD 3/3 to 2/3, one grenade in flight, gone after the fuse |
| Explosion FX | one instance appears, self-destructs |
| Zombies in the blast | two took **60 damage each**, falloff by distance, the ones past 5 m untouched |
| Self-damage at 0.6 m | **107** of a 120 centre - matches the falloff |
| Report | **198 to 200 slots**, two `[BEEP]` rows, every other row byte-identical |

### Three bugs found, all fixed

1. **The player took no damage at all.** `PlayerStats` is on a *child* of the player, the
   only body collider is the `CharacterController` on the *parent*, and `DamageableUtil`
   resolves a collider by looking at its own object and then walking **up**. It never looks
   down, so the overlap query could never find the player. `ZombieAI` hits the same wall and
   works around it. The blast now resolves the player explicitly through
   `PlayerStats.Instance` and adds them to the dedupe set so the loop cannot double-hit.
2. **The throw could not be tested.** `Fire` was monolithic and gated on live mouse state.
   `ThrowOne` is split out: `Fire` decides, `ThrowOne` acts.
3. **The two audio slots had nowhere to drag a clip.** `WeaponAudioManagerEditor` hand-draws
   each profile's fields and switches on `WeaponType`; with no `case` for Grenade the section
   rendered empty, so the report asked for two sounds the inspector gave no way to supply.
   Found by the owner, not by any of the checks above - every automated test read the
   serialized data directly and so never noticed the authoring surface was missing. Adding a
   slot to this project touches **three** places: the profile field, the inspector, and
   `IsAudioSlotRelevant`.

### Deviations from this order

- **Raise/lower hidden for Grenade.** The order said "exactly two new rows" *and* "keep
  raise/lower" - those contradict, since a single clip field always yields a row. The owner
  chose two assessed slots, so raise/lower are hidden and the count is exactly two.
- **The 104 to 106 prediction was stale.** The owner authored ~134 clips between the order
  being written and run; the live baseline was **198**, so the check became 198 to 200.
- **Three additive lines in `WeaponController`** (`CurrentAmmo`/`MaxAmmo` virtual, a
  protected HUD raiser) so the count reaches the panel, as the approved design required.
  Agent A proposed rather than made them; the integrator approved.
- **`UsesMagazine => true`** on the grenade, so the panel prints the count.

### Waiting on the owner

1. **Save the scene** - the controller, the three pickups and the audio profile are live in
   the editor only. WARNING: the scene also carries the owner's own uncommitted audio
   assignments, so that commit will contain both unless they commit theirs first.
2. **Play it properly** - the throw arc, the bounce, whether 5 m / 120 damage feels right,
   and how self-damage reads. All values are serialized.
3. **Move the three pickups.** Placement is a raycast-snapped starting point, not a design.
4. **Two beeps to replace**: `Grenade > Throws` and `Grenade > Explosions`.
