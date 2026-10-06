# Handout Mode — Work Order (parallel agents)

> Written 2026-09-10 at commit `f0da7d5`. **A work order for an implementing AI running four
> agents in parallel under one integrator**, and the record of what the system is. The owner's
> ask (2026-09-09): a mechanism that turns every audio manager to placeholder beeps for the
> student handout, and a way to reinstate the authored audio **and the mixer routing** later
> in the trimester. The authored sounds and routing stay in the project for now — they are
> the teacher's demos and exemplars — so nothing here deletes a clip.
>
> The owner's three decisions (multichoice, 2026-09-09): **routing is cleared in the handout
> too** (every `AudioMixerGroup` field on the profiles and every `outputAudioMixerGroup` on
> scene and prefab AudioSources; the tool carries a toggle); **the Music Manager's eleven slots
> are emptied**, not beeped, and the music-state loop stems stay empty; **everything else
> beeps, including the walkie-talkie demo clips and the demo heartbeat** — the Footsteps
> group with `Beep to replace - footsteps.wav`, every other slot with `Beep to replace.wav`.
>
> Read first: [CODE_STRUCTURE.md](CODE_STRUCTURE.md) (Assessment, Audio, Editor rows) and the
> STATUS block of [AUDIO_INTEGRITY_TASKS.md](AUDIO_INTEGRITY_TASKS.md), whose agent shape this
> copies: **only the integrator drives the editor**, scene edits are Undo-recorded and the
> owner saves, asset and prefab writes are logged with their `git checkout --` revert.

## The system

**One walk, two states, three tools, one proof.**

- **The walk.** `AudioProgressScanner` already discovers exactly the surface the report grades
  (scene managers, the registry's prefab-only components, the music state assets, the direct
  AudioSources). It records rows; it does not remember where a slot lives. It will now also
  hand back a **reference** per slot — the host object, the holding object and field or the
  list and index — through `WalkSlots(...)`, and the same for every `AudioMixerGroup` field
  and every AudioSource's `outputAudioMixerGroup`. `Scan()` becomes a client of that walk, so
  there is one surface, and the proof of that is a **byte-identical export** before and after.
- **The state.** `AudioHandoutState.asset` at `Assets/Audio/Handout/`: mode Authored or Handout,
  which snapshot the state came from, when, the counts at snapshot time. A banner in the
  report window and in every hand-drawn manager inspector while in Handout mode, so the owner
  never demos with beeps by accident.
- **Snapshot Authored Audio** writes a dated JSON under `Assets/Audio/Handout/Snapshots/` —
  every slot and every routing reference with two keys (a `GlobalObjectId` and the report's
  own group + label) and the referenced asset's guid. Never overwrites. **Refuses in Handout
  mode**, because that would snapshot beeps over the authored state.
- **Switch To Handout** refuses without a snapshot that matches the live state (it diffs
  first: any implemented slot or routing reference that differs from the newest snapshot is
  listed, and the tool stops — re-snapshot, then switch). Then: every slot the report lists
  gets the beep; the Footsteps group gets the footsteps beep; every slot hosted by
  `MusicManager` or a `MusicStateDefinition` is set to null; every routing reference is
  cleared when the toggle is on. Volumes, pitch ranges, loop flags, everything else: untouched.
  Scene edits are one Undo step; asset writes logged with their revert; the state flips.
- **Reinstate Authored Audio** puts every slot and routing reference back from the snapshot
  named in the state asset (or the newest), flips the state, re-runs the report, and refuses
  to call itself done unless implemented and total equal the counts recorded at snapshot time.
- **The proof** is the round trip: export → snapshot → switch → export (0 implemented, every
  routing reference null) → reinstate → export **byte-identical** to the first, routing list
  identical, and the touched prefab and asset files byte-identical on disk. A self-test menu
  item runs that loop inside one Undo group and prints the diff line count, which is 0.

Git is the second safety net: the snapshot commit and the scene at that commit are always one
checkout away, and no tool here deletes a clip file.

## Invariants

- **The report is unchanged by the refactor.** After Agent A, `Scan()` produces the same 244
  rows, same labels, same order, same export bytes. Any difference is a defect in A.
- **Nothing in the handout removes an asset.** Clips, the mixer, the demo folder all stay;
  the student copy's own trimming is a separate checklist, not this system.
- **A slot the report lists is a slot the switch touches, and no other.** `WalkSlots` and
  `Scan()` share one walk; the self-test compares the walk's count with the report's total.
- **Two keys per reference.** `GlobalObjectId` (scene) or asset guid + local id (prefab, asset)
  first; the report's group + label second. Reinstate matches on the first and falls back to
  the second, and logs every fallback.
