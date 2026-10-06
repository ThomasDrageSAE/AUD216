using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class RandomSoundClip
{
    public AudioClip clip;

    [Tooltip("Volume range for the sound clip. Min and max values between 0.0 and 1.0.")]
    [RangeSlider(0.0f, 1.0f)]
    public RangeFloat volumeRange = new RangeFloat(0.8f, 1.0f);

    [Tooltip("Pitch range for the sound clip. Min and max values typically between 0.8 and 1.2.")]
    [RangeSlider(0.8f, 1.2f)]
    public RangeFloat pitchRange = new RangeFloat(0.95f, 1.05f);
}

public class AudioRandomizer : MonoBehaviour
{
    [Header("Sound Settings")]
    [Tooltip("List of sound clips with their respective volume and pitch ranges.")]
    public List<RandomSoundClip> sounds = new List<RandomSoundClip>();

    [Tooltip("Interval range in seconds between sound plays. Min and max values.")]
    [RangeSlider(0, 60)]
    public RangeFloat intervalRange = new RangeFloat(1, 30);

    [Header("Area Settings")]
    [Tooltip("Maximum distance in meters from the player at which the sounds can be heard.")]
    public float maxDistance = 50f;

    [Header("Routing")]
    [Tooltip("Mixer group for the source added at runtime. Left empty the source plays on Master, so it ignores the SFX fader and the volume slider.")]
    [SerializeField] private AudioMixerGroup output;

    private AudioSource audioSource;

    private void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f;  // Make sound 3D
        if (output != null) audioSource.outputAudioMixerGroup = output;
        StartCoroutine(PlayRandomSounds());
    }

    private IEnumerator PlayRandomSounds()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(intervalRange.min, intervalRange.max));

            // No main camera this interval (scene loading, camera disabled): skip the
            // interval rather than throw on Camera.main.transform.
            Camera mainCamera = Camera.main;
            if (mainCamera == null) continue;

            if (Vector3.Distance(mainCamera.transform.position, transform.position) <= maxDistance)
            {
                PlaySound();
            }
        }
    }

    private void PlaySound()
    {
        if (sounds.Count == 0) return;

        RandomSoundClip soundClip = sounds[Random.Range(0, sounds.Count)];
        audioSource.clip = soundClip.clip;
        audioSource.volume = Random.Range(soundClip.volumeRange.min, soundClip.volumeRange.max);
        audioSource.pitch = Random.Range(soundClip.pitchRange.min, soundClip.pitchRange.max);
        audioSource.Play();
    }
}
