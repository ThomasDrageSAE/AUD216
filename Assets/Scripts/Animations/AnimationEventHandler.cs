using UnityEngine;
using System.Collections;
using TMPro;

public class AnimationEventHandler : MonoBehaviour
{
    public static AnimationEventHandler Instance { get; private set; }

    public enum AnnouncementType
    {
        DayNumber,
        EnemiesCleared,
        PlayerDeath
    }

    [Header("References")]
    [SerializeField] private GameObject dayNumberUI; // Reference to the Day Number UI GameObject
    [SerializeField] private GameObject enemiesClearedUI; // Reference to the Enemies Cleared UI GameObject
    [SerializeField] private WaveManager waveManager;
    [SerializeField] private DayNightCycle dayNightCycle;
    [SerializeField] private TextMeshProUGUI dayNumberText;
    [SerializeField] private TextMeshProUGUI enemiesClearedText;

    [Header("Timing Settings")]
    [SerializeField] private float dayAnnouncementTime = 0.26f;

    private bool isDayAnnouncementPlaying = false;
    private int currentDay = 1;
    private bool hasDayBeenAnnounced = false;
    private bool hasIncrementedDay = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Single-scene game: deliberately NOT DontDestroyOnLoad (see ObjectiveManager).
            // (The old SetParent(null) here existed only for DDoL's root requirement.)
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        if (waveManager == null)
        {
            waveManager = FindFirstObjectByType<WaveManager>();
        }

        if (dayNightCycle == null)
        {
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        }

        if (dayNightCycle == null)
        {
            Debug.LogWarning("AnimationEventHandler: no DayNightCycle in scene - day/night announcements disabled.", this);
            return;
        }

        // DayNightCycle owns the day number. This class used to run a second,
        // independently incremented counter, which drifted from the first whenever
        // time was set directly (wave clear, objective triggers, day-skip cheats).
        currentDay = dayNightCycle.GetCurrentDay();
        dayNightCycle.OnDayChanged += HandleDayChanged;

        float timeOfDay = dayNightCycle.GetTimeOfDay();
        float dawnStart = dayNightCycle.GetDawnStartTime();

