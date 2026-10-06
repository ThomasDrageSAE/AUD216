using UnityEngine;

/*
 * ObjectiveData.cs
 * 
 * Purpose: Defines individual objective properties and requirements
 * Used by: Mission system, objective tracking
 * 
 * Key Features:
 * - Scriptable object configuration
 * - Day/night objective settings
 * - Prerequisite system
 * - World marker integration
 * - Visual customization
 * 
 * Configuration Options:
 * - Unique identifier
 * - Description text
 * - Icon display
 * - Time restrictions
 * - Location markers
 * 
 * Dependencies:
 * - Unity ScriptableObject system
 * - UI sprite system
 * - World space marker system
 */

[CreateAssetMenu(fileName = "New Objective", menuName = "Game/Objective Data")]
public class ObjectiveData : ScriptableObject
{
    public string objectiveId;
    public string description;
    public Sprite icon;
    
    [Header("Day/Night Settings")]
    [Tooltip("If true, this objective will only appear during night time")]
    public bool isNightObjective = false;
    
    [Tooltip("Objectives that must be completed before this one can appear")]
    public ObjectiveData[] prerequisites;

    [Header("World Marker Settings")]
    public bool showWorldMarker = true;
    public string targetTag;  // Instead of Transform, use a tag or ID
    public Sprite markerIcon;
} 