- **Snapshot in Handout mode is refused; Switch without a matching snapshot is refused.**
- Singletons null-checked at the dereference; no `.meta` edits by hand; CRLF preserved
  (`file` before/after); `bash Documentation/tools/compile-check.sh` after every edit; scene
  edits through the tools, Undo-recorded, **the owner saves**.

## Phase 0 — the seams (integrator, before any agent starts)

Four small files so the lanes compile against each other without meeting:

- `Assets/Scripts/Assessment/AudioSlotRef.cs` — `AudioHostKind { Scene, Prefab, Asset }`,
  `AudioSlotRef` (Host, PrefabRoot, HostKind, GroupName, TypeName, ObjectName, Label,
  Container, Field, Index, Source, `Clip`, `Set(AudioClip)`), `AudioRoutingRef` (same shape,
  `Group`, `Set(AudioMixerGroup)`).
- `Assets/Scripts/Assessment/AudioHandoutState.cs` — the ScriptableObject and its constants.
- `Assets/Scripts/Editor/AudioHandoutTools.cs` — the public API as stubs
  (`ReportSurface`, `Snapshot`, `SwitchToHandout(bool clearRouting = true)`, `Reinstate`,
  each returning `Result { Success, Log, SlotsChanged, RoutingChanged, SnapshotPath }`) and
  the four menu items.
- `AudioProgressScanner.WalkSlots(Action<AudioSlotRef>, Action<AudioRoutingRef>,
  IEnumerable<GameObject> extraPrefabRoots = null)` — a stub that throws.

Compile green, committed alone.

## Agent plan

| Agent | Owns — nobody else opens these | Reads | Editor? |
|---|---|---|---|
| **A** — the walk | `Assessment/AudioProgressScanner.cs`, `Assessment/AudioSlotRef.cs` (bodies) | the report views | no |
| **B** — the tools | `Editor/AudioHandoutTools.cs`, `Editor/AudioHandoutSnapshot.cs` (new), `Assessment/AudioHandoutState.cs` (fields it needs) | `Editor/AudioSlotSurfaceTools.cs` for tool shape, `EnemyRoleSetupTools` | **no** — the integrator runs them |
| **C** — the banners | `Editor/AudioHandoutBanner.cs` (new), `Editor/AudioProgressReportWindow.cs`, the six hand-drawn editors (`Enemy`, `Footstep`, `Interaction`, `Player`, `River`, `Weapon` `AudioManagerEditor.cs`) | `AudioHandoutState` | no |
| **D** — the self-test | `Editor/AudioHandoutSelfTest.cs` (new) | `AudioHandoutTools` API, the scanner's `BuildExportText` | **no** — the integrator runs it |

## Agent A — the walk

1. Thread a *location* through `WalkObject` / `WalkValue`: the holding object and `FieldInfo`
   for a direct field, the `IList` and index for an element; and the host (`UnityEngine.Object`)
   plus host kind and prefab root from the three entry points (scene behaviours → Scene; the
   registry's components → Prefab with the prefab root; `MusicStateDefinition` → Asset with
   the definition as host).
2. `RecordSlot` builds an `AudioSlotRef` (Label = the same `TrimCategory(path)` text the row
   gets) and passes it to a sink. `Scan()`'s sink records the row exactly as today.
3. `WalkSlots(clips, routing, extraPrefabRoots)`: the same discovery as `Scan()` (registry
   first, scene behaviours, registry groups, direct AudioSources), calling the clip sink per
   slot; plus the routing sink for every `AudioMixerGroup`-typed field met by the same walk
   (before the Unity-object boundary), every scene AudioSource's `outputAudioMixerGroup`, and
   every AudioSource under the registry prefabs and under each `extraPrefabRoots` entry.
   Routing refs carry the same two-key information (Host, Label built from the object path
   and field name).
4. `AudioSlotRef.Set` / `AudioRoutingRef.Set`: `Field.SetValue(Container, value)` for a field,
   `((IList)Container)[Index] = value` for an element, `Source.clip` / `Source.outputAudioMixerGroup`
   for a direct source. No Undo, no dirtying — the caller does that. A value-type container
   cannot be written through a boxed copy: assert none exists (none is expected) and say so.
5. The report must not change: same rows, labels, order, export. Verify by reasoning; the
   integrator proves it with the export diff.

## Agent B — the tools

`Editor/AudioHandoutSnapshot.cs`: the JSON model (`takenAt`, `commit` if `git rev-parse` is
reachable, `implemented`, `total`, `slots[]`, `routing[]`; each entry: `id` (GlobalObjectId
string), `host` (a readable path: scene hierarchy or asset path, plus the component type),
`group`, `label`, `assetGuid`, `assetLocalId`, `assetName`), read/write, and the matcher
(by id, then by group + label, logging fallbacks).

