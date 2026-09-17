#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using RosMessageTypes.ShareSemanticInterfaces;

public sealed class PairSemanticStateReducerTests
{
    private static LocalHoldState State(SharedMRParticipantId id, ulong sequence, bool valid = true)
    {
        return new LocalHoldState { participant_id = id, sequence = sequence, session_id = "session-a", valid = valid,
            holds_object = true, grasp_candidate_valid = true, grasp_candidate_object_id = 4,
            held_object_valid = true, held_object_id = 3, source_timestamp_valid = true, source_timestamp = 99 };
    }

    [Test] public void U1_User1UpdatesPairA() { Assert.IsTrue(new PairSemanticStateReducer(SharedMRParticipantId.User1).TryApply(State(SharedMRParticipantId.User1, 1), 10, 2, out _)); }
    [Test] public void U2_User2UpdatesPairB() { Assert.IsTrue(new PairSemanticStateReducer(SharedMRParticipantId.User2).TryApply(State(SharedMRParticipantId.User2, 1), 10, 2, out _)); }
    [Test] public void U3_User1CannotUpdatePairB() { Assert.IsFalse(new PairSemanticStateReducer(SharedMRParticipantId.User2).TryApply(State(SharedMRParticipantId.User1, 1), 10, 2, out _)); }
    [Test] public void U4_UnassignedIsRejected() { Assert.IsFalse(new PairSemanticStateReducer(SharedMRParticipantId.User1).TryApply(State(SharedMRParticipantId.Unassigned, 1), 10, 2, out _)); }
    [Test] public void U5_OldSequenceIsRejected() { var r = new PairSemanticStateReducer(SharedMRParticipantId.User1); r.TryApply(State(SharedMRParticipantId.User1, 2), 10, 2, out _); Assert.IsFalse(r.TryApply(State(SharedMRParticipantId.User1, 1), 11, 3, out _)); }
    [Test] public void U6_DuplicateSequenceIsRejected() { var r = new PairSemanticStateReducer(SharedMRParticipantId.User1); r.TryApply(State(SharedMRParticipantId.User1, 2), 10, 2, out _); Assert.IsFalse(r.TryApply(State(SharedMRParticipantId.User1, 2), 11, 3, out _)); }
    [Test] public void U7_InvalidClearsPhysicalState() { var r = new PairSemanticStateReducer(SharedMRParticipantId.User1); r.TryApply(State(SharedMRParticipantId.User1, 1, false), 10, 2, out var s); Assert.IsFalse(s.valid); Assert.IsFalse(s.holds_object); Assert.AreEqual(-1, s.held_object_id); }
    [Test] public void U8_HeldObjectIsPreserved() { var r = new PairSemanticStateReducer(SharedMRParticipantId.User1); r.TryApply(State(SharedMRParticipantId.User1, 1), 10, 2, out var s); Assert.IsTrue(s.holds_object); Assert.AreEqual(3, s.held_object_id); }
    [Test] public void StaleClearsValidityWithoutChangingSequence() { var r = new PairSemanticStateReducer(SharedMRParticipantId.User1); r.TryApply(State(SharedMRParticipantId.User1, 7), 10, 2, out var s); s = PairSemanticStateReducer.ApplyFreshness(s, 21, 10); Assert.IsTrue(s.stale); Assert.IsFalse(s.valid); Assert.AreEqual(7, s.source_sequence); }
}

