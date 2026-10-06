using UnityEngine;

/*
 * Which of the project's two audio states is loaded: the owner's AUTHORED sounds and mixer
 * routing (teacher demos, exemplars), or the student HANDOUT (every slot a placeholder beep,
 * routing cleared). One asset, Assets/Audio/Handout/AudioHandoutState.asset, written only by
 * the three tools under Tools/Audio/Handout and read by the report window and the hand-drawn
 * manager inspectors, which show a banner while in Handout mode so nobody demos with beeps
 * by accident.
 *
 * The snapshot it names is the way back: Reinstate Authored Audio reads that file, and
 * refuses to call itself done unless the report's counts equal the ones recorded here.
 */
public enum AudioHandoutMode
{
    /// <summary>The owner's sounds and routing are in place.</summary>
    Authored,
    /// <summary>Every slot is a beep (or empty), routing cleared. What the students receive.</summary>
    Handout
}

public sealed class AudioHandoutState : ScriptableObject
{
    public const string Folder = "Assets/Audio/Handout";
    public const string AssetPath = Folder + "/AudioHandoutState.asset";
    public const string SnapshotFolder = Folder + "/Snapshots";

    [Tooltip("Authored: the owner's sounds and routing. Handout: beeps, routing cleared. Written by the Tools/Audio/Handout items only.")]
    public AudioHandoutMode mode = AudioHandoutMode.Authored;

    [Header("The snapshot this state came from, and that Reinstate will read")]
    [Tooltip("Project-relative path of the authored snapshot JSON.")]
    public string snapshotFile;
    [Tooltip("When the snapshot was taken (ISO 8601, local time).")]
    public string snapshotTakenAt;
    [Tooltip("git short hash at snapshot time, when git was reachable; otherwise empty.")]
    public string snapshotCommit;
    [Tooltip("The report's implemented count when the snapshot was taken. Reinstate must reach this number.")]
    public int implementedAtSnapshot;
    [Tooltip("The report's total when the snapshot was taken. Reinstate must reach this number.")]
    public int totalAtSnapshot;
    [Tooltip("How many mixer-group references the snapshot holds (routing fields plus AudioSource outputs).")]
    public int routingAtSnapshot;

    [Header("The switch, when in Handout mode")]
    [Tooltip("When Switch To Handout last ran (ISO 8601, local time). Empty in Authored mode.")]
    public string switchedAt;
    [Tooltip("Whether that switch cleared the mixer routing as well as the clips.")]
    public bool routingCleared;

    public bool IsHandout => mode == AudioHandoutMode.Handout;

#if UNITY_EDITOR
    /// <summary>The state asset, or null when it has never been created. Cheap enough per GUI pass.</summary>
    public static AudioHandoutState LoadOrNull()
    {
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioHandoutState>(AssetPath);
    }

    /// <summary>The state asset, created in Authored mode on first use. Folders created as needed.</summary>
    public static AudioHandoutState LoadOrCreate()
    {
        AudioHandoutState state = LoadOrNull();
        if (state != null) return state;

        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Audio"))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Audio");
        if (!UnityEditor.AssetDatabase.IsValidFolder(Folder))
            UnityEditor.AssetDatabase.CreateFolder("Assets/Audio", "Handout");

        state = CreateInstance<AudioHandoutState>();
        UnityEditor.AssetDatabase.CreateAsset(state, AssetPath);
        UnityEditor.AssetDatabase.SaveAssets();
        return state;
    }
#endif
}
