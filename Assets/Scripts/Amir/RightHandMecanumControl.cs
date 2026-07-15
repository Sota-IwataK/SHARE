using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using RosMessageTypes.Std;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class RightHandMecanumControl : MonoBehaviour
{
    private const float PublisherRegistrationSettleSeconds = 0.5f;
    private const bool ShowLegacyPopup = false;

    private enum MecanumState
    {
        IDLE,
        RIGHT_HAND_HOLDING,
        MOVE_READY_POPUP,
        MECANUM_CONTROL,
        HAND_OPEN_STOP
    }

    [Header("ROS")]
    [SerializeField] private string topicName = "/amir/right_hand_mecanum_input";
    [SerializeField] private int queueSize = 10;
    [SerializeField] private int publishHz = 30;

    [Header("Right Hand Input")]
    public Transform rightHandTrackingTransform;
    public OVRHand rightOvrHand;
    [SerializeField] private bool useOvrHand = true;
    [SerializeField] private bool useMrtkHandFallback = true;
    [SerializeField] private bool useControllerGripFallback = true;
    [SerializeField] private float ovrGripStrengthThreshold = 0.75f;
    [SerializeField] private float ovrGripReleaseThreshold = 0.55f;
    [SerializeField] private float controllerGripThreshold = 0.75f;
    [SerializeField] private float controllerGripReleaseThreshold = 0.55f;
    [SerializeField] private float mrtkFingerToPalmClosedDistance = 0.095f;
    [SerializeField] private float mrtkFingerToPalmOpenDistance = 0.120f;
    [SerializeField] private int mrtkClosedFingerCount = 3;

    [Header("State Timing")]
    [SerializeField] private float holdSeconds = 3.0f;
    [SerializeField] private float stopPublishSeconds = 0.3f;
    [SerializeField] private float handLostGraceSec = 0.4f;

    [Header("Diagnostics")]
    [SerializeField] private bool enableDiagnosticLog = true;
    [SerializeField] private float diagnosticLogIntervalSec = 0.5f;

    [Header("Output Clamp")]
    [SerializeField] private float maxDeltaX = 0.20f;
    [SerializeField] private float maxDeltaZ = 0.20f;
    [SerializeField] private float maxRollDeltaRad = 0.8f;

    [Header("Popup")]
    public Canvas popupCanvas;
    public TextMeshProUGUI popupText;
    [SerializeField] private Vector3 popupViewportOffset = new Vector3(0f, -0.08f, 0.8f);
    [SerializeField] private Vector2 popupSize = new Vector2(620f, 170f);
    [SerializeField] private float popupCanvasScale = 0.0018f;

    private const string MoveReadyText = "Move Ready";
    private const string ControlText = "Right Hand Rover Control";
    private const string StopText = "Stop";

    private MecanumState state = MecanumState.IDLE;
    private ROSConnection ros;
    private string registeredTopic;
    private string resolvedTopicName;
    private bool publisherRegistered;
    private float publisherReadyRealtime;
    private bool loggedPublishSkippedBeforeRegistration;
    private GameObject popupPanel;
    private bool ownsPopupCanvas;
    private bool legacyPopupDisabled;
    private Transform cachedCameraTransform;

    private Vector3 anchorPosition;
    private Quaternion anchorRotation = Quaternion.identity;
    private float holdStartTime;
    private float stopStartTime;
    private float nextPublishTime;
    private float nextDebugLogTime;

    private float lastHandSeenTime = -999f;
    private Vector3 lastValidHandPosition;
    private Quaternion lastValidHandRotation = Quaternion.identity;
    private bool hasLastValidHandPose;
    private bool gestureClosedLatched;
    private int publishCount;
    private float lastPublishInterval;
    private float publishHzWindowStartTime = -1f;
    private int publishHzWindowStartCount;
    private float measuredPublishHz;
    private string lastStateTransitionReason = "None";
    private string lastPublishSkipReason = "None";
    private float lastHoldTime;
    private bool lastActive;
    private float lastDeltaX;
    private float lastDeltaZ;
    private float lastRollDelta;
    private bool lastHandTracked;
    private bool lastInputPoseUsable;
    private bool lastCanPublish;
    private float lastPublishTime = -1f;

    public bool IsHandTracked => lastInputPoseUsable;
    public bool IsGestureHolding => state == MecanumState.RIGHT_HAND_HOLDING;
    public bool IsDriveActive => state == MecanumState.MECANUM_CONTROL;
    public bool IsCommandActive => lastActive;
    public bool IsPublishing => IsDriveActive
        && lastCanPublish
        && lastPublishTime >= 0f
        && Time.time - lastPublishTime <= Mathf.Max(0.25f, 1f / Mathf.Max(1, publishHz) * 2f);
    public float GestureHoldProgress01 => holdSeconds > 0f ? Mathf.Clamp01(lastHoldTime / holdSeconds) : 1f;
    public float ForwardInput => lastDeltaZ;
    public float StrafeInput => lastDeltaX;
    public float RotationInput => lastRollDelta;
    public string PublishTopic => topicName;
    public float LastPublishTime => lastPublishTime;
    public string CurrentStateText
    {
        get
        {
            if (!Application.isPlaying)
            {
                return "INITIALIZING";
            }

            if (!IsHandTracked)
            {
                return "HAND NOT DETECTED";
            }

            if (!lastCanPublish && (ros == null || !publisherRegistered || ros.HasConnectionError))
            {
                return "NOT READY";
            }

            if (IsPublishing)
            {
                return "PUBLISHING";
            }

            if (IsDriveActive)
            {
                return "ACTIVE";
            }

            if (IsGestureHolding)
            {
                return "HOLD TO ACTIVATE";
            }

            return "READY";
        }
    }

    private void Awake()
    {
        if (Application.isPlaying)
        {
            EnsurePublisher();
        }
    }

    private void Start()
    {
        EnsurePublisher();
        WarnIfMultipleControllers();
        if (ShowLegacyPopup)
        {
            EnsurePopup();
        }
        else
        {
            DisableLegacyPopup();
        }

        SetPopupVisible(false, string.Empty);
        PublishMecanumInput(false, 0f, 0f, 0f, true);
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            EnsurePublisher();
        }
    }

    private void Update()
    {
        bool hasPose = TryGetRightHandPose(out Vector3 handPosition, out Quaternion handRotation);
        if (hasPose)
        {
            lastHandSeenTime = Time.time;
            lastValidHandPosition = handPosition;
            lastValidHandRotation = handRotation;
            hasLastValidHandPose = true;
        }

        bool inDriveGrace = state == MecanumState.MECANUM_CONTROL
            && !hasPose
            && hasLastValidHandPose
            && Time.time - lastHandSeenTime <= handLostGraceSec;
        if (inDriveGrace)
        {
            handPosition = lastValidHandPosition;
            handRotation = lastValidHandRotation;
        }

        lastHandTracked = hasPose;
        bool poseAvailableForDrive = hasPose || inDriveGrace;
        lastInputPoseUsable = poseAvailableForDrive;
        bool handClosed = poseAvailableForDrive && IsRightHandClosed(hasPose);
        lastHoldTime = state == MecanumState.RIGHT_HAND_HOLDING ? Time.time - holdStartTime : 0f;

        switch (state)
        {
            case MecanumState.IDLE:
                SetPopupVisible(false, string.Empty);
                PublishMecanumInput(false, 0f, 0f, 0f, false);
                if (handClosed)
                {
                    holdStartTime = Time.time;
                    TransitionTo(MecanumState.RIGHT_HAND_HOLDING, "right_hand_closed");
                }
                break;

            case MecanumState.RIGHT_HAND_HOLDING:
                if (!handClosed)
                {
                    TransitionToIdleWithStop();
                    break;
                }

                lastHoldTime = Time.time - holdStartTime;
                PublishMecanumInput(false, 0f, 0f, 0f, false);
                if (lastHoldTime >= holdSeconds)
                {
                    TransitionTo(MecanumState.MOVE_READY_POPUP, "hold_complete");
                }
                break;

            case MecanumState.MOVE_READY_POPUP:
                CaptureAnchor(handPosition, handRotation);
                SetPopupVisible(true, MoveReadyText);
                PublishMecanumInput(false, 0f, 0f, 0f, true);
                TransitionTo(MecanumState.MECANUM_CONTROL, "drive_active");
                break;

            case MecanumState.MECANUM_CONTROL:
                if (!poseAvailableForDrive)
                {
                    BeginHandOpenStop("right_hand_lost_grace_timeout");
                    break;
                }

                if (!handClosed)
                {
                    BeginHandOpenStop(hasPose ? "hand_open_or_grip_released" : "right_hand_lost");
                    break;
                }

                SetPopupVisible(true, ControlText);
                PublishControlInput(handPosition, handRotation);
                break;

            case MecanumState.HAND_OPEN_STOP:
                SetPopupVisible(true, StopText);
                PublishMecanumInput(false, 0f, 0f, 0f, false);
                if (Time.time - stopStartTime >= stopPublishSeconds)
                {
                    TransitionTo(MecanumState.IDLE, "stop_publish_complete");
                    SetPopupVisible(false, string.Empty);
                }
                break;
        }

        UpdatePopupPose();
        LogDebugOncePerSecond();
    }

    private void PublishControlInput(Vector3 handPosition, Quaternion handRotation)
    {
        float deltaX = Mathf.Clamp(handPosition.x - anchorPosition.x, -maxDeltaX, maxDeltaX);
        float deltaZ = Mathf.Clamp(handPosition.z - anchorPosition.z, -maxDeltaZ, maxDeltaZ);
        float rollDelta = CalculateRollDeltaRad(handRotation);
        PublishMecanumInput(true, deltaX, deltaZ, rollDelta, false);
    }

    private void CaptureAnchor(Vector3 handPosition, Quaternion handRotation)
    {
        anchorPosition = handPosition;
        anchorRotation = handRotation;
    }

    private void TransitionToIdleWithStop()
    {
        TransitionTo(MecanumState.IDLE, "hold_cancelled_or_hand_lost");
        SetPopupVisible(false, string.Empty);
        PublishMecanumInput(false, 0f, 0f, 0f, true);
    }

    private void BeginHandOpenStop(string reason)
    {
        stopStartTime = Time.time;
        TransitionTo(MecanumState.HAND_OPEN_STOP, reason);
        SetPopupVisible(true, StopText);
        PublishMecanumInput(false, 0f, 0f, 0f, true);
    }

    private void TransitionTo(MecanumState nextState, string reason)
    {
        if (state == nextState)
        {
            return;
        }

        state = nextState;
        lastStateTransitionReason = reason;
    }

    private bool TryGetRightHandPose(out Vector3 position, out Quaternion rotation)
    {
        if (rightHandTrackingTransform != null)
        {
            position = rightHandTrackingTransform.position;
            rotation = rightHandTrackingTransform.rotation;
            return true;
        }

        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator != null)
        {
            if (aggregator.TryGetJoint(TrackedHandJoint.Wrist, XRNode.RightHand, out HandJointPose wristPose))
            {
                position = wristPose.Position;
                rotation = wristPose.Rotation;
                return true;
            }

            if (aggregator.TryGetJoint(TrackedHandJoint.Palm, XRNode.RightHand, out HandJointPose palmPose))
            {
                position = palmPose.Position;
                rotation = palmPose.Rotation;
                return true;
            }
        }

        if (rightOvrHand != null && rightOvrHand.IsTracked)
        {
            position = rightOvrHand.transform.position;
            rotation = rightOvrHand.transform.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private bool IsRightHandClosed(bool hasCurrentHandPose)
    {
        if (!hasCurrentHandPose && state == MecanumState.MECANUM_CONTROL)
        {
            return gestureClosedLatched;
        }

        bool hasClosedSignal = TryGetRightHandClosedSignal(gestureClosedLatched, out bool closedSignal);
        if (hasClosedSignal)
        {
            gestureClosedLatched = closedSignal;
            return gestureClosedLatched;
        }

        if (state != MecanumState.MECANUM_CONTROL)
        {
            gestureClosedLatched = false;
        }

        return gestureClosedLatched;
    }

    private bool TryGetRightHandClosedSignal(bool wasClosed, out bool closed)
    {
        if (useOvrHand && rightOvrHand != null && rightOvrHand.IsTracked)
        {
            float gripStrength =
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) +
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) +
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Ring) +
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);
            float averageGripStrength = gripStrength * 0.25f;
            closed = wasClosed
                ? averageGripStrength >= Mathf.Min(ovrGripStrengthThreshold, ovrGripReleaseThreshold)
                : averageGripStrength >= ovrGripStrengthThreshold;
            return true;
        }

        if (useMrtkHandFallback && TryGetMrtkClosedHand(wasClosed, out bool mrtkClosed))
        {
            closed = mrtkClosed;
            return true;
        }

        if (useControllerGripFallback)
        {
            float grip = OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger);
            closed = wasClosed
                ? grip >= Mathf.Min(controllerGripThreshold, controllerGripReleaseThreshold)
                : grip >= controllerGripThreshold;
            return true;
        }

        closed = false;
        return false;
    }

    private bool TryGetMrtkClosedHand(bool wasClosed, out bool closed)
    {
        closed = false;
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null ||
            !aggregator.TryGetJoint(TrackedHandJoint.Palm, XRNode.RightHand, out HandJointPose palmPose))
        {
            return false;
        }

        int trackedCount = 0;
        int closedCount = 0;
        CountClosedFinger(TrackedHandJoint.IndexTip, palmPose.Position, ref trackedCount, ref closedCount);
        CountClosedFinger(TrackedHandJoint.MiddleTip, palmPose.Position, ref trackedCount, ref closedCount);
        CountClosedFinger(TrackedHandJoint.RingTip, palmPose.Position, ref trackedCount, ref closedCount);
        CountClosedFinger(TrackedHandJoint.LittleTip, palmPose.Position, ref trackedCount, ref closedCount);

        if (wasClosed)
        {
            int openCount = 0;
            CountOpenFinger(TrackedHandJoint.IndexTip, palmPose.Position, ref openCount);
            CountOpenFinger(TrackedHandJoint.MiddleTip, palmPose.Position, ref openCount);
            CountOpenFinger(TrackedHandJoint.RingTip, palmPose.Position, ref openCount);
            CountOpenFinger(TrackedHandJoint.LittleTip, palmPose.Position, ref openCount);
            closed = openCount < mrtkClosedFingerCount;
        }
        else
        {
            closed = trackedCount >= mrtkClosedFingerCount && closedCount >= mrtkClosedFingerCount;
        }

        return trackedCount > 0;
    }

    private void CountClosedFinger(
        TrackedHandJoint joint,
        Vector3 palmPosition,
        ref int trackedCount,
        ref int closedCount)
    {
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null ||
            !aggregator.TryGetJoint(joint, XRNode.RightHand, out HandJointPose fingerPose))
        {
            return;
        }

        trackedCount++;
        if (Vector3.Distance(fingerPose.Position, palmPosition) <= mrtkFingerToPalmClosedDistance)
        {
            closedCount++;
        }
    }

    private void CountOpenFinger(
        TrackedHandJoint joint,
        Vector3 palmPosition,
        ref int openCount)
    {
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null ||
            !aggregator.TryGetJoint(joint, XRNode.RightHand, out HandJointPose fingerPose))
        {
            return;
        }

        if (Vector3.Distance(fingerPose.Position, palmPosition) >= mrtkFingerToPalmOpenDistance)
        {
            openCount++;
        }
    }

    private float CalculateRollDeltaRad(Quaternion currentRotation)
    {
        Quaternion localDelta = Quaternion.Inverse(anchorRotation) * currentRotation;
        localDelta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f)
        {
            angleDeg -= 360f;
        }

        float signedRollDeg = angleDeg * Mathf.Clamp(Vector3.Dot(axis.normalized, Vector3.forward), -1f, 1f);
        return Mathf.Clamp(signedRollDeg * Mathf.Deg2Rad, -maxRollDeltaRad, maxRollDeltaRad);
    }

    private void PublishMecanumInput(bool active, float deltaX, float deltaZ, float rollDelta, bool force)
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(topicName))
        {
            lastPublishSkipReason = "not_playing_or_empty_topic";
            return;
        }

        float publishInterval = 1f / Mathf.Max(1, publishHz);
        if (!force && Time.time < nextPublishTime)
        {
            lastPublishSkipReason = "rate_limited";
            return;
        }

        float clampedDeltaX = Mathf.Clamp(deltaX, -maxDeltaX, maxDeltaX);
        float clampedDeltaZ = Mathf.Clamp(deltaZ, -maxDeltaZ, maxDeltaZ);
        float clampedRollDelta = Mathf.Clamp(rollDelta, -maxRollDeltaRad, maxRollDeltaRad);
        lastActive = active;
        lastDeltaX = clampedDeltaX;
        lastDeltaZ = clampedDeltaZ;
        lastRollDelta = clampedRollDelta;

        EnsurePublisher();
        lastCanPublish = CanPublish();
        if (!lastCanPublish)
        {
            return;
        }

        var message = new Float32MultiArrayMsg
        {
            data = new[]
            {
                active ? 1f : 0f,
                clampedDeltaX,
                clampedDeltaZ,
                clampedRollDelta
            }
        };
        ros.Publish(resolvedTopicName, message);
        nextPublishTime = Time.time + publishInterval;
        lastPublishInterval = lastPublishTime >= 0f ? Time.time - lastPublishTime : 0f;
        lastPublishTime = Time.time;
        publishCount++;
        lastPublishSkipReason = "None";
    }

    private void EnsurePublisher()
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(topicName))
        {
            return;
        }

        ros ??= ROSConnection.GetOrCreateInstance();
        if (!RosTopicProvider.TryResolveTopic(
                RosInputTopicKey.RightHandMecanumInput,
                topicName,
                out resolvedTopicName,
                out _))
        {
            publisherRegistered = false;
            return;
        }

        if (registeredTopic == resolvedTopicName && publisherRegistered)
        {
            return;
        }

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        ros.RegisterPublisher<Float32MultiArrayMsg>(resolvedTopicName, queueSize, false);
        registeredTopic = resolvedTopicName;
        publisherRegistered = true;
        publisherReadyRealtime = Time.realtimeSinceStartup + PublisherRegistrationSettleSeconds;
        loggedPublishSkippedBeforeRegistration = false;
        // Debug.Log(
        //     "[RightHandMecanumControl] RegisterPublisher " + topicName +
        //     " messageType=" + MessageRegistry.GetRosMessageName<Float32MultiArrayMsg>() +
        //     " readyAfter=" + PublisherRegistrationSettleSeconds.ToString("F2") + "s");
    }

    private bool CanPublish()
    {
        if (ros == null || !publisherRegistered)
        {
            lastCanPublish = false;
            lastPublishSkipReason = "publisher_not_registered";
            LogPublishSkippedBeforeRegistration("publisher is not registered");
            return false;
        }

        if (!RosTopicProvider.CanPublish(RosInputTopicKey.RightHandMecanumInput, out string rosUserReason)
            || string.IsNullOrWhiteSpace(resolvedTopicName))
        {
            lastCanPublish = false;
            lastPublishSkipReason = string.IsNullOrWhiteSpace(rosUserReason) ? "ros_user_session_not_ready" : rosUserReason;
            LogPublishSkippedBeforeRegistration(lastPublishSkipReason);
            return false;
        }

        if (ros.HasConnectionError)
        {
            lastCanPublish = false;
            lastPublishSkipReason = "ros_connection_error";
            LogPublishSkippedBeforeRegistration("ROS connection is not ready");
            return false;
        }

        if (Time.realtimeSinceStartup < publisherReadyRealtime)
        {
            lastCanPublish = false;
            lastPublishSkipReason = "publisher_registration_settling";
            LogPublishSkippedBeforeRegistration("waiting for ROS-TCP publisher registration to settle");
            return false;
        }

        loggedPublishSkippedBeforeRegistration = false;
        lastCanPublish = true;
        return true;
    }

    private void LogPublishSkippedBeforeRegistration(string reason)
    {
        if (loggedPublishSkippedBeforeRegistration)
        {
            return;
        }

        loggedPublishSkippedBeforeRegistration = true;
        // Debug.LogWarning("[RightHandMecanumControl] Publish skipped for " + topicName + ": " + reason);
    }

    private void EnsurePopup()
    {
        if (popupText != null)
        {
            popupPanel = popupText.transform.parent != null ? popupText.transform.parent.gameObject : popupText.gameObject;
            return;
        }

        if (popupCanvas == null)
        {
            popupCanvas = FindExistingWorldSpaceCanvas();
        }

        if (popupCanvas == null)
        {
            popupCanvas = CreatePopupCanvas();
            ownsPopupCanvas = true;
        }

        popupPanel = new GameObject("RightHandMecanumPopupPanel", typeof(RectTransform), typeof(Image));
        popupPanel.transform.SetParent(popupCanvas.transform, false);
        var panelRect = popupPanel.GetComponent<RectTransform>();
        panelRect.sizeDelta = popupSize;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        var background = popupPanel.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.72f);

        var textObject = new GameObject("RightHandMecanumPopupText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(popupPanel.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        popupText = textObject.GetComponent<TextMeshProUGUI>();
        popupText.alignment = TextAlignmentOptions.Center;
        popupText.color = Color.white;
        popupText.fontSize = 52f;
        popupText.fontStyle = FontStyles.Bold;
        popupText.enableWordWrapping = false;
    }

    private Canvas FindExistingWorldSpaceCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].renderMode == RenderMode.WorldSpace)
            {
                return canvases[i];
            }
        }

        return null;
    }

    private Canvas CreatePopupCanvas()
    {
        var canvasObject = new GameObject("RightHandMecanumPopupCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = popupSize;
        rect.localScale = Vector3.one * popupCanvasScale;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        return canvas;
    }

    private void SetPopupVisible(bool visible, string text)
    {
        if (!ShowLegacyPopup)
        {
            DisableLegacyPopup();
            return;
        }

        if (popupPanel == null || popupText == null)
        {
            return;
        }

        popupText.text = text;
        popupPanel.SetActive(visible);
        if (ownsPopupCanvas && popupCanvas != null)
        {
            popupCanvas.gameObject.SetActive(visible);
        }
    }

    private void DisableLegacyPopup()
    {
        if (legacyPopupDisabled)
        {
            return;
        }

        legacyPopupDisabled = true;
        if (popupText != null && popupPanel == null)
        {
            popupPanel = popupText.transform.parent != null ? popupText.transform.parent.gameObject : popupText.gameObject;
        }

        if (popupText != null)
        {
            popupText.text = string.Empty;
            popupText.gameObject.SetActive(false);
        }

        if (popupPanel != null)
        {
            popupPanel.SetActive(false);
        }

        if (ownsPopupCanvas && popupCanvas != null)
        {
            popupCanvas.gameObject.SetActive(false);
        }

        HideLegacyPopupObjectByName("RightHandMecanumPopupText");
        HideLegacyPopupObjectByName("RightHandMecanumPopupPanel");
        HideLegacyPopupObjectByName("RightHandMecanumPopupCanvas");
    }

    private void HideLegacyPopupObjectByName(string objectName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate != null && candidate.gameObject.name == objectName)
            {
                TMP_Text text = candidate.GetComponent<TMP_Text>();
                if (text != null)
                {
                    text.text = string.Empty;
                }

                candidate.gameObject.SetActive(false);
            }
        }
    }

    private void UpdatePopupPose()
    {
        if (!ownsPopupCanvas || popupCanvas == null || !popupCanvas.gameObject.activeSelf)
        {
            return;
        }

        Transform cameraTransform = ResolveCameraTransform();
        if (cameraTransform == null)
        {
            return;
        }

        popupCanvas.transform.position =
            cameraTransform.position +
            cameraTransform.right * popupViewportOffset.x +
            cameraTransform.up * popupViewportOffset.y +
            cameraTransform.forward * popupViewportOffset.z;
        popupCanvas.transform.rotation = Quaternion.LookRotation(
            popupCanvas.transform.position - cameraTransform.position,
            Vector3.up);
    }

    private Transform ResolveCameraTransform()
    {
        if (cachedCameraTransform != null)
        {
            return cachedCameraTransform;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cachedCameraTransform = mainCamera.transform;
        }

        return cachedCameraTransform;
    }

    private void LogDebugOncePerSecond()
    {
        if (!enableDiagnosticLog || Time.time < nextDebugLogTime)
        {
            return;
        }

        UpdateMeasuredPublishHz();
        float lastHandSeenAgo = lastHandSeenTime > -900f ? Time.time - lastHandSeenTime : -1f;
        string rosStatus = ros == null
            ? "NoROS"
            : ros.HasConnectionError
                ? "ConnectionError"
                : publisherRegistered
                    ? "Ready"
                    : "NotRegistered";

        Debug.Log(
            "[RoverInputDiag] t=" + Time.time.ToString("F2") +
            " pubCount=" + publishCount +
            " pubHz=" + measuredPublishHz.ToString("F1") +
            " pubInterval=" + lastPublishInterval.ToString("F3") +
            " active=" + lastActive +
            " hand=" + lastHandTracked +
            " inputUsable=" + lastInputPoseUsable +
            " driveActive=" + IsDriveActive +
            " dx=" + lastDeltaX.ToString("F3") +
            " dz=" + lastDeltaZ.ToString("F3") +
            " roll=" + lastRollDelta.ToString("F3") +
            " lastHandSeenAgo=" + lastHandSeenAgo.ToString("F2") +
            " state=" + state +
            " ros=" + rosStatus +
            " skip=" + lastPublishSkipReason +
            " reason=" + lastStateTransitionReason);
        nextDebugLogTime = Time.time + Mathf.Max(0.1f, diagnosticLogIntervalSec);
    }

    private void UpdateMeasuredPublishHz()
    {
        if (publishHzWindowStartTime < 0f)
        {
            publishHzWindowStartTime = Time.time;
            publishHzWindowStartCount = publishCount;
            measuredPublishHz = 0f;
            return;
        }

        float elapsed = Time.time - publishHzWindowStartTime;
        if (elapsed < 0.5f)
        {
            return;
        }

        measuredPublishHz = (publishCount - publishHzWindowStartCount) / elapsed;
        publishHzWindowStartTime = Time.time;
        publishHzWindowStartCount = publishCount;
    }

    private void WarnIfMultipleControllers()
    {
        var controllers = FindObjectsByType<RightHandMecanumControl>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        if (controllers.Length <= 1)
        {
            return;
        }

        Debug.LogWarning(
            "[RoverInputDiag] Multiple active RightHandMecanumControl instances detected: " +
            controllers.Length +
            ". Duplicate publishers can make /amir/right_hand_mecanum_input appear intermittent.");
    }
}
