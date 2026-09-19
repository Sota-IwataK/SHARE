using System;
using System.Collections.Generic;

public enum ManualHandoverSessionLifecycleState
{
    NoSession = 0,
    Active = 1,
    Retired = 2
}

public enum ManualHandoverSessionLifecycleResult
{
    Accepted = 0,
    InvalidSession = 1,
    InvalidSnapshot = 2,
    ActiveSessionExists = 3,
    SessionIdAlreadyUsed = 4,
    NoActiveSession = 5,
    SessionAlreadyRetired = 6,
    RetiredSessionLedgerCapacityExceeded = 7
}

/// <summary>
/// Immutable lifecycle snapshot. A retired snapshot deliberately retains the final transport
/// state so a new State Authority can reconstruct retirement without private process state.
/// </summary>
public readonly struct ManualHandoverSessionLifecycleSnapshot
{
    private readonly string[] retiredSessionIds;

    public ManualHandoverSessionLifecycleState State { get; }
    public bool HasRetainedSession { get; }
    public ManualHandoverTransportSnapshot RetainedSession { get; }
    public IReadOnlyList<string> RetiredSessionIds => retiredSessionIds ?? Array.Empty<string>();

    public ManualHandoverSessionLifecycleSnapshot(
        ManualHandoverSessionLifecycleState state,
        bool hasRetainedSession,
        ManualHandoverTransportSnapshot retainedSession,
        IEnumerable<string> retiredSessionIds)
    {
        State = state;
        HasRetainedSession = hasRetainedSession;
        RetainedSession = retainedSession;
        this.retiredSessionIds = retiredSessionIds == null
            ? Array.Empty<string>()
            : new List<string>(retiredSessionIds).ToArray();
    }
}

/// <summary>
/// Pure session-lifecycle reducer. Request/Ready acceptance is delegated unchanged to the
/// frozen transport contract after the lifecycle has admitted the event.
/// </summary>
public sealed class ManualHandoverSessionLifecycleContract
{
    private readonly List<string> retiredSessionIds = new List<string>();
    private readonly HashSet<string> retiredSessionIdSet =
        new HashSet<string>(StringComparer.Ordinal);
    private ManualHandoverTransportSnapshot retainedSession;

    public ManualHandoverSessionLifecycleState State { get; private set; }
    public bool HasRetainedSession { get; private set; }
    public ManualHandoverTransportSnapshot RetainedSession => retainedSession;
    public int RetiredSessionCount => retiredSessionIds.Count;
    public IReadOnlyList<string> RetiredSessionIds => retiredSessionIds;

    public ManualHandoverSessionLifecycleContract()
    {
        State = ManualHandoverSessionLifecycleState.NoSession;
    }

    public static bool TryRestore(
        ManualHandoverSessionLifecycleSnapshot snapshot,
        out ManualHandoverSessionLifecycleContract lifecycle)
    {
        lifecycle = new ManualHandoverSessionLifecycleContract();
        if (!IsKnownState(snapshot.State))
        {
            lifecycle = null;
            return false;
        }

        IReadOnlyList<string> retiredIds = snapshot.RetiredSessionIds;
        for (int i = 0; i < retiredIds.Count; i++)
        {
            string sessionId = retiredIds[i];
            if (string.IsNullOrWhiteSpace(sessionId)
                || !lifecycle.retiredSessionIdSet.Add(sessionId))
            {
                lifecycle = null;
                return false;
            }
            lifecycle.retiredSessionIds.Add(sessionId);
        }

        switch (snapshot.State)
        {
            case ManualHandoverSessionLifecycleState.NoSession:
                if (snapshot.HasRetainedSession || retiredIds.Count != 0)
                {
                    lifecycle = null;
                    return false;
                }
                break;
            case ManualHandoverSessionLifecycleState.Active:
                if (!snapshot.HasRetainedSession
                    || !snapshot.RetainedSession.IsValid
                    || lifecycle.retiredSessionIdSet.Contains(
                        snapshot.RetainedSession.Session.CoordinationSessionId))
                {
                    lifecycle = null;
                    return false;
                }
                lifecycle.HasRetainedSession = true;
                lifecycle.retainedSession = snapshot.RetainedSession;
                break;
            case ManualHandoverSessionLifecycleState.Retired:
                if (!snapshot.HasRetainedSession
                    || !snapshot.RetainedSession.IsValid
                    || !lifecycle.retiredSessionIdSet.Contains(
                        snapshot.RetainedSession.Session.CoordinationSessionId))
                {
                    lifecycle = null;
                    return false;
                }
                lifecycle.HasRetainedSession = true;
                lifecycle.retainedSession = snapshot.RetainedSession;
                break;
        }

        lifecycle.State = snapshot.State;
        return true;
    }

