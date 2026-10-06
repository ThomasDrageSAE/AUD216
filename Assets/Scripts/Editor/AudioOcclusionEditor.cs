using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AudioOcclusion))]
public class AudioOcclusionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw the default inspector except for transitionSpeed, occludedVolume, and occludedFrequency
        DrawPropertiesExcluding(serializedObject, "transitionSpeed", "occludedVolume", "occludedFrequency");

        // Sliders go through serializedObject so edits are undoable and survive
        // domain reloads (they previously wrote straight onto the target).
        GUILayout.Space(10);

        EditorGUILayout.Slider(serializedObject.FindProperty("occludedVolume"), 0f, 1f, "Occluded Volume");

        GUILayout.Space(10);

        EditorGUILayout.Slider(serializedObject.FindProperty("occludedFrequency"), 3000f, 20000f, "Occluded Frequency");

        GUILayout.Space(10);

        EditorGUILayout.Slider(serializedObject.FindProperty("transitionSpeed"), 1f, 10f, "Transition Speed");

        // Adjust vertical space before the labels
        GUILayout.Space(-5);

        // Add a horizontal group with two labels below the transitionSpeed slider
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(EditorGUIUtility.labelWidth)); // Empty label for alignment
        GUILayout.Label("Slow", EditorStyles.miniLabel, GUILayout.Width(40));
        GUILayout.FlexibleSpace(); // Add flexible space to adjust the "Fast" label position
        GUILayout.Label("Fast", EditorStyles.miniLabel, GUILayout.Width(80)); // Adjust width as needed
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        serializedObject.ApplyModifiedProperties();
    }
}
