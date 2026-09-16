using System;
using System.Collections.Generic;
using UnityEngine;

public enum TaskPhase
{
    UNKNOWN = 0,
    INDEPENDENT = 1,
    PREPARING = 2,
    READY = 3,
    TRANSFER = 4,
    RELEASED = 5
}

public enum SharedControlOwnerType
{
    None = 0,
    Human = 1,
    Robot = 2
}

public enum SharedTimestampSource
{
    Unavailable = 0,
    RosHeader = 1,
    ReceiveTime = 2
}

[Serializable]
public struct SharedPose
{
    public Vector3 position;
    public Quaternion rotation;

    public SharedPose(Vector3 position, Quaternion rotation)
    {
        this.position = position;
        this.rotation = rotation;
    }
}

[Serializable]
public struct HumanState
{
    public int user_id;
    public SharedPose head_pose;
    public SharedPose left_hand_pose;
    public SharedPose right_hand_pose;
    public bool connected;
}

[Serializable]
public struct RobotState
{
    public int robot_id;
    public SharedPose pose;
    public int gripper_state;
    public bool connected;
    public SharedTimestampSource source_timestamp_type;
    public long source_timestamp;
    public long shared_timestamp;
    public int sequence;
}

[Serializable]
public struct ObjectState
{
    public int object_id;
    public SharedPose pose;
    public bool valid;
    public SharedTimestampSource source_timestamp_type;
    public long source_timestamp;
    public long shared_timestamp;
    public int sequence;
}

[Serializable]
public struct SharedControlState
{
    public int target_id;
    public SharedControlOwnerType owner_type;
    public int owner_id;
    public TaskPhase task_phase;
    public int sequence;
    public long shared_timestamp;
}

[Serializable]
public struct LocalHoldState
{
    public SharedMRParticipantId participant_id;
    public bool valid;
    public bool holds_object;
    public bool grasp_candidate_valid;
    public long grasp_candidate_object_id;
    public bool held_object_valid;
    public long held_object_id;
    public bool source_timestamp_valid;
    public ulong source_timestamp;
    public ulong sequence;
    public string session_id;
}

[Serializable]
public struct PairSemanticState
{
    public SharedMRParticipantId participant_id;
    public bool valid;
    public bool holds_object;
    public bool grasp_candidate_valid;
    public long grasp_candidate_object_id;
    public bool held_object_valid;
    public long held_object_id;
    public ulong source_sequence;
    public bool source_timestamp_valid;
    public ulong source_timestamp;
    public long receive_time;
    public int receive_tick;
    public bool stale;
    public string session_id;
}

/// <summary>Pure validation/mapping logic. Source clocks are retained but never compared across pairs.</summary>
public sealed class PairSemanticStateReducer
{
    public const int RetiredSessionCapacity = 4;
    private readonly SharedMRParticipantId expectedParticipant;
    private readonly string[] retiredSessions = new string[RetiredSessionCapacity];
    private string currentSession;
    private int retiredCount;
    private int retiredWriteIndex;
    private bool hasSequence;
    private ulong lastSequence;

    public PairSemanticStateReducer(SharedMRParticipantId expectedParticipant)
    {
        this.expectedParticipant = expectedParticipant;
    }

    public bool TryApply(LocalHoldState source, long receiveTime, int receiveTick, out PairSemanticState state)
    {
        state = default;
        if ((expectedParticipant != SharedMRParticipantId.User1 && expectedParticipant != SharedMRParticipantId.User2)
            || source.participant_id != expectedParticipant || string.IsNullOrEmpty(source.session_id))
            return false;

        bool sameSession = string.Equals(source.session_id, currentSession, StringComparison.Ordinal);
        if (currentSession != null && !sameSession)
        {
            if (IsRetired(source.session_id) || !CurrentSessionIsStale(receiveTime)) return false;
            Retire(currentSession);
            currentSession = source.session_id;
            hasSequence = false;
        }
        else if (currentSession == null)
        {
            currentSession = source.session_id;
        }
        if (hasSequence && source.sequence <= lastSequence) return false;

        hasSequence = true;
        lastSequence = source.sequence;
        state = new PairSemanticState
        {
            participant_id = expectedParticipant,
            valid = source.valid,
            holds_object = source.valid && source.holds_object,
            grasp_candidate_valid = source.valid && source.grasp_candidate_valid,
            grasp_candidate_object_id = source.valid && source.grasp_candidate_valid ? source.grasp_candidate_object_id : -1,
            held_object_valid = source.valid && source.holds_object && source.held_object_valid,
            held_object_id = source.valid && source.holds_object && source.held_object_valid ? source.held_object_id : -1,
            source_sequence = source.sequence,
            source_timestamp_valid = source.source_timestamp_valid,
            source_timestamp = source.source_timestamp,
            receive_time = receiveTime,
            receive_tick = receiveTick,
            stale = false
            ,session_id = source.session_id
        };
        lastReceiveTime = receiveTime;
        return true;
    }

    private long lastReceiveTime;
    private bool CurrentSessionIsStale(long now) => hasSequence && now - lastReceiveTime > staleAfter;
    private long staleAfter = 1000;
    public void SetStaleAfter(long milliseconds) { staleAfter = milliseconds < 0 ? 0 : milliseconds; }
    private bool IsRetired(string session)
    {
        for (int i = 0; i < retiredCount; i++) if (string.Equals(retiredSessions[i], session, StringComparison.Ordinal)) return true;
        return false;
    }
    private void Retire(string session)
    {
        retiredSessions[retiredWriteIndex] = session;
        retiredWriteIndex = (retiredWriteIndex + 1) % RetiredSessionCapacity;
        if (retiredCount < RetiredSessionCapacity) retiredCount++;
    }

    public static PairSemanticState ApplyFreshness(PairSemanticState state, long now, long staleAfter)
    {
        state.stale = staleAfter >= 0 && now - state.receive_time > staleAfter;
        if (state.stale)
        {
            state.valid = false;
            state.holds_object = false;
            state.held_object_id = -1;
        }
        return state;
    }
}

/// <summary>
/// Allocation-free, caller-buffer based read view over the existing Fusion state.
/// It deliberately contains no ROS publishing or robot command path.
/// </summary>
public static class SharedTeamState
{
    public static void ReadHumans(List<HumanState> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        SharedTeamStateNetwork.CopyHumanStates(destination);
    }

    public static void ReadRobots(List<RobotState> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        SharedTeamStateNetwork.CopyRobotStates(destination);
    }

    public static void ReadObjects(List<ObjectState> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        SharedTeamStateNetwork.CopyObjectStates(destination);
    }

    public static bool TryReadControl(out SharedControlState state)
    {
        return SharedTeamControlStateNetwork.TryRead(out state);
    }

    public static bool TryReadPair(SharedMRParticipantId participantId, out PairSemanticState state)
    {
        return SharedPairSemanticStateNetwork.TryRead(participantId, out state);
    }
}
