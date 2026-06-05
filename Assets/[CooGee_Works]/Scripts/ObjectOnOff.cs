using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectOnOff : MonoBehaviour
{
    public GameObject showOnOff;

    private void Start()
    {
        showOnOff.SetActive(false);
    }

    public void ShowOnOffObject()
    {
        showOnOff.SetActive(!showOnOff.activeSelf);
    }
}
