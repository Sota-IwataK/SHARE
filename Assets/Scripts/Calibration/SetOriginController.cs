using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SetOriginController : MonoBehaviour
{
    [SerializeField] private ObjectGenerationTest objectGeneration;
    [SerializeField] private List<GameObject> objectList;
    [SerializeField] private GameObject Origin;
    [SerializeField] private GameObject youbot;

    public void SetOriginButton()
    {
        objectList = objectGeneration.before_obj;
        Origin.transform.position = new Vector3(Camera.main.transform.position.x, Camera.main.transform.position.y - 0.45f, Camera.main.transform.position.z);
        youbot.transform.position = new Vector3(Camera.main.transform.position.x + 0.197f, Camera.main.transform.position.y - 0.45f, Camera.main.transform.position.z);
        foreach (var obj in objectList)
        {
            obj.transform.localPosition = new Vector3(obj.transform.localPosition.x, obj.transform.localPosition.y -0.3f, obj.transform.localPosition.z);
        }
    }
}
