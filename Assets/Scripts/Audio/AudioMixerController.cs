using UnityEngine;
using UnityEngine.Audio;

/*
 * AudioMixerController.cs
 * 
 * Purpose: Manages audio mixer volume levels and parameter control.
 * Used by: All audio systems, UI volume controls
 * 
 * Provides centralized control over audio mixer groups including master,
 * music, ambience, SFX, and UI sounds. Handles logarithmic volume conversion
 * for natural-feeling volume control.
 * 
 * Performance Considerations:
 * - Caches mixer references
 * - Uses efficient parameter setting
 * - Minimizes redundant mixer updates
 * 
 * Dependencies:
 * - Requires properly configured AudioMixer asset
 * - UI volume control system
 * - Persistent audio settings
 */

public class AudioMixerController : MonoBehaviour
{
    [SerializeField] private AudioMixer mainMixer;
    
    // Mixer group volume parameters
    private const string MASTER_VOL = "MasterVolume";
    private const string MUSIC_VOL = "MusicVolume";
    private const string AMBIENCE_VOL = "AmbienceVolume";
    private const string SFX_VOL = "SFXVolume";
    private const string UI_VOL = "UIVolume";
    
    public void SetMasterVolume(float volume)
    {
        SetVolume(MASTER_VOL, volume);
    }
    
    public void SetMusicVolume(float volume)
    {
        SetVolume(MUSIC_VOL, volume);
    }
    
    public void SetAmbienceVolume(float volume)
    {
        SetVolume(AMBIENCE_VOL, volume);
    }
    
    public void SetSFXVolume(float volume)
    {
        SetVolume(SFX_VOL, volume);
    }
    
    public void SetUIVolume(float volume)
    {
        SetVolume(UI_VOL, volume);
    }
    
    private void SetVolume(string parameterName, float volume)
    {
        if (mainMixer == null) return;

        float mixerVolume = volume <= 0 ? -80f : Mathf.Log10(volume) * 20f;

        // AudioMixer.SetFloat fails silently for a parameter that is not exposed, and
        // "MasterVolume" is NOT exposed on this project's mixer - only the eight bus
        // volumes are (Ambience/Enemies/Footsteps/Impacts/Music/SFX/UI/Weapons). Say so
        // rather than leaving a caller wondering why the slider does nothing.
        if (!mainMixer.SetFloat(parameterName, mixerVolume))
        {
            Debug.LogWarning($"[AudioMixerController] '{parameterName}' is not an exposed " +
                             "parameter on the mixer, so this volume change did nothing. " +
                             "Expose it on the mixer group, or use one of the eight bus " +
                             "parameters.", this);
        }
    }
} 