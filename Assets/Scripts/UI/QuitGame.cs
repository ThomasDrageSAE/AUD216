using UnityEngine;

/*
 * QuitGame.cs
 * 
 * Purpose: Handles application exit functionality with platform-specific behavior
 * Used by: Menu system, game exit options
 * 
 * Key Features:
 * - Platform-aware quit handling
 * - Editor and standalone build support
 * - Clean application termination
 * 
 * Performance Considerations:
 * - Minimal execution overhead
 * - One-time execution
 * 
 * Dependencies:
 * - Unity Editor namespace (editor only)
 * - Platform compilation directives
 */

public class QuitGame : MonoBehaviour
{
    public void Quit()
    {
#if UNITY_STANDALONE
        Application.Quit();
#endif

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
