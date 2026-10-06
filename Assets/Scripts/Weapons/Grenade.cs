using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * Grenade.cs
 *
 * Purpose: The thrown projectile - flight, bounces, the fuse, and the blast that follows.
 * Used by: GrenadeController, which instantiates one per throw and calls Launch. The
 *          component lives on Assets/Inbox/Prefabs/Grenade.prefab alongside a Rigidbody
 *          and a SphereCollider (built by Tools/Weapons/Build Grenade).
 *
 * Key Features:
 * - Launch ignores the thrower's own colliders, so a grenade cannot bounce off the player
 *   who just threw it
 * - Bounces reuse the surface impacts the students already author for bullets, through
 *   BulletImpactManager, at the surface actually struck
 * - One detonation, ever, whether it came from the fuse or the max-flight safety net
 * - Radius damage with a line-of-sight test (walls block), deduped per IDamageable, with a
 *   linear falloff from the centre to the edge
 * - Self-damage: the player takes falloff damage through the existing directional blood,
 *   plus a camera shake and a ringing tail scaled by how close they were standing
 *
 * Audio notes - this is a sound-design assessment project, so they are the point:
 * - This prefab carries NO AudioSource and NO serialized AudioClip. Its two assessed
 *   sounds (Throw and Explosion) are authored on the WeaponAudioManager profile like every
 *   other weapon sound, so they appear in the Ctrl+G report and are reset by the handout
 *   tool. An authored clip sitting on the prefab would be invisible to both.
 * - The explosion goes through the manager's DETACHED 3D one-shot: this object is
 *   destroyed on the same frame it explodes, so a source parented to it would be cut off
 *   mid-clip. It is also the first weapon sound in the level that happens away from the
 *   camera, which is exactly why the grenade is worth having.
 * - The ringing is generated in code rather than authored - see GetRingClip for why.
 *
 * Dependencies:
 * - WeaponAudioManager (PlayExplosionSound), BulletImpactManager (bounce impacts)
 * - DamageableUtil / IDamageable, ZombieAI, PlayerStats
 * - CameraShakeController
 */
public class Grenade : MonoBehaviour
{
    [Header("Fuse and Flight")]
    [Tooltip("Seconds from the throw to the blast. The owner's decision: a fixed fuse " +
             "from release, no cooking and no impact detonation")]
    [Min(0.1f)] [SerializeField] private float fuseSeconds = 3f;

    [Tooltip("Safety net, in seconds. A grenade that was spawned but never Launched has no " +
             "fuse running, and without this it would sit in the scene forever - the same " +
             "leak Arrow.maxFlightTime closes. Keep it comfortably above Fuse Seconds")]
    [Min(0.1f)] [SerializeField] private float maxFlightTime = 8f;

    [Header("Bounces")]
    [Tooltip("Relative speed below which a contact is a roll or a settle rather than a " +
             "bounce, and stays silent. Without this a grenade coming to rest machine-guns " +
             "the surface impact sound")]
    [Min(0f)] [SerializeField] private float bounceMinSpeed = 1f;

    [Tooltip("Seconds between audible bounces. One physics step can deliver several " +
             "contacts against the same floor")]
    [Min(0f)] [SerializeField] private float bounceCooldown = 0.15f;

    [Header("Blast")]
    [Tooltip("Metres. Everything damageable inside this sphere that the blast can see " +
             "takes damage - the player included")]
    [Min(0.1f)] [SerializeField] private float radius = 5f;

    [Tooltip("Damage at the very centre of the blast")]
    [Min(0f)] [SerializeField] private float centreDamage = 120f;

    [Tooltip("Fraction of Centre Damage still dealt at the edge of the radius. The falloff " +
             "between the two is linear")]
    [Range(0f, 1f)] [SerializeField] private float edgeFraction = 0.25f;

    [Tooltip("Spawned at the blast point. FX_Grenade_Explosive_01 from the pack: five " +
             "non-looping particle systems and a light, and deliberately no AudioSource")]
    [SerializeField] private GameObject explosionEffectPrefab;

