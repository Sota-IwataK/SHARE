using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


public class SelectObject : MonoBehaviour
{
    [SerializeField] RosSharp.RosBridgeClient.AfterObjectFloat32Publisher afterObjectFloat32;
    [SerializeField] RosSharp.RosBridgeClient.BeforeObjectFloat32Publisher beforeObjectFloat32;
    [SerializeField] RosSharp.RosBridgeClient.CalibrationFloat32Publisher calibrationFloat32Publisher;
    [SerializeField] CalibrationFloat32Subscriber calibrationFloat32Subscriber;
    [SerializeField] RosSharp.RosBridgeClient.IRM_SerectObjectPublisher irmSelectPublisher;  // 追加: IRM Select Publisher

    public List<GameObject> ObjectList;
    public List<float> beforeList;
    public List<float> afterList;
    public float[] beforeData;
    public float[] afterData;
    [SerializeField] private GameObject Origin;


    [ColorUsage(false, true)] public Color CalibrationColor;
    [ColorUsage(false, true)] public Color SelectColor;
    [ColorUsage(false, true)] public Color PickColor;
    [ColorUsage(false, true)] public Color DefalutColor;

    bool active = true;

    [SerializeField] private ObjectGenerationTest objectGeneration;
    public List<string> NotSelectObjectList;

    public bool CalibrationMode;
    public bool MoveMode;

    public List<GameObject> PickObjectList;
    public List<float> PickObjectPositionList;

    void Start()
    {
        NotSelectObjectList = objectGeneration.ObjectNameList;
    }

    void OnTriggerEnter(Collider other)
    {
        if(other.tag == "bottle")
        {
            if (CalibrationMode == true)
            {
                if (!ObjectList.Contains(other.gameObject))
                {
                    other.gameObject.GetComponent<MeshRenderer>().material.color = SelectColor;
                    var rend = other.GetComponent<Renderer>();
                    SetOpaque(rend.material);

                    beforeList.Add(other.gameObject.transform.localPosition.x);
                    beforeList.Add(other.gameObject.transform.localPosition.y);
                    beforeList.Add(other.gameObject.transform.localPosition.z);
                    ObjectList.Add(other.gameObject);
                    NotSelectObjectList.Remove(other.gameObject.name);
                }
            }

            if(MoveMode == true)
            {
                if (!PickObjectList.Contains(other.gameObject))
                {
                    other.gameObject.GetComponent<MeshRenderer>().material.color = PickColor;
                    PickObjectPositionList.Add(other.gameObject.transform.localPosition.x);
                    PickObjectPositionList.Add(other.gameObject.transform.localPosition.y);
                    PickObjectPositionList.Add(other.gameObject.transform.localPosition.z);
                    PickObjectList.Add(other.gameObject);
                }
            }
        }
    }
    public void CalibrationButton()
    {
        afterObjectFloat32.enabled = true;
        beforeObjectFloat32.enabled = true;
        calibrationFloat32Publisher.enabled = true;
        calibrationFloat32Subscriber.enabled = true;
        foreach (var name in NotSelectObjectList)
        {
            for (int j = 0; j < objectGeneration.before_obj.Count; j++)
            {
                if (objectGeneration.before_obj[j].name == name)
                {
                    objectGeneration.before_obj[j].gameObject.GetComponent<MeshRenderer>().material.color = CalibrationColor;
                }
            }
        }
    }

    public void PickModeButton()
    {
        CalibrationMode = false;
        MoveMode = !MoveMode;
    }

    private void ChangeObjectColor()
    {
        foreach (GameObject obj in objectGeneration.GenerateObjects)
        {
            obj.gameObject.GetComponent<MeshRenderer>().material.color = DefalutColor;
        }
    }

    public float[] AfterObjectMessage ()
    {
        if(active)
        {
            foreach (var obj in ObjectList)
            {
                afterList.Add(obj.transform.localPosition.x);
                afterList.Add(obj.transform.localPosition.y);
                afterList.Add(obj.transform.localPosition.z);
            }
            active = false;
        }

        beforeData = new float[] { beforeList[0], beforeList[1], beforeList[2], beforeList[3], beforeList[4], beforeList[5], beforeList[6], beforeList[7], beforeList[8], beforeList[9], beforeList[10], beforeList[11] };
        afterData = new float[] { afterList[0], afterList[1], afterList[2], afterList[3], afterList[4], afterList[5], afterList[6], afterList[7], afterList[8], afterList[9], afterList[10], afterList[11] };

        return afterData;
    }

    public float[] BeforeObjectMessage()
    {
        beforeData = new float[] { beforeList[0], beforeList[1], beforeList[2], beforeList[3], beforeList[4], beforeList[5], beforeList[6], beforeList[7], beforeList[8], beforeList[9], beforeList[10], beforeList[11] };

        return beforeData;
    }

    public float[] PickObjectMessage()
    {
        beforeData = PickObjectPositionList.ToArray();

        return beforeData;
    }

    //public float[] IRM_SelectMessage()
    //{
    //    var selectCoords = new List<float>();
    //    if (Origin == null)
    //    {
    //        Debug.LogError("Origin が設定されていません！");
    //        return selectCoords.ToArray();
    //    }

    //    // YouBot 向けに追加したいオフセット
    //   /* Vector3 axisOffset = new Vector3(-0.123f, 0.056f, 0f);*/

    //    Vector3 axisOffset = new Vector3(0.0f, 0.0f, 0.0f);

    //    // １度だけ取得しておく Origin のワールド座標
    //    Vector3 originWorld = Origin.transform.position;

    //    foreach (GameObject obj in ObjectList)
    //    {
    //        // 1) ボトルのワールド座標
    //        Vector3 bottleWorld = obj.transform.position;

    //        // 2) ワールド差分で相対位置を計算
    //        Vector3 relative = bottleWorld - originWorld;

    //        // 3) オフセットを加算
    //        Vector3 adjusted = relative + axisOffset;

    //        // 4) YouBot 向けに軸反転・入れ替え
    //        float youbot_x = -adjusted.x;
    //        float youbot_y = -adjusted.z;
    //        float youbot_z = adjusted.y;

    //        // 5) 配列に追加
    //        selectCoords.Add(youbot_x);
    //        selectCoords.Add(youbot_y);
    //        selectCoords.Add(youbot_z);
    //    }

    //    return selectCoords.ToArray();
    //}
    private void SetOpaque(Material m)
   {
       m.SetFloat("_Surface", 0f);
       m.SetOverrideTag("RenderType", "Opaque");
       m.SetInt("_SrcBlend",  (int) BlendMode.One);
       m.SetInt("_DstBlend",  (int) BlendMode.Zero);
       m.SetInt("_ZWrite",    1);
       m.DisableKeyword("_ALPHATEST_ON");
       m.DisableKeyword("_ALPHABLEND_ON");
       m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
       m.renderQueue = (int) RenderQueue.Geometry;
   }

}
