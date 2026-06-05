using RosMessageTypes.Geometry;
using UnityEngine;

public class YoubotOffsetSubscriber : RosTcpSubscriber<Vector3StampedMsg>
{
    [Tooltip("移動量を適用する Origin オブジェクト")]
    public GameObject OriginObject;

    public Vector3 BaseMovePosition;

    protected override void ReceiveMessage(Vector3StampedMsg message)
    {
        BaseMovePosition = new Vector3(
            -(float)message.vector.x,
            (float)message.vector.z,
            -(float)message.vector.y);
    }
}
