using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveOriginController : MonoBehaviour
{
    [SerializeField] private YouBotPosSubscriber youBotPosSubscriber;
    // Start is called before the first frame update
    
    private Vector3 messagePosition;
    private Quaternion messageRotation;

    [SerializeField] private GameObject ObjectListOrigin;
    [SerializeField] private GameObject userOrigin;

    public void MoveOrigin()
    {
        messagePosition = youBotPosSubscriber.messagePosition;
        messageRotation = youBotPosSubscriber.messageRotation;

        Debug.Log(messagePosition);
        this.transform.rotation = Quaternion.identity;
       
        //ObjectListOrigin.transform.localPosition = new Vector3( - 0.147f, 0.152f + 0.091f, 0);
        userOrigin.transform.localPosition = new Vector3(messagePosition.x - 0.147f, messagePosition.y + 0.152f, - messagePosition.z);
        userOrigin.transform.localRotation = messageRotation;

        Debug.Log("MoveOrigin");

        //this.transform.position = Camera.main.transform.position;
    }
}
