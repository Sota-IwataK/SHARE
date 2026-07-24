using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

public enum PhotonSharedBottleDetectionVisualState
{
    None = 0,
    Tracked = 1,
    Stale = 2,
    Lost = 3
}

public enum SharedBottleOrigin
{
    Manual = 0,
    RosDetected = 1
}

[DisallowMultipleComponent]
public class NetworkedSharedSceneObject :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    [Header("Shared Object")]
    public SharedNetworkObjectKind objectKind = SharedNetworkObjectKind.Bottle;
    public bool allowStateAuthorityGrab = true;
    public bool isPhotonSharedNetworkBottle;
    public SharedBottleOrigin bottleOrigin = SharedBottleOrigin.Manual;
    public int detectedBottleTrackId = -1;

    [Header("PC Editor Test Grab")]
    public bool allowMouseEditorGrab = true;
    public float mouseDragDistanceMeters = 0.7f;
    public float mouseFollowSpeed = 18f;
    public LayerMask pcBottleRaycastMask = ~0;
    public float pcBottleGrabPermissionTimeoutSeconds = 3f;

    [Header("Networked Pose Sync")]
    public bool syncPose = true;
    public float poseSendPositionThreshold = 0.0025f;
    public float poseSendRotationThresholdDegrees = 0.5f;
    public float poseLogIntervalSeconds = 0.25f;
    public float localGrabPoseSendIntervalSeconds = 0.04f;

    private bool localDragActive;
    private bool localGrabUsesMouseDrag;
    private bool pendingAuthorityRequest;
    private bool pendingMouseDrag;
    private bool xrSelectGrabActive;
    private Vector3 localDragTarget;
    private Vector3 lastSubmittedLocalPosePosition;
    private Quaternion lastSubmittedLocalPoseRotation = Quaternion.identity;
    private float lastLocalGrabPoseSendTime = -999f;
    private bool localLockOwnerPoseApplySkippedLogged;
    private int lastAppliedPoseVersion = -1;
    private int lastReceivedPoseVersion = -1;
    private float lastPoseSendLogTime = -999f;
    private float lastPoseReceiveLogTime = -999f;
    private float lastPoseApplyLogTime = -999f;
    private XRBaseInteractable xrInteractable;
    private bool xrCallbacksRegistered;
    private ObjectManipulator objectManipulator;
    private bool objectManipulatorCallbacksRegistered;
    private bool remoteInteractionEnabledLogged;

#if UNITY_EDITOR || UNITY_STANDALONE
    private bool pcMousePointerHeld;
    private bool pcMouseDragStartedLogged;
    private bool pcMouseGrabPermissionWaitLogged;
    private bool pcMouseGrabGrantedLogged;
    private bool pcInputStateLogged;
    private string lastPcInputStateSignature;
    private float lastPcInputStateLogTime = -999f;
    private float pcMouseGrabPermissionRequestStartTime = -999f;
    private float pcMouseDragPlaneY;
    private float lastPcMouseDraggingLogTime = -999f;
    private Vector3 lastPcMouseDraggingLogWorldPosition;
#endif

    public bool IsPhotonSharedNetworkBottle => isPhotonSharedNetworkBottle;
    public bool IsLocalGrabActive => localDragActive || pendingAuthorityRequest || xrSelectGrabActive;

