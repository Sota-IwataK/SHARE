using System.Collections.Generic;
using RosMessageTypes.Nav;
using UnityEngine;

public class SuggestionGenerateRoute : MonoBehaviour
{
    [SerializeField] private GameObject RouteObject;
    [SerializeField] private GameObject InitializeYouBotPosition;
    [SerializeField] private SuggestionRouteSubscriber suggestionRouteSubscriber;
    [SerializeField] private GameObject lineRendererPrefab;
    [SerializeField] private GameObject SuggestionMessageObject;

    private readonly List<GameObject> instantiatedObjects = new List<GameObject>();
    private PathMsg SuggestionRoutePath;
    private LineRenderer lineRenderer;
    private float distanceFromCamera = 2.0f;
    private bool Suggestioned = false;

    private void Start()
    {
        GameObject lineRendererObj = Instantiate(lineRendererPrefab);
        lineRenderer = lineRendererObj.GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
    }

    private void Update()
    {
        if (suggestionRouteSubscriber.messagePath == null ||
            suggestionRouteSubscriber.messagePath.poses.Length <= 0 ||
            Suggestioned ||
            SuggestionMessageObject.activeSelf)
        {
            return;
        }

        Suggestioned = true;
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Vector3 positionInFrontOfCamera = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
            SuggestionMessageObject.transform.position = positionInFrontOfCamera;
            SuggestionMessageObject.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward);
            SuggestionMessageObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Main Camera not found.");
        }

        SuggestionRoutePath = suggestionRouteSubscriber.messagePath;
        ClearExistingRouteObjects();
        GenerateRouteObjects(SuggestionRoutePath);
    }

    private void GenerateRouteObjects(PathMsg path)
    {
        foreach (var poseStamped in path.poses)
        {
            Debug.Log(poseStamped.pose.position);
            Vector3 position = new Vector3(
                (float)-poseStamped.pose.position.x,
                InitializeYouBotPosition.transform.position.y,
                (float)-poseStamped.pose.position.y);

            GameObject routeObject = Instantiate(RouteObject);
            routeObject.transform.parent = InitializeYouBotPosition.transform;
            routeObject.transform.localPosition = new Vector3(position.x, 0.147f, position.z);
            routeObject.transform.localRotation = Quaternion.identity;

            instantiatedObjects.Add(routeObject);
            AddLinePoint(routeObject.transform.position);
        }
    }

    private void ClearExistingRouteObjects()
    {
        foreach (var obj in instantiatedObjects)
        {
            Destroy(obj);
        }

        instantiatedObjects.Clear();
        lineRenderer.positionCount = 0;
    }

    private void AddLinePoint(Vector3 position)
    {
        lineRenderer.positionCount += 1;
        lineRenderer.SetPosition(lineRenderer.positionCount - 1, position);
    }
}
