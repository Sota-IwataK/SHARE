/// <summary>Normalized semantic inputs used by the pure coordination FSM.</summary>
public readonly struct CoordinationInputSnapshot
{
    public bool CoordinationRequested { get; }
    public bool ReadyConditionMet { get; }
    public bool TransferConditionMet { get; }
    public bool ReleaseConditionMet { get; }
    public bool CompletionConditionMet { get; }
    public bool ParticipantsConnected { get; }
    public bool InputsFresh { get; }
    public bool OwnershipConsistent { get; }

    public CoordinationInputSnapshot(
        bool coordinationRequested,
        bool readyConditionMet,
        bool transferConditionMet,
        bool releaseConditionMet,
        bool completionConditionMet,
        bool participantsConnected,
        bool inputsFresh,
        bool ownershipConsistent)
    {
        CoordinationRequested = coordinationRequested;
        ReadyConditionMet = readyConditionMet;
        TransferConditionMet = transferConditionMet;
        ReleaseConditionMet = releaseConditionMet;
        CompletionConditionMet = completionConditionMet;
        ParticipantsConnected = participantsConnected;
        InputsFresh = inputsFresh;
        OwnershipConsistent = ownershipConsistent;
    }
}

public enum CoordinationTransitionRejectionReason
{
    None = 0,
    NoOp = 1,
    InvalidCurrentPhase = 2,
    InvalidRequestedPhase = 3,
    IllegalTransition = 4,
    ParticipantDisconnected = 5,
    InputStale = 6,
    CoordinationNotRequested = 7,
    ReadyConditionNotMet = 8,
    TransferConditionNotMet = 9,
    ReleaseConditionNotMet = 10,
    CompletionConditionNotMet = 11,
    OwnershipInconsistent = 12
}

/// <summary>
/// Pure, deterministic Coordination FSM contract. It has no clock, transport, or command side effects.
/// </summary>
public static class CoordinationStateMachine
{
    public static bool TryTransition(
        TaskPhase current,
        TaskPhase requested,
        CoordinationInputSnapshot inputs,
        out TaskPhase next,
        out CoordinationTransitionRejectionReason reason)
    {
        next = current;
        reason = CoordinationTransitionRejectionReason.None;

        if (!IsValidPhase(current))
        {
            reason = CoordinationTransitionRejectionReason.InvalidCurrentPhase;
            return false;
        }

        if (!IsValidPhase(requested))
        {
            reason = CoordinationTransitionRejectionReason.InvalidRequestedPhase;
            return false;
        }

        if (current == requested)
        {
            reason = CoordinationTransitionRejectionReason.NoOp;
            return false;
        }

        if (!IsLegalTransition(current, requested))
        {
            reason = CoordinationTransitionRejectionReason.IllegalTransition;
            return false;
        }

        CoordinationTransitionRejectionReason guardFailure = EvaluateGuards(current, inputs);
        if (guardFailure != CoordinationTransitionRejectionReason.None)
        {
            reason = guardFailure;
            return false;
        }

        next = requested;
        return true;
    }

    public static bool IsValidPhase(TaskPhase phase)
    {
        switch (phase)
        {
            case TaskPhase.Independent:
            case TaskPhase.Preparing:
            case TaskPhase.Ready:
            case TaskPhase.Transfer:
            case TaskPhase.Released:
                return true;
            default:
                return false;
        }
    }

    public static bool TryParsePhase(int networkValue, out TaskPhase phase)
    {
        switch (networkValue)
        {
            case (int)TaskPhase.Independent:
                phase = TaskPhase.Independent;
                return true;
            case (int)TaskPhase.Preparing:
                phase = TaskPhase.Preparing;
                return true;
            case (int)TaskPhase.Ready:
                phase = TaskPhase.Ready;
                return true;
            case (int)TaskPhase.Transfer:
                phase = TaskPhase.Transfer;
                return true;
            case (int)TaskPhase.Released:
                phase = TaskPhase.Released;
                return true;
            default:
                phase = default;
                return false;
        }
    }

    private static bool IsLegalTransition(TaskPhase current, TaskPhase requested)
    {
        return (current == TaskPhase.Independent && requested == TaskPhase.Preparing)
            || (current == TaskPhase.Preparing && requested == TaskPhase.Ready)
            || (current == TaskPhase.Ready && requested == TaskPhase.Transfer)
            || (current == TaskPhase.Transfer && requested == TaskPhase.Released)
            || (current == TaskPhase.Released && requested == TaskPhase.Independent);
    }

    private static CoordinationTransitionRejectionReason EvaluateGuards(
        TaskPhase current,
        CoordinationInputSnapshot inputs)
    {
        if (current == TaskPhase.Released)
        {
            return inputs.CompletionConditionMet
                ? CoordinationTransitionRejectionReason.None
                : CoordinationTransitionRejectionReason.CompletionConditionNotMet;
        }

        if (!inputs.ParticipantsConnected)
            return CoordinationTransitionRejectionReason.ParticipantDisconnected;
        if (!inputs.InputsFresh)
            return CoordinationTransitionRejectionReason.InputStale;

        switch (current)
        {
            case TaskPhase.Independent:
                return inputs.CoordinationRequested
                    ? CoordinationTransitionRejectionReason.None
                    : CoordinationTransitionRejectionReason.CoordinationNotRequested;
            case TaskPhase.Preparing:
                return inputs.ReadyConditionMet
                    ? CoordinationTransitionRejectionReason.None
                    : CoordinationTransitionRejectionReason.ReadyConditionNotMet;
            case TaskPhase.Ready:
                if (!inputs.OwnershipConsistent)
                    return CoordinationTransitionRejectionReason.OwnershipInconsistent;
                return inputs.TransferConditionMet
                    ? CoordinationTransitionRejectionReason.None
                    : CoordinationTransitionRejectionReason.TransferConditionNotMet;
            case TaskPhase.Transfer:
                return inputs.ReleaseConditionMet
                    ? CoordinationTransitionRejectionReason.None
                    : CoordinationTransitionRejectionReason.ReleaseConditionNotMet;
            default:
                return CoordinationTransitionRejectionReason.InvalidCurrentPhase;
        }
    }
}
