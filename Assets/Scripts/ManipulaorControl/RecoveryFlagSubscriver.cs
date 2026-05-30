using UnityEngine;
using RosSharp.RosBridgeClient;
using std_msgs = RosSharp.RosBridgeClient.MessageTypes.Std;

public class RecoveryFlagSubscriver : UnitySubscriber<std_msgs.Bool>
{
  
    [SerializeField] private GameObject targetObject;

    private bool flagState;
    private bool isMessageReceived = false;

    protected override void Start()
    {
        base.Start(); // ROSサブスクライブ開始
    }

    private void Update()
    {
        if (isMessageReceived)
        {
            targetObject.SetActive(flagState); // true → アクティブ, false → 非アクティブ
            isMessageReceived = false;
        }
    }

    protected override void ReceiveMessage(std_msgs.Bool message)
    {
        flagState = message.data;
        isMessageReceived = true;
    }
}
