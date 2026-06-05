using RosMessageTypes.Std;
using UnityEngine;

public class Phase_name_Subscriber : RosTcpSubscriber<StringMsg>
{
    public string phase_name;

    protected override void ReceiveMessage(StringMsg message)
    {
        phase_name = message.data;
    }
}
