using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/*
 * PerformanceProbe.cs
 *
 * Purpose: Records a per-frame performance series for one named scenario and writes it to
 *          <project>/Exports/Profiling/<stamp>/frames_<scenario>.csv, plus a stats_*.json
 *          snapshot at the start and the end.
 * Used by: PERFORMANCE_PROFILING_TASKS.md phase 0. Driven from the Unity CLI:
 *
 *              PerformanceProbe.Begin("S1_spawn_idle", 20f);   // returns immediately
 *              ... poll PerformanceProbe.IsDone from a later CLI call ...
 *
 * Measurement only - it never changes the scene, and the host GameObject is hidden and
 * DontDestroyOnLoad, so leaving play mode disposes of it. The one thing it CAN change is
 * vSync/targetFrameRate, and only when Begin is asked to (uncapped runs); both are put
 * back in Stop, and neither is a serialized project setting at runtime, so nothing is
 * written to disk either way.
 *
 * Why a runtime probe rather than get_performance_stats: that command returns ONE
 * snapshot. Frame time is a distribution - the mean hides the hitches, and a spike on
 * zombie spawn is the interesting part. ProfilerRecorder samples per frame with no
 * managed allocation, so the probe does not distort what it is measuring.
 *
 * The counter names below were read off THIS editor (6000.0.72f1) by enumerating
 * ProfilerRecorderHandle.GetAvailable() - 4125 of them. Counters are resolved BY NAME
 * through that same enumeration rather than by (category, name): the categories are not
 * obvious from the names ("Batches Count" is Render, "Added Contacts" is Physics), and a
 * wrong guess silently yields an invalid recorder and an empty column. A name this editor
 * does not expose is skipped with a warning; its column is written empty.
 *
 * It also records MARKERS, not just counters, into markers_<scenario>.csv: every timing
 * marker in the categories below, accumulated over the window as total/mean/max ms. That
 * is the "where does the frame go" table. It is done here rather than through
 * ProfilerDriver.SaveProfile because the editor only fills the driver's frame ring buffer
 * while a Profiler WINDOW is repainting - driving the editor headlessly from the CLI
 * leaves firstFrameIndex at -1 and exports nothing. ProfilerRecorder has no such
 * dependency, so this works unattended.
 */

[DisallowMultipleComponent]
public class PerformanceProbe : MonoBehaviour
{
    /// <summary>Where captures land, relative to the project root (gitignored).</summary>
    public const string ExportRoot = "Exports/Profiling";

    /// <summary>
    /// One capture folder per run of the work order, so a scenario re-captured after a fix
    /// lands beside the original rather than overwriting it. Set from the CLI before the
    /// first Begin; defaults to the time of the first capture.
    /// </summary>
    public static string Stamp = string.Empty;

    /// <summary>False while a capture is running. The CLI polls this between calls.</summary>
    public static bool IsDone = true;

    /// <summary>Set when a capture finishes, so the driving script can log where it went.</summary>
    public static string LastCsvPath = string.Empty;

    /// <summary>Human-readable outcome of the last capture, for the CLI to echo.</summary>
    public static string LastSummary = string.Empty;

    // ---- The counters -------------------------------------------------------

