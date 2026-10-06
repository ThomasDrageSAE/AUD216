using UnityEngine;
using UnityEngine.UI;

/*
 * UIToggleButton.cs
 * 
 * Purpose: Adds audio feedback to UI toggle interactions
 * Used by: Settings menus, option toggles
 * 
 * Key Features:
 * - Automatic audio feedback on toggle
 * - Clean event subscription handling
 * - Component validation
 * 
 * Performance Considerations:
 * - Minimal memory footprint
 * - Event-based execution only
 * - Proper event cleanup
 * 
 * Dependencies:
 * - Requires Toggle component
 * - UIAudioManager for sound effects
 * - Unity UI system
 */

[RequireComponent(typeof(Toggle))]
public class UIToggleButton : MonoBehaviour
{
    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    private void OnToggleChanged(bool isOn)
    {
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayButtonToggle(isOn);
        }
    }

    private void OnDestroy()
    {
        toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }
} 