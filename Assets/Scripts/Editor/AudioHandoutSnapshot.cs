using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Debug = UnityEngine.Debug;   // System.Diagnostics brings its own Debug along with Process

/*
 * AudioHandoutSnapshot.cs
 *
 * Purpose: The JSON record of the AUTHORED audio state - every clip slot the progress
 *          report lists and every AudioMixerGroup reference beside them - and the matcher
 *          that pairs a live reference back up with its recorded value.
 * Written by: Tools/Audio/Handout/Snapshot Authored Audio (AudioHandoutTools.Snapshot)
 * Read by:    Switch To Handout (the freshness diff) and Reinstate Authored Audio.
 *
 * See Documentation/AUDIO_HANDOUT_TASKS.md for the system this belongs to.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ WHY THE FILE STORES GUIDS AND NOT REFERENCES
 * ---------------------------------------------------------------------------------------
 * EditorJsonUtility.ToJson writes a UnityEngine.Object reference as an instanceID, which is
 * a number the editor invents at load time and forgets on the next domain reload. A snapshot
 * made of instanceIDs would reinstate garbage the day after it was taken. So every reference
 * here is stored as the asset's GUID plus its LOCAL FILE ID:
 *
 *   - an AudioClip is the MAIN asset of its .wav / .mp3, so the guid alone would nearly do,
 *     but the local id is recorded too and checked on the way back;
 *   - an AudioMixerGroup is a SUB-ASSET of the .mixer file, so every group in the project
 *     shares one guid and ONLY the local id tells them apart. There is no fallback for a
 *     group whose local id has gone: reinstating the wrong bus is worse than saying so.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ TWO KEYS PER REFERENCE, AND WHAT EACH ONE IS FOR
 * ---------------------------------------------------------------------------------------
 * PRIMARY:  the host's GlobalObjectId + the row's label.
 *           GlobalObjectId is the scene guid + the object's file id for a scene component,
 *           and the asset guid + the component's local file id for one on a prefab. Both
 *           survive a restart, a rename and a move in the hierarchy. `id` identifies the
 *           HOST, never the slot - one EnemyAudioManager hosts dozens of slots - so the
 *           label is what separates them, and the pair is the key.
 * FALLBACK: the report's group + the row's label, which survives the host object being
 *           DELETED AND RECREATED (a new file id, so a new GlobalObjectId). Every fallback
 *           match is logged by name, because a fallback means an object identity changed and
 *           the reader should know which.
 *
 * An entry is claimed by at most one live reference, in walk order, so two rows that somehow
 * share a key cannot both take the same recorded value.
 */

/// <summary>One recorded reference: an AudioClip slot, or an AudioMixerGroup reference.</summary>
[Serializable]
public sealed class AudioHandoutSnapshotEntry
{
    /// <summary>GlobalObjectId of the HOST component or asset. Half of the primary key.</summary>
    public string id;
    /// <summary>Readable host: a scene hierarchy path or an asset path, plus the component type.</summary>
    public string host;
    /// <summary>Scene / Prefab / Asset - which write path put this value there.</summary>
    public string hostKind;
    /// <summary>The report's section name. Half of the fallback key.</summary>
    public string group;
    /// <summary>The report's row label. The other half of BOTH keys.</summary>
    public string label;

    /// <summary>GUID of the referenced clip or mixer. Empty string for an empty slot.</summary>
    public string assetGuid;
    /// <summary>Local file id inside that asset - the only thing telling two mixer groups apart. 0 when empty.</summary>
    public long assetLocalId;
    /// <summary>The clip's or group's name, for the log. Never used to resolve anything.</summary>
    public string assetName;

    /// <summary>Slots only: Implemented / Placeholder / Demo / Empty at snapshot time. "" for routing.</summary>
    public string state;

    public bool IsEmpty => string.IsNullOrEmpty(assetGuid);

    /// <summary>What the log calls this value.</summary>
    public string Describe() => IsEmpty ? "<none>" : (string.IsNullOrEmpty(assetName) ? assetGuid : assetName);

