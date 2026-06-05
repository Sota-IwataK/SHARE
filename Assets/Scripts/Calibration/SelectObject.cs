using System.Collections;
using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit.SpatialManipulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine.UI;
using UnityEngine.XR;


public class SelectObject : MonoBehaviour
{
    private enum AbsoluteCalibrationState
    {
        WaitingForPalm,
        MovePalmIntoSphere,
        HoldStill,
        Countdown,
        Calibrated,
        RecalibrationRequired
    }

    private const int CalibrationPointCount = 4;
    private const int CalibrationValueCount = CalibrationPointCount * 3;
    private const float CalibrationSphereSelectDistance = 0.18f;
    private const float CalibrationPointHoverSeconds = 0.4f;
    private const float CalibrationPointCooldownSeconds = 0.5f;
    private const float CalibrationDistanceLogInterval = 0.25f;
    private const float SendZoneSphereSelectDistance = 0.25f;
    private const float SendZoneSphereHoverSeconds = 0.4f;
    private const float DebugPanelDistance = 0.45f;
    private const float DebugPanelHeightOffset = -0.3f;
    private const float DebugPanelUpdateInterval = 0.1f;
    private const float MissingLeftHandLogInterval = 2f;
    private const float AbsoluteCalibrationUiUpdateInterval = 0.1f;
    private const float AbsoluteCalibrationPanelWidth = 0.54f;
    private const float AbsoluteCalibrationPanelHeight = 0.22f;
    private const float SetCenterSphereDiameter = 0.09f;
    private const float MinimumAbsoluteCalibrationRadius = 0.10f;
    private const float MinimumAbsoluteStillSpeedThreshold = 0.05f;
    private const float DefaultCalibrationSphereForward = 0.50f;
    private const float DefaultCalibrationSphereDown = 0.10f;
    private const float DefaultCalibrationTextForward = 0.60f;
    private const float DefaultCalibrationTextUp = 0.06f;
    private const float MinimumAbsoluteSphereAlpha = 0.75f;
    private const float AbsoluteCalibrationRingWidth = 0.007f;
    private const float AbsoluteCalibrationCenterMarkerDiameter = 0.035f;
    private const float AbsoluteCalibrationLabelHeightOffset = 0.14f;
    private const float AbsoluteCalibrationUiDebugLogInterval = 1.0f;
    private const float DefaultControlCenterHmdDown = 0.30f;
    private const float DefaultControlCenterPublishHz = 10f;
    private const int AbsoluteCalibrationRingSegments = 96;
    private const string AbsoluteCalibrationSphereRootName = "AbsoluteCalibrationSphereRoot";
    private const string AbsoluteCalibrationSphereVisualName = "AbsoluteCalibrationSphereVisual";
    private const string AbsoluteCalibrationSphereRingXyName = "AbsoluteCalibrationSphereOuterRingXY";
    private const string AbsoluteCalibrationSphereRingXzName = "AbsoluteCalibrationSphereOuterRingXZ";
    private const string AbsoluteCalibrationSphereRingYzName = "AbsoluteCalibrationSphereOuterRingYZ";
    private const string AbsoluteCalibrationSphereCenterMarkerName = "AbsoluteCalibrationSphereCenterMarker";
    private const string AbsoluteCalibrationSphereLabelName = "AbsoluteCalibrationSphereLabel";
    private const string AbsoluteCalibrationSphereLabelBackgroundName = "AbsoluteCalibrationSphereLabel_Background";
    private const string AbsoluteStatusWaiting = "Waiting";
    private const string AbsoluteStatusReady = "Ready";
    private const string AbsoluteStatusPublishingPalm = "Publishing Palm";
    private const string AbsoluteStatusReadyToSetCenter = "Ready to Set Center";
    private const string AbsoluteStatusWaitingForPalmPoseWorld = "Waiting for palm pose world";
    private const string AbsoluteStatusCalibrated = "Calibrated";
    private const string AbsoluteStatusTrackingLost = "Tracking Lost";
    private const string AbsoluteStatusRecalibrationRequired = "Recalibration Required";

    [SerializeField] AfterObjectFloat32Publisher afterObjectFloat32;
    [SerializeField] BeforeObjectFloat32Publisher beforeObjectFloat32;
    [SerializeField] CalibrationFloat32Publisher calibrationFloat32Publisher;
    [SerializeField] CalibrationFloat32Subscriber calibrationFloat32Subscriber;
    [SerializeField] IRM_SerectObjectPublisher irmSelectPublisher;  // 追加: IRM Select Publisher
    [SerializeField] private string sendZoneName = "CalibrationOperation";
    [SerializeField] private GameObject sendZone;
    [SerializeField] private float sendZoneHoverSeconds = 0.5f;
    [SerializeField] private float sendZoneRadius = 0.08f;
    [SerializeField] private handTracking sendZoneHandTracking;
    [SerializeField] private GameObject rosTcpComponents;
    [SerializeField] private bool showDebugVisuals = false;

    [Header("Absolute Scaled EE Calibration")]
    [SerializeField] private bool useAbsoluteScaledEeCalibration = true;
    [SerializeField] private bool showAbsoluteCalibrationUi = true;
    [SerializeField] private string resetRelativeAnchorTopic = "/amir_abs/reset_relative_anchor";
    [SerializeField, Min(0f)] private float resetRelativeAnchorCooldownSeconds = 0.5f;
    [SerializeField] private bool enableSetCenterPalmHover = true;
    [SerializeField] private TrackedHandJoint setCenterHoverJoint = TrackedHandJoint.IndexTip;
    [SerializeField, Min(0.01f)] private float setCenterButtonRadius = 0.08f;
    [SerializeField, Min(0.01f)] private float setCenterButtonHoverSeconds = 0.35f;
    [Header("Set Center UI Layout")]
    [SerializeField, Min(0f)] private float calibrationSphereForward = DefaultCalibrationSphereForward;
    [SerializeField, Min(0f)] private float calibrationSphereDown = DefaultCalibrationSphereDown;
    [SerializeField, Min(0f)] private float calibrationTextForward = DefaultCalibrationTextForward;
    [SerializeField] private float calibrationTextUp = DefaultCalibrationTextUp;
    [SerializeField, Min(0.01f)] private float absoluteCalibrationSphereDiameter = 0.20f;
    [SerializeField, Min(0.01f)] private float absoluteCalibrationRadius = 0.10f;
    [SerializeField, Min(0f)] private float absoluteStillSpeedThreshold = 0.05f;
    [SerializeField, Min(0.1f)] private float absoluteCountdownSeconds = 3.0f;
    [SerializeField, Min(0f)] private float absoluteSuccessHideDelaySeconds = 0.5f;
    [Header("Palm Pose Center Gate")]
    [SerializeField, Min(0.01f)] private float activationRadius = 0.08f;
    [SerializeField, Min(0f)] private float controlCenterHmdDown = DefaultControlCenterHmdDown;
    [SerializeField] private string controlCenterWorldTopic = "/palm_pose_control_center_world";
    [SerializeField] private string controlCenterWorldFrameId = "unity_world";
    [SerializeField, Min(0.1f)] private float controlCenterWorldPublishHz = DefaultControlCenterPublishHz;

    public List<GameObject> ObjectList;
    public List<float> beforeList;
    public List<float> afterList;
    public float[] beforeData;
    public float[] afterData;
    [SerializeField] private GameObject Origin;


    [ColorUsage(false, true)] public Color CalibrationColor;
    [ColorUsage(false, true)] public Color SelectColor;
    [ColorUsage(false, true)] public Color PickColor;
    [ColorUsage(false, true)] public Color DefalutColor;

    bool active = true;

    [SerializeField] private ObjectGenerationTest objectGeneration;
    public List<string> NotSelectObjectList;

    public bool CalibrationMode;
    public bool MoveMode;

    public List<GameObject> PickObjectList;
    public List<float> PickObjectPositionList;

    private float sendZoneHoverTimer;
    private bool calibrationComplete;
    private bool palmPosePublishingStarted;
    private bool wasInsideSendZone;
    private handPosePublisher palmPosePublisher;
    private airTapPublisher graspCommandPublisher;
    private float nextCalibrationDistanceLogTime;
    private int currentCalibrationIndex;
    private float calibrationPointHoverTimer;
    private float calibrationPointCooldownUntil;
    private bool calibrationPointHovering;
    private GameObject currentCalibrationSphere;
    private GameObject sendZoneSphere;
    private float nextSendZoneDistanceLogTime;
    private GameObject debugPanel;
    private TextMesh debugText;
    private float nextDebugPanelUpdateTime;
    private string debugState = "INIT";
    private bool debugRosTcpComponentsFound;
    private bool debugHandPosePublisherFound;
    private bool debugAirTapPublisherFound;
    private bool debugHandPosePublisherEnabled;
    private bool debugAirTapPublisherEnabled;
    private Vector3 lastDebugHandPosition;
    private bool hasLastDebugHandPosition;
    private float lastCalibrationDistance = -1f;
    private float lastSendZoneDistance = -1f;
    private float nextMissingLeftHandLogTime;
    private bool loggedAmirLeftHandOnlyMode;
    private bool absoluteCalibrationActive;
    private bool absoluteCalibrationCalibrated;
    private AbsoluteCalibrationState absoluteCalibrationState = AbsoluteCalibrationState.WaitingForPalm;
    private string absoluteCalibrationStatus = AbsoluteStatusWaiting;
    private GameObject absoluteCalibrationPanel;
    private TextMesh absoluteCalibrationText;
    private TextMesh absoluteCalibrationCountdownText;
    private GameObject absoluteCalibrationSphere;
    private GameObject absoluteCalibrationSphereVisual;
    private Renderer absoluteCalibrationSphereRenderer;
    private GameObject absoluteCalibrationCenterMarker;
    private Renderer absoluteCalibrationCenterMarkerRenderer;
    private TextMesh absoluteCalibrationSphereLabel;
    private GameObject absoluteCalibrationSphereLabelBackground;
    private Renderer absoluteCalibrationSphereLabelBackgroundRenderer;
    private GameObject startCalibrationButtonObject;
    private Renderer startCalibrationButtonRenderer;
    private GameObject setCenterButtonObject;
    private Renderer setCenterButtonRenderer;
    private float nextAbsoluteCalibrationUiUpdateTime;
    private float startCalibrationButtonHoverTimer;
    private bool startCalibrationButtonHovering;
    private float setCenterButtonHoverTimer;
    private bool setCenterButtonHovering;
    private bool absoluteCalibrationPanelPlaced;
    private bool absoluteCalibrationPanelShownForTrackingIssue;
    private Vector3 lastAbsolutePalmWorld;
    private bool hasLastAbsolutePalmWorld;
    private Vector3 absoluteCalibrationCenterWorld;
    private bool absoluteCalibrationSpherePlaced;
    private Vector3 previousAbsolutePalmWorld;
    private bool hasPreviousAbsolutePalmWorld;
    private float previousAbsolutePalmSampleTime;
    private float absoluteCalibrationPalmSpeed;
    private float absoluteCalibrationDistanceToCenter = -1f;
    private float absoluteCalibrationCountdownRemaining;
    private float absoluteCalibrationSuccessHideAt = -1f;
    private float nextAbsoluteCalibrationUiDebugLogTime;
    private ROSConnection absoluteCalibrationRos;
    private string registeredAbsoluteResetTopic;
    private bool isCenterReady;
    private bool isPublishingPalmPose;
    private Vector3 controlCenterWorld;
    private float controlCenterDistance = -1f;
    private float nextControlCenterWorldPublishTime;
    private ROSConnection controlCenterRos;
    private string registeredControlCenterWorldTopic;
    private RosMessageTypes.Geometry.PoseStampedMsg controlCenterWorldMessage;
    private Vector3 lastPalmWorldMsgPosition;
    private bool hasLastPalmWorldMsgPosition;
    private Vector3 lastControlCenterWorldMsgPosition;
    private bool hasLastControlCenterWorldMsgPosition;
    private Vector3 lastPalmControlCenterDeltaWorld;
    private bool hasLastPalmControlCenterDeltaWorld;

    void Start()
    {
        NormalizeAbsoluteCalibrationSettings();
        LogAmirLeftHandOnlyModeOnce();
        EnsureLists();
        ResetNotSelectObjectList();
        CalibrationMode = false;
        ResolveRuntimeReferences();
        SetPalmPosePublishersEnabled(false, false);
        SetSendZoneActive(false);
        objectGeneration?.HideSendZoneSphere();
        UpdateDebugPanel(true);
        UpdateAbsoluteCalibrationUi(true);
    }

    void Update()
    {
        UpdateCalibrationSphereDistanceSelection();
        UpdateSendZoneHover();
        UpdateDebugPanel(false);
        UpdatePalmPoseCenterGate();
        UpdateAbsoluteCalibrationCountdown();
        UpdateAbsoluteCalibrationUi(false);
    }

    private void OnValidate()
    {
        NormalizeAbsoluteCalibrationSettings();
    }

