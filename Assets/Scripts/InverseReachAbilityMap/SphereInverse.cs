using MixedReality.Toolkit.SpatialManipulation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.UX;
using MixedReality.Toolkit.Input; // Å©ïKóvÇ…âûÇ∂Çƒí«â¡
using UnityEngine.XR.Interaction.Toolkit;
using RosSharp.RosBridgeClient;

public class ShpereInverse : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] private GameObject SIRM;



    public void SphereIRM_False()
    {
        SIRM.GetComponent<SphereMapSubscriber>().enabled = false;

        GameObject target = GameObject.Find("Origin/BoundingBox/InverseReachAbilityMap/InverseReachMapSpheres");
        target.SetActive(false);

        //Debug.Log("BBox false");
    }
    public void SphereIRM_True()
    {

        SIRM.GetComponent<SphereMapSubscriber>().enabled = true;

        GameObject target = GameObject.Find("Origin/BoundingBox/InverseReachAbilityMap/InverseReachMapSpheres");
        target.SetActive(true);


    }

}