#if FUSION_WEAVER && FUSION2
    private PlayerRef lastObservedStateAuthority;
    private bool authorityObservationInitialized;

    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Quaternion NetworkRotation { get; set; }
    [Networked] public int NetworkPoseVersion { get; set; }
    [Networked] public NetworkBool IsGrabbed { get; set; }
    [Networked] public PlayerRef LockOwner { get; set; }
    [Networked] public PlayerRef SpawnedByPlayer { get; set; }
    [Networked] public float SpawnedAtRunnerTime { get; set; }
    [Networked] public int NetworkDetectionVisualState { get; set; }
    [Networked] public int DetectedBottleTrackId { get; set; }
    [Networked] public int NetworkBottleOrigin { get; set; }

    public string DebugSpawnedByPlayer => SpawnedByPlayer.ToString();
    public string DebugSpawnedAtRunnerTime => SpawnedAtRunnerTime.ToString("F3");
    public bool IsGrabbedByAnyUser => IsGrabbed;
    public bool HasLocalStateAuthority => HasStateAuthority;
    public int SharedDetectedBottleTrackId => DetectedBottleTrackId;
    public SharedBottleOrigin SharedOrigin => ClampBottleOrigin(NetworkBottleOrigin);
    public PhotonSharedBottleDetectionVisualState DetectionVisualState
        => ClampDetectionVisualState(NetworkDetectionVisualState);

    public bool IsLockedByOther
    {
        get
        {
            if (!IsGrabbed)
            {
                return false;
            }

            return Runner != null && LockOwner != Runner.LocalPlayer;
        }
    }

    public override void Spawned()
    {
        EnsureSharedBottleCollidersEnabled();
        if (HasStateAuthority)
        {
            PublishAuthorityPose("Spawned", true);
        }
        else
        {
            ApplyRemotePose("Spawned");
        }

        Debug.Log("[NetworkedSharedSceneObject] Spawned " + name
            + " kind=" + objectKind
            + " sharedNetworkBottle=" + isPhotonSharedNetworkBottle
            + " hasStateAuthority=" + HasStateAuthority
            + " inputAuthority=" + Object.InputAuthority
            + " stateAuthority=" + Object.StateAuthority
            + " localPlayer=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none"));

        if (isPhotonSharedNetworkBottle)
        {
            Debug.Log("[NetworkedSharedSceneObject] BOTTLE_SPAWN_OBSERVED"
                + " networkId=" + (Object != null && Object.Id.IsValid ? Object.Id.ToString() : "Invalid")
                + " localPlayer=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none")
                + " spawnedBy=" + SpawnedByPlayer
                + " spawnedAt=" + SpawnedAtRunnerTime.ToString("F3")
                + " hasStateAuthority=" + HasStateAuthority);
        }

        InitializeAuthorityObservation();
        EnsureRemoteInteractionEnabled();
        NotifyVisualNetworkSpawned();
    }

    public override void FixedUpdateNetwork()
    {
        TrackAuthorityChanged();
        CancelLocalGrabIfLockedByOther();
        TryActivateLocalGrabIfGranted();

#if UNITY_EDITOR || UNITY_STANDALONE
        TryLogPcBottleDragStarted();
#endif

        if (CanPublishLocalDragPose())
        {
            if (localGrabUsesMouseDrag)
            {
                transform.position = Vector3.Lerp(transform.position, localDragTarget, Runner.DeltaTime * mouseFollowSpeed);
            }

            SubmitLocalBottlePose(false);
        }

        if (HasStateAuthority && !IsGrabbed)
        {
            PublishAuthorityPose("FixedUpdateNetwork", false);
        }
    }

    public override void Render()
    {
        TrackAuthorityChanged();
        CancelLocalGrabIfLockedByOther();
        TryActivateLocalGrabIfGranted();
        EnsureRemoteInteractionEnabled();
        SubmitLocalBottlePose(false);

        if (!HasStateAuthority)
        {
            ApplyRemotePose("Render");
        }
    }

    private void LateUpdate()
    {
        if (Object == null || Runner == null || !Runner.IsRunning || HasStateAuthority)
        {
            return;
        }

        TrackAuthorityChanged();
        CancelLocalGrabIfLockedByOther();
        TryActivateLocalGrabIfGranted();
        EnsureRemoteInteractionEnabled();
        SubmitLocalBottlePose(false);
        ApplyRemotePose("LateUpdate");
    }

    public bool TryBeginLocalGrab()
    {
        return TryBeginLocalGrab(false);
    }

    public bool TryBeginLocalGrab(bool useMouseDrag)
    {
        if (!allowStateAuthorityGrab)
        {
            return false;
        }

        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            Debug.LogWarning("[NetworkedSharedSceneObject] Object is not spawned by Fusion yet: " + name);
            return false;
        }

        if (useMouseDrag)
        {
            localDragTarget = transform.position;
        }

        if (IsLockedByOther)
        {
            RejectLocalGrab("LockedByOther");
            return false;
        }

        if (IsLocalLockOwner() || pendingAuthorityRequest)
        {
            return true;
        }

        pendingAuthorityRequest = true;
        pendingMouseDrag = useMouseDrag;
        LogBottleState("PHOTON_BOTTLE_GRAB_REQUEST");
        RPC_RequestBottleGrab(Runner.LocalPlayer, useMouseDrag);
        TryActivateLocalGrabIfGranted();
        return true;
    }

    public void EndLocalGrab()
    {
        bool wasLocalOwner = IsLocalLockOwner();
        bool shouldSendRelease = wasLocalOwner || pendingAuthorityRequest;
        Vector3 finalPosition = localGrabUsesMouseDrag ? localDragTarget : transform.position;
        Quaternion finalRotation = transform.rotation;
        localDragActive = false;
        localGrabUsesMouseDrag = false;
        localLockOwnerPoseApplySkippedLogged = false;
        pendingAuthorityRequest = false;
        pendingMouseDrag = false;

        if (shouldSendRelease && Runner != null)
        {
            RPC_ReleaseBottleGrab(Runner.LocalPlayer, finalPosition, finalRotation);
        }
    }

    private void TryActivateLocalGrabIfGranted()
    {
        if (!pendingAuthorityRequest || !IsLocalLockOwner())
        {
            return;
        }

        pendingAuthorityRequest = false;
        localGrabUsesMouseDrag = pendingMouseDrag;
        pendingMouseDrag = false;
        localDragActive = true;
        lastSubmittedLocalPosePosition = transform.position;
        lastSubmittedLocalPoseRotation = transform.rotation;
        lastLocalGrabPoseSendTime = -999f;
        localLockOwnerPoseApplySkippedLogged = false;
        LogBottleState("PHOTON_BOTTLE_GRAB_GRANTED");
        SubmitLocalBottlePose(true);
    }

    public void SetSharedSpawnMetadata(PlayerRef spawnedBy, float spawnedAtRunnerTime)
    {
        if (!HasStateAuthority)
        {
            return;
        }

        SpawnedByPlayer = spawnedBy;
        SpawnedAtRunnerTime = spawnedAtRunnerTime;
    }

    public void SetDetectedBottleMetadata(int trackId, SharedBottleOrigin origin)
    {
        detectedBottleTrackId = trackId;
        bottleOrigin = origin;
        if (!HasStateAuthority)
        {
            return;
        }

        DetectedBottleTrackId = trackId;
        NetworkBottleOrigin = (int)origin;
    }

    public bool TryRequestSharedStateAuthority(string reason)
    {
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return false;
        }

        if (!HasStateAuthority)
        {
            Debug.Log("[NetworkedSharedSceneObject] StateAuthority unavailable for shared pose update"
            + " reason=" + reason
            + " object=" + name
            + " localPlayer=" + Runner.LocalPlayer);
            return false;
        }

        return !IsGrabbed;
    }

    public bool TryApplyAuthorityPose(Vector3 position, Quaternion rotation, string reason, bool force)
    {
        if (!syncPose || !HasStateAuthority)
        {
            return false;
        }

        if (IsGrabbed)
        {
            return false;
        }

        transform.SetPositionAndRotation(position, rotation);
        PublishAuthorityPose(reason, force);
        return true;
    }

    public bool TrySetDetectionVisualState(PhotonSharedBottleDetectionVisualState state, string reason)
    {
        if (!isPhotonSharedNetworkBottle || Object == null || Runner == null || !Runner.IsRunning)
        {
            return false;
        }

        if (!HasStateAuthority)
        {
            return false;
        }

        NetworkDetectionVisualState = (int)state;
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestBottleGrab(PlayerRef requestPlayer, bool useMouseDrag, RpcInfo info = default)
    {
        HostHandleBottleGrabRequest(ResolveRpcRequestPlayer(requestPlayer, info), useMouseDrag);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_SubmitBottlePose(PlayerRef requestPlayer, Vector3 position, Quaternion rotation, RpcInfo info = default)
    {
        HostApplyBottlePose(ResolveRpcRequestPlayer(requestPlayer, info), position, rotation, "RPC_SubmitBottlePose", false);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_ReleaseBottleGrab(PlayerRef requestPlayer, Vector3 position, Quaternion rotation, RpcInfo info = default)
    {
        HostReleaseBottleGrab(ResolveRpcRequestPlayer(requestPlayer, info), position, rotation);
    }

    private PlayerRef ResolveRpcRequestPlayer(PlayerRef requestPlayer, RpcInfo info)
    {
        if (info.Source != PlayerRef.None)
        {
            return info.Source;
        }

        if (requestPlayer != PlayerRef.None)
        {
            return requestPlayer;
        }

        return Runner != null ? Runner.LocalPlayer : PlayerRef.None;
    }

    private void HostHandleBottleGrabRequest(PlayerRef requestPlayer, bool useMouseDrag)
    {
        LogHostBottleState("PHOTON_BOTTLE_HOST_GRAB_REQUEST", requestPlayer);

        if (!HasStateAuthority)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_GRAB_REJECTED", requestPlayer, "NotStateAuthority");
            return;
        }

        if (requestPlayer == PlayerRef.None)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_GRAB_REJECTED", requestPlayer, "InvalidRequestPlayer");
            return;
        }

        if (IsGrabbed && LockOwner != requestPlayer)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_GRAB_REJECTED", requestPlayer, "LockedByOther");
            return;
        }

        IsGrabbed = true;
        LockOwner = requestPlayer;
        WriteNetworkPose("HostGrabStart", true);
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.GrabRpc);
        LogHostBottleState("PHOTON_BOTTLE_HOST_GRAB_GRANTED", requestPlayer);
        LogBottleState("PHOTON_BOTTLE_GRAB_GRANTED");
    }

    private bool HostApplyBottlePose(PlayerRef requestPlayer, Vector3 position, Quaternion rotation, string reason, bool force)
    {
        if (!HasStateAuthority)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_POSE_REJECTED", requestPlayer, "NotStateAuthority");
            return false;
        }

        if (!syncPose)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_POSE_REJECTED", requestPlayer, "SyncPoseDisabled");
            return false;
        }

        if (!IsGrabbed || LockOwner != requestPlayer)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_POSE_REJECTED", requestPlayer, "NotLockOwner");
            return false;
        }

        transform.SetPositionAndRotation(position, rotation);
        bool written = WriteNetworkPose(reason, force);
        if (written)
        {
            CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.GrabRpc);
            LogHostBottleState("PHOTON_BOTTLE_HOST_POSE_ACCEPTED", requestPlayer);
        }

        return written;
    }

    private void HostReleaseBottleGrab(PlayerRef requestPlayer, Vector3 position, Quaternion rotation)
    {
        if (!HasStateAuthority)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_POSE_REJECTED", requestPlayer, "ReleaseNotStateAuthority");
            return;
        }

        if (!IsGrabbed || LockOwner != requestPlayer)
        {
            LogHostBottleState("PHOTON_BOTTLE_HOST_POSE_REJECTED", requestPlayer, "ReleaseNotLockOwner");
            return;
        }

        HostApplyBottlePose(requestPlayer, position, rotation, "Release", true);
        Debug.Log("[NetworkedSharedSceneObject] BOTTLE_POSE_RELEASE_FINAL"
            + " position=" + FormatVector(transform.position)
            + " rotation=" + FormatQuaternion(transform.rotation)
            + " player=" + requestPlayer
            + " object=" + name);
        IsGrabbed = false;
        LockOwner = PlayerRef.None;
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.GrabRpc);
        LogHostBottleState("PHOTON_BOTTLE_HOST_RELEASED", requestPlayer);
        LogBottleState("PHOTON_BOTTLE_GRAB_RELEASED");
    }

    private void PublishAuthorityPose(string reason, bool force)
    {
        if (!CanPublishAuthorityPose())
        {
            return;
        }

        WriteNetworkPose(reason, force);
    }

    private bool WriteNetworkPose(string reason, bool force)
    {
        if (!syncPose || !HasStateAuthority)
        {
            return false;
        }

        Vector3 currentPosition = transform.position;
        Quaternion currentRotation = transform.rotation;

        bool changed = force
            || NetworkPoseVersion == 0
            || Vector3.Distance(NetworkPosition, currentPosition) >= Mathf.Max(0.0001f, poseSendPositionThreshold)
            || Quaternion.Angle(NetworkRotation, currentRotation) >= Mathf.Max(0.01f, poseSendRotationThresholdDegrees);

        if (!changed)
        {
            return false;
        }

        NetworkPosition = currentPosition;
        NetworkRotation = currentRotation;
        NetworkPoseVersion++;

        if (ShouldLogPose(ref lastPoseSendLogTime, force))
        {
            CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
                "[NetworkedSharedSceneObject] BOTTLE_POSE_SEND"
                + " reason=" + reason
                + " version=" + NetworkPoseVersion
                + " player=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none")
                + " position=" + FormatVector(NetworkPosition)
                + " rotation=" + FormatQuaternion(NetworkRotation)
                + " object=" + name);
        }

        return true;
    }

    private bool CanPublishAuthorityPose()
    {
        if (!syncPose || !HasStateAuthority)
        {
            return false;
        }

        return !IsGrabbed;
    }

    private bool CanPublishLocalDragPose()
    {
        return IsGrabbed
            && IsLocalLockOwner()
            && localDragActive;
    }

    private void SubmitLocalBottlePose(bool force)
    {
        if (Runner == null || !Runner.IsRunning || !CanPublishLocalDragPose())
        {
            return;
        }

        Vector3 posePosition = localGrabUsesMouseDrag ? localDragTarget : transform.position;
        Quaternion poseRotation = transform.rotation;
        float now = Time.unscaledTime;
        float minInterval = Mathf.Max(0.02f, localGrabPoseSendIntervalSeconds);
        float elapsed = now - lastLocalGrabPoseSendTime;
        bool intervalElapsed = elapsed >= minInterval;
        float sendIntervalMs = lastLocalGrabPoseSendTime < 0f ? 0f : Mathf.Max(0f, elapsed * 1000f);
        bool changed = force
            || Vector3.Distance(lastSubmittedLocalPosePosition, posePosition) >= Mathf.Max(0.0001f, poseSendPositionThreshold)
            || Quaternion.Angle(lastSubmittedLocalPoseRotation, poseRotation) >= Mathf.Max(0.01f, poseSendRotationThresholdDegrees);
        if (!force && (!changed || !intervalElapsed))
        {
            return;
        }

        lastSubmittedLocalPosePosition = posePosition;
        lastSubmittedLocalPoseRotation = poseRotation;
        lastLocalGrabPoseSendTime = now;
        LogLocalGrabPoseSend(posePosition, poseRotation, sendIntervalMs);

        if (HasStateAuthority)
        {
            HostApplyBottlePose(Runner.LocalPlayer, posePosition, poseRotation, "LocalStateAuthorityPose", force);
            return;
        }

        RPC_SubmitBottlePose(Runner.LocalPlayer, posePosition, poseRotation);
    }

    private bool ShouldSuppressNetworkPoseApplyForLocalGrab()
    {
        return IsGrabbed
            && IsLocalLockOwner()
            && localDragActive;
    }

    private void LogLocalGrabPoseSend(Vector3 position, Quaternion rotation, float sendIntervalMs)
    {
        if (!isPhotonSharedNetworkBottle)
        {
            return;
        }

        CommunicationHealthMonitor.Verbose(CommunicationChannel.GrabRpc,
            "[NetworkedSharedSceneObject] PHOTON_BOTTLE_LOCAL_GRAB_POSE_SEND"
            + " object=" + name
            + " player=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none")
            + " isLocalLockOwner=" + IsLocalLockOwner()
            + " localGrabActive=" + localDragActive
            + " position=" + FormatVector(position)
            + " rotation=" + FormatQuaternion(rotation)
            + " sendIntervalMs=" + sendIntervalMs.ToString("F1"));
    }

    private void LogPoseApplySkippedForLocalGrab()
    {
        if (localLockOwnerPoseApplySkippedLogged || !isPhotonSharedNetworkBottle)
        {
            return;
        }

        localLockOwnerPoseApplySkippedLogged = true;
        Debug.Log("[NetworkedSharedSceneObject] PHOTON_BOTTLE_POSE_APPLY_SKIPPED"
            + " reason=LocalLockOwnerManipulating"
            + " object=" + name
            + " localPlayer=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none")
            + " isLocalLockOwner=" + IsLocalLockOwner()
            + " localGrabActive=" + localDragActive
            + " position=" + FormatVector(transform.position)
            + " rotation=" + FormatQuaternion(transform.rotation)
            + " version=" + NetworkPoseVersion);
    }

    private void ApplyRemotePose(string reason)
    {
        if (!syncPose || HasStateAuthority || !IsValidNetworkRotation(NetworkRotation))
        {
            return;
        }

        if (ShouldSuppressNetworkPoseApplyForLocalGrab())
        {
            LogPoseApplySkippedForLocalGrab();
            return;
        }

        localLockOwnerPoseApplySkippedLogged = false;
        if (NetworkPoseVersion != lastReceivedPoseVersion)
        {
            lastReceivedPoseVersion = NetworkPoseVersion;
            if (ShouldLogPose(ref lastPoseReceiveLogTime, true))
            {
                CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
                    "[NetworkedSharedSceneObject] BOTTLE_POSE_RECEIVE"
                    + " reason=" + reason
                    + " version=" + NetworkPoseVersion
                    + " position=" + FormatVector(NetworkPosition)
                    + " rotation=" + FormatQuaternion(NetworkRotation)
                    + " object=" + name);
            }
        }

        transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);

        if (NetworkPoseVersion != lastAppliedPoseVersion || ShouldLogPose(ref lastPoseApplyLogTime, false))
        {
            lastAppliedPoseVersion = NetworkPoseVersion;
            CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
                "[NetworkedSharedSceneObject] BOTTLE_POSE_APPLY"
                + " remote=true"
                + " reason=" + reason
                + " version=" + NetworkPoseVersion
                + " position=" + FormatVector(transform.position)
                + " rotation=" + FormatQuaternion(transform.rotation)
                + " object=" + name);
        }
    }

    private void InitializeAuthorityObservation()
    {
        if (Object == null)
        {
            authorityObservationInitialized = false;
            lastObservedStateAuthority = PlayerRef.None;
            return;
        }

        authorityObservationInitialized = true;
        lastObservedStateAuthority = Object.StateAuthority;
    }

    private void TrackAuthorityChanged()
    {
        if (Object == null)
        {
            return;
        }

        if (!authorityObservationInitialized)
        {
            InitializeAuthorityObservation();
            return;
        }

        if (lastObservedStateAuthority == Object.StateAuthority)
        {
            return;
        }

        lastObservedStateAuthority = Object.StateAuthority;
        LogBottleState("PHOTON_BOTTLE_AUTHORITY_CHANGED");
    }

    private void CancelLocalGrabIfLockedByOther()
    {
        if (!IsLockedByOther)
        {
            return;
        }

        if (!pendingAuthorityRequest && !localDragActive && !xrSelectGrabActive)
        {
            return;
        }

        RejectLocalGrab("LockedByOther");
    }

    private void RejectLocalGrab(string reason)
    {
        localDragActive = false;
        localGrabUsesMouseDrag = false;
        localLockOwnerPoseApplySkippedLogged = false;
        pendingAuthorityRequest = false;
        pendingMouseDrag = false;
        xrSelectGrabActive = false;
        LogBottleState("PHOTON_BOTTLE_GRAB_REJECTED", reason);
    }

    private bool IsLocalLockOwner()
    {
        return Runner != null && IsGrabbed && LockOwner == Runner.LocalPlayer;
    }

    private void EnsureRemoteInteractionEnabled()
    {
        if (!isPhotonSharedNetworkBottle
            || remoteInteractionEnabledLogged
            || Object == null
            || Runner == null
            || !Runner.IsRunning
            || HasStateAuthority)
        {
            return;
        }

        bool changed = false;
        changed |= EnsureSharedBottleCollidersEnabled();

        if (objectManipulator == null)
        {
            objectManipulator = GetComponent<ObjectManipulator>();
        }

        if (objectManipulator != null && !objectManipulator.enabled)
        {
            objectManipulator.enabled = true;
            changed = true;
        }

        if (xrInteractable == null)
        {
            xrInteractable = GetComponent<XRBaseInteractable>();
        }

        if (xrInteractable != null && !xrInteractable.enabled)
        {
            xrInteractable.enabled = true;
            changed = true;
        }

        RegisterGrabCallbacks();
        remoteInteractionEnabledLogged = true;
        LogBottleState("PHOTON_BOTTLE_REMOTE_INTERACTION_ENABLED"
            + " changed=" + changed);
    }

    private void LogBottleState(string eventName)
    {
        LogBottleState(eventName, string.Empty);
    }

    private void LogBottleState(string eventName, string reason)
    {
        if (!isPhotonSharedNetworkBottle)
        {
            return;
        }

        string reasonText = string.IsNullOrWhiteSpace(reason) ? string.Empty : " reason=" + reason;
        string message = "[NetworkedSharedSceneObject] " + eventName
            + reasonText
            + " object=" + name
            + " localPlayer=" + (Runner != null ? Runner.LocalPlayer.ToString() : "none")
            + " grabOwner=" + LockOwner
            + " stateAuthority=" + (Object != null ? Object.StateAuthority.ToString() : "none")
            + " inputAuthority=" + (Object != null ? Object.InputAuthority.ToString() : "none")
            + " isLockedByOther=" + IsLockedByOther;
        CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync, message);
    }

    private void LogHostBottleState(string eventName, PlayerRef requestPlayer, string reason = "")
    {
        if (!isPhotonSharedNetworkBottle)
        {
            return;
        }

        string reasonText = string.IsNullOrWhiteSpace(reason) ? string.Empty : " reason=" + reason;
        string message = "[NetworkedSharedSceneObject] " + eventName
            + reasonText
            + " object=" + name
            + " requestPlayer=" + requestPlayer
            + " lockOwner=" + LockOwner
            + " stateAuthority=" + (Object != null ? Object.StateAuthority.ToString() : "none")
            + " inputAuthority=" + (Object != null ? Object.InputAuthority.ToString() : "none")
            + " isHost=" + HasStateAuthority
            + " isGrabbed=" + IsGrabbed
            + " position=" + FormatVector(transform.position)
            + " rotation=" + FormatQuaternion(transform.rotation)
            + " version=" + NetworkPoseVersion;
        if (eventName.IndexOf("REJECTED", System.StringComparison.Ordinal) >= 0)
        {
            Debug.LogWarning(message, this);
        }
        else
        {
            CommunicationHealthMonitor.Verbose(CommunicationChannel.GrabRpc, message);
        }
    }
