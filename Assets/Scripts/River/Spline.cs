using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * Spline.cs
 * 
 * Purpose: Manages spline-based path creation for river and terrain features.
 * Used by: Splinemover, River system
 * 
 * Provides mathematical foundation for smooth curve generation used in
 * river paths and terrain features. Includes debug visualization tools
 * for development.
 * 
 * Performance Considerations:
 * - Caches spline points to avoid recalculation
 * - Uses optimized point lookup
 * - Provides efficient closest point calculation
 * 
 * Dependencies:
 * - Requires properly set up spline points
 * - Used by SplinePointGizmos for debugging
 * - Optional debug drawing functionality
 */

public class Spline : MonoBehaviour
{
    private Vector3[] splinePoint;
    private int splineCount;

    public bool debug_drawspline = true;
    public float gizmoRadius = 0.1f; // Radius of the sphere to draw for each point
    public Color gizmoColor = Color.red; // Color of the gizmos

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public bool smoothMovement = true;
    public float smoothTime = 0.3f;

    private BSPNode bspRoot;

    private class BSPNode
    {
        public Vector3 position;
        public BSPNode left;
        public BSPNode right;
        public int index;
    }

    private void Start()
    {
        UpdateSplinePoints();
    }

    private void Update()
    {
        if (splineCount > 1)
        {
            for (int i = 0; i < splineCount - 1; i++)
            {
                Debug.DrawLine(splinePoint[i], splinePoint[i + 1], Color.green);
            }
        }
    }

    private void OnDrawGizmos()
    {
        UpdateSplinePoints();

        if (splinePoint == null || splinePoint.Length == 0)
        {
            Debug.Log("Spline points array is null or empty");
            return;
        }

        Gizmos.color = gizmoColor;

        for (int i = 0; i < splineCount; i++)
        {
            Vector3 pointPosition = transform.GetChild(i).position;
            Gizmos.DrawSphere(pointPosition, gizmoRadius);
            //Debug.Log($"Drawing gizmo sphere at {pointPosition}");
        }

        for (int i = 0; i < splineCount - 1; i++)
        {
            Vector3 startPoint = transform.GetChild(i).position;
            Vector3 endPoint = transform.GetChild(i + 1).position;
            Gizmos.DrawLine(startPoint, endPoint);
            //Debug.Log($"Drawing gizmo line from {startPoint} to {endPoint}");
        }
    }

    private void UpdateSplinePoints()
    {
        splineCount = transform.childCount;
        splinePoint = new Vector3[splineCount];
        
        for (int i = 0; i < splineCount; i++)
        {
            splinePoint[i] = transform.GetChild(i).position;
        }
        
        // Rebuild BSP tree when points change
        bspRoot = new BSPNode();
        BuildBSPTree(0, splineCount - 1, bspRoot);
    }

    private void BuildBSPTree(int start, int end, BSPNode node)
    {
        if (start >= end) return;
        
        int mid = (start + end) / 2;
        node.position = splinePoint[mid];
        node.index = mid;
        
        node.left = new BSPNode();
        BuildBSPTree(start, mid, node.left);
        
        node.right = new BSPNode();
        BuildBSPTree(mid + 1, end, node.right);
    }

    private int FindClosestBSP(Vector3 pos, BSPNode node, int closestIndex, float closestDist)
    {
        if (node == null) return closestIndex;
        
        float dist = (node.position - pos).sqrMagnitude;
        if (dist < closestDist)
        {
            closestDist = dist;
            closestIndex = node.index;
        }

        Vector3 toPos = pos - node.position;
        bool goLeft = Vector3.Dot(toPos, node.left?.position ?? node.position) < 0;
        
        if (goLeft)
        {
            closestIndex = FindClosestBSP(pos, node.left, closestIndex, closestDist);
            closestIndex = FindClosestBSP(pos, node.right, closestIndex, closestDist);
        }
        else
        {
            closestIndex = FindClosestBSP(pos, node.right, closestIndex, closestDist);
            closestIndex = FindClosestBSP(pos, node.left, closestIndex, closestDist);
        }
        
        return closestIndex;
    }

