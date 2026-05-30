using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient.MessageTypes.Nav;
using RosSharp.RosBridgeClient;

public class SuggestionGenerateRoute : MonoBehaviour
{
    [SerializeField] private GameObject RouteObject; // 経路を示すオブジェクトのプレハブ
    private List<GameObject> instantiatedObjects = new List<GameObject>(); // 生成したオブジェクトを管理

    [SerializeField] private GameObject InitializeYouBotPosition;

    [SerializeField] private SuggestionRouteSubscriber suggestionRouteSubscriber;

    private Path SuggestionRoutePath;

    [SerializeField] private GameObject lineRendererPrefab; // LineRendererのPrefab
    private LineRenderer lineRenderer;

    [SerializeField] private GameObject SuggestionMessageObject;
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
        if(suggestionRouteSubscriber.messagePath!= null && suggestionRouteSubscriber.messagePath.poses.Length > 0)
        {
            if(SuggestionMessageObject.activeSelf == false && !Suggestioned)
            {
                Suggestioned = true;
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    // カメラの前方にオブジェクトを配置
                    Vector3 positionInFrontOfCamera = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
                    SuggestionMessageObject.transform.position = positionInFrontOfCamera;

                    // カメラの向きに合わせて回転を設定
                    SuggestionMessageObject.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward);

                    // オブジェクトを有効化
                    SuggestionMessageObject.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("Main Camera が見つかりません！");
                }

                SuggestionRoutePath = suggestionRouteSubscriber.messagePath;
                ClearExistingRouteObjects();
                GenerateRouteObjects(SuggestionRoutePath);
            }


        }
    }
    // 経路上の各位置にオブジェクトを生成
    private void GenerateRouteObjects(Path path)
    {
        foreach (var poseStamped in path.poses)
        {
            Debug.Log(poseStamped.pose.position);
            Vector3 position = new Vector3(
                (float)-poseStamped.pose.position.x,
                (float)InitializeYouBotPosition.transform.position.y,
                (float)-poseStamped.pose.position.y
            );

            Quaternion rotation = new Quaternion(
                (float)poseStamped.pose.orientation.z,
                (float)-poseStamped.pose.orientation.x,
                (float)poseStamped.pose.orientation.y,
                (float)-poseStamped.pose.orientation.w
            );

            GameObject routeObject = Instantiate(RouteObject);
            routeObject.transform.parent = InitializeYouBotPosition.transform;
            routeObject.transform.localPosition = new Vector3(position.x,0.147f,position.z);
            routeObject.transform.localRotation = Quaternion.identity;
            //routeObject.transform.Rotate(transform.up, 90);

            instantiatedObjects.Add(routeObject); // 生成したオブジェクトをリストに追加

            AddLinePoint(instantiatedObjects[instantiatedObjects.Count - 1].transform.position,routeObject.transform.position);
        }
    }

    // 既存のオブジェクトをクリア
    private void ClearExistingRouteObjects()
    {
        foreach (var obj in instantiatedObjects)
        {
            Destroy(obj);
        }
        instantiatedObjects.Clear();
        lineRenderer.positionCount = 0;
    }

    private void AddLinePoint(Vector3 position, Vector3 positionForOrigin)
    {
        lineRenderer.positionCount += 1;
        lineRenderer.SetPosition(lineRenderer.positionCount - 1, position);
    }
}
