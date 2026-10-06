using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PlayerAudioManager))]
public class PlayerAudioManagerEditor : Editor
{
    private SerializedProperty soundProfiles;
    private SerializedProperty playerStats;
    private SerializedProperty defaultAudioSource;
    private bool[] foldoutStates;

    private void OnEnable()
    {
        soundProfiles = serializedObject.FindProperty("soundProfiles");
        playerStats = serializedObject.FindProperty("playerStats");
        defaultAudioSource = serializedObject.FindProperty("defaultAudioSource");
        InitializeFoldoutStates();
    }

    private void InitializeFoldoutStates()
    {
        if (foldoutStates == null || foldoutStates.Length != soundProfiles.arraySize)
        {
            foldoutStates = new bool[soundProfiles.arraySize];
            for (int i = 0; i < foldoutStates.Length; i++)
            {
                foldoutStates[i] = true;
            }
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        AudioHandoutBanner.Draw();

        // Re-sync with the profile count - Undo/multi-select/external edits can
        // change arraySize between OnEnable and here (IndexOutOfRange otherwise).
        InitializeFoldoutStates();

        EditorGUILayout.PropertyField(playerStats);
        EditorGUILayout.PropertyField(defaultAudioSource);
        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Player Sound Profiles", EditorStyles.boldLabel);

        for (int i = 0; i < soundProfiles.arraySize; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            SerializedProperty profile = soundProfiles.GetArrayElementAtIndex(i);
            SerializedProperty profileName = profile.FindPropertyRelative("profileName");
            SerializedProperty soundType = profile.FindPropertyRelative("soundType");
            
            // Header with foldout
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            string displayName = !string.IsNullOrEmpty(profileName.stringValue) 
                ? profileName.stringValue 
                : $"Profile {i + 1}";
            foldoutStates[i] = EditorGUILayout.Foldout(foldoutStates[i], displayName, true);
            EditorGUILayout.EndHorizontal();

            if (foldoutStates[i])
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(profileName);
                EditorGUILayout.PropertyField(soundType);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Audio Setup", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("customAudioSource"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("mixerGroupOutput"));

                EditorGUILayout.Space(5);
                
                var type = (PlayerAudioManager.SoundType)soundType.enumValueIndex;
                DrawSoundTypeProperties(profile, type);
                
                EditorGUI.indentLevel--;
            }

            // Remove button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                soundProfiles.DeleteArrayElementAtIndex(i);
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
            soundProfiles.arraySize++;
            InitializeFoldoutStates();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSoundTypeProperties(SerializedProperty profile, PlayerAudioManager.SoundType type)
    {
        // The clip fields drawn per type are mirrored in
        // PlayerAudioManager.PlayerSoundProfile.ClipFieldsFor, which the Ctrl+G progress
        // report uses to hide the slots a type never plays. Change both together.
        switch (type)
        {
            case PlayerAudioManager.SoundType.Health:
                EditorGUILayout.LabelField("Health Sounds", EditorStyles.boldLabel);
                
                EditorGUILayout.LabelField("Damage", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("damageSounds"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("damageVolume"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Heartbeat", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("lowHealthLoop"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("heartbeatAudioSource"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("playBelowThisHealth"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("lowHealthVolume"));
                break;

            case PlayerAudioManager.SoundType.Stamina:
                EditorGUILayout.LabelField("Stamina Sounds", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("staminaDepletedSound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("staminaRegeneratingSound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("staminaVolume"));
                break;

            case PlayerAudioManager.SoundType.Hunger:
                EditorGUILayout.LabelField("Hunger Sounds", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("hungerSounds"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("hungerVolume"));
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("eatingSound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("eatingVolume"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Hunger Loop", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("hungerLoop"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("hungerLoopAudioSource"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("playBelowThisHungerFraction"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("hungerLoopVolume"));
                break;
        }
    }
} 