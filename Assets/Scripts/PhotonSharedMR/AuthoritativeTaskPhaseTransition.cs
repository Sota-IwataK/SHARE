public enum AuthoritativeTransitionRejectionKind
{
    None = 0,
    RunnerUnavailable = 1,
    NoStateAuthority = 2,
    InvalidCurrentPhase = 3,
    InvalidRequestedPhase = 4,
    FsmRejected = 5
}

public readonly struct AuthoritativeTransitionRejection
{
    public AuthoritativeTransitionRejectionKind Kind { get; }
    public CoordinationTransitionRejectionReason FsmReason { get; }

    public AuthoritativeTransitionRejection(
        AuthoritativeTransitionRejectionKind kind,
        CoordinationTransitionRejectionReason fsmReason = CoordinationTransitionRejectionReason.None)
    {
        Kind = kind;
        FsmReason = fsmReason;
    }
}

/// <summary>Pure gate used by the Photon State Authority before committing a Networked phase.</summary>
public static class AuthoritativeTaskPhaseTransition
{
    public static bool TryEvaluate(
        bool runnerAvailable,
        bool hasStateAuthority,
        int currentNetworkValue,
        TaskPhase requested,
        CoordinationInputSnapshot inputs,
        out int nextNetworkValue,
        out AuthoritativeTransitionRejection rejection)
    {
        nextNetworkValue = currentNetworkValue;

        if (!runnerAvailable)
        {
            rejection = new AuthoritativeTransitionRejection(
                AuthoritativeTransitionRejectionKind.RunnerUnavailable);
            return false;
        }

        if (!hasStateAuthority)
        {
            rejection = new AuthoritativeTransitionRejection(
                AuthoritativeTransitionRejectionKind.NoStateAuthority);
            return false;
        }

        if (!CoordinationStateMachine.TryParsePhase(currentNetworkValue, out TaskPhase current))
        {
            rejection = new AuthoritativeTransitionRejection(
                AuthoritativeTransitionRejectionKind.InvalidCurrentPhase,
                CoordinationTransitionRejectionReason.InvalidCurrentPhase);
            return false;
        }

        if (!CoordinationStateMachine.IsValidPhase(requested))
        {
            rejection = new AuthoritativeTransitionRejection(
                AuthoritativeTransitionRejectionKind.InvalidRequestedPhase,
                CoordinationTransitionRejectionReason.InvalidRequestedPhase);
            return false;
        }

        if (!CoordinationStateMachine.TryTransition(
                current, requested, inputs, out TaskPhase next,
                out CoordinationTransitionRejectionReason fsmReason))
        {
            rejection = new AuthoritativeTransitionRejection(
                AuthoritativeTransitionRejectionKind.FsmRejected, fsmReason);
            return false;
        }

        rejection = new AuthoritativeTransitionRejection(
            AuthoritativeTransitionRejectionKind.None);
        nextNetworkValue = (int)next;
        return true;
    }
}
