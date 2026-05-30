using UnityEngine;

namespace RosSharp.RosBridgeClient
{
    public class Float32MultiPublisher : UnityPublisher<MessageTypes.Std.Float32MultiArray>
    {

        float[] data = new float[0];
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
            data = new float[] { 8, -2, 4 };
            message.data = data;
            Publish(message);
        }
    }
}
