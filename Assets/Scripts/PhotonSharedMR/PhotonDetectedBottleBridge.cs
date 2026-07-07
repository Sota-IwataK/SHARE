using UnityEngine;

public enum PhotonDetectionAuthorityMode
{
    Disabled = 0,
    LocalRealSenseAuthority = 1,
    HostLikeAuthority = 2
}

[DisallowMultipleComponent]
public class PhotonDetectedBottleBridge : MonoBehaviour
{
    private const string DetectedBottleId = "primary_bottle";
    private const string DetectionSpawnSource = "RealSenseDetection";

    [Header("Scene References")]
    public DetectedBottlePoseSubscriber detectedBottleSubscriber;
    public PhotonSharedBottleSpawner sharedBottleSpawner;
    public PhotonFusionSharedRoomBootstrap bootstrap;

    [Header("Detection Authority")]
    public bool enableBridge = true;
    public PhotonDetectionAuthorityMode authorityMode = PhotonDetectionAuthorityMode.LocalRealSenseAuthority;
    public bool localRealSenseAuthorityEnabled = true;

    [Header("Detection Lifetime")]
    public float validDetectionMaxAgeSeconds = 0.75f;
    public float lostDetectionAgeSeconds = 3.0f;
    public float sharedPoseUpdateMinIntervalSeconds = 0.05f;
    public float spawnRetryIntervalSeconds = 1.0f;

    [Header("Debug")]
    public bool enableDebugLogs;
    public float poseUpdateLogIntervalSeconds = 0.5f;
    public float stateLogIntervalSeconds = 2.0f;

    private NetworkedSharedSceneObject trackedSharedBottle;
    private float nextSharedPoseUpdateTime;
    private float nextSpawnAttemptTime;
    private float nextPoseUpdateLogTime;
    private float nextStateLogTime;
    private bool lastAuthorityState;
    private bool authorityStateInitialized;
    private string detectionState = "NotConnected";
    private string coordinateAlignmentStatus = "UnityWorldPoseForwardedToPhotonSharedSpace";
    private Vector3 lastDetectedWorldPosition;
    private Vector3 lastSharedWorldPosition;
    private string lastDetectionSourceFrame = "none";
    private float lastDetectionAgeSeconds = -1f;

    public string DetectionState => detectionState;
    public string CoordinateAlignmentStatus => coordinateAlignmentStatus;
    public Vector3 LastDetectedWorldPosition => lastDetectedWorldPosition;
    public Vector3 LastSharedWorldPosition => lastSharedWorldPosition;
    public string LastDetectionSourceFrame => lastDetectionSourceFrame;
    public float LastDetectionAgeSeconds => lastDetectionAgeSeconds;
    public bool IsDetectionAuthority => ResolveDetectionAuthority();

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();
        UpdateAuthorityLog();

        if (!enableBridge)
        {
            detectionState = "Disabled";
            return;
        }

        string sharedReason = "MissingSpawner";
        if (sharedBottleSpawner == null || !sharedBottleSpawner.CanSpawnSharedBottle(out sharedReason))
        {
            detectionState = "NotConnected";
            LogState("PHOTON_DETECTED_BOTTLE_IGNORED reason=" + sharedReason);
            return;
        }

        if (!ResolveDetectionAuthority())
        {
            detectionState = "NotAuthority";
            LogState("PHOTON_DETECTED_BOTTLE_IGNORED reason=NotAuthority");
            return;
        }

        if (!TryGetFreshDetectedPose(out UnityEngine.Pose detectedPose, out float ageSeconds))
        {
            UpdateLostDetectionState(ageSeconds);
            return;
        }

        detectionState = "Tracked";
        lastDetectedWorldPosition = detectedPose.position;
        lastDetectionSourceFrame = detectedBottleSubscriber != null
            ? detectedBottleSubscriber.LatestDetectionSourceFrame
            : "none";
        lastDetectionAgeSeconds = ageSeconds;
        LogPose("PHOTON_DETECTED_BOTTLE_VALID"
            + " id=" + DetectedBottleId
            + " position=" + FormatVector(detectedPose.position)
            + " sourceFrame=" + lastDetectionSourceFrame);

