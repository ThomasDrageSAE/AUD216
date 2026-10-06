using UnityEngine;

/// <summary>
/// Makes the wind chimes hang and move like they are hanging: the whole cluster leans on the
/// breeze, and the tubes, the clapper and the sail swing further and out of step with it.
///
/// No physics and no animation clip. A hanging object is a pendulum, and a pendulum is two
/// sines - so this is two sines per swinger, at periods that do not divide into each other, and
/// a third much slower one for the gust. Nothing repeats on a beat the eye can catch, and the
/// whole thing costs one Quaternion per swinger per frame, on a prop that is culled by
/// distance anyway.
///
/// The one thing NOT hard-coded is the axis, because this project has already been bitten once
/// by an assumed one - see MinigunController's derived barrel axis, and the propeller it turned
/// into when the axis was assumed. Each child's hang direction is measured from its own mesh at
/// startup (a tube's mesh sits below its pivot, and the direction from the pivot to the mesh's
/// middle IS the way it hangs), and it swings about the two axes perpendicular to that. A
/// re-exported model with different conventions still swings correctly, and the report says
/// what was measured.
///
/// The root is the exception: it swings about the WORLD horizontal axes, because what makes a
/// hanging thing hang is gravity, not the model.
///
/// Deliberately not connected to <see cref="WindChimeAudio"/>. Tying the loop's gain to the
/// gust would sound better and would also mean a student could no longer hear their own clip
/// at the level they authored it, which on an assessed slot is the wrong trade.
/// </summary>
public class WindChimeSway : MonoBehaviour
{
    [Header("The cluster")]

    [Tooltip("How far the whole assembly leans, in degrees.")]
    [Range(0f, 30f)]
    [SerializeField] private float swayAngle = 3.5f;

    [Tooltip("Seconds for one lean and back.")]
    [Min(0.1f)]
    [SerializeField] private float swayPeriod = 3.7f;

    [Header("The tubes, clapper and sail")]

    [Tooltip("Children that swing on their own as well. Left empty, every child with a mesh " +
             "is used, which is what the setup tool wires.")]
    [SerializeField] private Transform[] swingers;

    [Tooltip("How much further than the cluster the children swing. They are lighter and on " +
             "shorter cords, so they always move more.")]
    [Range(0f, 5f)]
    [SerializeField] private float swingerMultiplier = 2.2f;

    [Tooltip("Seconds of difference between the fastest and slowest child, spread evenly. " +
             "Zero makes them move as one rigid lump, which reads as a single solid object.")]
    [Range(0f, 3f)]
    [SerializeField] private float swingerPeriodSpread = 1.1f;

    [Header("Wind")]

    [Tooltip("Seconds between gusts. Much longer than the sway, so the chimes are sometimes " +
             "nearly still and sometimes moving properly.")]
    [Min(0.5f)]
    [SerializeField] private float gustPeriod = 13f;

    [Tooltip("How much the gust takes away at its lowest. 1 means the chimes stop dead " +
             "between gusts, 0 means the wind never changes.")]
    [Range(0f, 1f)]
    [SerializeField] private float gustDepth = 0.65f;

    [Header("Cost")]

    [Tooltip("Stop animating beyond this distance from the camera. The chimes are a few " +
             "centimetres of movement; past this nobody can see them move.")]
    [Min(0f)]
    [SerializeField] private float activeDistance = 40f;

    /// <summary>
    /// The second sine's period as a fraction of the first. Irrational-ish on purpose: at 0.83
    /// the two only line up again every few minutes, so the tip of the swing traces a slowly
    /// turning ellipse instead of a straight line back and forth.
    /// </summary>
    private const float SecondAxisPeriodRatio = 0.83f;

    private Quaternion baseRotation;
    private Swinger[] resolved;
    private Transform viewer;
    private float phase;

    /// <summary>One child, its rest pose, and the two axes it is free to swing about.</summary>
    private struct Swinger
    {
        public Transform Transform;
        public Quaternion BaseRotation;
        public Vector3 AxisA;
        public Vector3 AxisB;
        public float Rate;
        public float Phase;
    }

    private void Start()
    {
        baseRotation = transform.rotation;

        // Every instance gets its own place in the cycle, so two sets of chimes in one level
        // never swing in lockstep.
        phase = Random.Range(0f, Mathf.PI * 2f);

        ResolveSwingers();

        Camera camera = Camera.main;
        viewer = camera != null ? camera.transform : null;
    }

