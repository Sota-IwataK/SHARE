using System;
using System.Collections.Generic;
using System.Reflection;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class PhotonSharedMRGestureMenuController : MonoBehaviour
{
    private enum SwipeReference
    {
        PalmThenWrist,
        WristThenPalm
    }

    private struct GesturePose
    {
        public bool handTracked;
        public bool shapeMatched;
        public bool poseReady;
        public string blockReason;
        public Vector3 swipeWorldPosition;
        public Vector3 swipeHmdLocalPosition;
    }

    private struct SwipeSample
    {
        public float time;
        public Vector3 hmdLocalPosition;
    }

    [Header("Scene References")]
    public PhotonSharedMRMenuPanel menuPanel;
    public PhotonSharedMRLoginPanel loginPanel;
    public Transform hmdTransform;
    public OVRHand rightOvrHand;
    public RightHandMecanumControl rightHandMecanumControl;
    public PinchDistanceGripperController gripperController;

    [Header("Gesture Finger Pose")]
    [Range(0f, 1f)] public float indexExtendedThreshold = 0.75f;
    [Range(0f, 1f)] public float middleExtendedThreshold = 0.75f;
    public bool suppressWhenRingOrLittleExtended = true;
    [Range(0f, 1f)] public float ringLittleExtendedThreshold = 0.75f;
    public float thumbIndexPinchDistance = 0.04f;
    [Range(0f, 1f)] public float ovrIndexPinchBlockThreshold = 0.75f;
    [SerializeField] private SwipeReference swipeReference = SwipeReference.PalmThenWrist;

    [Header("Swipe Detection")]
    public float downwardSwipeDistance = 0.14f;
    public float downwardSwipeVelocity = 0.45f;
    public float swipeWindowSeconds = 0.45f;
    public float poseHoldSeconds = 0.12f;
    public float cooldownSeconds = 1.0f;
    public float releasePoseSeconds = 0.25f;

    [Header("Menu Placement")]
    public float menuDistanceFromHead = 0.80f;
    public float menuHorizontalOffset = 0.15f;
    public float menuVerticalOffset = 0.12f;
    public float menuScale = 0.001f;
    public bool faceCameraOnOpen = true;
    public bool keepWorldFixedWhileOpen = true;

    [Header("Conflict Blocks")]
    public bool blockWhenRightMecanumActive = true;
    public bool blockWhenGripperPinchActive = true;
    public bool blockWhenLocalPhotonBottleGrabActive = true;
    public bool blockWhenArmControlActive = true;
    public bool blockWhenModalUiOpen = true;
    public string[] modalUiObjectNames =
    {
        "LoginPanelCanvas",
        "LoginPanel",
        "BottleManualSpawnCanvas",
        "SpawnBottleButtonCollectionCanvas",
        "RightHandMecanumPopupCanvas",
        "RightHandMecanumPopupPanel"
    };

    [Header("Debug")]
    public bool enableGestureDebugLogs = false;

    private readonly List<SwipeSample> swipeSamples = new List<SwipeSample>(32);
    private bool armed;
    private bool waitingForRelease;
    private float poseHeldSince = -1f;
    private float releaseStartedAt = -1f;
    private float cooldownUntil = -999f;
    private float lastBlockedLogTime = -999f;
    private string lastBlockedReason = string.Empty;
    private bool lastObservedMenuVisible;
    private void Awake()
    {
        ResolveReferences();
        if (menuPanel != null)
        {
            menuPanel.SetMenuVisible(menuPanel.showOnStart);
            lastObservedMenuVisible = menuPanel.IsMenuVisible;
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResetArmState();
        lastObservedMenuVisible = menuPanel != null && menuPanel.IsMenuVisible;
    }

    private void Update()
    {
        ResolveReferences();
        if (!TryGetGesturePose(out GesturePose pose))
        {
            if (waitingForRelease)
            {
                UpdateReleaseGate(default);
            }

            ResetArmState();
            ObserveExternalMenuVisibility();
            return;
        }

        if (waitingForRelease)
        {
            UpdateReleaseGate(pose);
            ObserveExternalMenuVisibility();
            return;
        }

        if (Time.unscaledTime < cooldownUntil)
        {
            ResetArmState();
            ObserveExternalMenuVisibility();
            return;
        }

        if (!pose.poseReady)
        {
            if (pose.shapeMatched && !string.IsNullOrEmpty(pose.blockReason))
            {
                LogBlocked(pose.blockReason);
            }

            ResetArmState();
            ObserveExternalMenuVisibility();
            return;
        }

        if (TryGetContextBlockReason(out string contextBlockReason))
        {
            LogBlocked(contextBlockReason);
            ResetArmState();
            ObserveExternalMenuVisibility();
            return;
        }

        UpdateArmedSwipe(pose);
        ObserveExternalMenuVisibility();
    }

    private void OnValidate()
    {
        indexExtendedThreshold = Mathf.Clamp01(indexExtendedThreshold);
        middleExtendedThreshold = Mathf.Clamp01(middleExtendedThreshold);
        ringLittleExtendedThreshold = Mathf.Clamp01(ringLittleExtendedThreshold);
        thumbIndexPinchDistance = Mathf.Max(0.001f, thumbIndexPinchDistance);
        ovrIndexPinchBlockThreshold = Mathf.Clamp01(ovrIndexPinchBlockThreshold);
        downwardSwipeDistance = Mathf.Max(0.001f, downwardSwipeDistance);
        downwardSwipeVelocity = Mathf.Max(0.001f, downwardSwipeVelocity);
        swipeWindowSeconds = Mathf.Max(0.05f, swipeWindowSeconds);
        poseHoldSeconds = Mathf.Max(0f, poseHoldSeconds);
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        releasePoseSeconds = Mathf.Max(0f, releasePoseSeconds);
        menuDistanceFromHead = Mathf.Max(0.05f, menuDistanceFromHead);
        menuVerticalOffset = Mathf.Max(0f, menuVerticalOffset);
        menuScale = Mathf.Max(0.0001f, menuScale);
    }

    private void ResolveReferences()
    {
        if (menuPanel == null)
        {
            menuPanel = FindObjectOfType<PhotonSharedMRMenuPanel>(true);
        }

        if (loginPanel == null)
        {
            loginPanel = FindObjectOfType<PhotonSharedMRLoginPanel>(true);
        }

        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
        }

        if (rightHandMecanumControl == null)
        {
            rightHandMecanumControl = FindObjectOfType<RightHandMecanumControl>(true);
        }

        if (gripperController == null)
        {
            gripperController = FindObjectOfType<PinchDistanceGripperController>(true);
        }

        if (rightOvrHand == null)
        {
            rightOvrHand = FindRightOvrHand();
        }
    }

    private bool TryGetGesturePose(out GesturePose pose)
    {
        pose = new GesturePose();
        Transform hmd = ResolveHmdTransform();
        if (hmd == null)
        {
            return false;
        }

        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null)
        {
            return false;
        }

        if (!IsRightOvrHandStable())
        {
            return false;
        }

        if (!TryGetJoint(TrackedHandJoint.Palm, out HandJointPose palmPose) &&
            !TryGetJoint(TrackedHandJoint.Wrist, out palmPose))
        {
            return false;
        }

        if (!TryGetJoint(TrackedHandJoint.Wrist, out HandJointPose wristPose))
        {
            wristPose = palmPose;
        }

        pose.handTracked = true;
        Vector3 palmPosition = palmPose.Position;

        if (!TryGetFingerExtensionScore(
                TrackedHandJoint.IndexMetacarpal,
                TrackedHandJoint.IndexProximal,
                TrackedHandJoint.IndexIntermediate,
                TrackedHandJoint.IndexDistal,
                TrackedHandJoint.IndexTip,
                palmPosition,
                out float indexScore) ||
            !TryGetFingerExtensionScore(
                TrackedHandJoint.MiddleMetacarpal,
                TrackedHandJoint.MiddleProximal,
                TrackedHandJoint.MiddleIntermediate,
                TrackedHandJoint.MiddleDistal,
                TrackedHandJoint.MiddleTip,
                palmPosition,
                out float middleScore))
        {
            return false;
        }

        bool indexExtended = indexScore >= indexExtendedThreshold;
        bool middleExtended = middleScore >= middleExtendedThreshold;
        if (!indexExtended || !middleExtended)
        {
            return false;
        }

        pose.shapeMatched = true;

        if (IsThumbIndexPinching(palmPosition))
        {
            pose.blockReason = "thumb_index_pinch";
            return true;
        }

        if (suppressWhenRingOrLittleExtended && AreRingOrLittleClearlyExtended(palmPosition))
        {
            pose.blockReason = "ring_or_little_extended";
            return true;
        }

        pose.swipeWorldPosition = swipeReference == SwipeReference.PalmThenWrist
            ? palmPose.Position
            : wristPose.Position;
        pose.swipeHmdLocalPosition = hmd.InverseTransformPoint(pose.swipeWorldPosition);
        pose.poseReady = true;
        return true;
    }

    private bool TryGetJoint(TrackedHandJoint joint, out HandJointPose pose)
    {
        pose = default;
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        return aggregator != null && aggregator.TryGetJoint(joint, XRNode.RightHand, out pose);
    }

    private bool TryGetFingerExtensionScore(
        TrackedHandJoint metacarpalJoint,
        TrackedHandJoint proximalJoint,
        TrackedHandJoint intermediateJoint,
        TrackedHandJoint distalJoint,
        TrackedHandJoint tipJoint,
        Vector3 palmPosition,
        out float score)
    {
        score = 0f;
        if (!TryGetJoint(proximalJoint, out HandJointPose proximalPose) ||
            !TryGetJoint(tipJoint, out HandJointPose tipPose))
        {
            return false;
        }

        Vector3 proximalPosition = proximalPose.Position;
        Vector3 tipPosition = tipPose.Position;
        float palmToProximal = Vector3.Distance(palmPosition, proximalPosition);
        float palmToTip = Vector3.Distance(palmPosition, tipPosition);
        float reachScore = Mathf.InverseLerp(0.015f, 0.075f, palmToTip - palmToProximal);
        float straightnessScore = 1f;

        if (TryGetJoint(intermediateJoint, out HandJointPose intermediatePose) &&
            TryGetJoint(distalJoint, out HandJointPose distalPose))
        {
            Vector3 baseDirection = intermediatePose.Position - proximalPosition;
            Vector3 tipDirection = tipPosition - distalPose.Position;
            if (baseDirection.sqrMagnitude > 0.000001f && tipDirection.sqrMagnitude > 0.000001f)
            {
                straightnessScore = Mathf.Clamp01((Vector3.Dot(baseDirection.normalized, tipDirection.normalized) + 1f) * 0.5f);
            }
        }
        else if (TryGetJoint(metacarpalJoint, out HandJointPose metacarpalPose))
        {
            Vector3 baseDirection = proximalPosition - metacarpalPose.Position;
            Vector3 tipDirection = tipPosition - proximalPosition;
            if (baseDirection.sqrMagnitude > 0.000001f && tipDirection.sqrMagnitude > 0.000001f)
            {
                straightnessScore = Mathf.Clamp01((Vector3.Dot(baseDirection.normalized, tipDirection.normalized) + 1f) * 0.5f);
            }
        }

        score = Mathf.Clamp01((reachScore * 0.8f) + (straightnessScore * 0.2f));
        return true;
    }

    private bool IsThumbIndexPinching(Vector3 palmPosition)
    {
        if (rightOvrHand != null && rightOvrHand.IsTracked &&
            rightOvrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index) >= ovrIndexPinchBlockThreshold)
        {
            return true;
        }

        if (TryGetJoint(TrackedHandJoint.ThumbTip, out HandJointPose thumbPose) &&
            TryGetJoint(TrackedHandJoint.IndexTip, out HandJointPose indexPose))
        {
            return Vector3.Distance(thumbPose.Position, indexPose.Position) <= thumbIndexPinchDistance;
        }

        return false;
    }

    private bool IsRightOvrHandStable()
    {
        if (rightOvrHand == null)
        {
            return true;
        }

        return rightOvrHand.IsTracked && rightOvrHand.IsDataValid && rightOvrHand.IsDataHighConfidence;
    }

    private bool AreRingOrLittleClearlyExtended(Vector3 palmPosition)
    {
        bool ringExtended = TryGetFingerExtensionScore(
            TrackedHandJoint.RingMetacarpal,
            TrackedHandJoint.RingProximal,
            TrackedHandJoint.RingIntermediate,
            TrackedHandJoint.RingDistal,
            TrackedHandJoint.RingTip,
            palmPosition,
            out float ringScore) && ringScore >= ringLittleExtendedThreshold;

        bool littleExtended = TryGetFingerExtensionScore(
            TrackedHandJoint.LittleMetacarpal,
            TrackedHandJoint.LittleProximal,
            TrackedHandJoint.LittleIntermediate,
            TrackedHandJoint.LittleDistal,
            TrackedHandJoint.LittleTip,
            palmPosition,
            out float littleScore) && littleScore >= ringLittleExtendedThreshold;

        return ringExtended || littleExtended;
    }

    private void UpdateArmedSwipe(GesturePose pose)
    {
        float now = Time.unscaledTime;
        if (poseHeldSince < 0f)
        {
            poseHeldSince = now;
        }

        if (now - poseHeldSince < poseHoldSeconds)
        {
            return;
        }

        if (!armed)
        {
            armed = true;
            swipeSamples.Clear();
            LogDebug("PHOTON_MENU_GESTURE_ARMED");
        }

        AddSwipeSample(now, pose.swipeHmdLocalPosition);
        if (!TryDetectDownwardSwipe(now, out float distance, out float velocity))
        {
            return;
        }

        LogDebug("PHOTON_MENU_GESTURE_SWIPE distance=" + distance.ToString("F3")
            + " velocity=" + velocity.ToString("F3"));
        ToggleMenuFromGesture();
        cooldownUntil = now + cooldownSeconds;
        waitingForRelease = true;
        releaseStartedAt = -1f;
        ResetArmState();
    }

    private void AddSwipeSample(float now, Vector3 hmdLocalPosition)
    {
        swipeSamples.Add(new SwipeSample
        {
            time = now,
            hmdLocalPosition = hmdLocalPosition
        });

        float oldestAllowed = now - swipeWindowSeconds;
        for (int i = swipeSamples.Count - 1; i >= 0; i--)
        {
            if (swipeSamples[i].time < oldestAllowed)
            {
                swipeSamples.RemoveAt(i);
            }
        }
    }

    private bool TryDetectDownwardSwipe(float now, out float distance, out float velocity)
    {
        distance = 0f;
        velocity = 0f;
        if (swipeSamples.Count < 2)
        {
            return false;
        }

        SwipeSample current = swipeSamples[swipeSamples.Count - 1];
        SwipeSample highest = current;
        for (int i = 0; i < swipeSamples.Count; i++)
        {
            if (swipeSamples[i].hmdLocalPosition.y > highest.hmdLocalPosition.y)
            {
                highest = swipeSamples[i];
            }
        }

        float dt = Mathf.Max(0.0001f, current.time - highest.time);
        distance = highest.hmdLocalPosition.y - current.hmdLocalPosition.y;
        velocity = distance / dt;
        return distance >= downwardSwipeDistance && velocity >= downwardSwipeVelocity && now - highest.time <= swipeWindowSeconds;
    }

    private void UpdateReleaseGate(GesturePose pose)
    {
        if (pose.poseReady || pose.shapeMatched)
        {
            releaseStartedAt = -1f;
            return;
        }

        if (releaseStartedAt < 0f)
        {
            releaseStartedAt = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - releaseStartedAt >= releasePoseSeconds)
        {
            waitingForRelease = false;
            releaseStartedAt = -1f;
        }
    }

    private void ToggleMenuFromGesture()
    {
        if (menuPanel == null)
        {
            LogBlocked("menu_panel_missing");
            return;
        }

        bool open = !menuPanel.IsMenuVisible;
        if (open)
        {
            menuPanel.OpenAtHead(
                ResolveHmdTransform(),
                menuDistanceFromHead,
                menuHorizontalOffset,
                menuVerticalOffset,
                menuScale,
                faceCameraOnOpen);
        }
        else
        {
            menuPanel.CloseMenu();
        }

        lastObservedMenuVisible = open;
        LogDebug(open ? "PHOTON_MENU_OPEN" : "PHOTON_MENU_CLOSE");
    }

    private bool TryGetContextBlockReason(out string reason)
    {
        reason = string.Empty;
        if (blockWhenRightMecanumActive && IsRightMecanumActive())
        {
            reason = "right_hand_mecanum_active";
            return true;
        }

        if (blockWhenGripperPinchActive && IsGripperPinchActive())
        {
            reason = "gripper_pinch_active";
            return true;
        }

        if (blockWhenLocalPhotonBottleGrabActive && IsLocalPhotonBottleGrabActive())
        {
            reason = "local_photon_bottle_grab_active";
            return true;
        }

        if (blockWhenArmControlActive && IsArmControlActive())
        {
            reason = "arm_control_active";
            return true;
        }

        if (blockWhenModalUiOpen && IsBlockingModalUiOpen())
        {
            reason = "modal_ui_open";
            return true;
        }

        return false;
    }

    private bool IsRightMecanumActive()
    {
        if (rightHandMecanumControl == null || !rightHandMecanumControl.isActiveAndEnabled)
        {
            return false;
        }

        if (TryReadBoolMember(rightHandMecanumControl, "lastActive", out bool lastActive) && lastActive)
        {
            return true;
        }

        if (TryReadMember(rightHandMecanumControl, "state", out object stateValue))
        {
            string state = stateValue != null ? stateValue.ToString() : string.Empty;
            return state == "RIGHT_HAND_HOLDING" ||
                   state == "MOVE_READY_POPUP" ||
                   state == "MECANUM_CONTROL" ||
                   state == "HAND_OPEN_STOP";
        }

        return false;
    }

    private bool IsGripperPinchActive()
    {
        if (gripperController == null || !gripperController.isActiveAndEnabled)
        {
            return false;
        }

        if (TryReadMember(gripperController, "LastDistance", out object distanceValue) && distanceValue is float distance)
        {
            float threshold = thumbIndexPinchDistance;
            if (TryReadMember(gripperController, "closeThreshold", out object thresholdValue) && thresholdValue is float closeThreshold)
            {
                threshold = closeThreshold + 0.005f;
            }

            return distance > 0f && distance <= threshold;
        }

        return false;
    }

    private bool IsLocalPhotonBottleGrabActive()
    {
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject != null && sharedObject.IsPhotonSharedNetworkBottle && sharedObject.IsLocalGrabActive)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsArmControlActive()
    {
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            Type type = behaviour.GetType();
            if (type.Name != "SelectObject")
            {
                continue;
            }

            if (TryReadBoolMember(behaviour, "CalibrationMode", out bool calibrationMode) && calibrationMode)
            {
                return true;
            }

            if (TryReadBoolMember(behaviour, "MoveMode", out bool moveMode) && moveMode)
            {
                return true;
            }

            if (TryReadBoolMember(behaviour, "isPublishingPalmPose", out bool publishingPalmPose) && publishingPalmPose)
            {
                return true;
            }

            if (TryReadBoolMember(behaviour, "palmPosePublishingStarted", out bool palmPosePublishingStarted) && palmPosePublishingStarted)
            {
                return true;
            }

            if (TryReadBoolMember(behaviour, "absoluteCalibrationActive", out bool absoluteCalibrationActive) && absoluteCalibrationActive)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBlockingModalUiOpen()
    {
        if (modalUiObjectNames == null || modalUiObjectNames.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < modalUiObjectNames.Length; i++)
        {
            string objectName = modalUiObjectNames[i];
            if (string.IsNullOrWhiteSpace(objectName))
            {
                continue;
            }

            GameObject obj = FindSceneObjectByName(objectName);
            if (obj == null || !obj.activeInHierarchy)
            {
                continue;
            }

            if (menuPanel != null && menuPanel.PanelTransform != null && obj.transform.IsChildOf(menuPanel.PanelTransform))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private GameObject FindSceneObjectByName(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, objectName);
            if (found != null)
            {
                return found.gameObject;
            }
        }

        return null;
    }

    private static Transform FindChildRecursive(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == objectName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static OVRHand FindRightOvrHand()
    {
        OVRHand[] hands = FindObjectsOfType<OVRHand>(true);
        for (int i = 0; i < hands.Length; i++)
        {
            OVRHand hand = hands[i];
            if (hand != null && IsRightHandName(hand.name))
            {
                return hand;
            }
        }

        return null;
    }

    private static bool IsRightHandName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        string lowerName = objectName.ToLowerInvariant();
        return lowerName.Contains("right")
            || lowerName.Contains("righthand")
            || lowerName.Contains("right_hand")
            || lowerName.Contains("hand_r")
            || lowerName.EndsWith("_r")
            || lowerName.EndsWith("-r");
    }

    private Transform ResolveHmdTransform()
    {
        if (hmdTransform != null)
        {
            return hmdTransform;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            hmdTransform = mainCamera.transform;
        }

        return hmdTransform;
    }

    private void ResetArmState()
    {
        armed = false;
        poseHeldSince = -1f;
        swipeSamples.Clear();
    }

    private void ObserveExternalMenuVisibility()
    {
        if (menuPanel == null)
        {
            return;
        }

        bool visible = menuPanel.IsMenuVisible;
        if (visible == lastObservedMenuVisible)
        {
            return;
        }

        lastObservedMenuVisible = visible;
        LogDebug(visible ? "PHOTON_MENU_OPEN" : "PHOTON_MENU_CLOSE");
    }

    private void LogBlocked(string reason)
    {
        if (!enableGestureDebugLogs)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (reason == lastBlockedReason && now - lastBlockedLogTime < 0.5f)
        {
            return;
        }

        lastBlockedReason = reason;
        lastBlockedLogTime = now;
        Debug.Log("PHOTON_MENU_GESTURE_BLOCKED reason=" + reason);
    }

    private void LogDebug(string message)
    {
        if (enableGestureDebugLogs)
        {
            Debug.Log(message);
        }
    }

    private static bool TryReadBoolMember(object source, string memberName, out bool value)
    {
        value = false;
        if (!TryReadMember(source, memberName, out object rawValue) || !(rawValue is bool boolValue))
        {
            return false;
        }

        value = boolValue;
        return true;
    }

    private static bool TryReadMember(object source, string memberName, out object value)
    {
        value = null;
        if (source == null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = source.GetType();
        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            value = field.GetValue(source);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.GetIndexParameters().Length == 0)
        {
            value = property.GetValue(source);
            return true;
        }

        return false;
    }
}
