using RosMessageTypes.Std;
using UnityEngine;

public class RecoveryFlagSubscriver : RosTcpSubscriber<BoolMsg>
{
    [SerializeField] private GameObject targetObject;

    private bool flagState;
    private bool isMessageReceived = false;

    private void Update()
    {
        if (!isMessageReceived) return;

        if (targetObject != null) targetObject.SetActive(flagState);
        isMessageReceived = false;
    }

    protected override void ReceiveMessage(BoolMsg message)
    {
        flagState = message.data;
        isMessageReceived = true;
    }
}
