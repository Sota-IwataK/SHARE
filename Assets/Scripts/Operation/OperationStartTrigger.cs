using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
public class OperationStartTrigger : MonoBehaviour
{
    [SerializeField] private OperationController _operationController;

    private void OnTriggerEnter(Collider other)
    {
        if(other.tag == "UserHand")
        {
            Debug.Log("Start Operation");
            _operationController.operationButton();
        }
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
