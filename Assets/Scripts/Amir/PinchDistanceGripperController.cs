using System.Reflection;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;
using UnityEngine.Serialization;
using RosString = RosMessageTypes.Std.StringMsg;

[DisallowMultipleComponent]
public class PinchDistanceGripperController : MonoBehaviour
{
    private const float PublisherRegistrationSettleSeconds = 0.5f;

    private enum GripperState
    {
        Unknown,
        Open,
        Closed
    }

    [Header("Hand")]
    [SerializeField] private bool useRightHand = false;
    [FormerlySerializedAs("thumbTipTransform")]
    [SerializeField] private Transform thumbReferenceTransform;
    [FormerlySerializedAs("indexTipTransform")]
    [SerializeField] private Transform indexReferenceTransform;

    [Header("External Pinch Source")]
    [SerializeField] private MonoBehaviour externalPinchSource;
    [SerializeField] private bool resolveReferenceTransformsFromExternalSource = true;
    [SerializeField] private bool useExternalPinchState = false;
    [SerializeField] private string externalPinchBoolMemberName = "airtap";

    [Header("ROS")]
    [SerializeField] private string topicName = "/amir/gripper_cmd";
    [SerializeField] private int queueSize = 10;

    [Header("Control")]
    [SerializeField] private bool enableControl = true;
    [SerializeField, Min(0f)] private float openThreshold = 0.055f;
    [SerializeField, Min(0f)] private float closeThreshold = 0.030f;
    [SerializeField, Min(0f)] private float publishCooldownSec = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool logDistance = true;
    [SerializeField, Min(0.05f)] private float distanceLogIntervalSec = 0.5f;

    public bool EnableControl
    {
        get => enableControl;
        set => enableControl = value;
    }

    public float LastDistance { get; private set; }
    public string CurrentState => currentState.ToString();

    private const string OpenCommand = "open";
    private const string CloseCommand = "close";

    private static readonly string[] ThumbReferenceMemberNames =
    {
        "ThumbReferenceTransform",
        "thumbReferenceTransform",
        "ThumbTransform",
        "thumbTransform",
        "ThumbObject",
        "thumbObject"
    };

    private static readonly string[] IndexReferenceMemberNames =
    {
        "IndexReferenceTransform",
        "indexReferenceTransform",
        "IndexTransform",
        "indexTransform",
        "IndexObject",
        "indexObject"
    };

    private static readonly string[] PinchBoolMemberNames =
    {
        "IsPinching",
        "isPinching",
        "Pinching",
        "pinching",
        "airtap"
    };

    private ROSConnection ros;
    private string registeredTopic;
    private bool publisherRegistered;
    private float publisherReadyRealtime;
    private GripperState currentState = GripperState.Unknown;
    private float nextPublishAllowedTime;
    private float nextDistanceLogTime;
    private bool loggedMissingTransforms;
    private bool loggedInvalidTopic;
    private bool loggedPublishSkippedBeforeRegistration;

    private void Awake()
    {
        if (Application.isPlaying)
        {
            EnsurePublisher();
        }
    }

    private void OnEnable()
    {
        loggedMissingTransforms = false;
        loggedInvalidTopic = false;

        if (Application.isPlaying)
        {
            EnsurePublisher();
        }
    }

    private void Start()
    {
        EnsurePublisher();
    }

