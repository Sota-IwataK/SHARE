using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using UnityEngine;

public class WayPointController : MonoBehaviour
{
    [SerializeField] private GameObject WayPoint;
    [SerializeField] private PathSubscriber _pathSubscriber;
    public PathMsg messageData;
    [SerializeField] private GameObject Origin;
    [SerializeField] private PathPublisher _pathPublisher;
    [SerializeField] private GameObject LineRenderer;

    private bool UpdatePathStatus = false;
    private int wayPointNumber = 0;
    private Vector3 PreviousPosition;
    private Vector3 CurrentPosition;

    private void Update()
    {
        messageData = _pathSubscriber.messageData;
        if (messageData == null || UpdatePathStatus) return;

        foreach (PoseStampedMsg poseStamped in messageData.poses)
        {
            Vector3 position = GetPosition(poseStamped);
            Quaternion rotation = GetRotation(poseStamped);

            GameObject point = Instantiate(WayPoint);
            point.transform.parent = Origin.transform;
            point.transform.localPosition = position;
            point.transform.localRotation = rotation;
            point.GetComponent<EditWayPoint>().wayPointNumber = wayPointNumber;

            CurrentPosition = point.transform.position;
            if (wayPointNumber != 0)
            {
                SetLineRenderer(PreviousPosition, CurrentPosition, point);
            }

            PreviousPosition = CurrentPosition;
            wayPointNumber++;
            _pathPublisher.WayPointObjectList.Add(point);
        }

        UpdatePathStatus = true;
    }

    private static Vector3 GetPosition(PoseStampedMsg poseStamped)
    {
        return new Vector3(
            (float)-poseStamped.pose.position.x,
            (float)poseStamped.pose.position.z,
            (float)-poseStamped.pose.position.y);
    }

    private static Quaternion GetRotation(PoseStampedMsg poseStamped)
    {
        return new Quaternion(
            (float)poseStamped.pose.orientation.x,
            (float)poseStamped.pose.orientation.z,
            (float)poseStamped.pose.orientation.y,
            (float)poseStamped.pose.orientation.w);
    }

    private void SetLineRenderer(Vector3 pos1, Vector3 pos2, GameObject point)
    {
        GameObject lineRendererObj = Instantiate(LineRenderer);
        lineRendererObj.GetComponent<LineRenderer>().SetPosition(0, pos1);
        lineRendererObj.GetComponent<LineRenderer>().SetPosition(1, pos2);
        lineRendererObj.transform.parent = point.transform;
    }
}
