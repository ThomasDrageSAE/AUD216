using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/*
 * AudioHandoutTools.cs
 *
 * Tools/Audio/Handout: the three operations that move the project between its two audio
 * states, and a read-only report of the surface they act on. See
 * Documentation/AUDIO_HANDOUT_TASKS.md for the system and the rules.
 *
 *   Report Handout Surface   - read-only: the walk's counts against the report, mode, snapshot freshness
 *   Snapshot Authored Audio  - dated JSON of every slot and routing reference; refused in Handout mode
 *   Switch To Handout        - beeps / empties every listed slot, clears routing; refused without a matching snapshot
 *   Reinstate Authored Audio - every slot and routing reference back from the snapshot; verified by the report's counts
 *
 * Used by: the integrator, driven from the Unity CLI. The owner saves the scene afterwards.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ NO DIALOGS anywhere in this file
 * ---------------------------------------------------------------------------------------
 * Every item here is executed headless through the CLI bridge, and EditorUtility.DisplayDialog
 * would block it with a modal window nobody can see, let alone dismiss. A refusal is a
 * Result with Success = false and a Log that says why - same shape as AudioSlotSurfaceTools
 * and EnemyRoleSetupTools next door.
 *
 * ---------------------------------------------------------------------------------------
 * THE RULE THE SWITCH APPLIES (the owner's decisions, 2026-09-09)
 * ---------------------------------------------------------------------------------------
 *   host is Audio.FootstepAudioManager          -> "Beep to replace - footsteps.wav"
 *   host is MusicManager or MusicStateDefinition -> null (the eleven music slots are EMPTIED,
 *                                                  not beeped, and the loop stems stay empty)
 *   every other slot                            -> "Beep to replace.wav"
 *   every AudioMixerGroup reference             -> null, when clearRouting is on
 *
 * NOTHING ELSE MOVES. Volumes, pitch ranges, loop flags, array sizes, profile names: the
 * switch reads and writes exactly the references AudioProgressScanner.WalkSlots hands it,
 * one value each, and touches nothing beside them. The rule reads the host's TYPE, not a
 * name string, so renaming a GameObject cannot silently reroute a slot to the wrong value.
 *
 * ---------------------------------------------------------------------------------------
 * THREE KINDS OF WRITE, AND ONLY ONE OF THEM IS UNDOABLE
 * ---------------------------------------------------------------------------------------
 * SCENE hosts (the audio managers on the Audio Managers object, the waterfall's AudioSource)
 * are Undo.RecordObject'd once each, changed, and collapsed into ONE undo step. ⚠ THE OWNER
 * MUST SAVE THE SCENE or the switch is lost on the next reload.
 * PREFAB hosts (the boss, the rod, the enemy and studio prefabs, the bullet-impact source)
 * are changed in memory, marked dirty, and written with PrefabUtility.SavePrefabAsset once
 * per prefab file.
 * ASSET hosts (the three MusicStateDefinition ScriptableObjects) are changed, marked dirty
 * and flushed with AssetDatabase.SaveAssets.
 * ⚠ For those last two Ctrl+Z does NOTHING - `git checkout -- <path>` is the way back, and
 * the log names every path it wrote with exactly that command.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE PREFAB WRITE VERIFIES ITSELF
 * ---------------------------------------------------------------------------------------
 * Editing a component on a prefab ASSET directly (rather than through
 * PrefabUtility.LoadPrefabContents) and saving it with PrefabUtility.SavePrefabAsset is the
 * documented path, and it is the one used here because it keeps one code path for all three
 * host kinds and never opens a preview scene. It is also the one that fails SILENTLY if it
 * ever stops working, so after saving, each written file is RE-READ FROM DISK and the guid
 * of every clip written into it is counted in the YAML. Too few occurrences and the tool
 * says so and fails, naming LoadPrefabContents / SaveAsPrefabAsset / UnloadPrefabContents
 * (the path AudioSlotSurfaceTools uses for the boss) as the fix. A reference CLEARED to null
 * leaves no guid to look for, so those are reported as verified only by `git diff`.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ Binds to types, not to names: Audio.FootstepAudioManager, MusicManager,
 * MusicStateDefinition, and the two beep paths below. Moving or renaming a beep file breaks
 * the switch loudly (it refuses); renaming one of those three CLASSES compiles clean and
 * changes which slots are emptied instead of beeped - see CODE_STRUCTURE's editor-tooling
 * warning.
 */
public static class AudioHandoutTools
{
    public const string BeepPath = "Assets/Audio/Demo sounds/Beep to replace.wav";
    public const string FootstepBeepPath = "Assets/Audio/Demo sounds/Beep to replace - footsteps.wav";

    private const string LogPrefix = "[AudioHandoutTools]";
    private const string SwitchUndoLabel = "Switch To Handout";
    private const string ReinstateUndoLabel = "Reinstate Authored Audio";
    private const string SnapshotUndoLabel = "Snapshot Authored Audio";

    /// <summary>
    /// The prefabs whose AudioSources carry a mixer group today and which no scene walk and
    /// no registry entry reaches. Folders, not a name list, so a prefab added to either one
    /// is in the next snapshot without an edit here. The bullet-impact source is the one
    /// loose file, named because it is the one loose file.
    /// </summary>
    private static readonly string[] ExtraPrefabFolders =
    {
        "Assets/Inbox/Prefabs/Enemies",
        "Assets/Inbox/Prefabs/Studio equipment",
    };

    private const string BulletImpactPrefabPath = "Assets/Inbox/Prefabs/BulletImpactAudioSource.prefab";

    /// <summary>How many differences a refusal spells out before it says "and N more".</summary>
    private const int MaxListedDifferences = 40;

    // Difference kinds, so the refusal groups them and the reader can see at a glance
    // whether the project GREW a slot or whether a clip was re-authored.
    private const string KindSlotChanged = "IMPLEMENTED SLOT CHANGED SINCE THE SNAPSHOT";
    private const string KindSlotNew = "SLOT THE SNAPSHOT DOES NOT KNOW";
    private const string KindSlotGone = "SLOT THE SNAPSHOT HOLDS AND THE WALK NO LONGER FINDS";
    private const string KindRoutingChanged = "ROUTING REFERENCE CHANGED SINCE THE SNAPSHOT";
    private const string KindRoutingNew = "ROUTING REFERENCE THE SNAPSHOT DOES NOT KNOW";
    private const string KindRoutingGone = "ROUTING REFERENCE THE SNAPSHOT HOLDS AND THE WALK NO LONGER FINDS";

    /// <summary>What one operation did, for the console and for the self-test.</summary>
    public sealed class Result
    {
        public bool Success;
        public string Log = "";
        public int SlotsChanged;
        public int RoutingChanged;
        /// <summary>The snapshot written or read, project-relative.</summary>
        public string SnapshotPath;

        public override string ToString() => (Success ? "OK" : "FAILED") + "\n" + Log;
    }

    [MenuItem("Tools/Audio/Handout/Report Handout Surface", priority = 1600)]
    private static void ReportSurfaceMenu() => Report(ReportSurface());

