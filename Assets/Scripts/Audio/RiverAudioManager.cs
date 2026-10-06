using UnityEngine;
using UnityEngine.Audio;

[System.Serializable]
public class RiverProfile
{
    [Header("Identity")]
    [Tooltip("What this stretch of river is called. The inspector uses it as the entry's " +
             "header and the student progress report labels this river's row with it, so " +
             "the row reads \"River Area 1 - River\" instead of \"[0]\". Left empty the " +
             "inspector seeds it with the fixed name it used to print, \"River Area N\"")]
    public string areaName;

    [Header("Audio Configuration")]
    [Tooltip("Main audio clip for the river sound")]
    public AudioClip riverAudioClip;
    
    [Tooltip("AudioSource component for this river")]
    public AudioSource audioSource;
    
    [Tooltip("Mixer group for routing this river's audio")]
    public AudioMixerGroup mixerGroup;
    
    [Header("River Configuration")]
    [Tooltip("The spline path defining the river's course")]
    public Spline riverPath;
    
    [Tooltip("The player transform that the audio should follow")]
    public Transform followPlayer;

    [Header("Playback Settings")]
    [Tooltip("Automatically play when the game starts")]
    public bool playOnStart = true;
    
    [Tooltip("Loop the river sound continuously")]
    public bool loop = true;
    
    [Tooltip("Offset for initial audio playback (0-100% of clip length)")]
    [Range(0f, 1f)] 
    public float audioStartOffset = 0f;

    [Header("Optimization")]
    [Tooltip("Update interval for distance checks (seconds)")]
    [Range(0.1f, 2f)] public float updateInterval = 0.5f;

    // Internal state
    [HideInInspector] public Vector3 velocity = Vector3.zero;
    [HideInInspector] public Transform audioTransform;
    [HideInInspector] public float nextCheckTime;
    [HideInInspector] public bool isInRange;
    [HideInInspector] public int currentSegmentIndex;
    [HideInInspector] public int currentSegment;
    [HideInInspector] public float segmentProgress;
}

public class RiverAudioManager : MonoBehaviour
{
    [Header("River Management")]
    [Tooltip("Array of river profiles containing audio and path configurations")]
    [SerializeField] private RiverProfile[] riverProfiles;

    private void Awake()
    {
        foreach (var profile in riverProfiles)
        {
            if (profile.audioSource != null)
            {
                profile.audioTransform = profile.audioSource.transform;
                profile.audioSource.playOnAwake = false; // We handle playback
                profile.audioSource.loop = profile.loop;
            }
        }
    }

    private void OnEnable()
    {
        InitializeAudioSources();
    }

    private void InitializeAudioSources()
    {
        foreach (var profile in riverProfiles)
        {
            if (profile.audioSource != null)
            {
                // Configure audio source properties
                profile.audioSource.loop = profile.loop;
                
                if (profile.mixerGroup != null)
                {
                    profile.audioSource.outputAudioMixerGroup = profile.mixerGroup;
                }

                if (profile.riverAudioClip != null)
                {
                    profile.audioSource.clip = profile.riverAudioClip;
                }

                if (profile.audioSource.clip != null && profile.playOnStart)
                {
                    float offsetTime = profile.audioSource.clip.length * profile.audioStartOffset;
                    profile.audioSource.time = offsetTime;
                    profile.audioSource.Play();
                }
            }
        }
    }

    private void Update()
    {
        foreach (var profile in riverProfiles)
        {
            if (!IsProfileValid(profile)) continue;

            // Optimization: Distance-based audio management
            if (Time.time > profile.nextCheckTime)
            {
                profile.nextCheckTime = Time.time + profile.updateInterval;
                UpdateAudioState(profile);
            }

            if (profile.isInRange)
            {
                UpdateAudioPosition(profile);
            }
        }
    }

    private bool IsProfileValid(RiverProfile profile)
    {
        return profile.audioSource != null && 
               profile.riverPath != null && 
               profile.followPlayer != null && 
               profile.audioTransform != null;
    }

    private void UpdateAudioState(RiverProfile profile)
    {
        if (profile.audioSource.maxDistance <= 0) return;

        // Use squared distance comparison
        Vector3 delta = profile.audioTransform.position - profile.followPlayer.position;
        float sqrMaxDistance = profile.audioSource.maxDistance * profile.audioSource.maxDistance;
        profile.isInRange = delta.sqrMagnitude <= sqrMaxDistance;

        if (profile.isInRange && !profile.audioSource.isPlaying)
        {
            profile.audioSource.UnPause();
        }
        else if (!profile.isInRange && profile.audioSource.isPlaying)
        {
            profile.audioSource.Pause();
        }
    }

    private void UpdateAudioPosition(RiverProfile profile)
    {
        if (!IsProfileValid(profile)) return;

        Vector3 targetPosition = profile.riverPath.GetPositionAlongSpline(
            profile.followPlayer.position,
            ref profile.currentSegment,
            ref profile.segmentProgress
        );

        if (profile.riverPath.smoothMovement)
        {
            profile.audioTransform.position = Vector3.SmoothDamp(
                profile.audioTransform.position,
                targetPosition,
                ref profile.velocity,
                profile.riverPath.smoothTime
            );
        }
        else
        {
            profile.audioTransform.position = Vector3.MoveTowards(
                profile.audioTransform.position,
                targetPosition,
                profile.riverPath.moveSpeed * Time.deltaTime
            );
        }
    }

    private void OnDrawGizmos()
    {
        foreach (var profile in riverProfiles)
        {
            if (profile.riverPath == null || profile.followPlayer == null)
                continue;

            DrawRiverGizmos(profile);
        }
    }

    private void DrawRiverGizmos(RiverProfile profile)
    {
        if (profile.riverPath == null || profile.followPlayer == null)
            return;

        Gizmos.color = profile.riverPath.gizmoColor;
        
        if (profile.audioTransform != null)
        {
            Vector3 targetPosition = profile.riverPath.WhereOnSpline(profile.followPlayer.position);
            Gizmos.DrawLine(profile.audioTransform.position, targetPosition);
        }

        if (profile.riverPath.IsSplinePointInitialized() && profile.riverPath.debug_drawspline)
        {
            Gizmos.color = profile.riverPath.gizmoColor;
            for (int i = 0; i < profile.riverPath.transform.childCount - 1; i++)
            {
                Vector3 start = profile.riverPath.transform.GetChild(i).position;
                Vector3 end = profile.riverPath.transform.GetChild(i + 1).position;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(start, profile.riverPath.gizmoRadius);
            }
        }
    }

    public void SetRiverActive(int index, bool active)
    {
        if (index >= 0 && index < riverProfiles.Length)
        {
            if (riverProfiles[index].audioSource != null)
            {
                if (active) riverProfiles[index].audioSource.Play();
                else riverProfiles[index].audioSource.Stop();
            }
        }
    }

    public void SetAllRiversActive(bool active)
    {
        foreach (var profile in riverProfiles)
        {
            if (profile.audioSource != null)
            {
                if (active) profile.audioSource.Play();
                else profile.audioSource.Stop();
            }
        }
    }

    public void SetRiverAudioClip(int index, AudioClip newClip, bool playImmediately = true)
    {
        if (index >= 0 && index < riverProfiles.Length)
        {
            var profile = riverProfiles[index];
            if (profile.audioSource != null)
            {
                profile.riverAudioClip = newClip;
                profile.audioSource.clip = newClip;
                
                if (playImmediately && newClip != null)
                {
                    profile.audioSource.Play();
                }
            }
        }
    }
} 