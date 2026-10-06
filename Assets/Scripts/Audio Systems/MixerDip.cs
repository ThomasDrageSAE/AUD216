using UnityEngine.Audio;

/*
 * MixerDip.cs
 *
 * Purpose: Owns the "snapshot dip that must never latch" pattern in one place.
 * Used by: MushroomTripEffect (the trip wash), BossZombieController (the death silence),
 *          HeartbeatLayer (the low-health closedown).
 *
 * Snapshot state lives on the mixer ASSET and survives scene reloads, so any feature
 * that blends to a snapshot must guarantee the way back on every exit path - including
 * being destroyed mid-dip, which is how death-then-restart leaves the scene. Three
 * components each hand-rolled that flag-and-restore; a fourth copy is how the drift
 * starts, so it lives here now.
 *
 * Restore is a no-op unless THIS dip is active, which also stops one feature's teardown
 * from stomping a snapshot some other feature is legitimately holding.
 */
public class MixerDip
{
    private AudioMixerSnapshot restoreTo;
    private bool dipped;

    public bool IsDipped => dipped;

    /// <summary>
    /// Blends to <paramref name="target"/> and remembers the way back. A null target is
    /// a no-op - the feature simply is not authored.
    /// </summary>
    public void Dip(AudioMixerSnapshot target, AudioMixerSnapshot restore, float seconds)
    {
        if (target == null) return;

        restoreTo = restore;
        dipped = true;
        target.TransitionTo(seconds);
    }

    /// <summary>
    /// Blends back, once. Only when this dip is actually active, so calling it from
    /// every exit path (end-of-effect AND OnDestroy) is always safe.
    /// </summary>
    public void Restore(float seconds)
    {
        if (!dipped) return;

        dipped = false;
        if (restoreTo != null) restoreTo.TransitionTo(seconds);
    }
}
