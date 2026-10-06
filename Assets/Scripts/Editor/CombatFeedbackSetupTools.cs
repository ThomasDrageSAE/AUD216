using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/*
 * CombatFeedbackSetupTools.cs
 *
 * Purpose: Wires the combat-feedback HUD into the scene - the CombatFeedback listener, the
 *          hitmarker image under the reticle, and the per-weapon screen kick that replaces
 *          the two hard-coded multipliers WeaponController.ApplyRecoil used to carry.
 * Menu:    Tools/HUD/Report Combat Feedback   (read only)
 *          Tools/HUD/Add Combat Feedback      (idempotent - a second run changes nothing)
 * Used by: the integrator, driven from the Unity CLI. The owner saves the scene afterwards.
 *
 * ⚠ SCENE CHANGES ONLY. Nothing here writes an asset: the CombatFeedback component, the
 * hitmarker GameObject and all eighteen shake numbers land in the open scene, are
 * Undo-recorded as one step, and are LOST unless the owner saves. (Its sibling
 * EnemyRoleSetupTools does write prefabs, and says so in its own log.)
 *
 * ⚠ NO DIALOGS anywhere in this file. Both items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block it with a modal window nobody can see,
 * let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Run Report before and after Add: it prints one block with a fixed line order and
 * invariant-culture numbers, so the two runs diff cleanly and the diff IS the record of what
 * Add did. The five distinct shake magnitudes the work order asks to see are that report's
 * last block.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE HITMARKER MUST BE A CHILD OF reticleObject
 * ---------------------------------------------------------------------------------------
 * Not cosmetic, and not a preference. ReticleController.Show(false) hides the crosshair by
 * DEACTIVATING reticleObject; a marker parented anywhere else stays lit on screen with no
 * crosshair around it - during a cutscene, while the axe is out, whenever the reticle is
 * down. ReticleController.OnValidate already warns about exactly this, so a marker built in
 * the wrong place would announce itself every time somebody selected the reticle. This tool
 * builds it under reticleObject and refuses to rewire a marker that is somewhere else.
 *
 * ---------------------------------------------------------------------------------------
 * ⚠ THE WEAPONS ARE FOUND, NOT PATHED
 * ---------------------------------------------------------------------------------------
 * Every other setup tool here walks a hierarchy path; this one deliberately does not, and
 * the crossbow is why: its GameObject is named "CrossBow" while its weaponName is "Crossbow"
 * and its profile is "Crossbow". A path of "Player/FirstPersonCharacter/WeaponParent/Crossbow"
 * looks right, compiles, runs, reports success on five of six weapons and silently leaves the
 * crossbow with WeaponController's class-default kick. So the weapons are found by TYPE
 * (FindObjectsByType<WeaponController>, inactive included - every weapon but the held one is
 * switched off) and matched by weaponId, which is authored on all six and is the same key the
 * WeaponAudioManager profiles use. weaponName is the fallback for a controller whose id is
 * still Unassigned.
 *
 * ⚠ Binds to serialized fields by literal name: on ReticleController "reticleObject" and
 * "hitMarker"; on WeaponController "weaponId", "weaponName", "m_ShakeMagnitude",
 * "m_ShakeDuration", "m_ShakeRoughness". Renaming any of those compiles clean and breaks this
 * silently - see CODE_STRUCTURE's editor-tooling warning.
 */

public static class CombatFeedbackSetupTools
{
    private const string LogPrefix = "[CombatFeedbackSetupTools]";
    private const string UndoLabel = "Add Combat Feedback";

    // ---- Scene paths ---------------------------------------------------------

    /// <summary>Where the scene's singletons live, beside the audio managers and the pools.</summary>
    private const string HostPath = "Game Management";

    private const string FeedbackName = "Combat Feedback";
    private const string HitMarkerName = "Hit Marker";

    // ---- Asset paths ---------------------------------------------------------

    private const string HudSpriteFolder =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Sprites/HUD/";

    /// <summary>
    /// The marker's graphic, in preference order. The grungy dial is the work order's own
    /// suggestion and matches the rusty Apocalypse HUD boxes the owner chose for every other
    /// widget in this level; the small circle is the plainer fallback if the pack is trimmed.
    ///
    /// ⚠ If neither loads the Image is left with NO sprite, which uGUI draws as a solid white
    /// box the size of the marker - a worse bug than no marker at all, because it only appears
    /// for the 0.12 s of a hit. The tool says so rather than shipping it quietly.
    /// </summary>
    private static readonly string[] MarkerSpriteNames =
    {
        "SPR_HUD_DialReticle_Sml_Grungy",
        "SPR_HUD_Reticle_Circle_Sml"
    };

