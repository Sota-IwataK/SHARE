#if UNITY_EDITOR
using NUnit.Framework;

public sealed class CoordinationStateMachineTests
{
    private static readonly TaskPhase[] Phases =
    {
        TaskPhase.Independent,
        TaskPhase.Preparing,
        TaskPhase.Ready,
        TaskPhase.Transfer,
        TaskPhase.Released
    };

    private static CoordinationInputSnapshot AllConditionsMet()
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

    [Test]
    public void TaskPhaseWireValuesMatchFrozenContract()
    {
        Assert.AreEqual(0, (int)TaskPhase.Independent);
        Assert.AreEqual(1, (int)TaskPhase.Preparing);
        Assert.AreEqual(2, (int)TaskPhase.Ready);
        Assert.AreEqual(3, (int)TaskPhase.Transfer);
        Assert.AreEqual(4, (int)TaskPhase.Released);
    }

    [TestCase(TaskPhase.Independent, TaskPhase.Preparing)]
    [TestCase(TaskPhase.Preparing, TaskPhase.Ready)]
    [TestCase(TaskPhase.Ready, TaskPhase.Transfer)]
    [TestCase(TaskPhase.Transfer, TaskPhase.Released)]
    [TestCase(TaskPhase.Released, TaskPhase.Independent)]
    public void LegalTransitionSucceeds(TaskPhase current, TaskPhase requested)
    {
        Assert.IsTrue(CoordinationStateMachine.TryTransition(
            current, requested, AllConditionsMet(), out TaskPhase next,
            out CoordinationTransitionRejectionReason reason));
        Assert.AreEqual(requested, next);
        Assert.AreEqual(CoordinationTransitionRejectionReason.None, reason);
    }

    [Test]
    public void EverySkippedOrReverseTransitionIsRejected()
    {
        CoordinationInputSnapshot inputs = AllConditionsMet();
        foreach (TaskPhase current in Phases)
        {
            foreach (TaskPhase requested in Phases)
            {
                if (current == requested || IsForwardEdge(current, requested)) continue;
                Assert.IsFalse(CoordinationStateMachine.TryTransition(
                    current, requested, inputs, out TaskPhase next,
                    out CoordinationTransitionRejectionReason reason),
                    current + " -> " + requested);
                Assert.AreEqual(current, next, current + " -> " + requested);
                Assert.AreEqual(CoordinationTransitionRejectionReason.IllegalTransition, reason,
                    current + " -> " + requested);
            }
        }
    }

    [TestCase(TaskPhase.Independent)]
    [TestCase(TaskPhase.Preparing)]
    [TestCase(TaskPhase.Ready)]
    [TestCase(TaskPhase.Transfer)]
    [TestCase(TaskPhase.Released)]
    public void SameStateRequestIsRejectedAsNoOp(TaskPhase phase)
    {
        Assert.IsFalse(CoordinationStateMachine.TryTransition(
            phase, phase, AllConditionsMet(), out TaskPhase next,
            out CoordinationTransitionRejectionReason reason));
        Assert.AreEqual(phase, next);
        Assert.AreEqual(CoordinationTransitionRejectionReason.NoOp, reason);
    }

    [Test]
    public void InvalidCurrentPhaseIsRejected()
    {
        TaskPhase invalid = (TaskPhase)(-1);
        Assert.IsFalse(CoordinationStateMachine.TryTransition(
            invalid, TaskPhase.Independent, AllConditionsMet(), out TaskPhase next,
            out CoordinationTransitionRejectionReason reason));
        Assert.AreEqual(invalid, next);
        Assert.AreEqual(CoordinationTransitionRejectionReason.InvalidCurrentPhase, reason);
    }

    [Test]
    public void InvalidRequestedPhaseIsRejected()
    {
        TaskPhase invalid = (TaskPhase)5;
        Assert.IsFalse(CoordinationStateMachine.TryTransition(
            TaskPhase.Independent, invalid, AllConditionsMet(), out TaskPhase next,
            out CoordinationTransitionRejectionReason reason));
        Assert.AreEqual(TaskPhase.Independent, next);
        Assert.AreEqual(CoordinationTransitionRejectionReason.InvalidRequestedPhase, reason);
    }

    [Test]
    public void NetworkValueParserRejectsValuesOutsideFrozenRange()
    {
        Assert.IsFalse(CoordinationStateMachine.TryParsePhase(-1, out _));
        Assert.IsFalse(CoordinationStateMachine.TryParsePhase(5, out _));
        Assert.IsFalse(CoordinationStateMachine.TryParsePhase(99, out _));
        for (int value = 0; value <= 4; value++)
        {
            Assert.IsTrue(CoordinationStateMachine.TryParsePhase(value, out TaskPhase phase));
            Assert.AreEqual(value, (int)phase);
        }
    }

    [Test]
    public void MissingCoordinationRequestFailsClosed()
    {
        AssertGuardFailure(TaskPhase.Independent, TaskPhase.Preparing,
            Snapshot(coordinationRequested: false),
            CoordinationTransitionRejectionReason.CoordinationNotRequested);
    }

