using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * GrenadeSetupTools.cs
 *
 * Purpose: Builds everything the grenade needs that does NOT live in a .cs file - the
 *          bounce physics material, the projectile prefab, the in-hand controller, the three
 *          world pickups, and the WeaponAudioManager profile that carries the grenade's two
 *          assessed audio slots. Reskin Grenade then swaps the OLD tin-can stand-in for the
 *          owner's model in a scene that was already built.
 * Menu:    Tools/Weapons/Report Grenade Setup   (read only)
 *          Tools/Weapons/Build Grenade          (idempotent - a second run changes nothing)
 *          Tools/Weapons/Reskin Grenade         (idempotent - a second run changes nothing)
 * Used by: the integrator, driven from the Unity CLI, per the work orders in
 *          Documentation/GRENADE_TASKS.md and Documentation/HUD_AND_BARREL_TASKS.md
 *          (Agent D). The owner saves the scene afterwards.
 *
 * ⚠ THE MODEL CHANGED, 2026-09-06. Until then the grenade was SM_Prop_Can_01, a tin can
 * standing in as an improvised pipe bomb because no grenade mesh existed in the packs. The
 * owner has since added Assets/Grenade.fbx, so:
 *   - ModelPath is that FBX and ModelChildName is "Grenade_Model" (the FBX's own root is
 *     ALSO called "Grenade", and the weapon under WeaponParent is called "Grenade" too, so a
 *     child named after the FBX would read as "Grenade/Grenade" in the hierarchy.
 *     MinigunSetupTools renames its model child for the same reason);
 *   - the material step is a NO-OP. The FBX imports its own eight URP/Lit materials
 *     (M_Gren_Body_A..E, M_Gren_Metal, M_Gren_Accent, M_Gren_Ring) as sub-assets, so there
 *     is nothing to author. Assets/Inbox/Prefabs/Grenade_Can.mat is left on disk but is now
 *     referenced by nothing - the report says so, and deleting it is the owner's call;
 *   - IconPath is the pack's own pineapple grenade, ICON_SM_Wep_Grenade_01_BR. (_Apoc is a
 *     banded canister, and there is no _Side variant - WeaponItem.GetHudIcon() falls back to
 *     itemIcon, exactly as it did with the pipe bomb.)
 * Nothing about the grenade's BEHAVIOUR moved with it: Grenade.cs, GrenadeController.cs and
 * GrenadeItem.cs are untouched, and Reskin Grenade leaves the projectile's SphereCollider,
 * Rigidbody and Grenade component exactly as it finds them. Only the picture changed.
 *
 * Menu items rather than hand edits of the .unity file, for this project's standing reason:
 * the files on disk are routinely older than what is open in the editor, so a hand edit
 * would be lost the next time the owner saves. The same reason the performance and weapon
 * tools next door are menu items.
 *
 * ⚠ NO DIALOGS anywhere in this file. Both items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block it with a modal window nobody can see,
 * let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Run Report before and after Build: it prints one block with a fixed line order and
 * invariant-culture numbers, so the two runs diff cleanly and the diff IS the record of
 * what Build did.
 *
 * SCENE edits (the controller, the three pickups, the audio profile) are Undo-recorded and
 * need the owner to SAVE THE SCENE. ASSET writes (the material, the physics material, the
 * prefab) go straight to disk through AssetDatabase / PrefabUtility: Undo does not survive
 * them, and `git checkout --` is the way back. Every item says which it did.
 *
 * ⚠ Binds to serialized fields by literal name: on WeaponController "weaponId",
 * "weaponName", "weaponAudioManager", "firePoint", "fireRate", the six pose fields copied
 * from the Axe ("raisedPosition", "raisedRotation", "loweredPositionOffset",
 * "loweredRotationOffset", "raiseSpeed", "lowerSpeed") and the six bob fields; on
 * GrenadeController "grenadePrefab"; on Grenade "explosionEffectPrefab"; on Item /
 * WeaponItem / GrenadeItem "itemName", "itemIcon", "itemDescription", "weaponController",
 * "weaponName", "hudIcon", "maxCarried", "grenadesPerPickup"; and on WeaponAudioManager the
 * array "m_WeaponProfiles" plus every field of WeaponSoundProfile listed in WriteProfile.
 * Renaming any of those compiles clean and breaks this silently, like the other editors
 * here - see CODE_STRUCTURE's editor-tooling warning.
 *
 * ⚠ NEVER measure a model here with Renderer.bounds. It is a cached WORLD AABB the engine
 * refreshes when it draws, so on a GameObject instantiated moments ago in EDIT mode it reads
 * near-zero and a target-height division comes back as an enormous scale; it also swallows
 * ParticleSystemRenderers, which is how MinigunSetupTools once measured 18 m of muzzle-flash
 * tracer instead of 0.9 m of gun. TryGetWorldBounds below reads Mesh.bounds off the asset
 * instead - authored data, never stale, never a particle system.
 *
 * ⚠ Assets/Grenade.fbx imports with its ROOT at localScale 100 and its meshes authored at
 * 1/100 (useFileUnits/useFileScale, a centimetre FBX). That scale is CORRECT and is left
 * alone: BuildModelChild MULTIPLIES the instance root's existing localScale by its factor
 * rather than assigning Vector3.one, and it sizes the instance ROOT. "Normalising" the child
 * to 1 would shrink the grenade to a millimetre.
 *
 * ⚠ Unity 6 rename, verified against 6000.0.72f1 before this file was written: the managed
 * type is UnityEngine.PhysicsMaterial. The old PhysicMaterial survives only as an
 * [Obsolete] UnityUpgradable alias ("PhysicMaterial has been renamed to PhysicsMaterial"),
 * and the enum went the same way (PhysicMaterialCombine -> PhysicsMaterialCombine). The
 * FILE EXTENSION did NOT follow: the editor's asset-creation table still maps the
 * extension "physicMaterial" to the "New Physics Material" entry, so the asset must be
 * written as .physicMaterial - .physicsMaterial would not import as one. The project's own
 * Standard Assets physics materials use that extension too.
 */

public static class GrenadeSetupTools
{
    private const string LogPrefix = "[GrenadeSetupTools]";
    private const string UndoLabel = "Build Grenade";
    private const string ReskinUndoLabel = "Reskin Grenade";

    // ---- Asset paths ---------------------------------------------------------

    private const string PrefabFolder = "Assets/Inbox/Prefabs";

    /// <summary>
    /// ⚠ NO LONGER WRITTEN AND NO LONGER REFERENCED. It was the flat dark grey-green the tin
    /// can wore; Assets/Grenade.fbx brings its own eight materials. The path is kept only so
    /// the report can say the asset is now unused - deleting it is the owner's call, and this
    /// tool deliberately does not.
    /// </summary>
    private const string LegacyCanMaterialPath = PrefabFolder + "/Grenade_Can.mat";

    /// <summary>⚠ .physicMaterial, not .physicsMaterial - see the header.</summary>
    private const string BouncePath = PrefabFolder + "/Grenade_Bounce.physicMaterial";

    private const string ProjectilePath = PrefabFolder + "/Grenade.prefab";

    /// <summary>
    /// The owner dropped it in the Assets root, beside Minigun.fbx. Root 'Grenade' at
    /// localScale 100, three renderers (the body plus SM_Wep_Grenade_01_Pin_01 and
    /// SM_Wep_Grenade_01_Spoon_01), 1076 verts, eight embedded URP/Lit materials, and mesh
    /// bounds of about 0.06 x 0.11 x 0.07 m - a real frag grenade, so the scale factors this
    /// tool computes come out near 1 in the hand.
    /// </summary>
    private const string ModelPath = "Assets/Grenade.fbx";

    private const string ExplosionFxPath =
        "Assets/Inbox/Low Poly Models/Nature/Particles/Prefabs/FX_Grenade_Explosive_01.prefab";

    /// <summary>
    /// The pack's green pineapple with the silver spoon and ring - the owner's screenshot
    /// exactly. The other candidate, ICON_SM_Wep_Grenade_01_Apoc, is a banded canister and
    /// reads as something else. There is no _Side variant of either, which costs nothing:
    /// WeaponItem.GetHudIcon() falls back to itemIcon, as it already did for the pipe bomb.
    /// </summary>
    private const string IconPath =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Sprites/Icons_Explosives/ICON_SM_Wep_Grenade_01_BR.png";

    /// <summary>
    /// The same placeholder DistributionResetTools resets every assessed slot to, read from
    /// the same constant it does. The two grenade arrays ship holding this rather than
    /// empty - see WriteProfile for why that is load-bearing.
    /// </summary>
    private const string BeepClipPath = AudioProgressScanner.DemoSoundsFolder + "Beep to replace.wav";

    // ---- Scene paths ---------------------------------------------------------

    private const string WeaponParentPath = "Player/FirstPersonCharacter/WeaponParent";
    private const string PickupsPath = "Game Management/Pickups";
    private const string StudioRoomPath = "Game Management/Studio Room";
    private const string CampfirePath = "Game Management/Area Audio Manager AudioSources/CampfireMusic";

    private const string AxeControllerPath = WeaponParentPath + "/Axe";
    private const string AxePickupPath = PickupsPath + "/Pickup - Axe";

