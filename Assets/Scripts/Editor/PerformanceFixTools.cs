using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/*
 * PerformanceFixTools.cs
 *
 * Purpose: The four settings changes of phase 3 of the performance work order
 *          (Documentation/PERFORMANCE_FIX_TASKS.md), plus a read-only report that the
 *          integrator runs before and after each one so the before/after is a plain diff.
 * Menu:    Tools/Performance/Report Current Settings
 *          Tools/Performance/Apply Terrain Instancing And Shadow Mode
 *          Tools/Performance/Apply Shadow Distance 25
 *          Tools/Performance/Apply FXAA On Main Camera
 *          Tools/Performance/Resolve Duplicate Bloom
 * Used by: the integrator only, driven from the Unity CLI between profiler captures. The
 *          owner saves the scene afterwards.
 *
 * Menu items rather than hand edits of the .unity and .asset files, for this project's
 * standing reason: the files on disk are routinely older than what is open in the editor,
 * so a hand edit would be lost the next time the owner saves.
 *
 * ⚠ NO DIALOGS anywhere in this file. These items are executed headless through the CLI
 * bridge, and EditorUtility.DisplayDialog would block the bridge with a modal window that
 * nobody can see, let alone dismiss. A refusal is a Debug.LogWarning and an early return.
 *
 * Every item is idempotent - running it twice changes nothing the second time - and logs the
 * before and after value of everything it touches, so the integrator's two Report runs
 * bracket a record of exactly what moved. An item with nothing to do says so and stops.
 *
 * SCENE edits (terrain, camera) are Undo-recorded and need the owner to SAVE THE SCENE.
 * ASSET edits (the URP asset, the two Volume profiles) go through AssetDatabase.SaveAssets
 * and are written to disk immediately: Undo does not survive them, and git is the way back.
 *
 * Values below were verified against the repo at 461732b; see the "Verified anchors" table
 * in PERFORMANCE_FIX_TASKS.md. Nothing here binds to a serialized field by string - it is
 * all typed API - so a rename cannot break it silently.
 */
public static class PerformanceFixTools
{
    private const string LogPrefix = "[PerformanceFixTools]";

    /// <summary>The grade: Bloom sits here beside Vignette, LiftGammaGain and the rest.</summary>
    private const string GradeProfilePath =
        "Assets/Level for Sound Design/Post Processing Profile.asset";

    /// <summary>The duplicate: a second global Volume at the same priority, whose only real
    /// content is the competing Bloom this file deletes.</summary>
    private const string CameraProfilePath =
        "Assets/Level for Sound Design/FirstPersonCharacter Profile.asset";

    /// <summary>Verified present in Assets/Level for Sound Design.unity: the GameObject
    /// 'FirstPersonCharacter' carries the Camera, is tagged MainCamera, and holds the
    /// UniversalAdditionalCameraData. So Camera.main finds it, and this name is only the
    /// belt-and-braces fallback for a session where the tag has been lost.</summary>
    private const string CameraObjectName = "FirstPersonCharacter";

    /// <summary>Casters scale with the square of the distance: (25/40)² is about 0.39, so the
    /// ~717 idle shadow casters measured at 40 m fall to roughly 280.</summary>
    private const float TargetShadowDistance = 25f;

    /// <summary>The owner's choice for the single surviving Bloom.</summary>
    private const float TargetBloomIntensity = 0.4f;

    // ---- 1. Report ------------------------------------------------------------

    /// <summary>
    /// Reads and prints, changing nothing. Run before and after every other item here: the
    /// output is one block with a fixed line order and invariant-culture numbers, so two runs
    /// diff cleanly and the diff IS the record of what the apply items did.
    /// </summary>
    [MenuItem("Tools/Performance/Report Current Settings", priority = 100)]
    private static void ReportCurrentSettings()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Current Settings (read only - nothing was changed).");

