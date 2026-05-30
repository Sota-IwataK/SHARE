using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RosSharp.RosBridgeClient
{

    public class ObjectPositionPublisher : UnityPublisher<MessageTypes.Geometry.PoseStamped>
    {
        public string FrameId = "Unity";
        private MessageTypes.Geometry.PoseStamped message;
        public IdTracking idTracking;
        public ObjectGenerationTest objectGeneration;
        Vector3 pose;
        protected override void Start()
        {
            base.Start();
            InitializeMessage();

        }

        private void FixedUpdate()
        {
            if (idTracking.index != "")
            {
                pose = objectGeneration.ObjectPosition;
            }
            else
            {
                pose = new Vector3(0.4f, 0, 0.2f);
            }
            UpdateMessage();
        }
        private void InitializeMessage()
        {
            message = new MessageTypes.Geometry.PoseStamped
            {
                header = new MessageTypes.Std.Header()
                {
                    frame_id = FrameId
                }
            };
        }
        private void UpdateMessage()
        {
            message.header.Update();

            message.pose.position.x = pose.x;
            message.pose.position.y = pose.y;
            message.pose.position.z = pose.z;


            Publish(message);

        }
    }

}
