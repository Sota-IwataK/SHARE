using RosMessageTypes.Std;
using UnityEngine;

public class CalibrationFloat32Subscriber : RosTcpSubscriber<Float32MultiArrayMsg>
{
    public float[] messageData = new float[5];

    protected override void ReceiveMessage(Float32MultiArrayMsg message)
    {
        messageData = message.data;
    }
}
