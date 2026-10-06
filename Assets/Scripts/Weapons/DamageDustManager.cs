using UnityEngine;

/// <summary>
/// The dust a damageable prop sheds: a puff at every hit, and a cloud the size of the object
/// when it dies.
///
/// This is what the owner chose INSTEAD of a destruction system (2026-09-06): the props still
/// simply vanish at zero health, and the job of the cloud is to be dense enough at the moment
/// of the vanish that the eye reads "it broke" rather than "it blinked". So the cloud fills the
/// object's own bounds on the frame it is emitted, in an alpha-BLENDED material that actually
/// occludes what is behind it; the additive dust the pack uses for bullet impacts looks right
/// for a puff and hides nothing.
///
/// Two ParticleSystems for the whole level, built in code at startup and simulated in WORLD
/// space, so a hit anywhere is one Emit call with a position and nothing is instantiated
/// per shot. BulletImpactManager instantiates a prefab per hit and destroys it a second later;
/// at the minigun's 20 rounds a second that is 20 objects a second churning, which this
/// deliberately does not add to. The per-hit puff also GROWS as the object weakens - a cracked
/// wall sheds more than a fresh one - which is the one progressive cue left in the descope.
///
/// Colour is per surface tag, the same tags BulletImpactManager keys its impacts on, so a
/// prop that already sounds like rock now also dusts like rock. The two materials are the
/// pack's own (PolygonParticleFX_Cutout_Round for the puff, its VertexLit alpha-blended sibling
/// for the cloud), wired by Tools/Level/Add Damage Dust rather than looked up at runtime.
///
/// Scene-lifetime singleton, like the other managers here: no DontDestroyOnLoad, and every
/// caller null-checks Instance at the dereference, because it is null for the first frames
/// and after a reload.
/// </summary>
public class DamageDustManager : MonoBehaviour
{
    public static DamageDustManager Instance { get; private set; }

    /// <summary>What colour a surface's dust is. Looked up by the prop's tag.</summary>
    [System.Serializable]
    public class SurfaceDust
    {
        [Tooltip("The prop's tag - the same one BulletImpactManager keys its impact sounds on.")]
        public string surfaceTag = "Rock";

        [Tooltip("Dust colour for this surface. Alpha is the cloud's peak opacity.")]
        public Color colour = new Color(0.75f, 0.67f, 0.52f, 0.9f);
    }

    [Header("Materials (wired by Tools/Level/Add Damage Dust)")]

    [Tooltip("Additive soft round - the pack's own bullet-impact dust. For the per-hit puff.")]
    [SerializeField] private Material puffMaterial;

    [Tooltip("Alpha-blended round - the pack's VertexLit variant. For the death cloud, because " +
             "it has to HIDE the object vanishing and additive dust hides nothing.")]
    [SerializeField] private Material cloudMaterial;

    [Header("Colour by surface")]

    [SerializeField] private SurfaceDust[] surfaces =
    {
        new SurfaceDust { surfaceTag = "Rock", colour = new Color(0.72f, 0.68f, 0.60f, 0.55f) },
        new SurfaceDust { surfaceTag = "Wood", colour = new Color(0.55f, 0.42f, 0.27f, 0.50f) },
        new SurfaceDust { surfaceTag = "Dirt", colour = new Color(0.58f, 0.48f, 0.36f, 0.55f) },
        new SurfaceDust { surfaceTag = "Metal", colour = new Color(0.60f, 0.60f, 0.62f, 0.40f) },
    };

    [Tooltip("Dust for a prop whose tag is not listed above.")]
    [SerializeField] private Color defaultColour = new Color(0.70f, 0.66f, 0.60f, 0.50f);

    [Tooltip("Per-particle brightness variation, 0..1. Zero is a flat slab of one colour; a " +
             "little grain is what makes it read as dust rather than paint.")]
    [Range(0f, 0.5f)] [SerializeField] private float colourVariation = 0.18f;

    [Header("Per-hit puff")]

    [Tooltip("Particles for a hit that does 10% of the object's health, on an undamaged object.")]
    [Min(0)] [SerializeField] private int puffBaseCount = 18;

    [Tooltip("How much a bigger hit adds: count scales with (damage / maxHealth) / 0.1, up to " +
             "this many times the base. A grenade against a wall is a much bigger puff than a round.")]
    [Min(1f)] [SerializeField] private float puffDamageScaleMax = 4f;

    [Tooltip("How much a WEAKENED object adds: at zero health remaining the puff is this many " +
             "times bigger. The one progressive cue left - a cracked wall sheds more.")]
    [Min(1f)] [SerializeField] private float puffWeaknessScaleMax = 2.5f;

    [Min(1)] [SerializeField] private int puffMaxCount = 60;

