using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WeaponAudioManager))]
public class WeaponAudioManagerEditor : Editor
{
    private SerializedProperty weaponProfiles;
    private SerializedProperty weaponAudioSource;
    private SerializedProperty audioMixerGroup;
    
    // Add foldout states array
    private bool[] profileFoldouts;

    private void OnEnable()
    {
        weaponProfiles = serializedObject.FindProperty("m_WeaponProfiles");
        weaponAudioSource = serializedObject.FindProperty("m_WeaponAudioSource");
        audioMixerGroup = serializedObject.FindProperty("m_AudioMixerGroup");
        
        // Initialize foldout states
        InitializeFoldoutStates();
    }

    private void InitializeFoldoutStates()
    {
        if (profileFoldouts == null || profileFoldouts.Length != weaponProfiles.arraySize)
        {
            profileFoldouts = new bool[weaponProfiles.arraySize];
            for (int i = 0; i < profileFoldouts.Length; i++)
            {
                profileFoldouts[i] = true; // Default to expanded
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

        EditorGUILayout.LabelField("Weapon Profiles", EditorStyles.boldLabel);

        // Draw each profile
        for (int i = 0; i < weaponProfiles.arraySize; i++)
        {
            SerializedProperty profile = weaponProfiles.GetArrayElementAtIndex(i);
            SerializedProperty weaponName = profile.FindPropertyRelative("weaponName");
            SerializedProperty weaponType = profile.FindPropertyRelative("weaponType");
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Header with indented foldout
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            string label = string.IsNullOrEmpty(weaponName.stringValue) ? $"Weapon {i}" : weaponName.stringValue;
            profileFoldouts[i] = EditorGUILayout.Foldout(profileFoldouts[i], label, true);
            EditorGUILayout.EndHorizontal();

            if (profileFoldouts[i])
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(weaponName);
                EditorGUILayout.PropertyField(weaponType);
                
                var type = (WeaponType)weaponType.enumValueIndex;
                DrawWeaponTypeProperties(profile, type);
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        if (GUILayout.Button("Add New Weapon Profile"))
        {
            weaponProfiles.arraySize++;
            InitializeFoldoutStates();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Audio Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(weaponAudioSource, new GUIContent("Weapon Audiosource"));
        EditorGUILayout.PropertyField(audioMixerGroup, new GUIContent("Mixer Group Output"));

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawWeaponTypeProperties(SerializedProperty profile, WeaponType type)
    {
        // Draw properties with type-specific labels
        switch (type)
        {
            case WeaponType.Gun:
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootSounds"), new GUIContent("Shoot Sounds"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootVolume"), new GUIContent("Shoot Volume"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("minPitchVariation"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("maxPitchVariation"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("raiseWeaponSound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("lowerWeaponSound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("toggleSoundVolume"), new GUIContent("Raise/Lower Volume"));
                break;

            case WeaponType.Axe:
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootSounds"), new GUIContent("Axe Swing Sounds"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootVolume"), new GUIContent("Axe Volume"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("minPitchVariation"), new GUIContent("Min Swing Pitch"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("maxPitchVariation"), new GUIContent("Max Swing Pitch"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("raiseWeaponSound"), new GUIContent("Draw Axe Sound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("lowerWeaponSound"), new GUIContent("Sheathe Axe Sound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("toggleSoundVolume"), new GUIContent("Draw/Sheathe Volume"));
                break;

            case WeaponType.Grenade:
                // Throw and Explosion, and nothing else. A grenade is not fired, so there
                // is no Shoot Sounds; it is not reloaded; and although it IS raised and
                // lowered in hand, the owner chose two assessed slots for it, so the two
                // toggle clips are left off this inspector as well as out of the Ctrl+G
                // report. Adding them back means one line here and two in
                // WeaponSoundProfile.IsAudioSlotRelevant - do both or the report and the
                // inspector start disagreeing about what a student is asked to fill in.
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("throwSounds"), new GUIContent("Throw Sounds"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("throwVolume"), new GUIContent("Throw Volume"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("minPitchVariation"), new GUIContent("Min Pitch"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("maxPitchVariation"), new GUIContent("Max Pitch"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("explosionSounds"), new GUIContent("Explosion Sounds"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("explosionVolume"), new GUIContent("Explosion Volume"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("explosionMaxDistance"), new GUIContent("Explosion Max Distance"));
                EditorGUILayout.HelpBox(
                    "The explosion plays in 3D at the grenade, not at the camera - it is the only weapon " +
                    "sound in the level that does. Max Distance is where it fades to silence; the throw " +
                    "and the pitch range above behave like any other weapon sound.",
                    MessageType.Info);
                break;

            case WeaponType.Minigun:
                // A gun whose shots are a LOOP. Raise / lower and the reload block at the
                // bottom of this method exactly as Gun has them; no Shoot Sounds, because
                // nothing plays per round since 2026-09-06; and the five clips this type
                // exists for. This inspector is hand-drawn, so a WeaponType with no
                // case here renders an EMPTY profile with nothing to drag a clip into;
                // that is what happened to the grenade on 2026-09-05. Keep this list and
                // WeaponSoundProfile.IsAudioSlotRelevant in step: what the inspector
                // offers and what the Ctrl+G report asks for are meant to be one list.
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("raiseWeaponSound"), new GUIContent("Raise Minigun Sound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("lowerWeaponSound"), new GUIContent("Lower Minigun Sound"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("toggleSoundVolume"), new GUIContent("Raise/Lower Volume"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spinWindupSound"), new GUIContent("Spin Windup"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spinWinddownSound"), new GUIContent("Spin Winddown"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spinWinddownTail"), new GUIContent("   Tap Plays Its"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spinLoopSound"), new GUIContent("Spin Loop"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("spinVolume"), new GUIContent("Spin Volume"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootingStartupSound"), new GUIContent("Shooting Startup"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootingLoopSound"), new GUIContent("Shooting Loop"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootingShutdownSound"), new GUIContent("Shooting Shutdown"));
                EditorGUILayout.PropertyField(profile.FindPropertyRelative("shootVolume"), new GUIContent("Shooting Volume"));
                EditorGUILayout.HelpBox(
                    "Six clips. Spin Windup plays from the moment the trigger " +
                    "goes down, and its LENGTH is the wind-up: the first round leaves exactly " +
                    "when it ends. Then Spin Loop and Shooting Startup begin together, and " +
                    "Shooting Loop takes over at the exact sample the Startup finishes. When " +
                    "the trigger comes up both loops stop dead and Shooting Shutdown plays.\n\n" +
                    "Letting go during the wind-up - a tap - cuts to Spin Winddown for as long " +
                    "as the wind-up ran (0.2 s of wind-up, then 0.2 s of wind-down), and the " +
                    "barrels stop when it ends; no shutdown, because nothing fired. 'Tap Plays " +
                    "Its' picks the START of the clip (faded out at the cut) or its END - the " +
                    "end of a wind-down that decays to silence is silence, so Start is the " +
                    "default. Pressing again resumes the wind-up from where the barrels are.\n\n" +
                    "Spin Loop and Shooting Loop must be SEAMLESS loops: a fade or a stray " +
                    "transient at either end is heard as a tick every time the clip wraps. " +
                    "Shooting Startup must END where Shooting Loop BEGINS, or the handover clicks.",
                    MessageType.Info);
                break;
        }
        
        // The reload clips, for the types that reload.
        //
        // These were commented out as "for future implementation" - but the Ctrl+G progress
        // report has been asking for them the whole time, because IsAudioSlotRelevant only
        // hides them from the Axe and the Grenade. So the SMG and the Revolver each listed
        // three reload sounds a student was marked on and had nowhere to drop a clip into.
        // That is why all six read [EMPTY] with real clips authored either side of them.
        //
        // The same shape of bug as the grenade's empty section: this inspector is drawn by
        // hand, so a slot the report asks for has to be added here as well or it cannot be
        // filled. Adding an assessed sound to this project touches three places - the
        // profile field, this inspector, and IsAudioSlotRelevant.
        if (type == WeaponType.Gun || type == WeaponType.Minigun)
        {
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(profile.FindPropertyRelative("reloadStartSound"), new GUIContent("Reload Start Sound"));
            EditorGUILayout.PropertyField(profile.FindPropertyRelative("reloadEndSound"), new GUIContent("Reload End Sound"));
            EditorGUILayout.PropertyField(profile.FindPropertyRelative("reloadActionSounds"), new GUIContent("Reload Action Sounds"));
            EditorGUILayout.PropertyField(profile.FindPropertyRelative("reloadVolume"), new GUIContent("Reload Volume"));
        }
    }
} 