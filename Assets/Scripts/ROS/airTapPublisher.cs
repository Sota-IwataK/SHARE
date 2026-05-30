using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RosSharp.RosBridgeClient
{
    public class airTapPublisher : UnityPublisher<MessageTypes.Std.String>
    {
        public string FramId = "Unity";
        private MessageTypes.Std.String grip;
        public airTap_distance distance;
        public string outputdata;
        protected override void Start()
        {
            base.Start();
            InitialiizeMessage();

        }

        private void FixedUpdate()
        {
            outputdata = distance.bool2string();
            UpdateMessage();
        }

        private void InitialiizeMessage()
        {
            grip = new MessageTypes.Std.String
            {
                data = outputdata
            };
        }
        private void UpdateMessage()
        {
            grip.data = outputdata;
            Publish(grip);
        }

    }

}
