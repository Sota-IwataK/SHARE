using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ShareMr.Pouring
{
    // P0-04A: pure data/reducers only. No Unity, Photon, ROS, clock reads or IO.
    public enum PouringTaskKind { POURING = 1 }
    public enum PouringPhase { Independent, Preparing, Ready, Pouring, Completed }
    public enum TaskOutcome { UNKNOWN, SUCCESS, FAILURE, ABORTED }
    public enum HoldState { UNKNOWN, HELD, NOT_HELD }
    public enum BeadTransferState { UNKNOWN, NOT_STARTED, IN_PROGRESS, STOPPED }
    public enum TruthValue { UNKNOWN, TRUE, FALSE }
    public enum EvidenceSource { UNKNOWN, SENSOR_VERIFIED, OPERATOR_CONFIRMED, AUTHORITY_CONFIRMED }
    public enum PouringRelation { INDEPENDENT, CONFLICT, UNKNOWN, NOT_READY, COMPLEMENTARY }
    public enum PouringRole { Pourer, Receiver, Supervisor }
    public enum SourceLossPolicy { UNKNOWN, ABORT_IMMEDIATE, BLOCK_AND_ABORT_ON_TIMEOUT }
    public enum EvidenceQuality { UNKNOWN, OPERATOR_REPORTED, OBSERVER_VERIFIED, VIDEO_VERIFIED }
    public enum PouringEventKind
    {
        SessionRequested, ReadyAcknowledged, ReadyConditionAccepted, PourStart,
        PourEnd, Abort, ObservationLost, ObservationRecovered, SourceRetired, SourceLossTimeout
    }
    public enum EvidenceKind
    {
        SessionUnique, NoUnfinishedSession, ParticipantsConnected,
        SourceRobotConnected, DestinationRobotConnected, SourceCupObserved,
        DestinationCupObserved, SourceCupHold, DestinationCupHold,
        Preparation, NoConflict, NoStop, EndConfirmed
    }
    public enum PouringRejection
    {
        None, InvalidBinding, InvalidProtocol, InvalidState, IncompleteHistory,
        AcceptanceMismatch, InvalidEvent, SenderMismatch, SenderNotAdmitted,
        SenderNotAuthorized, DuplicateEvent, SequenceNotIncreasing, SessionMismatch,
        EventStaleOrClockUnknown, Terminal, IllegalTransition, EvidenceUnknown,
        ConditionFalse, EvidenceIdentityMismatch, EvidenceSequenceInvalid,
        ManualEvidenceNotAllowed, Blocked, RetiredSource, InvalidLifecycle,
        TimeoutUnknownOrNotReached, SequenceExhausted, AdmissionConflict,
        InvalidAssessment
    }

    internal static class PouringValues
    {
        internal static bool Text(string value) => !string.IsNullOrWhiteSpace(value);
        internal static bool Defined<T>(T value) where T : struct => Enum.IsDefined(typeof(T), value);
        internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
            => new List<T>(values ?? new T[0]).AsReadOnly();
        internal static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    }

    public readonly struct CupIdentity : IEquatable<CupIdentity>
    {
        public string SourceId { get; }
        public string SessionId { get; }
        public ulong ObjectId { get; }
        public string ClassId { get; }
        public CupIdentity(string sourceId, string sessionId, ulong objectId, string classId)
        { SourceId = sourceId; SessionId = sessionId; ObjectId = objectId; ClassId = classId; }
        public bool IsValid => PouringValues.Text(SourceId) && PouringValues.Text(SessionId)
            && PouringValues.Same(ClassId, "CUP");
        // Identity equality is the full key, not class, label, numeric ID or nearest pose.
        public bool Equals(CupIdentity other) => PouringValues.Same(SourceId, other.SourceId)
            && PouringValues.Same(SessionId, other.SessionId) && ObjectId == other.ObjectId;
        public override bool Equals(object obj) => obj is CupIdentity other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((SourceId == null ? 0 : StringComparer.Ordinal.GetHashCode(SourceId)) * 397
                ^ (SessionId == null ? 0 : StringComparer.Ordinal.GetHashCode(SessionId))) * 397 ^ ObjectId.GetHashCode(); }
        }
        public static bool operator ==(CupIdentity a, CupIdentity b) => a.Equals(b);
        public static bool operator !=(CupIdentity a, CupIdentity b) => !a.Equals(b);
        public bool SameSource(CupIdentity other) => PouringValues.Same(SourceId, other.SourceId)
            && PouringValues.Same(SessionId, other.SessionId);
    }

    public readonly struct TrialProtocolReference : IEquatable<TrialProtocolReference>
    {
        public string Id { get; }
        public string Version { get; }
        public string ContentRevision { get; }
        public TrialProtocolReference(string id, string version, string contentRevision)
        { Id = id; Version = version; ContentRevision = contentRevision; }
        public bool IsValid => PouringValues.Text(Id) && PouringValues.Text(Version) && PouringValues.Text(ContentRevision);
        public bool Equals(TrialProtocolReference other) => PouringValues.Same(Id, other.Id)
            && PouringValues.Same(Version, other.Version) && PouringValues.Same(ContentRevision, other.ContentRevision);
        public override bool Equals(object obj) => obj is TrialProtocolReference other && Equals(other);
        public override int GetHashCode() => (Id ?? "").GetHashCode() ^ (Version ?? "").GetHashCode()
            ^ (ContentRevision ?? "").GetHashCode();
    }

    public sealed class PouringSessionBinding
    {
        public PouringTaskKind TaskKind => PouringTaskKind.POURING;
        public string TrialId { get; }
        public string SessionId { get; }
        public string PourerUserId { get; }
        public string PourerRobotId { get; }
        public string ReceiverUserId { get; }
        public string ReceiverRobotId { get; }
        public string SupervisorId { get; }
        public CupIdentity SourceCup { get; }
        public CupIdentity DestinationCup { get; }
        public TrialProtocolReference Protocol { get; }
        public PouringSessionBinding(string trialId, string sessionId, string pourerUserId,
            string pourerRobotId, string receiverUserId, string receiverRobotId,
            CupIdentity sourceCup, CupIdentity destinationCup, TrialProtocolReference protocol,
            string supervisorId = null)
        {
            TrialId = trialId; SessionId = sessionId; PourerUserId = pourerUserId;
            PourerRobotId = pourerRobotId; ReceiverUserId = receiverUserId;
            ReceiverRobotId = receiverRobotId; SourceCup = sourceCup; DestinationCup = destinationCup;
            Protocol = protocol; SupervisorId = supervisorId;
        }
        public bool IsValid => PouringValues.Text(TrialId) && PouringValues.Text(SessionId)
            && PouringValues.Text(PourerUserId) && PouringValues.Text(ReceiverUserId)
            && PouringValues.Text(PourerRobotId) && PouringValues.Text(ReceiverRobotId)
            && !PouringValues.Same(PourerUserId, ReceiverUserId)
            && !PouringValues.Same(PourerRobotId, ReceiverRobotId)
            && SourceCup.IsValid && DestinationCup.IsValid && SourceCup != DestinationCup && Protocol.IsValid
            && (!PouringValues.Text(SupervisorId) || (!PouringValues.Same(SupervisorId, PourerUserId)
                && !PouringValues.Same(SupervisorId, ReceiverUserId)));
        public string User(PouringRole role) => role == PouringRole.Pourer ? PourerUserId
            : role == PouringRole.Receiver ? ReceiverUserId : role == PouringRole.Supervisor ? SupervisorId : null;
        public bool Matches(PouringSessionBinding other) => other != null && IsValid && other.IsValid
            && PouringValues.Same(TrialId, other.TrialId) && PouringValues.Same(SessionId, other.SessionId)
            && PouringValues.Same(PourerUserId, other.PourerUserId) && PouringValues.Same(PourerRobotId, other.PourerRobotId)
            && PouringValues.Same(ReceiverUserId, other.ReceiverUserId) && PouringValues.Same(ReceiverRobotId, other.ReceiverRobotId)
            && PouringValues.Same(SupervisorId, other.SupervisorId) && SourceCup == other.SourceCup
            && DestinationCup == other.DestinationCup && Protocol.Equals(other.Protocol);
    }

    public readonly struct ClockStamp : IEquatable<ClockStamp>
    {
        public long Nanoseconds { get; }
        public string Domain { get; }
        public ClockStamp(long nanoseconds, string domain) { Nanoseconds = nanoseconds; Domain = domain; }
        public bool IsValid => Nanoseconds >= 0 && PouringValues.Text(Domain);
        public bool Equals(ClockStamp other) => Nanoseconds == other.Nanoseconds && PouringValues.Same(Domain, other.Domain);
        public override bool Equals(object obj) => obj is ClockStamp other && Equals(other);
        public override int GetHashCode() => Nanoseconds.GetHashCode() ^ (Domain ?? "").GetHashCode();
        public bool Within(ClockStamp now, long? maximumAgeMs)
        {
            if (!IsValid || !now.IsValid || !PouringValues.Same(Domain, now.Domain)
                || !maximumAgeMs.HasValue || maximumAgeMs.Value < 0
                || maximumAgeMs.Value > long.MaxValue / 1000000L || now.Nanoseconds < Nanoseconds) return false;
            return now.Nanoseconds - Nanoseconds <= maximumAgeMs.Value * 1000000L;
        }
    }

    public sealed class EvidenceProvenance
    {
        public string TrialId { get; }
        public string SessionId { get; }
        public string SenderId { get; }
        public string SenderSessionId { get; }
        public ulong Sequence { get; }
        public ClockStamp SourceTimestamp { get; }
        // Source time mapped into the evaluation clock, never a replacement receive time.
        // Cross-clock mapping and its reference must be verified by the supplying adapter.
        public ClockStamp EvaluationTimestamp { get; }
        public string AlignmentReference { get; }
        public EvidenceProvenance(string trialId, string sessionId, string senderId, string senderSessionId,
            ulong sequence, ClockStamp sourceTimestamp, ClockStamp evaluationTimestamp, string alignmentReference = null)
        {
            TrialId = trialId; SessionId = sessionId; SenderId = senderId; SenderSessionId = senderSessionId;
            Sequence = sequence; SourceTimestamp = sourceTimestamp; EvaluationTimestamp = evaluationTimestamp;
            AlignmentReference = alignmentReference;
        }
        public bool IsValid => PouringValues.Text(TrialId) && PouringValues.Text(SessionId)
            && PouringValues.Text(SenderId) && PouringValues.Text(SenderSessionId) && SourceTimestamp.IsValid
            && EvaluationTimestamp.IsValid && (PouringValues.Same(SourceTimestamp.Domain, EvaluationTimestamp.Domain)
                ? SourceTimestamp.Equals(EvaluationTimestamp) : PouringValues.Text(AlignmentReference));
        public bool Same(EvidenceProvenance other) => other != null && PouringValues.Same(TrialId, other.TrialId)
            && PouringValues.Same(SessionId, other.SessionId) && PouringValues.Same(SenderId, other.SenderId)
            && PouringValues.Same(SenderSessionId, other.SenderSessionId) && Sequence == other.Sequence
            && SourceTimestamp.Equals(other.SourceTimestamp) && EvaluationTimestamp.Equals(other.EvaluationTimestamp)
            && PouringValues.Same(AlignmentReference, other.AlignmentReference);
    }

    public class EvidenceFact
    {
        public EvidenceKind Kind { get; }
        public TruthValue Truth { get; }
        public EvidenceSource Source { get; }
        public EvidenceProvenance Provenance { get; }
        public CupIdentity? Cup { get; }
        public string UserId { get; }
        public string RobotId { get; }
        public HoldState Hold { get; }
        public EvidenceFact(EvidenceKind kind, TruthValue truth, EvidenceSource source,
            EvidenceProvenance provenance, CupIdentity? cup = null, string userId = null, string robotId = null)
            : this(kind, truth, source, provenance, cup, userId, robotId, HoldState.UNKNOWN) { }
        protected EvidenceFact(EvidenceKind kind, TruthValue truth, EvidenceSource source,
            EvidenceProvenance provenance, CupIdentity? cup, string userId, string robotId, HoldState hold)
        {
            Kind = kind; Truth = truth; Source = source; Provenance = provenance; Cup = cup;
            UserId = userId; RobotId = robotId; Hold = hold;
        }
        public bool Same(EvidenceFact other) => other != null && Kind == other.Kind && Truth == other.Truth
            && Source == other.Source && Hold == other.Hold && Nullable.Equals(Cup, other.Cup)
            && (!Cup.HasValue || PouringValues.Same(Cup.Value.ClassId, other.Cup.Value.ClassId))
            && PouringValues.Same(UserId, other.UserId) && PouringValues.Same(RobotId, other.RobotId)
            && Provenance != null && Provenance.Same(other.Provenance);
    }

    public sealed class CupHoldEvidence : EvidenceFact
    {
        public CupHoldEvidence(bool sourceCup, HoldState hold, EvidenceSource evidenceSource,
            EvidenceProvenance provenance, CupIdentity cup, string userId, string robotId)
            : base(sourceCup ? EvidenceKind.SourceCupHold : EvidenceKind.DestinationCupHold,
                hold == HoldState.HELD ? TruthValue.TRUE : hold == HoldState.NOT_HELD ? TruthValue.FALSE : TruthValue.UNKNOWN,
                evidenceSource, provenance, cup, userId, robotId, hold) { }
    }

    public sealed class GuardSnapshot
    {
        public string Reference { get; }
        public TrialProtocolReference Protocol { get; }
        public IReadOnlyList<EvidenceFact> Facts { get; }
        public GuardSnapshot(string reference, TrialProtocolReference protocol, IEnumerable<EvidenceFact> facts)
        { Reference = reference; Protocol = protocol; Facts = PouringValues.Copy(facts); }
        public bool IsValid
        {
            get
            {
                if (!PouringValues.Text(Reference) || !Protocol.IsValid) return false;
                var kinds = new HashSet<EvidenceKind>();
                foreach (var f in Facts)
                    if (f == null || !PouringValues.Defined(f.Kind) || !PouringValues.Defined(f.Truth)
                        || !PouringValues.Defined(f.Source) || !PouringValues.Defined(f.Hold) || !kinds.Add(f.Kind)) return false;
                return true;
            }
        }
        public EvidenceFact Find(EvidenceKind kind)
        { foreach (var f in Facts) if (f != null && f.Kind == kind) return f; return null; }
    }

    public readonly struct EvidenceWriter
    {
        public string Id { get; }
        public string SessionId { get; }
        public EvidenceWriter(string id, string sessionId) { Id = id; SessionId = sessionId; }
        public bool IsValid => PouringValues.Text(Id) && PouringValues.Text(SessionId);
    }

    // No measured/default threshold is invented. Null means UNKNOWN for the consuming guard.
    // Physical geometry is supplied as a verified tri-valued fact; this class does no geometry.
    public sealed class TrialProtocol
    {
        public TrialProtocolReference Reference { get; }
        public long? EventMaxAgeMs { get; }
        public long? HoldMaxAgeMs { get; }
        public long? RobotMaxAgeMs { get; }
        public long? ObservationMaxAgeMs { get; }
        public long? PreparationMaxAgeMs { get; }
        public long? OtherMaxAgeMs { get; }
        public bool AllowManualHold { get; }
        public bool AllowManualPreparation { get; }
        public SourceLossPolicy SourceLossPolicy { get; }
        public long? SourceLossTimeoutMs { get; }
        public EvidenceQuality MinimumOutcomeQuality { get; }
        public IReadOnlyList<EvidenceWriter> Writers { get; }
        public IReadOnlyList<string> Assessors { get; }
        public TrialProtocol(TrialProtocolReference reference, long? eventMaxAgeMs, long? holdMaxAgeMs,
            long? robotMaxAgeMs, long? observationMaxAgeMs, long? preparationMaxAgeMs, long? otherMaxAgeMs,
            bool allowManualHold, bool allowManualPreparation, SourceLossPolicy sourceLossPolicy,
            long? sourceLossTimeoutMs, EvidenceQuality minimumOutcomeQuality,
            IEnumerable<EvidenceWriter> writers, IEnumerable<string> assessors)
        {
            Reference = reference; EventMaxAgeMs = eventMaxAgeMs; HoldMaxAgeMs = holdMaxAgeMs;
            RobotMaxAgeMs = robotMaxAgeMs; ObservationMaxAgeMs = observationMaxAgeMs;
            PreparationMaxAgeMs = preparationMaxAgeMs; OtherMaxAgeMs = otherMaxAgeMs;
            AllowManualHold = allowManualHold; AllowManualPreparation = allowManualPreparation;
            SourceLossPolicy = sourceLossPolicy; SourceLossTimeoutMs = sourceLossTimeoutMs;
            MinimumOutcomeQuality = minimumOutcomeQuality; Writers = PouringValues.Copy(writers);
            Assessors = PouringValues.Copy(assessors);
        }
        public bool IsValid
        {
            get
            {
                if (!Reference.IsValid || !PouringValues.Defined(SourceLossPolicy)
                    || !PouringValues.Defined(MinimumOutcomeQuality)) return false;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var w in Writers) if (!w.IsValid || !ids.Add(w.Id)) return false;
                ids.Clear();
                foreach (var a in Assessors) if (!PouringValues.Text(a) || !ids.Add(a)) return false;
                return true;
            }
        }
        public bool AllowsWriter(EvidenceProvenance provenance)
        {
            foreach (var w in Writers)
                if (PouringValues.Same(w.Id, provenance.SenderId)
                    && PouringValues.Same(w.SessionId, provenance.SenderSessionId)) return true;
            return false;
        }
        public long? Age(EvidenceKind kind)
        {
            switch (kind)
            {
                case EvidenceKind.SourceCupHold: case EvidenceKind.DestinationCupHold: return HoldMaxAgeMs;
                case EvidenceKind.SourceRobotConnected: case EvidenceKind.DestinationRobotConnected: return RobotMaxAgeMs;
                case EvidenceKind.SourceCupObserved: case EvidenceKind.DestinationCupObserved: return ObservationMaxAgeMs;
                case EvidenceKind.Preparation: return PreparationMaxAgeMs;
                default: return OtherMaxAgeMs;
            }
        }
        public bool SameContract(TrialProtocol other)
        {
            if (other == null || !Reference.Equals(other.Reference) || EventMaxAgeMs != other.EventMaxAgeMs
                || HoldMaxAgeMs != other.HoldMaxAgeMs || RobotMaxAgeMs != other.RobotMaxAgeMs
                || ObservationMaxAgeMs != other.ObservationMaxAgeMs || PreparationMaxAgeMs != other.PreparationMaxAgeMs
                || OtherMaxAgeMs != other.OtherMaxAgeMs || AllowManualHold != other.AllowManualHold
                || AllowManualPreparation != other.AllowManualPreparation || SourceLossPolicy != other.SourceLossPolicy
                || SourceLossTimeoutMs != other.SourceLossTimeoutMs || MinimumOutcomeQuality != other.MinimumOutcomeQuality
                || Writers.Count != other.Writers.Count || Assessors.Count != other.Assessors.Count) return false;
            for (int i = 0; i < Writers.Count; i++)
                if (!PouringValues.Same(Writers[i].Id, other.Writers[i].Id)
                    || !PouringValues.Same(Writers[i].SessionId, other.Writers[i].SessionId)) return false;
            for (int i = 0; i < Assessors.Count; i++) if (!PouringValues.Same(Assessors[i], other.Assessors[i])) return false;
            return true;
        }
        public bool AllowsAssessment(EvidenceQuality quality)
        {
            if (!PouringValues.Defined(quality) || MinimumOutcomeQuality == EvidenceQuality.UNKNOWN) return false;
            if (quality == MinimumOutcomeQuality) return true;
            if (MinimumOutcomeQuality == EvidenceQuality.OPERATOR_REPORTED)
                return quality == EvidenceQuality.OBSERVER_VERIFIED || quality == EvidenceQuality.VIDEO_VERIFIED;
            return MinimumOutcomeQuality == EvidenceQuality.OBSERVER_VERIFIED && quality == EvidenceQuality.VIDEO_VERIFIED;
        }
    }

    public sealed class EventIdentity
    {
        public string Id { get; }
        public string TrialId { get; }
        public string SessionId { get; }
        public string SenderId { get; }
        public string SenderSessionId { get; }
        public ulong SenderSequence { get; }
        public PouringEventKind Kind { get; }
        public ClockStamp SourceTimestamp { get; }
        public EventIdentity(string id, string trialId, string sessionId, string senderId,
            string senderSessionId, ulong senderSequence, PouringEventKind kind, ClockStamp sourceTimestamp)
        {
            Id = id; TrialId = trialId; SessionId = sessionId; SenderId = senderId;
            SenderSessionId = senderSessionId; SenderSequence = senderSequence; Kind = kind;
            SourceTimestamp = sourceTimestamp;
        }
        public bool IsValid => PouringValues.Text(Id) && PouringValues.Text(TrialId)
            && PouringValues.Text(SessionId) && PouringValues.Text(SenderId)
            && PouringValues.Text(SenderSessionId) && PouringValues.Defined(Kind) && SourceTimestamp.IsValid;
    }

    public sealed class PouringEvent
    {
        public EventIdentity Identity { get; }
        // Adapter assertion of authenticated identity, not a self-claimed payload identity.
        public string AuthenticatedSenderId { get; }
        public string Reason { get; }
        public CupIdentity? LifecycleCup { get; }
        public PouringEvent(EventIdentity identity, string authenticatedSenderId,
            string reason = null, CupIdentity? lifecycleCup = null)
        { Identity = identity; AuthenticatedSenderId = authenticatedSenderId; Reason = reason; LifecycleCup = lifecycleCup; }
    }

    public sealed class SenderSession
    {
        public PouringRole Role { get; }
        public string SenderId { get; }
        public string Id { get; }
        public bool Admitted { get; }
        public bool HasSequence { get; }
        public ulong LastAcceptedSequence { get; }
        public SenderSession(PouringRole role, string senderId, string id)
            : this(role, senderId, id, true, false, 0) { }
        internal SenderSession(PouringRole role, string senderId, string id, bool admitted, bool hasSequence, ulong sequence)
        { Role = role; SenderId = senderId; Id = id; Admitted = admitted; HasSequence = hasSequence; LastAcceptedSequence = sequence; }
    }

    public sealed class AcceptedEventRecord
    {
        public EventIdentity Identity { get; }
        public ulong CommitSequence { get; }
        public string GuardReference { get; }
        internal AcceptedEventRecord(EventIdentity identity, ulong commitSequence, string guardReference)
        { Identity = identity; CommitSequence = commitSequence; GuardReference = guardReference; }
    }

    public sealed class EventAcceptanceState
    {
        public IReadOnlyList<SenderSession> Senders { get; }
        public IReadOnlyList<AcceptedEventRecord> AcceptedEvents { get; }
        public ulong CommitSequence { get; }
        public ulong Revision { get; }
        internal EventAcceptanceState(IEnumerable<SenderSession> senders,
            IEnumerable<AcceptedEventRecord> acceptedEvents, ulong commitSequence, ulong revision)
        {
            Senders = PouringValues.Copy(senders); AcceptedEvents = PouringValues.Copy(acceptedEvents);
            CommitSequence = commitSequence; Revision = revision;
        }
        internal SenderSession Find(string sender, string session)
        {
            foreach (var s in Senders)
                if (PouringValues.Same(s.SenderId, sender) && PouringValues.Same(s.Id, session)) return s;
            return null;
        }
    }

    public sealed class CupLifecycle
    {
        public CupIdentity Cup { get; }
        public bool ObservationLost { get; }
        public bool Retired { get; }
        public ulong? LastObservationSequence { get; }
        public ClockStamp? LostAt { get; }
        internal CupLifecycle(CupIdentity cup, bool lost = false, bool retired = false,
            ulong? lastSequence = null, ClockStamp? lostAt = null)
        { Cup = cup; ObservationLost = lost; Retired = retired; LastObservationSequence = lastSequence; LostAt = lostAt; }
    }

    public sealed class PouringSessionState
    {
        public PouringSessionBinding Binding { get; }
        public TrialProtocol Protocol { get; }
        public PouringPhase Phase { get; }
        public TaskOutcome Outcome { get; }
        public bool Terminal { get; }
        public bool PourerReady { get; }
        public bool ReceiverReady { get; }
        // Operational transfer state; never a measurement of bead motion or task success.
        public BeadTransferState Transfer { get; }
        public CupLifecycle SourceLifecycle { get; }
        public CupLifecycle DestinationLifecycle { get; }
        public EventAcceptanceState Acceptance { get; }
        // Last verified raw facts/sequence watermarks, not a current-validity cache.
        // Loss/retirement is checked separately; historical facts are never rewritten.
        public IReadOnlyList<EvidenceFact> EvidenceHistory { get; }
        public bool Blocked => SourceLifecycle.ObservationLost || SourceLifecycle.Retired
            || DestinationLifecycle.ObservationLost || DestinationLifecycle.Retired;
        public PouringPhase? AbortOriginPhase { get; }
        public string AbortReason { get; }
        public string AbortEventId { get; }
        public string GuardSnapshotReference { get; }
        public ulong CommitSequence => Acceptance.CommitSequence;
        internal PouringSessionState(PouringSessionBinding binding, TrialProtocol protocol, PouringPhase phase,
            TaskOutcome outcome, bool terminal, bool pourerReady, bool receiverReady, BeadTransferState transfer,
            CupLifecycle sourceLifecycle, CupLifecycle destinationLifecycle, EventAcceptanceState acceptance,
            IEnumerable<EvidenceFact> history, PouringPhase? abortOrigin = null, string abortReason = null,
            string abortEventId = null, string guardReference = null)
        {
            Binding = binding; Protocol = protocol; Phase = phase; Outcome = outcome; Terminal = terminal;
            PourerReady = pourerReady; ReceiverReady = receiverReady; Transfer = transfer;
            SourceLifecycle = sourceLifecycle; DestinationLifecycle = destinationLifecycle; Acceptance = acceptance;
            EvidenceHistory = PouringValues.Copy(history); AbortOriginPhase = abortOrigin; AbortReason = abortReason;
            AbortEventId = abortEventId; GuardSnapshotReference = guardReference;
        }
    }

    public sealed class CommitProposal
    {
        public PouringSessionState ExpectedState { get; }
        public ulong ExpectedCommitSequence { get; }
        public ulong ExpectedAcceptanceRevision { get; }
        public PouringSessionState NextState { get; }
        public EventIdentity Event { get; }
        public GuardSnapshot GuardSnapshot { get; }
        public string GuardSnapshotReference => GuardSnapshot.Reference;
        internal CommitProposal(PouringSessionState current, PouringSessionState next,
            EventIdentity evt, GuardSnapshot snapshot)
        { ExpectedState = current; ExpectedCommitSequence = current.CommitSequence; ExpectedAcceptanceRevision = current.Acceptance.Revision;
            NextState = next; Event = evt; GuardSnapshot = snapshot; }
    }

    public sealed class TransitionResult
    {
        public bool Accepted { get; }
        public PouringSessionState NextState { get; }
        public PouringRejection Rejection { get; }
        public CommitProposal Proposal { get; }
        public string GuardSnapshotReference => Proposal == null ? null : Proposal.GuardSnapshotReference;
        internal TransitionResult(bool accepted, PouringSessionState next, PouringRejection rejection, CommitProposal proposal = null)
        { Accepted = accepted; NextState = next; Rejection = rejection; Proposal = proposal; }
    }

    // Immutable in-memory migration carrier. No Photon Host Migration/serialization is performed.
    public sealed class PouringAuthoritySnapshot
    {
        public PouringSessionState State { get; }
        public EventAcceptanceState Acceptance { get; }
        public TrialProtocolReference Protocol { get; }
        public int AcceptedEventCount { get; }
        public int SenderSessionCount { get; }
        public PouringAuthoritySnapshot(PouringSessionState state, EventAcceptanceState acceptance,
            TrialProtocolReference protocol, int acceptedEventCount, int senderSessionCount)
        { State = state; Acceptance = acceptance; Protocol = protocol;
            AcceptedEventCount = acceptedEventCount; SenderSessionCount = senderSessionCount; }
    }

    public sealed class OutcomeAssessment
    {
        public string TrialId { get; }
        public string SessionId { get; }
        public ulong Revision { get; }
        public string AssessorId { get; }
        public ClockStamp AssessmentTime { get; }
        public TaskOutcome AssessedOutcome { get; }
        public string Reason { get; }
        public string EvidenceReference { get; }
        public EvidenceQuality Quality { get; }
        public ulong CompletionCommitSequence { get; }
        public TrialProtocolReference Protocol { get; }
        public OutcomeAssessment(string trialId, string sessionId, ulong revision, string assessorId,
            ClockStamp assessmentTime, TaskOutcome assessedOutcome, string reason, string evidenceReference,
            EvidenceQuality quality, ulong completionCommitSequence, TrialProtocolReference protocol)
        {
            TrialId = trialId; SessionId = sessionId; Revision = revision; AssessorId = assessorId;
            AssessmentTime = assessmentTime; AssessedOutcome = assessedOutcome; Reason = reason;
            EvidenceReference = evidenceReference; Quality = quality; CompletionCommitSequence = completionCommitSequence;
            Protocol = protocol;
        }
    }

    public sealed class OutcomeAssessmentHistory
    {
        public IReadOnlyList<OutcomeAssessment> Assessments { get; }
        public OutcomeAssessmentHistory() : this(new OutcomeAssessment[0]) { }
        private OutcomeAssessmentHistory(IEnumerable<OutcomeAssessment> assessments)
        { Assessments = PouringValues.Copy(assessments); }
        public bool TryAppend(PouringSessionState completed, OutcomeAssessment assessment,
            out OutcomeAssessmentHistory next, out PouringRejection rejection)
        {
            next = this; rejection = PouringRejection.InvalidAssessment;
            if (completed == null || !completed.Terminal || completed.Phase != PouringPhase.Completed
                || assessment == null || !PouringValues.Same(assessment.TrialId, completed.Binding.TrialId)
                || !PouringValues.Same(assessment.SessionId, completed.Binding.SessionId)
                || !assessment.Protocol.Equals(completed.Binding.Protocol) || assessment.Revision == 0
                || assessment.CompletionCommitSequence != completed.CommitSequence || !assessment.AssessmentTime.IsValid
                || !PouringValues.Text(assessment.Reason) || !PouringValues.Text(assessment.EvidenceReference)
                || !PouringValues.Defined(assessment.AssessedOutcome) || assessment.AssessedOutcome == TaskOutcome.ABORTED
                || !PouringValues.Defined(assessment.Quality)) return false;
            if (Assessments.Count > 0)
            {
                var last = Assessments[Assessments.Count - 1];
                if (assessment.Revision <= last.Revision || !PouringValues.Same(last.TrialId, assessment.TrialId)
                    || !PouringValues.Same(last.SessionId, assessment.SessionId)
                    || last.CompletionCommitSequence != assessment.CompletionCommitSequence) return false;
            }
            bool assessor = false;
            foreach (var id in completed.Protocol.Assessors)
                if (PouringValues.Same(id, assessment.AssessorId)) assessor = true;
            if (!assessor || (assessment.AssessedOutcome != TaskOutcome.UNKNOWN
                && !completed.Protocol.AllowsAssessment(assessment.Quality))) return false;
            var copy = new List<OutcomeAssessment>(Assessments) { assessment };
            next = new OutcomeAssessmentHistory(copy); rejection = PouringRejection.None; return true;
        }
        public TaskOutcome Aggregate(PouringSessionState completed)
        {
            if (completed == null || !completed.Terminal) return TaskOutcome.UNKNOWN;
            if (completed.Outcome == TaskOutcome.ABORTED) return TaskOutcome.ABORTED;
            if (Assessments.Count == 0) return TaskOutcome.UNKNOWN;
            var last = Assessments[Assessments.Count - 1];
            if (!PouringValues.Same(last.TrialId, completed.Binding.TrialId)
                || !PouringValues.Same(last.SessionId, completed.Binding.SessionId)
                || last.CompletionCommitSequence != completed.CommitSequence
                || !last.Protocol.Equals(completed.Binding.Protocol)) return TaskOutcome.UNKNOWN;
            return last.AssessedOutcome;
        }
    }
}
