using UnityEngine;

/*
 * InteractionReach.cs
 *
 * Purpose: Keeps a prop's trigger sphere the same size in METRES however the scene has
 *          scaled the prop, and owns the local-space bounds maths both the runtime
 *          components and the prefab builder need.
 * Used by: MushroomTripInteraction, PropSoundInteraction, and PortedPropTools (editor).
 *
 * Why this exists. Both props are prefabs whose root carries a large scale - the models
 * import tiny, so the builder compensates with 100x - and the scene instances override
 * that with their own: 81.88 for the well, 13.41 for the mushroom. A SphereCollider's
 * radius is LOCAL and Unity multiplies it by lossyScale, so a single authored radius
 * cannot be right for both. Sized for the prefab, the mushroom's reach came out at 0.56 m
 * in the scene; you had to stand inside it to get a prompt.
 *
 * So the radius is re-derived on Start from the model's own bounds plus a reach in metres,
 * with lossyScale divided back out. The blocking BoxCollider is deliberately left alone -
 * that one SHOULD scale with the model, because it is the model's own body.
 *
 * ⚠ The editor gizmo therefore shows the authored radius, not the one that is played. That
 * is the trade for one prefab that works at any instance scale.
 *
 * The bounds helper lives here, in runtime code, rather than in the editor tool: editor
 * assemblies can reference runtime ones and not the reverse, and a second copy of this
 * particular calculation is exactly what produced the 385 m collider the player stood on.
 */
public static class InteractionReach
{
    /// <summary>
    /// Sizes the prop's trigger sphere to enclose its renderers plus
    /// <paramref name="reachMetres"/> of clearance, measured in world metres, whatever
    /// scale the instance carries. Does nothing when the prop has no trigger sphere.
    /// </summary>
    public static void Apply(GameObject root, float reachMetres)
    {
        if (root == null) return;

        SphereCollider trigger = FindTrigger(root);
        if (trigger == null) return;

        float scale = UniformScaleOf(root.transform);
        if (scale <= Mathf.Epsilon) return;

        float localReach = reachMetres / scale;

        if (TryGetLocalBounds(root.transform, out Bounds bounds))
        {
            trigger.center = bounds.center;
            trigger.radius = bounds.extents.magnitude + localReach;
        }
        else
        {
            // No renderers to measure; the reach alone still gives a usable prompt volume.
            trigger.radius = localReach;
        }
    }

    /// <summary>
    /// Whether the player is already standing inside the prop's trigger.
    /// </summary>
    /// <remarks>
    /// Resizing a collider does not retroactively raise OnTriggerEnter for something
    /// already within it, and Apply resizes on Start - so a player who spawns next to a
    /// prop would get no prompt until they stepped out of range and back in. Callers use
    /// this to seed their own in-range state instead of waiting for an event that has
    /// already been missed.
    /// </remarks>
    public static bool ContainsPlayer(GameObject root)
    {
        if (root == null) return false;

        SphereCollider trigger = FindTrigger(root);
        if (trigger == null) return false;

        Vector3 centre = trigger.transform.TransformPoint(trigger.center);
        float radius = trigger.radius * UniformScaleOf(trigger.transform);

        foreach (Collider hit in Physics.OverlapSphere(centre, radius))
        {
            if (hit.CompareTag("Player")) return true;
        }

        return false;
    }

    /// <summary>
    /// The prop's interaction volume: the first SphereCollider on the root that is a
    /// trigger. Chosen by isTrigger rather than by being first, so a prop that also
    /// carries a solid sphere - or gains a second volume later - cannot have the wrong
    /// one silently resized.
    /// </summary>
    private static SphereCollider FindTrigger(GameObject root)
    {
        foreach (SphereCollider candidate in root.GetComponents<SphereCollider>())
        {
            if (candidate.isTrigger) return candidate;
        }

        return null;
    }

    /// <summary>
    /// The largest absolute lossyScale component - the one Unity multiplies a
    /// SphereCollider's radius by.
    /// </summary>
    private static float UniformScaleOf(Transform target)
    {
        if (target == null) return 1f;

        Vector3 lossy = target.lossyScale;
        return Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
    }

    /// <summary>
    /// Combined bounds of every renderer under <paramref name="root"/>, in the root's own
    /// local space. False when there are no renderers at all.
    /// </summary>
    /// <remarks>
    /// ⚠ This must NOT use <c>Renderer.bounds</c> directly. That is a world-space AABB, so
    /// it already carries the root's rotation and scale - but <c>BoxCollider.size</c> and
    /// <c>SphereCollider.radius</c> are LOCAL, and Unity multiplies them by lossyScale
    /// again. Assigning one to the other applies the scale twice, which is what turned the
    /// well's 4.7 m box into a 385 m invisible slab. Corners are converted into the root's
    /// space instead, which divides both back out.
    /// </remarks>
    public static bool TryGetLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        Matrix4x4 worldToRoot = root.worldToLocalMatrix;
        bool started = false;

        foreach (Renderer renderer in renderers)
        {
            // localBounds is mesh space, so this is the tight oriented box rather than the
            // inflated world AABB of a rotated mesh.
            Bounds mesh = renderer.localBounds;
            Matrix4x4 meshToRoot = worldToRoot * renderer.transform.localToWorldMatrix;
            Vector3 centre = mesh.center;
            Vector3 extents = mesh.extents;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = centre + new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);

                Vector3 local = meshToRoot.MultiplyPoint3x4(point);

                if (!started)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return started;
    }
}