    [SerializeField] private Vector2 puffSize = new Vector2(0.20f, 0.45f);
    [SerializeField] private Vector2 puffLifetime = new Vector2(0.6f, 1.3f);

    [Tooltip("Metres per second away from the surface, along the hit normal.")]
    [SerializeField] private Vector2 puffSpeed = new Vector2(0.6f, 2.4f);

    [Tooltip("Random spread added to the puff's velocity, so it is a plume and not a jet.")]
    [Min(0f)] [SerializeField] private float puffSpread = 1.0f;

    [Header("Death cloud")]

    [Tooltip("Particles per cubic metre of the object's bounds. The cloud has to be dense on " +
             "its first frame - that frame is the one the object vanishes on.")]
    [Min(0f)] [SerializeField] private float cloudDensity = 70f;

    [Min(1)] [SerializeField] private int cloudMinCount = 50;
    [Min(1)] [SerializeField] private int cloudMaxCount = 260;

    [Tooltip("Particle size in METRES, min..max. Absolute, not a fraction of the object: the " +
             "first cut scaled by the longest edge and a 3.4 m wall threw 1.5 m particles that " +
             "whited out the screen. Density handles the big objects; size stays dust-sized.")]
    [SerializeField] private Vector2 cloudSize = new Vector2(0.30f, 0.65f);

    [SerializeField] private Vector2 cloudLifetime = new Vector2(1.4f, 2.6f);

    [Tooltip("Outward drift from the object's centre, m/s. Slow: rubble dust billows, it does not explode.")]
    [SerializeField] private Vector2 cloudOutwardSpeed = new Vector2(0.3f, 1.1f);

    [Tooltip("Upward drift, m/s, on top of the outward drift.")]
    [SerializeField] private Vector2 cloudRiseSpeed = new Vector2(0.2f, 0.6f);

    [Tooltip("A second, smaller burst of the impact puff at the object's base, so the cloud " +
             "has a 'hit the ground' skirt. Zero disables it.")]
    [Min(0)] [SerializeField] private int cloudSkirtCount = 40;

    [Header("Budget")]

    [Tooltip("Hard cap on live particles per system. Emit() past this drops the oldest, which " +
             "is the right failure: the newest dust is the one being looked at.")]
    [Min(100)] [SerializeField] private int maxParticlesPerSystem = 3000;

    private ParticleSystem puffs;
    private ParticleSystem clouds;
    private ParticleSystem.EmitParams emit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[{nameof(DamageDustManager)}] A second instance on '{name}' - " +
                             $"'{Instance.name}' already owns the dust. Destroying this one.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        puffs = BuildSystem("Dust Puffs", puffMaterial, softFade: true);
        clouds = BuildSystem("Dust Clouds", cloudMaterial, softFade: true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ==========================================================================
    //  The two calls DamageableObject makes
    // ==========================================================================

    /// <summary>
    /// A puff at the point of a hit. <paramref name="damageFraction"/> is this hit's damage over
    /// max health, <paramref name="healthFraction"/> is what is LEFT after it (0 = dead).
    /// </summary>
    public void Puff(Vector3 point, Vector3 normal, string surfaceTag, float damageFraction, float healthFraction)
    {
        if (puffs == null) return;

        // Base is tuned for a 10% hit; a bigger hit scales up to puffDamageScaleMax, a weaker
        // object up to puffWeaknessScaleMax, and the product is capped so a grenade on a
        // nearly-dead wall does not fire a thousand particles the frame before the cloud.
        float damageScale = Mathf.Clamp(damageFraction / 0.1f, 0.5f, puffDamageScaleMax);
        float weaknessScale = Mathf.Lerp(1f, puffWeaknessScaleMax, 1f - Mathf.Clamp01(healthFraction));
        int count = Mathf.Min(puffMaxCount, Mathf.RoundToInt(puffBaseCount * damageScale * weaknessScale));

        Color colour = ColourFor(surfaceTag);
        Vector3 along = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;

        for (int i = 0; i < count; i++)
        {
            emit.position = point + Random.insideUnitSphere * 0.08f;
            emit.velocity = along * Random.Range(puffSpeed.x, puffSpeed.y) + Random.insideUnitSphere * puffSpread;
            emit.startSize = Random.Range(puffSize.x, puffSize.y);
            emit.startLifetime = Random.Range(puffLifetime.x, puffLifetime.y);
            emit.startColor = Vary(colour);
            emit.rotation = Random.Range(0f, 360f);
            puffs.Emit(emit, 1);
        }
    }

    /// <summary>
    /// The cloud that stands in for the object on the frame it vanishes: fills its bounds,
    /// drifts outward and up, and fades. Call BEFORE the object is destroyed, with its
    /// renderer bounds - after Destroy there is nothing left to measure.
    /// </summary>
    public void Cloud(Bounds bounds, string surfaceTag)
    {
        if (clouds == null) return;

        Vector3 size = bounds.size;
        float volume = Mathf.Max(0.05f, size.x * size.y * size.z);
        float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        int count = Mathf.Clamp(Mathf.RoundToInt(volume * cloudDensity), cloudMinCount, cloudMaxCount);

        Color colour = ColourFor(surfaceTag);
        Vector3 centre = bounds.center;

        for (int i = 0; i < count; i++)
        {
            // Uniform through the box, not a sphere at the centre: a wall is long and thin,
            // and a ball of dust in its middle leaves both ends vanishing in clear air.
            Vector3 position = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));

            Vector3 outward = position - centre;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Random.insideUnitSphere;

            emit.position = position;
            emit.velocity = outward * Random.Range(cloudOutwardSpeed.x, cloudOutwardSpeed.y)
                            + Vector3.up * Random.Range(cloudRiseSpeed.x, cloudRiseSpeed.y);
            emit.startSize = Random.Range(cloudSize.x, cloudSize.y);
            emit.startLifetime = Random.Range(cloudLifetime.x, cloudLifetime.y);
            emit.startColor = Vary(colour);
            emit.rotation = Random.Range(0f, 360f);
            clouds.Emit(emit, 1);
        }

