using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * EnemyRoleSetupTools.cs
 *
 * Purpose: Puts the four zombie ROLES into the level. The role component on each enemy
 *          prefab, the retunes those roles need, the pack VFX they spawn, the enemyRole
 *          on each EnemyAudioManager profile, the SIX new assessed clip rows the students
 *          are graded on, and the Demon's spit projectile with its pool.
 * Menu:    Tools/Enemies/Report Enemy Roles   (read only)
 *          Tools/Enemies/Add Enemy Roles      (idempotent - a second run changes nothing)
 * Used by: the integrator, driven from the Unity CLI. The owner saves the scene afterwards.
 *
 * Menu items rather than hand edits of the .unity/.prefab files, for this project's standing
 * reason: the files on disk are routinely older than what is open in the editor, so a hand
 * edit would be lost the next time the owner saves. Same reason as MinigunSetupTools and
 * GrenadeSetupTools next door.
 *
 * ⚠ NO DIALOGS anywhere in this file. Both items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block it with a modal window nobody can see,
 * let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Run Report before and after Add: it prints one block with a fixed line order and
 * invariant-culture numbers, so the two runs diff cleanly and the diff IS the record of what
 * Add did.
 *
 * ---------------------------------------------------------------------------------------
 * TWO KINDS OF WRITE, AND ONLY ONE OF THEM IS UNDOABLE
 * ---------------------------------------------------------------------------------------
 * PREFAB and ASSET writes (the four enemy prefabs, the spit prefab, its material) go
 * straight to disk through PrefabUtility.SaveAsPrefabAsset / AssetDatabase.CreateAsset.
 * ⚠ Ctrl+Z does NOT bring them back - `git checkout -- <path>` is the way back, and the log
 * names every path it wrote. SCENE writes (the audio profiles, the spit pool) are
 * Undo-recorded as one step and need the owner to SAVE THE SCENE.
 *
 * ⚠ The boss is NEVER touched. Boss - Rock Golem.prefab keeps its BossZombieController and
 * gets no role; RefuseIfBoss() below is a hard guard on the prefab-editing path rather than
 * a comment, because "add a role to every enemy prefab" is exactly the shortcut somebody
 * takes later. Its PROFILE is still set to EnemyRole.None - that is a scene write on the
 * audio manager, and None is what hides all six role slots from its report rows.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE STEP THAT IS EASY TO MISS: AN EMPTY ARRAY IS NOT A REPORT ROW
 * ---------------------------------------------------------------------------------------
 * AudioProgressScanner.WalkValue records one row PER ARRAY ELEMENT (it walks `value is
 * IList` and calls RecordSlot per element), so an AudioClip[] of size 0 produces NO row at
 * all - not a MISSING row, nothing. The live proof is `headshotSounds: []` on all five
 * profiles: an assessed, played slot the Ctrl+G report has never once listed.
 *
 * So each profile's OWN role arrays are sized to 1 with a null element - one MISSING row
 * each, six in total, which is the number this feature is worth. Only the arrays that match
 * that profile's role: sizing all six on all five profiles would put THIRTY rows in the
 * report instead of six, and IAudioSlotFilter would hide most of them anyway, so they would
 * be sounds nobody is asked to author sitting in slots nobody can see. A clip is never
 * written into them - they are the students' to fill.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE PACK'S IMPACT EFFECTS LOOP
 * ---------------------------------------------------------------------------------------
 * Measured in the prefab YAML, not assumed: FX_Impact_Metal_01 (2 emitters), FX_Dust_Small_01
 * (1) and FX_ShardMagic_Explosion_01 (11) are all `looping: 1`. The role's lifetime field is
 * therefore how long the effect SPRAYS, not "long enough to outlast a burst" - a generous
 * number gives the Butcher a welder's fountain rather than a clang. BulletImpactManager
 * already lives with this by destroying after 1 s.
 *
 * FX_Impact_Water_01 is the ONE non-looping impact in the pack (both its emitters are
 * `looping: 0`), which is why it, and not the ShardMagic explosion its tooltip suggests, is
 * wired into the spit: a wet splash is the right read for a glob of bile, and a one-shot is
 * the right shape for something spawned detached at an impact point and destroyed on a timer.
 *
 * ⚠ Binds to serialized fields by literal name: on ZombieAI "maxHealth" and "moveSpeed"; on
 * ZombieRunner "dustPuff", on ZombieScreamer "rallyRing", on ZombieShield "blockSparks", on
 * ZombieSpit "impactFx"; on ZombieSpitPool "spitPrefab"; and on EnemyAudioManager the array
 * "enemyProfiles" plus the profile fields "enemyName", "enemyId", "enemyRole" and the six
 * role clip arrays. Renaming any of those compiles clean and breaks this silently - see
 * CODE_STRUCTURE's editor-tooling warning.
 */

public static class EnemyRoleSetupTools
{
    private const string LogPrefix = "[EnemyRoleSetupTools]";
    private const string UndoLabel = "Add Enemy Roles";

    // ---- Asset paths ---------------------------------------------------------

    private const string EnemyPrefabFolder = "Assets/Inbox/Prefabs/Enemies";

    /// <summary>
    /// ⚠ Never edited by this tool. Named only so the report can say so out loud and so
    /// RefuseIfBoss has something to compare against.
    /// </summary>
    private const string BossPrefabPath = EnemyPrefabFolder + "/Boss - Rock Golem.prefab";

    private const string ParticleFolder =
        "Assets/Inbox/Low Poly Models/Nature/Particles/Prefabs/";

    /// <summary>The shield's clang, seen. Two emitters, both LOOPING, 1 s duration.</summary>
    private const string BlockSparksFx = "FX_Impact_Metal_01";

    /// <summary>The runner's launch, seen. One emitter, LOOPING, 5 s duration.</summary>
    private const string DustPuffFx = "FX_Dust_Small_01";

    /// <summary>
    /// The spit's landing. The only NON-looping impact in the pack - see the file header for
    /// why that matters more than matching the ShardMagic explosion the tooltip suggests.
    /// </summary>
    private const string SpitImpactFx = "FX_Impact_Water_01";

    private const string SpitPrefabPath = EnemyPrefabFolder + "/ZombieSpit.prefab";
    private const string SpitMaterialPath = EnemyPrefabFolder + "/ZombieSpit_Bile.mat";

    /// <summary>
    /// A lumpy little rock reads as a glob far better than a perfect sphere does, and it is
    /// already in the pack at the right poly count for something eight of which are in the
    /// air at once. A primitive sphere is the fallback if the pack moves.
    /// </summary>
    private const string SpitModelPath =
        "Assets/Inbox/Low Poly Models/Nature/Models/SM_Rock_Small_01.fbx";

    private const string SpitModelChildName = "ZombieSpit_Model";

    // ---- Scene paths ---------------------------------------------------------

    /// <summary>Where the scene's singletons live, beside ArrowPool and the managers.</summary>
    private const string HostPath = "Game Management";

    private const string SpitPoolName = "Zombie Spit Pool";

    private const string ProfilesField = "enemyProfiles";

    // ---- The numbers ---------------------------------------------------------

