#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;

public sealed class ManualHandoverTransportTests
{
    private static readonly CanonicalBottleIdentity Target =
        new CanonicalBottleIdentity("source-🧪", "bottle-session", ulong.MaxValue);

    private static ManualHandoverSession Session()
    {
        Assert.IsTrue(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User1, 10,
            SharedMRParticipantId.User2, 20,
            out ManualHandoverRoleBinding roles));
        Assert.IsTrue(ManualHandoverSession.TryCreate(
            "handover-session", roles, Target, 0, 100,
            out ManualHandoverSession session));
        return session;
    }

    private static ManualHandoverTransportSnapshot Initial()
    {
        Assert.IsTrue(ManualHandoverTransportContract.TryCreateInitial(
            Session(), 500, out ManualHandoverTransportSnapshot snapshot));
        return snapshot;
    }

    private static ManualCoordinationRequest Request(
        SharedMRParticipantId source = SharedMRParticipantId.User1,
        ulong sequence = 1,
        string session = "handover-session",
        CanonicalBottleIdentity? target = null)
    {
        return new ManualCoordinationRequest(
            session, sequence, 1000, "quest.monotonic_ns", source, target ?? Target);
    }

    private static ReadyAcknowledgement Ready(
        SharedMRParticipantId source = SharedMRParticipantId.User2,
        ulong sequence = 2,
        string session = "handover-session",
        CanonicalBottleIdentity? target = null)
    {
        return new ReadyAcknowledgement(
            session, sequence, 2000, "quest.monotonic_ns", source, target ?? Target);
    }

    [Test]
    public void FullCanonicalIdentityRoundTripsLosslessly()
    {
        ManualHandoverTransportSnapshot snapshot = Initial();
        Assert.AreEqual(Target, snapshot.Session.TargetBottleKey);
        Assert.AreEqual(ulong.MaxValue, snapshot.Session.TargetBottleKey.ObjectId);
        Assert.AreEqual("source-🧪", snapshot.Session.TargetBottleKey.SourceId);
        Assert.AreEqual("bottle-session", snapshot.Session.TargetBottleKey.SessionId);
    }

    [Test]
    public void ValidGiverRequestIsAcceptedWithAuthorityMetadata()
    {
        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptRequest(
            Initial(), SharedMRParticipantId.User1, Request(), 600,
            out ManualHandoverTransportSnapshot next);
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, result);
        Assert.IsTrue(next.RequestAccepted);
        Assert.AreEqual(1UL, next.AuthorityAcceptedSequence);
        Assert.AreEqual(600, next.AuthorityAcceptedTimestamp);
        Assert.AreEqual(ManualHandoverTransportContract.AuthorityClockDomain,
            next.AuthorityClockDomain);
    }

    [Test]
    public void SourceAndAuthorityClocksRemainDistinctAndSourceTimeDoesNotOrderAcceptance()
    {
        ManualCoordinationRequest request = new ManualCoordinationRequest(
            "handover-session", 1, ulong.MaxValue, "device.unsynchronised_ticks",
            SharedMRParticipantId.User1, Target);
        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptRequest(
            Initial(), SharedMRParticipantId.User1, request, 601,
            out ManualHandoverTransportSnapshot next);

        Assert.AreEqual(ManualHandoverValidationResult.Accepted, result);
        Assert.AreEqual(ulong.MaxValue, next.Request.EventTimestamp);
        Assert.AreEqual("device.unsynchronised_ticks", next.Request.SourceClockDomain);
        Assert.AreEqual(601, next.AuthorityAcceptedTimestamp);
        Assert.AreEqual(ManualHandoverTransportContract.AuthorityClockDomain,
            next.AuthorityClockDomain);
    }

    [TestCase(SharedMRParticipantId.User2)]
    [TestCase(SharedMRParticipantId.User3)]
    public void NonGiverRequestIsRejected(SharedMRParticipantId sender)
    {
        AssertRejectedRequestUnchanged(
            sender, Request(source: sender),
            ManualHandoverValidationResult.SourceNotGiver);
    }

    [Test]
    public void RpcClaimMismatchIsRejected()
    {
        AssertRejectedRequestUnchanged(
            SharedMRParticipantId.User2, Request(source: SharedMRParticipantId.User1),
            ManualHandoverValidationResult.RpcSenderMismatch);
    }

    [Test]
    public void ValidReceiverReadyIsAccepted()
    {
        ManualHandoverTransportContract.TryAcceptRequest(
            Initial(), SharedMRParticipantId.User1, Request(), 600,
            out ManualHandoverTransportSnapshot requested);
        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptReady(
            requested, SharedMRParticipantId.User2, Ready(), 700,
            out ManualHandoverTransportSnapshot ready);
        Assert.AreEqual(ManualHandoverValidationResult.Accepted, result);
        Assert.IsTrue(ready.ReadyAccepted);
        Assert.AreEqual(2UL, ready.AuthorityAcceptedSequence);
        Assert.AreEqual(700, ready.AuthorityAcceptedTimestamp);
    }

    [Test]
    public void GiverReadyIsRejected()
    {
        ManualHandoverTransportContract.TryAcceptRequest(
            Initial(), SharedMRParticipantId.User1, Request(), 600,
            out ManualHandoverTransportSnapshot requested);
        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptReady(
            requested, SharedMRParticipantId.User1,
            Ready(source: SharedMRParticipantId.User1), 700, out ManualHandoverTransportSnapshot next);
        Assert.AreEqual(ManualHandoverValidationResult.SourceNotReceiver, result);
        AssertSnapshotEqual(requested, next);
    }

    [Test]
    public void ReadyBeforeRequestIsRejected()
    {
        ManualHandoverTransportSnapshot initial = Initial();
        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptReady(
            initial, SharedMRParticipantId.User2, Ready(sequence: 1), 700,
            out ManualHandoverTransportSnapshot next);
        Assert.AreEqual(ManualHandoverValidationResult.RequestRequired, result);
        AssertSnapshotEqual(initial, next);
    }

    [Test]
    public void WrongSessionAndTargetAreRejected()
    {
        AssertRejectedRequestUnchanged(
            SharedMRParticipantId.User1, Request(session: "other"),
            ManualHandoverValidationResult.SessionMismatch);
        CanonicalBottleIdentity wrong =
            new CanonicalBottleIdentity("source-🧪", "other-bottle-session", ulong.MaxValue);
        AssertRejectedRequestUnchanged(
            SharedMRParticipantId.User1, Request(target: wrong),
            ManualHandoverValidationResult.TargetMismatch);
    }

    [Test]
    public void AuthorityHandoffSnapshotReconstructionStillRejectsDuplicateAndOlderEvents()
    {
        ManualHandoverTransportContract.TryAcceptRequest(
            Initial(), SharedMRParticipantId.User1, Request(sequence: 5), 600,
            out ManualHandoverTransportSnapshot requested);
        ManualHandoverValidationResult duplicate = ManualHandoverTransportContract.TryAcceptReady(
            requested, SharedMRParticipantId.User2, Ready(sequence: 5), 700, out var duplicateNext);
        ManualHandoverValidationResult older = ManualHandoverTransportContract.TryAcceptReady(
            requested, SharedMRParticipantId.User2, Ready(sequence: 4), 700, out var olderNext);
        Assert.AreEqual(ManualHandoverValidationResult.DuplicateSequence, duplicate);
        Assert.AreEqual(ManualHandoverValidationResult.OlderSequence, older);
        AssertSnapshotEqual(requested, duplicateNext);
        AssertSnapshotEqual(requested, olderNext);
    }

    [Test]
    public void RejectedEventDoesNotMutateTaskPhaseOrControlVersion()
    {
        TaskPhase phase = TaskPhase.Independent;
        SharedControlState control = new SharedControlState
        { task_phase = phase, sequence = 9, shared_timestamp = 123 };
        AssertRejectedRequestUnchanged(
            SharedMRParticipantId.User2, Request(source: SharedMRParticipantId.User1),
            ManualHandoverValidationResult.RpcSenderMismatch);
        Assert.AreEqual(TaskPhase.Independent, phase);
        Assert.AreEqual(TaskPhase.Independent, control.task_phase);
        Assert.AreEqual(9, control.sequence);
        Assert.AreEqual(123, control.shared_timestamp);
    }

    [Test]
    public void TransportSchemaHasNoNumericBottleFallback()
    {
        string[] properties = typeof(ManualHandoverTransportSnapshot)
            .GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(properties, "TargetId");
        CollectionAssert.DoesNotContain(properties, "HeldObjectId");
        Assert.AreEqual(typeof(CanonicalBottleIdentity),
            typeof(ManualHandoverSession).GetProperty("TargetBottleKey").PropertyType);
    }

    private static void AssertRejectedRequestUnchanged(
        SharedMRParticipantId actual,
        ManualCoordinationRequest request,
        ManualHandoverValidationResult expected)
    {
        ManualHandoverTransportSnapshot initial = Initial();
        ManualHandoverValidationResult result = ManualHandoverTransportContract.TryAcceptRequest(
            initial, actual, request, 600, out ManualHandoverTransportSnapshot next);
        Assert.AreEqual(expected, result);
        AssertSnapshotEqual(initial, next);
    }

    private static void AssertSnapshotEqual(
        ManualHandoverTransportSnapshot expected,
        ManualHandoverTransportSnapshot actual)
    {
        Assert.AreEqual(expected.Session.CoordinationSessionId,
            actual.Session.CoordinationSessionId);
        Assert.AreEqual(expected.Session.TargetBottleKey, actual.Session.TargetBottleKey);
        Assert.AreEqual(expected.RequestAccepted, actual.RequestAccepted);
        Assert.AreEqual(expected.ReadyAccepted, actual.ReadyAccepted);
        Assert.AreEqual(expected.AuthorityAcceptedSequence, actual.AuthorityAcceptedSequence);
        Assert.AreEqual(expected.AuthorityAcceptedTimestamp, actual.AuthorityAcceptedTimestamp);
    }
}
#endif
