using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/*
 * MusicTestStemGenerator.cs
 *
 * Purpose: Generates synthetic, layered test music so the adaptive music system can be
 * verified end-to-end before any real stems exist.
 * Used by: Tools/Music/Generate Test Stems
 *
 * Everything it writes is disposable placeholder content. Each state gets a different
 * root note AND a different BPM (deliberately, so cross-grid transitions are exercised),
 * and each layer a distinct register, so you can hear exactly what is happening:
 *
 *   Pulse (minIntensity 0.00) - a click per beat, ACCENTED AND PITCHED UP ON BEAT 1
 *                               of every bar. This is the audible bar grid: if a
 *                               transition is bar-aligned, accents never stutter.
 *   Bass  (minIntensity 0.25) - one root note per bar
 *   Pads  (minIntensity 0.50) - sustained detuned triad
 *   Lead  (minIntensity 0.75) - arpeggio, highest register
 *
 * Loop segment B is transposed up a fourth so segment changes are obvious by ear.
 *
 * Audio is 22.05kHz 16-bit mono: plenty for test tones and roughly half the disk of
 * 44.1kHz. Written as raw PCM WAV via BinaryWriter, so no runtime audio APIs are needed.
 */

public static class MusicTestStemGenerator
{
    private const int SampleRate = 22050;
    private const string OutputFolder = "Assets/Audio/TestStems";
    private const string StateAssetFolder = "Assets/Audio/Music States";

    private const int LoopBars = 8;
    private const int TransitionBars = 2;   // intro and outro length

    private class StatePreset
    {
        public MusicManager.MusicState State;
        public string Name;
        public float Bpm;
        public int BeatsPerBar;
        public float RootHz;
        public int[] Triad;     // semitone offsets from the root
        public string Key;
    }

    private static readonly StatePreset[] Presets =
    {
        new StatePreset
        {
            State = MusicManager.MusicState.Exploring, Name = "Exploring", Bpm = 110f, BeatsPerBar = 4,
            RootHz = 130.81f, Triad = new[] { 0, 4, 7 }, Key = "C major"
        },
        new StatePreset
        {
            State = MusicManager.MusicState.Fighting, Name = "Fighting", Bpm = 140f, BeatsPerBar = 4,
            RootHz = 110.00f, Triad = new[] { 0, 3, 7 }, Key = "A minor"
        },
        new StatePreset
        {
            State = MusicManager.MusicState.Inside, Name = "Inside", Bpm = 90f, BeatsPerBar = 4,
            RootHz = 98.00f, Triad = new[] { 0, 4, 7 }, Key = "G major"
        }
    };