    /// <summary>
    /// Longest axis of the spit in WORLD metres. Small enough to read as a glob rather than
    /// as a thrown rock, big enough to see coming across the 12 m the Demon fires from -
    /// the whole point of the lob is that it is dodgeable, and a projectile nobody sees is
    /// not dodgeable, it is unfair.
    /// </summary>
    private const float SpitLength = 0.25f;

    /// <summary>
    /// Half the visual, so the glob lands where it looks like it lands. ⚠ NON-TRIGGER and
    /// small on purpose: ZombieSpit.OnCollisionEnter is what splashes it, and a trigger
    /// raises OnTriggerEnter instead and the spit would fly through the level to its
    /// lifetime. ZombieSpit.Launch logs a warning if it finds no Collider at all.
    /// </summary>
    private const float SpitColliderRadius = 0.12f;

    /// <summary>
    /// Bile. Saturated enough to read against terrain at 12 m without being a lit-up
    /// gameplay marker; the students and the owner can retune it in one .mat.
    /// </summary>
    private static readonly Color SpitColour = new Color(0.36f, 0.62f, 0.13f, 1f);

    // ==========================================================================
    //  The table - one row per role, read by BOTH the report and the apply
    // ==========================================================================

    /// <summary>
    /// One prefab retune, with the value it is expected to be leaving so the log reads as a
    /// diff rather than as an assertion. Applied only when the number actually differs.
    /// </summary>
    private sealed class StatChange
    {
        public readonly string Field;
        public readonly float Expected;
        public readonly float Value;
        public readonly string Why;

        public StatChange(string field, float expected, float value, string why)
        {
            Field = field;
            Expected = expected;
            Value = value;
            Why = why;
        }
    }

    /// <summary>
    /// Everything this tool knows about one role, in one place, so the report and the apply
    /// can never disagree about which prefab carries which component or which profile is
    /// asked for which sounds.
    /// </summary>
    private sealed class RoleSetup
    {
        public readonly string PrefabName;
        public readonly System.Type RoleType;
        public readonly EnemyRole ProfileRole;
        public readonly EnemyId ProfileId;
        public readonly string ProfileName;

        /// <summary>The serialized GameObject field on the role that holds its effect.</summary>
        public readonly string VfxField;

        /// <summary>The pack prefab that goes in it - null means "deliberately empty".</summary>
        public readonly string VfxPrefabName;

        /// <summary>Logged on every run, whether the field was written or left.</summary>
        public readonly string VfxNote;

        /// <summary>The profile's OWN role arrays. See the header: size 0 is no report row.</summary>
        public readonly string[] RoleClipArrays;

        public readonly StatChange[] Stats;

        public RoleSetup(string prefabName, System.Type roleType, EnemyRole profileRole,
                         EnemyId profileId, string profileName, string vfxField,
                         string vfxPrefabName, string vfxNote, string[] roleClipArrays,
                         StatChange[] stats)
        {
            PrefabName = prefabName;
            RoleType = roleType;
            ProfileRole = profileRole;
            ProfileId = profileId;
            ProfileName = profileName;
            VfxField = vfxField;
            VfxPrefabName = vfxPrefabName;
            VfxNote = vfxNote;
            RoleClipArrays = roleClipArrays;
            Stats = stats;
        }

        public string PrefabPath => EnemyPrefabFolder + "/" + PrefabName + ".prefab";
    }

    /// <summary>
    /// The four roles. The Ghoul's prefab is spelled `Choul` and its profile key is `Choul`
    /// too - its npcName is "Ghoul" and that is the only place the other spelling is right.
    /// </summary>
    private static readonly RoleSetup[] Roles =
    {
        new RoleSetup(
            prefabName: "Choul",
            roleType: typeof(ZombieRunner),
            profileRole: EnemyRole.Runner,
            profileId: EnemyId.Choul,
            profileName: "Choul",
            vfxField: "dustPuff",
            vfxPrefabName: DustPuffFx,
            vfxNote: "the puff at the feet on the frame the burst starts",
            roleClipArrays: new[] { "lungeSounds" },
            stats: new[]
            {
                new StatChange("moveSpeed", 7f, 4.5f,
                    "the Ghoul's 7 becomes a BURST rather than a walk - ZombieRunner " +
                    "multiplies this by lungeSpeedScale 2.2 for 2 s, so 4.5 x 2.2 = 9.9 m/s " +
                    "in the lunge against a walk of 4.5. Left at 7 the sprint would be 15.4 " +
                    "and the walk would already be faster than the player")
            }),

        new RoleSetup(
            prefabName: "Butcher",
            roleType: typeof(ZombieShield),
            profileRole: EnemyRole.Shield,
            profileId: EnemyId.Butcher,
            profileName: "Butcher",
            vfxField: "blockSparks",
            vfxPrefabName: BlockSparksFx,
            vfxNote: "sparks at the hit point, thrown back the way the round came",
            roleClipArrays: new[] { "blockSounds" },
            stats: new[]
            {
                new StatChange("maxHealth", 100f, 150f,
                    "a shield that dies in the same number of rounds as everything else is " +
                    "a skin, not a role - and the front arc only pays off if there is time " +
                    "to notice it and flank"),
                new StatChange("moveSpeed", 3.5f, 3f,
                    "a heavy walks like one, and the flank the shield asks for needs to be " +
                    "walkable")
            }),

        new RoleSetup(
            prefabName: "Demon",
            roleType: typeof(ZombieSpitter),
            profileRole: EnemyRole.Spitter,
            profileId: EnemyId.Demon,
            profileName: "Demon",

            // The spitter's own effect is on the PROJECTILE, not on the role: the splash
            // happens where the spit lands, metres from the Demon. It is wired into
            // ZombieSpit.prefab in step 4 instead.
            vfxField: null,
            vfxPrefabName: null,
            vfxNote: "no VFX field on ZombieSpitter - its effect is the spit's impactFx, " +
                     "which is on ZombieSpit.prefab and is wired in step 4",
            roleClipArrays: new[] { "spitWindupSounds", "spitSounds", "spitImpactSounds" },
            stats: new StatChange[0]),

        new RoleSetup(
            prefabName: "Yokai",
            roleType: typeof(ZombieScreamer),
            profileRole: EnemyRole.Screamer,
            profileId: EnemyId.Yokai,
            profileName: "Yokai",
            vfxField: "rallyRing",

            // ⚠ Deliberately null. There is no ring-shaped one-shot in the pack: the four
            // Shard*_Explosion effects are spheres of shards and all loop, the dust effects
            // are clouds, and FX_Impact_Water_Ripple_01 is a flat puddle ring at impact
            // scale. A wrong effect at the screamer's feet would read as damage rather than
            // as a call, and the scream is carried by SOUND - which is the point of the
            // course. The field is on the component for whoever authors one later.
            vfxPrefabName: null,
            vfxNote: "LEFT EMPTY on purpose - there is no ring-shaped one-shot in the pack " +
                     "(every Shard*_Explosion loops and reads as damage, the dust effects " +
                     "are clouds, the water ripple is puddle-sized). The rally still runs, " +
                     "and the scream is carried by the SOUND, which is what this course " +
                     "grades. Drop a ring prefab into Rally Ring if one is ever authored",
            roleClipArrays: new[] { "screamSounds" },
            stats: new StatChange[0])
    };

