using UnityEditor;

/*
 * AudioHandoutBanner.cs
 *
 * Purpose: one warning strip, drawn wherever audio is authored, saying that the project is
 * currently in HANDOUT mode - every slot a placeholder beep, routing cleared - so nobody
 * demos with beeps by accident or re-authors on top of the student copy.
 * Used by: the six hand-drawn manager inspectors (Enemy, Footstep, Interaction, Player,
 * River, Weapon) and AudioProgressReportWindow's header.
 *
 * Reads AudioHandoutState.asset, which only the Tools/Audio/Handout items write. In
 * Authored mode - and when the state asset has never been created - Draw() returns before
 * it builds a single string, because it runs on every repaint of every one of those
 * inspectors. In Handout mode the message is built once and rebuilt only when the state
 * behind it changes.
 */

public static class AudioHandoutBanner
{
    private static string cachedMessage;
    private static string cachedSnapshotFile;
    private static string cachedSnapshotTakenAt;
    private static bool cachedRoutingCleared;

    /// <summary>
    /// True while the state asset says Handout. For callers that want to tint or retitle
    /// as well as banner; false when the asset does not exist yet.
    /// </summary>
    public static bool IsHandout
    {
        get
        {
            AudioHandoutState state = AudioHandoutState.LoadOrNull();
            return state != null && state.IsHandout;
        }
    }

    /// <summary>
    /// The banner, or nothing at all in Authored mode. Call it first in OnInspectorGUI or
    /// OnGUI - it draws one layout element and never consumes an event.
    /// </summary>
    public static void Draw()
    {
        AudioHandoutState state = AudioHandoutState.LoadOrNull();
        if (state == null || !state.IsHandout) return;

        EditorGUILayout.HelpBox(Message(state), MessageType.Warning);
    }

    /// <summary>
    /// The warning text: what mode this is, and the exact menu item that undoes it, naming
    /// the snapshot Reinstate will read so the owner can see which authored state is the
    /// way back. Cached against the three fields it reads, so a repaint allocates nothing.
    /// </summary>
    private static string Message(AudioHandoutState state)
    {
        if (cachedMessage != null &&
            cachedSnapshotFile == state.snapshotFile &&
            cachedSnapshotTakenAt == state.snapshotTakenAt &&
            cachedRoutingCleared == state.routingCleared)
        {
            return cachedMessage;
        }

        // A state written by the tools always names its snapshot; a hand-edited asset may
        // not, and a banner that reads "from  ()." would be worse than one that says so.
        string file = string.IsNullOrEmpty(state.snapshotFile) ? "the newest snapshot" : state.snapshotFile;
        string takenAt = string.IsNullOrEmpty(state.snapshotTakenAt) ? "date not recorded" : state.snapshotTakenAt;

        string message =
            "HANDOUT MODE — every slot is a beep and routing is cleared. " +
            "Tools ▸ Audio ▸ Handout ▸ Reinstate Authored Audio restores the authored state from " +
            file + " (" + takenAt + ").";

        if (!state.routingCleared) message += " Routing was NOT cleared.";

        cachedMessage = message;
        cachedSnapshotFile = state.snapshotFile;
        cachedSnapshotTakenAt = state.snapshotTakenAt;
        cachedRoutingCleared = state.routingCleared;
        return cachedMessage;
    }
}
