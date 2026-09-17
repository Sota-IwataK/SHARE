#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public sealed class ManualHandoverContractTests
{
    private static readonly CanonicalBottleIdentity Target =
        new CanonicalBottleIdentity("tracker-a", "bottle-session-a", 42UL);

    private static ManualHandoverRoleBinding Roles()
    {
        Assert.IsTrue(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User2,
            10,
            SharedMRParticipantId.User1,
            20,
            out ManualHandoverRoleBinding roles));
        return roles;
    }

    private static ManualHandoverSession Session()
    {
        Assert.IsTrue(ManualHandoverSession.TryCreate(
            "coordination-session-a",
            Roles(),
            Target,
            0UL,
            1000UL,
            out ManualHandoverSession session));
        return session;
    }

    private static ManualCoordinationRequest Request(
        ulong sequence = 1UL,
        SharedMRParticipantId source = SharedMRParticipantId.User2,
        string session = "coordination-session-a",
        CanonicalBottleIdentity? target = null)
    {
        return new ManualCoordinationRequest(
            session,
            sequence,
            1100UL,
            "source.monotonic_ns",
            source,
            target ?? Target);
    }

    private static ReadyAcknowledgement Ready(
        ulong sequence = 2UL,
        SharedMRParticipantId source = SharedMRParticipantId.User1,
        string session = "coordination-session-a",
        CanonicalBottleIdentity? target = null)
    {
        return new ReadyAcknowledgement(
            session,
            sequence,
            1200UL,
            "source.monotonic_ns",
            source,
            target ?? Target);
    }

    [Test]
    public void ValidRoleBindingDoesNotHardCodeParticipantOrder()
    {
        ManualHandoverRoleBinding roles = Roles();
        Assert.AreEqual(SharedMRParticipantId.User2, roles.GiverParticipantId);
        Assert.AreEqual(SharedMRParticipantId.User1, roles.ReceiverParticipantId);
        Assert.AreEqual(10, roles.GiverRobotId);
        Assert.AreEqual(20, roles.ReceiverRobotId);
    }

    [Test]
    public void SameParticipantForBothRolesIsRejected()
    {
        Assert.IsFalse(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User1, 10,
            SharedMRParticipantId.User1, 20,
            out _));
    }

    [TestCase(SharedMRParticipantId.Unassigned, SharedMRParticipantId.User1)]
    [TestCase(SharedMRParticipantId.User1, SharedMRParticipantId.Unassigned)]
    public void UnassignedParticipantIsRejected(
        SharedMRParticipantId giver,
        SharedMRParticipantId receiver)
    {
        Assert.IsFalse(ManualHandoverRoleBinding.TryCreate(
            giver, 10, receiver, 20, out _));
    }

    [TestCase(-1, 20)]
    [TestCase(10, -1)]
    public void UnassignedRobotIsRejected(int giverRobot, int receiverRobot)
    {
        Assert.IsFalse(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User1, giverRobot,
            SharedMRParticipantId.User2, receiverRobot,
            out _));
    }

    [Test]
    public void SameRobotForBothRolesIsRejected()
    {
        Assert.IsFalse(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User1, 10,
            SharedMRParticipantId.User2, 10,
            out _));
    }

    [Test]
    public void CorrectRequestFromGiverIsAcceptedAndLatched()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.IsTrue(latch.HasCoordinationRequest);
        Assert.AreEqual(Target, latch.CoordinationRequest.TargetBottleKey);
    }

    [Test]
    public void RequestFromReceiverIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(
            ManualHandoverValidationResult.SourceNotGiver,
            latch.TryAccept(Request(source: SharedMRParticipantId.User1)));
        Assert.IsFalse(latch.HasCoordinationRequest);
    }

    [Test]
    public void CorrectReadyFromReceiverIsAcceptedAndLatched()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Ready()));
        Assert.IsTrue(latch.HasReadyAcknowledgement);
        Assert.AreEqual(Target, latch.ReadyAcknowledgement.TargetBottleKey);
    }

    [Test]
    public void ReadyFromGiverIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.AreEqual(
            ManualHandoverValidationResult.SourceNotReceiver,
            latch.TryAccept(Ready(source: SharedMRParticipantId.User2)));
        Assert.IsFalse(latch.HasReadyAcknowledgement);
    }

    [Test]
    public void ReadyBeforeRequestIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(
            ManualHandoverValidationResult.RequestRequired,
            latch.TryAccept(Ready(sequence: 1UL)));
    }

    [Test]
    public void WrongCoordinationSessionIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(
            ManualHandoverValidationResult.SessionMismatch,
            latch.TryAccept(Request(session: "coordination-session-b")));
    }

    [Test]
    public void ReadyWithWrongCoordinationSessionIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.AreEqual(
            ManualHandoverValidationResult.SessionMismatch,
            latch.TryAccept(Ready(session: "coordination-session-b")));
        Assert.IsFalse(latch.HasReadyAcknowledgement);
    }

    [Test]
    public void WrongFullTargetIdentityIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        CanonicalBottleIdentity wrong =
            new CanonicalBottleIdentity("tracker-b", "bottle-session-a", 42UL);
        Assert.AreEqual(
            ManualHandoverValidationResult.TargetMismatch,
            latch.TryAccept(Request(target: wrong)));
    }

    [Test]
    public void DifferentBottleSessionIdIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        CanonicalBottleIdentity wrongSession =
            new CanonicalBottleIdentity("tracker-a", "bottle-session-b", 42UL);
        Assert.AreEqual(
            ManualHandoverValidationResult.TargetMismatch,
            latch.TryAccept(Request(target: wrongSession)));
    }

    [Test]
    public void ReadyWithDifferentBottleSessionIdIsRejected()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        CanonicalBottleIdentity wrongSession =
            new CanonicalBottleIdentity("tracker-a", "bottle-session-b", 42UL);
        Assert.AreEqual(
            ManualHandoverValidationResult.TargetMismatch,
            latch.TryAccept(Ready(target: wrongSession)));
        Assert.IsFalse(latch.HasReadyAcknowledgement);
    }

    [Test]
    public void DuplicateSequenceIsRejectedWithoutMutatingReadyLatch()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.AreEqual(
            ManualHandoverValidationResult.DuplicateSequence,
            latch.TryAccept(Ready(sequence: 1UL)));
        Assert.IsFalse(latch.HasReadyAcknowledgement);
    }

    [Test]
    public void OlderSequenceIsRejectedWithoutMutatingReadyLatch()
    {
        ManualHandoverSession session;
        Assert.IsTrue(ManualHandoverSession.TryCreate(
            "coordination-session-a", Roles(), Target, 10UL, 1000UL, out session));
        var latch = new ManualHandoverEventLatch(session);
        Assert.AreEqual(
            ManualHandoverValidationResult.OlderSequence,
            latch.TryAccept(Request(sequence: 9UL)));
        Assert.IsFalse(latch.HasCoordinationRequest);
    }

    [Test]
    public void OneShotEventsCannotBeReplacedByNewerEvents()
    {
        var latch = new ManualHandoverEventLatch(Session());
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.AreEqual(
            ManualHandoverValidationResult.RequestAlreadyLatched,
            latch.TryAccept(Request(sequence: 2UL)));
        Assert.AreEqual(1UL, latch.CoordinationRequest.EventSequence);
    }

    [Test]
    public void ContractsExposeCanonicalIdentityAndNoNumericTargetFallback()
    {
        Assert.AreEqual(
            typeof(CanonicalBottleIdentity),
            typeof(ManualHandoverSession).GetProperty("TargetBottleKey").PropertyType);
        Assert.AreEqual(
            typeof(CanonicalBottleIdentity),
            typeof(ManualCoordinationRequest).GetProperty("TargetBottleKey").PropertyType);
        Assert.AreEqual(
            typeof(CanonicalBottleIdentity),
            typeof(ReadyAcknowledgement).GetProperty("TargetBottleKey").PropertyType);
        Assert.IsFalse(typeof(ManualHandoverSession).GetProperties()
            .Any(property => property.Name == "TargetId" || property.Name == "HeldObjectId"));
    }

    [TestCase(typeof(ManualHandoverRoleBinding))]
    [TestCase(typeof(ManualHandoverSession))]
    [TestCase(typeof(ManualCoordinationRequest))]
    [TestCase(typeof(ReadyAcknowledgement))]
    public void DtoIsImmutable(Type type)
    {
        Assert.IsTrue(type.IsValueType);
        Assert.IsTrue(type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"));
        Assert.IsTrue(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .All(property => property.SetMethod == null));
        Assert.IsEmpty(type.GetFields(BindingFlags.Instance | BindingFlags.Public));
    }

    [Test]
    public void EventAcceptanceDoesNotMutateFsmOrNetworkControlState()
    {
        TaskPhase fsmPhase = TaskPhase.Independent;
        var control = new SharedControlState
        {
            task_phase = TaskPhase.Independent,
            sequence = 77,
            shared_timestamp = 12345
        };
        var latch = new ManualHandoverEventLatch(Session());

        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Request()));
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, latch.TryAccept(Ready()));

        Assert.AreEqual(TaskPhase.Independent, fsmPhase);
        Assert.AreEqual(TaskPhase.Independent, control.task_phase);
        Assert.AreEqual(77, control.sequence);
        Assert.AreEqual(12345, control.shared_timestamp);
    }

    [Test]
    public void LogicalOwnershipChangesOnlyAtReleasedBoundary()
    {
        ManualHandoverRoleBinding roles = Roles();
        foreach (TaskPhase phase in new[]
        {
            TaskPhase.Independent,
            TaskPhase.Preparing,
            TaskPhase.Ready,
            TaskPhase.Transfer
        })
        {
            Assert.IsTrue(ManualHandoverOwnershipSemantics.TryGetExpectedRobotOwner(
                phase, roles, out int owner));
            Assert.AreEqual(roles.GiverRobotId, owner);
        }
        Assert.IsTrue(ManualHandoverOwnershipSemantics.TryGetExpectedRobotOwner(
            TaskPhase.Released, roles, out int releasedOwner));
        Assert.AreEqual(roles.ReceiverRobotId, releasedOwner);
    }
}
#endif
