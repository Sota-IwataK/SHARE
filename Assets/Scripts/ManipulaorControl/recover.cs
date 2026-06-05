using UnityEngine;

public class recover : MonoBehaviour
{
    [SerializeField] private GameObject rosConnector;

    // GameObject がアクティブになったときに呼ばれる
    private void OnEnable()
    {
        if (rosConnector == null) return;

        var handPose = rosConnector.GetComponent<handPosePublisher>();
        var airTap = rosConnector.GetComponent<airTapPublisher>();
        var floatSub = rosConnector.GetComponent<Float32MultiSubscriber>();

        if (handPose != null) handPose.enabled = false;
        if (airTap != null) airTap.enabled = false;
        if (floatSub != null) floatSub.enabled = false;

        UnityEngine.Debug.Log("Recover triggered: HansdTracking disabled.");
    }
}
