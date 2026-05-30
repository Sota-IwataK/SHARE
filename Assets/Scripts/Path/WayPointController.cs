using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Nav;

public class WayPointController : MonoBehaviour
{
    [SerializeField] private GameObject WayPoint;
    [SerializeField] private PathSubscriber _pathSubscriber;
    public Path messageData;
    [SerializeField] private GameObject Origin;
    private bool UpdatePathStatus = false;

    [SerializeField] private PathPublisher _pathPublisher;

    [SerializeField] private GameObject LineRenderer;

    private int wayPointNumber = 0;

    UnityEngine.Vector3 PreviousPosition;
    UnityEngine.Vector3 CurrentPosition;
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        messageData = _pathSubscriber.messageData;
        if(messageData != null && !UpdatePathStatus)
        {
            foreach (PoseStamped poseStamped in messageData.poses)
            {
                UnityEngine.Vector3 position = GetPosition(poseStamped);
                UnityEngine.Quaternion rotation = GetRotation(poseStamped);

                GameObject point =  Instantiate(WayPoint);
                point.transform.parent = Origin.transform;
                point.transform.localPosition = position;
                point.transform.localRotation = rotation;
                point.GetComponent<EditWayPoint>().wayPointNumber = wayPointNumber;

                CurrentPosition = point.transform.position;
                if (wayPointNumber != 0)
                {                    
                    SetLineRenderer(PreviousPosition,CurrentPosition, point);

                }
                PreviousPosition = CurrentPosition;
                wayPointNumber++;
                _pathPublisher.WayPointObjectList.Add(point);
            }
            UpdatePathStatus = true;
        }

    }

    private UnityEngine.Vector3 GetPosition(PoseStamped poseStamped)
    {
        // ROSの座標系からUnityの座標系へ変換（通常、ROSは右手系、Unityは左手系）
        return new UnityEngine.Vector3((float)-poseStamped.pose.position.x, (float)poseStamped.pose.position.z, (float)-poseStamped.pose.position.y);
    }

    private UnityEngine.Quaternion GetRotation(PoseStamped poseStamped)
    {
        // 四元数の変換も同様に行います
        return new UnityEngine.Quaternion((float)poseStamped.pose.orientation.x, (float)poseStamped.pose.orientation.z, (float)poseStamped.pose.orientation.y, (float)poseStamped.pose.orientation.w);
    }

    private void SetLineRenderer(UnityEngine.Vector3 pos1, UnityEngine.Vector3 pos2 ,GameObject point)
    {
        GameObject LineRendererObj = Instantiate(LineRenderer);
        LineRendererObj.GetComponent<LineRenderer>().SetPosition(0, pos1);
        LineRendererObj.GetComponent<LineRenderer>().SetPosition(1, pos2);
        LineRendererObj.transform.parent = point.transform;
    }   
}
