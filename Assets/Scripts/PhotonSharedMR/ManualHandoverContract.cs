using System;

public enum ManualHandoverValidationResult
{
    Accepted = 0,
    InvalidRoleBinding = 1,
    InvalidCoordinationSession = 2,
    InvalidCanonicalTarget = 3,
    InvalidSourceClockDomain = 4,
    SourceNotGiver = 5,
    SourceNotReceiver = 6,
    SessionMismatch = 7,
    TargetMismatch = 8,
    DuplicateSequence = 9,
    OlderSequence = 10,
    RequestAlreadyLatched = 11,
    ReadyAlreadyLatched = 12,
    RequestRequired = 13,
    RpcSenderMismatch = 14,
    AuthoritySequenceExhausted = 15,
    AuthorityStateInvalid = 16,
    TransportCapacityExceeded = 17,
    SessionRetired = 18
}

/// <summary>Immutable run/trial-scoped giver and receiver assignment.</summary>
public readonly struct ManualHandoverRoleBinding
{
    public SharedMRParticipantId GiverParticipantId { get; }
    public int GiverRobotId { get; }
    public SharedMRParticipantId ReceiverParticipantId { get; }
    public int ReceiverRobotId { get; }

    private ManualHandoverRoleBinding(
        SharedMRParticipantId giverParticipantId,
        int giverRobotId,
        SharedMRParticipantId receiverParticipantId,
        int receiverRobotId)
    {
        GiverParticipantId = giverParticipantId;
        GiverRobotId = giverRobotId;
        ReceiverParticipantId = receiverParticipantId;
        ReceiverRobotId = receiverRobotId;
    }

    public bool IsValid => IsAssignedParticipant(GiverParticipantId)
        && IsAssignedParticipant(ReceiverParticipantId)
        && GiverParticipantId != ReceiverParticipantId
        && GiverRobotId >= 0
        && ReceiverRobotId >= 0
        && GiverRobotId != ReceiverRobotId;

    public static bool TryCreate(
        SharedMRParticipantId giverParticipantId,
        int giverRobotId,
        SharedMRParticipantId receiverParticipantId,
        int receiverRobotId,
        out ManualHandoverRoleBinding binding)
    {
        binding = new ManualHandoverRoleBinding(
            giverParticipantId,
            giverRobotId,
            receiverParticipantId,
            receiverRobotId);
        if (binding.IsValid) return true;
        binding = default;
        return false;
    }

    private static bool IsAssignedParticipant(SharedMRParticipantId participant)
    {
        return participant == SharedMRParticipantId.User1
            || participant == SharedMRParticipantId.User2
            || participant == SharedMRParticipantId.User3;
    }
}

