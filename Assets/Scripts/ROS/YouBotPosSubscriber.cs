using RosMessageTypes.Geometry;
using UnityEngine;

public class YouBotPosSubscriber : RosTcpSubscriber<PoseStampedMsg>
{
    public Vector3 messagePosition;
    public Quaternion messageRotation;

    [SerializeField] private GameObject origin;

    protected override void ReceiveMessage(PoseStampedMsg message)
    {
        messagePosition = new Vector3(
            (float)-message.pose.position.x,
            (float)message.pose.position.z,
            (float)message.pose.position.y);

        messageRotation = new Quaternion(
            (float)message.pose.orientation.z,
            (float)-message.pose.orientation.x,
            (float)message.pose.orientation.y,
            (float)-message.pose.orientation.w);
    }
}