    // ---- The numbers ---------------------------------------------------------

    /// <summary>
    /// The marker's size in canvas units. The reticle root is 100 x 100 and its four ticks
    /// are 32 x 32 sitting at +/-30, so the crosshair spans about 92: 64 puts the marker
    /// INSIDE the ticks, where a hit confirmation belongs, rather than around them where it
    /// would read as a second crosshair. Measured off the scene, not chosen.
    /// </summary>
    private const float MarkerSize = 64f;

    /// <summary>
    /// One weapon's screen kick. CombatFeedback's sibling half of the feature: Agent D deleted
    /// ApplyRecoil's two hard-coded branches (isRevolver x1.5 magnitude / x1.2 duration, isSMG
    /// x0.7 / x0.8), and these are the numbers that replace them - including real ones for the
    /// four weapons that only ever got the class defaults.
    /// </summary>
    private sealed class ShakeSetting
    {
        public readonly WeaponId Id;
        public readonly string Name;
        public readonly float Magnitude;
        public readonly float Duration;
        public readonly float Roughness;
        public readonly string Why;

        public ShakeSetting(WeaponId id, string name, float magnitude, float duration,
                            float roughness, string why)
        {
            Id = id;
            Name = name;
            Magnitude = magnitude;
            Duration = duration;
            Roughness = roughness;
            Why = why;
        }
    }

    /// <summary>
    /// Agent D's table, unchanged. Six rows, not five: the work order's Agent D section lists
    /// the grenade alongside the other five, and the scene carries six WeaponControllers.
    /// </summary>
    private static readonly ShakeSetting[] Shakes =
    {
        new ShakeSetting(WeaponId.Revolver, "Revolver", 0.075f, 0.24f, 10f,
            "the biggest kick in the level, and the only one that was already being faked - " +
            "the deleted branch multiplied the shared 0.05/0.2 by 1.5 and 1.2, which is " +
            "exactly 0.075 and 0.24. Nothing about the revolver changes; it is just written " +
            "down now"),

        new ShakeSetting(WeaponId.SMG, "SMG", 0.035f, 0.16f, 12f,
            "the other faked one: 0.05 x 0.7 = 0.035 and 0.2 x 0.8 = 0.16. Rougher than the " +
            "revolver because it fires every 0.1 s and a smooth shake at that rate reads as " +
            "a slow drift rather than as a gun"),

        new ShakeSetting(WeaponId.Minigun, "Minigun", 0.022f, 0.08f, 16f,
            "⚠ the number that matters. MinigunController.FireOneRound calls ApplyRecoil " +
            "TWENTY times a second for up to a hundred seconds of held trigger, so the shake " +
            "must finish inside 0.05 s of firing or every round stacks on the last. Small and " +
            "short and rough: a hundred seconds of anything larger is nausea, and that is the " +
            "owner's ⏸ play check"),

        new ShakeSetting(WeaponId.Crossbow, "Crossbow", 0.02f, 0.12f, 8f,
            "the softest firearm kick: a string releasing, not a charge going off. It had NO " +
            "kick at all before this - the deleted branches only knew the revolver and the SMG"),

        new ShakeSetting(WeaponId.Axe, "Axe", 0.06f, 0.18f, 6f,
            "the swing, not a recoil: heavy and SMOOTH (roughness 6, the lowest here), because " +
            "an axe lands as one weight rather than as a rattle"),

        new ShakeSetting(WeaponId.Grenade, "Grenade", 0f, 0f, 0f,
            "no kick from the THROW - an underarm lob does not move the camera. The blast has " +
            "its own shake, fired by Grenade.cs when it detonates. ⚠ Zero is safe: " +
            "CameraShakeController.ShakeCoroutine's loop is `while (elapsed < duration)`, so a " +
            "duration of 0 runs zero iterations and never divides by it")
    };

    // ==========================================================================
    //  1. Report
    // ==========================================================================