/// <summary>Immutable identity and provenance for one manual handover trial.</summary>
public readonly struct ManualHandoverSession
{
    public string CoordinationSessionId { get; }
    public ManualHandoverRoleBinding RoleBinding { get; }
    public CanonicalBottleIdentity TargetBottleKey { get; }
    public ulong CreatedSequence { get; }
    public ulong CreatedTimestamp { get; }

    private ManualHandoverSession(
        string coordinationSessionId,
        ManualHandoverRoleBinding roleBinding,
        CanonicalBottleIdentity targetBottleKey,
        ulong createdSequence,
        ulong createdTimestamp)
    {
        CoordinationSessionId = coordinationSessionId;
        RoleBinding = roleBinding;
        TargetBottleKey = targetBottleKey;
        CreatedSequence = createdSequence;
        CreatedTimestamp = createdTimestamp;
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(CoordinationSessionId)
        && RoleBinding.IsValid
        && TargetBottleKey.IsValid;

    public static bool TryCreate(
        string coordinationSessionId,
        ManualHandoverRoleBinding roleBinding,
        CanonicalBottleIdentity targetBottleKey,
        ulong createdSequence,
        ulong createdTimestamp,
        out ManualHandoverSession session)
    {
        session = new ManualHandoverSession(
            coordinationSessionId,
            roleBinding,
            targetBottleKey,
            createdSequence,
            createdTimestamp);
        if (session.IsValid) return true;
        session = default;
        return false;
    }
}

public readonly struct ManualCoordinationRequest
{
    public string CoordinationSessionId { get; }
    public ulong EventSequence { get; }
    public ulong EventTimestamp { get; }
    public string SourceClockDomain { get; }
    public SharedMRParticipantId SourceParticipantId { get; }
    public CanonicalBottleIdentity TargetBottleKey { get; }

    public ManualCoordinationRequest(
        string coordinationSessionId,
        ulong eventSequence,
        ulong eventTimestamp,
        string sourceClockDomain,
        SharedMRParticipantId sourceParticipantId,
        CanonicalBottleIdentity targetBottleKey)
    {
        CoordinationSessionId = coordinationSessionId;
        EventSequence = eventSequence;
        EventTimestamp = eventTimestamp;
        SourceClockDomain = sourceClockDomain;
        SourceParticipantId = sourceParticipantId;
        TargetBottleKey = targetBottleKey;
    }
}

public readonly struct ReadyAcknowledgement
{
    public string CoordinationSessionId { get; }
    public ulong EventSequence { get; }
    public ulong EventTimestamp { get; }
    public string SourceClockDomain { get; }
    public SharedMRParticipantId SourceParticipantId { get; }
    public CanonicalBottleIdentity TargetBottleKey { get; }

    public ReadyAcknowledgement(
        string coordinationSessionId,
        ulong eventSequence,
        ulong eventTimestamp,
        string sourceClockDomain,
        SharedMRParticipantId sourceParticipantId,
        CanonicalBottleIdentity targetBottleKey)
    {
        CoordinationSessionId = coordinationSessionId;
        EventSequence = eventSequence;
        EventTimestamp = eventTimestamp;
        SourceClockDomain = sourceClockDomain;
        SourceParticipantId = sourceParticipantId;
        TargetBottleKey = targetBottleKey;
    }
}

/// <summary>
/// Validates and latches one request and one ready acknowledgement for one immutable session.
/// Event timestamps are retained as opaque values; this contract applies no timeout or clock conversion.
/// </summary>
public sealed class ManualHandoverEventLatch
{
    private readonly ManualHandoverSession session;
    private bool hasSequence;
    private ulong lastSequence;

    public ManualHandoverSession Session => session;
    public bool HasCoordinationRequest { get; private set; }
    public bool HasReadyAcknowledgement { get; private set; }
    public ManualCoordinationRequest CoordinationRequest { get; private set; }
    public ReadyAcknowledgement ReadyAcknowledgement { get; private set; }

    public ManualHandoverEventLatch(ManualHandoverSession session)
    {
        if (!session.IsValid)
            throw new ArgumentException("A valid manual handover session is required.", nameof(session));
        this.session = session;
        lastSequence = session.CreatedSequence;
        hasSequence = true;
    }

    public ManualHandoverValidationResult TryAccept(ManualCoordinationRequest request)
    {
        ManualHandoverValidationResult common = ValidateCommon(
            request.CoordinationSessionId,
            request.EventSequence,
            request.TargetBottleKey,
            request.SourceClockDomain);
        if (common != ManualHandoverValidationResult.Accepted) return common;
        if (request.SourceParticipantId != session.RoleBinding.GiverParticipantId)
            return ManualHandoverValidationResult.SourceNotGiver;
        if (HasCoordinationRequest)
            return ManualHandoverValidationResult.RequestAlreadyLatched;

        CoordinationRequest = request;
        HasCoordinationRequest = true;
        CommitSequence(request.EventSequence);
        return ManualHandoverValidationResult.Accepted;
    }

    public ManualHandoverValidationResult TryAccept(ReadyAcknowledgement acknowledgement)
    {
        ManualHandoverValidationResult common = ValidateCommon(
            acknowledgement.CoordinationSessionId,
            acknowledgement.EventSequence,
            acknowledgement.TargetBottleKey,
            acknowledgement.SourceClockDomain);
        if (common != ManualHandoverValidationResult.Accepted) return common;
        if (acknowledgement.SourceParticipantId != session.RoleBinding.ReceiverParticipantId)
            return ManualHandoverValidationResult.SourceNotReceiver;
        if (!HasCoordinationRequest)
            return ManualHandoverValidationResult.RequestRequired;
        if (HasReadyAcknowledgement)
            return ManualHandoverValidationResult.ReadyAlreadyLatched;

        ReadyAcknowledgement = acknowledgement;
        HasReadyAcknowledgement = true;
        CommitSequence(acknowledgement.EventSequence);
        return ManualHandoverValidationResult.Accepted;
    }

    private ManualHandoverValidationResult ValidateCommon(
        string coordinationSessionId,
        ulong eventSequence,
        CanonicalBottleIdentity targetBottleKey,
        string sourceClockDomain)
    {
        if (!string.Equals(
                coordinationSessionId,
                session.CoordinationSessionId,
                StringComparison.Ordinal))
            return ManualHandoverValidationResult.SessionMismatch;
        if (!targetBottleKey.IsValid)
            return ManualHandoverValidationResult.InvalidCanonicalTarget;
        if (string.IsNullOrWhiteSpace(sourceClockDomain))
            return ManualHandoverValidationResult.InvalidSourceClockDomain;
        if (targetBottleKey != session.TargetBottleKey)
            return ManualHandoverValidationResult.TargetMismatch;
        if (hasSequence && eventSequence == lastSequence)
            return ManualHandoverValidationResult.DuplicateSequence;
        if (hasSequence && eventSequence < lastSequence)
            return ManualHandoverValidationResult.OlderSequence;
        return ManualHandoverValidationResult.Accepted;
    }

    private void CommitSequence(ulong sequence)
    {
        lastSequence = sequence;
        hasSequence = true;
    }
}

/// <summary>
/// Logical ownership only. It is independent of Photon StateAuthority and InputAuthority.
/// Both-hold during Transfer retains the giver robot as logical owner.
/// </summary>
public static class ManualHandoverOwnershipSemantics
{
    public static bool TryGetExpectedRobotOwner(
        TaskPhase phase,
        ManualHandoverRoleBinding roles,
        out int robotId)
    {
        robotId = -1;
        if (!roles.IsValid || !CoordinationStateMachine.IsValidPhase(phase)) return false;
        robotId = phase == TaskPhase.Released
            ? roles.ReceiverRobotId
            : roles.GiverRobotId;
        return true;
    }
}
