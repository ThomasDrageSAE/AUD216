using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/*
 * ObjectiveRadar.cs
 * 
 * Purpose: Manages radar/compass system for objective tracking
 * Used by: Navigation system, objective tracking
 * 
 * Key Features:
 * - Dynamic objective indicator positioning
 * - Distance-based visibility
 * - Automatic cleanup of completed objectives
 * - Compass-style navigation
 * 
 * Performance Considerations:
 * - Optimized position calculations
 * - Efficient indicator pooling
 * - Smart cleanup of unused indicators
 * 
 * Dependencies:
 * - WorldSpaceObjectiveManager integration
 * - Unity UI system
 * - Requires player reference
 */
public class ObjectiveRadar : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public RectTransform radarCanvas;
    public RectTransform compassBar;
    public GameObject objectiveIndicatorPrefab;
    
    [Header("Settings")]
    public float maxDistance = 50f;
    public float compassBarWidth;

    private Dictionary<string, GameObject> indicators = new Dictionary<string, GameObject>();
    // Reused so the cleanup pass below doesn't allocate a list every frame
    private readonly List<string> indicatorsToRemove = new List<string>();

    private void Start()
    {
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (compassBarWidth == 0)
            compassBarWidth = compassBar.rect.width;
    }

    private void LateUpdate()
    {
        if (player == null || radarCanvas == null || WorldSpaceObjectiveManager.Instance == null) return;

        foreach (var marker in WorldSpaceObjectiveManager.Instance.ActiveMarkers)
        {
            string markerId = marker.Key;
            WorldSpaceObjectiveMarker worldMarker = marker.Value;

            if (worldMarker != null && worldMarker.TargetTransform != null)
            {
                UpdateOrCreateIndicator(markerId, worldMarker);
            }
        }

        // Clean up any indicators for objectives that no longer exist
        indicatorsToRemove.Clear();
        foreach (var indicator in indicators)
        {
            if (!WorldSpaceObjectiveManager.Instance.ActiveMarkers.ContainsKey(indicator.Key))
            {
                indicatorsToRemove.Add(indicator.Key);
            }
        }

        foreach (var markerId in indicatorsToRemove)
        {
            if (indicators[markerId] != null)
                Destroy(indicators[markerId]);
            indicators.Remove(markerId);
        }
    }

    private void UpdateOrCreateIndicator(string markerId, WorldSpaceObjectiveMarker worldMarker)
    {
        GameObject indicator;
        if (!indicators.TryGetValue(markerId, out indicator))
        {
            // Create new indicator
            indicator = Instantiate(objectiveIndicatorPrefab, compassBar);
            indicators[markerId] = indicator;

            // Set the icon if available
            Image indicatorImage = indicator.GetComponent<Image>();
            if (indicatorImage != null)
            {
                indicatorImage.sprite = worldMarker.MarkerIcon;
            }
        }

        UpdateIndicatorPosition(worldMarker.TargetTransform, indicator);
    }

    private void UpdateIndicatorPosition(Transform target, GameObject indicator)
    {
        Vector3 directionToTarget = target.position - player.position;
        float distanceToTarget = directionToTarget.magnitude;

        if (distanceToTarget <= maxDistance)
        {
            indicator.SetActive(true);

            Vector3 forwardVector = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            Vector3 targetVector = Vector3.ProjectOnPlane(directionToTarget, Vector3.up).normalized;
            
            float angleToTarget = Vector3.SignedAngle(forwardVector, targetVector, Vector3.up);
            float indicatorPosition = (angleToTarget / 180f) * compassBarWidth;
            
            // Clamp to edges when beyond ±58 degrees
            if (angleToTarget > 58f)
            {
                indicatorPosition = compassBarWidth * 0.322f; // Adjusted for 58 degrees (58/180)
            }
            else if (angleToTarget < -58f)
            {
                indicatorPosition = -compassBarWidth * 0.322f; // Adjusted for -58 degrees (-58/180)
            }
            
            RectTransform indicatorRect = indicator.GetComponent<RectTransform>();
            indicatorRect.anchoredPosition = new Vector2(indicatorPosition, indicatorRect.anchoredPosition.y);
        }
        else
        {
            indicator.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        foreach (var indicator in indicators.Values)
        {
            if (indicator != null)
                Destroy(indicator);
        }
        indicators.Clear();
    }
} 