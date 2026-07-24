using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MixedReality.Toolkit;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class DetectedBottleTrack
{
    public int TrackId;
    public Vector3 UnityPosition;
    public Quaternion UnityRotation = Quaternion.identity;
    public float LastSeenUnscaledTime;
    public bool HasBeenGrabbed;
    public bool IsCurrentlyGrabbed;
#if FUSION_WEAVER && FUSION2
    public Fusion.NetworkObject PhotonBottleObject;
#else
    public object PhotonBottleObject;
#endif
    public NetworkedSharedSceneObject SharedSceneObject;
    public bool TimedOutLogged;
    public bool UpdateLogged;
    public Vector3 LastLoggedPosition;
}

public class DetectedBottlePoseSubscriber : RosTcpSubscriber<PoseStampedMsg>
{
    private const string DefaultTopic = "/detected_bottle_pose";
    private const string DefaultPoseArrayTopic = "/detected_bottle_poses";
    private const string SpawnBottleButtonName = "Scan Bottles";
    private const string RuntimeObjectName = "DetectedBottlePoseRuntime";
    private const string ManualBottleName = "Manual Bottle";
    private const string OpticalFrameCoordinateMode = "AMIR: Unity=(-amir.y, amir.z, amir.x)";
    private const float ManualMenuDistance = 0.8f;
    private const float ManualMenuDown = 0.12f;
    private const float ManualMenuScale = 0.001f;

    public GameObject bottlePrefab;
    public Transform parent;
    public float positionScale = 1.0f;
    [SerializeField]
    private Vector3 bottleDisplayOffsetWorld = new Vector3(0f, -0.10f, 0.15f);
    [Range(0f, 1f)] public float smoothing = 0.2f;
    [HideInInspector]
    public float manualSpawnScale = 0.15f;
    public float createdBottleScale = 0.8f;
    public float detectedBottleScale = 0.15f;
    public bool keepBottleVisible = true;
    public bool usePoseArray = true;
    public int maxDisplayedBottles = 5;
    public int spawnDetectionCount = 2;
    public Transform palmPoseTransform;
    [Tooltip("Deprecated: RealSense Poseからのボトル生成には使用しません。")]
    public Transform hmdTransform;
    [Tooltip("Deprecated: RealSense Poseからのボトル生成には使用しません。")]
    public Transform amirMrAnchor;
    [SerializeField, Min(0.01f)]
    private float latestPoseMaxAgeSec = 0.75f;
    public float palmMaxDistance = 0.35f;
    public float palmWeight = 0.7f;
    public float gazeWeight = 0.3f;
    public float selectionScoreThreshold = 0.45f;
    public bool addToButtonCollectionMenu = false;
    public bool createRuntimeButton = false;
    public bool showDebugStatusText = true;
    [Tooltip("Deprecated: RealSense Poseからのボトル生成位置には影響しません。")]
    public bool forceSpawnInFrontOfHmd = false;
    public bool usePhotonSharedBottleWhenConnected = true;
    public PhotonSharedBottleSpawner photonBottleSpawner;
    public bool disableLocalBottleWhenPhotonConnected = true;
    [SerializeField, Min(0.01f)]
    private float trackAssociationDistanceM = 0.10f;
    [SerializeField, Min(0.1f)]
    private float trackLostTimeoutSec = 2.0f;
    [SerializeField, Min(0.01f)]
    private float rosPosePositionThresholdM = 0.02f;
    [SerializeField, Min(0.1f)]
    private float rosPoseUpdateRateHz = 10.0f;
    [SerializeField]
    private bool destroyUnclaimedTrackAfterTimeout = false;
    [SerializeField]
    private bool scanOnceOnStartup = true;
    [SerializeField, Min(0.1f)]
    private float startupScanTimeoutSec = 2.0f;
    [SerializeField, Min(0.1f)]
    private float manualScanWindowSec = 1.0f;
    [SerializeField]
    private bool removeUnseenUngrabbedRosBottlesOnScan = false;
    public float forceSpawnDistance = 0.5f;
    public float debugStatusDistance = 0.55f;
    public float debugStatusVerticalOffset = -0.08f;
    public float debugStatusFontSize = 0.1f;
    public float debugStatusWidth = 1.8f;
    public float debugStatusHeight = 1.0f;
    public string PoseArrayTopic = DefaultPoseArrayTopic;
    public Transform buttonCollectionRoot;
    public Canvas spawnButtonCanvas;

    public UnityEngine.Pose LatestDetectedBottlePose { get; private set; } =
        new UnityEngine.Pose(Vector3.zero, Quaternion.identity);
    public float LatestDetectedBottleTimestamp { get; private set; } = -1f;
    public bool HasValidDetectedBottle { get; private set; }
    public bool HasLatestValidBottlePose => HasValidDetectedBottle;
    public Vector3 LatestBottleUnityPosition => LatestDetectedBottlePose.position;
    public Quaternion LatestBottleUnityRotation => LatestDetectedBottlePose.rotation;
    public float LatestBottlePoseTime => LatestDetectedBottleTimestamp;
    public float LatestDetectionConfidence { get; private set; }
    public string LatestDetectionSourceFrame { get; private set; } = "none";
    public string LatestDetectionCoordinateMode => OpticalFrameCoordinateMode;
    public bool IsBottleScanInProgress { get; private set; }
    public bool HasCompletedInitialBottleScan { get; private set; }
    public string BottleScanStatus { get; private set; } = "Idle";
    public int DetectedBottleTrackCount => bottleTracks.Count;

    private GameObject bottleInstance;
    private GameObject manualBottleInstance;
    private readonly List<GameObject> bottleInstances = new List<GameObject>();
    private readonly List<GameObject> createdBottleInstances = new List<GameObject>();
    private readonly List<Vector3> latestDetectedUnityPositions = new List<Vector3>();
    private readonly List<DetectedBottleTrack> bottleTracks = new List<DetectedBottleTrack>();
    private readonly HashSet<int> currentFrameAssignedTrackIds = new HashSet<int>();
    private readonly Dictionary<int, float> lastPhotonTrackUpdateTime = new Dictionary<int, float>();
    private readonly Dictionary<int, Vector3> lastPhotonTrackUpdatePosition = new Dictionary<int, Vector3>();
    private readonly Dictionary<int, Quaternion> lastPhotonTrackUpdateRotation = new Dictionary<int, Quaternion>();
    private readonly List<UnityEngine.Pose> latestBottleSnapshot = new List<UnityEngine.Pose>();
    private readonly List<UnityEngine.Pose> activeScanSnapshot = new List<UnityEngine.Pose>();
    private readonly List<Vector3> bottleTargetPositions = new List<Vector3>();
    private readonly List<bool> bottleHasTargetPositions = new List<bool>();
    private Vector3 targetUnityPosition;
    private int nextBottleTrackId;
    private bool loggedMissingPrefab;
    private Button spawnBottleButton;
    private GameObject buttonCollectionSpawnButtonObject;
    private Canvas runtimeSpawnCanvas;
    private ROSConnection poseArrayRos;
    private string subscribedPoseArrayTopic;
    private GameObject candidateBottle;
    private GameObject candidateLabel;
    private float nextCandidateDebugLogTime;
    private TextMeshPro debugStatusText;
    private GameObject debugStatusObject;
    private float nextDebugStatusUpdateTime;
    private int poseArrayReceiveCount;
    private int latestDetectedCount;
    private int spawnBottleCallCount;
    private int spawnBottleManualCallCount;
    private int spawnBottleFromDetectionCallCount;
    private int hmdFrontSpawnCallCount;
    private int createdBottleCreatedCount;
    private int detectedBottleCreatedCount;
    private string latestPoseArrayFrameId = "none";
    private string latestCreatedBottleState = "none";
    private bool localDetectedBottleHiddenForPhoton;
    private bool latestRosBottlePoseCacheLogged;
    private Vector3 lastLoggedLatestRosBottlePosition;
    private Quaternion lastLoggedLatestRosBottleRotation = Quaternion.identity;
    private Coroutine bottleScanCoroutine;
    private string activeScanSource = "None";
    private bool hasLatestBottleSnapshot;
    private float latestBottleSnapshotTime = -1f;
    private string latestBottleSnapshotFrameId = "none";
    private bool initialPhotonCommitCompleted;
    private bool convertedWithoutDisplayOffsetLogged;
    private Vector3 latestRosPosition;
    private bool hasLatestPose;
    private double latestPoseReceivedTime = -1d;
    private string latestRosPoseFrameId = "none";
    private readonly List<Vector3> latestRosBottlePositions = new List<Vector3>();
    private double latestPoseArrayReceivedTime = -1d;
    private bool hasLatestPoseArray;
    private string latestPoseArrayGenerationFrameId = "none";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeSubscriberExists()
    {
        DetectedBottlePoseSubscriber existing = FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        if (existing != null)
        {
            return;
        }

        GameObject host = null;
        bottle legacyBottleSpawner = FindObjectOfType<bottle>(true);
        if (legacyBottleSpawner != null)
        {
            host = legacyBottleSpawner.gameObject;
        }

        if (host == null)
        {
            host = new GameObject(RuntimeObjectName);
        }

        DetectedBottlePoseSubscriber subscriber = host.AddComponent<DetectedBottlePoseSubscriber>();
        if (legacyBottleSpawner != null)
        {
            subscriber.bottlePrefab = legacyBottleSpawner.bottlePrefab;
            subscriber.parent = legacyBottleSpawner.parentObject != null
                ? legacyBottleSpawner.parentObject.transform
                : null;
        }
    }

    private void Reset()
    {
        EnsureDefaultTopics();
    }

    private void Awake()
    {
        EnsureDefaultTopics();
    }

    private void OnValidate()
    {
        EnsureDefaultTopics();
        positionScale = Mathf.Max(0f, positionScale);
        smoothing = Mathf.Clamp01(smoothing);
        createdBottleScale = Mathf.Max(0.001f, createdBottleScale);
        manualSpawnScale = createdBottleScale;
        detectedBottleScale = Mathf.Max(0.001f, detectedBottleScale);
        maxDisplayedBottles = Mathf.Max(1, maxDisplayedBottles);
        spawnDetectionCount = Mathf.Max(1, spawnDetectionCount);
        palmMaxDistance = Mathf.Max(0.001f, palmMaxDistance);
        palmWeight = Mathf.Max(0f, palmWeight);
        gazeWeight = Mathf.Max(0f, gazeWeight);
        selectionScoreThreshold = Mathf.Clamp01(selectionScoreThreshold);
        forceSpawnDistance = Mathf.Max(0.001f, forceSpawnDistance);
        debugStatusDistance = Mathf.Max(0.001f, debugStatusDistance);
        debugStatusFontSize = Mathf.Max(0.001f, debugStatusFontSize);
        debugStatusWidth = Mathf.Max(0.1f, debugStatusWidth);
        debugStatusHeight = Mathf.Max(0.1f, debugStatusHeight);
        trackAssociationDistanceM = Mathf.Max(0.01f, trackAssociationDistanceM);
        trackLostTimeoutSec = Mathf.Max(0.1f, trackLostTimeoutSec);
        rosPosePositionThresholdM = Mathf.Max(0.01f, rosPosePositionThresholdM);
        rosPoseUpdateRateHz = Mathf.Max(0.1f, rosPoseUpdateRateHz);
        startupScanTimeoutSec = Mathf.Max(0.1f, startupScanTimeoutSec);
        manualScanWindowSec = Mathf.Max(0.1f, manualScanWindowSec);
    }