    private void NormalizeAbsoluteCalibrationSettings()
    {
        if (string.IsNullOrWhiteSpace(resetRelativeAnchorTopic))
        {
            resetRelativeAnchorTopic = "/amir_abs/reset_relative_anchor";
        }

        resetRelativeAnchorCooldownSeconds = Mathf.Max(0f, resetRelativeAnchorCooldownSeconds);
        setCenterButtonRadius = Mathf.Max(0.01f, setCenterButtonRadius);
        setCenterButtonHoverSeconds = Mathf.Max(0.01f, setCenterButtonHoverSeconds);
        if (Mathf.Approximately(calibrationSphereForward, 0.40f))
        {
            calibrationSphereForward = DefaultCalibrationSphereForward;
        }

        if (Mathf.Approximately(calibrationSphereDown, 0.18f))
        {
            calibrationSphereDown = DefaultCalibrationSphereDown;
        }

        if (Mathf.Approximately(calibrationTextForward, 0.55f))
        {
            calibrationTextForward = DefaultCalibrationTextForward;
        }

        if (Mathf.Approximately(calibrationTextUp, 0.02f))
        {
            calibrationTextUp = DefaultCalibrationTextUp;
        }

        calibrationSphereForward = Mathf.Max(0f, calibrationSphereForward);
        calibrationSphereDown = Mathf.Max(0f, calibrationSphereDown);
        calibrationTextForward = Mathf.Max(0f, calibrationTextForward);
        absoluteCalibrationRadius = Mathf.Max(MinimumAbsoluteCalibrationRadius, absoluteCalibrationRadius);
        absoluteStillSpeedThreshold = Mathf.Max(MinimumAbsoluteStillSpeedThreshold, absoluteStillSpeedThreshold);
        absoluteCalibrationSphereDiameter = Mathf.Max(absoluteCalibrationRadius * 2f, absoluteCalibrationSphereDiameter);
        absoluteCountdownSeconds = Mathf.Max(0.1f, absoluteCountdownSeconds);
        absoluteSuccessHideDelaySeconds = Mathf.Max(0f, absoluteSuccessHideDelaySeconds);
        activationRadius = Mathf.Max(0.01f, activationRadius);
        controlCenterHmdDown = Mathf.Max(0f, controlCenterHmdDown);
        if (string.IsNullOrWhiteSpace(controlCenterWorldTopic))
        {
            controlCenterWorldTopic = "/palm_pose_control_center_world";
        }

        if (!controlCenterWorldTopic.StartsWith("/"))
        {
            controlCenterWorldTopic = "/" + controlCenterWorldTopic;
        }

        if (string.IsNullOrWhiteSpace(controlCenterWorldFrameId))
        {
            controlCenterWorldFrameId = "unity_world";
        }

        controlCenterWorldPublishHz = Mathf.Max(0.1f, controlCenterWorldPublishHz);
    }

    public void ConfigureAbsoluteScaledEeCalibration(bool enabled, bool showUi, float resetCooldownSeconds)
    {
        useAbsoluteScaledEeCalibration = enabled;
        showAbsoluteCalibrationUi = showUi;
        resetRelativeAnchorCooldownSeconds = Mathf.Max(0f, resetCooldownSeconds);
    }

