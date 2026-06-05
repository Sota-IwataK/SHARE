/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Microsoft;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit.Utilities;

public class airTap_distance : MonoBehaviour
{
    public GameObject sphereMarker;
    public GameObject maincam;

    GameObject thumbObject;
    GameObject indexObject;

    MixedRealityPose thumbpose;
    MixedRealityPose indexpose;
    Vector3 thumbPosition;
    Vector3 indexPosition;
    float distance;
    public bool airtap= false;
    string outputdata;
    Color defaultcolor;

    public Vector3 middlePoint;


    void Start()
    {

        thumbObject = Instantiate(sphereMarker,this.transform);
        indexObject = Instantiate(sphereMarker,this.transform);
        defaultcolor = indexObject.GetComponent<Renderer>().material.color;
    }

    void Update()
    {
        maincam.GetComponent<Renderer>();        
        thumbObject.GetComponent<Renderer>().enabled = false;
        indexObject.GetComponent<Renderer>().enabled = false;

        thumbPosition = ThumbPosition();
        indexPosition = IndexPosition();
        distance = PositionDistance(thumbPosition, indexPosition);
        

        middlePoint = (thumbPosition + indexPosition);

        //Debug.Log("middlePoint" + middlePoint + "indexPosition" + indexPosition + "thumbPosition" + thumbPosition);
        airtap = airTap(distance);
        outputdata = bool2string();

    }

    public Vector3 ThumbPosition()
    {
        if (HandJointUtils.TryGetJointPose(TrackedHandJoint.ThumbTip, Handedness.Left, out MixedRealityPose thumbpose))
        {
            thumbObject.GetComponent<Renderer>().enabled = true;
            thumbObject.transform.position = thumbpose.Position;
            thumbObject.transform.rotation = thumbpose.Rotation;
            thumbPosition = thumbpose.Position;
        }

        return thumbPosition;
    }

    public Vector3 IndexPosition()
    {

        if (HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, Handedness.Left, out MixedRealityPose indexpose))
        {
            indexObject.GetComponent<Renderer>().enabled = true;
            indexObject.transform.position = indexpose.Position;
            indexObject.transform.rotation = indexpose.Rotation;
            indexPosition = indexpose.Position;
        }
        return indexPosition;
    }
    
    public float PositionDistance(Vector3 thumb,Vector3 index)
    {
        float distance = Vector3.Distance(thumb, index);
        return distance;
    }

    public bool airTap(float distance)
    {
        if (distance <= 0.04){
            airtap = true;
            thumbObject.GetComponent<Renderer>().material.color = Color.red;
            indexObject.GetComponent<Renderer>().material.color = Color.red;
        }
        else
        {
            airtap = false;
            thumbObject.GetComponent<Renderer>().material.color = defaultcolor;
            indexObject.GetComponent<Renderer>().material.color = defaultcolor;
        }
        return airtap;

    }
    public string bool2string()
    {
        string outputdata;
        if (airtap == true)
        {
            outputdata = "close";
        }
        else
        {
            outputdata = "open";
        }
        return outputdata;
    }

}
*/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using UnityEngine.XR;

public class airTap_distance : MonoBehaviour
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

        if (aggregator != null && aggregator.TryGetJoint(TrackedHandJoint.ThumbTip, XRNode.LeftHand, out HandJointPose pose))
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

        if (aggregator != null && aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.LeftHand, out HandJointPose pose))
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
        }
        else
        {
            airtap = false;
            thumbObject.GetComponent<Renderer>().material.color = defaultcolor;
            indexObject.GetComponent<Renderer>().material.color = defaultcolor;
        }
        return airtap;
    }

    public string bool2string()
    {
        return airtap ? "close" : "open";
    }
}