    protected override void Start()
    {
        base.Start();
        EnsurePoseArraySubscriber();
        EnsureDebugStatusText();
        EnsureButtonCollectionSpawnButton();
        EnsureManualSpawnButton();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        CommunicationHealthMonitor.SetChannelEnabled(
            CommunicationChannel.PoseArray, usePoseArray);
        CommunicationHealthMonitor.SetChannelEnabled(
            CommunicationChannel.PoseStamped, !usePoseArray);
        EnsurePoseArraySubscriber();
        EnsureDebugStatusText();
    }

    private void Update()
    {
        UpdateRuntimeSpawnCanvasPose();
        UpdateDebugStatusText();
        RefreshTrackGrabStates();

        if (ShouldSuppressLocalBottleDisplay())
        {
            HideLocalDetectedBottleInstances();
            UpdateCandidateBottleLabel();
            return;
        }

        UpdateCandidateBottleLabel();
    }

    private void UpdateCandidateBottleLabel()
    {
        Transform resolvedHmdTransform = ResolveHmdTransform();
        if (resolvedHmdTransform == null)
        {
            candidateBottle = null;
            SetCandidateLabelVisible(false);
            return;
        }

        GameObject bestBottle = null;
        float bestScore = float.MinValue;
        float bestPalmDistance = float.PositiveInfinity;
        float bestGazeScore = 0f;

        for (int i = 0; i < createdBottleInstances.Count; i++)
        {
            GameObject bottle = createdBottleInstances[i];
            if (bottle == null || !bottle.activeInHierarchy)
            {
                continue;
            }

            float score = CalculateCandidateScore(
                bottle,
                resolvedHmdTransform,
                out float palmDistance,
                out float gazeScore);
            if (score > bestScore)
            {
                bestBottle = bottle;
                bestScore = score;
                bestPalmDistance = palmDistance;
                bestGazeScore = gazeScore;
            }
        }

        candidateBottle = bestBottle != null && bestScore >= selectionScoreThreshold
            ? bestBottle
            : null;

        if (candidateBottle == null)
        {
            SetCandidateLabelVisible(false);
            return;
        }

        GameObject label = EnsureCandidateLabel();
        if (label == null)
        {
            return;
        }

        label.SetActive(true);
        float labelHeight = Mathf.Max(0.18f, createdBottleScale * 1.2f);
        label.transform.position = candidateBottle.transform.position + Vector3.up * labelHeight;

        Vector3 directionToHmd = label.transform.position - resolvedHmdTransform.position;
        if (directionToHmd.sqrMagnitude < 0.0001f)
        {
            directionToHmd = resolvedHmdTransform.forward;
        }

        label.transform.rotation = Quaternion.LookRotation(directionToHmd, Vector3.up);

        if (Time.time >= nextCandidateDebugLogTime)
        {
            nextCandidateDebugLogTime = Time.time + 0.5f;
            Debug.Log("[DetectedBottlePoseSubscriber] Candidate bottle="
                + candidateBottle.name
                + " score=" + bestScore.ToString("F3")
                + " palmDistance=" + FormatDistance(bestPalmDistance)
                + " gazeScore=" + bestGazeScore.ToString("F3"));
        }
    }

    private float CalculateCandidateScore(
        GameObject bottle,
        Transform resolvedHmdTransform,
        out float palmDistance,
        out float gazeScore)
    {
        Vector3 directionToBottle = bottle.transform.position - resolvedHmdTransform.position;
        if (directionToBottle.sqrMagnitude > 0.0001f)
        {
            directionToBottle.Normalize();
            gazeScore = Mathf.Clamp01(Vector3.Dot(resolvedHmdTransform.forward, directionToBottle));
        }
        else
        {
            gazeScore = 0f;
        }

        if (palmPoseTransform == null)
        {
            palmDistance = float.PositiveInfinity;
            return gazeScore;
        }

        palmDistance = Vector3.Distance(palmPoseTransform.position, bottle.transform.position);
        float palmScore = 1f - Mathf.Clamp01(palmDistance / Mathf.Max(0.001f, palmMaxDistance));
        return palmWeight * palmScore + gazeWeight * gazeScore;
    }

    private GameObject EnsureCandidateLabel()
    {
        if (candidateLabel != null)
        {
            return candidateLabel;
        }

        candidateLabel = new GameObject("Candidate Bottle Label");
        TextMeshPro labelText = candidateLabel.AddComponent<TextMeshPro>();
        labelText.text = "Candidate";
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.fontSize = 0.12f;
        labelText.color = Color.white;
        labelText.enableWordWrapping = false;
        candidateLabel.SetActive(false);
        return candidateLabel;
    }

    private void SetCandidateLabelVisible(bool isVisible)
    {
        if (candidateLabel != null)
        {
            candidateLabel.SetActive(isVisible);
        }
    }

    private Transform ResolveHmdTransform()
    {
        if (hmdTransform != null)
        {
            return hmdTransform;
        }

        return Camera.main != null ? Camera.main.transform : null;
    }

    private static string FormatDistance(float distance)
    {
        return float.IsInfinity(distance) ? "n/a" : distance.ToString("F3");
    }

    private void StartBottleScan(string source, float timeoutSec, bool allowImmediateCachedSnapshot)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (bottleScanCoroutine != null)
        {
            StopCoroutine(bottleScanCoroutine);
        }

