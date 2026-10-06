using UnityEngine;
using TMPro;
using UnityEngine.UI;
using DG.Tweening;

/*
 * InteractionPromptUI.cs
 * 
 * Purpose: Manages UI prompts for player interactions
 * Used by: Interaction system, player feedback
 * 
 * Key Features:
 * - Dynamic prompt display
 * - Multi-component text management
 * - Smooth visibility transitions
 * - Debug logging support
 * 
 * Performance Considerations:
 * - Efficient UI updates
 * - Smart component caching
 * - Clean state management
 * 
 * Dependencies:
 * - TextMeshPro components
 * - Canvas system
 * - DOTween for animations
 */

public class InteractionPromptUI : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject uiContainer;
    [SerializeField] private TextMeshProUGUI objectLabel;
    [SerializeField] private TextMeshProUGUI inputKeyText;
    [SerializeField] private TextMeshProUGUI actionText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image backgroundImage;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        // Ensure UI is hidden at start
        if (uiContainer != null)
        {
            uiContainer.SetActive(false);
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    public void ShowPrompt(string objectName, string keyPrompt, string action)
    {
  //      Debug.Log($"ShowPrompt called with: {objectName}, {keyPrompt}, {action}");
        
        if (uiContainer != null)
        {
            uiContainer.SetActive(true);
        }

        if (objectLabel != null)
            objectLabel.text = objectName;
        if (inputKeyText != null)
            inputKeyText.text = keyPrompt;
        if (actionText != null)
            actionText.text = action;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
    }

    public void HidePrompt()
    {
        if (uiContainer != null)
        {
            uiContainer.SetActive(false);
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }
} 