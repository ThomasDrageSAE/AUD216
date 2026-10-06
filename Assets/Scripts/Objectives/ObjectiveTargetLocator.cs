using UnityEngine;
using System.Linq;

/*
 * ObjectiveTargetLocator.cs
 * 
 * Purpose: Manages objective target locations and markers
 * Used by: World space objectives, navigation
 * 
 * Key Features:
 * - Target registration
 * - Marker management
 * - Cleanup handling
 * - Target discovery
 * 
 * Location Management:
 * - Position tracking
 * - Marker creation/removal
 * - Instance identification
 * - Target validation
 * 
 * Dependencies:
 * - WorldSpaceObjectiveManager
 * - Objective system
 * - Scene management
 */

public class ObjectiveTargetLocator : MonoBehaviour
{
    [SerializeField] private string targetId;

    // Add this static method to find all targets for a specific objective
    public static ObjectiveTargetLocator[] FindLocatorsForTarget(string objectiveId)
    {
        ObjectiveTargetLocator[] allLocators = GameObject.FindObjectsByType<ObjectiveTargetLocator>(FindObjectsSortMode.None);
        return allLocators.Where(locator => locator.targetId == objectiveId).ToArray();
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(targetId))
        {
           // Debug.LogError($"[Target] Target ID not set on {gameObject.name}!");
            return;
        }

       // Debug.Log($"[Target] {targetId} Start - About to register");
        if (WorldSpaceObjectiveManager.Instance != null)
        {
            WorldSpaceObjectiveManager.Instance.RegisterTarget(targetId, transform);
        }
        else
        {
           // Debug.LogError("[Target] WorldSpaceObjectiveManager not found!");
        }
    }

    private void OnDisable()
    {
        CleanupMarkers();
    }

    private void OnDestroy()
    {
        CleanupMarkers();
    }

    private void CleanupMarkers()
    {
        if (!string.IsNullOrEmpty(targetId) && WorldSpaceObjectiveManager.Instance != null)
        {
            // Only this locator's own registration and marker go away; sibling
            // locators sharing the same targetId keep theirs.
            WorldSpaceObjectiveManager.Instance.UnregisterTarget(targetId, transform);
            WorldSpaceObjectiveManager.Instance.RemoveObjectiveMarker(targetId, transform);
        }
    }

    // Add getter for targetId
    public string GetTargetId() => targetId;
}