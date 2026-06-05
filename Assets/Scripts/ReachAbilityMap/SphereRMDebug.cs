using MixedReality.Toolkit.SpatialManipulation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.UX;
using MixedReality.Toolkit.Input; // Å©ïKóvÇ…âûÇ∂Çƒí«â¡
using UnityEngine.XR.Interaction.Toolkit;

public class ShpereRMDebug : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] private GameObject SRM;



    public void SphereRM_False()
    {
        SRM.GetComponent<PointCloudWithSpheres>().enabled = false;

        GameObject target = GameObject.Find("Origin/BoundingBox/ReachAbilityMap/PointCloudSpheres");
        target.SetActive(false);

        //Debug.Log("BBox false");
    }
    public void SphereRM_True()
    {
    
        SRM.GetComponent<PointCloudWithSpheres>().enabled = true;

        GameObject target = GameObject.Find("Origin/BoundingBox/ReachAbilityMap/PointCloudSpheres");
        target.SetActive(true);


    }

}


