using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosTime = RosMessageTypes.BuiltinInterfaces.TimeMsg;

public abstract class RosTcpPublisher<T> : MonoBehaviour where T : Message
{
    private const float PublisherRegistrationSettleSeconds = 0.5f;

    public string Topic;
    [SerializeField] private int queueSize = 10;
    [SerializeField] private bool latch;

    private ROSConnection ros;
    private string registeredTopic;
    private bool publisherRegistered;
    private float publisherReadyRealtime;
    private bool loggedPublishSkippedBeforeRegistration;

    protected virtual void Start()
    {
        EnsurePublisher();
    }

    protected void Publish(T message)
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(Topic)) return;

        EnsurePublisher();
        if (!CanPublish())
        {
            return;
        }

        ros.Publish(Topic, message);
    }

    protected void EnsurePublisher()
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(Topic)) return;

        ros ??= ROSConnection.GetOrCreateInstance();
        if (registeredTopic == Topic && publisherRegistered) return;

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        ros.RegisterPublisher<T>(Topic, queueSize, latch);
        registeredTopic = Topic;
        publisherRegistered = true;
        publisherReadyRealtime = Time.realtimeSinceStartup + PublisherRegistrationSettleSeconds;
        loggedPublishSkippedBeforeRegistration = false;
        Debug.Log(
            "[RosTcpPublisher] RegisterPublisher " + Topic +
            " messageType=" + MessageRegistry.GetRosMessageName<T>() +
            " readyAfter=" + PublisherRegistrationSettleSeconds.ToString("F2") + "s");
    }

    private bool CanPublish()
    {
        if (ros == null || !publisherRegistered)
        {
            LogPublishSkippedBeforeRegistration("publisher is not registered");
            return false;
        }

        if (ros.HasConnectionError)
        {
            LogPublishSkippedBeforeRegistration("ROS connection is not ready");
            return false;
        }

        if (Time.realtimeSinceStartup < publisherReadyRealtime)
        {
            LogPublishSkippedBeforeRegistration("waiting for ROS-TCP publisher registration to settle");
            return false;
        }

        loggedPublishSkippedBeforeRegistration = false;
        return true;
    }

    private void LogPublishSkippedBeforeRegistration(string reason)
    {
        if (loggedPublishSkippedBeforeRegistration)
        {
            return;
        }

        loggedPublishSkippedBeforeRegistration = true;
        Debug.LogWarning("[RosTcpPublisher] Publish skipped for " + Topic + ": " + reason);
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

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        string rosMessageName = MessageRegistry.GetRosMessageName<T>();
        ros.SubscribeByMessageName(Topic, rosMessageName, message =>
        {
            if (message is T typedMessage)
            {
                ReceiveMessage(typedMessage);
                return;
            }

            Debug.LogError(
                "[RosTcpSubscriber] Topic " + Topic + " expected " + typeof(T).Name +
                " but received " + message.GetType().Name);
        });
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
