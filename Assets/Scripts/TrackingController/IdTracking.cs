using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IdTracking : MonoBehaviour
{
    public string index;
    public Vector3 objectPosition;
    private void Start()
    {
        Debug.Log(index);
        objectPosition = new Vector3(0.147f, 0.4f, 0.2f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.tag == "bottle")
        {
            string name = other.name;

            string[] del = { "bottle" };

            string[] arr = name.Split(del, System.StringSplitOptions.None);
            index = arr[1];

            objectPosition = other.transform.position;
        }
        
        
    }
    private void OnTriggerExit(Collider other)
    {
        index = "";
        objectPosition = new Vector3(0.147f, 0.4f, 0.2f);
    }
}