    /// <summary>
    /// Joins the two halves of a key. A unit separator rather than a visible character,
    /// because a label may hold anything a field name and a profile name can spell, and
    /// "a" + "bc" must never key the same as "ab" + "c".
    /// </summary>
    public static readonly string KeySeparator = ((char)31).ToString();

    public string PrimaryKey => (id ?? "") + KeySeparator + (label ?? "");
    public string FallbackKey => (group ?? "") + KeySeparator + (label ?? "");

    /// <summary>True when the live reference below is the very asset this entry recorded.</summary>
    public bool Holds(string guid, long localId)
    {
        if (IsEmpty) return string.IsNullOrEmpty(guid);
        return assetGuid == guid && assetLocalId == localId;
    }
}

/// <summary>The snapshot file: what the authored state was, when, and at which commit.</summary>
[Serializable]
public sealed class AudioHandoutSnapshot
{
    /// <summary>ISO 8601 local time, with the machine's UTC offset.</summary>
    public string takenAt;
    /// <summary>`git rev-parse --short HEAD` at snapshot time, or "" when git was not reachable.</summary>
    public string commit;
    /// <summary>The progress report's implemented count at snapshot time. Reinstate must reach it.</summary>
    public int implemented;
    /// <summary>The progress report's total at snapshot time. Reinstate must reach it.</summary>
    public int total;

    public List<AudioHandoutSnapshotEntry> slots = new List<AudioHandoutSnapshotEntry>();
    public List<AudioHandoutSnapshotEntry> routing = new List<AudioHandoutSnapshotEntry>();

    // ---- File naming ---------------------------------------------------------

    public const string FilePrefix = "authored_";
    public const string FileExtension = ".json";
    private const string StampFormat = "yyyyMMdd_HHmmss";

    /// <summary>ISO 8601 local, offset included, so a snapshot taken in another timezone still reads true.</summary>
    public const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    public static string NowIso() => DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Project-relative path for a snapshot taken at <paramref name="when"/>.</summary>
    public static string PathFor(DateTime when)
    {
        return AudioHandoutState.SnapshotFolder + "/" + FilePrefix +
               when.ToString(StampFormat, CultureInfo.InvariantCulture) + FileExtension;
    }

    // ---- Paths ---------------------------------------------------------------