    public ManualHandoverSessionLifecycleSnapshot CaptureSnapshot()
    {
        return new ManualHandoverSessionLifecycleSnapshot(
            State, HasRetainedSession, retainedSession, retiredSessionIds);
    }

    public ManualHandoverSessionLifecycleResult TryActivate(
        ManualHandoverSession session,
        long authorityTimestamp)
    {
        return TryActivate(session, authorityTimestamp, int.MaxValue);
    }

    public ManualHandoverSessionLifecycleResult TryActivate(
        ManualHandoverSession session,
        long authorityTimestamp,
        int retiredSessionCapacity)
    {
        if (!session.IsValid)
            return ManualHandoverSessionLifecycleResult.InvalidSession;
        if (State == ManualHandoverSessionLifecycleState.Active)
            return ManualHandoverSessionLifecycleResult.ActiveSessionExists;
        if (retiredSessionIdSet.Contains(session.CoordinationSessionId))
            return ManualHandoverSessionLifecycleResult.SessionIdAlreadyUsed;
        if (State == ManualHandoverSessionLifecycleState.Retired
            && retiredSessionIds.Count >= retiredSessionCapacity)
            return ManualHandoverSessionLifecycleResult.RetiredSessionLedgerCapacityExceeded;
        if (!ManualHandoverTransportContract.TryCreateInitial(
                session, authorityTimestamp, out ManualHandoverTransportSnapshot initial))
            return ManualHandoverSessionLifecycleResult.InvalidSession;

        retainedSession = initial;
        HasRetainedSession = true;
        State = ManualHandoverSessionLifecycleState.Active;
        return ManualHandoverSessionLifecycleResult.Accepted;
    }

    public ManualHandoverSessionLifecycleResult TryRetire()
    {
        if (State == ManualHandoverSessionLifecycleState.NoSession)
            return ManualHandoverSessionLifecycleResult.NoActiveSession;
        if (State == ManualHandoverSessionLifecycleState.Retired)
            return ManualHandoverSessionLifecycleResult.SessionAlreadyRetired;
        if (!HasRetainedSession || !retainedSession.IsValid)
            return ManualHandoverSessionLifecycleResult.InvalidSnapshot;

        string sessionId = retainedSession.Session.CoordinationSessionId;
        if (!retiredSessionIdSet.Add(sessionId))
            return ManualHandoverSessionLifecycleResult.SessionIdAlreadyUsed;
        retiredSessionIds.Add(sessionId);
        State = ManualHandoverSessionLifecycleState.Retired;
        return ManualHandoverSessionLifecycleResult.Accepted;
    }

    public ManualHandoverValidationResult TryAcceptRequest(
        SharedMRParticipantId actualSender,
        ManualCoordinationRequest request,
        long authorityTimestamp)
    {
        ManualHandoverValidationResult lifecycleResult = ValidateEventState();
        if (lifecycleResult != ManualHandoverValidationResult.Accepted)
            return lifecycleResult;

        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptRequest(
            retainedSession, actualSender, request, authorityTimestamp,
            out ManualHandoverTransportSnapshot next);
        if (result == ManualHandoverValidationResult.Accepted)
            retainedSession = next;
        return result;
    }

    public ManualHandoverValidationResult TryAcceptReady(
        SharedMRParticipantId actualSender,
        ReadyAcknowledgement ready,
        long authorityTimestamp)
    {
        ManualHandoverValidationResult lifecycleResult = ValidateEventState();
        if (lifecycleResult != ManualHandoverValidationResult.Accepted)
            return lifecycleResult;

        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptReady(
            retainedSession, actualSender, ready, authorityTimestamp,
            out ManualHandoverTransportSnapshot next);
        if (result == ManualHandoverValidationResult.Accepted)
            retainedSession = next;
        return result;
    }

    private ManualHandoverValidationResult ValidateEventState()
    {
        if (State == ManualHandoverSessionLifecycleState.Retired)
            return ManualHandoverValidationResult.SessionRetired;
        return State == ManualHandoverSessionLifecycleState.Active
            && HasRetainedSession
            && retainedSession.IsValid
                ? ManualHandoverValidationResult.Accepted
                : ManualHandoverValidationResult.AuthorityStateInvalid;
    }

    private static bool IsKnownState(ManualHandoverSessionLifecycleState state)
    {
        return state == ManualHandoverSessionLifecycleState.NoSession
            || state == ManualHandoverSessionLifecycleState.Active
            || state == ManualHandoverSessionLifecycleState.Retired;
    }
}
