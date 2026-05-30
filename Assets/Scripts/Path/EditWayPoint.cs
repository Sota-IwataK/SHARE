using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Nav;


public class EditWayPoint : MonoBehaviour
{
    public int wayPointNumber;

    private RosConnector _rosConnector;
    private PathPublisher _pathPublisher;
    [SerializeField] private PathSubscriber _pathSubscriber;
    public Path messageData;

    private void Start()
    {
        _rosConnector = GameObject.Find("RosConnector").GetComponent<RosConnector>();
        _pathPublisher = _rosConnector.GetComponent<PathPublisher>();
        _pathSubscriber = _rosConnector.GetComponent<PathSubscriber>();
    }
    public void edit()
    {
        messageData = _pathSubscriber.messageData;

        messageData.poses[wayPointNumber].pose.position = new Point (this.transform.position.x,this.transform.position.y,this.transform.position.z);
        _pathPublisher.WayPointPoseList = messageData.poses;

        _pathPublisher.PublishStatus = true;
    }
}
