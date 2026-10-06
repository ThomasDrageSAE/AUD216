using UnityEngine;

/// <summary>
/// A prop the player can break: stone walls, pillars, stumps, logs, signs - 40 of them in the
/// level, tagged Rock or Wood so the impact manager already knows what they sound like.
///
/// It still simply vanishes at zero health. A destruction system was scoped and declined
/// (2026-09-06, too ambitious for now); what was asked for instead is that the props shed
/// MORE dust with every hit, and a cloud the size of the object at the moment it goes, so the
/// vanish is hidden inside it. Both come from <see cref="DamageDustManager"/>; this class only
/// knows where it was hit and how big it is.
///
/// The positional overload is the one worth calling. The interface's flat
/// <see cref="TakeDamage(float)"/> has no hit point, so a caller that only has that puts the
/// puff at the object's centre - which for a 4 m wall is a puff in the middle of a wall hit at
/// its end. Every weapon in this level goes through <see cref="DamageableUtil.Damage"/> and
/// passes the point; the flat method exists for callers that genuinely have none.
/// </summary>
public class DamageableObject : MonoBehaviour, IDamageable
{
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("Dust for hits and for the collapse. Off for a prop that should just go - a " +
             "hanging sign, say - or when the dust manager is absent from the scene.")]
    [SerializeField] private bool emitDust = true;

    private float currentHealth;
    private bool isDead;

    /// <summary>What is left, 0..1. Read by the setup tool's play check.</summary>
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    private void Start()
    {
        currentHealth = maxHealth;
    }

    /// <summary>The flat interface call: no hit point, so the puff sits at the centre.</summary>
    public void TakeDamage(float damage)
    {
        Bounds bounds = RendererBounds();
        TakeDamage(damage, bounds.center, Vector3.up);
    }

    /// <summary>
    /// Damage at a known point. <paramref name="hitNormal"/> is the direction the dust leaves
    /// the surface - the raycast's normal for a shot, the direction away from the blast for a
    /// grenade, straight up when nobody knows.
    /// </summary>
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        // Destroy() is deferred to end of frame, so without this guard a second hit in
        // the same frame re-entered Die(). ZombieAI guards the same way.
        if (isDead) return;

        currentHealth -= damage;

        // The puff is sized by how hard the hit was AND by how little is left, so the last
        // few rounds into a wall visibly shake more out of it than the first.
        if (emitDust && DamageDustManager.Instance != null && maxHealth > 0f)
        {
            DamageDustManager.Instance.Puff(hitPoint, hitNormal, gameObject.tag,
                                            damage / maxHealth, HealthFraction);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;

        // The cloud is emitted BEFORE Destroy, from the renderer bounds - after it there is
        // nothing left to measure. The bounds are a world AABB refreshed every frame the
        // object was drawn, which is exactly right for something that has stood in the
        // level since load (the edit-mode staleness in CODE_STRUCTURE is about objects
        // instantiated moments ago, and does not apply).
        if (emitDust && DamageDustManager.Instance != null)
        {
            DamageDustManager.Instance.Cloud(RendererBounds(), gameObject.tag);
        }

        Destroy(gameObject);
    }

    private Bounds RendererBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.5f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
