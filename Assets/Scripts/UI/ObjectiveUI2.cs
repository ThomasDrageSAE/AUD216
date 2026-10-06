using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/*
 * ObjectiveUI2.cs
 * 
 * Purpose: Manages individual objective UI elements and their animations
 * Used by: Mission system, objective tracking, quest UI
 * 
 * Key Features:
 * - Animated objective transitions with configurable states
 * - Dynamic status indication (active/inactive)
 * - Smooth completion animations
 * - Configurable display duration and timing
 * - Automatic cleanup after completion
 * 
 * Performance Considerations:
 * - Uses Unity's Animator system for efficient state management
 * - Coroutine-based timing for smooth transitions
 * - Smart cleanup of completed objectives
 * - Efficient component caching
 * 
 * Dependencies:
 * - TextMeshPro for text rendering
 * - Unity Animation system
 * - Requires configured animator controller with states:
 *   - In
 *   - Out
 *   - Active
 *   - Inactive
 * - ObjectiveData for initialization
 */

public class ObjectiveUI2 : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private Animator animator;

    // Animation state names from your animator
    private static readonly int STATE_IN = Animator.StringToHash("In");
    private static readonly int STATE_OUT = Animator.StringToHash("Out");
    private static readonly int STATE_ACTIVE = Animator.StringToHash("Active");
    private static readonly int STATE_INACTIVE = Animator.StringToHash("Inactive");

    private void Start()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    public void Initialize(ObjectiveData objectiveData)
    {
        if (descriptionText != null)
        {
            descriptionText.text = objectiveData.description;
        }

        if (iconImage != null && objectiveData.icon != null)
        {
            iconImage.sprite = objectiveData.icon;
            iconImage.enabled = true;
        }

        // Play the entrance animation
        PlayAnimation(STATE_IN);
    }

    public void PlayCompletionAnimation()
    {
        // Play the inactive animation when completed
        PlayAnimation(STATE_INACTIVE);
        StartCoroutine(RemoveAfterDelay());
    }

    private IEnumerator RemoveAfterDelay()
    {
        // Wait for inactive animation to finish
        yield return new WaitForSeconds(1f);
        
        // Play the out animation
        PlayAnimation(STATE_OUT);
        
        // Wait for out animation to finish before destroying
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    private void PlayAnimation(int stateHash)
    {
        if (animator != null)
        {
            animator.Play(stateHash);
        }
        else
        {
            Debug.LogWarning("No Animator found on ObjectiveUI");
        }
    }

    public void SetActive(bool active)
    {
        PlayAnimation(active ? STATE_ACTIVE : STATE_INACTIVE);
    }
} 