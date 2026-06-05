using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PublisherOn_Off : MonoBehaviour
{
    // Start is called before the first frame update
     [SerializeField] private GameObject rosConnector;
    public void ONPub()
    {
        rosConnector.GetComponent<BottleStatePublisher>().enabled = true;
    }

    // Update is called once per frame
    public void OFFPub()
    {
        rosConnector.GetComponent<BottleStatePublisher>().enabled = false;
    }
}
