using System.Collections.Generic;
using MixedReality.Toolkit.SpatialManipulation;
using RosMessageTypes.Nav;
using UnityEngine;

public class IRMRouteVisual : MonoBehaviour
{
    [Header("References")]
    public IRMRouteSubscriber subscriber;
    public IRMRoutePathPublisher publisher;
    public Transform waypointsContainer;
    public GameObject Aligin;

    [Header("Prefabs & Transforms")]
    public GameObject waypointPrefab;
    public GameObject lineRendererPrefab;
    public Transform startTransform;
    public GameObject suggestionMessage;

    [Header("Display Settings")]
    public float messageDistance = 2.0f;

    private readonly List<GameObject> waypoints = new List<GameObject>();
    private LineRenderer lineRenderer;

    private void Start()
    {
        subscriber = subscriber ?? GetComponent<IRMRouteSubscriber>();
        publisher = publisher ?? GetComponent<IRMRoutePathPublisher>();

        var lrObj = Instantiate(lineRendererPrefab, waypointsContainer);
        lrObj.transform.localPosition = Vector3.zero;
        lrObj.transform.localRotation = Quaternion.identity;

        lineRenderer = lrObj.GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0;

        publisher.WayPointObjectList = new List<GameObject>();
    }

    private void Update()
    {
        if (subscriber.messagePath == null || !subscriber.isDirty) return;

        Visualize(subscriber.messagePath);
        ShowSuggestionMessage();
        subscriber.isDirty = false;
    }

    public void DisvisualWaypoint()
    {
        if (waypointPrefab) waypointPrefab.SetActive(false);
        if (lineRendererPrefab) lineRendererPrefab.SetActive(false);
    }

    private void Visualize(PathMsg path)
    {
        Clear();

        int n = path.poses.Length;
        lineRenderer.positionCount = n + 1;
        lineRenderer.SetPosition(0, startTransform.position);

        for (int i = 0; i < path.poses.Length; i++)
        {
            var ps = path.poses[i];
            Vector3 rosLocal = new Vector3(
                -(float)ps.pose.position.x,
                0f,
                -(float)ps.pose.position.y);

            var go = Instantiate(waypointPrefab, waypointsContainer);
            go.transform.localPosition = rosLocal;
            go.transform.localRotation = Quaternion.identity;
            waypoints.Add(go);

            var manip = go.GetComponent<ObjectManipulator>() ?? go.AddComponent<ObjectManipulator>();
            int idx = i;
            manip.lastSelectExited.AddListener(_ => OnWaypointMoved(go, idx));

            lineRenderer.SetPosition(i + 1, go.transform.position);
            publisher.WayPointObjectList.Add(go);
        }
    }

    private void OnWaypointMoved(GameObject movedWaypoint, int index)
    {
        int lineIdx = index + 1;
        if (lineIdx < lineRenderer.positionCount)
        {
            lineRenderer.SetPosition(lineIdx, movedWaypoint.transform.position);
        }
    }

    private void Clear()
    {
        foreach (var wp in waypoints) Destroy(wp);
        waypoints.Clear();
        lineRenderer.positionCount = 0;
        publisher.WayPointObjectList.Clear();
    }

    private void ShowSuggestionMessage()
    {
        if (suggestionMessage == null || suggestionMessage.activeSelf) return;
        var cam = Camera.main;
        if (cam == null) return;

        suggestionMessage.transform.position = cam.transform.position + cam.transform.forward * messageDistance;
        suggestionMessage.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
        suggestionMessage.SetActive(true);
    }

    public void PublishEditedRoute()
    {
        Aligin.GetComponent<AlignToTarget>().enabled = false;
        publisher.PublishStatus = true;
    }
}
