using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Nav;
using UnityEngine.UIElements;

public class IRMRoutePathPublisher : UnityPublisher<Path>
{
    public string FrameId = "Unity";
    private Path message;
    UnityEngine.Vector3 pose;
    public PoseStamped[] WayPointPoseList;
    public UnityEngine.Transform startTransform;

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
            /* RosSharp.RosBridgeClient.MessageTypes.Geometry.Point position = GetPosition(WayPointObjectList[i].transform.localPosition);
 */
            // ① ワールド座標を取得xxxx
            UnityEngine.Vector3 worldPos = WayPointObjectList[i].transform.position;
                        // ② startTransform が指定されていれば、そこからの相対位置に変換
            UnityEngine.Vector3 relPos = startTransform != null
                            ? worldPos - startTransform.position
                            : WayPointObjectList[i].transform.localPosition;
            // ③ Unity→ROS 座標変換
            Point position = GetPosition(relPos);

            RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion orientation = GetRotation(WayPointObjectList[i].transform.localRotation);
            PoseStamped poseStamped = new PoseStamped();
            poseStamped.header = message.header;
            poseStamped.pose.position = position;
            poseStamped.pose.orientation = orientation;
            WayPointPoseList[i] = poseStamped;
        }
    }

    private RosSharp.RosBridgeClient.MessageTypes.Geometry.Point GetPosition(UnityEngine.Vector3 pos)
    {
        // ROS�̍��W�n����Unity�̍��W�n�֕ϊ��i�ʏ�AROS�͉E��n�AUnity�͍���n�j
        return new RosSharp.RosBridgeClient.MessageTypes.Geometry.Point((float)-pos.x, -(float)pos.z, (float)pos.y);
    }

    private RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion GetRotation(UnityEngine.Quaternion orientation)
    {
        // �l�����̕ϊ������l�ɍs���܂�
        return new RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion((float)orientation.z, -(float)orientation.x, (float)orientation.y, -(float)orientation.w);
    }
}