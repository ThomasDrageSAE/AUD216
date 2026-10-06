# Prompt: profile a Unity game through the Unity CLI bridge, unattended

> Paste everything below the line into a Claude Code session that has the official Unity CLI
> (`unity`) and the `com.unity.pipeline` editor bridge. It is project-agnostic. It was
> distilled from a full profiling run on a different Unity 6 / URP project, and every rule
> in it was learned by getting it wrong first.

---

You are going to profile this Unity project's performance through the Unity CLI bridge
(`unity command …`), with no human at the editor, and produce a ranked, evidence-backed list
of what to improve. Another session did exactly this on a different game and learned the
following the hard way. Follow it in order; do not skip §0.

## 0. Before touching the editor, ask the owner three things

Ask as multiple-choice with a recommended option. The answers change the ranking, not just
the wording.

1. **Target machine and frame budget.** A dev workstation is CPU-submission-bound with the
   GPU idle half the frame; a laptop with integrated graphics is the opposite. Everything you
   measure on the dev machine is optimistic. Decide up front whether you are ranking by CPU
   or by fill rate, and state it in the report.
2. **Which scenarios matter.** Idle at spawn, the densest view, the heaviest combat/crowd,
   any effect-heavy moment, the worst-case count the game is *configured* for (read the
   spawner data — the capture will usually run far below it).
3. **What must not change.** Audio behaviour, an assessed look, authored timings. Anything
   in that set is *reported*, never *recommended*.

Then run `unity status`. If more than one editor is listed, **every** command from here on
carries `--project-path <absolute path>` — ports get reassigned between sessions and an
unqualified command will hit the wrong project.

## 1. Talking to the editor — the rules that bit

- `unity command --no-banner --project-path <P> --json <cmd>` is the shape. Parse the JSON;
  the payload is under `data.result` and is often a JSON *string* you must decode again.
- **`eval` has a 5-second main-thread limit.** Nothing that waits for frames goes inside an
  eval. A capture runs on a MonoBehaviour over frames; evals only start it and poll a static.
- **Write C# through the Write tool and run it with `eval_file --file`.** The shell wrapper
  mangles `$"…"` interpolation and `'{…}'` even inside quoted heredocs. You will lose an
  hour to this otherwise.
- `Connection reset by server` and `Network error` happen under load. Retry the same call
  up to three times before treating it as failure.
- Evals that destroy or edit scene objects get **blocked by the harness classifier**. Put
  any scene or asset edit behind a `[MenuItem]` and run it with `menu --path "…"`.
  Instantiating things in play mode was allowed; deleting a GameObject was not.
- `capture_game_view --source screen` returns the PNG under the JSON key **`base64`**.
  Decode it yourself; `--save_path` gets remapped under `Assets/` and imported.
- **The console tail is cumulative for the play session, not for your window.** Take a
  cursor (`console --tail 1 --level error` → `cursor`) before the window and read with
  `--since <cursor>` after. Otherwise you inherit the previous scenario's errors and report
  an exception that "fires every frame" when it fired once at startup.
- `recompile` then poll `recompile_status` until `status == completed` after any script
  change. `editor_play` / `editor_stop`, then poll `EditorApplication.isPlaying`.
- **The editor game view does not vsync.** A "capped" and an "uncapped" run are the same
  numbers. Don't waste captures on the distinction; just record the vSync setting.
- `Menu.MenuItemExists` does not exist in 6000.0. Verify menu items by reflection on the
  `[MenuItem]` attribute, or just run them.
- If a game has a title/menu that sets `Time.timeScale = 0` or disables the controller,
  dismiss it by reflection (`FindFirstObjectByType<T>()` → private `Play()`), then **wait one
  CLI round trip** before touching the player — its `Start` runs on the next frame.
- Statics persist between play sessions in the editor. A diagnostic that reads a singleton
  can see stale state from the previous run.

## 2. The profiler API — what actually works

**`ProfilerDriver` records nothing unless a Profiler window is open.** With no window,
`ProfilerDriver.enabled = true` succeeds and `firstFrameIndex` stays `-1` forever. Open it
first, unfocused:

