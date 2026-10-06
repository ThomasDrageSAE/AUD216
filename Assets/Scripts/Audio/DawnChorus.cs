using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/*
 * DawnChorus.cs
 *
 * Purpose: The payoff for surviving a night - birdsong swells in at sunrise, an optional
 *          stinger marks the moment, and the player earns a little XP.
 * Used by: the scene; added with Tools > Audio > Add Dawn Chorus (a menu item, because
 *          this project's scene edits go through Unity - see CODE_STRUCTURE).
 *
 * Before this, night simply ended: the sky brightened and nothing acknowledged it. The
 * music already relaxes on its own (the intensity driver's night term falls at dawn with
 * a slow release), so this only adds the foreground: a chorus that swells from silence
 * over a few seconds rather than snapping on, which is what makes it read as a reward
 * rather than an ambience change.
 *
 * A dawn only counts if a night was actually witnessed - OnNightStateChanged can also
 * fire while the cycle settles at load, and being paid at spawn would cheapen the loop.
 */
[DisallowMultipleComponent]
public class DawnChorus : MonoBehaviour
{
    [Tooltip("One is chosen at random each dawn and swells in from silence")]
    [SerializeField] private AudioClip[] dawnChorusClips;

    [Range(0f, 1f)] [SerializeField] private float chorusVolume = 0.8f;

    [Tooltip("Seconds for the chorus to swell from silence to full - the swell is what " +
             "makes it a payoff rather than an ambience change")]
    [Min(0f)] [SerializeField] private float swellSeconds = 4f;

    [Tooltip("Optional one-shot over the swell - the 'you made it' accent")]
    [SerializeField] private AudioClip dawnStinger;

    [Range(0f, 1f)] [SerializeField] private float stingerVolume = 1f;

    [Tooltip("Mixer group for the chorus and stinger (ambience or SFX)")]
    [SerializeField] private AudioMixerGroup output;

    [Tooltip("XP awarded for surviving the night. 0 disables the award")]
    [Min(0f)] [SerializeField] private float survivalXp = 25f;

    private DayNightCycle dayNight;
    private AudioSource source;

    private void Start()
    {
        dayNight = FindFirstObjectByType<DayNightCycle>();
        if (dayNight == null)
        {
            Debug.LogWarning("[DawnChorus] No DayNightCycle in the scene; disabled.", this);
            enabled = false;
            return;
        }

        // The witnessed-night latch lives on the cycle itself now.
        dayNight.OnDawnAfterNight += OnDawnAfterNight;
    }

    private void OnDestroy()
    {
        if (dayNight != null) dayNight.OnDawnAfterNight -= OnDawnAfterNight;
    }

    private void OnDawnAfterNight()
    {
        StartCoroutine(PlayDawn());
    }

    private IEnumerator PlayDawn()
    {
        if (survivalXp > 0f && PlayerStats.Instance != null)
        {
            PlayerStats.Instance.AddXP(survivalXp);
        }

        EnsureSource();

        if (dawnStinger != null)
        {
            AudioEventLog.Record(AudioEventKind.Play, "DawnChorus", $"stinger '{dawnStinger.name}'");
            source.PlayOneShot(dawnStinger, stingerVolume);
        }

        AudioClip clip = PickChorusClip();
        if (clip == null) yield break;

        AudioEventLog.Record(AudioEventKind.Play, "DawnChorus", clip.name);

        source.clip = clip;
        source.volume = 0f;
        source.Play();

        float elapsed = 0f;
        while (elapsed < swellSeconds && source.isPlaying)
        {
            elapsed += Time.unscaledDeltaTime;   // the swell completes under a frozen timeScale; Clamp01 below caps a long hitch
            source.volume = chorusVolume * Mathf.Clamp01(elapsed / Mathf.Max(0.01f, swellSeconds));
            yield return null;
        }

        source.volume = chorusVolume;
    }

    private AudioClip PickChorusClip()
    {
        if (dawnChorusClips == null || dawnChorusClips.Length == 0) return null;
        return dawnChorusClips[Random.Range(0, dawnChorusClips.Length)];
    }

    private void EnsureSource()
    {
        if (source != null) return;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;   // the morning arriving, not a bird at a position
        if (output != null) source.outputAudioMixerGroup = output;
    }
}
