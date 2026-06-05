using System.Collections;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit.Subsystems;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine.XR;
using RosHeader = RosMessageTypes.Std.HeaderMsg;
using RosPoint = RosMessageTypes.Geometry.PointMsg;
using RosPose = RosMessageTypes.Geometry.PoseMsg;
using RosQuaternion = RosMessageTypes.Geometry.QuaternionMsg;
using RosTime = RosMessageTypes.BuiltinInterfaces.TimeMsg;

public class handPosePublisher : MonoBehaviour
{
    private const float MissingLeftHandLogInterval = 2f;
    private const float PoseLookupLogInterval = 2f;
    private const float PalmPoseWorldDiagnosticInterval = 1f;
    private const string PalmPoseWorldTopic = "/palm_pose_world";
    private const string PalmPoseWorldDiagTopic = "/palm_pose_world_diag";
    private const string PalmPoseWorldDiagFrameId = "palm_pose_world_diag";

    [SerializeField] private string topicName = "/palm_pose";
    [SerializeField] private string frameId = "amir_base";
    [SerializeField] private string hmdRelativeTopicName = "/palm_pose_hmd_relative";
    [SerializeField] private string hmdRelativeFrameId = "hmd";
    [SerializeField] private string worldTopicName = "/palm_pose_world";
    [SerializeField] private string worldFrameId = "unity_world";
    [SerializeField, Min(0.1f)] private float publishHz = 30f;
    [SerializeField, Min(0f)] private float lostThresholdSec = 1.0f;
    [SerializeField] private bool useRosCoordinateConversion = true;

    public handTracking handTracking;
    public int publishCount;
    public int hmdRelativePublishCount;
    public int worldPosePublishCount;
    public Vector3 lastPalmPosition;
    public Vector3 lastPalmHmdDelta;
    public Vector3 lastPalmWorldPosition;
    public string lastFrameId;
    public bool registered;
    public bool hmdRelativeRegistered;
    public bool worldRegistered;
    public bool rosReady;
    public float lastPublishTime;
    public bool hasPalmHmdAnchor;
    public bool publishHandPoseCalled;
    public bool publishWorldPoseCalled;
    public int palmPoseWorldDiagPublishCount;
    public string leftPalmFailReason = "unknown";

    private ROSConnection ros;
    private RosMessageTypes.Geometry.PoseStampedMsg message;
    private RosMessageTypes.Geometry.PoseStampedMsg hmdRelativeMessage;
    private RosMessageTypes.Geometry.PoseStampedMsg worldMessage;
    private RosMessageTypes.Geometry.PoseStampedMsg palmPoseWorldDiagMessage;
    private string registeredTopic;
    private string registeredHmdRelativeTopic;
    private string registeredWorldTopic;
    private string registeredPalmPoseWorldDiagTopic;
    private float nextPublishTime;
    private float nextPalmPoseWorldDiagPublishTime;
    private float nextMissingLeftHandLogTime;
    private float nextPoseLookupLogTime;
    private float nextPalmPoseWorldHeartbeatLogTime;
    private float nextPalmPoseWorldSkipLogTime;
    private float nextPalmPoseWorldPublishHandPoseHeartbeatLogTime;
    private float nextPalmPoseWorldTickLogTime;
    private float nextPalmPoseWorldTryGetFailureLogTime;
    private bool loggedLeftHandOnlyMode;
    private bool loggedPalmPoseWorldActive;
    private bool loggedRecalibrationRequired;
    private bool hasLastPalmWorldPosition;
    private bool isLeftPalmTracked;
    private bool hasAbsoluteCalibrationCenter;
    private bool recalibrationRequired;
    private float leftPalmLostStartTime = -1f;
    private float leftPalmLostDurationSec;
    private Vector3 palmHmdAnchorLocal;
    private Transform cachedHmdTransform;
    private Coroutine palmPoseWorldDiagCoroutine;

    public bool HasLastPalmWorldPosition => hasLastPalmWorldPosition;
    public Vector3 LastPalmWorldPosition => lastPalmWorldPosition;
    public bool IsLeftPalmTracked => isLeftPalmTracked;
    public float LeftPalmLostDurationSec => leftPalmLostDurationSec;
    public bool RecalibrationRequired => recalibrationRequired;
    public bool HasAbsoluteCalibrationCenter => hasAbsoluteCalibrationCenter;
    public bool IsWorldPosePublishing => isActiveAndEnabled && worldPosePublishCount > 0;
    public int WorldPublishCount => worldPosePublishCount;
    public bool IsPublisherComponentEnabled => isActiveAndEnabled;