`Editor/AudioHandoutTools.cs`, bodies for the four stubs, in the shape of `AudioSlotSurfaceTools`
(read-only report first; logs split into **assets written** and **scene fields set**):

1. `ReportSurface()` — read-only: the walk's counts (slots, routing) against the report's
   total, the current mode, the newest snapshot and whether the live state matches it.
2. `Snapshot()` — refuses in Handout mode. Runs the walk (extra prefab roots: every prefab
   under `Assets/Inbox/Prefabs/Enemies`, `Assets/Inbox/Prefabs/Studio equipment`, and
   `Assets/Inbox/Prefabs/BulletImpactAudioSource.prefab` — the prefabs whose AudioSources
   carry a group today), writes `Snapshots/authored_<yyyyMMdd_HHmmss>.json`, updates the
   state asset (snapshotFile, takenAt, commit, counts). Never overwrites.
3. `SwitchToHandout(clearRouting)` — refuses if no snapshot, or if any implemented slot or
   routing reference differs from the newest snapshot (lists them). Applies the rule: host is
   `FootstepAudioManager` → footsteps beep; host is `MusicManager` or a `MusicStateDefinition`
   → null; else → beep. Routing → null when the toggle is on. Scene hosts: `Undo.RecordObject`
   then `Set`, one Undo group named "Switch To Handout"; prefab hosts: `Set`, `EditorUtility.SetDirty`,
   `PrefabUtility.SavePrefabAsset(root)` (or LoadPrefabContents, B's choice — the file must be
   verified re-read); assets: `Set`, `SetDirty`, `SaveAssets`. Flips the state to Handout with
   `switchedAt` and `routingCleared`. Idempotent: a second run reports nothing to do.
4. `Reinstate()` — from the state's snapshot (or the newest): every entry matched and set
   back (loading the clip / group by guid + local id); same write paths; flips the state to
   Authored; re-scans and **fails loudly** if implemented or total differ from the snapshot's.
5. Beeps loaded from `Assets/Audio/Demo sounds/`; missing beep = refuse, never null.

## Agent C — the banners

`Editor/AudioHandoutBanner.cs`: `public static void Draw()` — nothing in Authored mode; in
Handout mode a `HelpBox` (warning) reading "HANDOUT MODE — every slot is a beep and routing is
cleared. Tools ▸ Audio ▸ Handout ▸ Reinstate Authored Audio restores the authored state from
<snapshot file> (<date>)". Called at the top of `OnInspectorGUI` in the six hand-drawn editors
and at the top of the report window's header. Reads `AudioHandoutState.LoadOrNull()` once per
GUI pass, cheap; no allocation per frame beyond the string.

## Agent D — the self-test

`Editor/AudioHandoutSelfTest.cs`, menu `Tools/Audio/Handout/Round-Trip Self-Test` (1604), also
callable as `public static Result Run()`:

