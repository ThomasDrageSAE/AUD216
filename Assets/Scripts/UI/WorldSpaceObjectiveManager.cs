using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

/*
 * WorldSpaceObjectiveManager.cs
 * 
 * Purpose: Manages 3D objective markers in the game world.
 * Used by: ObjectiveSystem, UI system
 * 
 * Controls the creation, positioning, and lifecycle of world-space
 * markers that indicate objective locations. Handles visibility,
 * distance checking, and marker cleanup.
 * 
 * Performance Considerations:
 * - Uses object pooling for markers
 * - Implements distance-based culling
 * - Optimizes marker updates
 * 
 * Dependencies:
 * - ObjectiveSystem
 * - UI prefabs for markers
 * - Requires proper layer setup
 */

public class WorldSpaceObjectiveManager : MonoBehaviour
{
    public static WorldSpaceObjectiveManager Instance { get; private set; }

    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private Transform markerContainer;

    private Dictionary<string, WorldSpaceObjectiveMarker> activeMarkers = 
        new Dictionary<string, WorldSpaceObjectiveMarker>();
    private Dictionary<string, List<Transform>> targetLocations = new Dictionary<string, List<Transform>>();

    public IReadOnlyDictionary<string, WorldSpaceObjectiveMarker> ActiveMarkers => activeMarkers;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Only log errors for missing critical components
        if (markerPrefab == null)
        {
            Debug.LogError("Marker Prefab not assigned in WorldSpaceObjectiveManager!");
        }
        if (markerContainer == null)
        {
            Debug.LogError("Marker Container not assigned in WorldSpaceObjectiveManager!");
        }
    }

    public void RemoveObjectiveMarker(string objectiveId, Transform target = null)
    {
        if (string.IsNullOrEmpty(objectiveId)) return;

        if (target != null)
        {
            // Remove specific marker for this target
            RemoveMarkerByKey($"{objectiveId}_{target.GetInstanceID()}");
            return;
        }

        // Markers created per target are keyed "<objectiveId>_<instanceId>", but markers
        // created directly (enemy compass pips) are keyed by the id alone. Only matching
        // the prefix leaked every direct marker, so clear both forms.
        RemoveMarkerByKey(objectiveId);

        string prefix = objectiveId + "_";
        var markersToRemove = activeMarkers.Keys
            .Where(k => k.StartsWith(prefix))
            .ToList();

        foreach (var markerId in markersToRemove)
        {
            RemoveMarkerByKey(markerId);
        }
    }

    private void RemoveMarkerByKey(string markerKey)
    {
        if (!activeMarkers.TryGetValue(markerKey, out WorldSpaceObjectiveMarker marker)) return;

        if (marker != null)
        {
            Destroy(marker.gameObject);
        }
        activeMarkers.Remove(markerKey);
    }

    public void RegisterTarget(string targetId, Transform target)
    {
        if (string.IsNullOrEmpty(targetId) || target == null) return;

        if (!targetLocations.TryGetValue(targetId, out List<Transform> targets))
        {
            targets = new List<Transform>();
            targetLocations[targetId] = targets;
        }

        if (!targets.Contains(target))
        {
            targets.Add(target);
        }
    }

    public List<Transform> GetTargetLocations(string targetId)
    {
        if (targetLocations.TryGetValue(targetId, out List<Transform> targets))
        {
            targets.RemoveAll(t => t == null);
            return targets;
        }
        return null;
    }

    public void CreateObjectiveMarkers(string objectiveId, List<Transform> targets, Sprite icon)
    {
        if (targets == null || targets.Count == 0) return;

        foreach (Transform target in targets)
        {
            string uniqueMarkerId = $"{objectiveId}_{target.GetInstanceID()}";
            
            // Create world space marker
            if (markerPrefab != null && markerContainer != null)
            {
                GameObject markerObj = Instantiate(markerPrefab, markerContainer);
                WorldSpaceObjectiveMarker marker = markerObj.GetComponent<WorldSpaceObjectiveMarker>();
                
                if (marker != null)
                {
                    marker.Initialize(target, icon);
                    activeMarkers[uniqueMarkerId] = marker;
                }
            }
        }
    }

    /// <summary>
    /// Removes a single registered transform. Multi-target objectives (firesticks)
    /// share one targetId, so dropping the whole list here used to unregister every
    /// other target as well.
    /// </summary>
    public void UnregisterTarget(string targetId, Transform target)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        if (!targetLocations.TryGetValue(targetId, out List<Transform> targets)) return;

        targets.Remove(target);
        if (targets.Count == 0)
        {
            targetLocations.Remove(targetId);
        }
    }
}