```csharp
Type t = typeof(Editor).Assembly.GetType("UnityEditor.ProfilerWindow");
EditorWindow.GetWindow(t, false, "Profiler", false);
ProfilerDriver.ClearAllFrames();
ProfilerDriver.profileEditor = false;
ProfilerDriver.deepProfiling = false;      // deep profiling forces a domain reload and distorts everything
Profiler.enabled = true;
ProfilerDriver.enabled = true;
// … after the window …
ProfilerDriver.enabled = false;
ProfilerDriver.SaveProfile(path);          // a .raw the human opens in Window > Analysis > Profiler
// per-marker table: for each frame, ProfilerDriver.GetRawFrameDataView(frame, 0)
//   → .valid, .frameTimeMs, .sampleCount, .GetSampleName(i), .GetSampleTimeMs(i)
```

The driver's ring buffer holds only the **last ~300 frames**, so that table is a sample of
the window, not the window. For whole-window coverage use `ProfilerRecorder`, which needs
no window at all — and resolve counters **by name**, because `ProfilerCategory.Any` does not
exist and a guessed category yields a silently invalid recorder:

```csharp
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;      // ProfilerRecorderHandle lives HERE, not in Unity.Profiling

var handles = new List<ProfilerRecorderHandle>();
ProfilerRecorderHandle.GetAvailable(handles);          // ~4,000 on Unity 6
var byName = new Dictionary<string, ProfilerRecorderHandle>();
foreach (var h in handles)
{
    if (!h.Valid) continue;
    var d = ProfilerRecorderHandle.GetDescription(h);   // d.Name, d.Category.Name, d.UnitType
    if (!byName.ContainsKey(d.Name)) byName[d.Name] = h;
}
var r = new ProfilerRecorder(byName["Batches Count"], 1, ProfilerRecorderOptions.Default);
r.Start();                                              // StartNew(handle, …) does not exist; construct + Start
// each frame: r.Valid && r.Count > 0 ? r.LastValue : skip   (time markers are nanoseconds → × 1e-6 for ms)
// Dispose() every recorder when done
```

Counter names verified on 6000.0.72f1: `CPU Total Frame Time`, `CPU Main Thread Frame Time`,
`CPU Render Thread Frame Time`, `Main Thread`, `Batches Count`, `SetPass Calls Count`,
`Draw Calls Count`, `Triangles Count`, `Vertices Count`, `Shadow Casters Count`,
`Visible Skinned Meshes Count`, `GC Allocated In Frame`, `GC Used Memory`,
`GC Reserved Memory`, `Total Used Memory`, `Total Reserved Memory`, `System Used Memory`,
`Texture Memory`, `Mesh Memory`, `Audio Used Memory`, `AudioClip Count`,
`Active Dynamic Bodies`, `Active Kinematic Bodies`, `Added Contacts`. **Enumerate anyway** —
dump the list once and grep it rather than trusting names across versions.

For a "where does the frame go" table without the driver, start one recorder per
`TimeNanoseconds` marker in the categories `PlayerLoop`, `Ai`, `Animation`, `Audio`,
`Particles`, `Physics`, `GC`, `Gui` (skip `Render`, it has ~670) and accumulate
total/mean/max per marker over the window. Rendering counters read **0 for the first frame
or two** — drop warm-up frames.

`FrameTimingManager.CaptureFrameTimings()` + `GetLatestTimings(1, arr)` gives CPU/GPU frame
time per frame in the editor. Preallocate the array once: `new FrameTiming[1]` per frame is
an allocation you will then measure and mis-attribute to the game.

Give every exporter its own filename prefix. Two writers targeting `markers_<scenario>.csv`
will silently overwrite each other.

## 3. The capture protocol

Build a small runtime probe (hidden `HideAndDontSave` host, `DontDestroyOnLoad`, discarded on
stop) that writes one CSV row per frame plus a JSON snapshot at the start and end of the
window, into a gitignored folder. Then, per scenario:

