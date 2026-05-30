using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Nav;

public class PathPublisher : UnityPublisher<Path>
{
    public string FrameId = "Unity";
    private Path message;
    UnityEngine.Vector3 pose;
    public PoseStamped[] WayPointPoseList;
    public List<GameObject> WayPointObjectList;

    public bool PublishStatus;
    

    protected override void Start()
    {
        base.Start();
        InitializeMessage();

    }

    private void FixedUpdate()
    {
        if (PublishStatus)
        {
            GetWayPointList();
            UpdateMessage();
        }
    }
    private void InitializeMessage()
    {
        message = new Path
        {
            header = new RosSharp.RosBridgeClient.MessageTypes.Std.Header()
            {
                frame_id = FrameId
            }
            
        };
    }
    private void UpdateMessage()
    {
        message.header.Update();
        message.poses = WayPointPoseList;
        Debug.Log("Publish : " + message.poses.Length);
        Publish(message);
        PublishStatus = false;
    }

    private void GetWayPointList()
    {
        Array.Resize(ref WayPointPoseList, WayPointObjectList.Count);

        for(int i = 0; i < WayPointObjectList.Count; i++)
        {
            RosSharp.RosBridgeClient.MessageTypes.Geometry.Point position = GetPosition(WayPointObjectList[i].transform.localPosition);
            RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion orientation = GetRotation(WayPointObjectList[i].transform.localRotation);
            PoseStamped poseStamped = new PoseStamped();
            poseStamped.pose.position = position;
            poseStamped.pose.orientation = orientation;
            WayPointPoseList[i] = poseStamped;
        }
    }

    private RosSharp.RosBridgeClient.MessageTypes.Geometry.Point GetPosition(UnityEngine.Vector3 pos)
    {
        // ROSの座標系からUnityの座標系へ変換（通常、ROSは右手系、Unityは左手系）
        return new RosSharp.RosBridgeClient.MessageTypes.Geometry.Point((float)-pos.x, -(float)pos.z, (float)pos.y);
    }

    private RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion GetRotation(UnityEngine.Quaternion orientation)
    {
        // 四元数の変換も同様に行います
        return new RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion((float)orientation.z, -(float)orientation.x, (float)orientation.y, -(float)orientation.w);
    }
}