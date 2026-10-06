using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/*
 * AudioHandoutSelfTest.cs
 *
 * Purpose: the proof that the handout round trip is LOSSLESS. Runs the whole loop -
 *          export -> Snapshot -> Switch To Handout -> export -> Reinstate -> export -
 *          and asserts that what came back is what went in: the same report bytes, the
 *          same mixer routing, the same prefab and asset files on disk.
 * Menu:    Tools/Audio/Handout/Round-Trip Self-Test   (also callable: Run())
 * Used by: the integrator, after Agents A-D land. See Documentation/AUDIO_HANDOUT_TASKS.md.
 *
 * ⚠ NO DIALOGS. Like AudioSlotSurfaceTools next door, this is run headless through the CLI
 * bridge, where EditorUtility.DisplayDialog would block on a modal window nobody can see.
 * A refusal is a line in the log and Success = false.
 *
 * ---------------------------------------------------------------------------------------
 * WHAT A CHECK LOOKS LIKE
 * ---------------------------------------------------------------------------------------
 * One grep-able line per check, tagged [1]..[N] in the order they run, PASS or FAIL, and
 * NUMBERS rather than impressions - the same discipline as Testing/EnemyRoleProbe:
 *
 *     [8] PASS  export C == export A: 0 differing lines of 262
 *     [4] FAIL  handout clip rule: 244 slots, 3 violations (first 10 below)
 *             [4.1] Enemy Audio Manager ▸ Bite: got 'Zombie_Bite', expected 'Beep to replace'
 *
 * A FAILING CHECK NEVER STOPS THE LATER ONES. All of them run, every time, so one run tells
 * the whole story; Success is "every check passed".
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE ONE LINE THAT IS ALLOWED TO DIFFER, AND WHY
 * ---------------------------------------------------------------------------------------
 * BuildExportText stamps `Scanned: yyyy-MM-dd HH:mm` from DateTime.Now. The round trip takes
 * seconds and the minute can roll over inside it, so a raw byte compare would fail on the
 * CLOCK rather than on the audio. Check [8] therefore compares the exports with that one
 * header line normalised, and states on the same line whether the raw bytes matched too.
 * Every other byte of the export must be identical; a difference anywhere else is a defect.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ TWO KINDS OF WRITE, TWO WAYS BACK
 * ---------------------------------------------------------------------------------------
 * SCENE fields are Undo-recorded, and the whole run is collapsed into ONE Undo group named
 * "Handout Round-Trip Self-Test", so a single Ctrl+Z returns the scene. PREFAB and ASSET
 * files are written straight to disk by the tools and Ctrl+Z does NOTHING for them -
 * `git checkout -- <path>` is the way back, and on failure the log names the paths.
 *
 * AssetDatabase.SaveAssets() runs BEFORE the baseline is recorded as well as after the round
 * trip. The owner's editor routinely holds unsaved asset edits; without that first save the
 * baseline would be the STALE disk bytes and check [10] would blame the round trip for an
 * edit that was already pending.
 *
 * ⚠ Binds to component types by NAME - "FootstepAudioManager", "MusicManager",
 * "MusicStateDefinition" - the way AudioProgressScanner does, because FootstepAudioManager
 * lives in namespace Audio and the other two do not. Renaming one compiles clean and turns
 * check [4] into a false FAIL; the violation lines name the type, so the cause is readable.
 */
public static class AudioHandoutSelfTest
{
    private const string UndoGroupName = "Handout Round-Trip Self-Test";

    /// <summary>
    /// The prefab folders whose AudioSources carry a mixer group today, which the walk
    /// cannot enumerate by itself. Mirrors AUDIO_HANDOUT_TASKS.md "Agent B" step 2 - the
    /// self-test must hand WalkSlots exactly the roots the tools hand it, or the routing
    /// list it records is not the list the switch cleared.
    /// </summary>
    private const string EnemiesFolder = "Assets/Inbox/Prefabs/Enemies";
    private const string StudioFolder = "Assets/Inbox/Prefabs/Studio equipment";
    private const string BulletImpactPrefabPath = "Assets/Inbox/Prefabs/BulletImpactAudioSource.prefab";

    // Matched by name, not by `is`: see the file header.
    private const string FootstepsTypeName = "FootstepAudioManager";
    private const string MusicManagerTypeName = "MusicManager";
    private const string MusicStateTypeName = "MusicStateDefinition";