    /// <summary>
    /// Reads and prints, changing nothing. The integrator runs it either side of Add so the
    /// before/after is a plain diff; that is also why the line order is fixed and the numbers
    /// are invariant-culture.
    /// </summary>
    [MenuItem("Tools/HUD/Report Combat Feedback", priority = 1208)]
    private static void ReportCombatFeedback()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Combat Feedback (read only - nothing was changed).");

        AppendFeedbackReport(report);
        AppendReticleReport(report);
        AppendSoundSlotReport(report);
        AppendShakeReport(report);

        Debug.Log(report.ToString().TrimEnd());
    }

    private static void AppendFeedbackReport(StringBuilder report)
    {
        CombatFeedback feedback = Object.FindFirstObjectByType<CombatFeedback>(FindObjectsInactive.Include);

        if (feedback == null)
        {
            report.AppendLine("LISTENER: <no CombatFeedback in the open scene> - hits flash " +
                              "nothing and confirm nothing.");
            return;
        }

        // activeInHierarchy, not activeSelf: CombatFeedback subscribes to ZombieAI.OnZombieHit
        // in OnEnable, so a component on a switched-off parent is a listener that never listens.
        report.AppendLine($"LISTENER: {HierarchyPath(feedback.transform)}, " +
                          $"enabled={feedback.enabled}, " +
                          $"activeInHierarchy={feedback.gameObject.activeInHierarchy}" +
                          (feedback.gameObject.activeInHierarchy ? string.Empty :
                           " ⚠ INACTIVE - OnEnable never runs, so it never subscribes to " +
                           "ZombieAI.OnZombieHit"));
    }

    private static void AppendReticleReport(StringBuilder report)
    {
        ReticleController reticle = Object.FindFirstObjectByType<ReticleController>(FindObjectsInactive.Include);
        if (reticle == null)
        {
            report.AppendLine("RETICLE: <no ReticleController in the open scene> - there is " +
                              "nowhere to hang a hitmarker.");
            return;
        }

        SerializedObject serialized = new SerializedObject(reticle);
        GameObject root = FindObject(serialized, "reticleObject");
        RectTransform marker = FindObject<RectTransform>(serialized, "hitMarker");

        report.AppendLine($"RETICLE: {HierarchyPath(reticle.transform)}, " +
                          $"reticleObject={(root == null ? "<none>" : HierarchyPath(root.transform))}" +
                          (root == null ? string.Empty : $" (activeSelf={root.activeSelf}, " +
                                                         $"{root.transform.childCount} child(ren))"));

        if (marker == null)
        {
            report.AppendLine("  hitMarker: <none> - CombatFeedback resolves null on every hit " +
                              "and flashes nothing.");
            return;
        }

        Graphic graphic = marker.GetComponent<Graphic>();
        if (graphic == null) graphic = marker.GetComponentInChildren<Graphic>(true);

        bool underRoot = root != null && marker.IsChildOf(root.transform);

        report.AppendLine($"  hitMarker: {HierarchyPath(marker)} " +
                          (underRoot
                              ? "(under reticleObject - correct)"
                              : "⚠ NOT under reticleObject: Show(false) will leave it visible, " +
                                "and ReticleController.OnValidate warns about it"));
        report.AppendLine($"    size={V2(marker.sizeDelta)}, anchoredPosition={V2(marker.anchoredPosition)}, " +
                          $"localScale={Num(marker.localScale.x)}, activeSelf={marker.gameObject.activeSelf}");
        report.AppendLine($"    Graphic: " +
                          (graphic == null
                              ? "<none> ⚠ CombatFeedback.TryResolveMarker returns false and nothing flashes"
                              : $"{graphic.GetType().Name}, colour RGBA " +
                                $"{Num(graphic.color.r)}/{Num(graphic.color.g)}/{Num(graphic.color.b)}/{Num(graphic.color.a)}, " +
                                $"raycastTarget={graphic.raycastTarget}" +
                                (graphic is Image image
                                    ? $", sprite={Obj(image.sprite)}" +
                                      (image.sprite == null
                                          ? " ⚠ no sprite - uGUI draws a solid white box"
                                          : string.Empty)
                                    : string.Empty)));
    }

    /// <summary>
    /// The two UI slots the marker plays. Not this tool's to create - they are fields Agent D
    /// added to UIAudioManager - but the whole feature is silent without them and this report
    /// is where the integrator looks before believing the Ctrl+G count.
    /// </summary>
    private static void AppendSoundSlotReport(StringBuilder report)
    {
        UIAudioManager ui = Object.FindFirstObjectByType<UIAudioManager>(FindObjectsInactive.Include);
        if (ui == null)
        {
            report.AppendLine("UI SOUNDS: <no UIAudioManager in the open scene>");
            return;
        }

        SerializedObject serialized = new SerializedObject(ui);
        report.AppendLine($"UI SOUNDS on '{HierarchyPath(ui.transform)}': " +
                          $"hitMarker={Prop(serialized, "hitMarker")}, " +
                          $"headshotPing={Prop(serialized, "headshotPing")} " +
                          "(two assessed slots - EMPTY is the correct state for a handout; " +
                          "Ctrl+G is what grades them)");
    }

    private static void AppendShakeReport(StringBuilder report)
    {
        WeaponController[] weapons = FindWeapons();
        report.AppendLine($"SCREEN KICK: {weapons.Length} WeaponController(s) in the open scene.");

        List<WeaponController> unmatched = new List<WeaponController>(weapons);

        foreach (ShakeSetting shake in Shakes)
        {
            WeaponController weapon = MatchWeapon(weapons, shake);
            if (weapon == null)
            {
                report.AppendLine($"  {shake.Name}: <not in the scene>");
                continue;
            }

            unmatched.Remove(weapon);

            SerializedObject serialized = new SerializedObject(weapon);
            report.AppendLine($"  {shake.Name} ({HierarchyPath(weapon.transform)}): " +
                              $"magnitude={PropNum(serialized, "m_ShakeMagnitude")} " +
                              $"(wants {Num(shake.Magnitude)}), " +
                              $"duration={PropNum(serialized, "m_ShakeDuration")} " +
                              $"(wants {Num(shake.Duration)}), " +
                              $"roughness={PropNum(serialized, "m_ShakeRoughness")} " +
                              $"(wants {Num(shake.Roughness)})");
        }

        // A weapon nobody claimed is a weapon still on the class defaults, and the work order's
        // check is "five weapons report five different shake magnitudes" - so an unmatched one
        // has to be visible, not quietly absent from the list above.
        foreach (WeaponController weapon in unmatched)
        {
            SerializedObject serialized = new SerializedObject(weapon);
            report.AppendLine($"  ⚠ UNMATCHED {HierarchyPath(weapon.transform)}: " +
                              $"weaponId={PropEnum<WeaponId>(serialized, "weaponId")}, " +
                              $"weaponName='{PropString(serialized, "weaponName")}' - this tool " +
                              "has no row for it and it keeps WeaponController's class defaults " +
                              $"(magnitude={PropNum(serialized, "m_ShakeMagnitude")}, " +
                              $"duration={PropNum(serialized, "m_ShakeDuration")}, " +
                              $"roughness={PropNum(serialized, "m_ShakeRoughness")})");
        }
    }

    // ==========================================================================
    //  2. Add
    // ==========================================================================

    /// <summary>
    /// Builds the three pieces in the order they depend on each other: the listener, the
    /// marker it flashes, and the kick that is the rest of the feel. Every step skips what is
    /// already correct, so a second run changes nothing and says so.
    /// </summary>
    [MenuItem("Tools/HUD/Add Combat Feedback", priority = 1209)]
    private static void AddCombatFeedback()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        EnsureFeedback(fields, skipped, notes);
        EnsureHitMarker(fields, skipped, notes);
        ApplyShakes(fields, skipped, notes);

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Add Combat Feedback finished. SCENE changes only - no asset " +
                       "was written, so there is nothing here `git checkout` would have to undo.");

        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - the scene already matched every number>");

        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"Every change is Undo-recorded as one step, '{UndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or they are lost. Then run " +
                       "Tools/HUD/Report Combat Feedback again and diff it against the run " +
                       "before this one - that diff is the record of what changed.");

        Debug.Log(log.ToString().TrimEnd());
    }

    // ---- Step 1: the listener ------------------------------------------------

    /// <summary>
    /// CombatFeedback under Game Management, beside the other scene-lifetime singletons. It
    /// needs no wiring at all: it subscribes to the STATIC ZombieAI.OnZombieHit and asks
    /// ReticleController for the marker fresh on every hit, so there is no reference here that
    /// a scene reload could leave dangling.
    /// </summary>
    private static void EnsureFeedback(List<string> fields, List<string> skipped, List<string> notes)
    {
        CombatFeedback existing = Object.FindFirstObjectByType<CombatFeedback>(FindObjectsInactive.Include);
        if (existing != null)
        {
            skipped.Add($"{HierarchyPath(existing.transform)} already has a CombatFeedback");

            if (!existing.gameObject.activeInHierarchy)
            {
                notes.Add($"⚠ {HierarchyPath(existing.transform)} is INACTIVE. CombatFeedback " +
                          "subscribes in OnEnable, so an inactive one never hears a hit. " +
                          "Switch it on by hand - this tool does not activate objects somebody " +
                          "may have switched off on purpose.");
            }

            return;
        }

        GameObject host = FindByPath(HostPath);
        if (host == null)
        {
            Debug.LogWarning($"{LogPrefix} No '{HostPath}' object in the open scene, so the " +
                             "CombatFeedback listener was not created. The hitmarker and the " +
                             "shake values below were still applied.");
            notes.Add($"⚠ No '{HostPath}' in the scene: CombatFeedback was NOT created, so " +
                      "nothing flashes the marker or plays the confirmation.");
            return;
        }

        GameObject made = new GameObject(FeedbackName);
        Undo.RegisterCreatedObjectUndo(made, UndoLabel);
        made.transform.SetParent(host.transform, false);
        Undo.AddComponent<CombatFeedback>(made);

        fields.Add($"created {HierarchyPath(made.transform)} with a CombatFeedback " +
                   "(no references to wire: it listens to the static ZombieAI.OnZombieHit and " +
                   "reads ReticleController.HitMarker at every hit)");
    }

    // ---- Step 2: the marker --------------------------------------------------

    /// <summary>
    /// One uGUI Image under the reticle's own root, wired into ReticleController.hitMarker.
    /// </summary>
    /// <remarks>
    /// ⚠ Alpha 0, not inactive. CombatFeedback.Paint drives colour, alpha and scale together
    /// and never touches activeSelf; a marker built switched OFF would stay off for ever and
    /// the flash would be a coroutine writing colours onto something nobody can see.
    ///
    /// ⚠ raycastTarget false. It sits over the middle of the screen on a Screen Space Overlay
    /// canvas; left true it is an invisible 64 px button between the player and anything the
    /// UI raycaster is meant to reach.
    /// </remarks>
    private static void EnsureHitMarker(List<string> fields, List<string> skipped, List<string> notes)
    {
        ReticleController reticle = Object.FindFirstObjectByType<ReticleController>(FindObjectsInactive.Include);
        if (reticle == null)
        {
            Debug.LogWarning($"{LogPrefix} No ReticleController in the open scene, so no hitmarker " +
                             "was built. Everything else was still done.");
            notes.Add("⚠ No ReticleController in the scene: the hitmarker was NOT built, and " +
                      "CombatFeedback will resolve null on every hit and flash nothing.");
            return;
        }

        SerializedObject serialized = new SerializedObject(reticle);
        SerializedProperty markerProperty = serialized.FindProperty("hitMarker");
        if (markerProperty == null)
        {
            notes.Add("⚠ ReticleController has no serialized field 'hitMarker' - it has been " +
                      "renamed, and this tool binds to it by name. NOT SET.");
            return;
        }

        GameObject root = FindObject(serialized, "reticleObject");
        if (root == null)
        {
            notes.Add("⚠ ReticleController.reticleObject is empty, so there is no crosshair root " +
                      "to hang the marker under. A marker parented anywhere else would stay on " +
                      "screen after Show(false), which is the one thing this must not do, so " +
                      "nothing was built. Wire reticleObject and re-run.");
            return;
        }

        // Already wired. The only thing worth checking is the thing OnValidate checks.
        if (markerProperty.objectReferenceValue is RectTransform already)
        {
            if (already.IsChildOf(root.transform))
            {
                skipped.Add($"{HierarchyPath(reticle.transform)} hitMarker already = " +
                            $"{HierarchyPath(already)}");
            }
            else
            {
                notes.Add($"⚠ hitMarker is already wired to {HierarchyPath(already)}, which is NOT " +
                          $"under {HierarchyPath(root.transform)}. Show(false) will leave it " +
                          "visible. Left alone rather than repointed - somebody put it there on " +
                          "purpose - but move it under the reticle root or clear the field and " +
                          "re-run.");
            }

            return;
        }

        // A marker built by an earlier run whose reference was lost (a reverted scene save is
        // the usual way). Re-wire it rather than building a second one.
        Transform existing = root.transform.Find(HitMarkerName);
        if (existing is RectTransform found)
        {
            markerProperty.objectReferenceValue = found;
            serialized.ApplyModifiedProperties();
            fields.Add($"{HierarchyPath(reticle.transform)} hitMarker = the existing " +
                       $"'{HitMarkerName}' already under the reticle root (re-wired - the field " +
                       "was empty)");
            return;
        }

        Sprite sprite = FindMarkerSprite(notes);

        GameObject marker = new GameObject(HitMarkerName, typeof(RectTransform),
                                           typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(marker, UndoLabel);

        // worldPositionStays false: a RectTransform dropped into a canvas with its world
        // position kept lands wherever it was in world space, which for an object created at
        // the origin is nowhere near the screen.
        marker.transform.SetParent(root.transform, false);
        marker.layer = root.layer;

        RectTransform rect = (RectTransform)marker.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
        rect.localScale = Vector3.one;

        Image image = marker.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.preserveAspect = true;

        // White at alpha 0. CombatFeedback writes its own hitColour / headshotColour over this
        // on every hit, so the only part that matters here is the ZERO: a marker that shipped
        // opaque would sit on the crosshair for ever until the first hit put it away.
        image.color = new Color(1f, 1f, 1f, 0f);

        markerProperty.objectReferenceValue = rect;
        serialized.ApplyModifiedProperties();

        fields.Add($"created {HierarchyPath(rect)} - Image, sprite={Obj(sprite)}, " +
                   $"{Num(MarkerSize)} x {Num(MarkerSize)} centred, colour white ALPHA 0, " +
                   $"raycastTarget=false, layer={LayerName(marker.layer)}; and " +
                   $"{HierarchyPath(reticle.transform)} hitMarker points at it");

        notes.Add($"The marker is a CHILD of {HierarchyPath(root.transform)}, which is what makes " +
                  "ReticleController.Show(false) take it down with the crosshair. " +
                  "ReticleController.OnValidate warns if it ever stops being one.");
    }

    /// <summary>
    /// The marker's sprite, by name across the project rather than by path: the Synty packs
    /// under Assets/Inbox have been reorganised before and will be again, and a name survives
    /// that where a path does not.
    /// </summary>
    private static Sprite FindMarkerSprite(List<string> notes)
    {
        foreach (string name in MarkerSpriteNames)
        {
            // The direct path first, because it is one load rather than a project-wide search.
            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(HudSpriteFolder + name + ".png");
            if (direct != null) return direct;

            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Sprite"))
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));

                // FindAssets matches substrings, so the name is re-checked.
                if (sprite != null && sprite.name == name) return sprite;
            }
        }

        notes.Add($"⚠ None of [{string.Join(", ", MarkerSpriteNames)}] is in the project, so the " +
                  "hitmarker Image has NO sprite and uGUI will draw it as a solid white box for " +
                  "the 0.12 s of every hit. Drop a sprite into its Source Image by hand, or " +
                  "check the InterfaceApocalypseHUD pack imported.");
        return null;
    }

    // ---- Step 3: the kick ----------------------------------------------------

    /// <summary>
    /// Writes the eighteen numbers. Each is compared before it is written, so a second run
    /// reports every one as already correct and touches nothing.
    /// </summary>
    private static void ApplyShakes(List<string> fields, List<string> skipped, List<string> notes)
    {
        WeaponController[] weapons = FindWeapons();
        if (weapons.Length == 0)
        {
            Debug.LogWarning($"{LogPrefix} No WeaponController in the open scene, so no shake " +
                             "values were written. Is the level loaded?");
            notes.Add("⚠ No WeaponController in the scene: all six kick settings were skipped.");
            return;
        }

        List<WeaponController> unmatched = new List<WeaponController>(weapons);

        foreach (ShakeSetting shake in Shakes)
        {
            WeaponController weapon = MatchWeapon(weapons, shake);
            if (weapon == null)
            {
                notes.Add($"⚠ No '{shake.Name}' WeaponController in the scene (looked for " +
                          $"weaponId={shake.Id}, then for a weaponName containing '{shake.Name}'), " +
                          "so its kick was not written.");
                continue;
            }

            unmatched.Remove(weapon);

            string path = HierarchyPath(weapon.transform);
            SerializedObject serialized = new SerializedObject(weapon);
            List<string> changed = new List<string>();

            SetFloat(serialized, "m_ShakeMagnitude", shake.Magnitude, path, changed, skipped, notes);
            SetFloat(serialized, "m_ShakeDuration", shake.Duration, path, changed, skipped, notes);
            SetFloat(serialized, "m_ShakeRoughness", shake.Roughness, path, changed, skipped, notes);

            if (changed.Count == 0) continue;

            // Undo-recorded: ApplyModifiedProperties registers the change against the object,
            // and the whole item collapses into one step.
            serialized.ApplyModifiedProperties();

            fields.Add($"{shake.Name} ({path}) {string.Join(", ", changed)} [{shake.Why}]");
        }

        foreach (WeaponController weapon in unmatched)
        {
            SerializedObject serialized = new SerializedObject(weapon);
            notes.Add($"⚠ {HierarchyPath(weapon.transform)} (weaponId=" +
                      $"{PropEnum<WeaponId>(serialized, "weaponId")}, weaponName=" +
                      $"'{PropString(serialized, "weaponName")}') matched no row in this tool's " +
                      "table, so it keeps WeaponController's class defaults (0.05 / 0.2 / 10). " +
                      "Add a row for it if it is a real weapon.");
        }
    }

    /// <summary>
    /// Every WeaponController in the scene, inactive included: every weapon but the one in the
    /// player's hands is switched off, and FindObjectsByType without the flag would find one.
    /// </summary>
    private static WeaponController[] FindWeapons()
    {
        return Object.FindObjectsByType<WeaponController>(FindObjectsInactive.Include,
                                                          FindObjectsSortMode.None);
    }

    /// <summary>
    /// One weapon, by id first and then by name. The id is the compile-checked key and is
    /// authored on all six controllers in this scene; the name test is the fallback for a
    /// controller whose id is still Unassigned, and is a Contains rather than an equals
    /// because the scene spells the crossbow's GameObject "CrossBow" - see the file header.
    /// </summary>
    private static WeaponController MatchWeapon(WeaponController[] weapons, ShakeSetting shake)
    {
        foreach (WeaponController weapon in weapons)
        {
            SerializedProperty id = new SerializedObject(weapon).FindProperty("weaponId");
            if (id != null && id.intValue == (int)shake.Id) return weapon;
        }

        // Only after every id has been considered, so an Unassigned controller never wins a
        // match away from the correctly-identified one.
        foreach (WeaponController weapon in weapons)
        {
            SerializedProperty name = new SerializedObject(weapon).FindProperty("weaponName");
            if (name == null || string.IsNullOrEmpty(name.stringValue)) continue;

            if (name.stringValue.IndexOf(shake.Name, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return weapon;
            }
        }

        return null;
    }

    private static void SetFloat(SerializedObject target, string name, float value, string path,
                                 List<string> changed, List<string> skipped, List<string> notes)
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null)
        {
            notes.Add($"⚠ {path} has no serialized field '{name}' - it has been renamed, and this " +
                      "tool binds to it by name. NOT SET.");
            return;
        }

        if (Mathf.Approximately(property.floatValue, value))
        {
            skipped.Add($"{path} {name} is already {Num(value)}");
            return;
        }

        float was = property.floatValue;
        property.floatValue = value;
        changed.Add($"{name} {Num(was)} -> {Num(value)}");
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

    /// <summary>An object-reference field's value, or null when the field has been renamed.</summary>
    private static T FindObject<T>(SerializedObject target, string name) where T : Object
    {
        SerializedProperty property = target.FindProperty(name);
        return property == null ? null : property.objectReferenceValue as T;
    }

    private static GameObject FindObject(SerializedObject target, string name) =>
        FindObject<GameObject>(target, name);

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

    private static string V2(Vector2 value) => $"({Num(value.x)}, {Num(value.y)})";

    private static string Obj(Object value) => value == null ? "<none>" : $"'{value.name}'";

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? $"<unnamed>({layer})" : $"{name}({layer})";
    }

    private static string Prop(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
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

    private static string PropEnum<T>(SerializedObject target, string name) where T : System.Enum
    {
        SerializedProperty property = target.FindProperty(name);
        if (property == null) return "<no such field>";
        return $"{(T)System.Enum.ToObject(typeof(T), property.intValue)} ({property.intValue})";
    }
}
