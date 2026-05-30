using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RosSharp.RosBridgeClient
{
    public class handPosePublisher : UnityPublisher<MessageTypes.Geometry.PoseStamped>
    {

        public string FrameId = "Unity";
        private MessageTypes.Geometry.PoseStamped message;
        public handTracking handTracking;

        protected override void Start()
        {
            base.Start();
            InitializeMessage();
        }

        private void FixedUpdate()
        {
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
            //Vector3 pose = handTracking.GetThumbPosition();
            //Quaternion rotation = handTracking.GetThumbRotation();
            Vector3 pose = handTracking.GetHandPositionFromOrigin();
            Quaternion rotation = handTracking.GetHandRotationFromOrigin();
            GetGeometryPoint(pose, message.pose.position);
            GetGeometryQuaternion(rotation, message.pose.orientation);

            Publish(message);
        
        }

        //ê≥ñ Ç©ÇÁå©ÇΩÇ∆Ç´
        private static void GetGeometryPoint(Vector3 position,MessageTypes.Geometry.Point geometryPoint)
        {
            geometryPoint.x = -position.x;
            geometryPoint.y = position.z;
            geometryPoint.z = position.y;

        }

        private static void GetGeometryQuaternion(Quaternion quaternion,MessageTypes.Geometry.Quaternion geometryQuaternion)
        {
            geometryQuaternion.x = quaternion.z;
            geometryQuaternion.y = -(quaternion.x);
            geometryQuaternion.z = quaternion.y;
            geometryQuaternion.w = -(quaternion.w);
        }




    }

}
