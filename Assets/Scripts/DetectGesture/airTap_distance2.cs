
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using UnityEngine.XR;

public class airTap_distance2 : MonoBehaviour
{
    public GameObject sphereMarker;
    public GameObject maincam;

    GameObject thumbObject;
    GameObject indexObject;

    Vector3 thumbPosition;
    Vector3 indexPosition;
    float distance;
    public bool airtap = false;
    string outputdata;
    Color defaultcolor;

    public Vector3 middlePoint;

    void Start()
    {
        thumbObject = Instantiate(sphereMarker, this.transform);
        indexObject = Instantiate(sphereMarker, this.transform);
        defaultcolor = indexObject.GetComponent<Renderer>().material.color;
    }

    void Update()
    {
        thumbObject.GetComponent<Renderer>().enabled = false;
        indexObject.GetComponent<Renderer>().enabled = false;

        thumbPosition = ThumbPosition();
        indexPosition = IndexPosition();
        distance = PositionDistance(thumbPosition, indexPosition);

        middlePoint = (thumbPosition + indexPosition) / 2f;

        airtap = airTap(distance);
        outputdata = bool2string();
    }

    public Vector3 ThumbPosition()
    {
        var aggregator = XRSubsystemHelpers.HandsAggregator;

        if (aggregator != null && aggregator.TryGetJoint(TrackedHandJoint.ThumbTip, XRNode.RightHand, out HandJointPose pose))
        {
            thumbObject.GetComponent<Renderer>().enabled = true;
            thumbObject.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            thumbPosition = pose.Position;
        }
        return thumbPosition;
    }

    public Vector3 IndexPosition()
    {
        var aggregator = XRSubsystemHelpers.HandsAggregator;

        if (aggregator != null && aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.RightHand, out HandJointPose pose))
        {
            indexObject.GetComponent<Renderer>().enabled = true;
            indexObject.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            indexPosition = pose.Position;
        }
        return indexPosition;
    }

    public float PositionDistance(Vector3 thumb, Vector3 index)
    {
        return Vector3.Distance(thumb, index);
    }

    public bool airTap(float distance)
    {
        if (distance <= 0.04f)
        {
            airtap = true;
            thumbObject.GetComponent<Renderer>().material.color = Color.red;
            indexObject.GetComponent<Renderer>().material.color = Color.red;
            // Debug.Log("Pinch");
        }
        else
        {
            airtap = false;
            thumbObject.GetComponent<Renderer>().material.color = defaultcolor;
            indexObject.GetComponent<Renderer>().material.color = defaultcolor;
            // Debug.Log("Pinchfalse");

        }
        return airtap;
    }

    public string bool2string()
    {
        return airtap ? "close" : "open";
    }
}
