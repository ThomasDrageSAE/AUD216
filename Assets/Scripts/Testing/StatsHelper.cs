using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using TMPro;
using System.Text;
using UnityStandardAssets.Characters.FirstPerson;
using Audio;  // Add this

public class StatsHelper : MonoBehaviour
{
    [Header("Shortcut Settings")]
    [Tooltip("Modifier key required to be held down (e.g., Left Alt)")]
    [SerializeField] private KeyCode modifierKey = KeyCode.LeftAlt;
    
    [Tooltip("Key to press while holding the modifier to view stats (e.g., S)")]
    [SerializeField] private KeyCode triggerKey = KeyCode.S;

    [Header("Audio Manager References")]
    [SerializeField] private AudioMixer mainAudioMixer;
    [SerializeField] private AmbienceManager ambienceManager;
    [SerializeField] private MusicManager musicManager;
    [SerializeField] private UIAudioManager uiAudioManager;
    [SerializeField] private WeaponAudioManager weaponAudioManager;
    [SerializeField] private BulletImpactManager bulletImpactManager;
    [SerializeField] private FootstepAudioManager playerMovementAudioManager;  // Update type
    [SerializeField] private EnemyAudioManager enemyAudioManager;
    //[SerializeField] private EnvironmentalAudioManager environmentalAudioManager;
    [SerializeField] private AudioMixerController audioMixerController;

    [Header("UI References")]
    [SerializeField] private GameObject reportUI;
    [SerializeField] private TextMeshProUGUI reportText;
    [SerializeField] private ScrollRect scrollRect;

    [Header("Report Colors")]
    [Tooltip("Color for category headers in the report")]
    [SerializeField] private Color headerColor = Color.yellow;
    
    [Tooltip("Color for audio clip names in the report")]
    [SerializeField] private Color audioClipColor = new Color(1f, 0.5f, 0f); // Orange

    private FirstPersonController fpsController;
    private bool wasGamePaused;

    private string HeaderColor => "#" + ColorUtility.ToHtmlStringRGB(headerColor);
    private string AudioClipColor => "#" + ColorUtility.ToHtmlStringRGB(audioClipColor);

    private void Start()
    {
        fpsController = FindFirstObjectByType<FirstPersonController>();
        if (reportUI != null)
        {
            reportUI.SetActive(false);
        }
    }

