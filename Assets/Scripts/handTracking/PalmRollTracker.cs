/*using UnityEngine;
using MixedReality.Toolkit.Input;      // TrackedHandJoint, HandJointPose
using MixedReality.Toolkit.Subsystems; // XRSubsystemHelpers
using UnityEngine.XR;
using MixedReality.Toolkit;

public class PalmRollTracker : MonoBehaviour
{
    [Header("Marker Prefabs")]
    public GameObject sphereMarker;        // MiddleMetacarpal 用
    public GameObject palmMarker;          // Palm 用
    public GameObject wristMarkerPrefab;   // Wrist 用

    [Header("Enable Markers")]
    public bool enableMidMarker = true;
    public bool enablePalmMarker = true;
    public bool enableWristMarker = true;

    [Header("Debug Euler Angles")]
    [SerializeField] private Vector3 midEulerAngles;
    [SerializeField] private Vector3 palmEulerAngles;
    [SerializeField] private Vector3 wristEulerAngles;

    private GameObject middleObject;
    private GameObject palmObject;
    private GameObject wristObject;

    void Start()
    {
        // Inspector のフラグに応じてインスタンス化
        if (enableMidMarker && sphereMarker != null)
        {
            middleObject = Instantiate(sphereMarker, transform);
            middleObject.SetActive(false);
        }
        if (enablePalmMarker && palmMarker != null)
        {
            palmObject = Instantiate(palmMarker, transform);
            palmObject.SetActive(false);
        }
        if (enableWristMarker && wristMarkerPrefab != null)
        {
            wristObject = Instantiate(wristMarkerPrefab, transform);
            wristObject.SetActive(false);
        }
    }

    void Update()
    {
        var agg = XRSubsystemHelpers.HandsAggregator;
        if (agg == null) { return; }

        // ── MiddleMiddleProximal ──
        if (enableMidMarker && middleObject != null &&
            agg.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.LeftHand, out HandJointPose midPose))
        {
            middleObject.SetActive(true);
            middleObject.transform.position = midPose.Position;
            middleObject.transform.rotation = midPose.Rotation;
            midEulerAngles = midPose.Rotation.eulerAngles;
        }
        else if (middleObject != null)
        {
            middleObject.SetActive(false);
        }

        // ── Palm ──
        if (enablePalmMarker && palmObject != null &&
            agg.TryGetJoint(TrackedHandJoint.Palm, XRNode.LeftHand, out HandJointPose palmPose))
        {
            palmObject.SetActive(true);
            palmObject.transform.position = palmPose.Position;
            palmObject.transform.rotation = palmPose.Rotation;
            palmEulerAngles = palmPose.Rotation.eulerAngles;
        }
        else if (palmObject != null)
        {
            palmObject.SetActive(false);
        }

        // ── Wrist ──
        if (enableWristMarker && wristObject != null &&
            agg.TryGetJoint(TrackedHandJoint.Wrist, XRNode.LeftHand, out HandJointPose wristPose))
        {
            wristObject.SetActive(true);
            wristObject.transform.position = wristPose.Position;
            wristObject.transform.rotation = wristPose.Rotation;
            wristEulerAngles = wristPose.Rotation.eulerAngles;
        }
        else if (wristObject != null)
        {
            wristObject.SetActive(false);
        }
    }
}
*/



using UnityEngine;
using MixedReality.Toolkit.Input;      // TrackedHandJoint, HandJointPose
using MixedReality.Toolkit.Subsystems; // XRSubsystemHelpers
using UnityEngine.XR;
using MixedReality.Toolkit;

public class PalmRollTracker : MonoBehaviour
{
    [Header("Marker Prefabs")]
    public GameObject sphereMarker;        // MiddleMetacarpal 用
    public GameObject palmMarker;          // Palm 用
    public GameObject wristMarkerPrefab;   // Wrist 用
    public GameObject ringMarkerPrefab;   // 小指の先用

    [Header("Enable Markers")]
    public bool enableMidMarker = true;
    public bool enablePalmMarker = true;
    public bool enableWristMarker = true;
    public bool enableRingMarker = true;  // 小指マーカーを有効化

    [Header("Debug Euler Angles")]
    [SerializeField] private Vector3 midEulerAngles;
    [SerializeField] private Vector3 palmEulerAngles;
    [SerializeField] private Vector3 wristEulerAngles;
    [SerializeField] private Vector3 ringEulerAngles; // 小指用

    private GameObject middleObject;
    private GameObject palmObject;
    private GameObject wristObject;
    private GameObject ringObject;       // 小指オブジェクト

    void Start()
    {
        if (enableMidMarker && sphereMarker != null)
        {
            middleObject = Instantiate(sphereMarker, transform);
            middleObject.SetActive(false);
        }
        if (enablePalmMarker && palmMarker != null)
        {
            palmObject = Instantiate(palmMarker, transform);
            palmObject.SetActive(false);
        }
        if (enableWristMarker && wristMarkerPrefab != null)
        {
            wristObject = Instantiate(wristMarkerPrefab, transform);
            wristObject.SetActive(false);
        }
        if (enableRingMarker && ringMarkerPrefab != null)
        {
            ringObject = Instantiate(ringMarkerPrefab, transform);
            ringObject.SetActive(false);
        }
    }

    void Update()
    {
        var agg = XRSubsystemHelpers.HandsAggregator;
        if (agg == null) return;

        // Middle Proximal
        if (enableMidMarker && middleObject != null &&
            agg.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.LeftHand, out HandJointPose midPose))
        {
            middleObject.SetActive(true);
            middleObject.transform.position = midPose.Position;
            middleObject.transform.rotation = midPose.Rotation;
            midEulerAngles = midPose.Rotation.eulerAngles;
        }
        else if (middleObject != null) middleObject.SetActive(false);

        // Palm
        if (enablePalmMarker && palmObject != null &&
            agg.TryGetJoint(TrackedHandJoint.Palm, XRNode.LeftHand, out HandJointPose palmPose))
        {
            palmObject.SetActive(true);
            palmObject.transform.position = palmPose.Position;
            palmObject.transform.rotation = palmPose.Rotation;
            palmEulerAngles = palmPose.Rotation.eulerAngles;
        }
        else if (palmObject != null) palmObject.SetActive(false);

        // Wrist
        if (enableWristMarker && wristObject != null &&
            agg.TryGetJoint(TrackedHandJoint.Wrist, XRNode.LeftHand, out HandJointPose wristPose))
        {
            wristObject.SetActive(true);
            wristObject.transform.position = wristPose.Position;
            wristObject.transform.rotation = wristPose.Rotation;
            wristEulerAngles = wristPose.Rotation.eulerAngles;
        }
        else if (wristObject != null) wristObject.SetActive(false);

        // Pinky Tip
        if (enableRingMarker && ringObject != null &&
            agg.TryGetJoint(TrackedHandJoint.RingTip, XRNode.LeftHand, out HandJointPose pinkyPose))
        {
            ringObject.SetActive(true);
            ringObject.transform.position = pinkyPose.Position;
            ringObject.transform.rotation = pinkyPose.Rotation;
            ringEulerAngles = pinkyPose.Rotation.eulerAngles;
        }
        else if (ringObject != null) ringObject.SetActive(false);
    }
}

