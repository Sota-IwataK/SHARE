using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosTime = RosMessageTypes.BuiltinInterfaces.TimeMsg;

public abstract class RosTcpPublisher<T> : MonoBehaviour where T : Message
{
    public string Topic;
    [SerializeField] private int queueSize = 10;
    [SerializeField] private bool latch;

    private ROSConnection ros;
    private string registeredTopic;

    protected virtual void Start()
    {
        EnsurePublisher();
    }

    protected void Publish(T message)
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(Topic)) return;

        EnsurePublisher();
        ros.Publish(Topic, message);
    }

    protected void EnsurePublisher()
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(Topic)) return;

        ros ??= ROSConnection.GetOrCreateInstance();
        if (registeredTopic == Topic) return;

        ros.RegisterPublisher<T>(Topic, queueSize, latch);
        registeredTopic = Topic;
    }
}

public abstract class RosTcpSubscriber<T> : MonoBehaviour where T : Message
{
    public string Topic;
    public float TimeStep;
    public bool EnsureThreadSafety;

    private ROSConnection ros;
    private string subscribedTopic;

    protected virtual void Start()
    {
        EnsureSubscriber();
    }

    protected virtual void OnEnable()
    {
        if (Application.isPlaying) EnsureSubscriber();
    }

    protected void EnsureSubscriber()
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(Topic)) return;

        ros ??= ROSConnection.GetOrCreateInstance();
        if (subscribedTopic == Topic) return;

        ros.Subscribe<T>(Topic, ReceiveMessage);
        subscribedTopic = Topic;
    }

    protected abstract void ReceiveMessage(T message);
}

public static class RosTcpUtility
{
    public static RosTime GetRosTime()
    {
        double time = Time.realtimeSinceStartup;
        int wholeSeconds = Mathf.FloorToInt((float)time);
        uint nanoseconds = (uint)((time - wholeSeconds) * 1000000000.0);

#if ROS2
        int seconds = wholeSeconds;
#else
        uint seconds = (uint)wholeSeconds;
#endif

        return new RosTime
        {
            sec = seconds,
            nanosec = nanoseconds
        };
    }
}
