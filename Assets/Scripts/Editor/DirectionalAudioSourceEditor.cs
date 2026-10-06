using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(DirectionalAudioSource))]
public class DirectionalAudioSourceEditor : Editor
{
    // Fixed Gradient Colors
    private readonly Color gradientStartColor = new Color(63f / 255f, 63f / 255f, 63f / 255f, 1f); // #3F3F3F with Alpha 255
    private readonly Color gradientEndColor = new Color(0f, 0f, 0f, 1f);                         // #000000 with Alpha 255
    private readonly int gradientSteps = 100;                                                    // Number of steps for the gradient

    // Fixed Fill Colors for On-Axis and Off-Axis
    private readonly Color onAxisFillColor = new Color(0f, 1f, 0f, 0.3f);  // Green with some transparency
    private readonly Color offAxisFillColor = new Color(1f, 0f, 0f, 0.2f); // Red with some transparency

    // Fixed Outline Colors
    private readonly Color onAxisOutlineColor = Color.green;
    private readonly Color sideAxisOutlineColor = Color.blue;
    private readonly Color offAxisOutlineColor = Color.red;

    public override void OnInspectorGUI()
    {
        // Draw the default inspector elements
        DrawDefaultInspector();

        // Reference to the target script
        DirectionalAudioSource das = (DirectionalAudioSource)target;

        EditorGUILayout.Space(10);

        // Display current filter frequency and volume reduction using public properties
        EditorGUILayout.LabelField("Current Filter Frequency", $"{das.CurrentFilterFrequencyValue:F0} Hz");
        EditorGUILayout.LabelField("Current Volume Reduction", $"{das.CurrentVolumeReductionValue:F1} dB");

        // Display Player Angle
        EditorGUILayout.LabelField("Player Angle", $"{das.CurrentListenerAngle:F1}°");

        EditorGUILayout.Space(10);

        // Removed Angle Circle Customization Controls to Reduce Clutter

        // Draw the angle circle visualization
        Rect rect = GUILayoutUtility.GetRect(300, 300); // Increased size for better visibility
        DrawAngleCircle(rect, das.onAxisAngle, das.offAxisAngle);
    }

    private void DrawAngleCircle(Rect rect, float onAxisAngle, float offAxisAngle)
    {
        // Calculate center and radius
        Vector2 center = new Vector2(rect.x + rect.width / 2, rect.y + rect.height / 2);
        float radius = Mathf.Min(rect.width, rect.height) * 0.45f;

        // Calculate displayOffAxisAngle based on offAxisAngle
        float displayOffAxisAngle = (180f - offAxisAngle) * 2f;

        // Define angle ranges
        float onAxisStartAngle = -onAxisAngle;
        float onAxisEndAngle = onAxisAngle;

        float offAxisStartAngle = 180f - displayOffAxisAngle / 2f;
        float offAxisEndAngle = 180f + displayOffAxisAngle / 2f;

        float sideAxisStartAngle1 = onAxisEndAngle;
        float sideAxisEndAngle1 = offAxisStartAngle;

        float sideAxisStartAngle2 = -offAxisStartAngle;
        float sideAxisEndAngle2 = onAxisStartAngle;

        // Draw the base circle
        Handles.color = Color.gray;
        Handles.DrawWireDisc(center, Vector3.forward, radius);

        // Draw filled Off-Axis region (Red)
        DrawFilledAngle(center, radius, offAxisStartAngle, offAxisEndAngle, offAxisFillColor);

        // Draw filled On-Axis region (Green)
        DrawFilledAngle(center, radius, onAxisStartAngle, onAxisEndAngle, onAxisFillColor);

        // Draw filled Side-Axis regions with Gradient (Fixed #3F3F3F to #000000)
        // Right Side: gradient from #3F3F3F to #000000
        DrawGradientFilledAngle(center, radius, sideAxisStartAngle1, sideAxisEndAngle1, gradientStartColor, gradientEndColor);
        // Left Side: gradient from #3F3F3F to #000000 (swap start and end angles)
        DrawGradientFilledAngle(center, radius, sideAxisEndAngle2, sideAxisStartAngle2, gradientStartColor, gradientEndColor);

        // Draw outlines
        // Off-Axis Outline (Red)
        Handles.color = offAxisOutlineColor;
        DrawAngle(center, radius, offAxisStartAngle, offAxisEndAngle);

        // On-Axis Outline (Green)
        Handles.color = onAxisOutlineColor;
        DrawAngle(center, radius, onAxisStartAngle, onAxisEndAngle);

        // Side-Axis Outline (Blue)
        Handles.color = sideAxisOutlineColor;
        DrawAngle(center, radius, sideAxisStartAngle1, sideAxisEndAngle1);
        DrawAngle(center, radius, sideAxisStartAngle2, sideAxisEndAngle2);

        // Draw labels
        GUI.Label(new Rect(rect.x, rect.y, rect.width, 20), "On-Axis: " + onAxisAngle.ToString("F1") + "°");
        GUI.Label(new Rect(rect.x, rect.y + 20, rect.width, 20), "Off-Axis: " + offAxisAngle.ToString("F1") + "°");
    }

    private void DrawAngle(Vector2 center, float radius, float startAngle, float endAngle)
    {
        Vector3 startDir = Quaternion.Euler(0, 0, startAngle) * Vector3.down;
        Vector3 endDir = Quaternion.Euler(0, 0, endAngle) * Vector3.down;
        float angleDifference = endAngle - startAngle;

        // Draw the arc
        Handles.DrawWireArc(center, Vector3.forward, startDir, angleDifference, radius);

        // Draw lines from center to arc start and end
        Handles.DrawLine(center, center + (Vector2)(startDir * radius));
        Handles.DrawLine(center, center + (Vector2)(endDir * radius));
    }

    private void DrawFilledAngle(Vector2 center, float radius, float startAngle, float endAngle, Color fillColor)
    {
        Vector3 startDir = Quaternion.Euler(0, 0, startAngle) * Vector3.down;
        Handles.color = fillColor;
        Handles.DrawSolidArc(center, Vector3.forward, startDir, endAngle - startAngle, radius);
    }

    private void DrawGradientFilledAngle(Vector2 center, float radius, float startAngle, float endAngle, Color startColor, Color endColor)
    {
        float totalAngle = endAngle - startAngle;
        float stepAngle = totalAngle / gradientSteps;

        for (int i = 0; i < gradientSteps; i++)
        {
            float currentStartAngle = startAngle + i * stepAngle;
            float currentEndAngle = currentStartAngle + stepAngle;

            // Calculate interpolation factor (0 to 1)
            float t = (float)i / gradientSteps;

            // Interpolate color
            Color currentColor = Color.Lerp(startColor, endColor, t);

            // Set Handles color
            Handles.color = currentColor;

            // Draw thin arc
            DrawFilledAngleSegment(center, radius, currentStartAngle, currentEndAngle);
        }
    }

    private void DrawFilledAngleSegment(Vector2 center, float radius, float startAngle, float endAngle)
    {
        Vector3 startDir = Quaternion.Euler(0, 0, startAngle) * Vector3.down;
        Handles.DrawSolidArc(center, Vector3.forward, startDir, endAngle - startAngle, radius);
    }
}
