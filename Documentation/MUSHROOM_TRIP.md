# Ported props — the mushroom trip, and the well

> Ported 2026-09-01 from a student submission — **Liam Chapman, 26T2 AUD216**. The mushroom
> mechanic (`MushroomTripTrigger.cs`) and both models are his; the implementation here was
> reworked to this project's conventions and its visuals rebuilt (see *Why the visuals
> differ*). If this ships in teaching material, credit him.

## What it does

Walk up to a mushroom, get the standard `E` prompt, press it: an interaction sound plays,
the screen washes into a warped, hue-cycling haze, the camera drifts and rolls for ~20
seconds, then everything eases back and the mushroom is gone.

## Files

| File | Role |
|---|---|
| `Assets/Scripts/Interactions/MushroomTripInteraction.cs` | The placeable mushroom: prompt, `E`, sound, hands a trip to the effect, removes itself |
| `Assets/Scripts/Camera/MushroomTripEffect.cs` | The trip itself — post-processing Volume, camera drift, mixer snapshots. Also holds `MushroomTripSettings`, the per-mushroom knobs |
| `Assets/Scripts/Interactions/PropSoundInteraction.cs` | The plain case: prompt, `E`, play a sound. Used by the well; reusable for any prop that only needs to be audible. Has a cooldown so a held key cannot machine-gun the clip |
| `Assets/Scripts/Editor/PortedPropTools.cs` | `Tools ▸ Interactions ▸ …` — builds both prefabs and adds their audio profiles |

`AudioProfileIds.InteractionId` gained `Mushroom` and `Well` members (appended, never
inserted — those values are serialized as ints).

`PropSoundInteraction` exists rather than reusing `InteractionSystemTMP` because that one
is built around the power grid (light switches, the electrical box, the mixing desk) and
pulls `ActionHandler` in with it; a prop that only makes a noise wants none of that.

## Why the visuals differ from the original