    void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("bottle"))
        {
            if(MoveMode == true)
            {
                if (!PickObjectList.Contains(other.gameObject))
                {
                    ApplyOpaqueColor(other.gameObject, PickColor);
                    PickObjectPositionList.Add(other.gameObject.transform.localPosition.x);
                    PickObjectPositionList.Add(other.gameObject.transform.localPosition.y);
                    PickObjectPositionList.Add(other.gameObject.transform.localPosition.z);
                    PickObjectList.Add(other.gameObject);
                }
            }
        }
    }
    public void CalibrationButton()
    {
        if (useAbsoluteScaledEeCalibration)
        {
            StartAbsoluteScaledEeCalibration();
            return;
        }

        EnsureLists();
        if (objectGeneration == null)
        {
            Debug.LogError("[SelectObject] objectGeneration is not assigned.");
            return;
        }

        objectGeneration.EnsureCalibrationBottles();
        ResetCalibrationSelection();
       currentCalibrationIndex = 0;
       calibrationPointHoverTimer = 0f;
       calibrationPointCooldownUntil = 0f;
       calibrationPointHovering = false;
       lastCalibrationDistance = -1f;
       lastSendZoneDistance = -1f;
       nextSendZoneDistanceLogTime = 0f;
        currentCalibrationSphere = objectGeneration.GetCurrentCalibrationSphere();
        if (!objectGeneration.MoveCalibrationSphereToPoint(currentCalibrationIndex, out Vector3 currentPointPosition))
        {
            Debug.LogError("[SelectObject] Axis calibration could not start because the current calibration sphere is unavailable.");
            return;
        }

        string axisBasisLog = null;
        if (objectGeneration.GetAxisCalibrationBasis(out Vector3 axisOrigin, out Vector3 xDir, out Vector3 yDir, out Vector3 zDir))
        {
            axisBasisLog = "[SelectObject] Axis basis: origin=" + axisOrigin
                + ", axisLength=" + objectGeneration.GetCalibrationAxisLength().ToString("F2")
                + ", xDir=" + xDir
                + ", yDir=" + yDir
                + ", zDir=" + zDir;
        }

        CalibrationMode = true;
        MoveMode = false;
        calibrationComplete = false;
        debugState = "AXIS CALIBRATION";
        sendZoneHoverTimer = 0f;
        wasInsideSendZone = false;
        nextCalibrationDistanceLogTime = 0f;
        ResolveRuntimeReferences();
        SetSendZoneActive(false);
        SetCalibrationPointPublishersEnabled(false);
        if (!palmPosePublishingStarted)
        {
            SetPalmPosePublishersEnabled(false, false);
        }
        if (calibrationFloat32Publisher != null) calibrationFloat32Publisher.enabled = true;
        if (calibrationFloat32Subscriber != null) calibrationFloat32Subscriber.enabled = true;
        objectGeneration.HideSendZoneSphere();
        Debug.Log("[SelectObject] Axis calibration started.");
        if (!string.IsNullOrEmpty(axisBasisLog))
        {
            Debug.Log(axisBasisLog);
        }
        Debug.Log("[SelectObject] Current calibration point="
            + objectGeneration.GetCalibrationPointLabel(currentCalibrationIndex)
            + ", position=" + currentPointPosition
            + ", sphereScale=" + objectGeneration.GetCalibrationSphereDiameter().ToString("F2")
            + ", threshold=" + CalibrationSphereSelectDistance.ToString("F2"));

        foreach (var name in NotSelectObjectList)
        {
            for (int j = 0; j < objectGeneration.before_obj.Count; j++)
            {
                if (objectGeneration.before_obj[j].name == name && objectGeneration.before_obj[j].name != "CalibrationSphere_Current")
                {
                    ApplyOpaqueColor(objectGeneration.before_obj[j].gameObject, CalibrationColor);
                }
            }
        }
    }

    public void MarkCalibrationComplete()
    {
        EnsureLists();

        if (beforeList.Count < CalibrationValueCount)
        {
            Debug.LogError("[SelectObject] Calibration complete was requested before 4 points were selected.");
            return;
        }

        if (calibrationComplete) return;

        CalibrationMode = false;
        calibrationComplete = true;
        debugState = "SENDZONE READY";
        sendZoneHoverTimer = 0f;
        wasInsideSendZone = false;
        SetCalibrationPointPublishersEnabled(true);
        ResolveRuntimeReferences();
        SetSendZoneActive(false);
        Debug.Log("[SelectObject] Four-point calibration complete. SendZone sphere enabled.");
        ShowSendZoneSphere();
    }

    public void PickModeButton()
    {
        CalibrationMode = false;
        MoveMode = !MoveMode;
    }

    private void StartAbsoluteScaledEeCalibration()
    {
        EnsureLists();
        ResolveRuntimeReferences();

        CalibrationMode = false;
        MoveMode = false;
        calibrationComplete = false;
        absoluteCalibrationActive = true;
        absoluteCalibrationCalibrated = false;
        absoluteCalibrationState = AbsoluteCalibrationState.WaitingForPalm;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        debugState = "ABSOLUTE WAITING FOR PALM";
        sendZoneHoverTimer = 0f;
        wasInsideSendZone = false;
        setCenterButtonHoverTimer = 0f;
        setCenterButtonHovering = false;
        startCalibrationButtonHoverTimer = 0f;
        startCalibrationButtonHovering = false;
        absoluteCalibrationPanelPlaced = false;
        absoluteCalibrationPanelShownForTrackingIssue = false;
        absoluteCalibrationSpherePlaced = false;
        absoluteCalibrationCountdownRemaining = absoluteCountdownSeconds;
        absoluteCalibrationSuccessHideAt = -1f;
        hasPreviousAbsolutePalmWorld = false;
        previousAbsolutePalmSampleTime = 0f;
        absoluteCalibrationPalmSpeed = 0f;
        absoluteCalibrationDistanceToCenter = -1f;
        lastCalibrationDistance = -1f;
        lastSendZoneDistance = -1f;

        SetCalibrationPointPublishersEnabled(false);
        if (calibrationFloat32Publisher != null) calibrationFloat32Publisher.enabled = false;
        if (calibrationFloat32Subscriber != null) calibrationFloat32Subscriber.enabled = false;
        SetSendZoneActive(false);
        objectGeneration?.HideCalibrationSphere();
        objectGeneration?.HideSendZoneSphere();

        EnsureAbsoluteCalibrationPanel();
        EnsureAbsoluteCalibrationSphere();
        if (!BeginPalmPoseCenterGate("CalibrationButton"))
        {
            SetAbsoluteCalibrationVisualsVisible(true);
            UpdateAbsoluteCalibrationUi(true);
            return;
        }

        UpdateAbsoluteCalibrationUi(true);
        Debug.Log("[AbsoluteCalibration] Center gate started; waiting for LEFT PALM contact before /palm_pose publish.");
    }

    public void SetCenterButton()
    {
        Debug.Log("[AbsoluteCalibration] Set Center button is disabled. Hold left palm inside the calibration sphere instead.");
        absoluteCalibrationActive = true;
        absoluteCalibrationState = AbsoluteCalibrationState.MovePalmIntoSphere;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        UpdateAbsoluteCalibrationUi(true);
    }

    public void StartCalibrationButton()
    {
        RequestAbsoluteCalibrationStartPublishing();
    }

    private bool RequestAbsoluteCalibrationStartPublishing()
    {
        Debug.Log("[AbsoluteCalibration] Start Calibration requested");
        if (!BeginPalmPoseCenterGate("StartCalibrationButton"))
        {
            return false;
        }

        absoluteCalibrationCalibrated = false;
        absoluteCalibrationState = AbsoluteCalibrationState.MovePalmIntoSphere;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        debugState = "CENTER GATE WAITING";
        UpdatePublisherDebugState();
        UpdateAbsoluteCalibrationUi(true);
        return true;
    }

    private bool RequestAbsoluteCalibrationSetCenter()
    {
        Debug.Log("[AbsoluteCalibration] Set Center requested");
        ResolveRuntimeReferences();

        if (!isPublishingPalmPose)
        {
            absoluteCalibrationState = AbsoluteCalibrationState.MovePalmIntoSphere;
            absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
            debugState = "CENTER GATE WAITING";
            UpdateAbsoluteCalibrationUi(true);
            Debug.LogWarning("[AbsoluteCalibration] Set Center skipped because /palm_pose has not started. Touch the center sphere first.");
            return false;
        }

        if (palmPosePublisher == null || palmPosePublisher.WorldPublishCount <= 0)
        {
            absoluteCalibrationStatus = AbsoluteStatusWaitingForPalmPoseWorld;
            absoluteCalibrationCalibrated = false;
            debugState = "ABSOLUTE WAITING FOR PALM POSE WORLD";
            UpdateAbsoluteCalibrationUi(true);
            Debug.LogWarning("[AbsoluteCalibration] Set Center skipped because /palm_pose_world has not published yet.");
            return false;
        }

        if (!TryGetPalmWorldForAbsoluteUi(out Vector3 palmWorld))
        {
            absoluteCalibrationStatus = AbsoluteStatusWaitingForPalmPoseWorld;
            absoluteCalibrationCalibrated = false;
            debugState = "ABSOLUTE WAITING FOR PALM POSE WORLD";
            UpdateAbsoluteCalibrationUi(true);
            Debug.LogWarning("[AbsoluteCalibration] Set Center skipped because left palm world position is not available.");
            return false;
        }

        lastAbsolutePalmWorld = palmWorld;
        hasLastAbsolutePalmWorld = true;

        if (!TryPublishAbsoluteCalibrationCenterReset())
        {
            absoluteCalibrationState = AbsoluteCalibrationState.MovePalmIntoSphere;
            absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
            debugState = "ABSOLUTE RESET FAILED";
            UpdateAbsoluteCalibrationUi(true);
            return false;
        }

        absoluteCalibrationCalibrated = true;
        absoluteCalibrationState = AbsoluteCalibrationState.Calibrated;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        debugState = "ABSOLUTE CALIBRATED";
        palmPosePublisher?.MarkAbsoluteCalibrationCalibrated();
        setCenterButtonHoverTimer = 0f;
        setCenterButtonHovering = false;
        startCalibrationButtonHoverTimer = 0f;
        startCalibrationButtonHovering = false;
        absoluteCalibrationSuccessHideAt = Time.time + absoluteSuccessHideDelaySeconds;
        UpdateAbsoluteCalibrationSphereVisual(true, true, true);
        UpdateAbsoluteCalibrationUi(true);
        return true;
    }

    private void HideAbsoluteCalibrationUiAfterSetCenter()
    {
        absoluteCalibrationActive = false;
        absoluteCalibrationPanelShownForTrackingIssue = false;
        absoluteCalibrationSpherePlaced = false;
        SetAbsoluteCalibrationVisualsVisible(false);
        Debug.Log("[AbsoluteCalibration] Set Center accepted; calibration UI hidden.");
    }

    private bool TryPublishAbsoluteCalibrationCenterReset()
    {
        return PalmHoverSendStartZone.TryPublishResetRelativeAnchor(
            "AbsoluteCalibration",
            true,
            resetRelativeAnchorTopic,
            resetRelativeAnchorCooldownSeconds,
            ref absoluteCalibrationRos,
            ref registeredAbsoluteResetTopic);
    }

    public void ResetPalmPoseCenterGate()
    {
        BeginPalmPoseCenterGate("Reset");
    }

    private bool BeginPalmPoseCenterGate(string source)
    {
        ResolveRuntimeReferences();

        SetPalmPosePublishersEnabled(false, false);
        palmPosePublishingStarted = false;
        isPublishingPalmPose = false;
        isCenterReady = false;
        controlCenterDistance = -1f;
        hasLastPalmWorldMsgPosition = false;
        hasLastControlCenterWorldMsgPosition = false;
        hasLastPalmControlCenterDeltaWorld = false;
        absoluteCalibrationCalibrated = false;
        absoluteCalibrationState = AbsoluteCalibrationState.MovePalmIntoSphere;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        absoluteCalibrationCountdownRemaining = absoluteCountdownSeconds;
        absoluteCalibrationSuccessHideAt = -1f;
        hasPreviousAbsolutePalmWorld = false;
        previousAbsolutePalmSampleTime = 0f;
        absoluteCalibrationPalmSpeed = 0f;
        absoluteCalibrationDistanceToCenter = -1f;

        Transform hmdTransform = FindHmdTransformForDebug();
        if (hmdTransform == null)
        {
            debugState = "CENTER GATE HMD MISSING";
            UpdatePublisherDebugState();
            UpdateAbsoluteCalibrationUi(true);
            Debug.LogWarning("[PalmPoseCenterGate] HMD transform was not found; center sphere could not be placed.");
            return false;
        }

        controlCenterWorld = hmdTransform.position + Vector3.down * controlCenterHmdDown;
        absoluteCalibrationCenterWorld = controlCenterWorld;
        isCenterReady = true;
        absoluteCalibrationActive = true;
        debugState = "CENTER GATE WAITING";

        EnsureAbsoluteCalibrationPanel();
        EnsureAbsoluteCalibrationSphere();
        PlaceAbsoluteCalibrationSphereAtControlCenter();
        SetAbsoluteCalibrationVisualsVisible(true);
        UpdateAbsoluteCalibrationSphereVisual(false, false, false);
        PublishControlCenterWorld(true);
        UpdatePublisherDebugState();
        Debug.Log("[PalmPoseCenterGate] " + source
            + " placed center sphere at " + controlCenterWorld.ToString("F3")
            + "; /palm_pose is stopped until LEFT PALM distance <= "
            + activationRadius.ToString("F3") + "m.");
        return true;
    }

    private void PlaceAbsoluteCalibrationSphereAtControlCenter()
    {
        EnsureAbsoluteCalibrationSphere();
        if (absoluteCalibrationSphere == null || !isCenterReady)
        {
            return;
        }

        absoluteCalibrationCenterWorld = controlCenterWorld;
        absoluteCalibrationSphere.transform.position = controlCenterWorld;
        absoluteCalibrationSphere.transform.rotation = Quaternion.identity;
        absoluteCalibrationSphere.SetActive(true);
        absoluteCalibrationSpherePlaced = true;
        UpdateAbsoluteCalibrationSphereLabelPose(FindHmdTransformForDebug());
    }

    private void UpdatePalmPoseCenterGate()
    {
        if (isPublishingPalmPose)
        {
            PublishControlCenterWorld(false);
        }

        if (!isCenterReady || absoluteCalibrationCalibrated)
        {
            return;
        }

        EnsureAbsoluteCalibrationSphere();
        PlaceAbsoluteCalibrationSphereAtControlCenter();

        if (!TryGetPalmWorldForAbsoluteUi(out Vector3 palmWorld))
        {
            controlCenterDistance = -1f;
            if (!isPublishingPalmPose)
            {
                absoluteCalibrationState = AbsoluteCalibrationState.WaitingForPalm;
                absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
                debugState = "CENTER GATE WAITING FOR PALM";
                UpdateAbsoluteCalibrationSphereVisual(false, false, false);
            }

            return;
        }

        lastAbsolutePalmWorld = palmWorld;
        hasLastAbsolutePalmWorld = true;
        controlCenterDistance = Vector3.Distance(palmWorld, controlCenterWorld);
        absoluteCalibrationDistanceToCenter = controlCenterDistance;
        UpdatePalmControlCenterDebugPositions(palmWorld);
        bool isTouchingCenter = controlCenterDistance <= activationRadius;

        if (!isPublishingPalmPose)
        {
            absoluteCalibrationState = isTouchingCenter
                ? AbsoluteCalibrationState.HoldStill
                : AbsoluteCalibrationState.MovePalmIntoSphere;
            absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
            debugState = isTouchingCenter ? "CENTER GATE CONTACT" : "CENTER GATE WAITING";
            UpdateAbsoluteCalibrationSphereVisual(isTouchingCenter, false, false);

            if (isTouchingCenter)
            {
                StartPalmPosePublishingFromCenterContact();
            }

            return;
        }

        debugState = "CENTER GATE PUBLISHING";
        UpdateAbsoluteCalibrationSphereVisual(true, true, false);
    }

    private void StartPalmPosePublishingFromCenterContact()
    {
        ResolveRuntimeReferences();
        if (palmPosePublisher == null)
        {
            debugState = "CENTER GATE PUBLISH FAILED";
            UpdatePublisherDebugState();
            Debug.LogError("[PalmPoseCenterGate] Cannot start /palm_pose publishing: handPosePublisher was not found on RosTcpComponents.");
            return;
        }

        bool hmdAnchorReset = palmPosePublisher.ResetPalmHmdAnchor();
        if (!hmdAnchorReset)
        {
            Debug.LogWarning("[PalmPoseCenterGate] /palm_pose_hmd_relative anchor reset failed at center contact. /palm_pose will still start.");
        }

        palmPosePublisher.enabled = true;
        if (graspCommandPublisher != null)
        {
            graspCommandPublisher.enabled = true;
        }
        palmPosePublishingStarted = true;
        isPublishingPalmPose = true;
        absoluteCalibrationState = AbsoluteCalibrationState.HoldStill;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        debugState = "CENTER GATE PUBLISHING";
        sendZoneHoverTimer = 0f;
        objectGeneration?.HideSendZoneSphere();
        PublishControlCenterWorld(true);
        UpdatePublisherDebugState();
        Debug.Log("[PalmPoseCenterGate] LEFT PALM touched center sphere; /palm_pose publish started. distance="
            + FormatFloatForDebug(controlCenterDistance)
            + ", center=" + controlCenterWorld.ToString("F3") + ".");
    }

    private bool EnsureAbsoluteCalibrationPalmPublishing()
    {
        ResolveRuntimeReferences();

        if (palmPosePublisher == null)
        {
            absoluteCalibrationStatus = AbsoluteStatusWaiting;
            absoluteCalibrationCalibrated = false;
            debugState = "ABSOLUTE PUBLISH START FAILED";
            UpdateAbsoluteCalibrationUi(true);
            Debug.LogError("[AbsoluteCalibration] Cannot start palm publishing: handPosePublisher was not found.");
            return false;
        }

        if (!palmPosePublisher.enabled)
        {
            palmPosePublisher.enabled = true;
        }

        if (graspCommandPublisher != null && !graspCommandPublisher.enabled)
        {
            graspCommandPublisher.enabled = true;
        }

        palmPosePublishingStarted = true;
        isPublishingPalmPose = true;
        UpdatePublisherDebugState();
        return true;
    }

    private void UpdateAbsoluteCalibrationCountdown()
    {
        if (!absoluteCalibrationActive || !showAbsoluteCalibrationUi || absoluteCalibrationCalibrated || !enableSetCenterPalmHover)
        {
            if (absoluteCalibrationCalibrated && absoluteCalibrationSuccessHideAt > 0f && Time.time >= absoluteCalibrationSuccessHideAt)
            {
                HideAbsoluteCalibrationUiAfterSetCenter();
                absoluteCalibrationSuccessHideAt = -1f;
            }

            return;
        }

        if (!isPublishingPalmPose)
        {
            EnsureAbsoluteCalibrationSphere();
            if (!absoluteCalibrationSpherePlaced && isCenterReady)
            {
                PlaceAbsoluteCalibrationSphereAtControlCenter();
            }

            return;
        }

        EnsureAbsoluteCalibrationSphere();
        if (absoluteCalibrationSphere == null)
        {
            return;
        }

        if (!absoluteCalibrationSpherePlaced)
        {
            PlaceAbsoluteCalibrationSphereInFrontOfHmd();
            if (!absoluteCalibrationSpherePlaced)
            {
                ResetAbsoluteCalibrationCountdown(AbsoluteCalibrationState.WaitingForPalm);
                return;
            }
        }

        if (!TryGetPalmWorldForAbsoluteUi(out Vector3 palmWorld))
        {
            ResetAbsoluteCalibrationCountdown(AbsoluteCalibrationState.WaitingForPalm);
            UpdateAbsoluteCalibrationSphereVisual(false, false, false);
            return;
        }

        float now = Time.time;
        if (hasPreviousAbsolutePalmWorld && previousAbsolutePalmSampleTime > 0f && now > previousAbsolutePalmSampleTime)
        {
            absoluteCalibrationPalmSpeed = Vector3.Distance(palmWorld, previousAbsolutePalmWorld) / Mathf.Max(0.0001f, now - previousAbsolutePalmSampleTime);
        }
        else
        {
            absoluteCalibrationPalmSpeed = 0f;
        }

        previousAbsolutePalmWorld = palmWorld;
        previousAbsolutePalmSampleTime = now;
        hasPreviousAbsolutePalmWorld = true;
        lastAbsolutePalmWorld = palmWorld;
        hasLastAbsolutePalmWorld = true;

        absoluteCalibrationDistanceToCenter = Vector3.Distance(palmWorld, absoluteCalibrationCenterWorld);
        bool insideSphere = absoluteCalibrationDistanceToCenter <= absoluteCalibrationRadius;
        bool still = absoluteCalibrationPalmSpeed <= absoluteStillSpeedThreshold;

        if (!insideSphere)
        {
            ResetAbsoluteCalibrationCountdown(AbsoluteCalibrationState.MovePalmIntoSphere);
            UpdateAbsoluteCalibrationSphereVisual(false, false, false);
            return;
        }

        if (!still)
        {
            ResetAbsoluteCalibrationCountdown(AbsoluteCalibrationState.HoldStill);
            UpdateAbsoluteCalibrationSphereVisual(true, false, false);
            return;
        }

        absoluteCalibrationState = AbsoluteCalibrationState.Countdown;
        absoluteCalibrationCountdownRemaining = Mathf.Max(0f, absoluteCalibrationCountdownRemaining - Time.deltaTime);
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        debugState = "ABSOLUTE COUNTDOWN";
        UpdateAbsoluteCalibrationSphereVisual(true, true, true);

        if (absoluteCalibrationCountdownRemaining <= 0f)
        {
            RequestAbsoluteCalibrationSetCenter();
        }
    }

    private void ResetAbsoluteCalibrationCountdown(AbsoluteCalibrationState state)
    {
        absoluteCalibrationState = state;
        absoluteCalibrationCountdownRemaining = absoluteCountdownSeconds;
        absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(state);
        debugState = "ABSOLUTE " + state.ToString().ToUpperInvariant();
    }

    private void UpdateAbsoluteCalibrationUi(bool force)
    {
        bool trackingStatusActive = TryGetAbsoluteCalibrationTrackingStatus(out string trackingStatus);
        bool preserveCalibrationProgress = IsAbsoluteCalibrationProgressState(absoluteCalibrationState);
        if (trackingStatusActive && showAbsoluteCalibrationUi && !preserveCalibrationProgress)
        {
            absoluteCalibrationActive = true;
            absoluteCalibrationPanelShownForTrackingIssue = true;
            if (trackingStatus == AbsoluteStatusRecalibrationRequired)
            {
                absoluteCalibrationCalibrated = false;
                absoluteCalibrationState = AbsoluteCalibrationState.RecalibrationRequired;
                absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
                debugState = "ABSOLUTE RECALIBRATION REQUIRED";
            }
            else
            {
                absoluteCalibrationState = AbsoluteCalibrationState.WaitingForPalm;
                absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
                debugState = "ABSOLUTE TRACKING LOST";
            }
        }
        else if (absoluteCalibrationPanelShownForTrackingIssue)
        {
            absoluteCalibrationPanelShownForTrackingIssue = false;
            if (absoluteCalibrationCalibrated)
            {
                absoluteCalibrationActive = false;
                SetAbsoluteCalibrationPanelVisible(false);
                return;
            }
        }

        if (!absoluteCalibrationActive || !showAbsoluteCalibrationUi)
        {
            SetAbsoluteCalibrationVisualsVisible(false);
            return;
        }

        EnsureAbsoluteCalibrationPanel();
        EnsureAbsoluteCalibrationSphere();
        SetAbsoluteCalibrationVisualsVisible(true);
        PlaceAbsoluteCalibrationPanelIfNeeded();
        if (!absoluteCalibrationSpherePlaced)
        {
            PlaceAbsoluteCalibrationSphereInFrontOfHmd();
        }

        Transform hmdTransform = FindHmdTransformForDebug();
        UpdateAbsoluteCalibrationSphereLabelPose(hmdTransform);
        LogAbsoluteCalibrationUiDebugIfNeeded(hmdTransform);

        if (!force && Time.time < nextAbsoluteCalibrationUiUpdateTime)
        {
            return;
        }

        nextAbsoluteCalibrationUiUpdateTime = Time.time + AbsoluteCalibrationUiUpdateInterval;
        bool palmReady = TryGetPalmWorldForAbsoluteUi(out Vector3 palmWorld);
        if (palmReady)
        {
            lastAbsolutePalmWorld = palmWorld;
            hasLastAbsolutePalmWorld = true;
        }

        if (!absoluteCalibrationCalibrated)
        {
            if (!palmReady)
            {
                absoluteCalibrationState = AbsoluteCalibrationState.WaitingForPalm;
            }

            absoluteCalibrationStatus = GetAbsoluteCalibrationStatusText(absoluteCalibrationState);
        }

        UpdateAbsoluteCalibrationText();
    }

    private static bool IsAbsoluteCalibrationProgressState(AbsoluteCalibrationState state)
    {
        return state == AbsoluteCalibrationState.MovePalmIntoSphere
            || state == AbsoluteCalibrationState.HoldStill
            || state == AbsoluteCalibrationState.Countdown;
    }

    private void EnsureAbsoluteCalibrationPanel()
    {
        if (absoluteCalibrationPanel == null)
        {
            absoluteCalibrationPanel = FindSceneObjectByName("AbsoluteCalibrationPanel");
        }

        if (absoluteCalibrationPanel == null)
        {
            absoluteCalibrationPanel = new GameObject("AbsoluteCalibrationPanel");
        }

        if (absoluteCalibrationText == null)
        {
            Transform textTransform = FindChildTransformByName(absoluteCalibrationPanel.transform, "AbsoluteCalibrationPanel_Text");
            GameObject textObject = textTransform != null
                ? textTransform.gameObject
                : new GameObject("AbsoluteCalibrationPanel_Text");
            textObject.transform.SetParent(absoluteCalibrationPanel.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0.075f, 0f);
            absoluteCalibrationText = textObject.GetComponent<TextMesh>();
            if (absoluteCalibrationText == null)
            {
                absoluteCalibrationText = textObject.AddComponent<TextMesh>();
            }

            absoluteCalibrationText.anchor = TextAnchor.UpperCenter;
            absoluteCalibrationText.alignment = TextAlignment.Center;
            absoluteCalibrationText.characterSize = 0.022f;
            absoluteCalibrationText.fontSize = 96;
            absoluteCalibrationText.color = Color.white;
        }

        if (absoluteCalibrationCountdownText == null)
        {
            Transform countdownTransform = FindChildTransformByName(absoluteCalibrationPanel.transform, "AbsoluteCalibrationCountdown_Text");
            GameObject countdownObject = countdownTransform != null
                ? countdownTransform.gameObject
                : new GameObject("AbsoluteCalibrationCountdown_Text");
            countdownObject.transform.SetParent(absoluteCalibrationPanel.transform, false);
            countdownObject.transform.localPosition = new Vector3(0f, -0.055f, -0.01f);
            absoluteCalibrationCountdownText = countdownObject.GetComponent<TextMesh>();
            if (absoluteCalibrationCountdownText == null)
            {
                absoluteCalibrationCountdownText = countdownObject.AddComponent<TextMesh>();
            }

            absoluteCalibrationCountdownText.anchor = TextAnchor.MiddleCenter;
            absoluteCalibrationCountdownText.alignment = TextAlignment.Center;
            absoluteCalibrationCountdownText.characterSize = 0.075f;
            absoluteCalibrationCountdownText.fontSize = 180;
            absoluteCalibrationCountdownText.color = Color.white;
        }

        EnsureAbsoluteCalibrationBackground();
        HideAbsoluteCalibrationButtonVisuals();
        EnsureAbsoluteCalibrationSphere();
        EnsureAbsoluteCalibrationPanelMovable();
    }

    private void EnsureAbsoluteCalibrationBackground()
    {
        Transform backgroundTransform = FindChildTransformByName(absoluteCalibrationPanel.transform, "AbsoluteCalibrationPanel_Background");
        GameObject background = backgroundTransform != null ? backgroundTransform.gameObject : null;
        if (background == null)
        {
            background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "AbsoluteCalibrationPanel_Background";
            background.transform.SetParent(absoluteCalibrationPanel.transform, false);
        }

        background.transform.localPosition = new Vector3(0f, 0f, 0.015f);
        background.transform.localScale = new Vector3(AbsoluteCalibrationPanelWidth, AbsoluteCalibrationPanelHeight, 1f);
        Collider backgroundCollider = background.GetComponent<Collider>();
        if (backgroundCollider != null)
        {
            Destroy(backgroundCollider);
        }

        Renderer backgroundRenderer = background.GetComponent<Renderer>();
        if (backgroundRenderer != null)
        {
            Color backgroundColor = new Color(0f, 0f, 0f, 0.88f);
            Material material = backgroundRenderer.sharedMaterial != null
                ? backgroundRenderer.material
                : CreateUnlitMaterial(backgroundColor);
            if (material != null)
            {
                ConfigureTransparentMaterial(material);
                SetMaterialColor(material, backgroundColor);
                backgroundRenderer.material = material;
            }
        }
    }

    private void EnsureAbsoluteCalibrationSphere()
    {
        if (absoluteCalibrationSphere == null)
        {
            absoluteCalibrationSphere = FindSceneObjectByName(AbsoluteCalibrationSphereRootName);
        }

        if (absoluteCalibrationSphere == null)
        {
            absoluteCalibrationSphere = new GameObject(AbsoluteCalibrationSphereRootName);
        }

        absoluteCalibrationSphere.name = AbsoluteCalibrationSphereRootName;
        absoluteCalibrationSphere.transform.SetParent(null, true);
        absoluteCalibrationSphere.transform.localScale = Vector3.one;
        absoluteCalibrationSphere.SetActive(true);

        SphereCollider sphereCollider = absoluteCalibrationSphere.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = absoluteCalibrationSphere.AddComponent<SphereCollider>();
        }

        sphereCollider.isTrigger = true;
        sphereCollider.radius = absoluteCalibrationRadius;

        Renderer rootRenderer = absoluteCalibrationSphere.GetComponent<Renderer>();
        if (rootRenderer != null)
        {
            rootRenderer.enabled = false;
        }

        EnsureAbsoluteCalibrationSphereVisual();
        EnsureAbsoluteCalibrationSphereRings();
        EnsureAbsoluteCalibrationCenterMarker();
        EnsureAbsoluteCalibrationSphereLabel();
    }

    private void EnsureAbsoluteCalibrationSphereVisual()
    {
        absoluteCalibrationSphereVisual = GetOrCreateAbsoluteCalibrationPrimitiveChild(
            AbsoluteCalibrationSphereVisualName,
            PrimitiveType.Sphere);
        absoluteCalibrationSphereVisual.transform.localPosition = Vector3.zero;
        absoluteCalibrationSphereVisual.transform.localRotation = Quaternion.identity;
        absoluteCalibrationSphereVisual.transform.localScale = Vector3.one * absoluteCalibrationSphereDiameter;
        absoluteCalibrationSphereVisual.SetActive(true);

        Collider visualCollider = absoluteCalibrationSphereVisual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            visualCollider.enabled = false;
        }

        absoluteCalibrationSphereRenderer = absoluteCalibrationSphereVisual.GetComponent<Renderer>();
        if (absoluteCalibrationSphereRenderer != null)
        {
            if (absoluteCalibrationSphereRenderer.sharedMaterial == null)
            {
                Material material = CreateUnlitMaterial(new Color(0f, 0.9f, 1f, MinimumAbsoluteSphereAlpha));
                if (material != null)
                {
                    SetOpaque(material);
                    absoluteCalibrationSphereRenderer.material = material;
                }
            }
            else
            {
                SetOpaque(absoluteCalibrationSphereRenderer.material);
            }

            absoluteCalibrationSphereRenderer.enabled = true;
            SetMaterialColor(
                absoluteCalibrationSphereRenderer.material,
                WithMinimumAlpha(new Color(0f, 0.9f, 1f, MinimumAbsoluteSphereAlpha), MinimumAbsoluteSphereAlpha));
        }
    }

    private void EnsureAbsoluteCalibrationSphereRings()
    {
        EnsureAbsoluteCalibrationSphereRing(AbsoluteCalibrationSphereRingXyName, Vector3.right, Vector3.up);
        EnsureAbsoluteCalibrationSphereRing(AbsoluteCalibrationSphereRingXzName, Vector3.right, Vector3.forward);
        EnsureAbsoluteCalibrationSphereRing(AbsoluteCalibrationSphereRingYzName, Vector3.up, Vector3.forward);
    }

    private void EnsureAbsoluteCalibrationSphereRing(string ringName, Vector3 axisA, Vector3 axisB)
    {
        GameObject ring = GetOrCreateAbsoluteCalibrationChild(ringName);
        ring.transform.localPosition = Vector3.zero;
        ring.transform.localRotation = Quaternion.identity;
        ring.transform.localScale = Vector3.one;
        ring.SetActive(true);

        LineRenderer lineRenderer = ring.GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = ring.AddComponent<LineRenderer>();
        }

        lineRenderer.enabled = true;
        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = true;
        lineRenderer.positionCount = AbsoluteCalibrationRingSegments;
        lineRenderer.widthMultiplier = AbsoluteCalibrationRingWidth;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.numCapVertices = 4;
        lineRenderer.startColor = Color.yellow;
        lineRenderer.endColor = Color.yellow;

        for (int i = 0; i < AbsoluteCalibrationRingSegments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / AbsoluteCalibrationRingSegments;
            Vector3 point = (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * absoluteCalibrationRadius;
            lineRenderer.SetPosition(i, point);
        }

        if (lineRenderer.sharedMaterial == null)
        {
            Material material = CreateUnlitMaterial(Color.yellow);
            if (material != null)
            {
                SetOpaque(material);
                lineRenderer.material = material;
            }
        }
        else
        {
            SetOpaque(lineRenderer.material);
        }

        SetMaterialColor(lineRenderer.material, Color.yellow);
    }

    private void EnsureAbsoluteCalibrationCenterMarker()
    {
        absoluteCalibrationCenterMarker = GetOrCreateAbsoluteCalibrationPrimitiveChild(
            AbsoluteCalibrationSphereCenterMarkerName,
            PrimitiveType.Sphere);
        absoluteCalibrationCenterMarker.transform.localPosition = Vector3.zero;
        absoluteCalibrationCenterMarker.transform.localRotation = Quaternion.identity;
        absoluteCalibrationCenterMarker.transform.localScale = Vector3.one * AbsoluteCalibrationCenterMarkerDiameter;
        absoluteCalibrationCenterMarker.SetActive(true);

        Collider markerCollider = absoluteCalibrationCenterMarker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
        }

        absoluteCalibrationCenterMarkerRenderer = absoluteCalibrationCenterMarker.GetComponent<Renderer>();
        if (absoluteCalibrationCenterMarkerRenderer == null)
        {
            return;
        }

        if (absoluteCalibrationCenterMarkerRenderer.sharedMaterial == null)
        {
            Material material = CreateUnlitMaterial(Color.white);
            if (material != null)
            {
                SetOpaque(material);
                absoluteCalibrationCenterMarkerRenderer.material = material;
            }
        }
        else
        {
            SetOpaque(absoluteCalibrationCenterMarkerRenderer.material);
        }

        absoluteCalibrationCenterMarkerRenderer.enabled = true;
        SetMaterialColor(absoluteCalibrationCenterMarkerRenderer.material, Color.white);
    }

    private void EnsureAbsoluteCalibrationSphereLabel()
    {
        GameObject labelObject = GetOrCreateAbsoluteCalibrationChild(AbsoluteCalibrationSphereLabelName);
        labelObject.SetActive(true);
        absoluteCalibrationSphereLabel = labelObject.GetComponent<TextMesh>();
        if (absoluteCalibrationSphereLabel == null)
        {
            absoluteCalibrationSphereLabel = labelObject.AddComponent<TextMesh>();
        }

        absoluteCalibrationSphereLabel.text = "LEFT PALM HERE";
        absoluteCalibrationSphereLabel.anchor = TextAnchor.MiddleCenter;
        absoluteCalibrationSphereLabel.alignment = TextAlignment.Center;
        absoluteCalibrationSphereLabel.characterSize = 0.022f;
        absoluteCalibrationSphereLabel.fontSize = 96;
        absoluteCalibrationSphereLabel.color = Color.yellow;

        absoluteCalibrationSphereLabelBackground = GetOrCreateAbsoluteCalibrationPrimitiveChild(
            AbsoluteCalibrationSphereLabelBackgroundName,
            PrimitiveType.Quad);
        absoluteCalibrationSphereLabelBackground.transform.localScale = new Vector3(0.24f, 0.055f, 1f);
        absoluteCalibrationSphereLabelBackground.SetActive(true);

        Collider backgroundCollider = absoluteCalibrationSphereLabelBackground.GetComponent<Collider>();
        if (backgroundCollider != null)
        {
            backgroundCollider.enabled = false;
        }

        absoluteCalibrationSphereLabelBackgroundRenderer = absoluteCalibrationSphereLabelBackground.GetComponent<Renderer>();
        if (absoluteCalibrationSphereLabelBackgroundRenderer != null)
        {
            Color backgroundColor = new Color(0f, 0f, 0f, 0.70f);
            Material material = absoluteCalibrationSphereLabelBackgroundRenderer.sharedMaterial != null
                ? absoluteCalibrationSphereLabelBackgroundRenderer.material
                : CreateUnlitMaterial(backgroundColor);
            if (material != null)
            {
                ConfigureTransparentMaterial(material);
                SetMaterialColor(material, backgroundColor);
                absoluteCalibrationSphereLabelBackgroundRenderer.material = material;
            }

            absoluteCalibrationSphereLabelBackgroundRenderer.enabled = true;
        }
    }

    private GameObject GetOrCreateAbsoluteCalibrationChild(string childName)
    {
        Transform childTransform = FindChildTransformByName(absoluteCalibrationSphere.transform, childName);
        GameObject child = childTransform != null ? childTransform.gameObject : new GameObject(childName);
        child.name = childName;
        child.transform.SetParent(absoluteCalibrationSphere.transform, false);
        return child;
    }

    private GameObject GetOrCreateAbsoluteCalibrationPrimitiveChild(string childName, PrimitiveType primitiveType)
    {
        Transform childTransform = FindChildTransformByName(absoluteCalibrationSphere.transform, childName);
        if (childTransform != null)
        {
            EnsurePrimitiveMesh(childTransform.gameObject, primitiveType);
            return childTransform.gameObject;
        }

        GameObject child = GameObject.CreatePrimitive(primitiveType);
        child.name = childName;
        child.transform.SetParent(absoluteCalibrationSphere.transform, false);
        return child;
    }

    private void PlaceAbsoluteCalibrationSphereInFrontOfHmd()
    {
        if (isCenterReady)
        {
            PlaceAbsoluteCalibrationSphereAtControlCenter();
            return;
        }

        EnsureAbsoluteCalibrationSphere();
        Transform hmdTransform = FindHmdTransformForDebug();
        if (hmdTransform == null || absoluteCalibrationSphere == null)
        {
            Debug.LogWarning("[AbsoluteCalibration] HMD transform was not found; calibration sphere was not placed.");
            return;
        }

        absoluteCalibrationCenterWorld = hmdTransform.position
            + hmdTransform.forward.normalized * calibrationSphereForward
            - hmdTransform.up.normalized * calibrationSphereDown;
        absoluteCalibrationSphere.transform.position = absoluteCalibrationCenterWorld;
        absoluteCalibrationSphere.transform.rotation = Quaternion.identity;
        absoluteCalibrationSphere.SetActive(true);
        UpdateAbsoluteCalibrationSphereLabelPose(hmdTransform);
        absoluteCalibrationSpherePlaced = true;
    }

    private void UpdateAbsoluteCalibrationSphereVisual(bool insideSphere, bool still, bool countdown)
    {
        if (absoluteCalibrationSphereRenderer == null && absoluteCalibrationSphere != null)
        {
            Transform visualTransform = FindChildTransformByName(absoluteCalibrationSphere.transform, AbsoluteCalibrationSphereVisualName);
            absoluteCalibrationSphereVisual = visualTransform != null ? visualTransform.gameObject : absoluteCalibrationSphereVisual;
            absoluteCalibrationSphereRenderer = absoluteCalibrationSphereVisual != null
                ? absoluteCalibrationSphereVisual.GetComponent<Renderer>()
                : null;
        }

        if (absoluteCalibrationSphereRenderer == null)
        {
            return;
        }

        Color color;
        if (absoluteCalibrationState == AbsoluteCalibrationState.Calibrated)
        {
            color = new Color(0f, 0.85f, 0.25f, 1f);
        }
        else if (countdown)
        {
            color = new Color(1f, 0.95f, 0f, 1f);
        }
        else if (isPublishingPalmPose)
        {
            color = new Color(0f, 0.95f, 0.35f, 1f);
        }
        else if (insideSphere && !still)
        {
            color = new Color(1f, 0.55f, 0f, 0.9f);
        }
        else
        {
            color = new Color(0f, 0.9f, 1f, MinimumAbsoluteSphereAlpha);
        }

        absoluteCalibrationSphere.SetActive(true);
        absoluteCalibrationSphereVisual?.SetActive(true);
        absoluteCalibrationSphereRenderer.enabled = true;
        SetOpaque(absoluteCalibrationSphereRenderer.material);
        SetMaterialColor(absoluteCalibrationSphereRenderer.material, WithMinimumAlpha(color, MinimumAbsoluteSphereAlpha));
    }

    private void UpdateAbsoluteCalibrationSphereLabelPose(Transform hmdTransform)
    {
        if (hmdTransform == null || absoluteCalibrationSphere == null || absoluteCalibrationSphereLabel == null)
        {
            return;
        }

        Vector3 labelPosition = absoluteCalibrationSphere.transform.position
            + hmdTransform.up.normalized * AbsoluteCalibrationLabelHeightOffset;
        Vector3 labelForward = labelPosition - hmdTransform.position;
        if (labelForward.sqrMagnitude < 0.0001f)
        {
            labelForward = hmdTransform.forward;
        }

        Quaternion labelRotation = Quaternion.LookRotation(labelForward, Vector3.up);
        absoluteCalibrationSphereLabel.transform.position = labelPosition;
        absoluteCalibrationSphereLabel.transform.rotation = labelRotation;
        absoluteCalibrationSphereLabel.gameObject.SetActive(true);

        if (absoluteCalibrationSphereLabelBackground != null)
        {
            absoluteCalibrationSphereLabelBackground.transform.position =
                labelPosition + (labelRotation * new Vector3(0f, 0f, 0.012f));
            absoluteCalibrationSphereLabelBackground.transform.rotation = labelRotation;
            absoluteCalibrationSphereLabelBackground.SetActive(true);
        }
    }

    private void LogAbsoluteCalibrationUiDebugIfNeeded(Transform hmdTransform)
    {
        if (!Application.isPlaying || Time.time < nextAbsoluteCalibrationUiDebugLogTime)
        {
            return;
        }

        nextAbsoluteCalibrationUiDebugLogTime = Time.time + AbsoluteCalibrationUiDebugLogInterval;
        bool rootActive = absoluteCalibrationSphere != null && absoluteCalibrationSphere.activeInHierarchy;
        bool visualRendererEnabled = absoluteCalibrationSphereRenderer != null && absoluteCalibrationSphereRenderer.enabled;
        bool labelActive = absoluteCalibrationSphereLabel != null && absoluteCalibrationSphereLabel.gameObject.activeInHierarchy;
        Vector3 spherePosition = absoluteCalibrationSphere != null ? absoluteCalibrationSphere.transform.position : Vector3.zero;
        float distanceToHmd = hmdTransform != null && absoluteCalibrationSphere != null
            ? Vector3.Distance(hmdTransform.position, spherePosition)
            : -1f;

        Debug.Log("[AMIR Calibration UI] "
            + "sphereRoot activeInHierarchy=" + rootActive
            + ", sphereVisual renderer.enabled=" + visualRendererEnabled
            + ", sphere position=" + FormatVectorForDebug(spherePosition, absoluteCalibrationSphere != null)
            + ", distanceToHmd=" + FormatFloatForDebug(distanceToHmd)
            + ", label active=" + labelActive
            + ", current state=" + absoluteCalibrationState
            + ", isCenterReady=" + isCenterReady
            + ", isPublishingPalmPose=" + isPublishingPalmPose
            + ", center distance=" + FormatFloatForDebug(controlCenterDistance)
            + ", palmWorld=" + FormatVectorForDebug(lastAbsolutePalmWorld, hasLastAbsolutePalmWorld)
            + ", controlCenterWorld=" + FormatVectorForDebug(controlCenterWorld, isCenterReady)
            + ", palmWorld-controlCenterWorld=" + FormatVectorForDebug(lastPalmControlCenterDeltaWorld, hasLastPalmControlCenterDeltaWorld)
            + ", palm msg position=" + FormatVectorForDebug(lastPalmWorldMsgPosition, hasLastPalmWorldMsgPosition)
            + ", control center msg position=" + FormatVectorForDebug(lastControlCenterWorldMsgPosition, hasLastControlCenterWorldMsgPosition));
    }

    private void HideAbsoluteCalibrationButtonVisuals()
    {
        HideAbsoluteCalibrationButtonVisual("AbsoluteCalibrationStartButton", ref startCalibrationButtonObject);
        HideAbsoluteCalibrationButtonVisual("AbsoluteCalibrationSetCenterButton", ref setCenterButtonObject);
    }

    private void HideAbsoluteCalibrationButtonVisual(string objectName, ref GameObject buttonObject)
    {
        if (buttonObject == null)
        {
            Transform buttonTransform = absoluteCalibrationPanel != null
                ? FindChildTransformByName(absoluteCalibrationPanel.transform, objectName)
                : null;
            buttonObject = buttonTransform != null ? buttonTransform.gameObject : null;
        }

        if (buttonObject == null)
        {
            return;
        }

        foreach (Renderer renderer in buttonObject.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }

        foreach (Collider collider in buttonObject.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (ObjectManipulator manipulator in buttonObject.GetComponentsInChildren<ObjectManipulator>(true))
        {
            manipulator.enabled = false;
        }
    }

    private void EnsureStartCalibrationButtonVisual()
    {
        if (startCalibrationButtonObject == null)
        {
            Transform buttonTransform = FindChildTransformByName(absoluteCalibrationPanel.transform, "AbsoluteCalibrationStartButton");
            startCalibrationButtonObject = buttonTransform != null ? buttonTransform.gameObject : null;
        }

        if (startCalibrationButtonObject == null)
        {
            startCalibrationButtonObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            startCalibrationButtonObject.name = "AbsoluteCalibrationStartButton";
            startCalibrationButtonObject.transform.SetParent(absoluteCalibrationPanel.transform, false);
        }

        startCalibrationButtonObject.transform.localPosition = new Vector3(-0.14f, -0.15f, -0.015f);
        startCalibrationButtonObject.transform.localScale = Vector3.one * SetCenterSphereDiameter;
        EnsurePrimitiveMesh(startCalibrationButtonObject, PrimitiveType.Sphere);
        BoxCollider boxCollider = startCalibrationButtonObject.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            Destroy(boxCollider);
        }

        SphereCollider sphereCollider = startCalibrationButtonObject.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = startCalibrationButtonObject.AddComponent<SphereCollider>();
        }

        sphereCollider.isTrigger = true;

        startCalibrationButtonRenderer = startCalibrationButtonObject.GetComponent<Renderer>();
        if (startCalibrationButtonRenderer != null && startCalibrationButtonRenderer.sharedMaterial == null)
        {
            Material material = CreateUnlitMaterial(new Color(0.18f, 0.18f, 0.18f, 1f));
            if (material != null)
            {
                startCalibrationButtonRenderer.material = material;
            }
        }

        Transform labelTransform = FindChildTransformByName(startCalibrationButtonObject.transform, "AbsoluteCalibrationStartButton_Text");
        if (labelTransform == null)
        {
            GameObject labelObject = new GameObject("AbsoluteCalibrationStartButton_Text");
            labelObject.transform.SetParent(startCalibrationButtonObject.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, -0.95f, -0.7f);
            labelObject.transform.localScale = new Vector3(3.2f, 3.2f, 3.2f);
            TextMesh labelText = labelObject.AddComponent<TextMesh>();
            labelText.text = "Start\nCalibration";
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.characterSize = 0.01f;
            labelText.fontSize = 40;
            labelText.color = Color.white;
        }
    }

    private void EnsureSetCenterButtonVisual()
    {
        if (setCenterButtonObject == null)
        {
            Transform buttonTransform = FindChildTransformByName(absoluteCalibrationPanel.transform, "AbsoluteCalibrationSetCenterButton");
            setCenterButtonObject = buttonTransform != null ? buttonTransform.gameObject : null;
        }

        if (setCenterButtonObject == null)
        {
            setCenterButtonObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            setCenterButtonObject.name = "AbsoluteCalibrationSetCenterButton";
            setCenterButtonObject.transform.SetParent(absoluteCalibrationPanel.transform, false);
        }

        setCenterButtonObject.transform.localPosition = new Vector3(0.14f, -0.15f, -0.015f);
        setCenterButtonObject.transform.localScale = Vector3.one * SetCenterSphereDiameter;
        EnsurePrimitiveMesh(setCenterButtonObject, PrimitiveType.Sphere);
        BoxCollider boxCollider = setCenterButtonObject.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            Destroy(boxCollider);
        }

        SphereCollider sphereCollider = setCenterButtonObject.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = setCenterButtonObject.AddComponent<SphereCollider>();
        }

        sphereCollider.isTrigger = true;

        setCenterButtonRenderer = setCenterButtonObject.GetComponent<Renderer>();
        if (setCenterButtonRenderer != null && setCenterButtonRenderer.sharedMaterial == null)
        {
            Material material = CreateUnlitMaterial(new Color(0.18f, 0.18f, 0.18f, 1f));
            if (material != null)
            {
                setCenterButtonRenderer.material = material;
            }
        }

        Transform labelTransform = FindChildTransformByName(setCenterButtonObject.transform, "AbsoluteCalibrationSetCenterButton_Text");
        if (labelTransform == null)
        {
            GameObject labelObject = new GameObject("AbsoluteCalibrationSetCenterButton_Text");
            labelObject.transform.SetParent(setCenterButtonObject.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, -0.8f, -0.7f);
            labelObject.transform.localScale = new Vector3(4f, 4f, 4f);
            TextMesh labelText = labelObject.AddComponent<TextMesh>();
            labelText.text = "Set Center";
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.characterSize = 0.01f;
            labelText.fontSize = 48;
            labelText.color = Color.white;
        }
    }

    private static void EnsurePrimitiveMesh(GameObject target, PrimitiveType primitiveType)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = target.AddComponent<MeshFilter>();
        }

        if (meshFilter.sharedMesh != null && meshFilter.sharedMesh.name.Contains(primitiveType.ToString()))
        {
            return;
        }

        GameObject temp = GameObject.CreatePrimitive(primitiveType);
        MeshFilter tempMeshFilter = temp.GetComponent<MeshFilter>();
        if (tempMeshFilter != null)
        {
            meshFilter.sharedMesh = tempMeshFilter.sharedMesh;
        }

        Destroy(temp);
    }

    private void SetAbsoluteCalibrationPanelVisible(bool visible)
    {
        if (absoluteCalibrationPanel == null)
        {
            absoluteCalibrationPanel = FindSceneObjectByName("AbsoluteCalibrationPanel");
        }

        if (absoluteCalibrationPanel == null)
        {
            return;
        }

        foreach (Renderer renderer in absoluteCalibrationPanel.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }

        foreach (Canvas canvas in absoluteCalibrationPanel.GetComponentsInChildren<Canvas>(true))
        {
            canvas.enabled = visible;
        }

        foreach (Graphic graphic in absoluteCalibrationPanel.GetComponentsInChildren<Graphic>(true))
        {
            graphic.enabled = visible;
        }

        foreach (TMP_Text tmpText in absoluteCalibrationPanel.GetComponentsInChildren<TMP_Text>(true))
        {
            tmpText.enabled = visible;
        }

        foreach (UnityEngine.UI.Text uiText in absoluteCalibrationPanel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            uiText.enabled = visible;
        }

        foreach (Collider collider in absoluteCalibrationPanel.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = visible;
        }

        foreach (ObjectManipulator manipulator in absoluteCalibrationPanel.GetComponentsInChildren<ObjectManipulator>(true))
        {
            manipulator.enabled = visible;
        }
    }

    private void SetAbsoluteCalibrationVisualsVisible(bool visible)
    {
        SetAbsoluteCalibrationPanelVisible(visible);
        if (visible)
        {
            HideAbsoluteCalibrationButtonVisuals();
        }

        if (absoluteCalibrationSphere == null)
        {
            absoluteCalibrationSphere = FindSceneObjectByName(AbsoluteCalibrationSphereRootName);
        }

        if (absoluteCalibrationSphere == null)
        {
            if (!visible)
            {
                return;
            }

            EnsureAbsoluteCalibrationSphere();
            if (absoluteCalibrationSphere == null)
            {
                return;
            }
        }

        absoluteCalibrationSphere.SetActive(visible);
        foreach (Renderer renderer in absoluteCalibrationSphere.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible && renderer.gameObject != absoluteCalibrationSphere;
        }

        foreach (Collider collider in absoluteCalibrationSphere.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = visible && collider.gameObject == absoluteCalibrationSphere;
        }

        foreach (ObjectManipulator manipulator in absoluteCalibrationSphere.GetComponentsInChildren<ObjectManipulator>(true))
        {
            manipulator.enabled = visible;
        }

        if (visible)
        {
            absoluteCalibrationSphere.SetActive(true);
            absoluteCalibrationSphereVisual?.SetActive(true);
            if (absoluteCalibrationSphereRenderer != null)
            {
                absoluteCalibrationSphereRenderer.enabled = true;
            }

            if (absoluteCalibrationSphereLabel != null)
            {
                absoluteCalibrationSphereLabel.gameObject.SetActive(true);
            }
        }
    }

    private void EnsureAbsoluteCalibrationPanelMovable()
    {
        if (absoluteCalibrationPanel == null)
        {
            return;
        }

        BoxCollider panelCollider = absoluteCalibrationPanel.GetComponent<BoxCollider>();
        if (panelCollider == null)
        {
            panelCollider = absoluteCalibrationPanel.AddComponent<BoxCollider>();
        }

        panelCollider.isTrigger = false;
        panelCollider.center = Vector3.zero;
        panelCollider.size = new Vector3(AbsoluteCalibrationPanelWidth, AbsoluteCalibrationPanelHeight, 0.04f);

        ObjectManipulator manipulator = absoluteCalibrationPanel.GetComponent<ObjectManipulator>();
        if (manipulator == null)
        {
            manipulator = absoluteCalibrationPanel.AddComponent<ObjectManipulator>();
        }

        manipulator.enabled = true;
    }

    private void PlaceAbsoluteCalibrationPanelIfNeeded()
    {
        if (!absoluteCalibrationPanelPlaced)
        {
            absoluteCalibrationPanelPlaced = PlaceAbsoluteCalibrationPanelInFrontOfHmd();
        }

        FaceAbsoluteCalibrationPanelToHmd();
    }

    private bool PlaceAbsoluteCalibrationPanelInFrontOfHmd()
    {
        Transform hmdTransform = FindHmdTransformForDebug();
        if (hmdTransform == null || absoluteCalibrationPanel == null)
        {
            return false;
        }

        absoluteCalibrationPanel.transform.position = hmdTransform.position
            + hmdTransform.forward.normalized * calibrationTextForward
            + hmdTransform.up.normalized * calibrationTextUp;
        absoluteCalibrationPanel.transform.rotation = Quaternion.LookRotation(
            absoluteCalibrationPanel.transform.position - hmdTransform.position,
            Vector3.up);
        return true;
    }

    private bool FaceAbsoluteCalibrationPanelToHmd()
    {
        Transform hmdTransform = FindHmdTransformForDebug();
        if (hmdTransform == null || absoluteCalibrationPanel == null)
        {
            return false;
        }

        Vector3 panelForward = absoluteCalibrationPanel.transform.position - hmdTransform.position;
        if (panelForward.sqrMagnitude < 0.0001f)
        {
            panelForward = hmdTransform.forward;
        }

        absoluteCalibrationPanel.transform.rotation = Quaternion.LookRotation(panelForward, Vector3.up);
        return true;
    }

    private void UpdateAbsoluteCalibrationText()
    {
        if (absoluteCalibrationText == null)
        {
            return;
        }

        string instruction = "Place LEFT PALM in the sphere";
        string subInstruction = "Hold still: " + Mathf.CeilToInt(absoluteCountdownSeconds) + " sec";
        if (absoluteCalibrationState == AbsoluteCalibrationState.WaitingForPalm)
        {
            subInstruction = "Hold still: " + Mathf.CeilToInt(absoluteCountdownSeconds) + " sec";
        }
        else if (absoluteCalibrationState == AbsoluteCalibrationState.MovePalmIntoSphere)
        {
            subInstruction = "Hold still: " + Mathf.CeilToInt(absoluteCountdownSeconds) + " sec";
        }
        else if (absoluteCalibrationState == AbsoluteCalibrationState.HoldStill)
        {
            instruction = "Hold still";
            subInstruction = "";
        }
        else if (absoluteCalibrationState == AbsoluteCalibrationState.Countdown)
        {
            instruction = "Hold still";
            subInstruction = "";
        }
        else if (absoluteCalibrationState == AbsoluteCalibrationState.Calibrated)
        {
            instruction = "Center Set";
            subInstruction = "";
        }
        else if (absoluteCalibrationState == AbsoluteCalibrationState.RecalibrationRequired)
        {
            instruction = "Place LEFT PALM in the sphere";
            subInstruction = "Hold still: " + Mathf.CeilToInt(absoluteCountdownSeconds) + " sec";
        }

        absoluteCalibrationText.text = string.IsNullOrEmpty(subInstruction)
            ? instruction
            : "AMIR Center\n" + instruction + "\n" + subInstruction;

        if (absoluteCalibrationCountdownText == null)
        {
            return;
        }

        if (absoluteCalibrationState == AbsoluteCalibrationState.Countdown)
        {
            int countdownValue = Mathf.Clamp(
                Mathf.CeilToInt(absoluteCalibrationCountdownRemaining),
                1,
                Mathf.Max(1, Mathf.CeilToInt(absoluteCountdownSeconds)));
            absoluteCalibrationCountdownText.text = countdownValue.ToString();
            absoluteCalibrationCountdownText.color = Color.white;
        }
        else if (absoluteCalibrationState == AbsoluteCalibrationState.Calibrated)
        {
            absoluteCalibrationCountdownText.text = "";
        }
        else
        {
            absoluteCalibrationCountdownText.text = "";
        }
    }

    private bool TryGetAbsoluteCalibrationTrackingStatus(out string trackingStatus)
    {
        trackingStatus = null;
        ResolveRuntimeReferences();
        if (!useAbsoluteScaledEeCalibration || palmPosePublisher == null || !palmPosePublisher.HasAbsoluteCalibrationCenter)
        {
            return false;
        }

        if (palmPosePublisher.RecalibrationRequired)
        {
            trackingStatus = AbsoluteStatusRecalibrationRequired;
            return true;
        }

        if (!palmPosePublisher.IsLeftPalmTracked && palmPosePublisher.LeftPalmLostDurationSec > 0f)
        {
            trackingStatus = AbsoluteStatusTrackingLost;
            return true;
        }

        return false;
    }

    private static string GetAbsoluteCalibrationStatusText(AbsoluteCalibrationState state)
    {
        switch (state)
        {
            case AbsoluteCalibrationState.WaitingForPalm:
                return "Waiting for palm";
            case AbsoluteCalibrationState.MovePalmIntoSphere:
                return "Move palm into sphere";
            case AbsoluteCalibrationState.HoldStill:
                return "Hold still";
            case AbsoluteCalibrationState.Countdown:
                return "Countdown";
            case AbsoluteCalibrationState.Calibrated:
                return "Center Set";
            case AbsoluteCalibrationState.RecalibrationRequired:
                return AbsoluteStatusRecalibrationRequired;
            default:
                return AbsoluteStatusWaiting;
        }
    }

    private void UpdateStartCalibrationButtonHover()
    {
        ResetStartCalibrationButtonHover(false);
    }

    private void ResetStartCalibrationButtonHover(bool updateColor)
    {
        bool hadHover = startCalibrationButtonHovering || startCalibrationButtonHoverTimer > 0f;
        startCalibrationButtonHoverTimer = 0f;
        startCalibrationButtonHovering = false;
        if (updateColor && hadHover)
        {
            bool publisherEnabled = palmPosePublisher != null && palmPosePublisher.IsPublisherComponentEnabled;
            bool worldPosePublished = palmPosePublisher != null && palmPosePublisher.WorldPublishCount > 0;
            SetStartCalibrationButtonColor(GetStartCalibrationButtonColor(publisherEnabled, worldPosePublished));
        }
    }

    private void UpdateSetCenterButtonHover()
    {
        ResetSetCenterButtonHover(false);
    }

    private void ResetSetCenterButtonHover(bool updateColor)
    {
        bool hadHover = setCenterButtonHovering || setCenterButtonHoverTimer > 0f;
        setCenterButtonHoverTimer = 0f;
        setCenterButtonHovering = false;
        if (updateColor && hadHover)
        {
            bool worldPosePublished = palmPosePublisher != null && palmPosePublisher.WorldPublishCount > 0;
            SetSetCenterButtonColor(GetSetCenterButtonColor(hasLastAbsolutePalmWorld && worldPosePublished));
        }
    }

    private bool TryGetSetCenterButtonHandWorldPosition(out Vector3 handWorldPosition)
    {
        handWorldPosition = Vector3.zero;
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null)
        {
            return false;
        }

        if (!aggregator.TryGetJoint(setCenterHoverJoint, XRNode.LeftHand, out HandJointPose pose))
        {
            return false;
        }

        handWorldPosition = pose.Position;
        return true;
    }

    private bool TryGetPalmWorldForAbsoluteUi(out Vector3 palmWorld)
    {
        palmWorld = Vector3.zero;
        ResolveRuntimeReferences();
        if (palmPosePublisher == null)
        {
            return false;
        }

        return palmPosePublisher.TryGetCurrentLeftPalmWorldPosition(out palmWorld);
    }

    private Color GetStartCalibrationButtonColor(bool publisherEnabled, bool worldPosePublished)
    {
        if (worldPosePublished)
        {
            return new Color(0f, 0.85f, 0.25f, 1f);
        }

        return publisherEnabled ? new Color(0.95f, 0.62f, 0.05f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
    }

    private Color GetSetCenterButtonColor(bool readyToSetCenter)
    {
        if (absoluteCalibrationCalibrated)
        {
            return new Color(0f, 0.85f, 0.25f, 1f);
        }

        return readyToSetCenter ? new Color(0.1f, 0.34f, 0.78f, 1f) : new Color(0.18f, 0.18f, 0.18f, 1f);
    }

    private void SetStartCalibrationButtonColor(Color color)
    {
        if (startCalibrationButtonRenderer == null && startCalibrationButtonObject != null)
        {
            startCalibrationButtonRenderer = startCalibrationButtonObject.GetComponent<Renderer>();
        }

        if (startCalibrationButtonRenderer == null)
        {
            return;
        }

        Material material = startCalibrationButtonRenderer.material;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private void SetSetCenterButtonColor(Color color)
    {
        if (setCenterButtonRenderer == null && setCenterButtonObject != null)
        {
            setCenterButtonRenderer = setCenterButtonObject.GetComponent<Renderer>();
        }

        if (setCenterButtonRenderer == null)
        {
            return;
        }

        Material material = setCenterButtonRenderer.material;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static Material CreateUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogWarning("[AbsoluteCalibration] No compatible shader was found for calibration UI material.");
            return null;
        }

        Material material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static Color WithMinimumAlpha(Color color, float minimumAlpha)
    {
        color.a = Mathf.Clamp01(Mathf.Max(minimumAlpha, color.a));
        return color;
    }

    private string FormatAbsolutePalmWorld()
    {
        if (!hasLastAbsolutePalmWorld)
        {
            return "x=n/a, y=n/a, z=n/a";
        }

        return "x=" + lastAbsolutePalmWorld.x.ToString("F3")
            + ", y=" + lastAbsolutePalmWorld.y.ToString("F3")
            + ", z=" + lastAbsolutePalmWorld.z.ToString("F3");
    }

    private void ChangeObjectColor()
    {
        foreach (GameObject obj in objectGeneration.GenerateObjects)
        {
            ApplyOpaqueColor(obj.gameObject, DefalutColor);
        }
    }

    public float[] AfterObjectMessage ()
    {
        EnsureLists();

        if(active)
        {
            afterList.Clear();
            foreach (var obj in ObjectList)
            {
                afterList.Add(obj.transform.localPosition.x);
                afterList.Add(obj.transform.localPosition.y);
                afterList.Add(obj.transform.localPosition.z);
            }
            active = false;
        }

        if (beforeList.Count < CalibrationValueCount || afterList.Count < CalibrationValueCount)
        {
            return afterData ?? new float[0];
        }

        beforeData = CopyCalibrationValues(beforeList);
        afterData = CopyCalibrationValues(afterList);

        return afterData;
    }

    public float[] BeforeObjectMessage()
    {
        EnsureLists();

        if (beforeList.Count < CalibrationValueCount)
        {
            return beforeData ?? new float[0];
        }

        beforeData = CopyCalibrationValues(beforeList);

        return beforeData;
    }

    public float[] PickObjectMessage()
    {
        EnsureLists();
        beforeData = PickObjectPositionList.ToArray();

        return beforeData;
    }

    //public float[] IRM_SelectMessage()
    //{
    //    var selectCoords = new List<float>();
    //    if (Origin == null)
    //    {
    //        Debug.LogError("Origin が設定されていません！");
    //        return selectCoords.ToArray();
    //    }

    //    // YouBot 向けに追加したいオフセット
    //   /* Vector3 axisOffset = new Vector3(-0.123f, 0.056f, 0f);*/

    //    Vector3 axisOffset = new Vector3(0.0f, 0.0f, 0.0f);

    //    // １度だけ取得しておく Origin のワールド座標
    //    Vector3 originWorld = Origin.transform.position;

    //    foreach (GameObject obj in ObjectList)
    //    {
    //        // 1) ボトルのワールド座標
    //        Vector3 bottleWorld = obj.transform.position;

    //        // 2) ワールド差分で相対位置を計算
    //        Vector3 relative = bottleWorld - originWorld;

    //        // 3) オフセットを加算
    //        Vector3 adjusted = relative + axisOffset;

    //        // 4) YouBot 向けに軸反転・入れ替え
    //        float youbot_x = -adjusted.x;
    //        float youbot_y = -adjusted.z;
    //        float youbot_z = adjusted.y;

    //        // 5) 配列に追加
    //        selectCoords.Add(youbot_x);
    //        selectCoords.Add(youbot_y);
    //        selectCoords.Add(youbot_z);
    //    }

    //    return selectCoords.ToArray();
    //}
   private void ApplyOpaqueColor(GameObject target, Color color)
   {
       if (target == null) return;

       Renderer rend = target.GetComponent<Renderer>();
       if (rend == null)
       {
           rend = target.GetComponentInChildren<Renderer>(true);
       }

       if (rend == null || rend.material == null) return;

       Color visibleColor = color;
       visibleColor.a = 1f;
       rend.material.color = visibleColor;
       SetOpaque(rend.material);
   }

   private void SetOpaque(Material m)
   {
       m.SetFloat("_Surface", 0f);
       m.SetOverrideTag("RenderType", "Opaque");
       m.SetInt("_SrcBlend",  (int) BlendMode.One);
       m.SetInt("_DstBlend",  (int) BlendMode.Zero);
       m.SetInt("_ZWrite",    1);
       m.DisableKeyword("_ALPHATEST_ON");
       m.DisableKeyword("_ALPHABLEND_ON");
       m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
       m.renderQueue = (int) RenderQueue.Geometry;
   }

   private void EnsureLists()
   {
       ObjectList ??= new List<GameObject>();
       beforeList ??= new List<float>();
       afterList ??= new List<float>();
       NotSelectObjectList ??= new List<string>();
       PickObjectList ??= new List<GameObject>();
       PickObjectPositionList ??= new List<float>();
       beforeData ??= new float[0];
       afterData ??= new float[0];
   }

   private void ResetCalibrationSelection()
   {
       ObjectList.Clear();
       beforeList.Clear();
       afterList.Clear();
       beforeData = new float[0];
       afterData = new float[0];
       active = true;
       ResetNotSelectObjectList();
   }

   private void ResetNotSelectObjectList()
   {
       if (objectGeneration != null && objectGeneration.ObjectNameList != null)
       {
           NotSelectObjectList = new List<string>(objectGeneration.ObjectNameList);
           NotSelectObjectList.Remove("CalibrationSphere_Current");
       }
   }

   private static float[] CopyCalibrationValues(List<float> source)
   {
       var values = new float[CalibrationValueCount];
       for (int i = 0; i < CalibrationValueCount; i++)
       {
           values[i] = source[i];
       }
       return values;
   }

   private void UpdateCalibrationSphereDistanceSelection()
   {
       if (!CalibrationMode || calibrationComplete) return;

       EnsureLists();

       if (Time.time < calibrationPointCooldownUntil)
       {
           return;
       }

       if (currentCalibrationSphere == null)
       {
           currentCalibrationSphere = objectGeneration != null ? objectGeneration.GetCurrentCalibrationSphere() : null;
       }

       if (currentCalibrationSphere == null || !currentCalibrationSphere.activeInHierarchy)
       {
           ResetCalibrationPointHover();
           return;
       }

       if (!TryGetCalibrationHandWorldPosition(out Vector3 handWorldPosition))
       {
           ResetCalibrationPointHover();
           return;
       }

       lastDebugHandPosition = handWorldPosition;
       hasLastDebugHandPosition = true;
       float distance = Vector3.Distance(handWorldPosition, currentCalibrationSphere.transform.position);
       lastCalibrationDistance = distance;

       if (Time.time >= nextCalibrationDistanceLogTime)
       {
           Debug.Log("[SelectObject] Hand distance to current point=" + distance.ToString("F3"));
           nextCalibrationDistanceLogTime = Time.time + CalibrationDistanceLogInterval;
       }

       if (distance > CalibrationSphereSelectDistance)
       {
           ResetCalibrationPointHover();
           return;
       }

       if (!calibrationPointHovering)
       {
           calibrationPointHovering = true;
           calibrationPointHoverTimer = 0f;
           string pointLabel = objectGeneration != null
               ? objectGeneration.GetCalibrationPointLabel(currentCalibrationIndex)
               : (currentCalibrationIndex + 1).ToString();
           Debug.Log("[SelectObject] Calibration point " + pointLabel + " hover started.");
       }

       calibrationPointHoverTimer += Time.deltaTime;
       if (calibrationPointHoverTimer >= CalibrationPointHoverSeconds)
       {
           RegisterCurrentCalibrationPoint();
       }
   }

   private void ResetCalibrationPointHover()
   {
       calibrationPointHoverTimer = 0f;
       calibrationPointHovering = false;
   }

   private void RegisterCurrentCalibrationPoint()
   {
       if (currentCalibrationSphere == null) return;

       string pointLabel = objectGeneration != null
           ? objectGeneration.GetCalibrationPointLabel(currentCalibrationIndex)
           : (currentCalibrationIndex + 1).ToString();
       Vector3 worldPosition = currentCalibrationSphere.transform.position;
       beforeList.Add(worldPosition.x);
       beforeList.Add(worldPosition.y);
       beforeList.Add(worldPosition.z);
       ObjectList.Add(currentCalibrationSphere);
       ApplyOpaqueColor(currentCalibrationSphere, SelectColor);
       Debug.Log("[SelectObject] Calibration point " + pointLabel
           + " registered. beforeList.Count=" + beforeList.Count);

       currentCalibrationIndex++;
       calibrationPointCooldownUntil = Time.time + CalibrationPointCooldownSeconds;
       ResetCalibrationPointHover();

       if (currentCalibrationIndex >= CalibrationPointCount)
       {
           objectGeneration.HideCalibrationSphere();
           MarkCalibrationComplete();
           return;
       }

       if (objectGeneration.MoveCalibrationSphereToPoint(currentCalibrationIndex, out Vector3 nextPointPosition))
       {
           currentCalibrationSphere = objectGeneration.GetCurrentCalibrationSphere();
           string nextPointLabel = objectGeneration.GetCalibrationPointLabel(currentCalibrationIndex);
           Debug.Log("[SelectObject] Current calibration point=" + nextPointLabel
               + ", position=" + nextPointPosition
               + ", sphereScale=" + objectGeneration.GetCalibrationSphereDiameter().ToString("F2")
               + ", threshold=" + CalibrationSphereSelectDistance.ToString("F2"));
       }
       else
       {
           Debug.LogError("[SelectObject] Failed to move to calibration point " + (currentCalibrationIndex + 1) + ".");
       }
   }

   private bool TryGetCalibrationHandWorldPosition(out Vector3 handWorldPosition)
   {
       handWorldPosition = Vector3.zero;

       var aggregator = XRSubsystemHelpers.HandsAggregator;
       if (aggregator == null)
       {
           return false;
       }

       if (aggregator.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.LeftHand, out HandJointPose leftMiddlePose))
       {
           handWorldPosition = leftMiddlePose.Position;
           return true;
       }

       if (aggregator.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.RightHand, out HandJointPose rightMiddlePose))
       {
           handWorldPosition = rightMiddlePose.Position;
           return true;
       }

       return false;
   }

   private void UpdateSendZoneHover()
   {
       if (!calibrationComplete || palmPosePublishingStarted) return;

       ResolveRuntimeReferences();

       if (sendZoneSphere == null)
       {
           sendZoneSphere = objectGeneration != null ? objectGeneration.GetSendZoneSphere() : null;
       }

       if (sendZoneSphere == null || !sendZoneSphere.activeInHierarchy)
       {
           ResetSendZoneHover();
           return;
       }

       if (!TryGetAmirControlHandWorldPosition(out Vector3 handWorldPosition))
       {
           ResetSendZoneHover();
           return;
       }

       lastDebugHandPosition = handWorldPosition;
       hasLastDebugHandPosition = true;
       float distance = Vector3.Distance(handWorldPosition, sendZoneSphere.transform.position);
       lastSendZoneDistance = distance;
       if (Time.time >= nextSendZoneDistanceLogTime)
       {
           Debug.Log("[SelectObject] Hand distance to SendZone=" + distance.ToString("F3"));
           nextSendZoneDistanceLogTime = Time.time + CalibrationDistanceLogInterval;
       }

       if (distance > SendZoneSphereSelectDistance)
       {
           ResetSendZoneHover();
           return;
       }

       if (!wasInsideSendZone)
       {
           Debug.Log("[SelectObject] SendZone hover started.");
       }

       wasInsideSendZone = true;
       sendZoneHoverTimer += Time.deltaTime;
       if (sendZoneHoverTimer >= SendZoneSphereHoverSeconds)
       {
           StartPalmPosePublishing();
       }
   }

   private void ResetSendZoneHover()
   {
       if (sendZoneHoverTimer > 0f || wasInsideSendZone)
       {
           Debug.Log("[SelectObject] SendZone hover reset.");
       }
       sendZoneHoverTimer = 0f;
       wasInsideSendZone = false;
   }

   private void ShowSendZoneSphere()
   {
       if (objectGeneration == null)
       {
           Debug.LogError("[SelectObject] Cannot show SendZone sphere because objectGeneration is not assigned.");
           return;
       }

       if (!objectGeneration.ShowSendZoneSphere(out Vector3 sendZonePosition))
       {
           Debug.LogError("[SelectObject] Failed to show SendZone sphere.");
           return;
       }

       sendZoneSphere = objectGeneration.GetSendZoneSphere();
       sendZoneHoverTimer = 0f;
       wasInsideSendZone = false;
       nextSendZoneDistanceLogTime = 0f;
       lastSendZoneDistance = -1f;
       Debug.Log("[SelectObject] SendZone sphere position=" + sendZonePosition
           + ", scale=" + objectGeneration.GetSendZoneSphereDiameter().ToString("F2")
           + ", threshold=" + SendZoneSphereSelectDistance.ToString("F2"));
   }

   private void EnsureDebugPanel()
   {
       if (debugPanel != null && debugText != null) return;

       debugPanel = new GameObject("PalmPublishDebugPanel");

       GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
       background.name = "PalmPublishDebugPanel_Background";
       background.transform.SetParent(debugPanel.transform, false);
       background.transform.localPosition = new Vector3(0f, 0f, 0.01f);
       background.transform.localScale = new Vector3(0.42f, 0.32f, 1f);
       Collider backgroundCollider = background.GetComponent<Collider>();
       if (backgroundCollider != null)
       {
           Destroy(backgroundCollider);
       }

       Renderer backgroundRenderer = background.GetComponent<Renderer>();
       if (backgroundRenderer != null)
       {
           Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
           Material material = shader != null ? new Material(shader) : backgroundRenderer.material;
           material.color = new Color(0f, 0f, 0f, 0.85f);
           backgroundRenderer.material = material;
       }

       GameObject textObject = new GameObject("PalmPublishDebugPanel_Text");
       textObject.transform.SetParent(debugPanel.transform, false);
       textObject.transform.localPosition = new Vector3(-0.19f, 0.14f, 0f);
       debugText = textObject.AddComponent<TextMesh>();
       debugText.anchor = TextAnchor.UpperLeft;
       debugText.alignment = TextAlignment.Left;
       debugText.characterSize = 0.008f;
       debugText.fontSize = 48;
       debugText.color = Color.white;
       debugText.text = "STATE: INIT";
   }

   private void UpdateDebugPanel(bool force)
   {
       if (!showDebugVisuals)
       {
           SetDebugPanelVisualsVisible(false);
           return;
       }

       EnsureDebugPanel();
       SetDebugPanelVisualsVisible(true);

       Transform hmdTransform = FindHmdTransformForDebug();
       if (hmdTransform != null)
       {
           Vector3 forwardFlat = Vector3.ProjectOnPlane(hmdTransform.forward, Vector3.up);
           if (forwardFlat.sqrMagnitude < 0.0001f)
           {
               forwardFlat = hmdTransform.forward;
           }

           debugPanel.transform.position = hmdTransform.position + forwardFlat.normalized * DebugPanelDistance;
           debugPanel.transform.position = new Vector3(
               debugPanel.transform.position.x,
               hmdTransform.position.y + DebugPanelHeightOffset,
               debugPanel.transform.position.z);
           debugPanel.transform.rotation = hmdTransform.rotation;
       }

       if (!force && Time.time < nextDebugPanelUpdateTime) return;
       nextDebugPanelUpdateTime = Time.time + DebugPanelUpdateInterval;

       ResolveRuntimeReferences();
       UpdatePublisherDebugState();
       if (TryGetAmirControlHandWorldPosition(out Vector3 handWorldPosition))
       {
           lastDebugHandPosition = handWorldPosition;
           hasLastDebugHandPosition = true;
       }

       debugText.text =
           "STATE: " + debugState + "\n"
           + "beforeList.Count: " + (beforeList != null ? beforeList.Count : 0) + "\n"
           + "currentCalibrationIndex: " + currentCalibrationIndex + "\n"
           + "currentPointName: " + GetCurrentPointNameForDebug() + "\n"
           + "hand position: " + FormatVectorForDebug(hasLastDebugHandPosition ? lastDebugHandPosition : Vector3.zero, hasLastDebugHandPosition) + "\n"
           + "distance to calibration point: " + FormatFloatForDebug(lastCalibrationDistance) + "\n"
           + "calibration hover timer: " + calibrationPointHoverTimer.ToString("F2") + "\n"
           + "SendZone visible: " + (sendZoneSphere != null && sendZoneSphere.activeInHierarchy) + "\n"
           + "distance to SendZone: " + FormatFloatForDebug(lastSendZoneDistance) + "\n"
           + "SendZone hover timer: " + sendZoneHoverTimer.ToString("F2") + "\n"
           + "centerSphere: " + (absoluteCalibrationSphere != null && absoluteCalibrationSphere.activeInHierarchy) + "\n"
           + "center distance: " + FormatFloatForDebug(controlCenterDistance) + "\n"
           + "isCenterReady: " + isCenterReady + "\n"
           + "isPublishingPalmPose: " + isPublishingPalmPose + "\n"
           + "palmWorld: " + FormatVectorForDebug(lastAbsolutePalmWorld, hasLastAbsolutePalmWorld) + "\n"
           + "controlCenterWorld: " + FormatVectorForDebug(controlCenterWorld, isCenterReady) + "\n"
           + "palmWorld - controlCenterWorld: " + FormatVectorForDebug(lastPalmControlCenterDeltaWorld, hasLastPalmControlCenterDeltaWorld) + "\n"
           + "palm msg position: " + FormatVectorForDebug(lastPalmWorldMsgPosition, hasLastPalmWorldMsgPosition) + "\n"
           + "control center msg position: " + FormatVectorForDebug(lastControlCenterWorldMsgPosition, hasLastControlCenterWorldMsgPosition) + "\n"
           + "publishStarted: " + palmPosePublishingStarted + "\n"
           + "RosTcpComponents: " + FoundMissing(debugRosTcpComponentsFound) + "\n"
           + "handPosePublisher: " + FoundMissing(debugHandPosePublisherFound) + "\n"
           + "handPosePublisher enabled: " + debugHandPosePublisherEnabled + "\n"
           + "airTapPublisher: " + FoundMissing(debugAirTapPublisherFound) + "\n"
           + "airTapPublisher enabled: " + debugAirTapPublisherEnabled + "\n"
           + "palm registered: " + (palmPosePublisher != null && palmPosePublisher.registered) + "\n"
           + "palm rosReady: " + (palmPosePublisher != null && palmPosePublisher.rosReady) + "\n"
           + "palm publish count: " + (palmPosePublisher != null ? palmPosePublisher.publishCount : 0) + "\n"
           + "last palm position: " + FormatVectorForDebug(palmPosePublisher != null ? palmPosePublisher.lastPalmPosition : Vector3.zero, palmPosePublisher != null && palmPosePublisher.publishCount > 0) + "\n"
           + "last palm frame_id: " + (palmPosePublisher != null ? palmPosePublisher.lastFrameId : "n/a") + "\n"
           + "last palm publish time: " + (palmPosePublisher != null && palmPosePublisher.lastPublishTime > 0f ? palmPosePublisher.lastPublishTime.ToString("F2") : "n/a") + "\n"
           + "airTap registered: " + (graspCommandPublisher != null && graspCommandPublisher.registered) + "\n"
           + "airTap publish count: " + (graspCommandPublisher != null ? graspCommandPublisher.publishCount : 0);
   }

   private void SetDebugPanelVisualsVisible(bool visible)
   {
       if (debugPanel == null)
       {
           debugPanel = FindSceneObjectByName("PalmPublishDebugPanel");
       }

       if (debugPanel == null)
       {
           return;
       }

       foreach (Renderer renderer in debugPanel.GetComponentsInChildren<Renderer>(true))
       {
           renderer.enabled = visible;
       }

       foreach (Canvas canvas in debugPanel.GetComponentsInChildren<Canvas>(true))
       {
           canvas.enabled = visible;
       }

       foreach (Graphic graphic in debugPanel.GetComponentsInChildren<Graphic>(true))
       {
           graphic.enabled = visible;
       }

       foreach (TMP_Text tmpText in debugPanel.GetComponentsInChildren<TMP_Text>(true))
       {
           tmpText.enabled = visible;
       }

       foreach (UnityEngine.UI.Text uiText in debugPanel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
       {
           uiText.enabled = visible;
       }
   }

   private void UpdatePublisherDebugState()
   {
       debugRosTcpComponentsFound = rosTcpComponents != null;
       debugHandPosePublisherFound = palmPosePublisher != null;
       debugAirTapPublisherFound = graspCommandPublisher != null;
       debugHandPosePublisherEnabled = palmPosePublisher != null && palmPosePublisher.enabled;
       debugAirTapPublisherEnabled = graspCommandPublisher != null && graspCommandPublisher.enabled;
   }

   private string GetCurrentPointNameForDebug()
   {
       if (objectGeneration == null || currentCalibrationIndex >= CalibrationPointCount)
       {
           return "Done";
       }

       return objectGeneration.GetCalibrationPointLabel(currentCalibrationIndex);
   }

   private static string FoundMissing(bool found)
   {
       return found ? "found" : "missing";
   }

   private static string FormatFloatForDebug(float value)
   {
       return value >= 0f ? value.ToString("F3") : "n/a";
   }

   private static string FormatVectorForDebug(Vector3 value, bool valid)
   {
       if (!valid) return "n/a";
       return "(" + value.x.ToString("F2") + ", " + value.y.ToString("F2") + ", " + value.z.ToString("F2") + ")";
   }

   private static Transform FindHmdTransformForDebug()
   {
       if (Camera.main != null)
       {
           return Camera.main.transform;
       }

       GameObject taggedCamera = GameObject.FindGameObjectWithTag("MainCamera");
       if (taggedCamera != null)
       {
           return taggedCamera.transform;
       }

       GameObject centerEyeAnchor = FindSceneObjectByName("CenterEyeAnchor");
       if (centerEyeAnchor != null)
       {
           return centerEyeAnchor.transform;
       }

       GameObject xrOrigin = FindSceneObjectByName("XROrigin") ?? FindSceneObjectByName("XR Origin");
       if (xrOrigin != null)
       {
           Camera xrCamera = xrOrigin.GetComponentInChildren<Camera>(true);
           if (xrCamera != null)
           {
               return xrCamera.transform;
           }
       }

       GameObject ovrCameraRig = FindSceneObjectByName("OVRCameraRig");
       if (ovrCameraRig != null)
       {
           Transform centerEye = FindChildTransformByName(ovrCameraRig.transform, "CenterEyeAnchor");
           if (centerEye != null)
           {
               return centerEye;
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

   private bool TryGetAmirControlHandWorldPosition(out Vector3 handWorldPosition)
   {
       handWorldPosition = Vector3.zero;

       var aggregator = XRSubsystemHelpers.HandsAggregator;
       if (aggregator == null)
       {
           LogMissingLeftHand("HandsAggregator is not available; RightHand fallback is disabled");
           return false;
       }

       if (!aggregator.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.LeftHand, out HandJointPose leftMiddlePose))
       {
           LogMissingLeftHand("LeftHand MiddleProximal is not tracked; RightHand fallback is disabled");
           return false;
       }

       handWorldPosition = leftMiddlePose.Position;
       return true;
   }

   private void LogAmirLeftHandOnlyModeOnce()
   {
       if (loggedAmirLeftHandOnlyMode || !Application.isPlaying)
       {
           return;
       }

       loggedAmirLeftHandOnlyMode = true;
       Debug.Log("[SelectObject] AMIR SendZone and /palm_pose start use XRNode.LeftHand only. RightHand fallback is disabled.");
   }

   private void LogMissingLeftHand(string reason)
   {
       if (!Application.isPlaying || Time.time < nextMissingLeftHandLogTime)
       {
           return;
       }

       nextMissingLeftHandLogTime = Time.time + MissingLeftHandLogInterval;
       Debug.Log("[SelectObject] AMIR control skipped: " + reason + ".");
   }

   private bool TryGetHandWorldPosition(out Vector3 handWorldPosition)
   {
       handWorldPosition = Vector3.zero;

       var aggregator = XRSubsystemHelpers.HandsAggregator;
       if (aggregator == null ||
           !aggregator.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.LeftHand, out HandJointPose middlePose))
       {
           return false;
       }

       if (sendZoneHandTracking != null && sendZoneHandTracking.middleObject != null)
       {
           handWorldPosition = sendZoneHandTracking.middleObject.transform.position;
           return true;
       }

       handWorldPosition = middlePose.Position;
       return true;
   }

   private bool IsInsideSendZone(Vector3 handWorldPosition)
   {
       Collider[] colliders = sendZone.GetComponentsInChildren<Collider>(true);
       foreach (Collider zoneCollider in colliders)
       {
           if (zoneCollider == null || !zoneCollider.enabled) continue;

           Bounds expandedBounds = zoneCollider.bounds;
           expandedBounds.Expand(0.02f);
           if (expandedBounds.Contains(handWorldPosition))
           {
               return true;
           }

           if (Vector3.Distance(zoneCollider.ClosestPoint(handWorldPosition), handWorldPosition) <= 0.02f)
           {
               return true;
           }
       }

       return Vector3.Distance(sendZone.transform.position, handWorldPosition) <= sendZoneRadius;
   }

   private void StartPalmPosePublishing()
   {
       debugState = "CENTER GATE REQUESTED";
       BeginPalmPoseCenterGate("StartPalmPosePublishing");
   }

   private void PublishControlCenterWorld(bool force)
   {
       if (!Application.isPlaying || !isCenterReady)
       {
           return;
       }

       if (!force && Time.time < nextControlCenterWorldPublishTime)
       {
           return;
       }

       nextControlCenterWorldPublishTime = Time.time + (1f / Mathf.Max(0.1f, controlCenterWorldPublishHz));
       if (!EnsureControlCenterWorldPublisher())
       {
           return;
       }

       if (controlCenterWorldMessage == null)
       {
           InitializeControlCenterWorldMessage();
       }

       controlCenterWorldMessage.header.stamp = GetRosTime();
       controlCenterWorldMessage.header.frame_id = controlCenterWorldFrameId;
       ApplyUnityWorldPose(controlCenterWorldMessage, controlCenterWorld);
       lastControlCenterWorldMsgPosition = ConvertUnityWorldToPublishedWorldPosition(controlCenterWorld);
       hasLastControlCenterWorldMsgPosition = true;
       controlCenterRos.Publish(controlCenterWorldTopic, controlCenterWorldMessage);
   }

   private bool EnsureControlCenterWorldPublisher()
   {
       if (!Application.isPlaying)
       {
           return false;
       }

       controlCenterRos ??= ROSConnection.GetOrCreateInstance();
       if (controlCenterRos == null)
       {
           return false;
       }

       if (registeredControlCenterWorldTopic != controlCenterWorldTopic)
       {
           controlCenterRos.RegisterPublisher<RosMessageTypes.Geometry.PoseStampedMsg>(controlCenterWorldTopic);
           registeredControlCenterWorldTopic = controlCenterWorldTopic;
           Debug.Log("[PalmPoseCenterGate] RegisterPublisher " + controlCenterWorldTopic);
       }

       return true;
   }

   private void InitializeControlCenterWorldMessage()
   {
       controlCenterWorldMessage = new RosMessageTypes.Geometry.PoseStampedMsg
       {
           header = new RosMessageTypes.Std.HeaderMsg(),
           pose = new RosMessageTypes.Geometry.PoseMsg
           {
               position = new RosMessageTypes.Geometry.PointMsg(),
               orientation = new RosMessageTypes.Geometry.QuaternionMsg()
           }
       };
   }

   private static RosMessageTypes.BuiltinInterfaces.TimeMsg GetRosTime()
   {
       double time = Time.realtimeSinceStartup;
       int wholeSeconds = Mathf.FloorToInt((float)time);
       uint nanoseconds = (uint)((time - wholeSeconds) * 1000000000.0);

#if ROS2
       int seconds = wholeSeconds;
#else
       uint seconds = (uint)wholeSeconds;
#endif

       return new RosMessageTypes.BuiltinInterfaces.TimeMsg
       {
           sec = seconds,
           nanosec = nanoseconds
       };
   }

   private void UpdatePalmControlCenterDebugPositions(Vector3 palmWorld)
   {
       lastPalmWorldMsgPosition = ConvertUnityWorldToPublishedWorldPosition(palmWorld);
       lastControlCenterWorldMsgPosition = ConvertUnityWorldToPublishedWorldPosition(controlCenterWorld);
       lastPalmControlCenterDeltaWorld = palmWorld - controlCenterWorld;
       hasLastPalmWorldMsgPosition = true;
       hasLastControlCenterWorldMsgPosition = true;
       hasLastPalmControlCenterDeltaWorld = true;
   }

   private static void ApplyUnityWorldPose(RosMessageTypes.Geometry.PoseStampedMsg message, Vector3 unityPosition)
   {
       Vector3 position = ConvertUnityWorldToPublishedWorldPosition(unityPosition);
       message.pose.position.x = position.x;
       message.pose.position.y = position.y;
       message.pose.position.z = position.z;
       message.pose.orientation.x = 0.0;
       message.pose.orientation.y = 0.0;
       message.pose.orientation.z = 0.0;
       message.pose.orientation.w = 1.0;
   }

   private static Vector3 ConvertUnityWorldToPublishedWorldPosition(Vector3 unityPosition)
   {
       return new Vector3(unityPosition.x, unityPosition.y, unityPosition.z);
   }

   private void SetCalibrationPointPublishersEnabled(bool enabled)
   {
       if (beforeObjectFloat32 != null) beforeObjectFloat32.enabled = enabled;
       if (afterObjectFloat32 != null) afterObjectFloat32.enabled = enabled;
   }

   private void SetPalmPosePublishersEnabled(bool enabled, bool logMissing)
   {
       ResolveRuntimeReferences();

       if (palmPosePublisher != null)
       {
           palmPosePublisher.enabled = enabled;
       }
       else if (logMissing)
       {
           Debug.LogError("[SelectObject] handPosePublisher was not found on RosTcpComponents.");
       }

       if (graspCommandPublisher != null)
       {
           graspCommandPublisher.enabled = enabled;
       }
       else if (logMissing)
       {
           Debug.LogError("[SelectObject] airTapPublisher was not found on RosTcpComponents.");
       }
   }

   private void SetSendZoneActive(bool enabled)
   {
       ResolveRuntimeReferences();
       if (sendZone == null)
       {
           if (enabled)
           {
               Debug.LogError("[SelectObject] SendZone GameObject was not found.");
           }
           return;
       }

       sendZone.SetActive(enabled);
   }

   private void ResolveRuntimeReferences()
   {
       if (sendZone == null && !string.IsNullOrWhiteSpace(sendZoneName))
       {
           sendZone = FindSceneObjectByName(sendZoneName);
       }

       if (sendZoneHandTracking == null)
       {
           sendZoneHandTracking = FindObjectOfType<handTracking>(true);
       }

       if (rosTcpComponents == null)
       {
           rosTcpComponents = GameObject.Find("RosTcpComponents");
       }

       if (rosTcpComponents == null)
       {
           rosTcpComponents = FindSceneObjectByName("RosTcpComponents");
       }

       if (rosTcpComponents == null)
       {
           rosTcpComponents = FindSceneObjectByName("RosConnector");
       }

        if (rosTcpComponents != null)
        {
            palmPosePublisher = rosTcpComponents.GetComponent<handPosePublisher>();
            graspCommandPublisher = rosTcpComponents.GetComponent<airTapPublisher>();
        }

        if (palmPosePublisher == null)
        {
            palmPosePublisher = FindObjectOfType<handPosePublisher>(true);
        }

        if (graspCommandPublisher == null)
        {
            graspCommandPublisher = FindObjectOfType<airTapPublisher>(true);
        }
    }

   private static GameObject FindSceneObjectByName(string objectName)
   {
       foreach (Transform candidate in FindObjectsOfType<Transform>(true))
       {
           if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
           {
               return candidate.gameObject;
           }
       }

       return null;
   }

}
