using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

/*
 * ProfilerCaptureTools.cs
 *
 * Purpose: Drives the Unity Profiler around one capture window and exports what it
 *          recorded as a .raw (for the Profiler window) and a per-marker CSV.
 * Menu:    Tools/Profiling/...
 * Used by: PERFORMANCE_PROFILING_TASKS.md phase 0, alongside PerformanceProbe.
 *
 * Measurement only. Nothing here touches the scene, an asset or a project setting, so
 * there is nothing to save afterwards - the exports land in <project>/Exports/Profiling/,
 * which is gitignored.
 *
 * Deep profiling is deliberately NOT enabled: it forces a domain reload, multiplies every
 * managed call's cost, and would make the numbers unusable for ranking. The marker CSV
 * below is built from the ordinary (non-deep) sampling, which is what a fix has to beat.
 *
 * ⚠ Binds to nothing by string except the marker names it FILTERS on, so a rename
 * elsewhere cannot break it silently.
 */

public static class ProfilerCaptureTools
{
    /// <summary>
    /// Names the export files. Set from the CLI before Stop so the pair of files matches
    /// the PerformanceProbe CSV for the same window.
    /// </summary>
    public static string Scenario = "unnamed";

    /// <summary>Mirrors PerformanceProbe.Stamp so both writers share one folder.</summary>
    public static string Stamp = string.Empty;

    /// <summary>How many markers the CSV lists by cost, before the always-keep filters.</summary>
    private const int TopMarkers = 60;

    /// <summary>
    /// Markers matching any of these are kept whatever they cost, because "it is cheap"
    /// is itself a finding worth recording for these systems.
    /// </summary>
    private static readonly string[] AlwaysKeep =
    {
        "Audio", "Physics", "Nav", "Animator", "Animation", "Particle",
        "Terrain", "Shadow", "Culling", "GC", "Exception", "Gfx", "Render",
    };

