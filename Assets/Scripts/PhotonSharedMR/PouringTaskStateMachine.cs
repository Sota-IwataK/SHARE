using System;
using System.Collections.Generic;

namespace ShareMr.Pouring
{
    /// <summary>
    /// Pure proposal builder. Authenticated sender/verified evidence are adapter assertions.
    /// This class does not authenticate networks, publish commands, commit Photon state or read clocks.
    /// Lifecycle facts are explicit supervisor-authorized inputs, never an implicit timer.
    /// </summary>
    public static class PouringTaskStateMachine
    {
        public static bool TryCreateInitial(PouringSessionBinding binding, TrialProtocol protocol,
            IEnumerable<SenderSession> admittedSenders, out PouringSessionState state, out PouringRejection rejection)
        {
            state = null; rejection = PouringRejection.InvalidBinding;
            if (binding == null || !binding.IsValid) return false;
            rejection = PouringRejection.InvalidProtocol;
            if (protocol == null || !protocol.IsValid || !binding.Protocol.Equals(protocol.Reference)) return false;
            var acceptance = new EventAcceptanceState(admittedSenders, new AcceptedEventRecord[0], 0, 0);
            var candidate = new PouringSessionState(binding, protocol, PouringPhase.Independent,
                TaskOutcome.UNKNOWN, false, false, false, BeadTransferState.NOT_STARTED,
                new CupLifecycle(binding.SourceCup), new CupLifecycle(binding.DestinationCup),
                acceptance, new EvidenceFact[0]);
            rejection = ValidateState(candidate);
            if (rejection != PouringRejection.None) return false;
            state = candidate; return true;
        }

