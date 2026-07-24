using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[DisallowMultipleComponent]
public class PhotonSharedBottleSpawner : MonoBehaviour
{
    [Header("Shared Bottle Spawn")]
    public bool enableSharedBottleSpawn = true;
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public DetectedBottlePoseSubscriber detectedBottleSubscriber;
    public GameObject networkBottlePrefab;
    public Transform spawnAnchor;
    public float spawnDistance = 0.65f;
    public float spawnVerticalOffset = -0.08f;
    public int maxSharedBottleCount = 8;

    [Header("Detected ROS Bottle Pose")]
    public bool stopRosPoseAfterFirstGrab = true;
    public float rosPoseUpdateRateHz = 10.0f;
    public float rosPosePositionThresholdM = 0.02f;
    public float rosPoseRotationThresholdDeg = 5.0f;

    [Header("Quest / Editor Spawn Controls")]
    public bool enableSpawnControls = true;
    public float controlsDistance = 0.95f;
    public float controlsVerticalOffset = -0.42f;
    public float controlsScale = 0.0012f;
    public bool allowKeyboardShortcutsInEditor = true;
    public KeyCode spawnKey = KeyCode.B;
    public KeyCode despawnKey = KeyCode.N;

    [Header("Runtime UI References")]
    public GameObject controlsRoot;
    public Button spawnButton;
    public Button despawnButton;
    public TMP_Text statusText;

    private readonly HashSet<string> observedBottleIds = new HashSet<string>();
    private readonly HashSet<string> localBottleLogIds = new HashSet<string>();
    private readonly HashSet<string> remoteBottleLogIds = new HashSet<string>();
    private int localSpawnRequestCount;
    private int remoteSpawnObservedCount;
    private string lastSpawnedBottleNetworkId = "None";
    private string lastSpawnError = "None";
    private readonly Dictionary<int, NetworkedSharedSceneObject> spawnedBottleByTrackId =
        new Dictionary<int, NetworkedSharedSceneObject>();
    private readonly Dictionary<int, bool> rosBottleWasGrabbedByTrackId =
        new Dictionary<int, bool>();
    private readonly Dictionary<int, Vector3> lastRosPoseAppliedPositionByTrackId =
        new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Quaternion> lastRosPoseAppliedRotationByTrackId =
        new Dictionary<int, Quaternion>();
    private readonly Dictionary<int, float> lastRosPoseAppliedTimeByTrackId =
        new Dictionary<int, float>();
    private readonly Dictionary<int, string> lastIgnoredRosPoseReasonByTrackId =
        new Dictionary<int, string>();

#if FUSION_WEAVER && FUSION2
    private NetworkObject pendingDespawnObject;
#endif

    public int SharedNetworkBottleCount => CountSharedNetworkBottles();
    public int LocalSpawnRequestCount => localSpawnRequestCount;
    public int RemoteSpawnObservedCount => remoteSpawnObservedCount;
    public string LastSpawnedBottleNetworkId => lastSpawnedBottleNetworkId;
    public string LastSpawnError => lastSpawnError;

    private void Awake()
    {
        ResolveReferences();
        EnsureControls();
        RefreshStatusText();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureControls();
        WireButtons();
    }

    private void OnDisable()
    {
        UnwireButtons();
    }

    private void Update()
    {
        ResolveReferences();
        ObserveSharedNetworkBottles();
        ProcessPendingDespawn();
        HandleKeyboardShortcuts();
        UpdateControlsPose();
        RefreshStatusText();
    }

    public void RequestSpawnInFrontOfHmd()
    {
        SpawnSharedBottle("PhotonSharedBottleSpawner");
    }

    public void SpawnSharedBottle(string source)
    {
        string resolvedSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
        if (!CanSpawnSharedBottle(out string reason))
        {
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_MODE local"
                + " source=" + resolvedSource
                + " reason=" + reason);
            FailSpawn(reason);
            return;
        }

        if (!TryResolveSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            localSpawnRequestCount++;
            FailSpawn("SpawnAnchorMissing");
            return;
        }