    private const string ScannedLinePrefix = "Scanned: ";
    private const int MaxDetailLines = 10;

    [MenuItem("Tools/Audio/Handout/Round-Trip Self-Test", priority = 1604)]
    private static void RunMenu()
    {
        AudioHandoutTools.Result result = Run();
        if (result.Success) Debug.Log(result);
        else Debug.LogWarning(result);
    }

    /// <summary>
    /// The round trip, inside one Undo group. Success is "every check passed".
    /// SlotsChanged / RoutingChanged carry the SWITCH's counts (what the handout touched);
    /// SnapshotPath is the snapshot Snapshot() wrote for this run.
    /// </summary>
    public static AudioHandoutTools.Result Run()
    {
        var result = new AudioHandoutTools.Result();
        var log = new Ledger();

        log.Note("HANDOUT ROUND-TRIP SELF-TEST");
        log.Note("export A -> Snapshot -> Switch To Handout -> export B -> Reinstate -> export C.");
        log.Note("Every check runs even after a failure; Success = all passed.");
        log.Note("");

        // ---- refusals ---------------------------------------------------------
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            log.Note("REFUSED: play mode is running. The round trip writes prefabs and assets to disk " +
                     "and records Undo on scene objects; neither survives a play-mode exit intact.");
            result.Log = log.ToString();
            return result;
        }

        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        if (state != null && state.IsHandout)
        {
            log.Note("REFUSED: the project is in HANDOUT mode. The round trip starts from AUTHORED - " +
                     "from beeps it would prove nothing, and its export A would be the beeps.");
            log.Note("  Tools ▸ Audio ▸ Handout ▸ Reinstate Authored Audio first, then run this again.");
            log.Note($"  state: {AudioHandoutState.AssetPath}, snapshot '{state.snapshotFile}', switched {state.switchedAt}.");
            result.Log = log.ToString();
            return result;
        }

        log.Note(state == null
            ? $"mode: AUTHORED (no state asset at {AudioHandoutState.AssetPath} yet - Snapshot creates it)."
            : $"mode: AUTHORED (state asset present, snapshot '{state.snapshotFile}').");

        // ---- 1. record the baseline ------------------------------------------
        // Before the baseline as well as after: an unsaved asset edit written by the
        // switch's SaveAssets would otherwise read as a round-trip difference.
        AssetDatabase.SaveAssets();

        List<GameObject> extraRoots = ExtraPrefabRoots(out string rootsNote);
        log.Note("extra prefab roots for the walk: " + rootsNote);

        string exportA = ExportOrNull(out string exportAError);
        Walk walkA = RunWalk(extraRoots);
        string routingA = walkA.Error == null ? BuildRoutingText(walkA) : null;

        List<FileRecord> baseline = RecordFiles(SwitchTargetPaths(out string targetsNote));
        log.Note("files the switch may write: " + targetsNote);
        log.Note(exportA == null
            ? "baseline export A: UNAVAILABLE - " + exportAError
            : $"baseline export A: {CountLines(exportA)} lines, {Encoding.UTF8.GetByteCount(exportA)} bytes.");
        log.Note(walkA.Error != null
            ? "baseline walk: UNAVAILABLE - " + walkA.Error
            : $"baseline walk: {walkA.Slots.Count} clip slots, {walkA.Routing.Count} routing refs.");
        log.Note($"baseline files: {baseline.Count} recorded, {CountUnreadable(baseline)} unreadable.");
        log.Note("");

