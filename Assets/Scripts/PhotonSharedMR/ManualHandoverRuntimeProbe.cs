using System;
using System.IO;
using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

public enum ManualHandoverProbeCase
{
    CaseA = 0,
    CaseB = 1
}

public enum ManualHandoverProbeCommand
{
    Dump = 0,
    InitializeCaseA = 1,
    InitializeCaseB = 2,
    SubmitRequest = 3,
    DuplicateRequest = 4,
    OlderRequest = 5,
    WrongSessionRequest = 6,
    WrongSourceRequest = 7,
    WrongTargetSessionRequest = 8,
    WrongObjectRequest = 9,
    WrongClaimedParticipantRequest = 10,
    WrongRoleRequest = 11,
    SubmitReady = 12,
    DuplicateReady = 13,
    OlderReady = 14,
    ReadyFromGiver = 15,
    WrongSessionReady = 16,
    WrongSourceReady = 17,
    WrongTargetSessionReady = 18,
    WrongObjectReady = 19,
    Retire = 20,
    DumpLifecycle = 21,
    InitializeNextCaseA = 22,
    InitializeNextCaseB = 23,
    SubmitRetiredRequest = 24,
    SubmitRetiredReady = 25,
    RunCapacityBoundary = 26
}

public readonly struct ManualHandoverProbeCommandEnvelope
{
    public ManualHandoverProbeCommand Command { get; }
    public string RunToken { get; }

    public ManualHandoverProbeCommandEnvelope(
        ManualHandoverProbeCommand command,
        string runToken)
    {
        Command = command;
        RunToken = runToken;
    }
}

/// <summary>
/// Development-build-only command probe for P1-03A manual-handover runtime verification.
/// It never mutates Networked fields directly and never invokes TaskPhase or ownership APIs.
/// </summary>
public sealed class ManualHandoverRuntimeProbe : MonoBehaviour
{
    public const string EnableArgument = "-manualHandoverProbe";
    public const string LabelArgument = "-manualHandoverProbeLabel";
    public const string RunTokenArgument = "-manualHandoverRunToken";
    public const string InitialCommandArgument = "-manualHandoverInitialCommand";
    public const string AutoJoinArgument = "-manualHandoverAutoJoin";
    public const string CommandFileName = "p103a_manual_handover.command";
    public const ulong CreatedSequence = 100UL;

    private const float CommandPollSeconds = 0.2f;
    private const float HeartbeatSeconds = 5f;
    private const string CanonicalSourceId = "p103a-runtime-probe";
    private const string SourceClockPrefix = "p103a.probe.";

    private string probeLabel = "ManualHandoverProbe";
    private string defaultRunToken;
    private string pendingInitialCommand;
    private string commandFilePath;
    private float nextCommandPollTime;
    private float nextHeartbeatTime;
    private bool initialCommandConsumed;
    private bool autoJoinRequested;
    private bool autoJoinStarted;
    private bool baselineCaptured;
    private SharedControlState baseline;
    private string lastSnapshotFingerprint;
    private string lastLifecycleFingerprint;
    private string lastDiagnosticFingerprint;
    private bool hasRetiredSnapshot;
    private ManualHandoverTransportSnapshot retiredSnapshot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateFromCommandLine()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        return;
#else
        string[] args = Environment.GetCommandLineArgs();
        if (!IsDevelopmentProbeEnabled(Debug.isDebugBuild, args))
        {
            return;
        }

        GameObject probeObject = new GameObject(nameof(ManualHandoverRuntimeProbe));
        DontDestroyOnLoad(probeObject);
        ManualHandoverRuntimeProbe probe = probeObject.AddComponent<ManualHandoverRuntimeProbe>();
        probe.probeLabel = GetArgValue(args, LabelArgument, "ManualHandoverProbe");
        probe.defaultRunToken = SanitizeRunToken(
            GetArgValue(args, RunTokenArgument, BuildDefaultRunToken()));
        probe.pendingInitialCommand = GetArgValue(args, InitialCommandArgument, null);
        probe.autoJoinRequested = HasArgument(args, AutoJoinArgument);
        probe.commandFilePath = Path.Combine(Application.persistentDataPath, CommandFileName);
        probe.DeleteStaleCommandFile();

