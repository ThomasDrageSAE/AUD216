using UnityEngine;
using UnityEngine.Audio;

public class UIAudioManager : MonoBehaviour
{
    [System.Serializable]
    public class AudioSourceSetup
    {
        [Tooltip("AudioSource component for this category")]
        public AudioSource audioSource;
        [Tooltip("AudioMixerGroup for routing audio")]
        public AudioMixerGroup mixerGroup;
    }

    [Header("Menu Sounds")]
    [SerializeField] private AudioSourceSetup menuAudioSetup;
    [Tooltip("Sound played when opening or showing a menu panel")]
    public AudioClip menuToggleOn;
    [Tooltip("Sound played when closing or hiding a menu panel")]
    public AudioClip menuToggleOff;
    [Tooltip("Master volume for all menu-related sounds")]
    [Range(0f, 1f)] public float menuVolume = 1f;

    [Header("Button Sounds")]
    [SerializeField] private AudioSourceSetup buttonAudioSetup;
    [Tooltip("Sound played when pressing or enabling a button")]
    public AudioClip buttonToggleOn;
    [Tooltip("Sound played when releasing or disabling a button")]
    public AudioClip buttonToggleOff;
    [Tooltip("Master volume for all button interaction sounds")]
    [Range(0f, 1f)] public float buttonVolume = 0.8f;

    [Header("Notification Sounds")]
    [SerializeField] private AudioSourceSetup notificationAudioSetup;
    [Tooltip("Sound played when player completes any objective")]
    public AudioClip objectiveCompleted;
    [Tooltip("Sound played when all zombies are eliminated for the night")]
    public AudioClip allEnemiesCleared;
    [Tooltip("Master volume for all notification sounds")]
    [Range(0f, 1f)] public float notificationVolume = 1f;

    [Header("Pickup Sounds")]
    [SerializeField] private AudioSourceSetup pickupAudioSetup;
    [Tooltip("Sound played when collecting or picking up any item")]
    public AudioClip itemPickup;
    [Tooltip("Sound played when dropping an inventory item back into the world (a double-tap of its number key)")]
    public AudioClip itemDrop;
    [Tooltip("Master volume for all pickup interaction sounds")]
    [Range(0f, 1f)] public float pickupVolume = 0.8f;

    [Header("Consumption Sounds")]
    [SerializeField] private AudioSourceSetup consumptionAudioSetup;
    [Tooltip("Sound played when eating fish")]
    public AudioClip fishEaten;
    [Tooltip("Sound played when eating from a food can")]
    public AudioClip foodCanEaten;
    [Tooltip("Sound played when using medicine")]
    public AudioClip medicineUsed;
    [Tooltip("Master volume for all consumption sounds")]
    [Range(0f, 1f)] public float consumptionVolume = 0.8f;

    [Header("Day/Night Sounds")]
    [SerializeField] private AudioSourceSetup dayNightAudioSetup;
    [Tooltip("Sound played when announcing a new day")]
    public AudioClip dayNumberSound;
    [Range(0f, 1f)] public float dayAnnouncementVolume = 1f;

    [Header("Combat Feedback Sounds")]
    // Deliberately no AudioSourceSetup of its own. A new one would have its AudioSource
    // created at Awake with no mixer group wired, which routes it straight to Master where
    // the UI fader cannot reach it - 30 sources in this scene already do that. These are
    // notifications to the player, so they ride the notification setup and its UI bus.
    [Tooltip("The player's own confirmation that a round landed. 2D, at the player - the " +
             "zombie's headshot sound stays 3D, out at the zombie")]
    public AudioClip hitMarker;
    [Tooltip("The player's confirmation that the round landed in the head band. A different " +
             "sound from the hitmarker, not a louder one - it is the reward")]
    public AudioClip headshotPing;
    [Tooltip("Master volume for the hitmarker and headshot confirmations")]
    [Range(0f, 1f)] public float combatFeedbackVolume = 0.8f;

    [Header("Level Up Sounds")]
    [SerializeField] private AudioSourceSetup levelUpAudioSetup;
    [Tooltip("Sound played when player levels up")]
    public AudioClip levelUpSound;
    [Range(0f, 1f)] public float levelUpVolume = 1f;

    private static UIAudioManager instance;
    public static UIAudioManager Instance { get { return instance; } }

    [Header("Diegetic Sounds")]
    [Tooltip("Routing for in-world sounds played through PlayOneShot (currently the fishing " +
             "cast and catch). Leave the mixer group empty to fall back to the UI button bus")]
    [SerializeField] private AudioSourceSetup diegeticAudioSetup;

    // Whether the diegetic setup had a scene-wired AudioSource before Awake
    // auto-created one; wired sources keep their authored spatialisation.
    private bool diegeticSourceWired;