    private void Update()
    {
        if (!enableControl)
        {
            return;
        }

        bool hasDistance = TryGetPinchDistance(out float distance);
        if (hasDistance)
        {
            LastDistance = distance;
        }

        LogDebugStatus(hasDistance, distance);

        if (useExternalPinchState && TryGetExternalPinchState(out bool externalPinching))
        {
            if ((currentState == GripperState.Unknown || currentState == GripperState.Open) && externalPinching)
            {
                TryPublishState(GripperState.Closed, CloseCommand, hasDistance ? distance : float.NaN);
                return;
            }

            if ((currentState == GripperState.Unknown || currentState == GripperState.Closed) && !externalPinching)
            {
                TryPublishState(GripperState.Open, OpenCommand, hasDistance ? distance : float.NaN);
            }

            return;
        }

        if (!hasDistance)
        {
            return;
        }

        if ((currentState == GripperState.Unknown || currentState == GripperState.Closed) &&
            distance >= openThreshold)
        {
            TryPublishState(GripperState.Open, OpenCommand, distance);
            return;
        }

        if ((currentState == GripperState.Unknown || currentState == GripperState.Open) &&
            distance <= closeThreshold)
        {
            TryPublishState(GripperState.Closed, CloseCommand, distance);
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            topicName = "/amir/gripper_cmd";
        }

        queueSize = Mathf.Max(1, queueSize);
        openThreshold = Mathf.Max(0f, openThreshold);
        closeThreshold = Mathf.Max(0f, closeThreshold);
        publishCooldownSec = Mathf.Max(0f, publishCooldownSec);
        distanceLogIntervalSec = Mathf.Max(0.05f, distanceLogIntervalSec);
        if (externalPinchBoolMemberName == null)
        {
            externalPinchBoolMemberName = "airtap";
        }

        if (closeThreshold > openThreshold)
        {
            closeThreshold = openThreshold;
        }
    }

    private bool TryGetPinchDistance(out float distance)
    {
        distance = 0f;
        ResolveExternalReferenceTransforms();

        if (thumbReferenceTransform == null || indexReferenceTransform == null)
        {
            if (!loggedMissingTransforms)
            {
                loggedMissingTransforms = true;
                Debug.LogWarning(
                    "[PinchDistanceGripperController] Thumb Reference Transform or Index Reference Transform is not assigned for " +
                    GetSelectedHandName() + " hand; gripper command will not be published.");
            }

            return false;
        }

        loggedMissingTransforms = false;
        distance = Vector3.Distance(thumbReferenceTransform.position, indexReferenceTransform.position);
        return true;
    }

