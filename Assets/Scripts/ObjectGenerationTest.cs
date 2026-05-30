using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.UX;
using MixedReality.Toolkit.SpatialManipulation;
using MixedReality.Toolkit.Input;
using Microsoft.MixedReality.OpenXR;
using UnityEngine.XR.Interaction.Toolkit;


public class ObjectGenerationTest : MonoBehaviour
{

    public Float32MultiSubscriber float32MultiSubscriber;
    // Start is called before the first frame update
    public float[] messageData = new float[5];

    public GameObject bottle;
    public GameObject Can;

    float label;
    float x;
    float y;
    float z;
    float id;
    string ObjectName = "bottle";
    public float[] idlist;
    GameObject obj;
    GameObject obj1;
    public airTap_distance distance;
    public string outputdata;
    public GameObject maincam;
    Vector3 camVector;
    public Vector3 ObjectPosition;
    public IdTracking idTracking;

    [SerializeField] private GameObject Origin;

    public List<GameObject> before_obj;

    public List<string> ObjectNameList;

    private ObjectManipulator objectManipulator;
    [SerializeField] private GameObject BBox;

    public Dictionary<float, Vector3> objectList = new Dictionary<float, Vector3>();

    public List<GameObject> GenerateObjects;

    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

        outputdata = distance.bool2string();
        camVector = Origin.transform.position;
        if (float32MultiSubscriber.messageData.Length != 0)
        {
            idlist = new float[float32MultiSubscriber.messageData.Length / 4];
            messageData = float32MultiSubscriber.messageData;

            for (int i = 0; i < messageData.Length / 5; i++)
            {

                if (i == 0)
                {
                    //minx = (messageData[0]/640)* Screen.currentResolution.width;
                    //miny = (messageData[1]/480)* Screen.currentResolution.height;
                    //maxx = (messageData[2]/640)* Screen.currentResolution.width;
                    //maxy = (messageData[3]/480)* Screen.currentResolution.height;
                    x = -messageData[0];
                    //y = messageData[2]-0.6f;
                    y = messageData[2]; //キャリブレーション時使う値
                    //y = messageData[2] - 0.25f;　//実際の値
                    z = -messageData[1];
                    label = messageData[3];
                    id = messageData[4];
                    ObjectName = "Object" + id.ToString();
                    if (label == 39)
                    {
                        if (!GameObject.Find(ObjectName))
                        {
                            Debug.Log("Buttlename : " + ObjectName);
                            Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);

                            obj = Instantiate(bottle, camVector, Quaternion.identity);
                            obj.name = ObjectName;
                            obj.transform.parent = Origin.transform;
                            obj.transform.localPosition = new Vector3(x, y, z);
                            //回転が反映していないのが原因？
                            before_obj.Add(obj);
                            ObjectNameList.Add(obj.name);
                            // ObjectManipulatorコンポーネントを取得
                            objectManipulator = obj.GetComponent<ObjectManipulator>();

                            if (objectManipulator != null)
                            {
                                // OnManipulationStartedイベントにメソッドを登録
                                //objectManipulator.OnManipulationStarted.AddListener(OnManipulationStartedHandler);
                                
                                objectManipulator.firstSelectEntered.AddListener(OnManipulationStartedHandler);
                                objectManipulator.lastSelectExited.AddListener(OnManipulationEndedHandler);

                            }
                        }

                        else if (outputdata == "open")
                        {

                            Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);
                            obj.transform.position = world_position;



                        }

                    }

                }
                else
                {
                    x = -messageData[5 * i + 0];
                    //y = messageData[5 * i + 2] - 0.6f;

                    y = messageData[5 * i + 2];//キャリブレーション時に使う値
                    //y = messageData[5 * i + 2] - 0.25f;//実際の値
                    z = -messageData[5 * i + 1];
                    label = messageData[5 * i + 3];
                    id = messageData[5 * i + 4];

                    ObjectName = "Object" + id.ToString();

                    if (label == 39)
                    {
                        if (!GameObject.Find(ObjectName))
                        {

                            Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);

                            obj1 = Instantiate(bottle, camVector, Quaternion.identity);
                            //obj1 = Instantiate(bottle, world_position, Quaternion.identity);
                            obj1.name = ObjectName;
                            obj1.transform.parent = Origin.transform;
                            obj1.transform.localPosition = new Vector3(x, y, z);

                            before_obj.Add(obj1);
                            ObjectNameList.Add(obj1.name);
                            // ObjectManipulatorコンポーネントを取得
                            objectManipulator = obj1.GetComponent<ObjectManipulator>();

                            if (objectManipulator != null)
                            {
                                // OnManipulationStartedイベントにメソッドを登録
                                objectManipulator.firstSelectEntered.AddListener(OnManipulationStartedHandler);
                                objectManipulator.lastSelectExited.AddListener(OnManipulationEndedHandler);
                            }
                        }


                        else if (outputdata == "open")
                        {

                            Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);
                            obj1.transform.position = world_position;


                        }

                    }

                }
                if (!objectList.ContainsKey(id))
                {
                    if (label == 0 || label == 1)
                    {
                        if (!GameObject.Find(ObjectName))
                        {
                            Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);

                            foreach (Vector3 objectPos in objectList.Values)
                            {
                                float distance = Vector3.Distance(objectPos, world_position);
                                UnityEngine.Debug.Log("objct distance ; " + distance);
                                if (Mathf.Abs(distance) <= 0.05)
                                {
                                    return;
                                }
                            }
                            if (label == 0)
                            {

                                obj1 = Instantiate(Can, camVector, Quaternion.identity);
                                //obj1.transform.localScale = Vector3.one * 1.2f;
                            }
                            else if (label == 1)
                            {
                                obj1 = Instantiate(bottle, camVector, Quaternion.identity);
                                //obj1.transform.localScale = Vector3.one * 0.8f;
                            }
                            //obj1 = Instantiate(bottle, world_position, Quaternion.identity);
                            obj1.name = ObjectName;
                            obj1.transform.parent = Origin.transform;
                            obj1.transform.localPosition = new Vector3(x, y, z);

                            before_obj.Add(obj1);
                            ObjectNameList.Add(obj1.name);

                            objectList.Add(id, world_position);
                            // ObjectManipulatorコンポーネントを取得
                            objectManipulator = obj1.GetComponent<ObjectManipulator>();
                            GenerateObjects.Add(obj1);
                            if (objectManipulator != null)
                            {
                                // OnManipulationStartedイベントにメソッドを登録
                                objectManipulator.firstSelectEntered.AddListener(OnManipulationStartedHandler);
                                objectManipulator.lastSelectExited.AddListener(OnManipulationEndedHandler);




                            }
                        }

                    }
                }

            }
        }
    }

    /*private void OnManipulationStartedHandler(ManipulationEventData arg0)
    {
        BBox.GetComponent<ObjectManipulator>().enabled = false;
    }
    private void OnManipulationEndedHandler(ManipulationEventData arg0)
    {
        BBox.GetComponent<ObjectManipulator>().enabled = true;

    }*/
    public void OnManipulationStartedHandler(SelectEnterEventArgs args)
    {

        BBox.GetComponent<ObjectManipulator>().enabled = false;
    }


    public void OnManipulationEndedHandler(SelectExitEventArgs args)
    {

        //BBox.GetComponent<ObjectManipulator>().enabled = true;
    }


}