    /// <summary>
    /// name -> column header. Order here is the column order in the CSV. Every one of
    /// these was verified present in this editor; see the file header.
    /// </summary>
    private static readonly (string Stat, string Column)[] Counters =
    {
        // Timing (nanoseconds; the CSV converts to ms)
        ("CPU Total Frame Time", "cpu_total_ms"),
        ("CPU Main Thread Frame Time", "cpu_main_ms"),
        ("CPU Render Thread Frame Time", "cpu_render_ms"),
        ("Main Thread", "main_thread_ms"),

        // Rendering
        ("Batches Count", "batches"),
        ("SetPass Calls Count", "setpass"),
        ("Draw Calls Count", "draw_calls"),
        ("Triangles Count", "triangles"),
        ("Vertices Count", "vertices"),
        ("Shadow Casters Count", "shadow_casters"),
        ("Visible Skinned Meshes Count", "skinned_visible"),
        ("Render Textures Count", "rt_count"),
        ("Used Buffers Count", "buffers"),

        // Managed memory and GC - the per-frame allocation is the one that matters
        ("GC Allocated In Frame", "gc_alloc_bytes"),
        ("GC Used Memory", "gc_used_bytes"),
        ("GC Reserved Memory", "gc_reserved_bytes"),

        // Totals
        ("Total Used Memory", "total_used_bytes"),
        ("Total Reserved Memory", "total_reserved_bytes"),
        ("System Used Memory", "system_used_bytes"),
        ("Texture Memory", "texture_bytes"),
        ("Mesh Memory", "mesh_bytes"),

        // Audio - this is a sound-design project, so the audio cost is a first-class column
        ("Audio Used Memory", "audio_used_bytes"),
        ("AudioClip Count", "audioclip_count"),

        // Physics
        ("Active Dynamic Bodies", "active_dynamic_bodies"),
        ("Active Kinematic Bodies", "active_kinematic_bodies"),
        ("Added Contacts", "added_contacts"),
    };

    /// <summary>
    /// Marker categories sampled per frame. PlayerLoop is the structural breakdown of a
    /// frame (which phase costs what); the rest are the systems this level actually
    /// leans on. Render is deliberately absent - it has 669 markers and the counters
    /// above already answer the rendering question.
    /// </summary>
    private static readonly string[] MarkerCategories =
    {
        "PlayerLoop", "Ai", "Animation", "Audio", "Particles", "Physics", "GC", "Gui",
    };

    /// <summary>Counters whose unit is nanoseconds, written as milliseconds.</summary>
    private static readonly HashSet<string> NanosecondColumns = new HashSet<string>
    {
        "cpu_total_ms", "cpu_main_ms", "cpu_render_ms", "main_thread_ms",
    };

    // ---- Instance state -----------------------------------------------------

    private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
    private readonly List<string> columns = new List<string>();
    private StringBuilder csv;
    private string scenario;
    private string folder;
    private float endTime;
    private int frame;

    // Sampled every SlowSampleFrames rather than per frame: these allocate or are simply
    // not worth the per-frame cost.
    private const int SlowSampleFrames = 30;
    private int playingSources;
    private int aliveParticles;

    // Markers: one recorder each, accumulated over the window rather than written per
    // frame - 300-odd columns per row would be unreadable and the totals are the point.
    private readonly List<ProfilerRecorder> markerRecorders = new List<ProfilerRecorder>();
    private readonly List<string> markerNames = new List<string>();
    private readonly List<string> markerCategoryOf = new List<string>();
    private double[] markerTotalMs;
    private double[] markerMaxMs;
    private int[] markerFrames;

    private bool restoreVSync;
    private int previousVSync;
    private int previousTargetFrameRate;

    // ---- Entry points -------------------------------------------------------

    /// <summary>
    /// Starts a capture. Returns immediately - poll <see cref="IsDone"/>. Uncapped runs
    /// drop vSync and the frame-rate cap for the duration and restore both afterwards.
    /// </summary>
    public static string Begin(string scenarioName, float seconds, bool uncapped = false)
    {
        if (!IsDone) return "A capture is already running (" + scenarioName + " refused).";
        if (!Application.isPlaying) return "Not in play mode - nothing to capture.";

        if (string.IsNullOrEmpty(Stamp)) Stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

        var host = new GameObject("Performance Probe") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);

        var probe = host.AddComponent<PerformanceProbe>();
        probe.scenario = Sanitise(scenarioName) + (uncapped ? "_uncapped" : string.Empty);
        probe.endTime = Time.realtimeSinceStartup + Mathf.Max(1f, seconds);