    private const string ControllerName = "Grenade";
    private const string ControllerPath = WeaponParentPath + "/" + ControllerName;
    private const string FirePointName = "FirePoint";

    /// <summary>
    /// ⚠ NOT the FBX's own root name. Assets/Grenade.fbx's root is called "Grenade", and so
    /// is the weapon under WeaponParent that carries it, so an unrenamed instance would read
    /// as "Grenade/Grenade" in the hierarchy and Transform.Find on the parent's name would be
    /// ambiguous. MinigunSetupTools made the same call with "Minigun_Model".
    /// </summary>
    private const string ModelChildName = "Grenade_Model";

    /// <summary>
    /// What the model child was called before 2026-09-06: the tin can. Reskin Grenade looks
    /// for exactly this name when deciding what to remove, so a child the owner has since
    /// renamed by hand is left alone rather than guessed at.
    /// </summary>
    private const string LegacyModelChildName = "SM_Prop_Can_01";

    private const string ProfileName = "Grenade";
    private const string ProfilesField = "m_WeaponProfiles";
    private const string ProjectileTag = "Projectile";

    /// <summary>
    /// Three of them so the merge path is exercisable in play: one to pick up, one to walk
    /// onto while already carrying (the count tops up), one more to walk onto while full
    /// (it stays on the ground). Names, not a count, so a re-run finds them by name.
    /// </summary>
    private static readonly string[] PickupNames =
    {
        "Pickup - Grenade", "Pickup - Grenade (2)", "Pickup - Grenade (3)"
    };

    // ---- The numbers, all of them the owner's to tune afterwards -------------

    /// <summary>
    /// Height in the hand, in WORLD metres, at the pose copied from the axe. Unchanged from
    /// the tin can: the owner tuned the pose against a 0.12 m object, and the FBX ships at
    /// 0.11 m, so the reskin scales it by about 1.09 and the pose still reads.
    /// </summary>
    private const float HandGrenadeHeight = 0.12f;

    /// <summary>Bigger in the world, so a pickup reads from a few metres away.</summary>
    private const float WorldGrenadeHeight = 0.3f;

    private const float ColliderRadius = 0.07f;
    private const float ProjectileMass = 0.4f;
    private const float Bounciness = 0.3f;
    private const float DynamicFriction = 0.6f;
    private const float FirePointForward = 0.3f;

    /// <summary>From the work order's numbers table: one grenade per 0.6 s of held click.</summary>
    private const float ThrowFireRate = 0.6f;

    private const int MaxCarried = 3;
    private const int GrenadesPerPickup = 3;

    private const float AxePickupOffset = 2f;
    private const float AnchorOffset = 1.5f;
    private const float DefaultHover = 0.5f;
    private const float RayStartHeight = 5f;
    private const float RayLength = 60f;
    private const float ClearRadius = 0.3f;

    // ==========================================================================
    //  1. Report
    // ==========================================================================

    /// <summary>
    /// Reads and prints, changing nothing. The integrator runs it either side of Build so
    /// the before/after is a plain diff; that is also why the line order is fixed and the
    /// numbers are invariant-culture.
    /// </summary>
    [MenuItem("Tools/Weapons/Report Grenade Setup", priority = 1100)]
    private static void ReportGrenadeSetup()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Grenade Setup (read only - nothing was changed).");

        AppendModelReport(report);
        AppendLegacyMaterialReport(report);
        AppendBounceReport(report);
        AppendProjectileReport(report);
        AppendControllerReport(report);
        AppendPickupsReport(report);
        AppendProfileReport(report);

