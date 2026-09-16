using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

/// <summary>Compact, room-global semantic state. No raw ROS telemetry is replicated.</summary>
[DisallowMultipleComponent]
public sealed class SharedPairSemanticStateNetwork :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    public static SharedPairSemanticStateNetwork Instance { get; private set; }
    [SerializeField, Min(0f)] private float staleAfterSeconds = 1.0f;
    [SerializeField] private bool enableAuditLogs = true;
    private int observedAuthority = int.MinValue;
    private ulong observedUser1Sequence = ulong.MaxValue;
    private ulong observedUser2Sequence = ulong.MaxValue;

#if FUSION_WEAVER && FUSION2
    [Networked] private NetworkBool User1Valid { get; set; }
    [Networked] private NetworkBool HasUser1Sample { get; set; }
    [Networked] private NetworkBool User1Holds { get; set; }
    [Networked] private NetworkBool User1CandidateValid { get; set; }
    [Networked] private long User1CandidateId { get; set; }
    [Networked] private NetworkBool User1HeldValid { get; set; }
    [Networked] private long User1HeldId { get; set; }
    [Networked] private ulong User1SourceSequence { get; set; }
    [Networked] private NetworkBool User1SourceTimestampValid { get; set; }
    [Networked] private ulong User1SourceTimestamp { get; set; }
    [Networked] private long User1ReceiveTime { get; set; }
    [Networked] private int User1ReceiveTick { get; set; }
    [Networked] private NetworkBool User1Stale { get; set; }
    [Networked] private NetworkString<_64> User1Session { get; set; }
    [Networked] private NetworkBool User2Valid { get; set; }
    [Networked] private NetworkBool HasUser2Sample { get; set; }
    [Networked] private NetworkBool User2Holds { get; set; }
    [Networked] private NetworkBool User2CandidateValid { get; set; }
    [Networked] private long User2CandidateId { get; set; }
    [Networked] private NetworkBool User2HeldValid { get; set; }
    [Networked] private long User2HeldId { get; set; }
    [Networked] private ulong User2SourceSequence { get; set; }
    [Networked] private NetworkBool User2SourceTimestampValid { get; set; }
    [Networked] private ulong User2SourceTimestamp { get; set; }
    [Networked] private long User2ReceiveTime { get; set; }
    [Networked] private int User2ReceiveTick { get; set; }
    [Networked] private NetworkBool User2Stale { get; set; }
    [Networked] private NetworkString<_64> User2Session { get; set; }

    public override void Spawned()
    {
        Instance = this;
        Audit("spawned");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
        {
            ObserveChanges();
            return;
        }

        long now = ReceiveTimeMilliseconds();
        long staleAfter = (long)(staleAfterSeconds * 1000f);
        if (User1Valid && now - User1ReceiveTime > staleAfter)
        {
            User1Valid = false; User1Holds = false; User1HeldValid = false; User1HeldId = -1; User1Stale = true; Audit("user1-stale");
        }
        if (User2Valid && now - User2ReceiveTime > staleAfter)
        {
            User2Valid = false; User2Holds = false; User2HeldValid = false; User2HeldId = -1; User2Stale = true; Audit("user2-stale");
        }
        ObserveChanges();
    }

    public bool SubmitLocal(PairSemanticState state)
    {
        if (Object == null || Runner == null || NetworkUserAvatar.Local == null
            || state.participant_id != NetworkUserAvatar.Local.ParticipantId || string.IsNullOrEmpty(state.session_id))
            return false;
        RPC_Submit((int)state.participant_id, state.valid, state.holds_object,
            state.grasp_candidate_valid, state.grasp_candidate_object_id, state.held_object_valid, state.held_object_id,
            state.source_timestamp_valid, state.source_timestamp, state.source_sequence, state.session_id);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_Submit(int participant, bool valid, bool holds, bool candidateValid, long candidateId,
        bool heldValid, long heldId, bool sourceTimestampValid, ulong sourceTimestamp, ulong sequence,
        string sessionId, RpcInfo info = default)
    {
        SharedMRParticipantId claimed = (SharedMRParticipantId)participant;
        if (!TryResolveParticipant(info.Source, out SharedMRParticipantId actual) || claimed != actual)
        {
            Debug.LogWarning("[PairSemanticState] rejected participant mismatch claimed=" + claimed + " source=" + info.Source);
            return;
        }
        ApplyAuthority(claimed, valid, holds, candidateValid, candidateId, heldValid, heldId,
            sourceTimestampValid, sourceTimestamp, sequence, sessionId);
    }

    private void ApplyAuthority(SharedMRParticipantId participant, bool valid, bool holds, bool candidateValid,
        long candidateId, bool heldValid, long heldId, bool sourceTimestampValid, ulong sourceTimestamp,
        ulong sequence, string sessionId)
    {
        if (!Object.HasStateAuthority) return;
        long now = ReceiveTimeMilliseconds();
        int tick = Runner.Tick.Raw;
        if (participant == SharedMRParticipantId.User1)
        {
            if (!AcceptSession(ref user1SessionPolicy, sessionId, sequence, now)) return;
            HasUser1Sample = true;
            User1Valid = valid; User1Holds = valid && holds; User1CandidateValid = valid && candidateValid;
            User1CandidateId = valid && candidateValid ? candidateId : -1;
            User1HeldValid = valid && holds && heldValid; User1HeldId = valid && holds && heldValid ? heldId : -1;
            User1SourceTimestampValid = sourceTimestampValid; User1SourceTimestamp = sourceTimestamp;
            User1SourceSequence = sequence; User1ReceiveTime = now; User1ReceiveTick = tick; User1Stale = false;
            User1Session = sessionId;
        }
        else if (participant == SharedMRParticipantId.User2)
        {
            if (!AcceptSession(ref user2SessionPolicy, sessionId, sequence, now)) return;
            HasUser2Sample = true;
            User2Valid = valid; User2Holds = valid && holds; User2CandidateValid = valid && candidateValid;
            User2CandidateId = valid && candidateValid ? candidateId : -1;
            User2HeldValid = valid && holds && heldValid; User2HeldId = valid && holds && heldValid ? heldId : -1;
            User2SourceTimestampValid = sourceTimestampValid; User2SourceTimestamp = sourceTimestamp;
            User2SourceSequence = sequence; User2ReceiveTime = now; User2ReceiveTick = tick; User2Stale = false;
            User2Session = sessionId;
        }
        else return;
        Audit("accepted-" + participant);
    }

    private PairSessionPolicy user1SessionPolicy;
    private PairSessionPolicy user2SessionPolicy;
    private bool AcceptSession(ref PairSessionPolicy policy, string sessionId, ulong sequence, long now)
    {
        if (policy == null) policy = new PairSessionPolicy((long)(staleAfterSeconds * 1000f));
        return policy.TryAccept(sessionId, sequence, now);
    }

    private bool TryResolveParticipant(PlayerRef source, out SharedMRParticipantId participant)
    {
        NetworkUserAvatar[] avatars = FindObjectsByType<NetworkUserAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar != null && avatar.Object != null
                && (avatar.Object.InputAuthority == source || avatar.Object.StateAuthority == source))
            {
                participant = avatar.ParticipantId;
                return participant == SharedMRParticipantId.User1 || participant == SharedMRParticipantId.User2;
            }
        }
        participant = SharedMRParticipantId.Unassigned;
        return false;
    }

    private long ReceiveTimeMilliseconds() => (long)(Runner.SimulationTime * 1000.0);
    private void ObserveChanges()
    {
        int authority = Object.StateAuthority.PlayerId;
        if (authority != observedAuthority || User1SourceSequence != observedUser1Sequence || User2SourceSequence != observedUser2Sequence)
        {
            observedAuthority = authority; observedUser1Sequence = User1SourceSequence; observedUser2Sequence = User2SourceSequence;
            Audit("observed");
        }
    }
    private void Audit(string reason)
    {
        if (!enableAuditLogs || Object == null || Runner == null) return;
        Debug.Log("[PairSemanticState] " + reason + " local="
            + (NetworkUserAvatar.Local != null ? NetworkUserAvatar.Local.ParticipantId.ToString() : "Unassigned")
            + " authority=" + Object.StateAuthority + " hasAuthority=" + Object.HasStateAuthority
            + " u1=" + User1Valid + "/" + User1Holds + "/" + User1HeldId + "/" + User1SourceSequence
            + " u2=" + User2Valid + "/" + User2Holds + "/" + User2HeldId + "/" + User2SourceSequence
            + " tick=" + Runner.Tick.Raw);
    }

    private PairSemanticState Read(SharedMRParticipantId id)
    {
        bool u1 = id == SharedMRParticipantId.User1;
        return new PairSemanticState {
            participant_id = id, valid = u1 ? User1Valid : User2Valid, holds_object = u1 ? User1Holds : User2Holds,
            grasp_candidate_valid = u1 ? User1CandidateValid : User2CandidateValid,
            grasp_candidate_object_id = u1 ? User1CandidateId : User2CandidateId,
            held_object_valid = u1 ? User1HeldValid : User2HeldValid,
            held_object_id = u1 ? User1HeldId : User2HeldId, source_sequence = u1 ? User1SourceSequence : User2SourceSequence,
            source_timestamp_valid = u1 ? User1SourceTimestampValid : User2SourceTimestampValid,
            source_timestamp = u1 ? User1SourceTimestamp : User2SourceTimestamp,
            receive_time = u1 ? User1ReceiveTime : User2ReceiveTime, receive_tick = u1 ? User1ReceiveTick : User2ReceiveTick,
            stale = u1 ? User1Stale : User2Stale, session_id = u1 ? User1Session.ToString() : User2Session.ToString() };
    }
