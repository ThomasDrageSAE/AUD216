using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/*
 * PortedPropTools.cs
 *
 * Purpose: Builds the prefabs for the props ported from the student submission, and adds
 *          their audio profiles to the scene's InteractionAudioManager.
 * Menu:    Tools/Interactions/...
 *
 * Both models arrived as bare FBXs with no prefab (their scene placed loose instances), so
 * the prefabs are built here through PrefabUtility rather than hand-written YAML - this
 * project has broken its scene that way before. All three items are safe to re-run.
 *
 * ⚠ The profile tool binds to InteractionAudioManager's array by the literal string
 * "interactionProfiles". Renaming that field compiles clean and breaks this silently, in
 * the same way as the other editors here (see CODE_STRUCTURE's editor-tooling warning).
 */

public static class PortedPropTools
{
    private const string ProfilesField = "interactionProfiles";

    // ---- Prefab building -----------------------------------------------------

    [MenuItem("Tools/Interactions/Create Trip Mushroom Prefab")]
    private static void CreateMushroom()
    {
        BuildPropPrefab(
            modelPath: "Assets/Mushroom Trip/Models/MushroomClump.fbx",
            prefabPath: "Assets/Mushroom Trip/Trip Mushroom.prefab",
            prefabName: "Trip Mushroom",
            solid: false,                       // a mushroom you walk over, not into
            addInteraction: root => root.AddComponent<MushroomTripInteraction>());
    }

    [MenuItem("Tools/Interactions/Create Well Prefab")]
    private static void CreateWell()
    {
        BuildPropPrefab(
            modelPath: "Assets/Well/Models/StylizedWell.fbx",
            prefabPath: "Assets/Well/Well.prefab",
            prefabName: "Well",
            solid: true,                        // stonework: the player should not walk through it
            addInteraction: root => root.AddComponent<PropSoundInteraction>());
    }

