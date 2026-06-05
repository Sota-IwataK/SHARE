using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Microsoft;
using MixedReality.Toolkit.UX;
using MixedReality.Toolkit.Input;

public class OperationStatusController : MonoBehaviour
{
    [SerializeField] private GameObject OperationUI;
    [SerializeField] private OperationStatusSubscriber _operationStatusSubscriber;
    private bool messageData;
    private bool status;
    private Transform maincamera;

    private MoveOriginController _moveOriginController;

    [SerializeField] private GameObject Origin;
   
    [SerializeField]private GameObject ObjectList;

    [SerializeField] private GameObject WayPointList;

    [SerializeField] private UpdateYouBotTransform updateYouBotTransform;
    private void Start()
    {
        _moveOriginController = Origin.GetComponent<MoveOriginController>();
    }
    private void Update()
    {
        if (messageData == false)
        {
            messageData = _operationStatusSubscriber.messageData;
            status = false;
        }

        if (messageData && !status)
        {
            maincamera = Camera.main.transform;
            
            OperationUI.SetActive(true);
            OperationUI.transform.position = new Vector3(maincamera.position.x, maincamera.position.y+0.1f, maincamera.position.z + 0.25f);
            status = true;
            //_moveOriginController.MoveOrigin();
            ObjectList.SetActive(true);
            WayPointList.SetActive(false);

            updateYouBotTransform.isUpdate = false;
        }
    }
}
