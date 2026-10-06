using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Hangs the owner's WindChimes.fbx in a tree, gives it the loop slot students fill in, and
/// makes it move. Two menu items under Tools/Level:
///
///   Report Wind Chimes  - read-only. What is in the scene, where, how big, how loud, and
///                         whether a student has put a clip in yet.
///   Add Wind Chimes     - creates them, or repairs what is there. Idempotent, and it never
///                         writes the clip field, so running it again cannot wipe a student's
///                         work.
///
/// Why a tool and not a hand-placed prop: the chimes have to sit INSIDE a canopy, which is a
/// position measured off the tree rather than typed in, and the same measurement has to be
/// repeatable after the owner moves the tree. The tool prints every number it used.
///
/// ⚠ Scene changes only. Nothing here writes an asset, and the FBX is not modified.
/// </summary>
public static class WindChimeSetupTools
{
    private const string LogPrefix = "[WindChimeSetupTools]";
    private const string UndoLabel = "Add Wind Chimes";

    /// <summary>The owner's import, dropped into Assets/ on 2026-09-06.</summary>
    private const string ModelPath = "Assets/WindChimes.fbx";

    /// <summary>
    /// The tree they hang in: the birch 14.5 m from the spawn and 31 degrees off the way the
    /// player is first looking, so it is close to straight ahead as they set off.
    ///
    /// NOT the willow 4.7 m away, which was tried first and photographed: a willow is a solid
    /// curtain of fronds from its crown to the ground, and chimes hung anywhere inside it were
    /// invisible at 1.6 m, never mind at 4 m (Exports/hud/windchimes_in_tree.png). A birch has
    /// a bare trunk and its leaves up top, so something hung under the lowest branches reads
    /// against the sky, which is the whole point of hanging it.
    ///
    /// At 14.5 m the loop is at about 45% of its level on the linear rolloff and grows as the
    /// player walks in - a chime that arrives rather than one that is simply on.
    /// </summary>
    private const string TreePath = "Level Assets/Navmesh Trees/SM_Tree_Birch_01";

    /// <summary>What the object is called once it is hanging.</summary>
    private const string ChimesName = "Wind Chimes";

    /// <summary>
    /// Where the clip slot lives: the scene root that already carries every other audio
    /// manager. The owner's call, 2026-09-06 - one inspector holds every slot a student has to
    /// fill, rather than sending them hunting for a prop in the hierarchy.
    /// </summary>
    private const string AudioManagersPath = "Audio Managers";

    /// <summary>
    /// The bus the chimes come out of. AMBIENCE rather than a group of their own: the level's
    /// world sounds each have a child group under it (Fire, River, Waterfall, Birds, Frogs),
    /// and adding a Chimes group would be an edit to the mixer ASSET, which is not this tool's
    /// to make. Straight to Master - the default for a fresh AudioSource - would put the
    /// chimes outside the AMBIENCE fader altogether, which 30 sources in this scene already
    /// are and none of them should be.
    /// </summary>
    private const string MixerGroupName = "AMBIENCE";

    /// <summary>
    /// How far up the tree they hang, as a fraction of its height. 0.7 puts the hanging point
    /// at about 2.5 m on this birch - at the bottom edge of the foliage, where a real one
    /// would be tied to the lowest branch - and leaves the tubes' bottom ends at about 1.9 m,
    /// just over a 1.8 m player's head. Measured off the tree's renderers rather than typed as
    /// a world height, so moving the tree and re-running still hangs them under the leaves.
    /// </summary>
    private const float HangHeightFraction = 0.7f;

    /// <summary>
    /// Metres out from the trunk, towards the player's spawn. Inside the canopy - this
    /// birch's is about 1.25 m in radius - but off the trunk itself, which is where a branch
    /// worth hanging something from is.
    /// </summary>
    private const float HangOffsetFromTrunk = 0.7f;

