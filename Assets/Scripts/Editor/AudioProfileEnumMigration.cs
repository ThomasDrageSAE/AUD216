using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/*
 * AudioProfileEnumMigration.cs (Editor)
 *
 * Purpose: Fills in the id field on every audio profile by matching its authored name
 *          to an enum member, and reports every name it could not match.
 * Menu:    Tools/Audio/Migrate Profile Keys To Enums
 *
 * This is deliberately one-way and conservative. It writes ids that are currently
 * Unassigned, it never edits or clears a name string, and it never overwrites an id that
 * someone has already set by hand. Anything it cannot match is logged with the vocabulary
 * it was matching against, so the fix is either a rename in the inspector or a new member
 * in AudioProfileIds - a decision left to a person.
 *
 * Run it, read the console summary, then play-verify each sound domain before anyone
 * starts converting callers from strings to ids.
 */
public static class AudioProfileEnumMigration
{
    private const string MenuPath = "Tools/Audio/Migrate Profile Keys To Enums";

    [MenuItem(MenuPath)]
    private static void Migrate()
    {
        var report = new StringBuilder();
        int assigned = 0;
        int unmapped = 0;
        int alreadySet = 0;

        foreach (var manager in UnityEngine.Object.FindObjectsByType<EnemyAudioManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            MigrateManager(manager, "enemyProfiles", "enemyName", "enemyId",
                typeof(EnemyId), report, ref assigned, ref unmapped, ref alreadySet);
        }

        foreach (var manager in UnityEngine.Object.FindObjectsByType<WeaponAudioManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            MigrateManager(manager, "m_WeaponProfiles", "weaponName", "weaponId",
                typeof(WeaponId), report, ref assigned, ref unmapped, ref alreadySet);
        }

        foreach (var manager in UnityEngine.Object.FindObjectsByType<InteractionAudioManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            MigrateManager(manager, "interactionProfiles", "interactionName", "interactionId",
                typeof(InteractionId), report, ref assigned, ref unmapped, ref alreadySet);
        }

        foreach (var manager in UnityEngine.Object.FindObjectsByType<Audio.FootstepAudioManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            MigrateManager(manager, "surfaceProfiles", "surfaceType", "surfaceId",
                typeof(SurfaceId), report, ref assigned, ref unmapped, ref alreadySet);
        }

        report.Insert(0, $"Audio profile id migration: {assigned} assigned, " +
                         $"{alreadySet} already set, {unmapped} unmapped.\n");

        if (unmapped > 0)
        {
            report.AppendLine();
            report.AppendLine("Unmapped names were left exactly as they are. Either rename the " +
                              "profile to match a member, or add a member to AudioProfileIds.cs.");
            Debug.LogWarning(report.ToString());
        }
        else
        {
            Debug.Log(report.ToString());
        }

        if (assigned > 0)
        {
            Debug.Log("Scene marked dirty - save it, then play-verify every sound domain " +
                      "(enemies, weapons, interactions, footsteps) before converting any callers.");
        }
    }

    private static void MigrateManager(Component manager, string arrayField, string nameField,
                                       string idField, Type idType, StringBuilder report,
                                       ref int assigned, ref int unmapped, ref int alreadySet)
    {
        var serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(arrayField);

        if (profiles == null || !profiles.isArray)
        {
            report.AppendLine($"  {manager.GetType().Name}: no '{arrayField}' array found - skipped.");
            return;
        }

        report.AppendLine($"  {manager.GetType().Name} on '{manager.name}' ({profiles.arraySize} profiles):");

        // Two authored names that canonicalise identically ("Firestick" / "fire stick")
        // would both map to one member; only the first can own the id's alias at runtime.
        var idsInThisManager = new HashSet<int>();

        int assignedHere = 0;

        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            SerializedProperty name = profile.FindPropertyRelative(nameField);
            SerializedProperty id = profile.FindPropertyRelative(idField);

            if (name == null || id == null)
            {
                report.AppendLine($"    [{i}] missing '{nameField}' or '{idField}' - skipped.");
                continue;
            }

            if (id.enumValueIndex != 0)
            {
                alreadySet++;
                idsInThisManager.Add(id.enumValueIndex);
                continue;
            }

            if (TryMatch(idType, name.stringValue, out int matchedIndex, out string matchedName))
            {
                if (!idsInThisManager.Add(matchedIndex))
                {
                    unmapped++;
                    report.AppendLine($"    '{name.stringValue}' -> {idType.Name}.{matchedName} is " +
                                      "ALREADY TAKEN by another profile on this manager - left Unassigned. " +
                                      "Two names that reduce to the same id need a rename first.");
                    continue;
                }

                id.enumValueIndex = matchedIndex;
                assigned++;
                assignedHere++;
                report.AppendLine($"    '{name.stringValue}' -> {idType.Name}.{matchedName}");
            }
            else
            {
                unmapped++;
                report.AppendLine($"    '{name.stringValue}' -> no match in {idType.Name} " +
                                  $"(accepts: {string.Join(", ", AssignedNames(idType))})");
            }
        }

        serialized.ApplyModifiedProperties();

        // Dirty the scene the manager actually lives in - the active scene is not
        // necessarily it in a multi-scene setup.
        if (assignedHere > 0 && manager.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }

    /// <summary>
    /// Matches an authored name to an enum member by canonical form, so spacing and
    /// capitalisation do not stand in the way ("Rock Golem" matches RockGolem).
    /// </summary>
    private static bool TryMatch(Type idType, string authored, out int enumValueIndex, out string matchedName)
    {
        enumValueIndex = 0;
        matchedName = null;

        string canonical = AudioProfileIds.Canonical(authored);
        if (canonical.Length == 0) return false;

        string[] names = Enum.GetNames(idType);
        for (int i = 1; i < names.Length; i++) // index 0 is Unassigned
        {
            if (AudioProfileIds.Canonical(names[i]) != canonical) continue;

            enumValueIndex = i;
            matchedName = names[i];
            return true;
        }

        return false;
    }

    private static IEnumerable<string> AssignedNames(Type idType)
    {
        string[] names = Enum.GetNames(idType);
        for (int i = 1; i < names.Length; i++) yield return names[i];
    }
}
