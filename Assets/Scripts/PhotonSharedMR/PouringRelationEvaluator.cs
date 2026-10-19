namespace ShareMr.Pouring
{
    /// <summary>
    /// Describes current verified physical evidence only. A COMPLEMENTARY result neither
    /// contains Ready acknowledgements nor authorizes a phase commit. The FSM rechecks both.
    /// No predictor, clock reads, network writes or robot commands are performed.
    /// </summary>
    public static class PouringRelationEvaluator
    {
        public static PouringRelation Evaluate(PouringSessionState state, GuardSnapshot evidence,
            TrialProtocol protocol, ClockStamp now)
        {
            if (state == null) return PouringRelation.INDEPENDENT;
            if (PouringTaskStateMachine.ValidateState(state) != PouringRejection.None)
                return PouringRelation.UNKNOWN;
            if (state.Phase == PouringPhase.Independent || state.Terminal)
                return PouringRelation.INDEPENDENT;

            // A valid explicit contradiction outranks missing physical observations.
            // Unverified mismatches are UNKNOWN; they do not manufacture a conflict fact.
            if (PouringTaskStateMachine.InspectFact(state, evidence, protocol, EvidenceKind.NoConflict, now)
                == PouringRejection.None && evidence.Find(EvidenceKind.NoConflict).Truth == TruthValue.FALSE)
                return PouringRelation.CONFLICT;

            bool unknown = state.Blocked;
            bool notReady = false;
            var required = new[]
            {
                EvidenceKind.NoConflict, EvidenceKind.NoStop, EvidenceKind.ParticipantsConnected,
                EvidenceKind.SourceRobotConnected, EvidenceKind.DestinationRobotConnected,
                EvidenceKind.SourceCupObserved, EvidenceKind.DestinationCupObserved,
                EvidenceKind.SourceCupHold, EvidenceKind.DestinationCupHold, EvidenceKind.Preparation
            };
            foreach (var kind in required)
            {
                if (PouringTaskStateMachine.InspectFact(state, evidence, protocol, kind, now)
                    != PouringRejection.None)
                { unknown = true; continue; }
                var truth = evidence.Find(kind).Truth;
                if (truth == TruthValue.UNKNOWN) unknown = true;
                else if (truth == TruthValue.FALSE) notReady = true;
            }
            if (unknown) return PouringRelation.UNKNOWN;
            return notReady ? PouringRelation.NOT_READY : PouringRelation.COMPLEMENTARY;
        }
    }
}