        // ---- the Undo group ---------------------------------------------------
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoGroupName);
        int undoGroup = Undo.GetCurrentGroup();

        try
        {
            // ---- 2. snapshot and switch --------------------------------------
            AudioHandoutTools.Result snapshot = Call(AudioHandoutTools.Snapshot, out string snapshotError);
            log.Record(snapshot != null && snapshot.Success, "Snapshot()",
                snapshot == null ? "threw - " + snapshotError
                                 : $"success={snapshot.Success} path='{snapshot.SnapshotPath}' " + FirstLine(snapshot.Log));
            if (snapshot != null) result.SnapshotPath = snapshot.SnapshotPath;

            AudioHandoutTools.Result switched = Call(() => AudioHandoutTools.SwitchToHandout(true), out string switchError);
            log.Record(switched != null && switched.Success, "SwitchToHandout(clearRouting: true)",
                switched == null ? "threw - " + switchError
                                 : $"success={switched.Success} slots={switched.SlotsChanged} " +
                                   $"routing={switched.RoutingChanged} " + FirstLine(switched.Log));
            if (switched != null)
            {
                result.SlotsChanged = switched.SlotsChanged;
                result.RoutingChanged = switched.RoutingChanged;
            }

            // ---- 3. the handout state ----------------------------------------
            AudioProgressReport reportB = ScanOrNull(out string scanBError);
            log.Record(reportB != null && reportB.Implemented == 0, "handout implemented == 0",
                reportB == null ? "Scan() threw - " + scanBError
                                : $"implemented={reportB.Implemented} placeholder={reportB.PlaceholderCount} " +
                                  $"empty={reportB.EmptyCount} total={reportB.Total}");

            Walk walkB = RunWalk(extraRoots);
            CheckClipRule(log, walkB);
            CheckRoutingCleared(log, walkB);

            bool countsKnown = walkB.Error == null && reportB != null;
            log.Record(countsKnown && walkB.Slots.Count == reportB.Total, "walk count == report total",
                !countsKnown ? "unavailable - " + (walkB.Error ?? "Scan() threw: " + scanBError)
                             : $"walk={walkB.Slots.Count} report={reportB.Total} " +
                               $"delta={walkB.Slots.Count - reportB.Total}");

            // ---- 4. reinstate and compare ------------------------------------
            AudioHandoutTools.Result reinstated = Call(AudioHandoutTools.Reinstate, out string reinstateError);
            log.Record(reinstated != null && reinstated.Success, "Reinstate()",
                reinstated == null ? "threw - " + reinstateError
                                   : $"success={reinstated.Success} slots={reinstated.SlotsChanged} " +
                                     $"routing={reinstated.RoutingChanged} " + FirstLine(reinstated.Log));

            AssetDatabase.SaveAssets();

            CheckExport(log, exportA, exportAError);
            CheckRouting(log, routingA, walkA.Error, extraRoots);
            CheckFiles(log, baseline);
            CheckStateAuthored(log);
        }
        finally
        {
            // One Ctrl+Z for the whole run, whatever happened above.
            Undo.CollapseUndoOperations(undoGroup);
        }

        result.Success = log.Failed == 0;
        log.Note("");
        log.Note($"SELF-TEST: {log.Passed}/{log.Count} checks passed, {log.Failed} failed.");
        if (!result.Success) AppendRecovery(log, baseline);
        result.Log = log.ToString();
        return result;
    }

    // ---- the checks -----------------------------------------------------------

    /// <summary>
    /// Footsteps beep under a FootstepAudioManager, empty under MusicManager or a
    /// MusicStateDefinition, the plain beep everywhere else - the owner's rule, 2026-09-09.
    /// </summary>
    private static void CheckClipRule(Ledger log, Walk walk)
    {
        var beep = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioHandoutTools.BeepPath);
        var footstepBeep = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioHandoutTools.FootstepBeepPath);

        if (walk.Error != null)
        {
            log.Record(false, "handout clip rule", "walk unavailable - " + walk.Error);
            return;
        }
        if (beep == null || footstepBeep == null)
        {
            log.Record(false, "handout clip rule",
                $"the beep assets are missing: '{AudioHandoutTools.BeepPath}' loaded={beep != null}, " +
                $"'{AudioHandoutTools.FootstepBeepPath}' loaded={footstepBeep != null}");
            return;
        }

        var violations = new List<string>();
        foreach (AudioSlotRef slot in walk.Slots)
        {
            AudioClip expected = ExpectedHandoutClip(slot, beep, footstepBeep);
            AudioClip actual;
            try { actual = slot.Clip; }
            catch (Exception e) { violations.Add($"{slot} [{HostTypeName(slot.Host)}]: unreadable - {Describe(e)}"); continue; }

            if (actual == expected) continue;
            violations.Add($"{slot} [{HostTypeName(slot.Host)}]: got {ClipName(actual)}, expected {ClipName(expected)}");
        }

        int tag = log.Record(violations.Count == 0, "handout clip rule",
            $"{walk.Slots.Count} slots, {violations.Count} violations" +
            (violations.Count > MaxDetailLines ? $" (first {MaxDetailLines} below)" : ""));
        log.Details(tag, violations);
    }

    private static void CheckRoutingCleared(Ledger log, Walk walk)
    {
        if (walk.Error != null)
        {
            log.Record(false, "handout routing cleared", "walk unavailable - " + walk.Error);
            return;
        }

        var violations = new List<string>();
        foreach (AudioRoutingRef routing in walk.Routing)
        {
            AudioMixerGroup group;
            try { group = routing.Group; }
            catch (Exception e) { violations.Add($"{routing}: unreadable - {Describe(e)}"); continue; }
            if (group == null) continue;
            violations.Add($"{routing}: still routed to '{group.name}'");
        }

        int tag = log.Record(violations.Count == 0, "handout routing cleared",
            $"{walk.Routing.Count} routing refs, {violations.Count} still set" +
            (violations.Count > MaxDetailLines ? $" (first {MaxDetailLines} below)" : ""));
        log.Details(tag, violations);
    }

    /// <summary>Export C against export A. The `Scanned:` clock line is normalised; see the header.</summary>
    private static void CheckExport(Ledger log, string exportA, string exportAError)
    {
        string exportC = ExportOrNull(out string exportCError);

        if (exportA == null || exportC == null)
        {
            log.Record(false, "export C == export A",
                $"unavailable - A: {exportAError ?? "ok"}, C: {exportCError ?? "ok"}");
            return;
        }

        bool rawEqual = string.Equals(exportA, exportC, StringComparison.Ordinal);
        string[] linesA = SplitLines(NormaliseExport(exportA));
        string[] linesC = SplitLines(NormaliseExport(exportC));

        var differences = new List<string>();
        int longest = Math.Max(linesA.Length, linesC.Length);
        for (int i = 0; i < longest; i++)
        {
            string a = i < linesA.Length ? linesA[i] : "<no line>";
            string c = i < linesC.Length ? linesC[i] : "<no line>";
            if (string.Equals(a, c, StringComparison.Ordinal)) continue;
            differences.Add($"line {i + 1}: A='{a}' C='{c}'");
        }

        int tag = log.Record(differences.Count == 0, "export C == export A",
            $"{differences.Count} differing lines of {linesA.Length} (C has {linesC.Length}); " +
            $"raw bytes equal={rawEqual} (the '{ScannedLinePrefix.Trim()}' clock line is normalised)" +
            (differences.Count > MaxDetailLines ? $"; first {MaxDetailLines} below" : ""));
        log.Details(tag, differences);
    }

    /// <summary>The routing list after Reinstate against the one recorded before Snapshot.</summary>
    private static void CheckRouting(Ledger log, string routingA, string walkAError, List<GameObject> extraRoots)
    {
        Walk walkC = RunWalk(extraRoots);

        if (routingA == null || walkC.Error != null)
        {
            log.Record(false, "routing list C == routing list A",
                $"unavailable - A: {walkAError ?? "ok"}, C: {walkC.Error ?? "ok"}");
            return;
        }

        string routingC = BuildRoutingText(walkC);
        string[] linesA = SplitLines(routingA);
        string[] linesC = SplitLines(routingC);

        var differences = new List<string>();
        int longest = Math.Max(linesA.Length, linesC.Length);
        for (int i = 0; i < longest; i++)
        {
            string a = i < linesA.Length ? linesA[i] : "<no line>";
            string c = i < linesC.Length ? linesC[i] : "<no line>";
            if (string.Equals(a, c, StringComparison.Ordinal)) continue;
            differences.Add($"line {i + 1}: A='{a}' C='{c}'");
        }

        int tag = log.Record(differences.Count == 0, "routing list C == routing list A",
            $"{differences.Count} differing lines of {linesA.Length} (C has {linesC.Length}); " +
            $"each line is 'GlobalObjectId | group ▸ label = <guid>:<localId>' or '= null'" +
            (differences.Count > MaxDetailLines ? $"; first {MaxDetailLines} below" : ""));
        log.Details(tag, differences);
    }

    /// <summary>Every prefab and asset the switch may write, re-read from disk and compared by length and MD5.</summary>
    private static void CheckFiles(Ledger log, List<FileRecord> baseline)
    {
        var differences = new List<string>();
        int compared = 0;

        foreach (FileRecord before in baseline)
        {
            FileRecord after = ReadFile(before.Path);
            if (before.Error != null || after.Error != null)
            {
                differences.Add($"{before.Path}: unreadable - before: {before.Error ?? "ok"}, after: {after.Error ?? "ok"}");
                continue;
            }

            compared++;
            if (before.Length == after.Length && string.Equals(before.Hash, after.Hash, StringComparison.Ordinal)) continue;
            differences.Add($"{before.Path}: {before.Length} bytes {before.Hash} -> {after.Length} bytes {after.Hash}");
        }

        int tag = log.Record(differences.Count == 0 && baseline.Count > 0, "files byte-identical on disk (line endings normalised; a prefab saved for the first time under this Unity version is reserialised ONCE - commit that and re-run)",
            $"{compared}/{baseline.Count} compared, {differences.Count} differ (md5 + length, re-read after SaveAssets)" +
            (baseline.Count == 0 ? " - NOTHING WAS RECORDED, the check proves nothing" : "") +
            (differences.Count > MaxDetailLines ? $"; first {MaxDetailLines} below" : ""));
        log.Details(tag, differences);
    }

    private static void CheckStateAuthored(Ledger log)
    {
        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        log.Record(state != null && !state.IsHandout, "state asset reads Authored",
            state == null ? $"no asset at {AudioHandoutState.AssetPath}"
                          : $"mode={state.mode} snapshot='{state.snapshotFile}' " +
                            $"implementedAtSnapshot={state.implementedAtSnapshot} " +
                            $"totalAtSnapshot={state.totalAtSnapshot} routingAtSnapshot={state.routingAtSnapshot}");
    }

    private static void AppendRecovery(Ledger log, List<FileRecord> baseline)
    {
        log.Note("");
        log.Note("RECOVERY - the round trip writes two kinds of thing, and they come back two different ways:");
        log.Note($"  SCENE fields: ONE Ctrl+Z. The whole run is a single Undo group, \"{UndoGroupName}\".");
        log.Note("  PREFAB and ASSET files: Ctrl+Z does NOTHING for them. `git checkout -- <path>` is the way back;");
        log.Note("  the paths this run recorded, and would therefore restore, are:");

        int listed = 0;
        foreach (FileRecord record in baseline)
        {
            if (listed++ >= MaxDetailLines) { log.Note($"    ... and {baseline.Count - MaxDetailLines} more."); break; }
            log.Note("    " + record.Path);
        }
        if (baseline.Count == 0) log.Note("    (none recorded - see the baseline lines above)");

        log.Note("  The scene is NOT saved by this test, and no clip file is ever deleted by any of these tools.");
    }

    // ---- the round trip's inputs ---------------------------------------------

    /// <summary>
    /// The prefab roots WalkSlots cannot find for itself: the enemy and studio-equipment
    /// prefabs and the bullet-impact source, whose AudioSources carry a mixer group today.
    /// Sorted by path so two runs hand the walk the same order and the routing list diffs.
    /// </summary>
    private static List<GameObject> ExtraPrefabRoots(out string note)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        int enemies = AddPrefabsUnder(EnemiesFolder, paths);
        int studio = AddPrefabsUnder(StudioFolder, paths);

        bool bulletImpact = File.Exists(BulletImpactPrefabPath);
        if (bulletImpact) paths.Add(BulletImpactPrefabPath);

        var roots = new List<GameObject>();
        int unloadable = 0;
        foreach (string path in paths)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) { unloadable++; continue; }
            roots.Add(root);
        }

        note = $"{roots.Count} ({enemies} enemies, {studio} studio equipment, " +
               $"bullet impact {(bulletImpact ? "found" : "MISSING")}" +
               (unloadable > 0 ? $", {unloadable} would not load" : "") + ")";
        return roots;
    }

    private static int AddPrefabsUnder(string folder, SortedSet<string> paths)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return 0;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        int added = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;
            if (paths.Add(path)) added++;
        }
        return added;
    }

    /// <summary>
    /// Every file the switch may write: the registry's prefabs (walked by the scanner), the
    /// extra prefab roots (walked for their AudioSources' routing) and every music state.
    /// The SCENE is not here - the tools never save it, the owner does.
    /// </summary>
    private static List<string> SwitchTargetPaths(out string note)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);

        int registry = 0;
        var found = UnityEngine.Object.FindFirstObjectByType<AssessedPrefabRegistry>(FindObjectsInactive.Include);
        if (found != null && found.prefabs != null)
        {
            foreach (GameObject prefab in found.prefabs)
            {
                if (prefab == null) continue;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!string.IsNullOrEmpty(path) && paths.Add(path)) registry++;
            }
        }

        int enemies = AddPrefabsUnder(EnemiesFolder, paths);
        int studio = AddPrefabsUnder(StudioFolder, paths);
        bool bulletImpact = File.Exists(BulletImpactPrefabPath);
        if (bulletImpact) paths.Add(BulletImpactPrefabPath);

        int music = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:" + MusicStateTypeName))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path) && paths.Add(path)) music++;
        }

        note = $"{paths.Count} ({registry} registry prefabs, {enemies} enemies, {studio} studio equipment, " +
               $"bullet impact {(bulletImpact ? "found" : "MISSING")}, {music} music states)" +
               (found == null ? " ⚠ no AssessedPrefabRegistry in the scene" : "");
        return new List<string>(paths);
    }

    // ---- the walk, the export, the tools: every call guarded -------------------

    private sealed class Walk
    {
        public readonly List<AudioSlotRef> Slots = new List<AudioSlotRef>();
        public readonly List<AudioRoutingRef> Routing = new List<AudioRoutingRef>();
        /// <summary>Non-null when WalkSlots threw - the lists are then partial and untrustworthy.</summary>
        public string Error;
    }

    private static Walk RunWalk(List<GameObject> extraRoots)
    {
        var walk = new Walk();
        try
        {
            AudioProgressScanner.WalkSlots(walk.Slots.Add, walk.Routing.Add, extraRoots);
        }
        catch (Exception e)
        {
            walk.Error = Describe(e);
        }
        return walk;
    }

    private static AudioProgressReport ScanOrNull(out string error)
    {
        error = null;
        try { return AudioProgressScanner.Scan(); }
        catch (Exception e) { error = Describe(e); return null; }
    }

    private static string ExportOrNull(out string error)
    {
        AudioProgressReport report = ScanOrNull(out error);
        if (report == null) return null;
        try { return AudioProgressScanner.BuildExportText(report); }
        catch (Exception e) { error = Describe(e); return null; }
    }

    private static AudioHandoutTools.Result Call(Func<AudioHandoutTools.Result> operation, out string error)
    {
        error = null;
        try { return operation(); }
        catch (Exception e) { error = Describe(e); return null; }
    }

    // ---- the routing list -----------------------------------------------------

    /// <summary>
    /// One line per routing reference: its host's GlobalObjectId and the report's group and
    /// label as the key - the two keys the snapshot uses - against the referenced group's
    /// asset guid and local file id, or "null". Text rather than a structure so the same
    /// line-by-line comparison serves it and the export.
    /// </summary>
    private static string BuildRoutingText(Walk walk)
    {
        var text = new StringBuilder();
        text.AppendLine("ROUTING REFERENCES: " + walk.Routing.Count.ToString(CultureInfo.InvariantCulture));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AudioRoutingRef routing in walk.Routing)
        {
            string key = RoutingKey(routing);
            string unique = key;
            for (int n = 2; !seen.Add(unique); n++)
                unique = key + " #" + n.ToString(CultureInfo.InvariantCulture);

            text.AppendLine(unique + " = " + GroupValue(routing));
        }
        return text.ToString();
    }

    private static string RoutingKey(AudioRoutingRef routing)
    {
        UnityEngine.Object host = routing.Host != null ? routing.Host : (UnityEngine.Object)routing.Source;
        string id = host != null ? GlobalObjectId.GetGlobalObjectIdSlow(host).ToString() : "<no host>";
        return id + " | " + routing;
    }

    private static string GroupValue(AudioRoutingRef routing)
    {
        AudioMixerGroup group;
        try { group = routing.Group; }
        catch (Exception e) { return "<unreadable: " + Describe(e) + ">"; }

        if (group == null) return "null";

        string guid;
        long localId;
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(group, out guid, out localId))
            return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);

        return "<not a saved asset> " + group.name;
    }

    // ---- the file records -----------------------------------------------------

    private sealed class FileRecord
    {
        public string Path;
        public long Length;
        public string Hash;
        public string Error;
    }

    private static List<FileRecord> RecordFiles(List<string> paths)
    {
        var records = new List<FileRecord>(paths.Count);
        foreach (string path in paths) records.Add(ReadFile(path));
        return records;
    }

    /// <summary>
    /// Project-relative paths are read as they stand: the editor's working directory is the
    /// project folder, which is why AssetDatabase paths work with System.IO here.
    /// </summary>
    private static FileRecord ReadFile(string path)
    {
        var record = new FileRecord { Path = path };
        try
        {
            // git checks these files out with CRLF (core.autocrlf) and Unity writes LF, so a
            // file Unity merely re-saved would differ in every line ending and nothing else.
            // Compare what git compares: the content with line endings normalised.
            byte[] bytes = Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n"));
            record.Length = bytes.LongLength;
            using (MD5 md5 = MD5.Create())
                record.Hash = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception e)
        {
            record.Error = Describe(e);
        }
        return record;
    }

    private static int CountUnreadable(List<FileRecord> records)
    {
        int unreadable = 0;
        foreach (FileRecord record in records) if (record.Error != null) unreadable++;
        return unreadable;
    }

    // ---- small helpers --------------------------------------------------------

    private static AudioClip ExpectedHandoutClip(AudioSlotRef slot, AudioClip beep, AudioClip footstepBeep)
    {
        Type host = slot.Host != null ? slot.Host.GetType() : null;
        if (IsOrDerivesFrom(host, FootstepsTypeName)) return footstepBeep;
        if (IsOrDerivesFrom(host, MusicManagerTypeName) || IsOrDerivesFrom(host, MusicStateTypeName)) return null;
        return beep;
    }

    /// <summary>By type NAME, up the base chain - see the header for why not `is`.</summary>
    private static bool IsOrDerivesFrom(Type type, string typeName)
    {
        for (Type current = type; current != null; current = current.BaseType)
            if (current.Name == typeName) return true;
        return false;
    }

    private static string HostTypeName(UnityEngine.Object host) => host == null ? "<no host>" : host.GetType().Name;

    private static string ClipName(AudioClip clip) => clip == null ? "<empty>" : "'" + clip.name + "'";

    /// <summary>Blanks the export's `Scanned:` clock line; every other byte must match.</summary>
    private static string NormaliseExport(string export)
    {
        string[] lines = SplitLines(export);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(ScannedLinePrefix, StringComparison.Ordinal)) continue;
            lines[i] = ScannedLinePrefix + "<normalised: the scan clock, not the audio>";
            break;
        }
        return string.Join("\n", lines);
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    private static int CountLines(string text) => SplitLines(text).Length;

    /// <summary>
    /// One line for a thrown exception. Unwraps a TargetInvocationException, because the
    /// walk writes slots through reflection and the wrapper's own message says nothing.
    /// </summary>
    private static string Describe(Exception e)
    {
        Exception cause = e;
        while (cause is System.Reflection.TargetInvocationException && cause.InnerException != null)
            cause = cause.InnerException;
        return cause.GetType().Name + ": " + FirstLine(cause.Message);
    }

    /// <summary>Keeps a tool's multi-line log to one line, so a check stays one grep-able line.</summary>
    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string[] lines = SplitLines(text);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed.Length > 160 ? trimmed.Substring(0, 157) + "..." : trimmed;
        }
        return "";
    }

    // ---- the ledger -----------------------------------------------------------

    /// <summary>
    /// The log. One line per check, tagged in the order the checks run, and indented detail
    /// lines beneath a failure. Counts the passes and the failures so Run() does not have to.
    /// </summary>
    private sealed class Ledger
    {
        private readonly StringBuilder text = new StringBuilder();
        private int count;

        public int Count => count;
        public int Passed { get; private set; }
        public int Failed { get; private set; }

        /// <summary>Writes the check line and returns its tag, for the detail lines below it.</summary>
        public int Record(bool ok, string what, string numbers)
        {
            count++;
            if (ok) Passed++; else Failed++;
            text.AppendLine($"[{count}] {(ok ? "PASS" : "FAIL")}  {what}: {numbers}");
            return count;
        }

        public void Details(int tag, List<string> lines)
        {
            for (int i = 0; i < lines.Count && i < MaxDetailLines; i++)
                text.AppendLine($"        [{tag}.{i + 1}] {lines[i]}");
        }

        public void Note(string line) => text.AppendLine(line);

        public override string ToString() => text.ToString();
    }
}
