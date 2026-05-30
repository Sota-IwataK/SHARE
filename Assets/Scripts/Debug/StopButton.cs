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
        rosConnector.GetComponent<RosSharp.RosBridgeClient.handPosePublisher>().enabled = false;
        rosConnector.GetComponent<RosSharp.RosBridgeClient.airTapPublisher>().enabled = false;
        rosConnector.GetComponent<Float32MultiSubscriber>().enabled = false;
        rosConnector.GetComponent<RosSharp.RosBridgeClient.CalibrationFloat32Publisher>().enabled = false;
        rosConnector.GetComponent<RosSharp.RosBridgeClient.PointCloudSubscriber>().enabled = false;
 
        rosConnector.GetComponent<BottleStatePublisher>().enabled = true;

    }


}
