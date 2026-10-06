using UnityEngine;
using UnityEditor;
using Audio;

[CustomEditor(typeof(FootstepAudioManager))]
public class FootstepAudioManagerEditor : Editor
{
    private SerializedProperty surfaceProfiles;
    private SerializedProperty movementProfile;
    private SerializedProperty audioSource;
    private SerializedProperty sfxMixerGroup;
    private SerializedProperty minPitchVariation;
    private SerializedProperty maxPitchVariation;

    private bool[] foldoutStates;

    private void OnEnable()
    {
        surfaceProfiles = serializedObject.FindProperty("surfaceProfiles");
        movementProfile = serializedObject.FindProperty("movementProfile");
        audioSource = serializedObject.FindProperty("audioSource");
        sfxMixerGroup = serializedObject.FindProperty("sfxMixerGroup");
        minPitchVariation = serializedObject.FindProperty("minPitchVariation");
        maxPitchVariation = serializedObject.FindProperty("maxPitchVariation");

        InitializeFoldoutStates();
    }

    private void InitializeFoldoutStates()
    {
        if (foldoutStates == null || foldoutStates.Length != surfaceProfiles.arraySize)
        {
            bool[] newStates = new bool[surfaceProfiles.arraySize];
            for (int i = 0; i < newStates.Length; i++)
            {
                newStates[i] = true; // Default to expanded
            }
            foldoutStates = newStates;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        AudioHandoutBanner.Draw();

        EditorGUILayout.LabelField("Footstep Surface Sounds", EditorStyles.boldLabel);
        
        // Resize foldout states if array size changes
        if (foldoutStates == null || foldoutStates.Length != surfaceProfiles.arraySize)
        {
            bool[] newStates = new bool[surfaceProfiles.arraySize];
            if (foldoutStates != null)
            {
                for (int i = 0; i < Mathf.Min(foldoutStates.Length, newStates.Length); i++)
                    newStates[i] = foldoutStates[i];
            }
            foldoutStates = newStates;
        }

        // Draw array elements with foldouts
        for (int i = 0; i < surfaceProfiles.arraySize; i++)
        {
            SerializedProperty element = surfaceProfiles.GetArrayElementAtIndex(i);
            SerializedProperty surfaceType = element.FindPropertyRelative("surfaceType");
            SerializedProperty footstepSounds = element.FindPropertyRelative("footstepSounds");
            SerializedProperty baseVolume = element.FindPropertyRelative("baseVolume");

            string label = string.IsNullOrEmpty(surfaceType.stringValue) ? $"Surface {i}" : surfaceType.stringValue;
            
            // Use stored foldout state
            foldoutStates[i] = EditorGUILayout.Foldout(foldoutStates[i], label, true);
            
            if (foldoutStates[i])
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(surfaceType, new GUIContent("Surface Type"));

                // The whole array, not a loop over the elements it happens to hold. Drawing
                // each element by hand rendered the clips already there but no size field
                // and no add/remove buttons, so a surface was stuck at whatever count the
                // scene was authored with - five wood footsteps and no way to add a sixth.
                // includeChildren gives the standard expandable list, matching the jump and
                // land arrays below and every other audio manager's inspector.
                EditorGUILayout.PropertyField(footstepSounds, new GUIContent("Footstep Sounds"), true);

                EditorGUILayout.PropertyField(baseVolume);
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(5);
        }

        EditorGUILayout.Space(10);
        // Remove headers and update labels
        SerializedProperty jumpSounds = movementProfile.FindPropertyRelative("jumpSounds");
        SerializedProperty jumpVolume = movementProfile.FindPropertyRelative("jumpVolume");
        SerializedProperty landSounds = movementProfile.FindPropertyRelative("landSounds");
        SerializedProperty landVolume = movementProfile.FindPropertyRelative("landVolume");

        EditorGUILayout.PropertyField(jumpSounds, new GUIContent("Jump Sounds"));
        EditorGUILayout.PropertyField(jumpVolume, new GUIContent("Jump Volume"));
        EditorGUILayout.PropertyField(landSounds, new GUIContent("Land Sounds"));
        EditorGUILayout.PropertyField(landVolume, new GUIContent("Land Volume"));
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Variation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(minPitchVariation, new GUIContent("Min Pitch"));
        EditorGUILayout.PropertyField(maxPitchVariation, new GUIContent("Max Pitch"));

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Audio Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(audioSource, new GUIContent("Audiosource"));
        EditorGUILayout.PropertyField(sfxMixerGroup, new GUIContent("Mixer Group Output"));

        serializedObject.ApplyModifiedProperties();
    }
} 