    /// <summary>The folder that holds Assets/, Library/ and the .git directory.</summary>
    public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    public static string AbsolutePath(string assetPath) =>
        Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));

    /// <summary>
    /// Every authored_*.json in the snapshot folder, NEWEST FIRST. The name carries the
    /// timestamp in a sortable format, so ordinal order IS chronological order - no file
    /// timestamps, which a checkout rewrites.
    /// </summary>
    public static List<string> AllPaths()
    {
        var found = new List<string>();
        string folder = AbsolutePath(AudioHandoutState.SnapshotFolder);
        if (!Directory.Exists(folder)) return found;

        foreach (string file in Directory.GetFiles(folder, FilePrefix + "*" + FileExtension))
        {
            found.Add(AudioHandoutState.SnapshotFolder + "/" + Path.GetFileName(file));
        }

        found.Sort((a, b) => string.CompareOrdinal(b, a));
        return found;
    }

    /// <summary>The newest snapshot's project-relative path, or null when none has been taken.</summary>
    public static string NewestPath()
    {
        List<string> all = AllPaths();
        return all.Count > 0 ? all[0] : null;
    }

    // ---- Read / write --------------------------------------------------------

    public static AudioHandoutSnapshot Read(string assetPath, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(assetPath)) { error = "no snapshot path given"; return null; }

        string absolute = AbsolutePath(assetPath);
        if (!File.Exists(absolute)) { error = $"'{assetPath}' does not exist"; return null; }

        try
        {
            var snapshot = JsonUtility.FromJson<AudioHandoutSnapshot>(File.ReadAllText(absolute));
            if (snapshot == null) { error = $"'{assetPath}' is not a snapshot (JsonUtility returned null)"; return null; }

            if (snapshot.slots == null) snapshot.slots = new List<AudioHandoutSnapshotEntry>();
            if (snapshot.routing == null) snapshot.routing = new List<AudioHandoutSnapshotEntry>();
            return snapshot;
        }
        catch (Exception e)
        {
            error = $"'{assetPath}' could not be read: {e.GetType().Name}: {e.Message}";
            return null;
        }
    }

    /// <summary>
    /// Writes a new snapshot. NEVER overwrites: a snapshot is the only way back to an
    /// authored state, and a second one taken in the same second is a mistake worth
    /// refusing rather than a file worth replacing.
    /// </summary>
    public static bool Write(AudioHandoutSnapshot snapshot, string assetPath, out string error)
    {
        error = null;
        string absolute = AbsolutePath(assetPath);

        if (File.Exists(absolute))
        {
            error = $"'{assetPath}' already exists - a snapshot is never overwritten. Wait a second and re-run.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? ProjectRoot);
            File.WriteAllText(absolute, JsonUtility.ToJson(snapshot, true));
        }
        catch (Exception e)
        {
            error = $"'{assetPath}' could not be written: {e.GetType().Name}: {e.Message}";
            return false;
        }

        // So it shows in the Project window and so a later AllPaths() sees it through the
        // same AssetDatabase everything else here uses.
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        return true;
    }

    /// <summary>
    /// The short commit hash, or "" when git is not on PATH, this is not a checkout, or the
    /// call takes too long. NEVER throws: a missing hash costs the snapshot a comment, and
    /// refusing to snapshot over it would cost the owner the way back.
    /// </summary>
    public static string GitShortHead()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
            {
                if (process == null) return "";

                // Read before waiting: a pipe that fills while nobody drains it deadlocks.
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { /* already gone */ }
                    return "";
                }

                return process.ExitCode == 0 ? output.Trim() : "";
            }
        }
        catch
        {
            return "";
        }
    }

    // ---- Building entries ----------------------------------------------------

    // GetGlobalObjectIdSlow is named for what it is. Two hundred slots share about twenty
    // hosts, so one lookup per host per operation is the difference between instant and a
    // visible pause. Cleared at the start of every tool run - an object recreated between
    // runs must get a new id, or the freshness check would miss exactly the case the
    // fallback key exists for.
    private static readonly Dictionary<UnityEngine.Object, string> IdCache =
        new Dictionary<UnityEngine.Object, string>();

    public static void ResetIdCache() => IdCache.Clear();

    /// <summary>The host's GlobalObjectId as a string - half the primary key.</summary>
    public static string IdOf(UnityEngine.Object host)
    {
        if (host == null) return "";
        if (IdCache.TryGetValue(host, out string cached)) return cached;

        string id;
        try { id = GlobalObjectId.GetGlobalObjectIdSlow(host).ToString(); }
        catch (Exception e) { id = ""; Debug.LogWarning($"[AudioHandoutSnapshot] No GlobalObjectId for '{host.name}': {e.Message}"); }

        IdCache[host] = id;
        return id;
    }

    /// <summary>A host a human can find: a hierarchy path or an asset path, plus the component type.</summary>
    public static string HostText(UnityEngine.Object host, GameObject prefabRoot, AudioHostKind kind)
    {
        if (host == null) return "<destroyed>";

        string type = host.GetType().Name;
        var component = host as Component;

        switch (kind)
        {
            case AudioHostKind.Prefab:
            {
                string assetPath = prefabRoot != null
                    ? AssetDatabase.GetAssetPath(prefabRoot)
                    : AssetDatabase.GetAssetPath(host);
                string inside = component != null ? HierarchyPath(component.transform) : host.name;
                return $"{assetPath} ▸ {inside} ({type})";
            }

            case AudioHostKind.Asset:
                return $"{AssetDatabase.GetAssetPath(host)} ({type})";

            default:
                return component != null
                    ? $"{HierarchyPath(component.transform)} ({type})"
                    : $"{host.name} ({type})";
        }
    }

    /// <summary>Hierarchy path, the way this project's logs and work orders write scene paths.</summary>
    public static string HierarchyPath(Transform transform)
    {
        if (transform == null) return "<none>";

        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }
        return path;
    }

    /// <summary>
    /// The guid + local id of a referenced asset. False (and empty values) for a null
    /// reference, which is exactly what an EMPTY slot records.
    /// </summary>
    public static bool AssetId(UnityEngine.Object asset, out string guid, out long localId, out string name)
    {
        guid = "";
        localId = 0;
        name = "";

        if (asset == null) return false;
        name = asset.name;

        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out localId))
        {
            guid = "";
            localId = 0;
            return false;
        }
        return true;
    }

    public static AudioHandoutSnapshotEntry EntryFor(AudioSlotRef slot)
    {
        AudioClip clip = slot.Clip;
        AssetId(clip, out string guid, out long localId, out string name);

        return new AudioHandoutSnapshotEntry
        {
            id = IdOf(slot.Host),
            host = HostText(slot.Host, slot.PrefabRoot, slot.HostKind),
            hostKind = slot.HostKind.ToString(),
            group = slot.GroupName,
            label = slot.Label,
            assetGuid = guid,
            assetLocalId = localId,
            assetName = name,
            state = AudioProgressScanner.ClassifyClip(clip).ToString()
        };
    }

    public static AudioHandoutSnapshotEntry EntryFor(AudioRoutingRef routing)
    {
        AudioMixerGroup group = routing.Group;
        AssetId(group, out string guid, out long localId, out string name);

        return new AudioHandoutSnapshotEntry
        {
            id = IdOf(routing.Host),
            host = HostText(routing.Host, routing.PrefabRoot, routing.HostKind),
            hostKind = routing.HostKind.ToString(),
            group = routing.GroupName,
            label = routing.Label,
            assetGuid = guid,
            assetLocalId = localId,
            assetName = name,
            state = ""
        };
    }

    // ---- Loading a recorded reference back -----------------------------------

    /// <summary>
    /// The clip an entry names. An AudioClip is the MAIN asset of its file, so a local id
    /// that no longer matches (a reimport with different settings can move it) is a note
    /// rather than a failure: the main clip at that guid is unambiguous.
    /// </summary>
    public static AudioClip LoadClip(AudioHandoutSnapshotEntry entry, out string note)
    {
        note = null;
        if (entry == null || entry.IsEmpty) return null;

        string path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
        if (string.IsNullOrEmpty(path))
        {
            note = $"guid {entry.assetGuid} ('{entry.assetName}') is not in the project any more";
            return null;
        }

        foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(candidate is AudioClip clip)) continue;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId) &&
                guid == entry.assetGuid && localId == entry.assetLocalId)
            {
                return clip;
            }
        }

        AudioClip main = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (main != null)
        {
            note = $"local id {entry.assetLocalId} no longer matches in '{path}' - used the main AudioClip '{main.name}' instead";
            return main;
        }

        note = $"'{path}' holds no AudioClip any more (wanted '{entry.assetName}')";
        return null;
    }

    /// <summary>
    /// The mixer group an entry names. ⚠ NO FALLBACK: every group in the project shares the
    /// mixer's guid and only the local id tells them apart, so a miss returns null and the
    /// caller fails loudly. Routing the whole game to whichever bus happened to load first
    /// is not a recovery.
    /// </summary>
    public static AudioMixerGroup LoadGroup(AudioHandoutSnapshotEntry entry, out string note)
    {
        note = null;
        if (entry == null || entry.IsEmpty) return null;

        string path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
        if (string.IsNullOrEmpty(path))
        {
            note = $"guid {entry.assetGuid} ('{entry.assetName}') is not in the project any more";
            return null;
        }

        foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(candidate is AudioMixerGroup group)) continue;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(group, out string guid, out long localId) &&
                guid == entry.assetGuid && localId == entry.assetLocalId)
            {
                return group;
            }
        }

        note = $"'{path}' has no AudioMixerGroup with local id {entry.assetLocalId} (wanted '{entry.assetName}') " +
               "- a group renamed is fine, a group DELETED is not, and this tool will not guess at another bus";
        return null;
    }

    /// <summary>A one-line summary for the report: what this snapshot holds.</summary>
    public string Describe(string assetPath)
    {
        string where = string.IsNullOrEmpty(assetPath) ? "<unsaved>" : assetPath;
        string at = string.IsNullOrEmpty(commit) ? "commit unknown" : "commit " + commit;
        return $"{where}  (taken {takenAt}, {at}, {implemented}/{total} implemented, " +
               $"{slots.Count} slots, {routing.Count} routing references)";
    }
}

