using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * AudioSlotSurfaceTools.cs
 *
 * Purpose: Makes the Ctrl+G student progress report list every slot the code actually
 *          plays. Five things, all of them the same defect wearing five hats: an EMPTY
 *          ARRAY IS NOT A REPORT ROW, so an assessed sound with `[]` behind it is a slot
 *          no student can see and no marker can grade.
 * Menu:    Tools/Audio/Report Slot Surface   (read only)
 *          Tools/Audio/Fix Slot Surface      (idempotent - a second run says "nothing to do")
 * Used by: the integrator, driven from the Unity CLI. The owner saves the scene afterwards.
 *
 * Menu items rather than hand edits of the .unity/.asset/.prefab files, for this project's
 * standing reason: the files on disk are routinely older than what is open in the editor, so
 * a hand edit would be lost the next time the owner saves. Same shape as EnemyRoleSetupTools
 * and CombatFeedbackSetupTools next door.
 *
 * ⚠ NO DIALOGS anywhere in this file. Both items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block it with a modal window nobody can see,
 * let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Run Report before and after Fix: one block, fixed line order, invariant-culture numbers,
 * so the two runs diff cleanly and the diff IS the record of what Fix did.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE DEFECT THIS TOOL EXISTS FOR: SIZE 0 IS NO ROW AT ALL
 * ---------------------------------------------------------------------------------------
 * AudioProgressScanner.WalkValue records one row PER ARRAY ELEMENT, so an AudioClip[] of
 * size 0 produces NO row - not a red MISSING row, nothing. A single AudioClip field records
 * even when it is null. So `headshotSounds: []` is an assessed, played slot (ZombieAI plays
 * it on every head hit) that the report has never once listed, on all five enemy profiles.
 * Sizing each to exactly ONE NULL element is what puts it in front of the student. A clip is
 * never written into any of them - those are the students' to fill.
 *
 * ---------------------------------------------------------------------------------------
 * THREE KINDS OF WRITE, AND ONLY ONE OF THEM IS UNDOABLE
 * ---------------------------------------------------------------------------------------
 * SCENE writes (the enemy profiles, the mushroom profile, the registry component and its
 * list) are Undo-recorded as one step and need the owner to SAVE THE SCENE.
 * PREFAB writes (the boss) go straight to disk through PrefabUtility.LoadPrefabContents /
 * SaveAsPrefabAsset / UnloadPrefabContents.
 * ASSET writes (the three MusicStateDefinition ScriptableObjects) go straight to disk too.
 * ⚠ For those last two Ctrl+Z does NOTHING - `git checkout -- <path>` is the way back, and
 * the log names every path it wrote.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE MUSIC STATES: THE ORDER SAID "stems: []", THE ASSETS SAY "loopSegments: []"
 * ---------------------------------------------------------------------------------------
 * Read off the three assets rather than assumed: `loopSegments: []` - there is no loop
 * segment to put a stem into. Only introSegment and outroSegment exist, each with
 * `stems: []`. So this tool CREATES loopSegments[0] as well as its one stem, which has a
 * consequence worth knowing before it is run:
 *
 *   MusicStateDefinition.OnValidate returns early while `loopSegments.Length == 0`. Once
 *   the array has an element it runs ValidateContent, which finds !HasLoops (a stem with a
 *   null clip is not content) and logs ONE WARNING PER ASSET:
 *       "'MusicState_Exploring' (Exploring) has no usable loop segments; the state cannot play."
 *   Three new warnings, on import and on every inspector touch, until a student drops a clip
 *   into the stem - at which point they stop by themselves. They are WARNINGS, not errors,
 *   and they are TRUE: the state genuinely cannot play. Nothing else changes.
 *
 * PLAYBACK IS UNCHANGED. Verified line by line against LayeredStatePlayer: a stem whose clip
 * is null is treated exactly as no stem at all, everywhere.
 *   - ctor slot count: MaxStemCount walks EnumerateSegments(), which yields only segments
 *     with HasContent, and HasContent requires a non-null clip -> still 0 -> still 1 slot.
 *   - ctor preload: guarded on `stem.clip != null`.
 *   - usableLoops: only HasContent segments are added -> still empty.
 *   - ScheduleEntry: PickFirstLoop() returns null on an empty usableLoops and the entry is
 *     abandoned, exactly as today.
 *   - ScheduleSegmentOnBank: `hasClip` requires a non-null clip, else Stop() and clip = null.
 *   - UpdateStemGates: `slotExists = stem != null && stem.clip != null` - the null-clip stem
 *     takes the "slot this segment does not use" branch, which the code already handles on
 *     purpose (it is the branch that stops an unused slot climbing to full).
 *   - ApplyVolumes: `if (source.clip == null) continue` - the source is never touched.
 * No exception, no silent source that still counts as a layer, no extra AudioSource.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ Binds to serialized fields by literal name: on EnemyAudioManager the array
 * "enemyProfiles" plus "enemyName" and "headshotSounds"; on InteractionAudioManager the
 * array "interactionProfiles" plus "interactionName" and "dialogueLines"; on
 * BossZombieController "phaseCrackSounds"; on AssessedPrefabRegistry "prefabs"; on
 * MusicStateDefinition "loopSegments", and on its segments/stems "segmentName",
 * "lengthInBars", "stems", "stemName", "clip", "minIntensity", "fadeRange", "volume",
 * "fadeInTime", "fadeOutTime". Renaming any of those compiles clean and breaks this
 * silently - see CODE_STRUCTURE's editor-tooling warning. Every lookup below reports a
 * "<no such field>" note rather than throwing.
 */

