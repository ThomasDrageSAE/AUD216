using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class MusicZoneManager : MonoBehaviour
{
    private AudioSource musicSource;
    public AudioClip audioClip; // Assign a single audio clip in the Inspector
    public float fadeTime = 2.0f; // Time in seconds for music to fade in/out

    private void Start()
    {
        musicSource = GetComponent<AudioSource>();

        if (musicSource == null || audioClip == null)
        {
            Debug.LogError("MusicZoneManager: AudioSource or audio clip is not assigned!");
            return;
        }

        // Initialize the AudioSource with the assigned clip and default volume and pitch
        musicSource.volume = 0.0f; // Start with the music muted
        musicSource.clip = audioClip;
        musicSource.pitch = 1.0f; // Set pitch to normal
        // Not started here: it used to Play() muted from scene load, holding a voice
        // for a zone the player may never enter. OnTriggerEnter starts it instead.
    }

    private void OnTriggerEnter(Collider other)
    {
        // Trigger messages arrive even while this component is disabled, so the
        // missing-AudioSource case Start warns about must be re-checked here.
        if (musicSource == null) return;

        if (other.CompareTag("Player"))
        {
            // Start playing the assigned audio clip from the beginning
            musicSource.clip = audioClip;
            musicSource.time = 0;
            musicSource.Play();

            // Fade in
            StopAllCoroutines();
            StartCoroutine(FadeMusic(musicSource, 1.0f, fadeTime));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (musicSource == null) return;

        if (other.CompareTag("Player"))
        {
            // Fade out
            StopAllCoroutines();
            StartCoroutine(FadeMusic(musicSource, 0.0f, fadeTime));
        }
    }

    private System.Collections.IEnumerator FadeMusic(AudioSource audioSource, float targetVolume, float duration)
    {
        float startVolume = audioSource.volume;
        float startTime = Time.time;

        while (Time.time < startTime + duration)
        {
            float elapsed = Time.time - startTime;
            audioSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
            yield return null;
        }

        audioSource.volume = targetVolume;

        // Faded to silence used to keep playing at volume 0 forever - inaudible, but
        // still holding a voice. Fully out means stopped; entry restarts from 0.
        if (targetVolume <= 0f) audioSource.Stop();
    }
}