/// <summary>
/// Pairs live references with recorded entries: by the host's GlobalObjectId + label first,
/// by the report's group + label second. Every fallback and every entry nobody claimed is
/// reported, because both mean the project's shape changed since the snapshot and the reader
/// is the one who has to decide whether that is fine.
/// </summary>
public sealed class AudioHandoutSnapshotMatcher
{
    private readonly Dictionary<string, List<AudioHandoutSnapshotEntry>> byPrimary =
        new Dictionary<string, List<AudioHandoutSnapshotEntry>>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AudioHandoutSnapshotEntry>> byFallback =
        new Dictionary<string, List<AudioHandoutSnapshotEntry>>(StringComparer.Ordinal);

    private readonly HashSet<AudioHandoutSnapshotEntry> claimed = new HashSet<AudioHandoutSnapshotEntry>();
    private readonly List<AudioHandoutSnapshotEntry> all;
    private readonly List<string> fallbacks = new List<string>();

    /// <summary>"clip slot" or "routing reference" - only used to word the log.</summary>
    private readonly string what;

    public AudioHandoutSnapshotMatcher(List<AudioHandoutSnapshotEntry> entries, string what)
    {
        this.what = what;
        all = entries ?? new List<AudioHandoutSnapshotEntry>();

        foreach (AudioHandoutSnapshotEntry entry in all)
        {
            if (entry == null) continue;
            Add(byPrimary, entry.PrimaryKey, entry);
            Add(byFallback, entry.FallbackKey, entry);
        }
    }

