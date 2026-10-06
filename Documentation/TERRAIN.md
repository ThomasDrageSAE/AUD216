# Terrain & Asset-Reference Guide — 26T1 AUD216 Project 1

> Written 2026-08-17 after recovering the terrain textures (see the incident record
> at the bottom). Read this before touching terrain assets, `.meta` files, or
> anything that looks like generated/junk folders.

## Terrain setup in `Level for Sound Design.unity`

| Scene object | TerrainData asset | Purpose |
|---|---|---|
| `Terrain - Main area` | `Assets/Inbox/Low Poly Models/Nature/Scenes/Terrains/Terrain_Main_Demo.asset` (guid `64b9bb98…`) | The playable island (Synty POLYGON Nature demo terrain) |
| `Terrain - Footsteps area` | `Assets/Inbox/Tidy/New Terrain.asset` (guid `919ee252…`) | Painted strips of every surface type, used to test footstep audio |

Both terrains render with URP's built-in `TerrainLit` material
(`m_MaterialTemplate` guid `594ea882…` lives in the URP **package**, not in
`Assets/` — its absence from the project is normal). Two further TerrainData
assets exist (`…/Terrains/NewTerrainData.asset`, `Assets/Inbox/Tidy/TerrainData_63a65054….asset`);
they are used by the Synty demo scenes / legacy copies and share the same layers.

## Terrain layers (the "textures")

All terrain layers live in
`Assets/Inbox/Low Poly Models/Nature/Scenes/Terrains/TerrainLayers/` (36 files).
The source textures are in `…/Nature/Textures/Ground_Textures/`.
**Layer order inside each TerrainData is data, not cosmetics** — the splatmaps
and the footstep code below both index into it.

### `Terrain - Main area` (Terrain_Main_Demo) — 17 layers, in slot order

| # | Layer file (`layer_<diffuse><normal><propsHash>`) | Diffuse + normal | Tile |
|---|---|---|---|
| 0 | layer_GrassBaseGrass_normals2023054508611406 | Grass + BaseGrass_normals | 5 |
| 1 | layer_RockwallRockwall_Normal2023054508611406 | Rockwall + Rockwall_Normal | 5 |
| 2 | layer_PebblesPebbles_normals2023054508611406 | Pebbles + Pebbles_normals | 5 |
| 3 | layer_Pebbles_SandPebbles_normalsb8875f45e9c15431 | SandPebbles + Pebbles_normals | 2.5 |
| 4 | layer_RockwallRockwall_Normalb3407d0e55802f81 | Rockwall + Rockwall_Normal | 2 |
| 5 | layer_FlowersFlowers_normals793646612c42751 | Flowers + Flowers_normals | 4 |
| 6 | layer_LeavesLeaves_normals_01b3407d0e55802f81 | Leaves + Leaves_normals_01 | 2 |
| 7 | layer_Leaves_02Leaves_normals_01b3407d0e55802f81 | Leaves_02 + Leaves_normals_01 | 2 |
| 8 | layer_SandGrassHigherDetail_normals2023054508611406 | Sand + GrassHigherDetail_normals | 5 |
| 9 | layer_SnowGrassHigherDetail_normalsb8875f45e9c15431 | Snow + GrassHigherDetail_normals | 2.5 |
| 10 | layer_MudMud_Normal2e716cd1dfbd9e5a | Mud + Mud_Normal | 4 (metallic 0.3, smooth 0.64) |
| 11 | layer_Sand_DarkerBaseGrass_normals2023054508611406 | Sand_Darker + BaseGrass_normals | 5 |
| 12 | layer_Grass_02BaseGrass_normals2023054508611406 | Grass_02 + BaseGrass_normals | 5 |
| 13 | layer_Leaves_03Leaves_normals_01b3407d0e55802f81 | Leaves_03 + Leaves_normals_01 | 2 |
| 14 | layer_Leaves_04Leaves_normals_01b3407d0e55802f81 | Leaves_04 + Leaves_normals_01 | 2 |
| 15 | layer_Pebbles_SandPebbles_normals36c77d22be2d3f9a | SandPebbles + Pebbles_normals | 2 (metallic 0.5, smooth 0.37) |
| 16 | layer_Sand_DesertBaseGrass_normals2023054508611406 | Sand_Desert + BaseGrass_normals | 5 |

### `Terrain - Footsteps area` (New Terrain) — 17 layers, in slot order

The slot **index is hardcoded** in
`Assets/Scripts/Footsteps/TerrainTextureDetector.cs` (`MapTextureToCategory`);
reordering layers on this terrain silently breaks footstep surface detection.

