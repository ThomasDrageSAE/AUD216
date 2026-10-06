# Minigun Barrel, Hotbar Selection, Compass, Grenade Reskin — Work Order (parallel agents)

> Written 2026-09-06 at commit `bb816c1`. **This is a work order for an implementing AI
> (Opus) running four agents in parallel.** Four separate owner requests, all verified
> against the live editor before writing:
>
> 1. **The minigun's barrels spin like a propeller** instead of turning about their own axis.
> 2. **The hotbar should show the Synty yellow border** around the slot whose item is equipped.
> 3. **The compass should sit top-right**, as in the Synty FPS demo screenshot.
> 4. **The grenade should use the owner's new `Assets/Grenade.fbx`** instead of the tin can,
>    with the pack's pineapple-grenade icon in the hotbar (added 2026-09-06, after the survey).
>
> Every path, field and value below was read from the scene or the code at `bb816c1`. Read
> first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (Weapons and Editor rows — including the two
> warnings added yesterday), the ground rules in [IMPROVEMENT_PLAN_2.md](IMPROVEMENT_PLAN_2.md),
> and the STATUS of [GRENADE_TASKS.md](GRENADE_TASKS.md), whose agent shape this copies:
> **only the integrator drives the editor**, scene edits are menu items the owner saves.
> ⏸ marks an owner checkpoint.

## What the survey found — read this before assigning anything

All three are smaller than they look, and one is not what it was described as.

