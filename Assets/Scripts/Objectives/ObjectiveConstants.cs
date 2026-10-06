using UnityEngine;

/*
 * ObjectiveConstants.cs
 * 
 * Purpose: Centralizes objective identifiers for consistent reference
 * Used by: Objective system, mission management
 * 
 * Key Features:
 * - Constant string definitions
 * - Shared objective identifiers
 * - Type-safe objective references
 * 
 * Usage:
 * - Day/night objective identification
 * - Mission system integration
 * - Event system references
 * 
 * Dependencies:
 * - Used by ObjectiveManager
 * - Used by DailyObjectiveManager
 * - Mission system integration
 */

public static class ObjectiveConstants
{
    // Day Objectives
    public const string PICKUP_FISHING_ROD = "PickupFishingRod";
    public const string CATCH_FISH = "CatchAFish";
    public const string PICKUP_AXE = "PickupAxe";
    public const string LIGHT_CAMPFIRE = "LightCampfire";

    // Studio power / equipment
    public const string FIX_STUDIO_POWER = "FixStudioPower";
    public const string TURN_ON_LIGHTS = "TurnOnLights";
    public const string TEST_MIXING_DESK = "TestMixingDesk";

    // Counter-based: completed once the required number of firesticks are lit
    public const string LIGHT_FIRESTICKS_SWAMP = "LightFiresticks";
    public const string LIGHT_FIRESTICKS_GRASSLANDS = "LightFiresticksGrasslands";

    // Counter-based: completed once the required number of fish are caught
    public const string CATCH_TWO_FISH = "CatchTwoFishes";
    public const string CATCH_THREE_FISH = "CatchThreeFishes";
    public const string CATCH_FOUR_FISH = "CatchFourFishes";
    public const string CATCH_FIVE_FISH = "CatchFiveFishes";

    // Day 3 engagement objectives (2026-09-02)
    public const string COOK_A_FISH = "CookAFish";
    public const string MAKE_WELL_OFFERING = "MakeWellOffering";

    // Night Objectives
    public const string ZOMBIE_DEFENCE = "ZombieDefence";
} 