    [Tooltip("Seconds before the effect instance is destroyed. The pack's systems all run " +
             "2 s, so this only has to outlast them")]
    [Min(0f)] [SerializeField] private float explosionEffectLifetime = 2.5f;

    [Header("Player Feedback")]
    [Tooltip("Camera shake at the centre of the blast. Magnitude is scaled down towards " +
             "the edge; only a player actually caught in the radius is shaken")]
    [Min(0f)] [SerializeField] private float shakeDuration = 0.35f;
    [Min(0f)] [SerializeField] private float shakeMagnitude = 0.25f;
    [Min(0f)] [SerializeField] private float shakeRoughness = 12f;
    [SerializeField] private Vector3 shakeDirection = new Vector3(0.3f, 0.5f, 0.3f);

    [Tooltip("Pitch of the ringing tail, in Hz. Generated, not a clip - see the note on " +
             "GetRingClip")]
    [Min(50f)] [SerializeField] private float ringFrequency = 3000f;

    [Tooltip("Seconds the ringing takes to fade out")]
    [Min(0.1f)] [SerializeField] private float ringDuration = 1.5f;

    [Tooltip("Loudest the ringing ever gets, at the centre of the blast. It sits under the " +
             "explosion, it is not a second explosion")]
    [Range(0f, 1f)] [SerializeField] private float maxRingVolume = 0.35f;

    private Rigidbody body;
    private Collider ownCollider;
    private WeaponAudioManager audioManager;
    private string audioProfileKey;

    private bool detonated;
    private float nextBounceTime;

