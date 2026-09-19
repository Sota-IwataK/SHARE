using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

/// <summary>Authority-owned Photon transport for manual handover session events.</summary>
[DisallowMultipleComponent]
public sealed class ManualHandoverSessionNetwork :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    public const int RetiredSessionCapacity = 16;
    public static ManualHandoverSessionNetwork Instance { get; private set; }
    public ManualHandoverEventDiagnostic LastDiagnostic { get; private set; }

#if FUSION_WEAVER && FUSION2
    [Networked] private int LifecycleStateValue { get; set; }
    [Networked] private NetworkBool SessionValid { get; set; }
    [Networked] private int RetiredSessionCountValue { get; set; }
    [Networked, Capacity(RetiredSessionCapacity)]
    private NetworkArray<NetworkString<_128>> RetiredSessionIdsValue => default;
    [Networked] private NetworkString<_128> CoordinationSessionIdValue { get; set; }
    [Networked] private int GiverParticipantValue { get; set; }
    [Networked] private int GiverRobotValue { get; set; }
    [Networked] private int ReceiverParticipantValue { get; set; }
    [Networked] private int ReceiverRobotValue { get; set; }
    [Networked] private NetworkString<_64> TargetSourceIdValue { get; set; }
    [Networked] private NetworkString<_128> TargetSessionIdValue { get; set; }
    [Networked] private ulong TargetObjectIdValue { get; set; }
    [Networked] private ulong CreatedSequenceValue { get; set; }
    [Networked] private ulong CreatedTimestampValue { get; set; }
    [Networked] private NetworkBool RequestAcceptedValue { get; set; }
    [Networked] private ulong RequestSourceSequenceValue { get; set; }
    [Networked] private ulong RequestSourceTimestampValue { get; set; }
    [Networked] private NetworkString<_64> RequestSourceClockValue { get; set; }
    [Networked] private int RequestSourceParticipantValue { get; set; }
    [Networked] private NetworkBool ReadyAcceptedValue { get; set; }
    [Networked] private ulong ReadySourceSequenceValue { get; set; }
    [Networked] private ulong ReadySourceTimestampValue { get; set; }
    [Networked] private NetworkString<_64> ReadySourceClockValue { get; set; }
    [Networked] private int ReadySourceParticipantValue { get; set; }
    [Networked] private ulong AuthorityAcceptedSequenceValue { get; set; }
    [Networked] private long AuthorityAcceptedTimestampValue { get; set; }
    [Networked] private NetworkString<_64> AuthorityClockDomainValue { get; set; }

    public override void Spawned() { Instance = this; }
    public override void Despawned(NetworkRunner runner, bool hasState)
    { if (Instance == this) Instance = null; }

    public bool TryInitializeSession(ManualHandoverSession session, out string reason)
    {
        if (Runner == null) { reason = "RunnerUnavailable"; return false; }
        if (!HasStateAuthority) { reason = "NoStateAuthority"; return false; }
        if (!TryReadLifecycleContract(out ManualHandoverSessionLifecycleContract lifecycle))
        { reason = "AuthorityStateInvalid"; return false; }

        ManualHandoverSessionLifecycleResult result = lifecycle.TryActivate(
            session, AuthorityTimeMilliseconds(), RetiredSessionCapacity);
        if (result != ManualHandoverSessionLifecycleResult.Accepted)
        { reason = result.ToString(); return false; }

        ManualHandoverTransportSnapshot snapshot = lifecycle.RetainedSession;
        if (!TryEncodeSession(snapshot.Session, out NetworkString<_128> coordinationSession,
                out NetworkString<_64> source, out NetworkString<_128> targetSession)
            || !TrySetNetworkString(ManualHandoverTransportContract.AuthorityClockDomain,
                out NetworkString<_64> authorityClock))
        { reason = "PhotonNetworkStringWouldTruncate"; return false; }

        CommitNewActiveSession(snapshot, coordinationSession, source, targetSession, authorityClock);
        reason = "Accepted";
        return true;
    }

    public bool TryRetireSession(out string reason)
    {
        if (Runner == null) { reason = "RunnerUnavailable"; return false; }
        if (!HasStateAuthority) { reason = "NoStateAuthority"; return false; }
        if (!TryReadLifecycleContract(out ManualHandoverSessionLifecycleContract lifecycle))
        { reason = "AuthorityStateInvalid"; return false; }
        if (lifecycle.State == ManualHandoverSessionLifecycleState.Active
            && lifecycle.RetiredSessionCount >= RetiredSessionCapacity)
        {
            reason = ManualHandoverSessionLifecycleResult
                .RetiredSessionLedgerCapacityExceeded.ToString();
            return false;
        }

        ManualHandoverSessionLifecycleResult result = lifecycle.TryRetire();
        if (result != ManualHandoverSessionLifecycleResult.Accepted)
        { reason = result.ToString(); return false; }

        string retiredId = lifecycle.RetainedSession.Session.CoordinationSessionId;
        if (!TrySetNetworkString(retiredId, out NetworkString<_128> encodedRetiredId))
        { reason = "PhotonNetworkStringWouldTruncate"; return false; }
        RetiredSessionIdsValue.Set(RetiredSessionCountValue, encodedRetiredId);
        RetiredSessionCountValue++;
        LifecycleStateValue = (int)ManualHandoverSessionLifecycleState.Retired;
        reason = "Accepted";
        return true;
    }

    public bool SubmitLocal(ManualCoordinationRequest request)
    {
        if (!CanSubmit(request.SourceParticipantId)
            || string.IsNullOrWhiteSpace(request.CoordinationSessionId)
            || string.IsNullOrWhiteSpace(request.SourceClockDomain)
            || !request.TargetBottleKey.IsValid) return false;
        RPC_SubmitRequest(request.CoordinationSessionId, request.EventSequence,
            request.EventTimestamp, request.SourceClockDomain, (int)request.SourceParticipantId,
            request.TargetBottleKey.SourceId, request.TargetBottleKey.SessionId,
            request.TargetBottleKey.ObjectId);
        return true;
    }

    public bool SubmitLocal(ReadyAcknowledgement ready)
    {
        if (!CanSubmit(ready.SourceParticipantId)
            || string.IsNullOrWhiteSpace(ready.CoordinationSessionId)
            || string.IsNullOrWhiteSpace(ready.SourceClockDomain)
            || !ready.TargetBottleKey.IsValid) return false;
        RPC_SubmitReady(ready.CoordinationSessionId, ready.EventSequence,
            ready.EventTimestamp, ready.SourceClockDomain, (int)ready.SourceParticipantId,
            ready.TargetBottleKey.SourceId, ready.TargetBottleKey.SessionId,
            ready.TargetBottleKey.ObjectId);
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_SubmitRequest(string sessionId, ulong sourceSequence, ulong sourceTimestamp,
        string sourceClock, int claimedParticipant, string targetSource, string targetSession,
        ulong targetObject, RpcInfo info = default)
    {
        ManualCoordinationRequest request = new ManualCoordinationRequest(
            sessionId, sourceSequence, sourceTimestamp, sourceClock,
            (SharedMRParticipantId)claimedParticipant,
            CreateIdentity(targetSource, targetSession, targetObject));
        ApplyRequest(info.Source, request);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_SubmitReady(string sessionId, ulong sourceSequence, ulong sourceTimestamp,
        string sourceClock, int claimedParticipant, string targetSource, string targetSession,
        ulong targetObject, RpcInfo info = default)
    {
        ReadyAcknowledgement ready = new ReadyAcknowledgement(
            sessionId, sourceSequence, sourceTimestamp, sourceClock,
            (SharedMRParticipantId)claimedParticipant,
            CreateIdentity(targetSource, targetSession, targetObject));
        ApplyReady(info.Source, ready);
    }

    private void ApplyRequest(PlayerRef source, ManualCoordinationRequest request)
    {
        if (!Object.HasStateAuthority) return;
        if (!TryReadLifecycleContract(out ManualHandoverSessionLifecycleContract lifecycle))
        { Record(ManualHandoverEventType.CoordinationRequest, request,
            ManualHandoverValidationResult.AuthorityStateInvalid); return; }
        if (lifecycle.State != ManualHandoverSessionLifecycleState.Active)
        {
            ManualHandoverValidationResult inactiveResult = lifecycle.TryAcceptRequest(
                SharedMRParticipantId.Unassigned, request, AuthorityTimeMilliseconds());
            Record(ManualHandoverEventType.CoordinationRequest, request, inactiveResult);
            return;
        }
        if (!TryResolveConnectedParticipant(source, out SharedMRParticipantId actual))
        { Record(ManualHandoverEventType.CoordinationRequest, request,
            ManualHandoverValidationResult.RpcSenderMismatch); return; }
        ManualHandoverValidationResult result = lifecycle.TryAcceptRequest(
            actual, request, AuthorityTimeMilliseconds());
        if (result == ManualHandoverValidationResult.Accepted)
            Commit(lifecycle.RetainedSession);
        Record(ManualHandoverEventType.CoordinationRequest, request, result);
    }

    private void ApplyReady(PlayerRef source, ReadyAcknowledgement ready)
    {
        if (!Object.HasStateAuthority) return;
        if (!TryReadLifecycleContract(out ManualHandoverSessionLifecycleContract lifecycle))
        { Record(ManualHandoverEventType.ReadyAcknowledgement, ready,
            ManualHandoverValidationResult.AuthorityStateInvalid); return; }
        if (lifecycle.State != ManualHandoverSessionLifecycleState.Active)
        {
            ManualHandoverValidationResult inactiveResult = lifecycle.TryAcceptReady(
                SharedMRParticipantId.Unassigned, ready, AuthorityTimeMilliseconds());
            Record(ManualHandoverEventType.ReadyAcknowledgement, ready, inactiveResult);
            return;
        }
        if (!TryResolveConnectedParticipant(source, out SharedMRParticipantId actual))
        { Record(ManualHandoverEventType.ReadyAcknowledgement, ready,
            ManualHandoverValidationResult.RpcSenderMismatch); return; }
        ManualHandoverValidationResult result = lifecycle.TryAcceptReady(
            actual, ready, AuthorityTimeMilliseconds());
        if (result == ManualHandoverValidationResult.Accepted)
            Commit(lifecycle.RetainedSession);
        Record(ManualHandoverEventType.ReadyAcknowledgement, ready, result);
    }

    private void CommitNewActiveSession(
        ManualHandoverTransportSnapshot snapshot,
        NetworkString<_128> coordinationSession,
        NetworkString<_64> targetSource,
        NetworkString<_128> targetSession,
        NetworkString<_64> authorityClock)
    {
        ManualHandoverSession session = snapshot.Session;
        CoordinationSessionIdValue = coordinationSession;
        GiverParticipantValue = (int)session.RoleBinding.GiverParticipantId;
        GiverRobotValue = session.RoleBinding.GiverRobotId;
        ReceiverParticipantValue = (int)session.RoleBinding.ReceiverParticipantId;
        ReceiverRobotValue = session.RoleBinding.ReceiverRobotId;
        TargetSourceIdValue = targetSource;
        TargetSessionIdValue = targetSession;
        TargetObjectIdValue = session.TargetBottleKey.ObjectId;
        CreatedSequenceValue = session.CreatedSequence;
        CreatedTimestampValue = session.CreatedTimestamp;
        RequestSourceSequenceValue = 0UL;
        RequestSourceTimestampValue = 0UL;
        RequestSourceClockValue = default;
        RequestSourceParticipantValue = (int)SharedMRParticipantId.Unassigned;
        RequestAcceptedValue = false;
        ReadySourceSequenceValue = 0UL;
        ReadySourceTimestampValue = 0UL;
        ReadySourceClockValue = default;
        ReadySourceParticipantValue = (int)SharedMRParticipantId.Unassigned;
        ReadyAcceptedValue = false;
        AuthorityAcceptedSequenceValue = snapshot.AuthorityAcceptedSequence;
        AuthorityAcceptedTimestampValue = snapshot.AuthorityAcceptedTimestamp;
        AuthorityClockDomainValue = authorityClock;
        LastDiagnostic = default;
        LifecycleStateValue = (int)ManualHandoverSessionLifecycleState.Active;
        SessionValid = true;
    }

    private void Commit(ManualHandoverTransportSnapshot snapshot)
    {
        if (snapshot.RequestAccepted && !RequestAcceptedValue)
        {
            TrySetNetworkString(snapshot.Request.SourceClockDomain, out NetworkString<_64> clock);
            RequestSourceSequenceValue = snapshot.Request.EventSequence;
            RequestSourceTimestampValue = snapshot.Request.EventTimestamp;
            RequestSourceClockValue = clock;
            RequestSourceParticipantValue = (int)snapshot.Request.SourceParticipantId;
            RequestAcceptedValue = true;
        }
        if (snapshot.ReadyAccepted && !ReadyAcceptedValue)
        {
            TrySetNetworkString(snapshot.Ready.SourceClockDomain, out NetworkString<_64> clock);
            ReadySourceSequenceValue = snapshot.Ready.EventSequence;
            ReadySourceTimestampValue = snapshot.Ready.EventTimestamp;
            ReadySourceClockValue = clock;
            ReadySourceParticipantValue = (int)snapshot.Ready.SourceParticipantId;
            ReadyAcceptedValue = true;
        }
        AuthorityAcceptedSequenceValue = snapshot.AuthorityAcceptedSequence;
        AuthorityAcceptedTimestampValue = snapshot.AuthorityAcceptedTimestamp;
    }

    private bool TryReadSnapshot(out ManualHandoverTransportSnapshot snapshot)
    {
        snapshot = default;
        if (!SessionValid
            || !ManualHandoverRoleBinding.TryCreate(
                (SharedMRParticipantId)GiverParticipantValue, GiverRobotValue,
                (SharedMRParticipantId)ReceiverParticipantValue, ReceiverRobotValue,
                out ManualHandoverRoleBinding roles)) return false;
        try
        {
            CanonicalBottleIdentity target = new CanonicalBottleIdentity(
                TargetSourceIdValue.ToString(), TargetSessionIdValue.ToString(), TargetObjectIdValue);
            if (!ManualHandoverSession.TryCreate(
                    CoordinationSessionIdValue.ToString(), roles, target,
                    CreatedSequenceValue, CreatedTimestampValue, out ManualHandoverSession session)) return false;
            ManualCoordinationRequest request = RequestAcceptedValue
                ? new ManualCoordinationRequest(session.CoordinationSessionId,
                    RequestSourceSequenceValue, RequestSourceTimestampValue,
                    RequestSourceClockValue.ToString(),
                    (SharedMRParticipantId)RequestSourceParticipantValue, target)
                : default;
            ReadyAcknowledgement ready = ReadyAcceptedValue
                ? new ReadyAcknowledgement(session.CoordinationSessionId,
                    ReadySourceSequenceValue, ReadySourceTimestampValue,
                    ReadySourceClockValue.ToString(),
                    (SharedMRParticipantId)ReadySourceParticipantValue, target)
                : default;
            snapshot = new ManualHandoverTransportSnapshot(
                session, RequestAcceptedValue, request, ReadyAcceptedValue, ready,
                AuthorityAcceptedSequenceValue, AuthorityAcceptedTimestampValue,
                AuthorityClockDomainValue.ToString());
            return snapshot.IsValid;
        }
        catch (System.ArgumentException) { return false; }
    }

    private bool TryReadLifecycleContract(out ManualHandoverSessionLifecycleContract lifecycle)
    {
        lifecycle = null;
        if (LifecycleStateValue < (int)ManualHandoverSessionLifecycleState.NoSession
            || LifecycleStateValue > (int)ManualHandoverSessionLifecycleState.Retired
            || RetiredSessionCountValue < 0
            || RetiredSessionCountValue > RetiredSessionCapacity)
        {
            return false;
        }

        bool hasRetainedSession = SessionValid;
        ManualHandoverTransportSnapshot retained = default;
        if (hasRetainedSession && !TryReadSnapshot(out retained))
        {
            return false;
        }

        string[] retiredIds = new string[RetiredSessionCountValue];
        for (int i = 0; i < retiredIds.Length; i++)
        {
            retiredIds[i] = RetiredSessionIdsValue.Get(i).ToString();
        }

        ManualHandoverSessionLifecycleSnapshot snapshot =
            new ManualHandoverSessionLifecycleSnapshot(
                (ManualHandoverSessionLifecycleState)LifecycleStateValue,
                hasRetainedSession,
                retained,
                retiredIds);
        return ManualHandoverSessionLifecycleContract.TryRestore(snapshot, out lifecycle);
    }

    private bool TryResolveConnectedParticipant(PlayerRef source, out SharedMRParticipantId participant)
    {
        bool active = false;
        foreach (PlayerRef player in Runner.ActivePlayers) if (player == source) { active = true; break; }
        if (!active) { participant = SharedMRParticipantId.Unassigned; return false; }
        NetworkUserAvatar[] avatars = FindObjectsByType<NetworkUserAvatar>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar != null && avatar.IsNetworkStateReady && avatar.Object != null
                && (avatar.Object.InputAuthority == source || avatar.Object.StateAuthority == source))
            { participant = avatar.ParticipantId; return participant != SharedMRParticipantId.Unassigned; }
        }
        participant = SharedMRParticipantId.Unassigned;
        return false;
    }

    private bool CanSubmit(SharedMRParticipantId claimed)
    {
        return Object != null && Runner != null && NetworkUserAvatar.Local != null
            && NetworkUserAvatar.Local.ParticipantId == claimed;
    }

    private long AuthorityTimeMilliseconds() => (long)(Runner.SimulationTime * 1000.0);

    private void Record(ManualHandoverEventType type, ManualCoordinationRequest value,
        ManualHandoverValidationResult result)
    { Record(type, value.CoordinationSessionId, value.SourceParticipantId, value.TargetBottleKey,
        value.EventSequence, value.EventTimestamp, value.SourceClockDomain, result); }
    private void Record(ManualHandoverEventType type, ReadyAcknowledgement value,
        ManualHandoverValidationResult result)
    { Record(type, value.CoordinationSessionId, value.SourceParticipantId, value.TargetBottleKey,
        value.EventSequence, value.EventTimestamp, value.SourceClockDomain, result); }
    private void Record(ManualHandoverEventType type, string sessionId,
        SharedMRParticipantId participant, CanonicalBottleIdentity target, ulong sourceSequence,
        ulong sourceTimestamp, string sourceClock, ManualHandoverValidationResult result)
    {
        LastDiagnostic = new ManualHandoverEventDiagnostic(type, sessionId, participant, target,
            sourceSequence, sourceTimestamp, sourceClock, AuthorityAcceptedSequenceValue,
            AuthorityAcceptedTimestampValue, AuthorityClockDomainValue.ToString(), result);
        Debug.Log("[ManualHandover] event=" + type + " session=" + sessionId
            + " participant=" + participant + " target=" + target
            + " sourceSequence=" + sourceSequence + " sourceTimestamp=" + sourceTimestamp
            + " sourceClock=" + sourceClock + " authoritySequence=" + AuthorityAcceptedSequenceValue
            + " authorityTimestamp=" + AuthorityAcceptedTimestampValue
            + " authorityClock=" + AuthorityClockDomainValue + " result=" + result, this);
    }

    private static CanonicalBottleIdentity CreateIdentity(string source, string session, ulong objectId)
    {
        try { return new CanonicalBottleIdentity(source, session, objectId); }
        catch (System.ArgumentException) { return default; }
    }
    private static bool TryEncodeSession(ManualHandoverSession session,
        out NetworkString<_128> coordinationSession, out NetworkString<_64> source,
        out NetworkString<_128> targetSession)
    {
        coordinationSession = default; source = default; targetSession = default;
        return coordinationSession.Set(session.CoordinationSessionId)
            && source.Set(session.TargetBottleKey.SourceId)
            && targetSession.Set(session.TargetBottleKey.SessionId);
    }
    private static bool TrySetNetworkString(string value, out NetworkString<_64> encoded)
    { encoded = default; return encoded.Set(value); }
    private static bool TrySetNetworkString(string value, out NetworkString<_128> encoded)
    { encoded = default; return encoded.Set(value); }
#else
    private void Awake() { Instance = this; }
#endif

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public static bool TryRead(out ManualHandoverTransportSnapshot snapshot)
    {
        snapshot = default;
#if FUSION_WEAVER && FUSION2
        if (Instance == null || Instance.Object == null
            || !Instance.TryReadLifecycleContract(
                out ManualHandoverSessionLifecycleContract lifecycle)
            || lifecycle.State != ManualHandoverSessionLifecycleState.Active)
        {
            return false;
        }
        snapshot = lifecycle.RetainedSession;
        return true;
#else
        return false;
#endif
    }

    public static bool TryReadLifecycle(out ManualHandoverSessionLifecycleSnapshot snapshot)
    {
        snapshot = default;
#if FUSION_WEAVER && FUSION2
        if (Instance == null || Instance.Object == null
            || !Instance.TryReadLifecycleContract(
                out ManualHandoverSessionLifecycleContract lifecycle))
        {
            return false;
        }
        snapshot = lifecycle.CaptureSnapshot();
        return true;
#else
        return false;
#endif
    }
}
