using RosMessageTypes.Std;
using UnityEngine;

public class IRM_SerectObjectPublisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    [SerializeField] private float[] pendingCoords = new float[0];
    [SerializeField] private float[] lastPublishedData;
    public GameObject Aligin;

    public void SetCoords(float[] coords)
    {
        pendingCoords = coords;
    }

    public void PublishSelectData()
    {
        if (Aligin != null) Aligin.GetComponent<AlignToTarget>().enabled = true;
        Publish(new Float32MultiArrayMsg { data = pendingCoords });
        lastPublishedData = pendingCoords;
    }
}
