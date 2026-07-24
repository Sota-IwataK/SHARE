using System;
using System.Collections.Generic;
using Fusion;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using UnityEngine;
using UnityEngine.XR;

public enum TargetSelectionState
{
    None,
    Candidate,
    Selected
}

public enum TransformDirectionAxis
{
    Forward,
    Backward,
    Up,
    Down,
    Right,
    Left
}

[DisallowMultipleComponent]
public sealed class SharedBottleTargetSelector : MonoBehaviour
{
    [Header("Pose Sources")]
    [SerializeField] private Transform hmdTransform;
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private TransformDirectionAxis leftHandDirectionAxis = TransformDirectionAxis.Forward;

    [Header("Target Conditions")]
    [SerializeField, Range(0.1f, 90f)] private float hmdMaxAngleDeg = 25f;
    [SerializeField, Range(0.1f, 90f)] private float leftHandMaxAngleDeg = 15f;
    [SerializeField, Min(0.05f)] private float maxTargetDistance = 3f;

    [Header("Score Weights")]
    [SerializeField, Min(0f)] private float hmdWeight = 0.35f;
    [SerializeField, Min(0f)] private float handWeight = 0.55f;
    [SerializeField, Min(0f)] private float distanceWeight = 0.10f;

    [Header("Selection Timing")]
    [SerializeField, Min(0f)] private float selectionDwellTimeSec = 0.30f;
    [SerializeField, Min(0f)] private float lostGraceTimeSec = 0.25f;
    [SerializeField, Min(0f)] private float switchDwellTimeSec = 0.20f;
    [SerializeField, Min(0f)] private float leftHandLostGraceTimeSec = 0.15f;
    [SerializeField, Min(0.1f)] private float claimRequestTimeoutSec = 2f;

    [Header("Updates")]
    [SerializeField, Min(1f)] private float selectionUpdateRateHz = 30f;
    [SerializeField, Min(0.02f)] private float candidateRefreshIntervalSec = 0.25f;

    [Header("Bottle Sources")]
    [SerializeField] private bool includeRosDetectedBottles = true;
    [SerializeField] private bool includeManualBottles;
    [SerializeField] private bool keepSelectedTargetWhenHandLost = true;
    [SerializeField] private bool excludeBottleGrabbedByOtherUser = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs;
    [SerializeField] private bool drawDebugRays;
    [SerializeField] private bool drawCandidateLines;

    private readonly List<NetworkedSharedSceneObject> bottles = new List<NetworkedSharedSceneObject>(8);
    private NetworkedSharedSceneObject candidate;
    private NetworkedSharedSceneObject currentTarget;
    private float candidateSince;
    private float candidateLostSince = -1f;
    private float switchCandidateSince;
    private float handLostSince = -1f;
    private float nextSelectionUpdate;
    private float nextCandidateRefresh;
    private bool warnedMissingHmd;
    private bool handLostLogged;
    private NetworkedBottleTargetClaim pendingClaim;
    private Evaluation pendingSelection;
    private bool pendingSwitch;
    private NetworkedSharedSceneObject blockedBottle;
    private float pendingClaimStartedAt = -1f;
    private bool selectedRetainedLogged;
    private bool selectedConditionsLostLogged;

    public NetworkedSharedSceneObject CurrentTarget => currentTarget;
    public NetworkObject CurrentTargetNetworkObject => currentTarget != null ? currentTarget.Object : null;
    public NetworkId CurrentTargetNetworkId
        => CurrentTargetNetworkObject != null ? CurrentTargetNetworkObject.Id : default;
    public bool HasTarget => currentTarget != null;
    public TargetSelectionState CurrentState { get; private set; }

    public event Action<NetworkedSharedSceneObject> TargetCandidateChanged;
    public event Action<NetworkedSharedSceneObject> TargetSelected;
    public event Action<NetworkedSharedSceneObject> TargetCleared;
    public event Action<NetworkedSharedSceneObject, NetworkedSharedSceneObject> TargetChanged;

    private struct Evaluation
    {
        public NetworkedSharedSceneObject Bottle;
        public Vector3 Center;
        public float HmdAngle;
        public float HandAngle;
        public float Distance;
        public float Score;
        public bool HmdPass;
        public bool HandPass;
        public bool DistancePass;
        public string FailureReason;
    }