#endif

    private void OnDestroy() { if (Instance == this) Instance = null; }
    public static bool TryRead(SharedMRParticipantId participant, out PairSemanticState state)
    {
#if FUSION_WEAVER && FUSION2
        if (Instance != null && Instance.Object != null
            && (participant == SharedMRParticipantId.User1 || participant == SharedMRParticipantId.User2))
        { state = Instance.Read(participant); return true; }
#endif
        state = default; return false;
    }
}

/// <summary>Bounded publisher-session lifecycle policy; independent instance per pair.</summary>
public sealed class PairSessionPolicy
{
    private readonly string[] retired = new string[PairSemanticStateReducer.RetiredSessionCapacity];
    private readonly long staleAfter;
    private string current;
    private ulong lastSequence;
    private long lastReceive;
    private bool hasCurrent;
    private int retiredCount;
    private int retiredIndex;

    public PairSessionPolicy(long staleAfterMilliseconds) { staleAfter = staleAfterMilliseconds; }
    public bool TryAccept(string session, ulong sequence, long receiveTime)
    {
        if (string.IsNullOrEmpty(session)) return false;
        if (!hasCurrent) { SetCurrent(session, sequence, receiveTime); return true; }
        if (string.Equals(session, current, System.StringComparison.Ordinal))
        {
            if (sequence <= lastSequence) return false;
            lastSequence = sequence; lastReceive = receiveTime; return true;
        }
        for (int i = 0; i < retiredCount; i++)
            if (string.Equals(retired[i], session, System.StringComparison.Ordinal)) return false;
        if (receiveTime - lastReceive <= staleAfter) return false;
        retired[retiredIndex] = current;
        retiredIndex = (retiredIndex + 1) % retired.Length;
        if (retiredCount < retired.Length) retiredCount++;
        SetCurrent(session, sequence, receiveTime);
        return true;
    }
    private void SetCurrent(string session, ulong sequence, long receiveTime)
    { current = session; lastSequence = sequence; lastReceive = receiveTime; hasCurrent = true; }
}
