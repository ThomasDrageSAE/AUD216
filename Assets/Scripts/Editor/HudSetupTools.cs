using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using TMPro;

/*
 * HudSetupTools.cs
 *
 * Purpose: The two HUD scene edits the Synty demo layout asks for - the compass strip moved
 *          from the top centre to the top right, and the HotbarSelectionUI component that
 *          lights the Synty yellow border on whichever hotbar slot is in the player's hands.
 *          Plus the read-only report the other two are diffed against.
 * Menu:    Tools/HUD/Report HUD Setup        (read only)
 *          Tools/HUD/Move Compass Top Right  (idempotent scene edit, Undo-recorded)
 *          Tools/HUD/Add Hotbar Selection    (idempotent scene edit, Undo-recorded)
 * Used by: the integrator, driven from the Unity CLI, per the work order in
 *          Documentation/HUD_AND_BARREL_TASKS.md (Agent C). The owner saves the scene.
 *
 * Menu items rather than hand edits of the .unity file, for this project's standing reason:
 * the files on disk are routinely older than what is open in the editor, so a hand edit
 * would be lost the next time the owner saves. Same reason GrenadeSetupTools and
 * MinigunSetupTools next door are menu items.
 *
 * ⚠ NO DIALOGS anywhere in this file. All three items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block it with a modal window nobody can see,
 * let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Run Report before and after each apply item: it prints one block with a fixed line order
 * and invariant-culture numbers, so the two runs diff cleanly and the diff IS the record of
 * what changed.
 *
 * EVERYTHING here is a SCENE edit. Nothing is written to disk - no AssetDatabase call in the
 * file - so Undo works for all of it and the owner must SAVE THE SCENE or it is lost.
 *
 * ⚠ Unlike the weapon tools, this one binds almost nothing by serialized-field name. The
 * fields it reads (Compass.viewDirection / compassElement / compassSize, EnemyRadar and
 * ObjectiveRadar's player / radarCanvas / compassBar / compassBarWidth, and
 * ItemInventoryUI.ItemSlots) are all PUBLIC, so a rename breaks the build here rather than
 * breaking silently. What it does bind to by literal string is the Synty prefab's GameObject
 * NAMES: "HUD_Compass_Military", "HUD_Hotbar_Minimal", "CompassMask", "Btn_HotbarItem_0N",
 * and each button's "Item" / "Selected" / "Highlighted" children. Renaming any of those
 * compiles clean and breaks this - and would also break HotbarSelectionUI, which derives the
 * same Selected objects from the same structure.
 *
 * ⚠ The compass's own parent in the authored scene is called "Top " - with a TRAILING SPACE.
 * That is not a typo below; it is the name in the scene.
 *
 * ⚠ UNITS. Canvas - Interface is ScreenSpaceOverlay with a CanvasScaler in ScaleWithScreenSize
 * at reference resolution 3840x2160, ScreenMatchMode.MatchWidthOrHeight with match = 1 (pure
 * height match). So every pixel number in this file and in the report is a 3840x2160
 * REFERENCE pixel, not a screen pixel: on a 1920x1080 display each of them is half a device
 * pixel, and on an ultrawide the horizontal margin grows because only height is matched.
 * Report HUD Setup prints the live scaler values so this can be checked rather than trusted.
 *
 * ⚠ The compass strip's SIZE is never written by this file. Its 1000x100 is what
 * Compass.compassSize = 1560 (and EnemyRadar/ObjectiveRadar.compassBarWidth = 1560) were
 * tuned against; shrinking the strip means retuning all three, which is not in this order.
 * Move Compass Top Right records sizeDelta before and after and warns if they ever differ.
 */

public static class HudSetupTools
{
    private const string LogPrefix = "[HudSetupTools]";
    private const string MoveUndoLabel = "Move Compass Top Right";
    private const string SelectionUndoLabel = "Add Hotbar Selection";
    private const string AmmoFitUndoLabel = "Fit Ammo Readout";
    private const string CentreUndoLabel = "Move Compass Top Centre";
    private const string MinimapUndoLabel = "Add Minimap";
    private const string PromptUndoLabel = "Move Interaction Prompt Beside Minimap";
    private const string SettingsBackgroundUndoLabel = "Stretch Settings Background";

    /// <summary>
    /// The settings (pause) menu canvas - the leading space is authored. Its backgrounds
    /// were sized by a stack of sizeDelta and localScale hacks (a stretch-anchored child with
    /// sizeDelta 3678 x 2255 at scale 0.26 under a parent at scale 2) that happen to land
    /// the vignette sprite at exactly the canvas width on a 16:9 view - so its rounded
    /// corners show at 4K, and at a wider Free Aspect view it falls 50 px short each side.
    /// Measured 2026-09-06.
    /// </summary>
    private const string SettingsCanvasName = " UI Canvas";
    private const string SettingsBackgroundPath = "Background";
    private const string SettingsFillPath = "Background/Background";
    private const string SettingsStripesPath = "Background/HazardStripes";
    private const string SettingsVignettePath = "Background/SPR_Vignette";

    /// <summary>
    /// How far past the canvas the vignette sprite reaches on every side, as a fraction of
    /// the canvas. The sprite is a rounded octagon; 0.2 keeps the rounding off-screen at 16:9
    /// and at the widest Free Aspect the owner works in, and still leaves its darkening at
    /// the screen's edges.
    /// </summary>
    private const float SettingsOverflow = 0.2f;

    /// <summary>
    /// The context-sensitive interaction widget - "Firesticks / E / light" and every other
    /// prompt InteractionPromptUI shows. Authored under the "Top Right" region, which is
    /// exactly where the minimap now is (measured 2026-09-06: widget canvas x 1519..2179,
    /// minimap 1624..2024), so it sits under the map. It moves to the map's left.
    /// </summary>
    private const string PromptWidgetName = "HUD_Interact_ContextSensitive_Object";

    /// <summary>
    /// Reference px between the prompt's right edge, as it measures in the editor, and the
    /// minimap's left edge. Generous, because the action label resizes to its text and grows
    /// TOWARDS the map: the editor's placeholder "Light Fire" measured 80 px wider than the
    /// runtime "light", and the longest string the code shows - "inventory full!" - is wider
    /// again. 120 leaves that one clear; measured 2026-09-06.
    /// </summary>
    private const float PromptGap = 120f;