    /// <summary>
    /// The FBX's own rotation, which is how it was authored to hang: the tubes run down the
    /// model's local -Z, and this quarter turn puts that axis on world down.
    ///
    /// Applied as a WORLD rotation on purpose. This willow's LOCAL rotation reads
    /// (2.4, 262.9, 18.3), which looks like an 18 degree lean and is not - it is cancelling
    /// the rotation on 'Level Assets' two levels up, and the tree stands upright in world
    /// space. Setting the world rotation is right either way, and does not depend on having
    /// read that chain correctly.
    /// </summary>
    private static readonly Quaternion HangRotation = Quaternion.Euler(270f, 0f, 0f);

    // ==========================================================================
    //  Report - reads nothing but the scene, changes nothing
    // ==========================================================================

    [MenuItem("Tools/Level/Report Wind Chimes", priority = 1300)]
    private static void ReportWindChimes()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Wind Chimes - read-only.");

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        report.AppendLine($"MODEL: {ModelPath} = {(model == null ? "<MISSING>" : "found")}");

        if (model != null && TryGetMeshBounds(model, out Bounds modelBounds))
        {
            report.AppendLine($"  mesh bounds at import scale: size {V3(modelBounds.size)} m, " +
                              $"centre {V3(modelBounds.center)}");
        }

        GameObject tree = FindByPath(TreePath);
        report.AppendLine($"TREE: {TreePath} = {(tree == null ? "<MISSING>" : "found")}");

        if (tree != null && TryGetRendererBounds(tree, out Bounds treeBounds))
        {
            report.AppendLine($"  world bounds: min {V3(treeBounds.min)}, max {V3(treeBounds.max)}, " +
                              $"centre {V3(treeBounds.center)}");
            report.AppendLine($"  hanging point this tool would use: {V3(HangPoint(tree, treeBounds))}");
        }

        GameObject chimes = FindChimes(tree);

        if (chimes == null)
        {
            report.AppendLine($"CHIMES: <none> - run Tools/Level/Add Wind Chimes.");
            Debug.Log(report.ToString().TrimEnd());
            return;
        }

        report.AppendLine($"CHIMES: {HierarchyPath(chimes.transform)}");
        report.AppendLine($"  position {V3(chimes.transform.position)}, " +
                          $"world rotation {V3(chimes.transform.eulerAngles)}, " +
                          $"localScale {V3(chimes.transform.localScale)}, active={chimes.activeInHierarchy}");

        if (TryGetMeshBounds(chimes, out Bounds hangingBounds))
        {
            report.AppendLine($"  hanging size: {V3(hangingBounds.size)} m, " +
                              $"top {Num(hangingBounds.max.y)} m, bottom {Num(hangingBounds.min.y)} m");
        }

