using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/*
 * InteractionProfileTools.cs
 *
 * Purpose: Housekeeping for the scene InteractionAudioManager's profile array.
 * Menu:    Tools/Audio/Sort Interaction Profiles
 *
 * The profiles were authored in the order the props were built (Firestick, Campfire,
 * Cave Door, ...), and the inspector, the Ctrl+G AUDIO IMPLEMENTATION PROGRESS popup and
 * its export all list them in ARRAY order - so a student hunting for one profile reads
 * the whole list. Sorting the data fixes all three views at once, which an inspector-only
 * sort would not.
 *
 * Safe to sort: nothing reads the array by position. The runtime rebuilds profileLookup
 * from the array in InteractionAudioManager.Awake and resolves every request by name or
 * id (AudioProfileKeys.TryGetProfile); the editors that touch it
 * (InteractionAudioManagerEditor, PortedPropTools, AudioProfileEnumMigration,
 * AudioWiringValidator) walk the whole array and match on interactionName. Order is
 * cosmetic.
 *
 * A menu item rather than a hand edit of the .unity file because the scene on disk is
 * routinely older than what is open in the editor - a hand edit is lost the next time the
 * owner saves.
 *
 * WARNING: binds to the serialized names "interactionProfiles" and "interactionName" by
 * literal string. Renaming either compiles clean and breaks this silently, like the other
 * editors here (see CODE_STRUCTURE's editor-tooling warning).
 */

public static class InteractionProfileTools
{
    private const string ProfilesField = "interactionProfiles";
    private const string NameField = "interactionName";
    private const string UndoLabel = "Sort Interaction Profiles";

    /// <summary>
    /// Reorders the manager's interaction profiles by name, A-Z. Idempotent: a second run
    /// finds nothing to move and says so.
    /// </summary>
    [MenuItem("Tools/Audio/Sort Interaction Profiles")]
    private static void SortInteractionProfiles()
    {
        InteractionAudioManager manager =
            Object.FindFirstObjectByType<InteractionAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Sort interaction profiles",
                "No InteractionAudioManager in the open scene - is the level loaded?", "OK");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogError($"[InteractionProfileTools] InteractionAudioManager has no array field " +
                           $"'{ProfilesField}' - it has been renamed, and this tool binds to it by " +
                           "name.", manager);
            return;
        }

        int count = profiles.arraySize;
        if (count < 2)
        {
            Debug.Log($"[InteractionProfileTools] The manager has {count} interaction profile(s); " +
                      "nothing to sort.", manager);
            return;
        }

        // Keys are read the way the runtime lookup reads them (trimmed, case-insensitive),
        // so names differing only in padding or case cannot swap places on a re-run. A blank
        // name sorts to the top where it is visible - the runtime can never play it.
        string[] keys = new string[count];
        for (int i = 0; i < count; i++)
        {
            SerializedProperty name = profiles.GetArrayElementAtIndex(i).FindPropertyRelative(NameField);
            keys[i] = name != null ? AudioProfileKeys.Normalize(name.stringValue) : string.Empty;
        }

        // Ties break on the current position, so the order is stable and a second run is a
        // no-op even if two profiles somehow share a name.
        List<int> wanted = new List<int>(count);
        for (int i = 0; i < count; i++) wanted.Add(i);
        wanted.Sort((a, b) =>
        {
            int byName = string.Compare(keys[a], keys[b], System.StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.CompareTo(b);
        });

        // Selection sort by moves. `live` tracks where each original element currently sits,
        // because MoveArrayElement shifts everything between the source and the destination.
        List<int> live = new List<int>(count);
        for (int i = 0; i < count; i++) live.Add(i);

        int moves = 0;
        for (int target = 0; target < count; target++)
        {
            int from = live.IndexOf(wanted[target]);
            if (from == target) continue;

            profiles.MoveArrayElement(from, target);

            int element = live[from];
            live.RemoveAt(from);
            live.Insert(target, element);
            moves++;
        }

        string order = string.Join(", ", NamesInOrder(keys, wanted));

        if (moves == 0)
        {
            Debug.Log($"[InteractionProfileTools] The {count} interaction profiles are already in " +
                      $"alphabetical order ({order}); nothing changed.", manager);
            return;
        }

        // Recorded before the values land: a SerializedObject only writes the component when
        // its properties are applied, so this captures the pre-sort order for Ctrl+Z.
        Undo.RecordObject(manager, UndoLabel);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

        Selection.activeObject = manager.gameObject;
        EditorGUIUtility.PingObject(manager.gameObject);

        Debug.Log($"[InteractionProfileTools] Sorted the {count} interaction profiles alphabetically " +
                  $"in {moves} move(s): {order}. Lookups are by name and id, so the order is cosmetic " +
                  "- it is what the inspector, the Ctrl+G progress report and its export read. " +
                  "SAVE THE SCENE.", manager);
    }

    /// <summary>The profile names in their new order, with blanks made visible.</summary>
    private static IEnumerable<string> NamesInOrder(string[] keys, List<int> order)
    {
        foreach (int index in order)
        {
            yield return string.IsNullOrEmpty(keys[index]) ? "(unnamed)" : keys[index];
        }
    }
}
