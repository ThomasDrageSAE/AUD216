using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the dust manager in the scene and hands it the pack's two dust materials. Two menu
/// items under Tools/Level:
///
///   Report Damage Dust  - read-only: is the manager there, are its materials wired, how many
///                         breakable props will use it and what tags they carry.
///   Add Damage Dust     - creates or repairs the manager. Idempotent.
///   Reset Damage Dust   - removes the component and adds it back, so the scene takes the
///                         CURRENT code defaults. ⚠ Serialized fields keep the values they
///                         had when the component was created; a retune of the defaults in
///                         DamageDustManager.cs changes nothing in a scene that already has
///                         one. Measured 2026-09-06: the first retune was invisible until this.
///
/// The materials are looked up by NAME across the project rather than by path, because the
/// Synty packs under Assets/Inbox have been reorganised before and will be again; a name
/// survives that, a path does not.
///
/// ⚠ Scene changes only. No asset is written.
/// </summary>
public static class DamageDustSetupTools
{
    private const string LogPrefix = "[DamageDustSetupTools]";
    private const string UndoLabel = "Add Damage Dust";

    /// <summary>Where the manager lives, beside the other game-management singletons.</summary>
    private const string HostPath = "Game Management";
    private const string ManagerName = "Damage Dust";

    /// <summary>
    /// The pack's bullet-impact dust: additive soft round. Right for a puff, useless for a
    /// cloud that has to hide something, because additive can only brighten what is behind
    /// it and never cover it.
    /// </summary>
    private const string PuffMaterialName = "PolygonParticleFX_Cutout_Round";

    /// <summary>
    /// The pack's alpha-blended round (Legacy Shaders/Particles/Alpha Blended), used by its
    /// dirt, water and metal impacts. Blended dust occludes, which is the cloud's whole job.
    /// </summary>
    private const string CloudMaterialName = "PolygonParticleFX_Cutout_Round_VertexLit";

    // ==========================================================================
    //  Report
    // ==========================================================================

    [MenuItem("Tools/Level/Report Damage Dust", priority = 1310)]
    private static void ReportDamageDust()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine($"{LogPrefix} Report Damage Dust - read-only.");