        public static TransitionResult Evaluate(PouringSessionState current, PouringEvent evt,
            PouringSessionBinding binding, GuardSnapshot evidence, TrialProtocol protocol,
            EventAcceptanceState acceptance, ClockStamp now)
        {
            PouringRejection fail = ValidateState(current);
            if (fail != PouringRejection.None) return Reject(current, fail);
            if (binding == null || !current.Binding.Matches(binding)) return Reject(current, PouringRejection.InvalidBinding);
            if (protocol == null || !protocol.IsValid || !current.Protocol.SameContract(protocol))
                return Reject(current, PouringRejection.InvalidProtocol);
            if (!ReferenceEquals(current.Acceptance, acceptance)) return Reject(current, PouringRejection.AcceptanceMismatch);
            if (current.Terminal) return Reject(current, PouringRejection.Terminal);
            if (evidence == null || !evidence.IsValid || !evidence.Protocol.Equals(binding.Protocol))
                return Reject(current, PouringRejection.EvidenceUnknown);
            SenderSession sender;
            fail = ValidateEvent(current, evt, protocol, now, out sender);
            if (fail != PouringRejection.None) return Reject(current, fail);

            var phase = current.Phase;
            var outcome = current.Outcome;
            bool terminal = false, pourerReady = current.PourerReady, receiverReady = current.ReceiverReady;
            var transfer = current.Transfer;
            var source = current.SourceLifecycle;
            var destination = current.DestinationLifecycle;
            PouringPhase? abortOrigin = null;
            string abortReason = null, abortId = null;
            bool abort = false;
            var kind = evt.Identity.Kind;

            switch (kind)
            {
                case PouringEventKind.SessionRequested:
                    if (phase != PouringPhase.Independent) return Reject(current, PouringRejection.IllegalTransition);
                    fail = Require(current, evidence, protocol, now, EvidenceKind.SessionUnique,
                        EvidenceKind.NoUnfinishedSession, EvidenceKind.ParticipantsConnected,
                        EvidenceKind.SourceRobotConnected, EvidenceKind.DestinationRobotConnected,
                        EvidenceKind.SourceCupObserved, EvidenceKind.DestinationCupObserved);
                    if (fail != PouringRejection.None) return Reject(current, fail);
                    if (current.Blocked) return Reject(current, PouringRejection.Blocked);
                    phase = PouringPhase.Preparing;
                    break;
                case PouringEventKind.ReadyAcknowledged:
                    if (phase != PouringPhase.Preparing && phase != PouringPhase.Ready)
                        return Reject(current, PouringRejection.IllegalTransition);
                    if (sender.Role == PouringRole.Pourer) pourerReady = true; else receiverReady = true;
                    break;
                case PouringEventKind.ReadyConditionAccepted:
                    if (phase != PouringPhase.Preparing) return Reject(current, PouringRejection.IllegalTransition);
                    fail = CheckReadiness(current, evidence, protocol, now, true);
                    if (fail != PouringRejection.None) return Reject(current, fail);
                    phase = PouringPhase.Ready;
                    break;
                case PouringEventKind.PourStart:
                    if (phase != PouringPhase.Ready) return Reject(current, PouringRejection.IllegalTransition);
                    fail = CheckReadiness(current, evidence, protocol, now, true);
                    if (fail != PouringRejection.None) return Reject(current, fail);
                    phase = PouringPhase.Pouring; transfer = BeadTransferState.IN_PROGRESS;
                    break;
                case PouringEventKind.PourEnd:
                    if (phase != PouringPhase.Pouring) return Reject(current, PouringRejection.IllegalTransition);
                    // Ending is independent of OPEN, held state, geometry and outcome assessment.
                    fail = Require(current, evidence, protocol, now, EvidenceKind.EndConfirmed);
                    if (fail != PouringRejection.None) return Reject(current, fail);
                    phase = PouringPhase.Completed; terminal = true;
                    outcome = TaskOutcome.UNKNOWN; transfer = BeadTransferState.STOPPED;
                    break;
                case PouringEventKind.Abort:
                    if (phase == PouringPhase.Independent) return Reject(current, PouringRejection.IllegalTransition);
                    if (!PouringValues.Text(evt.Reason)) return Reject(current, PouringRejection.InvalidEvent);
                    abort = true; abortReason = evt.Reason;
                    break;
                case PouringEventKind.ObservationLost:
                case PouringEventKind.ObservationRecovered:
                case PouringEventKind.SourceRetired:
                    if (phase == PouringPhase.Independent || !evt.LifecycleCup.HasValue
                        || !evt.LifecycleCup.Value.IsValid) return Reject(current, PouringRejection.InvalidLifecycle);
                    var cup = evt.LifecycleCup.Value;
                    bool isSource = cup == binding.SourceCup;
                    bool isDestination = cup == binding.DestinationCup;
                    if (!isSource && !isDestination) return Reject(current, PouringRejection.EvidenceIdentityMismatch);
                    var lifecycle = isSource ? source : destination;
                    if (lifecycle.Retired) return Reject(current, PouringRejection.RetiredSource);
                    if (kind == PouringEventKind.ObservationRecovered)
                    {
                        if (!lifecycle.ObservationLost) return Reject(current, PouringRejection.InvalidLifecycle);
                        var observationKind = isSource ? EvidenceKind.SourceCupObserved : EvidenceKind.DestinationCupObserved;
                        fail = Require(current, evidence, protocol, now, observationKind,
                            isSource ? EvidenceKind.SourceRobotConnected : EvidenceKind.DestinationRobotConnected,
                            EvidenceKind.NoConflict);
                        if (fail != PouringRejection.None) return Reject(current, fail);
                        var observation = evidence.Find(observationKind);
                        if (lifecycle.LastObservationSequence.HasValue
                            && observation.Provenance.Sequence <= lifecycle.LastObservationSequence.Value)
                            return Reject(current, PouringRejection.EvidenceSequenceInvalid);
                        lifecycle = new CupLifecycle(cup, false, false, observation.Provenance.Sequence);
                        if (isSource) source = lifecycle; else destination = lifecycle;
                    }
                    else if (kind == PouringEventKind.ObservationLost)
                    {
                        if (lifecycle.ObservationLost) return Reject(current, PouringRejection.InvalidLifecycle);
                        lifecycle = new CupLifecycle(cup, true, false, lifecycle.LastObservationSequence, now);
                        if (isSource) source = lifecycle; else destination = lifecycle;
                    }
                    else
                    {
                        // Retirement applies to the entire (source_id, source_session_id) namespace.
                        if (source.Cup.SameSource(cup))
                            source = new CupLifecycle(source.Cup, true, true, source.LastObservationSequence, now);
                        if (destination.Cup.SameSource(cup))
                            destination = new CupLifecycle(destination.Cup, true, true, destination.LastObservationSequence, now);
                        if (protocol.SourceLossPolicy == SourceLossPolicy.ABORT_IMMEDIATE)
                        { abort = true; abortReason = "SourceRetired"; }
                    }
                    break;
                case PouringEventKind.SourceLossTimeout:
                    if (phase == PouringPhase.Independent) return Reject(current, PouringRejection.IllegalTransition);
                    if (protocol.SourceLossPolicy != SourceLossPolicy.BLOCK_AND_ABORT_ON_TIMEOUT
                        || !TimeoutReached(source, protocol.SourceLossTimeoutMs, now)
                            && !TimeoutReached(destination, protocol.SourceLossTimeoutMs, now))
                        return Reject(current, PouringRejection.TimeoutUnknownOrNotReached);
                    abort = true; abortReason = "SourceLossTimeout";
                    break;
                default:
                    return Reject(current, PouringRejection.InvalidEvent);
            }
            if (abort)
            {
                abortOrigin = current.Phase; abortId = evt.Identity.Id;
                phase = PouringPhase.Completed; outcome = TaskOutcome.ABORTED; terminal = true;
                transfer = BeadTransferState.STOPPED;
            }

            if (acceptance.CommitSequence == ulong.MaxValue || acceptance.Revision == ulong.MaxValue)
                return Reject(current, PouringRejection.SequenceExhausted);
            ulong sequence = acceptance.CommitSequence + 1;
            var senders = new List<SenderSession>();
            foreach (var s in acceptance.Senders)
                senders.Add(ReferenceEquals(s, sender)
                    ? new SenderSession(s.Role, s.SenderId, s.Id, true, true, evt.Identity.SenderSequence) : s);
            var records = new List<AcceptedEventRecord>(acceptance.AcceptedEvents)
                { new AcceptedEventRecord(evt.Identity, sequence, evidence.Reference) };
            var nextAcceptance = new EventAcceptanceState(senders, records, sequence, acceptance.Revision + 1);
            var history = CaptureEvidence(current, evidence, protocol, now);
            source = CaptureObservation(source, history, EvidenceKind.SourceCupObserved);
            destination = CaptureObservation(destination, history, EvidenceKind.DestinationCupObserved);
            var next = new PouringSessionState(binding, current.Protocol, phase, outcome, terminal,
                pourerReady, receiverReady, transfer, source, destination, nextAcceptance, history,
                abortOrigin, abortReason, abortId, evidence.Reference);
            return new TransitionResult(true, next, PouringRejection.None,
                new CommitProposal(current, next, evt.Identity, evidence));
        }

