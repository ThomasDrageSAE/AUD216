using UnityEngine;
using System.Collections;

public class TriggerLightIntensity : MonoBehaviour
{
    [Header("Light Settings")]
    [SerializeField] private Light targetLight;
    #pragma warning disable 0414
    [SerializeField] private float startIntensity = 0f;
    #pragma warning restore 0414
    [SerializeField] private float endIntensity = 0.3f;
    [SerializeField] private float transitionDuration = 1f;
    
    [Header("Ambient Light Settings")]
    [SerializeField] private bool modifyAmbientLight = false;
    [SerializeField] private DayNightCycle dayNightCycle;
    [SerializeField] private float newAmbientNightIntensity = 0.2f;
    
    [Header("Trigger Settings")]
    [SerializeField] private bool triggerOnce = true;
    [SerializeField] private string playerTag = "Player";

    private bool hasTriggered = false;
    private Coroutine fadeCoroutine;

    private void Start()
    {
        if (targetLight == null)
        {
            Debug.LogError($"No light assigned to TriggerLightIntensity on {gameObject.name}");
            enabled = false;
            return;
        }

        if (modifyAmbientLight && dayNightCycle == null)
        {
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();
            if (dayNightCycle == null)
            {
                Debug.LogError($"No DayNightCycle found but modifyAmbientLight is enabled on {gameObject.name}");
                modifyAmbientLight = false;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            if (triggerOnce && hasTriggered) return;

            // Stop any existing fade
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
            }

            // Start new fade from current intensity to end intensity
            fadeCoroutine = StartCoroutine(FadeIntensity(targetLight.intensity, endIntensity));
            
            // Update ambient light immediately if enabled
            if (modifyAmbientLight && dayNightCycle != null)
            {
                dayNightCycle.SetAmbientNightIntensity(newAmbientNightIntensity);
            }
            
            hasTriggered = true;
        }
    }

    private IEnumerator FadeIntensity(float currentIntensity, float targetIntensity)
    {
        float elapsedTime = 0f;

        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / transitionDuration;
            
            // Use smoothstep for more natural transition
            t = t * t * (3f - 2f * t);
            
            targetLight.intensity = Mathf.Lerp(currentIntensity, targetIntensity, t);
            yield return null;
        }

        targetLight.intensity = targetIntensity;
        fadeCoroutine = null;
    }

    // Optional: Method to reset the trigger
    public void ResetTrigger()
    {
        hasTriggered = false;
    }

    // Optional: Visual debug in editor
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
    }
} 