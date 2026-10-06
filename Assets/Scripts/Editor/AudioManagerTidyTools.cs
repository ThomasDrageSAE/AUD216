using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * AudioManagerTidyTools.cs
 *
 * Purpose: The two scene edits behind the audio-manager tidy (Task 4 of
 *          Documentation/AUDIO_MANAGER_TIDY_TASKS.md) - retiring the legacy
 *          MusicController, and moving the Pickups Audio Manager in with the rest of the
 *          managers.
 * Menu:    Tools/Audio/Remove Legacy Music Controller
 *          Tools/Audio/Remove Pickups Audio System
 *
 * Menu items rather than hand edits of the .unity, for the standing reason: the scene on
 * disk is routinely older than what is open in the editor, so a hand edit would be lost the
 * next time the owner saves. Both items are Undo-recorded, log what they did, and are safe
 * to re-run and to run in either order.
 *
 * The pickups item replaced a "Move Pickups Audio Manager" tidy on 2026-09-03: the owner
 * deleted the skulls and the chest, so the manager it moved has nothing left to play.
 *
 * ⚠ MusicController and PickupsAudioManager are found BY NAME through reflection rather
 * than by referencing the types. That is deliberate: removing the component from the scene is phase (a), and
 * deleting MusicController.cs is phase (b) - after the owner has saved, because a class
 * deleted while a saved scene still references it leaves a "missing script" component. A
 * direct type reference here would stop this file compiling the moment phase (b) ran. Once
 * the class is gone the menu item simply reports that there is nothing left to do, and the
 * file keeps working for the second tool.
 *
 * ⚠ Binds to serialized fields by literal name ("audioSource1" / "audioSource2" on
 * MusicController; "interactionProfiles", "interactionName", "leftSpeakerSource" and
 * "rightSpeakerSource" on InteractionAudioManager). Renaming one compiles clean and breaks
 * this silently, like the other editor tools here - so every lookup below degrades to a
 * warning rather than an exception. Nothing on the interaction manager is written: it is
 * read only, purely so the log can say whether the speakers the legacy controller drove are
 * the same ones the Mixing Desk profile still plays through.
 */
public static class AudioManagerTidyTools
{
    // ---- Remove the legacy Music Controller (Task 4.1, phase a) ---------------

    /// <summary>Looked up by name; see the reflection note in the file header.</summary>
    private const string LegacyMusicTypeName = "MusicController";

    /// <summary>Looked up by name too, for the same phase-(a)/phase-(b) reason.</summary>
    private const string PickupsManagerTypeName = "PickupsAudioManager";
    private const string RemovePickupsUndoLabel = "Remove Pickups Audio System";

    private const string LegacyFirstSourceField = "audioSource1";     // = Speaker Right
    private const string LegacySecondSourceField = "audioSource2";    // = Speaker Left

    private const string ProfilesField = "interactionProfiles";
    private const string ProfileNameField = "interactionName";
    private const string LeftSpeakerField = "leftSpeakerSource";
    private const string RightSpeakerField = "rightSpeakerSource";