        UpdateSharedBottleFromDetection(detectedPose);
    }

    public bool TryGetAuthorityDetectedBottlePose(out UnityEngine.Pose pose)
    {
        ResolveReferences();
        if (!enableBridge
            || !ResolveDetectionAuthority()
            || sharedBottleSpawner == null
            || !sharedBottleSpawner.CanSpawnSharedBottle(out _)
            || !TryGetFreshDetectedPose(out pose, out _))
        {
            pose = new UnityEngine.Pose(Vector3.zero, Quaternion.identity);
            return false;
        }

        return true;
    }

    private void ResolveReferences()
    {
        if (detectedBottleSubscriber == null)
        {
            detectedBottleSubscriber = FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        }

        if (sharedBottleSpawner == null)
        {
            sharedBottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
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

    private void UpdateSharedBottleFromDetection(UnityEngine.Pose detectedPose)
    {
        trackedSharedBottle = ResolveTrackedSharedBottle();
        if (trackedSharedBottle == null)
        {
            if (Time.realtimeSinceStartup < nextSpawnAttemptTime)
            {
                return;
            }

            nextSpawnAttemptTime = Time.realtimeSinceStartup + Mathf.Max(0.1f, spawnRetryIntervalSeconds);
            sharedBottleSpawner.TrySpawnOrUpdateDetectedBottle(
                detectedPose.position,
                detectedPose.rotation);
            trackedSharedBottle = ResolveTrackedSharedBottle();
            lastSharedWorldPosition = detectedPose.position;
            if (trackedSharedBottle != null)
            {
                LogState("PHOTON_DETECTED_BOTTLE_SHARED_SPAWN"
                    + " id=" + DetectedBottleId
                    + " position=" + FormatVector(detectedPose.position));
            }
            return;
        }

        TrySetTrackedBottleDetectionVisualState(PhotonSharedBottleDetectionVisualState.Tracked, "DetectionTracked");

        if (trackedSharedBottle.IsGrabbedByAnyUser)
        {
            LogPose("PHOTON_DETECTED_BOTTLE_UPDATE_SKIPPED reason=GrabActive");
            return;
        }

        if (Time.realtimeSinceStartup < nextSharedPoseUpdateTime)
        {
            return;
        }

        nextSharedPoseUpdateTime = Time.realtimeSinceStartup
            + Mathf.Max(0.01f, sharedPoseUpdateMinIntervalSeconds);
        if (sharedBottleSpawner.TrySpawnOrUpdateDetectedBottle(
            detectedPose.position,
            detectedPose.rotation))
        {
            lastSharedWorldPosition = detectedPose.position;
            LogPose("PHOTON_DETECTED_BOTTLE_SHARED_UPDATE"
                + " id=" + DetectedBottleId
                + " position=" + FormatVector(detectedPose.position));
        }
    }

    private NetworkedSharedSceneObject ResolveTrackedSharedBottle()
    {
        if (trackedSharedBottle != null
            && trackedSharedBottle.isActiveAndEnabled
            && trackedSharedBottle.IsPhotonSharedNetworkBottle)
        {
            return trackedSharedBottle;
        }

        return sharedBottleSpawner != null ? sharedBottleSpawner.FindLatestSharedBottle() : null;
    }

    private bool TryGetFreshDetectedPose(out UnityEngine.Pose pose, out float ageSeconds)
    {
        pose = new UnityEngine.Pose(Vector3.zero, Quaternion.identity);
        ageSeconds = -1f;

        if (detectedBottleSubscriber == null
            || !detectedBottleSubscriber.HasValidDetectedBottle
            || !detectedBottleSubscriber.TryGetLatestDetectedBottlePose(out pose))
        {
            return false;
        }

        ageSeconds = Time.realtimeSinceStartup - detectedBottleSubscriber.LatestDetectedBottleTimestamp;
        lastDetectionAgeSeconds = ageSeconds;
        return ageSeconds <= Mathf.Max(0.02f, validDetectionMaxAgeSeconds);
    }

    private void UpdateLostDetectionState(float ageSeconds)
    {
        if (detectedBottleSubscriber == null || !detectedBottleSubscriber.HasValidDetectedBottle)
        {
            detectionState = "Lost";
            TrySetTrackedBottleDetectionVisualState(PhotonSharedBottleDetectionVisualState.Lost, "DetectionLost");
            LogState("PHOTON_DETECTED_BOTTLE_LOST");
            return;
        }

        if (ageSeconds >= Mathf.Max(validDetectionMaxAgeSeconds, lostDetectionAgeSeconds))
        {
            detectionState = "Lost";
            TrySetTrackedBottleDetectionVisualState(PhotonSharedBottleDetectionVisualState.Lost, "DetectionLost");
            LogState("PHOTON_DETECTED_BOTTLE_LOST"
                + " age=" + ageSeconds.ToString("F2"));
            return;
        }

        detectionState = "Stale";
        TrySetTrackedBottleDetectionVisualState(PhotonSharedBottleDetectionVisualState.Stale, "DetectionStale");
        LogState("PHOTON_DETECTED_BOTTLE_STALE"
            + " age=" + ageSeconds.ToString("F2"));
    }

    private void TrySetTrackedBottleDetectionVisualState(PhotonSharedBottleDetectionVisualState visualState, string reason)
    {
        NetworkedSharedSceneObject bottle = ResolveTrackedSharedBottle();
        if (bottle == null || bottle.IsGrabbedByAnyUser)
        {
            return;
        }

        bottle.TrySetDetectionVisualState(visualState, reason);
    }

    private bool ResolveDetectionAuthority()
    {
        if (!enableBridge || authorityMode == PhotonDetectionAuthorityMode.Disabled)
        {
            return false;
        }

        if (authorityMode == PhotonDetectionAuthorityMode.HostLikeAuthority)
        {
            return ResolveHostLikeAuthority();
        }

        if (authorityMode == PhotonDetectionAuthorityMode.LocalRealSenseAuthority)
        {
            return localRealSenseAuthorityEnabled && !IsQuestRuntime();
        }

        return false;
    }

    private bool ResolveHostLikeAuthority()
    {
        EnsureBootstrap(nameof(ResolveHostLikeAuthority), false);

        if (NetworkUserAvatar.Local != null)
        {
            return NetworkUserAvatar.Local.IsHostLikeUser;
        }

        if (bootstrap != null && bootstrap.defaultSessionSettings != null)
        {
            return bootstrap.defaultSessionSettings.isHostLikeUser;
        }

        return false;
    }

    private bool IsQuestRuntime()
    {
        if (Application.platform == RuntimePlatform.Android)
        {
            return true;
        }

        if (EnsureBootstrap(nameof(IsQuestRuntime), false) == null)
        {
            return false;
        }

        ShareDeviceType deviceType = bootstrap.DebugDeviceType;
        return deviceType == ShareDeviceType.QuestStandalone
            || deviceType == ShareDeviceType.QuestLink;
    }

    private void UpdateAuthorityLog()
    {
        bool authority = ResolveDetectionAuthority();
        if (!authorityStateInitialized || authority != lastAuthorityState)
        {
            authorityStateInitialized = true;
            lastAuthorityState = authority;
            if (enableDebugLogs)
            {
                Debug.Log("[PhotonDetectedBottleBridge] PHOTON_DETECTION_AUTHORITY"
                    + " enabled=" + authority
                    + " mode=" + authorityMode
                    + " localRealSenseAuthorityEnabled=" + localRealSenseAuthorityEnabled);
            }
        }
    }

    private void LogPose(string message)
    {
        if (!enableDebugLogs || Time.realtimeSinceStartup < nextPoseUpdateLogTime)
        {
            return;
        }

        nextPoseUpdateLogTime = Time.realtimeSinceStartup
            + Mathf.Max(0.05f, poseUpdateLogIntervalSeconds);
        Debug.Log("[PhotonDetectedBottleBridge] " + message);
    }

    private void LogState(string message)
    {
        if (!enableDebugLogs || Time.realtimeSinceStartup < nextStateLogTime)
        {
            return;
        }

        nextStateLogTime = Time.realtimeSinceStartup
            + Mathf.Max(0.05f, stateLogIntervalSeconds);
        Debug.Log("[PhotonDetectedBottleBridge] " + message);
    }

    private static string FormatVector(Vector3 value)
    {
        return value.ToString("F3");
    }
}
