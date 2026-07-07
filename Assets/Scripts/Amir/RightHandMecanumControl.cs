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
    [SerializeField] private float controllerGripThreshold = 0.75f;
    [SerializeField] private float mrtkFingerToPalmClosedDistance = 0.095f;
    [SerializeField] private int mrtkClosedFingerCount = 3;

    [Header("State Timing")]
    [SerializeField] private float holdSeconds = 3.0f;
    [SerializeField] private float stopPublishSeconds = 0.3f;

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
    private bool publisherRegistered;
    private float publisherReadyRealtime;
    private bool loggedPublishSkippedBeforeRegistration;
    private GameObject popupPanel;
    private bool ownsPopupCanvas;
    private Transform cachedCameraTransform;

    private Vector3 anchorPosition;
    private Quaternion anchorRotation = Quaternion.identity;
    private float holdStartTime;
    private float stopStartTime;
    private float nextPublishTime;
    private float nextDebugLogTime;

    private float lastHoldTime;
    private bool lastActive;
    private float lastDeltaX;
    private float lastDeltaZ;
    private float lastRollDelta;
    private bool lastHandTracked;
    private bool lastCanPublish;
    private float lastPublishTime = -1f;

    public bool IsHandTracked => lastHandTracked;
    public bool IsGestureHolding => state == MecanumState.RIGHT_HAND_HOLDING;
    public bool IsDriveActive => state == MecanumState.MECANUM_CONTROL;
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
        EnsurePopup();
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
        lastHandTracked = hasPose;
        bool handClosed = hasPose && IsRightHandClosed();
        lastHoldTime = state == MecanumState.RIGHT_HAND_HOLDING ? Time.time - holdStartTime : 0f;

        switch (state)
        {
            case MecanumState.IDLE:
                SetPopupVisible(false, string.Empty);
                PublishMecanumInput(false, 0f, 0f, 0f, false);
                if (handClosed)
                {
                    holdStartTime = Time.time;
                    state = MecanumState.RIGHT_HAND_HOLDING;
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
                    state = MecanumState.MOVE_READY_POPUP;
                }
                break;

            case MecanumState.MOVE_READY_POPUP:
                CaptureAnchor(handPosition, handRotation);
                SetPopupVisible(true, MoveReadyText);
                PublishMecanumInput(false, 0f, 0f, 0f, true);
                state = MecanumState.MECANUM_CONTROL;
                break;

            case MecanumState.MECANUM_CONTROL:
                if (!handClosed)
                {
                    stopStartTime = Time.time;
                    state = MecanumState.HAND_OPEN_STOP;
                    SetPopupVisible(true, StopText);
                    PublishMecanumInput(false, 0f, 0f, 0f, true);
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
                    state = MecanumState.IDLE;
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
        state = MecanumState.IDLE;
        SetPopupVisible(false, string.Empty);
        PublishMecanumInput(false, 0f, 0f, 0f, true);
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

    private bool IsRightHandClosed()
    {
        if (useOvrHand && rightOvrHand != null && rightOvrHand.IsTracked)
        {
            float gripStrength =
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) +
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Middle) +
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Ring) +
                rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);
            if (gripStrength * 0.25f >= ovrGripStrengthThreshold)
            {
                return true;
            }
        }

        if (useMrtkHandFallback && TryGetMrtkClosedHand(out bool mrtkClosed))
        {
            return mrtkClosed;
        }

        if (useControllerGripFallback)
        {
            return OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) >= controllerGripThreshold;
        }

        return false;
    }

    private bool TryGetMrtkClosedHand(out bool closed)
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

        closed = trackedCount >= mrtkClosedFingerCount && closedCount >= mrtkClosedFingerCount;
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
            return;
        }

        float publishInterval = 1f / Mathf.Max(1, publishHz);
        if (!force && Time.time < nextPublishTime)
        {
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
                clampedRollDelta,
                Time.time
            }
        };
        ros.Publish(topicName, message);
        nextPublishTime = Time.time + publishInterval;
        lastPublishTime = Time.time;
    }

    private void EnsurePublisher()
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(topicName))
        {
            return;
        }

        ros ??= ROSConnection.GetOrCreateInstance();
        if (registeredTopic == topicName && publisherRegistered)
        {
            return;
        }

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        ros.RegisterPublisher<Float32MultiArrayMsg>(topicName, queueSize, false);
        registeredTopic = topicName;
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
            LogPublishSkippedBeforeRegistration("publisher is not registered");
            return false;
        }

        if (ros.HasConnectionError)
        {
            LogPublishSkippedBeforeRegistration("ROS connection is not ready");
            return false;
        }

        if (Time.realtimeSinceStartup < publisherReadyRealtime)
        {
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
        if (Time.time < nextDebugLogTime)
        {
            return;
        }

        // Debug.Log(
        //     "[RightHandMecanumControl] state=" + state +
        //     " holdTime=" + lastHoldTime.ToString("F2") +
        //     " active=" + lastActive +
        //     " delta_x=" + lastDeltaX.ToString("F3") +
        //     " delta_z=" + lastDeltaZ.ToString("F3") +
        //     " roll_delta=" + lastRollDelta.ToString("F3"));
        nextDebugLogTime = Time.time + 1f;
    }
}
