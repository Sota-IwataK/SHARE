using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

[DisallowMultipleComponent]
public class SharedRobotTargetPublisher : MonoBehaviour
{
    [SerializeField] private string legacyTopicName = RosTopicProvider.RobotTarget;
    [SerializeField, Min(0.1f)] private float heartbeatHz = 1f;
    [SerializeField] private int queueSize = 10;

    private ROSConnection ros;
    private string resolvedTopicName;
    private string registeredTopicName;
    private bool publisherRegistered;
    private float nextHeartbeatTime;
    private string lastPublishedValue;
    private bool lastConnectionHadError;

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsurePublisher();
        if (!CanPublish(out string value))
        {
            return;
        }

        bool valueChanged = !string.Equals(value, lastPublishedValue, System.StringComparison.Ordinal);
        bool connectionRecovered = lastConnectionHadError && ros != null && !ros.HasConnectionError;
        bool heartbeatDue = Time.unscaledTime >= nextHeartbeatTime;
        if (!valueChanged && !connectionRecovered && !heartbeatDue)
        {
            return;
        }

        PublishRobotTarget(value);
    }

    private void EnsurePublisher()
    {
        ros ??= ROSConnection.GetOrCreateInstance();
        if (ros == null)
        {
            return;
        }

        if (!RosTopicProvider.TryResolveTopic(
                RosInputTopicKey.RobotTarget,
                legacyTopicName,
                out resolvedTopicName,
                out _))
        {
            publisherRegistered = false;
            return;
        }

        if (publisherRegistered && string.Equals(registeredTopicName, resolvedTopicName, System.StringComparison.Ordinal))
        {
            return;
        }

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        ros.RegisterPublisher<StringMsg>(resolvedTopicName, queueSize, false);
        registeredTopicName = resolvedTopicName;
        publisherRegistered = true;
        Debug.Log("[SharedRobotTargetPublisher] RegisterPublisher " + resolvedTopicName
            + " messageType=" + MessageRegistry.GetRosMessageName<StringMsg>());
    }

    private bool CanPublish(out string value)
    {
        value = string.Empty;
        if (ros == null || !publisherRegistered || string.IsNullOrWhiteSpace(resolvedTopicName))
        {
            return false;
        }

        lastConnectionHadError = ros.HasConnectionError;
        if (ros.HasConnectionError)
        {
            return false;
        }

        if (!RosTopicProvider.CanPublish(RosInputTopicKey.RobotTarget, out _))
        {
            return false;
        }

        return RosTopicProvider.TryGetLocalRobotTargetValue(out value, out _);
    }

    private void PublishRobotTarget(string value)
    {
        StringMsg message = new StringMsg
        {
            data = value
        };
        ros.Publish(resolvedTopicName, message);
        lastPublishedValue = value;
        nextHeartbeatTime = Time.unscaledTime + (1f / Mathf.Max(0.1f, heartbeatHz));
        lastConnectionHadError = false;
        Debug.Log("[SharedRobotTargetPublisher] Published " + resolvedTopicName + " value=" + value);
    }
}
