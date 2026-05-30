/*using UnityEngine;
using RosSharp.RosBridgeClient;
using MixedReality.Toolkit.Subsystems;
using MixedReality.Toolkit;
using UnityEngine.XR;

using RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{

public class RingFlag : UnityPublisher<std_msgs.Bool>
{
    [Header("Marker Prefabs")]
    public GameObject thumbMarkerPrefab;
    public GameObject littleMarkerPrefab;

    [Header("Distance Threshold (m)")]
    public float threshold = 0.02f;

    [Header("Activate Target On Close")]
    public GameObject toggleTarget;

    private GameObject thumbMarker;
    private GameObject littleMarker;
    private std_msgs.Bool message;
    private Color defaultColorThumb;
    private Color defaultColorRing;

    protected override void Start()
    {
        base.Start();
        message = new std_msgs.Bool { data = false };

        if (thumbMarkerPrefab != null)
        {
            thumbMarker = Instantiate(thumbMarkerPrefab, transform);
            thumbMarker.SetActive(false);
            defaultColorThumb = thumbMarker.GetComponent<Renderer>().material.color;
        }
        if (littleMarkerPrefab != null)
        {
            littleMarker = Instantiate(littleMarkerPrefab, transform);
            littleMarker.SetActive(false);
            defaultColorRing = littleMarker.GetComponent<Renderer>().material.color;
        }
    }

    void Update()
    {
        var agg = XRSubsystemHelpers.HandsAggregator;
        if (agg == null) return;

        // 親指ポーズ取得
        bool haveThumb = agg.TryGetJoint(TrackedHandJoint.ThumbTip, XRNode.LeftHand, out var thumbPose);
        if (haveThumb)
        {
            thumbMarker.SetActive(true);
            thumbMarker.transform.SetPositionAndRotation(thumbPose.Position, thumbPose.Rotation);
        }
        else thumbMarker?.SetActive(false);

        // 小指ポーズ取得
        bool haveRing = agg.TryGetJoint(TrackedHandJoint.LittleTip, XRNode.LeftHand, out var littlePose);
        if (haveRing)
        {
            littleMarker.SetActive(true);
            littleMarker.transform.SetPositionAndRotation(littlePose.Position, littlePose.Rotation);
        }
        else littleMarker?.SetActive(false);

        // 両方あれば距離チェック
        if (haveThumb && haveRing)
        {
            float dist = Vector3.Distance(thumbPose.Position, littlePose.Position);
            bool isClose = dist <= threshold;

            // マーカー色を更新
            thumbMarker.GetComponent<Renderer>().material.color = isClose ? Color.red : defaultColorThumb;
            littleMarker.GetComponent<Renderer>().material.color = isClose ? Color.red : defaultColorRing;

            // まだ false なら一度だけ true に切り替えて Publish & ターゲットをアクティブ化
            if (message.data == false)
            {
                var handPose = rosConnector.GetComponent<handPosePublisher>();
                var airTap = rosConnector.GetComponent<airTapPublisher>();
                var floatSub = rosConnector.GetComponent<Float32MultiSubscriber>();

                if (handPose != null) handPose.enabled = false;
                if (airTap != null) airTap.enabled = false;
                if (floatSub != null) floatSub.enabled = false;

                message.data = true;
                Publish(message);
                if (toggleTarget != null)
                    toggleTarget.SetActive(true);
                UnityEngine.Debug.Log("Recovery flag set to TRUE and target activated");
            }
        }
    }
}


}*/