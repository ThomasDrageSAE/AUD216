using System.Collections.Generic;
using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    /*
     * DayNightCycle.cs
     * 
     * Purpose: Controls the day/night cycle system including lighting, colors, and environmental effects
     * Used by: Main game world, lighting system, environmental systems
     * 
     * Key Features:
     * - Manages time progression through day/night cycles
     * - Controls sun position, intensity, and color throughout the cycle
     * - Handles sky and cloud color transitions
     * - Controls ambient lighting changes
     * - Manages particle systems based on time of day
     * - Provides time-based events and state information
     * 
     * Time Phases:
     * - Night (nightTime to dawnStart)
     * - Dawn (dawnStart to dayStart)
     * - Day (dayTime to duskStart)
     * - Dusk (duskStart to nightStart)
     * 
     * Performance Considerations:
     * - Uses interval-based updates for non-critical visual changes
     * - Caches particle system states
     * - Implements smooth transitions using interpolation
     * 
     * Dependencies:
     * - Requires configured Light component for sun
     * - Needs assigned skybox and cloud materials. These are cloned at Awake and the
     *   clones are swapped into every renderer that used them, so the material assets
     *   on disk are never written to.
     * - Optional particle system groups for environmental effects
     *
     * Note: the current day number lives here and is broadcast via OnDayChanged.
     * Time-critical objectives are owned by DailyObjectiveManager, which is also the
     * only system that pauses this clock.
     */

    [Header("Time Settings")]
    private const float dayLength = 100f;    // Duration of daytime in seconds
    private const float nightLength = 200f;  // Duration of nighttime in seconds
    [SerializeField, Range(0f, 1f)] private float timeOfDay = 0.5f;  // Normalized time (0-1) representing current time of day
    [SerializeField] private int dayNumber = 1;  // Current day number, increments at dayTransitionTime
    [SerializeField] private float dayTransitionTime = 0.14f;  // Time of day when the day number increments
    [SerializeField] private float currentTimeMultiplier = 1f;  // Controls speed of time passage (1 = normal, 2 = double speed, etc.)

    [Header("Cycle Thresholds")]
    [SerializeField, Range(0f, 1f)] private float dawnStart = 0.14f;  // When dawn begins
    [SerializeField, Range(0f, 1f)] private float dawn = 0.21f;       // Dawn complete
    [SerializeField, Range(0f, 1f)] private float dayStart = 0.301f;   // Start transition to day angle
    [SerializeField, Range(0f, 1f)] private float dayTime = 0.386f;    // When full day begins
    [SerializeField, Range(0f, 1f)] private float duskStart = 0.635f;  // Start transition to dusk angle
    [SerializeField, Range(0f, 1f)] private float dusk = 0.726f;       // Dusk complete
    [SerializeField, Range(0f, 1f)] private float nightStart = 0.794f; // Start transition to night angle
    [SerializeField, Range(0f, 1f)] private float nightTime = 0.88f;   // When night begins

    [Header("Sun Settings")]
    [SerializeField] private Light sunLight;
    [SerializeField] private float sunIntensityDay = 1.5f;
    [SerializeField] private float sunIntensityDawn = 1.0f;
    [SerializeField] private float sunIntensityDusk = 1.2f;
    [SerializeField] private float sunIntensityNight = 0f;
    [SerializeField] private Color dayColor = Color.white;
    [SerializeField] private Color dawnColor = new Color(1f, 0.8f, 0.5f, 1f);    // Warm orange
    [SerializeField] private Color duskColor = new Color(1f, 0.3f, 0.2f, 1f);    // Deep orange-red
    [SerializeField] private Color nightColor = new Color(0.1f, 0.1f, 0.3f, 1f); // Dark blue

    [Header("Ambient Light Settings")]
    [SerializeField] private Color ambientDayColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [SerializeField] private Color ambientDawnColor = new Color(0.5f, 0.4f, 0.3f, 1f);
    [SerializeField] private Color ambientDuskColor = new Color(0.4f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color ambientNightColor = new Color(0.1f, 0.1f, 0.2f, 1f);
    [SerializeField] private float ambientDayIntensity = 1f;
    [SerializeField] private float ambientDawnIntensity = 0.7f;
    [SerializeField] private float ambientDuskIntensity = 0.5f;
    [SerializeField] private float ambientNightIntensity = 0.2f;

    [Header("Rotation Settings")]
    [SerializeField] private float dayAngle = 30f;
    [SerializeField] private float dawnAngle = 0f;     // Angle during dawn
    [SerializeField] private float duskAngle = 180f;   // Angle during dusk
    [SerializeField] private float nightAngle = 260f;
    //[SerializeField, Range(0f, 1f)] private float transitionSmoothness = 0.5f;

    [Header("Sky Settings")]
    [SerializeField] private Material skydomeMaterial;
    
    [Header("Day Sky Colors")]
    [SerializeField] private Color dayTopColor = new Color(0.4f, 0.6f, 1f, 1f);     // Light blue
    [SerializeField] private Color dayBottomColor = new Color(0.7f, 0.8f, 1f, 1f);  // Pale blue
    
    [Header("Dawn Sky Colors")]
    [SerializeField] private Color dawnTopColor = new Color(0.5f, 0.3f, 0.2f, 1f);     // Orange-ish
    [SerializeField] private Color dawnBottomColor = new Color(0.8f, 0.6f, 0.4f, 1f);  // Light orange
    
    [Header("Dusk Sky Colors")]
    [SerializeField] private Color duskTopColor = new Color(0.3f, 0.2f, 0.4f, 1f);     // Purple-ish
    [SerializeField] private Color duskBottomColor = new Color(0.6f, 0.4f, 0.3f, 1f);  // Orange-purple
    
    [Header("Night Sky Colors")]
    [SerializeField] private Color nightTopColor = new Color(0.1f, 0.1f, 0.2f, 1f);    // Dark blue
    [SerializeField] private Color nightBottomColor = new Color(0.2f, 0.2f, 0.3f, 1f); // Slightly lighter blue

    [Header("Cloud Settings")]
    [SerializeField] private Material cloudMaterial;
    
    [Header("Day Cloud Colors")]
    [SerializeField] private Color cloudDayColor = new Color(1f, 1f, 1f, 1f);         // White
    [SerializeField] private Color cloudDayEmission = new Color(0.5f, 0.5f, 0.5f, 1f); // Subtle white glow
    
    [Header("Dawn Cloud Colors")]
    [SerializeField] private Color cloudDawnColor = new Color(1f, 0.8f, 0.6f, 1f);    // Warm white/orange
    [SerializeField] private Color cloudDawnEmission = new Color(0.6f, 0.4f, 0.2f, 1f); // Warm glow
    
    [Header("Dusk Cloud Colors")]
    [SerializeField] private Color cloudDuskColor = new Color(0.9f, 0.6f, 0.4f, 1f);  // Orange-pink
    [SerializeField] private Color cloudDuskEmission = new Color(0.5f, 0.3f, 0.2f, 1f); // Orange glow
    
    [Header("Night Cloud Colors")]
    [SerializeField] private Color cloudNightColor = new Color(0.3f, 0.3f, 0.4f, 1f); // Dark blue-grey
    [SerializeField] private Color cloudNightEmission = new Color(0.1f, 0.1f, 0.15f, 1f); // Subtle blue glow

    [Header("Particle Systems")]
    [SerializeField] private ParticleSystem[] subBeamParticles;      // SubBeam particles
    [SerializeField] private ParticleSystem[] duskParticles;         // Dusk particles
    [SerializeField] private ParticleSystem[] environmentParticles;  // Leaves, butterflies, grass etc.
    [SerializeField] private ParticleSystem[] snowParticles;        // Snow particles
    [SerializeField] private ParticleSystem[] rainParticles;        // Rain particles

    [System.Serializable]
    private class ParticleSystemControls
    {
        public bool playDuringDawn = true;
        public bool playDuringDay = true;
        public bool playDuringDusk = false;
        public bool playDuringNight = false;
    }

    [Header("Particle System Controls")]
    [SerializeField] private ParticleSystemControls subBeamControls = new ParticleSystemControls();
    [SerializeField] private ParticleSystemControls duskParticleControls = new ParticleSystemControls();
    [SerializeField] private ParticleSystemControls environmentControls = new ParticleSystemControls();
    [SerializeField] private ParticleSystemControls snowControls = new ParticleSystemControls();
    [SerializeField] private ParticleSystemControls rainControls = new ParticleSystemControls();

    // Note: time-critical objectives are owned by DailyObjectiveManager. This class
    // used to carry a duplicate copy of that config that was computed and never read.

    private bool isNighttime => timeOfDay <= dawnStart || timeOfDay >= nightStart;
    private bool m_IsTimePaused = false;
    //[SerializeField] private bool hasSetInitialMultiplier = false;
    private bool hasIncrementedDayToday = false;  // Flag to ensure we only increment once per cycle

    // Add these cached values
    private float lastUpdateTime;
    private const float UPDATE_INTERVAL = 0.1f;      // Recompute colour targets every 100ms
    private const float VISUAL_SMOOTHING = 8f;       // Approach rate toward those targets

    private static readonly int SkyColorTopId = Shader.PropertyToID("_ColorTop");
    private static readonly int SkyColorBottomId = Shader.PropertyToID("_ColorBottom");
    private static readonly int CloudColorId = Shader.PropertyToID("_Color");
    private static readonly int CloudEmissionId = Shader.PropertyToID("_EmissionColor");

    private Color currentSkyTopColor;
    private Color currentSkyBottomColor;
    private Color currentCloudColor;
    private Color currentCloudEmission;
    private float currentAmbientIntensity;
    private Color currentAmbientColor;

    private Color targetSkyTopColor;
    private Color targetSkyBottomColor;
    private Color targetCloudColor;
    private Color targetCloudEmission;
    private Color targetAmbientColor;
    private float targetAmbientIntensity;

    // Runtime clones of the sky/cloud materials, destroyed with this component.
    private readonly List<Material> runtimeMaterials = new List<Material>();

    private ParticleSystemState[] particleStates;

    private struct ParticleSystemState
    {
        public bool isPlaying;
        public ParticleSystem system;
    }

    public delegate void NightStateChanged(bool isNight);
    public event NightStateChanged OnNightStateChanged;

    /// <summary>
    /// Fired at a dawn that follows a WITNESSED night. OnNightStateChanged can also
    /// fire while the cycle settles at load, so every dawn-payoff consumer (DawnChorus,
    /// WellOffering) carried its own 'saw a night first' latch - it lives here once now.
    /// </summary>
    public event System.Action OnDawnAfterNight;

    private bool dawnLatchSawNight;

    public delegate void DayChangedHandler(int newDay);
    public event DayChangedHandler OnDayChanged;

    private void Awake()
    {
        CreateRuntimeMaterialInstances();

        // Cache initial colors
        currentSkyTopColor = skydomeMaterial != null ? skydomeMaterial.GetColor(SkyColorTopId) : Color.black;
        currentSkyBottomColor = skydomeMaterial != null ? skydomeMaterial.GetColor(SkyColorBottomId) : Color.black;
        currentCloudColor = cloudMaterial != null ? cloudMaterial.GetColor(CloudColorId) : Color.white;
        currentCloudEmission = cloudMaterial != null ? cloudMaterial.GetColor(CloudEmissionId) : Color.black;
        currentAmbientColor = RenderSettings.ambientLight;
        currentAmbientIntensity = RenderSettings.ambientIntensity;

        // Seed the targets so the first frames approach the right colours instead of black.
        GetCurrentSkyColors(out targetSkyTopColor, out targetSkyBottomColor);
        GetCurrentCloudColors(out targetCloudColor, out targetCloudEmission);
        GetCurrentAmbientLight(out targetAmbientColor, out targetAmbientIntensity);

        InitializeParticleStates();
    }

    /// <summary>
    /// Replaces the assigned sky/cloud materials with runtime clones and rebinds every
    /// renderer that used the originals.
    ///
    /// Material.SetColor writes straight into the material *asset*, so driving the
    /// serialized references directly rewrote Skydome_Day.mat and
    /// PolygonNature_Clouds.mat on disk on every play session.
    /// </summary>
    private void CreateRuntimeMaterialInstances()
    {
        if (skydomeMaterial == null && cloudMaterial == null) return;

        // Inactive renderers are included: cloud props may be enabled later.
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        skydomeMaterial = CloneAndRebind(skydomeMaterial, renderers);
        cloudMaterial = CloneAndRebind(cloudMaterial, renderers);
    }

    private Material CloneAndRebind(Material source, Renderer[] renderers)
    {
        if (source == null) return null;

        Material clone = new Material(source);
        clone.name = source.name + " (Runtime)";
        runtimeMaterials.Add(clone);

        int rebound = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            Material[] shared = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == source)
                {
                    shared[i] = clone;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = shared;
                rebound++;
            }
        }

        if (rebound == 0)
        {
            Debug.LogWarning($"[DayNightCycle] No renderer uses '{source.name}', so its colour changes will not be visible.", this);
        }

        return clone;
    }

    private void OnDestroy()
    {
        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
        runtimeMaterials.Clear();
    }

    private void InitializeParticleStates()
    {
        // Initialize particle system states
        int totalParticleSystems = 
            (subBeamParticles?.Length ?? 0) +
            (duskParticles?.Length ?? 0) +
            (environmentParticles?.Length ?? 0) +
            (snowParticles?.Length ?? 0) +
            (rainParticles?.Length ?? 0);

        particleStates = new ParticleSystemState[totalParticleSystems];
        int index = 0;

        void InitializeGroup(ParticleSystem[] systems)
        {
            if (systems == null) return;
            foreach (var system in systems)
            {
                if (system != null)
                {
                    particleStates[index++] = new ParticleSystemState 
                    { 
                        isPlaying = system.isPlaying,
                        system = system 
                    };
                }
            }
        }

        InitializeGroup(subBeamParticles);
        InitializeGroup(duskParticles);
        InitializeGroup(environmentParticles);
        InitializeGroup(snowParticles);
        InitializeGroup(rainParticles);
    }

    private void Start()
    {
        if (sunLight == null)
            sunLight = GetComponent<Light>();

        ValidateThresholds();
        UpdateAmbientLight();
    }

    private void Update()
    {
        if (m_IsTimePaused) return;

        // Always update time every frame for smooth transitions
        UpdateTime();

        // Update visuals with interpolation
        UpdateVisuals();
    }

    private void UpdateTime()
    {
        float deltaTime = Time.deltaTime * currentTimeMultiplier;
        float timeStep = isNighttime ? deltaTime / nightLength : deltaTime / dayLength;

        bool wasBeforeTransition = timeOfDay < dayTransitionTime;
        bool wasNight = isNighttime;

        // Reset hasIncrementedDayToday when we're in the proper window
        if (timeOfDay >= nightTime || timeOfDay < dayTransitionTime)
        {
            if (hasIncrementedDayToday)
            {
                hasIncrementedDayToday = false;
            }
        }

        timeOfDay += timeStep;
        if (timeOfDay >= 1f)
        {
            timeOfDay = 0f;
        }

        // Check day transition
        if (!hasIncrementedDayToday && wasBeforeTransition && timeOfDay >= dayTransitionTime)
        {
            dayNumber++;
            hasIncrementedDayToday = true;
            OnDayChanged?.Invoke(dayNumber);
        }

        // Check night state change
        if (wasNight != isNighttime)
        {
            OnNightStateChanged?.Invoke(isNighttime);

            if (isNighttime)
            {
                dawnLatchSawNight = true;
            }
            else if (dawnLatchSawNight)
            {
                dawnLatchSawNight = false;
                OnDawnAfterNight?.Invoke();
            }
        }
    }

    private void UpdateVisuals()
    {
        // Always update rotation for smooth shadows
        UpdateSunRotation();

        // Colour targets are recomputed on an interval; particle states go with them
        // since they don't need smoothing.
        if (Time.time >= lastUpdateTime + UPDATE_INTERVAL)
        {
            lastUpdateTime = Time.time;

            GetCurrentSkyColors(out targetSkyTopColor, out targetSkyBottomColor);
            GetCurrentCloudColors(out targetCloudColor, out targetCloudEmission);
            GetCurrentAmbientLight(out targetAmbientColor, out targetAmbientIntensity);

            UpdateParticleSystems();
        }

        // Approach the targets every frame. The previous code only ran this inside the
        // interval gate, where the interpolation factor was always >= 1, so every Lerp
        // was a snap and the visuals stepped at 10Hz.
        float t = 1f - Mathf.Exp(-VISUAL_SMOOTHING * Time.deltaTime);

        if (skydomeMaterial != null)
        {
            currentSkyTopColor = Color.Lerp(currentSkyTopColor, targetSkyTopColor, t);
            currentSkyBottomColor = Color.Lerp(currentSkyBottomColor, targetSkyBottomColor, t);

            skydomeMaterial.SetColor(SkyColorTopId, currentSkyTopColor);
            skydomeMaterial.SetColor(SkyColorBottomId, currentSkyBottomColor);
        }

        if (cloudMaterial != null)
        {
            currentCloudColor = Color.Lerp(currentCloudColor, targetCloudColor, t);
            currentCloudEmission = Color.Lerp(currentCloudEmission, targetCloudEmission, t);

            cloudMaterial.SetColor(CloudColorId, currentCloudColor);
            cloudMaterial.SetColor(CloudEmissionId, currentCloudEmission);
        }

        // Sun intensity and colour are already smooth functions of timeOfDay
        UpdateSunIntensity();
        UpdateSunColor();

        currentAmbientColor = Color.Lerp(currentAmbientColor, targetAmbientColor, t);
        currentAmbientIntensity = Mathf.Lerp(currentAmbientIntensity, targetAmbientIntensity, t);

        RenderSettings.ambientLight = currentAmbientColor;
        RenderSettings.ambientIntensity = currentAmbientIntensity;
    }

    private void UpdateParticleSystems()
    {
        // Defence only. particleStates is assigned unconditionally by
        // InitializeParticleStates, the last statement of Awake, and was measured
        // non-null (length 39) in a live session on 2026-09-04 - so this guard changes
        // no behaviour today. It exists so that an Awake that ever bails early degrades
        // to "particles do not switch by phase" instead of throwing every 10th of a
        // second on the UpdateVisuals path.
        if (particleStates == null) return;

        string currentPhase = GetCurrentPhase();
        
        for (int i = 0; i < particleStates.Length; i++)
        {
            var state = particleStates[i];
            if (state.system == null) continue;

            bool shouldPlay = ShouldParticleSystemPlay(state.system, currentPhase);
            
            if (shouldPlay != state.isPlaying)
            {
                if (shouldPlay)
                    state.system.Play();
                else
                    state.system.Stop();
                
                particleStates[i].isPlaying = shouldPlay;
            }
        }
    }

    private bool ShouldParticleSystemPlay(ParticleSystem system, string currentPhase)
    {
        // Find which particle system array this belongs to
        ParticleSystemControls controls = null;
        
        // Check each array without using Contains
        if (IsParticleInArray(system, subBeamParticles))
            controls = subBeamControls;
        else if (IsParticleInArray(system, duskParticles))
            controls = duskParticleControls;
        else if (IsParticleInArray(system, environmentParticles))
            controls = environmentControls;
        else if (IsParticleInArray(system, snowParticles))
            controls = snowControls;
        else if (IsParticleInArray(system, rainParticles))
            controls = rainControls;

        if (controls == null) return false;

        switch (currentPhase)
        {
            case "Night":
            case "Night to Dawn":
                return controls.playDuringNight;
            case "Dawn Hold":
            case "Dawn to Day":
                return controls.playDuringDawn;
            case "Day":
                return controls.playDuringDay;
            case "Day to Dusk":
            case "Dusk Hold":
                return controls.playDuringDusk;
            case "Dusk to Night":
                return controls.playDuringNight;
            default:
                return false;
        }
    }

    private bool IsParticleInArray(ParticleSystem system, ParticleSystem[] array)
    {
        if (array == null || system == null) return false;
        
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == system) return true;
        }
        return false;
    }

    private void UpdateSunRotation()
    {
        if (sunLight == null) return;

        float targetAngle;
        float t;

        if (timeOfDay < dawnStart)
            targetAngle = nightAngle;
        else if (timeOfDay < dawn)
        {
            t = (timeOfDay - dawnStart) / (dawn - dawnStart);
            targetAngle = Mathf.LerpAngle(nightAngle, dawnAngle, SmoothTransition(t));
        }
        else if (timeOfDay < dayStart)
            targetAngle = dawnAngle;
        else if (timeOfDay < dayTime)
        {
            t = (timeOfDay - dayStart) / (dayTime - dayStart);
            targetAngle = Mathf.LerpAngle(dawnAngle, dayAngle, SmoothTransition(t));
        }
        else if (timeOfDay < duskStart)
            targetAngle = dayAngle;
        else if (timeOfDay < dusk)
        {
            t = (timeOfDay - duskStart) / (dusk - duskStart);
            targetAngle = Mathf.LerpAngle(dayAngle, duskAngle, SmoothTransition(t));
        }
        else if (timeOfDay < nightStart)
            targetAngle = duskAngle;
        else if (timeOfDay < nightTime)
        {
            t = (timeOfDay - nightStart) / (nightTime - nightStart);
            targetAngle = Mathf.LerpAngle(duskAngle, nightAngle, SmoothTransition(t));
        }
        else
            targetAngle = nightAngle;

        // Apply rotation
        sunLight.transform.rotation = Quaternion.Euler(targetAngle, 0f, 0f);
    }

    private void UpdateSunIntensity()
    {
        float intensity;
        float t;
        
        if (timeOfDay < dawnStart)
        {
            // Night
            intensity = sunIntensityNight;
        }
        else if (timeOfDay < dawn)
        {
            // Night to Dawn
            t = (timeOfDay - dawnStart) / (dawn - dawnStart);
            t = SmoothTransition(t);
            intensity = Mathf.Lerp(sunIntensityNight, sunIntensityDawn, t);
        }
        else if (timeOfDay < dayStart)
        {
            // Hold at Dawn
            intensity = sunIntensityDawn;
        }
        else if (timeOfDay < dayTime)
        {
            // Dawn to Day
            t = (timeOfDay - dayStart) / (dayTime - dayStart);
            t = SmoothTransition(t);
            intensity = Mathf.Lerp(sunIntensityDawn, sunIntensityDay, t);
        }
        else if (timeOfDay < duskStart)
        {
            // Day
            intensity = sunIntensityDay;
        }
        else if (timeOfDay < dusk)
        {
            // Day to Dusk
            t = (timeOfDay - duskStart) / (dusk - duskStart);
            t = SmoothTransition(t);
            intensity = Mathf.Lerp(sunIntensityDay, sunIntensityDusk, t);
        }
        else if (timeOfDay < nightStart)
        {
            // Hold at Dusk
            intensity = sunIntensityDusk;
        }
        else if (timeOfDay < nightTime)
        {
            // Dusk to Night
            t = (timeOfDay - nightStart) / (nightTime - nightStart);
            t = SmoothTransition(t);
            intensity = Mathf.Lerp(sunIntensityDusk, sunIntensityNight, t);
        }
        else
        {
            // Night
            intensity = sunIntensityNight;
        }
        
        sunLight.intensity = intensity;
    }

    private void UpdateSunColor()
    {
        Color targetColor;
        float t;
        
        if (timeOfDay < dawnStart)
        {
            // Night
            targetColor = nightColor;
        }
        else if (timeOfDay < dawn)
        {
            // Night to Dawn
            t = (timeOfDay - dawnStart) / (dawn - dawnStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(nightColor, dawnColor, t);
        }
        else if (timeOfDay < dayStart)
        {
            // Hold at Dawn
            targetColor = dawnColor;
        }
        else if (timeOfDay < dayTime)
        {
            // Dawn to Day
            t = (timeOfDay - dayStart) / (dayTime - dayStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(dawnColor, dayColor, t);
        }
        else if (timeOfDay < duskStart)
        {
            // Day
            targetColor = dayColor;
        }
        else if (timeOfDay < dusk)
        {
            // Day to Dusk
            t = (timeOfDay - duskStart) / (dusk - duskStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(dayColor, duskColor, t);
        }
        else if (timeOfDay < nightStart)
        {
            // Hold at Dusk
            targetColor = duskColor;
        }
        else if (timeOfDay < nightTime)
        {
            // Dusk to Night
            t = (timeOfDay - nightStart) / (nightTime - nightStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(duskColor, nightColor, t);
        }
        else
        {
            // Night
            targetColor = nightColor;
        }

        sunLight.color = targetColor;
    }

    private void UpdateAmbientLight()
    {
        Color targetColor;
        float targetIntensity;
        float t;

        if (timeOfDay < dawnStart)
        {
            // Night
            targetColor = ambientNightColor;
            targetIntensity = ambientNightIntensity;
        }
        else if (timeOfDay < dawn)
        {
            // Night to Dawn
            t = (timeOfDay - dawnStart) / (dawn - dawnStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(ambientNightColor, ambientDawnColor, t);
            targetIntensity = Mathf.Lerp(ambientNightIntensity, ambientDawnIntensity, t);
        }
        else if (timeOfDay < dayStart)
        {
            // Hold at Dawn
            targetColor = ambientDawnColor;
            targetIntensity = ambientDawnIntensity;
        }
        else if (timeOfDay < dayTime)
        {
            // Dawn to Day
            t = (timeOfDay - dayStart) / (dayTime - dayStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(ambientDawnColor, ambientDayColor, t);
            targetIntensity = Mathf.Lerp(ambientDawnIntensity, ambientDayIntensity, t);
        }
        else if (timeOfDay < duskStart)
        {
            // Day
            targetColor = ambientDayColor;
            targetIntensity = ambientDayIntensity;
        }
        else if (timeOfDay < dusk)
        {
            // Day to Dusk
            t = (timeOfDay - duskStart) / (dusk - duskStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(ambientDayColor, ambientDuskColor, t);
            targetIntensity = Mathf.Lerp(ambientDayIntensity, ambientDuskIntensity, t);
        }
        else if (timeOfDay < nightStart)
        {
            // Hold at Dusk
            targetColor = ambientDuskColor;
            targetIntensity = ambientDuskIntensity;
        }
        else if (timeOfDay < nightTime)
        {
            // Dusk to Night
            t = (timeOfDay - nightStart) / (nightTime - nightStart);
            t = SmoothTransition(t);
            targetColor = Color.Lerp(ambientDuskColor, ambientNightColor, t);
            targetIntensity = Mathf.Lerp(ambientDuskIntensity, ambientNightIntensity, t);
        }
        else
        {
            // Night
            targetColor = ambientNightColor;
            targetIntensity = ambientNightIntensity;
        }

        RenderSettings.ambientLight = targetColor;
        RenderSettings.ambientIntensity = targetIntensity;
    }

    private float SmoothTransition(float t)
    {
        // Enhanced smoothing function for better transitions
        return t * t * (3f - 2f * t);
    }

    private void ValidateThresholds()
    {
        // Ensure thresholds are in correct order
        dawnStart = Mathf.Min(dawnStart, dawn);
        dawn = Mathf.Min(dawn, dayStart);
        dayStart = Mathf.Min(dayStart, dayTime);
        dayTime = Mathf.Min(dayTime, duskStart);
        duskStart = Mathf.Min(duskStart, dusk);
        dusk = Mathf.Min(dusk, nightStart);
        nightStart = Mathf.Min(nightStart, nightTime);
    }

    private string GetCurrentPhase()
    {
        if (timeOfDay < dawnStart) return "Night";
        if (timeOfDay < dawn) return "Night to Dawn";
        if (timeOfDay < dayStart) return "Dawn Hold";
        if (timeOfDay < dayTime) return "Dawn to Day";
        if (timeOfDay < duskStart) return "Day";
        if (timeOfDay < dusk) return "Day to Dusk";
        if (timeOfDay < nightStart) return "Dusk Hold";
        if (timeOfDay < nightTime) return "Dusk to Night";
        return "Night";
    }

    // Public methods for external control
    public void SetTimeOfDay(float newTime)
    {
        // If we're skipping from late night to early morning
        if (timeOfDay > nightTime && newTime < dayTransitionTime)
        {
            hasIncrementedDayToday = false;
        }
        
        timeOfDay = newTime;
    }

    public float GetTimeOfDay()
    {
        return timeOfDay;
    }

    public bool IsNight()
    {
        return isNighttime;
    }

    public float GetDawnStartTime()
    {
        return dawnStart;
    }

    public bool IsFullNight()
    {
        // Only return true during actual night time (after nightTime 0.868), not during dusk or transition
        bool isNight = timeOfDay >= nightTime || timeOfDay <= dawnStart;
        return isNight;
    }

    public float GetNightTime()
    {
        return nightTime;
    }

    public float GetDuskStartTime()
    {
        return duskStart;
    }

    public void SetTimeSpeedMultiplier(float _multiplier)
    {
        // Speed and pause are separate concerns. This used to clear the pause as a
        // side effect, so anything "resetting speed to 1" silently cancelled a pause
        // that gameplay was relying on.
        currentTimeMultiplier = Mathf.Max(0.1f, _multiplier);
    }

    public float GetTimeSpeedMultiplier()
    {
        return currentTimeMultiplier;
    }

    public void PauseTime()
    {
        if (!m_IsTimePaused)
        {
            m_IsTimePaused = true;
        }
    }

    public void ResumeTime()
    {
        if (m_IsTimePaused)
        {
            m_IsTimePaused = false;
        }
    }

    public bool IsTimePaused()
    {
        return m_IsTimePaused;
    }

    // Add public methods to access day information
    public int GetCurrentDay()
    {
        return dayNumber;
    }

    // Add method to set day number (for initialization/testing)
    public void SetDayNumber(int day)
    {
        dayNumber = day;
        OnDayChanged?.Invoke(dayNumber);
    }

    public float GetAmbientNightIntensity()
    {
        return ambientNightIntensity;
    }

    public void SetAmbientNightIntensity(float intensity)
    {
        ambientNightIntensity = intensity;
        UpdateAmbientLight(); // Refresh the ambient lighting
    }

    // Add helper methods for getting current colors
    private void GetCurrentSkyColors(out Color topColor, out Color bottomColor)
    {
        float t;

        if (timeOfDay < dawnStart)
        {
            topColor = nightTopColor;
            bottomColor = nightBottomColor;
        }
        else if (timeOfDay < dawn)
        {
            t = SmoothTransition((timeOfDay - dawnStart) / (dawn - dawnStart));
            topColor = Color.Lerp(nightTopColor, dawnTopColor, t);
            bottomColor = Color.Lerp(nightBottomColor, dawnBottomColor, t);
        }
        else if (timeOfDay < dayStart)
        {
            topColor = dawnTopColor;
            bottomColor = dawnBottomColor;
        }
        else if (timeOfDay < dayTime)
        {
            t = SmoothTransition((timeOfDay - dayStart) / (dayTime - dayStart));
            topColor = Color.Lerp(dawnTopColor, dayTopColor, t);
            bottomColor = Color.Lerp(dawnBottomColor, dayBottomColor, t);
        }
        else if (timeOfDay < duskStart)
        {
            topColor = dayTopColor;
            bottomColor = dayBottomColor;
        }
        else if (timeOfDay < dusk)
        {
            t = SmoothTransition((timeOfDay - duskStart) / (dusk - duskStart));
            topColor = Color.Lerp(dayTopColor, duskTopColor, t);
            bottomColor = Color.Lerp(dayBottomColor, duskBottomColor, t);
        }
        else if (timeOfDay < nightStart)
        {
            topColor = duskTopColor;
            bottomColor = duskBottomColor;
        }
        else if (timeOfDay < nightTime)
        {
            t = SmoothTransition((timeOfDay - nightStart) / (nightTime - nightStart));
            topColor = Color.Lerp(duskTopColor, nightTopColor, t);
            bottomColor = Color.Lerp(duskBottomColor, nightBottomColor, t);
        }
        else
        {
            topColor = nightTopColor;
            bottomColor = nightBottomColor;
        }
    }

    private void GetCurrentCloudColors(out Color cloudColor, out Color cloudEmission)
    {
        float t;

        if (timeOfDay < dawnStart)
        {
            cloudColor = cloudNightColor;
            cloudEmission = cloudNightEmission;
        }
        else if (timeOfDay < dawn)
        {
            t = SmoothTransition((timeOfDay - dawnStart) / (dawn - dawnStart));
            cloudColor = Color.Lerp(cloudNightColor, cloudDawnColor, t);
            cloudEmission = Color.Lerp(cloudNightEmission, cloudDawnEmission, t);
        }
        else if (timeOfDay < dayStart)
        {
            cloudColor = cloudDawnColor;
            cloudEmission = cloudDawnEmission;
        }
        else if (timeOfDay < dayTime)
        {
            t = SmoothTransition((timeOfDay - dayStart) / (dayTime - dayStart));
            cloudColor = Color.Lerp(cloudDawnColor, cloudDayColor, t);
            cloudEmission = Color.Lerp(cloudDawnEmission, cloudDayEmission, t);
        }
        else if (timeOfDay < duskStart)
        {
            cloudColor = cloudDayColor;
            cloudEmission = cloudDayEmission;
        }
        else if (timeOfDay < dusk)
        {
            t = SmoothTransition((timeOfDay - duskStart) / (dusk - duskStart));
            cloudColor = Color.Lerp(cloudDayColor, cloudDuskColor, t);
            cloudEmission = Color.Lerp(cloudDayEmission, cloudDuskEmission, t);
        }
        else if (timeOfDay < nightStart)
        {
            cloudColor = cloudDuskColor;
            cloudEmission = cloudDuskEmission;
        }
        else if (timeOfDay < nightTime)
        {
            t = SmoothTransition((timeOfDay - nightStart) / (nightTime - nightStart));
            cloudColor = Color.Lerp(cloudDuskColor, cloudNightColor, t);
            cloudEmission = Color.Lerp(cloudDuskEmission, cloudNightEmission, t);
        }
        else
        {
            cloudColor = cloudNightColor;
            cloudEmission = cloudNightEmission;
        }
    }

    private void GetCurrentAmbientLight(out Color ambientColor, out float intensity)
    {
        float t;

        if (timeOfDay < dawnStart)
        {
            ambientColor = ambientNightColor;
            intensity = ambientNightIntensity;
        }
        else if (timeOfDay < dawn)
        {
            t = SmoothTransition((timeOfDay - dawnStart) / (dawn - dawnStart));
            ambientColor = Color.Lerp(ambientNightColor, ambientDawnColor, t);
            intensity = Mathf.Lerp(ambientNightIntensity, ambientDawnIntensity, t);
        }
        else if (timeOfDay < dayStart)
        {
            ambientColor = ambientDawnColor;
            intensity = ambientDawnIntensity;
        }
        else if (timeOfDay < dayTime)
        {
            t = SmoothTransition((timeOfDay - dayStart) / (dayTime - dayStart));
            ambientColor = Color.Lerp(ambientDawnColor, ambientDayColor, t);
            intensity = Mathf.Lerp(ambientDawnIntensity, ambientDayIntensity, t);
        }
        else if (timeOfDay < duskStart)
        {
            ambientColor = ambientDayColor;
            intensity = ambientDayIntensity;
        }
        else if (timeOfDay < dusk)
        {
            t = SmoothTransition((timeOfDay - duskStart) / (dusk - duskStart));
            ambientColor = Color.Lerp(ambientDayColor, ambientDuskColor, t);
            intensity = Mathf.Lerp(ambientDayIntensity, ambientDuskIntensity, t);
        }
        else if (timeOfDay < nightStart)
        {
            ambientColor = ambientDuskColor;
            intensity = ambientDuskIntensity;
        }
        else if (timeOfDay < nightTime)
        {
            t = SmoothTransition((timeOfDay - nightStart) / (nightTime - nightStart));
            ambientColor = Color.Lerp(ambientDuskColor, ambientNightColor, t);
            intensity = Mathf.Lerp(ambientDuskIntensity, ambientNightIntensity, t);
        }
        else
        {
            ambientColor = ambientNightColor;
            intensity = ambientNightIntensity;
        }
    }
}