using UnityEngine;
using UnityEditor;
using static EnemyAudioManager;

[CustomEditor(typeof(EnemyAudioManager))]
public class EnemyAudioManagerEditor : Editor
{
    SerializedProperty enemyProfiles;
    SerializedProperty maxHearingDistance;
    private bool[] foldoutStates;
    private bool[] audioSetupFoldouts;

    private void OnEnable()
    {
        enemyProfiles = serializedObject.FindProperty("enemyProfiles");
        maxHearingDistance = serializedObject.FindProperty("maxHearingDistance");
        InitializeFoldoutStates();
    }

    private void InitializeFoldoutStates()
    {
        foldoutStates = new bool[enemyProfiles.arraySize];
        audioSetupFoldouts = new bool[enemyProfiles.arraySize];
        for (int i = 0; i < foldoutStates.Length; i++)
        {
            foldoutStates[i] = true;
            audioSetupFoldouts[i] = true;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        AudioHandoutBanner.Draw();

        // Re-sync foldout arrays with the profile count - Undo, multi-select or
        // external edits can change arraySize between OnEnable and here, which
        // used to throw IndexOutOfRange below.
        if (foldoutStates == null || foldoutStates.Length != enemyProfiles.arraySize)
        {
            InitializeFoldoutStates();
        }

        // Enemy Profiles section
        EditorGUILayout.LabelField("Enemy Sound Profiles", EditorStyles.boldLabel);

        for (int i = 0; i < enemyProfiles.arraySize; i++)
        {
            EditorGUI.indentLevel = 0;
            SerializedProperty profile = enemyProfiles.GetArrayElementAtIndex(i);
            SerializedProperty enemyName = profile.FindPropertyRelative("enemyName");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            string displayName = !string.IsNullOrEmpty(enemyName.stringValue) 
                ? enemyName.stringValue 
                : $"Profile {i + 1}";
            foldoutStates[i] = EditorGUILayout.Foldout(foldoutStates[i], displayName, true);
            EditorGUILayout.EndHorizontal();

            if (foldoutStates[i])
            {
                EditorGUI.indentLevel++;
                
                // Basic Settings
                EditorGUILayout.PropertyField(enemyName);
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("enemyType"));
                
                EditorGUILayout.Space(5);
                
                // Sounds
                EditorGUILayout.LabelField("Sounds", EditorStyles.boldLabel);
                SerializedProperty vocalSettings = profile.FindPropertyRelative("generalVocalisations");
                
                // Draw black background for array elements
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(vocalSettings.FindPropertyRelative("vocalisationSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("attackSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("deathSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                // Headshots had no line here at all, while ZombieAI has been playing this
                // slot on every head hit since the head band was added - so the sound was
                // wired, assessed once authored, and impossible to author. The third time
                // this hand-drawn inspector has hidden a live slot (the grenade's section,
                // then the SMG/Revolver reloads). Found and fixed 2026-09-07.
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("headshotSounds"));

                EditorGUILayout.Space(5);

                // Role sounds. The two bold headings above these fields ("Role" and "Role
                // Sounds") come from the [Header] attributes on EnemySoundProfile, which
                // PropertyField draws for us - do not add LabelFields for them as well or
                // each one renders twice.
                //
                // All six are drawn on EVERY profile, deliberately, even though the Ctrl+G
                // report only asks the profile's own role for its slots
                // (EnemySoundProfile.IsAudioSlotRelevant). Hiding the other five here would
                // mean a clip already dragged in vanishes the moment the Role changes -
                // silently unauthoring somebody's work - and this inspector's history is
                // slots that could not be seen, not slots that could. The HelpBox says
                // which ones count instead.
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("enemyRole"));

                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("lungeSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("blockSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spitWindupSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spitSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spitImpactSounds"));
                EditorGUI.DrawRect(GUILayoutUtility.GetLastRect(), new Color(0, 0, 0, 0.1f));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("screamSounds"));

                EditorGUILayout.HelpBox(
                    "A role sound is that enemy's TELL - the warning the player learns to read, " +
                    "because none of the four roles has an animation of its own.\n\n" +
                    "Runner (Ghoul) - Lunge: the growl before the sprint. It plays while the " +
                    "zombie is still standing still, so the clip's length IS the warning.\n" +
                    "Shield (Butcher) - Block: the clang of a round taken on the front, once per " +
                    "shot the shield eats.\n" +
                    "Spitter (Demon) - Spit Windup: the retch before the spit, then Spit as it " +
                    "launches and Spit Impact where it lands. Spit Impact is the one enemy sound " +
                    "that plays away from the enemy, in 3D at the splash.\n" +
                    "Screamer (Yokai) - Scream: the call that rallies the pack, which speeds up " +
                    "when it ends.\n\n" +
                    "Only the slots belonging to the Role above are played and listed in the " +
                    "Ctrl+G progress report. The others stay visible so that changing the Role " +
                    "never hides a clip you have already dropped in.",
                    MessageType.Info);

                EditorGUILayout.Space(5);

                // Audio Setup foldout grouping several properties together
                audioSetupFoldouts[i] = EditorGUILayout.Foldout(audioSetupFoldouts[i], "Audio Setup", true);
                if (audioSetupFoldouts[i])
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("enemyPrefab"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("minVocalDelay"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("maxVocalDelay"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("vocalisationVolume"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("attackVolume"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("deathVolume"));
                    // The volumes all live together in this foldout, so the two that were
                    // missing join them here rather than beside their own clips.
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("headshotVolume"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("roleVolume"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("vocalisationMixerGroup"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("attackMixerGroup"));
                    EditorGUILayout.PropertyField(profile.FindPropertyRelative("attackSoundDelay"));
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }

            // Remove button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                enemyProfiles.DeleteArrayElementAtIndex(i);
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
            enemyProfiles.arraySize++;
            InitializeFoldoutStates();
        }

        serializedObject.ApplyModifiedProperties();
    }
} 