    private void Awake()
    {
        EnsurePalmPoseWorldTopicName();
        LogPalmPoseWorldActiveOnce();
        InitializeMessage();
        EnsurePublisher();
        EnsurePalmPoseWorldDiagCoroutine();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        EnsurePalmPoseWorldTopicName();
        Debug.Log("[PalmPoseWorld] handPosePublisher enabled");
        Debug.Log("[handPosePublisher] OnEnable");
        LogLeftHandOnlyModeOnce();
        InitializeMessage();
        EnsurePublisher();
        EnsurePalmPoseWorldDiagCoroutine();
        nextPublishTime = 0f;
    }

    private void OnDisable()
    {
        if (!Application.isPlaying) return;

        Debug.Log("[PalmPoseWorld] handPosePublisher disabled");
    }

    private void OnDestroy()
    {
        if (palmPoseWorldDiagCoroutine != null)
        {
            StopCoroutine(palmPoseWorldDiagCoroutine);
            palmPoseWorldDiagCoroutine = null;
        }
    }

    private void Start()
    {
        EnsurePalmPoseWorldTopicName();
        LogPalmPoseWorldActiveOnce();
        LogLeftHandOnlyModeOnce();
        InitializeMessage();
        EnsurePublisher();
        EnsurePalmPoseWorldDiagCoroutine();
    }

    private void FixedUpdate()
    {
        if (Time.time < nextPublishTime) return;

        float interval = 1f / Mathf.Max(0.1f, publishHz);
        nextPublishTime = Time.time + interval;

        PublishHandPose();
    }

    private void Update()
    {
        PublishPalmPoseWorldDiag();
    }

    private void EnsurePalmPoseWorldDiagCoroutine()
    {
        if (!Application.isPlaying || palmPoseWorldDiagCoroutine != null)
        {
            return;
        }

        palmPoseWorldDiagCoroutine = StartCoroutine(PublishPalmPoseWorldDiagLoop());
    }