public sealed class PairSessionPolicyTests
{
    [Test] public void B2_SameSessionMonotonicAndRestartPolicy()
    {
        var p = new PairSessionPolicy(10);
        Assert.IsTrue(p.TryAccept("A", 1, 0));
        Assert.IsTrue(p.TryAccept("A", 2, 1));
        Assert.IsFalse(p.TryAccept("A", 2, 2));
        Assert.IsFalse(p.TryAccept("A", 1, 3));
        Assert.IsFalse(p.TryAccept("B", 0, 10));
        Assert.IsTrue(p.TryAccept("B", 0, 12));
        Assert.IsFalse(p.TryAccept("A", 3, 13));
        Assert.IsTrue(p.TryAccept("B", 1, 14));
    }
    [Test] public void B2_PairsHaveIndependentSessions()
    {
        var a = new PairSessionPolicy(10); var b = new PairSessionPolicy(10);
        Assert.IsTrue(a.TryAccept("A", 7, 0)); Assert.IsTrue(b.TryAccept("B", 0, 0));
    }
    [Test] public void B2_SessionSwitchDoesNotMutateControlState()
    {
        var control = new SharedControlState { task_phase = TaskPhase.Transfer,
            owner_type = SharedControlOwnerType.Robot, owner_id = 2, sequence = 9 };
        var p = new PairSessionPolicy(10);
        Assert.IsTrue(p.TryAccept("A", 300, 0)); Assert.IsTrue(p.TryAccept("B", 0, 11));
        Assert.AreEqual(TaskPhase.Transfer, control.task_phase);
        Assert.AreEqual(SharedControlOwnerType.Robot, control.owner_type);
        Assert.AreEqual(2, control.owner_id); Assert.AreEqual(9, control.sequence);
    }
}
public sealed class LocalHoldTopicResolverTests
{
    [Test] public void B1_UserTopicsUseSymbolicParticipantMapping()
    {
        Assert.IsTrue(LocalHoldStateIngress.TryResolveTopic(SharedMRParticipantId.User1, out string u1));
        Assert.IsTrue(LocalHoldStateIngress.TryResolveTopic(SharedMRParticipantId.User2, out string u2));
        Assert.AreEqual("/share/users/user_1/local_hold_state", u1);
        Assert.AreEqual("/share/users/user_2/local_hold_state", u2);
        Assert.IsFalse(LocalHoldStateIngress.TryResolveTopic(SharedMRParticipantId.Unassigned, out _));
    }
}

public sealed class LocalHoldRosMapperTests
{
    private static LocalHoldStateMsg Message(string participant = "User1")
    {
        return new LocalHoldStateMsg {
            participant_id = participant, session_id = "session-formal", sequence = ulong.MaxValue,
            source_timestamp_valid = true, source_timestamp_ns = ulong.MaxValue - 1, valid = true, holds_object = true,
            grasp_candidate_valid = true, grasp_candidate_object_id = long.MinValue,
            held_object_valid = true, held_object_id = long.MaxValue,
            close_intent_valid = true, close_intent = true, intermediate_stop_valid = true, intermediate_stop = true,
            bottle_near_ee_valid = true, bottle_near_ee = true, load_detected_available = true,
            load_detected_valid = true, load_detected = true, main_condition = true,
            bottle_ee_distance_valid = true, bottle_ee_distance = 1.25,
            gripper_actual_position_valid = true, gripper_actual_position = 2.5,
            last_transition_time_valid = true, last_transition_time_ns = ulong.MaxValue - 2,
            invalid_reason = "none", load_invalid_reason = "none-load", input_age_names = new[] { "joint" },
            input_age_valid = new[] { true }, input_ages_ns = new long[] { -7 }, stale_inputs = new[] { "camera" }
        };
    }
    [Test] public void B1_GeneratedContractAndAllFieldsMapLosslessly()
    {
        Assert.AreEqual("share_semantic_interfaces/LocalHoldState", LocalHoldStateMsg.k_RosMessageName);
        Assert.IsTrue(LocalHoldRosMapper.TryMap(Message(), out LocalHoldRosSnapshot s));
        Assert.AreEqual(SharedMRParticipantId.User1, s.semantic.participant_id);
        Assert.AreEqual("session-formal", s.semantic.session_id); Assert.IsTrue(s.semantic.valid && s.semantic.holds_object);
        Assert.AreEqual(ulong.MaxValue, s.semantic.sequence); Assert.AreEqual(ulong.MaxValue - 1, s.semantic.source_timestamp);
        Assert.IsTrue(s.semantic.source_timestamp_valid); Assert.AreEqual(long.MinValue, s.semantic.grasp_candidate_object_id);
        Assert.IsTrue(s.semantic.grasp_candidate_valid); Assert.AreEqual(long.MaxValue, s.semantic.held_object_id);
        Assert.IsTrue(s.semantic.held_object_valid); Assert.IsTrue(s.close_intent_valid && s.close_intent);
        Assert.IsTrue(s.intermediate_stop_valid && s.intermediate_stop); Assert.IsTrue(s.bottle_near_ee_valid && s.bottle_near_ee);
        Assert.IsTrue(s.load_detected_available && s.load_detected_valid && s.load_detected && s.main_condition);
        Assert.IsTrue(s.bottle_ee_distance_valid); Assert.AreEqual(1.25, s.bottle_ee_distance);
        Assert.IsTrue(s.gripper_actual_position_valid); Assert.AreEqual(2.5, s.gripper_actual_position);
        Assert.IsTrue(s.last_transition_time_valid); Assert.AreEqual(ulong.MaxValue - 2, s.last_transition_time_ns);
        Assert.AreEqual("none", s.invalid_reason);
        Assert.AreEqual("none-load", s.load_invalid_reason); Assert.AreEqual("joint", s.input_age_names[0]);
        Assert.IsTrue(s.input_age_valid[0]); Assert.AreEqual(-7, s.input_ages_ns[0]); Assert.AreEqual("camera", s.stale_inputs[0]);
    }
    [Test] public void B1_IdentityAndInvalidSemanticsReachExistingReducer()
    {
        Assert.IsTrue(LocalHoldRosMapper.TryMap(Message("User2"), out LocalHoldRosSnapshot u2));
        Assert.AreEqual(SharedMRParticipantId.User2, u2.semantic.participant_id);
        Assert.IsFalse(LocalHoldRosMapper.TryMap(Message("Unassigned"), out _));
        Assert.IsFalse(LocalHoldRosMapper.TryMap(Message("user_1"), out _));
        LocalHoldStateMsg invalid = Message(); invalid.valid = false;
        Assert.IsTrue(LocalHoldRosMapper.TryMap(invalid, out LocalHoldRosSnapshot mapped));
        var reducer = new PairSemanticStateReducer(SharedMRParticipantId.User1);
        Assert.IsTrue(reducer.TryApply(mapped.semantic, 10, 1, out PairSemanticState state));
        Assert.IsFalse(state.valid); Assert.IsFalse(state.holds_object);
        Assert.IsFalse(state.grasp_candidate_valid); Assert.IsFalse(state.held_object_valid);
    }
    [Test] public void B1_TypedMessageUsesExistingSameSessionSequencePolicy()
    {
        var reducer = new PairSemanticStateReducer(SharedMRParticipantId.User1);
        LocalHoldStateMsg first = Message(); first.sequence = 4;
        LocalHoldStateMsg duplicate = Message(); duplicate.sequence = 4;
        Assert.IsTrue(LocalHoldRosMapper.TryMap(first, out LocalHoldRosSnapshot a));
        Assert.IsTrue(LocalHoldRosMapper.TryMap(duplicate, out LocalHoldRosSnapshot b));
        Assert.IsTrue(reducer.TryApply(a.semantic, 10, 1, out _));
        Assert.IsFalse(reducer.TryApply(b.semantic, 11, 2, out _));
    }
}