    private void Awake()
    {
        ResolveHmd();
        ValidateWeights();
    }

    private void OnValidate()
    {
        hmdMaxAngleDeg = Mathf.Max(0.1f, hmdMaxAngleDeg);
        leftHandMaxAngleDeg = Mathf.Max(0.1f, leftHandMaxAngleDeg);
        maxTargetDistance = Mathf.Max(0.05f, maxTargetDistance);
        selectionUpdateRateHz = Mathf.Max(1f, selectionUpdateRateHz);
        candidateRefreshIntervalSec = Mathf.Max(0.02f, candidateRefreshIntervalSec);
        ValidateWeights();
    }

    private void OnDisable()
    {
        CancelPendingClaim();
        ReleaseClaim(currentTarget);
        SetLocallySelected(currentTarget, false);
        HideConflict(blockedBottle);
        ClearCandidateVisual(candidate);
        ClearSelectedVisual(currentTarget);
        candidate = null;
        currentTarget = null;
        blockedBottle = null;
        bottles.Clear();
        CurrentState = TargetSelectionState.None;
        hmdTransform = null;
    }

    private void OnDestroy()
    {
        bottles.Clear();
        candidate = null;
        currentTarget = null;
        hmdTransform = null;
        leftHandTransform = null;
        TargetCandidateChanged = null;
        TargetSelected = null;
        TargetCleared = null;
        TargetChanged = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSelectionUpdate)
        {
            return;
        }

        nextSelectionUpdate = Time.unscaledTime + 1f / selectionUpdateRateHz;
        RemoveDestroyedReferences();
        ValidateCurrentTargetOwnership();
        UpdatePendingClaimTimeout();
        if (!ResolveHmd())
        {
            return;
        }

        if (Time.unscaledTime >= nextCandidateRefresh)
        {
            RefreshBottles();
            nextCandidateRefresh = Time.unscaledTime + candidateRefreshIntervalSec;
        }

        if (!TryGetLeftHandPose(out Vector3 handOrigin, out Vector3 handDirection))
        {
            HandleHandTrackingLost();
            return;
        }

