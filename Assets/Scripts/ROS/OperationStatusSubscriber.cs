using RosMessageTypes.Std;
using UnityEngine;

public class OperationStatusSubscriber : RosTcpSubscriber<BoolMsg>
{
    public bool messageData;

    protected override void ReceiveMessage(BoolMsg message)
    {
        messageData = message.data;
    }
}
