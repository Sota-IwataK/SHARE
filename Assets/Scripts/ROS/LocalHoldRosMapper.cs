using System;
using RosMessageTypes.ShareSemanticInterfaces;

/// <summary>ROS-only full-fidelity view. Diagnostic arrays never enter Photon network state.</summary>
public sealed class LocalHoldRosSnapshot
{
    public LocalHoldState semantic;
    public bool close_intent_valid, close_intent;
    public bool intermediate_stop_valid, intermediate_stop;
    public bool bottle_near_ee_valid, bottle_near_ee;
    public bool load_detected_available, load_detected_valid, load_detected, main_condition;
    public bool bottle_ee_distance_valid;
    public double bottle_ee_distance;
    public bool gripper_actual_position_valid;
    public double gripper_actual_position;
    public bool last_transition_time_valid;
    public ulong last_transition_time_ns;
    public string invalid_reason, load_invalid_reason;
    public string[] input_age_names;
    public bool[] input_age_valid;
    public long[] input_ages_ns;
    public string[] stale_inputs;
}

public static class LocalHoldRosMapper
{
    public static bool TryMap(LocalHoldStateMsg message, out LocalHoldRosSnapshot result)
    {
        result = null;
        if (message == null || !TryMapParticipant(message.participant_id, out SharedMRParticipantId participant)
            || string.IsNullOrEmpty(message.session_id)) return false;
        result = new LocalHoldRosSnapshot
        {
            semantic = new LocalHoldState {
                participant_id = participant, session_id = message.session_id, sequence = message.sequence,
                source_timestamp_valid = message.source_timestamp_valid, source_timestamp = message.source_timestamp_ns,
                valid = message.valid, holds_object = message.holds_object,
                grasp_candidate_valid = message.grasp_candidate_valid,
                grasp_candidate_object_id = message.grasp_candidate_object_id,
                held_object_valid = message.held_object_valid, held_object_id = message.held_object_id },
            close_intent_valid = message.close_intent_valid, close_intent = message.close_intent,
            intermediate_stop_valid = message.intermediate_stop_valid, intermediate_stop = message.intermediate_stop,
            bottle_near_ee_valid = message.bottle_near_ee_valid, bottle_near_ee = message.bottle_near_ee,
            load_detected_available = message.load_detected_available, load_detected_valid = message.load_detected_valid,
            load_detected = message.load_detected, main_condition = message.main_condition,
            bottle_ee_distance_valid = message.bottle_ee_distance_valid, bottle_ee_distance = message.bottle_ee_distance,
            gripper_actual_position_valid = message.gripper_actual_position_valid,
            gripper_actual_position = message.gripper_actual_position,
            last_transition_time_valid = message.last_transition_time_valid,
            last_transition_time_ns = message.last_transition_time_ns,
            invalid_reason = message.invalid_reason, load_invalid_reason = message.load_invalid_reason,
            input_age_names = message.input_age_names, input_age_valid = message.input_age_valid,
            input_ages_ns = message.input_ages_ns, stale_inputs = message.stale_inputs
        };
        return true;
    }

    public static bool TryMapParticipant(string value, out SharedMRParticipantId participant)
    {
        if (string.Equals(value, nameof(SharedMRParticipantId.User1), StringComparison.Ordinal)) { participant = SharedMRParticipantId.User1; return true; }
        if (string.Equals(value, nameof(SharedMRParticipantId.User2), StringComparison.Ordinal)) { participant = SharedMRParticipantId.User2; return true; }
        if (string.Equals(value, nameof(SharedMRParticipantId.User3), StringComparison.Ordinal)) { participant = SharedMRParticipantId.User3; return true; }
        participant = SharedMRParticipantId.Unassigned; return false;
    }
}