#else
    public bool IsLockedByOther => false;
    public string DebugSpawnedByPlayer => "Unavailable";
    public string DebugSpawnedAtRunnerTime => "Unavailable";
    public bool IsGrabbedByAnyUser => localDragActive;
    public bool HasLocalStateAuthority => false;
    public int SharedDetectedBottleTrackId => detectedBottleTrackId;
    public SharedBottleOrigin SharedOrigin => bottleOrigin;
    private PhotonSharedBottleDetectionVisualState localDetectionVisualState = PhotonSharedBottleDetectionVisualState.None;
    public PhotonSharedBottleDetectionVisualState DetectionVisualState => localDetectionVisualState;

    public bool TryRequestSharedStateAuthority(string reason)
    {
        return false;
    }

    public bool TryApplyAuthorityPose(Vector3 position, Quaternion rotation, string reason, bool force)
    {
        transform.SetPositionAndRotation(position, rotation);
        return true;
    }

    public bool TrySetDetectionVisualState(PhotonSharedBottleDetectionVisualState state, string reason)
    {
        localDetectionVisualState = state;
        return true;
    }

    public void SetDetectedBottleMetadata(int trackId, SharedBottleOrigin origin)
    {
        detectedBottleTrackId = trackId;
        bottleOrigin = origin;
    }

    public bool TryBeginLocalGrab()
    {
        return TryBeginLocalGrab(false);
    }

    public bool TryBeginLocalGrab(bool useMouseDrag)
    {
        localDragActive = true;
        return true;
    }

    public void EndLocalGrab()
    {
        localDragActive = false;
    }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE
    private void Update()
    {
        HandlePcBottleMouseInput();
    }

    private void HandlePcBottleMouseInput()
    {
        if (!ShouldProcessPcBottleMouseInput())
        {
            return;
        }

        EnsureSharedBottleCollidersEnabled();
        bool leftMouseDown = Input.GetMouseButtonDown(0);
        bool leftMouseHeld = Input.GetMouseButton(0);
        LogPcBottleInputStateIfNeeded(leftMouseDown, leftMouseHeld);

        if (Input.GetMouseButtonDown(0))
        {
            TryBeginPcBottleMouseGrab();
        }

        if (!pcMousePointerHeld)
        {
            return;
        }

        if (IsLockedByOther)
        {
            RejectPcMouseLocalGrab("LockedByOther");
            LogPcBottleGrabRejected("LockedByOther", localDragTarget);
            ClearPcMouseState();
            return;
        }

        if (pendingAuthorityRequest && !HasPcMouseDragOwnership())
        {
            CheckPcBottleGrabPermissionTimeout();
        }

        if (Input.GetMouseButton(0))
        {
            if (TryUpdateMouseDragTargetOnPlane(out Vector3 worldPosition))
            {
                if (localDragActive && HasPcMouseDragOwnership())
                {
                    TryLogPcBottleDragStarted(worldPosition);
                    LogPcBottleDraggingIfNeeded(worldPosition);
                }
                else if (pendingAuthorityRequest && !pcMouseGrabPermissionWaitLogged)
                {
                    LogPcBottleGrabPermissionWait(worldPosition);
                }
            }

            return;
        }

        if (Input.GetMouseButtonUp(0))
        {
            ReleasePcBottleMouseGrab();
        }
    }

    private void TryBeginPcBottleMouseGrab()
    {
        bool pointerHit = TryGetPcBottlePointerHit(out Vector3 hitPoint);
        if (!pointerHit)
        {
            return;
        }

        if (!CanUsePcBottleMouseInput(out string unavailableReason))
        {
            LogPcBottleGrabRejected(unavailableReason, hitPoint);
            return;
        }

        pcMousePointerHeld = true;
        pcMouseDragStartedLogged = false;
        pcMouseGrabPermissionWaitLogged = false;
        pcMouseGrabGrantedLogged = false;
        pcMouseGrabPermissionRequestStartTime = -999f;
        lastPcMouseDraggingLogTime = -999f;
        lastPcMouseDraggingLogWorldPosition = hitPoint;
        pcMouseDragPlaneY = transform.position.y;
        localDragTarget = transform.position;

        LogPcBottleState("PHOTON_PC_BOTTLE_GRAB_REQUEST", hitPoint);
        bool accepted = TryBeginLocalGrab(true);
        if (!accepted)
        {
            LogPcBottleGrabRejected(GetPcBottleGrabRejectedReason(), hitPoint);
            ClearPcMouseState();
            return;
        }

        if (!HasPcMouseDragOwnership())
        {
            LogPcBottleGrabPermissionWait(localDragTarget);
        }

        if (TryUpdateMouseDragTargetOnPlane(out Vector3 worldPosition))
        {
            if (localDragActive && HasPcMouseDragOwnership())
            {
                TryLogPcBottleDragStarted(worldPosition);
            }
            else
            {
                LogPcBottleGrabPermissionWait(worldPosition);
            }
        }
    }

    private void ReleasePcBottleMouseGrab()
    {
        Vector3 worldPosition = localDragTarget;
        if (TryUpdateMouseDragTargetOnPlane(out Vector3 updatedWorldPosition))
        {
            worldPosition = updatedWorldPosition;
        }

        EndLocalGrab();
        LogPcBottleState("PHOTON_PC_BOTTLE_RELEASED", worldPosition);
        ClearPcMouseState();
    }

    private bool CanUsePcBottleMouseInput()
    {
        return CanUsePcBottleMouseInput(out _);
    }

    private bool CanUsePcBottleMouseInput(out string reason)
    {
        reason = string.Empty;
        if (!allowMouseEditorGrab)
        {
            reason = "MouseGrabDisabled";
            return false;
        }

        if (!HasReadyLocalAvatarForPcBottleInput())
        {
            reason = "LocalAvatarNotSpawned";
            return false;
        }

        if (!isPhotonSharedNetworkBottle)
        {
            reason = "NotPhotonSharedBottle";
            return false;
        }

        if (objectKind != SharedNetworkObjectKind.Bottle)
        {
            reason = "ObjectKindNotBottle";
            return false;
        }

        if (Camera.main == null)
        {
            reason = "CameraMissing";
            return false;
        }

        if (!gameObject.activeInHierarchy)
        {
            reason = "ObjectInactive";
            return false;
        }

        reason = "Ready";
        return true;
    }

    private bool ShouldProcessPcBottleMouseInput()
    {
        return (isPhotonSharedNetworkBottle && objectKind == SharedNetworkObjectKind.Bottle)
            || pcMousePointerHeld;
    }

    private bool TryGetPcBottlePointerHit(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        Camera camera = Camera.main;
        if (camera == null)
        {
            LogPcBottlePointerMiss(Vector3.zero, Vector3.zero, "None");
            return false;
        }

        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, pcBottleRaycastMask, QueryTriggerInteraction.Collide);
        float closestDistance = float.PositiveInfinity;
        NetworkedSharedSceneObject closestSharedObject = null;
        Collider closestCollider = null;
        Vector3 closestPoint = Vector3.zero;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            NetworkedSharedSceneObject sharedObject = hitCollider.GetComponentInParent<NetworkedSharedSceneObject>();
            if (sharedObject == null || !sharedObject.IsPcBottleRaycastCandidate())
            {
                continue;
            }

            if (hits[i].distance < closestDistance)
            {
                closestDistance = hits[i].distance;
                closestSharedObject = sharedObject;
                closestCollider = hitCollider;
                closestPoint = hits[i].point;
            }
        }

        if (closestSharedObject != this)
        {
            LogPcBottlePointerMiss(ray.origin, ray.direction, camera.name);
            return false;
        }

        hitPoint = closestPoint;
        LogPcBottlePointerHit(closestCollider, closestPoint);
        return true;
    }

    private bool IsPcBottleRaycastCandidate()
    {
        return allowMouseEditorGrab
            && isPhotonSharedNetworkBottle
            && objectKind == SharedNetworkObjectKind.Bottle
            && gameObject.activeInHierarchy;
    }

    private bool TryUpdateMouseDragTargetOnPlane(out Vector3 worldPosition)
    {
        worldPosition = localDragTarget;
        Camera camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, pcMouseDragPlaneY, 0f));
        if (!dragPlane.Raycast(ray, out float distance))
        {
            return false;
        }

        worldPosition = ray.GetPoint(distance);
        localDragTarget = worldPosition;
