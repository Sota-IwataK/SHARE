using System;

public enum ManualHandoverEventType
{
    Session = 0,
    CoordinationRequest = 1,
    ReadyAcknowledgement = 2
}

public readonly struct ManualHandoverTransportSnapshot
{
    public ManualHandoverSession Session { get; }
    public bool RequestAccepted { get; }
    public ManualCoordinationRequest Request { get; }
    public bool ReadyAccepted { get; }
    public ReadyAcknowledgement Ready { get; }
    public ulong AuthorityAcceptedSequence { get; }
    public long AuthorityAcceptedTimestamp { get; }
    public string AuthorityClockDomain { get; }

    public ManualHandoverTransportSnapshot(
        ManualHandoverSession session,
        bool requestAccepted,
        ManualCoordinationRequest request,
        bool readyAccepted,
        ReadyAcknowledgement ready,
        ulong authorityAcceptedSequence,
        long authorityAcceptedTimestamp,
        string authorityClockDomain)
    {
        Session = session;
        RequestAccepted = requestAccepted;
        Request = request;
        ReadyAccepted = readyAccepted;
        Ready = ready;
        AuthorityAcceptedSequence = authorityAcceptedSequence;
        AuthorityAcceptedTimestamp = authorityAcceptedTimestamp;
        AuthorityClockDomain = authorityClockDomain;
    }

    public bool IsValid => Session.IsValid
        && !string.IsNullOrWhiteSpace(AuthorityClockDomain)
        && (!ReadyAccepted || RequestAccepted);
}

public readonly struct ManualHandoverEventDiagnostic
{
    public ManualHandoverEventType EventType { get; }
    public string CoordinationSessionId { get; }
    public SharedMRParticipantId SourceParticipantId { get; }
    public CanonicalBottleIdentity TargetBottleKey { get; }
    public ulong SourceSequence { get; }
    public ulong SourceTimestamp { get; }
    public string SourceClockDomain { get; }
    public ulong AuthoritySequence { get; }
    public long AuthorityTimestamp { get; }
    public string AuthorityClockDomain { get; }
    public ManualHandoverValidationResult Result { get; }

    public ManualHandoverEventDiagnostic(
        ManualHandoverEventType eventType,
        string coordinationSessionId,
        SharedMRParticipantId sourceParticipantId,
        CanonicalBottleIdentity targetBottleKey,
        ulong sourceSequence,
        ulong sourceTimestamp,
        string sourceClockDomain,
        ulong authoritySequence,
        long authorityTimestamp,
        string authorityClockDomain,
        ManualHandoverValidationResult result)
    {
        EventType = eventType;
        CoordinationSessionId = coordinationSessionId;
        SourceParticipantId = sourceParticipantId;
        TargetBottleKey = targetBottleKey;
        SourceSequence = sourceSequence;
        SourceTimestamp = sourceTimestamp;
        SourceClockDomain = sourceClockDomain;
        AuthoritySequence = authoritySequence;
        AuthorityTimestamp = authorityTimestamp;
        AuthorityClockDomain = authorityClockDomain;
        Result = result;
    }
}

/// <summary>Pure authority-side validation and replay-safe snapshot reducer.</summary>
public static class ManualHandoverTransportContract
{
    public const int CoordinationSessionCapacity = 128;
    public const int SourceClockDomainCapacity = 64;
    public const string AuthorityClockDomain = "fusion.simulation_time_ms";

    public static bool TryCreateInitial(
        ManualHandoverSession session,
        long authorityTimestamp,
        out ManualHandoverTransportSnapshot snapshot)
    {
        snapshot = default;
        if (!session.IsValid || !FitsSession(session)) return false;
        snapshot = new ManualHandoverTransportSnapshot(
            session, false, default, false, default, 0UL,
            authorityTimestamp, AuthorityClockDomain);
        return true;
    }

