using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;

public class BottleStatePublisher : UnityPublisher<Float32MultiArray>
{
    [Tooltip("SignalManager をセットしてください")]
    public BottleSignalManager BottleSignalManager;

    private Float32MultiArray message;

    protected override void Start()
    {
        base.Start();
        message = new Float32MultiArray
        {
            layout = new MultiArrayLayout
            {
                dim = new[]
                {
                    new MultiArrayDimension { label = "bottles", size = 0, stride = 0 },
                    new MultiArrayDimension { label = "fields",  size = 9, stride = 9 }
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
        // ← Use the 'signals' instance, not the type!
        var infos = BottleSignalManager.signals;
        int n     = infos.Count;
        int total = n * 9;

        // 1) data 配列を作る
        float[] data = new float[total];
        for (int i = 0; i < n; i++)
        {
            var info = infos[i];
            data[i * 9 + 0] = info.bottleID;    // your ID field
            data[i * 9 + 1] = info.position.x;
            data[i * 9 + 2] = info.position.y;
            data[i * 9 + 3] = info.position.z;
            data[i * 9 + 4] = info.insideFlag;
            data[i * 9 + 5] = info.s_touch;
            data[i * 9 + 6] = info.s_hand;
            data[i * 9 + 7] = info.s_head;
            data[i * 9 + 8] = info.s_accel;
        }

        // 2) layout を更新
        message.layout.dim[0].size   = (uint)n;
        message.layout.dim[0].stride = (uint)total;
        // dim[1] は固定 (9)

        // 3) data をセットして Publish
        message.data = data;
        Publish(message);
    }
}
