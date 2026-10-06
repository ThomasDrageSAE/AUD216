using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine.UI;
using System.Collections;

/*
 * HUDManager.cs
 * 
 * Purpose: Controls the animation and management of HUD elements
 * Used by: Game UI system, objective displays, status indicators
 * 
 * Key Features:
 * - Animated UI element transitions
 * - Configurable animation directions and timing
 * - Object pooling for UI elements
 * - Smooth easing effects
 * 
 * Performance Considerations:
 * - Uses DOTween for efficient animations
 * - Implements object pooling for frequent UI elements
 * - Manages UI element lifecycle
 * 
 * Dependencies:
 * - DOTween for animations
 * - Unity UI system
 * - Object pooling system
 */

public class HUDManager : MonoBehaviour
{
    public enum AnimationDirection
    {
        Top,
        Bottom,
        Left,
        Right
    }

    [System.Serializable]
    public class HUDElement
    {
        public RectTransform element;
        [Tooltip("Direction from which the element will animate")]
        public AnimationDirection animationDirection = AnimationDirection.Top;
        [Tooltip("Optional delay before this element starts animating in")]
        public float animationDelay = 0f;
        [Tooltip("Should this element animate out after a delay?")]
        public bool shouldAnimateOut = false;
        [Tooltip("Time in seconds before the element animates out (if enabled)")]
        public float timeBeforeAnimateOut = 5f;

        // Runtime data
        [System.NonSerialized] public Vector2 originalPosition;
        [System.NonSerialized] public Coroutine animateOutCoroutine;
        [System.NonSerialized] public Tween currentTween;
    }

    [Header("Settings")]
    [SerializeField] private List<HUDElement> hudElements = new List<HUDElement>();
    [SerializeField] private float animationDuration = 0.5f;
    [SerializeField] private float offscreenOffset = 200f;
    [SerializeField] private Ease easeType = Ease.OutBack;

    private void Awake()
    {
        InitializeHUDElements();
    }

    private void InitializeHUDElements()
    {
        foreach (var hudElement in hudElements)
        {
            if (hudElement.element != null)
            {
                // Store original position in the element itself
                hudElement.originalPosition = hudElement.element.anchoredPosition;
                hudElement.element.gameObject.SetActive(false);
            }
        }
    }

    private void Start()
    {
        foreach (var hudElement in hudElements)
        {
            if (hudElement.element != null)
            {
                AnimateElementIn(hudElement);
            }
        }
    }

    private Vector2 GetStartPosition(HUDElement hudElement)
    {
        Vector2 startPos = hudElement.originalPosition;

        switch (hudElement.animationDirection)
        {
            case AnimationDirection.Top:
                startPos.y += offscreenOffset;
                break;
            case AnimationDirection.Bottom:
                startPos.y -= offscreenOffset;
                break;
            case AnimationDirection.Left:
                startPos.x -= offscreenOffset;
                break;
            case AnimationDirection.Right:
                startPos.x += offscreenOffset;
                break;
        }

        return startPos;
    }

    private void AnimateElementIn(HUDElement hudElement)
    {
        if (hudElement.element == null) return;

        // Kill any existing tween
        hudElement.currentTween?.Kill();
        
        Vector2 startPos = GetStartPosition(hudElement);
        hudElement.element.anchoredPosition = startPos;
        hudElement.element.gameObject.SetActive(true);

        // Store the new tween
        hudElement.currentTween = hudElement.element.DOAnchorPos(hudElement.originalPosition, animationDuration)
            .SetEase(easeType)
            .SetDelay(hudElement.animationDelay)
            .OnComplete(() => 
            {
                if (hudElement.shouldAnimateOut)
                {
                    if (hudElement.animateOutCoroutine != null)
                    {
                        StopCoroutine(hudElement.animateOutCoroutine);
                    }
                    hudElement.animateOutCoroutine = StartCoroutine(AnimateOutAfterDelay(hudElement));
                }
            });
    }

    private IEnumerator AnimateOutAfterDelay(HUDElement hudElement)
    {
        yield return new WaitForSeconds(hudElement.timeBeforeAnimateOut);
        AnimateElementOut(hudElement);
    }

    private void AnimateElementOut(HUDElement hudElement)
    {
        if (hudElement.element == null) return;

        // Kill any existing tween
        hudElement.currentTween?.Kill();

        Vector2 targetPos = GetStartPosition(hudElement);

        hudElement.currentTween = hudElement.element.DOAnchorPos(targetPos, animationDuration)
            .SetEase(easeType)
            .OnComplete(() => 
            {
                hudElement.element.gameObject.SetActive(false);
                hudElement.animateOutCoroutine = null;
            });
    }

    public void AnimateInElement(RectTransform element)
    {
        var hudElement = hudElements.Find(x => x.element == element);
        if (hudElement != null)
        {
            AnimateElementIn(hudElement);
        }
    }

    public void AnimateOutElement(RectTransform element)
    {
        var hudElement = hudElements.Find(x => x.element == element);
        if (hudElement != null)
        {
            if (hudElement.animateOutCoroutine != null)
            {
                StopCoroutine(hudElement.animateOutCoroutine);
                hudElement.animateOutCoroutine = null;
            }
            AnimateElementOut(hudElement);
        }
    }

    private void OnDestroy()
    {
        // Clean up all animations and coroutines
        foreach (var hudElement in hudElements)
        {
            if (hudElement.element != null)
            {
                hudElement.currentTween?.Kill();
                if (hudElement.animateOutCoroutine != null)
                {
                    StopCoroutine(hudElement.animateOutCoroutine);
                }
            }
        }
    }
} 