using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/*
 * MushroomTripEffect.cs
 *
 * Purpose: Runs a "trip" - a full-screen post-processing wash plus a slow camera drift,
 *          with an optional audio-mixer snapshot underneath it.
 * Used by: MushroomTripInteraction (the placeable mushroom).
 *
 * Ported from a student submission (Liam Chapman, 26T2 AUD216) which drove the visuals by
 * sliding a purchased full-screen-effect quad in front of the camera. That asset pack is
 * not licensed for this project, so the look is rebuilt from URP post-processing the
 * project already ships: lens distortion, chromatic aberration, a cycling hue, saturation,
 * vignette and bloom, all blended by a single Volume weight.
 *
 * Lives on its own runtime object rather than on the mushroom: the trip has to outlive the
 * thing that started it, and the original had to keep the mushroom alive for the whole
 * trip to avoid killing its own coroutine.
 *
 * Deliberately NOT DontDestroyOnLoad, like every other gameplay singleton here - this is a
 * single-scene game and a reload rebuilds it (see CODE_STRUCTURE's reload note).
 */

/// <summary>
/// Everything one mushroom does, authored on the mushroom itself so different mushrooms
/// can trip differently.
/// </summary>
[System.Serializable]
public class MushroomTripSettings
{
    [Header("Timing")]
    [Tooltip("Total length of the trip, including both fades")]
    [Min(0.1f)] public float duration = 20f;
    [Min(0f)] public float fadeIn = 2.5f;
    [Min(0f)] public float fadeOut = 4f;

    [Header("Screen")]
    [Tooltip("Negative bulges the image outward. This is the main 'warp'")]
    [Range(-1f, 1f)] public float lensDistortion = -0.35f;
    [Range(0f, 1f)] public float chromaticAberration = 0.8f;
    [Range(-100f, 100f)] public float saturation = 50f;
    [Tooltip("Degrees of hue rotation per second - the colour cycling. 0 holds still")]
    public float hueCycleSpeed = 40f;
    [Range(0f, 1f)] public float vignette = 0.45f;
    [Min(0f)] public float bloom = 3f;

    [Header("Camera drift")]
    // Speeds and roll are the submission's own play-tested numbers; its sway was 0.4,
    // pulled back here because this pivot moves the camera in plain metres. Push it back
    // up for a heavier trip.
    [Tooltip("Metres of sway. Applied to a pivot above the camera, so head bob, camera " +
             "shake and mouse look are untouched")]
    public float swayAmount = 0.2f;
    public float swaySpeed = 0.8f;
    [Tooltip("Degrees of roll")]
    public float rollAmount = 2f;
    public float rollSpeed = 0.8f;

    [Header("Audio (optional)")]
    [Tooltip("Snapshot to blend to for the duration. Leave empty until one is authored")]
    public AudioMixerSnapshot tripSnapshot;
    [Tooltip("Snapshot to return to when the trip ends - normally the mixer's default")]
    public AudioMixerSnapshot normalSnapshot;
    [Min(0f)] public float snapshotTransition = 2f;
}

