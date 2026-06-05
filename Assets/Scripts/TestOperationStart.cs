using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class TestOperationStart : MonoBehaviour
{
    [SerializeField] private OperationController _operationController;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("UserHand"))
        {
            Debug.Log("Start Operation");
            _operationController.operationButton();
        }

    }


}
