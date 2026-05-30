using UnityEngine;

namespace RosSharp.RosBridgeClient
{
    public class BaseMovePublisher : UnityPublisher<MessageTypes.Geometry.PoseStamped>
    {

        [SerializeField] private ObjectInfoSetting objectInfoSetting;


        public string FrameId = "Unity";
        private MessageTypes.Geometry.PoseStamped message;
        public bool MoveStatus = false;


        protected override void Start()
        {
            base.Start();
            InitializeMessage();

        }

        public void BaseMovePub()
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
            if (objectInfoSetting != null)
            {
                Vector3 pose = objectInfoSetting.GetMovePosition();
                GetGeometryPoint(pose, message.pose.position);
            }
            Publish(message);



        }

        private static void GetGeometryPoint(Vector3 position, MessageTypes.Geometry.Point geometryPoint)
        {
            geometryPoint.x = -position.x;
            geometryPoint.y = -position.z;
            geometryPoint.z = position.y;

        }

    }
}