[DisallowMultipleComponent]
public class MushroomTripEffect : MonoBehaviour
{
    private static MushroomTripEffect instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>The effect for this scene, created on first use so nothing needs wiring.</summary>
    public static MushroomTripEffect Get()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<MushroomTripEffect>();
        if (instance == null)
        {
            instance = new GameObject("Mushroom Trip Effect").AddComponent<MushroomTripEffect>();
        }
        return instance;
    }

    /// <summary>True while a trip is running - the mushrooms use it to refuse a double dose.</summary>
    public bool IsTripping { get; private set; }

    private MushroomTripSettings settings = new MushroomTripSettings();
    private Coroutine tripRoutine;
    private readonly MixerDip mixDip = new MixerDip();
    private float weight;      // 0-1, drives both the Volume and the camera drift
    private float hueTime;

    private Transform pivot;
    private AudioSource dialogueSource;
    private Transform cameraTransform;

    private Volume volume;
    private VolumeProfile profile;
    private LensDistortion lens;
    private ChromaticAberration chroma;
    private ColorAdjustments colour;
    private Vignette vignette;
    private Bloom bloom;

    /// <summary>
    /// Starts a trip, or replaces one already running (a second mushroom re-ups the dose
    /// rather than stacking a second set of fades on the same Volume).
    /// </summary>
    public void BeginTrip(MushroomTripSettings tripSettings)
    {
        if (tripSettings != null) settings = tripSettings;

        EnsurePivot();
        EnsureVolume();
        if (volume == null) return;

        ApplyLook();

        if (tripRoutine != null) StopCoroutine(tripRoutine);
        tripRoutine = StartCoroutine(TripRoutine());
    }

    /// <summary>
    /// Plays one dialogue line after a delay. Hosted here because the mushroom that
    /// asks destroys itself after its eat sound - about when the line is due - and a
    /// coroutine dies with its host. 2D: the player's own voice, not a sound in the
    /// world.
    /// </summary>
    public void PlayDialogue(AudioClip clip, float delaySeconds, float volume, AudioMixerGroup output)
    {
        if (clip == null) return;

        StartCoroutine(DialogueRoutine(clip, delaySeconds, volume, output));
    }

    private IEnumerator DialogueRoutine(AudioClip clip, float delaySeconds, float volume,
                                       AudioMixerGroup output)
    {
        if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);

        if (dialogueSource == null)
        {
            dialogueSource = gameObject.AddComponent<AudioSource>();
            dialogueSource.playOnAwake = false;
            dialogueSource.spatialBlend = 0f;
        }

        dialogueSource.outputAudioMixerGroup = output;

        AudioEventLog.Record(AudioEventKind.Play, "Mushroom", $"dialogue '{clip.name}'");
        dialogueSource.PlayOneShot(clip, volume);
    }

    private IEnumerator TripRoutine()
    {
        IsTripping = true;

        mixDip.Dip(settings.tripSnapshot, settings.normalSnapshot, settings.snapshotTransition);

        // Fades run from the CURRENT weight, so re-dosing mid-fade picks up where the
        // screen actually is instead of snapping.
        yield return Ramp(1f, settings.fadeIn);

        float hold = Mathf.Max(0f, settings.duration - settings.fadeIn - settings.fadeOut);
        float held = 0f;
        while (held < hold)
        {
            held += Time.deltaTime;
            yield return null;
        }

        // Restore-if-dipped, so a trip authored with no snapshot cannot stomp a
        // snapshot some other system (the heartbeat, the boss dip) is holding.
        mixDip.Restore(settings.fadeOut > 0.01f ? settings.fadeOut : settings.snapshotTransition);

        yield return Ramp(0f, settings.fadeOut);

        IsTripping = false;
        tripRoutine = null;
    }

    private IEnumerator Ramp(float target, float seconds)
    {
        if (seconds <= 0.01f)
        {
            SetWeight(target);
            yield break;
        }

        float from = weight;
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            SetWeight(Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds)));
            yield return null;
        }
        SetWeight(target);
    }

    private void SetWeight(float value)
    {
        weight = Mathf.Clamp01(value);
        if (volume != null) volume.weight = weight;
    }

    private void Update()
    {
        if (colour == null || weight <= 0.0001f) return;

        // The colour cycle is what sells it; everything else is a static wash that the
        // Volume weight fades in and out.
        hueTime += Time.deltaTime * settings.hueCycleSpeed;
        colour.hueShift.value = Mathf.Repeat(hueTime + 180f, 360f) - 180f;
    }

    private void LateUpdate()
    {
        if (pivot == null) return;

        // Absolute writes to a transform nothing else touches. The camera itself is
        // written every frame by mouse look, head bob and camera shake - and head bob
        // reads its own X/Z back when the player stands still, so an offset added there
        // would compound frame on frame.
        if (weight <= 0.0001f)
        {
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            return;
        }

        float t = Time.time;
        pivot.localPosition = new Vector3(
            Mathf.Sin(t * settings.swaySpeed) * settings.swayAmount,
            Mathf.Cos(t * settings.swaySpeed * 0.8f) * settings.swayAmount,
            0f) * weight;

        pivot.localRotation = Quaternion.Euler(
            0f, 0f, Mathf.Sin(t * settings.rollSpeed) * settings.rollAmount * weight);
    }

    /// <summary>
    /// Inserts an identity transform directly above the camera and moves the camera under
    /// it, keeping every LOCAL value unchanged (SetParent with worldPositionStays false).
    /// Because the pivot starts as an identity, the camera's world pose is untouched and -
    /// importantly - CameraShakeController's cached original localPosition, MouseLook's
    /// pitch and the head bob all keep working on numbers that never changed.
    /// </summary>
    private void EnsurePivot()
    {
        if (pivot != null) return;

        Camera camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (camera == null)
        {
            Debug.LogWarning("[MushroomTripEffect] No camera found; the trip will have no " +
                             "camera drift (the screen effect still runs).", this);
            return;
        }

        cameraTransform = camera.transform;

        GameObject host = new GameObject("Mushroom Trip Pivot");
        host.transform.SetParent(cameraTransform.parent, false);
        host.transform.localPosition = Vector3.zero;
        host.transform.localRotation = Quaternion.identity;
        host.transform.localScale = Vector3.one;

        cameraTransform.SetParent(host.transform, false);
        pivot = host.transform;
    }

    private void EnsureVolume()
    {
        if (volume != null) return;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave;

        // Add(overrides: true) marks every parameter as overridden, which is what makes
        // the Volume's weight blend them against whatever the scene's Global Volume set.
        lens = profile.Add<LensDistortion>(true);
        chroma = profile.Add<ChromaticAberration>(true);
        colour = profile.Add<ColorAdjustments>(true);
        vignette = profile.Add<Vignette>(true);
        bloom = profile.Add<Bloom>(true);

        GameObject host = new GameObject("Mushroom Trip Volume");
        host.transform.SetParent(transform, false);

        volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;   // above the scene's Global Volume
        volume.weight = 0f;
        volume.sharedProfile = profile;
    }

    private void ApplyLook()
    {
        if (lens == null) return;

        lens.intensity.value = settings.lensDistortion;
        chroma.intensity.value = settings.chromaticAberration;
        colour.saturation.value = settings.saturation;
        vignette.intensity.value = settings.vignette;
        bloom.intensity.value = settings.bloom;
    }

    private void OnDestroy()
    {
        // A trip cut short by destruction - and death -> scene reload IS this game's
        // restart path - must put the mixer back itself: snapshot state lives on the
        // mixer ASSET and survives scene loads.
        mixDip.Restore(0.5f);

        // Put the camera back where it was before this object goes away, or a scene
        // reload would leave it parented to a destroyed pivot.
        if (pivot != null)
        {
            if (cameraTransform != null) cameraTransform.SetParent(pivot.parent, false);
            Destroy(pivot.gameObject);
        }

        if (profile != null) Destroy(profile);
        if (instance == this) instance = null;
    }
}