        // Apply proposals in authority acceptance order; competing proposals cannot both apply.
        public static bool TryApplyProposal(PouringSessionState current, CommitProposal proposal, out PouringSessionState next)
        {
            next = current;
            if (proposal == null || !ReferenceEquals(proposal.ExpectedState, current)
                || current == null || current.Terminal || proposal.ExpectedCommitSequence != current.CommitSequence
                || proposal.ExpectedAcceptanceRevision != current.Acceptance.Revision
                || ValidateState(proposal.NextState) != PouringRejection.None) return false;
            next = proposal.NextState; return true;
        }

        // Caller supplies a verified admission decision and compare-and-set expectations.
        // Old session is fenced at acceptance, not assumed incapable of sending packets.
        public static bool TryAdmitSender(PouringSessionState current, PouringRole role, string senderId,
            string newSessionId, string expectedOldSessionId, ulong expectedAcceptanceRevision,
            out PouringSessionState next, out PouringRejection rejection)
        {
            next = current; rejection = ValidateState(current);
            if (rejection != PouringRejection.None) return false;
            rejection = PouringRejection.AdmissionConflict;
            if (current.Terminal || !PouringValues.Defined(role) || !PouringValues.Text(newSessionId)
                || !PouringValues.Same(current.Binding.User(role), senderId)
                || current.Acceptance.Revision != expectedAcceptanceRevision
                || current.Acceptance.Revision == ulong.MaxValue) return false;
            SenderSession old = null;
            foreach (var s in current.Acceptance.Senders)
            {
                if (PouringValues.Same(s.SenderId, senderId) && PouringValues.Same(s.Id, newSessionId)) return false;
                if (s.Role == role && s.Admitted) old = s;
            }
            if (old == null || !PouringValues.Same(old.Id, expectedOldSessionId)) return false;
            var senders = new List<SenderSession>();
            foreach (var s in current.Acceptance.Senders)
                senders.Add(ReferenceEquals(s, old)
                    ? new SenderSession(s.Role, s.SenderId, s.Id, false, s.HasSequence, s.LastAcceptedSequence) : s);
            senders.Add(new SenderSession(role, senderId, newSessionId));
            var acceptance = new EventAcceptanceState(senders, current.Acceptance.AcceptedEvents,
                current.CommitSequence, current.Acceptance.Revision + 1);
            next = CopyWithAcceptance(current, acceptance);
            rejection = PouringRejection.None; return true;
        }

