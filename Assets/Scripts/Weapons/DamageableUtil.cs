using UnityEngine;

/*
 * DamageableUtil.cs
 *
 * Purpose: One way to resolve the IDamageable behind a hit.
 * Used by: WeaponController, AxeController, Arrow.
 *
 * The three weapons each used to resolve differently - hit.transform, hit.collider and
 * collision.gameObject - which agree only while every damageable is a single collider on
 * the same object as its script. The first enemy with a child collider or a Rigidbody
 * would have made one weapon register hits and another not, silently and inconsistently.
 *
 * GetComponentInParent is the deliberate widening: a child collider on a damageable root
 * now takes damage from all three, which is what a player expects when they hit an arm.
 */
public static class DamageableUtil
{
    public static bool TryGetDamageable(Component hitContext, out IDamageable damageable)
    {
        damageable = null;
        if (hitContext == null) return false;

        damageable = hitContext.GetComponent<IDamageable>();
        if (damageable == null)
        {
            damageable = hitContext.GetComponentInParent<IDamageable>();
        }

        return damageable != null;
    }

    /// <summary>
    /// Deals damage to whatever <see cref="TryGetDamageable"/> found, passing the hit point to
    /// the types that can use one. Until 2026-09-06 every weapon carried its own copy of the
    /// zombie-or-not branch; a third positional type (the dust on a breakable prop) would have
    /// meant a third branch in five places, which is how one of them gets missed.
    ///
    /// <paramref name="hitNormal"/> is the direction dust leaves the surface: the raycast's
    /// normal for a shot, the direction away from the blast for a grenade, up when unknown.
    /// </summary>
    public static void Damage(IDamageable target, float amount, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (target == null) return;

        // Zombies take POSITIONAL damage so the head band pays double (ZombieAI.IsHeadPoint).
        if (target is ZombieAI zombie)
        {
            zombie.TakeDamage(amount, hitPoint);
        }
        // Props take it so the dust comes off where the round went in.
        else if (target is DamageableObject prop)
        {
            prop.TakeDamage(amount, hitPoint, hitNormal);
        }
        else
        {
            target.TakeDamage(amount);
        }
    }
}
