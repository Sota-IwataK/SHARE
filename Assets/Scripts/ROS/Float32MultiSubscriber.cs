using RosSharp.RosBridgeClient.MessageTypes.Std;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

    public class Float32MultiSubscriber : RosSharp.RosBridgeClient.UnitySubscriber<RosSharp.RosBridgeClient.MessageTypes.Std.Float32MultiArray>
    {
        public float[] messageData = new float[5];

        protected override void Start()
        {
            base.Start();
        }

        protected override void ReceiveMessage(Float32MultiArray message)
        {
            messageData = message.data;
        }
    }