#if !FUSION_WEAVER || !FUSION2
        transform.position = Vector3.Lerp(transform.position, localDragTarget, Time.deltaTime * mouseFollowSpeed);
#endif
        return true;
    }

    private void TryLogPcBottleDragStarted()
    {
        if (localDragActive && HasPcMouseDragOwnership())
        {
            TryLogPcBottleDragStarted(localDragTarget);
        }
    }

    private void TryLogPcBottleDragStarted(Vector3 worldPosition)
    {
        if (pcMouseDragStartedLogged || !pcMousePointerHeld)
        {
            return;
        }

        pcMouseDragStartedLogged = true;
        LogPcBottleGrabGranted(worldPosition);
        LogPcBottleState("PHOTON_PC_BOTTLE_DRAG_STARTED", worldPosition);
    }

    private bool HasPcMouseDragOwnership()
    {
#if FUSION_WEAVER && FUSION2
        return IsLocalLockOwner();
#else
        return localDragActive;
#endif
    }

    private void RejectPcMouseLocalGrab(string reason)
    {
#if FUSION_WEAVER && FUSION2
        RejectLocalGrab(reason);
#else
        localDragActive = false;
#endif
    }

    private void CheckPcBottleGrabPermissionTimeout()
    {
#if FUSION_WEAVER && FUSION2
        if (!pendingAuthorityRequest || pcMouseGrabPermissionRequestStartTime < 0f)
        {
            return;
        }

        if (Time.unscaledTime - pcMouseGrabPermissionRequestStartTime < Mathf.Max(0.1f, pcBottleGrabPermissionTimeoutSeconds))
        {
            return;
        }

        LogPcBottleGrabRejected("PermissionTimeout", localDragTarget);
        localDragActive = false;
        localGrabUsesMouseDrag = false;
        pendingAuthorityRequest = false;
        pendingMouseDrag = false;
        xrSelectGrabActive = false;
        ClearPcMouseState();
#endif
    }

    private void LogPcBottleDraggingIfNeeded(Vector3 worldPosition)
    {
        float now = Time.unscaledTime;
        if (now - lastPcMouseDraggingLogTime < 0.25f
            && Vector3.Distance(lastPcMouseDraggingLogWorldPosition, worldPosition) < 0.05f)
        {
            return;
        }

        lastPcMouseDraggingLogTime = now;
        lastPcMouseDraggingLogWorldPosition = worldPosition;
        LogPcBottleState("PHOTON_PC_BOTTLE_DRAGGING", worldPosition);
    }

    private void ClearPcMouseState()
    {
        pcMousePointerHeld = false;
        pcMouseDragStartedLogged = false;
        pcMouseGrabPermissionWaitLogged = false;
        pcMouseGrabGrantedLogged = false;
        pcMouseGrabPermissionRequestStartTime = -999f;
    }

    private void LogPcBottleInputStateIfNeeded(bool leftMouseDown, bool leftMouseHeld)
    {
        Camera camera = Camera.main;
        string signature = GetIsPcObserverForLog()
            + "|" + GetDeviceTypeForLog()
            + "|" + HasReadyLocalAvatarForPcBottleInput()
            + "|" + isPhotonSharedNetworkBottle
            + "|" + objectKind
            + "|" + (camera != null ? camera.name : "None")
            + "|" + Application.isFocused
            + "|" + leftMouseDown
            + "|" + leftMouseHeld;
        bool importantInput = leftMouseDown || leftMouseHeld || pcMousePointerHeld;
        if (pcInputStateLogged
            && string.Equals(signature, lastPcInputStateSignature, System.StringComparison.Ordinal)
            && (!importantInput || Time.unscaledTime - lastPcInputStateLogTime < 1f))
        {
            return;
        }

        pcInputStateLogged = true;
        lastPcInputStateSignature = signature;
        lastPcInputStateLogTime = Time.unscaledTime;
        Debug.Log("[NetworkedSharedSceneObject] PHOTON_PC_BOTTLE_INPUT_STATE"
            + " object=" + name
            + " isPcObserver=" + GetIsPcObserverForLog()
            + " deviceType=" + GetDeviceTypeForLog()
            + " networkReady=" + HasReadyLocalAvatarForPcBottleInput()
            + " isPhotonSharedNetworkBottle=" + isPhotonSharedNetworkBottle
            + " objectKind=" + objectKind
            + " cameraName=" + (camera != null ? camera.name : "None")
            + " gameViewFocused=" + Application.isFocused
            + " leftMouseDown=" + leftMouseDown
            + " leftMouseHeld=" + leftMouseHeld);
    }

    private void LogPcBottlePointerHit(Collider hitCollider, Vector3 hitPoint)
    {
        GameObject hitObject = hitCollider != null ? hitCollider.gameObject : null;
        Debug.Log("[NetworkedSharedSceneObject] PHOTON_PC_BOTTLE_POINTER_HIT"
            + " object=" + name
            + " hitObject=" + (hitObject != null ? hitObject.name : "None")
            + " hitLayer=" + (hitObject != null ? LayerMask.LayerToName(hitObject.layer) : "None")
            + " hasCollider=" + (hitCollider != null && hitCollider.enabled)
            + " isSharedBottle=" + isPhotonSharedNetworkBottle
            + " localPlayer=" + GetLocalPlayerForLog()
            + " lockOwner=" + GetLockOwnerForLog()
            + " stateAuthority=" + GetStateAuthorityForLog()
            + " mousePosition=" + FormatVector(Input.mousePosition)
            + " worldPosition=" + FormatVector(hitPoint));
    }

    private void LogPcBottlePointerMiss(Vector3 rayOrigin, Vector3 rayDirection, string cameraName)
    {
        Debug.Log("[NetworkedSharedSceneObject] PHOTON_PC_BOTTLE_POINTER_MISS"
            + " object=" + name
            + " rayOrigin=" + FormatVector(rayOrigin)
            + " rayDirection=" + FormatVector(rayDirection)
            + " cameraName=" + cameraName);
    }

    private void LogPcBottleGrabPermissionWait(Vector3 worldPosition)
    {
        if (pcMouseGrabPermissionRequestStartTime < 0f)
        {
            pcMouseGrabPermissionRequestStartTime = Time.unscaledTime;
        }

        if (pcMouseGrabPermissionWaitLogged)
        {
            return;
        }

        pcMouseGrabPermissionWaitLogged = true;
        LogPcBottleState("PHOTON_PC_BOTTLE_GRAB_PERMISSION_WAIT", worldPosition);
    }

    private void LogPcBottleGrabGranted(Vector3 worldPosition)
    {
        if (pcMouseGrabGrantedLogged || !pcMousePointerHeld)
        {
            return;
        }

        if (!HasPcMouseDragOwnership())
        {
            return;
        }

        pcMouseGrabGrantedLogged = true;
        LogPcBottleState("PHOTON_PC_BOTTLE_GRAB_GRANTED", worldPosition);
    }

    private void LogPcBottleGrabRejected(string reason, Vector3 worldPosition)
    {
        Debug.Log("[NetworkedSharedSceneObject] PHOTON_PC_BOTTLE_GRAB_REJECTED"
            + " reason=" + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason)
            + " object=" + name
            + " isGrabbed=" + GetIsGrabbedForLog()
            + " isLocalLockOwner=" + HasPcMouseDragOwnership()
            + " lockOwner=" + GetLockOwnerForLog()
            + " stateAuthority=" + GetStateAuthorityForLog()
            + " inputAuthority=" + GetInputAuthorityForLog()
            + " localPlayer=" + GetLocalPlayerForLog()
            + " mousePosition=" + FormatVector(Input.mousePosition)
            + " worldPosition=" + FormatVector(worldPosition));
    }

    private string GetPcBottleGrabRejectedReason()
    {
        if (!allowStateAuthorityGrab)
        {
            return "StateAuthorityGrabDisabled";
        }

        if (!HasReadyLocalAvatarForPcBottleInput())
        {
            return "LocalAvatarNotSpawned";
        }

        if (!isPhotonSharedNetworkBottle)
        {
            return "NotPhotonSharedBottle";
        }

        if (objectKind != SharedNetworkObjectKind.Bottle)
        {
            return "ObjectKindNotBottle";
        }

        if (Camera.main == null)
        {
            return "CameraMissing";
        }

        if (IsLockedByOther)
        {
            return "LockedByOther";
        }

#if FUSION_WEAVER && FUSION2
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            return "FusionObjectNotReady";
        }