        bottleScanCoroutine = StartCoroutine(BottleScanRoutine(source, timeoutSec, allowImmediateCachedSnapshot));
    }

    private IEnumerator BottleScanRoutine(string source, float timeoutSec, bool allowImmediateCachedSnapshot)
    {
        string resolvedSource = string.IsNullOrWhiteSpace(source) ? "Manual" : source;
        float resolvedTimeout = Mathf.Max(0.1f, timeoutSec);
        activeScanSource = resolvedSource;
        activeScanSnapshot.Clear();
        IsBottleScanInProgress = true;
        BottleScanStatus = "Scanning";
        ForceDebugStatusRefresh();

        if (string.Equals(resolvedSource, "Initial", System.StringComparison.Ordinal))
        {
            Debug.Log("[DetectedBottlePoseSubscriber] Initial bottle scan started:"
                + "\ntimeoutSec=" + resolvedTimeout.ToString("F2"));
        }
        else
        {
            Debug.Log("[DetectedBottlePoseSubscriber] Manual bottle scan started:"
                + "\nwindowSec=" + resolvedTimeout.ToString("F2"));
        }

        if (allowImmediateCachedSnapshot && hasLatestBottleSnapshot && latestBottleSnapshot.Count > 0)
        {
            CopySnapshot(latestBottleSnapshot, activeScanSnapshot);
            CommitActiveBottleSnapshot(resolvedSource);
            yield break;
        }

        float endTime = Time.unscaledTime + resolvedTimeout;
        while (IsBottleScanInProgress && Time.unscaledTime < endTime)
        {
            if (activeScanSnapshot.Count > 0)
            {
                CommitActiveBottleSnapshot(resolvedSource);
                yield break;
            }

            yield return null;
        }

        FinishBottleScanWithoutPose(resolvedSource);
    }

    private void FinishBottleScanWithoutPose(string source)
    {
        IsBottleScanInProgress = false;
        activeScanSource = "None";
        bottleScanCoroutine = null;
        BottleScanStatus = "Failed";
        if (string.Equals(source, "Initial", System.StringComparison.Ordinal))
        {
            HasCompletedInitialBottleScan = true;
        }

        Debug.Log("[DetectedBottlePoseSubscriber] Bottle scan finished without a valid pose:"
            + "\nsource=" + source);
        ForceDebugStatusRefresh();
    }

    private void CommitActiveBottleSnapshot(string source)
    {
        CommitBottleSnapshot(source, activeScanSnapshot);
        IsBottleScanInProgress = false;
        activeScanSource = "None";
        bottleScanCoroutine = null;
        BottleScanStatus = activeScanSnapshot.Count > 0 ? "Complete" : "Failed";
        if (string.Equals(source, "Initial", System.StringComparison.Ordinal))
        {
            HasCompletedInitialBottleScan = true;
        }

        ForceDebugStatusRefresh();
    }

    private void CommitBottleSnapshot(string source, List<UnityEngine.Pose> snapshot)
    {
        if (snapshot == null || snapshot.Count == 0)
        {
            FinishBottleScanWithoutPose(source);
            return;
        }

        currentFrameAssignedTrackIds.Clear();
        HashSet<int> seenTrackIds = new HashSet<int>();
        int createdTracks = 0;
        int updatedTracks = 0;
        int photonSpawned = 0;
        int photonUpdated = 0;
        int ignoredGrabbed = 0;
        int ignoredManual = 0;

        for (int i = 0; i < snapshot.Count; i++)
        {
            UnityEngine.Pose pose = snapshot[i];
            DetectedBottleTrack track = AssociateOrCreateTrack(pose.position, pose.rotation, out bool createdTrack);
            seenTrackIds.Add(track.TrackId);
            if (createdTrack)
            {
                createdTracks++;
            }
            else
            {
                updatedTracks++;
            }

            bool protectedByGrab = track.IsCurrentlyGrabbed || track.HasBeenGrabbed;
            if (protectedByGrab)
            {
                ignoredGrabbed++;
                continue;
            }

            if (ShouldSuppressLocalBottleDisplay())
            {
                PhotonSharedBottleSpawner spawner = ResolvePhotonBottleSpawner();
                bool existed = spawner != null && spawner.HasDetectedBottleTrack(track.TrackId);
                if (ForwardTrackToPhotonSharedBottleIfConnected(track))
                {
                    if (existed)
                    {
                        photonUpdated++;
                    }
                    else
                    {
                        photonSpawned++;
                    }
                }
            }
            else
            {
                SetBottleTarget(track.TrackId, track.UnityPosition);
                GameObject instance = EnsureBottleInstance(track.TrackId, false);
                if (instance != null)
                {
                    instance.transform.position = track.UnityPosition;
                    instance.transform.rotation = track.UnityRotation;
                }

                ShowBottleInstance(track.TrackId, false);
            }
        }

        if (ShouldSuppressLocalBottleDisplay())
        {
            HideLocalDetectedBottleInstances();
        }

        if (removeUnseenUngrabbedRosBottlesOnScan)
        {
            RemoveUnseenUngrabbedTracks(seenTrackIds);
        }

        latestDetectedCount = snapshot.Count;
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle snapshot committed:"
            + "\nsource=" + source
            + "\nposeCount=" + snapshot.Count
            + "\nupdatedTracks=" + updatedTracks
            + "\ncreatedTracks=" + createdTracks);

        PhotonSharedBottleSpawner resolvedSpawner = ResolvePhotonBottleSpawner();
        if (resolvedSpawner != null && resolvedSpawner.CanSpawnSharedBottle(out _))
        {
            resolvedSpawner.LogAppliedBottleSnapshot(source, photonSpawned, photonUpdated, ignoredGrabbed, ignoredManual);
        }
    }

    private void CacheBottleSnapshot(string frameId, Vector3 unityPosition, Quaternion unityRotation)
    {
        latestDetectedUnityPositions.Clear();
        latestDetectedUnityPositions.Add(unityPosition);
        latestBottleSnapshot.Clear();
        latestBottleSnapshot.Add(new UnityEngine.Pose(unityPosition, unityRotation));
        hasLatestBottleSnapshot = true;
        latestBottleSnapshotTime = Time.unscaledTime;
        latestBottleSnapshotFrameId = GetFrameId(frameId);
        latestDetectedCount = 1;
        if (IsBottleScanInProgress)
        {
            CopySnapshot(latestBottleSnapshot, activeScanSnapshot);
        }
    }

    private void TryCommitInitialSnapshotAfterPhotonJoin()
    {
        if (initialPhotonCommitCompleted
            || !scanOnceOnStartup
            || !hasLatestBottleSnapshot
            || latestBottleSnapshot.Count == 0
            || !ShouldSuppressLocalBottleDisplay())
        {
            return;
        }

        initialPhotonCommitCompleted = true;
        CopySnapshot(latestBottleSnapshot, activeScanSnapshot);
        CommitBottleSnapshot("Initial", activeScanSnapshot);
    }

    private void RemoveUnseenUngrabbedTracks(HashSet<int> seenTrackIds)
    {
        for (int i = bottleTracks.Count - 1; i >= 0; i--)
        {
            DetectedBottleTrack track = bottleTracks[i];
            if (track == null || seenTrackIds.Contains(track.TrackId))
            {
                continue;
            }

            RefreshTrackGrabState(track);
            if (track.IsCurrentlyGrabbed || track.HasBeenGrabbed)
            {
                continue;
            }

            PhotonSharedBottleSpawner spawner = ResolvePhotonBottleSpawner();
            if (spawner != null)
            {
                spawner.TryDespawnDetectedBottleTrack(track.TrackId);
            }

            RemoveLocalBottleInstance(track.TrackId);
            bottleTracks.RemoveAt(i);
        }
    }

    private static void CopySnapshot(List<UnityEngine.Pose> source, List<UnityEngine.Pose> destination)
    {
        destination.Clear();
        if (source == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            destination.Add(source[i]);
        }
    }

    public void SpawnBottleManual()
    {
        GenerateOrRefreshBottle();
    }

    public void SpawnBottleManual(int index)
    {
        GenerateOrRefreshBottle();
    }

    public void GenerateOrRefreshBottle()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double poseAge = hasLatestPose ? now - latestPoseReceivedTime : -1d;
        Debug.Log("[DetectedBottlePoseSubscriber] GenerateOrRefreshBottle called"
            + " hasLatestPose=" + hasLatestPose
            + " latestRosPosition=" + latestRosPosition.ToString("F3")
            + " poseAge=" + (poseAge >= 0d ? poseAge.ToString("F3") + "s" : "n/a"));

        if (bottlePrefab == null)
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] Bottle was not generated: bottlePrefab is not assigned.");
            return;
        }

        if (!TryGetLatestBottleWorldPose(out UnityEngine.Pose worldPose, out string failureReason))
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] Bottle was not generated: " + failureReason);
            return;
        }

        Vector3 worldPosition = worldPose.position;

        if (manualBottleInstance == null)
        {
            manualBottleInstance = Instantiate(
                bottlePrefab,
                worldPosition,
                worldPose.rotation,
                null);
            manualBottleInstance.name = ManualBottleName;
            manualBottleInstance.transform.localScale = Vector3.one * createdBottleScale;
            ApplyManualBottleInteractionSetup(manualBottleInstance);
        }
        else
        {
            manualBottleInstance.transform.position = worldPosition;
            Rigidbody[] rigidbodies = manualBottleInstance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].velocity = Vector3.zero;
                rigidbodies[i].angularVelocity = Vector3.zero;
            }
        }

        manualBottleInstance.SetActive(true);
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle generated/refreshed from latest PoseStamped at "
            + worldPosition.ToString("F3"));
    }

    public bool TryGetLatestBottleWorldPose(out UnityEngine.Pose worldPose)
    {
        return TryGetLatestBottleWorldPose(out worldPose, out _);
    }

    public bool TryGetLatestBottleWorldPose(
        out UnityEngine.Pose worldPose,
        out string failureReason)
    {
        worldPose = new UnityEngine.Pose(Vector3.zero, Quaternion.identity);
        failureReason = string.Empty;
        if (!hasLatestPose)
        {
            failureReason = "PoseNotReceived";
            return false;
        }

        if (!IsFinite(latestRosPosition))
        {
            failureReason = "NonFiniteRosPosition";
            return false;
        }

        double poseAge = Time.realtimeSinceStartupAsDouble - latestPoseReceivedTime;
        if (!IsFinite(poseAge) || poseAge < 0d)
        {
            failureReason = "InvalidPoseAge";
            return false;
        }

        if (latestPoseMaxAgeSec > 0f && poseAge > latestPoseMaxAgeSec)
        {
            failureReason = "PoseStale age=" + poseAge.ToString("F3")
                + "s maxAge=" + latestPoseMaxAgeSec.ToString("F3") + "s";
            return false;
        }

        Vector3 worldPosition = ConvertRosToBottleWorldPosition(
            latestRosPosition,
            latestRosPoseFrameId,
            out Vector3 convertedPosition);
        if (!IsFinite(worldPosition))
        {
            failureReason = "NonFinitePosition";
            return false;
        }

        worldPose = new UnityEngine.Pose(worldPosition, Quaternion.identity);
        failureReason = "Accepted";
        if (!convertedWithoutDisplayOffsetLogged)
        {
            convertedWithoutDisplayOffsetLogged = true;
            Debug.Log("[DetectedBottlePoseSubscriber] Bottle display offset applied"
                + " frame=" + latestRosPoseFrameId
                + " rosPosition=" + latestRosPosition.ToString("F3")
                + " convertedPosition=" + convertedPosition.ToString("F3")
                + " displayOffset=" + bottleDisplayOffsetWorld.ToString("F3")
                + " finalPosition=" + worldPosition.ToString("F3")
                + " positionScale=" + positionScale.ToString("F3"));
        }
        return true;
    }

    public void RequestBottleScan()
    {
        if (IsBottleScanInProgress)
        {
            if (bottleScanCoroutine != null)
            {
                StopCoroutine(bottleScanCoroutine);
                bottleScanCoroutine = null;
            }

            IsBottleScanInProgress = false;
            BottleScanStatus = "Idle";
            activeScanSource = "None";
            ForceDebugStatusRefresh();
            return;
        }

        StartBottleScan("Manual", manualScanWindowSec, false);
    }

    private void SpawnBottleManualLegacy(int index)
    {
        spawnBottleCallCount++;
        spawnBottleManualCallCount++;
        Debug.Log("[DetectedBottlePoseSubscriber] Spawn Bottle button pressed");
        ForceDebugStatusRefresh();

        int latestDetectionCount = latestDetectedUnityPositions.Count;
        if (latestDetectionCount > 0)
        {
            int createdBottleCount = Mathf.Min(
                Mathf.Max(1, spawnDetectionCount),
                latestDetectionCount);
            Debug.Log("[DetectedBottlePoseSubscriber] SpawnBottleManual using latest detections count="
                + latestDetectionCount);
            Debug.Log("[DetectedBottlePoseSubscriber] Spawning created bottles count="
                + createdBottleCount);

            for (int i = 0; i < createdBottleCount; i++)
            {
                SpawnBottleFromDetection(i);
            }

            return;
        }

        Debug.Log("[DetectedBottlePoseSubscriber] No latest detection. Falling back to HMD-front spawn.");
        SpawnBottleAtHmdFront();
    }

    public void SpawnBottleFromDetection(int index)
    {
        spawnBottleCallCount++;
        spawnBottleFromDetectionCallCount++;
        Debug.Log("[DetectedBottlePoseSubscriber] SpawnBottleFromDetection index=" + index);
        ForceDebugStatusRefresh();

        if (!HasLatestDetection(index))
        {
            Debug.Log("[DetectedBottlePoseSubscriber] No latest detection. Falling back to HMD-front spawn.");
            SpawnBottleAtHmdFront();
            return;
        }

        if (bottlePrefab == null)
        {
            Debug.LogError("[DetectedBottlePoseSubscriber] bottlePrefab is not assigned; detection spawn failed.");
            return;
        }

        Debug.Log("[DetectedBottlePoseSubscriber] Spawn context bottlePrefab=" + GetObjectName(bottlePrefab)
            + " createdBottleInstances slots=" + createdBottleInstances.Count
            + " nonNull=" + CountNonNullCreatedBottles()
            + " active=" + CountActiveCreatedBottles());

        Vector3 spawnPosition = latestDetectedUnityPositions[index];

        GameObject instance = EnsureCreatedBottleInstance(index, spawnPosition);
        if (instance == null)
        {
            return;
        }

        instance.name = GetCreatedBottleName(index);
        instance.transform.position = spawnPosition;
        instance.transform.localScale = Vector3.one * createdBottleScale;
        instance.SetActive(true);
        ApplyManualBottleInteractionSetup(instance);

        Debug.Log("[DetectedBottlePoseSubscriber] Created manipulable bottle from detection at position="
            + spawnPosition.ToString("F3"));
        Debug.Log("[DetectedBottlePoseSubscriber] Created bottle scale="
            + createdBottleScale.ToString("F3"));
        LogBottleVisibilityState("Created Bottle[" + index + "] after spawn", instance);
    }

    private void SpawnBottleAtHmdFront()
    {
        spawnBottleCallCount++;
        hmdFrontSpawnCallCount++;
        if (bottlePrefab == null)
        {
            Debug.LogError("[DetectedBottlePoseSubscriber] bottlePrefab is not assigned; manual spawn failed.");
            return;
        }

        if (!TryGetHmdFrontPosition(0.6f, -0.1f, out Vector3 spawnPosition))
        {
            Debug.LogError("[DetectedBottlePoseSubscriber] Camera.main was not found; manual spawn failed.");
            return;
        }

        bool reusingExistingBottle = manualBottleInstance != null;
        GameObject instance = EnsureManualBottleInstance();
        if (instance == null)
        {
            return;
        }

        if (reusingExistingBottle)
        {
            Debug.Log("[DetectedBottlePoseSubscriber] Reusing existing bottle instance");
        }

        instance.transform.position = spawnPosition;
        instance.transform.localScale = Vector3.one * createdBottleScale;
        instance.SetActive(true);
        ApplyManualBottleInteractionSetup(instance);
        Debug.Log("[DetectedBottlePoseSubscriber] Spawn Bottle position="
            + spawnPosition.ToString("F3"));
        Debug.Log("[DetectedBottlePoseSubscriber] Created bottle scale="
            + createdBottleScale.ToString("F3"));
        LogBottleVisibilityState("Manual Bottle after spawn", instance);
    }

    private bool HasLatestDetection(int index)
    {
        return index >= 0 && index < latestDetectedUnityPositions.Count;
    }

    private GameObject EnsureCreatedBottleInstance(int index, Vector3 initialPosition)
    {
        if (index < 0)
        {
            return null;
        }

        while (createdBottleInstances.Count <= index)
        {
            createdBottleInstances.Add(null);
        }

        GameObject instance = createdBottleInstances[index];
        if (instance != null)
        {
            return instance;
        }

        if (bottlePrefab == null)
        {
            Debug.LogError("[DetectedBottlePoseSubscriber] bottlePrefab is not assigned; detection bottle cannot be displayed.");
            return null;
        }

        instance = Instantiate(
            bottlePrefab,
            initialPosition,
            bottlePrefab.transform.rotation,
            parent);
        instance.name = GetCreatedBottleName(index);
        instance.transform.localScale = Vector3.one * createdBottleScale;
        instance.SetActive(true);
        createdBottleInstances[index] = instance;
        createdBottleCreatedCount++;
        LogBottleVisibilityState("Created Bottle[" + index + "] created", instance);
        return instance;
    }

    private static string GetCreatedBottleName(int index)
    {
        return "Created Bottle from Detection " + index;
    }

    private GameObject EnsureManualBottleInstance()
    {
        if (manualBottleInstance != null)
        {
            return manualBottleInstance;
        }

        if (bottlePrefab == null)
        {
            Debug.LogError("[DetectedBottlePoseSubscriber] bottlePrefab is not assigned; manual bottle cannot be displayed.");
            return null;
        }

        manualBottleInstance = Instantiate(
            bottlePrefab,
            Vector3.zero,
            bottlePrefab.transform.rotation,
            parent);
        manualBottleInstance.name = ManualBottleName;
        manualBottleInstance.transform.localScale = Vector3.one * createdBottleScale;
        manualBottleInstance.SetActive(true);
        LogBottleVisibilityState("Manual Bottle created", manualBottleInstance);
        return manualBottleInstance;
    }

    private bool TryGetHmdFrontPosition(float distance, float verticalOffset, out Vector3 position)
    {
        Transform resolvedHmdTransform = ResolveHmdTransform();
        if (resolvedHmdTransform == null)
        {
            position = Vector3.zero;
            return false;
        }

        Vector3 forward = resolvedHmdTransform.forward.sqrMagnitude > 0.0001f
            ? resolvedHmdTransform.forward.normalized
            : Vector3.forward;
        position = resolvedHmdTransform.position
            + forward * distance
            + Vector3.up * verticalOffset;
        return true;
    }

    private void EnsureDebugStatusText()
    {
        if (!Application.isPlaying || !showDebugStatusText)
        {
            return;
        }

        if (debugStatusText != null)
        {
            debugStatusObject.SetActive(true);
            return;
        }

        debugStatusObject = new GameObject("DetectedBottlePoseDebugStatus", typeof(RectTransform));
        debugStatusText = debugStatusObject.AddComponent<TextMeshPro>();
        debugStatusText.alignment = TextAlignmentOptions.TopLeft;
        debugStatusText.color = Color.cyan;
        debugStatusText.enableWordWrapping = false;
        debugStatusText.richText = false;
        ApplyDebugStatusTextStyle();
        ForceDebugStatusRefresh();
    }

    private void UpdateDebugStatusText()
    {
        if (!showDebugStatusText)
        {
            if (debugStatusObject != null)
            {
                debugStatusObject.SetActive(false);
            }

            return;
        }

        EnsureDebugStatusText();
        if (debugStatusText == null)
        {
            return;
        }

        ApplyDebugStatusTextStyle();
        Transform resolvedHmdTransform = ResolveHmdTransform();
        if (resolvedHmdTransform != null)
        {
            Transform debugTransform = debugStatusText.transform;
            debugTransform.position = resolvedHmdTransform.position
                + resolvedHmdTransform.forward.normalized * debugStatusDistance
                + Vector3.up * debugStatusVerticalOffset;

            Vector3 directionToHmd = debugTransform.position - resolvedHmdTransform.position;
            if (directionToHmd.sqrMagnitude < 0.0001f)
            {
                directionToHmd = resolvedHmdTransform.forward;
            }

            debugTransform.rotation = Quaternion.LookRotation(directionToHmd, Vector3.up);
        }

        if (Time.time < nextDebugStatusUpdateTime)
        {
            return;
        }

        nextDebugStatusUpdateTime = Time.time + 0.2f;
        debugStatusText.text = BuildDebugStatusText();
    }

    private void ApplyDebugStatusTextStyle()
    {
        if (debugStatusText == null)
        {
            return;
        }

        debugStatusText.fontSize = debugStatusFontSize;
        RectTransform rectTransform = debugStatusObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(debugStatusWidth, debugStatusHeight);
    }

    private void ForceDebugStatusRefresh()
    {
        nextDebugStatusUpdateTime = 0f;
    }

    private string BuildDebugStatusText()
    {
        StringBuilder builder = new StringBuilder(512);
        builder.AppendLine("[DetectedBottlePose]");
        builder.AppendLine("ROS: " + GetRosConnectionStatus());
        builder.AppendLine("Topic: " + PoseArrayTopic);
        builder.AppendLine("Subscribed: " + (string.IsNullOrWhiteSpace(subscribedPoseArrayTopic)
            ? "none"
            : subscribedPoseArrayTopic));
        builder.AppendLine("PoseArrayMsg: " + MessageRegistry.GetRosMessageName<PoseArrayMsg>());
        builder.AppendLine("PoseStampedMsg: " + MessageRegistry.GetRosMessageName<PoseStampedMsg>());
        builder.AppendLine("/detected_bottle_poses received: " + poseArrayReceiveCount);
        builder.AppendLine("Detected Bottles: " + bottleTracks.Count);
        builder.AppendLine("Scan Status: " + BottleScanStatus);
        builder.AppendLine("latestDetectedCount: " + latestDetectedCount);
        builder.AppendLine("latestDetectedUnityPositions: "
            + FormatPositions(latestDetectedUnityPositions, 5));
        builder.AppendLine("bottlePrefab: " + GetObjectName(bottlePrefab));
        builder.AppendLine("Spawn Bottle calls: " + spawnBottleCallCount
            + " manual=" + spawnBottleManualCallCount
            + " detection=" + spawnBottleFromDetectionCallCount
            + " hmd=" + hmdFrontSpawnCallCount);
        builder.AppendLine("Created Bottle created: " + createdBottleCreatedCount
            + " slots=" + createdBottleInstances.Count
            + " nonNull=" + CountNonNullCreatedBottles()
            + " active=" + CountActiveCreatedBottles());
        builder.AppendLine("Detected Bottle created: " + detectedBottleCreatedCount);
        builder.AppendLine("forceSpawnInFrontOfHmd: " + forceSpawnInFrontOfHmd);
        builder.AppendLine("lastFrameId: " + latestPoseArrayFrameId);
        builder.AppendLine("lastObject: " + latestCreatedBottleState);
        return builder.ToString();
    }

    private string GetRosConnectionStatus()
    {
        ROSConnection ros = poseArrayRos;
        if (ros == null && Application.isPlaying)
        {
            ros = ROSConnection.GetOrCreateInstance();
        }

        if (ros == null)
        {
            return "object=false thread=false error=unknown";
        }

        return "object=true thread=" + ros.HasConnectionThread
            + " error=" + ros.HasConnectionError;
    }

    private int CountActiveCreatedBottles()
    {
        int count = 0;
        for (int i = 0; i < createdBottleInstances.Count; i++)
        {
            GameObject instance = createdBottleInstances[i];
            if (instance != null && instance.activeSelf)
            {
                count++;
            }
        }

        return count;
    }

    private int CountNonNullCreatedBottles()
    {
        int count = 0;
        for (int i = 0; i < createdBottleInstances.Count; i++)
        {
            if (createdBottleInstances[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    private static string GetObjectName(Object unityObject)
    {
        return unityObject != null ? unityObject.name : "null";
    }

    private static string FormatPositions(IList<Vector3> positions, int maxCount)
    {
        if (positions == null || positions.Count == 0)
        {
            return "[]";
        }

        StringBuilder builder = new StringBuilder(128);
        builder.Append("[");
        int count = Mathf.Min(positions.Count, maxCount);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(i);
            builder.Append(":");
            builder.Append(positions[i].ToString("F3"));
        }

        if (positions.Count > count)
        {
            builder.Append(", ...");
        }

        builder.Append("]");
        return builder.ToString();
    }

    private void LogBottleVisibilityState(string context, GameObject instance)
    {
        string state = BuildBottleVisibilityState(instance);
        latestCreatedBottleState = context + " " + state;
        Debug.Log("[DetectedBottlePoseSubscriber] " + context + " " + state);
        ForceDebugStatusRefresh();
    }

    private static string BuildBottleVisibilityState(GameObject instance)
    {
        if (instance == null)
        {
            return "instance=null";
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        int enabledRendererCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
            {
                enabledRendererCount++;
            }
        }

        string layerName = LayerMask.LayerToName(instance.layer);
        if (string.IsNullOrWhiteSpace(layerName))
        {
            layerName = instance.layer.ToString();
        }

        return "activeSelf=" + instance.activeSelf
            + " activeInHierarchy=" + instance.activeInHierarchy
            + " position=" + instance.transform.position.ToString("F3")
            + " scale=" + instance.transform.localScale.ToString("F3")
            + " renderer.enabled=" + enabledRendererCount + "/" + renderers.Length
            + " layer=" + layerName
            + " material=" + GetMaterialSummary(renderers, 3);
    }

    private static string GetMaterialSummary(Renderer[] renderers, int maxCount)
    {
        if (renderers == null || renderers.Length == 0)
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder(96);
        int appended = 0;
        for (int i = 0; i < renderers.Length && appended < maxCount; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Material material = renderer.sharedMaterial;
            if (appended > 0)
            {
                builder.Append("|");
            }

            builder.Append(material != null ? material.name : "null");
            appended++;
        }

        if (renderers.Length > appended)
        {
            builder.Append("|...");
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private void ApplyManualBottleInteractionSetup(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        EnsureManualBottleCollider(instance);
        EnsureManualBottleRigidbody(instance);
        EnsureManualBottleObjectManipulator(instance);
        TryAddOptionalComponent(
            instance,
            "NearInteractionGrabbable",
            "MixedReality.Toolkit.Input.NearInteractionGrabbable",
            "Microsoft.MixedReality.Toolkit.Input.NearInteractionGrabbable");

        Debug.Log("[DetectedBottlePoseSubscriber] Manual bottle interaction setup applied");
    }

    private static void EnsureManualBottleCollider(GameObject instance)
    {
        if (instance.GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        BoxCollider boxCollider = instance.AddComponent<BoxCollider>();
        FitBoxColliderToRenderers(boxCollider);
        Debug.Log("[DetectedBottlePoseSubscriber] Added BoxCollider");
    }

    private static void EnsureManualBottleRigidbody(GameObject instance)
    {
        Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = instance.AddComponent<Rigidbody>();
            Debug.Log("[DetectedBottlePoseSubscriber] Added Rigidbody");
        }

        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
    }

    private static void EnsureManualBottleObjectManipulator(GameObject instance)
    {
        TryAddOptionalComponent(
            instance,
            "ObjectManipulator",
            "MixedReality.Toolkit.SpatialManipulation.ObjectManipulator",
            "Microsoft.MixedReality.Toolkit.UI.ObjectManipulator",
            "Microsoft.MixedReality.Toolkit.Input.ObjectManipulator");
    }

    private static void TryAddOptionalComponent(GameObject instance, string logName, params string[] typeNames)
    {
        System.Type componentType = FindComponentType(typeNames);
        if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            return;
        }

        Component component = instance.GetComponent(componentType);
        if (component == null)
        {
            component = instance.AddComponent(componentType);
            Debug.Log("[DetectedBottlePoseSubscriber] Added " + logName);
        }

        if (component is Behaviour behaviour)
        {
            behaviour.enabled = true;
        }
    }

    private static System.Type FindComponentType(params string[] typeNames)
    {
        foreach (string typeName in typeNames)
        {
            System.Type type = System.Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (string typeName in typeNames)
            {
                System.Type type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static void FitBoxColliderToRenderers(BoxCollider boxCollider)
    {
        Renderer[] renderers = boxCollider.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        bool hasBounds = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 localCorner = boxCollider.transform.InverseTransformPoint(worldCorner);
                        if (!hasBounds)
                        {
                            localMin = localCorner;
                            localMax = localCorner;
                            hasBounds = true;
                        }
                        else
                        {
                            localMin = Vector3.Min(localMin, localCorner);
                            localMax = Vector3.Max(localMax, localCorner);
                        }
                    }
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        boxCollider.center = (localMin + localMax) * 0.5f;
        boxCollider.size = localMax - localMin;
    }

    protected override void ReceiveMessage(PoseStampedMsg message)
    {
        if (message == null || message.pose == null || message.pose.position == null)
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] Invalid PoseStamped received; pose was not cached.");
            return;
        }

        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.PoseStamped);
        Vector3 rosPosition = new Vector3(
            (float)message.pose.position.x,
            (float)message.pose.position.y,
            (float)message.pose.position.z);
        CachePoseForManualGeneration(
            rosPosition,
            GetFrameId(message.header != null ? message.header.frame_id : null),
            "PoseStamped");
    }

    private void ReceivePoseArrayMessage(PoseArrayMsg message)
    {
        if (!usePoseArray || message == null || message.poses == null)
        {
            return;
        }

        poseArrayReceiveCount++;
        int receivedCount = message.poses.Length;
        string frameId = GetFrameId(message.header != null ? message.header.frame_id : null);
        latestPoseArrayFrameId = frameId;
        latestPoseArrayGenerationFrameId = frameId;
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.PoseArray);
        CommunicationHealthMonitor.Verbose(CommunicationChannel.PoseArray,
            "PoseArray received count=" + receivedCount);

        latestDetectedUnityPositions.Clear();
        latestBottleSnapshot.Clear();
        latestRosBottlePositions.Clear();
        latestPoseArrayReceivedTime = Time.realtimeSinceStartupAsDouble;
        hasLatestPoseArray = true;
        bool recordedPrimaryDetection = false;
        for (int i = 0; i < receivedCount; i++)
        {
            PoseMsg pose = message.poses[i];
            if (pose == null || pose.position == null)
            {
                continue;
            }

            Vector3 rosPosition = new Vector3(
                (float)pose.position.x,
                (float)pose.position.y,
                (float)pose.position.z);
            if (!IsFinite(rosPosition))
            {
                Debug.LogWarning("[DetectedBottlePoseSubscriber] PoseArray[" + i
                    + "] contains NaN or Infinity; skipped.");
                continue;
            }

            latestRosBottlePositions.Add(rosPosition);
            if (!recordedPrimaryDetection)
            {
                CachePoseForManualGeneration(rosPosition, frameId, "PoseArray[" + i + "]");
            }

            Vector3 unityPosition = ConvertRosToBottleWorldPosition(rosPosition, frameId);
            Quaternion unityRotation = ConvertRosToUnityRotation(pose.orientation);
            latestDetectedUnityPositions.Add(unityPosition);
            latestBottleSnapshot.Add(new UnityEngine.Pose(unityPosition, unityRotation));

            if (!recordedPrimaryDetection)
            {
                MarkLatestDetection(
                    unityPosition,
                    unityRotation,
                    frameId,
                    1f,
                    false,
                    0);
                recordedPrimaryDetection = true;
            }
        }

        CommunicationHealthMonitor.Verbose(CommunicationChannel.PoseArray,
            "Latest PoseArray cached count=" + latestDetectedUnityPositions.Count);
        latestDetectedCount = latestDetectedUnityPositions.Count;
        hasLatestBottleSnapshot = latestBottleSnapshot.Count > 0;
        latestBottleSnapshotTime = Time.unscaledTime;
        latestBottleSnapshotFrameId = frameId;
        if (IsBottleScanInProgress && hasLatestBottleSnapshot)
        {
            CopySnapshot(latestBottleSnapshot, activeScanSnapshot);
        }
        if (!recordedPrimaryDetection)
        {
            HasValidDetectedBottle = false;
            LatestDetectionConfidence = 0f;
        }

        CommunicationHealthMonitor.Verbose(CommunicationChannel.PoseArray,
            "PoseArray frame_id=" + frameId + " coordinateMode=" + OpticalFrameCoordinateMode);

        for (int i = 0; i < latestDetectedUnityPositions.Count; i++)
        {
            CommunicationHealthMonitor.Verbose(CommunicationChannel.PoseArray,
                "Cached detection[" + i + "] position="
                + latestDetectedUnityPositions[i].ToString("F3"));
        }

        ForceDebugStatusRefresh();
    }

    public bool TryGetLatestBottleWorldPoses(
        out IReadOnlyList<Vector3> worldPositions,
        out string failureReason)
    {
        worldPositions = new Vector3[0];
        failureReason = string.Empty;

        bool useArrayCache = usePoseArray && hasLatestPoseArray;
        if (!useArrayCache && !hasLatestPose)
        {
            failureReason = usePoseArray ? "PoseArrayNotReceived" : "PoseNotReceived";
            return false;
        }

        double receivedTime = useArrayCache
            ? latestPoseArrayReceivedTime
            : latestPoseReceivedTime;
        double poseAge = Time.realtimeSinceStartupAsDouble - receivedTime;
        if (!IsFinite(poseAge) || poseAge < 0d)
        {
            failureReason = "InvalidPoseAge";
            return false;
        }

        if (latestPoseMaxAgeSec > 0f && poseAge > latestPoseMaxAgeSec)
        {
            failureReason = "PoseStale age=" + poseAge.ToString("F3")
                + "s maxAge=" + latestPoseMaxAgeSec.ToString("F3") + "s";
            return false;
        }

        List<Vector3> convertedPositions = new List<Vector3>();
        string sourceFrame = useArrayCache
            ? latestPoseArrayGenerationFrameId
            : latestRosPoseFrameId;
        if (useArrayCache)
        {
            for (int i = 0; i < latestRosBottlePositions.Count; i++)
            {
                Vector3 rosPosition = latestRosBottlePositions[i];
                Vector3 converted = ConvertRosToBottleWorldPosition(
                    rosPosition,
                    sourceFrame,
                    out Vector3 convertedWithoutOffset);
                if (IsFinite(converted))
                {
                    convertedPositions.Add(converted);
                    CommunicationHealthMonitor.Verbose(CommunicationChannel.PoseArray,
                        "Converted bottle pose"
                        + " index=" + i
                        + " sourceFrame=" + sourceFrame
                        + " ros=" + rosPosition.ToString("F3")
                        + " convertedPosition=" + convertedWithoutOffset.ToString("F3")
                        + " finalPosition=" + converted.ToString("F3")
                        + " positionScale=" + positionScale.ToString("F3")
                        + " displayOffset=" + bottleDisplayOffsetWorld.ToString("F3"));
                }
            }
        }
        else
        {
            Vector3 converted = ConvertRosToBottleWorldPosition(
                latestRosPosition,
                sourceFrame,
                out Vector3 convertedWithoutOffset);
            if (IsFinite(converted))
            {
                convertedPositions.Add(converted);
                CommunicationHealthMonitor.Verbose(CommunicationChannel.PoseStamped,
                    "Converted bottle pose"
                    + " index=0"
                    + " sourceFrame=" + sourceFrame
                    + " ros=" + latestRosPosition.ToString("F3")
                    + " convertedPosition=" + convertedWithoutOffset.ToString("F3")
                    + " finalPosition=" + converted.ToString("F3")
                    + " positionScale=" + positionScale.ToString("F3")
                    + " displayOffset=" + bottleDisplayOffsetWorld.ToString("F3"));
            }
        }

        convertedPositions.Sort((a, b) =>
        {
            int xCompare = a.x.CompareTo(b.x);
            return xCompare != 0 ? xCompare : a.z.CompareTo(b.z);
        });

        worldPositions = convertedPositions;
        failureReason = "Accepted";
        CommunicationHealthMonitor.Verbose(
            useArrayCache ? CommunicationChannel.PoseArray : CommunicationChannel.PoseStamped,
            "Latest "
            + (useArrayCache ? "PoseArray" : "PoseStamped fallback")
            + " accepted validCount=" + convertedPositions.Count
            + " poseAge=" + poseAge.ToString("F3")
            + " sourceFrame=" + sourceFrame
            + " displayOffset=" + bottleDisplayOffsetWorld.ToString("F3"));
        return true;
    }

    private void CachePoseForManualGeneration(
        Vector3 rosPosition,
        string frameId,
        string source)
    {
        if (!IsFinite(rosPosition))
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] " + source
                + " pose contains NaN or Infinity; manual-generation cache was not updated.");
            return;
        }

        latestRosPosition = rosPosition;
        hasLatestPose = true;
        latestPoseReceivedTime = Time.realtimeSinceStartupAsDouble;
        latestRosPoseFrameId = GetFrameId(frameId);
        LatestDetectedBottleTimestamp = (float)latestPoseReceivedTime;
        LatestDetectionSourceFrame = latestRosPoseFrameId;
        CommunicationHealthMonitor.Verbose(
            source.StartsWith("PoseArray", System.StringComparison.Ordinal)
                ? CommunicationChannel.PoseArray
                : CommunicationChannel.PoseStamped,
            "Manual-generation pose cached"
            + " source=" + source
            + " sourceFrame=" + latestRosPoseFrameId
            + " rosPosition=" + latestRosPosition.ToString("F3")
            + " receivedTime=" + latestPoseReceivedTime.ToString("F3"));
    }

    private void UpdateBottleInstance(int index, PoseMsg pose, string frameId)
    {
        if (pose == null || pose.position == null)
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] Bottle[" + index + "] pose is null; skipped");
            return;
        }

        Vector3 rosPosition = new Vector3(
            (float)pose.position.x,
            (float)pose.position.y,
            (float)pose.position.z);
        Vector3 unityPosition = ConvertRosToBottleWorldPosition(rosPosition, frameId);
        SetBottleTarget(index, unityPosition);
        GameObject instance = EnsureBottleInstance(index, true);

        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] updated");
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] ROS position="
            + rosPosition.ToString("F3"));
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] Unity position="
            + unityPosition.ToString("F3")
            + " frame_id=" + frameId
            + " coordinateMode=" + OpticalFrameCoordinateMode);
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] final targetPosition="
            + bottleTargetPositions[index].ToString("F3"));
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] activeSelf="
            + (instance != null && instance.activeSelf));
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] localScale="
            + (instance != null ? instance.transform.localScale.ToString("F3") : "null"));
    }

    private void SetBottleTarget(int index, Vector3 unityPosition)
    {
        EnsureBottleListCapacity(index);
        bottleTargetPositions[index] = unityPosition;
        bottleHasTargetPositions[index] = true;

        if (index == 0)
        {
            targetUnityPosition = unityPosition;
            bottleInstance = bottleInstances[0];
        }
    }

    public bool TryGetLatestDetectedBottlePose(out UnityEngine.Pose pose)
    {
        pose = LatestDetectedBottlePose;
        return HasValidDetectedBottle;
    }

    private void MarkLatestDetection(
        Vector3 unityPosition,
        Quaternion unityRotation,
        string frameId,
        float confidence,
        bool forwardToPhoton = true,
        int trackId = 0)
    {
        if (!IsValidRotation(unityRotation))
        {
            unityRotation = bottlePrefab != null
                ? bottlePrefab.transform.rotation
                : Quaternion.identity;
        }

        LatestDetectedBottlePose = new UnityEngine.Pose(unityPosition, unityRotation);
        LatestDetectedBottleTimestamp = Time.realtimeSinceStartup;
        HasValidDetectedBottle = true;
        LatestDetectionConfidence = Mathf.Clamp01(confidence);
        LatestDetectionSourceFrame = GetFrameId(frameId);
        LogLatestRosBottlePoseCachedIfNeeded(unityPosition, unityRotation);
        if (forwardToPhoton)
        {
            ForwardLatestPoseToPhotonSharedBottleIfConnected(trackId, unityPosition, unityRotation, false, false);
        }
    }

    private DetectedBottleTrack AssociateOrCreateTrack(
        Vector3 unityPosition,
        Quaternion unityRotation,
        out bool createdTrack)
    {
        createdTrack = false;
        DetectedBottleTrack bestTrack = null;
        float bestDistance = float.PositiveInfinity;
        float maxAssociationDistance = Mathf.Max(0.01f, trackAssociationDistanceM);
        for (int i = 0; i < bottleTracks.Count; i++)
        {
            DetectedBottleTrack track = bottleTracks[i];
            if (track == null || currentFrameAssignedTrackIds.Contains(track.TrackId))
            {
                continue;
            }

            float distance = Vector3.Distance(track.UnityPosition, unityPosition);
            if (distance <= maxAssociationDistance && distance < bestDistance)
            {
                bestDistance = distance;
                bestTrack = track;
            }
        }

        if (bestTrack == null)
        {
            bestTrack = new DetectedBottleTrack
            {
                TrackId = nextBottleTrackId++,
                UnityPosition = unityPosition,
                UnityRotation = unityRotation,
                LastSeenUnscaledTime = Time.unscaledTime
            };
            bestTrack.UpdateLogged = true;
            bestTrack.LastLoggedPosition = unityPosition;
            bottleTracks.Add(bestTrack);
            createdTrack = true;
            Debug.Log("[DetectedBottlePoseSubscriber] Created bottle track:"
                + "\ntrackId=" + bestTrack.TrackId
                + "\nposition=" + unityPosition.ToString("F3"));
        }
        else
        {
            RefreshTrackGrabState(bestTrack);
            if (bestTrack.IsCurrentlyGrabbed || bestTrack.HasBeenGrabbed)
            {
                currentFrameAssignedTrackIds.Add(bestTrack.TrackId);
                return bestTrack;
            }

            if (!bestTrack.UpdateLogged
                || Vector3.Distance(bestTrack.LastLoggedPosition, unityPosition) >= Mathf.Max(0.01f, rosPosePositionThresholdM))
            {
                bestTrack.UpdateLogged = true;
                bestTrack.LastLoggedPosition = unityPosition;
                Debug.Log("[DetectedBottlePoseSubscriber] Updated bottle track:"
                    + "\ntrackId=" + bestTrack.TrackId
                    + "\nposition=" + unityPosition.ToString("F3"));
            }
        }

        currentFrameAssignedTrackIds.Add(bestTrack.TrackId);
        bestTrack.UnityPosition = unityPosition;
        bestTrack.UnityRotation = unityRotation;
        bestTrack.LastSeenUnscaledTime = Time.unscaledTime;
        bestTrack.TimedOutLogged = false;
        RefreshTrackGrabState(bestTrack);
        return bestTrack;
    }

    private bool ForwardTrackToPhotonSharedBottleIfConnected(DetectedBottleTrack track)
    {
        if (track == null)
        {
            return false;
        }

        float now = Time.unscaledTime;
        float minInterval = 1f / Mathf.Max(0.1f, rosPoseUpdateRateHz);
        if (lastPhotonTrackUpdateTime.TryGetValue(track.TrackId, out float lastUpdateTime)
            && now - lastUpdateTime < minInterval)
        {
            return false;
        }

        bool shouldUpdate = !lastPhotonTrackUpdatePosition.TryGetValue(track.TrackId, out Vector3 lastPosition)
            || !lastPhotonTrackUpdateRotation.TryGetValue(track.TrackId, out Quaternion lastRotation)
            || Vector3.Distance(lastPosition, track.UnityPosition) >= Mathf.Max(0.01f, rosPosePositionThresholdM)
            || Quaternion.Angle(lastRotation, track.UnityRotation) >= 5f;
        if (!shouldUpdate)
        {
            return false;
        }

        if (ForwardLatestPoseToPhotonSharedBottleIfConnected(
            track.TrackId,
            track.UnityPosition,
            track.UnityRotation,
            track.IsCurrentlyGrabbed,
            track.HasBeenGrabbed))
        {
            lastPhotonTrackUpdateTime[track.TrackId] = now;
            lastPhotonTrackUpdatePosition[track.TrackId] = track.UnityPosition;
            lastPhotonTrackUpdateRotation[track.TrackId] = track.UnityRotation;
            return true;
        }

        return false;
    }

    private bool ForwardLatestPoseToPhotonSharedBottleIfConnected(
        int trackId,
        Vector3 unityPosition,
        Quaternion unityRotation,
        bool isCurrentlyGrabbed,
        bool hasBeenGrabbed)
    {
        if (!usePhotonSharedBottleWhenConnected)
        {
            return false;
        }

        PhotonSharedBottleSpawner spawner = ResolvePhotonBottleSpawner();
        if (spawner == null)
        {
            return false;
        }

        if (!spawner.CanSpawnSharedBottle(out _))
        {
            return false;
        }

        bool updated = spawner.TrySpawnOrUpdateDetectedBottle(
            trackId,
            unityPosition,
            unityRotation,
            isCurrentlyGrabbed,
            hasBeenGrabbed);
        if (updated && disableLocalBottleWhenPhotonConnected)
        {
            HideLocalDetectedBottleInstances();
        }

        return updated;
    }

    private bool ShouldSuppressLocalBottleDisplay()
    {
        if (!usePhotonSharedBottleWhenConnected || !disableLocalBottleWhenPhotonConnected)
        {
            return false;
        }

        PhotonSharedBottleSpawner spawner = ResolvePhotonBottleSpawner();
        return spawner != null && spawner.CanSpawnSharedBottle(out _);
    }

    private PhotonSharedBottleSpawner ResolvePhotonBottleSpawner()
    {
        if (photonBottleSpawner != null)
        {
            return photonBottleSpawner;
        }

        photonBottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
        return photonBottleSpawner;
    }

    private void HideLocalDetectedBottleInstances()
    {
        bool changed = false;
        for (int i = 0; i < bottleInstances.Count; i++)
        {
            GameObject instance = bottleInstances[i];
            if (instance != null && instance.activeSelf)
            {
                instance.SetActive(false);
                changed = true;
            }
        }

        if (bottleInstance != null && bottleInstance.activeSelf)
        {
            bottleInstance.SetActive(false);
            changed = true;
        }

        if (changed || !localDetectedBottleHiddenForPhoton)
        {
            localDetectedBottleHiddenForPhoton = true;
            Debug.Log("[DetectedBottlePoseSubscriber] Local detected bottle display hidden while Photon shared bottle is active.");
        }
    }

    private void RefreshTrackGrabStates()
    {
        for (int i = 0; i < bottleTracks.Count; i++)
        {
            RefreshTrackGrabState(bottleTracks[i]);
        }
    }

    private void RefreshTrackGrabState(DetectedBottleTrack track)
    {
        if (track == null)
        {
            return;
        }

        NetworkedSharedSceneObject sharedObject = ResolveSharedSceneObjectForTrack(track.TrackId);
        track.SharedSceneObject = sharedObject;
#if FUSION_WEAVER && FUSION2
        track.PhotonBottleObject = sharedObject != null ? sharedObject.Object : null;
#endif
        bool isGrabbed = sharedObject != null && sharedObject.IsGrabbedByAnyUser;
        track.IsCurrentlyGrabbed = isGrabbed;
        if (isGrabbed)
        {
            track.HasBeenGrabbed = true;
        }
    }

    private NetworkedSharedSceneObject ResolveSharedSceneObjectForTrack(int trackId)
    {
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject != null
                && sharedObject.IsPhotonSharedNetworkBottle
                && sharedObject.SharedOrigin == SharedBottleOrigin.RosDetected
                && sharedObject.SharedDetectedBottleTrackId == trackId)
            {
                return sharedObject;
            }
        }

        return null;
    }

    private void UpdateBottleTrackTimeouts()
    {
        if (bottleTracks.Count == 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        float timeout = Mathf.Max(0.1f, trackLostTimeoutSec);
        for (int i = bottleTracks.Count - 1; i >= 0; i--)
        {
            DetectedBottleTrack track = bottleTracks[i];
            if (track == null)
            {
                bottleTracks.RemoveAt(i);
                continue;
            }

            if (now - track.LastSeenUnscaledTime < timeout)
            {
                continue;
            }

            RefreshTrackGrabState(track);
            if (track.IsCurrentlyGrabbed || track.HasBeenGrabbed || !destroyUnclaimedTrackAfterTimeout)
            {
                LogTrackTimedOutOnce(track, "Kept");
                continue;
            }

            bool despawned = true;
            PhotonSharedBottleSpawner spawner = ResolvePhotonBottleSpawner();
            if (spawner != null)
            {
                despawned = spawner.TryDespawnDetectedBottleTrack(track.TrackId);
            }

            if (!despawned)
            {
                LogTrackTimedOutOnce(track, "Kept");
                continue;
            }

            RemoveLocalBottleInstance(track.TrackId);
            bottleTracks.RemoveAt(i);
            Debug.Log("[DetectedBottlePoseSubscriber] Bottle track timed out:"
                + "\ntrackId=" + track.TrackId
                + "\naction=Despawned");
        }
    }

    private void LogTrackTimedOutOnce(DetectedBottleTrack track, string action)
    {
        if (track == null || track.TimedOutLogged)
        {
            return;
        }

        track.TimedOutLogged = true;
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle track timed out:"
            + "\ntrackId=" + track.TrackId
            + "\naction=" + action);
    }

    private void RemoveLocalBottleInstance(int trackId)
    {
        if (trackId >= 0 && trackId < bottleInstances.Count)
        {
            GameObject instance = bottleInstances[trackId];
            if (instance != null)
            {
                Destroy(instance);
            }

            bottleInstances[trackId] = null;
        }

        if (trackId >= 0 && trackId < bottleHasTargetPositions.Count)
        {
            bottleHasTargetPositions[trackId] = false;
        }

        if (trackId == 0)
        {
            bottleInstance = null;
        }
    }

    private void LogLatestRosBottlePoseCachedIfNeeded(Vector3 position, Quaternion rotation)
    {
        bool shouldLog = !latestRosBottlePoseCacheLogged
            || Vector3.Distance(lastLoggedLatestRosBottlePosition, position) >= 0.02f
            || Quaternion.Angle(lastLoggedLatestRosBottleRotation, rotation) >= 5f;
        if (!shouldLog)
        {
            return;
        }

        latestRosBottlePoseCacheLogged = true;
        lastLoggedLatestRosBottlePosition = position;
        lastLoggedLatestRosBottleRotation = rotation;
        Debug.Log("[DetectedBottlePoseSubscriber] Latest ROS bottle pose cached:"
            + "\nposition=" + position.ToString("F3")
            + "\nrotation=" + rotation.ToString("F3"));
    }

    private void EnsureBottleListCapacity(int index)
    {
        while (bottleInstances.Count <= index)
        {
            bottleInstances.Add(null);
        }

        while (bottleTargetPositions.Count <= index)
        {
            bottleTargetPositions.Add(Vector3.zero);
        }

        while (bottleHasTargetPositions.Count <= index)
        {
            bottleHasTargetPositions.Add(false);
        }
    }

    private int GetMaxDisplayedBottleCount()
    {
        return Mathf.Max(1, maxDisplayedBottles);
    }

    private GameObject EnsureBottleInstance(int index, bool logKeepVisible)
    {
        if (index < 0)
        {
            return null;
        }

        EnsureBottleListCapacity(index);
        if (index == 0 && bottleInstances[0] == null && bottleInstance != null)
        {
            bottleInstances[0] = bottleInstance;
        }

        GameObject instance = bottleInstances[index];
        if (instance != null)
        {
            if (index == 0)
            {
                bottleInstance = instance;
            }

            ShowBottleInstance(index, logKeepVisible);
            if (logKeepVisible)
            {
                Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] created or reused=reused");
            }

            return instance;
        }

        if (bottlePrefab == null)
        {
            if (!loggedMissingPrefab)
            {
                loggedMissingPrefab = true;
                Debug.LogError("[DetectedBottlePoseSubscriber] bottlePrefab is not assigned; detected bottle cannot be displayed.");
            }

            return null;
        }

        instance = Instantiate(
            bottlePrefab,
            bottleTargetPositions[index],
            bottlePrefab.transform.rotation,
            parent);
        instance.name = "Detected Bottle " + index;
        bottleInstances[index] = instance;
        if (index == 0)
        {
            bottleInstance = instance;
        }

        instance.transform.localScale = Vector3.one * detectedBottleScale;
        instance.transform.position = bottleTargetPositions[index];
        instance.SetActive(true);
        detectedBottleCreatedCount++;

        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] created");
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] created or reused=created");
        Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] position="
            + bottleTargetPositions[index].ToString("F3"));
        LogBottleVisibilityState("Detected Bottle[" + index + "] created", instance);
        ShowBottleInstance(index, logKeepVisible);
        return instance;
    }

    private GameObject GetBottleInstance(int index)
    {
        if (index < 0 || index >= bottleInstances.Count)
        {
            return null;
        }

        return bottleInstances[index];
    }

    private void ShowBottleInstance(int index, bool logKeepVisible)
    {
        GameObject instance = GetBottleInstance(index);
        if (instance == null)
        {
            return;
        }

        if (keepBottleVisible || !instance.activeSelf)
        {
            instance.SetActive(true);
            localDetectedBottleHiddenForPhoton = false;
        }

        if (logKeepVisible && keepBottleVisible)
        {
            if (index == 0)
            {
                Debug.Log("[DetectedBottlePoseSubscriber] Bottle instance kept visible");
            }

            Debug.Log("[DetectedBottlePoseSubscriber] Bottle[" + index + "] kept visible");
        }
    }

    private void EnsureManualSpawnButton()
    {
        if (!createRuntimeButton || spawnBottleButton != null)
        {
            return;
        }

        Canvas canvas = spawnButtonCanvas != null ? spawnButtonCanvas : CreateRuntimeSpawnCanvas();

        EnsureEventSystem();
        EnsureCanvasXrInteractable(canvas);
        spawnBottleButton = CreateSpawnBottleButton(canvas.transform);
        spawnBottleButton.onClick.AddListener(SpawnBottleManual);
    }

    private void EnsureButtonCollectionSpawnButton()
    {
        if (!addToButtonCollectionMenu || buttonCollectionSpawnButtonObject != null)
        {
            return;
        }

        Transform menuRoot = buttonCollectionRoot != null
            ? buttonCollectionRoot
            : FindButtonCollectionLikeRoot();
        if (menuRoot == null)
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] ButtonCollection menu was not found; enable createRuntimeButton to use the fallback Spawn Bottle UI.");
            return;
        }

        buttonCollectionSpawnButtonObject = FindExistingButtonCollectionSpawnButton(menuRoot);
        bool createdFromTemplate = false;
        if (buttonCollectionSpawnButtonObject == null)
        {
            Transform reusableButton = FindReusableButtonCollectionButton(menuRoot);
            if (reusableButton != null)
            {
                buttonCollectionSpawnButtonObject = reusableButton.gameObject;
            }
        }

        if (buttonCollectionSpawnButtonObject == null)
        {
            Transform templateButton = FindButtonCollectionTemplate(menuRoot);
            if (templateButton != null)
            {
                buttonCollectionSpawnButtonObject = Instantiate(
                    templateButton.gameObject,
                    templateButton.parent);
                createdFromTemplate = true;
            }
        }

        if (buttonCollectionSpawnButtonObject == null)
        {
            Canvas canvas = FindMenuCanvas(menuRoot);
            if (canvas == null)
            {
                canvas = CreateButtonCollectionCanvas(menuRoot);
            }

            EnsureEventSystem();
            EnsureCanvasXrInteractable(canvas);

            Transform buttonParent = FindButtonParent(canvas.transform);
            Button uiButton = FindExistingSpawnBottleUiButton(buttonParent);
            if (uiButton == null)
            {
                uiButton = CreateSpawnBottleButton(buttonParent);
                PositionButtonAfterExistingButtons(uiButton.GetComponent<RectTransform>(), buttonParent);
            }

            buttonCollectionSpawnButtonObject = uiButton.gameObject;
        }

        ConfigureButtonCollectionSpawnButton(buttonCollectionSpawnButtonObject, createdFromTemplate);
    }

    private static void EnsureCanvasXrInteractable(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        if (canvas.worldCamera == null)
        {
            canvas.worldCamera = Camera.main;
        }

        if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            Debug.Log("[DetectedBottlePoseSubscriber] TrackedDeviceGraphicRaycaster added to Spawn Bottle Canvas.");
        }
    }

    private Canvas CreateRuntimeSpawnCanvas()
    {
        GameObject canvasObject = new GameObject(
            "BottleManualSpawnCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster));

        runtimeSpawnCanvas = canvasObject.GetComponent<Canvas>();
        runtimeSpawnCanvas.renderMode = RenderMode.WorldSpace;
        runtimeSpawnCanvas.worldCamera = Camera.main;
        runtimeSpawnCanvas.sortingOrder = 50;

        RectTransform rectTransform = canvasObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(360f, 130f);
        rectTransform.localScale = Vector3.one * ManualMenuScale;
        UpdateRuntimeSpawnCanvasPose();
        Debug.Log("[DetectedBottlePoseSubscriber] World Space Canvas generated for Spawn Bottle button.");
        return runtimeSpawnCanvas;
    }

    private Canvas CreateButtonCollectionCanvas(Transform menuRoot)
    {
        GameObject canvasObject = new GameObject(
            "SpawnBottleButtonCollectionCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(menuRoot, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 60;

        RectTransform rectTransform = canvasObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(360f, 120f);
        rectTransform.localScale = GetLocalScaleForWorldScale(menuRoot, ManualMenuScale);
        rectTransform.localPosition = new Vector3(0f, -0.09f, 0.006f);
        rectTransform.localRotation = Quaternion.identity;

        Debug.Log("[DetectedBottlePoseSubscriber] Spawn Bottle Canvas generated under ButtonCollection menu: "
            + menuRoot.name);
        return canvas;
    }

    private Button CreateSpawnBottleButton(Transform canvasTransform)
    {
        GameObject buttonObject = new GameObject(
            SpawnBottleButtonName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(canvasTransform, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(320f, 96f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.05f, 0.12f, 0.18f, 0.90f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.10f, 0.28f, 0.40f, 0.95f);
        colors.pressedColor = new Color(0.00f, 0.55f, 0.75f, 1.00f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.text = SpawnBottleButtonName;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontSize = 36;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Debug.Log("[DetectedBottlePoseSubscriber] Spawn Bottle button generated.");
        return button;
    }

    private static Transform FindButtonCollectionLikeRoot()
    {
        Transform root = FindMenuRootByLabel("Operation Buttons");
        if (root != null)
        {
            return root;
        }

        root = FindMenuRootByLabel("Debugging Buttons");
        if (root != null)
        {
            return root;
        }

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        foreach (Transform transform in transforms)
        {
            if (NameLooksLikeButtonCollection(transform.name))
            {
                return transform;
            }
        }

        return null;
    }

    private static Transform FindMenuRootByLabel(string label)
    {
        TMP_Text[] tmpTexts = FindObjectsOfType<TMP_Text>(true);
        foreach (TMP_Text tmpText in tmpTexts)
        {
            if (tmpText != null && TextMatches(tmpText.text, label))
            {
                return ResolveMenuRoot(tmpText.transform);
            }
        }

        Text[] uiTexts = FindObjectsOfType<Text>(true);
        foreach (Text uiText in uiTexts)
        {
            if (uiText != null && TextMatches(uiText.text, label))
            {
                return ResolveMenuRoot(uiText.transform);
            }
        }

        return null;
    }

    private static Transform ResolveMenuRoot(Transform labelTransform)
    {
        Transform current = labelTransform;
        for (int i = 0; i < 6 && current != null; i++)
        {
            if (NameLooksLikeMenuRoot(current.name))
            {
                return current.name.Contains("Window") && current.parent != null
                    ? current.parent
                    : current;
            }

            current = current.parent;
        }

        return labelTransform.parent != null ? labelTransform.parent : labelTransform;
    }

    private static bool TextMatches(string text, string expected)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Replace("\n", " ").Contains(expected);
    }

    private static bool NameLooksLikeMenuRoot(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && (name.Contains("Window")
                || name.Contains("Menu")
                || name.Contains("Panel")
                || name.Contains("Collection")
                || name.Contains("Buttons"));
    }

    private static bool NameLooksLikeButtonCollection(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && (name.Contains("ButtonCollection")
                || name.Contains("Button Collection")
                || name.Contains("ObjectCollection")
                || name.Contains("Debugging Buttons")
                || name.Contains("Operation Buttons"));
    }

    private static Canvas FindMenuCanvas(Transform menuRoot)
    {
        Canvas canvas = menuRoot.GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            return canvas;
        }

        return menuRoot.GetComponentInChildren<Canvas>(true);
    }

    private static Transform FindButtonParent(Transform canvasTransform)
    {
        Button[] buttons = canvasTransform.GetComponentsInChildren<Button>(true);
        if (buttons.Length > 0)
        {
            return buttons[0].transform.parent;
        }

        return canvasTransform;
    }

    private static Button FindExistingSpawnBottleUiButton(Transform root)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button.name == SpawnBottleButtonName || TextMatches(GetButtonText(button), SpawnBottleButtonName))
            {
                return button;
            }
        }

        return null;
    }

    private GameObject FindExistingButtonCollectionSpawnButton(Transform root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            if (HasDirectButtonBehaviour(transform.gameObject)
                && (transform.name == SpawnBottleButtonName
                    || TextMatches(GetLabelTextSummary(transform.gameObject), SpawnBottleButtonName)))
            {
                return transform.gameObject;
            }
        }

        return null;
    }

    private static Transform FindReusableButtonCollectionButton(Transform root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            if (transform.name == "puras" && HasDirectButtonBehaviour(transform.gameObject))
            {
                return transform;
            }
        }

        return null;
    }

    private static Transform FindButtonCollectionTemplate(Transform root)
    {
        StatefulInteractable[] interactables = root.GetComponentsInChildren<StatefulInteractable>(true);
        foreach (StatefulInteractable interactable in interactables)
        {
            if (interactable == null
                || interactable.gameObject.name == SpawnBottleButtonName
                || interactable.gameObject.name == "puras"
                || !interactable.gameObject.activeSelf)
            {
                continue;
            }

            return interactable.transform;
        }

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button != null
                && button.gameObject.name != SpawnBottleButtonName
                && button.gameObject.name != "puras"
                && button.gameObject.activeSelf)
            {
                return button.transform;
            }
        }

        return null;
    }

    private void ConfigureButtonCollectionSpawnButton(GameObject buttonObject, bool positionAfterSiblings)
    {
        buttonObject.name = SpawnBottleButtonName;
        buttonObject.SetActive(true);

        string labelText = AssignSpawnBottleLabel(buttonObject);
        AssignSpawnBottleAction(buttonObject);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        if (positionAfterSiblings && rectTransform != null && rectTransform.parent != null)
        {
            PositionButtonAfterExistingButtons(rectTransform, rectTransform.parent);
            rectTransform.SetAsLastSibling();
        }

        Debug.Log("[DetectedBottlePoseSubscriber] Spawn Bottle button added to ButtonCollection");
        Debug.Log("[DetectedBottlePoseSubscriber] button GameObject path: "
            + GetGameObjectPath(buttonObject.transform));
        Debug.Log("[DetectedBottlePoseSubscriber] label text after assignment: "
            + labelText);
    }

    private static string AssignSpawnBottleLabel(GameObject buttonObject)
    {
        TMP_Text[] tmpTexts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text tmpText in tmpTexts)
        {
            tmpText.text = SpawnBottleButtonName;
        }

        Text[] uiTexts = buttonObject.GetComponentsInChildren<Text>(true);
        foreach (Text uiText in uiTexts)
        {
            uiText.text = SpawnBottleButtonName;
        }

        string labelText = GetLabelTextSummary(buttonObject);
        return string.IsNullOrWhiteSpace(labelText) ? SpawnBottleButtonName : labelText;
    }

    private void AssignSpawnBottleAction(GameObject buttonObject)
    {
        bool assigned = false;

        Button[] uiButtons = buttonObject.GetComponentsInChildren<Button>(true);
        foreach (Button uiButton in uiButtons)
        {
            uiButton.onClick = new Button.ButtonClickedEvent();
            uiButton.onClick.AddListener(SpawnBottleManual);
            assigned = true;
        }

        StatefulInteractable[] interactables = buttonObject.GetComponentsInChildren<StatefulInteractable>(true);
        foreach (StatefulInteractable interactable in interactables)
        {
            ReplaceStatefulOnClicked(interactable);
            interactable.OnClicked.AddListener(SpawnBottleManual);
            assigned = true;
        }

        if (!assigned)
        {
            Debug.LogWarning("[DetectedBottlePoseSubscriber] Spawn Bottle button has no Button or StatefulInteractable component.");
        }
    }

    private static void ReplaceStatefulOnClicked(StatefulInteractable interactable)
    {
        FieldInfo onClickedField = typeof(StatefulInteractable).GetField(
            "<OnClicked>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (onClickedField != null)
        {
            onClickedField.SetValue(interactable, new UnityEvent());
            return;
        }

        interactable.OnClicked.RemoveAllListeners();
    }

    private static bool HasButtonBehaviour(GameObject buttonObject)
    {
        return buttonObject.GetComponentInChildren<StatefulInteractable>(true) != null
            || buttonObject.GetComponentInChildren<Button>(true) != null;
    }

    private static bool HasDirectButtonBehaviour(GameObject buttonObject)
    {
        return buttonObject.GetComponent<StatefulInteractable>() != null
            || buttonObject.GetComponent<Button>() != null;
    }

    private static string GetLabelTextSummary(GameObject buttonObject)
    {
        string labelText = string.Empty;

        TMP_Text[] tmpTexts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text tmpText in tmpTexts)
        {
            if (!string.IsNullOrWhiteSpace(tmpText.text))
            {
                labelText = AppendLabelText(labelText, tmpText.text);
            }
        }

        Text[] uiTexts = buttonObject.GetComponentsInChildren<Text>(true);
        foreach (Text uiText in uiTexts)
        {
            if (!string.IsNullOrWhiteSpace(uiText.text))
            {
                labelText = AppendLabelText(labelText, uiText.text);
            }
        }

        return labelText;
    }

    private static string AppendLabelText(string current, string text)
    {
        return string.IsNullOrWhiteSpace(current)
            ? text
            : current + " | " + text;
    }

    private static string GetGameObjectPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static string GetButtonText(Button button)
    {
        Text text = button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            return text.text;
        }

        TMP_Text tmpText = button.GetComponentInChildren<TMP_Text>(true);
        return tmpText != null ? tmpText.text : string.Empty;
    }

    private static void PositionButtonAfterExistingButtons(RectTransform buttonRect, Transform parent)
    {
        RectTransform parentRect = parent as RectTransform;
        float y = 0f;
        bool foundExisting = false;
        RectTransform[] rectTransforms = parent.GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform rect in rectTransforms)
        {
            if (rect == null
                || rect == buttonRect
                || rect.parent != parent
                || !HasDirectButtonBehaviour(rect.gameObject)
                || !rect.gameObject.activeSelf)
            {
                continue;
            }

            float bottom = rect.anchoredPosition.y - rect.sizeDelta.y * 0.5f;
            if (!foundExisting || bottom < y)
            {
                y = bottom;
                foundExisting = true;
            }
        }

        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = foundExisting
            ? new Vector2(0f, y - buttonRect.sizeDelta.y * 0.65f)
            : Vector2.zero;

        if (parentRect != null)
        {
            float requiredHeight = Mathf.Abs(buttonRect.anchoredPosition.y) + buttonRect.sizeDelta.y;
            if (parentRect.sizeDelta.y < requiredHeight)
            {
                parentRect.sizeDelta = new Vector2(parentRect.sizeDelta.x, requiredHeight + 24f);
            }
        }
    }

    private static Vector3 GetLocalScaleForWorldScale(Transform parent, float worldScale)
    {
        Vector3 parentScale = parent.lossyScale;
        return new Vector3(
            SafeInverseScale(parentScale.x) * worldScale,
            SafeInverseScale(parentScale.y) * worldScale,
            SafeInverseScale(parentScale.z) * worldScale);
    }

    private static float SafeInverseScale(float scale)
    {
        return Mathf.Abs(scale) > 0.0001f ? 1f / scale : 1f;
    }

    private void UpdateRuntimeSpawnCanvasPose()
    {
        if (runtimeSpawnCanvas == null || Camera.main == null)
        {
            return;
        }

        Transform cameraTransform = Camera.main.transform;
        Transform canvasTransform = runtimeSpawnCanvas.transform;
        canvasTransform.position = cameraTransform.position
            + cameraTransform.forward.normalized * ManualMenuDistance
            + Vector3.down * ManualMenuDown;

        Vector3 forward = canvasTransform.position - cameraTransform.position;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = cameraTransform.forward;
        }

        canvasTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>(true);
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("BottleManualSpawnEventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
            Debug.Log("[DetectedBottlePoseSubscriber] EventSystem generated for Spawn Bottle button.");
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
            Debug.Log("[DetectedBottlePoseSubscriber] XRUIInputModule added to EventSystem.");
        }
    }

    private void EnsurePoseArraySubscriber()
    {
        if (!Application.isPlaying || !usePoseArray || string.IsNullOrWhiteSpace(PoseArrayTopic))
        {
            return;
        }

        poseArrayRos ??= ROSConnection.GetOrCreateInstance();
        if (subscribedPoseArrayTopic == PoseArrayTopic)
        {
            return;
        }

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        string rosMessageName = MessageRegistry.GetRosMessageName<PoseArrayMsg>();
        poseArrayRos.SubscribeByMessageName(PoseArrayTopic, rosMessageName, message =>
        {
            if (message is PoseArrayMsg poseArrayMessage)
            {
                ReceivePoseArrayMessage(poseArrayMessage);
                return;
            }

            Debug.LogError(
                "[DetectedBottlePoseSubscriber] Topic " + PoseArrayTopic +
                " expected PoseArrayMsg but received " + message.GetType().Name);
        });
        subscribedPoseArrayTopic = PoseArrayTopic;
        Debug.Log(
            "[DetectedBottlePoseSubscriber] Subscribe " + PoseArrayTopic +
            " messageType=" + rosMessageName);
        ForceDebugStatusRefresh();
    }

    private void EnsureDefaultTopics()
    {
        if (string.IsNullOrWhiteSpace(Topic))
        {
            Topic = DefaultTopic;
        }

        if (string.IsNullOrWhiteSpace(PoseArrayTopic))
        {
            PoseArrayTopic = DefaultPoseArrayTopic;
        }
    }

    private static Vector3 ConvertRosToUnity(Vector3 rosPosition, string sourceFrame)
    {
        string normalizedFrame = GetFrameId(sourceFrame).ToLowerInvariant();
        bool isOpticalFrame = normalizedFrame.Contains("optical_frame");
        bool isAmirBaseFrame = !isOpticalFrame
            && (normalizedFrame.Contains("base_link")
                || normalizedFrame.Contains("amir"));
        return isAmirBaseFrame
            ? ConvertAmirToUnity(rosPosition)
            : new Vector3(rosPosition.x, -rosPosition.y, rosPosition.z);
    }

    private Vector3 ConvertRosToBottleWorldPosition(
        Vector3 rosPosition,
        string sourceFrame)
    {
        return ConvertRosToBottleWorldPosition(rosPosition, sourceFrame, out _);
    }

    private Vector3 ConvertRosToBottleWorldPosition(
        Vector3 rosPosition,
        string sourceFrame,
        out Vector3 convertedPosition)
    {
        Vector3 unityAxes = ConvertRosToUnity(rosPosition, sourceFrame);
        convertedPosition = unityAxes * positionScale;
        return convertedPosition + bottleDisplayOffsetWorld;
    }

    private static Vector3 ConvertAmirToUnity(Vector3 amirPosition)
    {
        return new Vector3(
            -amirPosition.y,
            amirPosition.z,
            amirPosition.x);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static Quaternion ConvertRosToUnityRotation(QuaternionMsg rosRotation)
    {
        if (rosRotation == null)
        {
            return Quaternion.identity;
        }

        Quaternion unityRotation = new Quaternion(
            -(float)rosRotation.x,
            (float)rosRotation.y,
            -(float)rosRotation.z,
            (float)rosRotation.w);

        return IsValidRotation(unityRotation) ? Normalize(unityRotation) : Quaternion.identity;
    }

    private static Quaternion Normalize(Quaternion rotation)
    {
        float magnitude = Mathf.Sqrt(
            rotation.x * rotation.x
            + rotation.y * rotation.y
            + rotation.z * rotation.z
            + rotation.w * rotation.w);

        if (magnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        float inverse = 1f / magnitude;
        return new Quaternion(
            rotation.x * inverse,
            rotation.y * inverse,
            rotation.z * inverse,
            rotation.w * inverse);
    }

    private static bool IsValidRotation(Quaternion rotation)
    {
        return Mathf.Abs(rotation.x) > 0.000001f
            || Mathf.Abs(rotation.y) > 0.000001f
            || Mathf.Abs(rotation.z) > 0.000001f
            || Mathf.Abs(rotation.w) > 0.000001f;
    }

    private static string GetFrameId(string frameId)
    {
        return string.IsNullOrWhiteSpace(frameId)
            ? "unknown"
            : frameId;
    }
}
