using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace RosSharp.RosBridgeClient
{
    public class PickObjectPublisher : UnityPublisher<MessageTypes.Std.Float32MultiArray>
    {
        float[] data = new float[0];
        [SerializeField] private SelectObject select;
        protected override void Start()
        {
            base.Start();
        }

        private void FixedUpdate()
        {
            MessageTypes.Std.Float32MultiArray message;
            message = new MessageTypes.Std.Float32MultiArray();
            data = select.PickObjectMessage();
            message.data = data;
            Publish(message);
        }
    }
}