    /// <summary>
    /// Where the strip lived before 2026-09-06, read back from the scene as it was committed
    /// at 714d751^: under the "Top " region, anchored and pivoted top-centre, 106.7 reference
    /// px below the region's top edge. The owner's "where it was originally".
    /// </summary>
    private static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);
    private static readonly Vector2 OriginalCompassPosition = new Vector2(0f, -106.7f);

    /// <summary>
    /// The minimap: the owner's pick from the pack, 2026-09-06 - "compass" in the earlier
    /// request meant this. 400 reference px on a 3840-wide canvas, so about 200 px at 1080p,
    /// which is the proportion of the Synty demo screenshot; scale 1, unlike the compass dial
    /// tried before it, which at 0.6 was a 105 px coin.
    /// </summary>
    private const string MinimapPrefabPath =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Prefabs/Minimap/HUD_Minimap_Dial_LightsWires01.prefab";
    private const string MinimapName = "HUD_Minimap";
    private const float MinimapMargin = 30f;

    /// <summary>The compass dial this replaces. Removed if found.</summary>
    private const string OldDialName = "HUD_Compass_Dial";

    /// <summary>Inside the prefab: the mask, the demo map to remove, the sample icons' folder, the player marker's turning child.</summary>
    private const string MinimapMaskPath = "Minimap";
    private const string MinimapDemoMapPath = "Minimap/Map_Container/Map";
    private const string MinimapMapContainerPath = "Minimap/Map_Container";
    private const string MinimapElementsPath = "Minimap/Elements_Container";
    private const string MinimapPlayerMarkerPath = "Minimap/Map_Icon_Player/ICN";
    private const string MinimapLiveImageName = "Map_Live";

    private const string EnemyIconPrefabPath =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Prefabs/Map_Compass_Icons/Map_Icon_Enemy.prefab";
    private const string ObjectiveIconPrefabPath =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Prefabs/Map_Compass_Icons/Map_Icon_Objective Variant.prefab";

    /// <summary>The camera goes under the Player root, so it is torn down with the player on reload.</summary>
    private const string PlayerPath = "Player";
    private const string MinimapCameraName = "Minimap Camera";

    /// <summary>
    /// What the minimap camera does NOT draw: the player's own body and gun, the enemies
    /// (they are icons), the UI, and trigger volumes. Everything else - terrain, water, props,
    /// trees - is the ground the map shows.
    /// </summary>
    private static readonly string[] MinimapHiddenLayers = { "Player", "Enemy", "UI", "TriggerBoxes", "TransparentFX", "Ignore Raycast" };

    /// <summary>The two round-counter labels on CurrentWeaponHUD, by serialized name.</summary>
    private static readonly string[] AmmoLabelFields = { "ammoLabel", "ammoTotalLabel" };

    /// <summary>The widest string the counter has to hold: the minigun's belt, with the
    /// separator the total label draws in front of it.</summary>
    private const string WidestAmmoString = "/ 2000";

    /// <summary>The floor auto-sizing may shrink the counter to. Still legible at 1080p.</summary>
    private const float AmmoLabelMinFontSize = 32f;

    // ---- Scene paths ---------------------------------------------------------
    // ⚠ These start at "Game Management", not at the canvas: every canvas in this level is
    // parented under Game Management/Canvases. The work order's shorter paths
    // ("Canvas - Interface/HUD/...") are the paths as they read in the Hierarchy window
    // once that root is expanded, and would find nothing from a scene root.

    private const string CanvasesPath = "Game Management/Canvases";
    private const string InterfaceCanvasPath = CanvasesPath + "/Canvas - Interface";
    private const string ScreenSpacePath = InterfaceCanvasPath + "/HUD/ScreenSpace";

    /// <summary>⚠ Trailing space. That is the authored name of the top-centre region.</summary>
    private const string TopRegionPath = ScreenSpacePath + "/Top ";

    private const string TopRightRegionPath = ScreenSpacePath + "/Top Right";
    private const string CompassName = "HUD_Compass_Military";
    private const string HotbarPath = ScreenSpacePath + "/Bottom/HUD_Hotbar_Minimal";
    private const string HotbarName = "HUD_Hotbar_Minimal";

    private const string RadarCanvasPath = CanvasesPath + "/Canvas - Compass Enemies & Objectives";
    private const string CompassMaskName = "CompassMask";

    /// <summary>
    /// Where Move Compass Top Right parents the compass.
    ///
    /// ScreenSpace, NOT the "Top Right" layout region, and the choice is not arbitrary - see
    /// the block comment on MoveCompassTopRight for the two measured reasons. The one-line
    /// change to move it into the region instead is this constant plus the anchoredPosition
    /// compensation the region's own offset would need.
    /// </summary>
    private const string CompassTargetParentPath = ScreenSpacePath;

    // ---- Hotbar names --------------------------------------------------------

    private const string HotbarButtonPrefix = "Btn_HotbarItem_";
    private const string ItemChildName = "Item";
    private const string SelectedChildName = "Selected";
    private const string HighlightedChildName = "Highlighted";

    /// <summary>
    /// The hotbar prefab ships SIX buttons; Inventory only ever fills five (its MAX_SLOTS is
    /// a private const, so this tool counts ItemInventoryUI.ItemSlots instead of reading it).
    /// Button 05 is already switched off and parked outside the visible row - the report says
    /// so, and no apply item here touches it.
    /// </summary>
    private const int InventorySlots = 5;

    private const int HotbarButtons = 6;

    // ---- The numbers ---------------------------------------------------------

    /// <summary>
    /// Gap from the canvas's top and right edges to the compass's top-right corner, in
    /// 3840x2160 reference pixels (~1% of the reference width; ~20 device px on a 1080p
    /// screen). The owner's to tune by eye against the demo screenshot - it is the only
    /// aesthetic number in this file.
    /// </summary>
    private const float Margin = 40f;

    /// <summary>Anchor, and pivot, of the canvas's top-right corner.</summary>
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);

    private static readonly Vector2 TargetAnchoredPosition = new Vector2(-Margin, -Margin);

    /// <summary>
    /// Slack for the "is it already there?" test. RectTransform numbers are floats the owner
    /// may have nudged by a fraction of a pixel in the inspector; a hundredth of a reference
    /// pixel is invisible and re-applying over it would be noise, not a fix.
    /// </summary>
    private const float PositionEpsilon = 0.01f;

    /// <summary>
    /// One 8-bit alpha step. A uGUI Graphic below this contributes nothing a screenshot can
    /// show - which is exactly the state CompassMask is in.
    /// </summary>
    private const float InvisibleAlpha = 1f / 255f;

    // ==========================================================================
    //  1. Report
    // ==========================================================================

    /// <summary>
    /// Reads and prints, changing nothing. The integrator runs it either side of the two
    /// apply items so the before/after is a plain diff; that is also why the line order is
    /// fixed and every number is invariant-culture.
    /// </summary>
    [MenuItem("Tools/HUD/Report HUD Setup", priority = 1200)]
    private static void ReportHudSetup()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report HUD Setup (read only - nothing was changed).");

        Scene scene = SceneManager.GetActiveScene();
        report.AppendLine($"SCENE: '{scene.name}' valid={scene.IsValid()}, loaded={scene.isLoaded}");

        AppendCanvasReport(report);
        AppendCompassReport(report);
        AppendTopRightReport(report);
        AppendRadarReport(report);
        AppendCompassMaskReport(report);
        AppendHotbarReport(report);

        Debug.Log(report.ToString().TrimEnd());
    }

    /// <summary>
    /// The scaler first, because it is what makes every other number in the report mean
    /// something. Nothing below is in screen pixels.
    /// </summary>
    private static void AppendCanvasReport(StringBuilder report)
    {
        GameObject host = FindByPath(InterfaceCanvasPath);
        if (host == null)
        {
            report.AppendLine($"CANVAS {InterfaceCanvasPath}: ABSENT - every path below is relative " +
                              "to it, so expect the rest of this report to be empty too.");
            return;
        }

        Canvas canvas = host.GetComponent<Canvas>();
        CanvasScaler scaler = host.GetComponent<CanvasScaler>();

        report.AppendLine($"CANVAS {InterfaceCanvasPath}: PRESENT activeSelf={host.activeSelf}, " +
                          $"renderMode={(canvas != null ? canvas.renderMode.ToString() : "<no Canvas>")}, " +
                          $"sortingOrder={(canvas != null ? canvas.sortingOrder.ToString(CultureInfo.InvariantCulture) : "-")}");

        if (scaler == null)
        {
            report.AppendLine("  CanvasScaler: <none> - the HUD is then in raw screen pixels and the " +
                              "margins below will not hold across resolutions.");
            return;
        }

        report.AppendLine($"  CanvasScaler: uiScaleMode={scaler.uiScaleMode}, " +
                          $"referenceResolution={V2(scaler.referenceResolution)}, " +
                          $"screenMatchMode={scaler.screenMatchMode}, " +
                          $"matchWidthOrHeight={Num(scaler.matchWidthOrHeight)}, " +
                          $"referencePixelsPerUnit={Num(scaler.referencePixelsPerUnit)}, " +
                          $"live scaleFactor={Num(canvas != null ? canvas.scaleFactor : 0f)}");
        report.AppendLine($"  => UNITS: every px below is a {Num(scaler.referenceResolution.x)}x" +
                          $"{Num(scaler.referenceResolution.y)} REFERENCE pixel " +
                          $"(match={Num(scaler.matchWidthOrHeight)}: " +
                          $"{(scaler.matchWidthOrHeight >= 0.999f ? "height only" : scaler.matchWidthOrHeight <= 0.001f ? "width only" : "blended")}).");
    }

    private static void AppendCompassReport(StringBuilder report)
    {
        List<Compass> found = FindAllInScene<Compass>();
        if (found.Count == 0)
        {
            report.AppendLine("COMPASS: ABSENT - no Compass component anywhere in the open scene.");
            return;
        }

        Compass compass = found[0];
        RectTransform rect = compass.transform as RectTransform;

        report.AppendLine($"COMPASS '{HierarchyPath(compass.transform)}': PRESENT " +
                          $"(Compass components in scene: {found.Count}), " +
                          $"activeSelf={compass.gameObject.activeSelf}, " +
                          $"activeInHierarchy={compass.gameObject.activeInHierarchy}, " +
                          $"componentEnabled={compass.enabled}");
        report.AppendLine($"  parent: '{HierarchyPath(compass.transform.parent)}'");
        report.AppendLine("  " + RectLine("RectTransform", rect));
        report.AppendLine("  " + RectLine("parent RectTransform",
                                          compass.transform.parent as RectTransform));

        report.AppendLine($"  Compass: viewDirection={Obj(compass.viewDirection)}" +
                          $" ('{HierarchyPath(compass.viewDirection)}'), " +
                          $"compassElement={Obj(compass.compassElement)}, " +
                          $"compassSize={Num(compass.compassSize)}");

        if (compass.compassElement != null)
        {
            report.AppendLine("  " + RectLine("compassElement", compass.compassElement));
        }

        // Where the strip actually lands, expressed in the canvas's own coordinates. This is
        // the line that answers "is it on screen?" without a screenshot.
        report.AppendLine("  " + CanvasRectLine(rect));
    }

    /// <summary>
    /// The "Top Right" layout region, reported whether or not the compass ends up inside it.
    /// Its two awkward properties - see MoveCompassTopRight - are exactly the numbers on this
    /// line, so the integrator can check them against the live editor rather than trusting
    /// what was read off disk when this tool was written.
    /// </summary>
    private static void AppendTopRightReport(StringBuilder report)
    {
        GameObject region = FindByPath(TopRightRegionPath);
        if (region == null)
        {
            report.AppendLine($"LAYOUT REGION {TopRightRegionPath}: ABSENT");
            return;
        }

        RectTransform rect = region.transform as RectTransform;
        report.AppendLine($"LAYOUT REGION {TopRightRegionPath}: PRESENT activeSelf={region.activeSelf}, " +
                          $"children={region.transform.childCount}");
        report.AppendLine("  " + RectLine("RectTransform", rect));

        for (int i = 0; i < region.transform.childCount; i++)
        {
            Transform child = region.transform.GetChild(i);
            report.AppendLine($"  child[{i}] '{child.name}': activeSelf={child.gameObject.activeSelf}, " +
                              $"{RectLine("rect", child as RectTransform)}");
        }
    }

    private static void AppendRadarReport(StringBuilder report)
    {
        GameObject host = FindByPath(RadarCanvasPath);
        report.AppendLine(host == null
            ? $"RADAR CANVAS {RadarCanvasPath}: ABSENT"
            : $"RADAR CANVAS {RadarCanvasPath}: PRESENT activeSelf={host.activeSelf}, " +
              $"lossyScale={V3(host.transform.lossyScale)}");

        List<EnemyRadar> enemies = FindAllInScene<EnemyRadar>();
        if (enemies.Count == 0)
        {
            report.AppendLine("  EnemyRadar: ABSENT");
        }
        else
        {
            foreach (EnemyRadar radar in enemies)
            {
                report.AppendLine($"  EnemyRadar on '{HierarchyPath(radar.transform)}': " +
                                  $"enabled={radar.enabled}, " +
                                  $"player={Obj(radar.player)}, " +
                                  $"radarCanvas={Obj(radar.radarCanvas)} ('{HierarchyPath(radar.radarCanvas)}'), " +
                                  $"compassBar={Obj(radar.compassBar)} ('{HierarchyPath(radar.compassBar)}'), " +
                                  $"maxDistance={Num(radar.maxDistance)}, " +
                                  $"compassBarWidth={Num(radar.compassBarWidth)}");
                report.AppendLine($"      compassBar RESOLVING: {(radar.compassBar != null ? "YES" : "NO - indicators would have no parent")}");
            }
        }

        List<ObjectiveRadar> objectives = FindAllInScene<ObjectiveRadar>();
        if (objectives.Count == 0)
        {
            report.AppendLine("  ObjectiveRadar: ABSENT");
            return;
        }

        foreach (ObjectiveRadar radar in objectives)
        {
            report.AppendLine($"  ObjectiveRadar on '{HierarchyPath(radar.transform)}': " +
                              $"enabled={radar.enabled}, " +
                              $"player={Obj(radar.player)}, " +
                              $"radarCanvas={Obj(radar.radarCanvas)} ('{HierarchyPath(radar.radarCanvas)}'), " +
                              $"compassBar={Obj(radar.compassBar)} ('{HierarchyPath(radar.compassBar)}'), " +
                              $"maxDistance={Num(radar.maxDistance)}, " +
                              $"compassBarWidth={Num(radar.compassBarWidth)}");
            report.AppendLine($"      compassBar RESOLVING: {(radar.compassBar != null ? "YES" : "NO - indicators would have no parent")}");
            report.AppendLine($"      radarCanvas RESOLVING: {(radar.radarCanvas != null ? "YES" : "NO - ObjectiveRadar.Update() returns early on a null radarCanvas, so markers would stop")}");
        }
    }

    /// <summary>
    /// CompassMask, the object that looks like a second compass and is not. Reported, never
    /// deleted: it draws nothing, but the radars point their radarCanvas field at it, and
    /// ObjectiveRadar.Update() early-returns when that is null - so removing it would stop
    /// the objective markers silently.
    /// </summary>
    private static void AppendCompassMaskReport(StringBuilder report)
    {
        GameObject host = FindByPath(RadarCanvasPath + "/" + CompassMaskName);
        if (host == null)
        {
            report.AppendLine($"COMPASS MASK {RadarCanvasPath}/{CompassMaskName}: ABSENT");
            return;
        }

        report.AppendLine($"COMPASS MASK {RadarCanvasPath}/{CompassMaskName}: PRESENT " +
                          $"activeSelf={host.activeSelf}, activeInHierarchy={host.activeInHierarchy}, " +
                          $"lossyScale={V3(host.transform.lossyScale)}");
        report.AppendLine("  " + RectLine("RectTransform", host.transform as RectTransform));

        // Renderer, the 3D kind, is not how uGUI draws - it draws through Graphic. Both are
        // counted so a "0 renderers" line cannot be mistaken for "nothing here draws".
        Renderer[] renderers = host.GetComponentsInChildren<Renderer>(true);
        report.AppendLine($"  Renderer components (MeshRenderer and friends): {renderers.Length} " +
                          "(uGUI does not use these - the Graphic count below is the one that matters)");

        Graphic[] graphics = host.GetComponentsInChildren<Graphic>(true);
        report.AppendLine($"  Graphic components: {graphics.Length}");

        bool anythingVisible = false;
        bool zeroScale = host.transform.lossyScale.sqrMagnitude < 1e-8f;

        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            Image image = graphic as Image;
            bool live = graphic.enabled && graphic.gameObject.activeInHierarchy
                        && graphic.color.a > InvisibleAlpha && !zeroScale;
            anythingVisible |= live;

            report.AppendLine($"    [{i}] {graphic.GetType().Name} on '{HierarchyPath(graphic.transform)}': " +
                              $"enabled={graphic.enabled}, activeInHierarchy={graphic.gameObject.activeInHierarchy}, " +
                              $"color={Col(graphic.color)}, " +
                              $"sprite={(image != null ? Obj(image.sprite) : "n/a")}, " +
                              $"raycastTarget={graphic.raycastTarget}, DRAWS={live}");
        }

        Mask mask = host.GetComponent<Mask>();
        report.AppendLine(mask == null
            ? "  Mask: <none>"
            : $"  Mask: enabled={mask.enabled}, showMaskGraphic={mask.showMaskGraphic}, " +
              $"children to mask={host.transform.childCount}");

        report.AppendLine($"  VERDICT: active renderers that draw anything visible = " +
                          $"{(anythingVisible ? "YES" : "NO")}" +
                          $" (DRAWS above = enabled && activeInHierarchy && color.a > {Num(InvisibleAlpha)} " +
                          "&& non-zero scale)" +
                          (zeroScale ? "; its canvas root is at lossyScale 0, so nothing under it can render" : ""));
        report.AppendLine("  (⚠ the .unity file records this canvas at localScale 0, which means nothing: " +
                          "a ScreenSpaceOverlay Canvas has its RectTransform DRIVEN by Unity, so the " +
                          "serialized transform is stale and only the live lossyScale above is real.)");
        report.AppendLine("  ⚠ DO NOT DELETE. It is EnemyRadar.radarCanvas and ObjectiveRadar.radarCanvas; " +
                          "ObjectiveRadar.Update() returns early when radarCanvas is null, so deleting " +
                          "this would silently stop the objective markers on the compass.");
    }

    private static void AppendHotbarReport(StringBuilder report)
    {
        GameObject hotbar = FindHotbar(out string how);
        if (hotbar == null)
        {
            report.AppendLine($"HOTBAR {HotbarPath}: ABSENT ({how})");
            return;
        }

        ItemInventoryUI inventoryUI = hotbar.GetComponent<ItemInventoryUI>();
        HotbarSelectionUI selectionUI = hotbar.GetComponent<HotbarSelectionUI>();

        report.AppendLine($"HOTBAR '{HierarchyPath(hotbar.transform)}': PRESENT ({how}), " +
                          $"activeSelf={hotbar.activeSelf}, children={hotbar.transform.childCount}");
        report.AppendLine($"  ItemInventoryUI: {(inventoryUI == null ? "ABSENT" : "PRESENT")}" +
                          (inventoryUI != null
                              ? $", ItemSlots.Count={SlotCount(inventoryUI)}"
                              : " - HotbarSelectionUI reads its ItemSlots, so it needs this"));
        report.AppendLine($"  HotbarSelectionUI: {(selectionUI == null ? "ABSENT" : "PRESENT")}" +
                          (selectionUI != null ? $", enabled={selectionUI.enabled}" : ""));

        for (int i = 0; i < HotbarButtons; i++)
        {
            string buttonName = ButtonName(i);
            Transform button = hotbar.transform.Find(buttonName);

            if (button == null)
            {
                report.AppendLine($"  {buttonName}: ABSENT");
                continue;
            }

            Transform item = button.Find(ItemChildName);
            Transform selected = item != null ? item.Find(SelectedChildName) : null;
            Transform highlighted = item != null ? item.Find(HighlightedChildName) : null;

            string suffix = i >= InventorySlots
                ? "   <- beyond Inventory's five slots; reported only, never touched by this tool"
                : string.Empty;

            report.AppendLine($"  {buttonName}: activeSelf={button.gameObject.activeSelf}, " +
                              $"{ItemChildName}={(item == null ? "ABSENT" : "present")}, " +
                              $"{ItemChildName}/{SelectedChildName}=" +
                              $"{(selected == null ? "ABSENT" : $"present activeSelf={selected.gameObject.activeSelf}")}, " +
                              $"{ItemChildName}/{HighlightedChildName}=" +
                              $"{(highlighted == null ? "ABSENT" : $"present activeSelf={highlighted.gameObject.activeSelf}")}" +
                              suffix);
        }
    }

    // ==========================================================================
    //  2. Move Compass Top Right
    // ==========================================================================

    /// <summary>
    /// Puts the compass strip in the canvas's top-right corner, Margin reference pixels in
    /// from both edges, without touching its size.
    /// </summary>
    /// <remarks>
    /// WHY ScreenSpace AND NOT THE "Top Right" LAYOUT REGION, which is the obvious home for
    /// it and was the first thing tried. Two measured properties of that region rule it out:
    ///
    /// 1. It is at localScale 1.1. Reparenting a 1000x100 strip into it renders it at
    ///    1100x110 - a silent 10% enlargement, which is exactly the thing this order forbids
    ///    ("size unchanged"), and would put the strip 10% out of step with the compassSize
    ///    = 1560 and compassBarWidth = 1560 that the heading maths and the radar dots share.
    /// 2. Its anchoredPosition is (-41.47, +81.80) with anchor and pivot at (1, 1), so its
    ///    top edge sits 81.8 reference px ABOVE the canvas's top edge. A child placed at its
    ///    top with a -40 margin lands ~38 px off the top of the screen, and a third of the
    ///    strip is cut. Landing it correctly would mean compensating in code for a sibling
    ///    region's authored offset - a number that silently drifts the moment the owner nudges
    ///    the region.
    ///
    /// ScreenSpace has neither problem: it is a full-canvas stretch rect (anchors 0,0 - 1,1,
    /// offsets 0) at localScale 1, so its (1, 1) anchor IS the canvas's top-right corner and
    /// Margin means exactly Margin. This is the fallback the work order itself names
    /// ("if it does, reparent the compass to ScreenSpace and say so").
    ///
    /// The old parent "Top " is 600x600 and the strip is 1000 wide, so it already overflowed
    /// its region by 200 px a side. That never clipped anything and still does not: neither
    /// "Top ", "Top Right", ScreenSpace nor HUD carries a Mask or a RectMask2D - the only
    /// masking in the compass is Content/Mask, INSIDE the strip. Report HUD Setup prints all
    /// four rects so this stays checkable.
    /// </remarks>
    [MenuItem("Tools/HUD/Move Compass Top Right", priority = 1201)]
    private static void MoveCompassTopRight()
    {
        List<Compass> found = FindAllInScene<Compass>();
        if (found.Count == 0)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to move the compass: no Compass component in the " +
                             "open scene. Is the level loaded? Nothing was changed.");
            return;
        }

        if (found.Count > 1)
        {
            Debug.LogWarning($"{LogPrefix} {found.Count} Compass components in the scene; moving the " +
                             $"first, '{HierarchyPath(found[0].transform)}'. Check the others by hand.");
        }

        Compass compass = found[0];
        RectTransform rect = compass.transform as RectTransform;
        if (rect == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to move the compass: '{HierarchyPath(compass.transform)}' " +
                             "has a plain Transform, not a RectTransform, so it is not on a Canvas. " +
                             "Nothing was changed.");
            return;
        }

        GameObject parentObject = FindByPath(CompassTargetParentPath);
        RectTransform parent = parentObject != null ? parentObject.transform as RectTransform : null;
        if (parent == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to move the compass: '{CompassTargetParentPath}' is not " +
                             "in the open scene (or is not a RectTransform). Nothing was changed.");
            return;
        }

        // SetParent onto itself throws, and a compass that IS the ScreenSpace region would
        // mean the hierarchy has been rebuilt around this tool's assumptions.
        if (rect == parent)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED: the compass IS '{CompassTargetParentPath}'. " +
                             "Nothing was changed.");
            return;
        }

        // ---- before ----
        string beforeParent = HierarchyPath(rect.parent);
        Vector2 beforeAnchorMin = rect.anchorMin;
        Vector2 beforeAnchorMax = rect.anchorMax;
        Vector2 beforePivot = rect.pivot;
        Vector2 beforePosition = rect.anchoredPosition;
        Vector2 beforeSize = rect.sizeDelta;
        Vector3 beforeScale = rect.localScale;
        string beforeCanvasRect = CanvasRectLine(rect);

        bool alreadyThere = rect.parent == (Transform)parent
                            && Near(beforeAnchorMin, TopRight)
                            && Near(beforeAnchorMax, TopRight)
                            && Near(beforePivot, TopRight)
                            && Near(beforePosition, TargetAnchoredPosition);

        if (alreadyThere)
        {
            Debug.Log($"{LogPrefix} Move Compass Top Right: NOTHING TO DO.\n" +
                      $"'{HierarchyPath(rect)}' is already parented to '{CompassTargetParentPath}' at " +
                      $"anchors {V2(TopRight)}, pivot {V2(TopRight)}, anchoredPosition " +
                      $"{V2(TargetAnchoredPosition)} (margin {Num(Margin)} reference px).\n" +
                      $"  {RectLine("RectTransform", rect)}\n" +
                      $"  {beforeCanvasRect}\n" +
                      "The scene was NOT marked dirty and no Undo step was created.");
            return;
        }

        // ---- apply ----
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(MoveUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        bool reparented = false;
        if (rect.parent != (Transform)parent)
        {
            // worldPositionStays: FALSE. True would bake the difference between the two
            // parents' world scales into the compass's own localScale - and both parents are
            // at scale 1 today, so it would look like it worked and would quietly break the
            // day somebody scales a layout region. False keeps the local transform, which is
            // what "same size, different corner" means.
            Undo.SetTransformParent(rect, parent, false, MoveUndoLabel);
            reparented = true;
        }

        Undo.RecordObject(rect, MoveUndoLabel);

        // Order matters only in that anchoredPosition is last: with anchorMin == anchorMax
        // the sizeDelta IS the size, so moving the anchors and the pivot cannot resize the
        // strip, and the position is then measured from the corner we actually want.
        rect.anchorMin = TopRight;
        rect.anchorMax = TopRight;
        rect.pivot = TopRight;
        rect.anchoredPosition = TargetAnchoredPosition;

        EditorUtility.SetDirty(rect);
        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        // ---- log ----
        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Move Compass Top Right finished.");
        log.AppendLine($"COMPASS: '{HierarchyPath(rect)}'");
        log.AppendLine($"ROUTE: reparented to '{CompassTargetParentPath}' " +
                       $"({(reparented ? "moved" : "was already this parent")}), NOT to " +
                       $"'{TopRightRegionPath}'. Reason: {DescribeTopRightRegion()} " +
                       "ScreenSpace is a full-canvas stretch rect at localScale 1, so its (1, 1) anchor " +
                       "IS the canvas corner and the margin means exactly what it says.");

        log.AppendLine("BEFORE:");
        log.AppendLine($"  parent           = '{beforeParent}'");
        log.AppendLine($"  anchorMin/Max    = {V2(beforeAnchorMin)} / {V2(beforeAnchorMax)}");
        log.AppendLine($"  pivot            = {V2(beforePivot)}");
        log.AppendLine($"  anchoredPosition = {V2(beforePosition)}");
        log.AppendLine($"  sizeDelta        = {V2(beforeSize)}");
        log.AppendLine($"  localScale       = {V3(beforeScale)}");
        log.AppendLine($"  {beforeCanvasRect}");

        log.AppendLine("AFTER:");
        log.AppendLine($"  parent           = '{HierarchyPath(rect.parent)}'");
        log.AppendLine($"  anchorMin/Max    = {V2(rect.anchorMin)} / {V2(rect.anchorMax)}");
        log.AppendLine($"  pivot            = {V2(rect.pivot)}");
        log.AppendLine($"  anchoredPosition = {V2(rect.anchoredPosition)}  " +
                       $"(= -margin, -margin with margin = {Num(Margin)} reference px)");
        log.AppendLine($"  sizeDelta        = {V2(rect.sizeDelta)}");
        log.AppendLine($"  localScale       = {V3(rect.localScale)}");
        log.AppendLine($"  {CanvasRectLine(rect)}");

        log.AppendLine(Near(beforeSize, rect.sizeDelta) && (beforeScale - rect.localScale).sqrMagnitude < 1e-6f
            ? $"SIZE UNCHANGED: {V2(beforeSize)} -> {V2(rect.sizeDelta)} at localScale {V3(rect.localScale)} - " +
              "Compass.compassSize (1560) and the radars' compassBarWidth (1560) stay valid."
            : $"⚠ SIZE CHANGED: {V2(beforeSize)} @ {V3(beforeScale)} -> {V2(rect.sizeDelta)} @ " +
              $"{V3(rect.localScale)}. That was NOT intended - Compass.compassSize and both " +
              "compassBarWidth fields were tuned against the old size and now need retuning.");

        log.AppendLine("The radar indicators follow: EnemyRadar.compassBar and ObjectiveRadar.compassBar " +
                       "are direct object references to a child of the strip, not paths, so moving the " +
                       "strip moves them and re-wires nothing.");
        log.AppendLine($"Scene edit, Undo-recorded as one step, '{MoveUndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or it is lost.");

        Debug.Log(log.ToString().TrimEnd());
    }

    /// <summary>
    /// The two measured facts that rule the "Top Right" layout region out as the compass's
    /// parent, read live rather than quoted from the survey - so if the owner has since
    /// unscaled or repositioned the region, this log says so instead of repeating a stale
    /// justification.
    /// </summary>
    private static string DescribeTopRightRegion()
    {
        GameObject region = FindByPath(TopRightRegionPath);
        RectTransform rect = region != null ? region.transform as RectTransform : null;
        if (rect == null)
        {
            return $"'{TopRightRegionPath}' is not in the open scene at all.";
        }

        Vector3 scale = rect.localScale;
        bool scaled = Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f;

        // Anchored at its parent's top edge with pivot y = 1, the region's top edge is
        // exactly anchoredPosition.y above that edge; positive means it overhangs off-screen.
        bool anchoredToTop = Mathf.Approximately(rect.anchorMin.y, 1f)
                             && Mathf.Approximately(rect.anchorMax.y, 1f)
                             && Mathf.Approximately(rect.pivot.y, 1f);
        float overhang = anchoredToTop ? rect.anchoredPosition.y : 0f;

        string scaleClause = scaled
            ? $"it is at localScale {V3(scale)}, which would render the {Num(rect.rect.width)}-wide " +
              "region's children - the 1000x100 strip included - at that scale, silently resizing it"
            : $"its localScale is {V3(scale)} (no longer a problem)";

        string overhangClause = overhang > 0.5f
            ? $"and its top edge sits {Num(overhang)} reference px ABOVE the canvas top, so a child " +
              $"placed at its top with a {Num(Margin)} px margin would be " +
              $"{Num(overhang - Margin)} px off-screen"
            : "and its top edge no longer overhangs the canvas top";

        return $"{scaleClause}, {overhangClause}.";
    }

    // ==========================================================================
    //  3. Add Hotbar Selection
    // ==========================================================================

    /// <summary>
    /// Adds HotbarSelectionUI beside ItemInventoryUI on the hotbar, and verifies the Synty
    /// prefab structure that both of them derive the five Selected objects from.
    ///
    /// It VERIFIES rather than repairs the Selected children's active state: they belong to
    /// HotbarSelectionUI from the moment it exists, and it recomputes them itself, so
    /// switching one off here would be a change with no effect that still dirtied the scene.
    /// A stuck-on one is reported with a ⚠ instead.
    /// </summary>
    [MenuItem("Tools/HUD/Add Hotbar Selection", priority = 1202)]
    private static void AddHotbarSelection()
    {
        GameObject hotbar = FindHotbar(out string how);
        if (hotbar == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED to add HotbarSelectionUI: '{HotbarPath}' is not in " +
                             $"the open scene ({how}). Nothing was changed.");
            return;
        }

        List<string> added = new List<string>();
        List<string> skipped = new List<string>();
        List<string> verified = new List<string>();
        List<string> notes = new List<string>();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(SelectionUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- the component ----
        HotbarSelectionUI existing = hotbar.GetComponent<HotbarSelectionUI>();
        if (existing == null)
        {
            HotbarSelectionUI selection = Undo.AddComponent<HotbarSelectionUI>(hotbar);
            added.Add($"HotbarSelectionUI on '{HierarchyPath(hotbar.transform)}' " +
                      $"(enabled={selection.enabled}, no serialized wiring - it derives the five " +
                      "Selected objects from ItemInventoryUI.ItemSlots)");
        }
        else
        {
            skipped.Add($"HotbarSelectionUI on '{HierarchyPath(hotbar.transform)}' " +
                        $"(already present, enabled={existing.enabled})");
        }

        // ---- the thing it depends on ----
        ItemInventoryUI inventoryUI = hotbar.GetComponent<ItemInventoryUI>();
        if (inventoryUI == null)
        {
            notes.Add("⚠ No ItemInventoryUI on the hotbar. HotbarSelectionUI reads its ItemSlots to " +
                      "find the five slot roots, so with it missing the border will never light. " +
                      "Nothing here can fix that - the hotbar is authored, not built by this tool.");
        }
        else
        {
            verified.Add($"ItemInventoryUI present, ItemSlots.Count = {SlotCount(inventoryUI)} " +
                         $"(Inventory fills {InventorySlots})");
        }

        // ---- the five slots the script will own ----
        for (int i = 0; i < InventorySlots; i++)
        {
            string buttonName = ButtonName(i);
            Transform button = hotbar.transform.Find(buttonName);

            if (button == null)
            {
                notes.Add($"⚠ {buttonName}: ABSENT. That slot can never show the border.");
                continue;
            }

            Transform item = button.Find(ItemChildName);
            if (item == null)
            {
                notes.Add($"⚠ {buttonName}/{ItemChildName}: ABSENT - the Synty slot structure this " +
                          "and HotbarSelectionUI both assume is not there.");
                continue;
            }

            Transform selected = item.Find(SelectedChildName);
            if (selected == null)
            {
                notes.Add($"⚠ {buttonName}/{ItemChildName}/{SelectedChildName}: ABSENT. " +
                          "HotbarSelectionUI logs once and skips this slot; it will never light.");
                continue;
            }

            if (selected.gameObject.activeSelf)
            {
                notes.Add($"⚠ {buttonName}/{ItemChildName}/{SelectedChildName} is ACTIVE. Left as it is " +
                          "on purpose - HotbarSelectionUI owns that flag and switches it off on its " +
                          "first recompute. If it is still lit in play, the script is not running.");
            }
            else
            {
                verified.Add($"{buttonName}/{ItemChildName}/{SelectedChildName}: present, inactive - correct");
            }
        }

        // ---- the sixth button, reported only ----
        Transform spare = hotbar.transform.Find(ButtonName(InventorySlots));
        verified.Add(spare == null
            ? $"{ButtonName(InventorySlots)}: absent"
            : $"{ButtonName(InventorySlots)}: activeSelf={spare.gameObject.activeSelf} - " +
              "beyond Inventory's five slots and outside the visible row. NOT TOUCHED.");

        Undo.CollapseUndoOperations(undoGroup);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Add Hotbar Selection finished.");
        log.AppendLine($"HOTBAR: '{HierarchyPath(hotbar.transform)}' ({how})");

        log.AppendLine($"ADDED ({added.Count}):");
        AppendList(log, added, "  <none - the component was already there>");

        log.AppendLine($"SKIPPED, ALREADY PRESENT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing was already present - this was the first run>");

        log.AppendLine($"VERIFIED ({verified.Count}):");
        AppendList(log, verified, "  <nothing verified - see the notes>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        if (added.Count == 0)
        {
            log.AppendLine("NOTHING TO DO: the scene was NOT marked dirty and no Undo step was created " +
                           "(the verification above changes nothing).");
        }
        else
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine($"Scene edit, Undo-recorded as one step, '{SelectionUndoLabel}'. " +
                           $"SAVE THE SCENE ({scene.name}) or it is lost.");
        }

        Debug.Log(log.ToString().TrimEnd());
    }

    // ==========================================================================
    //  Shared helpers
    // ==========================================================================

    private static string ButtonName(int index) =>
        HotbarButtonPrefix + index.ToString("00", CultureInfo.InvariantCulture);

    private static string SlotCount(ItemInventoryUI inventoryUI) =>
        inventoryUI.ItemSlots == null
            ? "<null array>"
            : inventoryUI.ItemSlots.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The hotbar, by its authored path first and by its ItemInventoryUI second. Two routes
    /// because the path is a literal string this file cannot verify at compile time, while
    /// the component is the thing HotbarSelectionUI actually has to sit beside.
    /// </summary>
    private static GameObject FindHotbar(out string how)
    {
        GameObject byPath = FindByPath(HotbarPath);
        if (byPath != null)
        {
            how = $"found at {HotbarPath}";
            return byPath;
        }

        List<ItemInventoryUI> found = FindAllInScene<ItemInventoryUI>();
        if (found.Count > 0)
        {
            how = $"'{HotbarPath}' did not resolve; fell back to the ItemInventoryUI at " +
                  $"'{HierarchyPath(found[0].transform)}' ({found.Count} in the scene)";
            return found[0].gameObject;
        }

        how = $"neither '{HotbarPath}' nor any ItemInventoryUI is in the open scene";
        return null;
    }

    /// <summary>
    /// A GameObject by hierarchy path in the active scene. Transform.Find rather than
    /// GameObject.Find, because the latter cannot see an inactive object and several of the
    /// things this tool looks for are legitimately switched off.
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
    /// Every component of a type in the ACTIVE SCENE, inactive objects included.
    ///
    /// Walked by hand rather than through FindObjectsByType for two reasons: that call also
    /// returns components from an open prefab stage and from any additively loaded scene, and
    /// a tool that quietly edited a prefab the owner happened to have open would be very hard
    /// to explain. This also keeps the "which one did it pick" answer to a single scene.
    /// </summary>
    private static List<T> FindAllInScene<T>() where T : Component
    {
        List<T> results = new List<T>();

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return results;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            results.AddRange(root.GetComponentsInChildren<T>(true));
        }

        return results;
    }

    /// <summary>
    /// One RectTransform on one line, in the order the inspector shows them, so two Report
    /// runs diff a line at a time.
    /// </summary>
    private static string RectLine(string label, RectTransform rect)
    {
        if (rect == null) return $"{label}: <none>";

        return $"{label}: anchorMin={V2(rect.anchorMin)}, anchorMax={V2(rect.anchorMax)}, " +
               $"pivot={V2(rect.pivot)}, anchoredPosition={V2(rect.anchoredPosition)}, " +
               $"sizeDelta={V2(rect.sizeDelta)}, rect={Num(rect.rect.width)}x{Num(rect.rect.height)}, " +
               $"localScale={V3(rect.localScale)}, lossyScale={V3(rect.lossyScale)}";
    }

    /// <summary>
    /// Where a rect sits inside Canvas - Interface, in the canvas's own coordinates, with
    /// (0, 0) at the canvas centre and +y up. This is the line that says whether the strip is
    /// on screen: at the 3840x2160 reference the visible box is x -1920..1920, y -1080..1080.
    ///
    /// ⚠ In EDIT mode a ScreenSpaceOverlay canvas is sized to the Game view, not to the
    /// reference resolution, so these numbers are the reference numbers only when the Game
    /// view happens to match. The ratio to the canvas's own half-size is the part to read.
    /// </summary>
    private static string CanvasRectLine(RectTransform rect)
    {
        if (rect == null) return "canvas-local rect: <no RectTransform>";

        // includeInactive: true - Report HUD Setup has to work on a HUD the owner has switched
        // off to see the level, and the default overload silently returns null for one.
        Canvas canvas = rect.GetComponentInParent<Canvas>(true);
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null) return "canvas-local rect: <not under a Canvas>";

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        // GetWorldCorners returns bottom-left, top-left, top-right, bottom-right.
        Vector3 bottomLeft = canvasRect.InverseTransformPoint(corners[0]);
        Vector3 topRight = canvasRect.InverseTransformPoint(corners[2]);

        Rect canvasBounds = canvasRect.rect;
        bool inside = bottomLeft.x >= canvasBounds.xMin - 0.5f && topRight.x <= canvasBounds.xMax + 0.5f
                      && bottomLeft.y >= canvasBounds.yMin - 0.5f && topRight.y <= canvasBounds.yMax + 0.5f;

        return $"canvas-local rect (edit-mode Game view units, canvas is " +
               $"{Num(canvasBounds.width)}x{Num(canvasBounds.height)}): " +
               $"x {Num(bottomLeft.x)}..{Num(topRight.x)}, y {Num(bottomLeft.y)}..{Num(topRight.y)}, " +
               $"fully on screen={inside}";
    }

    private static bool Near(Vector2 a, Vector2 b) =>
        Mathf.Abs(a.x - b.x) <= PositionEpsilon && Mathf.Abs(a.y - b.y) <= PositionEpsilon;

    // ==========================================================================
    //  The strip back to top-centre, where it was
    // ==========================================================================

    /// <summary>
    /// Undoes Move Compass Top Right: the strip goes back under "Top ", anchored and pivoted
    /// top-centre at its original offset, size untouched. The owner's clarification of
    /// 2026-09-06 - the long compass was always meant to stay where it was, and the round one
    /// was to be ADDED top-right. Idempotent.
    /// </summary>
    [MenuItem("Tools/HUD/Move Compass Top Centre", priority = 1204)]
    private static void MoveCompassTopCentre()
    {
        List<Compass> found = FindAllInScene<Compass>();
        if (found.Count == 0)
        {
            Debug.LogWarning($"{LogPrefix} No Compass component in the open scene. Nothing was changed.");
            return;
        }

        RectTransform rect = found[0].transform as RectTransform;
        GameObject region = FindByPath(TopRegionPath);
        if (rect == null || region == null)
        {
            Debug.LogWarning($"{LogPrefix} No RectTransform on the compass or no '{TopRegionPath}' region. Nothing was changed.");
            return;
        }

        bool already = rect.parent == region.transform && Near(rect.anchorMin, TopCentre) && Near(rect.anchorMax, TopCentre)
                       && Near(rect.pivot, TopCentre) && Near(rect.anchoredPosition, OriginalCompassPosition);
        if (already)
        {
            Debug.Log($"{LogPrefix} The compass is already top-centre under '{TopRegionPath}'. Nothing to do.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(CentreUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        string wasParent = HierarchyPath(rect.parent);
        Vector2 wasPosition = rect.anchoredPosition;
        Vector2 wasSize = rect.sizeDelta;

        if (rect.parent != region.transform) Undo.SetTransformParent(rect, region.transform, false, CentreUndoLabel);
        Undo.RecordObject(rect, CentreUndoLabel);
        rect.anchorMin = TopCentre;
        rect.anchorMax = TopCentre;
        rect.pivot = TopCentre;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = OriginalCompassPosition;

        Undo.CollapseUndoOperations(undoGroup);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"{LogPrefix} Move Compass Top Centre: {CompassName} moved from {wasParent} " +
                  $"(anchoredPosition {V2(wasPosition)}) back to {HierarchyPath(rect.parent)}: anchors and pivot " +
                  $"{V2(TopCentre)}, anchoredPosition {V2(OriginalCompassPosition)}, size {V2(rect.sizeDelta)} " +
                  $"(was {V2(wasSize)} - unchanged). Undo '{CentreUndoLabel}'. SAVE THE SCENE ({scene.name}).");
    }

    // ==========================================================================
    //  The minimap, top-right
    // ==========================================================================

    /// <summary>
    /// Builds the minimap: the pack's lights-and-wires dial under ScreenSpace in the top-right
    /// corner, its demo map image and sample icons removed, a RawImage in their place showing
    /// a top-down camera that follows the player, and a <see cref="MinimapView"/> driving the
    /// camera, the player marker and the enemy / objective icons. Removes the compass dial
    /// that stood here before. Idempotent - re-running repairs the wiring and leaves an
    /// existing minimap where the owner has put it.
    /// </summary>
    [MenuItem("Tools/HUD/Add Minimap", priority = 1205)]
    private static void AddMinimap()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        GameObject screenSpace = FindByPath(ScreenSpacePath);
        GameObject playerRoot = FindByPath(PlayerPath);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinimapPrefabPath);
        GameObject enemyIcon = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyIconPrefabPath);
        GameObject objectiveIcon = AssetDatabase.LoadAssetAtPath<GameObject>(ObjectiveIconPrefabPath);

        if (screenSpace == null || playerRoot == null || prefab == null)
        {
            Debug.LogError($"{LogPrefix} Missing {(screenSpace == null ? ScreenSpacePath : playerRoot == null ? PlayerPath : MinimapPrefabPath)}. Nothing was changed.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(MinimapUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // ---- the compass dial this replaces ----
        Transform oldDial = screenSpace.transform.Find(OldDialName);
        if (oldDial != null)
        {
            Undo.DestroyObjectImmediate(oldDial.gameObject);
            fields.Add($"removed {OldDialName} - the owner's 'compass' meant this minimap");
        }

        // ---- the dial ----
        Transform existing = screenSpace.transform.Find(MinimapName);
        GameObject minimap;
        bool created = existing == null;

        if (created)
        {
            // A plain instance, not a prefab instance: children are deleted and added below,
            // and a prefab instance carrying that many overrides is one a re-import fights.
            minimap = Object.Instantiate(prefab, screenSpace.transform);
            minimap.name = MinimapName;
            Undo.RegisterCreatedObjectUndo(minimap, MinimapUndoLabel);

            RectTransform rect = minimap.GetComponent<RectTransform>();
            rect.anchorMin = TopRight;
            rect.anchorMax = TopRight;
            rect.pivot = TopRight;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(-MinimapMargin, -MinimapMargin);
            fields.Add($"created {HierarchyPath(minimap.transform)} from {System.IO.Path.GetFileName(MinimapPrefabPath)}: " +
                       $"anchors/pivot {V2(TopRight)}, anchoredPosition {V2(rect.anchoredPosition)}, {V2(rect.sizeDelta)} reference px");

            Transform demoMap = minimap.transform.Find(MinimapDemoMapPath);
            if (demoMap != null) { Object.DestroyImmediate(demoMap.gameObject); fields.Add("removed the demo map image"); }
            else notes.Add($"no '{MinimapDemoMapPath}' - the prefab has changed; check what is inside the mask");

            Transform elements = minimap.transform.Find(MinimapElementsPath);
            if (elements != null)
            {
                int removed = 0;
                for (int i = elements.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(elements.GetChild(i).gameObject); removed++; }
                fields.Add($"removed {removed} sample icons from {MinimapElementsPath}");
            }

            foreach (Animator animator in minimap.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);

            // The live picture, stretched over the mask, first child of the map container so
            // it draws under the marker and the icons.
            Transform container = minimap.transform.Find(MinimapMapContainerPath);
            if (container != null)
            {
                GameObject live = new GameObject(MinimapLiveImageName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                live.transform.SetParent(container, false);
                RectTransform liveRect = live.GetComponent<RectTransform>();
                liveRect.anchorMin = Vector2.zero;
                liveRect.anchorMax = Vector2.one;
                liveRect.offsetMin = Vector2.zero;
                liveRect.offsetMax = Vector2.zero;
                live.GetComponent<RawImage>().raycastTarget = false;
                live.transform.SetAsFirstSibling();
                fields.Add($"added {MinimapLiveImageName} (RawImage) stretched over the mask");
            }
            else notes.Add($"no '{MinimapMapContainerPath}' - the prefab has changed; the live image was not added");
        }
        else
        {
            minimap = existing.gameObject;
            skipped.Add($"{HierarchyPath(minimap.transform)} already exists - left where it is, wiring repaired");
        }

        // ---- the camera, under the player ----
        Transform cameraTransform = playerRoot.transform.Find(MinimapCameraName);
        Camera camera;
        if (cameraTransform == null)
        {
            GameObject cameraObject = new GameObject(MinimapCameraName);
            Undo.RegisterCreatedObjectUndo(cameraObject, MinimapUndoLabel);
            cameraObject.transform.SetParent(playerRoot.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            fields.Add($"created {HierarchyPath(cameraObject.transform)}");
        }
        else
        {
            camera = cameraTransform.GetComponent<Camera>();
            if (camera == null) camera = Undo.AddComponent<Camera>(cameraTransform.gameObject);
            skipped.Add($"{MinimapCameraName} already present");
        }

        Undo.RecordObject(camera, MinimapUndoLabel);
        camera.orthographic = true;
        camera.orthographicSize = 35f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 250f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.09f, 0.08f, 1f);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.depth = -10f;
        camera.useOcclusionCulling = true;

        int mask = ~0;
        foreach (string layer in MinimapHiddenLayers)
        {
            int index = LayerMask.NameToLayer(layer);
            if (index >= 0) mask &= ~(1 << index);
        }
        camera.cullingMask = mask;

        // URP: no shadows, no post, no AA, no depth/colour copies - a map, not a view. The
        // getter ADDS the component when it is missing (CODE_STRUCTURE, Editor row), which
        // here is what is wanted.
        UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        Undo.RecordObject(data, MinimapUndoLabel);
        data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
        data.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
        data.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
        fields.Add($"{MinimapCameraName}: orthographic 35 m, far 250, culling everything but {string.Join("/", MinimapHiddenLayers)}, no shadows / post / AA");

        // ---- the driver ----
        MinimapView view = minimap.GetComponent<MinimapView>();
        if (view == null) { view = Undo.AddComponent<MinimapView>(minimap); fields.Add("added MinimapView"); }
        else skipped.Add("MinimapView already present");

        SerializedObject serialized = new SerializedObject(view);
        Wire(serialized, "player", playerRoot.transform, fields, skipped);
        Wire(serialized, "minimapCamera", camera, fields, skipped);
        Wire(serialized, "mask", minimap.transform.Find(MinimapMaskPath), fields, skipped);
        Wire(serialized, "elements", minimap.transform.Find(MinimapElementsPath), fields, skipped);
        Wire(serialized, "playerMarker", minimap.transform.Find(MinimapPlayerMarkerPath), fields, skipped);
        Transform liveImage = minimap.transform.Find(MinimapMapContainerPath + "/" + MinimapLiveImageName);
        Wire(serialized, "mapImage", liveImage != null ? liveImage.GetComponent<RawImage>() : null, fields, skipped);
        Wire(serialized, "enemyIconPrefab", enemyIcon, fields, skipped);
        Wire(serialized, "objectiveIconPrefab", objectiveIcon, fields, skipped);
        serialized.ApplyModifiedProperties();

        if (enemyIcon == null) notes.Add($"⚠ No enemy icon prefab at {EnemyIconPrefabPath}");
        if (objectiveIcon == null) notes.Add($"⚠ No objective icon prefab at {ObjectiveIconPrefabPath}");

        Undo.CollapseUndoOperations(undoGroup);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Add Minimap finished. Scene changes only.");
        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing>");
        log.AppendLine($"ALREADY PRESENT ({skipped.Count}):");
        AppendList(log, skipped, "  <none>");
        if (notes.Count > 0) { log.AppendLine($"NOTES ({notes.Count}):"); AppendList(log, notes, string.Empty); }
        log.AppendLine($"Undo '{MinimapUndoLabel}'. SAVE THE SCENE ({scene.name}).");
        Debug.Log(log.ToString().TrimEnd());

        Selection.activeGameObject = minimap;
    }

    // ==========================================================================
    //  The interaction prompt, beside the minimap instead of under it
    // ==========================================================================

    /// <summary>
    /// Reparents the prompt widget from the "Top Right" region to ScreenSpace (scale 1,
    /// anchors that mean what they say - the region is at 1.1 and overhangs the canvas, see
    /// MoveCompassTopRight) and places it so the VISIBLE part of it - the union of its
    /// graphics, not its 600 px root, which is mostly empty - ends PromptGap px left of the
    /// minimap, centred on the map's height. Measured from the graphics because the widget's
    /// root is far wider than what it draws; right-aligning the root would leave a 300 px
    /// hole between prompt and map. Idempotent within a pixel.
    /// </summary>
    [MenuItem("Tools/HUD/Move Interaction Prompt Beside Minimap", priority = 1206)]
    private static void MoveInteractionPromptBesideMinimap()
    {
        GameObject screenSpace = FindByPath(ScreenSpacePath);
        RectTransform minimap = screenSpace != null ? screenSpace.transform.Find(MinimapName) as RectTransform : null;
        if (screenSpace == null || minimap == null)
        {
            Debug.LogError($"{LogPrefix} Need '{ScreenSpacePath}' and a '{MinimapName}' under it - run Add Minimap first. Nothing was changed.");
            return;
        }

        RectTransform widget = FindWidget(screenSpace.transform);
        if (widget == null)
        {
            Debug.LogError($"{LogPrefix} No '{PromptWidgetName}' under {ScreenSpacePath}. Nothing was changed.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(PromptUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        string wasParent = HierarchyPath(widget.parent);
        Vector2 wasPosition = widget.anchoredPosition;

        if (widget.parent != screenSpace.transform) Undo.SetTransformParent(widget, screenSpace.transform, false, PromptUndoLabel);
        Undo.RecordObject(widget, PromptUndoLabel);
        widget.localScale = Vector3.one;
        widget.anchorMin = TopRight;
        widget.anchorMax = TopRight;
        widget.pivot = TopRight;

        // Where the drawn part is now, and where the minimap is, both in ScreenSpace's own
        // coordinates (its (1,1) anchor is the canvas corner, so these are canvas px).
        RectTransform space = screenSpace.GetComponent<RectTransform>();
        Rect drawn = UnionOfGraphics(widget, space);
        Rect map = LocalRect(minimap, space);

        float targetRight = map.xMin - PromptGap;
        float targetCentreY = map.center.y;
        Vector2 shift = new Vector2(targetRight - drawn.xMax, targetCentreY - drawn.center.y);

        if (shift.magnitude < 0.5f && wasParent == HierarchyPath(screenSpace.transform))
        {
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"{LogPrefix} The interaction prompt is already beside the minimap ({Num(PromptGap)} px gap). Nothing to do.");
            return;
        }

        widget.anchoredPosition += shift;
        Rect after = UnionOfGraphics(widget, space);

        Undo.CollapseUndoOperations(undoGroup);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"{LogPrefix} Move Interaction Prompt Beside Minimap: {PromptWidgetName} from {wasParent} " +
                  $"(anchoredPosition {V2(wasPosition)}, scale 1.1 inherited) to {HierarchyPath(widget.parent)} at " +
                  $"anchors/pivot {V2(TopRight)}, anchoredPosition {V2(widget.anchoredPosition)}, scale 1.\n" +
                  $"  drawn part now: x {Num(after.xMin)}..{Num(after.xMax)}, y {Num(after.yMin)}..{Num(after.yMax)} (canvas px from the top-right corner)\n" +
                  $"  minimap:        x {Num(map.xMin)}..{Num(map.xMax)}, y {Num(map.yMin)}..{Num(map.yMax)}\n" +
                  $"  gap: {Num(map.xMin - after.xMax)} px (want {Num(PromptGap)}); overlap: {(after.xMax <= map.xMin ? "none" : "YES")}.\n" +
                  $"Undo '{PromptUndoLabel}'. SAVE THE SCENE ({scene.name}).");
    }

    /// <summary>The widget wherever it is under ScreenSpace - the region it was authored in, or ScreenSpace itself after a move.</summary>
    private static RectTransform FindWidget(Transform screenSpace)
    {
        foreach (RectTransform rect in screenSpace.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name == PromptWidgetName) return rect;
        }
        return null;
    }

    /// <summary>
    /// The rect that is actually drawn: the union of every Graphic under the root, in the
    /// given space. Inactive objects included - the prompt is hidden until the player walks
    /// up to something, and its rects are laid out all the same.
    /// </summary>
    private static Rect UnionOfGraphics(RectTransform root, RectTransform space)
    {
        bool any = false;
        float xMin = 0f, xMax = 0f, yMin = 0f, yMax = 0f;
        Vector3[] corners = new Vector3[4];

        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            // Only what the widget would DRAW: it carries two skins for its key button and
            // shows one, and the switched-off one reaches 80 px further right. The root itself
            // is switched off in edit mode (InteractionPromptUI hides it until the player
            // walks up to something), so activeInHierarchy is useless here - it is the chain
            // BELOW the root that decides.
            if (!ActiveBelow(graphic.transform, root)) continue;

            graphic.rectTransform.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = space.InverseTransformPoint(corners[i]);
                if (!any) { xMin = xMax = local.x; yMin = yMax = local.y; any = true; }
                xMin = Mathf.Min(xMin, local.x); xMax = Mathf.Max(xMax, local.x);
                yMin = Mathf.Min(yMin, local.y); yMax = Mathf.Max(yMax, local.y);
            }
        }

        return any ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : LocalRect(root, space);
    }

    /// <summary>True when every object from <paramref name="leaf"/> up to (not including) <paramref name="root"/> is switched on.</summary>
    private static bool ActiveBelow(Transform leaf, Transform root)
    {
        for (Transform t = leaf; t != null && t != root; t = t.parent)
        {
            if (!t.gameObject.activeSelf) return false;
        }
        return true;
    }

    private static Rect LocalRect(RectTransform rect, RectTransform space)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector3 bl = space.InverseTransformPoint(corners[0]);
        Vector3 tr = space.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(bl.x, bl.y, tr.x, tr.y);
    }

    private static void Wire(SerializedObject target, string field, Object value, List<string> fields, List<string> skipped)
    {
        SerializedProperty property = target.FindProperty(field);
        if (property == null) { fields.Add($"⚠ MinimapView has no '{field}' field"); return; }
        if (value == null) { fields.Add($"⚠ nothing to wire into MinimapView.{field}"); return; }
        if (property.objectReferenceValue == value) { skipped.Add($"MinimapView.{field} already wired"); return; }
        property.objectReferenceValue = value;
        fields.Add($"MinimapView.{field} = {value.name}");
    }

    // ==========================================================================
    //  The settings menu background, at every aspect
    // ==========================================================================

    /// <summary>
    /// Gives the settings menu's background layers honest geometry: stretch anchors, zero
    /// offsets, scale 1 - the fill and the striped vignette reaching SettingsOverflow past
    /// every canvas edge so the vignette's rounded corners are never on screen, whatever
    /// the Game view's aspect. Replaces the sizeDelta/scale hacks that only worked by luck
    /// at one aspect. Idempotent.
    /// </summary>
    [MenuItem("Tools/HUD/Stretch Settings Background", priority = 1207)]
    private static void StretchSettingsBackground()
    {
        GameObject canvas = FindSceneObjectNamed(SettingsCanvasName);
        if (canvas == null)
        {
            Debug.LogError($"{LogPrefix} No '{SettingsCanvasName}' canvas in the scene. Nothing was changed.");
            return;
        }

        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(SettingsBackgroundUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // The container: exactly the canvas.
        Stretch(canvas.transform, SettingsBackgroundPath, 0f, fields, skipped);
        // The flat fill: well past it - a solid colour has no edge worth seeing.
        Stretch(canvas.transform, SettingsFillPath, 0.5f, fields, skipped);
        // The stripes container (empty in this scene, normalised all the same) and the
        // vignette sprite: past every edge by the overflow, so the rounding is off-screen.
        Stretch(canvas.transform, SettingsStripesPath, SettingsOverflow, fields, skipped);
        Stretch(canvas.transform, SettingsVignettePath, SettingsOverflow, fields, skipped);

        Undo.CollapseUndoOperations(undoGroup);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Stretch Settings Background finished.");
        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing>");
        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <none>");
        log.AppendLine($"Undo '{SettingsBackgroundUndoLabel}'. SAVE THE SCENE ({scene.name}).");
        Debug.Log(log.ToString().TrimEnd());
    }

    /// <summary>Anchors a rect to its parent with <paramref name="overflow"/> of the parent past each edge, zero offsets, scale 1.</summary>
    private static void Stretch(Transform root, string path, float overflow, List<string> fields, List<string> skipped)
    {
        RectTransform rect = root.Find(path) as RectTransform;
        if (rect == null)
        {
            fields.Add($"⚠ no '{path}' under {root.name} - left as is");
            return;
        }

        Vector2 min = new Vector2(-overflow, -overflow);
        Vector2 max = new Vector2(1f + overflow, 1f + overflow);
        bool already = Near(rect.anchorMin, min) && Near(rect.anchorMax, max)
                       && rect.offsetMin.sqrMagnitude < 0.01f && rect.offsetMax.sqrMagnitude < 0.01f
                       && (rect.localScale - Vector3.one).sqrMagnitude < 0.0001f;
        if (already)
        {
            skipped.Add($"{path} already stretched with {Num(overflow * 100f)}% overflow");
            return;
        }

        string was = $"anchors {V2(rect.anchorMin)}/{V2(rect.anchorMax)}, sizeDelta {V2(rect.sizeDelta)}, scale {V3(rect.localScale)}";

        Undo.RecordObject(rect, SettingsBackgroundUndoLabel);
        rect.localScale = Vector3.one;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        fields.Add($"{path}: {was} -> anchors {V2(min)}/{V2(max)}, offsets 0, scale 1");
    }

    /// <summary>A scene object by exact name, active or not - GameObject.Find skips inactive ones and this canvas is off until the game pauses.</summary>
    private static GameObject FindSceneObjectNamed(string name)
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t.gameObject;
            }
        }
        return null;
    }

    // ==========================================================================
    //  Fit the ammo readout - four digits do not fit a box built for two
    // ==========================================================================

    /// <summary>
    /// Makes the bottom-right weapon panel's round counter survive a four-digit magazine.
    ///
    /// Measured 2026-09-06, after the minigun's belt went from 200 rounds to 2000: the label
    /// is a 90 px box at font 64 with wrapping on, and "2000" needs 126.9 px - so it WRAPPED,
    /// and the panel showed "20" with the rest pushed out of sight. Nobody had seen it because
    /// no weapon in this level had ever carried more than three digits.
    ///
    /// The fix is TMP's own auto-sizing rather than a wider box: widening it would push into
    /// the panel art beside it, and every other weapon keeps its 64 pt exactly as it reads
    /// today, because auto-sizing only ever shrinks below the maximum when it has to.
    /// Wrapping goes off as well - a counter that wraps is never what was wanted, whatever
    /// the number.
    ///
    /// Idempotent, and it reports what it measured either way.
    /// </summary>
    [MenuItem("Tools/HUD/Fit Ammo Readout", priority = 1203)]
    private static void FitAmmoReadout()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        CurrentWeaponHUD hud = Object.FindFirstObjectByType<CurrentWeaponHUD>(FindObjectsInactive.Include);
        if (hud == null)
        {
            Debug.LogWarning($"{LogPrefix} No CurrentWeaponHUD in the scene - nothing to fit.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(AmmoFitUndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        SerializedObject serialized = new SerializedObject(hud);

        // Both labels, because ammoTotalLabel ("/ 2000") is the wider string of the two and
        // may be wired later even though it is empty today.
        foreach (string name in AmmoLabelFields)
        {
            SerializedProperty property = serialized.FindProperty(name);
            TMP_Text label = property != null ? property.objectReferenceValue as TMP_Text : null;

            if (label == null)
            {
                notes.Add($"CurrentWeaponHUD.{name} is not wired, so nothing was fitted for it. " +
                          "That is the current state for the total label and costs nothing.");
                continue;
            }

            string path = HierarchyPath(label.transform);
            float width = label.rectTransform.rect.width;
            float needed = label.GetPreferredValues(WidestAmmoString).x;

            if (label.enableAutoSizing && label.textWrappingMode == TextWrappingModes.NoWrap)
            {
                skipped.Add($"{path} already auto-sizes and does not wrap " +
                            $"(box {Num(width)} px, '{WidestAmmoString}' wants {Num(needed)} px)");
                continue;
            }

            Undo.RecordObject(label, AmmoFitUndoLabel);

            float wasMax = label.fontSize;
            bool wasWrapping = label.textWrappingMode != TextWrappingModes.NoWrap;

            // Max stays at whatever the panel was authored with, so a two-digit magazine is
            // pixel-for-pixel what it is today; the minimum is the floor a four-digit one
            // shrinks to, and 32 keeps it comfortably readable at 1080p.
            label.enableAutoSizing = true;
            label.fontSizeMax = wasMax;
            label.fontSizeMin = AmmoLabelMinFontSize;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;

            EditorUtility.SetDirty(label);

            fields.Add($"{path} auto-size on ({Num(AmmoLabelMinFontSize)}..{Num(wasMax)} pt), " +
                       $"wrapping {(wasWrapping ? "on -> off" : "already off")} - box is {Num(width)} px " +
                       $"and '{WidestAmmoString}' wanted {Num(needed)} px at {Num(wasMax)} pt");
        }

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Fit Ammo Readout finished.");

        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - the labels already fit>");

        log.AppendLine($"ALREADY CORRECT ({skipped.Count}):");
        AppendList(log, skipped, "  <none>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"Undo-recorded as one step, '{AmmoFitUndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or the change is lost.");

        Debug.Log(log.ToString().TrimEnd());
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
    private static string HierarchyPath(Component component) =>
        component == null ? "<none>" : HierarchyPath(component.transform);

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

    private static string V3(Vector3 value) => $"({Num(value.x)}, {Num(value.y)}, {Num(value.z)})";

    private static string Col(Color value) =>
        $"({Num(value.r)}, {Num(value.g)}, {Num(value.b)}, {Num(value.a)})";

    private static string Obj(Object value) => value == null ? "<none>" : $"'{value.name}'";
}
