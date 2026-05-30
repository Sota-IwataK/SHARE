using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UpdateYouBotTransform : MonoBehaviour
{
    [SerializeField] private RosSharp.RosBridgeClient.YouBotPosSubscriber youBotPosSubscriber;

    private Vector3 messagePosition;
    private Quaternion messageRotation;

    public bool isUpdate = false;

    [SerializeField] private GameObject YoubotObject;
    // Update is called once per frame
    void Update()
    {
        if (isUpdate)
        {
            messagePosition = youBotPosSubscriber.messagePosition;
            messageRotation = youBotPosSubscriber.messageRotation;

            YoubotObject.transform.localPosition = messagePosition;
            YoubotObject.transform.localRotation = messageRotation;
        }

    }
}
