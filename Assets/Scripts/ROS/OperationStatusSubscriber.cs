using RosSharp.RosBridgeClient.MessageTypes.Std;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OperationStatusSubscriber : RosSharp.RosBridgeClient.UnitySubscriber<RosSharp.RosBridgeClient.MessageTypes.Std.Bool>
{
    public bool messageData;
    protected override void Start()
    {
        base.Start();
    }

    protected override void ReceiveMessage(RosSharp.RosBridgeClient.MessageTypes.Std.Bool message)
    {
        messageData = message.data;

    }
}