        SpawnSharedBottleAtPose(position, rotation, resolvedSource);
    }

    public NetworkedSharedSceneObject SpawnSharedBottleAtPose(Vector3 position, Quaternion rotation, string source)
    {
        string resolvedSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
        if (!CanSpawnSharedBottle(out string reason))
        {
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_MODE local"
                + " source=" + resolvedSource
                + " reason=" + reason);
            FailSpawn(reason);
            return null;
        }

        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_MODE shared source=" + resolvedSource);
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REQUEST source=" + resolvedSource);
        return RequestSpawnInternal(position, rotation, resolvedSource);
    }

    public bool TrySpawnOrUpdateDetectedBottle(Vector3 unityPosition, Quaternion unityRotation)
    {
        return TrySpawnOrUpdateDetectedBottle(0, unityPosition, unityRotation, false, false);
    }

    public bool SpawnOrRefreshFromLatestDetection()
    {
        return SyncBottlesFromLatestDetections();
    }

    public bool SpawnNewBottleFromLatestDetection()
    {
        return SyncBottlesFromLatestDetections();
    }

    public bool SyncBottlesFromLatestDetections()
    {
        Debug.Log("[PhotonSharedBottleSpawner] Sync detected bottles requested");
        ResolveReferences();
        if (!CanSpawnSharedBottle(out string reason))
        {
            Debug.LogWarning("[PhotonSharedBottleSpawner] Latest detection pose rejected reason=" + reason);
            return false;
        }

        Debug.Log("[PhotonSharedBottleSpawner] Role restriction disabled; shared participant may request bottle spawn");

        if (detectedBottleSubscriber == null)
        {
            Debug.LogWarning("[PhotonSharedBottleSpawner] Latest detection pose rejected"
                + " reason=MissingDetectedBottlePoseSubscriber");
            return false;
        }

        if (!detectedBottleSubscriber.TryGetLatestBottleWorldPoses(
            out IReadOnlyList<Vector3> detectedPositions,
            out string poseFailureReason))
        {
            Debug.LogWarning("[PhotonSharedBottleSpawner] Latest detection pose rejected"
                + " reason=" + poseFailureReason);
            return false;
        }

        int validCount = detectedPositions.Count;
        int targetCount = Mathf.Min(validCount, Mathf.Max(1, maxSharedBottleCount));
        if (validCount > targetCount)
        {
            Debug.LogWarning("[PhotonSharedBottleSpawner] Detection count truncated"
                + " valid=" + validCount
                + " max=" + Mathf.Max(1, maxSharedBottleCount));
        }

        if (targetCount == 0)
        {
            Debug.Log("[PhotonSharedBottleSpawner] Latest PoseArray contains no valid bottles");
        }

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            Debug.LogWarning("[PhotonSharedBottleSpawner] Detection synchronization rejected reason=RunnerNotRunning");
            return false;
        }

        List<NetworkedSharedSceneObject> rosDetectedBottles = CollectRosDetectedBottles();
        int currentCount = rosDetectedBottles.Count;
        int updateCount = Mathf.Min(currentCount, targetCount);
        int spawnCount = Mathf.Max(0, targetCount - currentCount);
        int despawnCount = Mathf.Max(0, currentCount - targetCount);
        Debug.Log("[PhotonSharedBottleSpawner] Detection synchronization"
            + " current=" + currentCount
            + " target=" + targetCount
            + " spawn=" + spawnCount
            + " update=" + updateCount
            + " despawn=" + despawnCount);

        for (int i = 0; i < updateCount; i++)
        {
            NetworkedSharedSceneObject bottle = rosDetectedBottles[i];
            if (bottle.IsGrabbedByAnyUser)
            {
                Debug.Log("[PhotonSharedBottleSpawner] Shared detected bottle update deferred"
                    + " networkId=" + FormatNetworkId(bottle.Object)
                    + " reason=Grabbed");
                continue;
            }

            if (!bottle.HasLocalStateAuthority)
            {
                bottle.Object.RequestStateAuthority();
                Debug.Log("[PhotonSharedBottleSpawner] Shared detected bottle update deferred"
                    + " networkId=" + FormatNetworkId(bottle.Object)
                    + " reason=StateAuthorityRequested");
                continue;
            }

            if (bottle.TryApplyAuthorityPose(
                detectedPositions[i],
                Quaternion.identity,
                "ManualDetectionSync",
                true))
            {
                Debug.Log("[PhotonSharedBottleSpawner] Shared detected bottle updated"
                    + " detectionIndex=" + i
                    + " networkId=" + FormatNetworkId(bottle.Object));
            }
        }

        for (int i = currentCount; i < targetCount; i++)
        {
            Debug.Log("[PhotonSharedBottleSpawner] Calling Runner.Spawn"
                + " detectionIndex=" + i
                + " position=" + FormatVector(detectedPositions[i])
                + " rotation=" + FormatQuaternion(Quaternion.identity));
            NetworkedSharedSceneObject spawned = RequestSpawnInternal(
                detectedPositions[i],
                Quaternion.identity,
                "PoseArrayDetection:" + i,
                -1,
                SharedBottleOrigin.RosDetected);
            if (spawned != null)
            {
                Debug.Log("[PhotonSharedBottleSpawner] Shared detected bottle spawned"
                    + " detectionIndex=" + i
                    + " networkId=" + FormatNetworkId(spawned.Object));
            }
        }

        for (int i = currentCount - 1; i >= targetCount; i--)
        {
            NetworkedSharedSceneObject bottle = rosDetectedBottles[i];
            string networkId = FormatNetworkId(bottle.Object);
            if (bottle.IsGrabbedByAnyUser)
            {
                Debug.Log("[PhotonSharedBottleSpawner] Despawn deferred"
                    + " networkId=" + networkId
                    + " reason=Grabbed");
                continue;
            }

            if (!bottle.HasLocalStateAuthority)
            {
                bottle.Object.RequestStateAuthority();
                Debug.Log("[PhotonSharedBottleSpawner] Despawn deferred"
                    + " networkId=" + networkId
                    + " reason=StateAuthorityRequested");
                continue;
            }

            DespawnNetworkBottle(runner, bottle.Object);
            Debug.Log("[PhotonSharedBottleSpawner] Shared detected bottle despawned"
                + " networkId=" + networkId);
        }

        return true;
