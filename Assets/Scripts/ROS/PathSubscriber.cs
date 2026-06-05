using RosMessageTypes.Nav;
using UnityEngine;

public class PathSubscriber : RosTcpSubscriber<PathMsg>
{
    public PathMsg messageData;

    protected override void ReceiveMessage(PathMsg message)
    {
        Debug.Log("Received Path Message");
        Debug.Log(message.poses.Length);
        messageData = message;
    }
}