    /// <summary>
    /// Plays an in-world one-shot. These used to go through the button setup on the UI
    /// mixer bus, so the fishing sounds moved whenever the UI bus was mixed. The setup
    /// fixes the ROUTING; true spatialisation additionally needs a positioned
    /// AudioSource wired into the setup (auto-created ones sit on the manager and stay
    /// 2D, which is the safe default at that position).
    /// </summary>
    public void PlayOneShot(AudioClip clip, float volume = 1f)
    {
        // "Configured" means someone actually authored the diegetic path - a wired
        // source or a wired mixer group. Otherwise honour the tooltip and fall back to
        // the button bus rather than routing to Master through an unconfigured setup.
        bool diegeticConfigured = diegeticAudioSetup != null &&
            (diegeticSourceWired || diegeticAudioSetup.mixerGroup != null);

        PlaySound(diegeticConfigured ? diegeticAudioSetup : buttonAudioSetup, clip, volume);
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            // Single-scene game: deliberately NOT DontDestroyOnLoad. Persisting across
            // a reload of the same scene left this instance alive holding references to
            // destroyed per-scene objects while the fresh copy destroyed itself, which
            // made restarting unplayable. The Instance guard still prevents duplicates.
            InitializeAudioSources();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeAudioSources()
    {
        diegeticSourceWired = diegeticAudioSetup != null && diegeticAudioSetup.audioSource != null;

        InitializeAudioSourceSetup(menuAudioSetup);
        InitializeAudioSourceSetup(buttonAudioSetup);
        InitializeAudioSourceSetup(notificationAudioSetup);
        InitializeAudioSourceSetup(pickupAudioSetup);
        InitializeAudioSourceSetup(consumptionAudioSetup);
        InitializeAudioSourceSetup(dayNightAudioSetup);
        InitializeAudioSourceSetup(levelUpAudioSetup);
        InitializeAudioSourceSetup(diegeticAudioSetup);
    }

    private void InitializeAudioSourceSetup(AudioSourceSetup setup)
    {
        if (setup == null) return;
        
        bool sourceWasWired = setup.audioSource != null;
        if (!sourceWasWired)
        {
            setup.audioSource = gameObject.AddComponent<AudioSource>();
        }

        setup.audioSource.playOnAwake = false;

        // Auto-created sources live on this manager, so 2D is the only sane blend for
        // them. A scene-wired source keeps whatever spatialisation its author set -
        // stomping it to 2D here is what kept the diegetic sounds flat.
        if (!sourceWasWired)
        {
            setup.audioSource.spatialBlend = 0f;
        }

        if (setup.mixerGroup != null)
        {
            setup.audioSource.outputAudioMixerGroup = setup.mixerGroup;
        }
    }

    private void PlaySound(AudioSourceSetup setup, AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip != null && setup?.audioSource != null)
        {
            setup.audioSource.PlayOneShot(clip, volumeMultiplier);
            AudioEventLog.Record(AudioEventKind.Play, "UIAudioManager", clip.name);
        }
    }

    // Menu toggle sounds
    public void PlayMenuToggle(bool isOpening)
    {
        AudioClip clipToPlay = isOpening ? menuToggleOn : menuToggleOff;
        PlaySound(menuAudioSetup, clipToPlay, menuVolume);
    }

    // Button toggle sounds
    public void PlayButtonToggle(bool isTogglingOn)
    {
        AudioClip clipToPlay = isTogglingOn ? buttonToggleOn : buttonToggleOff;
        PlaySound(buttonAudioSetup, clipToPlay, buttonVolume);
    }

    // Notification sounds
    public void PlayObjectiveCompleted()
    {
        PlaySound(notificationAudioSetup, objectiveCompleted, notificationVolume);
    }

    public void PlayAllEnemiesCleared()
    {
        PlaySound(notificationAudioSetup, allEnemiesCleared, notificationVolume);
    }

    // Pickup sound
    public void PlayPickupSound()
    {
        PlaySound(pickupAudioSetup, itemPickup, pickupVolume);
    }

    public void PlayDropSound()
    {
        PlaySound(pickupAudioSetup, itemDrop, pickupVolume);
    }

    // Consumption sounds
    public void PlayFishEatenSound()
    {
        PlaySound(consumptionAudioSetup, fishEaten, consumptionVolume);
    }

    public void PlayFoodCanEatenSound()
    {
        PlaySound(consumptionAudioSetup, foodCanEaten, consumptionVolume);
    }

    public void PlayMedicineUsedSound()
    {
        PlaySound(consumptionAudioSetup, medicineUsed, consumptionVolume);
    }

    public void PlayDayNumberSound()
    {
        PlaySound(dayNightAudioSetup, dayNumberSound, dayAnnouncementVolume);
    }

    public void PlayLevelUpSound()
    {
        PlaySound(levelUpAudioSetup, levelUpSound, levelUpVolume);
    }

    // Combat feedback. Called by CombatFeedback, once per landed hit; PlaySound already
    // returns on an empty slot, so an unauthored clip is silent rather than an error.
    public void PlayHitMarker()
    {
        PlaySound(notificationAudioSetup, hitMarker, combatFeedbackVolume);
    }

    public void PlayHeadshotPing()
    {
        PlaySound(notificationAudioSetup, headshotPing, combatFeedbackVolume);
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        if (menuAudioSetup == null || buttonAudioSetup == null || notificationAudioSetup == null || pickupAudioSetup == null || consumptionAudioSetup == null || dayNightAudioSetup == null || levelUpAudioSetup == null) return;
        
        ValidateAudioSetup(menuAudioSetup, "Menu");
        ValidateAudioSetup(buttonAudioSetup, "Button");
        ValidateAudioSetup(notificationAudioSetup, "Notification");
        ValidateAudioSetup(pickupAudioSetup, "Pickup");
        ValidateAudioSetup(consumptionAudioSetup, "Consumption");
        ValidateAudioSetup(dayNightAudioSetup, "Day/Night");
        ValidateAudioSetup(levelUpAudioSetup, "Level Up");
    }

    private void ValidateAudioSetup(AudioSourceSetup setup, string categoryName)
    {
        if (setup == null) return;
        
        if (setup.audioSource == null)
            Debug.LogWarning($"UIAudioManager: {categoryName} AudioSource is missing!");
        if (setup.mixerGroup == null)
            Debug.LogWarning($"UIAudioManager: {categoryName} AudioMixerGroup is missing!");
    }
    #endif
} 