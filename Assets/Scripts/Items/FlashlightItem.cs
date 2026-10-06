using UnityEngine;

/*
 * FlashlightItem.cs
 *
 * Purpose: A carried flashlight - press its slot number to toggle the beam.
 * Used by: the Flashlight Pickup prefab; collected like any other Item.
 *
 * No battery, by owner decision (2026-09-03): the torch always works. The original
 * battery-and-hum system (drain, sagging hum loop, warning beeps, dying click) was
 * removed with it - the audio surface is exactly the two toggles.
 *
 * AUDIO lives on the InteractionAudioManager's single "Flashlight" profile (one
 * section per prop, owner decision 2026-09-03): Toggle On Sound and Toggle Off Sound,
 * created by Tools > Interactions > Add Missing Audio Profiles, authored where
 * everything else is authored, listed in Ctrl+G, red in Ctrl+J until filled. They play
 * through a 2D source on the camera rig - the torch is in the hand, not out in the
 * world - and the profile's Mixer Group Output routes them.
 *
 * The light itself rides a runtime rig parented to the camera, so the beam follows the
 * player's look; the pickup object stays hidden in the world while collected (that is
 * how Item works), so nothing renders or plays THERE.
 */
public class FlashlightItem : Item
{
    [Header("Light")]
    [SerializeField] private Color lightColor = new Color(1f, 0.95f, 0.8f);
    [Min(1f)] [SerializeField] private float lightRange = 25f;
    [Range(10f, 120f)] [SerializeField] private float spotAngle = 55f;
    [Min(0.1f)] [SerializeField] private float lightIntensity = 2.2f;

    [Header("Audio")]
    [SerializeField] private string audioProfileName = "Flashlight";

    [Tooltip("Compile-checked identity. When set, this wins over the name above")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    private GameObject rig;        // camera-parented: light + audio
    private Light beam;
    private AudioSource rigSource;
    private InteractionAudioManager audioManager;
    private bool isOn;

    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;

    public override void UseItem()
    {
        if (isOn) TurnOff();
        else TurnOn();
    }

    /// <summary>Off before it leaves the hands - a torch on the ground should not beam.</summary>
    public override void Drop(Vector3 position)
    {
        if (isOn) TurnOff();
        base.Drop(position);
    }

    private void TurnOn()
    {
        EnsureRig();
        isOn = true;
        beam.enabled = true;
        PlayToggle(on: true);
    }

    private void TurnOff()
    {
        isOn = false;
        if (beam != null) beam.enabled = false;
        PlayToggle(on: false);
    }

    private void PlayToggle(bool on)
    {
        if (rigSource == null) return;
        if (audioManager == null) audioManager = FindFirstObjectByType<InteractionAudioManager>();

        // Through the manager, not clips on this component: both toggles are then
        // authored in the one "Flashlight" section, and a missing profile shows red
        // in Ctrl+J naming exactly what to author.
        if (audioManager == null || !audioManager.TryGetProfile(AudioProfileKey, out var profile)) return;

        AudioClip clip = on ? profile.toggleOnSound : profile.toggleOffSound;
        if (clip == null) return;

        if (profile.mixerGroupOutput != null)
        {
            rigSource.outputAudioMixerGroup = profile.mixerGroupOutput;
        }

        AudioEventLog.Record(AudioEventKind.Play, "Flashlight",
            $"toggle {(on ? "on" : "off")} '{clip.name}'");
        rigSource.PlayOneShot(clip, 1f);
    }

    private void EnsureRig()
    {
        if (rig != null) return;

        Camera camera = Camera.main;
        Transform parent = camera != null ? camera.transform : transform;

        rig = new GameObject("Flashlight Rig");
        rig.transform.SetParent(parent, false);
        rig.transform.localPosition = new Vector3(0.25f, -0.2f, 0.1f);   // held low-right
        rig.transform.localRotation = Quaternion.identity;

        beam = rig.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.color = lightColor;
        beam.range = lightRange;
        beam.spotAngle = spotAngle;
        beam.intensity = lightIntensity;
        beam.enabled = false;

        rigSource = rig.AddComponent<AudioSource>();
        rigSource.playOnAwake = false;
        rigSource.spatialBlend = 0f;   // in the hand, not out in the world
    }

    private void OnDestroy()
    {
        if (rig != null) Destroy(rig);
    }
}
