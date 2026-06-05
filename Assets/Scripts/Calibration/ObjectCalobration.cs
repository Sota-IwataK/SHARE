using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectCalobration : MonoBehaviour
{
    [SerializeField] private CalibrationFloat32Subscriber calibration;
    [SerializeField] private ObjectGenerationTest objectGeneration;

    [SerializeField ]private float[] data;

    [SerializeField] private Vector3 position;
    [SerializeField] private List<Vector3> positionList;

    [SerializeField] private List<GameObject> objectList;

    [SerializeField] private List<string> NotSelectGameObject;

    [SerializeField] private SelectObject selectObject;

    [SerializeField] private GameObject BoungingBox;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;

    [ColorUsage(false, true)] public Color DefalutColor;

    public void CalibrationButton()
    {
        if (calibration == null || calibration.messageData == null)
        {
            Debug.LogError("[ObjectCalobration] Calibration subscriber data is not available.");
            return;
        }

        if (objectGeneration == null || selectObject == null)
        {
            Debug.LogError("[ObjectCalobration] objectGeneration or selectObject is not assigned.");
            return;
        }

        positionList = new List<Vector3>();
        data = calibration.messageData;
        objectList = objectGeneration.before_obj;
        for (int i = 0; i < data.Length / 3; i++)
        {
            position = new Vector3(data[i * 3], data[i * 3 + 1], data[i * 3  + 2]);
            positionList.Add(position);
            Debug.Log(position);
        }

        NotSelectGameObject = selectObject.NotSelectObjectList;

        foreach (var name in NotSelectGameObject)
        {
            Debug.Log("NotSelectGameObjectName : " + name);
            for (int j = 0; j < objectList.Count; j++)
            {
                Debug.Log("ObjectName : "+objectList[j].name);
                if (objectList[j].name == name)
                {
                    Debug.Log(objectList[j].transform.position);
                    objectList[j].transform.localPosition = positionList[j];
                    Debug.Log(positionList[j]);
                }
            }
        }
        BoungingBox.transform.localPosition = pointCloudRenderer.BBoxPos;
        BoungingBox.transform.rotation = Quaternion.identity;
        BoungingBox.transform.localScale = pointCloudRenderer.BBoxSize;

        ChangeObjectColor();
        selectObject.CalibrationMode = false;
        selectObject.MarkCalibrationComplete();
        
    }

    private void ChangeObjectColor()
    {
        foreach (GameObject obj in objectGeneration.GenerateObjects)
        {
            obj.gameObject.GetComponent<MeshRenderer>().material.color = DefalutColor;
        }
    }
}