        public static PouringAuthoritySnapshot CaptureAuthority(PouringSessionState current)
        {
            if (ValidateState(current) != PouringRejection.None) throw new ArgumentException("Invalid state", nameof(current));
            return new PouringAuthoritySnapshot(current, current.Acceptance, current.Binding.Protocol,
                current.Acceptance.AcceptedEvents.Count, current.Acceptance.Senders.Count);
        }

        public static bool TryRestoreAuthority(PouringAuthoritySnapshot snapshot, PouringSessionBinding binding,
            TrialProtocol protocol, out PouringSessionState restored, out PouringRejection rejection)
        {
            restored = null; rejection = PouringRejection.IncompleteHistory;
            if (snapshot == null || snapshot.State == null || snapshot.Acceptance == null
                || !ReferenceEquals(snapshot.State.Acceptance, snapshot.Acceptance)
                || snapshot.AcceptedEventCount != snapshot.Acceptance.AcceptedEvents.Count
                || snapshot.SenderSessionCount != snapshot.Acceptance.Senders.Count) return false;
            if (binding == null || !snapshot.State.Binding.Matches(binding)
                || !snapshot.Protocol.Equals(binding.Protocol)) return false;
            if (protocol == null || !snapshot.State.Protocol.SameContract(protocol)) return false;
            rejection = ValidateState(snapshot.State);
            if (rejection != PouringRejection.None) return false;
            restored = snapshot.State; return true;
        }

        internal static PouringRejection ValidateState(PouringSessionState state)
        {
            if (state == null || state.Binding == null || !state.Binding.IsValid || state.Protocol == null
                || !state.Protocol.IsValid || !state.Binding.Protocol.Equals(state.Protocol.Reference))
                return PouringRejection.InvalidState;
            var a = state.Acceptance;
            if (a == null || state.SourceLifecycle == null || state.DestinationLifecycle == null)
                return PouringRejection.IncompleteHistory;
            if (!PouringValues.Defined(state.Phase) || !PouringValues.Defined(state.Outcome)
                || !PouringValues.Defined(state.Transfer) || state.Terminal != (state.Phase == PouringPhase.Completed)
                || !state.Terminal && state.Outcome != TaskOutcome.UNKNOWN
                || state.SourceLifecycle.Cup != state.Binding.SourceCup
                || state.DestinationLifecycle.Cup != state.Binding.DestinationCup)
                return PouringRejection.InvalidState;
            if (state.Outcome == TaskOutcome.ABORTED && (!state.AbortOriginPhase.HasValue
                || !PouringValues.Text(state.AbortReason) || !PouringValues.Text(state.AbortEventId)))
                return PouringRejection.InvalidState;
            if (a.CommitSequence != (ulong)a.AcceptedEvents.Count || a.Revision < a.CommitSequence)
                return PouringRejection.IncompleteHistory;
            var admittedRoles = new HashSet<PouringRole>();
            var senderKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sender in a.Senders)
            {
                if (sender == null || !PouringValues.Defined(sender.Role) || !PouringValues.Text(sender.Id)
                    || !PouringValues.Text(sender.SenderId) || !PouringValues.Same(state.Binding.User(sender.Role), sender.SenderId)
                    || !senderKeys.Add(sender.SenderId + "\n" + sender.Id)
                    || sender.Admitted && !admittedRoles.Add(sender.Role)) return PouringRejection.IncompleteHistory;
            }
            if (!admittedRoles.Contains(PouringRole.Pourer) || !admittedRoles.Contains(PouringRole.Receiver))
                return PouringRejection.IncompleteHistory;
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            var last = new Dictionary<SenderSession, ulong>();
            ulong sequence = 0;
            foreach (var record in a.AcceptedEvents)
            {
                if (record == null || record.Identity == null || !record.Identity.IsValid
                    || !eventIds.Add(record.Identity.Id) || record.CommitSequence != ++sequence
                    || !PouringValues.Text(record.GuardReference)
                    || !PouringValues.Same(record.Identity.TrialId, state.Binding.TrialId)
                    || !PouringValues.Same(record.Identity.SessionId, state.Binding.SessionId))
                    return PouringRejection.IncompleteHistory;
                var sender = a.Find(record.Identity.SenderId, record.Identity.SenderSessionId);
                if (sender == null || last.TryGetValue(sender, out ulong previous) && record.Identity.SenderSequence <= previous)
                    return PouringRejection.IncompleteHistory;
                last[sender] = record.Identity.SenderSequence;
            }
            foreach (var sender in a.Senders)
            {
                bool present = last.TryGetValue(sender, out ulong value);
                if (present != sender.HasSequence || present && value != sender.LastAcceptedSequence)
                    return PouringRejection.IncompleteHistory;
            }
            if (a.AcceptedEvents.Count > 0 && !PouringValues.Same(state.GuardSnapshotReference,
                a.AcceptedEvents[a.AcceptedEvents.Count - 1].GuardReference)) return PouringRejection.IncompleteHistory;
            return PouringRejection.None;
        }

