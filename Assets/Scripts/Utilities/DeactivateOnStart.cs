using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * DeactivateOnStart.cs
 * 
 * Purpose: Simple utility to deactivate GameObjects on start
 * Used by: Scene setup, UI management
 * 
 * Key Features:
 * - Automatic deactivation
 * - Start-up configuration
 * - Clean initialization
 * 
 * Performance Considerations:
 * - One-time execution
 * - Minimal overhead
 * - Start-only operation
 * 
 * Dependencies:
 * - None (utility component)
 */

public class DeactivateOnStart : MonoBehaviour
{

    void Start()
    {
        gameObject.SetActive(false);
    }
}