The submission drove its visuals by sliding a purchased **Hovl Studio "Fullscreen Effects"**
quad in front of the camera. That is a commercial Asset Store package and this project holds
no licence for it, so it was **not** copied. The look is rebuilt instead from URP
post-processing the project already ships — lens distortion, chromatic aberration, a cycling
hue, saturation, vignette and bloom — created at runtime as a `Volume` with priority 100
(above the scene's Global Volume) and blended by a single weight.

Nothing needs to exist in the scene for this: the effect object, its Volume and its profile
are all built on first use, matching how the title screen and the audio overlays install
themselves.

## The models, and the well

Two models came across from the same submission — both Blender exports, each with its
`.meta` copied alongside so the import settings (and their embedded materials) arrive
intact. His project placed loose instances of them and had no prefabs, so there were none
to copy.

| Asset | Feature |
|---|---|
| `Assets/Mushroom Trip/Models/MushroomClump.fbx` | The edible mushroom |
| `Assets/Well/Models/StylizedWell.fbx` | A stone well — a plain "press E, hear a sound" prop, using `PropSoundInteraction` |

**`Tools ▸ Interactions ▸ Create Trip Mushroom Prefab`** and
**`Tools ▸ Interactions ▸ Create Well Prefab`** build them, each with a trigger sphere sized
from the model's own bounds plus reach, an AudioSource and its interaction component. The
well also gets a solid box collider so the player cannot walk through the stonework (a
MeshCollider would hug it better, but these models import with Read/Write off). Both are
built through `PrefabUtility` rather than hand-written YAML, and are safe to re-run.

### Why the props carry an AudioSource

`InteractionAudioManager.PlaySimpleInteraction` plays through the profile's own
`customAudioSource` when it has one, and otherwise through whatever source the caller hands
it — **which it dereferences without a null check**. So each prop carries its own source,
added at runtime if the prefab has none. It also means the sound is positioned at the prop
(`spatialBlend = 1`) rather than flat in the player's head.

That is also why an eaten mushroom is hidden rather than destroyed immediately: the clip is
playing on its own source, so destroying the object would cut it off in the same frame.

## ⏸ Owner setup

> **Status 2026-09-02:** all four steps below are done and saved (confirmed by the
> headless setup pass — every tool reported already-applied). What remains is clip
> authoring: the `Mushroom Eating` / `Well` / `Well Splash` profiles and the
> `Mushroom Eaten Dialogue` lines.

1. **Place the props.** Run the two menu items above, then drag `Trip Mushroom` and `Well`
   into the scene. Any Synty mushroom works too —
   `Assets/Inbox/Low Poly Models/Nature/Prefabs/Plants/SM_Plant_Mushrooms_01…06` — given a
   **trigger** collider (`Is Trigger` ✓) and the **Mushroom Trip Interaction** component.
2. **Wire the sounds.** Run **`Tools ▸ Interactions ▸ Add Missing Audio Profiles`**. It adds
   empty **`Mushroom Eating`** and **`Well`** profiles (type `SimpleInteraction`, ids assigned) to
   the scene's Interaction Audio Manager, so the names already match what the scripts ask
   for. Then, on each: drop in a clip, set **Mixer Group Output** to **SFX**, and **save the
   scene**. Until a clip is in, the interaction fires silently and `Ctrl+J` shows a red
   `Mushroom` / `Well` miss naming exactly what to author — which is the intended workflow.
3. **Author the trip snapshot** (optional but recommended). In the Audio Mixer, add a
   second snapshot beside the existing one — e.g. heavy AUX reverb, ducked SFX, a lowpass —
   then assign it to **Trip Snapshot** on the mushroom, and the mixer's default to
   **Normal Snapshot**. Both fields are optional; with them empty the trip is silent-running
   and only the visuals change.
4. **Tune.** Every knob is on the mushroom under **Trip**, so different mushrooms can trip
   differently. `Consume On Use` off lets you re-eat the same one while tuning.

## ⚠ Collider sizing — the 145-metre bug

Both models import tiny, so the prefab roots carry **100×** scale (the well's scene instance
overrides it to 81.88, the mushroom's to 13.41). The first build of `PortedPropTools` sized
the colliders from `Renderer.bounds` — a **world-space** AABB, which already includes that
scale — and assigned it straight to `BoxCollider.size` / `SphereCollider.radius`, which are
**local** and get multiplied by `lossyScale` *again*. The scale was applied twice:

| | Intended | Actually built |
|---|---|---|
| Well blocking box | 4.7 × 3.7 × 3.5 m | **385 × 302 × 290 m**, solid |
| Well trigger | ~6.5 m | **407 m** |
| Mushroom trigger | ~5.3 m | **56 m** |

The well's box is rotated −90° on X, so its 3.54 local depth became the *vertical* 290 m —
a slab whose roof sits at **Y ≈ 144.4**. The player spawns 4.8 m from the well, inside it,
and the CharacterController's depenetration pushed them out onto that roof: standing at
**Y ≈ 145.3** (one skin width above), free to walk around up there and clip the terrain
features poking through. It reads as "the player is 145 metres tall".

`TryGetLocalBounds` now converts renderer corners into the root's own space, and
the builder now sizes the trigger to the **model only** — the player's reach is added at
runtime instead, so there is one copy of that number rather than two.
**Re-run both prefab builders** to rebuild the colliders — the scene instances do not
override them, so the corrected values propagate.

### ...and why the reach is re-applied at runtime

Rebuilding fixed the prefabs, but a prefab is built at **one** root scale (100) while the
scene instances carry their own — 81.88 for the well, **13.41** for the mushroom. Since a
`SphereCollider`'s radius is multiplied by `lossyScale`, the corrected mushroom trigger
came out at **0.56 m** in the scene: you had to stand inside it to get a prompt.

So `MushroomTripInteraction` and `PropSoundInteraction` now call
**`InteractionReach.Apply`** on `Start`, which re-derives the radius from the model's own
bounds plus an `Interaction Reach` in **metres** (1.2 m and 1.5 m), dividing `lossyScale`
back out of both. The reach then holds at any instance scale.

`InteractionReach` also owns `TryGetLocalBounds`, and `PortedPropTools` calls it rather
than keeping its own copy — runtime code cannot reference an editor assembly, but editor
code can reference runtime, and a second copy of exactly this calculation is what
produced the 385 m collider. The blocking `BoxCollider` is deliberately left alone: that
one *should* scale with the model, because it is the model's own body.

Two consequences of resizing on `Start` are handled explicitly. Resizing a collider raises
**no** `OnTriggerEnter` for something already inside it, so a player standing next to a prop
at load would get no prompt until they stepped out of range and back in — both components call
`InteractionReach.ContainsPlayer` after their prompt is resolved and seed their own in-range
state. And because the radius is stored in local units, rescaling or reparenting a prop at
runtime would leave it the world size it had at load; both expose **`RefreshReach()`** to
re-derive it.

⚠ The editor gizmo therefore shows the authored radius, not the played one.

## Mushroom eaten dialogue (added 2026-09-02)

The mushroom has a second authoring surface, **Mushroom Eaten Dialogue**: any number of
clips, one chosen at random per bite, played **Dialogue Delay** seconds (default 3) after
the interaction — the player reacting to what they just did. Playback is hosted by the
trip effect, not the mushroom, because the eaten mushroom destroys itself after its eat
sound — about when the line is due — and a coroutine dies with its host. It is 2D (the
player's own voice) and routed to the same mixer group as the eat sound.

The interaction profile was renamed **`Mushroom` → `Mushroom Eating`** at the same time,
so the slot says what to author. Re-running `Add Missing Audio Profiles` performs the
whole migration: the scene profile is renamed **in place** (clips and id untouched), and
the prefab's authored string follows via a targeted prefab edit — not a rebuild, so
authored dialogue clips survive. The enum member is now `InteractionId.MushroomEating`
(same int, so nothing re-serializes), and until the migration is run the old string still
resolves through the id alias.

## The well takes offerings (added 2026-09-02)

Drop an item within ~3 m of the rim (double-tap its number key) and `WellOffering` — added to the
prefab by **`Tools ▸ Interactions ▸ Add Well Offering`** — takes it: a short tumble, the
item vanishes, and after **Depth Seconds** of silence the **`Well Splash`** profile plays
from the well. That gap in the middle is the sound design; lengthen it and the well gets
deeper. At the next dawn the well gives the item back at the rim — or, with **Reward
Chance**, swaps it for one of the **Reward Items** (deactivated scene items the owner
assigns; each is used at most once, and the original is consumed).

The splash is an authorable slot like everything else: empty, it is a red `Well Splash`
row in Ctrl+J and an empty slot in Ctrl+G.

## Design notes (why it is not a straight copy)

- **The camera drift moves a pivot, not the camera.** A pivot is inserted directly above
  the camera at runtime with `SetParent(…, worldPositionStays: false)`, so every local value
  the camera already had is unchanged and the world pose is identical. This matters:
  `MouseLook`, the head bob and `CameraShakeController` all write the camera's own transform
  every frame, and `CameraShakeController` caches its original `localPosition` at `Awake`.
  Offsetting the camera itself would fight all three — and worse, the head bob *reads its
  own X/Z back* when the player stands still, so an added offset would compound frame on
  frame. The pivot is restored on destroy.
- **The trip outlives the mushroom.** The original kept the mushroom alive for the full 20
  seconds because the coroutine ran on it. Here the effect is a separate object, so the
  mushroom is consumed immediately.
- **…but not instantly destroyed.** The eat sound plays on the mushroom's own AudioSource,
  so destroying the object in the same frame would cut the clip off. It is hidden
  (renderers + colliders disabled) and destroyed after `Destroy Delay` seconds.
- **One trip at a time.** A second mushroom while tripping is refused; without that it
  would silently restart the fades and reset the timer, which reads as the first one having
  done nothing.
- **The prompt and sound use this project's systems** (`InteractionPromptUI`,
  `InteractionAudioManager`) rather than a per-mushroom TMP field, so the mushroom appears
  in the `Ctrl+G` progress report and the `Ctrl+J` event monitor like every other
  interaction.
- `MushroomTripEffect` is deliberately **not** `DontDestroyOnLoad`, like every other
  gameplay singleton here.