    /// <summary>
    /// The boss's profile. It gets EnemyRole.None - a scene write - and its PREFAB is never
    /// opened. None is what makes IAudioSlotFilter hide all six role slots on it, so the
    /// Rock Golem is never graded on a sound it cannot make.
    /// </summary>
    private const EnemyId BossProfileId = EnemyId.RockGolem;
    private const string BossProfileName = "Rock Golem";

    /// <summary>All six role arrays, for the report's "who has how many rows" line.</summary>
    private static readonly string[] AllRoleClipArrays =
    {
        "lungeSounds", "blockSounds", "spitWindupSounds", "spitSounds",
        "spitImpactSounds", "screamSounds"
    };

    // ==========================================================================
    //  1. Report
    // ==========================================================================

    /// <summary>
    /// Reads and prints, changing nothing. The integrator runs it either side of Add so the
    /// before/after is a plain diff; that is also why the line order is fixed and the numbers
    /// are invariant-culture.
    /// </summary>
    [MenuItem("Tools/Enemies/Report Enemy Roles", priority = 1400)]
    private static void ReportEnemyRoles()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Enemy Roles (read only - nothing was changed).");

        foreach (RoleSetup role in Roles) AppendPrefabReport(report, role);
        AppendBossReport(report);
        AppendProfileReport(report);
        AppendSpitPrefabReport(report);
        AppendSceneReport(report);