#endif

        return "GrabUnavailable";
    }

    private void LogPcBottleState(string eventName, Vector3 worldPosition)
    {
        LogPcBottleState(eventName, worldPosition, string.Empty);
    }

    private void LogPcBottleState(string eventName, Vector3 worldPosition, string reason)
    {
        if (!isPhotonSharedNetworkBottle)
        {
            return;
        }

        string reasonText = string.IsNullOrWhiteSpace(reason) ? string.Empty : " reason=" + reason;
        Debug.Log("[NetworkedSharedSceneObject] " + eventName
            + reasonText
            + " object=" + name
            + " localPlayer=" + GetLocalPlayerForLog()
            + " lockOwner=" + GetLockOwnerForLog()
            + " stateAuthority=" + GetStateAuthorityForLog()
            + " inputAuthority=" + GetInputAuthorityForLog()
            + " mousePosition=" + FormatVector(Input.mousePosition)
            + " worldPosition=" + FormatVector(worldPosition));
    }

    private bool HasReadyLocalAvatarForPcBottleInput()
    {
        return NetworkUserAvatar.Local != null && NetworkUserAvatar.Local.IsNetworkStateReady;
    }

    private bool GetIsPcObserverForLog()
    {
        return NetworkUserAvatar.Local != null
            && NetworkUserAvatar.Local.IsNetworkStateReady
            && NetworkUserAvatar.Local.IsPcObserverAvatar;
    }

    private string GetDeviceTypeForLog()
    {
        return NetworkUserAvatar.Local != null
            ? NetworkUserAvatar.Local.DeviceType.ToString()
            : "None";
    }

    private bool GetIsGrabbedForLog()
    {
#if FUSION_WEAVER && FUSION2
        return IsGrabbed;
#else
        return localDragActive;
#endif
    }

    private string GetLocalPlayerForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Runner != null ? Runner.LocalPlayer.ToString() : "none";
