using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class OperationDebug : MonoBehaviour
{
    // Start is called before the first frame update
    public GameObject targetObject; // 対象のオブジェクト

    public void ActiveReverse()
    {
        if (targetObject != null)
        {
            // 現在の状態を反転
            targetObject.SetActive(!targetObject.activeSelf);
            
        }
    }
    public void ToggleActive()
    {
        if (targetObject != null)
        {
            targetObject.SetActive(true);
            UnityEngine.Debug.Log("ON");
        }
    }

    public void ToggleFalse()
    {
        if (targetObject != null)
        {
            targetObject.SetActive(false); //
            UnityEngine.Debug.Log("OFF");
        }
    }
}
