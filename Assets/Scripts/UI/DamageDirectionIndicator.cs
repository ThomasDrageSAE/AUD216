using UnityEngine;

/*
 * DamageDirectionIndicator.cs
 *
 * Purpose: Makes the player's hit feedback directional - the Synty FX_Damage_EdgeofScreen
 *          smear is swung around the screen centre so the blood lands on the edge the
 *          attack came from, the way the pack's demo shows it.
 * Used by: PlayerStats. It lives on the damage-effect root (FX_Damage_EdgeofScreen under
 *          Canvas - Interface); PlayerStats adds it at runtime when the scene has none, so
 *          no scene edit is needed - add it by hand only to author the fields below.
 *
 * The edge-of-screen prefab is built for this: its "Damage" child hangs 1400 units from a
 * pivot 180 below the screen centre (the prefab's own default), and its animator drives
 * only that child's alpha and scale, so the ROOT's z rotation is free. At rotation 0 the
 * smear sits on the top edge, which reads as "in front of the player"; every other bearing
 * is the root turned by that angle, and the hit-line particles turn with it.
 *
 * The bearing is measured on the ground plane from the viewer (the first-person camera)
 * to the attacker, relative to where the viewer is looking, and it keeps tracking while
 * the effect is showing so a turn during the flash does not leave the blood pointing at
 * where the zombie used to be. A hit with no known source (IDamageable.TakeDamage) resets
 * the smear to the neutral top edge - what every hit showed before this existed.
 */
public class DamageDirectionIndicator : MonoBehaviour
{
    [Tooltip("Rotated around the screen centre to face the attacker. Empty = this object's RectTransform")]
    [SerializeField] private RectTransform pivot;
    [Tooltip("Whose position and facing the bearing is measured from. Empty = Camera.main")]
    [SerializeField] private Transform viewer;
    [Tooltip("Keep re-aiming at a moving attacker while the effect is showing")]
    [SerializeField] private bool trackWhileVisible = true;
    [Tooltip("Degrees added to the result, for art whose neutral edge is not the top")]
    [SerializeField] private float angleOffset = 0f;

    private Transform source;
    private Vector3 sourcePosition;
    private bool hasSource;

    private RectTransform Pivot
    {
        get
        {
            if (pivot == null) pivot = transform as RectTransform;
            return pivot;
        }
    }

    private Transform Viewer
    {
        get
        {
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            return viewer;
        }
    }

    /// <summary>Point the smear at an attacker, and follow it while the effect shows.</summary>
    public void PointAt(Transform attacker)
    {
        if (attacker == null)
        {
            Clear();
            return;
        }

        source = attacker;
        sourcePosition = attacker.position;
        hasSource = true;
        Aim();
    }

    /// <summary>Point the smear at a fixed world position - a trap, a blast.</summary>
    public void PointAt(Vector3 worldPosition)
    {
        source = null;
        sourcePosition = worldPosition;
        hasSource = true;
        Aim();
    }

    /// <summary>No known direction: back to the neutral pose, blood on the top edge.</summary>
    public void Clear()
    {
        source = null;
        hasSource = false;

        RectTransform rect = Pivot;
        if (rect != null) rect.localRotation = Quaternion.identity;
    }

    private void LateUpdate()
    {
        // After the camera has turned for this frame, so the smear never lags the view.
        if (trackWhileVisible && hasSource) Aim();
    }

    private void Aim()
    {
        Transform eye = Viewer;
        RectTransform rect = Pivot;
        if (eye == null || rect == null) return;

        if (source != null) sourcePosition = source.position;

        Vector3 toSource = sourcePosition - eye.position;
        toSource.y = 0f;
        Vector3 facing = eye.forward;
        facing.y = 0f;

        // Straight above or below, or a source at the eye itself: no bearing to show.
        if (toSource.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f) return;

        // Clockwise-from-forward on the ground is a NEGATIVE z rotation on the canvas
        // (UI turns counter-clockwise for positive z), so an attacker on the right swings
        // the top-edge smear round to the right edge.
        float bearing = Vector3.SignedAngle(facing, toSource, Vector3.up);
        rect.localRotation = Quaternion.Euler(0f, 0f, -bearing + angleOffset);
    }
}
