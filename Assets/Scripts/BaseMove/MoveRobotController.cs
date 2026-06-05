using MixedReality.Toolkit.UX;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MixedReality.Toolkit.SpatialManipulation;
using MixedReality.Toolkit.Input;
using Microsoft.MixedReality.OpenXR;
using static MixedReality.Toolkit.SpatialManipulation.ObjectManipulator;
using UnityEngine.XR.Interaction.Toolkit;
using System.Runtime.Serialization;
using System.Reflection;

public class MoveRobotController : MonoBehaviour
{
    [SerializeField] private PathPublisher pathPublisher;

    [SerializeField] private GameObject YoubotObject;
    [SerializeField] private GameObject BBox;

    private bool isManipulating = false;

    [SerializeField] private GameObject WayPointObject; // 生成するWaypointオブジェクト
    private float generationDistance = 0.5f;

    private Vector3 lastPosition;
    private Vector3 currentPosition;
    private float movedDistance = 0.0f;

    private List<GameObject> generatedObjects = new List<GameObject>();

    [SerializeField] private GameObject lineRendererPrefab; // LineRendererのPrefab
    private LineRenderer lineRenderer;
    private LineRenderer lineRenderer_Origin;

    [SerializeField] private GameObject WayPoints;

    [SerializeField] private GameObject Origin;
    private List<GameObject> generatedWayPointLists_Origin = new List<GameObject>();

    private bool isRobotMoveSetting = false;

    [SerializeField] private UpdateYouBotTransform updateYouBotTransform;

    private GameObject lastGeneratedWayPoint = null;
    

    public void PressRobotMoveSettingButton()
    {
        isRobotMoveSetting = true;



        Vector3 direction = Camera.main.transform.position - BBox.transform.position;
        Quaternion rotation = Quaternion.LookRotation(direction);
        //BBox.transform.localRotation = rotation * Quaternion.Euler(-90, 0, 0);
        BBox.transform.localRotation = rotation * Quaternion.Euler(0, 0, 0);
        
        currentPosition = YoubotObject.transform.position;
        lastPosition = currentPosition;

        // LineRendererを初期化
        GameObject lineRendererObj = Instantiate(lineRendererPrefab);
        lineRenderer = lineRendererObj.GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        //lineRenderer.SetPosition(0, currentPosition);

        // LineRendererを初期化
        GameObject lineRendererObj_Origin = Instantiate(lineRendererPrefab);
        lineRenderer_Origin = lineRendererObj_Origin.GetComponent<LineRenderer>();
        lineRenderer_Origin.positionCount = 0;
        //lineRenderer_Origin.SetPosition(0, Origin.transform.position);


        

        GenerateObject();
      
        YoubotObject.SetActive(true);
        // 子の youbot を後から明示的にアクティブ化（もし最初非アクティブにしてた場合）
        Transform youbot = YoubotObject.transform.Find("youbot");
        if (youbot != null)
        {
            youbot.gameObject.SetActive(true);
        }


    }

    public void PressRobotMoveActionButton()
    {
        
        isRobotMoveSetting = false;
        if (generatedObjects[generatedObjects.Count - 1]!= null)
        {
           if ((generatedObjects[generatedObjects.Count - 1].transform.position - YoubotObject.transform.position).magnitude <= 1){
               generatedObjects.RemoveAt(generatedObjects.Count - 1);
               generatedWayPointLists_Origin.RemoveAt(generatedObjects.Count - 1);
            }
        }

        //GenerateObject();
        pathPublisher.WayPointObjectList = generatedWayPointLists_Origin;
        pathPublisher.PublishStatus = true;
        updateYouBotTransform.isUpdate = true;
    }

    private void Update()
    {
        
        if (!isRobotMoveSetting) return; 

        currentPosition = YoubotObject.transform.localPosition;

        if (isManipulating)
        {
            movedDistance += Vector3.Distance(currentPosition, lastPosition);

            while (movedDistance >= generationDistance)
            {
                GenerateObject();
                movedDistance -= generationDistance;
                
            }
        }

        lastPosition = currentPosition;
    }

    public void OnManipulationStartedHandler(SelectEnterEventArgs args)
    {
        isManipulating = true;
        BBox.GetComponent<ObjectManipulator>().enabled = false;
    }