        // The skirt: impact-style dust kicked out along the ground from the base, so the
        // collapse reads as having hit something.
        if (puffs != null && cloudSkirtCount > 0)
        {
            Color skirt = colour;
            skirt.a = 1f;

            for (int i = 0; i < cloudSkirtCount; i++)
            {
                Vector3 flat = Random.insideUnitCircle.normalized;
                Vector3 along = new Vector3(flat.x, 0.15f, flat.y);

                emit.position = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x),
                    bounds.min.y + 0.05f,
                    Random.Range(bounds.min.z, bounds.max.z));
                emit.velocity = along * Random.Range(puffSpeed.x, puffSpeed.y * 1.5f);
                emit.startSize = Random.Range(puffSize.x, puffSize.y) * Mathf.Max(1f, largest * 0.5f);
                emit.startLifetime = Random.Range(puffLifetime.x, puffLifetime.y);
                emit.startColor = Vary(skirt);
                emit.rotation = Random.Range(0f, 360f);
                puffs.Emit(emit, 1);
            }
        }
    }

    /// <summary>Live particle counts, for the setup tool's play-mode check.</summary>
    public int LivePuffs => puffs != null ? puffs.particleCount : 0;
    public int LiveClouds => clouds != null ? clouds.particleCount : 0;

    // ==========================================================================
    //  Internals
    // ==========================================================================

    /// <summary>The colour, a little lighter or darker, alpha untouched.</summary>
    private Color Vary(Color colour)
    {
        float k = 1f + Random.Range(-colourVariation, colourVariation);
        return new Color(colour.r * k, colour.g * k, colour.b * k, colour.a);
    }

    private Color ColourFor(string surfaceTag)
    {
        if (surfaces != null && !string.IsNullOrEmpty(surfaceTag))
        {
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null && surfaces[i].surfaceTag == surfaceTag) return surfaces[i].colour;
            }
        }

        return defaultColour;
    }

    /// <summary>
    /// One world-space system that never emits on its own. Everything a burst needs is set
    /// per particle through EmitParams; the system only carries what is the same for all of
    /// them - the material, the fade, a touch of drag so dust slows, and a little gravity so
    /// a puff settles instead of hanging in the air.
    /// </summary>
    private ParticleSystem BuildSystem(string childName, Material material, bool softFade)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);

        ParticleSystem system = child.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = maxParticlesPerSystem;
        main.gravityModifier = 0.06f;
        main.startSpeed = 0f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        // Drag: dust decelerates fast, which is why a puff blooms and stops rather than
        // streaking. LimitVelocityOverLifetime with drag is the cheap way to say so.
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = 2.5f;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = true;

        // Grow a little and fade out, the shape every dust cloud has.
        ParticleSystem.SizeOverLifetimeModule sizeOverLife = system.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.35f)));

        if (softFade)
        {
            ParticleSystem.ColorOverLifetimeModule colourOverLife = system.colorOverLifetime;
            colourOverLife.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.3f), new GradientAlphaKey(0f, 1f) });
            colourOverLife.color = gradient;
        }

        // A slow tumble, so a big cloud particle is not a static disc.
        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (material != null) renderer.sharedMaterial = material;
        else Debug.LogWarning($"[{nameof(DamageDustManager)}] '{childName}' has no material - run " +
                              "Tools/Level/Add Damage Dust to wire the pack's dust materials.", this);

        return system;
    }
}