        private static PouringRejection ValidateEvent(PouringSessionState state, PouringEvent evt,
            TrialProtocol protocol, ClockStamp now, out SenderSession sender)
        {
            sender = null;
            if (evt == null || evt.Identity == null || !evt.Identity.IsValid) return PouringRejection.InvalidEvent;
            var id = evt.Identity;
            if (!PouringValues.Same(evt.AuthenticatedSenderId, id.SenderId)) return PouringRejection.SenderMismatch;
            if (!PouringValues.Same(id.TrialId, state.Binding.TrialId)
                || !PouringValues.Same(id.SessionId, state.Binding.SessionId)) return PouringRejection.SessionMismatch;
            foreach (var record in state.Acceptance.AcceptedEvents)
                if (PouringValues.Same(record.Identity.Id, id.Id)) return PouringRejection.DuplicateEvent;
            sender = state.Acceptance.Find(id.SenderId, id.SenderSessionId);
            if (sender == null || !sender.Admitted) return PouringRejection.SenderNotAdmitted;
            if (sender.HasSequence && id.SenderSequence <= sender.LastAcceptedSequence)
                return PouringRejection.SequenceNotIncreasing;
            bool permitted;
            switch (id.Kind)
            {
                case PouringEventKind.SessionRequested:
                case PouringEventKind.ReadyAcknowledged:
                case PouringEventKind.ReadyConditionAccepted:
                    permitted = sender.Role == PouringRole.Pourer || sender.Role == PouringRole.Receiver; break;
                case PouringEventKind.PourStart: case PouringEventKind.PourEnd:
                    permitted = sender.Role == PouringRole.Pourer; break;
                case PouringEventKind.Abort:
                    permitted = true; break;
                default:
                    permitted = sender.Role == PouringRole.Supervisor; break;
            }
            if (!permitted) return PouringRejection.SenderNotAuthorized;
            // No cross-domain subtraction and no time read. Missing event TTL rejects even an Abort packet.
            return id.SourceTimestamp.Within(now, protocol.EventMaxAgeMs)
                ? PouringRejection.None : PouringRejection.EventStaleOrClockUnknown;
        }