    private void Awake()
    {
        // Dev diagnostic tool - disable outside the editor and development builds.
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            enabled = false;
        }
    }

    private void Update()
    {
        if (reportUI == null) return;

        // Toggle report with shortcut
        if (Input.GetKey(modifierKey) && Input.GetKeyDown(triggerKey))
        {
            ToggleReportUI(!reportUI.activeSelf);
        }

        // Close report with Escape
        if (reportUI.activeSelf && Input.GetKeyDown(KeyCode.Escape))
        {
            ToggleReportUI(false);
        }
    }

    private void GenerateAudioImplementationReport()
    {
        StringBuilder report = new StringBuilder();
        //int totalAudioClips = 0;

        report.AppendLine("<size=150%><b>=== AUDIO IMPLEMENTATION REPORT ===</b></size>\n");

        // Check Music Manager
        CheckMusicSystem(report);

        // Check Ambiences
        CheckAmbiences(report);

        // Check UIAudioManager
        CheckUISounds(report);

        // Check BulletImpactManager
        CheckImpactEffects(report);

        // Check WeaponAudioManager
        CheckWeaponSounds(report);

        // Check Player Movement Audio
        CheckFootstepSounds(report);

        // Check Enemy Audio
        CheckEnemyAudio(report);

        // Add Overview section
        // report.AppendLine($"\n<color={HeaderColor}>--- OVERVIEW ---</color>");
        // report.AppendLine($"Total audioclips: {totalAudioClips}");

        report.AppendLine("\n<size=150%><b>=== END OF REPORT ===</b></size>");

        // Update the UI text
        reportText.text = report.ToString();

        // Reset scroll position to top
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void ToggleReportUI(bool show)
    {
        reportUI.SetActive(show);

        if (show)
        {
            // Generate fresh report when showing UI
            GenerateAudioImplementationReport();
            
            // Store current pause state
            wasGamePaused = Time.timeScale == 0;
            
            // Pause game and show cursor
            Time.timeScale = 0;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Disable first person controller input
            if (fpsController != null)
            {
                fpsController.enabled = false;
            }
        }
        else
        {
            // Restore previous pause state
            Time.timeScale = wasGamePaused ? 0 : 1;
            
            // Only hide cursor and lock it if game wasn't paused
            if (!wasGamePaused)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            // Re-enable first person controller input
            if (fpsController != null)
            {
                fpsController.enabled = true;
            }
        }
    }

   private void CheckMusicSystem(StringBuilder report)
{
    report.AppendLine($"\n<color={HeaderColor}>--- MUSIC SYSTEM ---</color>");
    
    if (musicManager == null)
    {
        report.AppendLine("No MusicManager found in scene!");
        return;
    }

    var tracksField = typeof(MusicManager).GetField("musicTracks", 
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
    
    if (tracksField != null)
    {
        var tracks = tracksField.GetValue(musicManager) as MusicManager.MusicTrack[];
        if (tracks != null)
        {
            foreach (var track in tracks)
            {
                // A layered state ignores its legacy track entirely, so reporting that
                // clip would name something that never plays.
                if (musicManager.IsLayered(track.state))
                {
                    report.AppendLine($"State: {track.state}: <color={AudioClipColor}>layered (adaptive stems)</color>");
                    continue;
                }

                bool hasClip = track.musicClips != null && track.musicClips.Length > 0 && track.musicClips[0] != null;
                report.AppendLine($"State: {track.state}: {(hasClip ? $"<color={AudioClipColor}>{track.musicClips[0].name}</color>" : "No music assigned")}");
            }
        }
    }
}

private void CheckAmbiences(StringBuilder report)
{
    report.AppendLine($"\n<color={HeaderColor}>--- AMBIENCES ---</color>");
    
    if (ambienceManager == null)
    {
        report.AppendLine("No AmbienceManager found in scene!");
        return;
    }

    var ambiencesField = typeof(AmbienceManager).GetField("ambienceTracks", 
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
    
    if (ambiencesField != null)
    {
        var ambiences = ambiencesField.GetValue(ambienceManager) as AmbienceManager.AmbienceTrack[];
        if (ambiences != null)
        {
            foreach (var ambience in ambiences)
            {
                report.AppendLine($"State: {ambience.state}: {(ambience.ambienceClip != null ? $"<color={AudioClipColor}>{ambience.ambienceClip.name}</color>" : "No ambience assigned")}");
            }
        }
    }
}

    // private void CheckEnvironmentalAudio(StringBuilder report)
    // {
    //     report.AppendLine($"\n<color={HeaderColor}>--- ENVIRONMENTAL AUDIO ---</color>");
    //     EnvironmentalAudioManager envManager = FindFirstObjectByType<EnvironmentalAudioManager>();
        
    //     if (envManager == null)
    //     {
    //         report.AppendLine("No EnvironmentalAudioManager found in scene!");
    //         return;
    //     }

    //     var soundsField = typeof(EnvironmentalAudioManager).GetField("environmentalSounds", 
    //         System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
    //     if (soundsField != null)
    //     {
    //         var sounds = soundsField.GetValue(envManager) as EnvironmentalAudioManager.EnvironmentalSound[];
    //         if (sounds != null)
    //         {
    //             report.AppendLine($"Total environmental sounds: {sounds.Length}");
    //             foreach (var sound in sounds)
    //             {
    //                 report.AppendLine($"\nSound: {sound.soundName}");
    //                 report.AppendLine($"Has Audio Clip: {(sound.clip != null ? "Yes - " + $"<color={AudioClipColor}>{sound.clip.name}</color>" : "No")}");
    //                 report.AppendLine($"Plays on Start: {sound.playOnStart}");
    //                 report.AppendLine($"Loops: {sound.loop}");
    //             }
    //         }
    //     }
    // }

    private void CheckEnemyAudio(StringBuilder report)
    {
        report.AppendLine($"\n<color={HeaderColor}>--- ENEMY AUDIO ---</color>");
        
        if (enemyAudioManager == null)
        {
            report.AppendLine("No EnemyAudioManager found in scene!");
            return;
        }

        var profilesField = typeof(EnemyAudioManager).GetField("enemyProfiles", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (profilesField != null)
        {
            var profiles = profilesField.GetValue(enemyAudioManager) as EnemyAudioManager.EnemySoundProfile[];
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    report.AppendLine($"\nEnemy: {profile.enemyName} ({profile.enemyType})");
                    report.AppendLine($"Vocalisations: {(profile.generalVocalisations.vocalisationSounds != null ? $"{profile.generalVocalisations.vocalisationSounds.Length} clips" : "missing")}");
                    report.AppendLine($"Attack Sounds: {(profile.attackSounds != null ? $"{profile.attackSounds.Length} clips" : "missing")}");
                    bool hasDeathSound = profile.deathSounds != null && profile.deathSounds.Length > 0;
                    string deathSoundInfo = hasDeathSound ? $"{profile.deathSounds.Length} death sounds" : "No death sounds";
                    report.AppendLine($"Death Sound: {(hasDeathSound ? $"<color={AudioClipColor}>{profile.deathSounds[0].name}</color>" : "missing")}");
                    report.AppendLine($"Volume Settings:");
                    report.AppendLine($"- Vocalisation Volume: {profile.vocalisationVolume:F2}");
                    report.AppendLine($"- Attack Volume: {profile.attackVolume:F2}");
                    report.AppendLine($"- Death Volume: {profile.deathVolume:F2}");
                    report.AppendLine($"Vocalisation Mixer: {(profile.vocalisationMixerGroup != null ? profile.vocalisationMixerGroup.name : "none")}");
                    report.AppendLine($"Attack Mixer: {(profile.attackMixerGroup != null ? profile.attackMixerGroup.name : "none")}");
                }
            }
        }
    }

    private void CheckFootstepSounds(StringBuilder report)
    {
        report.AppendLine($"\n<color={HeaderColor}>--- FOOTSTEP SOUNDS ---</color>");
        FootstepAudioManager movementAudio = FindFirstObjectByType<FootstepAudioManager>();
        
        if (movementAudio == null)
        {
            report.AppendLine("No FootstepAudioManager found in scene!");
            return;
        }

        var surfaceProfilesField = typeof(FootstepAudioManager).GetField("surfaceProfiles", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (surfaceProfilesField != null)
        {
            var profiles = surfaceProfilesField.GetValue(movementAudio) as FootstepAudioManager.SurfaceAudioProfile[];
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    report.AppendLine($"\nSurface Type: {profile.surfaceType}");
                    report.AppendLine($"Number of footstep sounds: {(profile.footstepSounds?.Length ?? 0)}");
                    if (profile.footstepSounds != null && profile.footstepSounds.Length > 0)
                    {
                        report.AppendLine("Footstep clips:");
                        foreach (var soundClip in profile.footstepSounds)
                        {
                            report.AppendLine($"- <color={AudioClipColor}>{soundClip?.name ?? "null"}</color>");
                        }
                    }
                }
            }
        }

        // Check movement profile
        var movementProfileField = typeof(FootstepAudioManager).GetField("movementProfile", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (movementProfileField != null)
        {
            var profile = movementProfileField.GetValue(movementAudio) as FootstepAudioManager.MovementAudioProfile;
            if (profile != null)
            {
                report.AppendLine("\nMovement Sounds:");
                report.AppendLine($"Jump Sounds: {profile.jumpSounds?.Length ?? 0} clips");
                report.AppendLine($"Land Sounds: {profile.landSounds?.Length ?? 0} clips");
            }
        }
    }

    private void CheckImpactEffects(StringBuilder report)
    {
        report.AppendLine($"\n<color={HeaderColor}>--- IMPACT EFFECTS ---</color>");
        BulletImpactManager impactManager = FindFirstObjectByType<BulletImpactManager>();
        
        if (impactManager == null)
        {
            report.AppendLine("No BulletImpactManager found in scene!");
            return;
        }

        var impactEffectsField = typeof(BulletImpactManager).GetField("impactEffects", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (impactEffectsField != null)
        {
            var impactEffects = impactEffectsField.GetValue(impactManager) as BulletImpactManager.ImpactEffect[];
            if (impactEffects != null)
            {
                foreach (var effect in impactEffects)
                {
                    report.AppendLine($"\nSurface Type: {effect.surfaceTag}");
                    report.AppendLine($"Number of impact sounds: {(effect.impactSounds?.Length ?? 0)}");
                    // if (effect.impactSounds != null && effect.impactSounds.Length > 0)
                    // {
                    //     report.AppendLine("Sound clips:");
                    //     foreach (var clip in effect.impactSounds)
                    //     {
                    //         report.AppendLine($"- {clip?.name ?? "null"}");
                    //     }
                    // }
                }
            }
        }

        var enemyImpactSoundsField = typeof(BulletImpactManager).GetField("enemyImpactSounds", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (enemyImpactSoundsField != null)
        {
            var enemyImpactSounds = enemyImpactSoundsField.GetValue(impactManager) as AudioClip[];
            report.AppendLine($"\nEnemy Impact Sounds: {(enemyImpactSounds?.Length ?? 0)} clips");
            // if (enemyImpactSounds != null && enemyImpactSounds.Length > 0)
            // {
            //     foreach (var clip in enemyImpactSounds)
            //     {
            //         report.AppendLine($"- {clip?.name ?? "null"}");
            //     }
            // }
        }
    }

    private void CheckWeaponSounds(StringBuilder report)
    {
        report.AppendLine($"\n<color={HeaderColor}>--- WEAPON SOUNDS ---</color>");
        WeaponAudioManager weaponAudio = FindFirstObjectByType<WeaponAudioManager>();
        
        if (weaponAudio == null)
        {
            report.AppendLine("No WeaponAudioManager found in scene!");
            return;
        }

        // Field is m_WeaponProfiles - the old "weaponProfiles" lookup silently
        // returned null, so this report section never showed anything.
        var profilesField = typeof(WeaponAudioManager).GetField("m_WeaponProfiles",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (profilesField != null)
        {
            var profiles = profilesField.GetValue(weaponAudio) as WeaponAudioManager.WeaponSoundProfile[];
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    report.AppendLine($"\nWeapon: {profile.weaponName}");
                    //report.AppendLine($"Shoot sounds: {(profile.shootSounds?.Length ?? 0)} clips");
                    report.AppendLine($"Has raise sound: {(profile.raiseWeaponSound != null ? $"<color={AudioClipColor}>{profile.raiseWeaponSound.name}</color>" : "False")}");
                    report.AppendLine($"Has lower sound: {(profile.lowerWeaponSound != null ? $"<color={AudioClipColor}>{profile.lowerWeaponSound.name}</color>" : "False")}");
                    report.AppendLine($"Reload action sounds: {(profile.reloadActionSounds?.Length ?? 0)} clips");
                    
                    if (profile.shootSounds != null && profile.shootSounds.Length > 0)
                    {
                        report.AppendLine("Shoot sound clips:");
                        foreach (var clip in profile.shootSounds)
                        {
                            report.AppendLine($"- <color={AudioClipColor}>{clip?.name ?? "null"}</color>");
                        }
                    }
                }
            }
        }
    }

    private void CheckUISounds(StringBuilder report)
    {
        report.AppendLine($"\n<color={HeaderColor}>--- UI SOUNDS ---</color>");
        UIAudioManager uiAudio = FindFirstObjectByType<UIAudioManager>();
        
        if (uiAudio == null)
        {
            report.AppendLine("No UIAudioManager found in scene!");
            return;
        }

        // Read the manager's public clip fields directly (the old reflection
        // looked up a "sounds" field that never existed, so this section was
        // silently empty).
        report.AppendLine($"Menu Toggle On: {(uiAudio.menuToggleOn != null ? $"<color={AudioClipColor}>{uiAudio.menuToggleOn.name}</color>" : "missing")}");
        report.AppendLine($"Menu Toggle Off: {(uiAudio.menuToggleOff != null ? $"<color={AudioClipColor}>{uiAudio.menuToggleOff.name}</color>" : "missing")}");
        report.AppendLine($"Button Toggle On: {(uiAudio.buttonToggleOn != null ? $"<color={AudioClipColor}>{uiAudio.buttonToggleOn.name}</color>" : "missing")}");
        report.AppendLine($"Button Toggle Off: {(uiAudio.buttonToggleOff != null ? $"<color={AudioClipColor}>{uiAudio.buttonToggleOff.name}</color>" : "missing")}");
        report.AppendLine($"Objective Complete: {(uiAudio.objectiveCompleted != null ? $"<color={AudioClipColor}>{uiAudio.objectiveCompleted.name}</color>" : "missing")}");
        report.AppendLine($"All Enemies Cleared: {(uiAudio.allEnemiesCleared != null ? $"<color={AudioClipColor}>{uiAudio.allEnemiesCleared.name}</color>" : "missing")}");
        report.AppendLine($"Item Pickup: {(uiAudio.itemPickup != null ? $"<color={AudioClipColor}>{uiAudio.itemPickup.name}</color>" : "missing")}");
    }

    

    // private void CheckAudioMixer(StringBuilder report)
    // {
    //     report.AppendLine("\n--- AUDIO MIXER EFFECTS ---");
        
    //     if (mainAudioMixer == null)
    //     {
    //         report.AppendLine("No AudioMixer reference set!");
    //         return;
    //     }

    //     AudioMixerGroup[] groups = mainAudioMixer.FindMatchingGroups(string.Empty);
        
    //     foreach (var group in groups)
    //     {
    //         report.AppendLine($"\nMixer Group: {group.name}");
    //         report.AppendLine("(Note: Detailed effect information requires custom editor inspection)");
    //     }
    // }
}