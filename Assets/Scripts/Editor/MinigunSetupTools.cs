using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * MinigunSetupTools.cs
 *
 * Purpose: Wires the minigun into the scene - the weapon in the player's hands built from
 *          Assets/Minigun.fbx, the world pickup that selects it, and the WeaponAudioManager
 *          profile that carries its NINE assessed audio slots (the standard gun set plus
 *          spin up, spin loop and spin down).
 * Menu:    Tools/Weapons/Report Minigun Setup   (read only)
 *          Tools/Weapons/Build Minigun          (idempotent - a second run changes nothing)
 * Used by: the integrator, driven from the Unity CLI. The owner saves the scene afterwards.
 *
 * Menu items rather than hand edits of the .unity file, for this project's standing reason:
 * the files on disk are routinely older than what is open in the editor, so a hand edit
 * would be lost the next time the owner saves. The same reason GrenadeSetupTools and
 * WeaponSetupTools next door are menu items.
 *
 * ⚠ NO DIALOGS anywhere in this file. Both items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block it with a modal window nobody can see,
 * let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Run Report before and after Build: it prints one block with a fixed line order and
 * invariant-culture numbers, so the two runs diff cleanly and the diff IS the record of
 * what Build did.
 *
 * EVERYTHING this tool writes is a SCENE edit - the controller, the pickup, the audio
 * profile. Nothing is written to disk: the FBX, its four materials and the two icon sprites
 * all already exist, so there is no AssetDatabase.CreateAsset anywhere here (which is the
 * one way this differs from GrenadeSetupTools, whose material/physics-material/prefab had to
 * be authored). The scene edits are Undo-recorded as one step and need the owner to SAVE THE
 * SCENE.
 *
 * ⚠ Binds to serialized fields by literal name: on MinigunController "barrel",
 * "spinUpSeconds", "spinDownSeconds", "maxBarrelRpm"; on WeaponController "weaponId",
 * "weaponName", "weaponAudioManager", "firePoint", "muzzleFlash", "fireRate", "damage",
 * "range", "maxAmmo", "ammoText", "shootableLayerMask", "aimDownSightsEnabled",
 * "isAutomatic", the six pose fields and the eight bob fields copied from the SMG; on Item /
 * WeaponItem "itemName", "itemIcon", "itemDescription", "weaponController", "weaponName",
 * "hudIcon"; and on WeaponAudioManager the array "m_WeaponProfiles" plus every field of
 * WeaponSoundProfile listed in WriteProfile. Renaming any of those compiles clean and breaks
 * this silently, like the other editors here - see CODE_STRUCTURE's editor-tooling warning.
 *
 * ⚠ The SMG is the template for everything that is a matter of taste rather than of the
 * minigun: the resting pose, the bob, the layer, the active state, the shootable mask, the
 * muzzle flash and the shape of the pickup are all READ off it rather than invented. It is
 * the closest firearm in the scene, and a number copied from something the owner has already
 * tuned in this level beats a number typed here.
 */

public static class MinigunSetupTools
{
    private const string LogPrefix = "[MinigunSetupTools]";
    private const string UndoLabel = "Build Minigun";
    private const string FixUndoLabel = "Fix Minigun Orientation and Ammo";

    // ---- Asset paths ---------------------------------------------------------

    /// <summary>The owner dropped it in the Assets root. 7 renderers, no animator, no
    /// skinned meshes, and its four materials import embedded and already URP/Lit.</summary>
    private const string ModelPath = "Assets/Minigun.fbx";

    private const string IconFolder =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Sprites/Icons_Weapons/";

    /// <summary>
    /// ⚠ A STAND-IN. The Apocalypse HUD pack ships no minigun icon. Of the weapons that do
    /// have both the plain and the _Side view the hotbar and the current-weapon panel need,
    /// the assault rifle is the closest read - a long two-handed gun with a magazine hanging
    /// off it - where the other candidate (ICON_SM_Wep_Grenade_Launcher_01) reads as a
    /// single-shot tube and would also sit confusingly next to the grenade in the hotbar.
    /// Replace both sprites the moment a minigun icon exists; nothing here depends on the
    /// name.
    /// </summary>
    private const string IconName = "ICON_SM_Wep_AssaultRifle_03";

    /// <summary>
    /// The owner's own art, 2026-09-06: a 512x512 hotbar icon and a 1024x256 side-on graphic
    /// for the bottom-right weapon panel. Both dropped into Assets/ as plain textures; Wire
    /// Minigun Icons imports them as sprites and wires them. These take precedence over the
    /// pack's assault-rifle stand-ins above wherever they exist.
    /// </summary>
    private const string OwnerIconPath = "Assets/minigunIcon.png";
    private const string OwnerHudIconPath = "Assets/minigunGraphic.png";

    private const string IconPath = IconFolder + IconName + ".png";

    /// <summary>The side-on view WeaponSetupTools' current-weapon panel expects.</summary>
    private const string HudIconPath = IconFolder + IconName + "_Side.png";

    /// <summary>
    /// The same placeholder DistributionResetTools resets every assessed slot to, read from
    /// the same constant it does (its own copy of this path is private). All nine minigun
    /// slots ship holding this rather than empty - see WriteProfile for why that is
    /// load-bearing.
    /// </summary>
    private const string BeepClipPath = AudioProgressScanner.DemoSoundsFolder + "Beep to replace.wav";

    /// <summary>
    /// Only used if the SMG has no muzzle flash to copy. The SMG's is a scene instance of
    /// this prefab that has since been unpacked, so copying the SMG's own is the truer
    /// match; this is the fallback for a scene where the SMG's slot is empty.
    /// </summary>
    private const string MuzzleFlashPrefabPath =
        "Assets/Inbox/Low Poly Models/Nature/Particles/Prefabs/FX_Gunshot_Heavy_Single_Tracers_01.prefab";

    // ---- Scene paths ---------------------------------------------------------

    private const string PlayerPath = "Player";
    private const string WeaponParentPath = "Player/FirstPersonCharacter/WeaponParent";
    private const string PickupsPath = "Game Management/Pickups";

    private const string SmgControllerPath = WeaponParentPath + "/SMG";
    private const string SmgPickupPath = PickupsPath + "/Pickup - SMG";

    private const string ControllerName = "Minigun";
    private const string ControllerPath = WeaponParentPath + "/" + ControllerName;

    private const string PickupName = "Pickup - Minigun";
    private const string PickupPath = PickupsPath + "/" + PickupName;

    /// <summary>
    /// The FBX's own root is called "Minigun" too, so the instance is renamed on the way in:
    /// "Minigun/Minigun" in the hierarchy would be read as a mistake by anyone opening the
    /// scene, and Transform.Find on the parent's name would be ambiguous.
    /// </summary>
    private const string ModelChildName = "Minigun_Model";

    /// <summary>The FBX's own muzzle locator - used as firePoint rather than making one.</summary>
    private const string MuzzlePointName = "Muzzle_Point_01";

    /// <summary>The FBX's rotating barrel cluster, which MinigunController turns.</summary>
    private const string BarrelName = "SK_Wep_Minigun_02_Barrel_01";

    /// <summary>Only created if the FBX has no Muzzle_Point_01 - it does today.</summary>
    private const string FirePointFallbackName = "FirePoint";

    private const string ProfileName = "Minigun";
    private const string ProfilesField = "m_WeaponProfiles";

    // ---- The numbers, all of them the owner's to tune afterwards -------------

    /// <summary>
    /// How long the gun is in the player's hands, in WORLD metres.
    ///
    /// The FBX ships about 1.51 m end to end, which is a vehicle mount rather than something
    /// a person carries: at scale 1 it fills the bottom third of the screen. 0.9 m is roughly
    /// the real M134's gun-plus-feed assembly, keeps it clearly the largest weapon in the
    /// level (the Synty SMG beside it is around half that), and still leaves the crosshair
    /// clear. Measured live off the model's renderers rather than assumed, so a re-imported
    /// or replaced FBX still lands at 0.9 m and the log says by what factor.
    /// </summary>
    private const float HandMinigunLength = 0.9f;

    /// <summary>
    /// The same measurement for the copy lying on the ground. Half the in-hand size: the
    /// pickup sits inside a glow sphere about 0.47 m across (copied from the SMG pickup), and
    /// a full-size gun would burst out of it. Still visibly the biggest thing on the pickup
    /// shelf, which is the point.
    /// </summary>
    private const float WorldMinigunLength = 0.45f;

    /// <summary>
    /// How the FBX has to be turned to point where the weapon points. **This is the fix for
    /// the gun hanging nose-down**, reported by the owner 2026-09-06.
    ///
    /// Every other weapon in this level lies along its controller's local +Z with +Y up - the
    /// SMG's silencer sits at z +0.38, its trigger below the barrel line at y +0.012. The
    /// minigun FBX is authored the other way up and the other way round: measured off the
    /// model's own locators, the muzzle is at (0, -0.0113, 0), so the barrel runs down local
    /// **-Y**, and the grip, trigger and button all sit at +Z, so local **+Z is the gun's
    /// underside**. Left at identity the barrel points at the floor, which is exactly what
    /// the owner saw, in the hand and on the pickup both.
    ///
    /// Euler(90, 180, 0) is the one rotation that fixes both at once: it carries -Y to +Z
    /// (barrel downrange) and +Z to -Y (grip underneath). A single -90 about X would point
    /// the barrel the right way and stand the gun on its head.
    ///
    /// Derived from the locators rather than eyeballed, so a re-exported FBX with the same
    /// bone layout still lands right - and if the layout changes, Report Minigun Setup prints
    /// the muzzle's local offset and this constant is one line to re-derive.
    /// </summary>
    private static readonly Quaternion ModelRotation = Quaternion.Euler(90f, 180f, 0f);

    /// <summary>
    /// Seconds between rounds: 0.05 = 20 rounds a second = 1200 rpm, twice the SMG's rate
    /// (0.1) and the same number as maxBarrelRpm, so at full speed the gun puts out one round
    /// per barrel revolution. With the owner's 2000-round belt that is 100 seconds of held
    /// trigger, which is the number that matters for the AUDIO: the shot clip has 50 ms of
    /// room before copies start stacking on each other.
    /// </summary>
    private const float FireRate = 0.05f;

    /// <summary>
    /// Per round. Less than the SMG's 10 because there are twice as many of them: 160 damage
    /// a second against the SMG's 100, so the minigun is the upgrade it looks like without
    /// being a different game.
    /// </summary>
    private const float Damage = 8f;

    /// <summary>Every firearm in this level is 100 m. An outlier here would want explaining
    /// in a level this size, and there is nothing to explain.</summary>
    private const float Range = 100f;

