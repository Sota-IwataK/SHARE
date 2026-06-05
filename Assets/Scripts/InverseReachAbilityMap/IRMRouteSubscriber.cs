using RosMessageTypes.Nav;
using UnityEngine;

public class IRMRouteSubscriber : RosTcpSubscriber<PathMsg>
{
    public PathMsg messagePath;
    public bool isDirty = false;

    protected override void ReceiveMessage(PathMsg message)
    {
        messagePath = message;
        Debug.Log("Received IRMPath Message");
        isDirty = true;
    }
}