1. Refuses in Handout mode. Records: export text A, the routing list (id → group guid), and
   the bytes of every prefab and asset the switch will write (the registry prefabs, the three
   extra prefab folders' prefabs, the music states).
2. `Snapshot()` → `SwitchToHandout(true)` → export B: asserts implemented == 0, every clip slot
   is a beep or null per the rule, every routing reference null, the walk's count == the
   report's total.
3. `Reinstate()` → export C: asserts C == A byte for byte (prints the unified diff line count
   and the first ten differing lines if not), routing list identical, every recorded file
   byte-identical, state back to Authored.
4. The whole run inside one Undo group; on any failure the log says exactly what differs and
   that Ctrl+Z or `git checkout --` returns the scene.

## Integrator — sequence and verification

1. Phase 0. Compile. Commit.
2. Land A. Compile. Export the report: **byte-identical** to the 2026-09-09 export (244).
3. Land B, C, D, compiling after each.
4. `Report Handout Surface`: walk count == 244, routing count logged. `Snapshot`: file
   written, state updated. Then the **self-test**: 0 diff lines. Then `Report Handout Surface`
   again: Authored, matching.
5. Inspect the switch's own output once by hand: `Switch To Handout` → report reads
   **0 / 244**, every row MISSING, every routing reference null; banner visible in the report
   window and on the Enemy manager's inspector; `Reinstate` → 174 / 244, export identical.
6. ⏸ **Owner saves the scene** — or chooses the bridge save.
7. Docs: `CODE_STRUCTURE.md` Assessment row (the walk hands back references; the state asset),
   Editor row (the three tools, the banner, the self-test); the work history; this file's
   STATUS; `README.md`'s index row.
8. Commits: Phase 0; A; B; C; D; the state asset + first snapshot; the owner-saved scene (if
   the switch was left applied — it will not be); docs. **Push only when the owner says.**

## Known risks, so nobody rediscovers them

- **`EditorJsonUtility.ToJson` stores references as instanceIDs**, which do not survive a
  restart. That is why the snapshot stores guid + local id per reference and its own ids.
- **`GlobalObjectId` for a prefab component** is the asset guid plus the component's local
  file id — stable; for a scene component it is the scene guid plus the object's file id —
  stable unless the object is recreated, which is why the second key exists.
- **Value-type containers**: reflection writes through a boxed copy vanish. The walk asserts
  none; `Set` throws if it meets one rather than silently doing nothing.
- **A prefab asset edited directly** (not through LoadPrefabContents) persists via
  `PrefabUtility.SavePrefabAsset`; B verifies by re-reading the file.
- **The beep is one clip asset shared by two hundred slots.** That is intended; the report
  classifies it as MISSING by name and by folder.
- **Snapshot freshness.** The owner authors between snapshots. Switch diffs first and refuses;
  Snapshot in Handout mode is refused; the newest authored snapshot wins.

## STATUS

**Done and verified 2026-09-10.** Four agents under one integrator, nine commits after the
order's own, every tool run against the live editor, the self-test run to eleven of eleven,
and the tools run once more by hand outside it.

### Measured, not asserted

| Check | Result |
|---|---|
| The refactored walk | Export after Agent A differs from the previous 244-row export in **0 lines** (timestamp excluded). |
| The surface | **244** clip slots (230 scene, 11 prefab, 3 asset) and **122** routing references (97 scene, 25 prefab); the walk's count equals the report's total. |
| The rule | 197 slots take the beep, 33 the footsteps beep, 14 are emptied (11 Music Manager, 3 music states). |
| Switch To Handout | 213 slots and 66 routing references changed (the rest were already a beep, null, or unrouted); report reads **0 / 244**, placeholder 230, empty 14; every routing reference null. |
| Reinstate | 213 slots and 66 routing references back; report **174 / 244**; export differs from the pre-switch export in **0 lines**; routing list identical (124 lines); all **22** prefab and asset files byte-identical on disk. |
| Self-test | **11 / 11** on the second run (the first run found two things, below). |
| Banner | `AudioHandoutBanner.IsHandout` true in Handout mode; the report window's title reads "Audio Progress — HANDOUT" and returns to "Audio Progress". |
| Snapshot | `Snapshots/authored_20260910_010636.json`: 244 slots, 122 routing references, 174/244 implemented, commit `39cc658`. The state asset names it. |

### What the first run found

| Finding | What was done |
|---|---|
| Three `Mushroom` siblings under one parent share a hierarchy path, and `FindObjectsByType` returns them in no fixed order, so the routing list came back with the same three lines shuffled. | The sweep breaks the tie on sibling indices, root to leaf. |
| Ten prefabs no tool had saved under Unity 6 (six unused enemies, the rod, the two speakers, the bullet-impact source) were **reserialised** on the first round trip: `m_audioClip` became `m_Resource` with the same guid, the rod lost five keys no field declares, a few new fields appeared, CRLF became LF. | Verified format-only (no clip, group or value changed) and committed as its own commit. The second round trip found all 22 files identical. |
| git checks these files out with CRLF and Unity writes LF, so a file Unity merely re-saved differed in every line ending. | The self-test compares with line endings normalised - what git compares. |

### How to use it

- **Before the handout:** `Tools ▸ Audio ▸ Handout ▸ Snapshot Authored Audio` (it refuses if the
  project is already in Handout mode), then `Switch To Handout`, then **save the scene**. The
  report reads 0 / 244 and a warning banner sits on every manager inspector and the report window.
- **To get the authored audio back:** `Reinstate Authored Audio`, then save the scene. It reads
  the snapshot named in `Assets/Audio/Handout/AudioHandoutState.asset` and refuses to call itself
  done unless the report reads what the snapshot recorded.
- **After authoring new sounds:** take a new snapshot before switching; `Switch` refuses if any
  implemented slot or routing reference differs from the newest one, and lists them.
- **To prove it still works:** `Round-Trip Self-Test` - eleven PASS lines, one Ctrl+Z if not.
- The snapshot folder accumulates one file per snapshot (the self-test takes one each run);
  older files can be deleted, the state asset names the one that matters.

### ⏸ What still needs the owner

The scene is **dirty in editor memory** with net-zero changes (the switch and the reinstate
cancelled out). Saving it is harmless and writes nothing new; discarding it is equally fine.

### Not this system

The student copy's own trimming - the owner's WAV files, `com.unity.pipeline` and `.mcp.json`,
the Exports folder, the snapshot folder (which must not travel), the editor-only probes - is a
separate, short checklist for handout day.