        if (uncapped)
        {
            probe.restoreVSync = true;
            probe.previousVSync = QualitySettings.vSyncCount;
            probe.previousTargetFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        IsDone = false;
        return "Capturing '" + probe.scenario + "' for " + seconds.ToString("0.#") + "s into " +
               ExportRoot + "/" + Stamp + "/";
    }

    /// <summary>Stops early. Safe when nothing is running.</summary>
    public static void Abort()
    {
        foreach (PerformanceProbe probe in FindObjectsByType<PerformanceProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            probe.Finish("aborted");
        }
        IsDone = true;
    }

    // ---- Lifecycle ----------------------------------------------------------

    private void Start()
    {
        folder = Path.Combine(Path.Combine(Application.dataPath, ".."), ExportRoot.Replace('/', Path.DirectorySeparatorChar));
        folder = Path.Combine(folder, Stamp);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PerformanceProbe] Cannot create " + folder + " (" + e.Message + "); this capture records nothing.");
            Finish("no folder");
            return;
        }

        Dictionary<string, ProfilerRecorderHandle> available = AvailableCounters();

        foreach ((string stat, string column) in Counters)
        {
            ProfilerRecorder recorder = default;

            if (available.TryGetValue(stat, out ProfilerRecorderHandle handle))
            {
                // Constructor + Start rather than StartNew: the StartNew overloads take a
                // (category, name) pair, and the handle is what the name lookup gives us.
                recorder = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default);
                recorder.Start();
            }
            else
            {
                Debug.LogWarning("[PerformanceProbe] Counter '" + stat + "' is not available in this editor; " +
                                 "column '" + column + "' will be empty.");
            }

            recorders.Add(recorder);
            columns.Add(column);
        }

        StartMarkerRecorders();

        csv = new StringBuilder(1 << 16);
        csv.Append("frame,time_s,unscaled_dt_ms,fps_instant,ft_cpu_ms,ft_gpu_ms,ft_cpu_main_ms,playing_audio_sources,alive_particles");
        foreach (string column in columns) csv.Append(',').Append(column);
        csv.AppendLine();

        WriteStatsSnapshot("start");
        SampleSlow();
    }

    private void LateUpdate()
    {
        if (csv == null) return;

        FrameTimingManager.CaptureFrameTimings();
        var timings = new FrameTiming[1];
        uint got = FrameTimingManager.GetLatestTimings(1, timings);

        if (frame % SlowSampleFrames == 0) SampleSlow();

        float dt = Time.unscaledDeltaTime;

        csv.Append(frame).Append(',')
           .Append(Time.realtimeSinceStartup.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
           .Append((dt * 1000f).ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
           .Append((dt > 0f ? 1f / dt : 0f).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
           .Append(got > 0 ? timings[0].cpuFrameTime.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty).Append(',')
           .Append(got > 0 ? timings[0].gpuFrameTime.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty).Append(',')
           .Append(got > 0 ? timings[0].cpuMainThreadFrameTime.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty).Append(',')
           .Append(playingSources).Append(',')
           .Append(aliveParticles);

        for (int i = 0; i < recorders.Count; i++)
        {
            csv.Append(',');
            ProfilerRecorder recorder = recorders[i];
            if (!recorder.Valid || recorder.Count == 0) continue;

            long value = recorder.LastValue;
            if (NanosecondColumns.Contains(columns[i]))
            {
                csv.Append((value * 1e-6).ToString("0.000", CultureInfo.InvariantCulture));
            }
            else
            {
                csv.Append(value);
            }
        }

        csv.AppendLine();

        for (int i = 0; i < markerRecorders.Count; i++)
        {
            ProfilerRecorder recorder = markerRecorders[i];
            if (!recorder.Valid || recorder.Count == 0) continue;

            double ms = recorder.LastValue * 1e-6;
            if (ms <= 0.0) continue;

            markerTotalMs[i] += ms;
            markerFrames[i]++;
            if (ms > markerMaxMs[i]) markerMaxMs[i] = ms;
        }

        frame++;

        if (Time.realtimeSinceStartup >= endTime) Finish("completed");
    }

    /// <summary>
    /// The counts that are not worth a per-frame walk. Cheap enough every 30 frames, and
    /// they answer "how much audio is actually playing here", which is the question this
    /// project cares about most.
    /// </summary>
    private void SampleSlow()
    {
        playingSources = 0;
        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (source.isPlaying) playingSources++;
        }

        aliveParticles = 0;
        foreach (ParticleSystem system in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            aliveParticles += system.particleCount;
        }
    }

    private void Finish(string reason)
    {
        if (csv == null)
        {
            Cleanup();
            return;
        }

        string path = Path.Combine(folder, "frames_" + scenario + ".csv");

        try
        {
            File.WriteAllText(path, csv.ToString());
            LastCsvPath = path;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PerformanceProbe] Could not write " + path + ": " + e.Message);
        }

        WriteMarkerCsv();
        WriteStatsSnapshot("end");

        LastSummary = "[PerformanceProbe] '" + scenario + "' " + reason + ": " + frame + " frames -> " + path;
        Debug.Log(LastSummary);

        csv = null;
        Cleanup();
    }

    private void Cleanup()
    {
        foreach (ProfilerRecorder recorder in recorders)
        {
            if (recorder.Valid) recorder.Dispose();
        }
        recorders.Clear();

        foreach (ProfilerRecorder recorder in markerRecorders)
        {
            if (recorder.Valid) recorder.Dispose();
        }
        markerRecorders.Clear();

        if (restoreVSync)
        {
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousTargetFrameRate;
            restoreVSync = false;
        }

        IsDone = true;
        if (gameObject != null) Destroy(gameObject);
    }

    /// <summary>
    /// The same shape get_performance_stats returns, taken in-process at the start and the
    /// end of the window, so a capture is self-describing without a second CLI round trip.
    /// </summary>
    private void WriteStatsSnapshot(string when)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine("{");
            text.AppendLine("  \"scenario\": \"" + scenario + "\",");
            text.AppendLine("  \"when\": \"" + when + "\",");
            text.AppendLine("  \"realtime\": " + Time.realtimeSinceStartup.ToString("0.000", CultureInfo.InvariantCulture) + ",");
            text.AppendLine("  \"timeScale\": " + Time.timeScale.ToString("0.###", CultureInfo.InvariantCulture) + ",");
            text.AppendLine("  \"vSyncCount\": " + QualitySettings.vSyncCount + ",");
            text.AppendLine("  \"targetFrameRate\": " + Application.targetFrameRate + ",");
            text.AppendLine("  \"qualityLevel\": \"" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "\",");
            text.AppendLine("  \"totalAllocatedBytes\": " + UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() + ",");
            text.AppendLine("  \"totalReservedBytes\": " + UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() + ",");
            text.AppendLine("  \"monoUsedBytes\": " + UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() + ",");
            text.AppendLine("  \"monoHeapBytes\": " + UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() + ",");
            text.AppendLine("  \"dspBufferLength\": " + DspBufferLength() + ",");
            text.AppendLine("  \"audioSourcesPlaying\": " + playingSources);
            text.AppendLine("}");

            File.WriteAllText(Path.Combine(folder, "stats_" + scenario + "_" + when + ".json"), text.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PerformanceProbe] Could not write the " + when + " snapshot: " + e.Message);
        }
    }

    /// <summary>
    /// One recorder per timing marker in <see cref="MarkerCategories"/>. Started here and
    /// disposed in Cleanup; a category this editor does not have is simply absent.
    /// </summary>
    private void StartMarkerRecorders()
    {
        var wanted = new HashSet<string>(MarkerCategories, StringComparer.OrdinalIgnoreCase);

        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);

        foreach (ProfilerRecorderHandle handle in handles)
        {
            if (!handle.Valid) continue;

            ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            if (!wanted.Contains(description.Category.Name)) continue;
            if (string.IsNullOrEmpty(description.Name)) continue;

            var recorder = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default);
            recorder.Start();

            markerRecorders.Add(recorder);
            markerNames.Add(description.Name);
            markerCategoryOf.Add(description.Category.Name);
        }

        markerTotalMs = new double[markerRecorders.Count];
        markerMaxMs = new double[markerRecorders.Count];
        markerFrames = new int[markerRecorders.Count];

        Debug.Log("[PerformanceProbe] Sampling " + markerRecorders.Count + " markers across " +
                  string.Join(", ", MarkerCategories) + ".");
    }

    /// <summary>
    /// The marker table: every marker that cost anything, most expensive first. This is
    /// the "where does the frame go" export the analysis agents rank from.
    /// </summary>
    private void WriteMarkerCsv()
    {
        if (markerRecorders.Count == 0 || frame == 0) return;

        var order = new List<int>(markerRecorders.Count);
        for (int i = 0; i < markerRecorders.Count; i++)
        {
            if (markerFrames[i] > 0) order.Add(i);
        }
        order.Sort((a, b) => markerTotalMs[b].CompareTo(markerTotalMs[a]));

        var text = new StringBuilder();
        text.AppendLine("# scenario=" + scenario + " frames=" + frame +
                        " markersSampled=" + markerRecorders.Count + " markersWithTime=" + order.Count);
        text.AppendLine("marker,category,frames_present,total_ms,mean_ms_per_frame,mean_ms_when_present,max_ms_in_a_frame,pct_of_captured_time");

        double windowMs = 0.0;
        for (int i = 0; i < markerRecorders.Count; i++)
        {
            if (markerNames[i] == "PlayerLoop") windowMs = markerTotalMs[i];
        }
        if (windowMs <= 0.0)
        {
            foreach (int i in order) windowMs = Math.Max(windowMs, markerTotalMs[i]);
        }

        foreach (int i in order)
        {
            text.Append(Escape(markerNames[i])).Append(',')
                .Append(Escape(markerCategoryOf[i])).Append(',')
                .Append(markerFrames[i]).Append(',')
                .Append(markerTotalMs[i].ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append((markerTotalMs[i] / frame).ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append((markerTotalMs[i] / markerFrames[i]).ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append(markerMaxMs[i].ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                .Append((windowMs > 0 ? markerTotalMs[i] / windowMs * 100.0 : 0).ToString("0.00", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        // "markers_recorder_" not "markers_": ProfilerCaptureTools writes markers_<scenario>.csv
        // from the ProfilerDriver frame data, and the two would overwrite each other. They
        // answer different questions - the driver's table has the call hierarchy and call
        // counts but only the last ~300 frames the ring buffer held; this one is every
        // frame of the window, sampled by ProfilerRecorder.
        string path = Path.Combine(folder, "markers_recorder_" + scenario + ".csv");
        try
        {
            File.WriteAllText(path, text.ToString());
            Debug.Log("[PerformanceProbe] Markers (recorder, whole window) -> " + path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PerformanceProbe] Could not write " + path + ": " + e.Message);
        }
    }

    private static string Escape(string value)
    {
        if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Every counter this editor exposes, by name. Built once per capture; the first
    /// name wins if two categories happen to share one.
    /// </summary>
    private static Dictionary<string, ProfilerRecorderHandle> AvailableCounters()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);

        var byName = new Dictionary<string, ProfilerRecorderHandle>(handles.Count);
        foreach (ProfilerRecorderHandle handle in handles)
        {
            if (!handle.Valid) continue;

            string name = ProfilerRecorderHandle.GetDescription(handle).Name;
            if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name)) byName[name] = handle;
        }

        return byName;
    }

    private static int DspBufferLength()
    {
        AudioSettings.GetDSPBufferSize(out int length, out int _);
        return length;
    }

    private static string Sanitise(string name)
    {
        var clean = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            clean.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        return clean.Length == 0 ? "scenario" : clean.ToString();
    }
}