    private static void Add(Dictionary<string, List<AudioHandoutSnapshotEntry>> index,
                            string key, AudioHandoutSnapshotEntry entry)
    {
        if (!index.TryGetValue(key, out List<AudioHandoutSnapshotEntry> bucket))
        {
            bucket = new List<AudioHandoutSnapshotEntry>();
            index[key] = bucket;
        }
        bucket.Add(entry);
    }

    /// <summary>
    /// The entry for this live reference, or null when the snapshot has none. Each entry is
    /// handed out once, in walk order, so two rows sharing a key cannot both take it.
    /// </summary>
    public AudioHandoutSnapshotEntry Match(string id, string group, string label)
    {
        AudioHandoutSnapshotEntry entry = FirstUnclaimed(byPrimary, Key(id, label));
        if (entry != null)
        {
            claimed.Add(entry);
            return entry;
        }

        entry = FirstUnclaimed(byFallback, Key(group, label));
        if (entry == null) return null;

        claimed.Add(entry);
        fallbacks.Add($"{group} ▸ {label}: matched by group + label, not by object id " +
                      $"(recorded host '{entry.host}' - that object was recreated, or its id changed)");
        return entry;
    }

    /// <summary>The same two-part key the entries index themselves under.</summary>
    private static string Key(string first, string label)
    {
        return (first ?? "") + AudioHandoutSnapshotEntry.KeySeparator + (label ?? "");
    }

    private AudioHandoutSnapshotEntry FirstUnclaimed(
        Dictionary<string, List<AudioHandoutSnapshotEntry>> index, string key)
    {
        if (!index.TryGetValue(key, out List<AudioHandoutSnapshotEntry> bucket)) return null;

        foreach (AudioHandoutSnapshotEntry entry in bucket)
        {
            if (!claimed.Contains(entry)) return entry;
        }
        return null;
    }

    /// <summary>Every match that had to fall back to group + label, worded for the log.</summary>
    public List<string> Fallbacks => fallbacks;

    /// <summary>Entries no live reference claimed: a slot or a routing field that has gone.</summary>
    public List<AudioHandoutSnapshotEntry> Unmatched()
    {
        var left = new List<AudioHandoutSnapshotEntry>();
        foreach (AudioHandoutSnapshotEntry entry in all)
        {
            if (entry != null && !claimed.Contains(entry)) left.Add(entry);
        }
        return left;
    }

    public string What => what;
}
