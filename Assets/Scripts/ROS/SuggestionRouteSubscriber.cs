using RosMessageTypes.Nav;
using UnityEngine;

public class SuggestionRouteSubscriber : RosTcpSubscriber<PathMsg>
{
    public PathMsg messagePath;

    protected override void ReceiveMessage(PathMsg message)
    {
        messagePath = message;
        Debug.Log("Received Path Message");
    }
}