| Index | Layer | Surface category |
|---|---|---|
| 0 | Grass_1 | grass |
| 1 | Rockwall1 | rock |
| 2 | Grass_5 | grass |
| 3 | Sand_3 | sand |
| 4 | Rockwall2 | rock |
| 5 | Grass_3 | grass |
| 6 | Grass_4 | grass |
| 7 | Grass_8 | grass |
| 8 | Sand_1 | sand |
| 9 | Snow | snow |
| 10 | Swamp | swamp |
| 11 | Sand_6 | sand |
| 12 | Grass_2 | grass |
| 13 | Grass_7 | grass |
| 14 | Grass_6 | grass |
| 15 | Sand_2 | sand |
| 16 | Sand_7 | sand |

(`Sand_4.terrainlayer` / `Sand_5.terrainlayer` also exist as spares from the
original set; no terrain currently references them.)

Footstep audio chain: `FirstPersonController` → `SurfaceDetector.GetSurfaceType`
(tags `Wood`/`Rock`/`Water`/`Swamp` win over terrain) →
`TerrainTextureDetector.MapTextureToCategory(index)` →
`FootstepAudioManager.PlayFootstep(surface)`.

## How Unity references assets — rules for AI/CLI edits

- Every asset's identity is the `guid` in its `.meta` file. Scenes and other
  **text** assets reference it as `{fileID: …, guid: …}` — find users of an asset
  with `grep -r <guid> Assets`.
- **Binary** assets (TerrainData `.asset` files here are binary) hold their
  references in an internal externals table — `grep` will NOT find the GUIDs
  they use. Absence of grep hits does not mean an asset is unused.
- Moving a file is safe only if its `.meta` moves with it (Unity does this
  in-editor). Deleting a `.meta`, or deleting an asset that a binary file
  references, breaks things with **no compile error** — the failure is visual
  (missing textures) or silent.
- Folders with generated-looking names can be load-bearing.
  `_TerrainAutoUpgrade*` folders hold real `.terrainlayer` assets that Unity
  created when upgrading old terrains — they are referenced by TerrainData and
  must never be deleted (that is exactly what caused the July 2026 texture loss).
- `Assets/Inbox/Tidy/` is a parking lot but **not** all junk:
  `New Terrain.asset` (footsteps terrain) lives there and is live in the main
  scene. Check references before deleting anything in it.

## Incident record — terrain texture loss & recovery

**Symptom:** all terrain surfaces (grass, dirt, snow, …) rendered untextured in
`Level for Sound Design`.

**Cause:** the pre-baseline hygiene pass (July 2026) deleted the
`_TerrainAutoUpgrade` / `_TerrainAutoUpgrade2` folders, following the original
Phase 0 cleanup note that wrongly listed them as junk. They contained all 34
`.terrainlayer` assets referenced (by GUID) from the binary TerrainData files.
Nothing in git history ever contained them (deleted before the baseline commit),
and Unity kept rendering from cached Library data for a while, which is why the
loss surfaced later.

**Recovery (2026-08-17):** rebuilt every layer at
`…/Terrains/TerrainLayers/` with the **original GUIDs**, so the untouched binary
TerrainData files reconnect without modification:

- GUID → filename mapping recovered from `Library/SourceAssetDB` / `ArtifactDB`
  (Unity's asset database still remembered the deleted paths).
- The 19 named layers (Grass_1…, Sand_1…, Snow, Swamp, Rockwall1/2) were copied
  verbatim (files + `.meta` GUIDs matched exactly) from the sibling project
  `~/Desktop/TIDY/AUD216 P2 PROJECTS/Felix/_Horror_Mansion_`, which shares the
  same Synty asset lineage.
- The 17 `layer_*` auto-upgrade layers were regenerated: texture pairs are
  encoded in their filenames, and tile/metallic/smoothness values were
  recovered via the property-hash suffix in each filename, cross-validated
  slot-by-slot against Felix's equivalents of the same terrain.
- Verified programmatically: every external reference of all four TerrainData
  assets now resolves; no duplicate GUIDs; every layer file carries the anchor
  `&8574412962073106934` the TerrainData PPtrs expect.
- **Confirmed in the Unity editor (2026-08-17): terrains render their textures
  as before the loss.**

If textures ever vanish again: **do not repaint or reassign layers** — first
check whether `.terrainlayer` files or their `.meta` GUIDs are missing, and
restore by GUID as above.