        Debug.Log(report.ToString().TrimEnd());
    }

    private static void AppendPrefabReport(StringBuilder report, RoleSetup role)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(role.PrefabPath);
        if (prefab == null)
        {
            report.AppendLine($"PREFAB {role.PrefabPath}: ABSENT - Add Enemy Roles will skip it.");
            return;
        }

        ZombieAI ai = prefab.GetComponentInChildren<ZombieAI>(true);
        if (ai == null)
        {
            report.AppendLine($"PREFAB {role.PrefabPath}: PRESENT but carries NO ZombieAI - " +
                              "a role cannot be added to it (every role is " +
                              "[RequireComponent(typeof(ZombieAI))]).");
            return;
        }

        SerializedObject serialized = new SerializedObject(ai);
        report.AppendLine($"PREFAB {role.PrefabPath}: PRESENT ZombieAI on '{ai.gameObject.name}', " +
                          $"maxHealth={PropNum(serialized, "maxHealth")}, " +
                          $"moveSpeed={PropNum(serialized, "moveSpeed")}, " +
                          $"xpReward={PropNum(serialized, "xpReward")}, " +
                          $"detectionRange={PropNum(serialized, "detectionRange")}");

        // Every role in the level, not just the expected one: [DisallowMultipleComponent] on
        // ZombieRole means a prefab that somehow acquired the WRONG role cannot simply have
        // the right one added beside it, and that is worth seeing before Add refuses.
        ZombieRole[] present = prefab.GetComponentsInChildren<ZombieRole>(true);
        if (present.Length == 0)
        {
            report.AppendLine($"  Role component: <none> (wants {role.RoleType.Name})");
        }
        else
        {
            foreach (ZombieRole carried in present)
            {
                bool right = carried.GetType() == role.RoleType;
                report.AppendLine($"  Role component: {carried.GetType().Name} on " +
                                  $"'{carried.gameObject.name}' " +
                                  (right ? "(correct)" : $"⚠ EXPECTED {role.RoleType.Name}"));

                if (right && !string.IsNullOrEmpty(role.VfxField))
                {
                    SerializedObject roleSerialized = new SerializedObject(carried);
                    report.AppendLine($"    {role.VfxField} = {Prop(roleSerialized, role.VfxField)}");
                }
            }
        }

        foreach (StatChange stat in role.Stats)
        {
            SerializedProperty property = serialized.FindProperty(stat.Field);
            string now = property == null ? "<no such field>" : Num(property.floatValue);
            bool done = property != null && Mathf.Approximately(property.floatValue, stat.Value);
            report.AppendLine($"  Retune {stat.Field}: now {now}, wants {Num(stat.Value)} " +
                              (done ? "(already applied)" : "(NOT applied)"));
        }
    }

    private static void AppendBossReport(StringBuilder report)
    {
        GameObject boss = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        report.AppendLine(boss == null
            ? $"BOSS {BossPrefabPath}: ABSENT."
            : $"BOSS {BossPrefabPath}: PRESENT, NEVER TOUCHED by this tool - it keeps its " +
              $"BossZombieController and gets no role. Roles on it: " +
              $"{boss.GetComponentsInChildren<ZombieRole>(true).Length} (must be 0). Its " +
              $"PROFILE is still set to EnemyRole.None, which hides all six role slots.");
    }

    private static void AppendProfileReport(StringBuilder report)
    {
        EnemyAudioManager manager = Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            report.AppendLine("PROFILES: <no EnemyAudioManager in the open scene>");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            report.AppendLine($"PROFILES: EnemyAudioManager has no array field '{ProfilesField}' - " +
                              "it has been renamed, and this tool binds to it by name.");
            return;
        }

        report.AppendLine($"PROFILES on '{HierarchyPath(manager.transform)}': {profiles.arraySize} " +
                          $"({ProfileNames(profiles)})");

        int rows = 0;
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            SerializedProperty roleProperty = profile.FindPropertyRelative("enemyRole");

            StringBuilder sizes = new StringBuilder();
            foreach (string field in AllRoleClipArrays)
            {
                SerializedProperty array = profile.FindPropertyRelative(field);
                if (array == null || !array.isArray)
                {
                    sizes.Append($" {field}=<no such field>");
                    continue;
                }

                sizes.Append($" {field}={array.arraySize}");
                rows += array.arraySize;
            }

            report.AppendLine($"  [{i}] '{ProfileNameOf(profile)}' enemyRole=" +
                              (roleProperty == null
                                  ? "<no such field - Agent E's slot is not in this build>"
                                  : $"{(EnemyRole)roleProperty.intValue} ({roleProperty.intValue})") +
                              $",{sizes}");
        }

        // The number the Ctrl+G export diff is checked against. One row per ELEMENT, which
        // is why every one of the six wanted arrays has to be sized to 1 - see the header.
        report.AppendLine($"  ROLE ROWS THE PROGRESS REPORT WILL LIST: {rows} (wants 6 after " +
                          "Add Enemy Roles: 1 on Choul, 1 on Butcher, 3 on Demon, 1 on Yokai, " +
                          "0 on Rock Golem)");
    }

    private static void AppendSpitPrefabReport(StringBuilder report)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpitPrefabPath);
        if (prefab == null)
        {
            report.AppendLine($"SPIT PREFAB {SpitPrefabPath}: ABSENT");
            return;
        }

        Rigidbody body = prefab.GetComponent<Rigidbody>();
        SphereCollider sphere = prefab.GetComponent<SphereCollider>();
        ZombieSpit spit = prefab.GetComponent<ZombieSpit>();

        report.AppendLine($"SPIT PREFAB {SpitPrefabPath}: PRESENT layer={LayerName(prefab.layer)}, " +
                          $"Rigidbody={(body == null ? "<none>" : $"useGravity={body.useGravity}, isKinematic={body.isKinematic}, mass={Num(body.mass)}")}");
        report.AppendLine($"  SphereCollider: " +
                          (sphere == null ? "<none> ⚠ the spit can never land"
                                          : $"radius={Num(sphere.radius)}, isTrigger={sphere.isTrigger}" +
                                            (sphere.isTrigger ? " ⚠ MUST be false - ZombieSpit splashes from OnCollisionEnter" : "")));
        report.AppendLine($"  ZombieSpit: " +
                          (spit == null
                              ? "<component missing>"
                              : $"impactFx={Prop(new SerializedObject(spit), "impactFx")}, " +
                                $"damage={PropNum(new SerializedObject(spit), "damage")}, " +
                                $"splashRadius={PropNum(new SerializedObject(spit), "splashRadius")}"));

        Material material = AssetDatabase.LoadAssetAtPath<Material>(SpitMaterialPath);
        report.AppendLine($"  MATERIAL {SpitMaterialPath}: " +
                          (material == null ? "ABSENT" : $"PRESENT shader='{material.shader.name}'"));
    }

    private static void AppendSceneReport(StringBuilder report)
    {
        ZombieSpitPool pool = Object.FindFirstObjectByType<ZombieSpitPool>(FindObjectsInactive.Include);
        report.AppendLine(pool == null
            ? "SPIT POOL: <none in the open scene> - every Demon would log once and bite instead."
            : $"SPIT POOL: {HierarchyPath(pool.transform)}, " +
              $"spitPrefab={Prop(new SerializedObject(pool), "spitPrefab")}, " +
              $"initialPoolSize={PropNum(new SerializedObject(pool), "initialPoolSize")}, " +
              $"poolContainer={Prop(new SerializedObject(pool), "poolContainer")}");

        // Not this tool's to build - Tools/HUD/Add Combat Feedback does it - but the work
        // order asks this report to say whether it is there, because a level with roles and
        // no hitmarker is a half-landed feature and the two tools are run back to back.
        CombatFeedback feedback = Object.FindFirstObjectByType<CombatFeedback>(FindObjectsInactive.Include);
        report.AppendLine(feedback == null
            ? "COMBAT FEEDBACK: <none in the open scene> - run Tools/HUD/Add Combat Feedback."
            : $"COMBAT FEEDBACK: {HierarchyPath(feedback.transform)} (built by Tools/HUD/Add Combat Feedback)");
    }

    // ==========================================================================
    //  2. Add
    // ==========================================================================

    /// <summary>
    /// Puts every piece in, skipping whatever is already correct, so a second run changes
    /// nothing and an interrupted run can simply be repeated. Four kinds of write, in the
    /// order a reader of the log wants them: the prefabs, the audio profiles, the projectile,
    /// the pool.
    /// </summary>
    [MenuItem("Tools/Enemies/Add Enemy Roles", priority = 1401)]
    private static void AddEnemyRoles()
    {
        List<string> written = new List<string>();   // ⚠ asset writes - not undoable
        List<string> fields = new List<string>();    // scene writes - undoable
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- 1 and 2: the role components, the retunes and the VFX, on the prefabs ----
        foreach (RoleSetup role in Roles) ApplyRolePrefab(role, written, skipped, notes);

        // ---- 3: the projectile and its material ----
        ZombieSpit spit = EnsureSpitPrefab(written, skipped, notes);

        // Flushed here rather than at the end, the way GrenadeSetupTools does it: the pool
        // below wires a reference to the spit prefab, and an asset that is still only in
        // memory is an asset a crashed editor would take the reference's target away with.
        if (written.Count > 0) AssetDatabase.SaveAssets();

        // ---- 4 and 5: the profiles, and the six report rows they carry ----
        ApplyProfiles(fields, skipped, notes);

        // ---- 6: the pool that hands the projectile out ----
        EnsureSpitPool(spit, fields, skipped, notes);

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Add Enemy Roles finished.");

        log.AppendLine($"⚠ ASSETS AND PREFABS WRITTEN ({written.Count}) - these went straight to " +
                       "disk and are NOT undoable. Ctrl+Z will not bring them back; " +
                       "`git checkout -- <path>` is the way back:");
        AppendList(log, written, "  <none - every prefab and asset was already correct>");

        log.AppendLine($"SCENE FIELDS SET ({fields.Count}):");
        AppendList(log, fields, "  <none - the scene was already wired>");

        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"The SCENE changes are Undo-recorded as one step, '{UndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or they are lost. Then run " +
                       "Tools/Enemies/Report Enemy Roles again and diff it against the run " +
                       "before this one - that diff is the record of what changed.");

        Debug.Log(log.ToString().TrimEnd());
    }

    // ---- Steps 1 and 2: one prefab -------------------------------------------

    /// <summary>
    /// Adds the role, applies the retunes and wires the VFX on one enemy prefab, and saves
    /// the asset ONLY if something actually changed - which is what stops a second run
    /// rewriting four prefab files for nothing and putting four no-op diffs in git.
    /// </summary>
    /// <remarks>
    /// ⚠ Through LoadPrefabContents / SaveAsPrefabAsset / UnloadPrefabContents, never through
    /// an instance in the open scene: a scene instance would record the change as a prefab
    /// OVERRIDE on that one zombie, and WaveManager.SpawnZombie instantiates the ASSET, so
    /// every spawned zombie would be roleless and the level would look correct in the editor
    /// and behave as though nothing had been done. The same shape DistributionResetTools,
    /// GrenadeSetupTools and LevelFeatureTools use.
    /// </remarks>
    private static void ApplyRolePrefab(RoleSetup role, List<string> written,
                                        List<string> skipped, List<string> notes)
    {
        if (RefuseIfBoss(role.PrefabPath, notes)) return;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(role.PrefabPath) == null)
        {
            notes.Add($"⚠ No prefab at {role.PrefabPath}, so {role.RoleType.Name} was not added " +
                      "and its retunes were not applied. Nothing else in this run was affected.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(role.PrefabPath);
        if (root == null)
        {
            notes.Add($"⚠ Could not open {role.PrefabPath} for editing. It was NOT rewritten.");
            return;
        }

        try
        {
            List<string> changes = new List<string>();

            ZombieAI ai = root.GetComponentInChildren<ZombieAI>(true);
            if (ai == null)
            {
                notes.Add($"⚠ {role.PrefabPath} carries no ZombieAI, so {role.RoleType.Name} " +
                          "could not be added ([RequireComponent(typeof(ZombieAI))]) and the " +
                          "prefab was NOT rewritten.");
                return;
            }

            AddRoleComponent(role, ai.gameObject, changes, skipped, notes);
            ApplyStats(role, ai, changes, skipped, notes);
            WireRoleVfx(role, ai.gameObject, changes, skipped, notes);

            if (changes.Count == 0)
            {
                // The whole point of the idempotence: nothing changed, so the file on disk is
                // not touched and git sees no diff on a re-run. Deliberately NOT phrased as
                // "already correct" - a run that changed nothing because every step REFUSED
                // (the wrong role already on the prefab, a missing effect) lands here too, and
                // the NOTES above are what say which of the two it was.
                skipped.Add($"{role.PrefabPath} - nothing to change, NOT rewritten");
                return;
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, role.PrefabPath);
            if (saved == null)
            {
                notes.Add($"⚠ Could not write {role.PrefabPath}. The changes below were made in " +
                          "memory and thrown away: " + string.Join("; ", changes));
                return;
            }

            written.Add($"{role.PrefabPath}: {string.Join("; ", changes)}");
        }
        finally
        {
            // Must happen whether the save succeeded, refused or threw, or the preview scene
            // is left open and the next LoadPrefabContents on this path misbehaves.
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// ⚠ The hard guard the work order asks for. The boss keeps BossZombieController and its
    /// own phase speed scale; a role beside it would fight over SetPhaseSpeedScale, which is
    /// the one piece of state the two would share. A warning and a refusal, never a dialog -
    /// this runs headless.
    /// </summary>
    private static bool RefuseIfBoss(string prefabPath, List<string> notes)
    {
        if (!prefabPath.Replace('\\', '/').Contains("/Boss - ")) return false;

        Debug.LogWarning($"{LogPrefix} REFUSED to edit '{prefabPath}': boss prefabs are never " +
                         "given a role. The Rock Golem keeps its BossZombieController, which " +
                         "owns the same SetPhaseSpeedScale a role would borrow. Nothing was " +
                         "changed on it.");
        notes.Add($"⚠ REFUSED to touch {prefabPath} - the boss is never given a role.");
        return true;
    }

    private static void AddRoleComponent(RoleSetup role, GameObject host, List<string> changes,
                                         List<string> skipped, List<string> notes)
    {
        ZombieRole existing = host.GetComponent<ZombieRole>();

        if (existing != null && existing.GetType() == role.RoleType)
        {
            skipped.Add($"{role.PrefabPath} already carries {role.RoleType.Name}");
            return;
        }

        if (existing != null)
        {
            // [DisallowMultipleComponent] on ZombieRole: AddComponent would throw, and a
            // throw here would abort the run half way through four prefabs.
            notes.Add($"⚠ {role.PrefabPath} already carries {existing.GetType().Name}, which is " +
                      $"NOT {role.RoleType.Name}. ZombieRole is [DisallowMultipleComponent], so " +
                      "the right one cannot be added beside it. Remove the wrong component by " +
                      "hand and re-run. Nothing was changed on this prefab.");
            return;
        }

        // Plain AddComponent, not Undo.AddComponent: this object lives in the prefab-contents
        // preview scene, and registering it with Undo would put a scene the user cannot see
        // into the undo stack.
        if (host.AddComponent(role.RoleType) == null)
        {
            notes.Add($"⚠ AddComponent({role.RoleType.Name}) returned null on {role.PrefabPath}.");
            return;
        }

        changes.Add($"+ {role.RoleType.Name} on '{host.name}'");
    }

    private static void ApplyStats(RoleSetup role, ZombieAI ai, List<string> changes,
                                   List<string> skipped, List<string> notes)
    {
        if (role.Stats.Length == 0) return;

        SerializedObject serialized = new SerializedObject(ai);
        bool any = false;

        foreach (StatChange stat in role.Stats)
        {
            SerializedProperty property = serialized.FindProperty(stat.Field);
            if (property == null)
            {
                notes.Add($"⚠ ZombieAI has no serialized field '{stat.Field}' - it has been " +
                          $"renamed, and this tool binds to it by name. {role.PrefabName} keeps " +
                          "its authored value.");
                continue;
            }

            if (Mathf.Approximately(property.floatValue, stat.Value))
            {
                skipped.Add($"{role.PrefabPath} {stat.Field} is already {Num(stat.Value)}");
                continue;
            }

            float was = property.floatValue;
            property.floatValue = stat.Value;
            any = true;

            // The expected old value is logged when it differs, because a prefab that is not
            // where the survey said it was is a prefab somebody has already retuned by hand.
            string surprise = Mathf.Approximately(was, stat.Expected)
                ? string.Empty
                : $" ⚠ (the survey expected {Num(stat.Expected)} here - it had been changed)";

            changes.Add($"{stat.Field} {Num(was)} -> {Num(stat.Value)}{surprise} [{stat.Why}]");
        }

        // Without Undo: the target lives in the prefab-contents preview scene.
        if (any) serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Puts the pack effect in the role's own GameObject slot. Only into an EMPTY slot: a
    /// prefab already pointing at something is an authored choice, and overwriting it on a
    /// re-run is exactly what "idempotent" is supposed to rule out.
    /// </summary>
    private static void WireRoleVfx(RoleSetup role, GameObject host, List<string> changes,
                                    List<string> skipped, List<string> notes)
    {
        if (string.IsNullOrEmpty(role.VfxField))
        {
            notes.Add($"{role.PrefabName}: {role.VfxNote}.");
            return;
        }

        Component component = host.GetComponent(role.RoleType);
        if (component == null) return;   // AddRoleComponent already said why

        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(role.VfxField);
        if (property == null)
        {
            notes.Add($"⚠ {role.RoleType.Name} has no serialized field '{role.VfxField}' - it has " +
                      "been renamed, and this tool binds to it by name. NOT SET.");
            return;
        }

        if (role.VfxPrefabName == null)
        {
            // The screamer's ring. Reported every run so the emptiness reads as a decision
            // rather than as a hole somebody forgot to fill.
            notes.Add($"{role.PrefabName}.{role.VfxField}: {role.VfxNote}.");
            if (property.objectReferenceValue != null)
            {
                skipped.Add($"{role.PrefabPath} {role.VfxField} already = " +
                            $"{Obj(property.objectReferenceValue)} - somebody authored one, kept");
            }
            return;
        }

        if (property.objectReferenceValue != null)
        {
            skipped.Add($"{role.PrefabPath} {role.VfxField} already = {Obj(property.objectReferenceValue)}");
            return;
        }

        string fxPath = ParticleFolder + role.VfxPrefabName + ".prefab";
        GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(fxPath);
        if (fx == null)
        {
            notes.Add($"⚠ No effect at {fxPath}, so {role.PrefabName}.{role.VfxField} is empty. " +
                      "The role still runs, invisibly. Check the Nature pack imported.");
            return;
        }

        property.objectReferenceValue = fx;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        changes.Add($"{role.VfxField} = '{fx.name}' ({role.VfxNote}) " +
                    $"{DescribeLooping(fx)}");
    }

    /// <summary>
    /// Says out loud, in the log, whether the effect just wired is a LOOPING system - because
    /// the lifetime field beside it means two different things depending on the answer, and
    /// all three of the pack's dust/impact effects this tool touches loop. Read off the asset
    /// rather than from a constant, so a replaced effect tells the truth.
    /// </summary>
    private static string DescribeLooping(GameObject fx)
    {
        int looping = 0;
        int total = 0;
        foreach (ParticleSystem system in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            total++;
            if (system.main.loop) looping++;
        }

        if (total == 0) return "- no ParticleSystem in it";

        return looping > 0
            ? $"⚠ {looping} of {total} emitter(s) LOOP, so the role's lifetime field is how long " +
              "it SPRAYS, not how long to wait before tidying up"
            : $"- {total} emitter(s), none looping (a true one-shot)";
    }

    // ---- Steps 3 and 4: the profiles -----------------------------------------

    /// <summary>
    /// Sets each profile's role and sizes ONLY that profile's own role arrays to one.
    /// </summary>
    /// <remarks>
    /// ⚠ The arraySize step is the one that is easy to miss and impossible to notice: see the
    /// file header. Size 0 is not a MISSING row, it is no row - proved live by headshotSounds,
    /// an assessed slot the report has never listed. One null element each is what puts the
    /// six new rows in front of the students.
    ///
    /// ⚠ Only the arrays matching the profile's OWN role. Sizing all six on all five profiles
    /// would be thirty rows for six sounds, and IAudioSlotFilter would hide twenty-four of
    /// them - slots nobody can see and nobody is asked to fill.
    /// </remarks>
    private static void ApplyProfiles(List<string> fields, List<string> skipped, List<string> notes)
    {
        EnemyAudioManager manager = Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning($"{LogPrefix} No EnemyAudioManager in the open scene, so no profile " +
                             "roles and none of the six report rows were set. The prefabs above " +
                             "were still edited - re-run this item with the level open.");
            notes.Add("⚠ No EnemyAudioManager in the open scene: the four roles and the six new " +
                      "assessed rows were NOT set. Open the level and re-run.");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogWarning($"{LogPrefix} EnemyAudioManager has no array field '{ProfilesField}' - " +
                             "it has been renamed, and this tool binds to it by name. No profile " +
                             "was changed.");
            return;
        }

        string managerPath = HierarchyPath(manager.transform);
        bool changed = false;

        foreach (RoleSetup role in Roles)
        {
            int index = FindProfileIndex(profiles, role.ProfileId, role.ProfileName);
            if (index < 0)
            {
                notes.Add($"⚠ No '{role.ProfileName}' profile on the EnemyAudioManager, so its " +
                          $"role is unset and its {role.RoleClipArrays.Length} assessed row(s) " +
                          "are missing from the report.");
                continue;
            }

            SerializedProperty profile = profiles.GetArrayElementAtIndex(index);
            changed |= SetProfileRole(profile, role.ProfileRole, managerPath, index,
                                      role.ProfileName, fields, skipped, notes);

            foreach (string field in role.RoleClipArrays)
            {
                changed |= SizeRoleArray(profile, field, managerPath, index, role.ProfileName,
                                         fields, skipped, notes);
            }
        }

        // The boss. Its PREFAB is untouchable; its profile still needs None, which is what
        // hides all six role slots on it.
        int bossIndex = FindProfileIndex(profiles, BossProfileId, BossProfileName);
        if (bossIndex < 0)
        {
            notes.Add($"⚠ No '{BossProfileName}' profile on the EnemyAudioManager - its role could " +
                      "not be set to None. If it defaults to None already this costs nothing.");
        }
        else
        {
            changed |= SetProfileRole(profiles.GetArrayElementAtIndex(bossIndex), EnemyRole.None,
                                      managerPath, bossIndex, BossProfileName, fields, skipped, notes);
        }

        if (changed)
        {
            serialized.ApplyModifiedProperties();   // Undo-recorded, unlike the prefab writes
            EditorUtility.SetDirty(manager);
        }

        notes.Add("The six role clip slots are left EMPTY on purpose - they are the students' " +
                  "to fill, and each empty element is exactly one MISSING row in the Ctrl+G " +
                  "report. A clip written here would be a placeholder nobody was asked for.");
    }

    private static bool SetProfileRole(SerializedProperty profile, EnemyRole role, string managerPath,
                                       int index, string profileName, List<string> fields,
                                       List<string> skipped, List<string> notes)
    {
        SerializedProperty property = profile.FindPropertyRelative("enemyRole");
        if (property == null)
        {
            notes.Add($"⚠ EnemySoundProfile has no serialized field 'enemyRole' - Agent E's slot " +
                      $"is not in this build, so '{profileName}' keeps no role and none of the " +
                      "six role slots can appear. NOT SET.");
            return false;
        }

        if (property.intValue == (int)role)
        {
            skipped.Add($"{managerPath} {ProfilesField}[{index}] '{profileName}' enemyRole is " +
                        $"already {role}");
            return false;
        }

        EnemyRole was = (EnemyRole)property.intValue;

        // intValue rather than enumValueIndex: enumValueIndex is the position in the enum's
        // declaration and this field serializes the VALUE. They agree today because EnemyRole
        // is contiguous from 0, and would silently stop agreeing the moment somebody assigned
        // an explicit number.
        property.intValue = (int)role;

        fields.Add($"{managerPath} {ProfilesField}[{index}] '{profileName}' enemyRole " +
                   $"{was} -> {role} ({(int)role})");
        return true;
    }

    /// <summary>
    /// Sizes one clip array to exactly one EMPTY element - one MISSING row in the report.
    /// Never touches an array that already has elements: those are the students' clips, or a
    /// previous run's row, and either way growing it would add a row nobody asked for.
    /// </summary>
    private static bool SizeRoleArray(SerializedProperty profile, string field, string managerPath,
                                      int index, string profileName, List<string> fields,
                                      List<string> skipped, List<string> notes)
    {
        SerializedProperty property = profile.FindPropertyRelative(field);
        if (property == null || !property.isArray)
        {
            notes.Add($"⚠ EnemySoundProfile has no array field '{field}' - Agent E's slot is not " +
                      $"in this build, so '{profileName}' gets no report row for it. NOT SET.");
            return false;
        }

        if (property.arraySize > 0)
        {
            skipped.Add($"{managerPath} {ProfilesField}[{index}] '{profileName}' {field} already " +
                        $"has {property.arraySize} element(s) - left exactly as it is");
            return false;
        }

        property.arraySize = 1;

        // Explicit, not assumed: growing a SerializedProperty array copies the previous last
        // element, and there is no previous element here - but a future reader should not have
        // to know that to be sure the new row is empty.
        property.GetArrayElementAtIndex(0).objectReferenceValue = null;

        fields.Add($"{managerPath} {ProfilesField}[{index}] '{profileName}' {field} 0 -> 1 " +
                   "element, [0] = <none> (an EMPTY array is NO report row at all; one null " +
                   "element is one MISSING row for the student to fill)");
        return true;
    }

    // ---- Step 5: the projectile ----------------------------------------------

    /// <summary>
    /// The thing the Demon throws. Built once and saved to disk; a re-run repairs only its
    /// impact effect, the one reference an interrupted first run could leave null.
    /// </summary>
    /// <remarks>
    /// ⚠ Deliberately carries NO AudioSource, for the same reason the grenade does not: the
    /// handout reset walks serialized AudioClip fields on components it knows about and never
    /// touches AudioSources, so a clip parked here would be invisible to the Ctrl+G report and
    /// would survive the reset. The splash is played by EnemyAudioManager.PlayRoleSoundAt from
    /// the profile, which is where every assessed sound in this project lives.
    /// </remarks>
    private static ZombieSpit EnsureSpitPrefab(List<string> written, List<string> skipped,
                                               List<string> notes)
    {
        GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(ParticleFolder + SpitImpactFx + ".prefab");
        if (fx == null)
        {
            notes.Add($"⚠ No impact effect at {ParticleFolder}{SpitImpactFx}.prefab, so the spit " +
                      "lands invisibly. It still splashes, still damages and still plays its " +
                      "sound. Check the Nature pack imported.");
        }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SpitPrefabPath);
        if (existing != null)
        {
            skipped.Add($"spit prefab {SpitPrefabPath}");

            ZombieSpit present = existing.GetComponent<ZombieSpit>();
            if (present == null)
            {
                notes.Add($"⚠ {SpitPrefabPath} exists but carries no ZombieSpit, so the pool below " +
                          "has nothing to hand out. Delete that prefab and re-run.");
                return null;
            }

            // The one reference worth repairing on a re-run: an unwired effect is a silent
            // hole rather than an error, and a half-finished earlier run is exactly how it
            // would come to be null.
            if (fx != null)
            {
                SerializedObject serialized = new SerializedObject(present);
                SerializedProperty impact = serialized.FindProperty("impactFx");
                if (impact != null && impact.objectReferenceValue == null)
                {
                    impact.objectReferenceValue = fx;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(existing);
                    written.Add($"{SpitPrefabPath} ZombieSpit.impactFx = '{fx.name}' " +
                                "(repaired - it was null)");
                }
            }

            return present;
        }

        Material material = EnsureSpitMaterial(written, skipped, notes);

        // A scratch object in the open scene, saved and then destroyed - the same shape
        // GrenadeSetupTools uses. The saved asset is an ordinary prefab referencing the FBX's
        // mesh, not a variant chained to the model importer.
        GameObject root = new GameObject("ZombieSpit");

        try
        {
            // Default. ⚠ NOT the Enemy layer: the spit has to be able to collide with the
            // world and with the player, and ZombieSpit ignores the shooter and its packmates
            // itself (packClearRadius) rather than relying on a layer matrix nobody can see.
            root.layer = 0;
            root.transform.position = Vector3.zero;   // so world bounds read as local

            BuildSpitVisual(root.transform, material, out string visualNote);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = false;
            body.mass = 0.3f;

            // A lobbed glob crosses more than its own diameter in one physics step near the
            // top of its fall, so the cheap discrete sweep would let it tunnel through the
            // terrain - the same setting Crossbow_Bolt and Grenade already carry.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = SpitColliderRadius;
            sphere.center = Vector3.zero;

            // ⚠ NOT a trigger. ZombieSpit splashes from OnCollisionEnter; a trigger raises
            // OnTriggerEnter instead and the spit would sail through the level and be
            // recycled on its lifetime, silently, having hit nothing.
            sphere.isTrigger = false;

            ZombieSpit spit = root.AddComponent<ZombieSpit>();
            if (fx != null)
            {
                SerializedObject serialized = new SerializedObject(spit);
                SerializedProperty impact = serialized.FindProperty("impactFx");
                if (impact != null)
                {
                    impact.objectReferenceValue = fx;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    notes.Add("⚠ ZombieSpit has no serialized field 'impactFx' - it has been " +
                              "renamed, and this tool binds to it by name.");
                }
            }

            EnsureFolder(EnemyPrefabFolder);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SpitPrefabPath);
            if (saved == null)
            {
                Debug.LogWarning($"{LogPrefix} Could not write {SpitPrefabPath}. The pool below " +
                                 "has nothing to hand out.");
                return null;
            }

            written.Add($"{SpitPrefabPath} (layer Default, Rigidbody useGravity mass " +
                        $"{Num(body.mass)} / ContinuousDynamic / Interpolate, SphereCollider " +
                        $"r={Num(SpitColliderRadius)} isTrigger=FALSE, ZombieSpit with impactFx=" +
                        $"{(fx != null ? $"'{fx.name}' {DescribeLooping(fx)}" : "<none>")}, " +
                        $"NO AudioSource; {visualNote})");

            return saved.GetComponent<ZombieSpit>();
        }
        finally
        {
            // The scratch instance must go whether or not the save succeeded, or it is left
            // behind in the open scene for the owner to find.
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// The glob's mesh, sized to <see cref="SpitLength"/> in world metres and recentred on the
    /// root - the launch point, the collider and the splash all read transform.position, so
    /// the body has to sit on the root rather than wherever the FBX's pivot happens to be.
    /// </summary>
    private static void BuildSpitVisual(Transform parent, Material material, out string note)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(SpitModelPath);

        if (model == null)
        {
            // A primitive sphere is the fallback. ⚠ CreatePrimitive adds its own
            // SphereCollider, which would sit beside the one the prefab needs and give the
            // spit two colliders and two OnCollisionEnter calls.
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.name = SpitModelChildName;
            sphere.transform.SetParent(parent, false);
            sphere.transform.localScale = Vector3.one * SpitLength;

            MeshRenderer sphereRenderer = sphere.GetComponent<MeshRenderer>();
            if (sphereRenderer != null && material != null) sphereRenderer.sharedMaterial = material;

            note = $"visual: a primitive sphere {Num(SpitLength)} m across (no model at " +
                   $"{SpitModelPath}), its own collider removed";
            return;
        }

        GameObject visual = Object.Instantiate(model);
        visual.name = SpitModelChildName;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        // Whatever the FBX importer added is not wanted on a projectile: the prefab's own
        // SphereCollider is what lands it, and a mesh collider on the rock would collide too.
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }
        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))
        {
            Object.DestroyImmediate(body);
        }
        foreach (LODGroup group in visual.GetComponentsInChildren<LODGroup>(true))
        {
            Object.DestroyImmediate(group);
        }

        float factor = 1f;
        if (TryGetMeshBounds(visual, out Bounds bounds) && LongestAxis(bounds.size) > 0.0001f)
        {
            factor = SpitLength / LongestAxis(bounds.size);
            visual.transform.localScale *= factor;

            // InverseTransformVector rather than a raw subtraction: localPosition is measured
            // in the parent's own space. The parent is at the origin and unrotated here, but
            // this is the line that would silently be wrong if it ever were not.
            Vector3 centre = parent.position + (bounds.center - parent.position) * factor;
            visual.transform.localPosition += parent.InverseTransformVector(parent.position - centre);
        }

        foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (material != null) renderer.sharedMaterial = material;
        }

        note = $"visual: '{model.name}' scaled x{Num(factor)} to {Num(SpitLength)} m and " +
               "recentred on the root";
    }

    /// <summary>
    /// The green the glob is painted. Written beside the prefab rather than reused from the
    /// pack, because tinting a shared pack material would repaint every rock in the level.
    /// </summary>
    private static Material EnsureSpitMaterial(List<string> written, List<string> skipped,
                                               List<string> notes)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(SpitMaterialPath);
        if (existing != null)
        {
            skipped.Add($"material {SpitMaterialPath} (shader '{existing.shader.name}')");
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            // This project is URP, so a null here means the render pipeline packages have
            // changed under the tool rather than that a Standard material is wanted - but a
            // magenta glob is still better than no glob and an aborted run.
            shader = Shader.Find("Standard");
            notes.Add("⚠ No 'Universal Render Pipeline/Lit' shader, so the spit's material was " +
                      "built on 'Standard' and will render magenta in URP. Change its shader by " +
                      "hand.");
        }

        if (shader == null)
        {
            notes.Add("⚠ Neither the URP Lit nor the Standard shader could be found, so no " +
                      "material was written and the spit uses whatever the model ships with.");
            return null;
        }

        Material material = new Material(shader) { name = "ZombieSpit_Bile" };

        // URP/Lit reads _BaseColor; the legacy _Color is set too so a material whose shader is
        // swapped by hand afterwards still comes out green rather than white.
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", SpitColour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", SpitColour);

        EnsureFolder(EnemyPrefabFolder);
        AssetDatabase.CreateAsset(material, SpitMaterialPath);

        written.Add($"{SpitMaterialPath} (shader '{shader.name}', base colour " +
                    $"RGB {Num(SpitColour.r)}/{Num(SpitColour.g)}/{Num(SpitColour.b)})");
        return material;
    }

    // ---- Step 6: the pool ----------------------------------------------------

    /// <summary>
    /// One pool per scene under Game Management, beside the other singletons. A SCENE change,
    /// Undo-recorded - the owner still has to save.
    /// </summary>
    private static void EnsureSpitPool(ZombieSpit spit, List<string> fields, List<string> skipped,
                                       List<string> notes)
    {
        ZombieSpitPool pool = Object.FindFirstObjectByType<ZombieSpitPool>(FindObjectsInactive.Include);

        if (pool == null)
        {
            GameObject host = FindByPath(HostPath);
            if (host == null)
            {
                Debug.LogWarning($"{LogPrefix} No '{HostPath}' object in the open scene, so the " +
                                 "spit pool was not created. Everything else was still done.");
                notes.Add($"⚠ No '{HostPath}' in the scene: the spit pool was NOT created and " +
                          "every Demon will log once and bite instead of spitting.");
                return;
            }

            GameObject made = new GameObject(SpitPoolName);
            Undo.RegisterCreatedObjectUndo(made, UndoLabel);
            made.transform.SetParent(host.transform, false);
            pool = Undo.AddComponent<ZombieSpitPool>(made);
            fields.Add($"created {HierarchyPath(made.transform)} with a ZombieSpitPool");
        }
        else
        {
            skipped.Add($"{HierarchyPath(pool.transform)} already has a ZombieSpitPool - " +
                        "repaired in place");
        }

        SerializedObject serialized = new SerializedObject(pool);
        SerializedProperty prefabProperty = serialized.FindProperty("spitPrefab");

        if (prefabProperty == null)
        {
            notes.Add("⚠ ZombieSpitPool has no serialized field 'spitPrefab' - it has been " +
                      "renamed, and this tool binds to it by name. NOT SET.");
        }
        else if (prefabProperty.objectReferenceValue != null)
        {
            skipped.Add($"{HierarchyPath(pool.transform)} spitPrefab already = " +
                        $"{Obj(prefabProperty.objectReferenceValue)}");
        }
        else if (spit == null)
        {
            notes.Add($"⚠ {HierarchyPath(pool.transform)} spitPrefab is EMPTY - the spit prefab " +
                      "could not be built or loaded. ZombieSpitPool.Initialize logs an error " +
                      "once and every Demon bites instead. Fix the prefab and re-run.");
        }
        else
        {
            // The COMPONENT, not the GameObject: ObjectPool<T> is generic over Component and
            // ZombieSpitPool declares the field as ZombieSpit.
            prefabProperty.objectReferenceValue = spit;
            serialized.ApplyModifiedProperties();
            fields.Add($"{HierarchyPath(pool.transform)} spitPrefab = the ZombieSpit component on " +
                       $"{SpitPrefabPath}");
        }

        notes.Add("poolContainer is deliberately LEFT EMPTY: ZombieSpitPool.Initialize makes one " +
                  "as a child of itself at startup, so a container authored here would only be a " +
                  "second thing to keep in step.");
    }

    // ==========================================================================
    //  Profile lookup
    // ==========================================================================

    /// <summary>The index of one profile, by id first and then by name. -1 when there is none.</summary>
    private static int FindProfileIndex(SerializedProperty profiles, EnemyId id, string name)
    {
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);

            SerializedProperty idProperty = profile.FindPropertyRelative("enemyId");
            if (idProperty != null && idProperty.intValue == (int)id) return i;
        }

        // Only after every id has been considered: a profile whose id is still Unassigned
        // would otherwise be matched by name before the correctly-identified one is reached.
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty nameProperty =
                profiles.GetArrayElementAtIndex(i).FindPropertyRelative("enemyName");
            if (nameProperty == null) continue;

            if (string.Equals(nameProperty.stringValue.Trim(), name,
                              System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ProfileNameOf(SerializedProperty profile)
    {
        SerializedProperty name = profile.FindPropertyRelative("enemyName");
        return name == null ? "<no enemyName>" : name.stringValue.Trim();
    }

    private static string ProfileNames(SerializedProperty profiles)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < profiles.arraySize; i++)
        {
            names.Add($"{i}:{ProfileNameOf(profiles.GetArrayElementAtIndex(i))}");
        }
        return string.Join(", ", names);
    }

    // ==========================================================================
    //  Shared helpers - the same shape as the other setup tools here
    // ==========================================================================

    /// <summary>
    /// A GameObject by hierarchy path in the active scene. Transform.Find rather than
    /// GameObject.Find, because the latter cannot see an inactive object and half the things
    /// a tool looks for could legitimately be switched off.
    /// </summary>
    private static GameObject FindByPath(string path)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return null;

        string[] parts = path.Split('/');

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;

            Transform current = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                current = current.Find(parts[i]);
                if (current == null) break;
            }

            if (current != null) return current.gameObject;
        }

        return null;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string built = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = built + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
            built = next;
        }
    }

    /// <summary>
    /// Combined world bounds from the MESHES rather than from Renderer.bounds. Renderer.bounds
    /// is a cached world AABB the engine refreshes when it draws, and on a GameObject
    /// instantiated moments ago in EDIT mode it reads near-zero - dividing a target length by
    /// that gives an enormous scale. It also includes ParticleSystemRenderers. Mesh.bounds is
    /// authored data on the asset: never stale, never a particle system. (Learned the hard way
    /// by MinigunSetupTools, which produced an 18 m minigun.)
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

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? $"<unnamed>({layer})" : $"{name}({layer})";
    }

    private static float LongestAxis(Vector3 size) => Mathf.Max(size.x, Mathf.Max(size.y, size.z));

    private static string Prop(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        return property == null ? "<no such field>" : Obj(property.objectReferenceValue);
    }

    private static string PropNum(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) return "<no such field>";
        return property.propertyType == SerializedPropertyType.Integer
            ? property.intValue.ToString(CultureInfo.InvariantCulture)
            : Num(property.floatValue);
    }
}
