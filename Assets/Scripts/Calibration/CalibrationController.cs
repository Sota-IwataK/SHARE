using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CalibrationController : MonoBehaviour
{
    [SerializeField] private RosSharp.RosBridgeClient.CalibrationFloat32Publisher calibrationFloat32Publisher;
    [SerializeField] private ObjectGenerationTest objectGeneration;
    public float[] data;
    public List<float> objpositionList;


    public void CalibrationButton()
    {
        calibrationFloat32Publisher.enabled = true;
    }

    public float[] CalibrationMessage()
    {
        objpositionList = new List<float>();
        if (objectGeneration.before_obj.Count >= 4)
        {
            for (int i = 0; i < objectGeneration.before_obj.Count; i++)
            {
                objpositionList.Add( objectGeneration.before_obj[i].transform.localPosition.x);
                objpositionList.Add( objectGeneration.before_obj[i].transform.localPosition.y);
                objpositionList.Add( objectGeneration.before_obj[i].transform.localPosition.z);
            }

            data = objpositionList.ToArray();
        }

        return data;
    }

}
