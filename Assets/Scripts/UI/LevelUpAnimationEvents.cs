using UnityEngine;

/*
 * LevelUpAnimationEvents.cs
 * 
 * Purpose: Bridges animation system with audio feedback for level-up sequences
 * Used by: Character progression system, animation events
 * 
 * Key Features:
 * - Animation-synchronized audio playback
 * - Centralized sound effect management
 * - Clean separation of animation and audio concerns
 * 
 * Performance Considerations:
 * - Lightweight event handling
 * - No update overhead
 * - Single responsibility focus
 * 
 * Dependencies:
 * - UIAudioManager for sound playback
 * - Animation system integration
 * - Requires configured animation events
 */
public class LevelUpAnimationEvents : MonoBehaviour
{
    public void PlayLevelUpSound()
    {
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayLevelUpSound();
        }
    }
} 