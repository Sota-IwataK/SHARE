using UnityEngine;
using RosSharp.RosBridgeClient;
using std_msgs = RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{
    public class Phase_name_Subscriber : UnitySubscriber<std_msgs.String>
    {


        public string phase_name;
        private bool isMessageChange = false;

        protected override void Start()
        {
            base.Start(); // ROS�T�u�X�N���C�u�J�n
        }


        protected override void ReceiveMessage(std_msgs.String message)
        {
            phase_name = message.data;
            
        }

    }

}