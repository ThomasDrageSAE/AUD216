using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/*
 * DistributionResetTools.cs
 *
 * Purpose: One command to make the level ready to hand out to a new student cohort -
 *          every authored clip in the audio MANAGERS goes back to the placeholder beep,
 *          the footsteps to their own beep.
 * Menu:    Tools/Distribution/Reset Audio Clips For Handout
 *
 * Rules, as specified by the owner (2026-09-02):
 *   - Every AudioClip field on the manager components becomes "Beep to replace.wav",
 *     so each slot is audible as a placeholder and shows as PLACEHOLDER in Ctrl+G.
 *     Arrays keep their sizes with every element beeped; an empty array stays empty.
 *   - The FootstepAudioManager gets its OWN beep, "Beep to replace - footsteps.wav", in
 *     every footstep / jump / land slot (owner, 2026-09-03 - it used to be emptied to
 *     zero slots; the footsteps are the first thing students replace, so they should be
 *     audible placeholders like everything else, and BEEP rows in Ctrl+G).
 *   - AudioSource components are NEVER touched: only AudioClip fields on the manager
 *     components themselves, never an AudioSource's clip (Resource) slot. AudioSource
 *     references serialize as PPtr<$AudioSource>, so the type filter below excludes
 *     them by construction.
 *
 * Extended same-day (owner request): the reset also covers the FEATURE components that
 * carry authorable clips (rod, mushroom dialogue, dawn chorus, heartbeat, flashlight,
 * cooking, boss, sound props) - in the scene AND inside their prefabs - and empties the
 * MusicStateDefinition stem assets, so the music content is a blank page too.
 *
 * ⚠ MAINTENANCE: the TYPE LISTS and PREFAB PATHS below must be extended when a new
 * audio manager, clip-carrying component, or clip-carrying prefab is added. Fields
 * WITHIN a listed type need nothing - they are discovered by walking the serialized
 * object, so new clip fields and profile entries are picked up automatically. See
 * CODE_STRUCTURE.md (editor tooling).
 *
 * Scene edits are undoable; PREFAB and ASSET edits are NOT (SaveAsPrefabAsset and
 * SaveAssets write immediately) - git is the way back. The scene still has to be
 * SAVED afterwards.
 */
public static class DistributionResetTools
{
    // The placeholders moved into the Demo sounds folder on 2026-09-03 (everything in
    // there is graded as not done by the progress report).
    private const string BeepClipPath = AudioProgressScanner.DemoSoundsFolder + "Beep to replace.wav";
    private const string FootstepsBeepClipPath = AudioProgressScanner.DemoSoundsFolder + "Beep to replace - footsteps.wav";

    /// <summary>Every clip slot on these becomes the beep.</summary>
    private static readonly Type[] BeepManagers =
    {
        typeof(EnemyAudioManager),
        typeof(WeaponAudioManager),
        typeof(PlayerAudioManager),
        typeof(UIAudioManager),
        typeof(InteractionAudioManager),
        typeof(RiverAudioManager),
        typeof(MusicManager),
    };

    /// <summary>Every clip slot on these becomes the FOOTSTEPS beep.</summary>
    private static readonly Type[] FootstepBeepManagers =
    {
        typeof(Audio.FootstepAudioManager),
    };

    /// <summary>Feature components whose clip slots also become the beep - found in the
    /// scene AND inside the feature prefabs below.</summary>
    private static readonly Type[] FeatureComponents =
    {
        typeof(FishingRod),
        typeof(MushroomTripInteraction),
        typeof(PropSoundInteraction),
        typeof(DawnChorus),
        typeof(HeartbeatLayer),
        typeof(FlashlightItem),
        typeof(CampfireCooking),
        typeof(BossZombieController),
    };

    /// <summary>Prefabs that carry the components above. The rod and boss prefabs are
    /// resolved from the scene at run time rather than listed.</summary>
    private static readonly string[] FeaturePrefabPaths =
    {
        "Assets/Mushroom Trip/Trip Mushroom.prefab",
        "Assets/Well/Well.prefab",
        "Assets/Flashlight/Flashlight Pickup.prefab",
    };