    [MenuItem("Tools/Audio/Handout/Snapshot Authored Audio", priority = 1601)]
    private static void SnapshotMenu() => Report(Snapshot());

    [MenuItem("Tools/Audio/Handout/Switch To Handout", priority = 1602)]
    private static void SwitchMenu() => Report(SwitchToHandout(true));

    [MenuItem("Tools/Audio/Handout/Reinstate Authored Audio", priority = 1603)]
    private static void ReinstateMenu() => Report(Reinstate());

    /// <summary>
    /// A refusal is a WARNING, not an Error: it means the tool declined to act, the project
    /// is untouched, and the log says what to do about it. An error entry would look like a
    /// crash in a console the integrator is already reading for real ones.
    /// </summary>
    private static void Report(Result result)
    {
        if (result.Success) Debug.Log(result.ToString());
        else Debug.LogWarning(result.ToString());
    }

    // ==========================================================================
    //  1. Report - reads and prints, changing nothing
    // ==========================================================================

    /// <summary>
    /// Read-only. The walk's counts against the progress report's total, the current mode,
    /// the newest snapshot, and whether the live state still matches it. Success is false
    /// when the walk could not run, when a beep is missing, or when the walk and the report
    /// disagree about how many slots there are - the three things that stop the switch.
    /// </summary>
    public static Result ReportSurface()
    {
        var result = new Result();
        var log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Report Handout Surface (read only - nothing was changed).");

        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        if (state == null)
        {
            log.AppendLine($"MODE: <no state asset at {AudioHandoutState.AssetPath}> - the project has never " +
                           "been snapshotted or switched. Snapshot Authored Audio creates it, in Authored mode.");
        }
        else
        {
            log.AppendLine($"MODE: {state.mode}" + (state.IsHandout
                ? $"   ⚠ switched {Or(state.switchedAt, "at an unrecorded time")}, routing cleared: {state.routingCleared}"
                : ""));
            log.AppendLine($"STATE'S SNAPSHOT: {Or(state.snapshotFile, "<none recorded>")} " +
                           $"(taken {Or(state.snapshotTakenAt, "?")}, commit {Or(state.snapshotCommit, "unknown")}, " +
                           $"{state.implementedAtSnapshot}/{state.totalAtSnapshot} implemented, " +
                           $"{state.routingAtSnapshot} routing reference(s))");
        }

        bool beepsOk = TryLoadBeeps(out AudioClip beep, out AudioClip footstepBeep, out string beepError);
        log.AppendLine(beepsOk
            ? $"BEEPS: '{beep.name}' and '{footstepBeep.name}' are both in place."
            : "BEEPS: ⚠ " + beepError);

        Surface surface = Walk(out string walkError);
        if (surface == null)
        {
            log.AppendLine("WALK: " + walkError);
            log.AppendLine("Nothing further can be reported until AudioProgressScanner.WalkSlots walks " +
                           "(Agent A's lane). Every other item under Tools/Audio/Handout will refuse the same way.");
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        log.AppendLine($"EXTRA PREFAB ROOTS ({surface.ExtraRoots.Count}) handed to the walk - the prefabs no " +
                       "scene walk and no registry entry reaches:");
        foreach (GameObject root in surface.ExtraRoots)
        {
            log.AppendLine("  " + AssetDatabase.GetAssetPath(root));
        }
        foreach (string note in surface.Notes) log.AppendLine("  " + note);

        log.AppendLine($"WALK: {surface.Slots.Count} clip slot(s), {surface.Routing.Count} mixer-group reference(s).");
        log.AppendLine("  slots by host: " + HostKindTally(surface.Slots.ConvertAll(slot => slot.HostKind)));
        log.AppendLine("  routing by host: " + HostKindTally(surface.Routing.ConvertAll(routing => routing.HostKind)));

        int beeps = 0, footsteps = 0, empties = 0;
        foreach (AudioSlotRef slot in surface.Slots)
        {
            switch (RuleFor(slot))
            {
                case HandoutValue.FootstepBeep: footsteps++; break;
                case HandoutValue.Empty: empties++; break;
                default: beeps++; break;
            }
        }
        log.AppendLine($"HANDOUT RULE: {beeps} slot(s) would take the beep, {footsteps} the footstep beep, " +
                       $"{empties} would be EMPTIED (MusicManager / MusicStateDefinition hosts).");

        AudioProgressReport report = AudioProgressScanner.Scan();
        bool countsAgree = surface.Slots.Count == report.Total;
        log.AppendLine(countsAgree
            ? $"REPORT: {report.Implemented}/{report.Total} implemented - the walk's slot count MATCHES the report's total."
            : $"REPORT: {report.Implemented}/{report.Total} implemented - ⚠ THE WALK SEES {surface.Slots.Count} " +
              $"SLOT(S) AND THE REPORT {report.Total}. A slot the report lists is a slot the switch touches, and " +
              "no other; one of the two walks has drifted. Do not switch until they agree.");

        List<string> snapshots = AudioHandoutSnapshot.AllPaths();
        if (snapshots.Count == 0)
        {
            log.AppendLine($"SNAPSHOTS: none in {AudioHandoutState.SnapshotFolder}. Switch To Handout will " +
                           "refuse until Snapshot Authored Audio has run.");
        }
        else
        {
            result.SnapshotPath = snapshots[0];
            AudioHandoutSnapshot newest = AudioHandoutSnapshot.Read(snapshots[0], out string readError);

            if (newest == null)
            {
                log.AppendLine($"SNAPSHOTS: {snapshots.Count} on disk, but the newest could not be read - {readError}");
            }
            else
            {
                log.AppendLine($"SNAPSHOTS: {snapshots.Count} on disk. Newest: {newest.Describe(snapshots[0])}");
                if (state != null && !string.IsNullOrEmpty(state.snapshotFile) && state.snapshotFile != snapshots[0])
                {
                    log.AppendLine($"  ⚠ the state asset still names '{state.snapshotFile}'. Switch To Handout " +
                                   "diffs against the NEWEST and re-points the state at it; Reinstate reads " +
                                   "whichever the state names at the time.");
                }

                List<Difference> differences = Diff(surface, newest, out List<string> fallbacks);
                AppendFallbacks(log, fallbacks);

                if (differences.Count == 0)
                {
                    log.AppendLine("FRESHNESS: the live state MATCHES the newest snapshot. Switch To Handout will run.");
                }
                else if (state != null && state.IsHandout)
                {
                    log.AppendLine($"FRESHNESS: {differences.Count} difference(s) - EXPECTED, the project is in " +
                                   "Handout mode and the snapshot records the authored state. Reinstate Authored " +
                                   "Audio is what closes them.");
                    AppendDifferences(log, differences);
                }
                else
                {
                    log.AppendLine($"FRESHNESS: ⚠ {differences.Count} difference(s) - Switch To Handout WILL REFUSE " +
                                   "until a new snapshot is taken:");
                    AppendDifferences(log, differences);
                }
            }
        }

        result.Success = beepsOk && countsAgree;
        result.Log = log.ToString().TrimEnd();
        return result;
    }

    // ==========================================================================
    //  2. Snapshot
    // ==========================================================================

    /// <summary>
    /// Writes Assets/Audio/Handout/Snapshots/authored_&lt;stamp&gt;.json and points the state
    /// asset at it. ⚠ REFUSED IN HANDOUT MODE: a snapshot taken over the beeps would record
    /// them as the authored state and throw away the only way back.
    /// </summary>
    public static Result Snapshot()
    {
        var result = new Result();
        var log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Snapshot Authored Audio.");

        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        if (state != null && state.IsHandout)
        {
            log.AppendLine("REFUSED: the project is in HANDOUT mode. Snapshotting now would record the beeps as " +
                           "the authored state and throw the real one away. Run Tools/Audio/Handout/Reinstate " +
                           "Authored Audio first, check the report reads what it should, then snapshot.");
            log.AppendLine($"The snapshot the state names: {Or(state.snapshotFile, "<none>")} " +
                           $"({Or(state.snapshotTakenAt, "?")}).");
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        Surface surface = Walk(out string walkError);
        if (surface == null)
        {
            log.AppendLine("REFUSED: " + walkError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        foreach (string note in surface.Notes) log.AppendLine("  " + note);

        AudioProgressReport report = AudioProgressScanner.Scan();
        if (surface.Slots.Count != report.Total)
        {
            log.AppendLine($"⚠ the walk sees {surface.Slots.Count} slot(s) and the report {report.Total}. The " +
                           "snapshot records the WALK, so it is still complete, but the two must agree before " +
                           "Switch To Handout is trusted.");
        }

        // One reading of the clock for both the file name and the recorded time, so the two
        // can never straddle a second boundary and disagree.
        DateTime takenAt = DateTime.Now;

        var snapshot = new AudioHandoutSnapshot
        {
            takenAt = takenAt.ToString(AudioHandoutSnapshot.TimeFormat, System.Globalization.CultureInfo.InvariantCulture),
            commit = AudioHandoutSnapshot.GitShortHead(),
            implemented = report.Implemented,
            total = report.Total
        };

        foreach (AudioSlotRef slot in surface.Slots) snapshot.slots.Add(AudioHandoutSnapshot.EntryFor(slot));
        foreach (AudioRoutingRef routing in surface.Routing) snapshot.routing.Add(AudioHandoutSnapshot.EntryFor(routing));

        EnsureFolders();

        string path = AudioHandoutSnapshot.PathFor(takenAt);
        if (!AudioHandoutSnapshot.Write(snapshot, path, out string writeError))
        {
            log.AppendLine("REFUSED: " + writeError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        state = AudioHandoutState.LoadOrCreate();
        Undo.RecordObject(state, SnapshotUndoLabel);
        state.mode = AudioHandoutMode.Authored;
        state.snapshotFile = path;
        state.snapshotTakenAt = snapshot.takenAt;
        state.snapshotCommit = snapshot.commit;
        state.implementedAtSnapshot = snapshot.implemented;
        state.totalAtSnapshot = snapshot.total;
        state.routingAtSnapshot = snapshot.routing.Count;
        state.switchedAt = "";
        EditorUtility.SetDirty(state);
        AssetDatabase.SaveAssets();

        log.AppendLine("⚠ ASSETS WRITTEN (2):");
        log.AppendLine($"  {path}: {snapshot.slots.Count} slot(s) + {snapshot.routing.Count} routing reference(s), " +
                       $"{snapshot.implemented}/{snapshot.total} implemented, commit " +
                       $"{Or(snapshot.commit, "unknown (git was not reachable - the snapshot is still valid)")}. " +
                       $"REVERT: git checkout -- \"{path}\"");
        log.AppendLine($"  {AudioHandoutState.AssetPath}: now names that snapshot. " +
                       $"REVERT: git checkout -- \"{AudioHandoutState.AssetPath}\"");
        log.AppendLine("SCENE FIELDS SET (0) - a snapshot reads, it never writes into the scene.");
        log.AppendLine("A snapshot is NEVER overwritten. Take another whenever the authored audio changes; " +
                       "Switch To Handout diffs against the newest and refuses if it is stale.");

        result.Success = true;
        result.SnapshotPath = path;
        result.Log = log.ToString().TrimEnd();
        return result;
    }

    // ==========================================================================
    //  3. Switch To Handout
    // ==========================================================================

    /// <summary>
    /// Puts the beep (or, for the music, nothing) into every slot the report lists, and
    /// clears every mixer-group reference when <paramref name="clearRouting"/> is on.
    /// Refuses without a snapshot, and refuses when the live state has moved away from the
    /// newest one. Idempotent: a second run in Handout mode reports nothing to do.
    /// </summary>
    public static Result SwitchToHandout(bool clearRouting = true)
    {
        var result = new Result();
        var log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Switch To Handout (routing: {(clearRouting ? "CLEARED" : "left exactly as it is")}).");

        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        if (state != null && state.IsHandout)
        {
            log.AppendLine($"NOTHING TO DO - the project is already in HANDOUT mode (switched " +
                           $"{Or(state.switchedAt, "at an unrecorded time")}, routing cleared: {state.routingCleared}). " +
                           "Reinstate Authored Audio is the way back; Report Handout Surface shows what is live.");
            result.Success = true;
            result.SnapshotPath = state.snapshotFile;
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        if (!TryLoadBeeps(out AudioClip beep, out AudioClip footstepBeep, out string beepError))
        {
            log.AppendLine("REFUSED: " + beepError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        // The NEWEST snapshot, not the one the state names: the owner authors between
        // snapshots and the newest is the only one that can still describe the live state.
        string snapshotPath = AudioHandoutSnapshot.NewestPath();
        if (snapshotPath == null)
        {
            log.AppendLine($"REFUSED: no snapshot in {AudioHandoutState.SnapshotFolder}. Switching without one " +
                           "would leave no way back to the authored audio. Run Tools/Audio/Handout/Snapshot " +
                           "Authored Audio first.");
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        result.SnapshotPath = snapshotPath;
        if (state != null && !string.IsNullOrEmpty(state.snapshotFile) && state.snapshotFile != snapshotPath)
        {
            log.AppendLine($"NOTE: the state asset names '{state.snapshotFile}'; the newest on disk is " +
                           $"'{snapshotPath}', and that is the one diffed against and recorded.");
        }

        AudioHandoutSnapshot snapshot = AudioHandoutSnapshot.Read(snapshotPath, out string readError);
        if (snapshot == null)
        {
            log.AppendLine("REFUSED: " + readError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        Surface surface = Walk(out string walkError);
        if (surface == null)
        {
            log.AppendLine("REFUSED: " + walkError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        foreach (string note in surface.Notes) log.AppendLine("  " + note);

        List<Difference> differences = Diff(surface, snapshot, out List<string> fallbacks);
        AppendFallbacks(log, fallbacks);

        if (differences.Count > 0)
        {
            log.AppendLine($"REFUSED: {differences.Count} difference(s) between the live audio and " +
                           $"'{snapshotPath}' ({snapshot.takenAt}). Switching now would beep over authored work " +
                           "the snapshot cannot put back. Run Snapshot Authored Audio, then switch.");
            AppendDifferences(log, differences);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        log.AppendLine($"Live state matches '{snapshotPath}' ({snapshot.slots.Count} slot(s), " +
                       $"{snapshot.routing.Count} routing reference(s)). Applying the handout rule.");

        var session = new WriteSession(SwitchUndoLabel);
        int beeped = 0, footstepped = 0, emptied = 0, slotsAlready = 0, routingAlready = 0;

        foreach (AudioSlotRef slot in surface.Slots)
        {
            HandoutValue rule = RuleFor(slot);
            AudioClip want = rule == HandoutValue.Empty
                ? null
                : rule == HandoutValue.FootstepBeep ? footstepBeep : beep;

            AudioClip have = slot.Clip;
            string where = $"{slot.GroupName} ▸ {slot.Label}";

            if (have == want) { slotsAlready++; continue; }
            if (!session.Prepare(slot.Host, slot.HostKind, where)) continue;

            try { slot.Set(want); }
            catch (Exception e) { session.RecordFailure(where, e); continue; }

            session.Applied(slot.Host, slot.HostKind, slot.PrefabRoot,
                            $"{where}: {Describe(have)} -> {Describe(want)}", want);

            switch (rule)
            {
                case HandoutValue.FootstepBeep: footstepped++; break;
                case HandoutValue.Empty: emptied++; break;
                default: beeped++; break;
            }
            result.SlotsChanged++;
        }

        if (clearRouting)
        {
            foreach (AudioRoutingRef routing in surface.Routing)
            {
                AudioMixerGroup have = routing.Group;
                string where = $"{routing.GroupName} ▸ {routing.Label}";

                if (have == null) { routingAlready++; continue; }
                if (!session.Prepare(routing.Host, routing.HostKind, where)) continue;

                try { routing.Set(null); }
                catch (Exception e) { session.RecordFailure(where, e); continue; }

                session.Applied(routing.Host, routing.HostKind, routing.PrefabRoot,
                                $"{where}: {Describe(have)} -> <none>", null);
                result.RoutingChanged++;
            }
        }

        state = AudioHandoutState.LoadOrCreate();
        Undo.RecordObject(state, SwitchUndoLabel);
        state.mode = AudioHandoutMode.Handout;
        state.switchedAt = AudioHandoutSnapshot.NowIso();
        state.routingCleared = clearRouting;

        // The state is pointed at the snapshot the switch actually VERIFIED against, which
        // is the newest. Without this, a state still naming an older file would send
        // Reinstate to a snapshot nothing here ever checked.
        state.snapshotFile = snapshotPath;
        state.snapshotTakenAt = snapshot.takenAt;
        state.snapshotCommit = snapshot.commit;
        state.implementedAtSnapshot = snapshot.implemented;
        state.totalAtSnapshot = snapshot.total;
        state.routingAtSnapshot = snapshot.routing.Count;
        EditorUtility.SetDirty(state);

        session.Finish();
        session.AppendLog(log, AudioHandoutState.AssetPath);

        log.AppendLine($"TOTALS: {result.SlotsChanged} slot(s) changed ({beeped} beep, {footstepped} footstep beep, " +
                       $"{emptied} emptied), {result.RoutingChanged} routing reference(s) cleared. " +
                       $"{slotsAlready} slot(s) and {routingAlready} routing reference(s) already held the " +
                       "handout value and were left alone.");
        log.AppendLine($"STATE: mode = Handout, switchedAt = {state.switchedAt}, routingCleared = {clearRouting}. " +
                       $"REVERT: git checkout -- \"{AudioHandoutState.AssetPath}\"");
        log.AppendLine($"The way back is Tools/Audio/Handout/Reinstate Authored Audio, which reads " +
                       $"'{snapshotPath}'. Running Switch To Handout again now reports nothing to do.");

        result.Success = !session.HasFailures;
        result.Log = log.ToString().TrimEnd();
        return result;
    }

    // ==========================================================================
    //  4. Reinstate
    // ==========================================================================

    /// <summary>
    /// Puts every slot and every routing reference back to what the snapshot recorded, then
    /// re-runs the progress report and FAILS LOUDLY unless implemented and total equal the
    /// counts the snapshot was taken with. An entry that cannot be resolved - a clip deleted,
    /// a mixer group gone - is a failure too: a half-reinstated project that reports Authored
    /// is the one outcome worse than a refusal.
    /// </summary>
    public static Result Reinstate()
    {
        var result = new Result();
        var log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Reinstate Authored Audio.");

        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        string snapshotPath = ResolveSnapshotPath(state, out string pathNote);
        if (pathNote != null) log.AppendLine("  " + pathNote);

        if (snapshotPath == null)
        {
            log.AppendLine($"REFUSED: no snapshot to reinstate from - the state asset names none and there is " +
                           $"nothing in {AudioHandoutState.SnapshotFolder}. `git checkout` of the scene, the " +
                           "prefabs and the music assets at the commit before the switch is the way back.");
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        result.SnapshotPath = snapshotPath;

        AudioHandoutSnapshot snapshot = AudioHandoutSnapshot.Read(snapshotPath, out string readError);
        if (snapshot == null)
        {
            log.AppendLine("REFUSED: " + readError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        Surface surface = Walk(out string walkError);
        if (surface == null)
        {
            log.AppendLine("REFUSED: " + walkError);
            result.Log = log.ToString().TrimEnd();
            return result;
        }

        foreach (string note in surface.Notes) log.AppendLine("  " + note);
        log.AppendLine($"Reinstating from {snapshot.Describe(snapshotPath)}");

        var slotMatcher = new AudioHandoutSnapshotMatcher(snapshot.slots, "clip slot");
        var routingMatcher = new AudioHandoutSnapshotMatcher(snapshot.routing, "routing reference");

        var unmatchedLive = new List<string>();
        var unresolved = new List<string>();
        var notes = new List<string>();

        var session = new WriteSession(ReinstateUndoLabel);
        int slotsAlready = 0, routingAlready = 0;

        foreach (AudioSlotRef slot in surface.Slots)
        {
            string where = $"{slot.GroupName} ▸ {slot.Label}";
            AudioHandoutSnapshotEntry entry =
                slotMatcher.Match(AudioHandoutSnapshot.IdOf(slot.Host), slot.GroupName, slot.Label);

            if (entry == null)
            {
                unmatchedLive.Add($"{where}: the snapshot has no entry for this slot, so it keeps whatever it " +
                                  $"holds now ({Describe(slot.Clip)}).");
                continue;
            }

            AudioClip want = null;
            if (!entry.IsEmpty)
            {
                want = AudioHandoutSnapshot.LoadClip(entry, out string note);
                if (note != null) notes.Add($"{where}: {note}");
                if (want == null)
                {
                    unresolved.Add($"{where}: the snapshot's clip could not be loaded - {note}");
                    continue;
                }
            }

            AudioClip have = slot.Clip;
            if (have == want) { slotsAlready++; continue; }
            if (!session.Prepare(slot.Host, slot.HostKind, where)) continue;

            try { slot.Set(want); }
            catch (Exception e) { session.RecordFailure(where, e); continue; }

            session.Applied(slot.Host, slot.HostKind, slot.PrefabRoot,
                            $"{where}: {Describe(have)} -> {Describe(want)}", want);
            result.SlotsChanged++;
        }

        foreach (AudioRoutingRef routing in surface.Routing)
        {
            string where = $"{routing.GroupName} ▸ {routing.Label}";
            AudioHandoutSnapshotEntry entry =
                routingMatcher.Match(AudioHandoutSnapshot.IdOf(routing.Host), routing.GroupName, routing.Label);

            if (entry == null)
            {
                unmatchedLive.Add($"{where}: the snapshot has no entry for this routing reference, so it keeps " +
                                  $"whatever it holds now ({Describe(routing.Group)}).");
                continue;
            }

            AudioMixerGroup want = null;
            if (!entry.IsEmpty)
            {
                want = AudioHandoutSnapshot.LoadGroup(entry, out string note);
                if (want == null)
                {
                    unresolved.Add($"{where}: the snapshot's mixer group could not be loaded - {note}");
                    continue;
                }
            }

            AudioMixerGroup have = routing.Group;
            if (have == want) { routingAlready++; continue; }
            if (!session.Prepare(routing.Host, routing.HostKind, where)) continue;

            try { routing.Set(want); }
            catch (Exception e) { session.RecordFailure(where, e); continue; }

            session.Applied(routing.Host, routing.HostKind, routing.PrefabRoot,
                            $"{where}: {Describe(have)} -> {Describe(want)}", want);
            result.RoutingChanged++;
        }

        state = AudioHandoutState.LoadOrCreate();
        Undo.RecordObject(state, ReinstateUndoLabel);
        state.mode = AudioHandoutMode.Authored;
        state.switchedAt = "";
        state.routingCleared = false;
        EditorUtility.SetDirty(state);

        session.Finish();
        session.AppendLog(log, AudioHandoutState.AssetPath);

        AppendFallbacks(log, slotMatcher.Fallbacks);
        AppendFallbacks(log, routingMatcher.Fallbacks);

        List<AudioHandoutSnapshotEntry> leftOverSlots = slotMatcher.Unmatched();
        List<AudioHandoutSnapshotEntry> leftOverRouting = routingMatcher.Unmatched();

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            foreach (string note in Capped(notes)) log.AppendLine("  " + note);
        }

        log.AppendLine($"TOTALS: {result.SlotsChanged} slot(s) and {result.RoutingChanged} routing reference(s) " +
                       $"put back; {slotsAlready} slot(s) and {routingAlready} routing reference(s) already held " +
                       "the authored value.");
        log.AppendLine($"STATE: mode = Authored, switchedAt cleared. " +
                       $"REVERT: git checkout -- \"{AudioHandoutState.AssetPath}\"");

        // ---- The gate ----
        AudioProgressReport report = AudioProgressScanner.Scan();
        bool countsOk = report.Implemented == snapshot.implemented && report.Total == snapshot.total;

        log.AppendLine(countsOk
            ? $"VERIFIED: the report reads {report.Implemented}/{report.Total}, exactly the counts " +
              $"'{snapshotPath}' was taken with."
            : $"⚠ FAILED: the report reads {report.Implemented}/{report.Total}, and '{snapshotPath}' was taken " +
              $"at {snapshot.implemented}/{snapshot.total}. The authored state is NOT fully back.");

        if (state != null && (state.implementedAtSnapshot != snapshot.implemented ||
                              state.totalAtSnapshot != snapshot.total))
        {
            log.AppendLine($"  NOTE: the state asset records {state.implementedAtSnapshot}/{state.totalAtSnapshot} " +
                           "for its snapshot, which is not what the file itself says. The FILE is what was " +
                           "reinstated and what the check above used.");
        }

        AppendProblems(log, "⚠ SLOTS AND ROUTING THE SNAPSHOT DOES NOT KNOW (they keep whatever they hold)",
                       unmatchedLive);
        AppendProblems(log, "⚠ ENTRIES THAT COULD NOT BE RESOLVED (a clip or a mixer group has gone)", unresolved);

        if (leftOverSlots.Count > 0 || leftOverRouting.Count > 0)
        {
            var lines = new List<string>();
            foreach (AudioHandoutSnapshotEntry entry in leftOverSlots)
                lines.Add($"{entry.group} ▸ {entry.label} held '{entry.Describe()}' on {entry.host}");
            foreach (AudioHandoutSnapshotEntry entry in leftOverRouting)
                lines.Add($"{entry.group} ▸ {entry.label} routed to '{entry.Describe()}' on {entry.host}");

            AppendProblems(log, "⚠ ENTRIES THE WALK NO LONGER FINDS (the project's shape changed since the snapshot)", lines);
        }

        bool clean = countsOk && unresolved.Count == 0 && unmatchedLive.Count == 0 &&
                     leftOverSlots.Count == 0 && leftOverRouting.Count == 0 && !session.HasFailures;

        if (!clean)
        {
            log.AppendLine("⚠ REINSTATE DID NOT FINISH CLEANLY. Everything above that is marked ⚠ is a reference " +
                           "the authored state is still missing. Ctrl+Z returns the scene; `git checkout -- <path>` " +
                           "returns the prefabs and assets named above; the snapshot's own commit " +
                           $"({Or(snapshot.commit, "unrecorded")}) is the state everything came from.");
        }

        result.Success = clean;
        result.Log = log.ToString().TrimEnd();
        return result;
    }

    // ==========================================================================
    //  The walk
    // ==========================================================================

    private sealed class Surface
    {
        public readonly List<AudioSlotRef> Slots = new List<AudioSlotRef>();
        public readonly List<AudioRoutingRef> Routing = new List<AudioRoutingRef>();
        public readonly List<GameObject> ExtraRoots = new List<GameObject>();
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// One walk, the same one the report grades, with the extra prefab roots handed in.
    /// Returns null and an explanation rather than throwing: until Agent A lands, WalkSlots
    /// is a stub that throws, and a menu item that throws a stack trace at the integrator is
    /// a worse way to say "not yet" than a sentence is.
    /// </summary>
    private static Surface Walk(out string error)
    {
        error = null;

        // A recreated object must get a fresh GlobalObjectId, or the freshness check would
        // miss exactly the case the fallback key exists for.
        AudioHandoutSnapshot.ResetIdCache();

        var surface = new Surface();
        CollectExtraPrefabRoots(surface);

        try
        {
            AudioProgressScanner.WalkSlots(
                slot => { if (slot != null) surface.Slots.Add(slot); },
                routing => { if (routing != null) surface.Routing.Add(routing); },
                surface.ExtraRoots);
        }
        catch (Exception e)
        {
            error = $"AudioProgressScanner.WalkSlots threw {e.GetType().Name}: {e.Message}";
            return null;
        }

        return surface;
    }

    /// <summary>
    /// The prefabs whose AudioSources carry a mixer group and which nothing else reaches.
    /// Found by FOLDER, never by a hard-coded name list - the crossbow shipped as both
    /// `CrossBow` and `Crossbow`, and a path constant was how that stayed invisible. The one
    /// loose file is named because it is one loose file, and its absence is reported.
    /// </summary>
    private static void CollectExtraPrefabRoots(Surface surface)
    {
        var byPath = new SortedDictionary<string, GameObject>(StringComparer.Ordinal);
        var folders = new List<string>();

        foreach (string folder in ExtraPrefabFolders)
        {
            if (AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
            else surface.Notes.Add($"⚠ '{folder}' is not a folder in this project - whatever prefabs it should " +
                                   "hold are NOT in the walk, so their routing is neither snapshotted nor cleared.");
        }

        if (folders.Count > 0)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) byPath[path] = prefab;
            }
        }

        var bulletImpact = AssetDatabase.LoadAssetAtPath<GameObject>(BulletImpactPrefabPath);
        if (bulletImpact != null) byPath[BulletImpactPrefabPath] = bulletImpact;
        else surface.Notes.Add($"⚠ '{BulletImpactPrefabPath}' is missing - its AudioSource's output group is " +
                               "NOT in the walk.");

        // Ordinal by path, so two runs hand the walk the same roots in the same order and
        // two snapshots of an unchanged project differ only in their timestamp.
        foreach (KeyValuePair<string, GameObject> pair in byPath) surface.ExtraRoots.Add(pair.Value);
    }

    // ==========================================================================
    //  The rule
    // ==========================================================================

    private enum HandoutValue
    {
        /// <summary>"Beep to replace.wav" - almost every slot.</summary>
        Beep,
        /// <summary>"Beep to replace - footsteps.wav" - the FootstepAudioManager's surfaces.</summary>
        FootstepBeep,
        /// <summary>Nothing, on purpose: the Music Manager's slots and the music states' stems.</summary>
        Empty
    }

    /// <summary>
    /// The owner's decision of 2026-09-09, read off the host's TYPE. A name string would let
    /// a renamed GameObject reroute a slot to the wrong value in silence.
    /// </summary>
    private static HandoutValue RuleFor(AudioSlotRef slot)
    {
        UnityEngine.Object host = slot.Host;
        if (host is MusicManager || host is MusicStateDefinition) return HandoutValue.Empty;
        if (host is Audio.FootstepAudioManager) return HandoutValue.FootstepBeep;
        return HandoutValue.Beep;
    }

    /// <summary>
    /// ⚠ A missing beep is a REFUSAL, never a null write. Null is the music's authored value
    /// and an empty slot in the report; a null written where a beep was meant would reinstate
    /// as an authored empty and there would be no way to tell the two apart afterwards.
    /// </summary>
    private static bool TryLoadBeeps(out AudioClip beep, out AudioClip footstepBeep, out string error)
    {
        error = null;
        beep = AssetDatabase.LoadAssetAtPath<AudioClip>(BeepPath);
        footstepBeep = AssetDatabase.LoadAssetAtPath<AudioClip>(FootstepBeepPath);

        var missing = new List<string>();
        if (beep == null) missing.Add(BeepPath);
        if (footstepBeep == null) missing.Add(FootstepBeepPath);
        if (missing.Count == 0) return true;

        error = "the placeholder beep is missing - " + string.Join(", ", missing) +
                ". Every slot the switch touches must end up holding a beep, or nothing ON PURPOSE for the " +
                "music; a null written where a beep was intended is indistinguishable from an authored empty " +
                "slot afterwards. Restore the file(s) and re-run.";
        return false;
    }

    // ==========================================================================
    //  The freshness diff
    // ==========================================================================

    private sealed class Difference
    {
        public string Kind;
        public string Text;
    }

    /// <summary>
    /// What has moved between the snapshot and the live project.
    ///
    /// A CLIP SLOT counts as different when either side classifies as Implemented and the
    /// two do not hold the same asset. Beep -> beep, beep -> empty and empty -> beep are all
    /// ignored: those are the placeholders, and swapping one for another is not authored work
    /// the snapshot could lose. A ROUTING REFERENCE counts as different whenever it holds
    /// anything but what was recorded, whatever that was - there is no placeholder mixer group.
    ///
    /// A reference on ONE side only is a difference too. The switch cannot put back a slot
    /// the snapshot never saw, so beeping it would be a one-way write; and a snapshot entry
    /// the walk no longer finds means the round trip can no longer be byte-identical.
    /// </summary>
    private static List<Difference> Diff(Surface surface, AudioHandoutSnapshot snapshot, out List<string> fallbacks)
    {
        var differences = new List<Difference>();
        fallbacks = new List<string>();

        var slotMatcher = new AudioHandoutSnapshotMatcher(snapshot.slots, "clip slot");
        string implemented = AudioSlotState.Implemented.ToString();

        foreach (AudioSlotRef slot in surface.Slots)
        {
            AudioClip clip = slot.Clip;
            string where = $"{slot.GroupName} ▸ {slot.Label}";
            AudioHandoutSnapshotEntry entry =
                slotMatcher.Match(AudioHandoutSnapshot.IdOf(slot.Host), slot.GroupName, slot.Label);

            if (entry == null)
            {
                differences.Add(new Difference
                {
                    Kind = KindSlotNew,
                    Text = $"{where}: holds {Describe(clip)} and the snapshot has no entry for it"
                });
                continue;
            }

            bool liveImplemented = AudioProgressScanner.ClassifyClip(clip) == AudioSlotState.Implemented;
            bool wasImplemented = entry.state == implemented;
            if (!liveImplemented && !wasImplemented) continue;

            AudioHandoutSnapshot.AssetId(clip, out string guid, out long localId, out string _);
            if (entry.Holds(guid, localId)) continue;

            differences.Add(new Difference
            {
                Kind = KindSlotChanged,
                Text = $"{where}: snapshot {entry.Describe()} -> live {Describe(clip)}"
            });
        }

        foreach (AudioHandoutSnapshotEntry entry in slotMatcher.Unmatched())
        {
            differences.Add(new Difference
            {
                Kind = KindSlotGone,
                Text = $"{entry.group} ▸ {entry.label}: recorded as {entry.Describe()} on {entry.host}"
            });
        }
        fallbacks.AddRange(slotMatcher.Fallbacks);

        var routingMatcher = new AudioHandoutSnapshotMatcher(snapshot.routing, "routing reference");

        foreach (AudioRoutingRef routing in surface.Routing)
        {
            AudioMixerGroup group = routing.Group;
            string where = $"{routing.GroupName} ▸ {routing.Label}";
            AudioHandoutSnapshotEntry entry =
                routingMatcher.Match(AudioHandoutSnapshot.IdOf(routing.Host), routing.GroupName, routing.Label);

            if (entry == null)
            {
                differences.Add(new Difference
                {
                    Kind = KindRoutingNew,
                    Text = $"{where}: routed to {Describe(group)} and the snapshot has no entry for it"
                });
                continue;
            }

            AudioHandoutSnapshot.AssetId(group, out string guid, out long localId, out string _);
            if (entry.Holds(guid, localId)) continue;

            differences.Add(new Difference
            {
                Kind = KindRoutingChanged,
                Text = $"{where}: snapshot {entry.Describe()} -> live {Describe(group)}"
            });
        }

        foreach (AudioHandoutSnapshotEntry entry in routingMatcher.Unmatched())
        {
            differences.Add(new Difference
            {
                Kind = KindRoutingGone,
                Text = $"{entry.group} ▸ {entry.label}: recorded as {entry.Describe()} on {entry.host}"
            });
        }
        fallbacks.AddRange(routingMatcher.Fallbacks);

        return differences;
    }

    private static void AppendDifferences(StringBuilder log, List<Difference> differences)
    {
        foreach (string kind in new[]
                 {
                     KindSlotChanged, KindRoutingChanged, KindSlotNew,
                     KindRoutingNew, KindSlotGone, KindRoutingGone
                 })
        {
            var lines = new List<string>();
            foreach (Difference difference in differences)
            {
                if (difference.Kind == kind) lines.Add(difference.Text);
            }
            if (lines.Count == 0) continue;

            log.AppendLine($"  {kind} ({lines.Count}):");
            foreach (string line in Capped(lines)) log.AppendLine("    " + line);
        }
    }

    private static void AppendFallbacks(StringBuilder log, List<string> fallbacks)
    {
        if (fallbacks == null || fallbacks.Count == 0) return;

        log.AppendLine($"MATCHED BY GROUP + LABEL, NOT BY OBJECT ID ({fallbacks.Count}) - each of these objects " +
                       "was recreated since the snapshot, which is fine but worth knowing:");
        foreach (string line in Capped(fallbacks)) log.AppendLine("  " + line);
    }

    private static void AppendProblems(StringBuilder log, string heading, List<string> lines)
    {
        if (lines == null || lines.Count == 0) return;

        log.AppendLine($"{heading} ({lines.Count}):");
        foreach (string line in Capped(lines)) log.AppendLine("  " + line);
    }

    private static List<string> Capped(List<string> lines)
    {
        if (lines.Count <= MaxListedDifferences) return lines;

        var capped = lines.GetRange(0, MaxListedDifferences);
        capped.Add($"...and {lines.Count - MaxListedDifferences} more.");
        return capped;
    }

    // ==========================================================================
    //  The writes
    // ==========================================================================

    /// <summary>
    /// One prefab or asset file and everything written into it, so the log names each file
    /// once with its revert command and the verification has something to look for.
    /// </summary>
    private sealed class FileWrites
    {
        public string Path;
        /// <summary>The prefab asset's root, the save target. Null for a ScriptableObject asset.</summary>
        public GameObject PrefabRoot;
        public readonly List<string> Lines = new List<string>();
        /// <summary>guid -> how many references in this file should now point at it.</summary>
        public readonly Dictionary<string, int> ExpectedGuids = new Dictionary<string, int>();
        /// <summary>References set to null: nothing positive to look for in the file afterwards.</summary>
        public int Cleared;
    }

    /// <summary>
    /// Owns the three write paths for one operation: the Undo group for the scene, the dirty
    /// flags and the save for prefabs and assets, the verification, and the two log sections
    /// the work order asks for - ASSETS WRITTEN apart from SCENE FIELDS SET.
    /// </summary>
    private sealed class WriteSession
    {
        private readonly string undoLabel;
        private readonly int undoGroup;

        private readonly HashSet<UnityEngine.Object> undoRecorded = new HashSet<UnityEngine.Object>();
        private readonly List<string> sceneLines = new List<string>();
        private readonly SortedDictionary<string, FileWrites> files =
            new SortedDictionary<string, FileWrites>(StringComparer.Ordinal);
        private readonly List<string> notes = new List<string>();
        private readonly List<string> failures = new List<string>();

        private bool anySceneWrite;

        public WriteSession(string undoLabel)
        {
            this.undoLabel = undoLabel;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoLabel);
            undoGroup = Undo.GetCurrentGroup();
        }

        public bool HasFailures => failures.Count > 0;

        /// <summary>
        /// Everything that must happen BEFORE the value changes: for a scene host, the Undo
        /// record, once per host however many of its slots move. Prefab and asset hosts need
        /// nothing here - they are dirtied and saved after the fact, and Ctrl+Z will not
        /// bring them back whatever this does.
        /// </summary>
        public bool Prepare(UnityEngine.Object host, AudioHostKind kind, string where)
        {
            if (host == null)
            {
                notes.Add($"⚠ {where}: its host object has gone. NOT written.");
                return false;
            }

            if (kind == AudioHostKind.Scene && undoRecorded.Add(host)) Undo.RecordObject(host, undoLabel);
            return true;
        }

        /// <summary>Records a write that has already happened, and dirties its host.</summary>
        public void Applied(UnityEngine.Object host, AudioHostKind kind, GameObject prefabRoot,
                            string line, UnityEngine.Object written)
        {
            EditorUtility.SetDirty(host);

            if (kind == AudioHostKind.Scene)
            {
                anySceneWrite = true;
                sceneLines.Add(line);
                return;
            }

            FileWrites file = FileFor(host, kind, prefabRoot);
            if (file == null)
            {
                notes.Add($"⚠ {line} - written, but its host has no asset path, so it cannot be saved or " +
                          "reverted by name. Check it by hand.");
                return;
            }

            file.Lines.Add(line);

            if (written == null)
            {
                file.Cleared++;
                return;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(written, out string guid, out long _))
            {
                file.ExpectedGuids.TryGetValue(guid, out int count);
                file.ExpectedGuids[guid] = count + 1;
            }
        }

        /// <summary>
        /// A reference that would not take its value: AudioSlotRef.Set throws rather than
        /// writing through a boxed copy of a value type, or silently doing nothing.
        /// </summary>
        public void RecordFailure(string where, Exception e)
        {
            failures.Add($"⚠ {where}: {e.GetType().Name}: {e.Message}");
        }

        private FileWrites FileFor(UnityEngine.Object host, AudioHostKind kind, GameObject prefabRoot)
        {
            string path = kind == AudioHostKind.Prefab && prefabRoot != null
                ? AssetDatabase.GetAssetPath(prefabRoot)
                : AssetDatabase.GetAssetPath(host);

            if (string.IsNullOrEmpty(path)) return null;

            if (!files.TryGetValue(path, out FileWrites file))
            {
                file = new FileWrites { Path = path };
                files[path] = file;
            }

            if (kind == AudioHostKind.Prefab && file.PrefabRoot == null)
            {
                // The walk should always hand back the root; loading it from the path is the
                // recovery when it does not, because without a root there is no save target.
                file.PrefabRoot = prefabRoot != null
                    ? prefabRoot
                    : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            return file;
        }

        /// <summary>Saves the prefabs, flushes the assets, verifies the files, closes the Undo group.</summary>
        public void Finish()
        {
            foreach (FileWrites file in files.Values)
            {
                if (file.PrefabRoot == null) continue;    // a ScriptableObject: SaveAssets below

                try
                {
                    PrefabUtility.SavePrefabAsset(file.PrefabRoot, out bool saved);
                    if (!saved)
                    {
                        failures.Add($"⚠ {file.Path}: PrefabUtility.SavePrefabAsset reported that nothing was " +
                                     "saved. The change is in memory only and will be lost.");
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"⚠ {file.Path}: PrefabUtility.SavePrefabAsset threw {e.GetType().Name}: {e.Message}");
                }
            }

            // The ScriptableObject hosts (the music states) and the state asset.
            AssetDatabase.SaveAssets();

            foreach (FileWrites file in files.Values) Verify(file);

            Undo.CollapseUndoOperations(undoGroup);

            if (anySceneWrite)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        /// <summary>
        /// Re-reads the file from disk and counts the guid of every clip or group written
        /// into it. This is the check the file header promises: a direct edit of a prefab
        /// asset that silently fails to persist would otherwise look exactly like a success.
        /// </summary>
        private void Verify(FileWrites file)
        {
            if (file.ExpectedGuids.Count == 0)
            {
                if (file.Cleared > 0)
                {
                    notes.Add($"{file.Path}: {file.Cleared} reference(s) CLEARED. A null write leaves no guid to " +
                              "look for, so this file's persistence is checked with `git diff`, not by this tool.");
                }
                return;
            }

            string text;
            try
            {
                text = File.ReadAllText(AudioHandoutSnapshot.AbsolutePath(file.Path));
            }
            catch (Exception e)
            {
                failures.Add($"⚠ {file.Path}: written, but could not be re-read to verify - " +
                             $"{e.GetType().Name}: {e.Message}");
                return;
            }

            foreach (KeyValuePair<string, int> wanted in file.ExpectedGuids)
            {
                int found = Occurrences(text, wanted.Key);
                if (found >= wanted.Value) continue;

                failures.Add($"⚠ {file.Path}: THE WRITE DID NOT PERSIST. guid {wanted.Key} appears {found} time(s) " +
                             $"in the file on disk and {wanted.Value} were expected. The direct-edit + " +
                             "PrefabUtility.SavePrefabAsset path has stopped working; switch this tool to " +
                             "PrefabUtility.LoadPrefabContents / SaveAsPrefabAsset / UnloadPrefabContents, the " +
                             "path AudioSlotSurfaceTools uses for the boss prefab.");
            }
        }

        private static int Occurrences(string text, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return 0;

            int count = 0;
            for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// The two sections the work order asks for, in this order: what went to DISK and is
        /// only revertible with git, then what is in the scene and needs the owner to save.
        /// </summary>
        public void AppendLog(StringBuilder log, string stateAssetPath)
        {
            int assetReferences = 0;
            foreach (FileWrites file in files.Values) assetReferences += file.Lines.Count;

            log.AppendLine($"⚠ ASSETS AND PREFABS WRITTEN ({assetReferences} reference(s) in {files.Count} file(s)) " +
                           "- these went STRAIGHT TO DISK and are NOT undoable. Ctrl+Z will not bring them back:");

            if (files.Count == 0)
            {
                log.AppendLine("  <none - no prefab or asset needed changing>");
            }
            else
            {
                foreach (FileWrites file in files.Values)
                {
                    log.AppendLine($"  {file.Path}  ({file.Lines.Count} reference(s))   " +
                                   $"REVERT: git checkout -- \"{file.Path}\"");
                    foreach (string line in file.Lines) log.AppendLine("    " + line);
                }
            }
            log.AppendLine($"  (the state asset {stateAssetPath} is written too, and is listed on its own line below)");

            log.AppendLine($"SCENE FIELDS SET ({sceneLines.Count}) - Undo-recorded as ONE step, '{undoLabel}'. " +
                           "⚠ THE OWNER MUST SAVE THE SCENE or these are lost on the next reload:");
            if (sceneLines.Count == 0) log.AppendLine("  <none - the scene already held these values>");
            foreach (string line in sceneLines) log.AppendLine("  " + line);

            if (notes.Count > 0)
            {
                log.AppendLine($"NOTES ({notes.Count}):");
                foreach (string line in Capped(notes)) log.AppendLine("  " + line);
            }

            if (failures.Count > 0)
            {
                log.AppendLine($"⚠ FAILURES ({failures.Count}) - these references were NOT written:");
                foreach (string line in Capped(failures)) log.AppendLine("  " + line);
            }
        }
    }

    // ==========================================================================
    //  Shared helpers
    // ==========================================================================

    /// <summary>
    /// Which snapshot Reinstate reads: the one the state names, or the newest on disk when
    /// the state names none or names one that has gone.
    /// </summary>
    private static string ResolveSnapshotPath(AudioHandoutState state, out string note)
    {
        note = null;
        string named = state != null ? state.snapshotFile : null;

        if (!string.IsNullOrEmpty(named) && File.Exists(AudioHandoutSnapshot.AbsolutePath(named))) return named;

        string newest = AudioHandoutSnapshot.NewestPath();

        if (!string.IsNullOrEmpty(named))
        {
            note = $"⚠ the state asset names '{named}', which is not on disk. " +
                   (newest == null ? "There is nothing to fall back to." : $"Falling back to the newest, '{newest}'.");
        }
        else if (newest != null)
        {
            note = $"the state asset names no snapshot; reading the newest on disk, '{newest}'.";
        }

        return newest;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Audio")) AssetDatabase.CreateFolder("Assets", "Audio");
        if (!AssetDatabase.IsValidFolder(AudioHandoutState.Folder))
            AssetDatabase.CreateFolder("Assets/Audio", "Handout");
        if (!AssetDatabase.IsValidFolder(AudioHandoutState.SnapshotFolder))
            AssetDatabase.CreateFolder(AudioHandoutState.Folder, "Snapshots");
    }

    private static string HostKindTally(List<AudioHostKind> kinds)
    {
        int scene = 0, prefab = 0, asset = 0;
        foreach (AudioHostKind kind in kinds)
        {
            switch (kind)
            {
                case AudioHostKind.Prefab: prefab++; break;
                case AudioHostKind.Asset: asset++; break;
                default: scene++; break;
            }
        }
        return $"{scene} scene, {prefab} prefab, {asset} asset";
    }

    /// <summary>What the log calls a reference's value. Quoted, so an empty name still reads.</summary>
    private static string Describe(UnityEngine.Object value) => value == null ? "<none>" : $"'{value.name}'";

    private static string Or(string value, string fallback = "<none>") =>
        string.IsNullOrEmpty(value) ? fallback : value;
}
