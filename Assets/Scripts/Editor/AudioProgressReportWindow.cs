using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * AudioProgressReportWindow.cs
 *
 * Purpose: Editor-side view of the student audio-progress report, so a teacher can
 * inspect a submission without entering Play mode.
 * Used by: Tools/Audio/Student Progress Report
 *
 * Same scanner as the in-game popup (AudioProgressScanner), so the two views always
 * agree. In the editor the placeholder check is stronger: anything under
 * Assets/Audio/TestStems or Assets/Audio/Demo sounds (both shown as MISSING) is caught by
 * asset path even if renamed.
 *
 * The Validate wiring button runs AudioWiringValidator, which answers the question the
 * progress bar cannot: a slot can be filled in and still never be reached, because the
 * name on the prefab does not match the name on the profile.
 */

public class AudioProgressReportWindow : EditorWindow
{
    private AudioProgressReport report;
    private bool missingOnly = true;
    private Vector2 scroll;
    private string lastExportPath;

    private static GUIStyle missingStyle;

    /// <summary>A label in the same red the in-game overlay uses for MISSING. Built once; GUI styles cannot be made outside OnGUI.</summary>
    private static GUIStyle MissingStyle
    {
        get
        {
            if (missingStyle == null)
            {
                missingStyle = new GUIStyle(EditorStyles.label);
                missingStyle.normal.textColor = new Color(1f, 0.32f, 0.32f);
                missingStyle.fontStyle = FontStyle.Bold;
            }
            return missingStyle;
        }
    }
    private List<string> wiringProblems;

    [MenuItem("Tools/Audio/Student Progress Report")]
    private static void Open()
    {
        var window = GetWindow<AudioProgressReportWindow>("Audio Progress");
        window.minSize = new Vector2(520f, 400f);
        window.Rescan();
    }

    private void Rescan()
    {
        report = AudioProgressScanner.Scan();

        // If wiring findings are on screen, refresh them with the scan - stale
        // findings after the teacher fixes a name would read as still-broken.
        if (wiringProblems != null)
        {
            wiringProblems = AudioWiringValidator.Validate();
        }

        Repaint();
    }

    private void OnFocus()
    {
        // Refresh whenever the teacher tabs back to the window - the scene may have
        // changed underneath it.
        Rescan();
    }

    private void OnGUI()
    {
        DrawHandoutHeader();

        if (report == null)
        {
            if (GUILayout.Button("Scan scene")) Rescan();
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Audio implementation progress", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Overall: {report.Implemented} / {report.Total} implemented ({report.Completion:P0})   " +
            $"MISSING: {report.Missing}");

        Rect barRect = EditorGUILayout.GetControlRect(false, 16f);
        EditorGUI.ProgressBar(barRect, report.Completion, $"{report.Completion:P0}");

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            missingOnly = GUILayout.Toggle(missingOnly, " Show missing only", GUILayout.Width(160f));
            if (GUILayout.Button("Rescan", GUILayout.Width(80f))) Rescan();
            if (GUILayout.Button("Export for grading", GUILayout.Width(140f)))
            {
                lastExportPath = AudioProgressScanner.ExportToFile(report);
                EditorUtility.RevealInFinder(lastExportPath);
            }
            if (GUILayout.Button("Validate wiring", GUILayout.Width(120f)))
            {
                wiringProblems = AudioWiringValidator.Validate();
            }
        }

        if (!string.IsNullOrEmpty(lastExportPath))
        {
            EditorGUILayout.LabelField($"Exported: {lastExportPath}", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(6f);
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawWiringSection();

        foreach (AudioGroupReport group in report.Groups)
        {
            bool groupComplete = group.Implemented == group.Total;
            if (missingOnly && groupComplete)
            {
                EditorGUILayout.LabelField($"✓ {group.Name}   {group.Implemented}/{group.Total}", EditorStyles.miniLabel);
                continue;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{group.Name}   {group.Implemented}/{group.Total} ({group.Completion:P0})",
                EditorStyles.boldLabel);

            foreach (AudioSlotInfo slot in group.Slots)
            {
                if (missingOnly && slot.State == AudioSlotState.Implemented) continue;

                // One word, in red, for everything not done - see AudioSlotState.
                if (slot.State == AudioSlotState.Implemented)
                {
                    EditorGUILayout.LabelField($"    ✓  {slot.Label}  —  {slot.ClipName}");
                }
                else
                {
                    EditorGUILayout.LabelField($"    ✗  MISSING   {slot.Label}", MissingStyle);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private const string HandoutTitleSuffix = " — HANDOUT";

    /// <summary>
    /// The handout banner above everything else, and the same warning on the tab itself:
    /// this window is usually docked, and a tab still reading "Audio Progress" while every
    /// row is a beep would mislead. The suffix is appended to whatever title the window
    /// carries and taken off again in Authored mode, and only ever assigned when the text
    /// actually changes - OnGUI runs many times a second.
    /// </summary>
    private void DrawHandoutHeader()
    {
        AudioHandoutBanner.Draw();

        string text = titleContent.text ?? string.Empty;
        bool wanted = AudioHandoutBanner.IsHandout;
        bool present = text.EndsWith(HandoutTitleSuffix);

        if (wanted && !present) titleContent.text = text + HandoutTitleSuffix;
        else if (!wanted && present) titleContent.text = text.Substring(0, text.Length - HandoutTitleSuffix.Length);
    }

    /// <summary>
    /// Shows the wiring findings above the slot list. Some findings are expected rather
    /// than broken - an unauthored "default" footstep surface, for one - so this reads as
    /// a to-do list, not a failure count.
    /// </summary>
    private void DrawWiringSection()
    {
        if (wiringProblems == null) return;

        EditorGUILayout.Space(6f);

        if (wiringProblems.Count == 0)
        {
            EditorGUILayout.HelpBox("Wiring check: every profile name matches, every surface is " +
                                    "covered, and no profile is empty.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"Wiring findings ({wiringProblems.Count})", EditorStyles.boldLabel);
        foreach (string problem in wiringProblems)
        {
            EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }
    }
}