    [MenuItem("Tools/Distribution/Reset Audio Clips For Handout")]
    private static void ResetAudioClips()
    {
        if (!EditorUtility.DisplayDialog("Reset audio clips for handout",
                "This replaces EVERY authored clip on the audio managers AND the feature " +
                "components (scene and prefabs) with the placeholder beep (the footsteps " +
                "with their own beep), and empties the music stem assets.\n\n" +
                "AudioSource components are not touched. Scene edits are undoable; prefab " +
                "and asset edits are not (git is the way back). Save the scene afterwards." +
                "\n\nReset now?", "Reset", "Cancel"))
        {
            return;
        }

        AudioClip beep = AssetDatabase.LoadAssetAtPath<AudioClip>(BeepClipPath);
        if (beep == null)
        {
            EditorUtility.DisplayDialog("Reset audio clips",
                $"No placeholder clip at {BeepClipPath}.", "OK");
            return;
        }

        AudioClip footstepsBeep = AssetDatabase.LoadAssetAtPath<AudioClip>(FootstepsBeepClipPath);
        if (footstepsBeep == null)
        {
            EditorUtility.DisplayDialog("Reset audio clips",
                $"No footsteps placeholder clip at {FootstepsBeepClipPath}.", "OK");
            return;
        }

        int managers = 0, clipsBeeped = 0, footstepClips = 0;

        foreach (Type type in BeepManagers)
        {
            foreach (UnityEngine.Object manager in UnityEngine.Object.FindObjectsByType(
                         type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                managers++;
                var (set, _) = ResetManager(manager, beep, emptyMode: false);
                clipsBeeped += set;
            }
        }

        foreach (Type type in FootstepBeepManagers)
        {
            foreach (UnityEngine.Object manager in UnityEngine.Object.FindObjectsByType(
                         type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                managers++;
                var (set, _) = ResetManager(manager, footstepsBeep, emptyMode: false);
                footstepClips += set;
            }
        }

        // Feature components in the open scene.
        int featureClips = 0, featureComponents = 0;
        foreach (Type type in FeatureComponents)
        {
            foreach (UnityEngine.Object component in UnityEngine.Object.FindObjectsByType(
                         type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                featureComponents++;
                var (set, _) = ResetManager(component, beep, emptyMode: false);
                featureClips += set;
            }
        }

        // The same components inside their prefabs - a scene instance without an
        // override inherits from there, so leaving the prefab authored would leave
        // the handout authored.
        int prefabsReset = 0;
        foreach (string path in AllFeaturePrefabPaths())
        {
            var (touched, set) = ResetPrefab(path, beep);
            if (touched) prefabsReset++;
            featureClips += set;
        }

        // Music content: every stem clip in the state assets goes, so the music is a
        // blank page for the cohort (the definitions, bar counts and thresholds stay).
        int stemsCleared = 0, musicAssets = 0;
        foreach (MusicStateDefinition definition in FindMusicDefinitions())
        {
            musicAssets++;
            var (cleared, _) = ResetManager(definition, null, emptyMode: true);
            stemsCleared += cleared;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();

        Debug.Log($"[DistributionResetTools] Reset {managers} manager(s) ({clipsBeeped} slots " +
                  $"beeped, {footstepClips} footstep slots given the footsteps beep), " +
                  $"{featureComponents} scene feature component(s) and " +
                  $"{prefabsReset} prefab(s) ({featureClips} slots beeped), and {musicAssets} " +
                  $"music asset(s) ({stemsCleared} stem clips cleared). AudioSources " +
                  "untouched. Check Ctrl+G reads all-placeholder, then SAVE THE SCENE.");
    }

    /// <summary>All feature prefab paths, including the two resolved from the scene.</summary>
    private static System.Collections.Generic.IEnumerable<string> AllFeaturePrefabPaths()
    {
        foreach (string path in FeaturePrefabPaths) yield return path;

        FishingRodItem rodItem = UnityEngine.Object.FindFirstObjectByType<FishingRodItem>(FindObjectsInactive.Include);
        if (rodItem != null)
        {
            SerializedProperty prefab = new SerializedObject(rodItem).FindProperty("fishingRodPrefab");
            string path = prefab != null ? AssetDatabase.GetAssetPath(prefab.objectReferenceValue) : null;
            if (!string.IsNullOrEmpty(path)) yield return path;
        }

        EnemyAudioManager enemyAudio = UnityEngine.Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (enemyAudio != null)
        {
            SerializedProperty profiles = new SerializedObject(enemyAudio).FindProperty("enemyProfiles");
            for (int i = 0; profiles != null && i < profiles.arraySize; i++)
            {
                SerializedProperty entry = profiles.GetArrayElementAtIndex(i);
                SerializedProperty type = entry.FindPropertyRelative("enemyType");
                SerializedProperty prefab = entry.FindPropertyRelative("enemyPrefab");
                if (type == null || prefab == null) continue;
                if (type.intValue != (int)EnemyAudioManager.EnemyType.Boss) continue;

                string path = AssetDatabase.GetAssetPath(prefab.objectReferenceValue);
                if (!string.IsNullOrEmpty(path)) yield return path;
            }
        }
    }

    /// <summary>Resets the feature components inside one prefab asset.</summary>
    private static (bool touched, int clips) ResetPrefab(string path, AudioClip beep)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return (false, 0);

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int clips = 0;
            foreach (Type type in FeatureComponents)
            {
                foreach (Component component in root.GetComponentsInChildren(type, true))
                {
                    var (set, _) = ResetManager(component, beep, emptyMode: false);
                    clips += set;
                }
            }

            if (clips > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            return (clips > 0, clips);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>The MusicStateDefinition assets the scene's MusicManagers reference.</summary>
    private static System.Collections.Generic.IEnumerable<MusicStateDefinition> FindMusicDefinitions()
    {
        foreach (MusicManager manager in UnityEngine.Object.FindObjectsByType<MusicManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedProperty list = new SerializedObject(manager).FindProperty("stateDefinitions");
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is MusicStateDefinition definition)
                {
                    yield return definition;
                }
            }
        }
    }

    /// <summary>
    /// Walks every serialized property of one manager. AudioClip fields are recognised by
    /// their serialized type string (PPtr&lt;$AudioClip&gt;), so new fields and profile
    /// entries need no registration here - only new manager TYPES do.
    /// </summary>
    private static (int clips, int arrays) ResetManager(UnityEngine.Object manager, AudioClip beep,
                                                        bool emptyMode)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.GetIterator();

        int clips = 0, arrays = 0;
        bool enterChildren = true;

        while (property.Next(enterChildren))
        {
            enterChildren = true;

            if (property.isArray &&
                property.propertyType != SerializedPropertyType.String &&
                property.arrayElementType == "PPtr<$AudioClip>")
            {
                if (emptyMode)
                {
                    if (property.arraySize > 0)
                    {
                        property.arraySize = 0;
                        arrays++;
                    }
                }
                else
                {
                    for (int i = 0; i < property.arraySize; i++)
                    {
                        property.GetArrayElementAtIndex(i).objectReferenceValue = beep;
                        clips++;
                    }
                }

                // The elements were just handled wholesale; do not also walk into them.
                enterChildren = false;
            }
            else if (property.propertyType == SerializedPropertyType.ObjectReference &&
                     property.type == "PPtr<$AudioClip>")
            {
                property.objectReferenceValue = emptyMode ? null : beep;
                clips++;
            }
        }

        // ApplyModifiedProperties (not ...WithoutUndo): a cohort reset you can Ctrl+Z.
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        return (clips, arrays);
    }
}