public static class AudioSlotSurfaceTools
{
    private const string LogPrefix = "[AudioSlotSurfaceTools]";
    private const string UndoLabel = "Fix Slot Surface";

    // ---- Scene ---------------------------------------------------------------

    /// <summary>
    /// Where the audio singletons live. The registry goes here because that is where the
    /// scanner already finds every manager it walks.
    /// </summary>
    private const string HostName = "Audio Managers";

    private const string EnemyProfilesField = "enemyProfiles";
    private const string InteractionProfilesField = "interactionProfiles";

    /// <summary>Played on every head hit by ZombieAI; `[]` on all five profiles today.</summary>
    private const string HeadshotField = "headshotSounds";

    /// <summary>Played a few seconds after a bite by MushroomTripInteraction.</summary>
    private const string DialogueField = "dialogueLines";

    /// <summary>
    /// The one interaction profile that plays dialogue. Matched the way AudioProfileKeys
    /// resolves a key - trimmed and case-insensitive - never by scene line.
    /// </summary>
    private const string MushroomProfileName = "Mushroom Eating";

    // ---- Prefab --------------------------------------------------------------

    /// <summary>Played by BossZombieController when a phase threshold is crossed.</summary>
    private const string PhaseCrackField = "phaseCrackSounds";

    private const string RegistryField = "prefabs";

    // ---- Music ---------------------------------------------------------------

    private const string LoopSegmentsField = "loopSegments";
    private const string StemsField = "stems";

    /// <summary>
    /// The defaults MusicStem declares, written explicitly rather than trusted to the
    /// array grow. Growing a SerializedProperty array COPIES the previous last element,
    /// and on an empty array the new element's contents are not something a reader should
    /// have to reason about. These are read off MusicStateDefinition.cs, not invented.
    /// </summary>
    private const string StemDefaultName = "Stem 1";
    private const float StemDefaultMinIntensity = 0f;
    private const float StemDefaultFadeRange = 0f;
    private const float StemDefaultVolume = 1f;
    private const float StemDefaultFadeIn = 1.5f;
    private const float StemDefaultFadeOut = 2.5f;

    /// <summary>Likewise the defaults MusicSegment declares, for the loop segment created.</summary>
    private const string SegmentDefaultName = "Loop";
    private const int SegmentDefaultBars = 8;

    // ==========================================================================
    //  1. Report - reads and prints, changing nothing
    // ==========================================================================

    [MenuItem("Tools/Audio/Report Slot Surface", priority = 1500)]
    private static void ReportSlotSurface()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Slot Surface (read only - nothing was changed).");

        int rows = 0;
        rows += AppendEnemyReport(report);
        rows += AppendMushroomReport(report);
        rows += AppendBossReport(report);
        AppendRegistryReport(report);
        rows += AppendMusicReport(report);

        report.AppendLine($"NEW REPORT ROWS THESE SLOTS WILL ADD: {rows} " +
                          "(wants 5 headshot + 1 dialogue + 1 phase crack + 3 music stems = 10 " +
                          "after Fix Slot Surface; the boss's four single fields and the rod's " +
                          "six fish rows arrive with Agent A's registry walk, not from here).");

