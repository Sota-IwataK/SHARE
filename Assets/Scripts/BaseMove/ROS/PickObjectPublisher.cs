using RosMessageTypes.Std;
using UnityEngine;

public class PickObjectPublisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    private const string DefaultTopic = "/Pick_Object_Command";

    [SerializeField] private SelectObject select;

    private void Awake()
    {
        EnsureDefaultTopic();
        EnsurePublisher();
    }

    protected override void Start()
    {
        EnsureDefaultTopic();
        base.Start();
    }

    private void FixedUpdate()
    {
        if (select == null) return;

        Publish(new Float32MultiArrayMsg { data = select.PickObjectMessage() });
    }

    private void EnsureDefaultTopic()
    {
        if (string.IsNullOrWhiteSpace(Topic))
        {
            Topic = DefaultTopic;
        }
    }
}