#else
        return false;
#endif
    }

    public bool HasDetectedBottleTrack(int trackId)
    {
        return ResolveDetectedRosBottle(Mathf.Max(0, trackId)) != null;
    }

    public void LogAppliedBottleSnapshot(
        string source,
        int spawned,
        int updated,
        int ignoredGrabbed,
        int ignoredManual)
    {
        CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
            "[PhotonSharedBottleSpawner] Applied bottle snapshot:"
            + "\nsource=" + (string.IsNullOrWhiteSpace(source) ? "Unknown" : source)
            + "\nspawned=" + spawned
            + "\nupdated=" + updated
            + "\nignoredGrabbed=" + ignoredGrabbed
            + "\nignoredManual=" + ignoredManual);
    }

    public bool TrySpawnOrUpdateDetectedBottle(
        int trackId,
        Vector3 unityPosition,
        Quaternion unityRotation,
        bool isCurrentlyGrabbed,
        bool hasBeenGrabbed)
    {
        ResolveReferences();
        int resolvedTrackId = Mathf.Max(0, trackId);

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning || bootstrap == null || !IsJoinedStatus(bootstrap.LastJoinStatus))
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "PhotonDisconnected");
            return false;
        }

        NetworkedSharedSceneObject sharedBottle = ResolveDetectedRosBottle(resolvedTrackId);
        if (sharedBottle == null)
        {
            if (!CanSpawnSharedBottle(out string reason))
            {
                LogIgnoredRosBottlePose(resolvedTrackId, IsPhotonReadyFailure(reason) ? "PhotonDisconnected" : reason);
                return false;
            }

            if (SharedNetworkBottleCount >= Mathf.Max(1, maxSharedBottleCount))
            {
                LogIgnoredRosBottlePose(resolvedTrackId, "MaxSharedBottleCountReached");
                return false;
            }

            sharedBottle = RequestSpawnInternal(
                unityPosition,
                unityRotation,
                "RosPose:" + resolvedTrackId,
                resolvedTrackId,
                SharedBottleOrigin.RosDetected);
            if (sharedBottle == null)
            {
                LogIgnoredRosBottlePose(resolvedTrackId, string.IsNullOrWhiteSpace(lastSpawnError) ? "SpawnFailed" : lastSpawnError);
                return false;
            }

            spawnedBottleByTrackId[resolvedTrackId] = sharedBottle;
            rosBottleWasGrabbedByTrackId[resolvedTrackId] = hasBeenGrabbed || isCurrentlyGrabbed;
            MarkRosPoseApplied(resolvedTrackId, unityPosition, unityRotation);
            ClearIgnoredRosBottlePose(resolvedTrackId);
            CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
                "[PhotonSharedBottleSpawner] Spawned ROS detected bottle:"
                + "\ntrackId=" + resolvedTrackId
                + "\nposition=" + FormatVector(unityPosition)
                + "\nauthority=" + runner.LocalPlayer);
            return true;
        }

        if (sharedBottle.IsGrabbedByAnyUser)
        {
            rosBottleWasGrabbedByTrackId[resolvedTrackId] = true;
            LogIgnoredRosBottlePose(resolvedTrackId, "BottleGrabbed");
            return false;
        }

        bool wasGrabbed = hasBeenGrabbed
            || (rosBottleWasGrabbedByTrackId.TryGetValue(resolvedTrackId, out bool storedWasGrabbed) && storedWasGrabbed);
        if (stopRosPoseAfterFirstGrab && wasGrabbed)
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "AlreadyManuallyPlaced");
            return false;
        }

        if (!sharedBottle.HasLocalStateAuthority
            && !sharedBottle.TryRequestSharedStateAuthority("RosPose"))
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "NotAuthority");
            return false;
        }

        float minInterval = 1f / Mathf.Max(0.01f, rosPoseUpdateRateHz);
        float now = Time.realtimeSinceStartup;
        if (lastRosPoseAppliedTimeByTrackId.TryGetValue(resolvedTrackId, out float lastAppliedTime)
            && now - lastAppliedTime < minInterval)
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "BelowThreshold");
            return false;
        }

        bool hasLastPosition = lastRosPoseAppliedPositionByTrackId.TryGetValue(resolvedTrackId, out Vector3 lastPosition);
        bool hasLastRotation = lastRosPoseAppliedRotationByTrackId.TryGetValue(resolvedTrackId, out Quaternion lastRotation);
        bool changedEnough = !hasLastPosition
            || !hasLastRotation
            || Vector3.Distance(lastPosition, unityPosition) >= Mathf.Max(0.0001f, rosPosePositionThresholdM)
            || Quaternion.Angle(lastRotation, unityRotation) >= Mathf.Max(0.01f, rosPoseRotationThresholdDeg);
        if (!changedEnough)
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "BelowThreshold");
            return false;
        }

        if (!sharedBottle.TryApplyAuthorityPose(unityPosition, unityRotation, "RosPose", true))
        {
            LogIgnoredRosBottlePose(resolvedTrackId, sharedBottle.IsGrabbedByAnyUser ? "BottleGrabbed" : "NotAuthority");
            return false;
        }

        spawnedBottleByTrackId[resolvedTrackId] = sharedBottle;
        rosBottleWasGrabbedByTrackId[resolvedTrackId] = wasGrabbed;
        MarkRosPoseApplied(resolvedTrackId, unityPosition, unityRotation);
        ClearIgnoredRosBottlePose(resolvedTrackId);
        CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
            "[PhotonSharedBottleSpawner] Updated ROS detected bottle:"
            + "\ntrackId=" + resolvedTrackId
            + "\nreason=RosPose");
        return true;
