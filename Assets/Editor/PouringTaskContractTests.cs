#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ShareMr.Pouring.Tests
{
    public sealed class PouringTaskContractTests
    {
        // Synthetic test values only: these are not calibrated production protocol parameters.
        private sealed class Fixture
        {
            internal readonly TrialProtocolReference Reference = new TrialProtocolReference("test", "1", "test-only");
            internal readonly CupIdentity Source;
            internal readonly CupIdentity Destination;
            internal readonly TrialProtocol Protocol;
            internal readonly PouringSessionBinding Binding;
            internal PouringSessionState State;
            internal ClockStamp Now = new ClockStamp(1000000000L, "test.monotonic");
            internal readonly List<EvidenceFact> Facts = new List<EvidenceFact>();
            private int eventId;
            private int snapshotId;

            internal Fixture(bool manual = false, long? holdAge = 1000, long? eventAge = 1000,
                SourceLossPolicy loss = SourceLossPolicy.BLOCK_AND_ABORT_ON_TIMEOUT,
                long? timeout = 100, CupIdentity? destination = null)
            {
                Source = new CupIdentity("camera-a", "objects-a", 1, "CUP");
                Destination = destination ?? new CupIdentity("camera-b", "objects-b", 2, "CUP");
                Binding = new PouringSessionBinding("trial", "coord", "u1", "new-amir", "u2", "old-amir",
                    Source, Destination, Reference, "supervisor");
                Protocol = new TrialProtocol(Reference, eventAge, holdAge, 1000, 1000, 1000, 1000,
                    manual, manual, loss, timeout, EvidenceQuality.OBSERVER_VERIFIED,
                    new[] { new EvidenceWriter("provider", "provider-session") }, new[] { "assessor" });
                Assert.IsTrue(PouringTaskStateMachine.TryCreateInitial(Binding, Protocol, Senders(),
                    out State, out var rejection), rejection.ToString());
                foreach (EvidenceKind kind in Enum.GetValues(typeof(EvidenceKind))) Facts.Add(Fact(kind, sequence: 1));
            }

            internal SenderSession[] Senders() => new[]
            {
                new SenderSession(PouringRole.Pourer, "u1", "sender-1"),
                new SenderSession(PouringRole.Receiver, "u2", "sender-2"),
                new SenderSession(PouringRole.Supervisor, "supervisor", "sender-3")
            };

            internal EvidenceFact Fact(EvidenceKind kind, TruthValue truth = TruthValue.TRUE,
                ulong sequence = 2, EvidenceSource source = EvidenceSource.SENSOR_VERIFIED,
                CupIdentity? cup = null, ClockStamp? timestamp = null, string writer = "provider",
                string writerSession = "provider-session", string robot = null, string trial = "trial")
            {
                var stamp = timestamp ?? Now;
                var provenance = new EvidenceProvenance(trial, "coord", writer, writerSession, sequence, stamp, stamp);
                bool left = kind == EvidenceKind.SourceCupHold || kind == EvidenceKind.SourceCupObserved
                    || kind == EvidenceKind.SourceRobotConnected;
                var identity = cup ?? (left ? Source : Destination);
                var user = left ? "u1" : "u2";
                var robotId = robot ?? (left ? "new-amir" : "old-amir");
                if (kind == EvidenceKind.SourceCupHold || kind == EvidenceKind.DestinationCupHold)
                    return new CupHoldEvidence(left, truth == TruthValue.TRUE ? HoldState.HELD
                        : truth == TruthValue.FALSE ? HoldState.NOT_HELD : HoldState.UNKNOWN,
                        source, provenance, identity, user, robotId);
                return new EvidenceFact(kind, truth, source, provenance,
                    kind == EvidenceKind.SourceCupObserved || kind == EvidenceKind.DestinationCupObserved
                        ? (CupIdentity?)identity : null, user, robotId);
            }

            internal GuardSnapshot Snapshot(EvidenceFact replacement = null, EvidenceKind? missing = null)
            {
                var list = new List<EvidenceFact>(Facts);
                if (replacement != null) { list.RemoveAll(f => f.Kind == replacement.Kind); list.Add(replacement); }
                if (missing.HasValue) list.RemoveAll(f => f.Kind == missing.Value);
                return new GuardSnapshot("guard-" + ++snapshotId, Reference, list);
            }

            internal PouringEvent Event(PouringEventKind kind, PouringRole role = PouringRole.Pourer,
                ulong? sequence = null, string id = null, string senderSession = null,
                CupIdentity? cup = null, ClockStamp? timestamp = null, string authenticated = null,
                string trial = "trial", string coordination = "coord")
            {
                string user = Binding.User(role);
                SenderSession active = null;
                foreach (var s in State.Acceptance.Senders) if (s.Role == role && s.Admitted) active = s;
                var identity = new EventIdentity(id ?? "event-" + ++eventId, trial, coordination, user,
                    senderSession ?? active.Id, sequence ?? (active.HasSequence ? active.LastAcceptedSequence + 1 : 1),
                    kind, timestamp ?? Now);
                return new PouringEvent(identity, authenticated ?? user, "test abort", cup);
            }

            internal TransitionResult Evaluate(PouringEvent evt, GuardSnapshot snapshot = null)
                => PouringTaskStateMachine.Evaluate(State, evt, Binding, snapshot ?? Snapshot(), Protocol, State.Acceptance, Now);

            internal TransitionResult Commit(PouringEventKind kind, PouringRole role = PouringRole.Pourer,
                GuardSnapshot snapshot = null, CupIdentity? cup = null)
            {
                var old = State;
                var result = Evaluate(Event(kind, role, cup: cup), snapshot);
                Assert.IsTrue(result.Accepted, result.Rejection.ToString());
                Assert.AreSame(old, result.Proposal.ExpectedState);
                Assert.IsTrue(PouringTaskStateMachine.TryApplyProposal(old, result.Proposal, out State));
                Assert.AreEqual(old.CommitSequence + 1, State.CommitSequence);
                return result;
            }

            internal void Preparing() { Commit(PouringEventKind.SessionRequested); }
            internal void Acks()
            {
                Commit(PouringEventKind.ReadyAcknowledged);
                Commit(PouringEventKind.ReadyAcknowledged, PouringRole.Receiver);
            }
            internal void Ready() { Preparing(); Acks(); Commit(PouringEventKind.ReadyConditionAccepted); }
            internal void Pouring() { Ready(); Commit(PouringEventKind.PourStart); }
            internal void Completed() { Pouring(); Commit(PouringEventKind.PourEnd); }
            internal OutcomeAssessment Assessment(TaskOutcome outcome, ulong revision = 1,
                EvidenceQuality quality = EvidenceQuality.VIDEO_VERIFIED, string assessor = "assessor",
                string session = "coord", ulong? commit = null)
                => new OutcomeAssessment("trial", session, revision, assessor, Now, outcome, "review",
                    "offline-evidence", quality, commit ?? State.CommitSequence, Reference);
        }

        private static void Rejected(TransitionResult result, PouringRejection reason)
        {
            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(reason, result.Rejection);
            Assert.IsNull(result.Proposal);
        }

        [Test]
        public void DistinctFullKeysAndSeparateHoldTransferContractsAreAccepted()
        {
            var f = new Fixture();
            Assert.IsTrue(f.Binding.IsValid);
            Assert.AreEqual(PouringTaskKind.POURING, f.Binding.TaskKind);
            Assert.AreEqual(BeadTransferState.NOT_STARTED, f.State.Transfer);
            Assert.AreNotEqual(f.Source, f.Destination);
            Assert.AreNotEqual(f.Source, new CupIdentity("other", f.Source.SessionId, f.Source.ObjectId, "CUP"));
            Assert.AreNotEqual(f.Source, new CupIdentity(f.Source.SourceId, "other", f.Source.ObjectId, "CUP"));
            Assert.AreEqual(f.Source, new CupIdentity(f.Source.SourceId, f.Source.SessionId, f.Source.ObjectId, "bottle"));
            Assert.IsFalse(new CupIdentity(f.Source.SourceId, f.Source.SessionId, f.Source.ObjectId, "bottle").IsValid);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvalidCupBindingIsRejected(bool sameKey)
        {
            var f = new Fixture();
            var destination = sameKey ? f.Source : new CupIdentity("camera-b", "objects-b", 2, "BOTTLE");
            var binding = new PouringSessionBinding("trial", "coord", "u1", "new-amir", "u2", "old-amir",
                f.Source, destination, f.Reference);
            Assert.IsFalse(PouringTaskStateMachine.TryCreateInitial(binding, f.Protocol, f.Senders(), out _, out var reason));
            Assert.AreEqual(PouringRejection.InvalidBinding, reason);
        }

        [Test]
        public void NormalFivePhasesEndWithoutReleaseOrSuccessInference()
        {
            var f = new Fixture();
            Assert.AreEqual(PouringPhase.Independent, f.State.Phase);
            f.Preparing(); Assert.AreEqual(PouringPhase.Preparing, f.State.Phase);
            f.Acks(); f.Commit(PouringEventKind.ReadyConditionAccepted);
            Assert.AreEqual(PouringPhase.Ready, f.State.Phase);
            f.Commit(PouringEventKind.PourStart);
            Assert.AreEqual(PouringPhase.Pouring, f.State.Phase);
            Assert.AreEqual(BeadTransferState.IN_PROGRESS, f.State.Transfer);
            var endOnly = new GuardSnapshot("end-only", f.Reference, new[] { f.Fact(EvidenceKind.EndConfirmed) });
            f.Commit(PouringEventKind.PourEnd, snapshot: endOnly);
            Assert.AreEqual(PouringPhase.Completed, f.State.Phase);
            Assert.IsTrue(f.State.Terminal);
            Assert.AreEqual(TaskOutcome.UNKNOWN, f.State.Outcome);
            Assert.AreEqual(BeadTransferState.STOPPED, f.State.Transfer);
            Assert.AreEqual(TaskOutcome.UNKNOWN, new OutcomeAssessmentHistory().Aggregate(f.State));
        }

        [TestCase(EvidenceKind.SessionUnique)]
        [TestCase(EvidenceKind.NoUnfinishedSession)]
        public void SessionAdmissionRequiresExplicitUniquenessAndNoUnfinishedSession(EvidenceKind kind)
        {
            var f = new Fixture();
            Rejected(f.Evaluate(f.Event(PouringEventKind.SessionRequested), f.Snapshot(f.Fact(kind, TruthValue.FALSE))),
                PouringRejection.ConditionFalse);
        }

        [TestCase(PouringEventKind.PourStart)]
        [TestCase(PouringEventKind.PourEnd)]
        [TestCase(PouringEventKind.ReadyConditionAccepted)]
        public void IndependentCannotSkipPhases(PouringEventKind kind)
        {
            var f = new Fixture();
            Rejected(f.Evaluate(f.Event(kind)), PouringRejection.IllegalTransition);
            Assert.AreEqual(0UL, f.State.CommitSequence);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingReadyAcknowledgementRejectsProgress(bool oneAck)
        {
            var f = new Fixture(); f.Preparing();
            if (oneAck) f.Commit(PouringEventKind.ReadyAcknowledged);
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyConditionAccepted)), PouringRejection.EvidenceUnknown);
        }

        [TestCase(EvidenceKind.SourceCupHold, TruthValue.UNKNOWN, PouringRejection.EvidenceUnknown)]
        [TestCase(EvidenceKind.DestinationCupHold, TruthValue.UNKNOWN, PouringRejection.EvidenceUnknown)]
        [TestCase(EvidenceKind.SourceCupHold, TruthValue.FALSE, PouringRejection.ConditionFalse)]
        [TestCase(EvidenceKind.DestinationCupHold, TruthValue.FALSE, PouringRejection.ConditionFalse)]
        public void EachCupMustRemainHeld(EvidenceKind kind, TruthValue truth, PouringRejection reason)
        {
            var f = new Fixture(); f.Ready();
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(f.Fact(kind, truth))), reason);
            Assert.AreEqual(PouringPhase.Ready, f.State.Phase);
        }

        [TestCase(EvidenceKind.SourceCupHold)]
        [TestCase(EvidenceKind.SourceCupObserved)]
        [TestCase(EvidenceKind.DestinationCupObserved)]
        public void FullIdentityCannotBeSubstituted(EvidenceKind kind)
        {
            var f = new Fixture(); f.Ready();
            var substitute = new CupIdentity("camera-new", "objects-new", 1, "CUP");
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart),
                f.Snapshot(f.Fact(kind, cup: substitute))), PouringRejection.EvidenceIdentityMismatch);
        }

        [Test]
        public void WrongRobotIsRejectedEvenForSameCup()
        {
            var f = new Fixture(); f.Ready();
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(f.Fact(
                EvidenceKind.SourceCupHold, robot: "old-amir"))), PouringRejection.EvidenceIdentityMismatch);
        }

        [TestCase("stale")]
        [TestCase("future")]
        [TestCase("clock")]
        public void InvalidEvidenceTimeIsUnknown(string variation)
        {
            var f = new Fixture(); f.Ready();
            var stamp = variation == "stale" ? new ClockStamp(0, "test.monotonic")
                : variation == "future" ? new ClockStamp(f.Now.Nanoseconds + 1, f.Now.Domain)
                : new ClockStamp(f.Now.Nanoseconds, "unmapped-clock");
            if (variation == "stale") f.Now = new ClockStamp(1001000000L, f.Now.Domain);
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(f.Fact(
                EvidenceKind.SourceCupHold, timestamp: stamp))), PouringRejection.EvidenceUnknown);
        }

        [Test]
        public void MissingHoldAgeRejectsWithoutInventingCalibration()
        {
            var f = new Fixture(holdAge: null); f.Preparing(); f.Acks();
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyConditionAccepted)), PouringRejection.EvidenceUnknown);
        }

        [Test]
        public void FreshPhysicalEvidenceCannotRefreshExpiredReadyAcknowledgements()
        {
            var f = new Fixture(); f.Ready();
            f.Now = new ClockStamp(f.Now.Nanoseconds + 1001000000L, f.Now.Domain);
            f.Facts.Clear();
            foreach (EvidenceKind kind in Enum.GetValues(typeof(EvidenceKind))) f.Facts.Add(f.Fact(kind));
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart)), PouringRejection.EvidenceUnknown);
            f.Acks(); f.Commit(PouringEventKind.PourStart);
        }

        [TestCase(EvidenceKind.NoConflict)]
        [TestCase(EvidenceKind.NoStop)]
        public void ConflictOrStopRejectsStart(EvidenceKind kind)
        {
            var f = new Fixture(); f.Ready();
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(f.Fact(kind, TruthValue.FALSE))),
                PouringRejection.ConditionFalse);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManualPreparationRequiresExplicitProtocolPermission(bool allow)
        {
            var f = new Fixture(manual: allow); f.Preparing(); f.Acks();
            var evidence = f.Snapshot(f.Fact(EvidenceKind.Preparation, source: EvidenceSource.OPERATOR_CONFIRMED));
            var result = f.Evaluate(f.Event(PouringEventKind.ReadyConditionAccepted), evidence);
            if (!allow) Rejected(result, PouringRejection.ManualEvidenceNotAllowed);
            else
            {
                Assert.IsTrue(result.Accepted);
                Assert.AreEqual(EvidenceSource.OPERATOR_CONFIRMED, result.Proposal.GuardSnapshot.Find(EvidenceKind.Preparation).Source);
            }
        }

        [Test]
        public void UncalibratedUnknownPreparationCannotAdvancePhase()
        {
            var f = new Fixture(); f.Preparing(); f.Acks();
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyConditionAccepted),
                f.Snapshot(f.Fact(EvidenceKind.Preparation, TruthValue.UNKNOWN))), PouringRejection.EvidenceUnknown);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SourceTimestampCannotBeRefreshedByReceptionTimeOrUnmappedClock(bool sameClock)
        {
            var f = new Fixture(); f.Ready();
            var provenance = new EvidenceProvenance("trial", "coord", "provider", "provider-session", 2,
                new ClockStamp(0, sameClock ? f.Now.Domain : "device-clock"), f.Now,
                sameClock ? "claimed-alignment" : null);
            var fact = new CupHoldEvidence(true, HoldState.HELD, EvidenceSource.SENSOR_VERIFIED,
                provenance, f.Source, "u1", "new-amir");
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(fact)), PouringRejection.EvidenceUnknown);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManualHoldIsProtocolControlledAndNeverRelabelled(bool allow)
        {
            var f = new Fixture(manual: allow); f.Preparing(); f.Acks();
            var manual = f.Fact(EvidenceKind.SourceCupHold, source: EvidenceSource.OPERATOR_CONFIRMED);
            var result = f.Evaluate(f.Event(PouringEventKind.ReadyConditionAccepted), f.Snapshot(manual));
            if (!allow) { Rejected(result, PouringRejection.ManualEvidenceNotAllowed); return; }
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(EvidenceSource.OPERATOR_CONFIRMED, result.Proposal.GuardSnapshot.Find(EvidenceKind.SourceCupHold).Source);
            Assert.AreEqual(EvidenceSource.OPERATOR_CONFIRMED, manual.Source);
        }

        [Test]
        public void ManualObservationCannotBecomeVerifiedIdentity()
        {
            var f = new Fixture(manual: true); f.Ready();
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(f.Fact(
                EvidenceKind.SourceCupObserved, source: EvidenceSource.OPERATOR_CONFIRMED))),
                PouringRejection.ManualEvidenceNotAllowed);
        }

        [TestCase(PouringPhase.Preparing)]
        [TestCase(PouringPhase.Ready)]
        [TestCase(PouringPhase.Pouring)]
        public void AbortAtomicallyEndsEachActivePhase(PouringPhase origin)
        {
            var f = new Fixture();
            if (origin == PouringPhase.Preparing) f.Preparing();
            else if (origin == PouringPhase.Ready) f.Ready(); else f.Pouring();
            var before = f.State;
            var empty = new GuardSnapshot("abort-unknown-physical-state", f.Reference, new EvidenceFact[0]);
            var result = f.Commit(PouringEventKind.Abort, snapshot: empty);
            Assert.AreEqual(PouringPhase.Completed, f.State.Phase);
            Assert.AreEqual(TaskOutcome.ABORTED, f.State.Outcome);
            Assert.IsTrue(f.State.Terminal);
            Assert.AreEqual(origin, f.State.AbortOriginPhase);
            Assert.AreEqual("test abort", f.State.AbortReason);
            Assert.AreEqual(result.Proposal.Event.Id, f.State.AbortEventId);
            Assert.AreEqual(empty.Reference, f.State.GuardSnapshotReference);
            Assert.IsFalse(before.Terminal);
            foreach (var kind in new[] { PouringEventKind.ReadyAcknowledged, PouringEventKind.PourStart,
                PouringEventKind.PourEnd, PouringEventKind.Abort })
                Rejected(f.Evaluate(f.Event(kind)), PouringRejection.Terminal);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FirstAppliedCompletionOrAbortWins(bool abortFirst)
        {
            var f = new Fixture(); f.Pouring();
            var abort = f.Evaluate(f.Event(PouringEventKind.Abort));
            var end = f.Evaluate(f.Event(PouringEventKind.PourEnd));
            Assert.IsTrue(abort.Accepted); Assert.IsTrue(end.Accepted);
            var first = abortFirst ? abort : end;
            var late = abortFirst ? end : abort;
            Assert.IsTrue(PouringTaskStateMachine.TryApplyProposal(f.State, first.Proposal, out var committed));
            Assert.IsFalse(PouringTaskStateMachine.TryApplyProposal(committed, late.Proposal, out var unchanged));
            Assert.AreSame(committed, unchanged);
            Assert.AreEqual(abortFirst ? TaskOutcome.ABORTED : TaskOutcome.UNKNOWN, committed.Outcome);
        }

        [Test]
        public void TemporaryLossBlocksAndRequiresNewObservationSequenceToRecover()
        {
            var f = new Fixture(); f.Ready();
            f.Commit(PouringEventKind.ObservationLost, PouringRole.Supervisor, cup: f.Source);
            var lost = f.State;
            Assert.IsTrue(lost.Blocked);
            Assert.AreEqual(PouringRelation.UNKNOWN, PouringRelationEvaluator.Evaluate(lost, f.Snapshot(), f.Protocol, f.Now));
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart)), PouringRejection.Blocked);
            Rejected(f.Evaluate(f.Event(PouringEventKind.ObservationRecovered, PouringRole.Supervisor, cup: f.Source)),
                PouringRejection.EvidenceSequenceInvalid);
            f.Commit(PouringEventKind.ObservationRecovered, PouringRole.Supervisor,
                f.Snapshot(f.Fact(EvidenceKind.SourceCupObserved, sequence: 2)), f.Source);
            Assert.IsFalse(f.State.Blocked);
            Assert.IsTrue(lost.Blocked); // prior immutable commit remains lost
            f.Commit(PouringEventKind.PourStart,
                snapshot: f.Snapshot(f.Fact(EvidenceKind.SourceCupObserved, sequence: 2)));
        }

        [Test]
        public void RetiredSourceCannotRecoverOrImplicitlyRebindAndStillAcceptsAbort()
        {
            var f = new Fixture(); f.Ready();
            f.Commit(PouringEventKind.SourceRetired, PouringRole.Supervisor, cup: f.Source);
            Assert.IsTrue(f.State.SourceLifecycle.Retired); Assert.IsTrue(f.State.Blocked);
            Rejected(f.Evaluate(f.Event(PouringEventKind.ObservationRecovered, PouringRole.Supervisor, cup: f.Source)),
                PouringRejection.RetiredSource);
            var newKey = new CupIdentity(f.Source.SourceId, "new-session", f.Source.ObjectId, "CUP");
            Rejected(f.Evaluate(f.Event(PouringEventKind.ObservationRecovered, PouringRole.Supervisor, cup: newKey)),
                PouringRejection.EvidenceIdentityMismatch);
            Assert.AreEqual(f.Source, f.State.Binding.SourceCup);
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart)), PouringRejection.Blocked);
            f.Commit(PouringEventKind.Abort);
            Assert.AreEqual(TaskOutcome.ABORTED, f.State.Outcome);
        }

        [Test]
        public void RetirementInvalidatesEveryCupInSameSourceSession()
        {
            var f = new Fixture(destination: new CupIdentity("camera-a", "objects-a", 2, "CUP")); f.Ready();
            f.Commit(PouringEventKind.SourceRetired, PouringRole.Supervisor, cup: f.Source);
            Assert.IsTrue(f.State.SourceLifecycle.Retired);
            Assert.IsTrue(f.State.DestinationLifecycle.Retired);
        }

        [Test]
        public void ImmediateLossPolicyUsesSameAtomicAbortCommit()
        {
            var f = new Fixture(loss: SourceLossPolicy.ABORT_IMMEDIATE); f.Preparing();
            f.Commit(PouringEventKind.SourceRetired, PouringRole.Supervisor, cup: f.Source);
            Assert.AreEqual(TaskOutcome.ABORTED, f.State.Outcome);
            Assert.IsTrue(f.State.Terminal);
            Assert.AreEqual(PouringPhase.Preparing, f.State.AbortOriginPhase);
            Assert.AreEqual("SourceRetired", f.State.AbortReason);
        }

        [Test]
        public void MissingSourceLossTimeoutStaysBlockedWithoutAutoAbort()
        {
            var f = new Fixture(timeout: null); f.Ready();
            f.Commit(PouringEventKind.SourceRetired, PouringRole.Supervisor, cup: f.Source);
            Rejected(f.Evaluate(f.Event(PouringEventKind.SourceLossTimeout, PouringRole.Supervisor)),
                PouringRejection.TimeoutUnknownOrNotReached);
            Assert.IsTrue(f.State.Blocked); Assert.IsFalse(f.State.Terminal);
        }

        [Test]
        public void RetirementTimeoutStartsAtRetirementNotEarlierTemporaryLoss()
        {
            var f = new Fixture(); f.Ready();
            f.Commit(PouringEventKind.ObservationLost, PouringRole.Supervisor, cup: f.Source);
            f.Now = new ClockStamp(f.Now.Nanoseconds + 200000000L, f.Now.Domain);
            f.Commit(PouringEventKind.SourceRetired, PouringRole.Supervisor, cup: f.Source);
            Rejected(f.Evaluate(f.Event(PouringEventKind.SourceLossTimeout, PouringRole.Supervisor)),
                PouringRejection.TimeoutUnknownOrNotReached);
            f.Now = new ClockStamp(f.Now.Nanoseconds + 100000000L, f.Now.Domain);
            f.Commit(PouringEventKind.SourceLossTimeout, PouringRole.Supervisor);
            Assert.AreEqual(TaskOutcome.ABORTED, f.State.Outcome);
        }

        [Test]
        public void DuplicateEventIdDoesNotGenerateAnotherCommit()
        {
            var f = new Fixture();
            var evt = f.Event(PouringEventKind.SessionRequested);
            var proposal = f.Evaluate(evt);
            Assert.IsTrue(PouringTaskStateMachine.TryApplyProposal(f.State, proposal.Proposal, out f.State));
            var before = f.State;
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyAcknowledged, id: evt.Identity.Id)), PouringRejection.DuplicateEvent);
            Assert.AreSame(before, f.State);
        }

        [TestCase(5UL)]
        [TestCase(4UL)]
        public void SameOrLowerSequenceWithDifferentIdIsRejected(ulong sequence)
        {
            var f = new Fixture();
            var first = f.Evaluate(f.Event(PouringEventKind.SessionRequested, sequence: 5));
            Assert.IsTrue(PouringTaskStateMachine.TryApplyProposal(f.State, first.Proposal, out f.State));
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyAcknowledged, sequence: sequence)),
                PouringRejection.SequenceNotIncreasing);
        }

        [Test]
        public void AdmissionFencesOldSessionAndStartsNewSequenceSpace()
        {
            var f = new Fixture(); f.Ready();
            var old = f.State;
            Assert.IsTrue(PouringTaskStateMachine.TryAdmitSender(old, PouringRole.Pourer, "u1", "replacement",
                "sender-1", old.Acceptance.Revision, out f.State, out _));
            Assert.AreEqual(old.CommitSequence, f.State.CommitSequence);
            Assert.AreEqual(old.Acceptance.Revision + 1, f.State.Acceptance.Revision);
            int active = 0;
            foreach (var s in f.State.Acceptance.Senders) if (s.Role == PouringRole.Pourer && s.Admitted) active++;
            Assert.AreEqual(1, active);
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart, senderSession: "sender-1", sequence: 100)),
                PouringRejection.SenderNotAdmitted);
            // Prior Ready Ack cannot silently transfer to the new sender session.
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart)), PouringRejection.EvidenceUnknown);
            var newAck = f.Evaluate(f.Event(PouringEventKind.ReadyAcknowledged, sequence: 0));
            Assert.IsTrue(newAck.Accepted);
            Assert.IsTrue(PouringTaskStateMachine.TryApplyProposal(f.State, newAck.Proposal, out f.State));
            f.Commit(PouringEventKind.PourStart);
            Assert.IsTrue(old.Acceptance.Senders[0].Admitted);
        }

        [Test]
        public void AdmissionCompareAndSetRejectsSecondWriterAndRetiredSessionReuse()
        {
            var f = new Fixture(); f.Preparing();
            var old = f.State;
            Assert.IsTrue(PouringTaskStateMachine.TryAdmitSender(old, PouringRole.Pourer, "u1", "next",
                "sender-1", old.Acceptance.Revision, out f.State, out _));
            Assert.IsFalse(PouringTaskStateMachine.TryAdmitSender(f.State, PouringRole.Pourer, "u1", "racer",
                "sender-1", old.Acceptance.Revision, out _, out _));
            Assert.IsFalse(PouringTaskStateMachine.TryAdmitSender(f.State, PouringRole.Pourer, "u1", "sender-1",
                "next", f.State.Acceptance.Revision, out _, out _));
            Assert.IsFalse(PouringTaskStateMachine.TryAdmitSender(f.State, PouringRole.Pourer, "u2", "wrong-user",
                "next", f.State.Acceptance.Revision, out _, out _));
        }

        [Test]
        public void InitialAdmissionCannotContainTwoSessionsForSameRole()
        {
            var f = new Fixture();
            var senders = new List<SenderSession>(f.Senders()) { new SenderSession(PouringRole.Pourer, "u1", "duplicate") };
            Assert.IsFalse(PouringTaskStateMachine.TryCreateInitial(f.Binding, f.Protocol, senders, out _, out _));
        }

        [TestCase("trial")]
        [TestCase("coordination")]
        [TestCase("authentication")]
        [TestCase("role")]
        public void EventScopeIdentityAndRoleAreValidated(string variation)
        {
            var f = new Fixture(); f.Ready();
            var evt = f.Event(PouringEventKind.PourStart,
                variation == "role" ? PouringRole.Receiver : PouringRole.Pourer,
                authenticated: variation == "authentication" ? "impostor" : null,
                trial: variation == "trial" ? "other-trial" : "trial",
                coordination: variation == "coordination" ? "other-coord" : "coord");
            Rejected(f.Evaluate(evt), variation == "role" ? PouringRejection.SenderNotAuthorized
                : variation == "authentication" ? PouringRejection.SenderMismatch : PouringRejection.SessionMismatch);
        }

        [Test]
        public void MissingEventTtlAndUnmappedEventClockRejectPackets()
        {
            var missing = new Fixture(eventAge: null);
            Rejected(missing.Evaluate(missing.Event(PouringEventKind.SessionRequested)), PouringRejection.EventStaleOrClockUnknown);
            var f = new Fixture(); f.Preparing();
            Rejected(f.Evaluate(f.Event(PouringEventKind.Abort, timestamp: new ClockStamp(1, "unknown-clock"))),
                PouringRejection.EventStaleOrClockUnknown);
        }

        [Test]
        public void AuthorityRestorePreservesHistoryAdmissionAndReplayRejection()
        {
            var f = new Fixture(); f.Ready();
            Assert.IsTrue(PouringTaskStateMachine.TryAdmitSender(f.State, PouringRole.Pourer, "u1", "new",
                "sender-1", f.State.Acceptance.Revision, out f.State, out _));
            var snapshot = PouringTaskStateMachine.CaptureAuthority(f.State);
            Assert.IsTrue(PouringTaskStateMachine.TryRestoreAuthority(snapshot, f.Binding, f.Protocol, out var restored, out _));
            Assert.AreSame(f.State, restored);
            Assert.AreSame(f.State.Acceptance, restored.Acceptance);
            var first = restored.Acceptance.AcceptedEvents[0].Identity;
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyAcknowledged, id: first.Id)), PouringRejection.DuplicateEvent);
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyAcknowledged, senderSession: "sender-1")),
                PouringRejection.SenderNotAdmitted);
            f.Commit(PouringEventKind.ReadyAcknowledged);
            f.Commit(PouringEventKind.PourStart);
        }

        [TestCase("state")]
        [TestCase("acceptance")]
        [TestCase("count")]
        [TestCase("senders")]
        [TestCase("protocol")]
        [TestCase("mixed")]
        public void IncompleteOrMixedAuthoritySnapshotCannotRestore(string variation)
        {
            var f = new Fixture(); f.Ready();
            var other = new Fixture();
            var snapshot = new PouringAuthoritySnapshot(variation == "state" ? null : f.State,
                variation == "acceptance" ? null : variation == "mixed" ? other.State.Acceptance : f.State.Acceptance,
                variation == "protocol" ? new TrialProtocolReference("wrong", "1", "wrong") : f.Reference,
                f.State.Acceptance.AcceptedEvents.Count + (variation == "count" ? 1 : 0),
                f.State.Acceptance.Senders.Count + (variation == "senders" ? 1 : 0));
            Assert.IsFalse(PouringTaskStateMachine.TryRestoreAuthority(snapshot, f.Binding, f.Protocol,
                out var restored, out var reason));
            Assert.IsNull(restored);
            Assert.AreEqual(PouringRejection.IncompleteHistory, reason);
        }

        [Test]
        public void RelationWithoutActiveSessionIsIndependent()
        {
            var f = new Fixture();
            Assert.AreEqual(PouringRelation.INDEPENDENT, PouringRelationEvaluator.Evaluate(null, null, null, f.Now));
            Assert.AreEqual(PouringRelation.INDEPENDENT, PouringRelationEvaluator.Evaluate(f.State, null, f.Protocol, f.Now));
            f.Completed();
            Assert.AreEqual(PouringRelation.INDEPENDENT, PouringRelationEvaluator.Evaluate(f.State, null, f.Protocol, f.Now));
        }

        [Test]
        public void KnownConflictOutranksMissingHoldEvidence()
        {
            var f = new Fixture(); f.Preparing();
            var evidence = f.Snapshot(f.Fact(EvidenceKind.NoConflict, TruthValue.FALSE), EvidenceKind.SourceCupHold);
            Assert.AreEqual(PouringRelation.CONFLICT, PouringRelationEvaluator.Evaluate(f.State, evidence, f.Protocol, f.Now));
        }

        [Test]
        public void MissingEvidenceOutranksExplicitFalsePreparation()
        {
            var f = new Fixture(); f.Preparing();
            var evidence = f.Snapshot(f.Fact(EvidenceKind.Preparation, TruthValue.FALSE), EvidenceKind.DestinationCupObserved);
            Assert.AreEqual(PouringRelation.UNKNOWN, PouringRelationEvaluator.Evaluate(f.State, evidence, f.Protocol, f.Now));
        }

        [TestCase(TruthValue.UNKNOWN, PouringRelation.UNKNOWN)]
        [TestCase(TruthValue.FALSE, PouringRelation.NOT_READY)]
        [TestCase(TruthValue.TRUE, PouringRelation.COMPLEMENTARY)]
        public void RelationUsesTriValuedPreparation(TruthValue truth, PouringRelation expected)
        {
            var f = new Fixture(); f.Preparing();
            Assert.AreEqual(expected, PouringRelationEvaluator.Evaluate(f.State,
                f.Snapshot(f.Fact(EvidenceKind.Preparation, truth)), f.Protocol, f.Now));
        }

        [Test]
        public void ComplementaryDoesNotCommitOrReplaceReadyAcknowledgements()
        {
            var f = new Fixture(); f.Preparing();
            var before = f.State;
            Assert.AreEqual(PouringRelation.COMPLEMENTARY, PouringRelationEvaluator.Evaluate(f.State, f.Snapshot(), f.Protocol, f.Now));
            Assert.AreSame(before, f.State);
            Rejected(f.Evaluate(f.Event(PouringEventKind.ReadyConditionAccepted)), PouringRejection.EvidenceUnknown);
        }

        [TestCase("writer")]
        [TestCase("session")]
        [TestCase("trial")]
        public void UntrustedEvidenceCannotAuthorizePouring(string variation)
        {
            var f = new Fixture(); f.Ready();
            var fact = f.Fact(EvidenceKind.SourceCupHold,
                writer: variation == "writer" ? "unknown" : "provider",
                writerSession: variation == "session" ? "retired-provider" : "provider-session",
                trial: variation == "trial" ? "wrong" : "trial");
            Assert.IsFalse(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(fact)).Accepted);
        }

        [Test]
        public void EqualEvidenceSequenceCannotChangePayload()
        {
            var f = new Fixture(); f.Ready();
            Rejected(f.Evaluate(f.Event(PouringEventKind.PourStart), f.Snapshot(f.Fact(
                EvidenceKind.Preparation, TruthValue.FALSE, sequence: 1))), PouringRejection.EvidenceSequenceInvalid);
        }

        [Test]
        public void SnapshotCollectionsAndRejectedEvaluationDoNotMutateInputs()
        {
            var f = new Fixture(); f.Preparing();
            var list = new List<EvidenceFact>(f.Facts);
            var snapshot = new GuardSnapshot("immutable", f.Reference, list);
            var count = list.Count; list.Clear();
            Assert.AreEqual(count, snapshot.Facts.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<EvidenceFact>)snapshot.Facts).Clear());
            var before = f.State;
            var records = before.Acceptance.AcceptedEvents.Count;
            var result = f.Evaluate(f.Event(PouringEventKind.PourStart), snapshot);
            Assert.IsFalse(result.Accepted); Assert.AreSame(before, result.NextState);
            Assert.AreEqual(records, before.Acceptance.AcceptedEvents.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<SenderSession>)before.Acceptance.Senders).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<AcceptedEventRecord>)before.Acceptance.AcceptedEvents).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<EvidenceFact>)before.EvidenceHistory).Clear());
        }

        [Test]
        public void ProtocolDefensivelyCopiesWriterAndAssessorCollections()
        {
            var f = new Fixture();
            var writers = new List<EvidenceWriter> { new EvidenceWriter("provider", "session") };
            var assessors = new List<string> { "assessor" };
            var protocol = new TrialProtocol(f.Reference, 1, 1, 1, 1, 1, 1, false, false,
                SourceLossPolicy.UNKNOWN, null, EvidenceQuality.UNKNOWN, writers, assessors);
            writers.Clear(); assessors.Clear();
            Assert.AreEqual(1, protocol.Writers.Count); Assert.AreEqual(1, protocol.Assessors.Count);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)protocol.Assessors).Clear());
        }

        [Test]
        public void AppendOnlyAssessmentRevisionsCorrectAggregateWithoutChangingCompletedCommit()
        {
            var f = new Fixture(); f.Completed();
            var completed = f.State;
            var commit = completed.CommitSequence;
            var empty = new OutcomeAssessmentHistory();
            Assert.AreEqual(TaskOutcome.UNKNOWN, empty.Aggregate(completed));
            Assert.IsTrue(empty.TryAppend(completed, f.Assessment(TaskOutcome.SUCCESS), out var first, out _));
            Assert.AreEqual(TaskOutcome.SUCCESS, first.Aggregate(completed));
            Assert.IsTrue(first.TryAppend(completed, f.Assessment(TaskOutcome.FAILURE, 2), out var second, out _));
            Assert.AreEqual(TaskOutcome.FAILURE, second.Aggregate(completed));
            Assert.AreEqual(0, empty.Assessments.Count); Assert.AreEqual(1, first.Assessments.Count);
            Assert.AreEqual(TaskOutcome.SUCCESS, first.Assessments[0].AssessedOutcome);
            Assert.AreEqual(2, second.Assessments.Count);
            Assert.AreEqual(TaskOutcome.UNKNOWN, completed.Outcome); Assert.AreEqual(commit, completed.CommitSequence);
            Assert.AreSame(completed, f.State);
            Assert.Throws<NotSupportedException>(() => ((IList<OutcomeAssessment>)second.Assessments).Clear());
            Assert.IsFalse(second.TryAppend(completed, f.Assessment(TaskOutcome.SUCCESS, 2), out _, out _));
        }

        [TestCase("quality")]
        [TestCase("assessor")]
        [TestCase("session")]
        [TestCase("commit")]
        public void InvalidAssessmentCannotCertifySuccess(string variation)
        {
            var f = new Fixture(); f.Completed();
            var assessment = f.Assessment(TaskOutcome.SUCCESS,
                quality: variation == "quality" ? EvidenceQuality.OPERATOR_REPORTED : EvidenceQuality.VIDEO_VERIFIED,
                assessor: variation == "assessor" ? "unknown" : "assessor",
                session: variation == "session" ? "other" : "coord",
                commit: variation == "commit" ? f.State.CommitSequence - 1 : f.State.CommitSequence);
            var history = new OutcomeAssessmentHistory();
            Assert.IsFalse(history.TryAppend(f.State, assessment, out var unchanged, out _));
            Assert.AreSame(history, unchanged);
            Assert.AreEqual(TaskOutcome.UNKNOWN, history.Aggregate(f.State));
        }

        [Test]
        public void AssessmentCannotOverwriteAbortOutcome()
        {
            var f = new Fixture(); f.Preparing(); f.Commit(PouringEventKind.Abort);
            var aborted = f.State;
            Assert.IsTrue(new OutcomeAssessmentHistory().TryAppend(aborted, f.Assessment(TaskOutcome.SUCCESS), out var history, out _));
            Assert.AreEqual(TaskOutcome.SUCCESS, history.Assessments[0].AssessedOutcome);
            Assert.AreEqual(TaskOutcome.ABORTED, history.Aggregate(aborted));
            Assert.AreEqual(TaskOutcome.ABORTED, aborted.Outcome);
            Assert.AreSame(aborted, f.State);
        }
    }
}
#endif