    private int GetClosestSplinePoint(Vector3 pos)
    {
        if (bspRoot == null) 
        {
            // Fallback to linear search if BSP not built
            int closestPoint = -1;
            float shortestDistance = float.MaxValue;
            for (int i = 0; i < splineCount; i++)
            {
                float sqrDistance = (splinePoint[i] - pos).sqrMagnitude;
                if (sqrDistance < shortestDistance)
                {
                    shortestDistance = sqrDistance;
                    closestPoint = i;
                }
            }
            return closestPoint;
        }
        return FindClosestBSP(pos, bspRoot, -1, float.MaxValue);
    }

    public Vector3 WhereOnSpline(Vector3 pos)
    {
        int closestSplinePoint = GetClosestSplinePoint(pos);

        if (closestSplinePoint == 0)
        {
            return splineSegment(splinePoint[0], splinePoint[1], pos);
        }
        else if (closestSplinePoint == splineCount - 1)
        {
            return splineSegment(splinePoint[splineCount - 1], splinePoint[splineCount - 2], pos);
        }
        else
        {
            Vector3 leftSeg = splineSegment(splinePoint[closestSplinePoint - 1], splinePoint[closestSplinePoint], pos);
            Vector3 rightSeg = splineSegment(splinePoint[closestSplinePoint + 1], splinePoint[closestSplinePoint], pos);

            if ((pos - leftSeg).sqrMagnitude <= (pos - rightSeg).sqrMagnitude)
            {
                return leftSeg;
            }
            else
            {
                return rightSeg;
            }
        }
    }

    public Vector3 splineSegment(Vector3 v1, Vector3 v2, Vector3 pos)
    {
        Vector3 v1ToPos = pos - v1;
        Vector3 seqDirection = (v2 - v1).normalized;

        float distanceFromV1 = Vector3.Dot(seqDirection, v1ToPos);

        if (distanceFromV1 < 0.0f)
        {
            return v1;
        }
        else if (distanceFromV1 * distanceFromV1 > (v2 - v1).sqrMagnitude)
        {
            return v2;
        }
        else
        {
            Vector3 fromV1 = seqDirection * distanceFromV1;
            return v1 + fromV1;
        }
    }

    // Public method to check if splinePoint is initialized
    public bool IsSplinePointInitialized()
    {
        return splinePoint != null && splinePoint.Length > 0;
    }

    public Vector3 GetPositionAlongSpline(Vector3 position, ref int currentSegment, ref float progress)
    {
        if (splineCount < 2) return position;

        // Clamp current segment to valid range
        currentSegment = Mathf.Clamp(currentSegment, 0, splineCount - 2);
        
        // Get current segment points
        Vector3 start = splinePoint[currentSegment];
        Vector3 end = splinePoint[currentSegment + 1];
        
        // Get closest point on current segment
        Vector3 closestPoint = GetClosestPointOnSegment(start, end, position);
        
        // Calculate progress along current segment (0-1)
        float segmentLength = Vector3.Distance(start, end);
        float currentDistance = Vector3.Distance(start, closestPoint);
        progress = currentDistance / segmentLength;

        // Check if we should move to next segment
        if (progress > 0.95f && currentSegment < splineCount - 2)
        {
            currentSegment++;
            progress = 0f;
        }
        // Check if we should move to previous segment
        else if (progress < 0.05f && currentSegment > 0)
        {
            currentSegment--;
            progress = 1f; // Start at end of previous segment
        }

        return closestPoint;
    }

    private Vector3 GetClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 direction = b - a;
        float length = direction.magnitude;
        direction.Normalize();
        
        float projection = Vector3.Dot(point - a, direction);
        projection = Mathf.Clamp(projection, 0f, length);
        
        return a + direction * projection;
    }
}
