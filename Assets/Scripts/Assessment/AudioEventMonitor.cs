using UnityEngine;

/*
 * AudioEventMonitor.cs
 *
 * Purpose: Live view of the audio events in AudioEventLog - what fired, what answered
 *          it, and what found nothing.
 * Toggle:  Ctrl+J.
 *
 * The companion to the Ctrl+G progress report: that one answers "how much have I filled
 * in", this one answers "is it being reached, and when". A student who cannot hear a
 * sound opens this, performs the action, and sees either nothing (the event never
 * fired - a gameplay wiring problem) or a red Miss row naming the key that failed (a
 * profile-name problem, with the name spelled out).
 *
 * Self-installing and NOT dev-gated, matching AudioProgressReportUI: this is an
 * assessment tool, so it has to work in the build the student hands in. It draws on the
 * right so it can be open alongside the progress report.
 */

[DisallowMultipleComponent]
public class AudioEventMonitor : MonoBehaviour
{
    private const float Width = 301f;   // 30% narrower than the original 430
    private const int MaxRows = 16;

    // The whole overlay is drawn at 200% via GUI.matrix (owner request - the raw
    // panel read too small on a high-resolution display). Layout coordinates below
    // are therefore in the halved "virtual" view.
    // 200% on the reference view, and in proportion to the screen height elsewhere - see
    // AudioOverlays.ResolutionScale. A fixed 2x was a quarter-width panel at 4K.
    private const float BaseScale = 2f;
    private static float Scale => AudioOverlays.ResolutionScale(BaseScale);

    private GUIStyle labelStyle;
    private GUIStyle headerStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Object.FindFirstObjectByType<AudioEventMonitor>() != null) return;

        var host = new GameObject("Audio Event Monitor");
        host.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<AudioEventMonitor>();
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.J))
        {
            AudioOverlays.Toggle(AudioOverlay.EventMonitor);
        }
    }

    private void OnGUI()
    {
        if (!AudioOverlays.IsShowing(AudioOverlay.EventMonitor)) return;

        EnsureStyles();

        // Saved and restored: the pause menu draws its own scaled GUI in the same frame,
        // and GUI.matrix is a global that would otherwise leak between the two.
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        float viewWidth = Screen.width / Scale;
        float viewHeight = Screen.height / Scale;

        float height = Mathf.Min(viewHeight - 40f, 380f);
        Rect area = new Rect(viewWidth - Width - 20f, 20f, Width, height);

        GUILayout.BeginArea(area, GUI.skin.box);

        AudioOverlays.DrawCloseButton(Width);

        GUILayout.Label("<b>AUDIO EVENTS</b>   (Ctrl+J to close)", headerStyle);

        // The whole ring is scanned rather than its newest MaxRows: footstep entries are
        // still recorded, just hidden, and slicing the top would let them crowd out the
        // rows actually worth showing.
        int rows = 0;
        foreach (AudioEventEntry entry in AudioEventLog.Recent(AudioEventLog.Capacity))
        {
            if (entry.Noisy) continue;

            rows++;
            float age = Time.unscaledTime - entry.Time;
            GUILayout.Label(
                $"<color={ColourOf(entry.Kind)}>{age,5:0.0}s  {entry.Source}  →  {entry.Detail}</color>",
                labelStyle);

            if (rows >= MaxRows) break;
        }

        if (rows == 0)
        {
            GUILayout.Label("Nothing yet - swing a weapon, pick something up, or walk past an enemy.",
                labelStyle);
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label(
            "<color=#ff6b6b>Red</color> = a lookup found no profile of that name, so nothing can play.\n" +
            "Footsteps are hidden here (too frequent) - but a footstep MISS still shows.",
            labelStyle);

        GUILayout.EndArea();

        GUI.matrix = previousMatrix;
    }

    private static string ColourOf(AudioEventKind kind)
    {
        switch (kind)
        {
            case AudioEventKind.Miss:  return "#ff6b6b";
            case AudioEventKind.State: return "#6bc8ff";
            default:                   return "#ffffff";
        }
    }

    private void EnsureStyles()
    {
        if (labelStyle != null) return;

        labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = false };
        headerStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
    }
}