        Debug.Log(report.ToString().TrimEnd());
    }

    /// <summary>
    /// The owner's FBX, measured the same way everything else here is measured, so the number
    /// the reskin divides into is visible in the report rather than only in the reskin's log.
    /// </summary>
    private static void AppendModelReport(StringBuilder report)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            report.AppendLine($"MODEL {ModelPath}: ABSENT - Build Grenade and Reskin Grenade will both refuse.");
            return;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

        report.AppendLine($"MODEL {ModelPath}: PRESENT root='{model.name}', " +
                          $"rootLocalScale={V3(model.transform.localScale)}, renderers={renderers.Length}, " +
                          $"materials=[{DistinctMaterialNames(model)}]");

        report.AppendLine(TryGetWorldBounds(model, out Bounds bounds)
            ? $"  Shipped mesh bounds at import scale: {V3(bounds.size)} m, height {Num(bounds.size.y)} m, " +
              $"centre {V3(bounds.center - model.transform.position)} from the root"
            : "  Shipped size: <no meshes to measure>");
    }

    /// <summary>
    /// The tin can's material. Reported, not deleted: nothing points at it any more, but it
    /// is an asset the owner may want to keep, and this tool does not delete the owner's files.
    /// </summary>
    private static void AppendLegacyMaterialReport(StringBuilder report)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(LegacyCanMaterialPath);
        if (material == null)
        {
            report.AppendLine($"LEGACY MATERIAL {LegacyCanMaterialPath}: ABSENT (nothing needs it)");
            return;
        }

        Color colour = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;

        report.AppendLine($"LEGACY MATERIAL {LegacyCanMaterialPath}: PRESENT shader='{material.shader.name}', " +
                          $"baseColor={Col(colour)} - UNUSED since the model changed to {ModelPath}, " +
                          "which brings its own materials. Not written and not referenced by this tool " +
                          "any more; deleting it is the owner's call.");
    }

    private static void AppendBounceReport(StringBuilder report)
    {
        PhysicsMaterial bounce = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BouncePath);
        if (bounce == null)
        {
            report.AppendLine($"PHYSICS MATERIAL {BouncePath}: ABSENT");
            return;
        }

        report.AppendLine($"PHYSICS MATERIAL {BouncePath}: PRESENT " +
                          $"bounciness={Num(bounce.bounciness)}, " +
                          $"dynamicFriction={Num(bounce.dynamicFriction)}, " +
                          $"staticFriction={Num(bounce.staticFriction)}, " +
                          $"frictionCombine={bounce.frictionCombine}, " +
                          $"bounceCombine={bounce.bounceCombine}");
    }

    private static void AppendProjectileReport(StringBuilder report)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
        if (prefab == null)
        {
            report.AppendLine($"PROJECTILE PREFAB {ProjectilePath}: ABSENT");
            return;
        }

        report.AppendLine($"PROJECTILE PREFAB {ProjectilePath}: PRESENT " +
                          $"layer={LayerName(prefab.layer)}, tag={prefab.tag}");

        Rigidbody body = prefab.GetComponent<Rigidbody>();
        report.AppendLine(body == null
            ? "  Rigidbody: <none>"
            : $"  Rigidbody: mass={Num(body.mass)}, collisionDetectionMode={body.collisionDetectionMode}, " +
              $"interpolation={body.interpolation}, useGravity={body.useGravity}, isKinematic={body.isKinematic}");

        SphereCollider sphere = prefab.GetComponent<SphereCollider>();
        report.AppendLine(sphere == null
            ? "  SphereCollider: <none>"
            : $"  SphereCollider: radius={Num(sphere.radius)}, center={V3(sphere.center)}, " +
              $"isTrigger={sphere.isTrigger}, sharedMaterial={Obj(sphere.sharedMaterial)}");

        Grenade grenade = prefab.GetComponent<Grenade>();
        if (grenade == null)
        {
            report.AppendLine("  Grenade: <component missing>");
        }
        else
        {
            SerializedObject serialized = new SerializedObject(grenade);
            report.AppendLine($"  Grenade: explosionEffectPrefab={Prop(serialized, "explosionEffectPrefab")}, " +
                              $"fuseSeconds={PropNum(serialized, "fuseSeconds")}, " +
                              $"radius={PropNum(serialized, "radius")}, " +
                              $"centreDamage={PropNum(serialized, "centreDamage")}, " +
                              $"edgeFraction={PropNum(serialized, "edgeFraction")}");
        }

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        report.AppendLine($"  Renderers: {renderers.Length}, materials=[{DistinctMaterialNames(prefab)}]");
        report.AppendLine($"  Model child: {DescribeModelChild(prefab.transform)}");

        AudioSource[] sources = prefab.GetComponentsInChildren<AudioSource>(true);
        report.AppendLine($"  AudioSource: {sources.Length} " +
                          "(0 is correct - the handout reset never walks AudioSources, so a clip " +
                          "here would escape it)");
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
                          $"localEuler={V3(host.transform.localEulerAngles)}");

        GrenadeController controller = host.GetComponent<GrenadeController>();
        if (controller == null)
        {
            report.AppendLine("  GrenadeController: <component missing>");
            return;
        }

        SerializedObject serialized = new SerializedObject(controller);
        report.AppendLine($"  GrenadeController: weaponId={PropEnum<WeaponId>(serialized, "weaponId")}, " +
                          $"weaponName='{PropString(serialized, "weaponName")}', " +
                          $"weaponAudioManager={Prop(serialized, "weaponAudioManager")}, " +
                          $"grenadePrefab={Prop(serialized, "grenadePrefab")}, " +
                          $"firePoint={Prop(serialized, "firePoint")}, " +
                          $"fireRate={PropNum(serialized, "fireRate")}, " +
                          $"throwSpeed={PropNum(serialized, "throwSpeed")}, " +
                          $"throwLift={PropNum(serialized, "throwLift")}, " +
                          $"carriedGrenades={PropNum(serialized, "carriedGrenades")}");
        report.AppendLine($"  Pose: raisedPosition={PropV3(serialized, "raisedPosition")}, " +
                          $"raisedRotation={PropV3(serialized, "raisedRotation")}, " +
                          $"loweredPositionOffset={PropV3(serialized, "loweredPositionOffset")}, " +
                          $"loweredRotationOffset={PropV3(serialized, "loweredRotationOffset")}");

        Transform firePoint = host.transform.Find(FirePointName);
        report.AppendLine(firePoint == null
            ? $"  {FirePointName} child: <none>"
            : $"  {FirePointName} child: localPosition={V3(firePoint.localPosition)}");

        report.AppendLine($"  Model child: {DescribeModelChild(host.transform)}");
    }

    private static void AppendPickupsReport(StringBuilder report)
    {
        GameObject pickups = FindByPath(PickupsPath);
        if (pickups == null)
        {
            report.AppendLine($"PICKUPS {PickupsPath}: <parent not in the open scene>");
            return;
        }

        int found = 0;
        StringBuilder lines = new StringBuilder();

        foreach (string name in PickupNames)
        {
            Transform pickup = pickups.transform.Find(name);
            if (pickup == null)
            {
                lines.AppendLine($"  '{name}': ABSENT");
                continue;
            }

            found++;

            SphereCollider trigger = pickup.GetComponent<SphereCollider>();
            GrenadeItem item = pickup.GetComponent<GrenadeItem>();

            lines.AppendLine($"  '{name}': PRESENT position={V3(pickup.position)}, " +
                             $"layer={LayerName(pickup.gameObject.layer)}, " +
                             $"lossyScale={V3(pickup.lossyScale)}");
            lines.AppendLine(trigger == null
                ? "      SphereCollider: <none>"
                : $"      SphereCollider: radius={Num(trigger.radius)}, isTrigger={trigger.isTrigger}, " +
                  $"world radius={Num(trigger.radius * MaxAbs(pickup.lossyScale))}");

            lines.AppendLine($"      Model child: {DescribeModelChild(pickup)}");

            if (item == null)
            {
                lines.AppendLine("      GrenadeItem: <component missing>");
            }
            else
            {
                SerializedObject serialized = new SerializedObject(item);
                lines.AppendLine($"      GrenadeItem: itemName='{PropString(serialized, "itemName")}', " +
                                 $"weaponName='{PropString(serialized, "weaponName")}', " +
                                 $"weaponController={Prop(serialized, "weaponController")}, " +
                                 $"itemIcon={Prop(serialized, "itemIcon")}, " +
                                 $"hudIcon={Prop(serialized, "hudIcon")}, " +
                                 $"maxCarried={PropNum(serialized, "maxCarried")}, " +
                                 $"grenadesPerPickup={PropNum(serialized, "grenadesPerPickup")}");
            }
        }

        report.AppendLine($"PICKUPS under {PickupsPath}: {found} of {PickupNames.Length} present");
        report.Append(lines);
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
        SerializedProperty throwSounds = profile.FindPropertyRelative("throwSounds");
        SerializedProperty explosionSounds = profile.FindPropertyRelative("explosionSounds");
        SerializedProperty shootSounds = profile.FindPropertyRelative("shootSounds");

        report.AppendLine($"AUDIO PROFILE '{ProfileName}' on '{HierarchyPath(manager.transform)}': " +
                          $"index {index} of {profiles.arraySize} ({ProfileNames(profiles)}), " +
                          $"weaponId={PropEnum<WeaponId>(profile, "weaponId")}, " +
                          $"weaponType={PropEnum<WeaponType>(profile, "weaponType")}");
        report.AppendLine($"  throwSounds: {ArraySize(throwSounds)} element(s) {ArrayContents(throwSounds)}, " +
                          $"throwVolume={PropNum(profile, "throwVolume")}");
        report.AppendLine($"  explosionSounds: {ArraySize(explosionSounds)} element(s) {ArrayContents(explosionSounds)}, " +
                          $"explosionVolume={PropNum(profile, "explosionVolume")}, " +
                          $"explosionMaxDistance={PropNum(profile, "explosionMaxDistance")}");
        report.AppendLine($"  hidden-by-filter slots: shootSounds={ArraySize(shootSounds)}, " +
                          $"raiseWeaponSound={Prop(profile, "raiseWeaponSound")}, " +
                          $"lowerWeaponSound={Prop(profile, "lowerWeaponSound")}, " +
                          $"reloadActionSounds={ArraySize(profile.FindPropertyRelative("reloadActionSounds"))}");
    }

    // ==========================================================================
    //  2. Build
    // ==========================================================================

    /// <summary>
    /// Builds, in order, the six pieces the grenade needs outside its three scripts. Every
    /// step skips what already exists, so a second run changes nothing and a run that was
    /// interrupted can simply be repeated.
    /// </summary>
    [MenuItem("Tools/Weapons/Build Grenade", priority = 1101)]
    private static void BuildGrenade()
    {
        List<string> assetsWritten = new List<string>();
        List<string> skipped = new List<string>();
        List<string> fields = new List<string>();
        List<string> notes = new List<string>();

        // Edit-mode queries read a physics scene that is only refreshed on demand; the
        // pickup placement below raycasts against it, and a stale one puts a pickup in
        // mid-air over geometry that has since moved.
        Physics.SyncTransforms();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- 1 + 2: the two assets the prefab needs before it can be built ----
        // Step 1 no longer writes anything: the model brings its own materials.
        NoteMaterialStepIsNoOp(skipped, notes);
        PhysicsMaterial bounce = EnsureBounceMaterial(assetsWritten, skipped);

        // ---- 3: the projectile ----
        GameObject projectile = EnsureProjectilePrefab(bounce, assetsWritten, skipped, fields, notes);

        if (assetsWritten.Count > 0) AssetDatabase.SaveAssets();

        // ---- 4: the weapon in the hand ----
        GrenadeController controller = EnsureController(projectile, skipped, fields, notes);

        // ---- 5: the three world pickups ----
        EnsurePickups(controller, skipped, fields, notes);

        // ---- 6: the audio profile, the reason the whole feature exists ----
        EnsureAudioProfile(skipped, fields, notes);

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Build Grenade finished.");

        log.AppendLine($"ASSETS WRITTEN ({assetsWritten.Count}) - not undoable, " +
                       "`git checkout -- <path>` is the way back:");
        AppendList(log, assetsWritten, "  <none - every asset already existed>");

        log.AppendLine($"SERIALIZED FIELDS SET ({fields.Count}):");
        AppendList(log, fields, "  <none - everything was already wired>");

        log.AppendLine($"SKIPPED, ALREADY PRESENT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine("The scene edits (controller, pickups, audio profile) are Undo-recorded " +
                       $"as one step, '{UndoLabel}'. SAVE THE SCENE ({scene.name}) or they are lost.");

        Debug.Log(log.ToString().TrimEnd());
    }

    // ---- Step 1: the material, which is now nobody's job ---------------------

    /// <summary>
    /// The step that used to author Grenade_Can.mat. It is kept as a step, and it writes
    /// nothing: Assets/Grenade.fbx imports its own URP/Lit materials as sub-assets, so
    /// authoring one here would only overwrite the owner's model with a flat colour. The log
    /// line exists so a reader of two builds' logs can see that the absence is deliberate
    /// rather than a step that silently stopped running.
    /// </summary>
    private static void NoteMaterialStepIsNoOp(List<string> skipped, List<string> notes)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        string materials = model != null ? DistinctMaterialNames(model) : "<model not loaded>";

        skipped.Add($"material step: NO-OP - {ModelPath} brings its own materials [{materials}], " +
                    "so nothing was authored and nothing was written");

        Debug.Log($"{LogPrefix} Material step: NO-OP. The grenade wears {ModelPath}'s own imported " +
                  $"materials [{materials}]. {LegacyCanMaterialPath} is NOT written and NOT referenced " +
                  "any more - it is now an unused asset. This tool does not delete it; that is the " +
                  "owner's call.");

        if (AssetDatabase.LoadAssetAtPath<Material>(LegacyCanMaterialPath) != null)
        {
            notes.Add($"{LegacyCanMaterialPath} still exists on disk and is now UNUSED (the tin can it " +
                      "was made for is gone). Left in place deliberately - deleting an asset is the " +
                      "owner's decision, not this tool's.");
        }
    }

    // ---- Step 2: the physics material ---------------------------------------

    /// <summary>
    /// How the can behaves off a wall: a little bounce, plenty of friction so it settles
    /// rather than rolling away down the terrain while the fuse burns.
    /// </summary>
    /// <remarks>
    /// ⚠ UnityEngine.PhysicsMaterial in Unity 6 (PhysicMaterial is the obsolete alias), but
    /// the asset extension is still .physicMaterial. See the file header.
    /// </remarks>
    private static PhysicsMaterial EnsureBounceMaterial(List<string> written, List<string> skipped)
    {
        PhysicsMaterial existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BouncePath);
        if (existing != null)
        {
            skipped.Add($"physics material {BouncePath} " +
                        $"(bounciness={Num(existing.bounciness)}, dynamicFriction={Num(existing.dynamicFriction)})");
            return existing;
        }

        PhysicsMaterial bounce = new PhysicsMaterial("Grenade_Bounce")
        {
            bounciness = Bounciness,
            dynamicFriction = DynamicFriction,

            // Matched to the dynamic value: a can that grips harder standing still than
            // sliding would creep on a slope and then stop dead, which reads as a stutter.
            staticFriction = DynamicFriction,

            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Average
        };

        EnsureFolder(PrefabFolder);
        AssetDatabase.CreateAsset(bounce, BouncePath);

        written.Add($"{BouncePath} (bounciness={Num(Bounciness)}, dynamicFriction={Num(DynamicFriction)}, " +
                    $"staticFriction={Num(DynamicFriction)}, both combines=Average)");
        return bounce;
    }

    // ---- Step 3: the projectile prefab --------------------------------------

    /// <summary>
    /// The thing that is actually thrown. Deliberately carries NO AudioSource: the handout
    /// reset tool walks serialized AudioClip fields on components it knows about and never
    /// touches AudioSources, so a clip parked on this prefab would be invisible to the
    /// Ctrl+G report and would survive the reset - the one way an assessed sound could
    /// escape the assessment.
    /// </summary>
    private static GameObject EnsureProjectilePrefab(PhysicsMaterial bounce,
                                                     List<string> written, List<string> skipped,
                                                     List<string> fields, List<string> notes)
    {
        GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionFxPath);
        if (fx == null)
        {
            notes.Add($"⚠ No explosion effect at {ExplosionFxPath} - the grenade will detonate " +
                      "silently as far as the eye is concerned. Check the pack imported.");
        }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
        if (existing != null)
        {
            skipped.Add($"projectile prefab {ProjectilePath}");

            // The one reference worth repairing on a re-run: an unwired effect is a silent
            // hole rather than an error, and a half-finished earlier run is exactly how it
            // would come to be null.
            Grenade component = existing.GetComponent<Grenade>();
            if (component != null && fx != null)
            {
                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty effect = serialized.FindProperty("explosionEffectPrefab");
                if (effect != null && effect.objectReferenceValue == null)
                {
                    effect.objectReferenceValue = fx;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                    fields.Add($"{ProjectilePath} Grenade.explosionEffectPrefab = '{fx.name}' (repaired - it was null)");
                }
            }

            return existing;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to build the projectile: no grenade model at " +
                             $"{ModelPath}. Nothing was written.");
            return null;
        }

        // The saved asset is an ordinary prefab referencing the FBX's meshes and materials,
        // not a variant chained to the model importer - see BuildModelChild, which uses a
        // plain Instantiate for exactly that reason. Same reasoning as PortedPropTools.
        GameObject root = new GameObject(ControllerName);

        try
        {
            root.layer = 0;                                  // Default, like the crossbow bolt
            root.transform.position = Vector3.zero;          // so world bounds read as local
            ApplyProjectileTag(root, notes);

            // recentre: true. The blast point, the throw origin and the physics centre all
            // read transform.position, so the body is put on the root rather than left
            // sitting wherever the FBX's pivot happens to be (below and behind it, here -
            // the mesh centre is about (0, 0.02, 0.01) from the root at import scale).
            BuildModelChild(model, root.transform, HandGrenadeHeight, true,
                            out float visualScale, out Bounds visualBounds);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = ProjectileMass;

            // A thrown can crosses more than its own diameter in one physics step, so the
            // cheap discrete sweep would let it tunnel through a wall - the same setting
            // Crossbow_Bolt already carries for the same reason.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = ColliderRadius;
            sphere.center = Vector3.zero;
            if (bounce != null) sphere.sharedMaterial = bounce;

            Grenade grenade = root.AddComponent<Grenade>();
            if (fx != null)
            {
                SerializedObject serialized = new SerializedObject(grenade);
                SerializedProperty effect = serialized.FindProperty("explosionEffectPrefab");
                if (effect != null)
                {
                    effect.objectReferenceValue = fx;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    notes.Add("⚠ Grenade has no serialized field 'explosionEffectPrefab' - it has " +
                              "been renamed, and this tool binds to it by name.");
                }
            }

            EnsureFolder(PrefabFolder);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
            if (saved == null)
            {
                Debug.LogWarning($"{LogPrefix} Could not write {ProjectilePath}. Nothing else was " +
                                 "changed by this step.");
                return null;
            }

            written.Add($"{ProjectilePath} (layer Default, tag {saved.tag}, Rigidbody mass " +
                        $"{Num(ProjectileMass)} / ContinuousDynamic / Interpolate, SphereCollider r=" +
                        $"{Num(ColliderRadius)} with '{(bounce != null ? bounce.name : "<none>")}', " +
                        $"Grenade component, NO AudioSource)");
            fields.Add($"{ProjectilePath} Grenade.explosionEffectPrefab = {(fx != null ? $"'{fx.name}'" : "<none>")}");
            fields.Add($"{ProjectilePath} child '{ModelChildName}' localScale x{Num(visualScale)} " +
                       $"-> mesh bounds {V3(visualBounds.size)} m (target height " +
                       $"{Num(HandGrenadeHeight)} m), recentred on the root");

            return saved;
        }
        finally
        {
            // The scratch instance must go whether or not the save succeeded, or it is left
            // behind in the open scene.
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Gives the object the Projectile tag if the project still declares one. It does - the
    /// tag exists in TagManager.asset and was unused until the grenade - but a tag that has
    /// been deleted throws on assignment, and a throw here would abort a half-built prefab.
    /// </summary>
    private static void ApplyProjectileTag(GameObject target, List<string> notes)
    {
        foreach (string tag in UnityEditorInternal.InternalEditorUtility.tags)
        {
            if (tag != ProjectileTag) continue;
            target.tag = ProjectileTag;
            return;
        }

        notes.Add($"⚠ No '{ProjectileTag}' tag in this project, so the prefab is left Untagged. " +
                  "Add it in Project Settings > Tags and Layers and re-run.");
    }

    /// <summary>
    /// The grenade mesh as a child, sized to a real-world height. A CHILD rather than the
    /// root so the root can stay at scale 1: the SphereCollider and the Rigidbody live there
    /// (on the projectile), as do the pickup's trigger and the weapon's authored pose, and a
    /// scaled root would scale every one of them with the model.
    ///
    /// NO material is applied. The FBX brings eight of its own, and overwriting them with a
    /// single flat colour is exactly what the tin can needed and the grenade does not.
    /// </summary>
    /// <param name="targetHeight">Height in WORLD metres - the parent's own scale is already
    /// in the measurement, so a pickup scaled to 0.47 still gets a grenade of the size asked
    /// for.</param>
    /// <param name="recentre">True on the projectile and the pickups, where the body must sit
    /// on the root - the blast point and the throw origin read transform.position, and the
    /// pickup spins about its own origin. False in the hand, where the FBX's own pivot is
    /// what the axe-derived pose was authored against.</param>
    /// <param name="worldBounds">The model's mesh bounds in WORLD space after the scaling.</param>
    private static GameObject BuildModelChild(GameObject model, Transform parent, float targetHeight,
                                              bool recentre, out float scaleFactor, out Bounds worldBounds)
    {
        scaleFactor = 1f;
        worldBounds = new Bounds(parent.position, Vector3.zero);

        // A missing model is reported by the caller that needed it; here it just means
        // there is no child to build, and the piece is still worth assembling without one.
        if (model == null) return null;

        // A plain Instantiate (not InstantiatePrefab) gives a disconnected copy, so what is
        // saved or left in the scene is ordinary geometry referencing the FBX's meshes and
        // materials rather than a variant chained to the model importer.
        GameObject visual = Object.Instantiate(model);
        visual.name = ModelChildName;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // ⚠ localScale is deliberately NOT assigned. Assets/Grenade.fbx imports with its root
        // at 100 and its meshes authored at 1/100; setting Vector3.one here would shrink the
        // grenade by a hundred and the factor below would then have to undo it. The scale is
        // MULTIPLIED a few lines down instead.

        // Whatever the FBX importer added is not wanted here: the physics belong to the root,
        // and an Animator or LODGroup on a 1076-vertex prop is dead weight.
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

        if (!TryGetWorldBounds(visual, out Bounds bounds) || bounds.size.y <= 0.0001f)
        {
            return visual;
        }

        // Mesh bounds scale exactly with the transform, so one measurement is enough: the
        // child sits at the parent's own origin, so both its size and its offset from that
        // origin come out multiplied by the same factor.
        scaleFactor = targetHeight / bounds.size.y;
        visual.transform.localScale *= scaleFactor;

        Vector3 centre = parent.position + (bounds.center - parent.position) * scaleFactor;
        worldBounds = new Bounds(centre, bounds.size * scaleFactor);

        if (recentre)
        {
            // InverseTransformVector rather than a raw subtraction: a pickup is neither at the
            // world origin nor unrotated nor at scale 1 (0.4715 here), and localPosition is
            // measured in the parent's own space.
            visual.transform.localPosition += parent.InverseTransformVector(parent.position - centre);
            worldBounds = new Bounds(parent.position, bounds.size * scaleFactor);
        }

        return visual;
    }

    // ---- Step 4: the controller in the hand ---------------------------------

    /// <summary>
    /// The grenade in the player's hand. Its resting pose, its layer and its active state
    /// are read off the Axe rather than invented: the axe is the closest thing in the scene
    /// to a held object, and every controller under WeaponParent is left ACTIVE at its
    /// lowered transform, with the raise/lower handled at runtime.
    /// </summary>
    private static GrenadeController EnsureController(GameObject projectile,
                                                      List<string> skipped, List<string> fields,
                                                      List<string> notes)
    {
        GameObject weaponParent = FindByPath(WeaponParentPath);
        if (weaponParent == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to build the controller: '{WeaponParentPath}' is " +
                             "not in the open scene - is the level loaded? Nothing in the scene was changed.");
            return null;
        }

        GameObject axe = FindByPath(AxeControllerPath);
        WeaponController axeController = axe != null ? axe.GetComponent<WeaponController>() : null;
        WeaponAudioManager manager = Object.FindFirstObjectByType<WeaponAudioManager>();
        Grenade prefabComponent = projectile != null ? projectile.GetComponent<Grenade>() : null;

        Transform existing = weaponParent.transform.Find(ControllerName);
        if (existing != null)
        {
            skipped.Add($"controller {ControllerPath}");

            GrenadeController present = existing.GetComponent<GrenadeController>();
            if (present == null)
            {
                notes.Add($"⚠ '{ControllerPath}' exists but carries no GrenadeController, so the " +
                          "pickups below have nothing to point at. Delete that GameObject and re-run, " +
                          "or add the component by hand.");
                return null;
            }

            RepairControllerReferences(present, manager, prefabComponent, fields);
            return present;
        }

        GameObject host = new GameObject(ControllerName);
        Undo.RegisterCreatedObjectUndo(host, UndoLabel);
        host.transform.SetParent(weaponParent.transform, false);

        if (axe != null)
        {
            host.layer = axe.layer;
            host.SetActive(axe.activeSelf);
            host.transform.localPosition = axe.transform.localPosition;
            host.transform.localRotation = axe.transform.localRotation;
            host.transform.localScale = axe.transform.localScale;
            fields.Add($"{ControllerPath} transform + layer + activeSelf copied from '{AxeControllerPath}' " +
                       $"(localPosition={V3(host.transform.localPosition)}, layer={LayerName(host.layer)}, " +
                       $"activeSelf={host.activeSelf})");
        }
        else
        {
            notes.Add($"⚠ '{AxeControllerPath}' is not in the scene, so the grenade's resting pose " +
                      "and bob could not be copied from it and are left at the class defaults.");
        }

        GameObject handModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (handModel == null)
        {
            notes.Add($"⚠ No grenade model at {ModelPath}, so the weapon in the hand has no visible " +
                      "mesh. It still throws.");
        }

        // recentre: false. The pose below is copied from the axe, which the owner tuned
        // against a model sitting on its own FBX pivot; moving this one would move the
        // grenade in the hand by the same amount and the pose would need retuning.
        BuildModelChild(handModel, host.transform, HandGrenadeHeight, false,
                        out float handScale, out Bounds handBounds);
        fields.Add($"{ControllerPath}/{ModelChildName} localScale x{Num(handScale)} -> mesh bounds " +
                   $"{V3(handBounds.size)} m (target height {Num(HandGrenadeHeight)} m)");

        GameObject firePoint = new GameObject(FirePointName);
        firePoint.transform.SetParent(host.transform, false);
        firePoint.transform.localPosition = new Vector3(0f, 0f, FirePointForward);
        firePoint.transform.localRotation = Quaternion.identity;
        fields.Add($"{ControllerPath}/{FirePointName} localPosition = (0, 0, {Num(FirePointForward)})");

        GrenadeController controller = host.AddComponent<GrenadeController>();

        SerializedObject serialized = new SerializedObject(controller);

        // The axe is a held object with a pose the owner has already tuned in this scene;
        // starting from it beats starting from the class defaults, which are a rifle's.
        // Done BEFORE the explicit writes below so that whatever CopyFromSerializedProperty
        // does to the pending modification set, the grenade's own values are the last word.
        if (axeController != null)
        {
            SerializedObject source = new SerializedObject(axeController);
            string[] pose =
            {
                "raisedPosition", "raisedRotation", "loweredPositionOffset", "loweredRotationOffset",
                "raiseSpeed", "lowerSpeed",
                "bobCurveX", "bobCurveY", "bobCurveRotation",
                "bobAmountWalk", "bobAmountRun", "bobSpeed"
            };

            List<string> copied = new List<string>();
            foreach (string name in pose)
            {
                SerializedProperty from = source.FindProperty(name);
                if (from == null) continue;
                serialized.CopyFromSerializedProperty(from);
                copied.Add(name);
            }

            if (copied.Count > 0)
            {
                fields.Add($"{ControllerPath} GrenadeController: {string.Join(", ", copied)} copied " +
                           $"from '{AxeControllerPath}'");
            }
        }

        // intValue rather than enumValueIndex: enumValueIndex is the position in the enum's
        // declaration, and this field serializes the VALUE. They agree today because
        // WeaponId is contiguous from 0, and would silently stop agreeing the moment
        // somebody assigned an explicit number.
        SetInt(serialized, "weaponId", (int)WeaponId.Grenade, ControllerPath, fields,
               $"{WeaponId.Grenade} ({(int)WeaponId.Grenade})");
        SetString(serialized, "weaponName", ProfileName, ControllerPath, fields);
        SetFloat(serialized, "fireRate", ThrowFireRate, ControllerPath, fields);
        SetObject(serialized, "weaponAudioManager", manager, ControllerPath, fields);
        SetObject(serialized, "firePoint", firePoint.transform, ControllerPath, fields);
        SetObject(serialized, "grenadePrefab", prefabComponent, ControllerPath, fields);

        if (manager == null)
        {
            notes.Add("⚠ No WeaponAudioManager in the open scene, so weaponAudioManager is null - " +
                      "the throw and the blast would both be silent.");
        }
        if (prefabComponent == null)
        {
            notes.Add($"⚠ No Grenade component on {ProjectilePath}, so grenadePrefab is null - " +
                      "Fire() will log and refuse to throw.");
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);

        // showsReticle is NOT serialized - WeaponController derives it in Start from
        // weaponName containing "SMG" or "Revolver". "Grenade" contains neither, so the
        // reticle is already off and there is nothing here to set. Recorded because the
        // work order asks for a serialized field that does not exist.
        notes.Add("ShowsReticle needs no wiring: WeaponController derives it in Start from " +
                  "weaponName ('SMG'/'Revolver'), so a weapon called 'Grenade' shows no reticle.");

        return controller;
    }

    /// <summary>
    /// Re-wires only the references that are missing on a controller this tool built
    /// earlier. Anything already pointing somewhere is an authored choice and is left alone,
    /// which is what makes a second run a no-op.
    /// </summary>
    private static void RepairControllerReferences(GrenadeController controller, WeaponAudioManager manager,
                                                   Grenade prefabComponent, List<string> fields)
    {
        SerializedObject serialized = new SerializedObject(controller);
        bool changed = false;

        changed |= FillIfEmpty(serialized, "weaponAudioManager", manager, ControllerPath, fields);
        changed |= FillIfEmpty(serialized, "grenadePrefab", prefabComponent, ControllerPath, fields);

        SerializedProperty firePoint = serialized.FindProperty("firePoint");
        if (firePoint != null && firePoint.objectReferenceValue == null)
        {
            Transform child = controller.transform.Find(FirePointName);
            if (child != null)
            {
                firePoint.objectReferenceValue = child;
                fields.Add($"{ControllerPath} firePoint = '{FirePointName}' (repaired - it was null)");
                changed = true;
            }
        }

        if (changed)
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }
    }

    // ---- Step 5: the three world pickups ------------------------------------

    /// <summary>
    /// Three grenades on the ground: one beside the axe, one by the studio, one by the
    /// campfire. Three because that is what it takes to walk the merge path in play - take
    /// one, top up on the second, bounce off the third with full hands. The placement is a
    /// starting point the owner moves; the ground snap only stops them spawning in mid-air
    /// or inside the terrain.
    /// </summary>
    private static void EnsurePickups(GrenadeController controller,
                                      List<string> skipped, List<string> fields, List<string> notes)
    {
        GameObject pickups = FindByPath(PickupsPath);
        if (pickups == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to build the pickups: '{PickupsPath}' is not in the " +
                             "open scene. The controller and the assets above were still built.");
            return;
        }

        GameObject axePickup = FindByPath(AxePickupPath);
        GameObject studio = FindByPath(StudioRoomPath);
        GameObject campfire = FindByPath(CampfirePath);
        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        GameObject pickupModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        if (icon == null)
        {
            notes.Add($"⚠ No sprite at {IconPath}, so the hotbar and the current-weapon panel will " +
                      "both show a blank for the grenade.");
        }

        if (controller == null)
        {
            notes.Add("⚠ The pickups were built with weaponController EMPTY - step 4 did not produce " +
                      "a GrenadeController. Picking one up would log and count nothing. Fix step 4 " +
                      "and re-run, or point the three items at the controller by hand.");
        }

        float hover = HoverHeight(axePickup);

        for (int i = 0; i < PickupNames.Length; i++)
        {
            string name = PickupNames[i];

            if (pickups.transform.Find(name) != null)
            {
                skipped.Add($"pickup {PickupsPath}/{name}");
                continue;
            }

            // Anchor per pickup: the axe for the first (2 m away, so the player finds them
            // together), then the two places the level already has something worth standing
            // near.
            GameObject anchorObject = i == 0 ? axePickup : i == 1 ? studio : campfire;
            string anchorName = i == 0 ? AxePickupPath : i == 1 ? StudioRoomPath : CampfirePath;
            float offset = i == 0 ? AxePickupOffset : AnchorOffset;

            if (anchorObject == null)
            {
                notes.Add($"⚠ Anchor '{anchorName}' is not in the scene, so '{name}' was placed at " +
                          "the world origin instead. Move it by hand.");
            }

            Vector3 anchor = anchorObject != null ? anchorObject.transform.position : Vector3.zero;
            Vector3 position = GroundedSpotNear(anchor, offset, hover, out string how);

            GameObject pickup = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(pickup, UndoLabel);
            pickup.transform.SetParent(pickups.transform, false);
            pickup.transform.position = position;

            CopyPickupShell(pickup, axePickup, fields, notes);

            // recentre: true - the pickup turns on the spot inside its glow sphere, and a
            // model left on the FBX's own pivot would orbit rather than spin.
            BuildModelChild(pickupModel, pickup.transform, WorldGrenadeHeight, true,
                            out float worldScale, out Bounds worldBounds);

            GrenadeItem item = pickup.AddComponent<GrenadeItem>();
            SerializedObject serialized = new SerializedObject(item);
            string path = $"{PickupsPath}/{name}";

            SetString(serialized, "itemName", ProfileName, path, fields);
            SetString(serialized, "itemDescription", ProfileName, path, fields);
            SetString(serialized, "weaponName", ProfileName, path, fields);
            SetObject(serialized, "itemIcon", icon, path, fields);
            SetObject(serialized, "hudIcon", icon, path, fields);
            SetObject(serialized, "weaponController", controller, path, fields);
            SetInt(serialized, "maxCarried", MaxCarried, path, fields);
            SetInt(serialized, "grenadesPerPickup", GrenadesPerPickup, path, fields);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(item);

            fields.Add($"{path} placed at {V3(position)} - {how}; " +
                       $"{ModelChildName} localScale x{Num(worldScale)} -> mesh bounds " +
                       $"{V3(worldBounds.size)} m (target height {Num(WorldGrenadeHeight)} m), " +
                       "recentred on the pickup's own origin so it turns on the spot");
        }
    }

    /// <summary>
    /// Everything a pickup in this scene is besides its Item component: the layer, the
    /// uniform scale, the little sphere mesh with the transparent pickup material, the
    /// trigger, and the slow spin. Copied off the axe pickup rather than typed out, so a
    /// grenade on the ground reads as the same kind of thing as everything else on it.
    /// </summary>
    private static void CopyPickupShell(GameObject pickup, GameObject axePickup,
                                        List<string> fields, List<string> notes)
    {
        string path = $"{PickupsPath}/{pickup.name}";

        MeshFilter sourceFilter = axePickup != null ? axePickup.GetComponent<MeshFilter>() : null;
        MeshRenderer sourceRenderer = axePickup != null ? axePickup.GetComponent<MeshRenderer>() : null;
        SphereCollider sourceTrigger = axePickup != null ? axePickup.GetComponent<SphereCollider>() : null;
        RotateObject sourceSpin = axePickup != null ? axePickup.GetComponent<RotateObject>() : null;

        if (axePickup != null)
        {
            pickup.layer = axePickup.layer;
            pickup.transform.localScale = axePickup.transform.localScale;
        }
        else
        {
            notes.Add($"⚠ '{AxePickupPath}' is not in the scene, so '{pickup.name}' could not copy its " +
                      "layer, scale, glow sphere or trigger size and falls back to defaults.");
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
            trigger.radius = 0.6f;
        }

        // Every other pickup in this scene turns on the spot; one that does not would read
        // as scenery rather than as something to walk into. Delete the component if the
        // owner disagrees - nothing depends on it.
        RotateObject spin = pickup.AddComponent<RotateObject>();
        if (sourceSpin != null) spin.degreesPerSecond = sourceSpin.degreesPerSecond;

        fields.Add($"{path} shell: layer={LayerName(pickup.layer)}, " +
                   $"localScale={V3(pickup.transform.localScale)}, " +
                   $"SphereCollider(isTrigger=true, radius={Num(trigger.radius)}, " +
                   $"world radius={Num(trigger.radius * MaxAbs(pickup.transform.lossyScale))}), " +
                   $"RotateObject({Num(spin.degreesPerSecond)} deg/s)" +
                   (sourceFilter != null ? ", glow sphere mesh + material copied from the axe pickup" : ""));
    }

    /// <summary>
    /// How far the axe pickup floats above the ground under it, so the grenades float the
    /// same. A pickup sitting exactly on the terrain looks dropped rather than placed.
    /// </summary>
    private static float HoverHeight(GameObject axePickup)
    {
        if (axePickup == null) return DefaultHover;

        Vector3 origin = axePickup.transform.position + Vector3.up * 0.1f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Clamp(axePickup.transform.position.y - hit.point.y, 0.2f, 2f);
        }

        return DefaultHover;
    }

    /// <summary>
    /// A spot near the anchor that is on the ground and not inside anything. Eight compass
    /// directions rather than one, because the two indoor anchors have walls on some sides
    /// and a pickup buried in rock cannot be walked into - which is the whole point of it.
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

    // ---- Step 6: the audio profile ------------------------------------------

    /// <summary>
    /// Appends the Grenade profile to the WeaponAudioManager. This is the step the whole
    /// feature exists for: it is what puts the two assessed slots into the Ctrl+G report.
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
            notes.Add($"⚠ No placeholder beep at {BeepClipPath}, so the two grenade arrays were sized " +
                      "to 1 with an EMPTY element. The two rows still appear in the report, but as " +
                      "missing audio rather than as placeholders - put the clip back and set the two " +
                      "elements by hand.");
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
    /// Growing a SerializedProperty array COPIES the previous last element into the new one,
    /// so anything left unwritten here would silently inherit the Axe's clips and volumes -
    /// the grenade would ship holding the axe's swings.
    ///
    /// ⚠⚠ throwSounds and explosionSounds are sized to ONE and filled with the placeholder
    /// beep, deliberately. DistributionResetTools walks existing AudioClip properties and
    /// points each at the beep; it does not GROW an array, so a slot shipped at size 0 stays
    /// at size 0 through the handout reset and the grenade's two assessed rows would simply
    /// vanish from the student's report. Size 1 + beep is what every other unfilled slot in
    /// this project looks like, and it is what makes the report read 104 -> 106.
    ///
    /// Raise and lower are left empty on purpose: WeaponSoundProfile.IsAudioSlotRelevant
    /// hides both for WeaponType.Grenade, so filling them would put clips in the scene that
    /// no report ever mentions and no student is ever asked to author.
    /// </remarks>
    private static void WriteProfile(SerializedProperty profile, AudioClip beep, string path,
                                     List<string> fields)
    {
        SetProfileString(profile, "weaponName", ProfileName, path, fields);
        SetProfileInt(profile, "weaponId", (int)WeaponId.Grenade, path, fields,
                      $"{WeaponId.Grenade} ({(int)WeaponId.Grenade})");
        SetProfileInt(profile, "weaponType", (int)WeaponType.Grenade, path, fields,
                      $"{WeaponType.Grenade} ({(int)WeaponType.Grenade})");

        // Hidden by the filter for a Grenade, but written anyway so nothing is inherited.
        ClearProfileClips(profile, "shootSounds", path, fields);
        SetProfileFloat(profile, "shootVolume", 1f, path, fields);
        SetProfileFloat(profile, "minPitchVariation", 0.95f, path, fields);
        SetProfileFloat(profile, "maxPitchVariation", 1.05f, path, fields);

        SetProfileObject(profile, "raiseWeaponSound", null, path, fields);
        SetProfileObject(profile, "lowerWeaponSound", null, path, fields);
        SetProfileFloat(profile, "toggleSoundVolume", 1f, path, fields);
        SetProfileFloat(profile, "togglePitchVariation", 0.1f, path, fields);

        SetProfileObject(profile, "reloadStartSound", null, path, fields);
        SetProfileObject(profile, "reloadEndSound", null, path, fields);
        ClearProfileClips(profile, "reloadActionSounds", path, fields);
        SetProfileFloat(profile, "reloadVolume", 1f, path, fields);

        // The two assessed slots.
        SetProfileClip(profile, "throwSounds", beep, path, fields);
        SetProfileFloat(profile, "throwVolume", 1f, path, fields);
        SetProfileClip(profile, "explosionSounds", beep, path, fields);
        SetProfileFloat(profile, "explosionVolume", 1f, path, fields);
        SetProfileFloat(profile, "explosionMaxDistance", 80f, path, fields);
    }

    /// <summary>The index of the Grenade profile, by name or by id. -1 when there is none.</summary>
    private static int FindProfileIndex(SerializedProperty profiles)
    {
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);

            SerializedProperty id = profile.FindPropertyRelative("weaponId");
            if (id != null && id.intValue == (int)WeaponId.Grenade) return i;

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
    //  3. Reskin - for the scene that already has a tin can in it
    // ==========================================================================

    /// <summary>
    /// Swaps the tin-can stand-in for Assets/Grenade.fbx everywhere it is used, and points
    /// the three GrenadeItems at the pack's pineapple icon. Five places, each of which skips
    /// itself if it already carries the new model, so a second run changes nothing and says so.
    ///
    /// This exists because Build Grenade only builds what is MISSING - it skipped every one
    /// of these the moment they existed, which is what makes it safe to re-run and useless
    /// for changing something already built. Build Grenade on a fresh scene now produces
    /// exactly what this leaves behind; this is the same change applied to the scene the
    /// owner already has.
    ///
    /// ⚠ NOTHING about the grenade's behaviour is touched: the projectile keeps its
    /// SphereCollider, its Rigidbody and its Grenade component untouched, and Grenade.cs,
    /// GrenadeController.cs and GrenadeItem.cs are not edited. Only the picture changes.
    /// </summary>
    [MenuItem("Tools/Weapons/Reskin Grenade", priority = 1102)]
    private static void ReskinGrenade()
    {
        List<string> replaced = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to reskin: no model at {ModelPath}. Is the FBX still " +
                             "in the project? Nothing was changed - not the prefab, not the scene.");
            return;
        }

        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        if (icon == null)
        {
            notes.Add($"⚠ No sprite at {IconPath}, so the three GrenadeItems were LEFT holding whatever " +
                      "icon they had. Check the InterfaceApocalypseHUD pack imported and that the " +
                      "png's Texture Type is Sprite, then re-run - the models below are unaffected.");
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(ReskinUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // 1 of 5: the thing that is actually thrown. An ASSET write - see the log below.
        bool assetChanged = ReskinProjectilePrefab(model, replaced, skipped, notes);

        // 2 of 5: the weapon in the player's hands.
        bool sceneChanged = ReskinSceneModel(FindByPath(ControllerPath), ControllerPath, model,
                                             HandGrenadeHeight, false, replaced, skipped, notes);

        if (sceneChanged)
        {
            notes.Add("⏸ Worth an owner look in play: the model in the HAND is left on the FBX's own " +
                      "pivot, as the tin can was, but the two pivots are not in the same place - the " +
                      "can's was at its base, and the grenade's mesh centre is about (0, 0.02, 0.01) " +
                      "from the root. So the grenade sits a little lower in frame than the can did. " +
                      $"If it reads wrong, nudge '{ControllerPath}/{ModelChildName}'s localPosition - " +
                      "NOT the 'Grenade' root, whose transform is the raise/lower pose and is driven " +
                      "every frame.");
        }

        // 3, 4 and 5 of 5: the three pickups, and their icons.
        sceneChanged |= ReskinPickupModels(model, replaced, skipped, notes);
        sceneChanged |= ReskinIcons(icon, replaced, skipped, notes);

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (sceneChanged && scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        AppendLeftoverCanNote(notes);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Reskin Grenade finished. The model is {ModelPath} and the icon " +
                       $"is {IconPath}.");

        log.AppendLine($"REPLACED ({replaced.Count}):");
        AppendList(log, replaced, "  <none - everything already wore the new model and icon>");

        log.AppendLine($"SKIPPED, ALREADY RESKINNED ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing was already reskinned - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        if (!assetChanged && !sceneChanged)
        {
            log.AppendLine("NOTHING CHANGED. Every one of the five places already carried the new " +
                           "model and icon, so this run was a no-op - the scene was not even marked " +
                           "dirty. That is the expected result of a second run.");
        }
        else
        {
            if (assetChanged)
            {
                log.AppendLine($"⚠ ASSET WRITE, NOT UNDOABLE: {ProjectilePath} was rewritten on disk " +
                               "through PrefabUtility.SaveAsPrefabAsset. Ctrl+Z will not bring the tin " +
                               $"can back - `git checkout -- {ProjectilePath}` is the way back.");
            }

            log.AppendLine(sceneChanged
                ? $"The scene edits are Undo-recorded as one step, '{ReskinUndoLabel}'. " +
                  $"SAVE THE SCENE ({scene.name}) or they are lost."
                : "No scene object changed, so the scene was not marked dirty.");
        }

        Debug.Log(log.ToString().TrimEnd());
    }

    /// <summary>
    /// The projectile prefab. Opened with LoadPrefabContents rather than edited through an
    /// instance, so the change lands on the asset itself and no scene has to be involved -
    /// the same shape DistributionResetTools and LevelFeatureTools use.
    ///
    /// ⚠ The SphereCollider (r 0.07, which still fits a 0.06 m-wide body), the Rigidbody
    /// (mass 0.4) and the Grenade component are read past and left exactly as they are. Only
    /// the model child is touched.
    /// </summary>
    /// <returns>True when the asset on disk was rewritten.</returns>
    private static bool ReskinProjectilePrefab(GameObject model, List<string> replaced,
                                               List<string> skipped, List<string> notes)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath) == null)
        {
            notes.Add($"⚠ No projectile prefab at {ProjectilePath}, so there was nothing to reskin " +
                      "there. Run Tools/Weapons/Build Grenade first - it now builds the new model " +
                      "straight away.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(ProjectilePath);
        if (root == null)
        {
            Debug.LogWarning($"{LogPrefix} Could not open {ProjectilePath} for editing. The scene part " +
                             "of the reskin was still attempted.");
            return false;
        }

        try
        {
            if (root.transform.Find(ModelChildName) != null)
            {
                skipped.Add($"{ProjectilePath}/{ModelChildName} - already the new model");
                return false;
            }

            string before = DescribeModelChild(root.transform);
            int removed = RemoveLegacyModelChildren(root.transform, false);

            // recentre: true - the blast point, the throw origin and the physics centre all
            // read transform.position.
            GameObject visual = BuildModelChild(model, root.transform, HandGrenadeHeight, true,
                                                out float scale, out Bounds bounds);
            if (visual == null)
            {
                notes.Add($"⚠ {ProjectilePath}: the model child could not be built, so the prefab was " +
                          "NOT rewritten and still holds whatever it held.");
                return false;
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
            if (saved == null)
            {
                Debug.LogWarning($"{LogPrefix} Could not write {ProjectilePath}. Nothing else in this " +
                                 "step was changed.");
                return false;
            }

            replaced.Add($"{ProjectilePath}: {before} -> {DescribeModelChild(root.transform)}; " +
                         $"localScale multiplied by {Num(scale)} on top of the FBX's own " +
                         $"{V3(model.transform.localScale)}, target height {Num(HandGrenadeHeight)} m, " +
                         $"measured height {Num(bounds.size.y)} m, recentred on the root. " +
                         $"{removed} legacy '{LegacyModelChildName}' child(ren) destroyed. " +
                         "SphereCollider, Rigidbody and the Grenade component were not touched.");
            return true;
        }
        finally
        {
            // Must happen whether the save succeeded, refused or threw, or the preview scene
            // is left open and the next LoadPrefabContents on this path misbehaves.
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// One scene object's model child, swapped in place and Undo-recorded. Used for the
    /// weapon in the hand and for each of the three pickups - the only difference between
    /// them is the target height and whether the model is recentred on its parent.
    /// </summary>
    /// <returns>True when the scene was changed.</returns>
    private static bool ReskinSceneModel(GameObject host, string path, GameObject model,
                                         float targetHeight, bool recentre, List<string> replaced,
                                         List<string> skipped, List<string> notes)
    {
        if (host == null)
        {
            notes.Add($"⚠ '{path}' is not in the open scene, so nothing there was reskinned. Is the " +
                      "level loaded, and has Build Grenade been run?");
            return false;
        }

        if (host.transform.Find(ModelChildName) != null)
        {
            skipped.Add($"{path}/{ModelChildName} - already the new model");
            return false;
        }

        string before = DescribeModelChild(host.transform);
        int removed = RemoveLegacyModelChildren(host.transform, true);

        if (removed == 0)
        {
            notes.Add($"'{path}' had no '{LegacyModelChildName}' child to remove - the new model was " +
                      "added anyway. If the owner had renamed it by hand, that renamed object is " +
                      "still there and is now a second mesh; delete it.");
        }

        GameObject visual = BuildModelChild(model, host.transform, targetHeight, recentre,
                                            out float scale, out Bounds bounds);
        if (visual == null)
        {
            notes.Add($"⚠ '{path}': the model child could not be built. The old one is already gone, " +
                      "so this object now has no mesh - Undo, or re-run once the model loads.");
            return removed > 0;
        }

        Undo.RegisterCreatedObjectUndo(visual, ReskinUndoLabel);

        replaced.Add($"{path}: {before} -> {DescribeModelChild(host.transform)}; localScale multiplied " +
                     $"by {Num(scale)} on top of the FBX's own {V3(model.transform.localScale)}, " +
                     $"target height {Num(targetHeight)} m, measured height {Num(bounds.size.y)} m" +
                     (recentre ? ", recentred on the parent's origin" : ", left on the FBX's own pivot") +
                     $". {removed} legacy child(ren) destroyed.");
        return true;
    }

    /// <summary>The three pickups, at the world size, recentred so each spins on the spot.</summary>
    private static bool ReskinPickupModels(GameObject model, List<string> replaced,
                                           List<string> skipped, List<string> notes)
    {
        GameObject pickups = FindByPath(PickupsPath);
        if (pickups == null)
        {
            notes.Add($"⚠ '{PickupsPath}' is not in the open scene, so none of the three pickups was " +
                      "reskinned.");
            return false;
        }

        bool changed = false;

        foreach (string name in PickupNames)
        {
            Transform pickup = pickups.transform.Find(name);
            string path = $"{PickupsPath}/{name}";

            if (pickup == null)
            {
                notes.Add($"⚠ '{path}' is not in the scene, so it was not reskinned.");
                continue;
            }

            changed |= ReskinSceneModel(pickup.gameObject, path, model, WorldGrenadeHeight, true,
                                        replaced, skipped, notes);
        }

        return changed;
    }

    /// <summary>
    /// The two icon fields on each of the three GrenadeItems. Both get the same sprite: the
    /// pack ships no _Side view of this icon, and WeaponItem.GetHudIcon() falls back to
    /// itemIcon anyway, so writing hudIcon explicitly only makes the report say what is shown.
    /// </summary>
    private static bool ReskinIcons(Sprite icon, List<string> replaced, List<string> skipped,
                                    List<string> notes)
    {
        if (icon == null) return false;

        GameObject pickups = FindByPath(PickupsPath);
        if (pickups == null) return false;

        bool changed = false;
        int seen = 0;

        foreach (string name in PickupNames)
        {
            Transform pickup = pickups.transform.Find(name);
            if (pickup == null) continue;

            GrenadeItem item = pickup.GetComponent<GrenadeItem>();
            string path = $"{PickupsPath}/{name}";

            if (item == null)
            {
                notes.Add($"⚠ '{path}' carries no GrenadeItem, so its icons could not be set.");
                continue;
            }

            seen++;

            SerializedObject serialized = new SerializedObject(item);
            SerializedProperty itemIcon = serialized.FindProperty("itemIcon");
            SerializedProperty hudIcon = serialized.FindProperty("hudIcon");

            if (itemIcon == null || hudIcon == null)
            {
                notes.Add($"⚠ {path}: GrenadeItem has no serialized 'itemIcon' and/or 'hudIcon' - the " +
                          "field has been renamed, and this tool binds to it by name. NOT SET.");
                continue;
            }

            if (itemIcon.objectReferenceValue == icon && hudIcon.objectReferenceValue == icon)
            {
                skipped.Add($"{path} icons - already '{icon.name}'");
                continue;
            }

            string before = $"itemIcon={Obj(itemIcon.objectReferenceValue)}, " +
                            $"hudIcon={Obj(hudIcon.objectReferenceValue)}";

            itemIcon.objectReferenceValue = icon;
            hudIcon.objectReferenceValue = icon;

            // ApplyModifiedProperties (not ...WithoutUndo) registers the change itself, which
            // is what folds it into the group this menu item collapses.
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(item);

            replaced.Add($"{path} GrenadeItem icons: {before} -> both '{icon.name}' ({IconPath})");
            changed = true;
        }

        // Three is what the level has. A fourth would be an item this tool does not know
        // about, still showing the pipe bomb in the hotbar - worth saying out loud.
        GrenadeItem[] all = Object.FindObjectsByType<GrenadeItem>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None);
        if (all.Length != seen)
        {
            notes.Add($"⚠ The scene holds {all.Length} GrenadeItem(s) but only {seen} of them are the " +
                      $"three this tool knows by name ({string.Join(", ", PickupNames)}). The others " +
                      "still hold their old icons - point them at the sprite by hand, or rename them.");
        }

        return changed;
    }

    /// <summary>
    /// Destroys the tin-can children of one object, by exact name. By name rather than "every
    /// child with a renderer", because the weapon in the hand also owns a FirePoint and a
    /// pickup owns its glow sphere, and neither is the model.
    /// </summary>
    /// <param name="undoRecorded">True in the open scene; false inside a prefab-contents
    /// preview scene, where Undo does not apply and would warn.</param>
    private static int RemoveLegacyModelChildren(Transform parent, bool undoRecorded)
    {
        List<GameObject> doomed = new List<GameObject>();

        foreach (Transform child in parent)
        {
            if (child.name == LegacyModelChildName) doomed.Add(child.gameObject);
        }

        foreach (GameObject child in doomed)
        {
            if (undoRecorded) Undo.DestroyObjectImmediate(child);
            else Object.DestroyImmediate(child);
        }

        return doomed.Count;
    }

    /// <summary>
    /// Whether any tin can is still in the open scene. The integrator's check is
    /// `grep -c "m_Name: SM_Prop_Can_01"` on the saved scene reading 0; this is the same
    /// question asked before the save, so a miss is caught while it is still cheap.
    /// </summary>
    private static void AppendLeftoverCanNote(List<string> notes)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;

        List<string> found = new List<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == LegacyModelChildName) found.Add(HierarchyPath(transform));
            }
        }

        notes.Add(found.Count == 0
            ? $"No '{LegacyModelChildName}' left anywhere in the open scene. (The integrator's " +
              "`grep -c \"m_Name: SM_Prop_Can_01\"` on the saved scene should read 0 to match.)"
            : $"⚠ {found.Count} '{LegacyModelChildName}' object(s) still in the scene: " +
              $"{string.Join(", ", found)}. Those are not among the four the grenade used - check " +
              "whether they are level scenery before touching them.");
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
        fields.Add($"{path}.{name} = {Obj(value)}");
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
    /// Combined world bounds of every MESH under the object. False when it has none.
    /// </summary>
    /// <remarks>
    /// ⚠ From the MESHES, not from Renderer.bounds, for two independent reasons - both of
    /// which have already cost this project a debugging session (CODE_STRUCTURE, Editor row):
    ///
    /// 1. Renderer.bounds is a cached world AABB the engine refreshes when it draws. On a
    ///    GameObject instantiated moments ago in EDIT mode it has not been refreshed yet, so
    ///    it reads near-zero; dividing a target height by that gives an enormous scale.
    /// 2. It also includes ParticleSystemRenderers. Measuring a weapon that carries a muzzle
    ///    flash that way reported ~18 m of tracer instead of ~0.9 m of gun.
    ///
    /// Mesh.bounds is authored data on the asset: never stale, never a particle system. The
    /// eight corners are transformed rather than the centre and extents, because a rotated
    /// child (the grenade's spoon is one) has no meaningful axis-aligned box until its corners
    /// are in world space.
    /// </remarks>
    private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(root.transform.position, Vector3.zero);

        bool any = false;

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            EncapsulateMesh(filter.sharedMesh, filter.transform, ref bounds, ref any);
        }

        // Nothing in Assets/Grenade.fbx is skinned today, but the minigun's barrel is, and a
        // model this tool is pointed at tomorrow might be. Costs one loop and cannot include
        // a particle system.
        foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned.sharedMesh == null) continue;
            EncapsulateMesh(skinned.sharedMesh, skinned.transform, ref bounds, ref any);
        }

        return any;
    }

    /// <summary>The eight corners of one mesh's local bounds, in world space.</summary>
    private static void EncapsulateMesh(Mesh mesh, Transform on, ref Bounds bounds, ref bool any)
    {
        Bounds local = mesh.bounds;

        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                (corner & 4) == 0 ? -local.extents.z : local.extents.z);

            Vector3 world = on.TransformPoint(local.center + offset);

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

    /// <summary>
    /// The model child as it stands, measured with MESH bounds. One line, used by the report
    /// and by the reskin's before/after logging so the two can never disagree about how a
    /// size was arrived at. Looks for the new name first, then the tin can's.
    /// </summary>
    private static string DescribeModelChild(Transform parent)
    {
        if (parent == null) return "<no parent>";

        Transform child = parent.Find(ModelChildName);
        if (child == null) child = parent.Find(LegacyModelChildName);
        if (child == null) return "<none>";

        if (!TryGetWorldBounds(child.gameObject, out Bounds bounds))
        {
            return $"'{child.name}' localScale={V3(child.localScale)}, <no meshes to measure>";
        }

        return $"'{child.name}' localScale={V3(child.localScale)}, mesh bounds {V3(bounds.size)} m " +
               $"(height {Num(bounds.size.y)} m), centre {V3(bounds.center - parent.position)} from " +
               $"the root, materials=[{DistinctMaterialNames(child.gameObject)}]";
    }

    /// <summary>
    /// The distinct material names under an object, in the order they are first met. Named
    /// rather than counted because the whole point of the reskin is WHICH materials are on
    /// the grenade, and a count of eight would not say they are the FBX's own.
    /// </summary>
    private static string DistinctMaterialNames(GameObject root)
    {
        if (root == null) return "<none>";

        List<string> names = new List<string>();

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                string name = material == null ? "<missing>" : material.name;
                if (!names.Contains(name)) names.Add(name);
            }
        }

        return names.Count == 0 ? "<none>" : string.Join(", ", names);
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

    private static string Col(Color value) =>
        $"({Num(value.r)}, {Num(value.g)}, {Num(value.b)}, {Num(value.a)})";

    private static string Obj(Object value) => value == null ? "<none>" : $"'{value.name}'";

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? $"<unnamed>({layer})" : $"{name}({layer})";
    }

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

    private static string PropV3(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        return property == null ? "<no such field>" : V3(property.vector3Value);
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