    /// <summary>
    /// The owner's number, raised from 200 to 2000 on 2026-09-06: "provide 10 times the
    /// amount of rounds so the play can shoot for quite a while". At the 0.05 s fire rate
    /// that is 100 seconds of held trigger rather than 10 - long enough to hear a shot loop
    /// settle, which is the point of a minigun in a sound-design level. The reload is still
    /// reachable (fire dry, or R) but nobody will meet it by accident.
    /// </summary>
    private const int MaxAmmo = 2000;

    /// <summary>The owner's numbers, and MinigunController's own defaults. Written anyway so
    /// the log records them and a component whose defaults have drifted is corrected.</summary>
    private const float SpinUpSeconds = 0.7f;
    private const float SpinDownSeconds = 1f;
    private const float MaxBarrelRpm = 1200f;

    private const float PickupOffsetFromPlayer = 3f;
    private const float DefaultHover = 0.5f;
    private const float RayStartHeight = 5f;
    private const float RayLength = 60f;
    private const float ClearRadius = 0.3f;

    /// <summary>
    /// The pose, bob and feel fields copied off the SMG. The SMG is the closest firearm in
    /// the scene and the owner has already tuned all of these in this level, so starting from
    /// them beats starting from the class defaults - which are nobody's weapon.
    /// </summary>
    private static readonly string[] CopiedFromSmg =
    {
        "raisedPosition", "raisedRotation", "loweredPositionOffset", "loweredRotationOffset",
        "raiseSpeed", "lowerSpeed",
        "bobCurveX", "bobCurveY", "bobCurveRotation",
        "bobAmountWalk", "bobAmountRun", "bobSpeed",
        "bobRotationAmountWalk", "bobRotationAmountRun"
    };

    /// <summary>
    /// The nine slots the minigun is assessed on, in the order WeaponSoundProfile declares
    /// them. Named once, used by both the report and the seeding, so the two can never
    /// disagree about what "the nine" are.
    /// </summary>
    private static readonly string[] AssessedClipFields =
    {
        "raiseWeaponSound", "lowerWeaponSound",
        "reloadStartSound", "reloadEndSound", "reloadActionSounds",
        "spinWindupSound", "spinWinddownSound", "spinLoopSound",
        "shootingStartupSound", "shootingLoopSound", "shootingShutdownSound"
    };

    /// <summary>
    /// The owner's five clips, by the field each one fills. Wire Minigun Clips fills EMPTY
    /// slots from these names and never overwrites a clip already there.
    /// </summary>
    private static readonly (string field, string clipName)[] OwnerClips =
    {
        ("spinWindupSound", "MinigunSpinWindup"),
        ("spinWinddownSound", "MinigunSpinWindDown"),
        ("spinLoopSound", "MinigunSpinLoop"),
        ("shootingStartupSound", "MinigunShootingStartup"),
        ("shootingLoopSound", "MinigunShootingLoop"),
        ("shootingShutdownSound", "MinigunShootingShutdown"),
    };

    /// <summary>Where the owner keeps them. Searched by exact clip name, so a moved folder is one constant.</summary>
    private const string OwnerClipFolder = "Assets/Audio/Weapons/Minigun";

    // ==========================================================================
    //  1. Report
    // ==========================================================================

    /// <summary>
    /// Reads and prints, changing nothing. The integrator runs it either side of Build so
    /// the before/after is a plain diff; that is also why the line order is fixed and the
    /// numbers are invariant-culture.
    /// </summary>
    [MenuItem("Tools/Weapons/Report Minigun Setup", priority = 1110)]
    private static void ReportMinigunSetup()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Minigun Setup (read only - nothing was changed).");

        AppendModelReport(report);
        AppendControllerReport(report);
        AppendPickupReport(report);
        AppendProfileReport(report);

