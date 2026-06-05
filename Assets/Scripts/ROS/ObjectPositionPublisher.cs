using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using UnityEngine;

public class ObjectPositionPublisher : RosTcpPublisher<PoseStampedMsg>
{
    public string FrameId = "Unity";
    public IdTracking idTracking;
    public ObjectGenerationTest objectGeneration;

    private PoseStampedMsg message;
    private Vector3 pose;

    protected override void Start()
    {
        base.Start();
        message = new PoseStampedMsg();
    }

    private void FixedUpdate()
    {
        if (idTracking != null && idTracking.index != "" && objectGeneration != null)
        {
            pose = objectGeneration.ObjectPosition;
        }
        else
        {
            pose = new Vector3(0.4f, 0f, 0.2f);
        }

        message.header = new HeaderMsg { stamp = RosTcpUtility.GetRosTime(), frame_id = FrameId };
        message.pose.position = new PointMsg(pose.x, pose.y, pose.z);

        Publish(message);
    }
}
