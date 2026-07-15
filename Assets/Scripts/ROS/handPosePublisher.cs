using System.Collections;
using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit.Subsystems;
using TMPro;
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
    private const string PrimaryPalmPoseTopic = "/palm_pose";

    [SerializeField] private string topicName = PrimaryPalmPoseTopic;
    [SerializeField] private string frameId = "amir_base";
    [SerializeField] private string hmdRelativeTopicName = "/palm_pose_hmd_relative";
    [SerializeField] private string hmdRelativeFrameId = "hmd";
    [SerializeField] private string worldTopicName = "/palm_pose_world";
    [SerializeField] private string worldFrameId = "unity_world";
    [SerializeField, Min(0.1f)] private float publishHz = 30f;
    [SerializeField, Min(0f)] private float lostThresholdSec = 1.0f;
    [SerializeField] private bool useRosCoordinateConversion = true;
    [SerializeField] private bool publishEnabled;
    [SerializeField, Min(0.1f)] private float publishStartDelaySec = 3.0f;
    [SerializeField, Min(1f)] private float controlPublishRateHz = 60.0f;
    [SerializeField, Min(1f)] private float diagnosticPublishRateHz = 10.0f;
    [SerializeField] private Transform hmdTransform;
    [SerializeField] private GameObject publishStartGuideSpherePrefab;
    [SerializeField, Min(0.01f)] private float publishStartGuideSphereDiameterM = 0.05f;
    [SerializeField] private GameObject publishStartCountdownPrefab;
    [SerializeField, Min(0.1f)] private float countdownDistanceFromHmdM = 0.60f;
    [SerializeField] private Vector3 countdownLocalOffset = new Vector3(0f, -0.12f, 0f);

    public static handPosePublisher ActiveInstance { get; private set; }
    public static handPosePublisher LastPublishCandidate { get; private set; }

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
    private string resolvedTopicName;
    private string resolvedHmdRelativeTopicName;
    private string resolvedWorldTopicName;
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
    private bool loggedMultipleInstanceWarning;
    private readonly HashSet<string> loggedPublishDisabledTopics = new HashSet<string>();
    private bool hasLastPalmWorldPosition;
    private bool isLeftPalmTracked;
    private bool hasAbsoluteCalibrationCenter;
    private bool recalibrationRequired;
    private float leftPalmLostStartTime = -1f;
    private float leftPalmLostDurationSec;
    private Vector3 palmHmdAnchorLocal;
    private Transform cachedHmdTransform;
    private Coroutine palmPoseWorldDiagCoroutine;
    private Coroutine publishStartDelayCoroutine;
    private GameObject publishStartGuideSphere;
    private GameObject publishStartCountdown;
    private TMP_Text publishStartCountdownText;

    public bool HasLastPalmWorldPosition => hasLastPalmWorldPosition;
    public Vector3 LastPalmWorldPosition => lastPalmWorldPosition;
    public bool IsLeftPalmTracked => isLeftPalmTracked;
    public float LeftPalmLostDurationSec => leftPalmLostDurationSec;
    public bool RecalibrationRequired => recalibrationRequired;
    public bool HasAbsoluteCalibrationCenter => hasAbsoluteCalibrationCenter;
    public bool IsWorldPosePublishing => isActiveAndEnabled && worldPosePublishCount > 0;
    public int WorldPublishCount => worldPosePublishCount;
    public bool IsPublisherComponentEnabled => isActiveAndEnabled;
    public bool IsPublishStartPending { get; private set; }
    public bool PublishEnabled
    {
        get => publishEnabled;
        set => SetPublishEnabled(value);
    }

    public void SetPublishEnabled(bool enabled)
    {
        bool changed = publishEnabled != enabled;
        publishEnabled = enabled;
        if (publishEnabled && !this.enabled)
        {
            this.enabled = true;
        }

        if (changed)
        {
            loggedPublishDisabledTopics.Clear();
        }

        Debug.Log("[handPosePublisher] PublishEnabled changed:"
            + "\nsender=" + name
            + "\npath=" + GetHierarchyPath(transform)
            + "\nprimaryTopic=" + GetPrimaryPalmPoseTopicName()
            + "\nvalue=" + publishEnabled
            + "\nlastCandidate=" + (LastPublishCandidate != null ? LastPublishCandidate.name : "<null>"));
    }

    public void TogglePublishEnabled()
    {
        SetPublishEnabled(!publishEnabled);
    }

    public void BeginLeftPalmPosePublishWithDelay()
    {
        if (!Application.isPlaying)
        {
            SetPublishEnabled(true);
            return;
        }

        if (!this.enabled)
        {
            this.enabled = true;
        }

        StopPublishStartDelayCoroutine();
        SetPublishEnabled(false);
        loggedPublishDisabledTopics.Clear();

        if (!TryResolvePublishStartHmdTransform(out Transform startHmdTransform))
        {
            IsPublishStartPending = false;
            Debug.LogError("[handPosePublisher] Cannot begin PalmPose publish start delay: HMD transform was not found. "
                + "sender=" + name
                + " path=" + GetHierarchyPath(transform));
            return;
        }

        IsPublishStartPending = true;
        Vector3 guidePosition = GetPublishStartGuidePosition(startHmdTransform);
        ShowPublishStartGuideSphere(guidePosition);
        ShowPublishStartCountdown(startHmdTransform);
        UpdatePublishStartCountdown(publishStartDelaySec);
        Debug.Log("[handPosePublisher] PalmPose publish start pending:"
            + "\nsender=" + name
            + "\npath=" + GetHierarchyPath(transform)
            + "\ndelaySec=" + publishStartDelaySec.ToString("F1")
            + "\nguidePosition=" + guidePosition.ToString("F3"));

        publishStartDelayCoroutine = StartCoroutine(BeginLeftPalmPosePublishAfterDelay());
    }

    public void StopLeftPalmPosePublish()
    {
        StopPublishStartDelayCoroutine();
        IsPublishStartPending = false;
        SetPublishEnabled(false);
        loggedPublishDisabledTopics.Clear();
        HidePublishStartGuideSphere();
        HidePublishStartCountdown();
        Debug.Log("[handPosePublisher] PalmPose publish stopped:"
            + "\nsender=" + name
            + "\npath=" + GetHierarchyPath(transform));
    }

    private void Awake()
    {
        RegisterActiveInstance();
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

        StopPublishStartDelayCoroutine();
        HidePublishStartGuideSphere();
        HidePublishStartCountdown();
    }

    private void RegisterActiveInstance()
    {
        if (ActiveInstance == null)
        {
            ActiveInstance = this;
            Debug.Log("[handPosePublisher] ActiveInstance registered: " + name
                + " path=" + GetHierarchyPath(transform));
            if (FindObjectsOfType<handPosePublisher>(true).Length > 1)
            {
                LogMultipleInstanceWarning();
            }

            return;
        }

        LogMultipleInstanceWarning();
    }

    private void LogMultipleInstanceWarning()
    {
        if (loggedMultipleInstanceWarning)
        {
            return;
        }

        loggedMultipleInstanceWarning = true;
        Debug.LogWarning("[handPosePublisher] Multiple handPosePublisher instances detected: "
            + "self=" + name
            + " selfPath=" + GetHierarchyPath(transform)
            + " selfEnabled=" + enabled
            + " selfActiveInHierarchy=" + gameObject.activeInHierarchy
            + " active=" + (ActiveInstance != null ? ActiveInstance.name : "<null>")
            + " activePath=" + (ActiveInstance != null ? GetHierarchyPath(ActiveInstance.transform) : "<null>")
            + " activeEnabled=" + (ActiveInstance != null ? ActiveInstance.enabled.ToString() : "<null>")
            + " activeInHierarchy=" + (ActiveInstance != null ? ActiveInstance.gameObject.activeInHierarchy.ToString() : "<null>"));
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

    private void Update()
    {
        if (Time.time < nextPublishTime)
        {
            return;
        }

        float interval = 1f / Mathf.Max(1f, controlPublishRateHz);
        nextPublishTime = Time.time + interval;
        PublishHandPose();
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
        while (true)
        {
            PublishPalmPoseWorldDiag();
            yield return new WaitForSecondsRealtime(1f / Mathf.Max(1f, diagnosticPublishRateHz));
        }
    }

    private IEnumerator BeginLeftPalmPosePublishAfterDelay()
    {
        float endTime = Time.unscaledTime + publishStartDelaySec;
        while (Time.unscaledTime < endTime)
        {
            UpdatePublishStartCountdown(Mathf.Max(0f, endTime - Time.unscaledTime));
            yield return null;
        }

        publishStartDelayCoroutine = null;
        HidePublishStartGuideSphere();
        HidePublishStartCountdown();
        IsPublishStartPending = false;
        nextPublishTime = 0f;
        SetPublishEnabled(true);
        Debug.Log("[handPosePublisher] PalmPose publish started:"
            + "\nsender=" + name
            + "\npath=" + GetHierarchyPath(transform)
            + "\nrateHz=" + controlPublishRateHz.ToString("F1"));
    }

    private void StopPublishStartDelayCoroutine()
    {
        if (publishStartDelayCoroutine == null)
        {
            return;
        }

        StopCoroutine(publishStartDelayCoroutine);
        publishStartDelayCoroutine = null;
    }

    private bool TryResolvePublishStartHmdTransform(out Transform resolvedHmdTransform)
    {
        resolvedHmdTransform = hmdTransform;
        if (resolvedHmdTransform != null)
        {
            return true;
        }

        if (Camera.main != null)
        {
            resolvedHmdTransform = Camera.main.transform;
            return true;
        }

        return false;
    }

    private Vector3 GetPublishStartGuidePosition(Transform startHmdTransform)
    {
        Vector3 horizontalForward = Vector3.ProjectOnPlane(startHmdTransform.forward, Vector3.up).normalized;
        if (horizontalForward.sqrMagnitude < 0.0001f)
        {
            horizontalForward = Vector3.forward;
        }

        return startHmdTransform.position
            + Vector3.down * 0.15f
            + horizontalForward * 0.10f;
    }

    private void ShowPublishStartGuideSphere(Vector3 guidePosition)
    {
        HidePublishStartGuideSphere();

        publishStartGuideSphere = publishStartGuideSpherePrefab != null
            ? Instantiate(publishStartGuideSpherePrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        publishStartGuideSphere.name = "PalmPosePublishStartGuide";
        publishStartGuideSphere.transform.position = guidePosition;
        publishStartGuideSphere.transform.localScale = Vector3.one * publishStartGuideSphereDiameterM;

        foreach (Collider guideCollider in publishStartGuideSphere.GetComponentsInChildren<Collider>(true))
        {
            guideCollider.enabled = false;
        }
    }

    private void HidePublishStartGuideSphere()
    {
        if (publishStartGuideSphere == null)
        {
            return;
        }

        Destroy(publishStartGuideSphere);
        publishStartGuideSphere = null;
    }

    private void ShowPublishStartCountdown(Transform countdownHmdTransform)
    {
        HidePublishStartCountdown();

        publishStartCountdown = publishStartCountdownPrefab != null
            ? Instantiate(publishStartCountdownPrefab)
            : CreateDefaultPublishStartCountdown();

        publishStartCountdown.name = "PalmPosePublishStartCountdown";
        publishStartCountdown.transform.SetParent(countdownHmdTransform, false);
        publishStartCountdown.transform.localPosition = countdownLocalOffset + Vector3.forward * countdownDistanceFromHmdM;
        publishStartCountdown.transform.localRotation = Quaternion.identity;
        publishStartCountdown.transform.localScale = publishStartCountdownPrefab != null
            ? publishStartCountdown.transform.localScale
            : Vector3.one * 0.001f;

        publishStartCountdownText = publishStartCountdown.GetComponentInChildren<TMP_Text>(true);
        if (publishStartCountdownText != null)
        {
            publishStartCountdownText.raycastTarget = false;
        }
    }

    private GameObject CreateDefaultPublishStartCountdown()
    {
        GameObject canvasObject = new GameObject("PalmPosePublishStartCountdownCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(420f, 160f);

        GameObject textObject = new GameObject("CountdownText");
        textObject.transform.SetParent(canvasObject.transform, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 38f;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return canvasObject;
    }

    private void UpdatePublishStartCountdown(float remainingSec)
    {
        if (publishStartCountdownText == null)
        {
            return;
        }

        publishStartCountdownText.text = "Left Hand Control Starts In\n" + remainingSec.ToString("F1") + " s";
    }

    private void HidePublishStartCountdown()
    {
        publishStartCountdownText = null;
        if (publishStartCountdown == null)
        {
            return;
        }

        Destroy(publishStartCountdown);
        publishStartCountdown = null;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(topicName)) topicName = PrimaryPalmPoseTopic;
        if (string.IsNullOrWhiteSpace(frameId)) frameId = "amir_base";
        if (string.IsNullOrWhiteSpace(hmdRelativeTopicName)) hmdRelativeTopicName = "/palm_pose_hmd_relative";
        if (string.IsNullOrWhiteSpace(hmdRelativeFrameId)) hmdRelativeFrameId = "hmd";
        EnsurePalmPoseWorldTopicName();
        if (string.IsNullOrWhiteSpace(worldFrameId)) worldFrameId = "unity_world";
        publishHz = Mathf.Max(0.1f, publishHz);
        lostThresholdSec = Mathf.Max(0f, lostThresholdSec);
        publishStartDelaySec = Mathf.Max(0.1f, publishStartDelaySec);
        controlPublishRateHz = Mathf.Max(1f, controlPublishRateHz);
        diagnosticPublishRateHz = Mathf.Max(1f, diagnosticPublishRateHz);
        publishStartGuideSphereDiameterM = Mathf.Max(0.01f, publishStartGuideSphereDiameterM);
        countdownDistanceFromHmdM = Mathf.Max(0.1f, countdownDistanceFromHmdM);
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

        if (!RosTopicProvider.TryResolveTopic(RosInputTopicKey.PalmPose, topicName, out resolvedTopicName, out _)
            || !RosTopicProvider.TryResolveTopic(RosInputTopicKey.PalmPoseHmdRelative, hmdRelativeTopicName, out resolvedHmdRelativeTopicName, out _)
            || !RosTopicProvider.TryResolveTopic(RosInputTopicKey.PalmPoseWorld, PalmPoseWorldTopic, out resolvedWorldTopicName, out _))
        {
            registered = false;
            hmdRelativeRegistered = false;
            worldRegistered = false;
            return;
        }

        if (registeredTopic != resolvedTopicName)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(resolvedTopicName);
            registeredTopic = resolvedTopicName;
            registered = true;
            Debug.Log("[handPosePublisher] RegisterPublisher " + resolvedTopicName);
        }
        else
        {
            registered = true;
        }

        if (!string.IsNullOrWhiteSpace(resolvedHmdRelativeTopicName) && registeredHmdRelativeTopic != resolvedHmdRelativeTopicName)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(resolvedHmdRelativeTopicName);
            registeredHmdRelativeTopic = resolvedHmdRelativeTopicName;
            hmdRelativeRegistered = true;
            Debug.Log("[handPosePublisher] RegisterPublisher " + resolvedHmdRelativeTopicName);
        }
        else if (!string.IsNullOrWhiteSpace(resolvedHmdRelativeTopicName))
        {
            hmdRelativeRegistered = true;
        }

        if (registeredWorldTopic != resolvedWorldTopicName)
        {
            ros.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(resolvedWorldTopicName);
            registeredWorldTopic = resolvedWorldTopicName;
            worldRegistered = true;
            Debug.Log("[PalmPoseWorld] publisher registered: " + resolvedWorldTopicName);
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
        if (string.IsNullOrWhiteSpace(resolvedTopicName)
            || !CanPublishLeftPalmPose(resolvedTopicName)
            || !RosTopicProvider.CanPublish(RosInputTopicKey.PalmPose, out _))
        {
            return;
        }

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

        ros.Publish(resolvedTopicName, message);
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

        if (string.IsNullOrWhiteSpace(resolvedHmdRelativeTopicName)
            || !CanPublishLeftPalmPose(resolvedHmdRelativeTopicName)
            || !RosTopicProvider.CanPublish(RosInputTopicKey.PalmPoseHmdRelative, out _))
        {
            return;
        }

        ros.Publish(resolvedHmdRelativeTopicName, hmdRelativeMessage);
        hmdRelativePublishCount++;
        lastPalmHmdDelta = palmHmdDelta;
        if (hmdRelativePublishCount <= 5 || hmdRelativePublishCount % 30 == 0)
        {
            Debug.Log("[handPosePublisher] Published " + resolvedHmdRelativeTopicName
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
        if (ros == null || string.IsNullOrWhiteSpace(resolvedWorldTopicName))
        {
            return;
        }

        if (worldMessage == null)
        {
            InitializeMessage();
        }

        if (!TryGetLeftPalmWorldPosition(out Vector3 palmWorldPosition, resolvedWorldTopicName))
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

        if (!CanPublishLeftPalmPose(resolvedWorldTopicName)
            || !RosTopicProvider.CanPublish(RosInputTopicKey.PalmPoseWorld, out _))
        {
            return;
        }

        ros.Publish(resolvedWorldTopicName, worldMessage);
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
        if (Time.unscaledTime < nextPalmPoseWorldDiagPublishTime)
        {
            return;
        }

        nextPalmPoseWorldDiagPublishTime = Time.unscaledTime + (1f / Mathf.Max(1f, diagnosticPublishRateHz));
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

        if (!CanPublishLeftPalmPose(PalmPoseWorldDiagTopic))
        {
            return;
        }

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

    private bool CanPublishLeftPalmPose(string topicName)
    {
        handPosePublisher previousCandidate = LastPublishCandidate;
        LastPublishCandidate = this;
        if (previousCandidate != this)
        {
            Debug.Log("[handPosePublisher] LastPublishCandidate updated: sender=" + name
                + " path=" + GetHierarchyPath(transform)
                + " topic=" + topicName);
        }

        if (publishEnabled && !IsPublishStartPending)
        {
            return true;
        }

        if (topicName == PalmPoseWorldDiagTopic)
        {
            return false;
        }

        if (Application.isPlaying && !loggedPublishDisabledTopics.Contains(topicName))
        {
            loggedPublishDisabledTopics.Add(topicName);
            Debug.Log("[handPosePublisher] Skip publish: sender=" + name
                + " path=" + GetHierarchyPath(transform)
                + " topic=" + topicName
                + " primaryTopic=" + GetPrimaryPalmPoseTopicName()
                + " PublishEnabled=" + publishEnabled
                + " pending=" + IsPublishStartPending);
        }

        return false;
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
        if (hmdTransform != null && hmdTransform.gameObject.activeInHierarchy)
        {
            return hmdTransform;
        }

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

    private string GetPrimaryPalmPoseTopicName()
    {
        return string.IsNullOrWhiteSpace(topicName) ? PrimaryPalmPoseTopic : topicName;
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return "<null>";
        }

        string path = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
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