#else
        LogIgnoredRosBottlePose(resolvedTrackId, "PhotonDisconnected");
        return false;
#endif
    }

    public bool TryDespawnDetectedBottleTrack(int trackId)
    {
        int resolvedTrackId = Mathf.Max(0, trackId);
#if FUSION_WEAVER && FUSION2
        NetworkedSharedSceneObject sharedBottle = ResolveDetectedRosBottle(resolvedTrackId);
        if (sharedBottle == null || sharedBottle.Object == null)
        {
            RemoveRosTrackState(resolvedTrackId);
            return true;
        }

        if (sharedBottle.IsGrabbedByAnyUser
            || (stopRosPoseAfterFirstGrab
                && rosBottleWasGrabbedByTrackId.TryGetValue(resolvedTrackId, out bool wasGrabbed)
                && wasGrabbed))
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "AlreadyManuallyPlaced");
            return false;
        }

        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "PhotonDisconnected");
            return false;
        }

        if (!sharedBottle.HasLocalStateAuthority)
        {
            LogIgnoredRosBottlePose(resolvedTrackId, "NotAuthority");
            return false;
        }

        DespawnNetworkBottle(runner, sharedBottle.Object);
        RemoveRosTrackState(resolvedTrackId);
        return true;
#else
        RemoveRosTrackState(resolvedTrackId);
        return true;
#endif
    }

    public bool CanSpawnSharedBottle(out string reason)
    {
        ResolveReferences();
        EnsureBootstrap(nameof(CanSpawnSharedBottle), false);

        if (!enableSharedBottleSpawn)
        {
            reason = "SpawnerDisabled";
            return false;
        }

        if (bootstrap == null)
        {
            reason = "MissingBootstrap";
            return false;
        }

        if (!bootstrap.SharedModeSelected)
        {
            reason = "SharedModeNotSelected";
            return false;
        }

        if (IsDisconnectOrLeaveStatus(bootstrap.LastJoinStatus))
        {
            reason = bootstrap.LastJoinStatus;
            return false;
        }

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        if (runner == null)
        {
            reason = "RunnerMissing";
            return false;
        }

        if (!runner.IsRunning)
        {
            reason = "RunnerNotRunning";
            return false;
        }

        if (!runner.IsInSession)
        {
            reason = "RunnerNotInSession";
            return false;
        }

        if (runner.GameMode != GameMode.Shared || runner.Topology != Topologies.Shared)
        {
            reason = "RunnerIsNotShared gameMode=" + runner.GameMode
                + " topology=" + runner.Topology;
            return false;
        }

        if (runner.LocalPlayer == PlayerRef.None)
        {
            reason = "LocalPlayerNotReady";
            return false;
        }

        if (!IsJoinedStatus(bootstrap.LastJoinStatus))
        {
            reason = "PhotonNotJoined";
            return false;
        }

        if (networkBottlePrefab == null)
        {
            reason = "MissingNetworkBottlePrefab";
            return false;
        }

        if (networkBottlePrefab.GetComponent<NetworkObject>() == null)
        {
            reason = "NetworkBottlePrefabMissingNetworkObject";
            return false;
        }

        if (detectedBottleSubscriber == null)
        {
            reason = "MissingDetectedBottlePoseSubscriber";
            return false;
        }

        reason = "Ready";
        return true;
#else
        reason = "FusionDisabled";
        return false;
#endif
    }

    public void RequestDespawnLastSharedBottle()
    {
#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            FailSpawn("RunnerNotRunningForDespawn");
            return;
        }

        NetworkedSharedSceneObject target = FindNewestSharedBottleWithNetworkObject();
        if (target == null || target.Object == null)
        {
            FailSpawn("NoSharedBottleToDespawn");
            return;
        }

        if (!target.HasStateAuthority)
        {
            pendingDespawnObject = target.Object;
            target.Object.RequestStateAuthority();
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_DESPAWN authorityRequested=True"
                + " networkId=" + FormatNetworkId(target.Object)
                + " player=" + runner.LocalPlayer);
            return;
        }

        DespawnNetworkBottle(runner, target.Object);
#else
        FailSpawn("FusionDisabledForDespawn");
