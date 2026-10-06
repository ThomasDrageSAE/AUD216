using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class RandomTriggerAudioVariations : MonoBehaviour
{
    [Header("Clips")]
    public AudioClip[] audioClips;

    [Header("Pitch Range")]
    public float minPitch = 0.95f;
    public float maxPitch = 1.05f;

    [Header("Volume Range")]
    [Range(0f, 1f)] public float minVolume = 0.8f;
    [Range(0f, 1f)] public float maxVolume = 1.0f;

    [Header("Spam Protection")]
    public float cooldownSeconds = 0.25f;   // tweak this

    private AudioSource audioSource;
    private int lastClipIndex = -1;
    private float nextAllowedTime = 0f;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (Time.time < nextAllowedTime) return;

        nextAllowedTime = Time.time + cooldownSeconds;

        float volume = Random.Range(minVolume, maxVolume);
        AudioUtil.PlayRandomOneShot(audioSource, audioClips, ref lastClipIndex,
            volume, minPitch, maxPitch);
    }
}