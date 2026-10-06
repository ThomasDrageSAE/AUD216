using UnityEngine;
using System.Collections;

/*
 * TitleAnimationController.cs
 * 
 * Purpose: Controls title sequence animations and timing
 * Used by: Game intro, title screen
 * 
 * Key Features:
 * - Timed animation sequence
 * - Object activation control
 * - Configurable display duration
 * - Animation parameter management
 * 
 * Performance Considerations:
 * - Efficient coroutine usage
 * - Clean animation handling
 * - Smart state management
 * 
 * Dependencies:
 * - Requires Animator component
 * - Animation system setup
 * - Optional object references
 */

public class TitleAnimationController : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private Animator titleAnimator;
    [SerializeField] private string parameter = "Book";
    [SerializeField] private float displayDuration = 4f;
    
    [Header("Objects To Enable")]
    [SerializeField] private GameObject firstObjectToEnable;
    [SerializeField] private GameObject secondObjectToEnable;

    private void Start()
    {
        if (firstObjectToEnable != null)
        {
            firstObjectToEnable.SetActive(true);
        }

        if (secondObjectToEnable != null)
        {
            secondObjectToEnable.SetActive(true);
        }
        
        StartCoroutine(PlayTitleSequence());
    }

    private IEnumerator PlayTitleSequence()
    {
        // Start the animation
        if (titleAnimator != null)
        {
            titleAnimator.SetBool(parameter, true);
        }

        // Wait for the specified duration
        yield return new WaitForSeconds(displayDuration);

        // Toggle the animation off
        if (titleAnimator != null)
        {
            titleAnimator.SetBool(parameter, false);
        }
    }
} 