    private IEnumerator PublishPalmPoseWorldDiagLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(PalmPoseWorldDiagnosticInterval);
        while (true)
        {
            PublishPalmPoseWorldDiag();
            yield return wait;
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(topicName)) topicName = "/palm_pose";
        if (string.IsNullOrWhiteSpace(frameId)) frameId = "amir_base";
        if (string.IsNullOrWhiteSpace(hmdRelativeTopicName)) hmdRelativeTopicName = "/palm_pose_hmd_relative";
        if (string.IsNullOrWhiteSpace(hmdRelativeFrameId)) hmdRelativeFrameId = "hmd";
        EnsurePalmPoseWorldTopicName();
        if (string.IsNullOrWhiteSpace(worldFrameId)) worldFrameId = "unity_world";
        publishHz = Mathf.Max(0.1f, publishHz);
        lostThresholdSec = Mathf.Max(0f, lostThresholdSec);
    }

    private void InitializeMessage()
    {
        message = new RosMessageTypes.Geometry.PoseStampedMsg
        {
            header = new RosHeader(),
            pose = new RosPose
            {
                position = new RosPoint(),
                orientation = new RosQuaternion()
            }
        };

        hmdRelativeMessage = new RosMessageTypes.Geometry.PoseStampedMsg
        {
            header = new RosHeader(),
            pose = new RosPose
            {
                position = new RosPoint(),
                orientation = new RosQuaternion()
            }
        };

        worldMessage = new RosMessageTypes.Geometry.PoseStampedMsg
        {
            header = new RosHeader(),
            pose = new RosPose
            {
                position = new RosPoint(),
                orientation = new RosQuaternion()
            }
        };

        palmPoseWorldDiagMessage = new RosMessageTypes.Geometry.PoseStampedMsg
        {
            header = new RosHeader(),
            pose = new RosPose
            {
                position = new RosPoint(),
                orientation = new RosQuaternion()
            }
        };
    }

    private void EnsurePublisher()
    {
        if (!Application.isPlaying) return;

        EnsurePalmPoseWorldTopicName();
        ros ??= ROSConnection.GetOrCreateInstance();
        rosReady = ros != null;
        if (!rosReady) return;

        if (registeredTopic != topicName)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(topicName);
            registeredTopic = topicName;
            registered = true;
            Debug.Log("[handPosePublisher] RegisterPublisher " + topicName);
        }
        else
        {
            registered = true;
        }

        if (!string.IsNullOrWhiteSpace(hmdRelativeTopicName) && registeredHmdRelativeTopic != hmdRelativeTopicName)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(hmdRelativeTopicName);
            registeredHmdRelativeTopic = hmdRelativeTopicName;
            hmdRelativeRegistered = true;
            Debug.Log("[handPosePublisher] RegisterPublisher " + hmdRelativeTopicName);
        }
        else if (!string.IsNullOrWhiteSpace(hmdRelativeTopicName))
        {
            hmdRelativeRegistered = true;
        }

        if (registeredWorldTopic != PalmPoseWorldTopic)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(PalmPoseWorldTopic);
            registeredWorldTopic = PalmPoseWorldTopic;
            worldRegistered = true;
            Debug.Log("[PalmPoseWorld] publisher registered: " + PalmPoseWorldTopic);
        }
        else
        {
            worldRegistered = true;
        }

        if (registeredPalmPoseWorldDiagTopic != PalmPoseWorldDiagTopic)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(PalmPoseWorldDiagTopic);
            registeredPalmPoseWorldDiagTopic = PalmPoseWorldDiagTopic;
            Debug.Log("[PalmPoseWorldDiag] publisher registered: " + PalmPoseWorldDiagTopic);
        }
    }

    private void PublishHandPose()
    {
        publishHandPoseCalled = true;
        LogPalmPoseWorldPublishHandPoseHeartbeat();
        EnsurePublisher();
        RosTime stamp = GetRosTime();
        PublishWorldPose(stamp);

        if (handTracking == null)
        {
            LogPublishSkipped("handTracking reference is not assigned");
            return;
        }

        if (!TryConfirmLeftHandTracked())
        {
            return;
        }

        if (ros == null) return;
        if (message == null) InitializeMessage();

        Vector3 pose = handTracking.GetHandPositionFromOrigin();
        Quaternion rotation = handTracking.GetHandRotationFromOrigin();

        message.header.stamp = stamp;
        message.header.frame_id = frameId;
        SetGeometryPoint(pose, message.pose.position);
        SetGeometryQuaternion(rotation, message.pose.orientation);

        ros.Publish(topicName, message);
        PublishHmdRelativePose(stamp);
        publishCount++;
        lastPalmPosition = pose;
        lastFrameId = frameId;
        lastPublishTime = Time.time;
        if (publishCount <= 5 || publishCount % 30 == 0)
        {
            Debug.Log("[handPosePublisher] Published count=" + publishCount);
        }
    }

    public bool ResetPalmHmdAnchor()
    {
        if (!TryGetPalmHmdLocal(out Vector3 palmHmdLocal))
        {
            Debug.LogWarning("[handPosePublisher] Failed to reset HMD-relative palm anchor.");
            return false;
        }

        palmHmdAnchorLocal = palmHmdLocal;
        hasPalmHmdAnchor = true;
        Debug.Log("[handPosePublisher] Reset HMD-relative palm anchor: " + palmHmdAnchorLocal.ToString("F3"));
        return true;
    }

    private void PublishHmdRelativePose(RosTime stamp)
    {
        if (string.IsNullOrWhiteSpace(hmdRelativeTopicName))
        {
            return;
        }

        if (hmdRelativeMessage == null)
        {
            InitializeMessage();
        }

        if (!TryGetPalmHmdLocal(out Vector3 palmHmdLocal))
        {
            return;
        }

        if (!hasPalmHmdAnchor)
        {
            palmHmdAnchorLocal = palmHmdLocal;
            hasPalmHmdAnchor = true;
            Debug.Log("[handPosePublisher] Initialized HMD-relative palm anchor from first publish sample.");
        }

        Vector3 palmHmdDelta = palmHmdLocal - palmHmdAnchorLocal;
        hmdRelativeMessage.header.stamp = stamp;
        hmdRelativeMessage.header.frame_id = hmdRelativeFrameId;
        hmdRelativeMessage.pose.position.x = palmHmdDelta.x;
        hmdRelativeMessage.pose.position.y = palmHmdDelta.y;
        hmdRelativeMessage.pose.position.z = palmHmdDelta.z;
        hmdRelativeMessage.pose.orientation.x = 0.0;
        hmdRelativeMessage.pose.orientation.y = 0.0;
        hmdRelativeMessage.pose.orientation.z = 0.0;
        hmdRelativeMessage.pose.orientation.w = 1.0;

        ros.Publish(hmdRelativeTopicName, hmdRelativeMessage);
        hmdRelativePublishCount++;
        lastPalmHmdDelta = palmHmdDelta;
        if (hmdRelativePublishCount <= 5 || hmdRelativePublishCount % 30 == 0)
        {
            Debug.Log("[handPosePublisher] Published " + hmdRelativeTopicName
                + " count=" + hmdRelativePublishCount
                + " delta=" + palmHmdDelta.ToString("F3"));
        }
    }

    private void PublishWorldPose(RosTime stamp)
    {
        publishWorldPoseCalled = true;
        LogPalmPoseWorldTick();
        EnsurePalmPoseWorldTopicName();
        EnsurePublisher();
        if (ros == null)
        {
            return;
        }

        if (worldMessage == null)
        {
            InitializeMessage();
        }

        if (!TryGetLeftPalmWorldPosition(out Vector3 palmWorldPosition, PalmPoseWorldTopic))
        {
            return;
        }

        worldMessage.header.stamp = stamp;
        worldMessage.header.frame_id = worldFrameId;
        worldMessage.pose.position.x = palmWorldPosition.x;
        worldMessage.pose.position.y = palmWorldPosition.y;
        worldMessage.pose.position.z = palmWorldPosition.z;
        worldMessage.pose.orientation.x = 0.0;
        worldMessage.pose.orientation.y = 0.0;
        worldMessage.pose.orientation.z = 0.0;
        worldMessage.pose.orientation.w = 1.0;

        ros.Publish(PalmPoseWorldTopic, worldMessage);
        worldPosePublishCount++;
        lastPalmWorldPosition = palmWorldPosition;
        hasLastPalmWorldPosition = true;
        if (worldPosePublishCount <= 5)
        {
            Debug.Log("[PalmPoseWorld] LEFT published pos=" + palmWorldPosition.ToString("F3"));
        }

        LogPalmPoseWorldHeartbeat(palmWorldPosition);
    }

    private void PublishPalmPoseWorldDiag()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsurePublisher();
        if (Time.time < nextPalmPoseWorldDiagPublishTime)
        {
            return;
        }

        nextPalmPoseWorldDiagPublishTime = Time.time + PalmPoseWorldDiagnosticInterval;
        if (ros == null)
        {
            return;
        }

        if (palmPoseWorldDiagMessage == null)
        {
            InitializeMessage();
        }

        palmPoseWorldDiagMessage.header.stamp = GetRosTime();
        palmPoseWorldDiagMessage.header.frame_id = PalmPoseWorldDiagFrameId;
        palmPoseWorldDiagMessage.pose.position.x = publishHandPoseCalled ? 1.0 : 0.0;
        palmPoseWorldDiagMessage.pose.position.y = publishWorldPoseCalled ? 1.0 : 0.0;
        palmPoseWorldDiagMessage.pose.position.z = isLeftPalmTracked ? 1.0 : 0.0;
        palmPoseWorldDiagMessage.pose.orientation.x = recalibrationRequired ? 1.0 : 0.0;
        palmPoseWorldDiagMessage.pose.orientation.y = isActiveAndEnabled ? 1.0 : 0.0;
        palmPoseWorldDiagMessage.pose.orientation.z = ros != null ? 1.0 : 0.0;
        palmPoseWorldDiagMessage.pose.orientation.w = worldPosePublishCount;

        ros.Publish(PalmPoseWorldDiagTopic, palmPoseWorldDiagMessage);
        palmPoseWorldDiagPublishCount++;
    }

    public bool TryGetCurrentLeftPalmWorldPosition(out Vector3 palmWorldPosition)
    {
        EnsurePalmPoseWorldTopicName();
        if (!TryGetLeftPalmWorldPosition(out palmWorldPosition, PalmPoseWorldTopic))
        {
            return false;
        }

        lastPalmWorldPosition = palmWorldPosition;
        hasLastPalmWorldPosition = true;
        return true;
    }

    public void MarkAbsoluteCalibrationCalibrated()
    {
        hasAbsoluteCalibrationCenter = true;
        recalibrationRequired = false;
        loggedRecalibrationRequired = false;
        leftPalmLostStartTime = isLeftPalmTracked ? -1f : Time.time;
        leftPalmLostDurationSec = 0f;
        Debug.Log("[PalmPoseWorld] absolute calibration valid after Set Center.");
    }

    private void EnsurePalmPoseWorldTopicName()
    {
        string normalizedTopic = string.IsNullOrWhiteSpace(worldTopicName)
            ? PalmPoseWorldTopic
            : worldTopicName.Trim();

        if (!normalizedTopic.StartsWith("/"))
        {
            normalizedTopic = "/" + normalizedTopic;
        }

        if (normalizedTopic != PalmPoseWorldTopic)
        {
            Debug.LogWarning("[PalmPoseWorld] world topic was '" + worldTopicName
                + "' and was corrected to " + PalmPoseWorldTopic + ".");
            normalizedTopic = PalmPoseWorldTopic;
        }

        worldTopicName = normalizedTopic;
    }

    private void LogPalmPoseWorldActiveOnce()
    {
        if (loggedPalmPoseWorldActive)
        {
            return;
        }

        loggedPalmPoseWorldActive = true;
        Debug.Log("[BuildCheck] absolute palm world publisher build active");
    }

    private void LogPalmPoseWorldLeftSkipped()
    {
        if (!Application.isPlaying || Time.time < nextPalmPoseWorldSkipLogTime)
        {
            return;
        }

        nextPalmPoseWorldSkipLogTime = Time.time + PalmPoseWorldDiagnosticInterval;
        Debug.LogWarning("[PalmPoseWorld] LEFT publish skipped: left palm not tracked");
    }

    private void LogPalmPoseWorldHeartbeat(Vector3 palmWorldPosition)
    {
        if (!Application.isPlaying || Time.time < nextPalmPoseWorldHeartbeatLogTime)
        {
            return;
        }

        nextPalmPoseWorldHeartbeatLogTime = Time.time + PalmPoseWorldDiagnosticInterval;
        if (recalibrationRequired)
        {
            Debug.Log("[PalmPoseWorld] LEFT publish heartbeat while recalibration required pos="
                + palmWorldPosition.ToString("F3"));
            return;
        }

        Debug.Log("[PalmPoseWorld] LEFT publish heartbeat pos=" + palmWorldPosition.ToString("F3"));
    }

    private void LogPalmPoseWorldPublishHandPoseHeartbeat()
    {
        if (!Application.isPlaying || Time.time < nextPalmPoseWorldPublishHandPoseHeartbeatLogTime)
        {
            return;
        }

        nextPalmPoseWorldPublishHandPoseHeartbeatLogTime = Time.time + PalmPoseWorldDiagnosticInterval;
        Debug.Log("[PalmPoseWorld] PublishHandPose heartbeat");
    }

    private void LogPalmPoseWorldTick()
    {
        if (!Application.isPlaying || Time.time < nextPalmPoseWorldTickLogTime)
        {
            return;
        }

        nextPalmPoseWorldTickLogTime = Time.time + PalmPoseWorldDiagnosticInterval;
        Debug.Log("[PalmPoseWorld] PublishWorldPose tick");
    }

    private void LogPalmPoseWorldTryGetFailed(string logTopicName, string reason)
    {
        if (logTopicName != PalmPoseWorldTopic || !Application.isPlaying || Time.time < nextPalmPoseWorldTryGetFailureLogTime)
        {
            return;
        }

        nextPalmPoseWorldTryGetFailureLogTime = Time.time + PalmPoseWorldDiagnosticInterval;
        Debug.LogWarning("[PalmPoseWorld] TryGetLeftPalm failed: " + reason);
    }

    private bool TryConfirmLeftHandTracked()
    {
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null)
        {
            RecordLeftPalmTracking(false);
            LogPublishSkipped("HandsAggregator is not available; RightHand fallback is disabled");
            return false;
        }

        if (!aggregator.TryGetJoint(TrackedHandJoint.Palm, XRNode.LeftHand, out _))
        {
            RecordLeftPalmTracking(false);
            LogPublishSkipped("LeftHand Palm tracking is unavailable; RightHand fallback is disabled");
            return false;
        }

        RecordLeftPalmTracking(true);
        return true;
    }

    private bool TryGetPalmHmdLocal(out Vector3 palmHmdLocal)
    {
        palmHmdLocal = Vector3.zero;

        if (!TryGetLeftPalmWorldPosition(out Vector3 palmWorldPosition, hmdRelativeTopicName))
        {
            return false;
        }

        Transform hmdTransform = GetHmdTransform();
        if (hmdTransform == null)
        {
            LogPoseTopicSkipped(hmdRelativeTopicName, "HMD transform was not found");
            return false;
        }

        palmHmdLocal = hmdTransform.InverseTransformPoint(palmWorldPosition);
        return true;
    }

    private bool TryGetLeftPalmWorldPosition(out Vector3 palmWorldPosition)
    {
        return TryGetLeftPalmWorldPosition(out palmWorldPosition, hmdRelativeTopicName);
    }

    private bool TryGetLeftPalmWorldPosition(out Vector3 palmWorldPosition, string logTopicName)
    {
        palmWorldPosition = Vector3.zero;

        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null)
        {
            SetLeftPalmFailed("subsystem not found");
            RecordLeftPalmTracking(false);
            LogPalmPoseWorldTryGetFailed(logTopicName, "subsystem not found");
            LogPoseTopicSkipped(logTopicName, "HandsAggregator is not available; RightHand fallback is disabled");
            return false;
        }

        if (!aggregator.TryGetJoint(TrackedHandJoint.Palm, XRNode.LeftHand, out HandJointPose palmPose))
        {
            string reason = GetLeftPalmFailureReason(aggregator);
            SetLeftPalmFailed(reason);
            RecordLeftPalmTracking(false);
            LogPalmPoseWorldTryGetFailed(logTopicName, reason);
            LogPoseTopicSkipped(logTopicName, "LeftHand Palm is not tracked; RightHand fallback is disabled");
            return false;
        }

        RecordLeftPalmTracking(true);
        palmWorldPosition = palmPose.Position;
        SetLeftPalmSucceeded(palmWorldPosition);
        return true;
    }

    private void SetLeftPalmSucceeded(Vector3 palmWorldPosition)
    {
        isLeftPalmTracked = true;
        leftPalmFailReason = "none";
        lastPalmWorldPosition = palmWorldPosition;
        hasLastPalmWorldPosition = true;
    }

    private void SetLeftPalmFailed(string reason)
    {
        isLeftPalmTracked = false;
        leftPalmFailReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    private string GetLeftPalmFailureReason(IHandsAggregatorSubsystem aggregator)
    {
        if (aggregator == null)
        {
            return "subsystem not found";
        }

        System.Collections.Generic.IReadOnlyList<HandJointPose> leftHandJoints;
        if (!aggregator.TryGetEntireHand(XRNode.LeftHand, out leftHandJoints))
        {
            return "left hand not tracked";
        }

        return "palm joint not available";
    }

    private void RecordLeftPalmTracking(bool tracked)
    {
        if (tracked)
        {
            isLeftPalmTracked = true;
            leftPalmLostStartTime = -1f;
            leftPalmLostDurationSec = 0f;
            return;
        }

        if (leftPalmLostStartTime < 0f)
        {
            leftPalmLostStartTime = Time.time;
        }

        isLeftPalmTracked = false;
        leftPalmLostDurationSec = Mathf.Max(0f, Time.time - leftPalmLostStartTime);

        if (!hasAbsoluteCalibrationCenter || recalibrationRequired || leftPalmLostDurationSec < lostThresholdSec)
        {
            return;
        }

        recalibrationRequired = true;
        if (!loggedRecalibrationRequired)
        {
            loggedRecalibrationRequired = true;
            Debug.LogWarning("[PalmPoseWorld] absolute calibration invalid: left palm tracking lost for "
                + leftPalmLostDurationSec.ToString("F2") + " sec. Recalibration Required.");
        }
    }

    private Transform GetHmdTransform()
    {
        if (cachedHmdTransform != null && cachedHmdTransform.gameObject.activeInHierarchy)
        {
            return cachedHmdTransform;
        }

        cachedHmdTransform = FindHmdTransform();
        return cachedHmdTransform;
    }

    private Transform FindHmdTransform()
    {
        Transform centerEyeAnchor = FindSceneTransformByName("CenterEyeAnchor");
        if (centerEyeAnchor != null)
        {
            return centerEyeAnchor;
        }

        Transform mainCameraByName = FindSceneTransformByName("MainCamera");
        if (mainCameraByName != null)
        {
            return mainCameraByName;
        }

        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        Transform hmdByName = FindSceneTransformByName("HMD");
        if (hmdByName != null)
        {
            return hmdByName;
        }

        Transform xrOrigin = FindSceneTransformByName("XROrigin") ?? FindSceneTransformByName("XR Origin");
        if (xrOrigin != null)
        {
            Camera xrCamera = xrOrigin.GetComponentInChildren<Camera>(true);
            if (xrCamera != null)
            {
                return xrCamera.transform;
            }
        }

        Transform ovrCameraRig = FindSceneTransformByName("OVRCameraRig");
        if (ovrCameraRig != null)
        {
            Transform ovrCenterEye = FindChildTransformByName(ovrCameraRig, "CenterEyeAnchor");
            if (ovrCenterEye != null)
            {
                return ovrCenterEye;
            }

            Camera ovrCamera = ovrCameraRig.GetComponentInChildren<Camera>(true);
            if (ovrCamera != null)
            {
                return ovrCamera.transform;
            }
        }

        Camera[] cameras = FindObjectsOfType<Camera>(true);
        return cameras.Length > 0 ? cameras[0].transform : null;
    }

    private static Transform FindSceneTransformByName(string objectName)
    {
        foreach (Transform candidate in FindObjectsOfType<Transform>(true))
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
            {
                return candidate;
            }
        }

        return null;
    }

    private static Transform FindChildTransformByName(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }

    private void LogLeftHandOnlyModeOnce()
    {
        if (loggedLeftHandOnlyMode)
        {
            return;
        }

        loggedLeftHandOnlyMode = true;
        Debug.Log("[handPosePublisher] /palm_pose uses XRNode.LeftHand only. RightHand fallback is disabled.");
    }

    private void LogPublishSkipped(string reason)
    {
        if (!Application.isPlaying || Time.time < nextMissingLeftHandLogTime)
        {
            return;
        }

        nextMissingLeftHandLogTime = Time.time + MissingLeftHandLogInterval;
        Debug.Log("[handPosePublisher] Skip /palm_pose publish: " + reason + ".");
    }

    private void LogPoseTopicSkipped(string logTopicName, string reason)
    {
        if (logTopicName == PalmPoseWorldTopic)
        {
            LogPalmPoseWorldLeftSkipped();
            return;
        }

        if (!Application.isPlaying || Time.time < nextPoseLookupLogTime)
        {
            return;
        }

        nextPoseLookupLogTime = Time.time + PoseLookupLogInterval;
        Debug.Log("[handPosePublisher] Skip " + logTopicName + " publish: " + reason + ".");
    }

    private static RosTime GetRosTime()
    {
        double time = Time.realtimeSinceStartup;
        int wholeSeconds = Mathf.FloorToInt((float)time);
        uint nanoseconds = (uint)((time - wholeSeconds) * 1000000000.0);

#if ROS2
        int seconds = wholeSeconds;
#else
        uint seconds = (uint)wholeSeconds;
#endif

        return new RosTime
        {
            sec = seconds,
            nanosec = nanoseconds
        };
    }

    private void SetGeometryPoint(Vector3 position, RosPoint geometryPoint)
    {
        if (useRosCoordinateConversion)
        {
            geometryPoint.x = -position.x;
            geometryPoint.y = position.z;
            geometryPoint.z = position.y;
            return;
        }

        geometryPoint.x = position.x;
        geometryPoint.y = position.y;
        geometryPoint.z = position.z;
    }

    private void SetGeometryQuaternion(Quaternion quaternion, RosQuaternion geometryQuaternion)
    {
        if (useRosCoordinateConversion)
        {
            geometryQuaternion.x = quaternion.z;
            geometryQuaternion.y = -quaternion.x;
            geometryQuaternion.z = quaternion.y;
            geometryQuaternion.w = -quaternion.w;
            return;
        }

        geometryQuaternion.x = quaternion.x;
        geometryQuaternion.y = quaternion.y;
        geometryQuaternion.z = quaternion.z;
        geometryQuaternion.w = quaternion.w;
    }
}
