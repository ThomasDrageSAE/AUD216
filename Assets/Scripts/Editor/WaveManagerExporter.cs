using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System;

/*
 * WaveManagerExporter.cs
 * 
 * Purpose: Exports WaveManager configuration data to JSON for review
 * Used by: Unity Editor
 * 
 * Key Features:
 * - Custom editor with export button
 * - Serializes all WaveManager fields
 * - Handles nested structures (Week → Night → Wave)
 * - Exports Unity references as names/paths
 */

[CustomEditor(typeof(WaveManager))]
public class WaveManagerExporter : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WaveManager waveManager = (WaveManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export Configuration", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Export Configuration to JSON"))
        {
            ExportConfiguration(waveManager);
        }
    }

    private void ExportConfiguration(WaveManager waveManager)
    {
        try
        {
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
            json.AppendLine($"  \"componentName\": \"WaveManager\",");
            json.AppendLine($"  \"gameObjectName\": \"{waveManager.gameObject.name}\",");
            json.AppendLine("  \"configuration\": {");

            // Export weeks data using reflection to access private fields
            SerializedObject serializedObject = new SerializedObject(waveManager);
            SerializedProperty weeksProperty = serializedObject.FindProperty("weeks");

            json.AppendLine("    \"weeks\": [");
            if (weeksProperty != null && weeksProperty.isArray)
            {
                for (int weekIndex = 0; weekIndex < weeksProperty.arraySize; weekIndex++)
                {
                    SerializedProperty weekProperty = weeksProperty.GetArrayElementAtIndex(weekIndex);
                    json.AppendLine("      {");
                    json.AppendLine($"        \"weekIndex\": {weekIndex},");
                    
                    SerializedProperty weekNameProperty = weekProperty.FindPropertyRelative("weekName");
                    if (weekNameProperty != null)
                    {
                        json.AppendLine($"        \"weekName\": \"{weekNameProperty.stringValue}\",");
                    }

                    SerializedProperty nightsProperty = weekProperty.FindPropertyRelative("nights");
                    json.AppendLine("        \"nights\": [");
                    if (nightsProperty != null && nightsProperty.isArray)
                    {
                        for (int nightIndex = 0; nightIndex < nightsProperty.arraySize; nightIndex++)
                        {
                            SerializedProperty nightProperty = nightsProperty.GetArrayElementAtIndex(nightIndex);
                            json.AppendLine("          {");
                            json.AppendLine($"            \"nightIndex\": {nightIndex},");
                            
                            SerializedProperty nightNameProperty = nightProperty.FindPropertyRelative("nightName");
                            if (nightNameProperty != null)
                            {
                                json.AppendLine($"            \"nightName\": \"{nightNameProperty.stringValue}\",");
                            }

                            SerializedProperty wavesProperty = nightProperty.FindPropertyRelative("waves");
                            json.AppendLine("            \"waves\": [");
                            if (wavesProperty != null && wavesProperty.isArray)
                            {
                                for (int waveIndex = 0; waveIndex < wavesProperty.arraySize; waveIndex++)
                                {
                                    SerializedProperty waveProperty = wavesProperty.GetArrayElementAtIndex(waveIndex);
                                    json.AppendLine("              {");
                                    json.AppendLine($"                \"waveIndex\": {waveIndex},");
                                    
                                    SerializedProperty waveNameProperty = waveProperty.FindPropertyRelative("waveName");
                                    if (waveNameProperty != null)
                                    {
                                        json.AppendLine($"                \"waveName\": \"{EscapeJson(waveNameProperty.stringValue)}\",");
                                    }

                                    SerializedProperty numberOfZombiesProperty = waveProperty.FindPropertyRelative("numberOfZombies");
                                    if (numberOfZombiesProperty != null)
                                    {
                                        json.AppendLine($"                \"numberOfZombies\": {numberOfZombiesProperty.intValue},");
                                    }

                                    SerializedProperty spawnDelayProperty = waveProperty.FindPropertyRelative("spawnDelay");
                                    if (spawnDelayProperty != null)
                                    {
                                        json.AppendLine($"                \"spawnDelay\": {spawnDelayProperty.floatValue},");
                                    }

                                    SerializedProperty timeToStartProperty = waveProperty.FindPropertyRelative("timeToStart");
                                    if (timeToStartProperty != null)
                                    {
                                        json.AppendLine($"                \"timeToStart\": {timeToStartProperty.floatValue},");
                                    }

                                    SerializedProperty zombieSpeedMultiplierProperty = waveProperty.FindPropertyRelative("zombieSpeedMultiplier");
                                    if (zombieSpeedMultiplierProperty != null)
                                    {
                                        json.AppendLine($"                \"zombieSpeedMultiplier\": {zombieSpeedMultiplierProperty.floatValue},");
                                    }

                                    SerializedProperty healthMultiplierProperty = waveProperty.FindPropertyRelative("healthMultiplier");
                                    if (healthMultiplierProperty != null)
                                    {
                                        json.AppendLine($"                \"healthMultiplier\": {healthMultiplierProperty.floatValue},");
                                    }

                                    // Export zombie prefabs
                                    SerializedProperty zombiePrefabsProperty = waveProperty.FindPropertyRelative("zombiePrefabs");
                                    json.AppendLine("                \"zombiePrefabs\": [");
                                    if (zombiePrefabsProperty != null && zombiePrefabsProperty.isArray)
                                    {
                                        for (int prefabIndex = 0; prefabIndex < zombiePrefabsProperty.arraySize; prefabIndex++)
                                        {
                                            SerializedProperty prefabProperty = zombiePrefabsProperty.GetArrayElementAtIndex(prefabIndex);
                                            GameObject prefab = prefabProperty.objectReferenceValue as GameObject;
                                            json.Append("                  ");
                                            if (prefab != null)
                                            {
                                                json.AppendLine($"\"{EscapeJson(prefab.name)}\"");
                                            }
                                            else
                                            {
                                                json.AppendLine("null");
                                            }
                                            if (prefabIndex < zombiePrefabsProperty.arraySize - 1)
                                            {
                                                json.Append(",");
                                            }
                                            json.AppendLine();
                                        }
                                    }
                                    json.AppendLine("                ],");

                                    // Export spawn points
                                    SerializedProperty spawnPointsProperty = waveProperty.FindPropertyRelative("waveSpawnPoints");
                                    json.AppendLine("                \"waveSpawnPoints\": [");
                                    if (spawnPointsProperty != null && spawnPointsProperty.isArray)
                                    {
                                        for (int spawnIndex = 0; spawnIndex < spawnPointsProperty.arraySize; spawnIndex++)
                                        {
                                            SerializedProperty spawnProperty = spawnPointsProperty.GetArrayElementAtIndex(spawnIndex);
                                            Transform spawnPoint = spawnProperty.objectReferenceValue as Transform;
                                            json.Append("                  ");
                                            if (spawnPoint != null)
                                            {
                                                json.AppendLine($"\"{EscapeJson(spawnPoint.name)}\"");
                                            }
                                            else
                                            {
                                                json.AppendLine("null");
                                            }
                                            if (spawnIndex < spawnPointsProperty.arraySize - 1)
                                            {
                                                json.Append(",");
                                            }
                                            json.AppendLine();
                                        }
                                    }
                                    json.AppendLine("                ]");

                                    json.Append("              }");
                                    if (waveIndex < wavesProperty.arraySize - 1)
                                    {
                                        json.Append(",");
                                    }
                                    json.AppendLine();
                                }
                            }
                            json.AppendLine("            ]");

                            json.Append("          }");
                            if (nightIndex < nightsProperty.arraySize - 1)
                            {
                                json.Append(",");
                            }
                            json.AppendLine();
                        }
                    }
                    json.AppendLine("        ]");

                    json.Append("      }");
                    if (weekIndex < weeksProperty.arraySize - 1)
                    {
                        json.Append(",");
                    }
                    json.AppendLine();
                }
            }
            json.AppendLine("    ],");

            // Export UI references
            json.AppendLine("    \"uiElements\": {");
            SerializedProperty weekTextProperty = serializedObject.FindProperty("weekText");
            SerializedProperty nightTextProperty = serializedObject.FindProperty("nightText");
            SerializedProperty waveTextProperty = serializedObject.FindProperty("waveText");
            SerializedProperty zombieCountTextProperty = serializedObject.FindProperty("zombieCountText");

            json.AppendLine($"      \"weekText\": \"{GetObjectReferenceName(weekTextProperty)}\",");
            json.AppendLine($"      \"nightText\": \"{GetObjectReferenceName(nightTextProperty)}\",");
            json.AppendLine($"      \"waveText\": \"{GetObjectReferenceName(waveTextProperty)}\",");
            json.AppendLine($"      \"zombieCountText\": \"{GetObjectReferenceName(zombieCountTextProperty)}\"");
            json.AppendLine("    },");

            // Export DayNightCycle reference
            SerializedProperty dayNightCycleProperty = serializedObject.FindProperty("dayNightCycle");
            json.AppendLine($"    \"dayNightCycle\": \"{GetObjectReferenceName(dayNightCycleProperty)}\",");

            // Note: the wave-completion animation fields this used to export were never
            // read at runtime and have been removed from WaveManager.

            // Export night end settings
            SerializedProperty zombieDefenseTimeTillDawnProperty = serializedObject.FindProperty("zombieDefenseTimeTillDawn");
            json.AppendLine($"    \"zombieDefenseTimeTillDawn\": {(zombieDefenseTimeTillDawnProperty != null ? zombieDefenseTimeTillDawnProperty.floatValue.ToString() : "0.1")}");

            json.AppendLine("  }");
            json.AppendLine("}");

            // Write to file
            string fileName = $"WaveManager_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string filePath = Path.Combine(exportDir, fileName);
            File.WriteAllText(filePath, json.ToString());

            EditorUtility.DisplayDialog("Export Successful",
                $"Configuration exported to:\n{filePath}",
                "OK");
            
            Debug.Log($"[WaveManagerExporter] Configuration exported to: {filePath}");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Export Failed", 
                $"Error exporting configuration:\n{e.Message}", 
                "OK");
            Debug.LogError($"[WaveManagerExporter] Export failed: {e}");
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