#else
        return "none";
#endif
    }

    private string GetLockOwnerForLog()
    {
#if FUSION_WEAVER && FUSION2
        return LockOwner.ToString();
#else
        return "none";
#endif
    }

    private string GetStateAuthorityForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Object != null ? Object.StateAuthority.ToString() : "none";
#else
        return "none";
#endif
    }

    private string GetInputAuthorityForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Object != null ? Object.InputAuthority.ToString() : "none";
#else
        return "none";
#endif
    }
#endif

    private void Awake()
    {
        EnsureSharedBottleCollidersEnabled();
        RegisterGrabCallbacks();
    }

    private void OnEnable()
    {
        EnsureSharedBottleCollidersEnabled();
        RegisterGrabCallbacks();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        ClearPcMouseState();
#endif
        NotifyVisualNetworkDespawned();
        UnregisterXrGrabCallbacks();
        UnregisterObjectManipulatorCallbacks();
    }

    private void RegisterGrabCallbacks()
    {
        RegisterXrGrabCallbacks();
        RegisterObjectManipulatorCallbacks();
    }

    private void NotifyVisualNetworkSpawned()
    {
        PhotonSharedBottleVisualController visualController = GetComponent<PhotonSharedBottleVisualController>();
        if (visualController != null)
        {
            visualController.NotifyNetworkSpawned();
        }
    }

    private void NotifyVisualNetworkDespawned()
    {
        PhotonSharedBottleVisualController visualController = GetComponent<PhotonSharedBottleVisualController>();
        if (visualController != null)
        {
            visualController.NotifyNetworkDespawned();
        }
    }

    private void RegisterXrGrabCallbacks()
    {
        if (xrCallbacksRegistered)
        {
            return;
        }

        xrInteractable = GetComponent<XRBaseInteractable>();
        if (xrInteractable == null)
        {
            return;
        }

        xrInteractable.selectEntered.AddListener(OnXrSelectEntered);
        xrInteractable.selectExited.AddListener(OnXrSelectExited);
        xrCallbacksRegistered = true;
    }

    private void RegisterObjectManipulatorCallbacks()
    {
        if (objectManipulatorCallbacksRegistered)
        {
            return;
        }

        objectManipulator = GetComponent<ObjectManipulator>();
        if (objectManipulator == null || ReferenceEquals(objectManipulator, xrInteractable))
        {
            return;
        }

        objectManipulator.firstSelectEntered.AddListener(OnXrSelectEntered);
        objectManipulator.lastSelectExited.AddListener(OnXrSelectExited);
        objectManipulatorCallbacksRegistered = true;
    }

    private void UnregisterXrGrabCallbacks()
    {
        if (!xrCallbacksRegistered || xrInteractable == null)
        {
            return;
        }

        xrInteractable.selectEntered.RemoveListener(OnXrSelectEntered);
        xrInteractable.selectExited.RemoveListener(OnXrSelectExited);
        xrCallbacksRegistered = false;
    }

    private void UnregisterObjectManipulatorCallbacks()
    {
        if (!objectManipulatorCallbacksRegistered || objectManipulator == null)
        {
            return;
        }

        objectManipulator.firstSelectEntered.RemoveListener(OnXrSelectEntered);
        objectManipulator.lastSelectExited.RemoveListener(OnXrSelectExited);
        objectManipulatorCallbacksRegistered = false;
    }

    private void OnXrSelectEntered(SelectEnterEventArgs args)
    {
        xrSelectGrabActive = TryBeginLocalGrab(false);
    }

    private void OnXrSelectExited(SelectExitEventArgs args)
    {
        if (xrSelectGrabActive)
        {
            EndLocalGrab();
        }

        xrSelectGrabActive = false;
    }

    private bool ShouldLogPose(ref float lastLogTime, bool force)
    {
        if (objectKind != SharedNetworkObjectKind.Bottle)
        {
            return false;
        }

        float now = Time.unscaledTime;
        if (force || now - lastLogTime >= Mathf.Max(0.02f, poseLogIntervalSeconds))
        {
            lastLogTime = now;
            return true;
        }

        return false;
    }

    private bool EnsureSharedBottleCollidersEnabled()
    {
        if (!isPhotonSharedNetworkBottle || objectKind != SharedNetworkObjectKind.Bottle)
        {
            return false;
        }

        bool changed = false;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider targetCollider = colliders[i];
            if (targetCollider != null && !targetCollider.enabled)
            {
                targetCollider.enabled = true;
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsValidNetworkRotation(Quaternion rotation)
    {
        return Mathf.Abs(rotation.x) > 0.000001f
            || Mathf.Abs(rotation.y) > 0.000001f
            || Mathf.Abs(rotation.z) > 0.000001f
            || Mathf.Abs(rotation.w) > 0.000001f;
    }

    private static PhotonSharedBottleDetectionVisualState ClampDetectionVisualState(int state)
    {
        if (state < (int)PhotonSharedBottleDetectionVisualState.None
            || state > (int)PhotonSharedBottleDetectionVisualState.Lost)
        {
            return PhotonSharedBottleDetectionVisualState.None;
        }

        return (PhotonSharedBottleDetectionVisualState)state;
    }

    private static SharedBottleOrigin ClampBottleOrigin(int origin)
    {
        if (!System.Enum.IsDefined(typeof(SharedBottleOrigin), origin))
        {
            return SharedBottleOrigin.Manual;
        }

        return (SharedBottleOrigin)origin;
    }

    private static string FormatVector(Vector3 value)
    {
        return value.ToString("F3");
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return value.ToString("F3");
    }
}