    private void EnsurePublisher()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(topicName))
        {
            if (!loggedInvalidTopic)
            {
                loggedInvalidTopic = true;
                Debug.LogWarning("[PinchDistanceGripperController] topicName is empty; gripper command will not be published.");
            }

            return;
        }

        ros ??= ROSConnection.GetOrCreateInstance();
        if (ros == null || (registeredTopic == topicName && publisherRegistered))
        {
            return;
        }

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        ros.RegisterPublisher<RosString>(topicName, queueSize, false);
        registeredTopic = topicName;
        publisherRegistered = true;
        publisherReadyRealtime = Time.realtimeSinceStartup + PublisherRegistrationSettleSeconds;
        loggedPublishSkippedBeforeRegistration = false;
        Debug.Log(
            "[PinchDistanceGripperController] RegisterPublisher " + topicName +
            " messageType=" + MessageRegistry.GetRosMessageName<RosString>() +
            " readyAfter=" + PublisherRegistrationSettleSeconds.ToString("F2") + "s");
    }

    private bool TryPublishState(GripperState nextState, string command, float distance)
    {
        if (!Application.isPlaying || Time.time < nextPublishAllowedTime)
        {
            return false;
        }

        EnsurePublisher();
        if (ros == null || string.IsNullOrWhiteSpace(topicName) || !CanPublish())
        {
            return false;
        }

        GripperState previousState = currentState;
        var message = new RosString
        {
            data = command
        };

        ros.Publish(topicName, message);
        currentState = nextState;
        nextPublishAllowedTime = Time.time + publishCooldownSec;

        Debug.Log(
            "[PinchDistanceGripperController] Published " + topicName + " " + command +
            " distance=" + FormatDistance(distance));

        Debug.Log(
            "[PinchDistanceGripperController] state change " + previousState + " -> " + nextState +
            " command=" + command +
            " distance=" + FormatDistance(distance) +
            " hand=" + GetSelectedHandName() +
            " topic=" + topicName);

        return true;
    }

    private bool CanPublish()
    {
        if (!publisherRegistered)
        {
            LogPublishSkippedBeforeRegistration("publisher is not registered");
            return false;
        }

        if (ros != null && ros.HasConnectionError)
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
        Debug.LogWarning("[PinchDistanceGripperController] Publish skipped for " + topicName + ": " + reason);
    }

    private void LogDebugStatus(bool hasDistance, float distance)
    {
        if (!logDistance || Time.time < nextDistanceLogTime)
        {
            return;
        }

        nextDistanceLogTime = Time.time + distanceLogIntervalSec;
        Debug.Log(
            "[PinchDistanceGripperController] thumbReference=" + GetTransformName(thumbReferenceTransform) +
            " indexReference=" + GetTransformName(indexReferenceTransform) +
            " distance=" + (hasDistance ? FormatDistance(distance) : "unavailable") +
            " state=" + currentState +
            " hand=" + GetSelectedHandName());
    }

    private void ResolveExternalReferenceTransforms()
    {
        if (!resolveReferenceTransformsFromExternalSource || externalPinchSource == null ||
            (thumbReferenceTransform != null && indexReferenceTransform != null))
        {
            return;
        }

        if (thumbReferenceTransform == null &&
            TryGetTransformFromExternalSource(externalPinchSource, ThumbReferenceMemberNames, out Transform thumbTransform))
        {
            thumbReferenceTransform = thumbTransform;
        }

        if (indexReferenceTransform == null &&
            TryGetTransformFromExternalSource(externalPinchSource, IndexReferenceMemberNames, out Transform indexTransform))
        {
            indexReferenceTransform = indexTransform;
        }
    }

    private bool TryGetExternalPinchState(out bool isPinching)
    {
        isPinching = false;
        if (externalPinchSource == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(externalPinchBoolMemberName) &&
            TryGetBoolMember(externalPinchSource, externalPinchBoolMemberName, out isPinching))
        {
            return true;
        }

        for (int i = 0; i < PinchBoolMemberNames.Length; i++)
        {
            if (TryGetBoolMember(externalPinchSource, PinchBoolMemberNames[i], out isPinching))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTransformFromExternalSource(
        MonoBehaviour source,
        string[] memberNames,
        out Transform result)
    {
        result = null;
        for (int i = 0; i < memberNames.Length; i++)
        {
            if (TryGetMemberValue(source, memberNames[i], out object value))
            {
                result = ExtractTransform(value);
                if (result != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetBoolMember(MonoBehaviour source, string memberName, out bool result)
    {
        result = false;
        if (!TryGetMemberValue(source, memberName, out object value) || !(value is bool boolValue))
        {
            return false;
        }

        result = boolValue;
        return true;
    }

    private static bool TryGetMemberValue(MonoBehaviour source, string memberName, out object value)
    {
        value = null;
        if (source == null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        System.Type sourceType = source.GetType();

        FieldInfo field = sourceType.GetField(memberName, flags);
        if (field != null)
        {
            value = field.GetValue(source);
            return value != null;
        }

        PropertyInfo property = sourceType.GetProperty(memberName, flags);
        if (property != null && property.GetIndexParameters().Length == 0)
        {
            value = property.GetValue(source);
            return value != null;
        }

        MethodInfo method = sourceType.GetMethod(memberName, flags);
        if (method != null && method.GetParameters().Length == 0)
        {
            value = method.Invoke(source, null);
            return value != null;
        }

        return false;
    }

    private static Transform ExtractTransform(object value)
    {
        if (value is Transform transform)
        {
            return transform;
        }

        if (value is GameObject gameObject)
        {
            return gameObject.transform;
        }

        if (value is Component component)
        {
            return component.transform;
        }

        return null;
    }

    private static string FormatDistance(float distance)
    {
        return float.IsNaN(distance) ? "n/a" : distance.ToString("F4") + "m";
    }

    private static string GetTransformName(Transform transform)
    {
        return transform != null ? transform.name : "<unassigned>";
    }

    private string GetSelectedHandName()
    {
        return useRightHand ? "Right" : "Left";
    }
}