        DamageDustManager manager = Object.FindFirstObjectByType<DamageDustManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            report.AppendLine("MANAGER: <none> - breakable props will hit and vanish with no dust. " +
                              "Run Tools/Level/Add Damage Dust.");
        }
        else
        {
            SerializedObject serialized = new SerializedObject(manager);
            report.AppendLine($"MANAGER: {HierarchyPath(manager.transform)}, active={manager.gameObject.activeInHierarchy}");
            report.AppendLine($"  puffMaterial  = {Obj(serialized.FindProperty("puffMaterial").objectReferenceValue)}");
            report.AppendLine($"  cloudMaterial = {Obj(serialized.FindProperty("cloudMaterial").objectReferenceValue)}");

            SerializedProperty surfaces = serialized.FindProperty("surfaces");
            report.Append("  surfaces:");
            for (int i = 0; i < surfaces.arraySize; i++)
            {
                SerializedProperty entry = surfaces.GetArrayElementAtIndex(i);
                report.Append($" {entry.FindPropertyRelative("surfaceTag").stringValue}");
            }
            report.AppendLine();
            report.AppendLine($"  puff base {serialized.FindProperty("puffBaseCount").intValue} (max {serialized.FindProperty("puffMaxCount").intValue}), " +
                              $"cloud {Num(serialized.FindProperty("cloudDensity").floatValue)}/m3 " +
                              $"({serialized.FindProperty("cloudMinCount").intValue}..{serialized.FindProperty("cloudMaxCount").intValue})");
        }

        Material puff = FindMaterial(PuffMaterialName);
        Material cloud = FindMaterial(CloudMaterialName);
        report.AppendLine($"PACK MATERIALS: puff '{PuffMaterialName}' = {(puff == null ? "<MISSING>" : AssetDatabase.GetAssetPath(puff))}");
        report.AppendLine($"                cloud '{CloudMaterialName}' = {(cloud == null ? "<MISSING>" : AssetDatabase.GetAssetPath(cloud))}");

        DamageableObject[] props = Object.FindObjectsByType<DamageableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var byTag = new SortedDictionary<string, int>();
        foreach (DamageableObject prop in props)
        {
            byTag.TryGetValue(prop.tag, out int count);
            byTag[prop.tag] = count + 1;
        }

        report.Append($"BREAKABLE PROPS: {props.Length} -");
        foreach (KeyValuePair<string, int> entry in byTag) report.Append($" {entry.Value} x {entry.Key}");
        report.AppendLine();

        Debug.Log(report.ToString().TrimEnd());
    }

    // ==========================================================================
    //  Add
    // ==========================================================================

    [MenuItem("Tools/Level/Add Damage Dust", priority = 1311)]
    private static void AddDamageDust()
    {
        List<string> fields = new List<string>();
        List<string> skipped = new List<string>();
        List<string> notes = new List<string>();

        GameObject host = GameObject.Find(HostPath);
        if (host == null)
        {
            Debug.LogError($"{LogPrefix} No '{HostPath}' object in the scene. Nothing was changed.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        DamageDustManager manager = Object.FindFirstObjectByType<DamageDustManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            GameObject made = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(made, UndoLabel);
            made.transform.SetParent(host.transform, false);
            manager = Undo.AddComponent<DamageDustManager>(made);
            fields.Add($"created {HierarchyPath(made.transform)} with a DamageDustManager");
        }
        else
        {
            skipped.Add($"{HierarchyPath(manager.transform)} already has a DamageDustManager - repaired in place");
        }

        SerializedObject serialized = new SerializedObject(manager);
        WireMaterial(serialized, "puffMaterial", PuffMaterialName, fields, skipped, notes);
        WireMaterial(serialized, "cloudMaterial", CloudMaterialName, fields, skipped, notes);
        serialized.ApplyModifiedProperties();

        int props = Object.FindObjectsByType<DamageableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        notes.Add($"{props} DamageableObject props in the scene will puff on every hit and cloud on " +
                  "death. Nothing on them was changed - they find the manager through its Instance " +
                  "and carry on silently if it is missing.");
        notes.Add("The colours, counts and sizes are all on the manager, in the inspector, with " +
                  "tooltips. The cloud's density is the number that decides whether the vanish " +
                  "is hidden.");

        Undo.CollapseUndoOperations(undoGroup);

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

        StringBuilder log = new StringBuilder();
        log.AppendLine($"{LogPrefix} Add Damage Dust finished. Scene changes only - no asset was written.");

        log.AppendLine($"CHANGED ({fields.Count}):");
        AppendList(log, fields, "  <nothing - the manager was already wired>");

        log.AppendLine($"ALREADY PRESENT ({skipped.Count}):");
        AppendList(log, skipped, "  <nothing existed yet - this was the first run>");

        if (notes.Count > 0)
        {
            log.AppendLine($"NOTES ({notes.Count}):");
            AppendList(log, notes, string.Empty);
        }

        log.AppendLine($"Undo-recorded as one step, '{UndoLabel}'. " +
                       $"SAVE THE SCENE ({scene.name}) or the manager is lost.");

        Debug.Log(log.ToString().TrimEnd());

        Selection.activeGameObject = manager.gameObject;
        EditorGUIUtility.PingObject(manager.gameObject);
    }

    /// <summary>
    /// The only way a scene picks up new code defaults: a fresh component. Undo-recorded as one
    /// step, and the materials are re-wired on the way back.
    /// </summary>
    [MenuItem("Tools/Level/Reset Damage Dust To Defaults", priority = 1312)]
    private static void ResetDamageDust()
    {
        DamageDustManager manager = Object.FindFirstObjectByType<DamageDustManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.Log($"{LogPrefix} No DamageDustManager to reset - running Add Damage Dust instead.");
            AddDamageDust();
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Reset Damage Dust");
        int undoGroup = Undo.GetCurrentGroup();

        string path = HierarchyPath(manager.transform);
        Undo.DestroyObjectImmediate(manager);
        Debug.Log($"{LogPrefix} Removed the DamageDustManager on {path}; adding a fresh one with the " +
                  "code's current defaults.");

        AddDamageDust();
        Undo.CollapseUndoOperations(undoGroup);
    }

    private static void WireMaterial(SerializedObject serialized, string field, string materialName,
                                     List<string> fields, List<string> skipped, List<string> notes)
    {
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            notes.Add($"⚠ DamageDustManager has no '{field}' field.");
            return;
        }

        if (property.objectReferenceValue != null)
        {
            skipped.Add($"{field} already = {Obj(property.objectReferenceValue)}");
            return;
        }

        Material material = FindMaterial(materialName);
        if (material == null)
        {
            notes.Add($"⚠ No material named '{materialName}' anywhere in the project, so {field} is " +
                      "empty and that system will render with Unity's default particle material - " +
                      "visible, but pink-tinted magenta in URP. Wire one by hand.");
            return;
        }

        property.objectReferenceValue = material;
        fields.Add($"{field} = '{material.name}' ({AssetDatabase.GetAssetPath(material)})");
    }

    /// <summary>The first material with exactly this name. FindAssets matches substrings, so the name is re-checked.</summary>
    private static Material FindMaterial(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} t:Material"))
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material != null && material.name == name) return material;
        }

        return null;
    }

    // ==========================================================================
    //  Helpers, the same shape as the other setup tools here
    // ==========================================================================

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

    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Obj(Object value) => value == null ? "<none>" : $"'{value.name}'";
}