#endif
    }

    public void RequestSpawn(Vector3 position, Quaternion rotation)
    {
        RequestSpawn(position, rotation, string.Empty);
    }

    public void RequestSpawn(Vector3 position, Quaternion rotation, string source)
    {
        RequestSpawnInternal(position, rotation, source);
    }

    private NetworkedSharedSceneObject RequestSpawnInternal(
        Vector3 position,
        Quaternion rotation,
        string source,
        int detectedTrackId = -1,
        SharedBottleOrigin bottleOrigin = SharedBottleOrigin.Manual)
    {
        localSpawnRequestCount++;
        lastSpawnError = "None";
        string sourceSuffix = string.IsNullOrWhiteSpace(source) ? string.Empty : " source=" + source;

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        string playerText = runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None";
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REQUEST"
            + sourceSuffix
            + " player=" + playerText
            + " position=" + FormatVector(position)
            + " rotation=" + FormatQuaternion(rotation));

        if (!enableSharedBottleSpawn)
        {
            FailSpawn("SpawnerDisabled");
            return null;
        }

        if (runner == null || !runner.IsRunning)
        {
            FailSpawn("RunnerNotRunning");
            return null;
        }

        if (!IsValidSharedRunner(runner))
        {
            LogRunnerInventory(runner);
            FailSpawn("RunnerIsNotActiveShared");
            return null;
        }

        if (networkBottlePrefab == null)
        {
            FailSpawn("MissingNetworkBottlePrefab");
            return null;
        }

        if (networkBottlePrefab.GetComponent<NetworkObject>() == null)
        {
            FailSpawn("NetworkBottlePrefabMissingNetworkObject");
            return null;
        }

        if (SharedNetworkBottleCount >= Mathf.Max(1, maxSharedBottleCount))
        {
            FailSpawn("MaxSharedBottleCountReached count=" + SharedNetworkBottleCount);
            return null;
        }

        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_AUTHORITY"
            + " player=" + runner.LocalPlayer
            + " mode=Shared"
            + " prefab=" + networkBottlePrefab.name);

        try
        {
            LogRunnerInventory(runner);
            Debug.Log("[PhotonSharedBottleSpawner] Calling Runner.Spawn"
                + " position=" + FormatVector(position)
                + " rotation=" + FormatQuaternion(rotation));
            NetworkObject spawned = runner.Spawn(
                networkBottlePrefab,
                position,
                rotation,
                runner.LocalPlayer,
                (spawnRunner, obj) =>
                {
                    ConfigureSpawnedBottle(
                        obj,
                        spawnRunner.LocalPlayer,
                        (float)spawnRunner.SimulationTime,
                        detectedTrackId,
                        bottleOrigin);
                });

            if (spawned == null)
            {
                FailSpawn("RunnerSpawnReturnedNull");
                return null;
            }

            RegisterObservedBottle(spawned, true);
            Debug.Log("[PhotonSharedBottleSpawner] Shared bottle spawned"
                + " networkId=" + FormatNetworkId(spawned)
                + " instanceId=" + spawned.GetInstanceID());
            return spawned.GetComponent<NetworkedSharedSceneObject>();
        }
        catch (Exception ex)
        {
            Debug.LogError("[PhotonSharedBottleSpawner] Runner.Spawn failed"
                + " exception=" + ex.GetType().Name
                + ": " + ex.Message
                + "\n" + ex.StackTrace);
            FailSpawn(ex.GetType().Name + ": " + ex.Message);
            return null;
        }
#else
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REQUEST player=FusionDisabled"
            + " position=" + FormatVector(position)
            + " rotation=" + FormatQuaternion(rotation));
        FailSpawn("FusionDisabled");
        return null;
#endif
    }

    public NetworkedSharedSceneObject FindLatestSharedBottle()
    {
        return FindNewestSharedBottleWithNetworkObject();
    }

#if FUSION_WEAVER && FUSION2
    private List<NetworkedSharedSceneObject> CollectRosDetectedBottles()
    {
        NetworkedSharedSceneObject[] sharedObjects =
            FindObjectsOfType<NetworkedSharedSceneObject>(true);
        List<NetworkedSharedSceneObject> result = new List<NetworkedSharedSceneObject>();
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject == null
                || !sharedObject.isActiveAndEnabled
                || !sharedObject.IsPhotonSharedNetworkBottle
                || sharedObject.SharedOrigin != SharedBottleOrigin.RosDetected
                || sharedObject.Object == null
                || !sharedObject.Object.Id.IsValid)
            {
                continue;
            }

            result.Add(sharedObject);
        }

        result.Sort((a, b) => string.CompareOrdinal(
            FormatNetworkId(a.Object),
            FormatNetworkId(b.Object)));
        return result;
    }
