using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System;

/*
 * ObjectiveManagerExporter.cs
 * 
 * Purpose: Exports ObjectiveManager configuration data to JSON for review
 * Used by: Unity Editor
 * 
 * Key Features:
 * - Custom editor with export button
 * - Serializes all ObjectiveManager fields
 * - Exports all objectives with full details
 * - Handles prerequisites and references
 */

[CustomEditor(typeof(ObjectiveManager))]
public class ObjectiveManagerExporter : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ObjectiveManager objectiveManager = (ObjectiveManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export Configuration", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Export Configuration to JSON"))
        {
            ExportConfiguration(objectiveManager);
        }
    }

    private void ExportConfiguration(ObjectiveManager objectiveManager)
    {
        try
        {
            // Create export directory if it doesn't exist
            // Export outside Assets/ so generated JSON isn't imported into the project
            string exportDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Exports"));
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            // Build export data
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"exportTimestamp\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
            json.AppendLine($"  \"componentName\": \"ObjectiveManager\",");
            json.AppendLine($"  \"gameObjectName\": \"{objectiveManager.gameObject.name}\",");
            json.AppendLine("  \"configuration\": {");

            SerializedObject serializedObject = new SerializedObject(objectiveManager);

            // Export allObjectives list
            SerializedProperty allObjectivesProperty = serializedObject.FindProperty("allObjectives");
            json.AppendLine("    \"allObjectives\": [");
            if (allObjectivesProperty != null && allObjectivesProperty.isArray)
            {
                for (int i = 0; i < allObjectivesProperty.arraySize; i++)
                {
                    SerializedProperty objectiveProperty = allObjectivesProperty.GetArrayElementAtIndex(i);
                    ObjectiveData objectiveData = objectiveProperty.objectReferenceValue as ObjectiveData;
                    
                    json.AppendLine("      {");
                    if (objectiveData != null)
                    {
                        json.AppendLine($"        \"objectiveId\": \"{EscapeJson(objectiveData.objectiveId)}\",");
                        json.AppendLine($"        \"description\": \"{EscapeJson(objectiveData.description)}\",");
                        json.AppendLine($"        \"icon\": \"{(objectiveData.icon != null ? EscapeJson(objectiveData.icon.name) : "null")}\",");
                        json.AppendLine($"        \"isNightObjective\": {objectiveData.isNightObjective.ToString().ToLower()},");
                        json.AppendLine($"        \"showWorldMarker\": {objectiveData.showWorldMarker.ToString().ToLower()},");
                        json.AppendLine($"        \"targetTag\": \"{EscapeJson(objectiveData.targetTag ?? "")}\",");
                        json.AppendLine($"        \"markerIcon\": \"{(objectiveData.markerIcon != null ? EscapeJson(objectiveData.markerIcon.name) : "null")}\",");

                        // Export prerequisites
                        json.AppendLine("        \"prerequisites\": [");
                        if (objectiveData.prerequisites != null && objectiveData.prerequisites.Length > 0)
                        {
                            for (int j = 0; j < objectiveData.prerequisites.Length; j++)
                            {
                                ObjectiveData prerequisite = objectiveData.prerequisites[j];
                                json.Append("          ");
                                if (prerequisite != null)
                                {
                                    json.AppendLine($"\"{EscapeJson(prerequisite.objectiveId)}\"");
                                }
                                else
                                {
                                    json.AppendLine("null");
                                }
                                if (j < objectiveData.prerequisites.Length - 1)
                                {
                                    json.Append(",");
                                }
                                json.AppendLine();
                            }
                        }
                        json.AppendLine("        ]");
                    }
                    else
                    {
                        json.AppendLine("        \"objectiveId\": \"null\",");
                        json.AppendLine("        \"description\": \"null\",");
                        json.AppendLine("        \"icon\": \"null\",");
                        json.AppendLine("        \"isNightObjective\": false,");
                        json.AppendLine("        \"showWorldMarker\": true,");
                        json.AppendLine("        \"targetTag\": \"\",");
                        json.AppendLine("        \"markerIcon\": \"null\",");
                        json.AppendLine("        \"prerequisites\": []");
                    }

                    json.Append("      }");
                    if (i < allObjectivesProperty.arraySize - 1)
                    {
                        json.Append(",");
                    }
                    json.AppendLine();
                }
            }
            json.AppendLine("    ],");

            // Export objectiveUIPrefab reference
            SerializedProperty objectiveUIPrefabProperty = serializedObject.FindProperty("objectiveUIPrefab");
            json.AppendLine($"    \"objectiveUIPrefab\": \"{GetObjectReferenceName(objectiveUIPrefabProperty)}\",");

            // Export objectivesContainer reference
            SerializedProperty objectivesContainerProperty = serializedObject.FindProperty("objectivesContainer");
            json.AppendLine($"    \"objectivesContainer\": \"{GetObjectReferenceName(objectivesContainerProperty)}\"");

            json.AppendLine("  }");
            json.AppendLine("}");

            // Write to file
            string fileName = $"ObjectiveManager_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string filePath = Path.Combine(exportDir, fileName);
            File.WriteAllText(filePath, json.ToString());

            EditorUtility.DisplayDialog("Export Successful",
                $"Configuration exported to:\n{filePath}",
                "OK");
            
            Debug.Log($"[ObjectiveManagerExporter] Configuration exported to: {filePath}");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Export Failed", 
                $"Error exporting configuration:\n{e.Message}", 
                "OK");
            Debug.LogError($"[ObjectiveManagerExporter] Export failed: {e}");
        }
    }

    private string GetObjectReferenceName(SerializedProperty property)
    {
        if (property == null || property.objectReferenceValue == null)
        {
            return "null";
        }

        UnityEngine.Object obj = property.objectReferenceValue;
        return EscapeJson(obj.name);
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}

