#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;

public sealed class AuthoritativeTaskPhaseTransitionTests
{
    private static CoordinationInputSnapshot ValidInputs()
    {
        return new CoordinationInputSnapshot(
            coordinationRequested: true,
            readyConditionMet: true,
            transferConditionMet: true,
            releaseConditionMet: true,
            completionConditionMet: true,
            participantsConnected: true,
            inputsFresh: true,
            ownershipConsistent: true);
    }

    [TestCase(TaskPhase.Independent, TaskPhase.Preparing)]
    [TestCase(TaskPhase.Preparing, TaskPhase.Ready)]
    [TestCase(TaskPhase.Ready, TaskPhase.Transfer)]
    [TestCase(TaskPhase.Transfer, TaskPhase.Released)]
    [TestCase(TaskPhase.Released, TaskPhase.Independent)]
    public void ValidTransitionProducesCommitPlan(TaskPhase current, TaskPhase requested)
    {
        Assert.IsTrue(AuthoritativeTaskPhaseTransition.TryEvaluate(
            true, true, (int)current, requested, ValidInputs(), out int nextNetworkValue,
            out AuthoritativeTransitionRejection rejection));
        Assert.AreEqual((int)requested, nextNetworkValue);
        Assert.AreEqual(AuthoritativeTransitionRejectionKind.None, rejection.Kind);
        Assert.AreEqual(CoordinationTransitionRejectionReason.None, rejection.FsmReason);
    }

    [Test]
    public void IllegalTransitionDoesNotCommitVersionedState()
    {
        AssertRejectedStateIsUnchanged(
            TaskPhase.Independent, TaskPhase.Transfer, ValidInputs(), true, true,
            AuthoritativeTransitionRejectionKind.FsmRejected,
            CoordinationTransitionRejectionReason.IllegalTransition);
    }

    [Test]
    public void GuardFailureDoesNotCommitVersionedState()
    {
        CoordinationInputSnapshot stale = new CoordinationInputSnapshot(
            true, true, true, true, true, true, false, true);
        AssertRejectedStateIsUnchanged(
            TaskPhase.Independent, TaskPhase.Preparing, stale, true, true,
            AuthoritativeTransitionRejectionKind.FsmRejected,
            CoordinationTransitionRejectionReason.InputStale);
    }

    [Test]
    public void NoOpDoesNotCommitVersionedState()
    {
        AssertRejectedStateIsUnchanged(
            TaskPhase.Ready, TaskPhase.Ready, ValidInputs(), true, true,
            AuthoritativeTransitionRejectionKind.FsmRejected,
            CoordinationTransitionRejectionReason.NoOp);
    }

    [Test]
    public void InvalidCurrentNetworkValueFailsClosed()
    {
        AssertRejectedRawStateIsUnchanged(
            99, TaskPhase.Independent, true, true,
            AuthoritativeTransitionRejectionKind.InvalidCurrentPhase,
            CoordinationTransitionRejectionReason.InvalidCurrentPhase);
    }

    [Test]
    public void InvalidRequestedValueFailsClosed()
    {
        AssertRejectedStateIsUnchanged(
            TaskPhase.Independent, (TaskPhase)99, ValidInputs(), true, true,
            AuthoritativeTransitionRejectionKind.InvalidRequestedPhase,
            CoordinationTransitionRejectionReason.InvalidRequestedPhase);
    }

    [Test]
    public void NonStateAuthorityFailsClosed()
    {
        AssertRejectedStateIsUnchanged(
            TaskPhase.Independent, TaskPhase.Preparing, ValidInputs(), true, false,
            AuthoritativeTransitionRejectionKind.NoStateAuthority,
            CoordinationTransitionRejectionReason.None);
    }

    [Test]
    public void RunnerUnavailableFailsClosed()
    {
        AssertRejectedStateIsUnchanged(
            TaskPhase.Independent, TaskPhase.Preparing, ValidInputs(), false, false,
            AuthoritativeTransitionRejectionKind.RunnerUnavailable,
            CoordinationTransitionRejectionReason.None);
    }

    [Test]
    public void ArbitraryTaskPhaseSetterIsNotExposed()
    {
        MethodInfo setter = typeof(SharedTeamControlStateNetwork).GetMethod(
            "TrySetTaskPhase",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNull(setter);
    }

    [Test]
    public void TaskPhaseWireValuesRemainFrozen()
    {
        Assert.AreEqual(0, (int)TaskPhase.Independent);
        Assert.AreEqual(1, (int)TaskPhase.Preparing);
        Assert.AreEqual(2, (int)TaskPhase.Ready);
        Assert.AreEqual(3, (int)TaskPhase.Transfer);
        Assert.AreEqual(4, (int)TaskPhase.Released);
    }

    private static void AssertRejectedStateIsUnchanged(
        TaskPhase current,
        TaskPhase requested,
        CoordinationInputSnapshot inputs,
        bool runnerAvailable,
        bool hasStateAuthority,
        AuthoritativeTransitionRejectionKind expectedKind,
        CoordinationTransitionRejectionReason expectedFsmReason)
    {
        AssertRejectedRawStateIsUnchanged(
            (int)current, requested, runnerAvailable, hasStateAuthority,
            expectedKind, expectedFsmReason, inputs);
    }

    private static void AssertRejectedRawStateIsUnchanged(
        int currentNetworkValue,
        TaskPhase requested,
        bool runnerAvailable,
        bool hasStateAuthority,
        AuthoritativeTransitionRejectionKind expectedKind,
        CoordinationTransitionRejectionReason expectedFsmReason,
        CoordinationInputSnapshot? customInputs = null)
    {
        int phase = currentNetworkValue;
        int sequence = 41;
        long timestamp = 123456789L;

        bool accepted = AuthoritativeTaskPhaseTransition.TryEvaluate(
            runnerAvailable,
            hasStateAuthority,
            phase,
            requested,
            customInputs ?? ValidInputs(),
            out int nextNetworkValue,
            out AuthoritativeTransitionRejection rejection);
        if (accepted)
        {
            phase = nextNetworkValue;
            sequence++;
            timestamp++;
        }

        Assert.IsFalse(accepted);
        Assert.AreEqual(currentNetworkValue, phase);
        Assert.AreEqual(41, sequence);
        Assert.AreEqual(123456789L, timestamp);
        Assert.AreEqual(expectedKind, rejection.Kind);
        Assert.AreEqual(expectedFsmReason, rejection.FsmReason);
    }
}
#endif
