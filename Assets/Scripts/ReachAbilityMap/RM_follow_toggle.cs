using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RM_follow_toggle : MonoBehaviour
{
    [SerializeField] private GameObject RMfull;
    
     public void ActiveFlase()
    {
 
        RMfull.GetComponent<FollowDeltaMovement>().enabled = false;


    }
    public void ActiveTrue()
    {
    
        RMfull.GetComponent<FollowDeltaMovement>().enabled = true;



    }

}
