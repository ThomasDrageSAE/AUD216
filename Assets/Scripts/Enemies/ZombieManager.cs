using UnityEngine;
using System.Collections.Generic;

/*
 * ZombieManager.cs
 * 
 * Purpose: Global manager for all zombie entities in the game
 * Used by: Individual Zombie components, MusicManager, Game systems
 * 
 * Key Features:
 * - Maintains list of all active zombies in the scene
 * - Controls music state based on zombie presence
 * - Provides zombie activity status to other systems
 * 
 * Performance Considerations:
 * - Automatically cleans up null references
 * - Uses efficient list operations for tracking
 * 
 * Dependencies:
 * - Requires MusicManager for combat state changes
 * - Individual Zombie components must register/unregister
 */

public class ZombieManager : MonoBehaviour
{
    private List<GameObject> activeZombies = new List<GameObject>();  // Tracks all active zombies
    private MusicManager musicManager;                               // Reference for music state control
   
    void Start()
    {
        musicManager = FindFirstObjectByType<MusicManager>();
    }

    /// <summary>
    /// Registers a new zombie with the manager and updates music state
    /// </summary>
    /// <param name="zombie">The zombie GameObject to register</param>
    public void RegisterZombie(GameObject zombie)
    {
        if (!activeZombies.Contains(zombie))
        {
            activeZombies.Add(zombie);
            UpdateMusicState();
        }
    }

    /// <summary>
    /// Unregisters a zombie (typically on death) and updates music state
    /// </summary>
    /// <param name="zombie">The zombie GameObject to unregister</param>
    public void UnregisterZombie(GameObject zombie)
    {
        if (activeZombies.Contains(zombie))
        {
            activeZombies.Remove(zombie);
            UpdateMusicState();
        }
    }

    /// <summary>
    /// Updates background music based on zombie presence
    /// </summary>
    private void UpdateMusicState()
    {
        // Clean up null references before checking count
        activeZombies.RemoveAll(zombie => zombie == null);
        
        if (musicManager != null)
        {
            bool hasActiveZombies = activeZombies.Count > 0;
//            Debug.Log($"[ZombieManager] Updating music state. Active zombies: {activeZombies.Count}");
            musicManager.SetFightingState(hasActiveZombies);
        }
    }

    /// <summary>
    /// Checks if any zombies are currently active in the scene
    /// <summary>
    /// Number of zombies currently alive. Drives the combat term of the adaptive music
    /// intensity, so it prunes destroyed entries before counting.
    /// </summary>
    public int GetActiveZombieCount()
    {
        activeZombies.RemoveAll(zombie => zombie == null);
        return activeZombies.Count;
    }
}
