using UnityEngine;
using System;

/*
 * EventManager.cs
 * 
 * Purpose: Provides centralized event management and communication
 * Used by: Game systems requiring event-based communication
 * 
 * Key Features:
 * - Type-safe event subscription
 * - Global event broadcasting
 * - Event cleanup handling
 * - Singleton pattern
 * 
 * Event Types:
 * - Game state events
 * - Player actions
 * - System notifications
 * - Scene transitions
 * 
 * Performance Considerations:
 * - Efficient event dispatch
 * - Automatic event cleanup
 * - Memory leak prevention
 * - Smart delegate management
 * 
 * Dependencies:
 * - None (core system)
 * - Used by multiple game systems
 */

public class EventManager : MonoBehaviour
{
    // Singleton instance
    public static EventManager Instance;

    private void Awake()
    {
        // Singleton pattern implementation
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject); // Ensure only one instance exists
            return;
        }
    }

    private ActionHandler actionHandler;

    private void Start()
    {
        actionHandler = FindFirstObjectByType<ActionHandler>();
    }

    /// <summary>
    /// Routes an interaction to the ActionHandler. (TriggerAction used to exist as a
    /// byte-identical duplicate of this with no callers and no scene wiring.)
    /// </summary>
    public void PerformAction(string actionText, GameObject objectToInteractWith, GameObject switchObject)
    {
        // Cached at Start; re-find once if the scene was reloaded under us.
        if (actionHandler == null)
        {
            actionHandler = FindFirstObjectByType<ActionHandler>();
        }

        if (actionHandler == null)
        {
            Debug.LogError("ActionHandler not found in the scene.");
            return;
        }

        actionHandler.HandleAction(actionText, objectToInteractWith, switchObject);
    }

    // Modified ShowInteractionPrompt method to include duration
    public void ShowInteractionPrompt(string promptText, float duration = 0f)
    {
        if (UIManager.Instance == null)
        {
            Debug.LogWarning("[EventManager] No UIManager; interaction prompt suppressed.", this);
            return;
        }

        UIManager.Instance.DisplayInteractionPrompt(promptText);

        // If duration is specified, hide the prompt after that duration
        if (duration > 0)
        {
            CancelInvoke(nameof(HideInteractionPrompt));
            Invoke(nameof(HideInteractionPrompt), duration);
        }
    }

    public void HideInteractionPrompt()
    {
        if (UIManager.Instance == null) return;

        UIManager.Instance.HidePrompt();
    }

    // Add this new method
    public void ShowFishingPrompt(string promptText)
    {
        if (UIManager.Instance == null)
        {
            Debug.LogWarning("[EventManager] No UIManager; fishing prompt suppressed.", this);
            return;
        }

        UIManager.Instance.DisplayFishingPrompt(promptText);
    }
}
