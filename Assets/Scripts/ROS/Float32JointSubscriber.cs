using RosMessageTypes.Std;
using UnityEngine;

public class Float32JointSubscriber : RosTcpSubscriber<Float32MultiArrayMsg>
{
    public float[] messageData = new float[5];

    protected override void ReceiveMessage(Float32MultiArrayMsg message)
    {
        messageData = message.data;
    }
}
