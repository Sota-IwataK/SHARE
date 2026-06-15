using System;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

public static class Ros2MessageRegistryCompatibility
{
    private static bool loggedRegistration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureRegisteredBeforeSceneLoad()
    {
        EnsureRegistered();
    }

    public static void EnsureRegistered()
    {
#if ROS2
        RegisterEndpointMessageName("builtin_interfaces/Time", TimeMsg.Deserialize);
        RegisterEndpointMessageName("builtin_interfaces/Duration", DurationMsg.Deserialize);

        RegisterEndpointMessageName("std_msgs/Header", HeaderMsg.Deserialize);
        RegisterEndpointMessageName("std_msgs/String", StringMsg.Deserialize);
        RegisterEndpointMessageName("std_msgs/Empty", EmptyMsg.Deserialize);
        RegisterEndpointMessageName("std_msgs/Float32MultiArray", Float32MultiArrayMsg.Deserialize);
        RegisterEndpointMessageName("std_msgs/MultiArrayLayout", MultiArrayLayoutMsg.Deserialize);
        RegisterEndpointMessageName("std_msgs/MultiArrayDimension", MultiArrayDimensionMsg.Deserialize);

        RegisterEndpointMessageName("geometry_msgs/Point", PointMsg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/Quaternion", QuaternionMsg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/Vector3", Vector3Msg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/Vector3Stamped", Vector3StampedMsg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/Twist", TwistMsg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/Pose", PoseMsg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/PoseStamped", PoseStampedMsg.Deserialize);
        RegisterEndpointMessageName("geometry_msgs/PoseArray", PoseArrayMsg.Deserialize);

        if (!loggedRegistration)
        {
            loggedRegistration = true;
            Debug.Log(
                "[Ros2MessageRegistryCompatibility] ROS-TCP endpoint message names registered. " +
                "PoseArray=" + MessageRegistry.GetRosMessageName<PoseArrayMsg>() +
                ", PoseStamped=" + MessageRegistry.GetRosMessageName<PoseStampedMsg>() +
                ", String=" + MessageRegistry.GetRosMessageName<StringMsg>());
        }
#endif
    }

    private static void RegisterEndpointMessageName<T>(
        string messageName,
        Func<MessageDeserializer, T> deserialize) where T : Message
    {
        bool hasDeserializer = MessageRegistry.GetDeserializeFunction(messageName) != null;
        bool isPublishName = MessageRegistry.GetRosMessageName<T>() == messageName;
        if (!hasDeserializer || !isPublishName)
        {
            MessageRegistry.Register(messageName, deserialize);
        }
    }
}
