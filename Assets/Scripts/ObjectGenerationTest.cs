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
    private const int CalibrationSphereCount = 4;
    private const string CalibrationCurrentSphereName = "CalibrationSphere_Current";
    private const float CalibrationAxisSpawnDistance = 0.45f;
    private const float CalibrationAxisHeightOffset = -0.18f;
    private const float CalibrationAxisLength = 0.15f;
    private const float CalibrationSphereDiameter = 0.12f;
    private const string SendZoneSphereName = "SendZoneSphere";
    private const float SendZoneSphereSpawnDistance = 0.4f;
    private const float SendZoneSphereHeightOffset = -0.22f;
    private const float SendZoneSphereDiameter = 0.16f;
    private static readonly string[] CalibrationPointLabels = { "O", "+X", "+Y", "+Z" };
    private static readonly Color[] CalibrationPointColors =
    {
        Color.white,
        Color.red,
        Color.green,
        Color.blue
    };
    private static readonly Color SendZoneSphereColor = Color.yellow;

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

    private Vector3 calibrationCenter;
    private Vector3 calibrationRight;
    private Vector3 calibrationUp;
    private Vector3 calibrationForward;
    private bool hasCalibrationReference;

    void Start()
    {

    }

    public void EnsureCalibrationBottles()
    {
        EnsureObjectLists();
        RemoveNullGeneratedObjects();

        hasCalibrationReference = false;
        if (!RefreshCalibrationReference())
        {
            return;
        }

        HideLegacyCalibrationSpheres();
        GameObject sphere = GetOrCreateCurrentCalibrationSphere(out bool spawned);
        if (sphere == null)
        {
            Debug.LogError("[ObjectGenerationTest] Cannot spawn calibration sphere.");
            return;
        }

        MoveCalibrationSphereToPoint(0, out _);
        RegisterGeneratedObject(sphere);
        Debug.Log(spawned
            ? "[ObjectGenerationTest] Spawned sequential calibration sphere. name=CalibrationSphere_Current"
            : "[ObjectGenerationTest] Reused sequential calibration sphere. name=CalibrationSphere_Current");
    }

    public bool MoveCalibrationSphereToPoint(int zeroBasedIndex, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (zeroBasedIndex < 0 || zeroBasedIndex >= CalibrationSphereCount)
        {
            Debug.LogError("[ObjectGenerationTest] Invalid calibration point index: " + zeroBasedIndex);
            return false;
        }

        if (!hasCalibrationReference && !RefreshCalibrationReference())
        {
            return false;
        }

        GameObject sphere = GetOrCreateCurrentCalibrationSphere(out _);
        if (sphere == null)
        {
            return false;
        }

        worldPosition = GetCalibrationPointWorldPosition(zeroBasedIndex);
        PrepareCalibrationSphere(sphere, worldPosition, zeroBasedIndex);
        LogCalibrationSphere(sphere);
        return true;
    }

    public GameObject GetCurrentCalibrationSphere()
    {
        return FindSceneObjectByName(CalibrationCurrentSphereName);
    }

    public void HideCalibrationSphere()
    {
        GameObject sphere = GetCurrentCalibrationSphere();
        if (sphere != null)
        {
            sphere.SetActive(false);
        }
    }

    public bool GetAxisCalibrationBasis(out Vector3 origin, out Vector3 xDir, out Vector3 yDir, out Vector3 zDir)
    {
        if (!hasCalibrationReference && !RefreshCalibrationReference())
        {
            origin = Vector3.zero;
            xDir = Vector3.right;
            yDir = Vector3.up;
            zDir = Vector3.forward;
            return false;
        }

        origin = calibrationCenter;
        xDir = calibrationRight;
        yDir = calibrationUp;
        zDir = calibrationForward;
        return true;
    }

    public string GetCalibrationPointLabel(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= CalibrationPointLabels.Length)
        {
            return "?";
        }

        return CalibrationPointLabels[zeroBasedIndex];
    }

    public float GetCalibrationAxisLength()
    {
        return CalibrationAxisLength;
    }

    public float GetCalibrationSphereDiameter()
    {
        return CalibrationSphereDiameter;
    }

    public float GetSendZoneSphereDiameter()
    {
        return SendZoneSphereDiameter;
    }

    public bool ShowSendZoneSphere(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        Transform hmdTransform = FindHmdTransform();
        if (hmdTransform == null)
        {
            Debug.LogError("[ObjectGenerationTest] Cannot spawn SendZone sphere because no HMD transform was found.");
            return false;
        }

        Vector3 forwardFlat = GetForwardFlat(hmdTransform);
        worldPosition = hmdTransform.position + forwardFlat * SendZoneSphereSpawnDistance;
        worldPosition.y = hmdTransform.position.y + SendZoneSphereHeightOffset;

        GameObject sphere = FindSceneObjectByName(SendZoneSphereName);
        if (sphere == null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = SendZoneSphereName;
        }

        PrepareVisibleSphere(sphere, SendZoneSphereName, worldPosition, SendZoneSphereDiameter, SendZoneSphereColor, "Untagged");
        return true;
    }

    public GameObject GetSendZoneSphere()
    {
        return FindSceneObjectByName(SendZoneSphereName);
    }

    public void HideSendZoneSphere()
    {
        GameObject sphere = GetSendZoneSphere();
        if (sphere != null)
        {
            sphere.SetActive(false);
        }
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

    private void EnsureObjectLists()
    {
        before_obj ??= new List<GameObject>();
        ObjectNameList ??= new List<string>();
        GenerateObjects ??= new List<GameObject>();
    }

    private void RemoveNullGeneratedObjects()
    {
        before_obj.RemoveAll(item => item == null);
        ObjectNameList.RemoveAll(string.IsNullOrEmpty);
        GenerateObjects.RemoveAll(item => item == null);
    }

    private static string GetCalibrationSphereName(int zeroBasedIndex)
    {
        return "CalibrationSphere_" + (zeroBasedIndex + 1);
    }

    private bool RefreshCalibrationReference()
    {
        Transform hmdTransform = FindHmdTransform();
        if (hmdTransform == null)
        {
            Debug.LogError("[ObjectGenerationTest] Cannot spawn calibration sphere because no HMD transform was found.");
            return false;
        }

        Vector3 forwardFlat = GetForwardFlat(hmdTransform);
        calibrationCenter = hmdTransform.position + forwardFlat * CalibrationAxisSpawnDistance;
        calibrationCenter.y = hmdTransform.position.y + CalibrationAxisHeightOffset;
        calibrationRight = hmdTransform.right.sqrMagnitude > 0.0001f ? hmdTransform.right.normalized : Vector3.right;
        calibrationUp = Vector3.up;
        calibrationForward = forwardFlat;
        hasCalibrationReference = true;
        LogCalibrationSpawnReference(hmdTransform);
        return true;
    }

    private GameObject GetOrCreateCurrentCalibrationSphere(out bool spawned)
    {
        spawned = false;
        GameObject sphere = FindSceneObjectByName(CalibrationCurrentSphereName);
        if (sphere == null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = CalibrationCurrentSphereName;
            spawned = true;
        }

        if (Origin != null)
        {
            sphere.transform.SetParent(Origin.transform, true);
        }

        return sphere;
    }

    private Vector3 GetCalibrationPointWorldPosition(int zeroBasedIndex)
    {
        switch (zeroBasedIndex)
        {
            case 0:
                return calibrationCenter;
            case 1:
                return calibrationCenter + calibrationRight * CalibrationAxisLength;
            case 2:
                return calibrationCenter + calibrationUp * CalibrationAxisLength;
            case 3:
                return calibrationCenter + calibrationForward * CalibrationAxisLength;
            default:
                return calibrationCenter;
        }
    }

    private void HideLegacyCalibrationSpheres()
    {
        for (int i = 0; i < CalibrationSphereCount; i++)
        {
            GameObject legacySphere = FindSceneObjectByName(GetCalibrationSphereName(i));
            if (legacySphere != null)
            {
                legacySphere.SetActive(false);
            }
        }
    }

    private void PrepareCalibrationSphere(GameObject target, Vector3 worldPosition, int zeroBasedIndex)
    {
        PrepareVisibleSphere(target, CalibrationCurrentSphereName, worldPosition, CalibrationSphereDiameter, GetCalibrationPointColor(zeroBasedIndex), "bottle");
    }

    private void PrepareVisibleSphere(GameObject target, string objectName, Vector3 worldPosition, float diameter, Color color, string tagName)
    {
        target.SetActive(true);
        target.name = objectName;
        target.tag = tagName;
        SetLayerRecursively(target, LayerMask.NameToLayer("Default"));
        target.transform.position = worldPosition;
        target.transform.localScale = Vector3.one * diameter;

        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = target.AddComponent<MeshRenderer>();
        }
        renderer.enabled = true;

        Material material = EnsureQuestVisibleMaterial(renderer);
        if (material != null)
        {
            SetMaterialOpaqueColor(material, color);
        }

        SphereCollider sphereCollider = target.GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = target.AddComponent<SphereCollider>();
        }
        sphereCollider.enabled = true;
        sphereCollider.isTrigger = true;
    }

    private static Color GetCalibrationPointColor(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= CalibrationPointColors.Length)
        {
            return Color.white;
        }

        return CalibrationPointColors[zeroBasedIndex];
    }

    private static Transform FindHmdTransform()
    {
        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        GameObject taggedCamera = GameObject.FindGameObjectWithTag("MainCamera");
        if (taggedCamera != null)
        {
            return taggedCamera.transform;
        }

        Transform centerEyeAnchor = FindSceneTransformByName("CenterEyeAnchor");
        if (centerEyeAnchor != null)
        {
            return centerEyeAnchor;
        }

        Transform xrOrigin = FindSceneTransformByName("XROrigin") ?? FindSceneTransformByName("XR Origin");
        if (xrOrigin != null)
        {
            Camera xrCamera = xrOrigin.GetComponentInChildren<Camera>(true);
            if (xrCamera != null)
            {
                return xrCamera.transform;
            }
        }

        Transform ovrCameraRig = FindSceneTransformByName("OVRCameraRig");
        if (ovrCameraRig != null)
        {
            Transform ovrCenterEye = FindChildTransformByName(ovrCameraRig, "CenterEyeAnchor");
            if (ovrCenterEye != null)
            {
                return ovrCenterEye;
            }
        }

        return null;
    }

    private static Vector3 GetForwardFlat(Transform hmdTransform)
    {
        Vector3 forwardFlat = Vector3.ProjectOnPlane(hmdTransform.forward, Vector3.up);
        if (forwardFlat.sqrMagnitude < 0.0001f)
        {
            forwardFlat = Vector3.forward;
        }

        return forwardFlat.normalized;
    }

    private static Material EnsureQuestVisibleMaterial(Renderer renderer)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Standard");
        if (shader == null)
        {
            return renderer.material;
        }

        Material material = renderer.material;
        if (material == null || material.shader == null || material.shader != shader)
        {
            material = new Material(shader);
            renderer.material = material;
        }

        return material;
    }

    private static void SetMaterialOpaqueColor(Material material, Color color)
    {
        Color visibleColor = color;
        visibleColor.a = 1f;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", visibleColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", visibleColor);
        }

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 1f);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = -1;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        int resolvedLayer = layer >= 0 ? layer : 0;
        target.layer = resolvedLayer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, resolvedLayer);
        }
    }

    private static GameObject FindSceneObjectByName(string objectName)
    {
        Transform transform = FindSceneTransformByName(objectName);
        return transform != null ? transform.gameObject : null;
    }

    private static Transform FindSceneTransformByName(string objectName)
    {
        foreach (Transform candidate in FindObjectsOfType<Transform>(true))
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
            {
                return candidate;
            }
        }

        return null;
    }

    private static Transform FindChildTransformByName(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }

    private void LogCalibrationSpawnReference(Transform hmdTransform)
    {
        Debug.Log("[ObjectGenerationTest] HMD transform: name=" + hmdTransform.name);
        Debug.Log("[ObjectGenerationTest] HMD world position=" + hmdTransform.position
            + ", forward=" + hmdTransform.forward);

        if (Origin != null)
        {
            Debug.Log("[ObjectGenerationTest] ObjectList world position=" + Origin.transform.position
                + ", localScale=" + Origin.transform.localScale);
        }
        else
        {
            Debug.LogWarning("[ObjectGenerationTest] ObjectList Origin is not assigned. Calibration spheres will remain unparented.");
        }
    }

    private static void LogCalibrationSphere(GameObject sphere)
    {
        SphereCollider sphereCollider = sphere.GetComponent<SphereCollider>();
        MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
        Material material = renderer != null ? renderer.material : null;
        Color materialColor = Color.clear;
        if (material != null)
        {
            if (material.HasProperty("_BaseColor"))
            {
                materialColor = material.GetColor("_BaseColor");
            }
            else if (material.HasProperty("_Color"))
            {
                materialColor = material.GetColor("_Color");
            }
        }

        Debug.Log("[ObjectGenerationTest] Calibration sphere: name=" + sphere.name
            + ", world position=" + sphere.transform.position
            + ", local position=" + sphere.transform.localPosition
            + ", scale=" + sphere.transform.localScale
            + ", tag=" + sphere.tag
            + ", layer=" + LayerMask.LayerToName(sphere.layer)
            + ", collider=" + (sphereCollider != null && sphereCollider.enabled)
            + ", renderer.enabled=" + (renderer != null && renderer.enabled)
            + ", material shader=" + (material != null && material.shader != null ? material.shader.name : "null")
            + ", material color alpha=" + materialColor.a);
    }

    private void RegisterGeneratedObject(GameObject target)
    {
        if (!before_obj.Contains(target))
        {
            before_obj.Add(target);
        }

        if (!ObjectNameList.Contains(target.name))
        {
            ObjectNameList.Add(target.name);
        }

        if (!GenerateObjects.Contains(target))
        {
            GenerateObjects.Add(target);
        }

        objectManipulator = target.GetComponent<ObjectManipulator>();
        if (objectManipulator != null)
        {
            objectManipulator.firstSelectEntered.AddListener(OnManipulationStartedHandler);
            objectManipulator.lastSelectExited.AddListener(OnManipulationEndedHandler);
        }
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
