using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using UnityEngine;

public class EditWayPoint : MonoBehaviour
{
    public int wayPointNumber;

    private PathPublisher pathPublisher;
    [SerializeField] private PathSubscriber pathSubscriber;
    public PathMsg messageData;

    private void Start()
    {
        pathPublisher = FindObjectOfType<PathPublisher>();
        if (pathSubscriber == null) pathSubscriber = FindObjectOfType<PathSubscriber>();
    }

    public void edit()
    {
        if (pathPublisher == null || pathSubscriber == null || pathSubscriber.messageData == null) return;

        messageData = pathSubscriber.messageData;
        messageData.poses[wayPointNumber].pose.position =
            new PointMsg(transform.position.x, transform.position.y, transform.position.z);
        pathPublisher.WayPointPoseList = messageData.poses;
        pathPublisher.PublishStatus = true;
    }
}