        HandleHandTrackingRestored();
        EvaluateSelection(handOrigin, handDirection);
    }

    private bool ResolveHmd()
    {
        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
        }

        if (hmdTransform != null)
        {
            return true;
        }

        if (!warnedMissingHmd)
        {
            warnedMissingHmd = true;
            Debug.LogWarning("[SharedBottleTargetSelector] HMD transform is unavailable; selection is paused.", this);
        }

        return false;
    }

    private bool TryGetLeftHandPose(out Vector3 origin, out Vector3 direction)
    {
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator != null
            && aggregator.TryGetJoint(TrackedHandJoint.IndexProximal, XRNode.LeftHand, out HandJointPose proximal)
            && aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.LeftHand, out HandJointPose tip))
        {
            origin = proximal.Position;
            direction = tip.Position - proximal.Position;
            if (direction.sqrMagnitude > 0.000001f)
            {
                direction.Normalize();
                return true;
            }
        }

        if (leftHandTransform != null)
        {
            origin = leftHandTransform.position;
            direction = DirectionFromAxis(leftHandTransform, leftHandDirectionAxis);
            return direction.sqrMagnitude > 0.000001f;
        }

        origin = default;
        direction = default;
        return false;
    }

    private static Vector3 DirectionFromAxis(Transform source, TransformDirectionAxis axis)
    {
        switch (axis)
        {
            case TransformDirectionAxis.Backward: return -source.forward;
            case TransformDirectionAxis.Up: return source.up;
            case TransformDirectionAxis.Down: return -source.up;
            case TransformDirectionAxis.Right: return source.right;
            case TransformDirectionAxis.Left: return -source.right;
            default: return source.forward;
        }
    }

    private void RefreshBottles()
    {
        bottles.Clear();
        NetworkedSharedSceneObject[] found = FindObjectsByType<NetworkedSharedSceneObject>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            NetworkedSharedSceneObject bottle = found[i];
            if (IsEligibleBottle(bottle))
            {
                bottles.Add(bottle);
            }
        }
    }

    private bool IsEligibleBottle(NetworkedSharedSceneObject bottle)
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

        if (bottle.SharedOrigin == SharedBottleOrigin.RosDetected ? !includeRosDetectedBottles : !includeManualBottles)
        {
            return false;
        }

        return !excludeBottleGrabbedByOtherUser || !bottle.IsLockedByOther;
    }

    private void EvaluateSelection(Vector3 handOrigin, Vector3 handDirection)
    {
        bool hasBest = TryFindBest(handOrigin, handDirection, out Evaluation best);
        UpdateBlockedConflict(hasBest ? best.Bottle : null);
        Evaluation selectedEval = default;
        bool targetValid = currentTarget != null
            && TryEvaluate(currentTarget, handOrigin, handDirection, out selectedEval);

        if (drawDebugRays)
        {
            Debug.DrawRay(hmdTransform.position, hmdTransform.forward * maxTargetDistance, Color.cyan);
            Debug.DrawRay(handOrigin, handDirection * maxTargetDistance, Color.magenta);
        }

        if (currentTarget == null)
        {
            UpdateInitialCandidate(hasBest ? best : default, hasBest);
            return;
        }

        EvaluateRetainedTargetSwitch(hasBest ? best : default, hasBest, targetValid, selectedEval);
    }

    private void EvaluateRetainedTargetSwitch(
        Evaluation best,
        bool hasBest,
        bool targetConditionsValid,
        Evaluation selectedEvaluation)
    {
        candidateLostSince = -1f;
        if (enableDebugLogs && !selectedRetainedLogged)
        {
            selectedRetainedLogged = true;
            Debug.Log("[SharedBottleTargetSelector] Selected target retained networkId="
                + IdOf(currentTarget), this);
        }
        if (!targetConditionsValid && enableDebugLogs && !selectedConditionsLostLogged)
        {
            selectedConditionsLostLogged = true;
            Debug.Log("[SharedBottleTargetSelector] Selected target retained until another target is confirmed"
                + " networkId=" + IdOf(currentTarget)
                + " reason=" + selectedEvaluation.FailureReason, this);
        }
        else if (targetConditionsValid)
        {
            selectedConditionsLostLogged = false;
        }

        if (!hasBest || best.Bottle == currentTarget || best.Bottle == blockedBottle)
        {
            SetCandidate(null, default);
            return;
        }

        if (candidate != best.Bottle)
        {
            SetCandidate(best.Bottle, best);
            switchCandidateSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - switchCandidateSince >= switchDwellTimeSec)
        {
            RequestTargetClaim(best, true);
        }
    }

    private void UpdateInitialCandidate(Evaluation best, bool hasBest)
    {
        if (hasBest && best.Bottle == blockedBottle)
        {
            SetCandidate(null, default);
            return;
        }
        if (!hasBest)
        {
            if (candidate != null)
            {
                if (candidateLostSince < 0f) candidateLostSince = Time.unscaledTime;
                if (Time.unscaledTime - candidateLostSince >= lostGraceTimeSec)
                {
                    SetCandidate(null, default);
                }
            }
            return;
        }

        candidateLostSince = -1f;
        if (candidate != best.Bottle)
        {
            SetCandidate(best.Bottle, best);
            candidateSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - candidateSince >= selectionDwellTimeSec)
        {
            RequestTargetClaim(best, false);
        }
    }

    private bool TryFindBest(Vector3 handOrigin, Vector3 handDirection, out Evaluation best)
    {
        best = default;
        best.Score = float.PositiveInfinity;
        bool found = false;
        for (int i = bottles.Count - 1; i >= 0; i--)
        {
            NetworkedSharedSceneObject bottle = bottles[i];
            if (!TryEvaluate(bottle, handOrigin, handDirection, out Evaluation evaluation))
            {
                continue;
            }

            if (drawCandidateLines)
            {
                Debug.DrawLine(hmdTransform.position, evaluation.Center, Color.yellow);
                Debug.DrawLine(handOrigin, evaluation.Center, Color.magenta);
            }

            if (evaluation.Score < best.Score)
            {
                best = evaluation;
                found = true;
            }
        }
        return found;
    }

    private bool TryEvaluate(
        NetworkedSharedSceneObject bottle,
        Vector3 handOrigin,
        Vector3 handDirection,
        out Evaluation result)
    {
        result = default;
        if (!IsEligibleBottle(bottle))
        {
            result.FailureReason = "ineligible_or_despawned";
            return false;
        }

        Vector3 center = GetBottleCenter(bottle.gameObject);
        Vector3 hmdDelta = center - hmdTransform.position;
        Vector3 handDelta = center - handOrigin;
        float distance = hmdDelta.magnitude;
        result.Bottle = bottle;
        result.Center = center;
        result.Distance = distance;
        result.DistancePass = distance > 0.0001f && distance <= maxTargetDistance;
        if (distance <= 0.0001f || handDelta.sqrMagnitude <= 0.000001f)
        {
            result.FailureReason = "degenerate_direction";
            return false;
        }

        Vector3 hmdToTarget = hmdDelta / distance;
        Vector3 handToTarget = handDelta.normalized;
        float hmdAngle = Vector3.Angle(hmdTransform.forward, hmdToTarget);
        float handAngle = Vector3.Angle(handDirection, handToTarget);
        result.HmdAngle = hmdAngle;
        result.HandAngle = handAngle;
        result.HmdPass = Vector3.Dot(hmdTransform.forward, hmdToTarget) > 0f
            && hmdAngle <= hmdMaxAngleDeg;
        result.HandPass = Vector3.Dot(handDirection, handToTarget) > 0f
            && handAngle <= leftHandMaxAngleDeg;
        if (!result.HmdPass || !result.HandPass || !result.DistancePass)
        {
            result.FailureReason = !result.HmdPass
                ? "hmd_angle"
                : (!result.HandPass ? "hand_angle" : "distance");
            return false;
        }

        float weightSum = hmdWeight + handWeight + distanceWeight;
        float hmdScore = Mathf.Pow(hmdAngle / hmdMaxAngleDeg, 2f);
        float handScore = Mathf.Pow(handAngle / leftHandMaxAngleDeg, 2f);
        float distanceScore = Mathf.Clamp01(distance / maxTargetDistance);
        result = new Evaluation
        {
            Bottle = bottle,
            Center = center,
            HmdAngle = hmdAngle,
            HandAngle = handAngle,
            Distance = distance,
            HmdPass = true,
            HandPass = true,
            DistancePass = true,
            FailureReason = string.Empty,
            Score = (hmdWeight * hmdScore + handWeight * handScore + distanceWeight * distanceScore)
                    / Mathf.Max(0.0001f, weightSum)
        };
        return true;
    }

    private static Vector3 GetBottleCenter(GameObject bottle)
    {
        Collider[] colliders = bottle.GetComponentsInChildren<Collider>(false);
        if (TryCombinedBounds(colliders, out Bounds bounds))
        {
            return bounds.center;
        }

        Renderer[] renderers = bottle.GetComponentsInChildren<Renderer>(false);
        if (TryCombinedBounds(renderers, out bounds))
        {
            return bounds.center;
        }

        return bottle.transform.position;
    }

    private static bool TryCombinedBounds(Component[] components, out Bounds combined)
    {
        combined = default;
        bool initialized = false;
        for (int i = 0; i < components.Length; i++)
        {
            Bounds bounds;
            if (components[i] is Collider collider)
            {
                if (!collider.enabled) continue;
                bounds = collider.bounds;
            }
            else if (components[i] is Renderer renderer)
            {
                if (!renderer.enabled) continue;
                bounds = renderer.bounds;
            }
            else continue;

            if (!initialized) { combined = bounds; initialized = true; }
            else combined.Encapsulate(bounds);
        }
        return initialized;
    }

    private void SetCandidate(NetworkedSharedSceneObject next, Evaluation evaluation)
    {
        if (candidate == next) return;
        if (pendingClaim != null && pendingSelection.Bottle != next)
        {
            CancelPendingClaim();
        }
        ClearCandidateVisual(candidate);
        candidate = next;
        if (candidate != null && candidate != currentTarget)
        {
            ApplyEffect(candidate, LocalTargetEffectState.Candidate);
            CurrentState = currentTarget == null ? TargetSelectionState.Candidate : TargetSelectionState.Selected;
        }
        else
        {
            CurrentState = currentTarget != null ? TargetSelectionState.Selected : TargetSelectionState.None;
        }

        TargetCandidateChanged?.Invoke(candidate);
        if (enableDebugLogs && candidate != null)
        {
            Debug.Log("[SharedBottleTargetSelector] Candidate changed networkId=" + candidate.Object.Id
                + " hmdAngle=" + evaluation.HmdAngle.ToString("F1")
                + " handAngle=" + evaluation.HandAngle.ToString("F1")
                + " distance=" + evaluation.Distance.ToString("F2")
                + " score=" + evaluation.Score.ToString("F3"), this);
            Debug.Log("[SharedBottleTargetSelector] Candidate state networkId=" + candidate.Object.Id
                + " hmdAngle=" + evaluation.HmdAngle.ToString("F1")
                + " handAngle=" + evaluation.HandAngle.ToString("F1")
                + " hmdPass=" + evaluation.HmdPass
                + " handPass=" + evaluation.HandPass
                + " distancePass=" + evaluation.DistancePass
                + " score=" + evaluation.Score.ToString("F3"), this);
        }
    }

    private void RequestTargetClaim(Evaluation selection, bool switched)
    {
        if (pendingClaim != null || selection.Bottle == null || selection.Bottle == blockedBottle)
        {
            return;
        }

        NetworkedBottleTargetClaim claim = selection.Bottle.GetComponent<NetworkedBottleTargetClaim>();
        if (claim == null)
        {
            Debug.LogWarning("[SharedBottleTargetSelector] Target claim component missing networkId="
                + IdOf(selection.Bottle), this);
            return;
        }

        pendingClaim = claim;
        pendingSelection = selection;
        pendingSwitch = switched;
        pendingClaimStartedAt = Time.unscaledTime;
        claim.LocalClaimAccepted += HandleLocalClaimAccepted;
        claim.LocalClaimRejected += HandleLocalClaimRejected;
        claim.RequestClaim();
    }

    private void HandleLocalClaimAccepted()
    {
        if (pendingClaim == null
            || pendingSelection.Bottle == null
            || pendingSelection.Bottle.GetComponent<NetworkedBottleTargetClaim>() != pendingClaim
            || !pendingClaim.IsOwnedByLocalPlayer)
        {
            return;
        }
        Evaluation selection = pendingSelection;
        bool switched = pendingSwitch;
        CancelPendingClaim(false);
        FinalizeTargetSelection(selection, switched);
    }

    private void HandleLocalClaimRejected(PlayerRef owner)
    {
        NetworkedSharedSceneObject rejectedBottle = pendingSelection.Bottle;
        NetworkedBottleTargetClaim rejectedClaim = pendingClaim;
        CancelPendingClaim(false);
        ClearCandidateVisual(candidate);
        candidate = null;
        blockedBottle = rejectedBottle;
        CurrentState = currentTarget != null ? TargetSelectionState.Selected : TargetSelectionState.None;

        BottleTargetConflictEffectController conflict =
            rejectedBottle != null ? rejectedBottle.GetComponent<BottleTargetConflictEffectController>() : null;
        if (conflict != null && rejectedClaim != null)
        {
            conflict.ShowConflict(rejectedClaim.ResolveOwnerDisplayName(owner));
        }
        if (enableDebugLogs && currentTarget != null)
        {
            Debug.Log("[SharedBottleTargetSelector] Target switch rejected; current selected target retained"
                + " currentNetworkId=" + IdOf(currentTarget)
                + " requestedNetworkId=" + IdOf(rejectedBottle)
                + " owner=" + owner, this);
        }
    }

    private void FinalizeTargetSelection(Evaluation selection, bool switched)
    {
        NetworkedSharedSceneObject old = currentTarget;
        ClearCandidateVisual(candidate);
        currentTarget = selection.Bottle;
        candidate = null;
        candidateLostSince = -1f;
        selectedRetainedLogged = false;
        selectedConditionsLostLogged = false;
        CurrentState = TargetSelectionState.Selected;
        ApplyEffect(currentTarget, LocalTargetEffectState.Selected);
        SetLocallySelected(currentTarget, true);
        HideConflict(currentTarget);

        if (old != null && old != currentTarget)
        {
            SetLocallySelected(old, false);
            ClearSelectedVisual(old);
            ReleaseClaim(old);
        }

        TargetSelected?.Invoke(currentTarget);
        TargetChanged?.Invoke(old, currentTarget);
        if (enableDebugLogs)
        {
            Debug.Log(switched
                ? "[SharedBottleTargetSelector] Target switch completed oldNetworkId="
                    + IdOf(old) + " newNetworkId=" + IdOf(currentTarget)
                : "[SharedBottleTargetSelector] Target selected networkId=" + IdOf(currentTarget)
                    + " dwell=" + selectionDwellTimeSec.ToString("F2"), this);
        }
    }

    private void ClearTarget(string reason, float lostElapsed = 0f, bool releaseClaim = true)
    {
        NetworkedSharedSceneObject old = currentTarget;
        CancelPendingClaim();
        if (releaseClaim) ReleaseClaim(old);
        SetLocallySelected(old, false);
        ClearSelectedVisual(old);
        currentTarget = null;
        CurrentState = candidate != null ? TargetSelectionState.Candidate : TargetSelectionState.None;
        TargetCleared?.Invoke(old);
        TargetChanged?.Invoke(old, null);
        if (enableDebugLogs)
        {
            Debug.Log("[SharedBottleTargetSelector] Target cleared networkId=" + IdOf(old)
                + " reason=" + reason
                + " lostElapsed=" + lostElapsed.ToString("F3")
                + " keepWhenHandLost=" + keepSelectedTargetWhenHandLost, this);
        }
    }

    private void HandleHandTrackingLost()
    {
        if (handLostSince < 0f)
        {
            handLostSince = Time.unscaledTime;
            if (enableDebugLogs && !handLostLogged)
            {
                handLostLogged = true;
                Debug.Log("[SharedBottleTargetSelector] Left hand tracking lost"
                    + " state=" + CurrentState
                    + " targetId=" + IdOf(currentTarget)
                    + " graceElapsed=0.000"
                    + " graceLimit=" + leftHandLostGraceTimeSec.ToString("F3"), this);
            }
        }

        if (Time.unscaledTime - handLostSince < leftHandLostGraceTimeSec) return;
        SetCandidate(null, default);
        if (currentTarget != null && enableDebugLogs && !selectedConditionsLostLogged)
        {
            selectedConditionsLostLogged = true;
            Debug.Log("[SharedBottleTargetSelector] Selected target retained until another target is confirmed"
                + " networkId=" + IdOf(currentTarget)
                + " reason=left_hand_tracking_lost", this);
        }
    }

    private void HandleHandTrackingRestored()
    {
        handLostSince = -1f;
        handLostLogged = false;
    }

    private static void ApplyEffect(
        NetworkedSharedSceneObject bottle,
        LocalTargetEffectState state)
    {
        if (bottle == null) return;
        BottleTargetEffectController effect = bottle.GetComponent<BottleTargetEffectController>();
        if (effect != null) effect.SetLocalTargetState(state);
    }

    private void ClearCandidateVisual(NetworkedSharedSceneObject bottle)
    {
        if (bottle == currentTarget) return;
        ApplyEffect(bottle, LocalTargetEffectState.Normal);
    }

    private static void ClearSelectedVisual(NetworkedSharedSceneObject bottle)
    {
        ApplyEffect(bottle, LocalTargetEffectState.Normal);
    }

    private void CancelPendingClaim(bool release = true)
    {
        if (pendingClaim == null) return;
        pendingClaim.LocalClaimAccepted -= HandleLocalClaimAccepted;
        pendingClaim.LocalClaimRejected -= HandleLocalClaimRejected;
        if (release) pendingClaim.ReleaseClaim();
        pendingClaim = null;
        pendingSelection = default;
        pendingSwitch = false;
        pendingClaimStartedAt = -1f;
    }

    private void UpdatePendingClaimTimeout()
    {
        if (pendingClaim == null
            || pendingClaimStartedAt < 0f
            || Time.unscaledTime - pendingClaimStartedAt < claimRequestTimeoutSec)
        {
            return;
        }

        NetworkedSharedSceneObject requested = pendingSelection.Bottle;
        CancelPendingClaim();
        ClearCandidateVisual(candidate);
        candidate = null;
        CurrentState = currentTarget != null ? TargetSelectionState.Selected : TargetSelectionState.None;
        Debug.LogWarning("[SharedBottleTargetSelector] Target switch timed out; current selected target retained"
            + " currentNetworkId=" + IdOf(currentTarget)
            + " requestedNetworkId=" + IdOf(requested), this);
    }

    private void ValidateCurrentTargetOwnership()
    {
        if (currentTarget == null) return;
        NetworkedBottleTargetClaim claim = currentTarget.GetComponent<NetworkedBottleTargetClaim>();
        if (claim == null || !claim.IsOwnedByLocalPlayer)
        {
            ClearTarget("claim_ownership_lost", 0f, false);
        }
    }

    private void RemoveDestroyedReferences()
    {
        if (!ReferenceEquals(currentTarget, null) && currentTarget == null)
        {
            CancelPendingClaim();
            ClearCandidateVisual(candidate);
            HideConflict(blockedBottle);
            candidate = null;
            blockedBottle = null;
            currentTarget = null;
            pendingSelection = default;
            selectedRetainedLogged = false;
            selectedConditionsLostLogged = false;
            CurrentState = TargetSelectionState.None;
        }

        if (!ReferenceEquals(candidate, null) && candidate == null)
        {
            candidate = null;
            CurrentState = currentTarget != null ? TargetSelectionState.Selected : TargetSelectionState.None;
        }

        if (!ReferenceEquals(blockedBottle, null) && blockedBottle == null)
            blockedBottle = null;

        if (!ReferenceEquals(pendingClaim, null) && pendingClaim == null)
        {
            pendingClaim = null;
            pendingSelection = default;
            pendingSwitch = false;
            pendingClaimStartedAt = -1f;
        }
    }

    private void UpdateBlockedConflict(NetworkedSharedSceneObject bestBottle)
    {
        if (blockedBottle == null) return;
        NetworkedBottleTargetClaim claim = blockedBottle.GetComponent<NetworkedBottleTargetClaim>();
        if (bestBottle != blockedBottle || claim == null || !claim.IsOwnedByOtherPlayer)
        {
            HideConflict(blockedBottle);
            blockedBottle = null;
        }
    }

    private static void HideConflict(NetworkedSharedSceneObject bottle)
    {
        if (bottle == null) return;
        BottleTargetConflictEffectController conflict =
            bottle.GetComponent<BottleTargetConflictEffectController>();
        if (conflict != null) conflict.HideConflict();
    }

    private static void SetLocallySelected(NetworkedSharedSceneObject bottle, bool selected)
    {
        if (bottle == null) return;
        BottleOutOfRangeEffectController rangeEffect =
            bottle.GetComponent<BottleOutOfRangeEffectController>();
        if (rangeEffect != null) rangeEffect.SetLocallySelected(selected);
    }

    private static void ReleaseClaim(NetworkedSharedSceneObject bottle)
    {
        if (bottle == null) return;
        NetworkedBottleTargetClaim claim = bottle.GetComponent<NetworkedBottleTargetClaim>();
        if (claim != null) claim.ReleaseClaim();
    }

    private static string IdOf(NetworkedSharedSceneObject bottle)
    {
        return bottle != null && bottle.Object != null ? bottle.Object.Id.ToString() : "Invalid";
    }

    private void ValidateWeights()
    {
        if (hmdWeight + handWeight + distanceWeight <= 0.0001f)
        {
            Debug.LogWarning("[SharedBottleTargetSelector] Score weights total zero; using equal runtime normalization.", this);
        }
        else if (!Mathf.Approximately(hmdWeight + handWeight + distanceWeight, 1f))
        {
            Debug.LogWarning("[SharedBottleTargetSelector] Score weights do not total 1.0; they are normalized internally.", this);
        }
    }
}
