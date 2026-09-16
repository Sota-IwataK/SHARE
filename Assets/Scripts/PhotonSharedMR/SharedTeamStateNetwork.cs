using System.Collections.Generic;
using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[DisallowMultipleComponent]
public sealed class SharedTeamStateNetwork :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    public const float DefaultPoseRateHz = 8f;
    private static readonly HashSet<SharedTeamStateNetwork> Instances = new HashSet<SharedTeamStateNetwork>();

    [Header("P1-02 Shared Team State")]
    [SerializeField, Range(5f, 10f)] private float poseRateHz = DefaultPoseRateHz;
    [SerializeField] private int localRobotId = -1;
    [SerializeField] private Transform localRobotPoseSource;
    [SerializeField] private bool sampleLocalRobotPose;
    [SerializeField] private bool enableEventLogs = true;

    private NetworkUserAvatar avatar;
    private float nextRobotPoseSampleTime;

#if FUSION_WEAVER && FUSION2
    [Networked] public int RobotIdValue { get; private set; }
    [Networked] public Vector3 RobotPosition { get; private set; }
    [Networked] public Quaternion RobotRotation { get; private set; }
    [Networked] public int GripperStateValue { get; private set; }
    [Networked] public NetworkBool RobotConnectedValue { get; private set; }
    [Networked] public int RobotTimestampTypeValue { get; private set; }
    [Networked] public long RobotSourceTimestampValue { get; private set; }
    [Networked] public long RobotSharedTimestampValue { get; private set; }
    [Networked] public int RobotSequenceValue { get; private set; }
#endif

    public float PoseRateHz => poseRateHz;