        Debug.Log(report.ToString().TrimEnd());
    }

    /// <summary>Every enemy profile, addressed by its enemyName. Five today, all empty.</summary>
    private static int AppendEnemyReport(StringBuilder report)
    {
        EnemyAudioManager manager = Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            report.AppendLine("ENEMY PROFILES: <no EnemyAudioManager in the open scene>");
            return 0;
        }

        SerializedProperty profiles = ArrayOn(manager, EnemyProfilesField);
        if (profiles == null)
        {
            report.AppendLine($"ENEMY PROFILES: EnemyAudioManager has no array field " +
                              $"'{EnemyProfilesField}' - it has been renamed, and this tool binds " +
                              "to it by name.");
            return 0;
        }

        report.AppendLine($"ENEMY PROFILES on '{HierarchyPath(manager.transform)}': {profiles.arraySize}");

        int rows = 0;
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            SerializedProperty array = profile.FindPropertyRelative(HeadshotField);
            string name = NameOf(profile, "enemyName");

            if (array == null || !array.isArray)
            {
                report.AppendLine($"  '{name}' {HeadshotField}=<no such field>");
                continue;
            }

            rows += array.arraySize == 0 ? 1 : 0;
            report.AppendLine($"  '{name}' {HeadshotField}={array.arraySize}" +
                              (array.arraySize == 0
                                  ? "  ⚠ EMPTY = NO REPORT ROW (Fix sizes it to 1)"
                                  : " (already sized - left exactly as it is)"));
        }

        return rows;
    }

    private static int AppendMushroomReport(StringBuilder report)
    {
        InteractionAudioManager manager =
            Object.FindFirstObjectByType<InteractionAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            report.AppendLine("INTERACTION PROFILES: <no InteractionAudioManager in the open scene>");
            return 0;
        }

        SerializedProperty profiles = ArrayOn(manager, InteractionProfilesField);
        if (profiles == null)
        {
            report.AppendLine($"INTERACTION PROFILES: InteractionAudioManager has no array field " +
                              $"'{InteractionProfilesField}' - renamed, and this tool binds by name.");
            return 0;
        }

        int index = FindByName(profiles, "interactionName", MushroomProfileName);
        if (index < 0)
        {
            report.AppendLine($"INTERACTION PROFILES on '{HierarchyPath(manager.transform)}': " +
                              $"{profiles.arraySize}, but NO '{MushroomProfileName}' profile - its " +
                              "dialogue row cannot be added.");
            return 0;
        }

        SerializedProperty array = profiles.GetArrayElementAtIndex(index).FindPropertyRelative(DialogueField);
        if (array == null || !array.isArray)
        {
            report.AppendLine($"INTERACTION PROFILES: '{MushroomProfileName}' has no array field " +
                              $"'{DialogueField}' - renamed, and this tool binds by name.");
            return 0;
        }

        report.AppendLine($"INTERACTION PROFILES on '{HierarchyPath(manager.transform)}': " +
                          $"{profiles.arraySize}; '{MushroomProfileName}' [{index}] " +
                          $"{DialogueField}={array.arraySize}" +
                          (array.arraySize == 0
                              ? "  ⚠ EMPTY = NO REPORT ROW (Fix sizes it to 1)"
                              : " (already sized - left exactly as it is)"));

        return array.arraySize == 0 ? 1 : 0;
    }

    private static int AppendBossReport(StringBuilder report)
    {
        string path = FindPrefabWith<BossZombieController>();
        if (path == null)
        {
            report.AppendLine("BOSS PREFAB: <no prefab in the project carries BossZombieController>");
            return 0;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        BossZombieController boss = prefab == null ? null : prefab.GetComponentInChildren<BossZombieController>(true);
        if (boss == null)
        {
            report.AppendLine($"BOSS PREFAB {path}: could not be loaded.");
            return 0;
        }

        SerializedProperty array = new SerializedObject(boss).FindProperty(PhaseCrackField);
        if (array == null || !array.isArray)
        {
            report.AppendLine($"BOSS PREFAB {path}: BossZombieController has no array field " +
                              $"'{PhaseCrackField}' - renamed, and this tool binds by name.");
            return 0;
        }

        report.AppendLine($"BOSS PREFAB {path}: {PhaseCrackField}={array.arraySize}" +
                          (array.arraySize == 0
                              ? "  ⚠ EMPTY = NO REPORT ROW (Fix sizes it to 1 - a PREFAB WRITE)"
                              : " (already sized - left exactly as it is)"));

        return array.arraySize == 0 ? 1 : 0;
    }

    private static void AppendRegistryReport(StringBuilder report)
    {
        GameObject host = FindHost();
        report.AppendLine(host == null
            ? $"REGISTRY HOST: <no '{HostName}' object and no EnemyAudioManager in the open scene>"
            : $"REGISTRY HOST: {HierarchyPath(host.transform)}");

        string bossPath = FindPrefabWith<BossZombieController>();
        string rodPath = FindPrefabWith<FishingRod>();
        report.AppendLine($"  prefab-only components found by type: BossZombieController -> " +
                          $"{bossPath ?? "<none>"}; FishingRod -> {rodPath ?? "<none>"}");

        AssessedPrefabRegistry registry =
            Object.FindFirstObjectByType<AssessedPrefabRegistry>(FindObjectsInactive.Include);
        if (registry == null)
        {
            report.AppendLine("  AssessedPrefabRegistry: <none in the open scene> (Fix adds it and " +
                              "fills it with the two prefabs above)");
            return;
        }

        SerializedProperty list = ArrayOn(registry, RegistryField);
        if (list == null)
        {
            report.AppendLine($"  AssessedPrefabRegistry on '{HierarchyPath(registry.transform)}' has " +
                              $"no array field '{RegistryField}' - renamed, and this tool binds by name.");
            return;
        }

        List<string> names = new List<string>();
        for (int i = 0; i < list.arraySize; i++)
        {
            names.Add(Obj(list.GetArrayElementAtIndex(i).objectReferenceValue));
        }

        report.AppendLine($"  AssessedPrefabRegistry on '{HierarchyPath(registry.transform)}': " +
                          $"{RegistryField}={list.arraySize} ({string.Join(", ", names)})");
    }

    private static int AppendMusicReport(StringBuilder report)
    {
        MusicStateDefinition[] definitions = FindMusicDefinitions();
        if (definitions.Length == 0)
        {
            report.AppendLine("MUSIC STATES: <no MusicStateDefinition asset in the project>");
            return 0;
        }

        HashSet<MusicStateDefinition> referenced = ReferencedByScene();

        int rows = 0;
        foreach (MusicStateDefinition definition in definitions)
        {
            string assetPath = AssetDatabase.GetAssetPath(definition);
            SerializedProperty loops = new SerializedObject(definition).FindProperty(LoopSegmentsField);

            if (loops == null || !loops.isArray)
            {
                report.AppendLine($"MUSIC STATE {assetPath}: no array field '{LoopSegmentsField}' - " +
                                  "renamed, and this tool binds by name.");
                continue;
            }

            string reach = referenced.Contains(definition)
                ? "referenced by the scene's MusicManager, so its rows reach the report"
                : "⚠ NOT referenced by any MusicManager in the open scene, so its rows do NOT " +
                  "reach the report";

            if (loops.arraySize == 0)
            {
                report.AppendLine($"MUSIC STATE {assetPath}: {LoopSegmentsField}=0  ⚠ THERE IS NO " +
                                  "LOOP SEGMENT AT ALL (only intro/outro, both with stems: []). Fix " +
                                  "creates loopSegments[0] AND its one empty stem - an ASSET WRITE. " +
                                  $"({reach})");
                rows++;
                continue;
            }

            SerializedProperty stems =
                loops.GetArrayElementAtIndex(0).FindPropertyRelative(StemsField);
            if (stems == null || !stems.isArray)
            {
                report.AppendLine($"MUSIC STATE {assetPath}: loopSegments[0] has no array field " +
                                  $"'{StemsField}' - renamed, and this tool binds by name.");
                continue;
            }

            report.AppendLine($"MUSIC STATE {assetPath}: {LoopSegmentsField}={loops.arraySize}, " +
                              $"loopSegments[0].{StemsField}={stems.arraySize}" +
                              (stems.arraySize == 0
                                  ? "  ⚠ EMPTY = NO REPORT ROW (Fix sizes it to 1 - an ASSET WRITE)"
                                  : " (already sized - left exactly as it is)") +
                              $"  ({reach})");

            rows += stems.arraySize == 0 ? 1 : 0;
        }

        return rows;
    }

    // ==========================================================================
    //  2. Fix
    // ==========================================================================

    /// <summary>
    /// Sizes every empty assessed array to exactly ONE NULL element, puts the registry on the
    /// host, and writes the three music stems. Skips whatever is already right, so a second
    /// run changes nothing and an interrupted run can simply be repeated.
    /// </summary>
    [MenuItem("Tools/Audio/Fix Slot Surface", priority = 1501)]
    private static void FixSlotSurface()
    {
        List<string> written = new List<string>();   // ⚠ asset and prefab writes - NOT undoable
        List<string> fields = new List<string>();    // scene writes - undoable
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- 1: headshotSounds on every enemy profile ----
        FixHeadshotSounds(fields, skipped, notes);

        // ---- 2: dialogueLines on the mushroom profile ----
        FixMushroomDialogue(fields, skipped, notes);

        // ---- 3: phaseCrackSounds on the boss prefab (a PREFAB WRITE) ----
        FixBossPhaseCracks(written, skipped, notes);

        // ---- 5: one empty loop stem per music state (ASSET WRITES) ----
        FixMusicStems(written, skipped, notes);

        // Flushed before the scene write below, the way EnemyRoleSetupTools does it: the
        // registry references prefab assets, and an asset still only in memory is one a
        // crashed editor would take the reference's target away with.
        if (written.Count > 0) AssetDatabase.SaveAssets();

        // ---- 4: the registry on the Audio Managers object ----
        FixRegistry(fields, skipped, notes);

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Fix Slot Surface finished.");

        if (written.Count == 0 && fields.Count == 0)
        {
            log.AppendLine("NOTHING TO DO - every assessed array already has its element, the " +
                           "registry is already in place, and no file was rewritten.");
        }

        log.AppendLine($"⚠ ASSETS AND PREFABS WRITTEN ({written.Count}) - these went straight to " +
                       "disk and are NOT undoable. Ctrl+Z will not bring them back; " +
                       "`git checkout -- <path>` is the way back:");
        AppendList(log, written, "  <none - every asset and prefab was already correct>");

        log.AppendLine($"SCENE FIELDS SET ({fields.Count}):");
        AppendList(log, fields, "  <none - the scene was already sized>");

        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine("Every new element is left EMPTY on purpose - they are the students' to " +
                       "fill, and each null element is exactly one MISSING row in the Ctrl+G " +
                       "report. A clip written here would be a placeholder nobody asked for.");

        log.AppendLine($"The SCENE changes are Undo-recorded as one step, '{UndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or they are lost. The prefab and asset " +
                       "writes above are already on disk. Then run Tools/Audio/Report Slot " +
                       "Surface again and diff it against the run before this one.");

        Debug.Log(log.ToString().TrimEnd());
    }

    // ---- Step 1: the five headshot arrays ------------------------------------

    /// <summary>
    /// Every profile the manager carries, addressed by its enemyName rather than by index or
    /// by scene line, so a reordered array still lands on the right rows. An array that
    /// already has elements is never grown: those are the students' clips, or a previous
    /// run's row, and either way growing it adds a row nobody asked for.
    /// </summary>
    private static void FixHeadshotSounds(List<string> fields, List<string> skipped, List<string> notes)
    {
        EnemyAudioManager manager = Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning($"{LogPrefix} No EnemyAudioManager in the open scene, so none of the " +
                             "five headshot rows were added. Open the level and re-run.");
            notes.Add("⚠ No EnemyAudioManager in the open scene: the five headshotSounds rows were " +
                      "NOT added.");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(EnemyProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogWarning($"{LogPrefix} EnemyAudioManager has no array field " +
                             $"'{EnemyProfilesField}' - it has been renamed, and this tool binds to " +
                             "it by name. No profile was changed.");
            return;
        }

        string managerPath = HierarchyPath(manager.transform);
        bool changed = false;

        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            changed |= SizeToOne(profile, HeadshotField, managerPath,
                                 $"{EnemyProfilesField}[{i}] '{NameOf(profile, "enemyName")}'",
                                 fields, skipped, notes);
        }

        if (changed)
        {
            serialized.ApplyModifiedProperties();   // Undo-recorded, unlike the asset writes
            EditorUtility.SetDirty(manager);
        }
    }

    // ---- Step 2: the mushroom's dialogue -------------------------------------

    private static void FixMushroomDialogue(List<string> fields, List<string> skipped, List<string> notes)
    {
        InteractionAudioManager manager =
            Object.FindFirstObjectByType<InteractionAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning($"{LogPrefix} No InteractionAudioManager in the open scene, so the " +
                             "mushroom dialogue row was not added. Open the level and re-run.");
            notes.Add("⚠ No InteractionAudioManager in the open scene: the dialogueLines row was " +
                      "NOT added.");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(InteractionProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogWarning($"{LogPrefix} InteractionAudioManager has no array field " +
                             $"'{InteractionProfilesField}' - it has been renamed, and this tool " +
                             "binds to it by name. No profile was changed.");
            return;
        }

        int index = FindByName(profiles, "interactionName", MushroomProfileName);
        if (index < 0)
        {
            notes.Add($"⚠ No '{MushroomProfileName}' profile on the InteractionAudioManager, so its " +
                      "dialogue row is missing from the report. MushroomTripInteraction plays " +
                      "dialogueLines from that profile by name.");
            return;
        }

        if (SizeToOne(profiles.GetArrayElementAtIndex(index), DialogueField,
                      HierarchyPath(manager.transform),
                      $"{InteractionProfilesField}[{index}] '{MushroomProfileName}'",
                      fields, skipped, notes))
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
        }
    }

    // ---- Step 3: the boss's phase cracks (a PREFAB WRITE) --------------------

    /// <summary>
    /// Through LoadPrefabContents / SaveAsPrefabAsset / UnloadPrefabContents, never through a
    /// scene instance: the boss has NO scene instance at all (the boss wave instantiates the
    /// asset), which is the whole reason its slots never reached the report.
    /// </summary>
    private static void FixBossPhaseCracks(List<string> written, List<string> skipped, List<string> notes)
    {
        string path = FindPrefabWith<BossZombieController>();
        if (path == null)
        {
            notes.Add("⚠ No prefab in the project carries BossZombieController, so phaseCrackSounds " +
                      "was not sized and the boss's phase-crack row is missing.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            notes.Add($"⚠ Could not open {path} for editing. It was NOT rewritten.");
            return;
        }

        try
        {
            BossZombieController boss = root.GetComponentInChildren<BossZombieController>(true);
            if (boss == null)
            {
                notes.Add($"⚠ {path} carries no BossZombieController once opened. NOT rewritten.");
                return;
            }

            SerializedObject serialized = new SerializedObject(boss);
            SerializedProperty array = serialized.FindProperty(PhaseCrackField);

            if (array == null || !array.isArray)
            {
                notes.Add($"⚠ BossZombieController has no array field '{PhaseCrackField}' - it has " +
                          $"been renamed, and this tool binds to it by name. {path} NOT rewritten.");
                return;
            }

            if (array.arraySize > 0)
            {
                skipped.Add($"{path} {PhaseCrackField} already has {array.arraySize} element(s) - " +
                            "left exactly as it is, NOT rewritten");
                return;
            }

            array.arraySize = 1;
            array.GetArrayElementAtIndex(0).objectReferenceValue = null;

            // Without Undo: the target lives in the prefab-contents preview scene.
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
            {
                notes.Add($"⚠ Could not write {path}. The change was made in memory and thrown away.");
                return;
            }

            written.Add($"{path}: {PhaseCrackField} 0 -> 1 element, [0] = <none>. " +
                        $"REVERT: git checkout -- \"{path}\"");
        }
        finally
        {
            // Must happen whether the save succeeded, refused or threw, or the preview scene is
            // left open and the next LoadPrefabContents on this path misbehaves.
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- Step 4: the registry ------------------------------------------------

    /// <summary>
    /// The one door through the scanner's Unity-object boundary. It lists prefabs BY HAND, on
    /// purpose - following every prefab reference would drag the whole asset graph into the
    /// report. The two prefabs are found by the COMPONENT they carry, never by a hard-coded
    /// path: the crossbow's CrossBow/Crossbow lesson.
    /// </summary>
    private static void FixRegistry(List<string> fields, List<string> skipped, List<string> notes)
    {
        GameObject host = FindHost();
        if (host == null)
        {
            Debug.LogWarning($"{LogPrefix} No '{HostName}' object and no EnemyAudioManager in the " +
                             "open scene, so the registry was not added and the boss and rod rows " +
                             "will not appear. Open the level and re-run.");
            notes.Add($"⚠ No host for the registry: the boss's and the rod's rows were NOT added.");
            return;
        }

        List<GameObject> wanted = new List<GameObject>();
        AddWantedPrefab<BossZombieController>(wanted, notes, "the boss's five slots");
        AddWantedPrefab<FishingRod>(wanted, notes, "the rod's six fish rows");

        AssessedPrefabRegistry registry =
            Object.FindFirstObjectByType<AssessedPrefabRegistry>(FindObjectsInactive.Include);

        if (registry == null)
        {
            registry = Undo.AddComponent<AssessedPrefabRegistry>(host);
            if (registry == null)
            {
                notes.Add($"⚠ AddComponent(AssessedPrefabRegistry) returned null on " +
                          $"'{HierarchyPath(host.transform)}'.");
                return;
            }

            fields.Add($"{HierarchyPath(host.transform)} + AssessedPrefabRegistry");
        }
        else
        {
            skipped.Add($"{HierarchyPath(registry.transform)} already carries AssessedPrefabRegistry");

            if (registry.gameObject != host)
            {
                notes.Add($"The registry is on '{HierarchyPath(registry.transform)}', not on " +
                          $"'{HostName}'. Left where it is - the scanner finds it by type, so its " +
                          "host does not matter.");
            }
        }

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty(RegistryField);
        if (list == null || !list.isArray)
        {
            notes.Add($"⚠ AssessedPrefabRegistry has no array field '{RegistryField}' - it has been " +
                      "renamed, and this tool binds to it by name. The list was NOT filled.");
            return;
        }

        // Only what is MISSING is appended. An entry somebody added by hand is an authored
        // choice, and clobbering it on a re-run is exactly what idempotence rules out.
        bool changed = false;
        foreach (GameObject prefab in wanted)
        {
            if (Contains(list, prefab))
            {
                skipped.Add($"{HierarchyPath(registry.transform)} {RegistryField} already contains " +
                            $"'{prefab.name}'");
                continue;
            }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = prefab;
            changed = true;

            fields.Add($"{HierarchyPath(registry.transform)} {RegistryField} += " +
                       $"'{AssetDatabase.GetAssetPath(prefab)}'");
        }

        if (changed)
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(registry);
        }
    }

    private static void AddWantedPrefab<T>(List<GameObject> wanted, List<string> notes, string why)
        where T : Component
    {
        string path = FindPrefabWith<T>();
        if (path == null)
        {
            notes.Add($"⚠ No prefab in the project carries {typeof(T).Name}, so {why} will not " +
                      "appear in the report.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            notes.Add($"⚠ Could not load {path}, so {why} will not appear in the report.");
            return;
        }

        wanted.Add(prefab);
    }

    // ---- Step 5: the music stems (ASSET WRITES) ------------------------------

    /// <summary>
    /// One empty stem in each state's LOOP segment - the owner's 2026-09-08 decision: the
    /// stems were emptied deliberately for the handout (21bfca0) and they COUNT.
    /// </summary>
    /// <remarks>
    /// ⚠ Read the file header before changing this. `loopSegments` is EMPTY on all three
    /// assets, so the loop segment is created here as well as its stem, and that flips
    /// MusicStateDefinition.OnValidate from an early return into one warning per asset.
    /// Warnings, not errors, and true ones. Playback is unchanged - a null-clip stem is
    /// treated by LayeredStatePlayer exactly as no stem, verified site by site in the header.
    /// </remarks>
    private static void FixMusicStems(List<string> written, List<string> skipped, List<string> notes)
    {
        MusicStateDefinition[] definitions = FindMusicDefinitions();
        if (definitions.Length == 0)
        {
            notes.Add("⚠ No MusicStateDefinition asset in the project, so the three music rows were " +
                      "not added.");
            return;
        }

        bool anyLoopCreated = false;

        foreach (MusicStateDefinition definition in definitions)
        {
            string assetPath = AssetDatabase.GetAssetPath(definition);

            SerializedObject serialized = new SerializedObject(definition);
            SerializedProperty loops = serialized.FindProperty(LoopSegmentsField);
            if (loops == null || !loops.isArray)
            {
                notes.Add($"⚠ {assetPath}: MusicStateDefinition has no array field " +
                          $"'{LoopSegmentsField}' - renamed, and this tool binds by name. NOT written.");
                continue;
            }

            List<string> changes = new List<string>();

            if (loops.arraySize == 0)
            {
                loops.arraySize = 1;
                InitialiseSegment(loops.GetArrayElementAtIndex(0), notes, assetPath);
                changes.Add($"{LoopSegmentsField} 0 -> 1 segment ('{SegmentDefaultName}', " +
                            $"{SegmentDefaultBars} bars)");
                anyLoopCreated = true;
            }

            SerializedProperty stems =
                loops.GetArrayElementAtIndex(0).FindPropertyRelative(StemsField);
            if (stems == null || !stems.isArray)
            {
                notes.Add($"⚠ {assetPath}: loopSegments[0] has no array field '{StemsField}' - " +
                          "renamed, and this tool binds by name. NOT written.");
                continue;
            }

            if (stems.arraySize > 0)
            {
                if (changes.Count == 0)
                {
                    skipped.Add($"{assetPath} loopSegments[0].{StemsField} already has " +
                                $"{stems.arraySize} element(s) - left exactly as it is, NOT rewritten");
                    continue;
                }
            }
            else
            {
                stems.arraySize = 1;
                InitialiseStem(stems.GetArrayElementAtIndex(0), notes, assetPath);
                changes.Add($"loopSegments[0].{StemsField} 0 -> 1 stem " +
                            $"('{StemDefaultName}', clip = <none>, minIntensity " +
                            $"{Num(StemDefaultMinIntensity)}, fadeRange {Num(StemDefaultFadeRange)}, " +
                            $"volume {Num(StemDefaultVolume)}, fadeIn {Num(StemDefaultFadeIn)}s, " +
                            $"fadeOut {Num(StemDefaultFadeOut)}s)");
            }

            // Without Undo: an asset write is not undoable, and pretending otherwise in the
            // undo stack is worse than saying so in the log.
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);

            written.Add($"{assetPath}: {string.Join("; ", changes)}. " +
                        $"REVERT: git checkout -- \"{assetPath}\"");
        }

        if (anyLoopCreated)
        {
            notes.Add("⚠ EXPECT THREE NEW CONSOLE WARNINGS. MusicStateDefinition.OnValidate " +
                      "returns early while loopSegments is empty; now that each asset has a loop " +
                      "segment it runs ValidateContent, which finds no usable loops (a stem with a " +
                      "null clip is not content) and logs \"'<state>' has no usable loop segments; " +
                      "the state cannot play\" per asset, on import and on every inspector touch. " +
                      "They are WARNINGS, not errors, they are TRUE, and they stop by themselves " +
                      "the moment a student drops a clip into the stem. PLAYBACK IS UNCHANGED: " +
                      "LayeredStatePlayer treats a null-clip stem exactly as no stem at every " +
                      "site (slot count, preload, scheduling, gates, volumes) - see this file's " +
                      "header for the line-by-line.");
        }
    }

    /// <summary>
    /// The MusicSegment defaults, written explicitly. Growing a SerializedProperty array
    /// copies the previous last element and there is none here, so a future reader should not
    /// have to know what Unity does with an empty one to be sure this segment is blank.
    /// </summary>
    private static void InitialiseSegment(SerializedProperty segment, List<string> notes, string assetPath)
    {
        SerializedProperty name = segment.FindPropertyRelative("segmentName");
        SerializedProperty bars = segment.FindPropertyRelative("lengthInBars");
        SerializedProperty stems = segment.FindPropertyRelative(StemsField);

        if (name != null) name.stringValue = SegmentDefaultName;
        if (bars != null) bars.intValue = SegmentDefaultBars;
        if (stems != null && stems.isArray) stems.arraySize = 0;

        if (name == null || bars == null)
        {
            notes.Add($"⚠ {assetPath}: MusicSegment is missing 'segmentName' or 'lengthInBars' - " +
                      "renamed, and this tool binds by name. The new segment kept Unity's values.");
        }
    }

    /// <summary>
    /// The MusicStem defaults, read off MusicStateDefinition.cs rather than invented. The clip
    /// stays null on purpose: a null clip is exactly one MISSING row for the student.
    /// </summary>
    private static void InitialiseStem(SerializedProperty stem, List<string> notes, string assetPath)
    {
        bool complete = true;
        complete &= SetString(stem, "stemName", StemDefaultName);
        complete &= SetObject(stem, "clip", null);
        complete &= SetFloat(stem, "minIntensity", StemDefaultMinIntensity);
        complete &= SetFloat(stem, "fadeRange", StemDefaultFadeRange);
        complete &= SetFloat(stem, "volume", StemDefaultVolume);
        complete &= SetFloat(stem, "fadeInTime", StemDefaultFadeIn);
        complete &= SetFloat(stem, "fadeOutTime", StemDefaultFadeOut);

        if (!complete)
        {
            notes.Add($"⚠ {assetPath}: MusicStem is missing one of stemName/clip/minIntensity/" +
                      "fadeRange/volume/fadeInTime/fadeOutTime - renamed, and this tool binds by " +
                      "name. The new stem kept Unity's values for whichever was missing.");
        }
    }

    // ==========================================================================
    //  Shared helpers - the same shape as the other setup tools here
    // ==========================================================================

    /// <summary>
    /// Sizes one clip array to exactly one EMPTY element - one MISSING row in the report.
    /// Never touches an array that already has elements: those are the students' clips, or a
    /// previous run's row, and either way growing it would add a row nobody asked for.
    /// </summary>
    private static bool SizeToOne(SerializedProperty profile, string field, string hostPath,
                                  string where, List<string> fields, List<string> skipped,
                                  List<string> notes)
    {
        SerializedProperty array = profile.FindPropertyRelative(field);
        if (array == null || !array.isArray)
        {
            notes.Add($"⚠ {where} has no array field '{field}' - it has been renamed, and this tool " +
                      "binds to it by name. NOT SET, and there is no report row for it.");
            return false;
        }

        if (array.arraySize > 0)
        {
            skipped.Add($"{hostPath} {where} {field} already has {array.arraySize} element(s) - " +
                        "left exactly as it is");
            return false;
        }

        array.arraySize = 1;

        // Explicit, not assumed: growing a SerializedProperty array copies the previous last
        // element, and there is none here - but a future reader should not have to know that
        // to be sure the new row is empty.
        array.GetArrayElementAtIndex(0).objectReferenceValue = null;

        fields.Add($"{hostPath} {where} {field} 0 -> 1 element, [0] = <none> (an EMPTY array is " +
                   "NO report row at all; one null element is one MISSING row for the student)");
        return true;
    }

    /// <summary>
    /// The registry's host. By name first, because that is what the work order names; the
    /// EnemyAudioManager's own object is the fallback, since that IS the Audio Managers object
    /// in this level and a renamed host should not silently cost the report ten rows.
    /// </summary>
    private static GameObject FindHost()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == HostName) return root;
            }
        }

        EnemyAudioManager manager = Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        return manager == null ? null : manager.gameObject;
    }

    /// <summary>
    /// The single prefab asset carrying a component, found by TYPE. Never a hard-coded path:
    /// the crossbow shipped as both `CrossBow` and `Crossbow`, and a path constant was how
    /// that stayed invisible. More than one match is reported rather than guessed at.
    /// </summary>
    private static string FindPrefabWith<T>() where T : Component
    {
        string found = null;
        int matches = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponentInChildren<T>(true) == null) continue;

            matches++;
            if (found == null) found = path;
        }

        if (matches > 1)
        {
            Debug.LogWarning($"{LogPrefix} {matches} prefabs carry {typeof(T).Name}; using " +
                             $"'{found}'. Check that is the one the game spawns.");
        }

        return found;
    }

    /// <summary>Every MusicStateDefinition in the project, by type rather than by path.</summary>
    private static MusicStateDefinition[] FindMusicDefinitions()
    {
        List<MusicStateDefinition> found = new List<MusicStateDefinition>();

        foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(MusicStateDefinition)))
        {
            var definition = AssetDatabase.LoadAssetAtPath<MusicStateDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null) found.Add(definition);
        }

        // Stable order, so two report runs diff cleanly.
        found.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a),
                                                   AssetDatabase.GetAssetPath(b)));
        return found.ToArray();
    }

    /// <summary>
    /// The definitions the open scene's MusicManagers actually reference - the only ones whose
    /// rows reach the report, since the scanner follows MusicStateDefinition from a manager
    /// field and from nowhere else.
    /// </summary>
    private static HashSet<MusicStateDefinition> ReferencedByScene()
    {
        var referenced = new HashSet<MusicStateDefinition>();

        foreach (MusicManager manager in Object.FindObjectsByType<MusicManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedProperty list = new SerializedObject(manager).FindProperty("stateDefinitions");
            if (list == null || !list.isArray) continue;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is MusicStateDefinition definition)
                {
                    referenced.Add(definition);
                }
            }
        }

        return referenced;
    }

    private static SerializedProperty ArrayOn(Object target, string field)
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(field);
        return property != null && property.isArray ? property : null;
    }

    /// <summary>
    /// Trimmed and case-insensitive, the same way AudioProfileKeys resolves a lookup, so this
    /// finds the profile the runtime would find rather than a differently-cased near miss.
    /// </summary>
    private static int FindByName(SerializedProperty profiles, string nameField, string wanted)
    {
        for (int i = 0; i < profiles.arraySize; i++)
        {
            string name = NameOf(profiles.GetArrayElementAtIndex(i), nameField);
            if (string.Equals(name, wanted.Trim(), System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static string NameOf(SerializedProperty profile, string nameField)
    {
        SerializedProperty name = profile.FindPropertyRelative(nameField);
        return name == null ? $"<no {nameField}>" : name.stringValue.Trim();
    }

    private static bool Contains(SerializedProperty list, Object value)
    {
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == value) return true;
        }
        return false;
    }

    private static bool SetString(SerializedProperty entry, string field, string value)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p == null) return false;
        p.stringValue = value;
        return true;
    }

    private static bool SetFloat(SerializedProperty entry, string field, float value)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p == null) return false;
        p.floatValue = value;
        return true;
    }

    private static bool SetObject(SerializedProperty entry, string field, Object value)
    {
        SerializedProperty p = entry.FindPropertyRelative(field);
        if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) return false;
        p.objectReferenceValue = value;
        return true;
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

    /// <summary>Hierarchy path, the way this project's logs and work orders write scene paths.</summary>
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

    /// <summary>Invariant culture, so the report diffs the same on a comma-decimal machine.</summary>
    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Obj(Object value) => value == null ? "<none>" : $"'{value.name}'";
}