    private void Update()
    {
        if (!InRange()) return;

        float time = Time.time;

        // 0..1, and slow. Mathf.Sin of a rate rather than PingPong so there is no corner at
        // the turn - a gust that arrives with a kink in it reads as a scripted move.
        float gust = 0.5f + 0.5f * Mathf.Sin(time * Rate(gustPeriod) + phase);
        float wind = Mathf.Lerp(1f - gustDepth, 1f, gust);

        // The cluster, about the WORLD horizontals: it hangs from a branch, and gravity does
        // not care how the model was authored.
        float rate = Rate(swayPeriod);
        float lean = swayAngle * wind;
        float x = lean * Mathf.Sin(time * rate + phase);
        float z = lean * Mathf.Sin(time * rate / SecondAxisPeriodRatio + phase * 1.7f);

        transform.rotation = Quaternion.Euler(x, 0f, z) * baseRotation;

        if (resolved == null) return;

        float childLean = swayAngle * swingerMultiplier * wind;

        for (int i = 0; i < resolved.Length; i++)
        {
            Swinger swinger = resolved[i];
            if (swinger.Transform == null) continue;

            float a = childLean * Mathf.Sin(time * swinger.Rate + swinger.Phase);
            float b = childLean * Mathf.Sin(time * swinger.Rate / SecondAxisPeriodRatio + swinger.Phase * 1.7f);

            // Composed on the LEFT of the rest pose, so both angles are read in the parent's
            // space - the space the axes below were measured into.
            swinger.Transform.localRotation =
                Quaternion.AngleAxis(a, swinger.AxisA) * Quaternion.AngleAxis(b, swinger.AxisB) * swinger.BaseRotation;
        }
    }

    /// <summary>
    /// True while the chimes are close enough to be worth moving. The camera is looked up
    /// again when it is missing rather than once and for all: this level reloads the scene to
    /// restart, and a cached camera from the previous load is a null reference.
    /// </summary>
    private bool InRange()
    {
        if (activeDistance <= 0f) return true;

        if (viewer == null)
        {
            Camera camera = Camera.main;
            viewer = camera != null ? camera.transform : null;
            if (viewer == null) return true;      // no camera yet: animate rather than freeze
        }

        return (viewer.position - transform.position).sqrMagnitude <= activeDistance * activeDistance;
    }

    /// <summary>
    /// Works out, once, which way each child hangs and therefore which two axes it may swing
    /// about. Measured from the mesh rather than assumed - see the note at the top.
    /// </summary>
    private void ResolveSwingers()
    {
        Transform[] candidates = swingers != null && swingers.Length > 0 ? swingers : Children();
        var built = new System.Collections.Generic.List<Swinger>(candidates.Length);

        float rate = Rate(swayPeriod);

        for (int i = 0; i < candidates.Length; i++)
        {
            Transform child = candidates[i];
            if (child == null || child == transform) continue;

            if (!TryGetHangDirection(child, out Vector3 hang)) continue;

            // In the PARENT's space, which is where localRotation is read.
            Vector3 hangInParent = child.localRotation * hang;

            // Any vector not parallel to the hang direction will do for the first cross
            // product; right and forward between them cover every direction a chime can hang.
            Vector3 seed = Mathf.Abs(Vector3.Dot(hangInParent, Vector3.right)) < 0.9f
                ? Vector3.right
                : Vector3.forward;

            Vector3 axisA = Vector3.Cross(hangInParent, seed).normalized;
            Vector3 axisB = Vector3.Cross(hangInParent, axisA).normalized;

            // Spread across the children so they never arrive together. The period stretches
            // as well as the phase, because six tubes moving at one speed is a windscreen
            // wiper however staggered their starts are.
            float spread = candidates.Length > 1 ? (float)i / (candidates.Length - 1) : 0f;
            float period = swayPeriod + swingerPeriodSpread * (spread - 0.5f);

            built.Add(new Swinger
            {
                Transform = child,
                BaseRotation = child.localRotation,
                AxisA = axisA,
                AxisB = axisB,
                Rate = Rate(Mathf.Max(0.1f, period)),
                Phase = phase + spread * Mathf.PI * 2f,
            });
        }

        resolved = built.ToArray();
    }

    /// <summary>
    /// The direction a child hangs, in its own local space: from its pivot towards the middle
    /// of its mesh. A chime tube is modelled hanging below its pivot, so this is the cord.
    /// False when there is no mesh, or when the pivot sits in the middle of it and there is no
    /// direction to read - a child like that is left alone rather than swung about a guess.
    /// </summary>
    private static bool TryGetHangDirection(Transform child, out Vector3 hang)
    {
        hang = Vector3.zero;

        MeshFilter filter = child.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;

        Vector3 centre = filter.sharedMesh.bounds.center;
        if (centre.sqrMagnitude < 1e-10f) return false;

        hang = centre.normalized;
        return true;
    }

    private Transform[] Children()
    {
        var children = new System.Collections.Generic.List<Transform>(transform.childCount);
        foreach (Transform child in transform) children.Add(child);
        return children.ToArray();
    }

    private static float Rate(float period) => Mathf.PI * 2f / Mathf.Max(0.01f, period);
}
