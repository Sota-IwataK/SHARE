using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StopButton : MonoBehaviour
{
    // Start is called before the first frame update
    [SerializeField] private GameObject rosConnector;
    [SerializeField] private PointCloudRenderer pointCloudRender;
    
    public void stopButton()
    {
        rosConnector.GetComponent<handPosePublisher>().enabled = false;
        rosConnector.GetComponent<airTapPublisher>().enabled = false;
        rosConnector.GetComponent<Float32MultiSubscriber>().enabled = false;
        rosConnector.GetComponent<CalibrationFloat32Publisher>().enabled = false;
        rosConnector.GetComponent<PointCloudSubscriber>().enabled = false;
 
        rosConnector.GetComponent<BottleStatePublisher>().enabled = true;

    }


}
