using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Nav;

public class PathSubscriber : UnitySubscriber<RosSharp.RosBridgeClient.MessageTypes.Nav.Path>
{


    public Path messageData;
    protected override void Start()
    {
        base.Start();
    }

    protected override void ReceiveMessage(Path message)
    {
        Debug.Log("Received Path Message");

        Debug.Log(message.poses.Length);
        messageData = message;

    }


}