    /// <summary>
    /// The generated ringing, built once and shared. Reset on load with the project's other
    /// reset-on-load statics: the clip is a UnityEngine.Object, so a cached one survives
    /// into the next play session in the editor as a destroyed reference.
    /// </summary>
    private static AudioClip ringClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ringClip = null;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        ownCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        // Runs whether or not Launch was called, so a grenade spawned by hand in the editor
        // still cleans itself up. Launch's fuse is much shorter, and Detonate is one-shot,
        // so in normal play this coroutine never gets to the end.
        StartCoroutine(MaxFlightRoutine());
    }

    /// <summary>
    /// Hands the grenade its velocity and starts the fuse. The ignore list is the thrower's
    /// own colliders: the grenade spawns inside the player's capsule, and without this the
    /// first thing it hits is the person who threw it - it bounces straight back at their
    /// feet, which reads as a bug rather than a short throw.
    /// </summary>
    /// <param name="velocity">World-space launch velocity, set directly rather than as a
    /// force so the arc does not depend on the prefab's mass.</param>
    /// <param name="ignore">Colliders the grenade may pass through - the thrower's.</param>
    /// <param name="audio">The scene's WeaponAudioManager, carried so the blast can be
    /// heard after the controller has stopped caring about this grenade.</param>
    /// <param name="audioKey">The thrower's profile name, so the explosion is authored on
    /// the same profile as the throw rather than on a profile of its own.</param>
    public void Launch(Vector3 velocity, Collider[] ignore, WeaponAudioManager audio, string audioKey)
    {
        audioManager = audio;
        audioProfileKey = audioKey;

        if (body == null) body = GetComponent<Rigidbody>();
        if (ownCollider == null) ownCollider = GetComponent<Collider>();

        if (ownCollider != null && ignore != null)
        {
            foreach (Collider other in ignore)
            {
                // Physics.IgnoreCollision logs an error for a collider that is disabled or
                // on an inactive object, so both are checked rather than caught.
                if (other == null || other == ownCollider) continue;
                if (!other.enabled || !other.gameObject.activeInHierarchy) continue;

                Physics.IgnoreCollision(ownCollider, other);
            }
        }

        if (body != null)
        {
            body.linearVelocity = velocity;

            // A little tumble, so the can does not fly like a dart. Cosmetic only.
            body.angularVelocity = Random.insideUnitSphere * 6f;
        }

        StartCoroutine(FuseRoutine());
    }

    private IEnumerator FuseRoutine()
    {
        // No fuse sound: the owner's decision, and the reason the grenade has exactly two
        // assessed audio slots rather than three.
        yield return new WaitForSeconds(fuseSeconds);
        Detonate();
    }

    private IEnumerator MaxFlightRoutine()
    {
        yield return new WaitForSeconds(maxFlightTime);
        Detonate();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (detonated) return;

        // A grenade settling on the floor delivers a stream of contacts; only the ones that
        // arrive with real speed, and no faster than the cooldown, are heard as bounces.
        if (Time.time < nextBounceTime) return;
        if (collision.relativeVelocity.magnitude < bounceMinSpeed) return;

        nextBounceTime = Time.time + bounceCooldown;

        if (BulletImpactManager.Instance == null) return;

        // contactCount is 0 only in the degenerate case where the pair separated in the
        // same step - which this body can produce, being ContinuousDynamic
        // (Grenade.prefab) where a sweep hit can report no contacts at all. The same guard
        // ZombieSpit.cs:288-289 carries.
        Vector3 point = collision.contactCount > 0
            ? collision.GetContact(0).point : transform.position;
        Vector3 normal = collision.contactCount > 0
            ? collision.GetContact(0).normal : Vector3.up;

        // The impact manager is tag-driven and takes a RaycastHit; a Collision is not one,
        // so the surface has to be re-found by casting back into it from just outside.
        // Scoped to the collider we actually hit (Collider.Raycast) rather than the world:
        // the origin sits inside the grenade's own sphere, and a world cast from in there
        // is not reliably reported.
        Ray ray = new Ray(point + normal * 0.05f, -normal);
        if (collision.collider.Raycast(ray, out RaycastHit hit, 0.2f))
        {
            BulletImpactManager.Instance.PlayImpactEffect(hit);
        }

        // A miss is silence. Fabricating a RaycastHit would hand the impact manager a
        // surface tag it never actually touched, and the wrong authored sound is worse
        // than none in an assessment about which sound belongs where.
    }

    /// <summary>
    /// The blast, exactly once. Order matters: the sound leaves first (it is detached, and
    /// this object is gone by the end of the method), then the effect, then the damage
    /// query, then the player's own feedback, then the grenade itself.
    /// </summary>
    private void Detonate()
    {
        if (detonated) return;
        detonated = true;

        StopAllCoroutines();

        Vector3 blastPoint = transform.position;

        // Out of every query below in one line: the grenade is not its own blast victim,
        // and its sphere must not be able to shadow a target from the line-of-sight test.
        if (ownCollider != null) ownCollider.enabled = false;

        if (audioManager != null)
        {
            audioManager.PlayExplosionSound(audioProfileKey, blastPoint);
        }

        if (explosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(explosionEffectPrefab, blastPoint, Quaternion.identity);
            Destroy(effect, explosionEffectLifetime);
        }

        ApplyBlastDamage(blastPoint);

        Destroy(gameObject);
    }

    /// <summary>
    /// Damages everything the blast can see inside the radius, then hands the player their
    /// own feedback if they were one of them.
    /// </summary>
    private void ApplyBlastDamage(Vector3 blastPoint)
    {
        // Everything, on purpose. A curated mask would silently let a layer nobody
        // remembered to tick take damage through a wall, and the line-of-sight test below
        // is the thing that is supposed to decide that. Triggers are ignored: an inventory
        // pickup radius or a music trigger box is not cover, and not a target.
        Collider[] caught = Physics.OverlapSphere(blastPoint, radius, ~0, QueryTriggerInteraction.Ignore);

        // A zombie carries several colliders (its own are added at runtime), so without
        // this the same target takes the blast once per limb.
        HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

        bool playerCaught = false;
        float playerProximity = 0f;

        // The player is resolved BEFORE the loop and by name, not through the overlap.
        //
        // ⚠ The overlap can never find them. PlayerStats sits on Player/PlayerStats, a
        // CHILD, while the only body collider is the CharacterController on Player, the
        // PARENT - and DamageableUtil looks at the collider's own object and then walks
        // UP. It never looks down. ZombieAI hits the same wall and works around it the
        // same way (GetComponentInChildren, ZombieAI.cs ~:431). Left to the loop, a
        // grenade at the player's feet did exactly nothing, which is how this was found.
        //
        // Added to alreadyHit either way, so if a future collider ever does resolve to
        // PlayerStats the loop cannot damage them a second time.
        PlayerStats player = PlayerStats.Instance;
        if (player != null)
        {
            // The capsule's middle, not the object's origin at the feet: a blast level
            // with the player's chest is not 1 m further away than one at their boots.
            Transform playerRoot = player.transform.root;
            CharacterController body = playerRoot.GetComponent<CharacterController>();
            Vector3 playerCentre = body != null
                ? playerRoot.TransformPoint(body.center)
                : playerRoot.position + Vector3.up;

            float playerDistance = Vector3.Distance(blastPoint, playerCentre);
            if (playerDistance <= radius && HasLineOfSightTo(blastPoint, playerCentre, body))
            {
                float falloff = Mathf.Lerp(1f, edgeFraction, Mathf.Clamp01(playerDistance / radius));

                // The Transform overload, so the existing edge-of-screen blood swings round
                // to the grenade. It is read once here and cached by the indicator, so
                // destroying this object at the end of Detonate cannot strand it.
                player.TakeDamage(centreDamage * falloff, transform);

                playerCaught = true;
                playerProximity = 1f - Mathf.Clamp01(playerDistance / radius);
            }

            alreadyHit.Add(player);
        }

        foreach (Collider hitCollider in caught)
        {
            if (hitCollider == null) continue;
            if (!DamageableUtil.TryGetDamageable(hitCollider, out IDamageable target)) continue;

            // Contains, not Add: a limb hidden behind a wall must not use up the target's
            // one slot and leave the limb that IS exposed unhurt.
            if (alreadyHit.Contains(target)) continue;
            if (!HasLineOfSight(blastPoint, hitCollider, target, out Vector3 hitPoint)) continue;

            alreadyHit.Add(target);

            float distance = Vector3.Distance(blastPoint, hitCollider.bounds.center);
            float falloff = Mathf.Lerp(1f, edgeFraction, Mathf.Clamp01(distance / radius));
            float damage = centreDamage * falloff;

            // Positional, so the head band pays double exactly as it does for a bullet - a
            // grenade under the chin should not be worth less - and a prop's dust leaves it
            // AWAY from the blast, which is the direction the pressure went.
            Vector3 away = hitPoint - blastPoint;
            DamageableUtil.Damage(target, damage, hitPoint,
                                  away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.up);
        }

        // Only for a player who was actually in it. The shake and the ringing are how the
        // blast felt from where they were standing, not an event the whole level gets.
        if (!playerCaught) return;

        if (CameraShakeController.Instance != null)
        {
            CameraShakeController.Instance.ShakeCamera(
                shakeDuration,
                shakeMagnitude * playerProximity,
                shakeRoughness,
                shakeDirection);
        }

        PlayRinging(playerProximity);
    }

    /// <summary>
    /// Line of sight to a point rather than to a collider, for the player - whose damageable
    /// component is not on the thing the ray can hit, so the collider-based test below has
    /// nothing to compare its result against. The player's own capsule is not cover.
    /// </summary>
    private bool HasLineOfSightTo(Vector3 blastPoint, Vector3 target, Collider ignore)
    {
        Vector3 toTarget = target - blastPoint;
        float distance = toTarget.magnitude;
        if (distance <= 0.01f) return true;

        RaycastHit[] blockers = Physics.RaycastAll(blastPoint, toTarget / distance, distance,
                                                   ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit blocker in blockers)
        {
            if (blocker.collider == null) continue;
            if (blocker.collider == ignore) continue;
            if (blocker.collider == ownCollider) continue;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the blast can reach this collider without a wall in the way, and where on it
    /// the blast lands. Anything solid between the two that is not part of the target
    /// itself blocks - a zombie's own second collider is not cover.
    /// </summary>
    private bool HasLineOfSight(Vector3 blastPoint, Collider hitCollider, IDamageable target, out Vector3 hitPoint)
    {
        hitPoint = hitCollider.bounds.center;

        Vector3 toTarget = hitPoint - blastPoint;
        float distance = toTarget.magnitude;

        // Detonating inside a target: nothing can be between them.
        if (distance < 0.01f) return true;

        if (!Physics.Raycast(blastPoint, toTarget / distance, out RaycastHit blocker, distance,
                             ~0, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        // Resolved the same way the damage is, so a limb, a ragdoll bone and the root all
        // count as the target rather than as the wall in front of it.
        if (DamageableUtil.TryGetDamageable(blocker.collider, out IDamageable blocking) && blocking == target)
        {
            // The surface the blast actually struck, which is where a zombie's hit point
            // should be measured.
            hitPoint = blocker.point;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The ringing left behind by a close blast, played 2D on a throwaway host.
    /// </summary>
    /// <remarks>
    /// GENERATED, deliberately. The owner authored exactly two assessed audio slots for the
    /// grenade - Throw and Explosion - and a ringing tail is an EFFECT of the blast rather
    /// than a sound anyone authors. A serialized AudioClip field here would become a third
    /// row in the Ctrl+G report, a third thing the handout tool resets to a beep, and a
    /// third thing a student would be marked on. A sine built in code is none of those, and
    /// it costs one array of floats, once per session.
    /// </remarks>
    private void PlayRinging(float proximity)
    {
        AudioClip clip = GetRingClip(ringFrequency, ringDuration);
        if (clip == null) return;

        float volume = Mathf.Clamp(maxRingVolume * proximity, 0f, maxRingVolume);
        if (volume <= 0.001f) return;

        // HideAndDontSave: this host exists for the length of one clip and must never be
        // saved into the scene or shown in the hierarchy. It destroys itself below.
        GameObject host = new GameObject("Grenade Ringing");
        host.hideFlags = HideFlags.HideAndDontSave;

        AudioSource source = host.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.playOnAwake = false;

        // On the weapon bus, like the detached explosion host beside it. A runtime source
        // left on Master ignores the SFX fader and the options-menu volume slider, so the
        // ring was the one part of a blast the player could not turn down.
        if (audioManager != null) source.outputAudioMixerGroup = audioManager.OutputGroup;

        // 2D on purpose: the ringing happens in the player's ears, not at the blast. The
        // blast itself is the 3D sound, and it is the authored one.
        source.spatialBlend = 0f;
        source.Play();

        Destroy(host, clip.length + 0.1f);
    }

    /// <summary>
    /// Builds the ringing once and keeps it: a sine at Ring Frequency with a linear fade to
    /// silence, mono, at the output sample rate so nothing has to resample it.
    /// </summary>
    private static AudioClip GetRingClip(float frequency, float duration)
    {
        if (ringClip != null) return ringClip;

        int sampleRate = AudioSettings.outputSampleRate;
        if (sampleRate <= 0) sampleRate = 48000;

        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
        float[] samples = new float[sampleCount];

        // Phase is accumulated and wrapped rather than multiplied out. At 3 kHz over 1.5 s
        // the raw angle reaches ~1.4 million radians, where a float has lost enough
        // precision to audibly distort the sine.
        float step = 2f * Mathf.PI * frequency / sampleRate;
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            // Linear fade rather than a curve: a ring that ends abruptly clicks, and the
            // point of it is to get out of the way of the explosion, not to be noticed.
            float fade = 1f - (float)i / sampleCount;
            samples[i] = Mathf.Sin(phase) * fade;

            phase += step;
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
        }

        ringClip = AudioClip.Create("GrenadeRinging", sampleCount, 1, sampleRate, false);
        ringClip.SetData(samples, 0);
        return ringClip;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }
}
