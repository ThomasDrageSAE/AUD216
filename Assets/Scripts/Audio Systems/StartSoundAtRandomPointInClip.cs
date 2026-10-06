using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StartSoundAtRandomPointInClip : MonoBehaviour
{
    public AudioClip clip;
    public AudioSource audioSource;

    void Start()
    {
        if (audioSource == null || clip == null)
        {
            Debug.LogWarning("StartSoundAtRandomPointInClip: audioSource or clip not assigned.", this);
            return;
        }

        audioSource.clip = clip;
        int randomStartTime = Random.Range(0, clip.samples - 1); //clip.samples is the length of the clip in samples
        audioSource.timeSamples = randomStartTime;
        audioSource.Play();
    }
}