    public static ManualHandoverValidationResult TryAcceptRequest(
        ManualHandoverTransportSnapshot current,
        SharedMRParticipantId actualSender,
        ManualCoordinationRequest request,
        long authorityTimestamp,
        out ManualHandoverTransportSnapshot next)
    {
        next = current;
        if (!current.IsValid) return ManualHandoverValidationResult.AuthorityStateInvalid;
        if (actualSender != request.SourceParticipantId)
            return ManualHandoverValidationResult.RpcSenderMismatch;
        if (!FitsEvent(request.CoordinationSessionId, request.SourceClockDomain, request.TargetBottleKey))
            return ManualHandoverValidationResult.TransportCapacityExceeded;
        if (!TryRestoreLatch(current, out ManualHandoverEventLatch latch))
            return ManualHandoverValidationResult.AuthorityStateInvalid;
        ManualHandoverValidationResult result = latch.TryAccept(request);
        if (result != ManualHandoverValidationResult.Accepted) return result;
        if (current.AuthorityAcceptedSequence == ulong.MaxValue)
            return ManualHandoverValidationResult.AuthoritySequenceExhausted;
        next = new ManualHandoverTransportSnapshot(
            current.Session, true, request, current.ReadyAccepted, current.Ready,
            current.AuthorityAcceptedSequence + 1UL,
            authorityTimestamp, AuthorityClockDomain);
        return ManualHandoverValidationResult.Accepted;
    }

    public static ManualHandoverValidationResult TryAcceptReady(
        ManualHandoverTransportSnapshot current,
        SharedMRParticipantId actualSender,
        ReadyAcknowledgement ready,
        long authorityTimestamp,
        out ManualHandoverTransportSnapshot next)
    {
        next = current;
        if (!current.IsValid) return ManualHandoverValidationResult.AuthorityStateInvalid;
        if (actualSender != ready.SourceParticipantId)
            return ManualHandoverValidationResult.RpcSenderMismatch;
        if (!FitsEvent(ready.CoordinationSessionId, ready.SourceClockDomain, ready.TargetBottleKey))
            return ManualHandoverValidationResult.TransportCapacityExceeded;
        if (!TryRestoreLatch(current, out ManualHandoverEventLatch latch))
            return ManualHandoverValidationResult.AuthorityStateInvalid;
        ManualHandoverValidationResult result = latch.TryAccept(ready);
        if (result != ManualHandoverValidationResult.Accepted) return result;
        if (current.AuthorityAcceptedSequence == ulong.MaxValue)
            return ManualHandoverValidationResult.AuthoritySequenceExhausted;
        next = new ManualHandoverTransportSnapshot(
            current.Session, current.RequestAccepted, current.Request, true, ready,
            current.AuthorityAcceptedSequence + 1UL,
            authorityTimestamp, AuthorityClockDomain);
        return ManualHandoverValidationResult.Accepted;
    }

    private static bool TryRestoreLatch(
        ManualHandoverTransportSnapshot snapshot,
        out ManualHandoverEventLatch latch)
    {
        latch = new ManualHandoverEventLatch(snapshot.Session);
        if (snapshot.RequestAccepted
            && latch.TryAccept(snapshot.Request) != ManualHandoverValidationResult.Accepted)
            return false;
        if (snapshot.ReadyAccepted
            && latch.TryAccept(snapshot.Ready) != ManualHandoverValidationResult.Accepted)
            return false;
        return true;
    }

    private static bool FitsSession(ManualHandoverSession session)
    {
        return CanonicalBottlePhotonContract.Fits(
                session.CoordinationSessionId, CoordinationSessionCapacity)
            && FitsIdentity(session.TargetBottleKey);
    }

    private static bool FitsEvent(
        string coordinationSessionId,
        string clockDomain,
        CanonicalBottleIdentity identity)
    {
        return CanonicalBottlePhotonContract.Fits(
                coordinationSessionId, CoordinationSessionCapacity)
            && CanonicalBottlePhotonContract.Fits(clockDomain, SourceClockDomainCapacity)
            && FitsIdentity(identity);
    }

    private static bool FitsIdentity(CanonicalBottleIdentity identity)
    {
        return identity.IsValid
            && CanonicalBottlePhotonContract.Fits(
                identity.SourceId, CanonicalBottlePhotonContract.SourceIdCapacity)
            && CanonicalBottlePhotonContract.Fits(
                identity.SessionId, CanonicalBottlePhotonContract.SessionIdCapacity);
    }
}