    [MenuItem("Tools/Profiling/Start Profiler Recording")]
    private static void StartRecording()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[ProfilerCaptureTools] Not in play mode. Enter play mode first, or the " +
                             "capture records the editor loop rather than the game.");
        }

        // The editor only fills the profiler's frame ring buffer while something is
        // consuming it: with no Profiler window open, ProfilerDriver.enabled = true
        // records nothing and firstFrameIndex stays -1. Opening the window (docked,
        // not focused) is what makes a headless capture work.
        EditorWindow profilerWindow = OpenProfilerWindow();

        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.deepProfiling = false;
        Profiler.enabled = true;
        ProfilerDriver.enabled = true;

        Debug.Log("[ProfilerCaptureTools] Recording (profiler window " +
                  (profilerWindow != null ? "open" : "NOT open - frames may not be collected") +
                  "). deepProfiling=false on purpose - it distorts every managed call. " +
                  "Stop with Tools/Profiling/Stop Recording And Export.");
    }

    [MenuItem("Tools/Profiling/Stop Recording And Export")]
    private static void StopAndExport()
    {
        if (!ProfilerDriver.enabled)
        {
            Debug.Log("[ProfilerCaptureTools] The profiler was not recording - nothing to export.");
            return;
        }

        ProfilerDriver.enabled = false;
        Profiler.enabled = false;

        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;

        if (first < 0 || last < first)
        {
            Debug.LogWarning("[ProfilerCaptureTools] The profiler captured no frames (first=" + first +
                             ", last=" + last + "). Nothing exported.");
            return;
        }

        string folder = EnsureFolder();
        if (folder == null) return;

        string raw = Path.Combine(folder, "profile_" + Sanitise(Scenario) + ".raw");
        try
        {
            ProfilerDriver.SaveProfile(raw);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ProfilerCaptureTools] SaveProfile failed: " + e.Message);
            raw = "<not written>";
        }

        string csv = WriteMarkerCsv(folder, first, last);

        Debug.Log("[ProfilerCaptureTools] Exported frames " + first + "-" + last + " (" +
                  (last - first + 1) + ") for '" + Scenario + "'.\n  raw: " + raw + "\n  markers: " + csv +
                  "\nOpen the .raw with Window > Analysis > Profiler > Load.");
    }

    /// <summary>
    /// Walks every captured frame's main thread and totals each marker. This is the table
    /// the analysis agents rank from: name, how often it ran, its total and mean cost, its
    /// worst frame, and its share of the main thread across the window.
    /// </summary>
    private static string WriteMarkerCsv(string folder, int first, int last)
    {
        var total = new Dictionary<string, double>();
        var max = new Dictionary<string, double>();
        var frames = new Dictionary<string, int>();
        var calls = new Dictionary<string, long>();

        double mainThreadTotalMs = 0.0;
        int framesRead = 0;

        for (int frame = first; frame <= last; frame++)
        {
            using (var data = ProfilerDriver.GetRawFrameDataView(frame, 0))
            {
                if (!data.valid) continue;
                framesRead++;
                mainThreadTotalMs += data.frameTimeMs;

                var perFrame = new Dictionary<string, double>();

                for (int i = 0; i < data.sampleCount; i++)
                {
                    string name = data.GetSampleName(i);
                    if (string.IsNullOrEmpty(name)) continue;

                    double ms = data.GetSampleTimeMs(i);
                    if (ms <= 0.0) continue;

                    perFrame.TryGetValue(name, out double soFar);
                    perFrame[name] = soFar + ms;

                    calls.TryGetValue(name, out long count);
                    calls[name] = count + 1;
                }

                foreach (KeyValuePair<string, double> pair in perFrame)
                {
                    total.TryGetValue(pair.Key, out double sum);
                    total[pair.Key] = sum + pair.Value;

                    max.TryGetValue(pair.Key, out double worst);
                    if (pair.Value > worst) max[pair.Key] = pair.Value;

                    frames.TryGetValue(pair.Key, out int seen);
                    frames[pair.Key] = seen + 1;
                }
            }
        }

        var ranked = new List<KeyValuePair<string, double>>(total);
        ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

        var keep = new List<KeyValuePair<string, double>>();
        for (int i = 0; i < ranked.Count; i++)
        {
            if (i < TopMarkers || MatchesAlwaysKeep(ranked[i].Key)) keep.Add(ranked[i]);
        }

        var text = new StringBuilder();
        text.AppendLine("# scenario=" + Scenario + " frames=" + framesRead + " mainThreadTotalMs=" +
                        mainThreadTotalMs.ToString("0.000", CultureInfo.InvariantCulture) +
                        " meanFrameMs=" + (framesRead > 0 ? (mainThreadTotalMs / framesRead) : 0).ToString("0.000", CultureInfo.InvariantCulture));
        text.AppendLine("marker,frames_seen,calls,total_ms,mean_ms_per_captured_frame,mean_ms_when_present,max_ms_in_a_frame,share_of_main_thread_pct");

        foreach (KeyValuePair<string, double> entry in keep)
        {
            string name = entry.Key;
            double sum = entry.Value;
            int seen = frames.TryGetValue(name, out int s) ? s : 0;
            long callCount = calls.TryGetValue(name, out long c) ? c : 0;
            double worst = max.TryGetValue(name, out double w) ? w : 0.0;

            text.Append(Escape(name)).Append(',')
                .Append(seen).Append(',')
                .Append(callCount).Append(',')
                .Append(sum.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append((framesRead > 0 ? sum / framesRead : 0).ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append((seen > 0 ? sum / seen : 0).ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append(worst.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append((mainThreadTotalMs > 0 ? sum / mainThreadTotalMs * 100.0 : 0).ToString("0.00", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        string path = Path.Combine(folder, "markers_" + Sanitise(Scenario) + ".csv");
        try
        {
            File.WriteAllText(path, text.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ProfilerCaptureTools] Could not write " + path + ": " + e.Message);
            return "<not written>";
        }

        return path;
    }

    /// <summary>
    /// A read-only inventory of every texture, mesh, audio clip and material the project
    /// holds - what Project Auditor would tell us about assets, without installing it.
    /// </summary>
    [MenuItem("Tools/Profiling/Export Asset Inventory")]
    private static void ExportAssetInventory()
    {
        string folder = EnsureFolder();
        if (folder == null) return;

        var text = new StringBuilder();
        text.AppendLine("kind,path,detail1,detail2,detail3,detail4,detail5");

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) continue;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            text.Append("texture,").Append(Escape(path)).Append(',')
                .Append(texture.width).Append('x').Append(texture.height).Append(',')
                .Append(texture.format).Append(',')
                .Append("mips=").Append(texture.mipmapCount).Append(',')
                .Append(importer != null ? "maxSize=" + importer.maxTextureSize : "maxSize=?").Append(',')
                .Append(importer != null ? "compression=" + importer.textureCompression : "compression=?")
                .AppendLine();
        }

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            AudioImporterSampleSettings settings = importer != null ? importer.defaultSampleSettings : default;

            text.Append("audio,").Append(Escape(path)).Append(',')
                .Append(clip.length.ToString("0.00", CultureInfo.InvariantCulture)).Append("s,")
                .Append(clip.channels).Append("ch@").Append(clip.frequency).Append(',')
                .Append("load=").Append(importer != null ? settings.loadType.ToString() : "?").Append(',')
                .Append("compression=").Append(importer != null ? settings.compressionFormat.ToString() : "?").Append(',')
                .Append("preload=").Append(importer != null ? settings.preloadAudioData.ToString() : "?")
                .AppendLine();
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Mesh"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) continue;

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            text.Append("mesh,").Append(Escape(path)).Append(',')
                .Append(mesh.vertexCount).Append("verts,")
                .Append(mesh.subMeshCount).Append("sub,")
                .Append("readwrite=").Append(importer != null ? importer.isReadable.ToString() : mesh.isReadable.ToString()).Append(',')
                .Append("compression=").Append(importer != null ? importer.meshCompression.ToString() : "?").Append(',')
                .Append("blendshapes=").Append(mesh.blendShapeCount)
                .AppendLine();
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null) continue;

            text.Append("material,").Append(Escape(path)).Append(',')
                .Append(Escape(material.shader.name)).Append(',')
                .Append("renderQueue=").Append(material.renderQueue).Append(',')
                .Append("passes=").Append(material.passCount).Append(",,")
                .AppendLine();
        }

        string csv = Path.Combine(folder, "assets_inventory.csv");
        try
        {
            File.WriteAllText(csv, text.ToString());
            Debug.Log("[ProfilerCaptureTools] Asset inventory -> " + csv);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ProfilerCaptureTools] Could not write " + csv + ": " + e.Message);
        }
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>
    /// Shows the Profiler window without stealing focus from the game view. Returns null
    /// when the type cannot be found, which is not fatal - the caller says so in the log.
    /// </summary>
    private static EditorWindow OpenProfilerWindow()
    {
        Type type = typeof(Editor).Assembly.GetType("UnityEditor.ProfilerWindow");
        if (type == null) return null;

        try
        {
            EditorWindow window = EditorWindow.GetWindow(type, false, "Profiler", false);
            if (window != null) window.Repaint();
            return window;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ProfilerCaptureTools] Could not open the Profiler window: " + e.Message);
            return null;
        }
    }

    private static string EnsureFolder()
    {
        if (string.IsNullOrEmpty(Stamp)) Stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

        string folder = Path.Combine(Path.Combine(Application.dataPath, ".."),
            Path.Combine("Exports", Path.Combine("Profiling", Stamp)));

        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ProfilerCaptureTools] Cannot create " + folder + ": " + e.Message);
            return null;
        }
    }

    private static bool MatchesAlwaysKeep(string marker)
    {
        foreach (string needle in AlwaysKeep)
        {
            if (marker.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static string Escape(string value)
    {
        if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Sanitise(string name)
    {
        var clean = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            clean.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        return clean.Length == 0 ? "unnamed" : clean.ToString();
    }
}
