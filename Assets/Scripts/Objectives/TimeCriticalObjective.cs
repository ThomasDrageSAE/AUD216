using UnityEngine;

/*
 * TimeCriticalObjective.cs
 * 
 * Purpose: Manages time-sensitive mission requirements
 * Used by: Day/night cycle, mission pacing
 * 
 * Key Features:
 * - Time-based objective tracking
 * - Day-specific requirements
 * - Time pause triggers
 * - Multiple objective support
 * 
 * Time Management:
 * - Configurable pause times
 * - Day-based scheduling
 * - Multiple objective dependencies
 * - Flexible completion windows
 * 
 * Dependencies:
 * - DayNightCycle system
 * - ObjectiveManager
 * - Mission progression system
 */

[System.Serializable]
public class TimeCriticalObjective
{
    [Tooltip("The day this time-critical objective applies to")]
    public int dayNumber;
    
    [Tooltip("The objective IDs that must be completed")]
    public string[] requiredObjectiveIds;
    
    [Tooltip("The time at which to pause if objectives aren't complete (0-1)")]
    [Range(0f, 1f)]
    public float pauseTimeOfDay = 0.792f;
} 