| Request | What is actually true at `bb816c1` |
|---|---|
| Barrels spin like a propeller | `MinigunController` rotates `barrel` about **`Vector3.forward` (local Z)** in `Space.Self`. The barrel mesh's local bounds are **(0.002, 0.009, 0.002)** — the barrels run along **local Y** — and the direction from the barrel to `Muzzle_Point_01`, in the barrel's own space, is exactly **(0, −1, 0)**. Spinning about Z turns a Y-long cluster end over end. **One axis is wrong.** |
| The hotbar has no equipped indicator | It has one, switched off. Every slot `Btn_HotbarItem_0N/Item` already carries a child **`Selected`** (inactive) whose child `SPR_Selected` is **`SPR_HUD_Frame_Border_Grunge_Med` tinted (0.898, 0.839, 0.294, 1)** — the exact Synty yellow border — plus a `Highlighted` sibling (a translucent yellow fill, the hover state). **Nothing activates them**: no script in the project references `Selected`, and there is no "which slot is equipped" signal anywhere in the UI. |
| The grenade is a tin can | Placeholder by decision: no grenade mesh existed in the packs on 2026-09-05, so `SM_Prop_Can_01` stood in as an improvised pipe bomb with `ICON_SM_Wep_PipeBomb_01`. **The owner has now added `Assets/Grenade.fbx`** (untracked, 39 KB): root `Grenade` at **localScale 100** with meshes authored at 1/100, three renderers — the body, `SM_Wep_Grenade_01_Pin_01`, `SM_Wep_Grenade_01_Spoon_01` — 1076 verts, **eight URP/Lit materials already imported**, and **mesh bounds 0.06 × 0.11 × 0.07 m at import scale** — real frag-grenade size, so it needs no scaling to be right in the world. The matching pack icon is **`ICON_SM_Wep_Grenade_01_BR`** (the green pineapple body, silver spoon and ring — the owner's screenshot exactly; `_Apoc` is a banded canister). No `_Side` variant exists; `WeaponItem.GetHudIcon()` falls back to `itemIcon`, as it does today. |
| The level has no compass | **It does, and it draws.** `Canvas - Interface/HUD/ScreenSpace/Top /HUD_Compass_Military` is active, 38 of its 44 images render, and the play-mode screenshot (`Exports/hud/hud_now.png`) shows it **top-centre** — W / NW / N with yellow objective markers. `Compass` on it drives the strip from `FirstPersonCharacter`'s heading; `EnemyRadar` and `ObjectiveRadar` (on a separate `Canvas - Compass Enemies & Objectives`) spawn their dots under its `Compass Bar Target for objectives & enemys`. The request is really **"move it to the top-right and make sure it reads like the demo"**, not "add one". |

The one design decision this order makes on the owner's behalf, because the request only says
"when an inventory item is equipped": **equipped means in the hands** — a raised weapon
(`WeaponController.IsWeaponRaised()`) or a raised fishing rod (`FishingRod.IsRaised()`). Food
and medicine are used on the spot and are never equipped, so their slots never light. If that
reading is wrong it is one predicate in one method.

## Invariants

- **Audio behaviour is unchanged.** Nothing under `Assets/Scripts/Audio*/` or `Assessment/` is
  edited; the Ctrl+G export before and after is identical (the integrator diffs it).
- **Only the integrator drives the editor.** Agents never run a `unity` command.
- **Scene edits are one idempotent, Undo-recorded `[MenuItem]`** the integrator runs and the
  owner saves. ⚠ The owner's scene carries their own uncommitted audio work; the integrator
  saves nothing without checking `git diff --stat` on the `.unity` first and saying what else
  the save carries.
- `bash Documentation/tools/compile-check.sh` after every edit, both assemblies ✅; then
  `recompile` + poll `recompile_status` before the editor is used. ⚠ CRLF on every `.cs`,
  verified with `file`. ⚠ The shell mangles C# `$"…"` — write code with the Write tool.
- No `[SerializeField]` renamed or removed. No `EditorUtility.DisplayDialog` (it hangs the CLI
  bridge). Always `--project-path D:/GitHub/AUD216_Project1_Nature_Level`.
- ⚠ **Read the console with a cursor, never a tail.** The bridge's console buffer is
  cumulative for the play session; three times now a startup burst has been misread as a
  per-frame storm. `console --tail 1 --level error` → `cursor`, then `--since <cursor>`.
- One commit per agent landing, house message style, the harness attribution trailer. Push
  only when the owner says.

## Verified anchors

| Area | Anchor | Value at `bb816c1` |
|---|---|---|
| Barrel rotation | `Assets/Scripts/Weapons/MinigunController.cs` ~:259 | `barrel.Rotate(Vector3.forward, spinFraction * maxBarrelRpm * RpmToDegreesPerSecond * Time.deltaTime, Space.Self);` guarded by a null check with a once-only warning. `barrel` is a serialized `Transform`; `firePoint` (inherited) is the model's `Muzzle_Point_01`. `spinFraction` is a private float 0–1 driven by the state machine. |
| Barrel geometry | scene, `…/WeaponParent/Minigun/Minigun_Model/SK_Wep_Minigun_02_Barrel_01` | mesh local bounds **(0.002, 0.009, 0.002)**, centre (0, −0.004, 0); barrel `localEuler (0,0,0)`, `localPos (0, −0.002, 0)`; model child `localEuler (0,0,0)`; **barrel→muzzle in barrel-local space = (0, −1, 0)**. So the spin axis is barrel-local ±Y. The barrel's local Z currently points along the weapon root's forward — i.e. *sideways through the cluster*. |
| Equipped signal | `Assets/Scripts/Weapons/WeaponController.cs` | `ToggleWeaponPosition()` (~:405) flips `isWeaponRaised`, plays raise/lower, then calls `UpdateAmmoUI(isReloading)`, which raises the static event **`OnHudStateChanged(WeaponController)`** (~:623) *before* its `ammoText == null` return — so **every raise and lower of every weapon already fires it**. `CurrentWeaponHUD.OnWeaponStateChanged` (`UI/CurrentWeaponHUD.cs`) is the precedent: show on `IsWeaponRaised()`, hide only if the lowered weapon is the one shown. `protected RaiseHudStateChanged()` exists for subclasses. |
| Rod state | `Items/FishingRodItem.cs`, `Items/FishingRod.cs` | `FishingRodItem.UseItem()` toggles a **private** `isRaised`, calls `LowerOtherItems()` (which `ToggleWeaponPosition()`s any raised weapon — so the weapon event fires on a rod raise too), then `MoveToRaisedPosition/Lowered`. Public: `GetFishingRodController()` → `FishingRod`, whose **`IsRaised()`** and `IsTransitioning()` are public. **No event on the rod.** `ForceToLowered()` exists. |
| Hotbar UI | `Assets/Scripts/UI/ItemInventoryUI.cs` on `Canvas - Interface/HUD/ScreenSpace/Bottom/HUD_Hotbar_Minimal` | `[SerializeField] Image[] itemSlots` (5) → `Btn_HotbarItem_0N/Item/Icon`; subscribes `Inventory.OnItemAdded/OnItemRemoved`; sets the icon sprite and alpha. **No selection concept.** Slot children, per `Item`: `SPR_Background` (active), `Icon` (active), `Highlighted` (**inactive**, child `SPR_Highlighted` = the grunge tile at yellow α 0.298), `Selected` (**inactive**, child `SPR_Selected` = `SPR_HUD_Frame_Border_Grunge_Med` at yellow α 1). The hotbar has **six** buttons (`Btn_HotbarItem_00–05`); `Inventory.MAX_SLOTS` is **5** and the screenshot shows five, so button 05 is already out of the visible layout. |
| Inventory → controller | `Inventory.Instance.GetAllItems()`; `WeaponItem.GetWeaponController()`; `FishingRodItem.GetFishingRodController()` | The map from a raised controller to a slot index. |
| Compass | `Canvas - Interface/HUD/ScreenSpace/Top /HUD_Compass_Military` | RectTransform: anchorMin/Max **(0.5, 1)**, anchoredPos **(0, −106.7)**, size **(1000, 100)**; parent `Top ` is 600×600 at (0,0); `Canvas - Interface` is ScreenSpaceOverlay, scaled (its reference is 2790×1440 at the capture). `Compass` component: `viewDirection = FirstPersonCharacter`, `compassElement = HUD_Compass_Content_Military` (4400 wide), `compassSize = 1560`; `LateUpdate` sets `compassElement.anchoredPosition.x = (signedHeading / 180) * compassSize`. Children: `Content/Mask/CompassPoints`, `Content/Mask/Icons/Compass_Objective_Icon` (inactive template), `Content/Compass Bar Target for objectives & enemys` (100×100 at (0,19)). Screen rect at capture: **(1061, 1302)–(1728, 1369)** of 2790×1440 — top-centre. |
| The other compass canvas | `Canvas - Compass Enemies & Objectives` | Holds `EnemyRadar` (`compassBar` → the Military compass's `Compass Bar Target…`; instantiates `enemyIndicatorPrefab` under it, throttled by `CountCheckInterval`) and `ObjectiveRadar`; also a `CompassMask` (478×62, top-centre, y −22.7). The screenshot shows **one** strip, so `CompassMask` draws nothing visible today — it is a leftover, not a second compass. Report it; do not delete it in this order. |
| The can, everywhere it is used | `Assets/Inbox/Prefabs/Grenade.prefab` (child `SM_Prop_Can_01`, **1** ref); scene: `Player/FirstPersonCharacter/WeaponParent/Grenade/SM_Prop_Can_01` and the three `Game Management/Pickups/Pickup - Grenade*/SM_Prop_Can_01` (**4** instances); `Assets/Inbox/Prefabs/Grenade_Can.mat` (flat colour, only the can uses it) | `Editor/GrenadeSetupTools.cs` hard-codes `ModelPath` (`…/PolygonExtras/Models/SM_Prop_Can_01.fbx`, :76), `IconPath` (`…/ICON_SM_Wep_PipeBomb_01.png`, :80), `MaterialPath` (:68) and `ModelChildName = "SM_Prop_Can_01"` (:104); hand size constant 0.12 m, world 0.30 m; the projectile's `SphereCollider` is r 0.07 on the root, Rigidbody mass 0.4, `Grenade` component with `explosionEffectPrefab` wired. |
| The Synty demo layout | the owner's screenshot (not in the repo) | Compass strip **top-right**; hotbar bottom-centre with a **yellow border** around the equipped slot; the rusty current-weapon panel bottom-right (already built, `CurrentWeaponHUD`). |
| Play-mode verification kit | `Exports/hud/hud_now.png`; `capture_game_view --source screen` (key `base64`); the title is dismissed by reflection (`TitleScreen` → private `Play()`); an item is picked up by **moving the pickup onto the player** (teleporting the player has failed twice) | Pickups: `Game Management/Pickups/Pickup - {SMG, Revolver, Crossbow, FishingRod, Axe, Grenade, Minigun}`. |

## Agent plan

```
Phase 1  A · B · C · D in parallel  disjoint files
Phase 2  Integrator                 A+B land → recompile → play checks · C and D land → tools run
                                   → ⏸ owner saves → screenshot checks · docs · commits
```

| Who | Owns (writes) | Reads | Editor |
|---|---|---|---|
| **A** — barrel | `Weapons/MinigunController.cs` only | the model hierarchy (from this table) | no |
| **B** — hotbar selection | `UI/HotbarSelectionUI.cs` (new), **one added line** in `Items/FishingRodItem.cs`, **one added accessor** in `UI/ItemInventoryUI.cs` | `WeaponController`, `CurrentWeaponHUD`, `Inventory`, `WeaponItem`, `FishingRod` | no |
| **C** — HUD tool | `Editor/HudSetupTools.cs` (new, the only file) | the scene, `WeaponSetupTools.cs` / `GrenadeSetupTools.cs` for the house shape | no |
| **D** — grenade reskin | `Editor/GrenadeSetupTools.cs` only (the constants, plus a new `Reskin Grenade` item) | `Assets/Grenade.fbx`, the projectile prefab, the scene | no |
| **Integrator** | docs, this STATUS, the scene via C's tool | everything | **yes — the only one** |

A and B never touch the same file. B references nothing C creates; C references B's component by
name (`HotbarSelectionUI`), so C compile-checks **after** B lands. D touches only
`GrenadeSetupTools.cs`, which nobody else opens, and no runtime script — the projectile's
behaviour is untouched, only its picture.

---

## Agent A — the barrel spins about its own axis

The bug is one wrong vector: `Vector3.forward` where the barrels run along local Y. Do not just
swap the constant — make the axis **derived**, so the next FBX with a different rig does not
regress it:

- Add `[SerializeField] private Vector3 barrelSpinAxis = Vector3.zero;` with a tooltip: "Axis
  the barrels turn about, in the barrel's own space. Leave at zero to derive it from the fire
  point — the direction from the barrel to the muzzle IS the barrel axis. Set it only for a rig
  where that is not true."
- In `Start` (after the base's), resolve `resolvedSpinAxis`: if `barrelSpinAxis` is non-zero,
  normalise and use it; else if `barrel != null && firePoint != null`, use
  `barrel.InverseTransformDirection((firePoint.position - barrel.position).normalized)`; else
  fall back to `Vector3.up` with the once-only warning explaining why. Log the resolved axis
  once at `Debug.Log` level — the integrator reads it.
- Rotate with `barrel.Rotate(resolvedSpinAxis, degrees, Space.Self)`. **Sign**: ⚠ **the first
  draft of this line had the handedness backwards, and Agent A caught it.** Unity's positive
  rotation is *clockwise when the axis points at the eye* — the standard check being that
  `Rotate(0, 90, 0)` turns a character to its right, clockwise seen from above with +Y toward
  the viewer. The derived axis points **downrange, away from the shooter**, so positive
  `degrees` is **anti-clockwise seen from behind the gun**. Nothing in the code depends on
  which is true — that is exactly why it is a checkbox — but do not restate the wrong version
  in a comment. Add `[SerializeField] private bool reverseSpin = false;` and say in its tooltip
  which way is which, so the owner can flip it by eye without touching code.
- Keep the null-barrel warning path exactly as it is.
- **Test seam** (editor-only, so the integrator can drive the spin without holding a mouse
  across consecutive frames, which the CLI cannot do):
  ```csharp
  #if UNITY_EDITOR
      /// <summary>Editor-only: forces the barrel to full speed for scripted checks.</summary>
      public void EditorForceSpin(float fraction) { spinFraction = Mathf.Clamp01(fraction); spinState = SpinState.Firing; }
  #endif
  ```
  (use the real names of the state enum and field — read the file). Comment that it exists
  because a held trigger is the one thing a CLI-driven check cannot produce.
- Compile-check. Report the resolved-axis logic, the sign convention, and the exact lines.

## Agent B — the yellow border follows what is in the hands

**`Assets/Scripts/UI/HotbarSelectionUI.cs`**, a `MonoBehaviour` that lives on the same object as
`ItemInventoryUI` (Agent C's tool adds it). House header; `///` summaries that say *why*.

- Needs the five slot roots. Add to `ItemInventoryUI` one public read-only accessor —
  `public IReadOnlyList<Image> ItemSlots => itemSlots;` — and derive each slot's `Selected`
  object as `itemSlots[i].transform.parent.Find("Selected")`. Cache the five in `Start`; a
  missing one logs once and is skipped. **No new serialized wiring** — the Synty prefab
  structure is the contract, and the tool verifies it.
- **Weapons**: subscribe to `WeaponController.OnHudStateChanged` in `OnEnable`, unsubscribe in
  `OnDisable` (the event is static — an un-unsubscribed handler survives a scene reload and
  throws on a destroyed component). On the event, recompute. Mirror `CurrentWeaponHUD`'s rule:
  a raised weapon selects its slot; a lowered weapon deselects **only if it was the selected
  one** (weapon swaps lower the old before raising the new).
- **The rod** has no event. Add to `FishingRodItem` exactly one line,
  `public bool IsRaised => isRaised;` (the field already exists), and have `HotbarSelectionUI`
  poll it in `Update` — five slots, one bool, nothing to measure. Recompute only when the
  answer changes (compare-before-write: `SetActive` on a uGUI object is a canvas rebuild, and
  the canvases already rebuild every frame at idle — do not add to it).
- **Mapping** raised thing → slot: walk `Inventory.Instance.GetAllItems()`; a `WeaponItem` whose
  `GetWeaponController()` is the raised weapon, or a `FishingRodItem` whose `IsRaised` is true.
  Exactly one slot may be selected; if the walk finds none, all five go off.
- **Consumables never select** — say so in a comment, with the reason above.
- Guard: `Inventory.Instance` null → treat as nothing selected (the project's guard-at-the-site
  rule; see the header of `ObjectiveManager.cs`).
- `Highlighted` (the hover fill) stays untouched — it is the mouse-over state, not equip.

Compile-check. Report the diff, the two one-line additions quoted, and the exact rule.

## Agent C — the HUD tool

One new file, `Assets/Scripts/Editor/HudSetupTools.cs`, in the house shape of
`GrenadeSetupTools.cs` (report item first, apply items idempotent and Undo-recorded, before/after
logging, `SAVE THE SCENE`, no dialogs). Menu root `Tools/HUD/`.

1. **`Report HUD Setup`** — read-only, diffable: the compass's RectTransform (anchors, pivot,
   anchoredPosition, size), its `Compass` wiring, `EnemyRadar.compassBar`, whether
   `CompassMask` on the other canvas has any active renderers; for each `Btn_HotbarItem_0N`
   whether `Item/Selected` and `Item/Highlighted` exist and are active; whether
   `HotbarSelectionUI` is on `HUD_Hotbar_Minimal`; whether `Btn_HotbarItem_05` is active.
2. **`Move Compass Top Right`** — on `HUD_Compass_Military`'s RectTransform: `Undo.RecordObject`,
   then `anchorMin = anchorMax = (1, 1)`, `pivot = (1, 1)`, `anchoredPosition = (−margin, −margin)`
   with `margin` a named constant (~40 px in the canvas's reference units — read the
   `CanvasScaler` on `Canvas - Interface` and say what units that is), **size unchanged**
   (1000×100 — the strip's width is what `compassSize` was tuned against; shrinking it means
   retuning `Compass.compassSize`, which is not in this order). Verify the parent `Top ` does
   not clip it (it is 600×600 at the top-centre — if it does, reparent the compass to
   `ScreenSpace` and say so). Idempotent: already there → "nothing to do".
3. **`Add Hotbar Selection`** — `Undo.AddComponent<HotbarSelectionUI>` on `HUD_Hotbar_Minimal`
   if absent; verify the five `Item/Selected` children exist and are **inactive** (the script
   owns their state from here); log any slot missing one. Do **not** touch `Btn_HotbarItem_05`
   — it is already outside the visible layout; report it only.
4. Every apply item marks the scene dirty and logs `SAVE THE SCENE`.

Compile-check **after B lands**. Report the menu paths, every field written, the scaler units.

## Agent D — the grenade wears the owner's model

All in `Assets/Scripts/Editor/GrenadeSetupTools.cs`, nowhere else. Two jobs:

**1. Retarget the constants**, so a `Build Grenade` on a fresh scene produces the new grenade:
`ModelPath` → `Assets/Grenade.fbx`; `IconPath` → `…/Icons_Explosives/ICON_SM_Wep_Grenade_01_BR.png`;
`ModelChildName` → `Grenade_Model` (the FBX root is also called `Grenade`, and the scene already
has a `Grenade` controller — `Grenade/Grenade` is unreadable; `MinigunSetupTools` made the same
call). The material step becomes a no-op with a log line: the FBX imports its own eight URP/Lit
materials, so `Grenade_Can.mat` is no longer written or referenced. **Do not delete the `.mat`**
— report that it is now unused and leave it to the owner.

**2. `Tools/Weapons/Reskin Grenade`** — for the scene that exists. Idempotent; each of the five
places skips itself if it already carries the new model:
- **The projectile prefab** `Assets/Inbox/Prefabs/Grenade.prefab`: open with
  `PrefabUtility.LoadPrefabContents`, destroy the `SM_Prop_Can_01` child, instantiate the FBX as a
  child named `Grenade_Model`, size it to the existing hand constant (0.12 m tall) **measuring
  with mesh bounds** — the FBX is 0.11 m native, so expect a factor near 1 — recentre it so the
  body sits on the root (the mesh centre is (0, 0.02, 0.01)), leave the `SphereCollider`
  (r 0.07 fits a 0.06-wide body), the `Rigidbody` and the `Grenade` component exactly as they
  are, `SaveAsPrefabAsset`, `UnloadPrefabContents`. ⚠ **Asset write — not undoable**; say so in
  the log; `git checkout -- Assets/Inbox/Prefabs/Grenade.prefab` is the revert.
- **The in-hand weapon** `…/WeaponParent/Grenade/SM_Prop_Can_01` → replaced by a `Grenade_Model`
  instance at the hand size, Undo-recorded.
- **The three pickups** `…/Pickup - Grenade*/SM_Prop_Can_01` → replaced at the world size
  (0.30 m constant, inside the 0.4715-scaled pickup as today), recentred so it spins on the spot,
  Undo-recorded.
- **The three `GrenadeItem`s**: `itemIcon` and `hudIcon` → `ICON_SM_Wep_Grenade_01_BR`.
- ⚠ **The FBX root is at localScale 100** with its meshes authored at 1/100 — keep that scale on
  the instance and size the *instance root*; do not "normalise" the child, and never measure with
  `Renderer.bounds` (stale after instantiate, and it swallows particle systems — CODE_STRUCTURE,
  Editor row).
- Log every object replaced, the measured mesh-bounds size before and after, and
  `SAVE THE SCENE`.

Also commit-worthy: `Assets/Grenade.fbx` + `.meta` are **untracked** — the integrator adds them
in D's commit. Optional, flag only: the pin and spoon are separate meshes, so a later nicety could
drop the pin on throw. Not this order.

Compile-check. Report the constants changed, the five replacements with their measured sizes,
and confirmation that `Grenade.cs` / `GrenadeController.cs` / `GrenadeItem.cs` were not opened.

---

## Integrator — sequence and verification

1. Export the Ctrl+G report → `Exports/hud/audio_before.txt`. Take `Exports/hud/hud_now.png`
   as the "before" (it already exists from the survey).
2. **A and B land** → compile-check, `recompile`, poll.
3. **Barrel, verified numerically, not by eye:** enter play, dismiss the title, bring
   `Pickup - Minigun` onto the player, select its slot (`FirstPersonController.SelectInventorySlot`).
   Read the barrel's `localRotation` and `firePoint` direction in barrel-local space. Call
   `EditorForceSpin(1f)` by direct call, wait ~0.5 s, call it again (the state machine will
   have started spinning down — re-forcing keeps it spinning), read again. Compute
   `delta = Quaternion.Inverse(before) * after` → `delta.ToAngleAxis(out angle, out axis)`.
   **Pass:** `axis` is within a few degrees of ±barrel-local Y (i.e. `Mathf.Abs(axis.y) > 0.98`)
   and the barrel→muzzle direction in barrel-local space is **unchanged** to three decimals
   (the axis of rotation is the one line that does not move). Then `capture_game_view` for the
   owner. Log the resolved axis the controller printed.
4. **Hotbar:** in the same session, with the minigun raised → exactly one `Selected` active,
   on the minigun's slot. Lower it (`ToggleWeaponPosition`) → none active. Bring
   `Pickup - FishingRod` onto the player, `UseItem` its slot → the rod's slot active and the
   minigun's not. Screenshot each state. Read the console **with a cursor**: 0 new errors.
5. **C lands** → compile-check, `recompile`. `Report HUD Setup`, then `Move Compass Top Right`
   and `Add Hotbar Selection`, then `Report` again; keep both logs.
5b. **D lands** → compile-check, `recompile`. `Report Grenade Setup`, then `Reskin Grenade`, then
   `Report` again. Verify: the projectile prefab's renderers are the three FBX meshes and **no**
   `SM_Prop_Can_01` remains anywhere (`grep -c "m_Name: SM_Prop_Can_01"` on the saved scene → 0);
   mesh-bounds height of the in-hand model ≈ 0.12 m and of a pickup model ≈ 0.30 m; then in play,
   bring `Pickup - Grenade` onto the player, equip, `ThrowOne()`, and confirm it still detonates
   (the physics and the `Grenade` component were not touched — this proves it). Screenshot the
   grenade in hand and a pickup on the ground for the owner.
6. **⏸ Owner saves the scene.** Before asking: `git diff --stat "Assets/Level for Sound Design.unity"`
   and say what else the save will carry.
7. Play once more: screenshot → the compass strip is top-right and still reads W/N/E with the
   objective markers; heading changes as the player turns (rotate the camera by reflection,
   screenshot twice). The hotbar border shows on the equipped slot in the same frame.
8. **Audio invariant:** export again; `diff` against step 1 is empty. (The reskin adds no
   slot and touches no clip; if the diff is not empty, something else moved.)
9. **Docs:** `CODE_STRUCTURE.md` — Weapons row (the derived spin axis, and that `Vector3.forward`
   was the propeller), UI row (`HotbarSelectionUI`, the "equipped = in the hands" rule, that
   `Selected`/`Highlighted` are Synty's own children), Editor tooling bullet, work-history rows.
   `README.md` index row. This file's STATUS.
10. Commits: A, B, C's tool, **D's tool + `Assets/Grenade.fbx` + its meta + the reskinned
    prefab**, the owner-saved scene, docs. Push only when the owner says.

**⏸ Owner checkpoints, collected:** (a) save the scene after step 5; (b) hold the minigun's
trigger and watch the barrels turn about their axis — and flip `Reverse Spin` if they turn the
wrong way for the eye; (c) equip each weapon and the rod and confirm the border lands on the
right slot; (d) the compass position and margin against the demo screenshot; (e) the new grenade in hand
and on the ground, and the pineapple icon in the hotbar and the bottom-right panel.

## Questions for the owner, if they arise

None are expected; the three requests are unambiguous. Ask only if the compass has to shrink to
fit top-right at the owner's aspect ratio (that retunes `compassSize`), or if "equipped" should
also cover consumables.

## STATUS

**Done and verified 2026-09-06.** Four agents in parallel, one integrator. All four requests
work and are visible in `Exports/hud/hud_grenade_equipped.png`.

| Request | Result |
|---|---|
| Barrels spin like a propeller | **Fixed and proved numerically.** The axis resolved to barrel-local (0, -1, 0); between two samples the barrel turned 35.9 deg about (0, 1, 0), the barrel-to-muzzle direction drifted **0.00000**, and the rotation axis dotted with the spin axis at **1.0000**. A propeller fails both. |
| Yellow equipped border | **Working.** Slot lights on the minigun, moves to the grenade, clears when nothing is in hand. |
| Compass top-right | **Moved**, size unchanged (1000x100), so `compassSize` and both radars' `compassBarWidth` stay valid. |
| Grenade reskin | **Done**, 8 replacements, 0 tin cans left in the saved scene, and a thrown grenade still detonates. |

### Three corrections to this order, found while running it

1. **The handedness was stated backwards** (Agent A). Unity's positive rotation is clockwise
   when the axis points *at* the eye; the derived axis points downrange, so positive is
   anti-clockwise from behind. Corrected in the Agent A section above.
2. **`Top Right` is the wrong parent** (Agent C). It is at localScale **1.1** - reparenting the
   strip there would silently render it 1100x110 and desync `compassSize` - and its top edge
   sits 81.8 px **above** the canvas, so a -40 margin would clip it. The compass went to
   `ScreenSpace` instead, the route this order sanctioned as the fallback.
3. **`CompassMask` is not a leftover** (Agent C). `EnemyRadar.radarCanvas` **and**
   `ObjectiveRadar.radarCanvas` both point at it, and `ObjectiveRadar.Update` early-returns when
   it is null - deleting it would silently stop the objective markers. It draws nothing only
   because its Image sits at alpha 1/255.

### The hotbar took three attempts, and the reason is worth keeping

`SetActive` on the `Selected` child did nothing: **every clip** in Synty's button controller
animates `Item/Selected . m_IsActive`, so the Animator rewrites it every update. Driving the
controller's own `Selected` trigger did not hold either, because `Selectable` re-fires `Normal`
from `DoStateTransition`. The fix is to take the frame off the Animator - transition to `None`,
Animator disabled - which costs the hover animation an FPS with a locked cursor never shows.
Along the way `ApplySelection` was also made self-healing (it drives every frame from the wanted
slot rather than early-returning on a cached index), because a cache that can disagree with the
thing it caches wedges the border permanently.

### Waiting on the owner

1. **Play it.** Hold the minigun's trigger and watch the barrels; flip **Reverse Spin** if they
   turn the wrong way for the eye. The direction is a matter of the eye, not the maths.
2. **The minigun reads very large in first person** (`Exports/hud/hud_minigun_equipped.png`) -
   `HandMinigunLength` in `MinigunSetupTools` is the one number.
3. **Recoil is still at the class default**, tuned for a weapon firing every 0.1 s rather than
   every 0.05 s; it may read as jammed nose-up.
4. **`Grenade_Can.mat` is now unused** - deleting it is the owner's call.