#endif

    private NetworkedSharedSceneObject ResolveDetectedRosBottle(int trackId)
    {
        if (spawnedBottleByTrackId.TryGetValue(trackId, out NetworkedSharedSceneObject cachedBottle)
            && cachedBottle != null
            && cachedBottle.isActiveAndEnabled
            && cachedBottle.IsPhotonSharedNetworkBottle
            && cachedBottle.SharedOrigin == SharedBottleOrigin.RosDetected)
        {
            return cachedBottle;
        }

        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject == null
                || !sharedObject.IsPhotonSharedNetworkBottle
                || sharedObject.SharedOrigin != SharedBottleOrigin.RosDetected
                || sharedObject.SharedDetectedBottleTrackId != trackId)
            {
                continue;
            }

            spawnedBottleByTrackId[trackId] = sharedObject;
            return sharedObject;
        }

        spawnedBottleByTrackId.Remove(trackId);
        return null;
    }

    private void MarkRosPoseApplied(int trackId, Vector3 position, Quaternion rotation)
    {
        lastRosPoseAppliedPositionByTrackId[trackId] = position;
        lastRosPoseAppliedRotationByTrackId[trackId] = rotation;
        lastRosPoseAppliedTimeByTrackId[trackId] = Time.realtimeSinceStartup;
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.BottleSync);
    }

    private void LogIgnoredRosBottlePose(int trackId, string reason)
    {
        string resolvedReason = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;
        if (lastIgnoredRosPoseReasonByTrackId.TryGetValue(trackId, out string lastReason)
            && string.Equals(lastReason, resolvedReason, StringComparison.Ordinal))
        {
            return;
        }

        lastIgnoredRosPoseReasonByTrackId[trackId] = resolvedReason;
        Debug.Log("[PhotonSharedBottleSpawner] Ignored ROS pose:"
            + "\ntrackId=" + trackId
            + "\nreason=" + resolvedReason);
    }

    private void ClearIgnoredRosBottlePose(int trackId)
    {
        lastIgnoredRosPoseReasonByTrackId.Remove(trackId);
    }

    private void RemoveRosTrackState(int trackId)
    {
        spawnedBottleByTrackId.Remove(trackId);
        rosBottleWasGrabbedByTrackId.Remove(trackId);
        lastRosPoseAppliedPositionByTrackId.Remove(trackId);
        lastRosPoseAppliedRotationByTrackId.Remove(trackId);
        lastRosPoseAppliedTimeByTrackId.Remove(trackId);
        lastIgnoredRosPoseReasonByTrackId.Remove(trackId);
    }

    private static bool IsPhotonReadyFailure(string reason)
    {
        return string.Equals(reason, "MissingBootstrap", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "RunnerMissing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "RunnerNotRunning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "PhotonNotJoined", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "FusionDisabled", StringComparison.OrdinalIgnoreCase);
    }

    private void ResolveReferences()
    {
        if (detectedBottleSubscriber == null)
        {
            detectedBottleSubscriber = FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        }

        if (bootstrap == null)
        {
            EnsureBootstrap(nameof(ResolveReferences), false);
        }

        EnsureBootstrap(nameof(ResolveReferences), false);
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private static bool IsJoinedStatus(string status)
    {
        return string.Equals(status, "Joined", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "AlreadyRunning", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisconnectOrLeaveStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.IndexOf("Leaving", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("Leave", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("Shutdown", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("Disconnect", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TryResolveSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        Transform anchor = spawnAnchor;
        if (anchor == null && Camera.main != null)
        {
            anchor = Camera.main.transform;
        }

        if (anchor == null && bootstrap != null)
        {
            anchor = bootstrap.headSource;
        }

        if (anchor == null)
        {
            position = transform.position;
            rotation = transform.rotation;
            return false;
        }

        Vector3 forward = anchor.forward.sqrMagnitude > 0.0001f ? anchor.forward.normalized : Vector3.forward;
        position = anchor.position
            + forward * Mathf.Max(0.05f, spawnDistance)
            + Vector3.up * spawnVerticalOffset;
        rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
        return true;
    }

    private void HandleKeyboardShortcuts()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (!allowKeyboardShortcutsInEditor)
        {
            return;
        }

        if (Input.GetKeyDown(spawnKey))
        {
            RequestSpawnInFrontOfHmd();
        }
        else if (Input.GetKeyDown(despawnKey))
        {
            RequestDespawnLastSharedBottle();
        }
#endif
    }

    private void EnsureControls()
    {
        if (!enableSpawnControls)
        {
            if (controlsRoot != null)
            {
                controlsRoot.SetActive(false);
            }

            return;
        }

        if (controlsRoot == null)
        {
            controlsRoot = CreateControlsUi();
        }

        controlsRoot.SetActive(true);
        EnsureEventSystem();
        WireButtons();
    }

    private GameObject CreateControlsUi()
    {
        GameObject canvasObject = new GameObject(
            "SharedBottleSpawnerCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 92;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(420f, 190f);
        canvasRect.localScale = Vector3.one * Mathf.Max(0.0001f, controlsScale);

        GameObject panel = CreateUiObject("SharedBottleSpawnerPanel", canvasObject.transform, typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.055f, 0.92f);

        spawnButton = CreateButton(panel.transform, "SpawnSharedBottleButton", "Spawn Shared Bottle", new Vector2(0f, 42f), new Vector2(330f, 52f));
        despawnButton = CreateButton(panel.transform, "DespawnSharedBottleButton", "Despawn Last", new Vector2(0f, -22f), new Vector2(330f, 44f));
        statusText = CreateLabel(panel.transform, "SharedBottleSpawnerStatus", "SharedBottle: 0", 16, new Vector2(0f, -76f), new Vector2(370f, 30f));

        return canvasObject;
    }

    private void WireButtons()
    {
        if (spawnButton != null)
        {
            spawnButton.onClick.RemoveListener(RequestSpawnInFrontOfHmd);
            spawnButton.onClick.AddListener(RequestSpawnInFrontOfHmd);
        }

        if (despawnButton != null)
        {
            despawnButton.onClick.RemoveListener(RequestDespawnLastSharedBottle);
            despawnButton.onClick.AddListener(RequestDespawnLastSharedBottle);
        }
    }

    private void UnwireButtons()
    {
        if (spawnButton != null)
        {
            spawnButton.onClick.RemoveListener(RequestSpawnInFrontOfHmd);
        }

        if (despawnButton != null)
        {
            despawnButton.onClick.RemoveListener(RequestDespawnLastSharedBottle);
        }
    }

    private void UpdateControlsPose()
    {
        if (!enableSpawnControls || controlsRoot == null || !controlsRoot.activeSelf)
        {
            return;
        }

        Transform target = spawnAnchor != null ? spawnAnchor : (Camera.main != null ? Camera.main.transform : null);
        if (target == null)
        {
            return;
        }

        Vector3 forward = target.forward.sqrMagnitude > 0.0001f ? target.forward.normalized : Vector3.forward;
        Transform controlsTransform = controlsRoot.transform;
        controlsTransform.position = target.position
            + forward * Mathf.Max(0.05f, controlsDistance)
            + Vector3.up * controlsVerticalOffset;
        controlsTransform.localScale = Vector3.one * Mathf.Max(0.0001f, controlsScale);

        Vector3 facing = controlsTransform.position - target.position;
        if (facing.sqrMagnitude < 0.0001f)
        {
            facing = forward;
        }

        controlsTransform.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }

    private void RefreshStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = "SharedNetworkBottleCount: " + SharedNetworkBottleCount
            + "  Last: " + lastSpawnedBottleNetworkId;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent, params Type[] componentTypes)
    {
        GameObject obj = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        obj.transform.SetParent(parent, false);
        for (int i = 0; i < componentTypes.Length; i++)
        {
            if (componentTypes[i] != null && obj.GetComponent(componentTypes[i]) == null)
            {
                obj.AddComponent(componentTypes[i]);
            }
        }

        return obj;
    }

    private static Button CreateButton(Transform parent, string objectName, string text, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent, typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, position, size);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.06f, 0.38f, 0.34f, 0.98f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.08f, 0.52f, 0.46f, 1f);
        colors.pressedColor = new Color(0.02f, 0.26f, 0.24f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        CreateLabel(buttonObject.transform, "Label", text, 20, Vector2.zero, size - new Vector2(12f, 8f));
        return button;
    }

    private static TMP_Text CreateLabel(Transform parent, string objectName, string text, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject labelObject = CreateUiObject(objectName, parent, typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, position, size);

        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        return label;
    }

    private static void ConfigureCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>(true);
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("PhotonSharedMRSpawnEventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }
    }

    private int CountSharedNetworkBottles()
    {
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        int count = 0;
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject != null && sharedObject.IsPhotonSharedNetworkBottle)
            {
                count++;
            }
        }

        return count;
    }

    private NetworkedSharedSceneObject FindNewestSharedBottleWithNetworkObject()
    {
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        NetworkedSharedSceneObject newest = null;
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject == null || !sharedObject.IsPhotonSharedNetworkBottle)
            {
                continue;
            }

#if FUSION_WEAVER && FUSION2
            if (sharedObject.Object == null || !sharedObject.Object.Id.IsValid)
            {
                continue;
            }

            newest = sharedObject;
#endif
        }

        return newest;
    }

    private void ObserveSharedNetworkBottles()
    {
#if FUSION_WEAVER && FUSION2
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject == null || !sharedObject.IsPhotonSharedNetworkBottle || sharedObject.Object == null)
            {
                continue;
            }

            RegisterObservedBottle(sharedObject.Object, false);
        }
