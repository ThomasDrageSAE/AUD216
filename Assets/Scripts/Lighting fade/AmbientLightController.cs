using UnityEngine;
using System.Collections;

public class AmbientLightController : MonoBehaviour
{
    [Header("Ambient Light Settings")]
    [Tooltip("Target ambient intensity when fully dimmed.")]
    public float targetAmbientIntensity = 0.2f; // Adjust as needed
    [Tooltip("Original ambient intensity when fully bright.")]
    public float originalAmbientIntensity = 1f; // Set to your initial ambient intensity
    [Tooltip("Duration of the fade in seconds.")]
    public float fadeDuration = 3f;

    [Header("Ambient Color Settings")]
    [Tooltip("Target ambient color when fully dimmed.")]
    public Color dimAmbientColor = Color.black; // Adjust as needed
    [Tooltip("Original ambient color when fully bright.")]
    public Color originalAmbientColor = Color.white; // Set to your initial ambient color

    private bool isFading = false;

    void Start()
    {
        // Initialize ambient settings
        RenderSettings.ambientIntensity = originalAmbientIntensity;
        RenderSettings.ambientLight = originalAmbientColor;
    }

    // Call this method to start fading to dimmed ambient light
    public void FadeToDimmed()
    {
        if (!isFading)
            StartCoroutine(FadeAmbient(originalAmbientIntensity, targetAmbientIntensity, originalAmbientColor, dimAmbientColor, fadeDuration));
    }

    // Call this method to fade back to original ambient light
    public void FadeToOriginal()
    {
        if (!isFading)
            StartCoroutine(FadeAmbient(targetAmbientIntensity, originalAmbientIntensity, dimAmbientColor, originalAmbientColor, fadeDuration));
    }

    IEnumerator FadeAmbient(float fromIntensity, float toIntensity, Color fromColor, Color toColor, float duration)
    {
        isFading = true;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            RenderSettings.ambientIntensity = Mathf.Lerp(fromIntensity, toIntensity, t);
            RenderSettings.ambientLight = Color.Lerp(fromColor, toColor, t);
            yield return null;
        }

        RenderSettings.ambientIntensity = toIntensity;
        RenderSettings.ambientLight = toColor;
        isFading = false;
    }
}
