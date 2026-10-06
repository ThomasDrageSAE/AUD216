using UnityEngine;

public class TimedActivator : MonoBehaviour
{
    [Header("Activation Settings")]
    [SerializeField] private GameObject targetObject;
    [SerializeField] private int activationDay = 1;
    [SerializeField, Range(0f, 1f)] private float activationTimeOfDay = 0.5f;

    [Header("Dependencies")]
    [SerializeField] private DayNightCycle dayNightCycle;

    private bool isActivated = false;

    private void Start()
    {
        if (dayNightCycle == null)
        {
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();
            if (dayNightCycle == null)
            {
                Debug.LogError("No DayNightCycle found in the scene!");
                return;
            }
        }

        if (targetObject != null)
        {
            targetObject.SetActive(false); // Ensure the object starts inactive
        }
    }

    private void Update()
    {
        if (isActivated || targetObject == null || dayNightCycle == null) return;

        int currentDay = dayNightCycle.GetCurrentDay();
        float currentTime = dayNightCycle.GetTimeOfDay();

        if (currentDay >= activationDay && currentTime >= activationTimeOfDay)
        {
            targetObject.SetActive(true);
            isActivated = true;
        }
    }
} 