#endif
    }

    private void ProcessPendingDespawn()
    {
#if FUSION_WEAVER && FUSION2
        if (pendingDespawnObject == null)
        {
            return;
        }

        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            pendingDespawnObject = null;
            return;
        }

        if (!pendingDespawnObject.HasStateAuthority)
        {
            return;
        }

        DespawnNetworkBottle(runner, pendingDespawnObject);
        pendingDespawnObject = null;
#endif
    }

#if FUSION_WEAVER && FUSION2
    private static bool IsValidSharedRunner(NetworkRunner targetRunner)
    {
        return targetRunner != null
            && targetRunner.IsRunning
            && targetRunner.IsInSession
            && targetRunner.GameMode == GameMode.Shared
            && targetRunner.Topology == Topologies.Shared
            && targetRunner.LocalPlayer != PlayerRef.None;
    }

    private static void LogRunnerInventory(NetworkRunner selectedRunner)
    {
        Debug.Log("[BottleSpawnCheck]"
            + " runner=" + selectedRunner.name
            + " state=" + selectedRunner.State
            + " running=" + selectedRunner.IsRunning
            + " inSession=" + selectedRunner.IsInSession
            + " topology=" + selectedRunner.Topology
            + " mode=" + selectedRunner.GameMode
            + " localPlayer=" + selectedRunner.LocalPlayer
            + " isServer=" + selectedRunner.IsServer
            + " isClient=" + selectedRunner.IsClient
            + " isSharedMaster=" + selectedRunner.IsSharedModeMasterClient
            + " runnerCount=" + NetworkRunner.Instances.Count);

        foreach (NetworkRunner candidateRunner in NetworkRunner.Instances)
        {
            if (candidateRunner == null) continue;
            Debug.Log("[RunnerInventory]"
                + " selected=" + (candidateRunner == selectedRunner)
                + " name=" + candidateRunner.name
                + " state=" + candidateRunner.State
                + " running=" + candidateRunner.IsRunning
                + " inSession=" + candidateRunner.IsInSession
                + " topology=" + candidateRunner.Topology
                + " mode=" + candidateRunner.GameMode
                + " localPlayer=" + candidateRunner.LocalPlayer);
        }
    }

    private NetworkRunner ResolveRunner()
    {
        if (EnsureBootstrap(nameof(ResolveRunner), false) == null)
        {
            return null;
        }

        return bootstrap.Runner;
    }

    private static void ConfigureSpawnedBottle(
        NetworkObject obj,
        PlayerRef spawnedBy,
        float spawnedAtRunnerTime,
        int detectedTrackId,
        SharedBottleOrigin bottleOrigin)
    {
        NetworkedSharedSceneObject sharedObject = obj != null ? obj.GetComponent<NetworkedSharedSceneObject>() : null;
        if (sharedObject == null)
        {
            return;
        }

        sharedObject.objectKind = SharedNetworkObjectKind.Bottle;
        sharedObject.allowStateAuthorityGrab = true;
        sharedObject.allowMouseEditorGrab = true;
        sharedObject.syncPose = true;
        sharedObject.isPhotonSharedNetworkBottle = true;
        sharedObject.SetSharedSpawnMetadata(spawnedBy, spawnedAtRunnerTime);
        sharedObject.SetDetectedBottleMetadata(detectedTrackId, bottleOrigin);
    }

    private void RegisterObservedBottle(NetworkObject networkObject, bool fromLocalSpawnCall)
    {
        if (networkObject == null || !networkObject.Id.IsValid)
        {
            return;
        }

        NetworkedSharedSceneObject sharedObject = networkObject.GetComponent<NetworkedSharedSceneObject>();
        if (sharedObject == null || !sharedObject.IsPhotonSharedNetworkBottle)
        {
            return;
        }

        NetworkRunner runner = ResolveRunner();
        string networkId = FormatNetworkId(networkObject);
        observedBottleIds.Add(networkId);
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.BottleSync);
        lastSpawnedBottleNetworkId = networkId;
        if (sharedObject.SharedOrigin == SharedBottleOrigin.RosDetected
            && sharedObject.SharedDetectedBottleTrackId >= 0)
        {
            spawnedBottleByTrackId[sharedObject.SharedDetectedBottleTrackId] = sharedObject;
        }

        bool localAuthority = runner != null
            && runner.IsRunning
            && (networkObject.StateAuthority == runner.LocalPlayer || networkObject.InputAuthority == runner.LocalPlayer);

        if ((fromLocalSpawnCall || localAuthority) && localBottleLogIds.Add(networkId))
        {
            CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
                "[PhotonSharedBottleSpawner] BOTTLE_SPAWN_LOCAL"
                + " networkId=" + networkId
                + " player=" + (runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None")
                + " spawnedBy=" + sharedObject.DebugSpawnedByPlayer
                + " spawnedAt=" + sharedObject.DebugSpawnedAtRunnerTime);
        }
        else if (!localAuthority && remoteBottleLogIds.Add(networkId))
        {
            remoteSpawnObservedCount++;
            CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
                "[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REMOTE"
                + " networkId=" + networkId
                + " player=" + (runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None")
                + " stateAuthority=" + networkObject.StateAuthority
                + " spawnedBy=" + sharedObject.DebugSpawnedByPlayer
                + " spawnedAt=" + sharedObject.DebugSpawnedAtRunnerTime);
        }
    }

    private void DespawnNetworkBottle(NetworkRunner runner, NetworkObject target)
    {
        string networkId = FormatNetworkId(target);
        NetworkedSharedSceneObject sharedObject = target != null ? target.GetComponent<NetworkedSharedSceneObject>() : null;
        if (sharedObject != null
            && sharedObject.SharedOrigin == SharedBottleOrigin.RosDetected
            && sharedObject.SharedDetectedBottleTrackId >= 0)
        {
            RemoveRosTrackState(sharedObject.SharedDetectedBottleTrackId);
        }

        runner.Despawn(target);
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.BottleSync);
        observedBottleIds.Remove(networkId);
        localBottleLogIds.Remove(networkId);
        remoteBottleLogIds.Remove(networkId);
        CommunicationHealthMonitor.Verbose(CommunicationChannel.BottleSync,
            "[PhotonSharedBottleSpawner] BOTTLE_DESPAWN"
            + " networkId=" + networkId
            + " player=" + runner.LocalPlayer);
    }

    private static string FormatNetworkId(NetworkObject networkObject)
    {
        return networkObject != null && networkObject.Id.IsValid
            ? networkObject.Id.ToString()
            : "Invalid";
    }
#endif

    private void FailSpawn(string reason)
    {
        lastSpawnError = reason;
        Debug.LogWarning("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_FAILED reason=" + reason);
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
