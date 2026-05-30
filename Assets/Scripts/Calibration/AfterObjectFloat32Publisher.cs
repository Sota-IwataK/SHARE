using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RosSharp.RosBridgeClient
{
    public class AfterObjectFloat32Publisher : UnityPublisher<MessageTypes.Std.Float32MultiArray>
    {

        float[] data = new float[0];
        [SerializeField] private SelectObject select;
        protected override void Start()
        {
            //RosConnector ros_connector = GetComponent<RosConnector>();
            //ros_connector.IsConnected.WaitOne(ros_connector.SecondsTimeout * 1000);
            base.Start();
        }

        private void FixedUpdate()
        {
            MessageTypes.Std.Float32MultiArray message;
            message = new MessageTypes.Std.Float32MultiArray();
            data = select.AfterObjectMessage();
            message.data = data;
            Publish(message);
        }
    }
}
