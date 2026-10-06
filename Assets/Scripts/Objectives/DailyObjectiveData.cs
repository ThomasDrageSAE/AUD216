using UnityEngine;

/*
 * DailyObjectiveData.cs
 * 
 * Purpose: Scriptable object for daily objective configuration
 * Used by: DailyObjectiveManager, mission system
 * 
 * Key Features:
 * - Day-specific objectives
 * - Time-based triggers
 * - Objective sequencing
 * - Mission organization
 * 
 * Configuration:
 * - Objective timing
 * - Day assignments
 * - Appearance conditions
 * - Mission dependencies
 * 
 * Dependencies:
 * - ObjectiveData system
 * - ScriptableObject system
 * - Mission progression
 */

[System.Serializable]
public class DailyObjective
{
    [Tooltip("The objective ID that matches with the objective in ObjectiveManager")]
    public string objectiveId;
    
    [Tooltip("The day this objective should appear (Day 1, Day 2, etc.)")]
    public int dayNumber;
    
    [Tooltip("The time of day this objective should appear (0-1)")]
    [Range(0f, 1f)]
    public float timeToAppear;
}

[CreateAssetMenu(fileName = "DailyObjectives", menuName = "Game/Daily Objectives")]
public class DailyObjectiveData : ScriptableObject
{
    public DailyObjective[] objectives;
} 