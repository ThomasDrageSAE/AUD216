using UnityEngine;

/*
 * MenuManager.cs
 * 
 * Purpose: Handles menu toggling and state management
 * Used by: Game UI system, pause functionality
 * 
 * Key Features:
 * - Menu toggle with Escape key
 * - Audio feedback for menu actions
 * - Clean state management
 * 
 * Performance Considerations:
 * - Minimal update overhead
 * - Efficient audio handling
 * - Simple state tracking
 * 
 * Dependencies:
 * - UIAudioManager for sound effects
 * - Requires menu panel setup
 */
public class MenuManager : MonoBehaviour
{
    [SerializeField] private GameObject menuPanel;
    private bool isMenuOpen = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        isMenuOpen = !isMenuOpen;
        menuPanel.SetActive(isMenuOpen);
        
        // Play appropriate sound
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayMenuToggle(isMenuOpen);
        }
    }
} 