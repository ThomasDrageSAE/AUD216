using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RiverAudioManager))]
public class RiverAudioManagerEditor : Editor
{
    private SerializedProperty riverProfiles;

    private void OnEnable()
    {
        riverProfiles = serializedObject.FindProperty("riverProfiles");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        AudioHandoutBanner.Draw();

        EditorGUILayout.LabelField("River Management", EditorStyles.boldLabel);
        
        // Draw array size field
        EditorGUI.BeginChangeCheck();
        int arraySize = EditorGUILayout.IntField("Size", riverProfiles.arraySize);
        if (EditorGUI.EndChangeCheck())
        {
            riverProfiles.arraySize = arraySize;
        }

        // Draw each profile under its own name. The name used to be a fixed
        // "River Area N" printed here and stored nowhere, which left the progress report
        // calling these rows "[0]" and "[1]"; it is a serialized field on RiverProfile
        // now, so the report can read it too. An empty one is seeded with the old fixed
        // name the first time this inspector draws, so existing scenes read exactly as
        // they always did - the write goes through ApplyModifiedProperties below, so it
        // is Undo-able and the owner saves it with the scene.
        for (int i = 0; i < riverProfiles.arraySize; i++)
        {
            SerializedProperty profile = riverProfiles.GetArrayElementAtIndex(i);
            SerializedProperty areaName = profile.FindPropertyRelative("areaName");

            string label = $"River Area {i + 1}";
            if (areaName != null)
            {
                if (string.IsNullOrEmpty(areaName.stringValue) && !Application.isPlaying)
                {
                    areaName.stringValue = label;
                }
                if (!string.IsNullOrEmpty(areaName.stringValue)) label = areaName.stringValue;
            }

            EditorGUILayout.PropertyField(profile, new GUIContent(label), true);
        }

        serializedObject.ApplyModifiedProperties();
    }
} 