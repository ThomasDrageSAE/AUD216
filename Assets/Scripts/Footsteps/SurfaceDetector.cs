using UnityEngine;

public class SurfaceDetector : MonoBehaviour
{
    [Tooltip("Layers to include in surface detection")]
    public LayerMask layerMask = -1;
    public bool DebugFootstepSurface;

    [SerializeField] private float sphereRadius = 0.3f;
    [SerializeField] private float rayStartHeight = 0.1f;  // Lowered to just above feet
    [SerializeField] private float rayDistance = 1.0f;     // Added explicit ray distance

    private CharacterController characterController;

    private void Start()
    {
        characterController = GetComponent<CharacterController>();
    }

    public string GetSurfaceType(Vector3 position)
    {
        // Start from the feet position
        Vector3 rayStart = position + Vector3.up * rayStartHeight;
        RaycastHit hit;

        // Try SphereCast first, fall back to a regular Raycast
        if (Physics.SphereCast(rayStart, sphereRadius, Vector3.down, out hit, rayDistance, layerMask) ||
            Physics.Raycast(rayStart, Vector3.down, out hit, rayDistance, layerMask))
        {
            if (DebugFootstepSurface)
            {
                Debug.DrawLine(rayStart, hit.point, Color.green, 0.1f);
            }

            return ResolveSurface(hit);
        }

        if (DebugFootstepSurface)
        {
            Debug.DrawRay(rayStart, Vector3.down * rayDistance, Color.red, 0.1f);
        }

        return "default";
    }

    private string ResolveSurface(RaycastHit hit)
    {
        // Tagged objects take priority over terrain textures
        if (hit.collider.CompareTag("Wood")) return "wood";
        if (hit.collider.CompareTag("Rock")) return "rock";
        if (hit.collider.CompareTag("Water")) return "water";
        if (hit.collider.CompareTag("Swamp")) return "swamp";

        Terrain terrain = hit.collider.GetComponent<Terrain>();
        if (terrain != null)
        {
            TerrainTextureDetector detector = terrain.GetComponent<TerrainTextureDetector>();
            if (detector != null)
            {
                return detector.MapTextureToCategory(detector.GetTextureAtPoint(hit.point));
            }
        }

        return "default";
    }

    private void OnDrawGizmos()
    {
        if (DebugFootstepSurface)
        {
            Vector3 rayStart = transform.position + Vector3.up * rayStartHeight;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(rayStart, sphereRadius);
            Gizmos.DrawLine(rayStart, rayStart + Vector3.down * rayDistance);
        }
    }
}
