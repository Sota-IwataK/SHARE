using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum BottleContainmentMode
{
    CenterPoint,
    EntireBottleBounds
}

[DisallowMultipleComponent]
public sealed class SharedBottleRangeMonitor : MonoBehaviour
{
    [Header("Operation Range")]
    [SerializeField] private OperationRangeVolume operationRange;
    [SerializeField] private BottleContainmentMode containmentMode = BottleContainmentMode.CenterPoint;

    [Header("Bottle Sources")]
    [SerializeField] private bool includeRosDetectedBottles = true;
    [SerializeField] private bool includeManualBottles;

    [Header("Hysteresis")]
    [SerializeField, Min(0f)] private float outsideMargin = 0.02f;
    [SerializeField, Min(0f)] private float insideRecoveryMargin = 0.02f;
    [SerializeField, Min(0f)] private float stateChangeDwellSec = 0.15f;

    [Header("Updates")]
    [SerializeField, Min(1f)] private float rangeEvaluationRateHz = 15f;
    [SerializeField, Min(0.02f)] private float bottleRefreshIntervalSec = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs;

    private sealed class BottleState
    {
        public NetworkedSharedSceneObject Bottle;
        public bool IsOut;
        public bool PendingOut;
        public bool HasPending;
        public float PendingSince;
    }

    private readonly List<NetworkedSharedSceneObject> bottles = new List<NetworkedSharedSceneObject>(8);
    private readonly Dictionary<NetworkedSharedSceneObject, BottleState> states =
        new Dictionary<NetworkedSharedSceneObject, BottleState>(8);
    private readonly List<NetworkedSharedSceneObject> staleKeys = new List<NetworkedSharedSceneObject>(8);
    private readonly HashSet<NetworkedSharedSceneObject> diagnosticLogged =
        new HashSet<NetworkedSharedSceneObject>();
    private float nextEvaluation;
    private float nextRefresh;
    private bool missingRangeLogged;

    public event Action<NetworkedSharedSceneObject, bool> RangeStateChanged;
    public event Action<NetworkedSharedSceneObject> EnteredOperationRange;
    public event Action<NetworkedSharedSceneObject> ExitedOperationRange;

    private void Awake()
    {
        ResolveOperationRange();
    }

    private void OnDisable()
    {
        ClearRuntimeState();
    }

    private void OnDestroy()
    {
        ClearRuntimeState();
        operationRange = null;
        RangeStateChanged = null;
        EnteredOperationRange = null;
        ExitedOperationRange = null;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (now >= nextRefresh)
        {
            RefreshBottles();
            nextRefresh = now + bottleRefreshIntervalSec;
        }

        if (now < nextEvaluation)
        {
            return;
        }
        nextEvaluation = now + 1f / Mathf.Max(1f, rangeEvaluationRateHz);

        if (!ResolveOperationRange())
        {
            if (!missingRangeLogged)
            {
                missingRangeLogged = true;
                Debug.LogWarning("[SharedBottleRangeMonitor] Operation range geometry unavailable", this);
            }
            return;
        }

        missingRangeLogged = false;
        for (int i = bottles.Count - 1; i >= 0; i--)
        {
            NetworkedSharedSceneObject bottle = bottles[i];
            if (!IsEligible(bottle))
            {
                bottles.RemoveAt(i);
                if (bottle != null)
                {
                    states.Remove(bottle);
                    diagnosticLogged.Remove(bottle);
                }
                continue;
            }
            EvaluateBottle(bottle, now);
        }
    }

    private bool ResolveOperationRange()
    {
        if (operationRange == null)
        {
            GameObject rangeObject = GameObject.Find("Origin/BoundingBox");
            if (rangeObject != null)
            {
                operationRange = rangeObject.GetComponent<OperationRangeVolume>();
            }
        }
        return operationRange != null && operationRange.IsConfigured;
    }

    private void RefreshBottles()
    {
        bottles.Clear();
        NetworkedSharedSceneObject[] found = FindObjectsByType<NetworkedSharedSceneObject>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            NetworkedSharedSceneObject bottle = found[i];
            if (!IsEligible(bottle)) continue;
            bottles.Add(bottle);
            if (!states.ContainsKey(bottle))
            {
                states.Add(bottle, new BottleState { Bottle = bottle });
            }
        }

