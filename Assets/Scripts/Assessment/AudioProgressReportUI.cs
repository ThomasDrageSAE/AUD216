using UnityEngine;

/*
 * AudioProgressReportUI.cs
 *
 * Purpose: In-game popup showing the student's audio implementation progress -
 * every AudioClip slot in the scene, shown as implemented or MISSING (in red - the
 * scanner still tells a beep from a demo clip from an empty slot, the report does not), with
 * per-manager and overall completion.
 * Used by: students during play-testing, and the teacher for grading.
 *
 * Toggle with Ctrl+G ("grade"). Self-installing: a RuntimeInitializeOnLoadMethod adds
 * it to a hidden GameObject, so no scene wiring is required and it is present in
 * builds too - it is an assessment tool, so unlike the debug overlays it is NOT
 * dev-gated.
 *
 * "Export" writes a timestamped text summary the teacher can keep with marks:
 * <project>/Exports in the editor, persistentDataPath in a build.
 */

[DisallowMultipleComponent]
public class AudioProgressReportUI : MonoBehaviour
{
    private const float Width = 560f;

    // The whole popup is drawn scaled via GUI.matrix: 200% on the reference view (owner
    // request - the raw panel read too small on a high-resolution display), and in
    // proportion to the screen height everywhere else, so a 4K view gets the same panel as
    // the Free Aspect one and not a third of it. Layout coordinates below are in the
    // scaled-down "virtual" view.
    private const float BaseScale = 2f;
    private static float Scale => AudioOverlays.ResolutionScale(BaseScale);

    private AudioProgressReport report;
    private bool wasVisible;
    private bool missingOnly = true;
    private Vector2 scroll;
    private string lastExportPath;

    private GUIStyle labelStyle;
    private GUIStyle headerStyle;
    private GUIStyle panelStyle;
    private Texture2D panelBackground;
    private Texture2D barBackground;
    private Texture2D barFill;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Object.FindFirstObjectByType<AudioProgressReportUI>() != null) return;

        var host = new GameObject("Audio Progress Report");
        host.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<AudioProgressReportUI>();
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.G))
        {
            AudioOverlays.Toggle(AudioOverlay.ProgressReport);
        }
    }

    private void OnGUI()
    {
        if (!AudioOverlays.IsShowing(AudioOverlay.ProgressReport))
        {
            wasVisible = false;
            return;
        }

        EnsureStyles();

        // Rescanned each time it opens, by whichever route opened it - the Ctrl+G key or
        // the pause-menu button. Both go through AudioOverlays, so neither can be the
        // only one that refreshes.
        if (!wasVisible || report == null)
        {
            report = AudioProgressScanner.Scan();
            wasVisible = true;
        }

        // Saved and restored: the pause menu draws its own scaled GUI in the same frame,
        // and GUI.matrix is a global that would otherwise leak between the two.
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        float viewWidth = Screen.width / Scale;
        float viewHeight = Screen.height / Scale;

        float height = Mathf.Min(viewHeight - 40f, 640f);
        Rect area = new Rect((viewWidth - Width) * 0.5f, 20f, Width, height);

        GUILayout.BeginArea(area, panelStyle);

        AudioOverlays.DrawCloseButton(Width);

        // --- Header ------------------------------------------------------------
        GUILayout.Label("<b>AUDIO IMPLEMENTATION PROGRESS</b>", headerStyle);
        GUILayout.Label(
            $"Overall: <b>{report.Implemented} / {report.Total}</b> implemented ({report.Completion:P0})   " +
            $"·  <color={MissingHex}><b>MISSING {report.Missing}</b></color>",
            labelStyle);
        DrawBar(report.Completion, CompletionColor(report.Completion), 14f);

        GUILayout.BeginHorizontal();
        missingOnly = GUILayout.Toggle(missingOnly, " Show missing only", GUILayout.Width(160f));
        if (GUILayout.Button("Rescan", GUILayout.Width(70f)))
        {
            report = AudioProgressScanner.Scan();
        }
        if (GUILayout.Button("Export", GUILayout.Width(140f)))
        {
            lastExportPath = AudioProgressScanner.ExportToFile(report);
            Debug.Log($"[AudioProgressReport] Exported to {lastExportPath}");
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(lastExportPath))
        {
            GUILayout.Label($"Exported: {lastExportPath}", labelStyle);
        }

        GUILayout.Space(4f);

        // --- Groups --------------------------------------------------------------
        scroll = GUILayout.BeginScrollView(scroll);

        foreach (AudioGroupReport group in report.Groups)
        {
            bool groupComplete = group.Implemented == group.Total;
            if (missingOnly && groupComplete)
            {
                GUILayout.Label($"<color=#7ec87e><b>✓ {group.Name}</b>  {group.Implemented}/{group.Total}</color>", labelStyle);
                continue;
            }

            GUILayout.Space(4f);
            GUILayout.Label($"<b>{group.Name}</b>   {group.Implemented}/{group.Total} ({group.Completion:P0})", labelStyle);
            DrawBar(group.Completion, CompletionColor(group.Completion), 8f);

            foreach (AudioSlotInfo slot in group.Slots)
            {
                if (missingOnly && slot.State == AudioSlotState.Implemented) continue;

                // A row not done says MISSING, in red, whatever kind of not-done it is -
                // empty, a beep, a demo clip - and carries no clip name: it is the same few
                // demo files every time, and the label is what the student needs to read.
                string line = slot.State == AudioSlotState.Implemented
                    ? $"  <color=#7ec87e>✓</color> {slot.Label}  —  {slot.ClipName}"
                    : $"  <color={MissingHex}><b>✗ MISSING</b></color> {slot.Label}";
                GUILayout.Label(line, labelStyle);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.matrix = previousMatrix;
    }

    /// <summary>The red every MISSING row and count is drawn in.</summary>
    private const string MissingHex = "#ff5252";

    private static Color CompletionColor(float completion)
    {
        if (completion >= 0.999f) return new Color(0.49f, 0.78f, 0.49f);
        if (completion >= 0.5f) return new Color(1f, 0.70f, 0.28f);
        return new Color(1f, 0.42f, 0.42f);
    }

    private void DrawBar(float value01, Color fill, float barHeight)
    {
        Rect rect = GUILayoutUtility.GetRect(Width - 30f, barHeight);
        GUI.DrawTexture(rect, barBackground);

        Rect filled = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value01), rect.height);
        Color previous = GUI.color;
        GUI.color = fill;
        GUI.DrawTexture(filled, barFill);
        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = true };
            headerStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
        }

        if (panelBackground == null)
        {
            // The default GUI.skin.box is translucent, so the level showed through the
            // report and made it hard to read. A flat opaque dark grey instead.
            panelBackground = new Texture2D(1, 1);
            panelBackground.SetPixel(0, 0, new Color(0.13f, 0.13f, 0.14f, 1f));
            panelBackground.Apply();

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelBackground;
            // The skin's border 9-slices its own texture; against a 1x1 fill it would
            // just smear the edges.
            panelStyle.border = new RectOffset(0, 0, 0, 0);
            panelStyle.padding = new RectOffset(12, 12, 10, 10);
        }

        if (barBackground == null)
        {
            barBackground = new Texture2D(1, 1);
            barBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            barBackground.Apply();
        }

        if (barFill == null)
        {
            barFill = new Texture2D(1, 1);
            barFill.SetPixel(0, 0, Color.white);
            barFill.Apply();
        }
    }

    private void OnDestroy()
    {
        if (panelBackground != null) Destroy(panelBackground);
        if (barBackground != null) Destroy(barBackground);
        if (barFill != null) Destroy(barFill);
    }
}