    [TestCase(TaskPhase.Independent, TaskPhase.Preparing)]
    [TestCase(TaskPhase.Preparing, TaskPhase.Ready)]
    [TestCase(TaskPhase.Ready, TaskPhase.Transfer)]
    [TestCase(TaskPhase.Transfer, TaskPhase.Released)]
    public void DisconnectedParticipantFailsClosed(TaskPhase current, TaskPhase requested)
    {
        AssertGuardFailure(current, requested, Snapshot(participantsConnected: false),
            CoordinationTransitionRejectionReason.ParticipantDisconnected);
    }

    [TestCase(TaskPhase.Independent, TaskPhase.Preparing)]
    [TestCase(TaskPhase.Preparing, TaskPhase.Ready)]
    [TestCase(TaskPhase.Ready, TaskPhase.Transfer)]
    [TestCase(TaskPhase.Transfer, TaskPhase.Released)]
    public void StaleInputFailsClosed(TaskPhase current, TaskPhase requested)
    {
        AssertGuardFailure(current, requested, Snapshot(inputsFresh: false),
            CoordinationTransitionRejectionReason.InputStale);
    }

    [Test]
    public void MissingReadyConditionFailsClosed()
    {
        AssertGuardFailure(TaskPhase.Preparing, TaskPhase.Ready,
            Snapshot(readyConditionMet: false),
            CoordinationTransitionRejectionReason.ReadyConditionNotMet);
    }

    [Test]
    public void MissingTransferConditionFailsClosed()
    {
        AssertGuardFailure(TaskPhase.Ready, TaskPhase.Transfer,
            Snapshot(transferConditionMet: false),
            CoordinationTransitionRejectionReason.TransferConditionNotMet);
    }

    [Test]
    public void InconsistentOwnershipFailsClosed()
    {
        AssertGuardFailure(TaskPhase.Ready, TaskPhase.Transfer,
            Snapshot(ownershipConsistent: false),
            CoordinationTransitionRejectionReason.OwnershipInconsistent);
    }

    [Test]
    public void MissingReleaseConditionFailsClosed()
    {
        AssertGuardFailure(TaskPhase.Transfer, TaskPhase.Released,
            Snapshot(releaseConditionMet: false),
            CoordinationTransitionRejectionReason.ReleaseConditionNotMet);
    }

    [Test]
    public void MissingCompletionConditionFailsClosed()
    {
        AssertGuardFailure(TaskPhase.Released, TaskPhase.Independent,
            Snapshot(completionConditionMet: false),
            CoordinationTransitionRejectionReason.CompletionConditionNotMet);
    }

    [Test]
    public void EvaluationDoesNotMutateCurrentOrInputs()
    {
        TaskPhase current = TaskPhase.Ready;
        CoordinationInputSnapshot inputs = Snapshot(transferConditionMet: false);

        CoordinationStateMachine.TryTransition(
            current, TaskPhase.Transfer, inputs, out _, out _);

        Assert.AreEqual(TaskPhase.Ready, current);
        Assert.IsFalse(inputs.TransferConditionMet);
        Assert.IsTrue(inputs.ParticipantsConnected);
        Assert.IsTrue(inputs.InputsFresh);
        Assert.IsTrue(inputs.OwnershipConsistent);
    }

    private static CoordinationInputSnapshot Snapshot(
        bool coordinationRequested = true,
        bool readyConditionMet = true,
        bool transferConditionMet = true,
        bool releaseConditionMet = true,
        bool completionConditionMet = true,
        bool participantsConnected = true,
        bool inputsFresh = true,
        bool ownershipConsistent = true)
    {
        return new CoordinationInputSnapshot(
            coordinationRequested,
            readyConditionMet,
            transferConditionMet,
            releaseConditionMet,
            completionConditionMet,
            participantsConnected,
            inputsFresh,
            ownershipConsistent);
    }

    private static void AssertGuardFailure(
        TaskPhase current,
        TaskPhase requested,
        CoordinationInputSnapshot inputs,
        CoordinationTransitionRejectionReason expectedReason)
    {
        Assert.IsFalse(CoordinationStateMachine.TryTransition(
            current, requested, inputs, out TaskPhase next,
            out CoordinationTransitionRejectionReason reason));
        Assert.AreEqual(current, next);
        Assert.AreEqual(expectedReason, reason);
    }

    private static bool IsForwardEdge(TaskPhase current, TaskPhase requested)
    {
        return (current == TaskPhase.Independent && requested == TaskPhase.Preparing)
            || (current == TaskPhase.Preparing && requested == TaskPhase.Ready)
            || (current == TaskPhase.Ready && requested == TaskPhase.Transfer)
            || (current == TaskPhase.Transfer && requested == TaskPhase.Released)
            || (current == TaskPhase.Released && requested == TaskPhase.Independent);
    }
}
#endif
