using System.Collections.Generic;
using UnityEngine;

public enum OperationRangeSource
{
    LocalOriginArmVolume,
    ReachabilityMap,
    InverseReachabilityMap
}

[DisallowMultipleComponent]
public sealed class OperationRangeVolume : MonoBehaviour
{
    public struct PointDiagnostic
    {
        public Vector3 WorldPoint;
        public int ColliderCount;
        public string InsideColliderName;
        public float NearestSurfaceDistance;
        public bool IsInside;
    }

    [SerializeField] private OperationRangeSource operationRangeSource =
        OperationRangeSource.LocalOriginArmVolume;
    [SerializeField] private Transform rangeReferenceRoot;
    [SerializeField] private Transform localReachabilityVolumeRoot;
    [SerializeField, Min(0f)] private float insideEpsilon = 0.001f;

    private readonly List<Collider> activeRangeColliders = new List<Collider>();
    private bool missingGeometryWarningLogged;
    private bool readyLogged;

    public OperationRangeSource Source => operationRangeSource;
    public Transform RangeReferenceRoot => rangeReferenceRoot;
    public Transform RangeGeometryRoot => localReachabilityVolumeRoot;
    public int ColliderCount => activeRangeColliders.Count;
    public bool IsConfigured => operationRangeSource == OperationRangeSource.LocalOriginArmVolume
        && rangeReferenceRoot != null
        && localReachabilityVolumeRoot != null
        && activeRangeColliders.Count > 0;

    private void Awake()
    {
        DisableOperationRangeLayerCollisions();
        RefreshRangeColliders();
    }

    private void OnEnable()
    {
        RefreshRangeColliders();
    }

    private void OnValidate()
    {
        insideEpsilon = Mathf.Max(0f, insideEpsilon);
        if (!Application.isPlaying) RefreshRangeColliders();
    }

    [ContextMenu("Refresh Range Colliders")]
    public void RefreshRangeColliders()
    {
        activeRangeColliders.Clear();
        readyLogged = false;

        if (operationRangeSource == OperationRangeSource.LocalOriginArmVolume
            && localReachabilityVolumeRoot != null)
        {
            int operationRangeLayer = LayerMask.NameToLayer("OperationRange");
            Collider[] found = localReachabilityVolumeRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < found.Length; i++)
            {
                Collider candidate = found[i];
                if (candidate != null
                    && candidate.enabled
                    && candidate.gameObject.layer == operationRangeLayer
                    && IsSupportedClosedCollider(candidate))
                {
                    activeRangeColliders.Add(candidate);
                }
            }
        }

