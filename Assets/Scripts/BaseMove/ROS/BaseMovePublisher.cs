using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using UnityEngine;

public class BaseMovePublisher : RosTcpPublisher<PoseStampedMsg>
{
    [SerializeField] private ObjectInfoSetting objectInfoSetting;

    public string FrameId = "Unity";
    public bool MoveStatus = false;

    private PoseStampedMsg message;

    protected override void Start()
    {
        base.Start();
        message = new PoseStampedMsg();
    }

    public void BaseMovePub()
    {
        if (objectInfoSetting == null) return;

        Vector3 pose = objectInfoSetting.GetMovePosition();
        message.header = new HeaderMsg { stamp = RosTcpUtility.GetRosTime(), frame_id = FrameId };
        message.pose.position = new PointMsg(-pose.x, -pose.z, pose.y);

        Publish(message);
    }
}