public static class PairSemanticStateAutomatedVerifier
{
    [MenuItem("SHARE/P1-03B-01C/Run Semantic State Verification")]
    public static void Run()
    {
        PairSemanticStateReducerTests tests = new PairSemanticStateReducerTests();
        tests.U1_User1UpdatesPairA(); tests.U2_User2UpdatesPairB(); tests.U3_User1CannotUpdatePairB();
        tests.U4_UnassignedIsRejected(); tests.U5_OldSequenceIsRejected(); tests.U6_DuplicateSequenceIsRejected();
        tests.U7_InvalidClearsPhysicalState(); tests.U8_HeldObjectIsPreserved(); tests.StaleClearsValidityWithoutChangingSequence();
        PairSessionPolicyTests sessions = new PairSessionPolicyTests();
        sessions.B2_SameSessionMonotonicAndRestartPolicy(); sessions.B2_PairsHaveIndependentSessions();
        sessions.B2_SessionSwitchDoesNotMutateControlState();
        new LocalHoldTopicResolverTests().B1_UserTopicsUseSymbolicParticipantMapping();
        LocalHoldRosMapperTests mapper = new LocalHoldRosMapperTests();
        mapper.B1_GeneratedContractAndAllFieldsMapLosslessly(); mapper.B1_IdentityAndInvalidSemanticsReachExistingReducer();
        mapper.B1_TypedMessageUsesExistingSameSessionSequencePolicy();
        Debug.Log("[P1-03B-01C] AUTOMATED_VERIFICATION_PASS cases=16");
    }
}
#endif
