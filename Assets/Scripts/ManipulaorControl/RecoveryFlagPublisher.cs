using RosMessageTypes.Std;
using UnityEngine;

public class RecoveryFlagPublisher : RosTcpPublisher<BoolMsg>
{
    private BoolMsg message;
    [SerializeField] private bool currentState;

    protected override void Start()
    {
        base.Start();
        message = new BoolMsg { data = currentState };
    }

    private void FixedUpdate()
    {
        Publish(message);
    }

    public void PublishRecoveryFlag(bool value)
    {
        currentState = value;
        message ??= new BoolMsg();
        message.data = value;
        Publish(message);
    }

    public void SetRecoveryFlagFalse()
    {
        PublishRecoveryFlag(false);
        Debug.Log("Recovery flag set to FALSE");
    }

    public void SetRecoveryFlagTrue()
    {
        PublishRecoveryFlag(true);
        Debug.Log("Recovery flag set to TRUE");
    }
}
