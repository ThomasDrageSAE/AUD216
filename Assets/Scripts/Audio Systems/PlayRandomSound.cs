using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * PlayRandomSound.cs
 * 
 * Purpose: Plays random audio clips with variations in pitch and volume
 * Used by: Ambient sound emitters, environmental effects
 * 
 * Key Features:
 * - Randomly selects from a list of audio clips
 * - Varies pitch and volume within configurable ranges
 * - Adds random delays between sounds
 * 
 * Performance Considerations:
 * - Only updates when not playing
 * - Efficient audio clip selection
 * 
 * Dependencies:
 * - Requires AudioSource component
 * - Needs configured audio clips list
 */

[RequireComponent(typeof(AudioSource))]
public class PlayRandomSound : MonoBehaviour
{
    [Tooltip("List of audio clips to randomly play from")]
    public List<AudioClip> myAudioClips;

    private AudioSource myAudioSource;
    [Tooltip("Minimum pitch modification")]
    public float minPitch = .95f;
    [Tooltip("Maximum pitch modification")]
    public float maxPitch = 1.1f;
    [Tooltip("Minimum volume level")]
    public float minVol = 0.6f;
    [Tooltip("Maximum volume level")]
    public float maxVol = 1f;
    [Tooltip("Minimum delay between sounds")]
    public float minDelay = 0.5f;
    [Tooltip("Maximum delay between sounds")]
    public float maxDelay = 4f;

    private void Awake()
    {
        myAudioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (myAudioClips == null || myAudioClips.Count == 0) return;

        if (!myAudioSource.isPlaying)
        {
            int index = Random.Range(0, myAudioClips.Count);
            myAudioSource.clip = myAudioClips[index];
            myAudioSource.pitch = Random.Range(minPitch, maxPitch);
            myAudioSource.volume = Random.Range(minVol, maxVol);
            // PlayDelayed keeps the source in the playing state during the wait,
            // so Update won't retrigger. Calling Play() here would cancel the delay.
            myAudioSource.PlayDelayed(Random.Range(minDelay, maxDelay));
        }
    }
}