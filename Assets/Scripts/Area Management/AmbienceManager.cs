using UnityEngine;
using UnityEngine.Audio;
using System.Collections;

public class AmbienceManager : MonoBehaviour
{
    public enum AmbienceState
    {
        Outside,
        Swamp,
        Cave,
        Campfire
    }

    [System.Serializable]
    public class AmbienceTrack
    {
        public string trackName;
        public AmbienceState state;
        public AudioClip ambienceClip;
        [Range(0f, 1f)]
        public float volume = 1.0f;
        public AudioMixerGroup outputMixerGroup;
        public bool loop = true;
        [HideInInspector] public AudioSource source;
    }

    public AmbienceTrack[] ambienceTracks;
    public float crossFadeDuration = 2.0f;
    public AmbienceState startingState = AmbienceState.Outside;

    private AmbienceState currentState;
    private Coroutine fadeCoroutine;

    private void Awake()
    {
        InitializeAudioSources();
    }

    private void Start()
    {
        ChangeState(startingState, true);
    }

    private void InitializeAudioSources()
    {
        foreach (var track in ambienceTracks)
        {
            track.source = gameObject.AddComponent<AudioSource>();
            track.source.clip = track.ambienceClip;
            track.source.loop = track.loop;
            track.source.volume = 0;
            track.source.playOnAwake = false;
            track.source.spatialBlend = 0f;
            
            if (track.outputMixerGroup != null)
            {
                track.source.outputAudioMixerGroup = track.outputMixerGroup;
            }
        }
    }

    public void ChangeState(AmbienceState newState, bool immediate = false)
    {
        if (currentState == newState && !immediate) return;

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }

        if (immediate)
        {
            SetStateVolumes(newState);
            currentState = newState;
            
            // Start playing all tracks (they'll be muted if not active)
            foreach (var track in ambienceTracks)
            {
                if (!track.source.isPlaying)
                    track.source.Play();
            }
        }
        else
        {
            fadeCoroutine = StartCoroutine(CrossFadeToState(newState));
        }
    }

    private void SetStateVolumes(AmbienceState state)
    {
        foreach (var track in ambienceTracks)
        {
            track.source.volume = track.state == state ? track.volume : 0f;
        }
    }

    private IEnumerator CrossFadeToState(AmbienceState newState)
    {
        float elapsedTime = 0f;
        
        // Store initial volumes
        float[] startVolumes = new float[ambienceTracks.Length];
        float[] targetVolumes = new float[ambienceTracks.Length];
        
        for (int i = 0; i < ambienceTracks.Length; i++)
        {
            startVolumes[i] = ambienceTracks[i].source.volume;
            targetVolumes[i] = ambienceTracks[i].state == newState ? ambienceTracks[i].volume : 0f;
            
            // Start playing if not already playing
            if (!ambienceTracks[i].source.isPlaying)
                ambienceTracks[i].source.Play();
        }

        while (elapsedTime < crossFadeDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;   // finishes (and commits currentState) under the pause menu and the death screen
            float t = elapsedTime / crossFadeDuration;

            for (int i = 0; i < ambienceTracks.Length; i++)
            {
                ambienceTracks[i].source.volume = Mathf.Lerp(startVolumes[i], targetVolumes[i], t);
            }

            yield return null;
        }

        // Ensure final volumes are set exactly
        for (int i = 0; i < ambienceTracks.Length; i++)
        {
            ambienceTracks[i].source.volume = targetVolumes[i];
        }

        currentState = newState;
    }

    public AmbienceState GetCurrentState()
    {
        return currentState;
    }
} 