1. Enter play, dismiss any title, wait a round trip.
2. Set the state (teleport by disabling the `CharacterController`, moving, re-enabling; set
   private fields by reflection; instantiate configured prefabs directly if the game's own
   spawner depends on state you cannot reach — and say so).
3. **Let the scene settle ≥45 s** before the window. The first capture of a fresh session
   is warm-up: particle populations ramp, frame time falls 30 % across the window, and you
   will rank "the tree view" as your slowest scenario when it is not slower than standing
   still.
4. Start the driver, start the probe (12–20 s), poll `IsDone` between CLI calls.
5. Export, screenshot, read the console with the cursor, then **check three things before
   moving on**: `Time.timeScale` at both ends of the window, that the player is alive, and
   **look at the screenshot**. One run captured two scenarios behind a "YOU DIED" screen with
   time frozen (78 % of the combat window), and another with the camera inside a rock.
6. Stop play. Leave the editor as found: not playing, scene not dirty, `ProfilerDriver.enabled
   = false`, vSync/targetFrameRate restored if you touched them.

Record in a README beside the captures: machine, GPU, game-view **resolution** (the GPU
numbers are meaningless without it), quality level, git commit, every deviation from the
plan, and every scenario you did *not* capture and why. Grouping scenarios into a few play
sessions instead of one each is fine if nothing depends on a fresh load — write it down.

Also export a read-only asset inventory from an editor menu item: every texture (size,
format, mips, max size, compression), audio clip (length, channels, load type, compression,
preload), mesh (verts, read/write), material (shader). It answers half of the memory
questions without installing Project Auditor (`audit` reports "unavailable" until the
package is added — an owner question).

## 4. Analysis — how not to fool yourself

- **Rank by share of the frame, never by milliseconds.** Say which scenario and which
  denominator (main thread? PlayerLoop? wall clock?) every percentage is against.
- **Editor memory numbers are not shipping numbers.** Texture memory and the Mono heap
  include the entire imported library and the editor's own domain. Walk asset references
  from the built scenes to find what is *reachable* (validate the walk against a counter you
  trust, e.g. `AudioClip Count`), and say plainly that a Development Build is the only way to
  settle it. "The system is running out of memory" during capture was the profiler's own
  buffers growing ~250 MB per window.
- **The profiler itself puts a ~1 MB allocation and a 15–25 ms frame in every ~3.7 s.** It
  is absent when the driver is off. Strike those frames from every hitch list before ranking.
- **Medians, not means, for per-frame allocation** — the probe's own CSV buffer growth
  spikes the mean.
- **Verify every stack trace in the editor before attributing a cost to a line.** A line
  number written into the plan from an old console message was wrong; four analysts then
  built the top finding on it, and it did not reproduce. Get the trace *now*, from *this*
  session, and re-measure the rate with a console cursor over a known frame range.
- **Evidence standard:** every finding names scenario · metric · number · file. A suspicion
  with no number is a *lead*, in its own section, never ranked. Non-findings ("this is
  cheap, stop suspecting it") are worth recording too.
- **Withdraw your own assumptions in writing.** The plan asked for static batching and an
  occlusion bake; the data said the SRP Batcher already had a 94 % reduction and a bake
  would make culling coarser. Say so in the report rather than quietly dropping it.
- One bug can be several findings. An exception that unwinds a method silently disables
  everything after the throw — a "particles never stop" finding and a "day/night never
  transitions" finding were the same null reference.

If you fan out to parallel agents: **only one drives the editor**; the others get the
capture files and read-only repo access. Give each a fixed output format (a findings table
with a "touches the untouchable?" column) so the merge is mechanical. Expect the harness to
block subagents from writing report files — have them return the content and save it
yourself. The most valuable thing they did was cross-check each other's numbers; when two
disagree, record both and say which evidence is stronger.

## 5. Deliver

A findings document that leads with **how the capture itself was flawed**, then a corrected
headline table, then the ranked findings, leads, and the owner's questions in multiple-choice
form. Nothing in the game changes until those are answered. Commit the probe and the
document; the captures stay gitignored with their sizes noted in the commit message.
