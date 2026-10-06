using UnityEngine;

public class AmbientFadeTrigger : MonoBehaviour
{
    // Define an enumeration for fade directions
    public enum FadeDirection { FadeIn, FadeOut }

    // Public field to select fade direction in the Inspector
    public FadeDirection fadeDirection = FadeDirection.FadeIn;

    // Reference to the AmbientLightController script
    private AmbientLightController ambientLightController;

    void Start()
    {
        // Find the AmbientLightController in the scene
        ambientLightController = FindFirstObjectByType<AmbientLightController>();
        if (ambientLightController == null)
        {
            Debug.LogError("AmbientLightController not found in the scene. Please ensure it is attached to a GameObject.");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Check if the colliding object is the player
        if (other.CompareTag("Player") && ambientLightController != null)
        {
            // Execute fade based on the selected direction
            if (fadeDirection == FadeDirection.FadeIn)
            {
                ambientLightController.FadeToDimmed();
            }
            else if (fadeDirection == FadeDirection.FadeOut)
            {
                ambientLightController.FadeToOriginal();
            }
        }
    }
}
