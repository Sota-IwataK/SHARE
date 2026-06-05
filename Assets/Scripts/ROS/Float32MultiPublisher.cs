using RosMessageTypes.Std;

public class Float32MultiPublisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    private readonly float[] data = { 8f, -2f, 4f };

    private void FixedUpdate()
    {
        Publish(new Float32MultiArrayMsg { data = data });
    }
}
