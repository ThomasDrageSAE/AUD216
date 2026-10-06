using UnityEngine;
using UnityEditor;
using static InteractionAudioManager;

[CustomEditor(typeof(InteractionAudioManager))]
public class InteractionAudioManagerEditor : Editor
{
    SerializedProperty interactionProfiles;
    private bool[] foldoutStates;

    private void OnEnable()
    {
        interactionProfiles = serializedObject.FindProperty("interactionProfiles");
        InitializeFoldoutStates();
    }

    private void InitializeFoldoutStates()
    {
        foldoutStates = new bool[interactionProfiles.arraySize];
        for (int i = 0; i < foldoutStates.Length; i++)
        {
            foldoutStates[i] = true;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        AudioHandoutBanner.Draw();

        // Re-sync with the profile count - Undo/multi-select/external edits can
        // change arraySize between OnEnable and here (IndexOutOfRange otherwise).
        if (foldoutStates == null || foldoutStates.Length != interactionProfiles.arraySize)
        {
            InitializeFoldoutStates();
        }

        for (int i = 0; i < interactionProfiles.arraySize; i++)
        {
            EditorGUI.indentLevel = 0;
            SerializedProperty profile = interactionProfiles.GetArrayElementAtIndex(i);
            SerializedProperty interactionName = profile.FindPropertyRelative("interactionName");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            string displayName = !string.IsNullOrEmpty(interactionName.stringValue) 
                ? interactionName.stringValue 
                : $"Profile {i + 1}";
            foldoutStates[i] = EditorGUILayout.Foldout(foldoutStates[i], displayName, true);
            EditorGUILayout.EndHorizontal();

            if (foldoutStates[i])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(interactionName);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("interactionType"));
                
                // The clip fields drawn per type are mirrored in
                // InteractionSoundProfile.ClipFieldsFor, which the Ctrl+G progress report
                // uses to hide the slots a type never plays. Change both together.
                var interactionType = (InteractionType)profile.FindPropertyRelative("interactionType").enumValueIndex;
                
                switch (interactionType)
                {
                    case InteractionType.SimpleInteraction:
                        EditorGUILayout.LabelField("Simple Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("interactionSound"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("customAudioSource"));
                        break;

                    case InteractionType.ElectricalBox:
                        EditorGUILayout.LabelField("Electrical Box Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("leverSound"));
                        EditorGUILayout.Space(5);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("sparksSound"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("customAudioSource"));
                        break;

                    // GeneralInteraction draws nothing: its one clip field, interactionSounds[],
                    // was deleted 2026-09-08 because nothing in the project played it. The enum
                    // member is kept because it serializes as an int (InteractionAudioManager),
                    // so this switch simply has no case for it and only Audio Routing is drawn.

                    case InteractionType.Firesticks:
                    case InteractionType.Campfire:
                        EditorGUILayout.LabelField("Fire Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("igniteSounds"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("fireLoop"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("fireLoopStartDelay"));
                        break;

                    case InteractionType.MixingDesk:
                        EditorGUILayout.LabelField("Mixing Desk Audio", EditorStyles.boldLabel);
                        
                        EditorGUILayout.LabelField("Left Speaker", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("leftSpeakerAudio"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("leftSpeakerSource"));
                        
                        EditorGUILayout.Space(5);
                        
                        EditorGUILayout.LabelField("Right Speaker", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("rightSpeakerAudio"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("rightSpeakerSource"));
                        
                        EditorGUILayout.Space(5);
                        break;

                    case InteractionType.Flashlight:
                        EditorGUILayout.LabelField("Flashlight Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("toggleOnSound"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("toggleOffSound"));
                        break;

                    case InteractionType.Cooking:
                        EditorGUILayout.LabelField("Cooking Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("sizzleLoop"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("doneChime"));
                        break;

                    case InteractionType.Fishing:
                        EditorGUILayout.LabelField("Fishing Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("castSound"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("biteSound"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("caughtSound"));
                        EditorGUILayout.LabelField("Per-tier bite/catch overrides live on the rod's Fish Tiers.",
                            EditorStyles.miniLabel);
                        break;

                    case InteractionType.Mushroom:
                        EditorGUILayout.LabelField("Mushroom Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("interactionSound"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("customAudioSource"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("dialogueLines"));
                        break;

                    case InteractionType.WalkieTalkie:
                        EditorGUILayout.LabelField("Walkie Talkie Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("walkieClips"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("walkiePlaybackMode"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("walkieMinGapSeconds"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("walkieMaxGapSeconds"));
                        break;

                    case InteractionType.Turntable:
                        EditorGUILayout.LabelField("Turntable Audio", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("turntableClips"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("turntablePlaybackMode"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("turntableMinGapSeconds"));
                        EditorGUILayout.PropertyField(profile.FindPropertyRelative("turntableMaxGapSeconds"));
                        break;
                }

                EditorGUILayout.LabelField("Audio Routing", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("mixerGroupOutput"));
            }

            // Remove button at bottom
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                interactionProfiles.DeleteArrayElementAtIndex(i);
                InitializeFoldoutStates();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        if (GUILayout.Button("Add New Profile"))
        {
            interactionProfiles.arraySize++;
            InitializeFoldoutStates();
        }

        serializedObject.ApplyModifiedProperties();
    }
} 