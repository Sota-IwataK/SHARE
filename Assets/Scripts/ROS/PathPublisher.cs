using System;
using System.Collections.Generic;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;
using UnityEngine;

public class PathPublisher : RosTcpPublisher<PathMsg>
{
    public string FrameId = "Unity";
    public PoseStampedMsg[] WayPointPoseList;
    public List<GameObject> WayPointObjectList;
    public bool PublishStatus;

    private PathMsg message;

    protected override void Start()
    {
        base.Start();
        message = new PathMsg();
    }

    private void FixedUpdate()
    {
        if (!PublishStatus) return;

        GetWayPointList();
        message.header = new HeaderMsg { stamp = RosTcpUtility.GetRosTime(), frame_id = FrameId };
        message.poses = WayPointPoseList;
        Debug.Log("Publish : " + message.poses.Length);
        Publish(message);
        PublishStatus = false;
    }

    private void GetWayPointList()
    {
        if (WayPointObjectList == null) return;

        Array.Resize(ref WayPointPoseList, WayPointObjectList.Count);

        for (int i = 0; i < WayPointObjectList.Count; i++)
        {
            PoseStampedMsg poseStamped = new PoseStampedMsg
            {
                pose =
                {
                    position = GetPosition(WayPointObjectList[i].transform.localPosition),
                    orientation = GetRotation(WayPointObjectList[i].transform.localRotation)
                }
            };
            WayPointPoseList[i] = poseStamped;
        }
    }

    private static PointMsg GetPosition(Vector3 pos)
    {
        return new PointMsg(-pos.x, -pos.z, pos.y);
    }

    private static QuaternionMsg GetRotation(Quaternion orientation)
    {
        return new QuaternionMsg(orientation.z, -orientation.x, orientation.y, -orientation.w);
    }
}
