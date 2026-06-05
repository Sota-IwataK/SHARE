using RosMessageTypes.Std;

public class DetectBottle_score_subscriber : RosTcpSubscriber<Float32Msg>
{
    public float bottle_score;

    protected override void ReceiveMessage(Float32Msg message)
    {
        bottle_score = message.data;
    }
}