        AppendTerrainReport(report);
        AppendPipelineReport(report);
        AppendCameraReport(report);
        AppendProfileReport(report, "GRADE profile", GradeProfilePath);
        AppendProfileReport(report, "CAMERA profile", CameraProfilePath);

        Debug.Log(report.ToString().TrimEnd());
    }

    private static void AppendTerrainReport(StringBuilder report)
    {
        Terrain[] terrains = FindTerrains();
        report.AppendLine($"TERRAINS ({terrains.Length} found, inactive included):");

        if (terrains.Length == 0)
        {
            report.AppendLine("  <none in the open scene(s)>");
            return;
        }

        foreach (Terrain terrain in terrains)
        {
            report.AppendLine($"  '{HierarchyPath(terrain.transform)}': drawInstanced={terrain.drawInstanced}, " +
                              $"shadowCastingMode={terrain.shadowCastingMode}, " +
                              $"detailObjectDistance={Num(terrain.detailObjectDistance)}, " +
                              $"heightmapPixelError={Num(terrain.heightmapPixelError)}");
        }
    }

    private static void AppendPipelineReport(StringBuilder report)
    {
        UniversalRenderPipelineAsset pipeline = GetPipelineAsset();

        if (pipeline == null)
        {
            report.AppendLine("URP ASSET: <none - GraphicsSettings.defaultRenderPipeline is not a " +
                              "UniversalRenderPipelineAsset>");
            return;
        }

        report.AppendLine($"URP ASSET '{pipeline.name}' ({AssetDatabase.GetAssetPath(pipeline)}): " +
                          $"shadowDistance={Num(pipeline.shadowDistance)}, " +
                          $"mainLightShadowmapResolution={pipeline.mainLightShadowmapResolution}, " +
                          $"shadowCascadeCount={pipeline.shadowCascadeCount}");
    }

    private static void AppendCameraReport(StringBuilder report)
    {
        Camera camera = FindMainCamera(out string how);

        if (camera == null)
        {
            report.AppendLine($"MAIN CAMERA: <none found - {how}>");
            return;
        }

        // TryGetComponent, not GetUniversalAdditionalCameraData: that extension ADDS the
        // component when it is missing, which would make this read-only report a scene edit.
        if (!camera.TryGetComponent(out UniversalAdditionalCameraData data))
        {
            report.AppendLine($"MAIN CAMERA '{HierarchyPath(camera.transform)}' ({how}): " +
                              "<no UniversalAdditionalCameraData component>");
            return;
        }

        report.AppendLine($"MAIN CAMERA '{HierarchyPath(camera.transform)}' ({how}): " +
                          $"antialiasing={data.antialiasing}, " +
                          $"antialiasingQuality={data.antialiasingQuality}, " +
                          $"renderPostProcessing={data.renderPostProcessing}");
    }

    private static void AppendProfileReport(StringBuilder report, string label, string path)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

        if (profile == null)
        {
            report.AppendLine($"{label} ({path}): <not found>");
            return;
        }

        if (!profile.TryGet(out Bloom bloom))
        {
            report.AppendLine($"{label} '{profile.name}': Bloom override ABSENT");
            return;
        }

        report.AppendLine($"{label} '{profile.name}': Bloom override PRESENT, " +
                          $"intensity.value={Num(bloom.intensity.value)}, " +
                          $"intensity.overrideState={bloom.intensity.overrideState}, " +
                          $"active={bloom.active}");
    }

    // ---- 2. Terrain instancing and shadow mode --------------------------------

    /// <summary>
    /// Turns on instanced terrain drawing and takes the terrains off two-sided shadow casting.
    ///
    /// Both are invisible changes. The terrain material resolves to URP's stock TerrainLit,
    /// which already carries the instancing path, so drawInstanced needs no shader work here;
    /// and a heightfield has no meaningful back faces, so casting shadows from both sides is
    /// rasterisation spent on triangles that face away from every light. Terrain.Heightmap
    /// .RenderStep1+2+3 measured 0.275 ms/frame, 4.2 % of the main thread, before this.
    ///
    /// detailObjectDistance is deliberately NOT touched: the owner keeps it at 80 because
    /// lowering it makes the grass pop in closer, which is a visible change.
    /// </summary>
    [MenuItem("Tools/Performance/Apply Terrain Instancing And Shadow Mode", priority = 120)]
    private static void ApplyTerrainInstancingAndShadowMode()
    {
        const string undoLabel = "Terrain instancing and shadow mode";

        Terrain[] terrains = FindTerrains();
        if (terrains.Length == 0)
        {
            Debug.LogWarning($"{LogPrefix} No Terrain in the open scene(s), inactive included - " +
                             "nothing to do. Nothing was changed.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(undoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        List<string> changed = new List<string>();
        List<string> alreadyDone = new List<string>();
        HashSet<string> dirtiedScenes = new HashSet<string>();

        foreach (Terrain terrain in terrains)
        {
            string path = HierarchyPath(terrain.transform);
            bool instancedWas = terrain.drawInstanced;
            ShadowCastingMode shadowsWere = terrain.shadowCastingMode;

            if (instancedWas && shadowsWere == ShadowCastingMode.On)
            {
                alreadyDone.Add($"'{path}' (drawInstanced=True, shadowCastingMode=On)");
                continue;
            }

            Undo.RecordObject(terrain, undoLabel);
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.On;

            changed.Add($"'{path}': drawInstanced {instancedWas} -> {terrain.drawInstanced}, " +
                        $"shadowCastingMode {shadowsWere} -> {terrain.shadowCastingMode} " +
                        $"(detailObjectDistance left at {Num(terrain.detailObjectDistance)}, " +
                        $"heightmapPixelError left at {Num(terrain.heightmapPixelError)})");

            Scene scene = terrain.gameObject.scene;
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                dirtiedScenes.Add(scene.name);
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (changed.Count == 0)
        {
            Debug.Log($"{LogPrefix} All {terrains.Length} terrain(s) were already instanced and " +
                      $"casting one-sided shadows - nothing to do, and no need to save. " +
                      $"{string.Join("; ", alreadyDone)}");
            return;
        }

        string skipped = alreadyDone.Count > 0
            ? $" Left alone (already correct): {string.Join("; ", alreadyDone)}."
            : string.Empty;

        Debug.Log($"{LogPrefix} Terrain instancing and shadow mode, {changed.Count} of " +
                  $"{terrains.Length} terrain(s) changed: {string.Join("; ", changed)}.{skipped} " +
                  "detailObjectDistance was NOT touched (owner's decision - lowering it pops the " +
                  $"grass in). This is a SCENE edit and it is undoable. SAVE THE SCENE " +
                  $"({string.Join(", ", dirtiedScenes)}) or it is lost.");
    }

    // ---- 3. Shadow distance ---------------------------------------------------

    /// <summary>
    /// Pulls the main light's shadow distance in from 40 m to 25 m on the pipeline asset.
    /// Fewer renderers fall inside the shadow-casting volume, so the culling and the shadow
    /// pass both get less to do; the cascade count and the shadowmap resolution are left
    /// alone because they change shadow quality where shadows still exist, and this change is
    /// meant to be invisible at the distances the player actually looks at.
    /// </summary>
    [MenuItem("Tools/Performance/Apply Shadow Distance 25", priority = 121)]
    private static void ApplyShadowDistance25()
    {
        UniversalRenderPipelineAsset pipeline = GetPipelineAsset();

        if (pipeline == null)
        {
            Debug.LogWarning($"{LogPrefix} GraphicsSettings.defaultRenderPipeline is null or is not a " +
                             "UniversalRenderPipelineAsset, so there is no shadowDistance to set. " +
                             "REFUSED - nothing was changed. Check Project Settings > Graphics.");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(pipeline);
        float before = pipeline.shadowDistance;

        if (Mathf.Approximately(before, TargetShadowDistance))
        {
            Debug.Log($"{LogPrefix} '{pipeline.name}' ({assetPath}) already has " +
                      $"shadowDistance={Num(before)} - nothing to do, nothing written. " +
                      $"mainLightShadowmapResolution={pipeline.mainLightShadowmapResolution} and " +
                      $"shadowCascadeCount={pipeline.shadowCascadeCount} are not touched by this item.");
            return;
        }

        Undo.RecordObject(pipeline, "Shadow distance 25");
        pipeline.shadowDistance = TargetShadowDistance;
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();

        Debug.Log($"{LogPrefix} '{pipeline.name}' ({assetPath}): shadowDistance " +
                  $"{Num(before)} -> {Num(pipeline.shadowDistance)}. Left alone: " +
                  $"mainLightShadowmapResolution={pipeline.mainLightShadowmapResolution}, " +
                  $"shadowCascadeCount={pipeline.shadowCascadeCount}. " +
                  "⚠ This is an ASSET edit: AssetDatabase.SaveAssets has already written it to " +
                  $"disk, so Ctrl+Z will NOT bring the old value back - `git checkout -- \"{assetPath}\"` " +
                  "is the way back.");
    }

    // ---- 4. FXAA --------------------------------------------------------------

    /// <summary>
    /// Switches the first-person camera to FXAA. The URP asset has MSAA off, so the level is
    /// currently rendering with no antialiasing at all; FXAA is a single full-screen pass with
    /// no extra geometry work, which is the cheap way to buy back the edge quality an assessed
    /// look wants. antialiasingQuality is left where the owner set it.
    /// </summary>
    [MenuItem("Tools/Performance/Apply FXAA On Main Camera", priority = 122)]
    private static void ApplyFxaaOnMainCamera()
    {
        Camera camera = FindMainCamera(out string how);

        if (camera == null)
        {
            Debug.LogWarning($"{LogPrefix} No camera found - {how}. REFUSED: nothing was changed. " +
                             $"Expected either a camera tagged MainCamera or one on a GameObject " +
                             $"named '{CameraObjectName}'.");
            return;
        }

        string cameraPath = HierarchyPath(camera.transform);

        // GetUniversalAdditionalCameraData ADDS the component when it is missing. On this scene
        // it is already there (verified), so this is a lookup; the branch below only exists so
        // that adding one is reported as the scene change it would be.
        bool hadData = camera.TryGetComponent(out UniversalAdditionalCameraData _);
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();

        if (!hadData)
        {
            Debug.LogWarning($"{LogPrefix} '{cameraPath}' had no UniversalAdditionalCameraData, so one " +
                             "was added. That is a scene change in itself - check it is on the camera " +
                             "you expected before saving.", data);
        }

        AntialiasingMode before = data.antialiasing;

        if (hadData && before == AntialiasingMode.FastApproximateAntialiasing)
        {
            Debug.Log($"{LogPrefix} '{cameraPath}' ({how}) is already on " +
                      $"antialiasing={before} - nothing to do, and no need to save. " +
                      $"antialiasingQuality={data.antialiasingQuality} and " +
                      $"renderPostProcessing={data.renderPostProcessing} are not touched by this item.");
            return;
        }

        Undo.RecordObject(data, "FXAA on main camera");
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        Scene scene = camera.gameObject.scene;
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"{LogPrefix} '{cameraPath}' ({how}): antialiasing {before} -> " +
                  $"{data.antialiasing}. Left alone: antialiasingQuality={data.antialiasingQuality}, " +
                  $"renderPostProcessing={data.renderPostProcessing}. This is a look change - " +
                  "compare the before/after screenshots. It is a SCENE edit and it is undoable. " +
                  $"SAVE THE SCENE ({scene.name}) or it is lost.");
    }

    // ---- 5. Duplicate Bloom ---------------------------------------------------

    /// <summary>
    /// Leaves exactly one Bloom override in the level.
    ///
    /// Two GLOBAL Volumes ('Post Processing' and 'FirstPersonCharacter') sit at the same
    /// priority (0) and the same weight (1) and both override Bloom - at 0.4 and at 1.47. With
    /// equal priority the winner is whichever the volume manager happens to sort first, so the
    /// bloom level of an assessed look is currently undefined and can differ between sessions.
    /// The owner chose 0.4, kept in the grade profile beside the rest of the grade, so the
    /// FirstPersonCharacter profile's Bloom is deleted outright rather than merely disabled -
    /// a disabled override is a trap the next person has to rediscover.
    ///
    /// The camera profile's ChromaticAberration (intensity 0, not overridden) and the Volume
    /// components themselves are left exactly as they are: only the Bloom is in scope.
    /// </summary>
    [MenuItem("Tools/Performance/Resolve Duplicate Bloom", priority = 123)]
    private static void ResolveDuplicateBloom()
    {
        VolumeProfile grade = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradeProfilePath);
        VolumeProfile cameraProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(CameraProfilePath);

        if (grade == null || cameraProfile == null)
        {
            Debug.LogWarning($"{LogPrefix} REFUSED - nothing was changed. Could not load " +
                             $"{(grade == null ? $"the grade profile at '{GradeProfilePath}'" : "")}" +
                             $"{(grade == null && cameraProfile == null ? " and " : "")}" +
                             $"{(cameraProfile == null ? $"the camera profile at '{CameraProfilePath}'" : "")}" +
                             " as a VolumeProfile. Check the paths against the anchors table in " +
                             "Documentation/PERFORMANCE_FIX_TASKS.md.");
            return;
        }

        if (!grade.TryGet(out Bloom gradeBloom))
        {
            Debug.LogWarning($"{LogPrefix} REFUSED - nothing was changed. The grade profile " +
                             $"'{grade.name}' has no Bloom override, so there is no surviving Bloom " +
                             "to keep. Deleting the camera profile's Bloom here would leave the level " +
                             "with none at all, which is a bigger look change than the one asked for.");
            return;
        }

        bool wroteAnything = false;

        // (a) the keeper.
        float gradeIntensityBefore = gradeBloom.intensity.value;
        bool gradeOverrideBefore = gradeBloom.intensity.overrideState;

        if (Mathf.Approximately(gradeIntensityBefore, TargetBloomIntensity) && gradeOverrideBefore)
        {
            Debug.Log($"{LogPrefix} Grade profile '{grade.name}': Bloom intensity unchanged at " +
                      $"{Num(gradeIntensityBefore)} (overrideState={gradeOverrideBefore}) - it is " +
                      "already the owner's chosen value, so nothing was written to it.");
        }
        else
        {
            gradeBloom.intensity.value = TargetBloomIntensity;
            gradeBloom.intensity.overrideState = true;
            grade.isDirty = true;                       // so a running play session re-evaluates
            EditorUtility.SetDirty(grade);
            wroteAnything = true;

            Debug.Log($"{LogPrefix} Grade profile '{grade.name}': Bloom intensity.value " +
                      $"{Num(gradeIntensityBefore)} -> {Num(gradeBloom.intensity.value)}, " +
                      $"intensity.overrideState {gradeOverrideBefore} -> " +
                      $"{gradeBloom.intensity.overrideState}.");
        }

        // (b) the duplicate.
        if (!cameraProfile.TryGet(out Bloom cameraBloom))
        {
            Debug.Log($"{LogPrefix} Camera profile '{cameraProfile.name}' has no Bloom override - " +
                      "nothing to do, it has already been resolved. " +
                      DescribeCameraProfileLeftovers(cameraProfile));

            if (wroteAnything) AssetDatabase.SaveAssets();
            return;
        }

        float removedIntensity = cameraBloom.intensity.value;
        bool removedOverride = cameraBloom.intensity.overrideState;

        cameraProfile.Remove<Bloom>();

        // VolumeProfile.Remove only drops the reference from profile.components - the Bloom is a
        // sub-asset of the .asset file and would stay in it, orphaned, as a component nobody can
        // see or edit. DestroyImmediate with allowDestroyingAssets:true is the form that takes an
        // object out of an asset file; Undo.DestroyObjectImmediate is not used because
        // AssetDatabase.SaveAssets below writes the file straight away, so an undo entry here
        // would promise a revert this item cannot deliver.
        Object.DestroyImmediate(cameraBloom, true);

        cameraProfile.isDirty = true;
        EditorUtility.SetDirty(cameraProfile);
        AssetDatabase.SaveAssets();

        Debug.Log($"{LogPrefix} Camera profile '{cameraProfile.name}' ({CameraProfilePath}): Bloom " +
                  $"override DELETED (it was intensity.value={Num(removedIntensity)}, " +
                  $"intensity.overrideState={removedOverride}). The level now has exactly one Bloom, " +
                  $"in the grade profile at {Num(gradeBloom.intensity.value)}, so which volume wins is " +
                  $"no longer undefined. {DescribeCameraProfileLeftovers(cameraProfile)} " +
                  "⚠⚠ NOT UNDOABLE. Both profiles are ASSETS and AssetDatabase.SaveAssets has already " +
                  $"written them to disk; the sub-asset is destroyed. `git checkout -- " +
                  $"\"{CameraProfilePath}\" \"{GradeProfilePath}\"` is the ONLY way back. " +
                  "This is a look change - compare the before/after screenshots.");
    }

    /// <summary>Names what was deliberately left in the camera profile, so the log shows the
    /// item did not quietly take anything else with it.</summary>
    private static string DescribeCameraProfileLeftovers(VolumeProfile profile)
    {
        List<string> kept = new List<string>();
        foreach (VolumeComponent component in profile.components)
        {
            if (component != null) kept.Add(component.GetType().Name);
        }

        return kept.Count == 0
            ? "That profile now holds no overrides at all; its Volume component is left in place."
            : $"Left untouched in that profile: {string.Join(", ", kept)} (and its Volume component).";
    }

    // ---- Shared helpers -------------------------------------------------------

    /// <summary>Every Terrain in the open scene(s), inactive included, in a stable order -
    /// FindObjectsByType does not promise one, and these logs are meant to be diffed.</summary>
    private static Terrain[] FindTerrains()
    {
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        System.Array.Sort(terrains, (a, b) => string.CompareOrdinal(
            HierarchyPath(a.transform), HierarchyPath(b.transform)));

        return terrains;
    }

    /// <summary>
    /// The camera the level renders through, and a phrase saying how it was found so the log
    /// makes clear which rule fired. Camera.main is the primary route (the FirstPersonCharacter
    /// GameObject is tagged MainCamera); the name lookup covers a session where the tag is
    /// missing or the object is inactive, which Camera.main will not see.
    /// </summary>
    private static Camera FindMainCamera(out string how)
    {
        Camera main = Camera.main;
        if (main != null)
        {
            how = "Camera.main";
            return main;
        }

        foreach (Camera candidate in Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.gameObject.name != CameraObjectName) continue;

            how = $"fallback: the Camera on the GameObject named '{CameraObjectName}' " +
                  "(Camera.main was null - no active, enabled camera is tagged MainCamera)";
            return candidate;
        }

        how = $"Camera.main is null and no camera sits on a GameObject named '{CameraObjectName}'";
        return null;
    }

    private static UniversalRenderPipelineAsset GetPipelineAsset()
    {
        return GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
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

    /// <summary>Invariant culture, so the report diffs the same on a machine with a comma
    /// decimal separator.</summary>
    private static string Num(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- Forward+

    /// <summary>
    /// Switches the renderer from Forward to Forward+.
    ///
    /// Why it matters here: the URP asset already asks for the GPU Resident Drawer
    /// (m_GPUResidentDrawerMode: 1) and for its occlusion culling
    /// (m_GPUResidentDrawerEnableOcclusionCullingInCameras: 1), but URP disables both on a
    /// Forward renderer and logs "GPUResidentDrawer Disabled due to some configured
    /// Universal Renderers not supporting Forward+". So today those two settings do nothing
    /// at all. Forward+ also removes the per-object additional-light limit of 4, which the
    /// studio exceeds: four shadowed spots plus the always-on second directional light make
    /// five, so one of them is silently dropped per object.
    ///
    /// m_RenderingMode: 0 = Forward, 1 = Deferred, 2 = Forward+.
    /// Written through SerializedObject because the field is private on the renderer data.
    ///
    /// ⚠⚠ MEASURED AND REVERTED, 2026-09-04. It was applied, captured and rolled back on this
    /// project because it is a REGRESSION here, not a win. Same day, same machine, uncapped:
    ///
    ///     40 zombies   115 fps  ->  88 fps with Forward+  ->  115 fps again once reverted
    ///     tree view    173      ->  144                   ->  162
    ///     idle         170      ->  138                   ->  141
    ///
    /// The clustered lighting pass costs more than the GPU Resident Drawer wins back at this
    /// scene's scale. The phase-1 analysis estimated an 11-26 % gain from it; that estimate
    /// was wrong, and it was labelled an estimate at the time. The item is kept because
    /// "switch to Forward+" is an obvious suggestion that will come up again - run it, capture
    /// it, and you will get the numbers above. Do not enable it on a hunch.
    /// </summary>
    [MenuItem("Tools/Performance/Apply Forward Plus", priority = 124)]
    private static void ApplyForwardPlus()
    {
        ScriptableRendererData data = FindRendererData(out string assetPath);
        if (data == null)
        {
            Debug.LogWarning("[PerformanceFixTools] Could not find a renderer on the active URP asset. Nothing changed.");
            return;
        }

        var so = new SerializedObject(data);
        SerializedProperty mode = so.FindProperty("m_RenderingMode");
        if (mode == null)
        {
            Debug.LogWarning("[PerformanceFixTools] '" + data.name + "' has no m_RenderingMode property " +
                             "(is it a UniversalRendererData?). Nothing changed.");
            return;
        }

        string before = DescribeRenderingMode(mode.enumValueIndex);
        if (mode.enumValueIndex == 2)
        {
            Debug.Log("[PerformanceFixTools] '" + data.name + "' is already Forward+ - nothing to do.");
            return;
        }

        mode.enumValueIndex = 2;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        Debug.Log("[PerformanceFixTools] '" + data.name + "' (" + assetPath + "): renderingMode " +
                  before + " -> Forward+. The GPU Resident Drawer and its occlusion culling, both " +
                  "already switched on in the URP asset, can now actually run, and the per-object " +
                  "additional-light limit of 4 no longer applies. ⚠ This changes the LIGHTING PATH to " +
                  "clustered - compare the before/after screenshots, especially in the studio. " +
                  "ASSET edit: AssetDatabase.SaveAssets has already written it; " +
                  "`git checkout -- \"" + assetPath + "\"` is the way back.");
    }

    /// <summary>Human-readable name for the m_RenderingMode enum index.</summary>
    private static string DescribeRenderingMode(int index)
    {
        switch (index)
        {
            case 0: return "Forward";
            case 1: return "Deferred";
            case 2: return "Forward+";
            default: return "unknown(" + index + ")";
        }
    }

    /// <summary>
    /// The first renderer on the active URP asset. m_RendererDataList is private, so this
    /// goes through SerializedObject rather than the public API, which only exposes the
    /// current renderer as a ScriptableRenderer (not its data asset).
    /// </summary>
    private static ScriptableRendererData FindRendererData(out string assetPath)
    {
        assetPath = null;
        if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)) return null;

        var so = new SerializedObject(urp);
        SerializedProperty list = so.FindProperty("m_RendererDataList");
        if (list == null || list.arraySize == 0) return null;

        var data = list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableRendererData;
        if (data != null) assetPath = AssetDatabase.GetAssetPath(data);
        return data;
    }

    // --------------------------------------------------------- Texture caps

    /// <summary>
    /// The textures the scene actually reaches that are larger than they need to be, and the
    /// two project-authored icons that ship uncompressed.
    ///
    /// The icons are uncompressed for a subtle reason worth recording: they are 817x817 and
    /// 585x585, and neither dimension is a multiple of 4, so Unity cannot block-compress them
    /// however the importer is set - it silently falls back to RGBA32. Capping them at 256
    /// makes them a compressible size as well as a sane one for a screen icon.
    ///
    /// ⚠ The effective cap is the DefaultTexturePlatform entry in platformSettings, which is
    /// what TextureImporter.maxTextureSize reads and writes. The `maxTextureSize` near the top
    /// of a .meta is a legacy field and setting it does nothing.
    /// </summary>
    private static readonly (string Path, int Max)[] TextureCaps =
    {
        ("Assets/Inbox/Low Poly Models/Nature/FishingRod_Texture_01_A.png", 2048),
        ("Assets/Inbox/Low Poly Models/Textures/Alts/PolygonHorror_Texture_01_A.png", 2048),
        ("Assets/Inbox/Low Poly Models/Nature/Textures/Misc/Skybox_Diff.png", 2048),
        ("Assets/Inbox/Low Poly Models/Nature/Textures/Misc/Skybox_No_Snow_Diff.png", 2048),
        ("Assets/Inbox/Low Poly Models/Nature/Textures/Misc/Skybox_Normal.png", 2048),
        ("Assets/Walkie Talkie/Walkie Talkie Icon.png", 256),
        ("Assets/Flashlight/Torch Inventory Icon.png", 256),
    };

    [MenuItem("Tools/Performance/Apply Texture Size Caps", priority = 125)]
    private static void ApplyTextureCaps()
    {
        var changed = new List<string>();
        var skipped = new List<string>();

        foreach ((string path, int max) in TextureCaps)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                skipped.Add(path + " (not found or not a texture)");
                continue;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            string was = texture != null
                ? texture.width + "x" + texture.height + " " + texture.format
                : "unknown";

            bool needsCap = importer.maxTextureSize != max;
            bool needsCompression = importer.textureCompression == TextureImporterCompression.Uncompressed;

            if (!needsCap && !needsCompression)
            {
                skipped.Add(System.IO.Path.GetFileName(path) + " (already max=" + max + ")");
                continue;
            }

            int before = importer.maxTextureSize;
            importer.maxTextureSize = max;
            if (needsCompression) importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();

            var after = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            changed.Add(System.IO.Path.GetFileName(path) + ": maxTextureSize " + before + " -> " + max +
                        ", was " + was + ", now " +
                        (after != null ? after.width + "x" + after.height + " " + after.format : "unknown"));
        }

        var text = new StringBuilder();
        text.Append("[PerformanceFixTools] Texture caps: ").Append(changed.Count).Append(" changed, ")
            .Append(skipped.Count).AppendLine(" skipped.");
        foreach (string c in changed) text.Append("  changed  ").AppendLine(c);
        foreach (string s in skipped) text.Append("  skipped  ").AppendLine(s);

        if (changed.Count > 0)
        {
            text.AppendLine("⚠ These are IMPORT settings written straight to the .meta files and reimported; " +
                            "Ctrl+Z will not undo them. `git checkout -- <the .meta files>` is the way back. " +
                            "This is a look change on the skybox especially - a gradient is where 4096 -> 2048 " +
                            "can band. Compare the before/after screenshots.");
        }

        Debug.Log(text.ToString());
    }

}