        internal static PouringRejection InspectFact(PouringSessionState state, GuardSnapshot snapshot,
            TrialProtocol protocol, EvidenceKind kind, ClockStamp now)
        {
            if (state == null || snapshot == null || !snapshot.IsValid || protocol == null || !protocol.IsValid
                || !state.Binding.Protocol.Equals(snapshot.Protocol) || !state.Protocol.SameContract(protocol))
                return PouringRejection.EvidenceUnknown;
            var f = snapshot.Find(kind);
            if (f == null || f.Provenance == null || !f.Provenance.IsValid
                || f.Source == EvidenceSource.UNKNOWN || !protocol.AllowsWriter(f.Provenance))
                return PouringRejection.EvidenceUnknown;
            var p = f.Provenance;
            if (!PouringValues.Same(p.TrialId, state.Binding.TrialId)
                || !PouringValues.Same(p.SessionId, state.Binding.SessionId)) return PouringRejection.SessionMismatch;
            if (!p.EvaluationTimestamp.Within(now, protocol.Age(kind))) return PouringRejection.EvidenceUnknown;
            bool left = kind == EvidenceKind.SourceCupHold || kind == EvidenceKind.SourceCupObserved
                || kind == EvidenceKind.SourceRobotConnected;
            bool right = kind == EvidenceKind.DestinationCupHold || kind == EvidenceKind.DestinationCupObserved
                || kind == EvidenceKind.DestinationRobotConnected;
            bool cupFact = kind == EvidenceKind.SourceCupHold || kind == EvidenceKind.DestinationCupHold
                || kind == EvidenceKind.SourceCupObserved || kind == EvidenceKind.DestinationCupObserved;
            var expectedCup = left ? state.Binding.SourceCup : state.Binding.DestinationCup;
            if (cupFact && (!f.Cup.HasValue || !f.Cup.Value.IsValid || f.Cup.Value != expectedCup))
                return PouringRejection.EvidenceIdentityMismatch;
            if ((left || right) && kind != EvidenceKind.SourceCupObserved && kind != EvidenceKind.DestinationCupObserved)
            {
                if (!PouringValues.Same(f.UserId, left ? state.Binding.PourerUserId : state.Binding.ReceiverUserId)
                    || !PouringValues.Same(f.RobotId, left ? state.Binding.PourerRobotId : state.Binding.ReceiverRobotId))
                    return PouringRejection.EvidenceIdentityMismatch;
            }
            if (cupFact && (left ? state.SourceLifecycle.Retired : state.DestinationLifecycle.Retired))
                return PouringRejection.RetiredSource;
            bool hold = kind == EvidenceKind.SourceCupHold || kind == EvidenceKind.DestinationCupHold;
            if (hold && (f.Hold == HoldState.UNKNOWN || (f.Hold == HoldState.HELD) != (f.Truth == TruthValue.TRUE)
                || (f.Hold == HoldState.NOT_HELD) != (f.Truth == TruthValue.FALSE))) return PouringRejection.EvidenceUnknown;
            if (f.Source == EvidenceSource.OPERATOR_CONFIRMED
                && !(hold && protocol.AllowManualHold || kind == EvidenceKind.Preparation && protocol.AllowManualPreparation
                    || kind == EvidenceKind.EndConfirmed)) return PouringRejection.ManualEvidenceNotAllowed;
            if ((hold || kind == EvidenceKind.Preparation || kind == EvidenceKind.SourceCupObserved
                || kind == EvidenceKind.DestinationCupObserved) && f.Source == EvidenceSource.AUTHORITY_CONFIRMED)
                return PouringRejection.EvidenceUnknown;
            foreach (var old in state.EvidenceHistory)
            {
                if (old.Kind != kind) continue;
                if (!PouringValues.Same(old.Provenance.SenderId, p.SenderId)
                    || !PouringValues.Same(old.Provenance.SenderSessionId, p.SenderSessionId)
                    || p.Sequence < old.Provenance.Sequence
                    || p.Sequence == old.Provenance.Sequence && !f.Same(old)) return PouringRejection.EvidenceSequenceInvalid;
            }
            return PouringRejection.None;
        }

        internal static PouringRejection Require(PouringSessionState state, GuardSnapshot snapshot,
            TrialProtocol protocol, ClockStamp now, params EvidenceKind[] kinds)
        {
            foreach (var kind in kinds)
            {
                var check = InspectFact(state, snapshot, protocol, kind, now);
                if (check != PouringRejection.None) return check;
                var truth = snapshot.Find(kind).Truth;
                if (truth == TruthValue.UNKNOWN) return PouringRejection.EvidenceUnknown;
                if (truth == TruthValue.FALSE) return PouringRejection.ConditionFalse;
            }
            return PouringRejection.None;
        }