    /// <summary>
    /// Removes the legacy MusicController component (and its host object, if that object
    /// exists only to carry it). The two AudioSources it pointed at are the mixing-desk
    /// speakers and are NOT touched - they are played by the InteractionAudioManager's
    /// Mixing Desk profile now, which the log confirms before anything is destroyed.
    /// Phase (b) - deleting the class - happens only after the owner has saved the scene.
    /// </summary>
    [MenuItem("Tools/Audio/Remove Legacy Music Controller")]
    private static void RemoveLegacyMusicController()
    {
        Type legacyType = FindComponentTypeByName(LegacyMusicTypeName);
        if (legacyType == null)
        {
            Debug.Log($"[AudioManagerTidyTools] There is no {LegacyMusicTypeName} class in this " +
                      "project any more, so there is no component to remove - nothing to do.");
            return;
        }

        UnityEngine.Object[] found = UnityEngine.Object.FindObjectsByType(
            legacyType, FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (found.Length == 0)
        {
            Debug.Log($"[AudioManagerTidyTools] No {LegacyMusicTypeName} in the open scene " +
                      "(inactive objects included) - nothing to do. Speaker playback runs through " +
                      "the InteractionAudioManager's Mixing Desk profile.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        const string undoLabel = "Remove Legacy Music Controller";

        List<string> removedComponents = new List<string>();
        List<string> removedObjects = new List<string>();
        List<string> keptObjects = new List<string>();

        foreach (UnityEngine.Object candidate in found)
        {
            Component controller = candidate as Component;
            if (controller == null) continue;

            GameObject host = controller.gameObject;
            Scene scene = host.scene;                       // captured before anything is destroyed
            string hostPath = HierarchyPath(host.transform);

            ReportSpeakerSources(controller, hostPath);

            if (PrefabUtility.IsPartOfPrefabInstance(controller))
            {
                Debug.LogWarning($"[AudioManagerTidyTools] '{hostPath}' is part of a prefab " +
                                 "instance, so this tool will not delete the component from it - " +
                                 "remove it on the prefab asset instead. Nothing was changed for " +
                                 "that object.", controller);
                continue;
            }

            Undo.DestroyObjectImmediate(controller);
            removedComponents.Add($"'{hostPath}'");

            // The host exists only to carry the controller: after the component is gone it has
            // nothing but its Transform and no children, so leaving it would leave an empty
            // object in the hierarchy the owner would have to delete by hand.
            bool hostIsSpare = host.transform.childCount == 0 &&
                               host.GetComponents<Component>().Length <= 1;

            if (!hostIsSpare)
            {
                keptObjects.Add($"'{hostPath}' (it still has other components or children)");
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(host))
            {
                keptObjects.Add($"'{hostPath}' (a prefab instance - delete it on the prefab asset)");
            }
            else
            {
                Undo.DestroyObjectImmediate(host);
                removedObjects.Add($"'{hostPath}'");
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        if (removedComponents.Count == 0)
        {
            Debug.Log($"[AudioManagerTidyTools] Found {found.Length} {LegacyMusicTypeName}(s) but " +
                      "removed none - see the warnings above. Nothing changed.");
            return;
        }

        Undo.SetCurrentGroupName(undoLabel);
        Undo.CollapseUndoOperations(undoGroup);

        string report = $"Removed the legacy {LegacyMusicTypeName} component from " +
                        $"{string.Join(", ", removedComponents)}";

        report += removedObjects.Count > 0
            ? $", and deleted {string.Join(", ", removedObjects)} - the object carried nothing but " +
              "that component."
            : ".";

        if (keptObjects.Count > 0)
        {
            report += $" Kept {string.Join(", ", keptObjects)}.";
        }

        Debug.Log($"[AudioManagerTidyTools] {report} The mixing-desk speaker AudioSources were NOT " +
                  "touched. SAVE THE SCENE - the class itself (Assets/Scripts/Audio/" +
                  "MusicController.cs and its .meta) is only deleted after that save, or the saved " +
                  "scene is left with a missing script.");
    }

    /// <summary>
    /// Says in the log which AudioSources the controller drove, that they are untouched, and
    /// whether the InteractionAudioManager's Mixing Desk profile plays through those same two
    /// sources - i.e. whether the speakers still have an owner once this component is gone.
    /// Read only: nothing on either manager is written.
    /// </summary>
    private static void ReportSpeakerSources(Component controller, string hostPath)
    {
        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty first = serialized.FindProperty(LegacyFirstSourceField);
        SerializedProperty second = serialized.FindProperty(LegacySecondSourceField);

        if (first == null || second == null)
        {
            Debug.LogWarning($"[AudioManagerTidyTools] '{hostPath}' has no " +
                             $"'{LegacyFirstSourceField}' / '{LegacySecondSourceField}' field - " +
                             "the legacy class has been renamed, so the sources it drove cannot be " +
                             "listed here. The component is still removed, and no AudioSource is " +
                             "touched; check the mixing-desk speakers by hand.", controller);
            return;
        }

        AudioSource firstSource = first.objectReferenceValue as AudioSource;
        AudioSource secondSource = second.objectReferenceValue as AudioSource;

        Debug.Log($"[AudioManagerTidyTools] '{hostPath}' pointed at " +
                  $"{LegacyFirstSourceField} = {Describe(firstSource)} and " +
                  $"{LegacySecondSourceField} = {Describe(secondSource)} " +
                  "(the mixing-desk speakers). Those AudioSources are NOT touched by this tool - " +
                  "only the component that used to start them is removed.", controller);

        ReportMixingDeskUse(firstSource, secondSource);
    }

    /// <summary>
    /// Which interaction profiles, if any, still play through those two AudioSources. A field
    /// that will not resolve by name is a warning, never an exception - the removal is safe
    /// either way, this is only the evidence line in the log.
    /// </summary>
    private static void ReportMixingDeskUse(AudioSource firstSource, AudioSource secondSource)
    {
        if (firstSource == null && secondSource == null)
        {
            Debug.Log("[AudioManagerTidyTools] The legacy controller had no AudioSources assigned, " +
                      "so there is nothing to cross-check against the Mixing Desk profile.");
            return;
        }

        InteractionAudioManager manager = UnityEngine.Object.FindFirstObjectByType<InteractionAudioManager>(
            FindObjectsInactive.Include);

        if (manager == null)
        {
            Debug.LogWarning("[AudioManagerTidyTools] No InteractionAudioManager in the open scene, " +
                             "so this tool cannot confirm that the Mixing Desk profile still drives " +
                             "those speakers. Their AudioSources are untouched either way.");
            return;
        }

        SerializedProperty profiles = new SerializedObject(manager).FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogWarning($"[AudioManagerTidyTools] InteractionAudioManager has no array field " +
                             $"'{ProfilesField}' - it has been renamed, and this tool binds to it by " +
                             "name. Skipping the Mixing Desk cross-check; nothing is changed on the " +
                             "interaction manager either way.", manager);
            return;
        }

        bool fieldsMissing = false;
        List<string> users = new List<string>();

        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            SerializedProperty left = profile.FindPropertyRelative(LeftSpeakerField);
            SerializedProperty right = profile.FindPropertyRelative(RightSpeakerField);

            if (left == null || right == null)
            {
                fieldsMissing = true;
                continue;
            }

            bool usesLeft = Matches(left.objectReferenceValue, firstSource, secondSource);
            bool usesRight = Matches(right.objectReferenceValue, firstSource, secondSource);
            if (!usesLeft && !usesRight) continue;

            SerializedProperty name = profile.FindPropertyRelative(ProfileNameField);
            string profileName = name != null && !string.IsNullOrEmpty(name.stringValue)
                ? name.stringValue.Trim()
                : $"profile [{i}]";

            string fields = usesLeft && usesRight
                ? $"{LeftSpeakerField} + {RightSpeakerField}"
                : usesLeft ? LeftSpeakerField : RightSpeakerField;

            users.Add($"'{profileName}' ({fields})");
        }

        if (fieldsMissing)
        {
            Debug.LogWarning($"[AudioManagerTidyTools] Some interaction profiles have no " +
                             $"'{LeftSpeakerField}' / '{RightSpeakerField}' field - those fields " +
                             "have been renamed, so the Mixing Desk cross-check below may be " +
                             "incomplete. Nothing is changed on the interaction manager.", manager);
        }

        if (users.Count > 0)
        {
            Debug.Log("[AudioManagerTidyTools] Those same AudioSources are the speakers of " +
                      $"InteractionAudioManager profile(s) {string.Join(", ", users)}, so speaker " +
                      "playback carries on through the Mixing Desk profile after the legacy " +
                      "controller is gone. The interaction manager is read only here - nothing on " +
                      "it was changed.", manager);
        }
        else
        {
            Debug.LogWarning("[AudioManagerTidyTools] None of the interaction profiles reference " +
                             "those AudioSources, so removing the legacy controller leaves them " +
                             "with nothing driving them. The sources themselves stay in the scene; " +
                             "check the Mixing Desk profile's speaker slots before saving.", manager);
        }
    }

    private static bool Matches(UnityEngine.Object candidate, AudioSource first, AudioSource second)
    {
        if (candidate == null) return false;
        return candidate == first || candidate == second;
    }

    // ---- Move the Pickups Audio Manager (Task 4.2) ---------------------------

    private const string ManagersRootName = "Audio Managers";

    /// <summary>
    /// Removes the Pickups Audio Manager - the world skull/chest pickups' sound manager -
    /// from the scene, along with its GameObject when that carried nothing else. The owner
    /// deleted the skulls and the chest on 2026-09-03, so the manager has nothing left to
    /// play: its five slots asked students for sounds the game could no longer make.
    ///
    /// Phase (a) of the removal, exactly like the legacy MusicController above: the class
    /// files under Assets/Scripts/Pickups/ are deleted only after the owner has saved this
    /// scene, or the saved scene keeps a missing script. The type is found BY NAME through
    /// reflection so that later deletion cannot stop this file compiling.
    /// </summary>
    [MenuItem("Tools/Audio/Remove Pickups Audio System")]
    private static void RemovePickupsAudioSystem()
    {
        Type managerType = FindComponentTypeByName(PickupsManagerTypeName);

        if (managerType == null)
        {
            Debug.Log($"[AudioManagerTidyTools] There is no {PickupsManagerTypeName} class in this " +
                      "project any more, so there is no component to remove - nothing to do.");
            return;
        }

        UnityEngine.Object[] managers = UnityEngine.Object.FindObjectsByType(
            managerType, FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (managers.Length == 0)
        {
            Debug.Log($"[AudioManagerTidyTools] No {PickupsManagerTypeName} in the open scene " +
                      "(inactive objects included) - nothing to do. The skull and chest pickups it " +
                      "served have already been removed.");
            return;
        }

        int removedComponents = 0;
        var removedHosts = new List<string>();
        var keptHosts = new List<string>();
        Scene scene = default;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(RemovePickupsUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        foreach (UnityEngine.Object found in managers)
        {
            var manager = found as Component;
            if (manager == null) continue;

            GameObject host = manager.gameObject;
            string hostPath = HierarchyPath(host.transform);
            scene = host.scene;

            if (PrefabUtility.IsPartOfPrefabInstance(host))
            {
                Debug.LogWarning($"[AudioManagerTidyTools] '{hostPath}' is part of a prefab instance, " +
                                 "so this tool will not delete the component from it - remove it on the " +
                                 "prefab asset instead. Nothing was changed for that object.", host);
                continue;
            }

            // What else is on the object decides whether the object goes with the component.
            // An AudioSource that exists only to play the pickup sounds is part of the feature.
            var survivors = new List<string>();
            foreach (Component component in host.GetComponents<Component>())
            {
                if (component == null || component == manager) continue;
                if (component is Transform) continue;
                if (component is AudioSource) continue;      // the manager's own output
                survivors.Add(component.GetType().Name);
            }

            Undo.DestroyObjectImmediate(manager);
            removedComponents++;

            if (survivors.Count == 0 && host.transform.childCount == 0)
            {
                Undo.DestroyObjectImmediate(host);
                removedHosts.Add(hostPath);
            }
            else
            {
                keptHosts.Add($"'{hostPath}' (still carries {string.Join(", ", survivors)}" +
                              (host.transform.childCount > 0 ? $" and {host.transform.childCount} child(ren)" : "") + ")");
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (removedComponents == 0)
        {
            Debug.LogWarning("[AudioManagerTidyTools] Found the Pickups Audio Manager but removed " +
                             "nothing - see the warnings above. Nothing changed.");
            return;
        }

        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        string hostNote = removedHosts.Count > 0
            ? $" and deleted {string.Join(", ", removedHosts)} - the object carried nothing but that " +
              "component and its AudioSource."
            : keptHosts.Count > 0 ? $" Kept {string.Join("; ", keptHosts)}." : string.Empty;

        Debug.Log($"[AudioManagerTidyTools] Removed {removedComponents} Pickups Audio Manager " +
                  $"component(s){hostNote} Its five clip slots leave the Ctrl+G report with the " +
                  "skulls and chest they belonged to. SAVE THE SCENE - the classes under " +
                  "Assets/Scripts/Pickups/ are deleted only after that save, or the saved scene is " +
                  "left with missing scripts.");
    }

    // ---- Shared helpers ------------------------------------------------------

    /// <summary>
    /// The component type with this name, from whichever loaded assembly has it, or null when
    /// the class no longer exists. See the reflection note in the file header for why the
    /// legacy controller is found this way instead of by a direct type reference.
    /// </summary>
    private static Type FindComponentTypeByName(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType(typeName, false);
            }
            catch (Exception)
            {
                continue;               // dynamic or unloadable assembly: not where our scripts are
            }

            if (type != null && typeof(Component).IsAssignableFrom(type)) return type;
        }

        return null;
    }

    /// <summary>
    /// A GameObject with this name in the given scene, inactive ones included. Scene roots
    /// first, so the tidy target is the root 'Audio Managers' rather than a same-named child
    /// somewhere else in the hierarchy.
    /// </summary>
    private static GameObject FindObjectByName(Scene scene, string name)
    {
        if (!scene.IsValid()) return null;

        GameObject[] roots = scene.GetRootGameObjects();

        foreach (GameObject root in roots)
        {
            if (root.name == name) return root;
        }

        foreach (GameObject root in roots)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.gameObject.name == name) return candidate.gameObject;
            }
        }

        return null;
    }

    /// <summary>Hierarchy path, the way the log lines and the work order write scene paths.</summary>
    private static string HierarchyPath(Transform transform)
    {
        if (transform == null) return "<none>";

        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }

        return path;
    }

    private static string Describe(AudioSource source)
    {
        return source == null ? "<none>" : $"'{HierarchyPath(source.transform)}'";
    }
}
