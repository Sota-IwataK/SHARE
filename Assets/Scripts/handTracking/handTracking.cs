
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Microsoft;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit;
using UnityEngine.XR;
using MixedReality.Toolkit.Subsystems;



public class handTracking : MonoBehaviour
{

    public GameObject sphereMarker;

    public GameObject middleObject;


    public GameObject indexObject;

    Vector3 indexPosition;

    [SerializeField] private airTap_distance AirTap_Distance;

    [SerializeField] private GameObject origin;

    [SerializeField] private Transform handPositionFromOrigin;

    private float maxDistance = 0.7f;

    private float minDistance = 0.05f;

    private float minHight = 0.0f;
    void Start()
    {
        middleObject = Instantiate(sphereMarker, this.transform);
        middleObject.SetActive(false);

    }

    void Update()
    {
        middleObject.GetComponent<Renderer>().enabled = false;

        // HandsAggregatorSubsystem 
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator != null &&
            aggregator.TryGetJoint(TrackedHandJoint.MiddleProximal, XRNode.LeftHand, out HandJointPose middlepose))
        {
            middleObject.GetComponent<Renderer>().enabled = true;
            Quaternion rotation = GetHandRotationFromOrigin();
            //middleObject.transform.position = (middlepose.Position + AirTap_Distance.middlePoint) / 3;
            middleObject.transform.position = (middlepose.Position + AirTap_Distance.middlePoint) / 2;


            middleObject.transform.rotation = middlepose.Rotation;
            handPositionFromOrigin.position = middleObject.transform.position;
        }


    }


    public Vector3 GetHandPositionFromOrigin()
    {
        middleObject.SetActive(true);

        float x = handPositionFromOrigin.position.x - origin.transform.position.x;
        float y = handPositionFromOrigin.position.y - origin.transform.position.y;
        float z = handPositionFromOrigin.position.z - origin.transform.position.z;
        Vector3 direction = (handPositionFromOrigin.position - origin.transform.position).normalized;  // �_A����_B�ւ̒P�ʃx�N�g��
        float distance = Vector2.Distance(new Vector2(handPositionFromOrigin.position.x, handPositionFromOrigin.position.z), new Vector3(origin.transform.position.x, origin.transform.position.z)); // �_A�Ɠ_B�̌��݂̋���


        //float x = handPositionFromOrigin.localPosition.x - origin.transform.localPosition.x;
        //float y = handPositionFromOrigin.localPosition.y - origin.transform.localPosition.y;
        //float z = handPositionFromOrigin.localPosition.z - origin.transform.localPosition.z;
        //Vector3 direction = (handPositionFromOrigin.localPosition - origin.transform.localPosition).normalized;  // �_A����_B�ւ̒P�ʃx�N�g��
        //float distance = Vector2.Distance(new Vector2(handPositionFromOrigin.localPosition.x, handPositionFromOrigin.localPosition.z), new Vector3(origin.transform.localPosition.x, origin.transform.localPosition.z)); // �_A�Ɠ_B�̌��݂̋���


        Vector3 pose = new Vector3(x, y, z);

        if (y < minHight)
        {
            y = minHight;
        }

        if (distance >= maxDistance)
        {
            // �_B��_A����maxDistance�������ꂽ�ʒu�ɒ���
            pose = origin.transform.localPosition + direction * maxDistance;
        }
        else if (distance < minDistance)
        {
            // �_B��_A����minDistance�������ꂽ�ʒu�ɒ���
            pose = origin.transform.localPosition + direction * minDistance;
        }
        // print(pose);
        return pose;
    }

    public Quaternion GetHandRotationFromOrigin()
    {
        //middleObject.GetComponent<Renderer>().enabled = true;
        middleObject.SetActive(true);

        Vector3 direction = handPositionFromOrigin.position - origin.transform.position;
        Quaternion rotation = Quaternion.LookRotation(direction) * origin.transform.localRotation;
        //Debug.Log(rotation);
        return rotation;
    }
}
