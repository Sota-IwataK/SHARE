using RosMessageTypes.Std;
using UnityEngine;

public class ZRotationPublisher : RosTcpPublisher<Float64Msg>
{
    [Header("Rotation Source")]
    public Transform targetObject;

    private readonly Float64Msg message = new Float64Msg();

    private void FixedUpdate()
    {
        if (targetObject == null) return;

        float zDeg = targetObject.rotation.eulerAngles.z;
        if (zDeg > 180f) zDeg -= 360f;
        message.data = zDeg * Mathf.Deg2Rad;
        Publish(message);
    }
}