    [MenuItem("Tools/Music/Generate Test Stems")]
    public static void Generate()
    {
        if (!EditorUtility.DisplayDialog(
                "Generate Music Test Stems",
                $"This writes placeholder WAV stems to {OutputFolder} and MusicStateDefinition " +
                $"assets to {StateAssetFolder}, overwriting any previously generated ones.\n\n" +
                "These are disposable test tones, not real music.\n\nContinue?",
                "Generate", "Cancel"))
        {
            return;
        }

        Directory.CreateDirectory(OutputFolder);
        Directory.CreateDirectory(StateAssetFolder);

        var readme = new StringBuilder();
        readme.AppendLine("# Generated music test stems");
        readme.AppendLine();
        readme.AppendLine("Everything in this folder is GENERATED PLACEHOLDER CONTENT, produced by");
        readme.AppendLine("`Tools/Music/Generate Test Stems` (MusicTestStemGenerator.cs). It exists so the");
        readme.AppendLine("adaptive music system can be verified before real stems are authored.");
        readme.AppendLine();
        readme.AppendLine("Replacing it with real music is a pure content swap: point the");
        readme.AppendLine("MusicStateDefinition assets at your clips and set bpm / beatsPerBar /");
        readme.AppendLine("lengthInBars. No code changes are needed.");
        readme.AppendLine();
        readme.AppendLine("## Layers (same in every segment)");
        readme.AppendLine();
        readme.AppendLine("| Layer | minIntensity | What you hear |");
        readme.AppendLine("|---|---|---|");
        readme.AppendLine("| Pulse | 0.00 | Click per beat, accented + pitched up on beat 1 of each bar |");
        readme.AppendLine("| Bass | 0.25 | One root note per bar |");
        readme.AppendLine("| Pads | 0.50 | Sustained detuned triad |");
        readme.AppendLine("| Lead | 0.75 | Arpeggio, highest register |");
        readme.AppendLine();
        readme.AppendLine("The Pulse accent is the audible bar grid. A bar-aligned transition never");
        readme.AppendLine("stutters that accent; a misaligned one obviously does.");
        readme.AppendLine();
        readme.AppendLine("## States");
        readme.AppendLine();
        readme.AppendLine("| State | Key | BPM | Stinger lead-in (seconds) |");
        readme.AppendLine("|---|---|---|---|");

        generatedClipPaths.Clear();

        int progress = 0;
        try
        {
            foreach (StatePreset preset in Presets)
            {
                EditorUtility.DisplayProgressBar("Generating test stems", preset.Name,
                    progress++ / (float)Presets.Length);

                GenerateStateAudio(preset);

                double beat = 60.0 / preset.Bpm;
                string leadIn = beat.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                readme.AppendLine($"| {preset.Name} | {preset.Key} | {preset.Bpm} | {leadIn} |");
            }
        }
        catch (System.Exception e)
        {
            // Abandon rather than half-complete: creating the state assets now would wire
            // them to a mix of this run's and the previous run's clips.
            Debug.LogError($"[MusicTestStemGenerator] Generation failed, no state assets were " +
                           $"written: {e}");
            EditorUtility.DisplayDialog("Generation failed",
                $"Stem generation failed and was abandoned.\n\n{e.Message}", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        readme.AppendLine();
        readme.AppendLine("Set each transition rule's `stingerLeadIn` to the value above for the state");
        readme.AppendLine("being entered, so the stinger's accent lands exactly on the sync point.");
        readme.AppendLine();
        readme.AppendLine("The three states use different BPMs on purpose, so transitions between them");
        readme.AppendLine("exercise the cross-grid path (each state runs on its own musical clock).");

        File.WriteAllText(Path.Combine(OutputFolder, "README.md"), readme.ToString());

        AssetDatabase.Refresh();

        // Import settings must be forced before the clips are referenced, so the assets
        // point at PCM audio rather than Unity's default lossy import.
        foreach (string clipPath in generatedClipPaths)
        {
            ConfigureAudioImport(clipPath);
        }

        // Clips must be imported before they can be referenced by the state assets.
        foreach (StatePreset preset in Presets)
        {
            CreateStateAsset(preset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MusicTestStemGenerator] Generated test stems in {OutputFolder} and " +
                  $"MusicStateDefinition assets in {StateAssetFolder}. Assign the assets to " +
                  "MusicManager's 'State Definitions' array.");

        EditorUtility.RevealInFinder($"{StateAssetFolder}/MusicState_{Presets[0].Name}.asset");
    }

    // ---- Audio generation ----------------------------------------------------

    private static void GenerateStateAudio(StatePreset preset)
    {
        // Loop A at the root, loop B up a perfect fourth so segment changes are obvious.
        GenerateSegment(preset, "LoopA", LoopBars, Semitones(preset.RootHz, 0));
        GenerateSegment(preset, "LoopB", LoopBars, Semitones(preset.RootHz, 5));
        GenerateSegment(preset, "Intro", TransitionBars, Semitones(preset.RootHz, 0));
        GenerateSegment(preset, "Outro", TransitionBars, Semitones(preset.RootHz, 0));
        GenerateStinger(preset);
    }

    private static void GenerateSegment(StatePreset preset, string segmentName, int bars, float rootHz)
    {
        double beat = 60.0 / preset.Bpm;
        double duration = bars * preset.BeatsPerBar * beat;
        int totalSamples = Mathf.CeilToInt((float)(duration * SampleRate));

        // A little extra room so the last note's release is not clipped. The system
        // schedules on musical length, so this simply rings past the loop point - which
        // is exactly the tail behaviour the double-buffered player is built to allow.
        int tailSamples = Mathf.CeilToInt(0.35f * SampleRate);
        int bufferSamples = totalSamples + tailSamples;

        float[] pulse = new float[bufferSamples];
        float[] bass = new float[bufferSamples];
        float[] pads = new float[bufferSamples];
        float[] lead = new float[bufferSamples];

        int totalBeats = bars * preset.BeatsPerBar;

        // Pulse: click every beat; beat 1 of each bar is louder and an octave up.
        for (int b = 0; b < totalBeats; b++)
        {
            bool downbeat = (b % preset.BeatsPerBar) == 0;
            double start = b * beat;
            AddClick(pulse, start, downbeat ? 1760f : 880f, downbeat ? 0.9f : 0.45f, 0.07);
        }

        // Bass: one root note per bar, low register.
        for (int bar = 0; bar < bars; bar++)
        {
            double start = bar * preset.BeatsPerBar * beat;
            AddTone(bass, start, beat * preset.BeatsPerBar * 0.92, rootHz * 0.5f, 0.55f, 0.01, 0.25);
        }

        // Pads: sustained detuned triad. The release is written INTO the tail, not inside
        // the bar count, so the layer is at full level at both edges of the musical
        // window. Releasing before the musical end would punch a hole at every loop
        // point and read as a broken scheduler.
        const double padRelease = 0.30;
        foreach (int semitone in preset.Triad)
        {
            float f = Semitones(rootHz, semitone);
            AddTone(pads, 0.0, duration + padRelease, f * 0.997f, 0.16f, 0.02, padRelease);
            AddTone(pads, 0.0, duration + padRelease, f * 1.003f, 0.16f, 0.02, padRelease);
        }

        // Lead: arpeggio through the triad, one note per beat, highest register.
        for (int b = 0; b < totalBeats; b++)
        {
            int semitone = preset.Triad[b % preset.Triad.Length];
            float f = Semitones(rootHz, semitone) * 4f;
            AddTone(lead, b * beat, beat * 0.55, f, 0.28f, 0.005, 0.12);
        }

        WriteWav(StemPath(preset.Name, segmentName, "Pulse"), pulse);
        WriteWav(StemPath(preset.Name, segmentName, "Bass"), bass);
        WriteWav(StemPath(preset.Name, segmentName, "Pads"), pads);
        WriteWav(StemPath(preset.Name, segmentName, "Lead"), lead);
    }

    private static void GenerateStinger(StatePreset preset)
    {
        // One beat of riser, then an accent that decays for a beat. Scheduling the clip at
        // (syncPoint - oneBeat) puts the accent exactly on the boundary.
        double beat = 60.0 / preset.Bpm;
        int samples = Mathf.CeilToInt((float)(beat * 2.0 * SampleRate));
        float[] data = new float[samples];

        int riserSamples = Mathf.CeilToInt((float)(beat * SampleRate));
        for (int i = 0; i < riserSamples && i < samples; i++)
        {
            double t = i / (double)SampleRate;
            double k = i / (double)riserSamples;
            float freq = Mathf.Lerp(220f, 1320f, (float)(k * k));
            float env = (float)(k * k) * 0.35f;
            data[i] += Mathf.Sin((float)(2.0 * Mathf.PI * freq * t)) * env;
        }

        // The accent tone and click land on the same sample and sum, so their combined
        // peak has to fit inside unity or WriteWav's clamp silently hard-clips it.
        AddTone(data, beat, beat * 0.9, Semitones(preset.RootHz, 0) * 2f, 0.55f, 0.002, 0.5);
        AddClick(data, beat, 2200f, 0.35f, 0.09);

        string leadIn = beat.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        WriteWav($"{OutputFolder}/Stinger_{preset.Name}_lead{leadIn}.wav", data);
    }

    private static string StemPath(string stateName, string segmentName, string stemName)
    {
        // Forward slashes: these double as AssetDatabase paths, which reject backslashes.
        return $"{OutputFolder}/{stateName}_{segmentName}_{stemName}.wav";
    }

    private static float Semitones(float baseHz, int semitones)
    {
        return baseHz * Mathf.Pow(2f, semitones / 12f);
    }

    /// <summary>Additive sine with linear attack and release, mixed into the buffer.</summary>
    private static void AddTone(float[] buffer, double startSeconds, double durationSeconds,
                                float frequency, float amplitude, double attack, double release)
    {
        int start = Mathf.Clamp(Mathf.RoundToInt((float)(startSeconds * SampleRate)), 0, buffer.Length);
        int count = Mathf.RoundToInt((float)(durationSeconds * SampleRate));
        int attackSamples = Mathf.Max(1, Mathf.RoundToInt((float)(attack * SampleRate)));
        int releaseSamples = Mathf.Max(1, Mathf.RoundToInt((float)(release * SampleRate)));

        for (int i = 0; i < count; i++)
        {
            int index = start + i;
            if (index >= buffer.Length) break;

            float env = 1f;
            if (i < attackSamples) env = i / (float)attackSamples;
            int fromEnd = count - i;
            if (fromEnd < releaseSamples) env = Mathf.Min(env, fromEnd / (float)releaseSamples);

            double t = i / (double)SampleRate;
            buffer[index] += Mathf.Sin((float)(2.0 * Mathf.PI * frequency * t)) * amplitude * env;
        }
    }

    /// <summary>Short exponentially-decaying burst - the metronome click.</summary>
    private static void AddClick(float[] buffer, double startSeconds, float frequency,
                                 float amplitude, double durationSeconds)
    {
        int start = Mathf.Clamp(Mathf.RoundToInt((float)(startSeconds * SampleRate)), 0, buffer.Length);
        int count = Mathf.RoundToInt((float)(durationSeconds * SampleRate));

        for (int i = 0; i < count; i++)
        {
            int index = start + i;
            if (index >= buffer.Length) break;

            double t = i / (double)SampleRate;
            float env = Mathf.Exp((float)(-28.0 * t));
            buffer[index] += Mathf.Sin((float)(2.0 * Mathf.PI * frequency * t)) * amplitude * env;
        }
    }

    private static readonly List<string> generatedClipPaths = new List<string>();

    private static void WriteWav(string path, float[] samples)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        int dataBytes = samples.Length * sizeof(short);

        // The conversion below clamps, which would hide an over-unity mix. Say so instead.
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float magnitude = Mathf.Abs(samples[i]);
            if (magnitude > peak) peak = magnitude;
        }

        if (peak > 1f)
        {
            Debug.LogWarning($"[MusicTestStemGenerator] {Path.GetFileName(path)} peaks at {peak:F3} " +
                             "and will hard-clip. Reduce the amplitudes that sum into it.");
        }

        using (var stream = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                                            // PCM chunk size
            writer.Write((short)1);                                      // PCM format
            writer.Write((short)channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * channels * bitsPerSample / 8);     // byte rate
            writer.Write((short)(channels * bitsPerSample / 8));         // block align
            writer.Write((short)bitsPerSample);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f));
            }
        }

        generatedClipPaths.Add(path);
    }

    /// <summary>
    /// Forces PCM / Decompress On Load. Unity's default import is lossy Vorbis, whose
    /// encoder delay and transient pre-echo would smear the very click transients and
    /// sample-locked stem alignment this content exists to demonstrate.
    /// </summary>
    private static void ConfigureAudioImport(string assetPath)
    {
        if (!(AssetImporter.GetAtPath(assetPath) is AudioImporter importer)) return;

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;

        importer.defaultSampleSettings = settings;
        importer.forceToMono = true;
        importer.SaveAndReimport();
    }

    // ---- State asset creation ------------------------------------------------

    private static void CreateStateAsset(StatePreset preset)
    {
        string assetPath = $"{StateAssetFolder}/MusicState_{preset.Name}.asset";

        MusicStateDefinition definition = AssetDatabase.LoadAssetAtPath<MusicStateDefinition>(assetPath);
        bool isNew = definition == null;
        if (isNew) definition = ScriptableObject.CreateInstance<MusicStateDefinition>();

        definition.state = preset.State;
        definition.bpm = preset.Bpm;
        definition.beatsPerBar = preset.BeatsPerBar;
        definition.loopOrder = MusicManager.PlaybackMode.Sequential;
        definition.avoidRepeatInRandom = true;

        definition.introSegment = BuildSegment(preset.Name, "Intro", TransitionBars);
        definition.outroSegment = BuildSegment(preset.Name, "Outro", TransitionBars);
        definition.loopSegments = new[]
        {
            BuildSegment(preset.Name, "LoopA", LoopBars),
            BuildSegment(preset.Name, "LoopB", LoopBars)
        };

        if (isNew) AssetDatabase.CreateAsset(definition, assetPath);
        else EditorUtility.SetDirty(definition);
    }

    private static MusicSegment BuildSegment(string stateName, string segmentName, int bars)
    {
        var layers = new List<MusicStem>
        {
            BuildStem(stateName, segmentName, "Pulse", 0.00f, 0.9f, 0.35f, 0.8f),
            BuildStem(stateName, segmentName, "Bass",  0.25f, 0.9f, 1.20f, 2.0f),
            BuildStem(stateName, segmentName, "Pads",  0.50f, 0.8f, 2.00f, 3.0f),
            BuildStem(stateName, segmentName, "Lead",  0.75f, 0.7f, 1.00f, 2.0f)
        };

        return new MusicSegment
        {
            segmentName = segmentName,
            lengthInBars = bars,
            stems = layers.ToArray()
        };
    }

    private static MusicStem BuildStem(string stateName, string segmentName, string stemName,
                                       float minIntensity, float volume, float fadeIn, float fadeOut)
    {
        string path = $"{OutputFolder}/{stateName}_{segmentName}_{stemName}.wav";
        return new MusicStem
        {
            stemName = stemName,
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path),
            minIntensity = minIntensity,
            volume = volume,
            fadeInTime = fadeIn,
            fadeOutTime = fadeOut
        };
    }
}
