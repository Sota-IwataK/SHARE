#if UNITY_EDITOR
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class ManualHandoverSessionLifecycleTests
{
    private static ManualHandoverSession SessionA()
    {
        Assert.IsTrue(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User1, 101,
            SharedMRParticipantId.User2, 202,
            out ManualHandoverRoleBinding roles));
        Assert.IsTrue(ManualHandoverSession.TryCreate(
            "trial-a", roles,
            new CanonicalBottleIdentity("source-a", "target-a", 1UL),
            100UL, 1000UL, out ManualHandoverSession session));
        return session;
    }

    private static ManualHandoverSession SessionB()
    {
        Assert.IsTrue(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User2, 303,
            SharedMRParticipantId.User1, 404,
            out ManualHandoverRoleBinding roles));
        Assert.IsTrue(ManualHandoverSession.TryCreate(
            "trial-b", roles,
            new CanonicalBottleIdentity("source-b", "target-b", 2UL),
            200UL, 2000UL, out ManualHandoverSession session));
        return session;
    }

    private static ManualCoordinationRequest RequestA(ulong sequence = 101UL)
    {
        ManualHandoverSession session = SessionA();
        return new ManualCoordinationRequest(
            session.CoordinationSessionId, sequence, 1100UL, "quest.a",
            session.RoleBinding.GiverParticipantId, session.TargetBottleKey);
    }

    private static ReadyAcknowledgement ReadyA(ulong sequence = 102UL)
    {
        ManualHandoverSession session = SessionA();
        return new ReadyAcknowledgement(
            session.CoordinationSessionId, sequence, 1200UL, "quest.b",
            session.RoleBinding.ReceiverParticipantId, session.TargetBottleKey);
    }

    private static ManualHandoverSessionLifecycleContract ActiveA()
    {
        var lifecycle = new ManualHandoverSessionLifecycleContract();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionA(), 10L));
        return lifecycle;
    }

    private static ManualHandoverSession IndexedSession(int index)
    {
        Assert.Greater(index, 0);
        Assert.IsTrue(ManualHandoverRoleBinding.TryCreate(
            SharedMRParticipantId.User1, 101,
            SharedMRParticipantId.User2, 202,
            out ManualHandoverRoleBinding roles));
        Assert.IsTrue(ManualHandoverSession.TryCreate(
            "capacity-trial-" + index,
            roles,
            new CanonicalBottleIdentity(
                "capacity-source", "capacity-target-" + index, (ulong)index),
            1000UL + (ulong)index,
            2000UL + (ulong)index,
            out ManualHandoverSession session));
        return session;
    }

    private static void RetireIndexedSessions(
        ManualHandoverSessionLifecycleContract lifecycle,
        int count,
        int capacity)
    {
        for (int index = 1; index <= count; index++)
        {
            Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
                lifecycle.TryActivate(IndexedSession(index), 3000L + index, capacity));
            Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
                lifecycle.TryRetire());
        }
    }

    [Test]
    public void NoSessionTransitionsToActive()
    {
        var lifecycle = new ManualHandoverSessionLifecycleContract();
        Assert.AreEqual(ManualHandoverSessionLifecycleState.NoSession, lifecycle.State);
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionA(), 10L));
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Active, lifecycle.State);
        Assert.AreEqual("trial-a", lifecycle.RetainedSession.Session.CoordinationSessionId);
    }

    [Test]
    public void ActiveTransitionsToRetiredOnlyByExplicitCall()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Retired, lifecycle.State);
        CollectionAssert.Contains(lifecycle.RetiredSessionIds.ToArray(), "trial-a");
    }

    [Test]
    public void ActiveOverwriteIsRejectedWithoutMutation()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.ActiveSessionExists,
            lifecycle.TryActivate(SessionB(), 20L));
        Assert.AreEqual("trial-a", lifecycle.RetainedSession.Session.CoordinationSessionId);
    }

    [Test]
    public void AnyRetiredSessionIdCannotBeReusedInALaterTrial()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionB(), 20L));
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.SessionIdAlreadyUsed,
            lifecycle.TryActivate(SessionA(), 30L));
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Retired, lifecycle.State);
        Assert.AreEqual("trial-b", lifecycle.RetainedSession.Session.CoordinationSessionId);
    }

    [TestCase(101UL)]
    [TestCase(100UL)]
    [TestCase(99UL)]
    public void RetiredSessionRejectsRequestIncludingDuplicateAndOlder(ulong sequence)
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        ManualHandoverTransportSnapshot before = lifecycle.RetainedSession;
        Assert.AreEqual(ManualHandoverValidationResult.SessionRetired,
            lifecycle.TryAcceptRequest(SharedMRParticipantId.User1, RequestA(sequence), 20L));
        AssertTransportEqual(before, lifecycle.RetainedSession);
    }

    [TestCase(102UL)]
    [TestCase(100UL)]
    [TestCase(99UL)]
    public void RetiredSessionRejectsReadyIncludingDuplicateAndOlder(ulong sequence)
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        ManualHandoverTransportSnapshot before = lifecycle.RetainedSession;
        Assert.AreEqual(ManualHandoverValidationResult.SessionRetired,
            lifecycle.TryAcceptReady(SharedMRParticipantId.User2, ReadyA(sequence), 20L));
        AssertTransportEqual(before, lifecycle.RetainedSession);
    }

    [Test]
    public void DelayedOldRequestAndReadyCannotEnterNewTrial()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionB(), 20L));
        ManualHandoverTransportSnapshot before = lifecycle.RetainedSession;

        Assert.AreEqual(ManualHandoverValidationResult.SessionMismatch,
            lifecycle.TryAcceptRequest(SharedMRParticipantId.User1, RequestA(), 21L));
        Assert.AreEqual(ManualHandoverValidationResult.SessionMismatch,
            lifecycle.TryAcceptReady(SharedMRParticipantId.User2, ReadyA(), 22L));
        AssertTransportEqual(before, lifecycle.RetainedSession);
    }

    [Test]
    public void NewSessionStartsCleanAndAuthoritySequenceIsSessionScoped()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverValidationResult.Accepted,
            lifecycle.TryAcceptRequest(SharedMRParticipantId.User1, RequestA(), 11L));
        Assert.AreEqual(ManualHandoverValidationResult.Accepted,
            lifecycle.TryAcceptReady(SharedMRParticipantId.User2, ReadyA(), 12L));
        Assert.AreEqual(2UL, lifecycle.RetainedSession.AuthorityAcceptedSequence);
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionB(), 20L));

        ManualHandoverTransportSnapshot trialB = lifecycle.RetainedSession;
        Assert.IsFalse(trialB.RequestAccepted);
        Assert.IsFalse(trialB.ReadyAccepted);
        Assert.AreEqual(0UL, trialB.AuthorityAcceptedSequence);
        Assert.AreEqual(SessionB().TargetBottleKey, trialB.Session.TargetBottleKey);
        Assert.AreNotEqual(SessionA().TargetBottleKey, trialB.Session.TargetBottleKey);
    }

    [Test]
    public void RolesAndCanonicalTargetCanChangeBetweenTrials()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionB(), 20L));
        Assert.AreEqual(SharedMRParticipantId.User2,
            lifecycle.RetainedSession.Session.RoleBinding.GiverParticipantId);
        Assert.AreEqual(SharedMRParticipantId.User1,
            lifecycle.RetainedSession.Session.RoleBinding.ReceiverParticipantId);
        Assert.AreEqual(new CanonicalBottleIdentity("source-b", "target-b", 2UL),
            lifecycle.RetainedSession.Session.TargetBottleKey);
    }

    [Test]
    public void ActiveAndRetiredSnapshotsReconstructAcrossAuthorityHandoff()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverValidationResult.Accepted,
            lifecycle.TryAcceptRequest(SharedMRParticipantId.User1, RequestA(), 11L));
        Assert.IsTrue(ManualHandoverSessionLifecycleContract.TryRestore(
            lifecycle.CaptureSnapshot(), out ManualHandoverSessionLifecycleContract activeRestored));
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Active, activeRestored.State);
        Assert.IsTrue(activeRestored.RetainedSession.RequestAccepted);
        Assert.AreEqual(ManualHandoverValidationResult.DuplicateSequence,
            activeRestored.TryAcceptRequest(SharedMRParticipantId.User1, RequestA(), 12L));

        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            activeRestored.TryRetire());
        Assert.IsTrue(ManualHandoverSessionLifecycleContract.TryRestore(
            activeRestored.CaptureSnapshot(), out ManualHandoverSessionLifecycleContract retiredRestored));
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Retired, retiredRestored.State);
        Assert.AreEqual(ManualHandoverValidationResult.SessionRetired,
            retiredRestored.TryAcceptReady(SharedMRParticipantId.User2, ReadyA(), 13L));
    }

    [Test]
    public void InvalidRetiredSnapshotWithoutLedgerEntryFailsClosed()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        ManualHandoverSessionLifecycleSnapshot invalid =
            new ManualHandoverSessionLifecycleSnapshot(
                ManualHandoverSessionLifecycleState.Retired,
                true,
                lifecycle.RetainedSession,
                new string[0]);
        Assert.IsFalse(ManualHandoverSessionLifecycleContract.TryRestore(invalid, out _));
    }

    [Test]
    public void LifecycleOperationsDoNotMutateTaskPhaseSharedControlOrOwner()
    {
        TaskPhase phase = TaskPhase.Independent;
        SharedControlState control = new SharedControlState
        {
            task_phase = TaskPhase.Independent,
            sequence = 77,
            shared_timestamp = 88L,
            owner_type = SharedControlOwnerType.Robot,
            owner_id = 101
        };

        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted, lifecycle.TryRetire());
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(SessionB(), 20L));

        Assert.AreEqual(TaskPhase.Independent, phase);
        Assert.AreEqual(TaskPhase.Independent, control.task_phase);
        Assert.AreEqual(77, control.sequence);
        Assert.AreEqual(88L, control.shared_timestamp);
        Assert.AreEqual(SharedControlOwnerType.Robot, control.owner_type);
        Assert.AreEqual(101, control.owner_id);
    }

    [Test]
    public void DisconnectOrHandoffReconstructionDoesNotAutoRetireOrComplete()
    {
        ManualHandoverSessionLifecycleContract lifecycle = ActiveA();
        ManualHandoverSessionLifecycleSnapshot replicated = lifecycle.CaptureSnapshot();
        Assert.IsTrue(ManualHandoverSessionLifecycleContract.TryRestore(
            replicated, out ManualHandoverSessionLifecycleContract restored));
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Active, restored.State);
        Assert.AreEqual(0, restored.RetiredSessionCount);
        Assert.IsFalse(restored.RetainedSession.ReadyAccepted);
    }

    [Test]
    public void LifecycleProductionPathDoesNotCallFsmOrDisconnectCallbacks()
    {
        string lifecycleSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts/PhotonSharedMR/ManualHandoverSessionLifecycle.cs"));
        string networkSource = File.ReadAllText(Path.Combine(
            Application.dataPath, "Scripts/PhotonSharedMR/ManualHandoverSessionNetwork.cs"));

        StringAssert.DoesNotContain("CoordinationStateMachine.", lifecycleSource);
        StringAssert.DoesNotContain("AuthoritativeTaskPhaseTransition", lifecycleSource);
        StringAssert.DoesNotContain("CoordinationStateMachine.", networkSource);
        StringAssert.DoesNotContain("AuthoritativeTaskPhaseTransition", networkSource);
        StringAssert.DoesNotContain("OnPlayerLeft", networkSource);
        StringAssert.DoesNotContain("OnDisconnectedFromServer", networkSource);
        StringAssert.DoesNotContain("OnShutdown", networkSource);
    }

    [TestCase(14)]
    [TestCase(15)]
    public void CapacityGuardAllowsNextSessionBelowCapacity(int retiredCount)
    {
        const int capacity = ManualHandoverSessionNetwork.RetiredSessionCapacity;
        var lifecycle = new ManualHandoverSessionLifecycleContract();
        RetireIndexedSessions(lifecycle, retiredCount, capacity);

        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(
                IndexedSession(retiredCount + 1), 4000L + retiredCount, capacity));
        Assert.AreEqual(ManualHandoverSessionLifecycleState.Active, lifecycle.State);
    }

    [Test]
    public void SixteenthSessionCanRetireAndSeventeenthIsRejectedWithoutMutation()
    {
        const int capacity = ManualHandoverSessionNetwork.RetiredSessionCapacity;
        var lifecycle = new ManualHandoverSessionLifecycleContract();
        RetireIndexedSessions(lifecycle, capacity - 1, capacity);

        ManualHandoverSession trial16 = IndexedSession(capacity);
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryActivate(trial16, 5000L, capacity));
        Assert.AreEqual(ManualHandoverValidationResult.Accepted,
            lifecycle.TryAcceptRequest(
                trial16.RoleBinding.GiverParticipantId,
                new ManualCoordinationRequest(
                    trial16.CoordinationSessionId,
                    trial16.CreatedSequence + 1UL,
                    5001UL,
                    "capacity.giver",
                    trial16.RoleBinding.GiverParticipantId,
                    trial16.TargetBottleKey),
                5001L));
        Assert.AreEqual(ManualHandoverValidationResult.Accepted,
            lifecycle.TryAcceptReady(
                trial16.RoleBinding.ReceiverParticipantId,
                new ReadyAcknowledgement(
                    trial16.CoordinationSessionId,
                    trial16.CreatedSequence + 2UL,
                    5002UL,
                    "capacity.receiver",
                    trial16.RoleBinding.ReceiverParticipantId,
                    trial16.TargetBottleKey),
                5002L));
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            lifecycle.TryRetire());
        Assert.AreEqual(capacity, lifecycle.RetiredSessionCount);

        ManualHandoverSessionLifecycleSnapshot before = lifecycle.CaptureSnapshot();
        TaskPhase phase = TaskPhase.Independent;
        SharedControlState control = new SharedControlState
        {
            task_phase = TaskPhase.Independent,
            sequence = 77,
            shared_timestamp = 88L,
            owner_type = SharedControlOwnerType.Robot,
            owner_id = 101
        };

        Assert.AreEqual(
            ManualHandoverSessionLifecycleResult.RetiredSessionLedgerCapacityExceeded,
            lifecycle.TryActivate(IndexedSession(capacity + 1), 6000L, capacity));
        AssertLifecycleEqual(before, lifecycle.CaptureSnapshot());
        Assert.AreEqual(TaskPhase.Independent, phase);
        Assert.AreEqual(TaskPhase.Independent, control.task_phase);
        Assert.AreEqual(77, control.sequence);
        Assert.AreEqual(88L, control.shared_timestamp);
        Assert.AreEqual(SharedControlOwnerType.Robot, control.owner_type);
        Assert.AreEqual(101, control.owner_id);
    }

    [Test]
    public void CapacityGuardPreservesReuseAndActiveOverwriteReasonPriority()
    {
        const int capacity = ManualHandoverSessionNetwork.RetiredSessionCapacity;
        var full = new ManualHandoverSessionLifecycleContract();
        RetireIndexedSessions(full, capacity, capacity);
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.SessionIdAlreadyUsed,
            full.TryActivate(IndexedSession(1), 7000L, capacity));

        var active = new ManualHandoverSessionLifecycleContract();
        RetireIndexedSessions(active, capacity - 1, capacity);
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.Accepted,
            active.TryActivate(IndexedSession(capacity), 7001L, capacity));
        Assert.AreEqual(ManualHandoverSessionLifecycleResult.ActiveSessionExists,
            active.TryActivate(IndexedSession(capacity + 1), 7002L, capacity));
    }

    private static void AssertLifecycleEqual(
        ManualHandoverSessionLifecycleSnapshot expected,
        ManualHandoverSessionLifecycleSnapshot actual)
    {
        Assert.AreEqual(expected.State, actual.State);
        Assert.AreEqual(expected.HasRetainedSession, actual.HasRetainedSession);
        CollectionAssert.AreEqual(
            expected.RetiredSessionIds.ToArray(), actual.RetiredSessionIds.ToArray());
        if (!expected.HasRetainedSession)
        {
            return;
        }

        ManualHandoverTransportSnapshot left = expected.RetainedSession;
        ManualHandoverTransportSnapshot right = actual.RetainedSession;
        Assert.AreEqual(left.Session.CoordinationSessionId,
            right.Session.CoordinationSessionId);
        Assert.AreEqual(left.Session.RoleBinding.GiverParticipantId,
            right.Session.RoleBinding.GiverParticipantId);
        Assert.AreEqual(left.Session.RoleBinding.GiverRobotId,
            right.Session.RoleBinding.GiverRobotId);
        Assert.AreEqual(left.Session.RoleBinding.ReceiverParticipantId,
            right.Session.RoleBinding.ReceiverParticipantId);
        Assert.AreEqual(left.Session.RoleBinding.ReceiverRobotId,
            right.Session.RoleBinding.ReceiverRobotId);
        Assert.AreEqual(left.Session.TargetBottleKey, right.Session.TargetBottleKey);
        Assert.AreEqual(left.RequestAccepted, right.RequestAccepted);
        Assert.AreEqual(left.Request.EventSequence, right.Request.EventSequence);
        Assert.AreEqual(left.Request.EventTimestamp, right.Request.EventTimestamp);
        Assert.AreEqual(left.ReadyAccepted, right.ReadyAccepted);
        Assert.AreEqual(left.Ready.EventSequence, right.Ready.EventSequence);
        Assert.AreEqual(left.Ready.EventTimestamp, right.Ready.EventTimestamp);
        Assert.AreEqual(left.AuthorityAcceptedSequence, right.AuthorityAcceptedSequence);
        Assert.AreEqual(left.AuthorityAcceptedTimestamp, right.AuthorityAcceptedTimestamp);
        Assert.AreEqual(left.AuthorityClockDomain, right.AuthorityClockDomain);
    }

    private static void AssertTransportEqual(
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