        AppendAudioReport(report, chimes);
        AppendSwayReport(report, chimes);

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            report.AppendLine($"  distance from the player's spawn: " +
                              $"{Num(Vector3.Distance(player.transform.position, chimes.transform.position))} m");
        }

        Debug.Log(report.ToString().TrimEnd());
    }

    private static void AppendAudioReport(StringBuilder report, GameObject chimes)
    {
        WindChimeAudio audio = chimes.GetComponent<WindChimeAudio>();

        if (audio == null)
        {
            report.AppendLine("  WindChimeAudio: <MISSING> - the chimes have no speaker in them.");
        }
        else
        {
            SerializedObject serialized = new SerializedObject(audio);
            SerializedProperty bus = serialized.FindProperty("outputMixerGroup");
            report.AppendLine($"  WindChimeAudio: volume {PropNum(serialized, "volume")}, " +
                              $"fadeIn {PropNum(serialized, "fadeInDuration")} s, " +
                              $"distance {PropNum(serialized, "minDistance")}..{PropNum(serialized, "maxDistance")} m, " +
                              $"bus {(bus == null || bus.objectReferenceValue == null ? "<none - straight to Master>" : "'" + bus.objectReferenceValue.name + "'")} " +
                              "(the clip comes from the manager - see below)");
        }

        GameObject host = FindByPath(AudioManagersPath);
        WindChimeAudioManager manager = host != null ? host.GetComponent<WindChimeAudioManager>() : null;

        if (manager == null)
        {
            report.AppendLine($"  WindChimeAudioManager on {AudioManagersPath}: <MISSING> - there is " +
                              "no slot for a student to fill. Run Add Wind Chimes.");
        }
        else
        {
            SerializedProperty targets = new SerializedObject(manager).FindProperty("chimes");
            report.AppendLine($"  WindChimeAudioManager on {AudioManagersPath}: windChimeLoop = " +
                              $"{(manager.Loop == null ? "<EMPTY - the assessed slot>" : "'" + manager.Loop.name + "'")}, " +
                              $"chimes wired = {(targets == null ? "?" : targets.arraySize.ToString())} " +
                              "(0 means every set in the scene, found at startup)");
        }

        AudioSource source = chimes.GetComponent<AudioSource>();
        report.AppendLine(source == null
            ? "  AudioSource: <MISSING>"
            : $"  AudioSource: loop={source.loop}, playOnAwake={source.playOnAwake}, " +
              $"spatialBlend={Num(source.spatialBlend)}, rolloff={source.rolloffMode}, " +
              $"clip={(source.clip == null ? "<none>" : source.clip.name)}");
    }

    private static void AppendSwayReport(StringBuilder report, GameObject chimes)
    {
        WindChimeSway sway = chimes.GetComponent<WindChimeSway>();

        if (sway == null)
        {
            report.AppendLine("  WindChimeSway: <MISSING> - the chimes would hang dead still.");
            return;
        }

        SerializedObject serialized = new SerializedObject(sway);
        SerializedProperty swingers = serialized.FindProperty("swingers");

        report.AppendLine($"  WindChimeSway: swayAngle {PropNum(serialized, "swayAngle")} deg, " +
                          $"period {PropNum(serialized, "swayPeriod")} s, " +
                          $"gust {PropNum(serialized, "gustPeriod")} s at depth {PropNum(serialized, "gustDepth")}, " +
                          $"children x{PropNum(serialized, "swingerMultiplier")}, " +
                          $"swingers wired = {(swingers == null ? "?" : swingers.arraySize.ToString())} " +
                          "(0 means every child with a mesh, resolved at startup)");
    }

    // ==========================================================================
    //  Add - creates them, or repairs what is already there
    // ==========================================================================

    [MenuItem("Tools/Level/Add Wind Chimes", priority = 1301)]
    private static void AddWindChimes()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"{LogPrefix} No model at {ModelPath}. Nothing was changed.");
            return;
        }

        GameObject tree = FindByPath(TreePath);
        if (tree == null)
        {
            Debug.LogError($"{LogPrefix} No tree at {TreePath}. Nothing was changed - move the " +
                           "chimes by hand, or point TreePath at another tree and re-run.");
            return;
        }

        if (!TryGetRendererBounds(tree, out Bounds treeBounds))
        {
            Debug.LogError($"{LogPrefix} {TreePath} has no renderers to measure. Nothing was changed.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- the object itself ----
        GameObject chimes = FindChimes(tree);
        bool created = chimes == null;

        if (chimes == null)
        {
            chimes = (GameObject)PrefabUtility.InstantiatePrefab(model, tree.transform);
            chimes.name = ChimesName;
            Undo.RegisterCreatedObjectUndo(chimes, UndoLabel);

            // A prefab instance rather than a plain copy, so a re-import of the FBX reaches
            // the chimes in the scene. The two components below are added on top of it, which
            // Unity records as instance overrides - that is the normal way to dress a model.
            fields.Add($"created {HierarchyPath(chimes.transform)} from {ModelPath} " +
                       "(a prefab instance, so a re-imported FBX still updates it)");

            StripImportedExtras(chimes, notes);
        }
        else
        {
            skipped.Add($"{HierarchyPath(chimes.transform)} already exists - repaired in place, " +
                        "and neither its position nor the clip slot was touched");
        }

        // ---- where it hangs ----
        //
        // Only on the run that CREATES them. The owner places the chimes by hand (their call,
        // 2026-09-06), so a later run of this tool - to repair a component, or after the
        // manager changed - must not drag them back to where the maths says they go. Repairing
        // a prop and moving it are different jobs, and only one of them was asked for.
        if (created)
        {
            Vector3 hangPoint = HangPoint(tree, treeBounds);
            Undo.RecordObject(chimes.transform, UndoLabel);

            chimes.transform.position = hangPoint;
            chimes.transform.rotation = HangRotation;

            fields.Add($"hung at {V3(hangPoint)} " +
                       $"({Num(HangHeightFraction * 100f)}% of the tree's height, " +
                       $"{Num(HangOffsetFromTrunk)} m out from the trunk towards the player), " +
                       $"world rotation {V3(HangRotation.eulerAngles)} so the tubes hang straight " +
                       "down whatever the branch above them is doing");
        }
        else
        {
            skipped.Add($"left exactly where they are, at {V3(chimes.transform.position)} " +
                        "- placement is the owner's, and this tool only moves chimes it just made");
        }

        // ---- the loop a student fills in ----
        AudioSource source = chimes.GetComponent<AudioSource>();
        if (source == null)
        {
            source = Undo.AddComponent<AudioSource>(chimes);
            fields.Add("added an AudioSource (WindChimeAudio configures it: 3D, looping, linear rolloff)");
        }
        else
        {
            skipped.Add("AudioSource already present");
        }

        source.playOnAwake = false;

        WindChimeAudio audio = chimes.GetComponent<WindChimeAudio>();

        if (audio == null)
        {
            audio = Undo.AddComponent<WindChimeAudio>(chimes);
            fields.Add("added WindChimeAudio - it owns the level, the rolloff and the fade; the " +
                       "clip comes from the manager");
        }
        else
        {
            skipped.Add("WindChimeAudio already present");
        }

        EnsureMixerGroup(audio, fields, skipped, notes);

        // ---- the movement ----
        if (chimes.GetComponent<WindChimeSway>() == null)
        {
            Undo.AddComponent<WindChimeSway>(chimes);
            fields.Add("added WindChimeSway - the cluster leans on the gust, the six tubes and the " +
                       "clapper swing further and out of step, each about an axis measured from its " +
                       "own mesh");
        }
        else
        {
            skipped.Add("WindChimeSway already present");
        }

        // ---- the clip slot, on the Audio Managers object ----
        EnsureManager(chimes, fields, skipped, notes);

        if (TryGetMeshBounds(chimes, out Bounds hangingBounds))
        {
            notes.Add($"Hanging size {V3(hangingBounds.size)} m, top at {Num(hangingBounds.max.y)} m and " +
                      $"bottom at {Num(hangingBounds.min.y)} m. A 1.8 m player walks under them.");
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            notes.Add($"{Num(Vector3.Distance(player.transform.position, chimes.transform.position))} m " +
                      "from the player's spawn, and audible to 25 m, so they are heard immediately.");
        }

        notes.Add($"The loop slot is EMPTY on purpose - it is the students' to fill. It is on " +
                  $"'{AudioManagersPath}' with every other manager, and in the audio progress " +
                  "report (Ctrl+G) under 'Wind Chime Audio Manager'.");

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Add Wind Chimes finished. Scene changes only - no asset was written.");

        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - the chimes were already hung correctly>");

        log.AppendLine($"ALREADY PRESENT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"Undo-recorded as one step, '{UndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or the chimes are lost.");

        Debug.Log(log.ToString().TrimEnd());

        Selection.activeGameObject = chimes;
        EditorGUIUtility.PingObject(chimes);
    }

    /// <summary>
    /// Routes the chimes into the AMBIENCE bus. A fresh AudioSource goes straight to Master,
    /// where the ambience fader cannot reach it - on a project marked for its mixing, a prop
    /// outside the bus it belongs to is a defect, not a detail.
    /// </summary>
    private static void EnsureMixerGroup(WindChimeAudio audio, List<string> fields,
                                         List<string> skipped, List<string> notes)
    {
        if (audio == null) return;

        SerializedObject serialized = new SerializedObject(audio);
        SerializedProperty property = serialized.FindProperty("outputMixerGroup");

        if (property == null)
        {
            notes.Add("⚠ WindChimeAudio has no outputMixerGroup field, so the chimes stay on " +
                      "whatever bus their AudioSource is set to.");
            return;
        }

        if (property.objectReferenceValue != null)
        {
            skipped.Add($"WindChimeAudio already routes to " +
                        $"'{property.objectReferenceValue.name}'");
            return;
        }

        AudioMixerGroup group = FindMixerGroup(MixerGroupName);

        if (group == null)
        {
            notes.Add($"⚠ No '{MixerGroupName}' group found in any AudioMixer, so the chimes go " +
                      "straight to Master and the ambience fader will not touch them.");
            return;
        }

        property.objectReferenceValue = group;
        serialized.ApplyModifiedProperties();
        fields.Add($"WindChimeAudio.outputMixerGroup = '{group.name}' - the same bus the level's " +
                   "other world sounds sit under");
    }

    /// <summary>
    /// The first mixer group with this name, across every AudioMixer in the project. By name
    /// rather than by asset path so a mixer that is moved or renamed does not break it.
    /// </summary>
    private static AudioMixerGroup FindMixerGroup(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioMixer"))
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AssetDatabase.GUIDToAssetPath(guid));
            if (mixer == null) continue;

            foreach (AudioMixerGroup group in mixer.FindMatchingGroups(name))
            {
                if (group.name == name) return group;
            }
        }

        return null;
    }

    /// <summary>
    /// Puts the clip slot on the Audio Managers object and points it at these chimes.
    ///
    /// The reference is wired explicitly rather than left to the manager's find-them-all
    /// fallback, because a scene reference is to the OBJECT and not to its path: it survives
    /// the owner dragging the prop into another tree, which is exactly what is expected to
    /// happen to it. The fallback stays for a set of chimes added later by hand.
    /// </summary>
    private static void EnsureManager(GameObject chimes, List<string> fields, List<string> skipped,
                                      List<string> notes)
    {
        GameObject host = FindByPath(AudioManagersPath);

        if (host == null)
        {
            notes.Add($"⚠ No '{AudioManagersPath}' object, so the clip slot has nowhere to live. " +
                      "The chimes will hang silently. Nothing else was affected.");
            return;
        }

        WindChimeAudioManager manager = host.GetComponent<WindChimeAudioManager>();

        if (manager == null)
        {
            manager = Undo.AddComponent<WindChimeAudioManager>(host);
            fields.Add($"added WindChimeAudioManager to {AudioManagersPath} - its windChimeLoop " +
                       "field is the assessed slot, and it appears in the progress report on its own");
        }
        else
        {
            skipped.Add($"{AudioManagersPath} already has a WindChimeAudioManager, clip untouched");
        }

        WindChimeAudio audio = chimes.GetComponent<WindChimeAudio>();
        if (audio == null) return;

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty targets = serialized.FindProperty("chimes");

        if (targets == null)
        {
            notes.Add("⚠ WindChimeAudioManager has no 'chimes' field - it will fall back to " +
                      "finding every set in the scene at startup, which still works.");
            return;
        }

        for (int i = 0; i < targets.arraySize; i++)
        {
            if (targets.GetArrayElementAtIndex(i).objectReferenceValue == audio)
            {
                skipped.Add($"{AudioManagersPath} WindChimeAudioManager already points at these chimes");
                return;
            }
        }

        // Growing a SerializedProperty array COPIES the previous last element, so the new slot
        // is written rather than assumed empty.
        int index = targets.arraySize;
        targets.arraySize = index + 1;
        targets.GetArrayElementAtIndex(index).objectReferenceValue = audio;
        serialized.ApplyModifiedProperties();

        fields.Add($"{AudioManagersPath} WindChimeAudioManager.chimes[{index}] = " +
                   $"{HierarchyPath(chimes.transform)}");
    }

    /// <summary>
    /// Where in the canopy the chimes hang: up the trunk by a fraction of the tree's height,
    /// and out from it towards the player, so they are on the side that is looked at rather
    /// than hidden behind the trunk.
    /// </summary>
    private static Vector3 HangPoint(GameObject tree, Bounds treeBounds)
    {
        Vector3 trunk = new Vector3(tree.transform.position.x, 0f, tree.transform.position.z);

        GameObject player = GameObject.FindWithTag("Player");
        Vector3 towards = player != null
            ? new Vector3(player.transform.position.x, 0f, player.transform.position.z) - trunk
            : Vector3.left;

        // A player standing exactly at the trunk would give a zero direction and a NaN normal.
        Vector3 outwards = towards.sqrMagnitude > 0.0001f ? towards.normalized : Vector3.left;

        float height = Mathf.Lerp(treeBounds.min.y, treeBounds.max.y, HangHeightFraction);

        return new Vector3(trunk.x, height, trunk.z) + outwards * HangOffsetFromTrunk;
    }

    /// <summary>
    /// What the FBX importer added that a prop hanging in a tree does not want. The chimes are
    /// scenery: a collider on them would block a shot, and an Animator would fight
    /// <see cref="WindChimeSway"/> for the same transforms.
    /// </summary>
    private static void StripImportedExtras(GameObject root, List<string> notes)
    {
        int colliders = 0;
        int animators = 0;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            Undo.DestroyObjectImmediate(collider);
            colliders++;
        }

        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        {
            Undo.DestroyObjectImmediate(animator);
            animators++;
        }

        if (colliders > 0 || animators > 0)
        {
            notes.Add($"Removed {colliders} collider(s) and {animators} animator(s) the importer added.");
        }
    }

    private static GameObject FindChimes(GameObject tree)
    {
        if (tree != null)
        {
            Transform child = tree.transform.Find(ChimesName);
            if (child != null) return child.gameObject;
        }

        // Also findable after the owner has dragged it somewhere else in the hierarchy, which
        // is the likeliest thing to happen to a prop.
        foreach (WindChimeAudio audio in Object.FindObjectsByType<WindChimeAudio>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            return audio.gameObject;
        }

        return null;
    }

    // ==========================================================================
    //  Measuring
    // ==========================================================================

    /// <summary>
    /// World bounds from the MESHES. Renderer.bounds is a cached world box the engine
    /// refreshes when it draws, so on an object instantiated moments ago in edit mode it reads
    /// near-zero - and it includes particle systems. See the same note in MinigunSetupTools,
    /// where believing Renderer.bounds produced a gun 18 m long.
    /// </summary>
    private static bool TryGetMeshBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;

            Bounds local = filter.sharedMesh.bounds;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3(
                    (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                    (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                    (corner & 4) == 0 ? -local.extents.z : local.extents.z);

                Vector3 world = filter.transform.TransformPoint(local.center + offset);

                if (!any)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }

        return any;
    }

    /// <summary>
    /// Renderer bounds, which for a tree that has been sitting in the scene since it was
    /// placed are both refreshed and the right answer - a tree's canopy is its renderers.
    /// </summary>
    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(root.transform.position, Vector3.zero);
        bool any = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return any;
    }

    // ==========================================================================
    //  Small shared helpers, the same shape as the other setup tools here
    // ==========================================================================

    private static GameObject FindByPath(string path)
    {
        GameObject direct = GameObject.Find(path);
        if (direct != null) return direct;

        // GameObject.Find skips inactive objects; this level switches whole branches off.
        string[] parts = path.Split('/');
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;

            Transform current = root.transform;
            for (int i = 1; i < parts.Length && current != null; i++)
            {
                current = current.Find(parts[i]);
            }

            if (current != null) return current.gameObject;
        }

        return null;
    }

    private static void AppendList(StringBuilder log, List<string> lines, string emptyLine)
    {
        if (lines.Count == 0)
        {
            if (!string.IsNullOrEmpty(emptyLine)) log.AppendLine(emptyLine);
            return;
        }

        foreach (string line in lines) log.Append("  ").AppendLine(line);
    }

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

    /// <summary>Invariant culture, so a report diffs the same on a comma-decimal machine.</summary>
    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string V3(Vector3 value) => $"({Num(value.x)}, {Num(value.y)}, {Num(value.z)})";

    private static string PropNum(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) return "<no field>";

        return property.propertyType == SerializedPropertyType.Float
            ? Num(property.floatValue)
            : property.intValue.ToString(CultureInfo.InvariantCulture);
    }
}
