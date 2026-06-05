using System;
using System.Collections.Generic;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;
using UnityEngine;

public class IRMRoutePathPublisher : RosTcpPublisher<PathMsg>
{
    public string FrameId = "Unity";
    public PoseStampedMsg[] WayPointPoseList;
    public Transform startTransform;
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
            Vector3 worldPos = WayPointObjectList[i].transform.position;
            Vector3 relPos = startTransform != null
                ? worldPos - startTransform.position
                : WayPointObjectList[i].transform.localPosition;

            PoseStampedMsg poseStamped = new PoseStampedMsg
            {
                header = message.header,
                pose =
                {
                    position = GetPosition(relPos),
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