        Debug.Log("[ManualHandoverProbe] event=START"
            + " label=" + probe.probeLabel
            + " debugBuild=" + Debug.isDebugBuild
            + " commandFile=" + probe.commandFilePath
            + " runToken=" + probe.defaultRunToken
            + " initialCommand=" + (probe.pendingInitialCommand ?? "none")
            + " autoJoin=" + probe.autoJoinRequested);
#endif
    }

    private void Update()
    {
        TryStartSharedRoom();
        CaptureBaselineIfAvailable();
        LogStateChanges();

        if (!initialCommandConsumed && !string.IsNullOrWhiteSpace(pendingInitialCommand)
            && ManualHandoverSessionNetwork.Instance != null
            && NetworkUserAvatar.Local != null)
        {
            initialCommandConsumed = true;
            ExecuteRawCommand(pendingInitialCommand, "command-line");
        }

        if (Time.realtimeSinceStartup >= nextCommandPollTime)
        {
            nextCommandPollTime = Time.realtimeSinceStartup + CommandPollSeconds;
            PollCommandFile();
        }

        if (Time.realtimeSinceStartup >= nextHeartbeatTime)
        {
            nextHeartbeatTime = Time.realtimeSinceStartup + HeartbeatSeconds;
            LogCurrentState("heartbeat");
        }
    }

    public static bool IsDevelopmentProbeEnabled(bool isDebugBuild, string[] args)
    {
        if (!isDebugBuild || args == null)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], EnableArgument, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasArgument(string[] args, string expected)
    {
        if (args == null)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private void TryStartSharedRoom()
    {
        if (!autoJoinRequested || autoJoinStarted)
        {
            return;
        }

        PhotonFusionSharedRoomBootstrap bootstrap = FindFirstObjectByType<
            PhotonFusionSharedRoomBootstrap>(FindObjectsInactive.Exclude);
        if (bootstrap == null)
        {
            return;
        }

        autoJoinStarted = true;
        if (!bootstrap.IsRunning)
        {
            _ = bootstrap.StartSharedRoom();
        }
    }

    public static bool TryParseCommand(
        string raw,
        string fallbackRunToken,
        out ManualHandoverProbeCommandEnvelope envelope)
    {
        envelope = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string[] parts = raw.Trim().Split(new[] { '|' }, 2);
        string commandText = NormalizeCommandName(parts[0]);
        string runToken = parts.Length > 1
            ? SanitizeRunToken(parts[1])
            : SanitizeRunToken(fallbackRunToken);

        ManualHandoverProbeCommand command;
        switch (commandText)
        {
            case "dump": command = ManualHandoverProbeCommand.Dump; break;
            case "dump-lifecycle": command = ManualHandoverProbeCommand.DumpLifecycle; break;
            case "init-a": command = ManualHandoverProbeCommand.InitializeCaseA; break;
            case "init-b": command = ManualHandoverProbeCommand.InitializeCaseB; break;
            case "new-session-a":
            case "init-next-a": command = ManualHandoverProbeCommand.InitializeNextCaseA; break;
            case "new-session-b":
            case "init-next-b": command = ManualHandoverProbeCommand.InitializeNextCaseB; break;
            case "retire": command = ManualHandoverProbeCommand.Retire; break;
            case "request-retired": command = ManualHandoverProbeCommand.SubmitRetiredRequest; break;
            case "ready-retired": command = ManualHandoverProbeCommand.SubmitRetiredReady; break;
            case "capacity-boundary": command = ManualHandoverProbeCommand.RunCapacityBoundary; break;
            case "request": command = ManualHandoverProbeCommand.SubmitRequest; break;
            case "request-duplicate": command = ManualHandoverProbeCommand.DuplicateRequest; break;
            case "request-older": command = ManualHandoverProbeCommand.OlderRequest; break;
            case "request-wrong-session": command = ManualHandoverProbeCommand.WrongSessionRequest; break;
            case "request-wrong-source": command = ManualHandoverProbeCommand.WrongSourceRequest; break;
            case "request-wrong-target-session": command = ManualHandoverProbeCommand.WrongTargetSessionRequest; break;
            case "request-wrong-object": command = ManualHandoverProbeCommand.WrongObjectRequest; break;
            case "request-wrong-claim": command = ManualHandoverProbeCommand.WrongClaimedParticipantRequest; break;
            case "request-wrong-role": command = ManualHandoverProbeCommand.WrongRoleRequest; break;
            case "ready": command = ManualHandoverProbeCommand.SubmitReady; break;
            case "ready-duplicate": command = ManualHandoverProbeCommand.DuplicateReady; break;
            case "ready-older": command = ManualHandoverProbeCommand.OlderReady; break;
            case "ready-from-giver": command = ManualHandoverProbeCommand.ReadyFromGiver; break;
            case "ready-wrong-session": command = ManualHandoverProbeCommand.WrongSessionReady; break;
            case "ready-wrong-source": command = ManualHandoverProbeCommand.WrongSourceReady; break;
            case "ready-wrong-target-session": command = ManualHandoverProbeCommand.WrongTargetSessionReady; break;
            case "ready-wrong-object": command = ManualHandoverProbeCommand.WrongObjectReady; break;
            default: return false;
        }

        envelope = new ManualHandoverProbeCommandEnvelope(command, runToken);
        return true;
    }

    public static bool TryCreateVerificationSession(
        ManualHandoverProbeCase probeCase,
        string runToken,
        ulong createdTimestamp,
        out ManualHandoverSession session)
    {
        session = default;
        string token = SanitizeRunToken(runToken);
        SharedMRParticipantId giver = probeCase == ManualHandoverProbeCase.CaseA
            ? SharedMRParticipantId.User1
            : SharedMRParticipantId.User2;
        SharedMRParticipantId receiver = probeCase == ManualHandoverProbeCase.CaseA
            ? SharedMRParticipantId.User2
            : SharedMRParticipantId.User1;
        int giverRobot = probeCase == ManualHandoverProbeCase.CaseA ? 101 : 202;
        int receiverRobot = probeCase == ManualHandoverProbeCase.CaseA ? 202 : 101;
        if (!ManualHandoverRoleBinding.TryCreate(
                giver, giverRobot, receiver, receiverRobot,
                out ManualHandoverRoleBinding roles))
        {
            return false;
        }

        string caseLabel = probeCase == ManualHandoverProbeCase.CaseA ? "a" : "b";
        CanonicalBottleIdentity target = new CanonicalBottleIdentity(
            CanonicalSourceId,
            "canonical-" + caseLabel + "-" + token,
            probeCase == ManualHandoverProbeCase.CaseA ? 103020201UL : 103020202UL);
        return ManualHandoverSession.TryCreate(
            "coordination-" + caseLabel + "-" + token,
            roles,
            target,
            CreatedSequence,
            createdTimestamp,
            out session);
    }

    private void DeleteStaleCommandFile()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(commandFilePath) && File.Exists(commandFilePath))
            {
                File.Delete(commandFilePath);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ManualHandoverProbe] event=STALE_COMMAND_DELETE_FAILED"
                + " label=" + probeLabel
                + " exception=" + exception.GetType().Name);
        }
    }

    private void PollCommandFile()
    {
        if (string.IsNullOrWhiteSpace(commandFilePath) || !File.Exists(commandFilePath))
        {
            return;
        }

        try
        {
            string raw = File.ReadAllText(commandFilePath);
            File.Delete(commandFilePath);
            ExecuteRawCommand(raw, "command-file");
        }
        catch (Exception exception)
        {
            Debug.LogError("[ManualHandoverProbe] event=COMMAND_FILE_ERROR"
                + " label=" + probeLabel
                + " exception=" + exception.GetType().Name
                + " message=" + exception.Message);
        }
    }

    private void ExecuteRawCommand(string raw, string source)
    {
        if (!TryParseCommand(raw, defaultRunToken, out ManualHandoverProbeCommandEnvelope envelope))
        {
            Debug.LogWarning("[ManualHandoverProbe] event=COMMAND_REJECTED"
                + " label=" + probeLabel
                + " source=" + source
                + " reason=UnknownOrEmptyCommand"
                + " raw=" + (raw ?? "null"));
            return;
        }

        ExecuteCommand(envelope, source);
    }

    private void ExecuteCommand(ManualHandoverProbeCommandEnvelope envelope, string source)
    {
        ManualHandoverSessionNetwork network = ManualHandoverSessionNetwork.Instance;
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        if (network == null || localAvatar == null)
        {
            LogCommandResult(envelope.Command, source, false,
                "RuntimeNotReady", "Unavailable");
            return;
        }

        CaptureBaselineIfAvailable();
        if (envelope.Command == ManualHandoverProbeCommand.Dump
            || envelope.Command == ManualHandoverProbeCommand.DumpLifecycle)
        {
            LogCommandResult(envelope.Command, source, true, "Dumped", "None");
            LogCurrentState("command-dump");
            return;
        }

        if (envelope.Command == ManualHandoverProbeCommand.InitializeCaseA
            || envelope.Command == ManualHandoverProbeCommand.InitializeCaseB
            || envelope.Command == ManualHandoverProbeCommand.InitializeNextCaseA
            || envelope.Command == ManualHandoverProbeCommand.InitializeNextCaseB)
        {
            ExecuteInitialize(network, envelope, source);
            return;
        }

        if (envelope.Command == ManualHandoverProbeCommand.Retire)
        {
            ExecuteRetire(network, envelope.Command, source);
            return;
        }

        if (envelope.Command == ManualHandoverProbeCommand.RunCapacityBoundary)
        {
            ExecuteCapacityBoundary(network, envelope, source);
            return;
        }

        if (envelope.Command == ManualHandoverProbeCommand.SubmitRetiredRequest
            || envelope.Command == ManualHandoverProbeCommand.SubmitRetiredReady)
        {
            ExecuteRetiredEvent(network, localAvatar.ParticipantId, envelope.Command, source);
            return;
        }

        if (!ManualHandoverSessionNetwork.TryRead(out ManualHandoverTransportSnapshot snapshot))
        {
            LogCommandResult(envelope.Command, source, false,
                "SessionUnavailable", "Unavailable");
            return;
        }

        SharedMRParticipantId local = localAvatar.ParticipantId;
        if (IsRequestCommand(envelope.Command))
        {
            ExecuteRequest(network, snapshot, local, envelope.Command, source);
        }
        else
        {
            ExecuteReady(network, snapshot, local, envelope.Command, source);
        }
    }

    private void ExecuteInitialize(
        ManualHandoverSessionNetwork network,
        ManualHandoverProbeCommandEnvelope envelope,
        string source)
    {
#if FUSION_WEAVER && FUSION2
        if (network.Object == null || !network.Object.HasStateAuthority)
        {
            LogCommandResult(envelope.Command, source, false,
                "NoStateAuthority", "Unavailable");
            return;
        }
#else
        LogCommandResult(envelope.Command, source, false,
            "FusionInactive", "Unavailable");
        return;
#endif

        ManualHandoverProbeCase probeCase = envelope.Command
            == ManualHandoverProbeCommand.InitializeCaseA
            || envelope.Command == ManualHandoverProbeCommand.InitializeNextCaseA
            ? ManualHandoverProbeCase.CaseA
            : ManualHandoverProbeCase.CaseB;
        ulong timestamp = UtcMilliseconds();
        if (!TryCreateVerificationSession(
                probeCase, envelope.RunToken, timestamp,
                out ManualHandoverSession session))
        {
            LogCommandResult(envelope.Command, source, false,
                "SessionConstructionFailed", "Unavailable");
            return;
        }

        bool accepted = network.TryInitializeSession(session, out string reason);
        LogCommandResult(envelope.Command, source, accepted, reason,
            accepted ? ManualHandoverValidationResult.Accepted.ToString() : "Unavailable");
        LogCurrentState("after-initialize");
    }

    private void ExecuteRetire(
        ManualHandoverSessionNetwork network,
        ManualHandoverProbeCommand command,
        string source)
    {
        if (ManualHandoverSessionNetwork.TryReadLifecycle(
                out ManualHandoverSessionLifecycleSnapshot lifecycle)
            && lifecycle.State == ManualHandoverSessionLifecycleState.Active
            && lifecycle.HasRetainedSession)
        {
            retiredSnapshot = lifecycle.RetainedSession;
            hasRetiredSnapshot = true;
        }

        bool accepted = network.TryRetireSession(out string reason);
        LogCommandResult(command, source, accepted, reason,
            accepted ? ManualHandoverSessionLifecycleResult.Accepted.ToString() : "Unavailable");
        LogCurrentState("after-retire");
    }

    private void ExecuteRetiredEvent(
        ManualHandoverSessionNetwork network,
        SharedMRParticipantId local,
        ManualHandoverProbeCommand command,
        string source)
    {
        if (!hasRetiredSnapshot)
        {
            LogCommandResult(command, source, false,
                "RetiredSnapshotUnavailable", "Unavailable");
            return;
        }

        ManualHandoverSession session = retiredSnapshot.Session;
        bool isRequest = command == ManualHandoverProbeCommand.SubmitRetiredRequest;
        SharedMRParticipantId required = isRequest
            ? session.RoleBinding.GiverParticipantId
            : session.RoleBinding.ReceiverParticipantId;
        if (local != required)
        {
            LogCommandResult(command, source, false,
                isRequest ? "LocalParticipantIsNotRetiredGiver" : "LocalParticipantIsNotRetiredReceiver",
                "Unavailable");
            return;
        }

        ulong sequence = NextSequence(retiredSnapshot);
        bool submitted;
        if (isRequest)
        {
            submitted = network.SubmitLocal(new ManualCoordinationRequest(
                session.CoordinationSessionId,
                sequence,
                UtcMilliseconds(),
                BuildSourceClock(local),
                local,
                session.TargetBottleKey));
        }
        else
        {
            submitted = network.SubmitLocal(new ReadyAcknowledgement(
                session.CoordinationSessionId,
                sequence,
                UtcMilliseconds(),
                BuildSourceClock(local),
                local,
                session.TargetBottleKey));
        }

        string expected = "AuthorityStateInvalid";
        if (ManualHandoverSessionNetwork.TryReadLifecycle(
                out ManualHandoverSessionLifecycleSnapshot current))
        {
            expected = current.State == ManualHandoverSessionLifecycleState.Retired
                ? ManualHandoverValidationResult.SessionRetired.ToString()
                : current.State == ManualHandoverSessionLifecycleState.Active
                    ? ManualHandoverValidationResult.SessionMismatch.ToString()
                    : ManualHandoverValidationResult.AuthorityStateInvalid.ToString();
        }
        LogCommandResult(command, source, submitted,
            submitted ? "SubmittedRetiredEventToProductionTransport" : "ProductionLocalGuardRejected",
            expected);
        LogCurrentState("after-command-" + command);
    }

    private void ExecuteCapacityBoundary(
        ManualHandoverSessionNetwork network,
        ManualHandoverProbeCommandEnvelope envelope,
        string source)
    {
#if FUSION_WEAVER && FUSION2
        if (network.Object == null || !network.Object.HasStateAuthority)
        {
            LogCommandResult(envelope.Command, source, false,
                "NoStateAuthority", "Unavailable");
            return;
        }
#else
        LogCommandResult(envelope.Command, source, false,
            "FusionInactive", "Unavailable");
        return;
#endif

        if (!ManualHandoverSessionNetwork.TryReadLifecycle(
                out ManualHandoverSessionLifecycleSnapshot initial)
            || initial.State != ManualHandoverSessionLifecycleState.NoSession
            || initial.HasRetainedSession
            || initial.RetiredSessionIds.Count != 0)
        {
            LogCommandResult(envelope.Command, source, false,
                "CapacityBoundaryRequiresFreshRoom", "NoSession");
            LogCurrentState("capacity-boundary-precondition-failed");
            return;
        }

        ulong timestamp = UtcMilliseconds();
        for (int trial = 1; trial <= ManualHandoverSessionNetwork.RetiredSessionCapacity; trial++)
        {
            string token = envelope.RunToken + "-capacity-" + trial.ToString("D2");
            string initializeReason = "SessionConstructionFailed";
            string retireReason = "NotAttempted";
            if (!TryCreateVerificationSession(
                    ManualHandoverProbeCase.CaseA, token, timestamp + (ulong)trial,
                    out ManualHandoverSession session)
                || !network.TryInitializeSession(session, out initializeReason)
                || !network.TryRetireSession(out retireReason))
            {
                Debug.LogError("[ManualHandoverProbe] event=CAPACITY_BOUNDARY"
                    + " label=" + probeLabel
                    + " result=FAIL"
                    + " trial=" + trial
                    + " initializeReason=" + initializeReason
                    + " retireReason=" + retireReason);
                LogCommandResult(envelope.Command, source, false,
                    "BoundarySetupFailedAtTrial" + trial, "Accepted");
                LogCurrentState("capacity-boundary-setup-failed");
                return;
            }
        }

        if (!ManualHandoverSessionNetwork.TryReadLifecycle(
                out ManualHandoverSessionLifecycleSnapshot beforeRejection))
        {
            LogCommandResult(envelope.Command, source, false,
                "BoundarySnapshotUnavailable", "Retired");
            return;
        }

        string trial17Token = envelope.RunToken + "-capacity-17";
        bool trial17Constructed = TryCreateVerificationSession(
            ManualHandoverProbeCase.CaseA, trial17Token,
            timestamp + (ulong)ManualHandoverSessionNetwork.RetiredSessionCapacity + 1UL,
            out ManualHandoverSession trial17);
        string rejectionReason = trial17Constructed
            ? "NotAttempted"
            : "SessionConstructionFailed";
        bool trial17Accepted = trial17Constructed
            && network.TryInitializeSession(trial17, out rejectionReason);

        bool hasAfter = ManualHandoverSessionNetwork.TryReadLifecycle(
            out ManualHandoverSessionLifecycleSnapshot afterRejection);
        bool unchanged = hasAfter && LifecycleEquivalent(beforeRejection, afterRejection);
        bool passed = beforeRejection.State == ManualHandoverSessionLifecycleState.Retired
            && beforeRejection.RetiredSessionIds.Count
                == ManualHandoverSessionNetwork.RetiredSessionCapacity
            && !trial17Accepted
            && string.Equals(rejectionReason,
                ManualHandoverSessionLifecycleResult
                    .RetiredSessionLedgerCapacityExceeded.ToString(),
                StringComparison.Ordinal)
            && unchanged;

        Debug.Log("[ManualHandoverProbe] event=CAPACITY_BOUNDARY"
            + " label=" + probeLabel
            + " result=" + (passed ? "PASS" : "FAIL")
            + " completedRetirements=" + beforeRejection.RetiredSessionIds.Count
            + " trial17Accepted=" + trial17Accepted
            + " rejectionReason=" + rejectionReason
            + " stateUnchanged=" + unchanged);
        LogCommandResult(envelope.Command, source, passed,
            rejectionReason, ManualHandoverSessionLifecycleResult
                .RetiredSessionLedgerCapacityExceeded.ToString());
        LogCurrentState("after-capacity-boundary");
    }

    private void ExecuteRequest(
        ManualHandoverSessionNetwork network,
        ManualHandoverTransportSnapshot snapshot,
        SharedMRParticipantId local,
        ManualHandoverProbeCommand command,
        string source)
    {
        bool isWrongRole = command == ManualHandoverProbeCommand.WrongRoleRequest;
        if (!isWrongRole && command != ManualHandoverProbeCommand.WrongClaimedParticipantRequest
            && local != snapshot.Session.RoleBinding.GiverParticipantId)
        {
            LogCommandResult(command, source, false, "LocalParticipantIsNotGiver", "Unavailable");
            return;
        }
        if (isWrongRole && local == snapshot.Session.RoleBinding.GiverParticipantId)
        {
            LogCommandResult(command, source, false, "IssueFromReceiverRequired", "SourceNotGiver");
            return;
        }

        string sessionId = snapshot.Session.CoordinationSessionId;
        CanonicalBottleIdentity target = snapshot.Session.TargetBottleKey;
        SharedMRParticipantId claimed = local;
        ulong sequence = NextSequence(snapshot);
        string expected = ExpectedResult(command);

        switch (command)
        {
            case ManualHandoverProbeCommand.DuplicateRequest:
                sequence = snapshot.RequestAccepted
                    ? snapshot.Request.EventSequence
                    : snapshot.Session.CreatedSequence;
                break;
            case ManualHandoverProbeCommand.OlderRequest:
                sequence = snapshot.RequestAccepted && snapshot.Request.EventSequence > 0
                    ? snapshot.Request.EventSequence - 1UL
                    : 0UL;
                break;
            case ManualHandoverProbeCommand.WrongSessionRequest:
                sessionId += "-wrong";
                break;
            case ManualHandoverProbeCommand.WrongSourceRequest:
                target = new CanonicalBottleIdentity(
                    target.SourceId + "-wrong", target.SessionId, target.ObjectId);
                break;
            case ManualHandoverProbeCommand.WrongTargetSessionRequest:
                target = new CanonicalBottleIdentity(
                    target.SourceId, target.SessionId + "-wrong", target.ObjectId);
                break;
            case ManualHandoverProbeCommand.WrongObjectRequest:
                target = new CanonicalBottleIdentity(
                    target.SourceId, target.SessionId, target.ObjectId + 1UL);
                break;
            case ManualHandoverProbeCommand.WrongClaimedParticipantRequest:
                claimed = local == SharedMRParticipantId.User1
                    ? SharedMRParticipantId.User2
                    : SharedMRParticipantId.User1;
                break;
        }

        ManualCoordinationRequest request = new ManualCoordinationRequest(
            sessionId,
            sequence,
            UtcMilliseconds(),
            BuildSourceClock(local),
            claimed,
            target);
        bool submitted = network.SubmitLocal(request);
        string reason = submitted ? "SubmittedToProductionTransport" : "ProductionLocalGuardRejected";
        LogCommandResult(command, source, submitted, reason, expected);
        LogCurrentState("after-command-" + command);
    }

    private void ExecuteReady(
        ManualHandoverSessionNetwork network,
        ManualHandoverTransportSnapshot snapshot,
        SharedMRParticipantId local,
        ManualHandoverProbeCommand command,
        string source)
    {
        bool fromGiver = command == ManualHandoverProbeCommand.ReadyFromGiver;
        if (!fromGiver && local != snapshot.Session.RoleBinding.ReceiverParticipantId)
        {
            LogCommandResult(command, source, false, "LocalParticipantIsNotReceiver", "Unavailable");
            return;
        }
        if (fromGiver && local != snapshot.Session.RoleBinding.GiverParticipantId)
        {
            LogCommandResult(command, source, false, "IssueFromGiverRequired", "SourceNotReceiver");
            return;
        }

        string sessionId = snapshot.Session.CoordinationSessionId;
        CanonicalBottleIdentity target = snapshot.Session.TargetBottleKey;
        ulong sequence = NextSequence(snapshot);
        string expected = ExpectedResult(command);

        switch (command)
        {
            case ManualHandoverProbeCommand.DuplicateReady:
                sequence = snapshot.ReadyAccepted
                    ? snapshot.Ready.EventSequence
                    : NextSequence(snapshot);
                break;
            case ManualHandoverProbeCommand.OlderReady:
                sequence = snapshot.ReadyAccepted && snapshot.Ready.EventSequence > 0
                    ? snapshot.Ready.EventSequence - 1UL
                    : 0UL;
                break;
            case ManualHandoverProbeCommand.WrongSessionReady:
                sessionId += "-wrong";
                break;
            case ManualHandoverProbeCommand.WrongSourceReady:
                target = new CanonicalBottleIdentity(
                    target.SourceId + "-wrong", target.SessionId, target.ObjectId);
                break;
            case ManualHandoverProbeCommand.WrongTargetSessionReady:
                target = new CanonicalBottleIdentity(
                    target.SourceId, target.SessionId + "-wrong", target.ObjectId);
                break;
            case ManualHandoverProbeCommand.WrongObjectReady:
                target = new CanonicalBottleIdentity(
                    target.SourceId, target.SessionId, target.ObjectId + 1UL);
                break;
        }

        ReadyAcknowledgement ready = new ReadyAcknowledgement(
            sessionId,
            sequence,
            UtcMilliseconds(),
            BuildSourceClock(local),
            local,
            target);
        bool submitted = network.SubmitLocal(ready);
        string reason = submitted ? "SubmittedToProductionTransport" : "ProductionLocalGuardRejected";
        LogCommandResult(command, source, submitted, reason, expected);
        LogCurrentState("after-command-" + command);
    }

    private void CaptureBaselineIfAvailable()
    {
        if (baselineCaptured || !SharedTeamState.TryReadControl(out SharedControlState current))
        {
            return;
        }

        baseline = current;
        baselineCaptured = true;
        Debug.Log("[ManualHandoverProbe] event=BASELINE"
            + " label=" + probeLabel
            + ControlFields(current)
            + " nonInterference=BASELINE");
    }

    private void LogStateChanges()
    {
        if (ManualHandoverSessionNetwork.TryReadLifecycle(
                out ManualHandoverSessionLifecycleSnapshot lifecycle))
        {
            if (lifecycle.State == ManualHandoverSessionLifecycleState.Retired
                && lifecycle.HasRetainedSession)
            {
                retiredSnapshot = lifecycle.RetainedSession;
                hasRetiredSnapshot = true;
            }

            string lifecycleFingerprint = LifecycleFingerprint(lifecycle);
            if (!string.Equals(lastLifecycleFingerprint, lifecycleFingerprint,
                    StringComparison.Ordinal))
            {
                lastLifecycleFingerprint = lifecycleFingerprint;
                lastSnapshotFingerprint = lifecycle.HasRetainedSession
                    ? SnapshotFingerprint(lifecycle.RetainedSession)
                    : null;
                LogCurrentState("manual-state-changed");
            }
        }

        ManualHandoverSessionNetwork network = ManualHandoverSessionNetwork.Instance;
        if (network == null)
        {
            return;
        }

        ManualHandoverEventDiagnostic diagnostic = network.LastDiagnostic;
        if (string.IsNullOrWhiteSpace(diagnostic.CoordinationSessionId))
        {
            return;
        }

        string diagnosticFingerprint = diagnostic.EventType + "|"
            + diagnostic.CoordinationSessionId + "|"
            + diagnostic.SourceParticipantId + "|"
            + diagnostic.TargetBottleKey + "|"
            + diagnostic.SourceSequence + "|"
            + diagnostic.SourceTimestamp + "|"
            + diagnostic.AuthoritySequence + "|"
            + diagnostic.Result;
        if (string.Equals(lastDiagnosticFingerprint, diagnosticFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        lastDiagnosticFingerprint = diagnosticFingerprint;
        Debug.Log("[ManualHandoverProbe] event=AUTHORITY_DIAGNOSTIC"
            + " label=" + probeLabel
            + " type=" + diagnostic.EventType
            + " coordinationSession=" + diagnostic.CoordinationSessionId
            + " sourceParticipant=" + diagnostic.SourceParticipantId
            + " target=" + diagnostic.TargetBottleKey
            + " sourceSequence=" + diagnostic.SourceSequence
            + " sourceTimestamp=" + diagnostic.SourceTimestamp
            + " sourceClock=" + diagnostic.SourceClockDomain
            + " authoritySequence=" + diagnostic.AuthoritySequence
            + " authorityTimestamp=" + diagnostic.AuthorityTimestamp
            + " authorityClock=" + diagnostic.AuthorityClockDomain
            + " result=" + diagnostic.Result);
    }

    private void LogCurrentState(string reason)
    {
        ManualHandoverSessionNetwork network = ManualHandoverSessionNetwork.Instance;
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        SharedMRParticipantId local = localAvatar != null
            ? localAvatar.ParticipantId
            : SharedMRParticipantId.Unassigned;

        string playerRef = "Unavailable";
        string stateAuthority = "Unavailable";
        string master = "Unavailable";
        int activePlayers = 0;
#if FUSION_WEAVER && FUSION2
        if (network != null && network.Runner != null)
        {
            NetworkRunner runner = network.Runner;
            playerRef = runner.LocalPlayer.ToString();
            master = runner.IsSharedModeMasterClient
                ? runner.LocalPlayer.ToString()
                : network.Object != null
                    ? network.Object.StateAuthority.ToString()
                    : "Remote";
            foreach (PlayerRef ignored in runner.ActivePlayers)
            {
                activePlayers++;
            }
            if (network.Object != null)
            {
                stateAuthority = network.Object.StateAuthority.ToString();
            }
        }
#endif

        bool hasLifecycle = ManualHandoverSessionNetwork.TryReadLifecycle(
            out ManualHandoverSessionLifecycleSnapshot lifecycle);
        bool hasSnapshot = hasLifecycle && lifecycle.HasRetainedSession;
        ManualHandoverTransportSnapshot snapshot = hasSnapshot
            ? lifecycle.RetainedSession
            : default;
        string lifecycleFields = hasLifecycle
            ? " lifecycleState=" + lifecycle.State
                + " retiredCount=" + lifecycle.RetiredSessionIds.Count
                + " retiredSessions=" + string.Join(",", lifecycle.RetiredSessionIds)
            : " lifecycleState=Unavailable retiredCount=-1 retiredSessions=Unavailable";
        string manualFields = hasSnapshot
            ? " sessionValid=True"
                + " coordinationSession=" + snapshot.Session.CoordinationSessionId
                + " giver=" + snapshot.Session.RoleBinding.GiverParticipantId
                + " giverRobot=" + snapshot.Session.RoleBinding.GiverRobotId
                + " receiver=" + snapshot.Session.RoleBinding.ReceiverParticipantId
                + " receiverRobot=" + snapshot.Session.RoleBinding.ReceiverRobotId
                + " targetSource=" + snapshot.Session.TargetBottleKey.SourceId
                + " targetSession=" + snapshot.Session.TargetBottleKey.SessionId
                + " targetObject=" + snapshot.Session.TargetBottleKey.ObjectId
                + " requestAccepted=" + snapshot.RequestAccepted
                + " requestSequence=" + (snapshot.RequestAccepted ? snapshot.Request.EventSequence : 0UL)
                + " readyAccepted=" + snapshot.ReadyAccepted
                + " readySequence=" + (snapshot.ReadyAccepted ? snapshot.Ready.EventSequence : 0UL)
                + " authoritySequence=" + snapshot.AuthorityAcceptedSequence
                + " authorityTimestamp=" + snapshot.AuthorityAcceptedTimestamp
            : " sessionValid=False";

        string controlFields = SharedTeamState.TryReadControl(out SharedControlState control)
            ? ControlFields(control)
                + " nonInterference=" + (ControlMatchesBaseline(control) ? "PASS" : "FAIL")
            : " taskPhase=Unavailable sharedSequence=-1 sharedTimestamp=-1"
                + " ownerType=Unavailable ownerId=-1 nonInterference=Unavailable";

        Debug.Log("[ManualHandoverProbe] event=STATE"
            + " label=" + probeLabel
            + " reason=" + reason
            + " localParticipant=" + local
            + " playerRef=" + playerRef
            + " master=" + master
            + " stateAuthority=" + stateAuthority
            + " activePlayers=" + activePlayers
            + lifecycleFields
            + manualFields
            + controlFields);
    }

    private void LogCommandResult(
        ManualHandoverProbeCommand command,
        string source,
        bool submitted,
        string reason,
        string expected)
    {
        SharedMRParticipantId local = NetworkUserAvatar.Local != null
            ? NetworkUserAvatar.Local.ParticipantId
            : SharedMRParticipantId.Unassigned;
        Debug.Log("[ManualHandoverProbe] event=COMMAND"
            + " label=" + probeLabel
            + " source=" + source
            + " command=" + command
            + " localParticipant=" + local
            + " submitted=" + submitted
            + " reason=" + reason
            + " expectedAuthorityResult=" + expected);
    }

    private bool ControlMatchesBaseline(SharedControlState current)
    {
        return baselineCaptured
            && current.task_phase == baseline.task_phase
            && current.sequence == baseline.sequence
            && current.shared_timestamp == baseline.shared_timestamp
            && current.owner_type == baseline.owner_type
            && current.owner_id == baseline.owner_id;
    }

    private static string ControlFields(SharedControlState value)
    {
        return " taskPhase=" + value.task_phase
            + " sharedSequence=" + value.sequence
            + " sharedTimestamp=" + value.shared_timestamp
            + " ownerType=" + value.owner_type
            + " ownerId=" + value.owner_id;
    }

    private static string SnapshotFingerprint(ManualHandoverTransportSnapshot value)
    {
        return value.Session.CoordinationSessionId + "|"
            + value.RequestAccepted + "|"
            + (value.RequestAccepted ? value.Request.EventSequence : 0UL) + "|"
            + value.ReadyAccepted + "|"
            + (value.ReadyAccepted ? value.Ready.EventSequence : 0UL) + "|"
            + value.AuthorityAcceptedSequence + "|"
            + value.AuthorityAcceptedTimestamp;
    }

    private static string LifecycleFingerprint(ManualHandoverSessionLifecycleSnapshot value)
    {
        return value.State + "|"
            + value.HasRetainedSession + "|"
            + (value.HasRetainedSession ? SnapshotFingerprint(value.RetainedSession) : "none") + "|"
            + string.Join(",", value.RetiredSessionIds);
    }

    private static bool LifecycleEquivalent(
        ManualHandoverSessionLifecycleSnapshot left,
        ManualHandoverSessionLifecycleSnapshot right)
    {
        if (left.State != right.State
            || left.HasRetainedSession != right.HasRetainedSession
            || left.RetiredSessionIds.Count != right.RetiredSessionIds.Count)
        {
            return false;
        }

        for (int i = 0; i < left.RetiredSessionIds.Count; i++)
        {
            if (!string.Equals(left.RetiredSessionIds[i], right.RetiredSessionIds[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (!left.HasRetainedSession)
        {
            return true;
        }

        ManualHandoverTransportSnapshot a = left.RetainedSession;
        ManualHandoverTransportSnapshot b = right.RetainedSession;
        return string.Equals(a.Session.CoordinationSessionId,
                b.Session.CoordinationSessionId, StringComparison.Ordinal)
            && a.Session.RoleBinding.GiverParticipantId
                == b.Session.RoleBinding.GiverParticipantId
            && a.Session.RoleBinding.GiverRobotId == b.Session.RoleBinding.GiverRobotId
            && a.Session.RoleBinding.ReceiverParticipantId
                == b.Session.RoleBinding.ReceiverParticipantId
            && a.Session.RoleBinding.ReceiverRobotId == b.Session.RoleBinding.ReceiverRobotId
            && a.Session.TargetBottleKey == b.Session.TargetBottleKey
            && a.RequestAccepted == b.RequestAccepted
            && (!a.RequestAccepted
                || (a.Request.EventSequence == b.Request.EventSequence
                    && a.Request.EventTimestamp == b.Request.EventTimestamp))
            && a.ReadyAccepted == b.ReadyAccepted
            && (!a.ReadyAccepted
                || (a.Ready.EventSequence == b.Ready.EventSequence
                    && a.Ready.EventTimestamp == b.Ready.EventTimestamp))
            && a.AuthorityAcceptedSequence == b.AuthorityAcceptedSequence
            && a.AuthorityAcceptedTimestamp == b.AuthorityAcceptedTimestamp;
    }

    private static ulong NextSequence(ManualHandoverTransportSnapshot snapshot)
    {
        ulong latest = snapshot.Session.CreatedSequence;
        if (snapshot.RequestAccepted && snapshot.Request.EventSequence > latest)
        {
            latest = snapshot.Request.EventSequence;
        }
        if (snapshot.ReadyAccepted && snapshot.Ready.EventSequence > latest)
        {
            latest = snapshot.Ready.EventSequence;
        }
        return latest == ulong.MaxValue ? ulong.MaxValue : latest + 1UL;
    }

    private static bool IsRequestCommand(ManualHandoverProbeCommand command)
    {
        return command >= ManualHandoverProbeCommand.SubmitRequest
            && command <= ManualHandoverProbeCommand.WrongRoleRequest;
    }

    private static string ExpectedResult(ManualHandoverProbeCommand command)
    {
        switch (command)
        {
            case ManualHandoverProbeCommand.SubmitRequest:
            case ManualHandoverProbeCommand.SubmitReady:
                return ManualHandoverValidationResult.Accepted.ToString();
            case ManualHandoverProbeCommand.DuplicateRequest:
            case ManualHandoverProbeCommand.DuplicateReady:
                return ManualHandoverValidationResult.DuplicateSequence.ToString();
            case ManualHandoverProbeCommand.OlderRequest:
            case ManualHandoverProbeCommand.OlderReady:
                return ManualHandoverValidationResult.OlderSequence.ToString();
            case ManualHandoverProbeCommand.WrongSessionRequest:
            case ManualHandoverProbeCommand.WrongSessionReady:
                return ManualHandoverValidationResult.SessionMismatch.ToString();
            case ManualHandoverProbeCommand.WrongSourceRequest:
            case ManualHandoverProbeCommand.WrongTargetSessionRequest:
            case ManualHandoverProbeCommand.WrongObjectRequest:
            case ManualHandoverProbeCommand.WrongSourceReady:
            case ManualHandoverProbeCommand.WrongTargetSessionReady:
            case ManualHandoverProbeCommand.WrongObjectReady:
                return ManualHandoverValidationResult.TargetMismatch.ToString();
            case ManualHandoverProbeCommand.WrongRoleRequest:
                return ManualHandoverValidationResult.SourceNotGiver.ToString();
            case ManualHandoverProbeCommand.ReadyFromGiver:
                return ManualHandoverValidationResult.SourceNotReceiver.ToString();
            case ManualHandoverProbeCommand.WrongClaimedParticipantRequest:
                return "ProductionLocalGuardRejected";
            default:
                return "Unavailable";
        }
    }

    private static string BuildSourceClock(SharedMRParticipantId participant)
    {
        return SourceClockPrefix + participant.ToString().ToLowerInvariant();
    }

    private static ulong UtcMilliseconds()
    {
        long value = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return value < 0 ? 0UL : (ulong)value;
    }

    private static string BuildDefaultRunToken()
    {
        return DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
    }

    private static string SanitizeRunToken(string value)
    {
        string source = string.IsNullOrWhiteSpace(value)
            ? BuildDefaultRunToken()
            : value.Trim();
        char[] buffer = new char[Math.Min(source.Length, 64)];
        int count = 0;
        for (int i = 0; i < source.Length && count < buffer.Length; i++)
        {
            char c = source[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                buffer[count++] = c;
            }
        }
        return count == 0 ? "run" : new string(buffer, 0, count);
    }

    private static string NormalizeCommandName(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace('_', '-')
            .ToLowerInvariant();
    }

    private static string GetArgValue(string[] args, string key, string fallback)
    {
        string prefix = key + "=";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[i].Substring(prefix.Length);
            }
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return fallback;
    }
}