    /// <summary>
    /// Instantiates the model, gives it a trigger sized from its own bounds (plus a solid
    /// box when asked), an AudioSource and its interaction component, then saves it.
    /// </summary>
    private static void BuildPropPrefab(string modelPath, string prefabPath, string prefabName,
                                        bool solid, Action<GameObject> addInteraction)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog(prefabName,
                $"No model at {modelPath}.\n\nThe prefab is built from that FBX; check it " +
                "imported correctly.", "OK");
            return;
        }

        // A plain Instantiate (not InstantiatePrefab) gives a disconnected copy, so the
        // saved asset is an ordinary prefab referencing the FBX's meshes and materials
        // rather than a variant chained to the model importer.
        GameObject root = UnityEngine.Object.Instantiate(model);
        root.name = prefabName;

        try
        {
            bool hasBounds = TryGetLocalBounds(root, out Bounds bounds);

            if (solid && hasBounds)
            {
                // Blocking volume. A MeshCollider would hug the shape better, but these
                // models import with Read/Write disabled.
                BoxCollider blocker = root.AddComponent<BoxCollider>();
                blocker.center = bounds.center;
                blocker.size = bounds.size;
            }

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = hasBounds ? bounds.center : Vector3.zero;

            // The model's own size only. The player's reach is added at runtime by
            // InteractionReach.Apply, from the interaction component's Interaction Reach in
            // metres. It used to be baked in here as well, and two copies of one number that
            // agreed only by coincidence is the same drift that hid the 385 m collider.
            trigger.radius = hasBounds ? bounds.extents.magnitude : 0f;

            // The sound plays from the prop itself, and InteractionAudioManager
            // dereferences whatever source it is handed when the profile has none.
            AudioSource source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;

            addInteraction(root);

            EnsureFolderFor(prefabPath);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (saved == null)
            {
                Debug.LogError($"[PortedPropTools] Could not write {prefabPath}.");
                return;
            }

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[PortedPropTools] Created {prefabPath}. Drag it into the scene, then run " +
                      "Tools > Interactions > Add Missing Audio Profiles to give it a sound slot.", saved);
        }
        finally
        {
            // The scratch instance must go whether or not the save succeeded, or it is
            // left behind in the open scene.
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Combined bounds of every renderer, in the root's own local space. False when the
    /// model has no renderers at all.
    /// </summary>
    /// <remarks>
    /// Delegates to the runtime helper so the prefab built here and the trigger re-sized
    /// on Start agree by construction. A second copy of this calculation is what produced
    /// the 385 m collider the player ended up standing on - see InteractionReach.
    /// </remarks>
    private static bool TryGetLocalBounds(GameObject root, out Bounds bounds)
    {
        return InteractionReach.TryGetLocalBounds(root.transform, out bounds);
    }

    private static void EnsureFolderFor(string assetPath)
    {
        string folder = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string built = parts[0];                       // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = built + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
            built = next;
        }
    }

    // ---- Audio profiles ------------------------------------------------------

    /// <summary>
    /// Brings the scene's InteractionAudioManager to the current profile set: renames
    /// and removals first (the 2026-09-03 one-section-per-prop consolidation), then a
    /// row for every profile the scripts ask for, then prefab-side key migrations.
    /// Idempotent - re-running against an up-to-date scene changes nothing.
    /// </summary>
    [MenuItem("Tools/Interactions/Add Missing Audio Profiles")]
    private static void AddMissingProfiles()
    {
        InteractionAudioManager manager = UnityEngine.Object.FindFirstObjectByType<InteractionAudioManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Interaction audio profiles",
                "No InteractionAudioManager in the open scene.\n\nOpen the main scene and " +
                "run this again.", "OK");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogError($"[PortedPropTools] InteractionAudioManager has no array field " +
                           $"'{ProfilesField}' - it has been renamed, and this tool binds to it " +
                           "by name.", manager);
            return;
        }

        int changed = 0;

        // 2026-09-02 rename: the slot is called "Mushroom Eating" so it says what to
        // author. An existing "Mushroom" profile is renamed IN PLACE - clips and id
        // untouched - rather than duplicated, and the prefab's authored string follows.
        if (RenameProfile(profiles, "Mushroom", "Mushroom Eating")) changed++;

        // 2026-09-03 consolidation: ONE profile per prop. The four tapes collapse into
        // a single "Tape" (the owner places one recorder, in the studio); the two
        // flashlight profiles into one "Flashlight" carrying both toggles. Renames keep
        // clips and ids (the renamed enum members kept their ints, so Tape 1's id now
        // reads as Tape and Flashlight On's as Flashlight); removals drop rows the
        // owner chose to retire.
        if (RemoveProfile(profiles, "Tape 1")) changed++;
        if (RemoveProfile(profiles, "Tape 2")) changed++;
        if (RemoveProfile(profiles, "Tape 3")) changed++;
        if (RemoveProfile(profiles, "Tape 4")) changed++;
        if (RenameProfile(profiles, "Flashlight On", "Flashlight")) changed++;
        if (RemoveProfile(profiles, "Flashlight Off")) changed++;

        // 2026-09-03, third pass: the TURNTABLE REPLACES THE TAPE (the 'repurpose
        // the radio' request meant the tape recorder), and the walkie talkie -
        // mistakenly consumed for the turntable in the second pass - is restored:
        // its row gets its name back, and any content the brief field rename
        // shuffled into the turntable slots moves home.
        if (RemoveProfile(profiles, "Tape")) changed++;
        changed += RestoreWalkieTalkieRow(profiles);
        changed += MoveWalkieContentHome(profiles);

        if (EnsureProfile(profiles, "Mushroom Eating", InteractionId.MushroomEating,
                InteractionAudioManager.InteractionType.Mushroom)) changed++;
        if (EnsureProfile(profiles, "Well", InteractionId.Well)) changed++;
        if (EnsureProfile(profiles, "Well Splash", InteractionId.WellSplash)) changed++;
        if (EnsureProfile(profiles, "Flashlight", InteractionId.Flashlight,
                InteractionAudioManager.InteractionType.Flashlight)) changed++;
        if (EnsureProfile(profiles, "Cooking", InteractionId.Cooking,
                InteractionAudioManager.InteractionType.Cooking)) changed++;
        if (EnsureProfile(profiles, "Fishing", InteractionId.Fishing,
                InteractionAudioManager.InteractionType.Fishing)) changed++;
        if (EnsureProfile(profiles, "Walkie Talkie", InteractionId.WalkieTalkie,
                InteractionAudioManager.InteractionType.WalkieTalkie)) changed++;
        if (EnsureProfile(profiles, "Turntable", InteractionId.Turntable,
                InteractionAudioManager.InteractionType.Turntable)) changed++;

        // Rows that predate their type or id (renamed above, or created back when
        // everything was a SimpleInteraction) are aligned in place, clips untouched.
        changed += AlignProfileShape(profiles, "Mushroom Eating", InteractionId.MushroomEating,
            InteractionAudioManager.InteractionType.Mushroom);
        changed += AlignProfileShape(profiles, "Flashlight", InteractionId.Flashlight,
            InteractionAudioManager.InteractionType.Flashlight);
        changed += AlignProfileShape(profiles, "Walkie Talkie", InteractionId.WalkieTalkie,
            InteractionAudioManager.InteractionType.WalkieTalkie);
        changed += AlignProfileShape(profiles, "Turntable", InteractionId.Turntable,
            InteractionAudioManager.InteractionType.Turntable);

        changed += SeedFishingClips(profiles);
        changed += MigrateMushroomPrefabProfileName();

        if (changed == 0)
        {
            Debug.Log("[PortedPropTools] Interaction profiles are already present and named; " +
                      "nothing changed.", manager);
            return;
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

        Debug.Log($"[PortedPropTools] Applied {changed} interaction-profile change(s). For any " +
                  "new empty profile: drop its clips in, set its Mixer Group Output (SFX), and " +
                  "SAVE THE SCENE.", manager);
    }

    /// <summary>
    /// Deletes every tape recorder placed in the open scene (any PropSoundInteraction
    /// whose profile key is one of the tapes), logging each name and position first.
    /// The tape recorder is fully retired (2026-09-03): the studio TURNTABLE replaced
    /// it, and its prefab is gone. Undo-able.
    /// </summary>
    [MenuItem("Tools/Interactions/Remove Placed Tape Recorders")]
    private static void RemovePlacedTapeRecorders()
    {
        PropSoundInteraction[] props = UnityEngine.Object.FindObjectsByType<PropSoundInteraction>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int removed = 0;
        foreach (PropSoundInteraction prop in props)
        {
            SerializedObject serialized = new SerializedObject(prop);
            string profileName = serialized.FindProperty("audioProfileName")?.stringValue?.Trim() ?? "";
            int profileId = serialized.FindProperty("audioProfileId")?.intValue ?? 0;

            // RetiredTape..RetiredTape4 hold ints 10..13; literals so the retired
            // members need no direct (and warning-raising) reference.
            bool isTape = profileName.StartsWith("Tape", StringComparison.OrdinalIgnoreCase) ||
                          (profileId >= 10 && profileId <= 13);
            if (!isTape) continue;

            Debug.Log($"[PortedPropTools] Removing placed tape recorder '{prop.gameObject.name}' " +
                      $"at {prop.transform.position} (profile '{profileName}').");
            Undo.DestroyObjectImmediate(prop.gameObject);
            removed++;
        }

        if (removed == 0)
        {
            Debug.Log("[PortedPropTools] No placed tape recorders found; nothing removed.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[PortedPropTools] Removed {removed} tape recorder(s) (undo-able). The " +
                  "turntable replaced them - nothing to place. SAVE THE SCENE.");
    }

    /// <summary>
    /// Renames an existing profile in place - clips, sources and id untouched. Same
    /// trimmed, case-insensitive matching as EnsureProfile.
    /// </summary>
    private static bool RenameProfile(SerializedProperty profiles, string oldName, string newName)
    {
        SerializedProperty entry = FindProfile(profiles, oldName);
        if (entry == null) return false;

        entry.FindPropertyRelative("interactionName").stringValue = newName;
        return true;
    }

    /// <summary>Deletes a profile row outright. Only the consolidation calls this.</summary>
    private static bool RemoveProfile(SerializedProperty profiles, string name)
    {
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty existing = profiles.GetArrayElementAtIndex(i)
                .FindPropertyRelative("interactionName");

            if (existing != null && !string.IsNullOrEmpty(existing.stringValue) &&
                string.Equals(existing.stringValue.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                profiles.DeleteArrayElementAtIndex(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>An existing row by name, or null. Trimmed, case-insensitive.</summary>
    private static SerializedProperty FindProfile(SerializedProperty profiles, string name)
    {
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty entry = profiles.GetArrayElementAtIndex(i);
            SerializedProperty existing = entry.FindPropertyRelative("interactionName");

            if (existing != null && !string.IsNullOrEmpty(existing.stringValue) &&
                string.Equals(existing.stringValue.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets an existing row's id and type to their canonical values, clips untouched.
    /// Counts each actual change, so an aligned scene reports zero.
    /// </summary>
    private static int AlignProfileShape(SerializedProperty profiles, string name, InteractionId id,
                                         InteractionAudioManager.InteractionType type)
    {
        SerializedProperty entry = FindProfile(profiles, name);
        if (entry == null) return 0;

        int changed = 0;

        SerializedProperty idProp = entry.FindPropertyRelative("interactionId");
        if (idProp != null && idProp.intValue != (int)id)
        {
            idProp.intValue = (int)id;
            changed++;
        }

        SerializedProperty typeProp = entry.FindPropertyRelative("interactionType");
        if (typeProp != null && typeProp.intValue != (int)type)
        {
            typeProp.intValue = (int)type;
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// The two clips that were authored on the FishingRod prefab before its audio moved
    /// onto the manager (2026-09-03), carried across by asset guid so nothing already
    /// authored is lost. Fills empty slots only; re-running never overwrites.
    /// </summary>
    private static int SeedFishingClips(SerializedProperty profiles)
    {
        const string castGuid = "c67186a6dcd0a4669a302a2ecaf429bf";    // was fishingStartSound
        const string caughtGuid = "1cce807618ee74cb5b04d2d48766d04a";  // was fishCaughtSound

        SerializedProperty entry = FindProfile(profiles, "Fishing");
        if (entry == null) return 0;

        int changed = 0;
        changed += SetClipFromGuidIfEmpty(entry, "castSound", castGuid);
        changed += SetClipFromGuidIfEmpty(entry, "caughtSound", caughtGuid);
        return changed;
    }

    private static int SetClipFromGuidIfEmpty(SerializedProperty entry, string field, string guid)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p == null || p.objectReferenceValue != null) return 0;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return 0;

        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) return 0;

        p.objectReferenceValue = clip;
        Debug.Log($"[PortedPropTools] Fishing profile: '{field}' seeded with '{clip.name}' " +
                  "(carried over from the rod prefab).");
        return 1;
    }

    /// <summary>
    /// Moves the Trip Mushroom prefab's authored profile string off the old "Mushroom".
    /// A targeted edit rather than a rebuild, so anything the owner has authored on the
    /// prefab (tuning, reach) survives. Returns 1 when it changed anything.
    /// </summary>
    private static int MigrateMushroomPrefabProfileName()
    {
        const string prefabPath = "Assets/Mushroom Trip/Trip Mushroom.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return 0;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            MushroomTripInteraction interaction = root.GetComponentInChildren<MushroomTripInteraction>(true);
            if (interaction == null) return 0;

            SerializedObject serialized = new SerializedObject(interaction);
            SerializedProperty name = serialized.FindProperty("audioProfileName");
            if (name == null ||
                !string.Equals(name.stringValue.Trim(), "Mushroom", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            name.stringValue = "Mushroom Eating";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[PortedPropTools] Trip Mushroom prefab now asks for 'Mushroom Eating'.");
            return 1;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Undoes the second pass's mistake: the walkie talkie's row was renamed to
    /// "Turntable". When that state is found (a Turntable row, no Walkie Talkie row),
    /// the row gets its name back - the enum members were renamed back the same way,
    /// so its id and type ints already read as WalkieTalkie again - and EnsureProfile
    /// then creates a genuinely fresh Turntable row.
    /// </summary>
    private static int RestoreWalkieTalkieRow(SerializedProperty profiles)
    {
        if (FindProfile(profiles, "Walkie Talkie") != null) return 0;

        SerializedProperty entry = FindProfile(profiles, "Turntable");
        if (entry == null) return 0;

        entry.FindPropertyRelative("interactionName").stringValue = "Walkie Talkie";
        Debug.Log("[PortedPropTools] The 'Turntable' row was the walkie talkie's, renamed " +
                  "in error - its name is restored.");
        return 1;
    }

    /// <summary>
    /// While the turntable briefly wore the walkie talkie's field names
    /// (FormerlySerializedAs), anything authored on the walkie row was shuffled into
    /// the turntable slots on reload. Moves it home - only when the walkie slots are
    /// empty and the turntable slots are not, so re-running never overwrites.
    /// </summary>
    private static int MoveWalkieContentHome(SerializedProperty profiles)
    {
        SerializedProperty entry = FindProfile(profiles, "Walkie Talkie");
        if (entry == null) return 0;

        SerializedProperty walkieClips = entry.FindPropertyRelative("walkieClips");
        SerializedProperty turntableClips = entry.FindPropertyRelative("turntableClips");
        if (walkieClips == null || turntableClips == null) return 0;
        if (walkieClips.arraySize > 0 || turntableClips.arraySize == 0) return 0;

        walkieClips.arraySize = turntableClips.arraySize;
        for (int i = 0; i < turntableClips.arraySize; i++)
        {
            walkieClips.GetArrayElementAtIndex(i).objectReferenceValue =
                turntableClips.GetArrayElementAtIndex(i).objectReferenceValue;
        }
        turntableClips.arraySize = 0;

        CopyIntThenReset(entry, "walkiePlaybackMode", "turntablePlaybackMode", 0);
        CopyFloatThenReset(entry, "walkieMinGapSeconds", "turntableMinGapSeconds", 8f);
        CopyFloatThenReset(entry, "walkieMaxGapSeconds", "turntableMaxGapSeconds", 20f);

        Debug.Log("[PortedPropTools] Walkie Talkie clips/mode/gaps moved home from the " +
                  "turntable slots they were shuffled into.");
        return 1;
    }

    private static void CopyIntThenReset(SerializedProperty entry, string toField,
                                         string fromField, int resetTo)
    {
        SerializedProperty to = entry.FindPropertyRelative(toField);
        SerializedProperty from = entry.FindPropertyRelative(fromField);
        if (to == null || from == null) return;

        to.intValue = from.intValue;
        from.intValue = resetTo;
    }

    private static void CopyFloatThenReset(SerializedProperty entry, string toField,
                                           string fromField, float resetTo)
    {
        SerializedProperty to = entry.FindPropertyRelative(toField);
        SerializedProperty from = entry.FindPropertyRelative(fromField);
        if (to == null || from == null) return;

        to.floatValue = from.floatValue;
        from.floatValue = resetTo;
    }

    /// <summary>
    /// Removes the Crossbow profile from the scene's WeaponAudioManager - the level
    /// has no crossbow pickup yet (2026-09-03). WeaponId.Crossbow stays for the later
    /// revision that brings the weapon in; re-add the row by hand (or restore it from
    /// git history) when it does.
    /// </summary>
    [MenuItem("Tools/Audio/Remove Crossbow Weapon Profile")]
    private static void RemoveCrossbowWeaponProfile()
    {
        WeaponAudioManager manager = UnityEngine.Object.FindFirstObjectByType<WeaponAudioManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Crossbow profile",
                "No WeaponAudioManager in the open scene.", "OK");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty("m_WeaponProfiles");
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogError("[PortedPropTools] WeaponAudioManager has no array field " +
                           "'m_WeaponProfiles' - it has been renamed, and this tool binds " +
                           "to it by name.", manager);
            return;
        }

        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty entry = profiles.GetArrayElementAtIndex(i);
            SerializedProperty name = entry.FindPropertyRelative("weaponName");
            SerializedProperty id = entry.FindPropertyRelative("weaponId");

            bool isCrossbow =
                (name != null && string.Equals(name.stringValue.Trim(),
                    "Crossbow", StringComparison.OrdinalIgnoreCase)) ||
                (id != null && id.intValue == (int)WeaponId.Crossbow);
            if (!isCrossbow) continue;

            profiles.DeleteArrayElementAtIndex(i);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Debug.Log("[PortedPropTools] Crossbow weapon profile removed - the level has " +
                      "no crossbow pickup yet. SAVE THE SCENE.", manager);
            return;
        }

        Debug.Log("[PortedPropTools] No Crossbow profile on the WeaponAudioManager; " +
                  "nothing removed.", manager);
    }

    /// <summary>
    /// Appends an empty profile of the given type unless one of that name already
    /// exists. Matching is trimmed and case-insensitive, the same way AudioProfileKeys
    /// resolves a lookup, so this cannot create a duplicate the runtime would treat as
    /// the same key.
    /// </summary>
    private static bool EnsureProfile(SerializedProperty profiles, string name, InteractionId id,
        InteractionAudioManager.InteractionType type = InteractionAudioManager.InteractionType.SimpleInteraction)
    {
        if (FindProfile(profiles, name) != null) return false;

        profiles.InsertArrayElementAtIndex(profiles.arraySize);
        SerializedProperty entry = profiles.GetArrayElementAtIndex(profiles.arraySize - 1);

        // Unity's insert COPIES the previous element, so every content field is cleared
        // explicitly - otherwise the new profile would silently inherit the last one's
        // clips and play something that was never authored for it.
        SetString(entry, "interactionName", name);
        SetInt(entry, "interactionId", (int)id);
        SetInt(entry, "interactionType", (int)type);

        ClearObject(entry, "interactionSound");
        ClearObject(entry, "customAudioSource");
        ClearObject(entry, "fireLoop");
        ClearObject(entry, "leverSound");
        ClearObject(entry, "sparksSound");
        ClearObject(entry, "mixerGroupOutput");
        ClearObject(entry, "leftSpeakerAudio");
        ClearObject(entry, "rightSpeakerAudio");
        ClearObject(entry, "leftSpeakerSource");
        ClearObject(entry, "rightSpeakerSource");
        ClearObject(entry, "toggleOnSound");
        ClearObject(entry, "toggleOffSound");
        ClearObject(entry, "sizzleLoop");
        ClearObject(entry, "doneChime");
        ClearObject(entry, "castSound");
        ClearObject(entry, "biteSound");
        ClearObject(entry, "caughtSound");
        ClearArray(entry, "igniteSounds");
        ClearArray(entry, "dialogueLines");
        ClearArray(entry, "walkieClips");
        SetFloat(entry, "walkieMinGapSeconds", 8f);
        SetFloat(entry, "walkieMaxGapSeconds", 20f);
        SetInt(entry, "walkiePlaybackMode", 0);   // RandomContinuous
        ClearArray(entry, "turntableClips");
        SetFloat(entry, "turntableMinGapSeconds", 8f);
        SetFloat(entry, "turntableMaxGapSeconds", 20f);
        SetInt(entry, "turntablePlaybackMode", 0);   // RandomContinuous

        return true;
    }

    private static void SetString(SerializedProperty entry, string field, string value)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p != null) p.stringValue = value;
    }

    private static void SetInt(SerializedProperty entry, string field, int value)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p != null) p.intValue = value;
    }

    private static void SetFloat(SerializedProperty entry, string field, float value)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p != null) p.floatValue = value;
    }

    private static void ClearObject(SerializedProperty entry, string field)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p != null && p.propertyType == SerializedPropertyType.ObjectReference)
        {
            p.objectReferenceValue = null;
        }
    }

    private static void ClearArray(SerializedProperty entry, string field)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p != null && p.isArray) p.arraySize = 0;
    }
}
