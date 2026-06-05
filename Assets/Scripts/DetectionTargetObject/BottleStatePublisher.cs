using RosMessageTypes.Std;
using UnityEngine;

public class BottleStatePublisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    [Tooltip("Bottle state source")]
    public BottleSignalManager BottleSignalManager;

    private Float32MultiArrayMsg message;

    protected override void Start()
    {
        base.Start();
        message = new Float32MultiArrayMsg
        {
            layout = new MultiArrayLayoutMsg
            {
                dim = new[]
                {
                    new MultiArrayDimensionMsg { label = "bottles", size = 0, stride = 0 },
                    new MultiArrayDimensionMsg { label = "fields", size = 9, stride = 9 }
                },
                data_offset = 0
            },
            data = new float[0]
        };
    }

    private void FixedUpdate()
    {
        PublishBottleStates();
    }

    public void PublishBottleStates()
    {
        if (BottleSignalManager == null || message == null) return;

        var infos = BottleSignalManager.signals;
        int n = infos.Count;
        int total = n * 9;

        float[] data = new float[total];
        for (int i = 0; i < n; i++)
        {
            var info = infos[i];
            data[i * 9 + 0] = info.bottleID;
            data[i * 9 + 1] = info.position.x;
            data[i * 9 + 2] = info.position.y;
            data[i * 9 + 3] = info.position.z;
            data[i * 9 + 4] = info.insideFlag;
            data[i * 9 + 5] = info.s_touch;
            data[i * 9 + 6] = info.s_hand;
            data[i * 9 + 7] = info.s_head;
            data[i * 9 + 8] = info.s_accel;
        }

        message.layout.dim[0].size = (uint)n;
        message.layout.dim[0].stride = (uint)total;
        message.data = data;
        Publish(message);
    }
}
