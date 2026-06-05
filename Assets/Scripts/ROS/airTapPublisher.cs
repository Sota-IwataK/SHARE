using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosString = RosMessageTypes.Std.StringMsg;

public class airTapPublisher : MonoBehaviour
{
    [SerializeField] private string topicName = "/grasp_command";
    [SerializeField, Min(0.1f)] private float publishHz = 10f;
    [SerializeField] private string openCommand = "open";
    [SerializeField] private string closeCommand = "close";
    [SerializeField] private string stopCommand = "stop";

    public airTap_distance distance;
    public string outputdata;
    public bool registered;
    public int publishCount;

    private ROSConnection ros;
    private RosString grip;
    private string registeredTopic;
    private float nextPublishTime;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        Debug.Log("[airTapPublisher] OnEnable");
        InitializeMessage();
        EnsurePublisher();
        nextPublishTime = 0f;
    }

    private void Start()
    {
        InitializeMessage();
        EnsurePublisher();
    }

    private void FixedUpdate()
    {
        if (Time.time < nextPublishTime) return;

        float interval = 1f / Mathf.Max(0.1f, publishHz);
        nextPublishTime = Time.time + interval;

        PublishCurrentCommand();
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(topicName)) topicName = "/grasp_command";
        if (openCommand == null) openCommand = "open";
        if (closeCommand == null) closeCommand = "close";
        if (stopCommand == null) stopCommand = "stop";
        publishHz = Mathf.Max(0.1f, publishHz);
    }

    public void PublishStopCommand()
    {
        PublishCommand(stopCommand);
    }

    private void InitializeMessage()
    {
        grip = new RosString
        {
            data = outputdata
        };
    }

    private void EnsurePublisher()
    {
        if (!Application.isPlaying) return;

        ros ??= ROSConnection.GetOrCreateInstance();
        if (ros == null) return;

        if (registeredTopic != topicName)
        {
            ros.RegisterPublisher<RosString>(topicName);
            registeredTopic = topicName;
            registered = true;
            Debug.Log("[airTapPublisher] RegisterPublisher " + topicName);
        }
        else
        {
            registered = true;
        }
    }

    private void PublishCurrentCommand()
    {
        string command = distance == null ? stopCommand : (distance.airtap ? closeCommand : openCommand);
        PublishCommand(command);
    }

    private void PublishCommand(string command)
    {
        if (!Application.isPlaying) return;

        EnsurePublisher();
        if (ros == null) return;
        if (grip == null) InitializeMessage();

        outputdata = command;
        grip.data = outputdata;
        ros.Publish(topicName, grip);
        publishCount++;
    }
}
