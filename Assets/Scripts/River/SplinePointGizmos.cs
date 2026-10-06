using UnityEngine;

[ExecuteInEditMode]
public class SplinePointGizmos : MonoBehaviour
{
    // List of spline points
    public Transform[] splinePoints;

    // Radius of the sphere to draw for each point
    public float gizmoRadius = 0.1f;

    // Color of the gizmos
    public Color gizmoColor = Color.red;

    private void OnDrawGizmos()
    {
        if (splinePoints == null || splinePoints.Length == 0)
            return;

        // Set the color of the gizmos
        Gizmos.color = gizmoColor;

        // Draw a sphere at each spline point
        foreach (Transform point in splinePoints)
        {
            if (point != null)
            {
                Gizmos.DrawSphere(point.position, gizmoRadius);
            }
        }

        // Optionally, draw lines between the points
        for (int i = 0; i < splinePoints.Length - 1; i++)
        {
            if (splinePoints[i] != null && splinePoints[i + 1] != null)
            {
                Gizmos.DrawLine(splinePoints[i].position, splinePoints[i + 1].position);
            }
        }
    }
}
