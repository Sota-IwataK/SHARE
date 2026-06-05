/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
public class RightHandTracking : MonoBehaviour
{

    [SerializeField] private GameObject LightHand;

    void Start() 
    {

    }

    void Update()
    {
        if (HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, Handedness.Right, out MixedRealityPose indexpose))
        {
            LightHand.SetActive(true);
            LightHand.GetComponent<Renderer>().enabled = true;
            LightHand.transform.position = indexpose.Position;
            LightHand.transform.rotation = indexpose.Rotation;

        }

    }
}
*/
using UnityEngine;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using UnityEngine.XR;

public class RightHandTracking : MonoBehaviour
{
    [SerializeField] private GameObject LightHand;

    void Update()
    {
        // HandsAggregatorSubsystem 経由で右手がトラッキングされているか確認
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator != null && aggregator.TryGetJoint(TrackedHandJoint.IndexTip, XRNode.RightHand, out HandJointPose indexPose))
        {
            LightHand.SetActive(true);
            LightHand.GetComponent<Renderer>().enabled = true;
            LightHand.transform.SetPositionAndRotation(indexPose.Position, indexPose.Rotation);
        }
        else
        {
            LightHand.SetActive(false);
        }
    }
}

