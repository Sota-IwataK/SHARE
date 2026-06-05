using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MixedReality.Toolkit.UX;
using MixedReality.Toolkit.Input; // ←追加（必要なら）
using UnityEngine.XR.Interaction.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;


public class OperationController : MonoBehaviour
{
    [SerializeField] private GameObject rosConnector;
    //s[SerializeField] private GameObject mark;
    [SerializeField] private ObjectGenerationTest objectGenerationTest;
    //[SerializeField] private TextMeshPro OperationUI;
    [SerializeField] private GameObject BBox;
    [SerializeField] public GameObject Phasename_UI;

    
    private bool prevBoundsControlState = true;
    private bool prevBoxColliderState = true;
    private bool prevManipulatorState = true;
    void Start()
    {
        if (BBox.GetComponent<BoundsControl>() != null)
            Debug.LogWarning("BoundsControl が見つかりました！");
        else
            Debug.LogWarning("BoundsControl は見つかりませんでした");

        if (BBox.GetComponent<ObjectManipulator>() != null)
            Debug.LogWarning("ObjectManipulator が見つかりました！");
        else
            Debug.LogWarning("ObjectManipulator は見つかりませんでした");

        if (BBox.GetComponent<BoxCollider>() != null)
            Debug.LogWarning("BoxCollider が見つかりました！");
        else
            Debug.LogWarning("BoxCollider は見つかりませんでした");
    }


    public void operationButton()
    {
        //rosConnector.GetComponent<BaseMovePublisher>().enabled = false;
        //mark.SetActive(true);
        //OperationUI.text = "Start Operation";
        Invoke("operation", 3f);
        objectGenerationTest.enabled = false;
    }

    public void operation()
    {
        Debug.Log("=== OPERATION START ===");

        rosConnector.GetComponent<handPosePublisher>().enabled = true;
        rosConnector.GetComponent<airTapPublisher>().enabled = true;
        rosConnector.GetComponent<Float32MultiSubscriber>().enabled = true;
        // rosConnector.GetComponent<ZRotationPublisher>().enabled = true;
        rosConnector.GetComponent<Phase_name_Subscriber>().enabled = true;
        rosConnector.GetComponent<BottleStatePublisher>().enabled = true;

        

        if (BBox != null)
        {
            var bounds = BBox.GetComponent<BoundsControl>();
            var manip = BBox.GetComponent<ObjectManipulator>();
            var col = BBox.GetComponent<BoxCollider>();

            if (bounds != null)
            {
                Debug.Log("Disabling BoundsControl...");
                bounds.enabled = false;
            }
            if (manip != null)
            {
                Debug.Log("Disabling ObjectManipulator...");
                manip.enabled = false;
            }
            if (col != null)
            {
                Debug.Log("Disabling BoxCollider...");
                col.enabled = false;
            }

            Debug.Log($"After disable: BoundsControl: {bounds.enabled}, Manipulator: {manip.enabled}, Collider: {col.enabled}");
        }
        else
        {
            Debug.LogWarning("BBox が null です！");
        }

        GameObject target = GameObject.Find("Origin/BoundingBox/BoundingBoxWithTraditionalHandles(Clone)");
        target.SetActive(false);

        Debug.Log("=== OPERATION END =f==");
        Phasename_UI.SetActive(true);

        // 最後に自分を非アクティブに
        this.gameObject.SetActive(false);



    }
}