        Debug.Log(report.ToString().TrimEnd());
    }

    private static void AppendModelReport(StringBuilder report)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            report.AppendLine($"MODEL {ModelPath}: ABSENT - Build Minigun will refuse.");
            return;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        bool hasMuzzle = FindDeepChild(model.transform, MuzzlePointName) != null;
        bool hasBarrel = FindDeepChild(model.transform, BarrelName) != null;

        report.AppendLine($"MODEL {ModelPath}: PRESENT root='{model.name}', renderers={renderers.Length}, " +
                          $"{MuzzlePointName}={(hasMuzzle ? "found" : "MISSING")}, " +
                          $"{BarrelName}={(hasBarrel ? "found" : "MISSING")}");

        report.AppendLine(TryGetWorldBounds(model, out Bounds bounds)
            ? $"  Shipped size at scale 1: {V3(bounds.size)} m, longest axis {Num(LongestAxis(bounds.size))} m"
            : "  Shipped size: <no renderers to measure>");
    }

    private static void AppendControllerReport(StringBuilder report)
    {
        GameObject host = FindByPath(ControllerPath);
        if (host == null)
        {
            report.AppendLine($"CONTROLLER {ControllerPath}: ABSENT");
            return;
        }

        report.AppendLine($"CONTROLLER {ControllerPath}: PRESENT activeSelf={host.activeSelf}, " +
                          $"layer={LayerName(host.layer)}, localPosition={V3(host.transform.localPosition)}, " +
                          $"localEuler={V3(host.transform.localEulerAngles)}, " +
                          $"localScale={V3(host.transform.localScale)}");

        MinigunController controller = host.GetComponent<MinigunController>();
        if (controller == null)
        {
            report.AppendLine("  MinigunController: <component missing>");
            return;
        }

        SerializedObject serialized = new SerializedObject(controller);

        // MinigunController's own four.
        report.AppendLine($"  Spin: barrel={Prop(serialized, "barrel")}, " +
                          $"spinUpSeconds={PropNum(serialized, "spinUpSeconds")}, " +
                          $"spinDownSeconds={PropNum(serialized, "spinDownSeconds")}, " +
                          $"maxBarrelRpm={PropNum(serialized, "maxBarrelRpm")}");

        // The inherited ones the work order names.
        report.AppendLine($"  Identity: weaponId={PropEnum<WeaponId>(serialized, "weaponId")}, " +
                          $"weaponName='{PropString(serialized, "weaponName")}', " +
                          $"weaponAudioManager={Prop(serialized, "weaponAudioManager")}");
        report.AppendLine($"  Ballistics: fireRate={PropNum(serialized, "fireRate")}, " +
                          $"damage={PropNum(serialized, "damage")}, " +
                          $"range={PropNum(serialized, "range")}, " +
                          $"maxAmmo={PropNum(serialized, "maxAmmo")}, " +
                          $"shootableLayerMask={PropMask(serialized, "shootableLayerMask")}");
        report.AppendLine($"  Wiring: firePoint={Prop(serialized, "firePoint")}, " +
                          $"muzzleFlash={Prop(serialized, "muzzleFlash")}, " +
                          $"ammoText={Prop(serialized, "ammoText")}, " +
                          $"aimDownSightsEnabled={PropBool(serialized, "aimDownSightsEnabled")}, " +
                          $"isAutomatic={PropBool(serialized, "isAutomatic")}");

        // Not serialized, and deliberately NOT read off the component: showsReticle is
        // latched in Start from ShouldShowReticle, so out of play mode it reads false on
        // every weapon in the scene and printing it would be a lie in a diffable report.
        // MinigunController overrides ShouldShowReticle to a constant true, so the answer is
        // known at edit time without asking.
        report.AppendLine("  Reticle: YES - MinigunController overrides ShouldShowReticle to a " +
                          "constant true. Nothing to wire, and no field to check here.");

        Transform model = host.transform.Find(ModelChildName);
        if (model == null)
        {
            report.AppendLine($"  {ModelChildName} child: <none>");
        }
        else
        {
            report.AppendLine(TryGetWorldBounds(model.gameObject, out Bounds bounds)
                ? $"  {ModelChildName} child: localScale={V3(model.localScale)}, " +
                  $"world size {V3(bounds.size)} m, longest axis {Num(LongestAxis(bounds.size))} m"
                : $"  {ModelChildName} child: localScale={V3(model.localScale)}, <no renderers>");
        }
    }

    private static void AppendPickupReport(StringBuilder report)
    {
        GameObject pickup = FindByPath(PickupPath);
        if (pickup == null)
        {
            report.AppendLine($"PICKUP {PickupPath}: ABSENT");
            return;
        }

        report.AppendLine($"PICKUP {PickupPath}: PRESENT position={V3(pickup.transform.position)}, " +
                          $"layer={LayerName(pickup.layer)}, lossyScale={V3(pickup.transform.lossyScale)}, " +
                          $"activeSelf={pickup.activeSelf}");

        SphereCollider trigger = pickup.GetComponent<SphereCollider>();
        report.AppendLine(trigger == null
            ? "  SphereCollider: <none>"
            : $"  SphereCollider: radius={Num(trigger.radius)}, isTrigger={trigger.isTrigger}, " +
              $"world radius={Num(trigger.radius * MaxAbs(pickup.transform.lossyScale))}");

        RotateObject spin = pickup.GetComponent<RotateObject>();
        report.AppendLine(spin == null
            ? "  RotateObject: <none>"
            : $"  RotateObject: {Num(spin.degreesPerSecond)} deg/s");

        MeshFilter filter = pickup.GetComponent<MeshFilter>();
        MeshRenderer renderer = pickup.GetComponent<MeshRenderer>();
        report.AppendLine($"  Glow sphere: mesh={(filter != null ? Obj(filter.sharedMesh) : "<no MeshFilter>")}, " +
                          $"material={(renderer != null ? Obj(renderer.sharedMaterial) : "<no MeshRenderer>")}");

        WeaponItem item = pickup.GetComponent<WeaponItem>();
        if (item == null)
        {
            report.AppendLine("  WeaponItem: <component missing>");
            return;
        }

        SerializedObject serialized = new SerializedObject(item);
        report.AppendLine($"  WeaponItem: itemName='{PropString(serialized, "itemName")}', " +
                          $"weaponName='{PropString(serialized, "weaponName")}', " +
                          $"itemDescription='{PropString(serialized, "itemDescription")}', " +
                          $"objectiveId='{PropString(serialized, "objectiveId")}'");
        report.AppendLine($"    weaponController={Prop(serialized, "weaponController")}, " +
                          $"itemIcon={Prop(serialized, "itemIcon")}, " +
                          $"hudIcon={Prop(serialized, "hudIcon")}");
    }

    private static void AppendProfileReport(StringBuilder report)
    {
        WeaponAudioManager manager = Object.FindFirstObjectByType<WeaponAudioManager>();
        if (manager == null)
        {
            report.AppendLine("AUDIO PROFILE: <no WeaponAudioManager in the open scene>");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            report.AppendLine($"AUDIO PROFILE: WeaponAudioManager has no array field '{ProfilesField}'");
            return;
        }

        int index = FindProfileIndex(profiles);
        if (index < 0)
        {
            report.AppendLine($"AUDIO PROFILE '{ProfileName}' on '{HierarchyPath(manager.transform)}': " +
                              $"ABSENT ({profiles.arraySize} profile(s): {ProfileNames(profiles)})");
            return;
        }

        SerializedProperty profile = profiles.GetArrayElementAtIndex(index);

        report.AppendLine($"AUDIO PROFILE '{ProfileName}' on '{HierarchyPath(manager.transform)}': " +
                          $"index {index} of {profiles.arraySize} ({ProfileNames(profiles)}), " +
                          $"weaponId={PropEnum<WeaponId>(profile, "weaponId")}, " +
                          $"weaponType={PropEnum<WeaponType>(profile, "weaponType")}");

        int filled = 0;
        foreach (string field in AssessedClipFields)
        {
            SerializedProperty property = profile.FindPropertyRelative(field);
            if (property == null)
            {
                report.AppendLine($"  {field}: <no such field>");
                continue;
            }

            if (property.isArray)
            {
                if (property.arraySize > 0 &&
                    property.GetArrayElementAtIndex(0).objectReferenceValue != null)
                {
                    filled++;
                }
                report.AppendLine($"  {field}: {property.arraySize} element(s) {ArrayContents(property)}");
            }
            else
            {
                if (property.objectReferenceValue != null) filled++;
                report.AppendLine($"  {field}: {Obj(property.objectReferenceValue)}");
            }
        }

        // "Filled" here means "holds a clip", which the placeholder beep does. The Ctrl+G
        // progress report is the one that separates a beep (Demo) from authored audio; this
        // line only says whether the nine rows exist and point at something at all.
        report.AppendLine($"  ASSESSED SLOTS HOLDING A CLIP: {filled} of {AssessedClipFields.Length} " +
                          "(a placeholder beep counts here; Ctrl+G is what grades them)");
        report.AppendLine($"  Volumes: shootVolume={PropNum(profile, "shootVolume")}, " +
                          $"toggleSoundVolume={PropNum(profile, "toggleSoundVolume")}, " +
                          $"reloadVolume={PropNum(profile, "reloadVolume")}, " +
                          $"spinVolume={PropNum(profile, "spinVolume")}");
        report.AppendLine($"  Hidden by the filter for a Minigun: " +
                          $"throwSounds={ArraySize(profile.FindPropertyRelative("throwSounds"))}, " +
                          $"explosionSounds={ArraySize(profile.FindPropertyRelative("explosionSounds"))}");
    }

    // ==========================================================================
    //  2. Build
    // ==========================================================================

    /// <summary>
    /// Builds, in order, the three pieces the minigun needs outside its one script. Every
    /// step skips what already exists, so a second run changes nothing and a run that was
    /// interrupted can simply be repeated.
    /// </summary>
    [MenuItem("Tools/Weapons/Build Minigun", priority = 1111)]
    private static void BuildMinigun()
    {
        List<string> skipped = new List<string>();
        List<string> fields = new List<string>();
        List<string> notes = new List<string>();

        // Edit-mode queries read a physics scene that is only refreshed on demand; the
        // pickup placement below raycasts against it, and a stale one puts the pickup in
        // mid-air over geometry that has since moved.
        Physics.SyncTransforms();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- 1: the weapon in the hand ----
        MinigunController controller = EnsureController(skipped, fields, notes);

        // ---- 2: the pickup that selects it ----
        EnsurePickup(controller, skipped, fields, notes);

        // ---- 3: the audio profile, the reason the whole feature exists ----
        EnsureAudioProfile(skipped, fields, notes);

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Build Minigun finished. No assets were written - the FBX, its " +
                       "four URP/Lit materials and both icon sprites already existed, so every change " +
                       "below is a SCENE change.");

        log.AppendLine($"SERIALIZED FIELDS SET ({fields.Count}):");
        AppendList(log, fields, "  <none - everything was already wired>");

        log.AppendLine($"SKIPPED, ALREADY PRESENT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"Every change is Undo-recorded as one step, '{UndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or they are lost.");

        Debug.Log(log.ToString().TrimEnd());
    }

    // ==========================================================================
    //  Repair - the two numbers that changed after the gun was already built
    // ==========================================================================

    /// <summary>
    /// Applies ModelRotation and MaxAmmo to the minigun that is ALREADY in the scene, which
    /// Build Minigun would not: it skips every part it finds, so a constant that changes
    /// after the build never reaches the scene. Rebuilding instead would throw away the audio
    /// profile's clip assignments, which is the one thing in this feature that is the
    /// students' own work.
    ///
    /// Idempotent - each repair checks first and reports itself as already correct.
    /// </summary>
    [MenuItem("Tools/Weapons/Fix Minigun Orientation and Ammo", priority = 1112)]
    private static void FixMinigunOrientationAndAmmo()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(FixUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- 1: the gun in the hand ----
        GameObject host = FindByPath(ControllerPath);
        if (host == null)
        {
            notes.Add($"⚠ No '{ControllerPath}' - run Build Minigun first. Nothing was changed in " +
                      "the hand.");
        }
        else
        {
            // recentre false: the in-hand model keeps the FBX's own pivot, which is what the
            // pose offsets copied from the SMG were tuned against. Only the pickup recentres.
            TurnModel(host.transform, false, fields, skipped, notes);

            Transform firePoint = FindDeepChild(host.transform, MuzzlePointName);
            if (firePoint == null)
            {
                notes.Add($"⚠ No '{MuzzlePointName}' under {ControllerPath}, so the muzzle flash " +
                          "could not be checked. If the flash fires sideways, that is why.");
            }
            else if (Quaternion.Angle(firePoint.rotation, host.transform.rotation) < 0.5f)
            {
                skipped.Add($"{ControllerPath}/.../{MuzzlePointName} already faces downrange");
            }
            else
            {
                Undo.RecordObject(firePoint, FixUndoLabel);
                firePoint.rotation = host.transform.rotation;
                fields.Add($"{ControllerPath}/.../{MuzzlePointName} turned to the controller's own " +
                           "orientation - the muzzle flash and the tracers are parented to it and " +
                           "fire along its forward");
            }
        }

        // ---- 2: the copy on the ground ----
        GameObject pickup = FindByPath(PickupPath);
        if (pickup == null)
        {
            notes.Add($"⚠ No '{PickupPath}' - nothing was changed on the ground.");
        }
        else
        {
            // recentre true: the pickup turns on the spot inside its glow sphere, so its model
            // has to sit on the pickup's own origin however the mesh is turned.
            TurnModel(pickup.transform, true, fields, skipped, notes);
        }

        // ---- 3: the belt ----
        MinigunController controller = host != null ? host.GetComponent<MinigunController>() : null;
        if (controller == null)
        {
            notes.Add("⚠ No MinigunController, so maxAmmo was not written.");
        }
        else
        {
            SerializedObject serialized = new SerializedObject(controller);
            SerializedProperty ammo = serialized.FindProperty("maxAmmo");

            if (ammo == null)
            {
                MissingField(ControllerPath, "maxAmmo", fields);
            }
            else if (ammo.intValue == MaxAmmo)
            {
                skipped.Add($"{ControllerPath} maxAmmo is already {MaxAmmo}");
            }
            else
            {
                int was = ammo.intValue;
                ammo.intValue = MaxAmmo;
                serialized.ApplyModifiedProperties();
                fields.Add($"{ControllerPath} maxAmmo {was} -> {MaxAmmo} " +
                           $"({Num(MaxAmmo * FireRate)} s of held trigger, was {Num(was * FireRate)} s)");
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Fix Minigun Orientation and Ammo finished. Scene changes only - " +
                       "no asset was written, and the audio profile was not touched.");

        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - the scene already matched the constants>");

        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <none>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"Undo-recorded as one step, '{FixUndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or the changes are lost.");

        Debug.Log(log.ToString().TrimEnd());
    }

    /// <summary>
    /// Turns one existing Minigun_Model to <see cref="ModelRotation"/> and puts it back where
    /// it was. Rotating about the FBX's pivot moves the mesh, so the offset has to be
    /// recomputed rather than kept - which is what BuildModelChild does on a fresh build,
    /// starting from a localPosition of zero, and why this resets to zero before measuring.
    /// </summary>
    private static void TurnModel(Transform parent, bool recentre, List<string> fields,
                                  List<string> skipped, List<string> notes)
    {
        string path = HierarchyPath(parent) + "/" + ModelChildName;
        Transform model = parent.Find(ModelChildName);

        if (model == null)
        {
            notes.Add($"⚠ No '{ModelChildName}' under {HierarchyPath(parent)} - nothing to turn.");
            return;
        }

        if (Quaternion.Angle(model.localRotation, ModelRotation) < 0.5f)
        {
            skipped.Add($"{path} is already at {V3(ModelRotation.eulerAngles)}");
            return;
        }

        Undo.RecordObject(model, FixUndoLabel);

        Vector3 wasEuler = model.localRotation.eulerAngles;
        Vector3 wasPosition = model.localPosition;

        model.localPosition = Vector3.zero;
        model.localRotation = ModelRotation;

        if (recentre && TryGetWorldBounds(model.gameObject, out Bounds bounds))
        {
            // InverseTransformVector, not a subtraction: localPosition is measured in the
            // parent's space, and the pickup is neither at the world origin nor at scale 1.
            model.localPosition = parent.InverseTransformVector(parent.position - bounds.center);
        }

        fields.Add($"{path} localRotation {V3(wasEuler)} -> {V3(ModelRotation.eulerAngles)}, " +
                   $"localPosition {V3(wasPosition)} -> {V3(model.localPosition)}" +
                   (recentre ? " (recentred on the pickup's origin)" : " (the FBX's own pivot kept)"));
    }

    // ==========================================================================
    //  The owner's art for the hotbar and the weapon panel
    // ==========================================================================

    /// <summary>
    /// Imports the owner's two PNGs as UI sprites (Sprite (2D and UI), single, alpha is
    /// transparency, no mipmaps - the settings every other HUD icon in the pack uses) and
    /// wires them onto the minigun's WeaponItem: itemIcon for the hotbar, hudIcon for the
    /// bottom-right panel. Import settings are ASSET changes made through Unity's own
    /// TextureImporter, which rewrites the .meta the sanctioned way; the wiring is a scene
    /// change. Idempotent.
    /// </summary>
    [MenuItem("Tools/Weapons/Wire Minigun Icons", priority = 1114)]
    private static void WireMinigunIcons()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        Sprite icon = ImportAsUiSprite(OwnerIconPath, fields, skipped, notes);
        Sprite hudIcon = ImportAsUiSprite(OwnerHudIconPath, fields, skipped, notes);

        int wired = 0;
        foreach (WeaponItem item in Object.FindObjectsByType<WeaponItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (item.GetItemName() != ProfileName) continue;

            SerializedObject serialized = new SerializedObject(item);
            string path = HierarchyPath(item.transform);
            bool changed = false;
            changed |= WireSprite(serialized, "itemIcon", icon, path, fields, skipped);
            changed |= WireSprite(serialized, "hudIcon", hudIcon, path, fields, skipped);
            if (changed) serialized.ApplyModifiedProperties();
            wired++;
        }

        if (wired == 0) notes.Add($"⚠ No WeaponItem named '{ProfileName}' in the scene - run Build Minigun first.");

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Wire Minigun Icons finished.");
        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - already imported and wired>");
        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <none>");
        if (notes.Count > 0) { log.AppendLine($"NOTES ({notes.Count}):"); AppendList(log, notes, string.Empty); }
        log.AppendLine($"SAVE THE SCENE ({scene.name}) for the wiring; the import settings are already on disk.");
        Debug.Log(log.ToString().TrimEnd());
    }

    private static Sprite ImportAsUiSprite(string path, List<string> fields, List<string> skipped, List<string> notes)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            notes.Add($"⚠ No texture at {path}.");
            return null;
        }

        bool needs = importer.textureType != TextureImporterType.Sprite
                     || importer.spriteImportMode != SpriteImportMode.Single
                     || !importer.alphaIsTransparency
                     || importer.mipmapEnabled;
        if (needs)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            fields.Add($"{path} imported as Sprite (2D and UI), single, alpha-is-transparency, no mipmaps - an ASSET change through the importer");
        }
        else
        {
            skipped.Add($"{path} is already a UI sprite");
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) notes.Add($"⚠ {path} imported but no Sprite loads from it.");
        return sprite;
    }

    private static bool WireSprite(SerializedObject target, string field, Sprite sprite, string path,
                                   List<string> fields, List<string> skipped)
    {
        if (sprite == null) return false;
        SerializedProperty property = target.FindProperty(field);
        if (property == null) { MissingField(path, field, fields); return false; }
        if (property.objectReferenceValue == sprite) { skipped.Add($"{path} {field} already = '{sprite.name}'"); return false; }
        string was = property.objectReferenceValue == null ? "<none>" : property.objectReferenceValue.name;
        property.objectReferenceValue = sprite;
        fields.Add($"{path} {field} '{was}' -> '{sprite.name}'");
        return true;
    }

    // ==========================================================================
    //  Wire the owner's five clips into the profile's empty slots
    // ==========================================================================

    /// <summary>
    /// Fills any EMPTY minigun clip slot from the five clips the owner authored in
    /// Assets/Audio/Weapons/Minigun, matched by exact name. Never overwrites: a slot that
    /// already holds a clip - the owner's, a student's, a placeholder - is reported and left.
    /// Undo-recorded; SAVE THE SCENE afterwards.
    /// </summary>
    [MenuItem("Tools/Weapons/Wire Minigun Clips", priority = 1113)]
    private static void WireMinigunClips()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        WeaponAudioManager manager = Object.FindFirstObjectByType<WeaponAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError($"{LogPrefix} No WeaponAudioManager in the scene. Nothing was changed.");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        int index = profiles != null ? FindProfileIndex(profiles) : -1;
        if (index < 0)
        {
            Debug.LogError($"{LogPrefix} No '{ProfileName}' profile on the WeaponAudioManager - run Build Minigun first.");
            return;
        }

        SerializedProperty profile = profiles.GetArrayElementAtIndex(index);

        foreach ((string field, string clipName) in OwnerClips)
        {
            SerializedProperty slot = profile.FindPropertyRelative(field);
            if (slot == null)
            {
                MissingField(ProfileName, field, fields);
                continue;
            }

            if (slot.objectReferenceValue != null)
            {
                skipped.Add($"{field} already holds '{slot.objectReferenceValue.name}' - left alone");
                continue;
            }

            AudioClip clip = FindClipByName(clipName);
            if (clip == null)
            {
                notes.Add($"⚠ No AudioClip named '{clipName}' under {OwnerClipFolder}, so {field} stays empty.");
                continue;
            }

            slot.objectReferenceValue = clip;
            fields.Add($"{field} = '{clip.name}' ({Num(clip.length)} s)");
        }

        serialized.ApplyModifiedProperties();

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Wire Minigun Clips finished. Scene change only - no asset was written.");
        log.AppendLine($"FILLED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - every slot already held a clip>");
        log.AppendLine($"LEFT ALONE ({skipped.Count}):");
        AppendList(log, skipped, "  <none>");
        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }
        log.AppendLine($"SAVE THE SCENE ({scene.name}) or the wiring is lost.");
        Debug.Log(log.ToString().TrimEnd());
    }

    /// <summary>An AudioClip with exactly this name under the owner's folder. FindAssets matches substrings, so the name is re-checked.</summary>
    private static AudioClip FindClipByName(string clipName)
    {
        if (!AssetDatabase.IsValidFolder(OwnerClipFolder)) return null;

        foreach (string guid in AssetDatabase.FindAssets($"{clipName} t:AudioClip", new[] { OwnerClipFolder }))
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null && clip.name == clipName) return clip;
        }

        return null;
    }

    // ---- Step 1: the controller in the hand ---------------------------------

    /// <summary>
    /// The minigun in the player's hands. Its resting pose, bob, layer, active state,
    /// shootable mask and muzzle flash are all read off the SMG rather than invented: it is
    /// the closest firearm in the scene, and every controller under WeaponParent is left
    /// ACTIVE at its lowered transform with the raise/lower handled at runtime.
    /// </summary>
    private static MinigunController EnsureController(List<string> skipped, List<string> fields,
                                                      List<string> notes)
    {
        GameObject weaponParent = FindByPath(WeaponParentPath);
        if (weaponParent == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to build the controller: '{WeaponParentPath}' is " +
                             "not in the open scene - is the level loaded? Nothing was changed.");
            return null;
        }

        WeaponAudioManager manager = Object.FindFirstObjectByType<WeaponAudioManager>();
        GameObject smg = FindByPath(SmgControllerPath);
        WeaponController smgController = smg != null ? smg.GetComponent<WeaponController>() : null;

        Transform existing = weaponParent.transform.Find(ControllerName);
        if (existing != null)
        {
            skipped.Add($"controller {ControllerPath}");

            MinigunController present = existing.GetComponent<MinigunController>();
            if (present == null)
            {
                notes.Add($"⚠ '{ControllerPath}' exists but carries no MinigunController, so the " +
                          "pickup below has nothing to point at. Delete that GameObject and re-run, " +
                          "or add the component by hand.");
                return null;
            }

            RepairControllerReferences(present, manager, fields);
            return present;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to build the controller: no model at {ModelPath}. " +
                             "Nothing was changed.");
            return null;
        }

        GameObject host = new GameObject(ControllerName);
        Undo.RegisterCreatedObjectUndo(host, UndoLabel);
        host.transform.SetParent(weaponParent.transform, false);

        if (smg != null)
        {
            host.layer = smg.layer;
            host.SetActive(smg.activeSelf);
            host.transform.localPosition = smg.transform.localPosition;
            host.transform.localRotation = smg.transform.localRotation;
            host.transform.localScale = smg.transform.localScale;
            fields.Add($"{ControllerPath} transform + layer + activeSelf copied from '{SmgControllerPath}' " +
                       $"(localPosition={V3(host.transform.localPosition)}, " +
                       $"localEuler={V3(host.transform.localEulerAngles)}, " +
                       $"localScale={V3(host.transform.localScale)}, layer={LayerName(host.layer)}, " +
                       $"activeSelf={host.activeSelf})");
        }
        else
        {
            notes.Add($"⚠ '{SmgControllerPath}' is not in the scene, so the minigun's resting pose, " +
                      "bob, layer, shootable mask and muzzle flash could NOT be copied from it. The " +
                      "pose and bob fall back to the class defaults and the mask is left at Nothing - " +
                      "the gun will hit nothing at all until that is fixed.");
        }

        // The model as a CHILD rather than as the root, so the root can keep the SMG's scale
        // of 1: the pose offsets and the bob are authored against an unscaled weapon, and a
        // scaled root would quietly scale every one of them.
        GameObject visual = BuildModelChild(model, host.transform, HandMinigunLength, false,
                                            out float handScale, out Bounds handBounds);
        fields.Add($"{ControllerPath}/{ModelChildName} localScale x{Num(handScale)} -> " +
                   $"{Num(LongestAxis(handBounds.size))} m long (target {Num(HandMinigunLength)} m)");

        if (smgController != null && TryGetWorldBounds(smg, out Bounds smgBounds))
        {
            notes.Add($"For comparison, the SMG in the same hands measures " +
                      $"{Num(LongestAxis(smgBounds.size))} m along its longest axis; the minigun is " +
                      $"now {Num(HandMinigunLength)} m. Change HandMinigunLength and re-run on a " +
                      "deleted Minigun to resize.");
        }

        // The FBX's own muzzle locator, which is the whole reason not to create one.
        Transform firePoint = FindDeepChild(visual != null ? visual.transform : host.transform,
                                            MuzzlePointName);
        if (firePoint != null)
        {
            // The locator is a bone inside the model, so it inherits ModelRotation and its
            // forward now points at the gun's underside. Nothing aims by it - the shot is
            // traced from the camera (WeaponController.Shoot) - but the muzzle flash and the
            // tracers are parented to it and fire along its forward, so it is turned back to
            // face downrange. Matching the host exactly is what the SMG does: its FirePoint is
            // a plain child at identity, i.e. the controller's own orientation.
            firePoint.rotation = host.transform.rotation;

            fields.Add($"{ControllerPath} firePoint = the model's own '{MuzzlePointName}' " +
                       $"(world position {V3(firePoint.position)}), turned to the controller's " +
                       "own orientation so the flash and tracers fire downrange");
        }
        else
        {
            GameObject made = new GameObject(FirePointFallbackName);
            made.transform.SetParent(host.transform, false);
            made.transform.localPosition = new Vector3(0f, 0f, HandMinigunLength * 0.5f);
            firePoint = made.transform;

            notes.Add($"⚠ The model has no '{MuzzlePointName}', so a '{FirePointFallbackName}' child " +
                      $"was created at (0, 0, {Num(HandMinigunLength * 0.5f)}) instead. Move it to the " +
                      "muzzle by hand - it is where the flash is played from.");
        }

        ParticleSystem flash = EnsureMuzzleFlash(firePoint, smg, smgController, fields, notes);

        MinigunController controller = host.AddComponent<MinigunController>();
        SerializedObject serialized = new SerializedObject(controller);

        // Done BEFORE the explicit writes below so that whatever CopyFromSerializedProperty
        // does to the pending modification set, the minigun's own values are the last word.
        if (smgController != null)
        {
            SerializedObject source = new SerializedObject(smgController);
            List<string> copied = new List<string>();

            foreach (string name in CopiedFromSmg)
            {
                SerializedProperty from = source.FindProperty(name);
                if (from == null) continue;
                serialized.CopyFromSerializedProperty(from);
                copied.Add(name);
            }

            if (copied.Count > 0)
            {
                fields.Add($"{ControllerPath} MinigunController: {string.Join(", ", copied)} copied " +
                           $"from '{SmgControllerPath}'");
            }

            // Read rather than invented, per the work order: the mask is the list of layers a
            // bullet in this level may hit, and it is a project fact, not a weapon choice.
            SerializedProperty mask = source.FindProperty("shootableLayerMask");
            if (mask != null)
            {
                SetInt(serialized, "shootableLayerMask", mask.intValue, ControllerPath, fields,
                       $"{mask.intValue} ({MaskNames(mask.intValue)}) copied from '{SmgControllerPath}'");
            }
            else
            {
                notes.Add("⚠ The SMG has no serialized 'shootableLayerMask' to copy, so the minigun's " +
                          "is left at Nothing and every shot will pass through the world. Set it by hand.");
            }
        }

        // intValue rather than enumValueIndex: enumValueIndex is the position in the enum's
        // declaration, and this field serializes the VALUE. They agree today because
        // WeaponId is contiguous from 0, and would silently stop agreeing the moment
        // somebody assigned an explicit number.
        SetInt(serialized, "weaponId", (int)WeaponId.Minigun, ControllerPath, fields,
               $"{WeaponId.Minigun} ({(int)WeaponId.Minigun})");
        SetString(serialized, "weaponName", ProfileName, ControllerPath, fields);
        SetObject(serialized, "weaponAudioManager", manager, ControllerPath, fields);
        SetObject(serialized, "firePoint", firePoint, ControllerPath, fields);
        SetObject(serialized, "muzzleFlash", flash, ControllerPath, fields);

        SetFloat(serialized, "fireRate", FireRate, ControllerPath, fields);
        SetFloat(serialized, "damage", Damage, ControllerPath, fields);
        SetFloat(serialized, "range", Range, ControllerPath, fields);
        SetInt(serialized, "maxAmmo", MaxAmmo, ControllerPath, fields);

        // No aim pose is authored for this gun, and a minigun is not a weapon anybody sights
        // down: aiming would snap it to WeaponController's default aimPosition, which is the
        // rifle's, and drag the barrel through the camera.
        SetBool(serialized, "aimDownSightsEnabled", false, ControllerPath, fields);

        // MinigunController overrides Fire() whole and never reads this, so it changes
        // nothing today. Set anyway: it is the truth about the weapon, and if the component
        // is ever swapped for the plain WeaponController the gun would otherwise become a
        // click-per-round weapon with a 0.05 s fire rate.
        SetBool(serialized, "isAutomatic", true, ControllerPath, fields);

        // The barrel, and the three spin numbers behind it. Written explicitly rather than
        // left at the class defaults so the log carries the owner's numbers and a component
        // whose defaults have since drifted is brought back to them.
        Transform barrel = FindDeepChild(visual != null ? visual.transform : host.transform, BarrelName);
        SetObject(serialized, "barrel", barrel, ControllerPath, fields);
        if (barrel == null)
        {
            notes.Add($"⚠ The model has no '{BarrelName}', so nothing will turn. The gun still fires " +
                      "and sounds correct - MinigunController warns once at runtime and carries on.");
        }

        SetFloat(serialized, "spinUpSeconds", SpinUpSeconds, ControllerPath, fields);
        SetFloat(serialized, "spinDownSeconds", SpinDownSeconds, ControllerPath, fields);
        SetFloat(serialized, "maxBarrelRpm", MaxBarrelRpm, ControllerPath, fields);

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);

        if (manager == null)
        {
            notes.Add("⚠ No WeaponAudioManager in the open scene, so weaponAudioManager is null - " +
                      "the shots, the spin-up, the loop and the spin-down would all be silent.");
        }

        AppendStandingNotes(notes);
        return controller;
    }

    /// <summary>
    /// The three things a reader of this log needs to know that are not a field this tool
    /// sets. Added on the first build only, when the controller is actually created.
    /// </summary>
    private static void AppendStandingNotes(List<string> notes)
    {
        // The work order asks for ammoText to be left alone; this is the verification of why.
        notes.Add("ammoText is deliberately LEFT UNASSIGNED, and that costs nothing. " +
                  "MinigunController.FireOneRound spends its rounds through the base's " +
                  "TrySpendRound, which calls WeaponController.UpdateAmmoUI; UpdateAmmoUI raises " +
                  "OnHudStateChanged FIRST and only then early-returns on a null ammoText. " +
                  "MinigunController overrides none of UsesMagazine / CurrentAmmo / MaxAmmo, so " +
                  "the bottom-right CurrentWeaponHUD panel reads the base magazine and shows " +
                  $"'{MaxAmmo}' counting down. The legacy 'Ammo Readout' text is the older display " +
                  "and is shared by every gun. ⚠ Note that Tools/Weapons/Create Ammo Readout wires " +
                  "ammoText on EVERY weapon whose UsesMagazine is true, minigun included - if the " +
                  "owner re-runs it, this field fills itself in and the readout simply also works.");

        notes.Add("The crosshair needs no wiring: MinigunController overrides ShouldShowReticle to " +
                  "a constant true, which is how it gets one without a third weaponName.Contains " +
                  "test in the base. There is no serialized field to set.");

        notes.Add("⚠ NOT SET, and worth an owner pass in play: recoil and camera shake are left at " +
                  "WeaponController's class defaults (kickbackDistance 0.1, recoilRotation 10, " +
                  "returnSpeed 10, shake 0.2 s / 0.05 / 10). Those are tuned for a weapon firing " +
                  $"every 0.1-0.5 s; this one fires every {Num(FireRate)} s, so the kick never " +
                  "recovers between rounds and the gun will read as jammed nose-up. The SMG's own " +
                  "numbers (0.16 / 10 / 7.57, shake 0.2 / 0.28 / 5.6) are the place to start, scaled " +
                  "down. Deliberately not copied: the work order lists the fields to set, and " +
                  "guessing at feel is the owner's ⏸ checkpoint, not this tool's.");

        notes.Add($"⚠ The model child is parented at localRotation identity. The FBX's own forward " +
                  "may not be the barrel's forward, and MinigunController turns the barrel about its " +
                  $"local Z. If the gun points sideways in the hands, or the barrel spins about the " +
                  $"wrong axis, rotate '{ModelChildName}' - not the '{ControllerName}' root, whose " +
                  "rotation is the raise/lower pose and is driven every frame.");
    }

    /// <summary>
    /// Gives the minigun the same muzzle flash the SMG has. The SMG's is a SCENE instance
    /// (an unpacked copy of FX_Gunshot_Heavy_Single_Tracers_01, parented under its FirePoint,
    /// with the WeaponController pointing at the FX_Gunshot_Tracer_Single_01 ParticleSystem
    /// inside it) rather than a reference to the prefab asset, so the equivalent thing is to
    /// duplicate that scene subtree - which also carries any tweak the owner has made to it.
    /// </summary>
    private static ParticleSystem EnsureMuzzleFlash(Transform firePoint, GameObject smg,
                                                    WeaponController smgController,
                                                    List<string> fields, List<string> notes)
    {
        if (firePoint == null) return null;

        ParticleSystem source = null;
        if (smgController != null)
        {
            SerializedProperty property = new SerializedObject(smgController).FindProperty("muzzleFlash");
            if (property != null) source = property.objectReferenceValue as ParticleSystem;
        }

        if (source == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuzzleFlashPrefabPath);
            if (prefab == null)
            {
                notes.Add("⚠ muzzleFlash is LEFT NULL: the SMG has none to copy and there is no " +
                          $"prefab at {MuzzleFlashPrefabPath}. The gun fires and sounds correct with " +
                          "nothing at the muzzle. Drag a particle system into Muzzle Flash by hand.");
                return null;
            }

            GameObject fromPrefab = PrefabUtility.InstantiatePrefab(prefab, firePoint) as GameObject;
            if (fromPrefab == null) return null;

            Undo.RegisterCreatedObjectUndo(fromPrefab, UndoLabel);
            fromPrefab.transform.localPosition = Vector3.zero;
            fromPrefab.transform.localRotation = Quaternion.identity;

            ParticleSystem fallback = fromPrefab.GetComponentInChildren<ParticleSystem>(true);
            fields.Add($"{ControllerPath} muzzle flash = a new instance of {MuzzleFlashPrefabPath} " +
                       $"under '{firePoint.name}' (the SMG had none to copy)");
            return fallback;
        }

        // A persistent object is a prefab asset the SMG references directly; the equivalent
        // thing is then to reference the same asset, not to put a copy in the scene.
        if (EditorUtility.IsPersistent(source))
        {
            fields.Add($"{ControllerPath} muzzleFlash = the SAME prefab asset the SMG references " +
                       $"('{source.name}' at {AssetDatabase.GetAssetPath(source)}) - it is an asset " +
                       "reference on the SMG, so it is one here too");
            return source;
        }

        // Nothing to do on a re-run: the FX is already under the fire point.
        Transform already = firePoint.Find(FxRootUnder(source.transform, smg).name);
        if (already != null)
        {
            ParticleSystem existing = FindParticleSystemNamed(already, source.name);
            if (existing != null) return existing;
        }

        Transform fxRoot = FxRootUnder(source.transform, smg);

        GameObject copy = Object.Instantiate(fxRoot.gameObject, firePoint);
        Undo.RegisterCreatedObjectUndo(copy, UndoLabel);
        copy.name = fxRoot.name;
        copy.transform.localPosition = fxRoot.localPosition;
        copy.transform.localRotation = fxRoot.localRotation;

        // ⚠ The minigun's fire point lives INSIDE the scaled model child, where the SMG's is
        // a child of an unscaled weapon root. Left alone the flash would come out at the
        // model's scale factor. Matching world scale instead keeps the flash the size the
        // owner authored it, on a gun that is a different size.
        copy.transform.localScale = DivideComponents(fxRoot.lossyScale, firePoint.lossyScale);

        ParticleSystem flash = FindParticleSystemNamed(copy.transform, source.name);
        if (flash == null) flash = copy.GetComponentInChildren<ParticleSystem>(true);

        fields.Add($"{ControllerPath} muzzle flash = a copy of the SMG's own scene FX " +
                   $"'{fxRoot.name}' parented under '{firePoint.name}' " +
                   $"(localScale {V3(copy.transform.localScale)}, chosen so its WORLD scale matches " +
                   $"the SMG's {V3(fxRoot.lossyScale)}); muzzleFlash points at " +
                   $"'{(flash != null ? flash.name : "<none found>")}' inside it");

        if (flash == null)
        {
            notes.Add("⚠ The copied muzzle-flash subtree has no ParticleSystem in it, so muzzleFlash " +
                      "is null. Point it at one by hand.");
        }

        return flash;
    }

    /// <summary>
    /// The top of the SMG's muzzle-flash subtree: climbs from the ParticleSystem the SMG
    /// references until the next step up would leave the FX and land on the weapon's fire
    /// point (or on the weapon itself). Duplicating that node rather than the leaf keeps
    /// whatever sibling emitters the effect is built from.
    /// </summary>
    private static Transform FxRootUnder(Transform flash, GameObject smg)
    {
        Transform smgRoot = smg != null ? smg.transform : null;
        Transform current = flash;

        while (current.parent != null && current.parent != smgRoot && IsDescendantOf(current.parent, smgRoot))
        {
            // The fire point is the SMG's own child, and it is not part of the effect.
            if (smgRoot != null && current.parent.parent == smgRoot) break;
            current = current.parent;
        }

        return current;
    }

    private static bool IsDescendantOf(Transform candidate, Transform root)
    {
        if (root == null) return false;

        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current == root) return true;
        }

        return false;
    }

    private static ParticleSystem FindParticleSystemNamed(Transform root, string name)
    {
        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (system.gameObject.name == name) return system;
        }
        return null;
    }

    /// <summary>
    /// Re-wires only the references that are missing on a controller this tool built
    /// earlier. Anything already pointing somewhere is an authored choice and is left alone,
    /// which is what makes a second run a no-op.
    /// </summary>
    private static void RepairControllerReferences(MinigunController controller,
                                                   WeaponAudioManager manager, List<string> fields)
    {
        SerializedObject serialized = new SerializedObject(controller);
        bool changed = false;

        changed |= FillIfEmpty(serialized, "weaponAudioManager", manager, ControllerPath, fields);

        Transform model = controller.transform.Find(ModelChildName);
        Transform search = model != null ? model : controller.transform;

        changed |= FillIfEmpty(serialized, "firePoint", FindDeepChild(search, MuzzlePointName),
                               ControllerPath, fields);
        changed |= FillIfEmpty(serialized, "barrel", FindDeepChild(search, BarrelName),
                               ControllerPath, fields);

        if (changed)
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }
    }

    // ---- Step 2: the world pickup -------------------------------------------

    /// <summary>
    /// One minigun on the ground, near where the player starts - the owner's placement
    /// decision. Shaped off the SMG pickup rather than typed out, so it reads as the same
    /// kind of thing as everything else on the pickup shelf. The ground snap only stops it
    /// spawning in mid-air or inside the terrain; the spot is a starting point the owner
    /// moves.
    /// </summary>
    private static void EnsurePickup(MinigunController controller, List<string> skipped,
                                     List<string> fields, List<string> notes)
    {
        GameObject pickups = FindByPath(PickupsPath);
        if (pickups == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to build the pickup: '{PickupsPath}' is not in the " +
                             "open scene. The controller above was still built.");
            return;
        }

        if (pickups.transform.Find(PickupName) != null)
        {
            skipped.Add($"pickup {PickupPath}");
            return;
        }

        GameObject smgPickup = FindByPath(SmgPickupPath);
        GameObject player = FindByPath(PlayerPath);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        Sprite hudIcon = AssetDatabase.LoadAssetAtPath<Sprite>(HudIconPath);

        if (icon == null || hudIcon == null)
        {
            notes.Add($"⚠ Missing icon sprite: itemIcon={(icon != null ? "ok" : IconPath)}, " +
                      $"hudIcon={(hudIcon != null ? "ok" : HudIconPath)}. A blank shows in the hotbar " +
                      "or the current-weapon panel. Check the InterfaceApocalypseHUD pack imported, " +
                      "and that the .png's Texture Type is Sprite.");
        }

        if (controller == null)
        {
            notes.Add("⚠ The pickup was built with weaponController EMPTY - step 1 did not produce a " +
                      "MinigunController. Picking it up would log 'No weapon controller found' and " +
                      "select nothing. Fix step 1 and re-run, or point the item at the controller " +
                      "by hand.");
        }

        if (player == null)
        {
            notes.Add($"⚠ '{PlayerPath}' is not in the scene, so the pickup could not be placed near " +
                      "the spawn and sits at the world origin instead. Move it by hand.");
        }

        Vector3 anchor = player != null ? player.transform.position : Vector3.zero;
        float hover = HoverHeight(smgPickup);
        Vector3 position = GroundedSpotNear(anchor, PickupOffsetFromPlayer, hover, out string how);

        GameObject pickup = new GameObject(PickupName);
        Undo.RegisterCreatedObjectUndo(pickup, UndoLabel);
        pickup.transform.SetParent(pickups.transform, false);
        pickup.transform.position = position;

        CopyPickupShell(pickup, smgPickup, fields, notes);

        GameObject visual = BuildModelChild(model, pickup.transform, WorldMinigunLength, true,
                                            out float worldScale, out Bounds worldBounds);
        if (model == null)
        {
            notes.Add($"⚠ No model at {ModelPath}, so the pickup is a bare glow sphere with no gun " +
                      "inside it. It is still walkable.");
        }
        else
        {
            fields.Add($"{PickupPath}/{ModelChildName} localScale x{Num(worldScale)} -> " +
                       $"{Num(LongestAxis(worldBounds.size))} m long (target {Num(WorldMinigunLength)} m), " +
                       "recentred on the pickup's own origin so it turns on the spot");
        }

        WeaponItem item = pickup.AddComponent<WeaponItem>();
        SerializedObject serialized = new SerializedObject(item);

        SetString(serialized, "itemName", ProfileName, PickupPath, fields);

        // WeaponItem overrides GetItemName to return THIS field, not Item's itemName, so a
        // blank here is what makes a warning read "No weapon controller found for ". The SMG
        // pickup ships blank; the minigun does not repeat that.
        SetString(serialized, "weaponName", ProfileName, PickupPath, fields);

        SetString(serialized, "itemDescription", "Six barrels, 200 rounds, and a second of " +
                                                 "spin-up before the first one leaves.", PickupPath, fields);
        SetObject(serialized, "itemIcon", icon, PickupPath, fields);
        SetObject(serialized, "hudIcon", hudIcon, PickupPath, fields);
        SetObject(serialized, "weaponController", controller, PickupPath, fields);

        // objectiveId is left blank on purpose: the SMG's "CollectYourSMG" is a scripted
        // objective the ObjectiveManager knows about, and inventing an id here would raise a
        // "Failed to complete objective" the first time the minigun is picked up.
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(item);

        notes.Add($"⚠ The two icons are STAND-INS: the Apocalypse HUD pack ships no minigun icon, so " +
                  $"the pickup uses '{IconName}' (and its '_Side' view for the current-weapon panel). " +
                  "It is an assault rifle. The closest alternative with a matching _Side pair was " +
                  "ICON_SM_Wep_Grenade_Launcher_01, which reads as a single-shot tube and would sit " +
                  "confusingly beside the grenade in the hotbar. Swap both sprites when a real one " +
                  "exists - nothing depends on the name.");

        fields.Add($"{PickupPath} placed at {V3(position)} - {how}. " +
                   $"Anchor: '{PlayerPath}' at {V3(anchor)}, the owner's 'near the player spawn'.");

        notes.Add("objectiveId left blank: the SMG pickup's 'CollectYourSMG' is a real objective the " +
                  "ObjectiveManager knows, and an invented id would log a failure on first pickup.");
    }

    /// <summary>
    /// Everything a pickup in this scene is besides its Item component: the layer, the
    /// uniform scale, the little glow-sphere mesh with its transparent material, the trigger,
    /// and the slow spin. Copied off the SMG pickup rather than typed out.
    /// </summary>
    private static void CopyPickupShell(GameObject pickup, GameObject smgPickup,
                                        List<string> fields, List<string> notes)
    {
        MeshFilter sourceFilter = smgPickup != null ? smgPickup.GetComponent<MeshFilter>() : null;
        MeshRenderer sourceRenderer = smgPickup != null ? smgPickup.GetComponent<MeshRenderer>() : null;
        SphereCollider sourceTrigger = smgPickup != null ? smgPickup.GetComponent<SphereCollider>() : null;
        RotateObject sourceSpin = smgPickup != null ? smgPickup.GetComponent<RotateObject>() : null;

        if (smgPickup != null)
        {
            pickup.layer = smgPickup.layer;
            pickup.transform.localScale = smgPickup.transform.localScale;
        }
        else
        {
            notes.Add($"⚠ '{SmgPickupPath}' is not in the scene, so '{pickup.name}' could not copy its " +
                      "layer, scale, glow sphere or trigger size and falls back to defaults. It will " +
                      "look wrong next to the others.");
            pickup.layer = LayerMask.NameToLayer("TriggerBoxes");
            pickup.transform.localScale = Vector3.one * 0.47f;
        }

        if (sourceFilter != null && sourceFilter.sharedMesh != null && sourceRenderer != null)
        {
            pickup.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            pickup.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
        }

        SphereCollider trigger = pickup.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        if (sourceTrigger != null)
        {
            trigger.radius = sourceTrigger.radius;
            trigger.center = sourceTrigger.center;
        }
        else
        {
            trigger.radius = 0.5f;
        }

        RotateObject spin = pickup.AddComponent<RotateObject>();
        if (sourceSpin != null) spin.degreesPerSecond = sourceSpin.degreesPerSecond;

        fields.Add($"{PickupPath} shell: layer={LayerName(pickup.layer)}, " +
                   $"localScale={V3(pickup.transform.localScale)}, " +
                   $"SphereCollider(isTrigger=true, radius={Num(trigger.radius)}, " +
                   $"world radius={Num(trigger.radius * MaxAbs(pickup.transform.lossyScale))}), " +
                   $"RotateObject({Num(spin.degreesPerSecond)} deg/s)" +
                   (sourceFilter != null ? ", glow sphere mesh + material copied from the SMG pickup" : ""));
    }

    /// <summary>
    /// How far the SMG pickup floats above the ground under it, so the minigun floats the
    /// same. A pickup sitting exactly on the terrain looks dropped rather than placed.
    /// </summary>
    private static float HoverHeight(GameObject smgPickup)
    {
        if (smgPickup == null) return DefaultHover;

        Vector3 origin = smgPickup.transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Clamp(smgPickup.transform.position.y - hit.point.y, 0.2f, 2f);
        }

        return DefaultHover;
    }

    /// <summary>
    /// A spot near the anchor that is on the ground and not inside anything. Eight compass
    /// directions rather than one, because the spawn has scenery around it and a pickup
    /// buried in rock cannot be walked into - which is the whole point of it.
    /// </summary>
    private static Vector3 GroundedSpotNear(Vector3 anchor, float offset, float hover, out string how)
    {
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 candidate = anchor + direction * offset;

            if (!Physics.Raycast(candidate + Vector3.up * RayStartHeight, Vector3.down,
                                 out RaycastHit hit, RayLength, ~0, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 spot = hit.point + Vector3.up * hover;
            if (Physics.CheckSphere(spot, ClearRadius, ~0, QueryTriggerInteraction.Ignore)) continue;

            how = $"{Num(offset)} m along {V3(direction)} from the anchor, dropped onto " +
                  $"'{hit.collider.name}' at {V3(hit.point)} and floated {Num(hover)} m";
            return spot;
        }

        how = $"⚠ no clear ground within {Num(offset)} m of the anchor in any of eight directions - " +
              "left hanging at the anchor's own height; move it by hand";
        return anchor + Vector3.forward * offset + Vector3.up * hover;
    }

    // ---- Step 3: the audio profile ------------------------------------------

    /// <summary>
    /// Appends the Minigun profile to the WeaponAudioManager. This is the step the whole
    /// feature exists for: it is what puts the nine assessed slots into the Ctrl+G report.
    /// </summary>
    private static void EnsureAudioProfile(List<string> skipped, List<string> fields, List<string> notes)
    {
        WeaponAudioManager manager = Object.FindFirstObjectByType<WeaponAudioManager>();
        if (manager == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to add the audio profile: no WeaponAudioManager in " +
                             "the open scene. Everything else was still built.");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty(ProfilesField);
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogWarning($"{LogPrefix} WeaponAudioManager has no array field '{ProfilesField}' - it " +
                             "has been renamed, and this tool binds to it by name. No profile was added.");
            return;
        }

        int existing = FindProfileIndex(profiles);
        if (existing >= 0)
        {
            skipped.Add($"audio profile '{ProfileName}' at index {existing} of {profiles.arraySize}");
            return;
        }

        AudioClip beep = AssetDatabase.LoadAssetAtPath<AudioClip>(BeepClipPath);
        if (beep == null)
        {
            notes.Add($"⚠ No placeholder beep at {BeepClipPath}, so the two minigun ARRAYS were sized " +
                      "to 1 with an EMPTY element and the seven single-clip slots were left null. All " +
                      "nine rows still appear in the report, but as missing audio rather than as " +
                      "placeholders - put the clip back and re-run on a deleted profile.");
        }

        int index = profiles.arraySize;
        profiles.arraySize = index + 1;

        SerializedProperty profile = profiles.GetArrayElementAtIndex(index);
        WriteProfile(profile, beep, $"{ProfilesField}[{index}]", fields);

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        Scene scene = manager.gameObject.scene;
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        fields.Add($"'{HierarchyPath(manager.transform)}' {ProfilesField}: appended at INDEX {index} " +
                   $"(array is now {profiles.arraySize} long: {ProfileNames(profiles)})");
    }

    /// <summary>
    /// Writes every field of the new profile, including the ones being set to nothing.
    /// </summary>
    /// <remarks>
    /// ⚠ Growing a SerializedProperty array COPIES the previous last element into the new
    /// one, so anything left unwritten here would silently inherit the profile before it -
    /// which is the Grenade's, and the minigun would ship holding its throw and its blast.
    /// GrenadeSetupTools hit exactly this. Every one of WeaponSoundProfile's twenty-four
    /// serialized fields is therefore assigned below, the empty ones included.
    ///
    /// ⚠⚠ shootSounds and reloadActionSounds are sized to ONE and filled with the placeholder
    /// beep, deliberately. DistributionResetTools walks existing AudioClip properties and
    /// points each at the beep; it does not GROW an array, so a slot shipped at size 0 stays
    /// at size 0 through the handout reset and two of the minigun's nine assessed rows would
    /// simply vanish from the student's report. Size 1 + beep is what every other unfilled
    /// slot in this project looks like.
    ///
    /// throwSounds and explosionSounds are the two slots WeaponSoundProfile.IsAudioSlotRelevant
    /// hides for a Minigun, so they are emptied rather than seeded: a clip there would be a
    /// sound in the scene that no report ever mentions and no student is ever asked to author.
    /// </remarks>
    private static void WriteProfile(SerializedProperty profile, AudioClip beep, string path,
                                     List<string> fields)
    {
        SetProfileString(profile, "weaponName", ProfileName, path, fields);
        SetProfileInt(profile, "weaponId", (int)WeaponId.Minigun, path, fields,
                      $"{WeaponId.Minigun} ({(int)WeaponId.Minigun})");
        SetProfileInt(profile, "weaponType", (int)WeaponType.Minigun, path, fields,
                      $"{WeaponType.Minigun} ({(int)WeaponType.Minigun})");

        // 1 of 9.
        // (shootSounds: cleared below - see the note beside the five.)
        SetProfileFloat(profile, "shootVolume", 1f, path, fields);
        SetProfileFloat(profile, "minPitchVariation", 0.95f, path, fields);
        SetProfileFloat(profile, "maxPitchVariation", 1.05f, path, fields);

        // 2 and 3 of 9.
        SetProfileObject(profile, "raiseWeaponSound", beep, path, fields);
        SetProfileObject(profile, "lowerWeaponSound", beep, path, fields);
        SetProfileFloat(profile, "toggleSoundVolume", 1f, path, fields);
        SetProfileFloat(profile, "togglePitchVariation", 0.1f, path, fields);

        // 4, 5 and 6 of 9.
        SetProfileObject(profile, "reloadStartSound", beep, path, fields);
        SetProfileObject(profile, "reloadEndSound", beep, path, fields);
        SetProfileClip(profile, "reloadActionSounds", beep, path, fields);
        SetProfileFloat(profile, "reloadVolume", 1f, path, fields);

        // Hidden by the filter for a Minigun, but written anyway so nothing is inherited.
        ClearProfileClips(profile, "throwSounds", path, fields);
        SetProfileFloat(profile, "throwVolume", 1f, path, fields);
        ClearProfileClips(profile, "explosionSounds", path, fields);
        SetProfileFloat(profile, "explosionVolume", 1f, path, fields);
        SetProfileFloat(profile, "explosionMaxDistance", 80f, path, fields);

        // The five this weapon type exists for. shootSounds is NOT seeded: since 2026-09-06
        // the minigun has no per-round shot (the shooting loop is the gunfire), the filter
        // hides the slot, and a clip in a hidden slot is a placeholder nobody can find.
        ClearProfileClips(profile, "shootSounds", path, fields);
        SetProfileObject(profile, "spinWindupSound", beep, path, fields);
        SetProfileObject(profile, "spinWinddownSound", beep, path, fields);
        SetProfileObject(profile, "spinLoopSound", beep, path, fields);
        SetProfileObject(profile, "shootingStartupSound", beep, path, fields);
        SetProfileObject(profile, "shootingLoopSound", beep, path, fields);
        SetProfileObject(profile, "shootingShutdownSound", beep, path, fields);
        SetProfileFloat(profile, "spinVolume", 1f, path, fields);
    }

    /// <summary>The index of the Minigun profile, by id or by name. -1 when there is none.</summary>
    private static int FindProfileIndex(SerializedProperty profiles)
    {
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);

            SerializedProperty id = profile.FindPropertyRelative("weaponId");
            if (id != null && id.intValue == (int)WeaponId.Minigun) return i;

            SerializedProperty name = profile.FindPropertyRelative("weaponName");
            if (name == null) continue;
            if (string.Equals(name.stringValue.Trim(), ProfileName, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ProfileNames(SerializedProperty profiles)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty name = profiles.GetArrayElementAtIndex(i).FindPropertyRelative("weaponName");
            names.Add(name != null ? $"{i}:{name.stringValue.Trim()}" : $"{i}:<no weaponName>");
        }
        return string.Join(", ", names);
    }

    // ==========================================================================
    //  The model child
    // ==========================================================================

    /// <summary>
    /// The minigun mesh as a child, sized to a real-world length. A CHILD rather than the
    /// root so the root can stay at the SMG's scale: the pose offsets, the bob and the
    /// pickup's trigger all live on the root, and a scaled root would scale them too.
    /// </summary>
    /// <param name="targetLength">Longest axis in WORLD metres - the parent's own scale is
    /// already in the measurement, so a pickup scaled to 0.47 still gets a gun of the size
    /// asked for.</param>
    /// <param name="recentre">True for the pickup, where the gun should turn about its own
    /// middle inside the glow sphere. False in the hand, where the FBX's own pivot is what
    /// the pose was authored against.</param>
    /// <param name="worldBounds">The model's bounds in WORLD space after the scaling.</param>
    private static GameObject BuildModelChild(GameObject model, Transform parent, float targetLength,
                                              bool recentre, out float scaleFactor, out Bounds worldBounds)
    {
        scaleFactor = 1f;
        worldBounds = new Bounds(parent.position, Vector3.zero);

        // A missing model is reported by the caller that needed it; here it just means there
        // is no child to build, and the piece is still worth assembling without one.
        if (model == null) return null;

        GameObject visual = Object.Instantiate(model);
        visual.name = ModelChildName;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;

        // Not identity - see ModelRotation. Set BEFORE the bounds are measured, so the scale
        // and the recentring below are computed against the model as it will actually sit.
        // (A 90 degree turn only permutes an axis-aligned box, so the longest axis, and
        // therefore the scale factor, comes out the same either way; the recentring offset
        // does not.)
        visual.transform.localRotation = ModelRotation;
        visual.transform.localScale = Vector3.one;

        // Whatever the FBX importer added is not wanted on a weapon in the hand or on a
        // trigger volume: the pickup's own SphereCollider is the trigger, and a collider on
        // the gun would block it.
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }
        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))
        {
            Object.DestroyImmediate(body);
        }
        foreach (Animator animator in visual.GetComponentsInChildren<Animator>(true))
        {
            Object.DestroyImmediate(animator);
        }
        foreach (LODGroup group in visual.GetComponentsInChildren<LODGroup>(true))
        {
            Object.DestroyImmediate(group);
        }

        if (!TryGetWorldBounds(visual, out Bounds bounds) || LongestAxis(bounds.size) <= 0.0001f)
        {
            return visual;
        }

        // Renderer bounds scale exactly with the transform, so one measurement is enough: the
        // child sits at the parent's own origin, so both its size and its offset from that
        // origin come out multiplied by the same factor.
        scaleFactor = targetLength / LongestAxis(bounds.size);
        visual.transform.localScale *= scaleFactor;

        Vector3 centre = parent.position + (bounds.center - parent.position) * scaleFactor;
        worldBounds = new Bounds(centre, bounds.size * scaleFactor);

        if (recentre)
        {
            // InverseTransformVector rather than a raw subtraction: the parent is neither at
            // the world origin nor unrotated nor at scale 1, and localPosition is measured in
            // the parent's own space.
            visual.transform.localPosition += parent.InverseTransformVector(parent.position - centre);
            worldBounds = new Bounds(parent.position, bounds.size * scaleFactor);
        }

        return visual;
    }

    // ==========================================================================
    //  Serialized-property writers - each one logs what it set
    // ==========================================================================

    private static void SetString(SerializedObject target, string name, string value,
                                  string path, List<string> fields)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.stringValue = value;
        fields.Add($"{path} {name} = '{value}'");
    }

    private static void SetInt(SerializedObject target, string name, int value,
                               string path, List<string> fields, string display = null)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.intValue = value;
        fields.Add($"{path} {name} = {display ?? value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void SetFloat(SerializedObject target, string name, float value,
                                 string path, List<string> fields)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.floatValue = value;
        fields.Add($"{path} {name} = {Num(value)}");
    }

    private static void SetBool(SerializedObject target, string name, bool value,
                                string path, List<string> fields)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.boolValue = value;
        fields.Add($"{path} {name} = {value}");
    }

    private static void SetObject(SerializedObject target, string name, Object value,
                                  string path, List<string> fields)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.objectReferenceValue = value;
        fields.Add($"{path} {name} = {Obj(value)}");
    }

    /// <summary>Writes only into an empty slot, so a re-run never overwrites a choice.</summary>
    private static bool FillIfEmpty(SerializedObject target, string name, Object value,
                                    string path, List<string> fields)
    {
        if (value == null) return false;

        SerializedProperty property = target.FindProperty(name);
        if (property == null || property.objectReferenceValue != null) return false;

        property.objectReferenceValue = value;
        fields.Add($"{path} {name} = {Obj(value)} (repaired - it was null)");
        return true;
    }

    private static void SetProfileString(SerializedProperty profile, string name, string value,
                                         string path, List<string> fields)
    {
        SerializedProperty property = profile.FindPropertyRelative(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.stringValue = value;
        fields.Add($"{path}.{name} = '{value}'");
    }

    private static void SetProfileInt(SerializedProperty profile, string name, int value,
                                      string path, List<string> fields, string display = null)
    {
        SerializedProperty property = profile.FindPropertyRelative(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.intValue = value;
        fields.Add($"{path}.{name} = {display ?? value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void SetProfileFloat(SerializedProperty profile, string name, float value,
                                        string path, List<string> fields)
    {
        SerializedProperty property = profile.FindPropertyRelative(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.floatValue = value;
        fields.Add($"{path}.{name} = {Num(value)}");
    }

    private static void SetProfileObject(SerializedProperty profile, string name, Object value,
                                         string path, List<string> fields)
    {
        SerializedProperty property = profile.FindPropertyRelative(name);
        if (property == null) { MissingField(path, name, fields); return; }
        property.objectReferenceValue = value;
        fields.Add(value != null
            ? $"{path}.{name} = {Obj(value)} ({BeepClipPath})"
            : $"{path}.{name} = <none>");
    }

    /// <summary>Empties a clip array - for the slots this weapon type can never play.</summary>
    private static void ClearProfileClips(SerializedProperty profile, string name,
                                          string path, List<string> fields)
    {
        SerializedProperty property = profile.FindPropertyRelative(name);
        if (property == null || !property.isArray) { MissingField(path, name, fields); return; }

        property.arraySize = 0;
        fields.Add($"{path}.{name} = 0 element(s)");
    }

    /// <summary>
    /// Sizes a clip array to exactly one and puts the given clip in it. Size 1 even when the
    /// clip could not be loaded: an element holding nothing still shows in the Ctrl+G report
    /// as a slot awaiting audio, where an array of size 0 shows as nothing at all. Assigns
    /// rather than appends, so a re-run could never grow the array.
    /// </summary>
    private static void SetProfileClip(SerializedProperty profile, string name, AudioClip clip,
                                       string path, List<string> fields)
    {
        SerializedProperty property = profile.FindPropertyRelative(name);
        if (property == null || !property.isArray) { MissingField(path, name, fields); return; }

        property.arraySize = 1;
        property.GetArrayElementAtIndex(0).objectReferenceValue = clip;

        fields.Add(clip != null
            ? $"{path}.{name} = 1 element(s), [0] = '{clip.name}' ({BeepClipPath})"
            : $"⚠ {path}.{name} = 1 element(s), [0] = <none> (the placeholder beep would not load)");
    }

    private static void MissingField(string path, string name, List<string> fields)
    {
        fields.Add($"⚠ {path} has no serialized field '{name}' - it has been renamed, and this tool " +
                   "binds to it by name. NOT SET.");
    }

    // ==========================================================================
    //  Shared helpers
    // ==========================================================================

    /// <summary>
    /// A GameObject by hierarchy path in the active scene. Transform.Find rather than
    /// GameObject.Find, because the latter cannot see an inactive object and half the things
    /// this tool looks for could legitimately be switched off.
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

            // A second root of the same name may still hold the rest of the path.
            if (current != null) return current.gameObject;
        }

        return null;
    }

    /// <summary>
    /// A descendant by name at any depth, including the node itself. By name rather than by
    /// path because the FBX importer is free to re-parent as it likes - the model's own
    /// hierarchy is not this tool's to assume, only its node names are.
    /// </summary>
    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name) return child;
        }

        return null;
    }

    /// <summary>Combined world bounds of every renderer under the object. False when it has none.</summary>
    private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(root.transform.position, Vector3.zero);

        // From the MESHES, not from Renderer.bounds, for two independent reasons - both of
        // which bit this tool on its first run and produced a minigun 18 m long:
        //
        // 1. Renderer.bounds is a cached world AABB the engine refreshes when it draws. On a
        //    GameObject instantiated moments ago in EDIT mode it has not been refreshed yet,
        //    so it reads near-zero; dividing a target length by that gives an enormous scale.
        // 2. It also includes ParticleSystemRenderers. The weapons here carry a muzzle flash
        //    whose tracers span the level, so measuring an SMG this way reports ~18 m of
        //    tracer rather than ~0.4 m of gun.
        //
        // Mesh.bounds is authored data on the asset: never stale, never a particle system.
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
    /// Per-component division with a guard: a zero anywhere in a lossyScale would otherwise
    /// come back as an infinity and put the effect at an unrenderable scale.
    /// </summary>
    private static Vector3 DivideComponents(Vector3 numerator, Vector3 denominator)
    {
        return new Vector3(
            Mathf.Approximately(denominator.x, 0f) ? numerator.x : numerator.x / denominator.x,
            Mathf.Approximately(denominator.y, 0f) ? numerator.y : numerator.y / denominator.y,
            Mathf.Approximately(denominator.z, 0f) ? numerator.z : numerator.z / denominator.z);
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

    private static string V3(Vector3 value) => $"({Num(value.x)}, {Num(value.y)}, {Num(value.z)})";

    private static string Obj(Object value) => value == null ? "<none>" : $"'{value.name}'";

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? $"<unnamed>({layer})" : $"{name}({layer})";
    }

    /// <summary>The layers a mask lets through, spelled out - "987" tells nobody anything.</summary>
    private static string MaskNames(int bits)
    {
        if (bits == 0) return "Nothing";

        List<string> names = new List<string>();
        for (int i = 0; i < 32; i++)
        {
            if ((bits & (1 << i)) == 0) continue;

            string name = LayerMask.LayerToName(i);
            names.Add(string.IsNullOrEmpty(name) ? i.ToString(CultureInfo.InvariantCulture) : name);
        }

        return names.Count == 0 ? "Nothing" : string.Join(" | ", names);
    }

    private static float LongestAxis(Vector3 size) => Mathf.Max(size.x, Mathf.Max(size.y, size.z));

    private static float MaxAbs(Vector3 scale) =>
        Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

    // ---- Report formatters ---------------------------------------------------

    private static string Prop(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        return property == null ? "<no such field>" : Obj(property.objectReferenceValue);
    }

    private static string Prop(SerializedProperty parent, string name)
    {
        SerializedProperty property = parent.FindPropertyRelative(name);
        return property == null ? "<no such field>" : Obj(property.objectReferenceValue);
    }

    private static string PropString(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        return property == null ? "<no such field>" : property.stringValue;
    }

    private static string PropBool(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        return property == null ? "<no such field>" : property.boolValue.ToString();
    }

    private static string PropMask(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) return "<no such field>";
        return $"{property.intValue} ({MaskNames(property.intValue)})";
    }

    private static string PropNum(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) return "<no such field>";
        return property.propertyType == SerializedPropertyType.Integer
            ? property.intValue.ToString(CultureInfo.InvariantCulture)
            : Num(property.floatValue);
    }

    private static string PropNum(SerializedProperty parent, string name)
    {
        SerializedProperty property = parent.FindPropertyRelative(name);
        if (property == null) return "<no such field>";
        return property.propertyType == SerializedPropertyType.Integer
            ? property.intValue.ToString(CultureInfo.InvariantCulture)
            : Num(property.floatValue);
    }

    private static string PropEnum<T>(SerializedObject target, string name) where T : System.Enum
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) return "<no such field>";
        return $"{(T)System.Enum.ToObject(typeof(T), property.intValue)} ({property.intValue})";
    }

    private static string PropEnum<T>(SerializedProperty parent, string name) where T : System.Enum
    {
        SerializedProperty property = parent.FindPropertyRelative(name);
        if (property == null) return "<no such field>";
        return $"{(T)System.Enum.ToObject(typeof(T), property.intValue)} ({property.intValue})";
    }

    private static string ArraySize(SerializedProperty property) =>
        property == null || !property.isArray ? "<no such field>"
                                              : property.arraySize.ToString(CultureInfo.InvariantCulture);

    private static string ArrayContents(SerializedProperty property)
    {
        if (property == null || !property.isArray) return string.Empty;
        if (property.arraySize == 0) return "[]";

        List<string> names = new List<string>();
        for (int i = 0; i < property.arraySize; i++)
        {
            names.Add(Obj(property.GetArrayElementAtIndex(i).objectReferenceValue));
        }

        return "[" + string.Join(", ", names) + "]";
    }
}