        staleKeys.Clear();
        foreach (KeyValuePair<NetworkedSharedSceneObject, BottleState> pair in states)
        {
            if (pair.Key == null || !bottles.Contains(pair.Key))
            {
                staleKeys.Add(pair.Key);
            }
        }
        for (int i = 0; i < staleKeys.Count; i++)
        {
            diagnosticLogged.Remove(staleKeys[i]);
            states.Remove(staleKeys[i]);
        }
    }

    private bool IsEligible(NetworkedSharedSceneObject bottle)
    {
        if (bottle == null || !bottle.isActiveAndEnabled || !bottle.IsPhotonSharedNetworkBottle)
        {
            return false;
        }
        NetworkObject networkObject = bottle.Object;
        if (networkObject == null || !networkObject.Id.IsValid)
        {
            return false;
        }
        return bottle.SharedOrigin == SharedBottleOrigin.RosDetected
            ? includeRosDetectedBottles
            : includeManualBottles;
    }

    private void EvaluateBottle(NetworkedSharedSceneObject bottle, float now)
    {
        if (!IsEligible(bottle) || !states.TryGetValue(bottle, out BottleState state))
        {
            return;
        }

        if (!TryGetBottleBounds(bottle, out Bounds bounds))
        {
            bounds = new Bounds(bottle.transform.position, Vector3.zero);
        }

        bool isInside;
        OperationRangeVolume.PointDiagnostic centerDiagnostic = default;
        if (containmentMode == BottleContainmentMode.EntireBottleBounds)
        {
            isInside = ContainsAllCorners(bounds, state.IsOut);
            operationRange.TryEvaluateWorldPoint(bounds.center, out centerDiagnostic);
        }
        else
        {
            if (!operationRange.TryEvaluateWorldPoint(bounds.center, out centerDiagnostic))
                return;
            isInside = IsInsideWithSpatialMargin(bounds.center, state.IsOut);
        }
        if (diagnosticLogged.Add(bottle))
        {
            LogDiagnostic(bottle, centerDiagnostic, isInside, state.IsOut);
        }
        bool desiredOut = !isInside;

        if (desiredOut == state.IsOut)
        {
            state.HasPending = false;
            return;
        }

        if (!state.HasPending || state.PendingOut != desiredOut)
        {
            state.HasPending = true;
            state.PendingOut = desiredOut;
            state.PendingSince = now;
            return;
        }

        if (now - state.PendingSince < stateChangeDwellSec)
        {
            return;
        }

        state.HasPending = false;
        state.IsOut = desiredOut;
        LogDiagnostic(bottle, centerDiagnostic, isInside, desiredOut);
        BottleOutOfRangeEffectController effect = bottle.GetComponent<BottleOutOfRangeEffectController>();
        if (effect != null)
        {
            effect.SetOutOfRange(desiredOut);
        }
        BottleTargetEffectController targetEffect = bottle.GetComponent<BottleTargetEffectController>();
        if (targetEffect != null)
        {
            targetEffect.SetOutOfRange(desiredOut);
        }

        RangeStateChanged?.Invoke(bottle, desiredOut);
        if (desiredOut)
        {
            ExitedOperationRange?.Invoke(bottle);
            if (enableDebugLogs)
            {
                Debug.Log("[SharedBottleRangeMonitor] Bottle exited origin_arm local RM"
                    + " networkId=" + networkObjectId(bottle)
                    + " worldCenter=" + centerDiagnostic.WorldPoint.ToString("F3")
                    + " nearestDistance=" + centerDiagnostic.NearestSurfaceDistance.ToString("F3"), this);
            }
        }
        else
        {
            EnteredOperationRange?.Invoke(bottle);
            if (enableDebugLogs)
            {
                Debug.Log("[SharedBottleRangeMonitor] Bottle entered origin_arm local RM"
                    + " networkId=" + networkObjectId(bottle), this);
            }
        }
    }

    private bool ContainsAllCorners(Bounds bounds, bool recovering)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
            if (!IsInsideWithSpatialMargin(corner, recovering))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsInsideWithSpatialMargin(Vector3 worldPoint, bool recovering)
    {
        float margin = recovering ? insideRecoveryMargin : outsideMargin;
        bool centerInside = operationRange.ContainsWorldPoint(worldPoint);
        if (margin <= 0f)
        {
            return centerInside;
        }

        Vector3[] directions =
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };
        // Seven-point sampling is an approximation used only to suppress boundary chatter.
        if (recovering)
        {
            if (!centerInside) return false;
            for (int i = 0; i < directions.Length; i++)
            {
                if (!operationRange.ContainsWorldPoint(worldPoint + directions[i] * margin))
                    return false;
            }
            return true;
        }

        if (centerInside) return true;
        for (int i = 0; i < directions.Length; i++)
        {
            if (operationRange.ContainsWorldPoint(worldPoint + directions[i] * margin))
                return true;
        }
        return false;
    }

    private static bool TryGetBottleBounds(NetworkedSharedSceneObject bottle, out Bounds combined)
    {
        Collider[] colliders = bottle.GetComponentsInChildren<Collider>(false);
        bool initialized = false;
        combined = default;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || IsEffectTransform(collider.transform)) continue;
            if (!initialized) { combined = collider.bounds; initialized = true; }
            else combined.Encapsulate(collider.bounds);
        }
        if (initialized) return true;

        Renderer[] renderers = bottle.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || IsEffectTransform(renderer.transform)) continue;
            if (!initialized) { combined = renderer.bounds; initialized = true; }
            else combined.Encapsulate(renderer.bounds);
        }
        return initialized;
    }

    private static bool IsEffectTransform(Transform item)
    {
        for (Transform current = item; current != null; current = current.parent)
        {
            if (current.name == "LocalTargetEffectRoot"
                || current.name == "LocalOutOfRangeEffectRoot"
                || current.name == "LocalTargetConflictEffectRoot"
                || current.name == "CandidateRing"
                || current.name == "SelectedRing"
                || current.name == "WarningRing"
                || current.name == "OutOfRangeLabel"
                || current.name == "TargetOwnerLabel")
            {
                return true;
            }
        }
        return false;
    }

    private static string networkObjectId(NetworkedSharedSceneObject bottle)
    {
        return bottle != null && bottle.Object != null && bottle.Object.Id.IsValid
            ? bottle.Object.Id.ToString()
            : "Invalid";
    }

    private void LogDiagnostic(
        NetworkedSharedSceneObject bottle,
        OperationRangeVolume.PointDiagnostic diagnostic,
        bool isInside,
        bool stateIsOut)
    {
        if (!enableDebugLogs) return;
        Debug.Log("[OperationRangeDiagnostic]"
            + "\nnetworkId=" + networkObjectId(bottle)
            + "\nworldCenter=" + diagnostic.WorldPoint.ToString("F3")
            + "\nreferenceRoot=" + GetHierarchyPath(operationRange.RangeReferenceRoot)
            + "\ngeometryRoot=" + GetHierarchyPath(operationRange.RangeGeometryRoot)
            + "\nsource=" + operationRange.Source
            + "\ncolliderCount=" + diagnostic.ColliderCount
            + "\ninsideCollider=" + diagnostic.InsideColliderName
            + "\nnearestSurfaceDistance=" + diagnostic.NearestSurfaceDistance.ToString("F3")
            + "\noutsideMargin=" + outsideMargin.ToString("F3")
            + "\ninsideRecoveryMargin=" + insideRecoveryMargin.ToString("F3")
            + "\ncontainmentMode=" + containmentMode
            + "\nresult=" + (isInside ? "IN_RANGE" : "OUT_OF_RANGE")
            + "\nstate=" + (stateIsOut ? "OutOfRange" : "InRange"), this);
    }

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null) return "<null>";
        string path = item.name;
        for (Transform parent = item.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    private void ClearRuntimeState()
    {
        bottles.Clear();
        states.Clear();
        staleKeys.Clear();
        diagnosticLogged.Clear();
        nextEvaluation = 0f;
        nextRefresh = 0f;
    }
}