#if FUSION_WEAVER && FUSION2
    public override void Spawned()
    {
        avatar = GetComponent<NetworkUserAvatar>();
        Instances.Add(this);
        if (HasStateAuthority)
        {
            RobotIdValue = localRobotId;
            RobotConnectedValue = false;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Instances.Remove(this);
    }

    private void OnDestroy()
    {
        Instances.Remove(this);
    }

    public override void FixedUpdateNetwork()
    {
        if (sampleLocalRobotPose && HasStateAuthority && localRobotPoseSource != null
            && Runner.SimulationTime >= nextRobotPoseSampleTime)
        {
            nextRobotPoseSampleTime = (float)Runner.SimulationTime + 1f / Mathf.Clamp(poseRateHz, 5f, 10f);
            RobotPosition = localRobotPoseSource.position;
            RobotRotation = localRobotPoseSource.rotation;
        }

    }

    public bool TryReportRobotConnection(int robotId, bool connected)
    {
        if (!HasStateAuthority || robotId < 0) return false;
        RobotIdValue = robotId;
        RobotConnectedValue = connected;
        LogEvent("ROBOT_CONNECTED", robotId, connected ? 1 : 0);
        return true;
    }

    public bool TryReportRobotPose(int robotId, Vector3 position, Quaternion rotation,
        SharedTimestampSource sourceTimestampType, long sourceTimestamp)
    {
        if (!HasStateAuthority || robotId < 0 || sourceTimestampType == SharedTimestampSource.Unavailable
            || !IsFinite(position) || !IsValid(rotation)) return false;
        RobotIdValue = robotId;
        RobotPosition = position;
        RobotRotation = rotation;
        RobotTimestampTypeValue = (int)sourceTimestampType;
        RobotSourceTimestampValue = sourceTimestamp;
        RobotSharedTimestampValue = CurrentSharedTimestamp();
        RobotSequenceValue = RobotSequenceValue == int.MaxValue ? 1 : RobotSequenceValue + 1;
        return true;
    }

    public bool TryReportGripperState(int robotId, int gripperState)
    {
        if (!HasStateAuthority || robotId < 0) return false;
        RobotIdValue = robotId;
        GripperStateValue = gripperState;
        LogEvent("GRIPPER", robotId, gripperState);
        return true;
    }

    private long CurrentSharedTimestamp()
    {
        return Runner == null ? 0L : (long)(Runner.SimulationTime * 1000.0);
    }

    private void LogEvent(string eventName, int value1, int value2)
    {
        if (enableEventLogs)
            Debug.Log("[SharedTeamState] " + eventName + " value1=" + value1 + " value2=" + value2);
    }
#else
    private void Awake() { avatar = GetComponent<NetworkUserAvatar>(); Instances.Add(this); }
    private void OnDestroy() { Instances.Remove(this); }
#endif

    internal static void CopyHumanStates(List<HumanState> destination)
    {
        foreach (SharedTeamStateNetwork item in Instances)
        {
            if (item == null || item.avatar == null || !item.avatar.IsNetworkStateReady) continue;
            destination.Add(new HumanState
            {
                user_id = (int)item.avatar.ParticipantId,
                head_pose = new SharedPose(item.avatar.HeadWorldPosition, ReadHeadRotation(item.avatar)),
                left_hand_pose = new SharedPose(ReadLeftHandPosition(item.avatar), ReadLeftHandRotation(item.avatar)),
                right_hand_pose = new SharedPose(ReadRightHandPosition(item.avatar), ReadRightHandRotation(item.avatar)),
                connected = true
            });
        }
    }

    internal static void CopyRobotStates(List<RobotState> destination)
    {
#if FUSION_WEAVER && FUSION2
        foreach (SharedTeamStateNetwork item in Instances)
        {
            if (item == null || item.RobotIdValue < 0) continue;
            destination.Add(new RobotState
            {
                robot_id = item.RobotIdValue,
                pose = new SharedPose(item.RobotPosition, item.RobotRotation),
                gripper_state = item.GripperStateValue,
                connected = item.RobotConnectedValue,
                source_timestamp_type = (SharedTimestampSource)item.RobotTimestampTypeValue,
                source_timestamp = item.RobotSourceTimestampValue,
                shared_timestamp = item.RobotSharedTimestampValue,
                sequence = item.RobotSequenceValue
            });
        }
#endif
    }

    internal static void CopyObjectStates(List<ObjectState> destination)
    {
        NetworkedSharedSceneObject[] sharedObjects = UnityEngine.Object.FindObjectsByType<NetworkedSharedSceneObject>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject item = sharedObjects[i];
            if (item == null || !item.IsPhotonSharedNetworkBottle) continue;
            destination.Add(new ObjectState
            {
                object_id = item.SharedDetectedBottleTrackId >= 0 ? item.SharedDetectedBottleTrackId : item.GetInstanceID(),
                pose = new SharedPose(item.transform.position, item.transform.rotation),
                valid = item.gameObject.activeInHierarchy,
                source_timestamp_type = item.SharedSourceTimestampType,
                source_timestamp = item.SharedSourceTimestamp,
                shared_timestamp = item.SharedTelemetryTimestamp,
                sequence = item.SharedTelemetrySequence
            });
        }
    }

    private static Quaternion ReadHeadRotation(NetworkUserAvatar item)
    {
#if FUSION_WEAVER && FUSION2
        return item.HeadRotation;
#else
        return item.transform.rotation;
#endif
    }

    private static Vector3 ReadLeftHandPosition(NetworkUserAvatar item)
    {
#if FUSION_WEAVER && FUSION2
        return item.LeftHandPosition;
#else
        return item.transform.position;
#endif
    }

    private static Quaternion ReadLeftHandRotation(NetworkUserAvatar item)
    {
#if FUSION_WEAVER && FUSION2
        return item.LeftHandRotation;
#else
        return item.transform.rotation;
#endif
    }

    private static Vector3 ReadRightHandPosition(NetworkUserAvatar item)
    {
#if FUSION_WEAVER && FUSION2
        return item.RightHandPosition;
#else
        return item.transform.position;
#endif
    }

    private static Quaternion ReadRightHandRotation(NetworkUserAvatar item)
    {
#if FUSION_WEAVER && FUSION2
        return item.RightHandRotation;
#else
        return item.transform.rotation;
#endif
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool IsValid(Quaternion value) => IsFinite(new Vector3(value.x, value.y, value.z))
        && float.IsFinite(value.w)
        && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.0001f;
}
