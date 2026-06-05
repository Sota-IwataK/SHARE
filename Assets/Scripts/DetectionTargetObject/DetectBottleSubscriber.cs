using RosMessageTypes.Std;

public class DetectBottleSubscriber : RosTcpSubscriber<Int32Msg>
{
    public int bottle_id = -1;

    protected override void ReceiveMessage(Int32Msg message)
    {
        bottle_id = message.data;
    }
}