        // If we start between dawn start and announcement time, set up for the announcement
        if (timeOfDay >= dawnStart && timeOfDay < dayAnnouncementTime)
        {
            hasIncrementedDay = true;
            hasDayBeenAnnounced = false;
        }
        // If we start after announcement time but before night, mark day 1 as announced
        else if (timeOfDay >= dayAnnouncementTime && timeOfDay < dayNightCycle.GetNightTime())
        {
            hasDayBeenAnnounced = true;
        }
    }

    private void OnDestroy()
    {
        if (dayNightCycle != null)
        {
            dayNightCycle.OnDayChanged -= HandleDayChanged;
        }
    }

    private void HandleDayChanged(int newDay)
    {
        currentDay = newDay;
        hasIncrementedDay = true;
        hasDayBeenAnnounced = false;
    }

    private void Update()
    {
        if (dayNightCycle == null || isDayAnnouncementPlaying) return;

        // Announce the day once time reaches the announcement point, provided a day
        // change is pending.
        if (dayNightCycle.GetTimeOfDay() >= dayAnnouncementTime && !hasDayBeenAnnounced && hasIncrementedDay)
        {
            hasDayBeenAnnounced = true;
            hasIncrementedDay = false;
            ShowDayAnnouncement();
        }
    }

    private void ShowDayAnnouncement()
    {
        if (isDayAnnouncementPlaying) return;

        if (dayNumberText != null)
        {
            dayNumberText.text = $"DAY {currentDay}";
        }
        
        if (dayNumberUI != null)
        {
            dayNumberUI.SetActive(true);
            if (enemiesClearedUI != null)
            {
                enemiesClearedUI.SetActive(false);
            }
        }
        isDayAnnouncementPlaying = true;
        StartCoroutine(PlayWithDelays(AnnouncementType.DayNumber));
    }

    /// <summary>
    /// Public method to force show the day announcement (for testing/debugging)
    /// </summary>
    public void ShowDayAnnouncementNow()
    {
        ShowDayAnnouncement();
    }

    public void ShowEnemiesClearedAnnouncement()
    {
        if (enemiesClearedText != null)
        {
            enemiesClearedText.text = "ENEMIES CLEARED!";
        }
        
        if (enemiesClearedUI != null)
        {
            enemiesClearedUI.SetActive(true);
            dayNumberUI?.SetActive(false); // Ensure other UI is hidden
        }
        isDayAnnouncementPlaying = true;
        StartCoroutine(PlayWithDelays(AnnouncementType.EnemiesCleared));
    }

    /// <summary>
    /// Shows a custom announcement message using the enemies cleared UI
    /// </summary>
    /// <param name="message">The custom message to display</param>
    public void ShowCustomAnnouncement(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return; // Don't show empty messages
        }

        if (enemiesClearedText != null)
        {
            enemiesClearedText.text = message;
        }
        
        if (enemiesClearedUI != null)
        {
            enemiesClearedUI.SetActive(true);
            dayNumberUI?.SetActive(false); // Ensure other UI is hidden
        }
        isDayAnnouncementPlaying = true;
        StartCoroutine(PlayWithDelays(AnnouncementType.EnemiesCleared));
    }

    private IEnumerator PlayWithDelays(AnnouncementType type)
    {
        GameObject targetUI = type == AnnouncementType.DayNumber ? dayNumberUI : enemiesClearedUI;
        if (targetUI != null)
        {
            Animator animator = targetUI.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                // Play the "In" animation state which we can see exists in the Animator Controller
                animator.Play("In", 0);
                
                // Optional debug log to confirm the animation is playing
   //             Debug.Log($"Playing 'In' animation on {targetUI.name}");
            }
            else
            {
                Debug.LogError($"Missing Animator or AnimatorController on {targetUI.name}");
            }
        }
        else
        {
            Debug.LogError($"Target UI is null for announcement type: {type}");
        }
        yield break;
    }

    public void OnAnimationComplete(AnnouncementType type)
    {
        StartCoroutine(CompleteWithDelay(type));
    }

    private IEnumerator CompleteWithDelay(AnnouncementType type)
    {
        if (type == AnnouncementType.DayNumber)
        {
            isDayAnnouncementPlaying = false;
            dayNumberUI?.SetActive(false);
        }
        else
        {
            isDayAnnouncementPlaying = false;
            enemiesClearedUI?.SetActive(false);
        }
        yield break;
    }

    public void PlayClearedSound()
    {
        waveManager?.PlayClearedSound();
    }

    public void PlayDayNumberSound()
    {
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayDayNumberSound();
        }
    }

    public void TriggerAnimation()
    {
        if (!isDayAnnouncementPlaying)
        {
            if (dayNumberUI != null)
            {
                dayNumberUI.SetActive(true);
            }
            isDayAnnouncementPlaying = true;
            StartCoroutine(PlayWithDelays(AnnouncementType.DayNumber));
        }
    }

    public void SkipAnimation()
    {
        if (isDayAnnouncementPlaying)
        {
            StopAllCoroutines();
            isDayAnnouncementPlaying = false;
            if (dayNumberUI != null)
            {
                dayNumberUI.SetActive(false);
            }
        }
    }

    public int GetCurrentDay()
    {
        return currentDay;
    }

    public void ShowDeathAnnouncement()
    {
        if (dayNumberText != null)
        {
            dayNumberText.text = "YOU DIED!";
        }
        
        if (dayNumberUI != null)
        {
            dayNumberUI.SetActive(true);
            enemiesClearedUI?.SetActive(false);
        }
        isDayAnnouncementPlaying = true;
        StartCoroutine(PlayWithDelays(AnnouncementType.PlayerDeath));
    }
}