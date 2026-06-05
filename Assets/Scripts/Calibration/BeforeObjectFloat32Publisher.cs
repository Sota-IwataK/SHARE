using RosMessageTypes.Std;
using UnityEngine;

public class BeforeObjectFloat32Publisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    [SerializeField] private SelectObject select;

    private void FixedUpdate()
    {
        if (select == null) return;

        Publish(new Float32MultiArrayMsg { data = select.BeforeObjectMessage() });
    }
}
