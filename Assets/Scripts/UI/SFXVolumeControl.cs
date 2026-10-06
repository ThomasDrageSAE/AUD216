using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/*
 * SFXVolumeControl.cs
 * 
 * Purpose: Controls sound effect volume through UI
 * Used by: Audio settings, volume management
 * 
 * Key Features:
 * - Real-time volume adjustment
 * - Decibel conversion handling
 * - Slider-based control
 * - Initial state restoration
 * 
 * Performance Considerations:
 * - Efficient volume calculations
 * - Event-driven updates
 * - Smart parameter caching
 * 
 * Dependencies:
 * - AudioMixer system
 * - UI Slider component
 * - Requires proper mixer setup
 */
public class SFXVolumeControl : MonoBehaviour
{
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string sfxVolumeParameter = "SFXVolume";
    [SerializeField] private Slider sfxVolumeSlider;

    private const float MIN_DB = -80f;
    private const string PrefKeyPrefix = "AudioVolume_";

    // One key per exposed mixer parameter, so the eight controls persist independently.
    private string PrefKey => PrefKeyPrefix + sfxVolumeParameter;

    private void Start()
    {
        if (audioMixer == null || sfxVolumeSlider == null)
        {
            Debug.LogError("SFXVolumeControl: Missing required references!");
            return;
        }

        // A saved value wins over the mixer asset's serialized default, so a mix
        // survives quitting the game. Applied in Start, not Awake: mixer writes made
        // during Awake are clobbered by the mixer's own snapshot initialisation.
        if (PlayerPrefs.HasKey(PrefKey))
        {
            float saved = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKey));
            sfxVolumeSlider.SetValueWithoutNotify(saved);
            SetSFXVolume(saved);
        }
        else if (audioMixer.GetFloat(sfxVolumeParameter, out float currentVolume))
        {
            // Convert from decibels back to slider value (0-1)
            sfxVolumeSlider.SetValueWithoutNotify(Mathf.Pow(10f, currentVolume / 20f));
        }

        // SetValueWithoutNotify above is deliberate: each slider ALSO carries a
        // serialized onValueChanged call to this method, so a notifying set would
        // write the mixer twice before this listener is even attached.
        sfxVolumeSlider.onValueChanged.AddListener(SetSFXVolume);
    }

    public void SetSFXVolume(float sliderValue)
    {
        if (audioMixer == null) return;

        // Convert slider value (0-1) to decibels, protecting against log10(0)
        float dbValue = sliderValue <= 0f
            ? MIN_DB
            : Mathf.Max(Mathf.Log10(sliderValue) * 20f, MIN_DB);

        audioMixer.SetFloat(sfxVolumeParameter, dbValue);

        PlayerPrefs.SetFloat(PrefKey, Mathf.Clamp01(sliderValue));
    }

    private void OnDisable()
    {
        // Flush on the way out; PlayerPrefs otherwise only writes on a clean quit.
        PlayerPrefs.Save();
    }

    private void OnValidate()
    {
        if (audioMixer == null)
            Debug.LogWarning("SFXVolumeControl: AudioMixer reference is missing!");
        if (sfxVolumeSlider == null)
            Debug.LogWarning("SFXVolumeControl: Slider reference is missing!");
    }
}
