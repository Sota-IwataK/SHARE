using RosMessageTypes.Std;
using UnityEngine;

public class CubeSlamSubscriber : RosTcpSubscriber<Float32MultiArrayMsg>
{
    public GameObject objectGeneration;
    public float[] messageData = new float[5];

    protected override void ReceiveMessage(Float32MultiArrayMsg message)
    {
        messageData = message.data;
        if (objectGeneration != null) objectGeneration.SetActive(true);
    }
}