    public void OnManipulationEndedHandler(SelectExitEventArgs args)
    {
        isManipulating = false;
        //BBox.GetComponent<ObjectManipulator>().enabled = true;
    }

    private void GenerateObject()
    {

        Vector3 spawnPosition = YoubotObject.transform.position;
        Quaternion spawnRotation = YoubotObject.transform.rotation; // 初期は現在の向き
                                                                    // WayPointを生成
        GameObject newWayPoint = Instantiate(WayPointObject);
        newWayPoint.transform.position = spawnPosition;
        newWayPoint.transform.parent = WayPoints.transform;
        newWayPoint.transform.localRotation = Quaternion.identity;

      /*  if (lastGeneratedWayPoint != null)
        {
            Vector3 direction = (lastGeneratedWayPoint.transform.position - spawnPosition).normalized;
            Debug.Log(direction);
            if (direction != Vector3.zero)
            {
                spawnRotation = Quaternion.LookRotation(direction, Vector3.up);
                newWayPoint.transform.rotation = spawnRotation * Quaternion.Euler(-90, -90, 0);

            }
        }*/



        Vector3 WaypointPositionForOrigin = newWayPoint.transform.localPosition;
        Quaternion WaypointRotationForOrigin = newWayPoint.transform.localRotation;

        GameObject newWayPoint_Origin = Instantiate(WayPointObject);
        newWayPoint_Origin.transform.parent = Origin.transform;
        newWayPoint_Origin.transform.localPosition = WaypointPositionForOrigin;
        newWayPoint_Origin.transform.localRotation = WaypointRotationForOrigin;



        lastGeneratedWayPoint = newWayPoint;

        generatedObjects.Add(newWayPoint);
        generatedWayPointLists_Origin.Add(newWayPoint_Origin);
        // LineRendererにポイントを追加
        AddLinePoint(YoubotObject.transform.position, newWayPoint_Origin.transform.position);
        newWayPoint.GetComponent<ObjectManipulator>().selectExited.AddListener(EditRouteObject);
        newWayPoint_Origin.GetComponent<ObjectManipulator>().selectExited.AddListener(EditRouteObject);


    }

    public void EditRouteObject(SelectExitEventArgs args)
    {
         GameObject currentlyManipulatedObject = args.interactableObject.transform.gameObject;

        int index = generatedWayPointLists_Origin.IndexOf(currentlyManipulatedObject);

        if(index >= 0)
        {
            Vector3 WaypointPositionForOrigin = currentlyManipulatedObject.transform.localPosition;
            generatedObjects[index].transform.localPosition = WaypointPositionForOrigin;
            generatedObjects[index].transform.localRotation = currentlyManipulatedObject.transform.localRotation;
            UpdateLinePoint(index, generatedObjects[index].transform.position, currentlyManipulatedObject.transform.position);
        }

        index = generatedObjects.IndexOf(currentlyManipulatedObject);

        if (index >= 0)
        {
            Vector3 WaypointPositionForOrigin = currentlyManipulatedObject.transform.localPosition;
            generatedWayPointLists_Origin[index].transform.localPosition = WaypointPositionForOrigin;
            generatedWayPointLists_Origin[index].transform.localRotation = currentlyManipulatedObject.transform.localRotation;
            UpdateLinePoint(index, currentlyManipulatedObject.transform.position, generatedWayPointLists_Origin[index].transform.position);
        }
    }

    private void AddLinePoint(Vector3 position,Vector3 positionForOrigin)
    {
        lineRenderer.positionCount += 1;
        lineRenderer_Origin.positionCount += 1;
        lineRenderer.SetPosition(lineRenderer.positionCount - 1, position);
        lineRenderer_Origin.SetPosition(lineRenderer_Origin.positionCount - 1, positionForOrigin);
    }

    public void UpdateLinePoint(int PositionCount,Vector3 UpdatePos,Vector3 UpdatePosForOrigin)
    {
        lineRenderer.SetPosition(PositionCount, UpdatePos);
        lineRenderer_Origin.SetPosition(PositionCount, UpdatePosForOrigin);
    }
}
