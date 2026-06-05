using RosMessageTypes.Std;
using UnityEngine;

public class CalibrationFloat32Publisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    [SerializeField] private CalibrationController calibrationController;

    private void FixedUpdate()
    {
        if (calibrationController == null) return;

        Publish(new Float32MultiArrayMsg { data = calibrationController.CalibrationMessage() });
    }
}