        internal static PouringRejection CheckReadiness(PouringSessionState state, GuardSnapshot snapshot,
            TrialProtocol protocol, ClockStamp now, bool requireAcks)
        {
            if (state.Blocked) return PouringRejection.Blocked;
            var fail = Require(state, snapshot, protocol, now, EvidenceKind.NoConflict,
                EvidenceKind.NoStop, EvidenceKind.ParticipantsConnected, EvidenceKind.SourceRobotConnected,
                EvidenceKind.DestinationRobotConnected, EvidenceKind.SourceCupObserved,
                EvidenceKind.DestinationCupObserved, EvidenceKind.SourceCupHold, EvidenceKind.DestinationCupHold,
                EvidenceKind.Preparation);
            if (fail != PouringRejection.None) return fail;
            if (requireAcks && (!ReadyAckValid(state, PouringRole.Pourer, now)
                || !ReadyAckValid(state, PouringRole.Receiver, now))) return PouringRejection.EvidenceUnknown;
            return PouringRejection.None;
        }

        private static bool ReadyAckValid(PouringSessionState state, PouringRole role, ClockStamp now)
        {
            if (!(role == PouringRole.Pourer ? state.PourerReady : state.ReceiverReady)) return false;
            for (int i = state.Acceptance.AcceptedEvents.Count - 1; i >= 0; i--)
            {
                var e = state.Acceptance.AcceptedEvents[i].Identity;
                if (e.Kind != PouringEventKind.ReadyAcknowledged || !PouringValues.Same(e.SenderId, state.Binding.User(role))) continue;
                var sender = state.Acceptance.Find(e.SenderId, e.SenderSessionId);
                return sender != null && sender.Admitted && e.SourceTimestamp.Within(now, state.Protocol.OtherMaxAgeMs);
            }
            return false;
        }

        private static List<EvidenceFact> CaptureEvidence(PouringSessionState state, GuardSnapshot snapshot,
            TrialProtocol protocol, ClockStamp now)
        {
            var history = new List<EvidenceFact>(state.EvidenceHistory);
            foreach (var fact in snapshot.Facts)
            {
                if (InspectFact(state, snapshot, protocol, fact.Kind, now) != PouringRejection.None) continue;
                history.RemoveAll(old => old.Kind == fact.Kind);
                history.Add(fact);
            }
            return history;
        }

        private static CupLifecycle CaptureObservation(CupLifecycle lifecycle, List<EvidenceFact> history, EvidenceKind kind)
        {
            if (lifecycle.Retired || lifecycle.ObservationLost) return lifecycle;
            foreach (var f in history)
                if (f.Kind == kind && f.Truth == TruthValue.TRUE && f.Cup.HasValue && f.Cup.Value == lifecycle.Cup)
                    return new CupLifecycle(lifecycle.Cup, false, false, f.Provenance.Sequence);
            return lifecycle;
        }

        private static bool TimeoutReached(CupLifecycle lifecycle, long? timeoutMs, ClockStamp now)
        {
            if (!lifecycle.Retired || !lifecycle.LostAt.HasValue || !timeoutMs.HasValue || timeoutMs.Value < 0
                || timeoutMs.Value > long.MaxValue / 1000000L || !now.IsValid) return false;
            var lost = lifecycle.LostAt.Value;
            return lost.IsValid && PouringValues.Same(lost.Domain, now.Domain) && now.Nanoseconds >= lost.Nanoseconds
                && now.Nanoseconds - lost.Nanoseconds >= timeoutMs.Value * 1000000L;
        }

        private static PouringSessionState CopyWithAcceptance(PouringSessionState current, EventAcceptanceState acceptance)
            => new PouringSessionState(current.Binding, current.Protocol, current.Phase, current.Outcome,
                current.Terminal, current.PourerReady, current.ReceiverReady, current.Transfer,
                current.SourceLifecycle, current.DestinationLifecycle, acceptance, current.EvidenceHistory,
                current.AbortOriginPhase, current.AbortReason, current.AbortEventId, current.GuardSnapshotReference);

        private static TransitionResult Reject(PouringSessionState current, PouringRejection reason)
            => new TransitionResult(false, current, reason);
    }
}