        if (activeRangeColliders.Count > 0)
        {
            missingGeometryWarningLogged = false;
            LogReadyOnce();
        }
        else if (!missingGeometryWarningLogged)
        {
            missingGeometryWarningLogged = true;
            Debug.LogWarning(
                "[OperationRangeVolume] Local origin_arm reachability geometry is not configured;"
                + " range evaluation skipped",
                this);
        }
    }

    public bool TryEvaluateWorldPoint(Vector3 worldPoint, out PointDiagnostic diagnostic)
    {
        RemoveDestroyedReferences();
        diagnostic = new PointDiagnostic
        {
            WorldPoint = worldPoint,
            ColliderCount = activeRangeColliders.Count,
            InsideColliderName = string.Empty,
            NearestSurfaceDistance = float.PositiveInfinity
        };
        if (!IsConfigured) return false;

        float nearestSquared = float.PositiveInfinity;
        for (int i = 0; i < activeRangeColliders.Count; i++)
        {
            Collider rangeCollider = activeRangeColliders[i];
            if (!rangeCollider.enabled || !rangeCollider.gameObject.activeInHierarchy) continue;

            if (Contains(rangeCollider, worldPoint))
            {
                diagnostic.IsInside = true;
                diagnostic.InsideColliderName = GetHierarchyPath(rangeCollider.transform);
                diagnostic.NearestSurfaceDistance = 0f;
                return true;
            }

            Vector3 closest = rangeCollider.ClosestPoint(worldPoint);
            nearestSquared = Mathf.Min(nearestSquared, (closest - worldPoint).sqrMagnitude);
        }

        diagnostic.NearestSurfaceDistance = float.IsPositiveInfinity(nearestSquared)
            ? float.PositiveInfinity
            : Mathf.Sqrt(nearestSquared);
        return true;
    }

    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        return TryEvaluateWorldPoint(worldPoint, out PointDiagnostic diagnostic)
            && diagnostic.IsInside;
    }

    private bool Contains(Collider rangeCollider, Vector3 worldPoint)
    {
        if (rangeCollider is BoxCollider box)
        {
            Vector3 offset = box.transform.InverseTransformPoint(worldPoint) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(offset.x) <= half.x + insideEpsilon
                && Mathf.Abs(offset.y) <= half.y + insideEpsilon
                && Mathf.Abs(offset.z) <= half.z + insideEpsilon;
        }
        if (rangeCollider is SphereCollider sphere)
        {
            Vector3 offset = sphere.transform.InverseTransformPoint(worldPoint) - sphere.center;
            float radius = sphere.radius + insideEpsilon;
            return offset.sqrMagnitude <= radius * radius;
        }
        if (rangeCollider is CapsuleCollider capsule)
        {
            Vector3 point = capsule.transform.InverseTransformPoint(worldPoint) - capsule.center;
            Vector3 axis = capsule.direction == 0
                ? Vector3.right
                : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            float radius = capsule.radius + insideEpsilon;
            float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            float projection = Mathf.Clamp(Vector3.Dot(point, axis), -halfSegment, halfSegment);
            return (point - axis * projection).sqrMagnitude <= radius * radius;
        }

        Vector3 closest = rangeCollider.ClosestPoint(worldPoint);
        return (closest - worldPoint).sqrMagnitude <= insideEpsilon * insideEpsilon;
    }

    private void RemoveDestroyedReferences()
    {
        for (int i = activeRangeColliders.Count - 1; i >= 0; i--)
        {
            if (activeRangeColliders[i] == null) activeRangeColliders.RemoveAt(i);
        }
    }

    private static bool IsSupportedClosedCollider(Collider candidate)
    {
        if (candidate is BoxCollider || candidate is SphereCollider || candidate is CapsuleCollider)
            return true;
        MeshCollider mesh = candidate as MeshCollider;
        return mesh != null && mesh.sharedMesh != null;
    }

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null) return "<null>";
        string path = item.name;
        for (Transform parent = item.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    private void LogReadyOnce()
    {
        if (readyLogged) return;
        readyLogged = true;
        Debug.Log("[OperationRangeVolume]"
            + "\nsource=" + operationRangeSource
            + "\nreferenceRoot=" + (rangeReferenceRoot != null ? rangeReferenceRoot.name : "<null>")
            + "\ngeometryRoot=" + (localReachabilityVolumeRoot != null
                ? localReachabilityVolumeRoot.name
                : "<null>")
            + "\ncolliderCount=" + activeRangeColliders.Count
            + "\nstate=READY", this);
    }

    private static void DisableOperationRangeLayerCollisions()
    {
        int operationRangeLayer = LayerMask.NameToLayer("OperationRange");
        if (operationRangeLayer < 0) return;
        for (int layer = 0; layer < 32; layer++)
            Physics.IgnoreLayerCollision(operationRangeLayer, layer, true);
    }

    private void OnDrawGizmosSelected()
    {
        if (operationRangeSource != OperationRangeSource.LocalOriginArmVolume) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < activeRangeColliders.Count; i++)
        {
            Collider rangeCollider = activeRangeColliders[i];
            if (rangeCollider != null)
                Gizmos.DrawWireCube(rangeCollider.bounds.center, rangeCollider.bounds.size);
        }
    }
}