/*

using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit; // ← 追加
using MixedReality.Toolkit.UX;

public class ObjectGenerationTest : MonoBehaviour
{
    public Float32MultiSubscriber float32MultiSubscriber;
    public float[] messageData = new float[5];

    public GameObject bottle;
    public GameObject Can;

    float label;
    float x;
    float y;
    float z;
    float id;
    string ObjectName = "bottle";
    public float[] idlist;
    GameObject obj;
    GameObject obj1;
    public airTap_distance distance;
    public string outputdata;
    public GameObject maincam;
    Vector3 camVector;
    public Vector3 ObjectPosition;
    public IdTracking idTracking;

    [SerializeField] private GameObject Origin;
    [SerializeField] private GameObject BBox;

    public List<GameObject> before_obj;
    public List<string> ObjectNameList;
    public Dictionary<float, Vector3> objectList = new Dictionary<float, Vector3>();
    public List<GameObject> GenerateObjects;

    void Start() { }

    void Update()
    {
        outputdata = distance.bool2string();
        camVector = Origin.transform.position;

        if (float32MultiSubscriber.messageData.Length == 0) return;

        idlist = new float[float32MultiSubscriber.messageData.Length / 4];
        messageData = float32MultiSubscriber.messageData;

        for (int i = 0; i < messageData.Length / 5; i++)
        {
            x = -messageData[5 * i + 0];
            y = messageData[5 * i + 2];
            z = -messageData[5 * i + 1];
            label = messageData[5 * i + 3];
            id = messageData[5 * i + 4];

            ObjectName = "Object" + id.ToString();

            if (label == 39)
            {
                if (!GameObject.Find(ObjectName))
                {
                    GameObject newObj = Instantiate(bottle, camVector, Quaternion.identity);
                    newObj.name = ObjectName;
                    newObj.transform.parent = Origin.transform;
                    newObj.transform.localPosition = new Vector3(x, y, z);

                    before_obj.Add(newObj);
                    ObjectNameList.Add(newObj.name);
                    GenerateObjects.Add(newObj);

                    AddGrabEvents(newObj);
                }
                else if (outputdata == "open")
                {
                    GameObject existingObj = GameObject.Find(ObjectName);
                    if (existingObj != null)
                    {
                        Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);
                        existingObj.transform.position = world_position;
                    }
                }
            }
            else if ((label == 0 || label == 1) && !objectList.ContainsKey(id))
            {
                if (!GameObject.Find(ObjectName))
                {
                    Vector3 world_position = new Vector3(camVector.x + x, camVector.y + y, camVector.z + z);

                    foreach (Vector3 objectPos in objectList.Values)
                    {
                        if (Vector3.Distance(objectPos, world_position) <= 0.05f) return;
                    }

                    GameObject newObj = label == 0 ? Instantiate(Can) : Instantiate(bottle);
                    newObj.name = ObjectName;
                    newObj.transform.parent = Origin.transform;
                    newObj.transform.localPosition = new Vector3(x, y, z);

                    before_obj.Add(newObj);
                    ObjectNameList.Add(newObj.name);
                    objectList.Add(id, world_position);
                    GenerateObjects.Add(newObj);

                    AddGrabEvents(newObj);
                }
            }
        }
    }

    private void AddGrabEvents(GameObject obj)
    {
        var grab = obj.GetComponent<XRGrabInteractable>();
        if (grab == null)
        {
            grab = obj.AddComponent<XRGrabInteractable>();
        }

        grab.selectEntered.AddListener((args) => OnGrabStarted());
        grab.selectExited.AddListener((args) => OnGrabEnded());
    }

    private void OnGrabStarted()
    {
        if (BBox != null) BBox.GetComponent<Collider>().enabled = false;
    }

    private void OnGrabEnded()
    {
        if (BBox != null) BBox.GetComponent<Collider>().enabled = true;
    }
}
*/