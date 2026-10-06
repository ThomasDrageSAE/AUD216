using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;

namespace Audio
{
    public class FootstepAudioManager : MonoBehaviour
    {
        [System.Serializable]
        public class SurfaceAudioProfile
        {
            public string surfaceType;

            [Tooltip("Compile-checked identity for this profile. Leave Unassigned until Tools/Audio/Migrate Profile Keys To Enums has been run; once set, this name also works as a spelling of the profile.")]
            public SurfaceId surfaceId;

            public AudioClip[] footstepSounds;
            [Range(0f, 1f)] public float baseVolume = 1f;
        }

        [System.Serializable]
        public class MovementAudioProfile
        {
            public AudioClip[] jumpSounds;
            [Range(0f, 1f)] public float jumpVolume = 1f;

            public AudioClip[] landSounds;
            [Range(0f, 1f)] public float landVolume = 1f;
        }

        [Header("Surface Profiles")]
        [SerializeField] private SurfaceAudioProfile[] surfaceProfiles;
        [SerializeField] private MovementAudioProfile movementProfile;

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioMixerGroup sfxMixerGroup;

        [Header("Variation")]
        [Tooltip("Random pitch range applied to footstep/jump/land sounds so repeats are less noticeable")]
        [Range(0.8f, 1.2f)] [SerializeField] private float minPitchVariation = 0.95f;
        [Range(0.8f, 1.2f)] [SerializeField] private float maxPitchVariation = 1.05f;

        private Dictionary<string, SurfaceAudioProfile> surfaceLookup;
        private int lastFootstepIndex = -1;
        private int lastJumpIndex = -1;
        private int lastLandIndex = -1;

        private void Awake()
        {
            InitializeAudioSources();
            InitializeSurfaceLookup();
        }

        private void InitializeAudioSources()
        {
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;

            if (sfxMixerGroup != null)
            {
                audioSource.outputAudioMixerGroup = sfxMixerGroup;
            }
        }

        private void InitializeSurfaceLookup()
        {
            surfaceLookup = AudioProfileKeys.CreateLookup<SurfaceAudioProfile>();
            if (surfaceProfiles == null) return;

            foreach (var profile in surfaceProfiles)
            {
                if (profile == null) continue;
                AudioProfileKeys.AddProfile(surfaceLookup, profile.surfaceType, profile, this, "FootstepAudioManager");
            }

            // Second pass: aliases only for profiles the lookup actually serves - a
            // duplicate that pass 1 rejected must not smuggle itself back in via its id.
            foreach (var profile in surfaceProfiles)
            {
                if (profile == null) continue;
                if (!AudioProfileKeys.Owns(surfaceLookup, profile.surfaceType, profile)) continue;
                AudioProfileKeys.AddIdAlias(surfaceLookup, profile.surfaceId, profile);
            }
        }

        public void PlayFootstep(string surfaceType)
        {
            // noisyLog: a footstep fires several times a second, so both of its log
            // entries are marked high-frequency and hidden by the Ctrl+J monitor. A
            // surface MISS is still recorded normally and still shows in red.
            if (!AudioProfileKeys.TryGetProfile(surfaceLookup, surfaceType,
                    out SurfaceAudioProfile profile, this, "FootstepAudioManager", noisyLog: true))
                return;

            // The gain rides on the voice, not the source: writing profile.baseVolume onto
            // the shared source retroactively rescaled every one-shot still ringing on it.
            AudioUtil.PlayRandomOneShot(audioSource, profile.footstepSounds, ref lastFootstepIndex,
                profile.baseVolume, minPitchVariation, maxPitchVariation, noisyLog: true);
        }

        public void PlayJumpSound()
        {
            AudioUtil.PlayRandomOneShot(audioSource, movementProfile.jumpSounds, ref lastJumpIndex,
                movementProfile.jumpVolume, minPitchVariation, maxPitchVariation);
        }

        public void PlayLandSound()
        {
            AudioUtil.PlayRandomOneShot(audioSource, movementProfile.landSounds, ref lastLandIndex,
                movementProfile.landVolume, minPitchVariation, maxPitchVariation);
        }
    }
} 