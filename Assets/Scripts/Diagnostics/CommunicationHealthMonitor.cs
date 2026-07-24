using System;
using System.Text;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public enum CommunicationChannel
{
    RosTcp,
    PoseStamped,
    PoseArray,
    Photon,
    BottleSync,
    GrabRpc,
    TargetClaim,
    Count
}

[DisallowMultipleComponent]
public sealed class CommunicationHealthMonitor : MonoBehaviour
{
    private enum ChannelKind { Connection, Continuous, Event }
    private enum HealthState { Unknown, Ok, Stale, Disconnected, Error }

    private sealed class ChannelState
    {
        public bool Enabled = true;
        public bool Used;
        public bool EverSucceeded;
        public double LastSuccessTime = -1d;
        public long SuccessCount;
        public HealthState State = HealthState.Unknown;
        public string Detail = string.Empty;
        public string LastFailureKey = string.Empty;
        public double LastFailureLogTime = -999d;
        public double FailureStartedTime = -1d;
    }

    public static CommunicationHealthMonitor Instance { get; private set; }
    public static bool VerboseLogsEnabled => Instance != null && Instance.enableVerboseLogs;

    [Header("Summary")]
    [SerializeField, Min(1f)] private float summaryIntervalSec = 30f;
    [SerializeField, Min(0f)] private float duplicateFailureSuppressSec = 5f;
    [SerializeField] private bool enableOkSummary = true;
    [SerializeField] private bool enableStatusSummary = true;
    [SerializeField] private bool enableVerboseLogs;

    [Header("Continuous Timeouts")]
    [SerializeField, Min(0.1f)] private float poseStampedTimeoutSec = 2f;
    [SerializeField, Min(0.1f)] private float poseArrayTimeoutSec = 2f;

    [Header("Channels")]
    [SerializeField] private bool monitorRosTcp = true;
    [SerializeField] private bool monitorPoseStamped = true;
    [SerializeField] private bool monitorPoseArray = true;
    [SerializeField] private bool monitorPhoton = true;

    private readonly ChannelState[] channels = new ChannelState[(int)CommunicationChannel.Count];
    private double nextSummaryTime;
    private double nextConnectionPoll;
    private ROSConnection rosConnection;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[COMM][WARN] duplicate CommunicationHealthMonitor disabled", this);
            enabled = false;
            return;
        }
        Instance = this;
        for (int i = 0; i < channels.Length; i++) channels[i] = new ChannelState();
        channels[(int)CommunicationChannel.RosTcp].Enabled = monitorRosTcp;
        channels[(int)CommunicationChannel.PoseStamped].Enabled = monitorPoseStamped;
        channels[(int)CommunicationChannel.PoseArray].Enabled = monitorPoseArray;
        channels[(int)CommunicationChannel.Photon].Enabled = monitorPhoton;
        nextSummaryTime = Time.realtimeSinceStartupAsDouble + summaryIntervalSec;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        rosConnection = null;
    }

    private void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        CheckContinuous(CommunicationChannel.PoseStamped, poseStampedTimeoutSec, now);
        CheckContinuous(CommunicationChannel.PoseArray, poseArrayTimeoutSec, now);
        if (now >= nextConnectionPoll)
        {
            nextConnectionPoll = now + 1d;
            PollRosConnection();
        }
        if (now >= nextSummaryTime)
        {
            nextSummaryTime = now + summaryIntervalSec;
            WriteSummary();
        }
    }

    public static void ReportSuccess(CommunicationChannel channel)
    {
        CommunicationHealthMonitor monitor = Instance;
        if (monitor == null || !monitor.TryState(channel, out ChannelState state) || !state.Enabled) return;
        double now = Time.realtimeSinceStartupAsDouble;
        HealthState previous = state.State;
        double failureStart = state.FailureStartedTime;
        state.Used = true;
        state.EverSucceeded = true;
        state.LastSuccessTime = now;
        state.SuccessCount++;
        state.State = HealthState.Ok;
        state.Detail = string.Empty;
        state.FailureStartedTime = -1d;
        if (previous == HealthState.Stale || previous == HealthState.Disconnected || previous == HealthState.Error)
        {
            string downtime = failureStart >= 0d ? " downtime=" + (now - failureStart).ToString("F2") + "s" : string.Empty;
            Debug.Log("[COMM][RECOVERED] channel=" + ChannelName(channel) + downtime);
        }
    }

    public static void ReportFailure(
        CommunicationChannel channel,
        string reason,
        LogType severity = LogType.Warning)
    {
        CommunicationHealthMonitor monitor = Instance;
        if (monitor == null || !monitor.TryState(channel, out ChannelState state) || !state.Enabled) return;
        double now = Time.realtimeSinceStartupAsDouble;
        state.Used = true;
        state.State = severity == LogType.Error || severity == LogType.Exception
            ? HealthState.Error : HealthState.Stale;
        state.Detail = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;
        if (state.FailureStartedTime < 0d) state.FailureStartedTime = now;
        string key = state.State + "|" + state.Detail;
        if (key == state.LastFailureKey && now - state.LastFailureLogTime < monitor.duplicateFailureSuppressSec) return;
        state.LastFailureKey = key;
        state.LastFailureLogTime = now;
        string message = "[COMM][" + (state.State == HealthState.Error ? "ERROR" : "WARN")
            + "] channel=" + ChannelName(channel) + " state=" + state.State.ToString().ToUpperInvariant()
            + " detail=" + state.Detail;
        if (state.State == HealthState.Error) Debug.LogError(message);
        else Debug.LogWarning(message);
    }

    public static void SetConnectionState(
        CommunicationChannel channel,
        bool connected,
        string detail = null)
    {
        if (connected)
        {
            ReportSuccess(channel);
            return;
        }
        CommunicationHealthMonitor monitor = Instance;
        if (monitor == null || !monitor.TryState(channel, out ChannelState state) || !state.Enabled) return;
        string resolvedDetail = string.IsNullOrWhiteSpace(detail) ? "Disconnected" : detail;
        if (state.State == HealthState.Disconnected
            && string.Equals(state.Detail, resolvedDetail, StringComparison.Ordinal))
        {
            return;
        }
        double now = Time.realtimeSinceStartupAsDouble;
        state.Used = true;
        state.State = HealthState.Disconnected;
        state.Detail = resolvedDetail;
        if (state.FailureStartedTime < 0d) state.FailureStartedTime = now;
        state.LastFailureKey = HealthState.Disconnected + "|" + resolvedDetail;
        state.LastFailureLogTime = now;
        Debug.LogError("[COMM][ERROR] channel=" + ChannelName(channel)
            + " state=DISCONNECTED detail=" + resolvedDetail);
    }

    public static void SetChannelEnabled(CommunicationChannel channel, bool enabled)
    {
        CommunicationHealthMonitor monitor = Instance;
        if (monitor == null || !monitor.TryState(channel, out ChannelState state)) return;
        state.Enabled = enabled;
        if (!enabled)
        {
            state.State = HealthState.Unknown;
            state.Used = false;
            state.EverSucceeded = false;
            state.LastSuccessTime = -1d;
            state.FailureStartedTime = -1d;
            state.Detail = string.Empty;
        }
    }

    public static void Verbose(CommunicationChannel channel, string detail)
    {
        if (VerboseLogsEnabled)
        {
            Debug.Log("[COMM][VERBOSE] channel=" + ChannelName(channel) + " detail=" + detail);
        }
    }

    private void CheckContinuous(CommunicationChannel channel, float timeout, double now)
    {
        if (!TryState(channel, out ChannelState state) || !state.Enabled || !state.EverSucceeded) return;
        double elapsed = now - state.LastSuccessTime;
        if (elapsed <= timeout || state.State == HealthState.Stale) return;
        ReportFailure(channel,
            "lastReceived=" + elapsed.ToString("F2") + "s timeout=" + timeout.ToString("F2") + "s");
    }

    private void PollRosConnection()
    {
        if (!channels[(int)CommunicationChannel.RosTcp].Enabled) return;
        rosConnection ??= ROSConnection.GetOrCreateInstance();
        bool connected = rosConnection != null && rosConnection.HasConnectionThread && !rosConnection.HasConnectionError;
        SetConnectionState(CommunicationChannel.RosTcp, connected,
            rosConnection == null ? "ConnectionMissing" : "ConnectionError");
    }

    private void WriteSummary()
    {
        StringBuilder ok = new StringBuilder();
        StringBuilder status = new StringBuilder();
        for (int i = 0; i < (int)CommunicationChannel.Count; i++)
        {
            CommunicationChannel channel = (CommunicationChannel)i;
            ChannelState state = channels[i];
            if (!state.Enabled) continue;
            ChannelKind kind = KindOf(channel);
            bool includeOk = state.State == HealthState.Ok
                && (kind != ChannelKind.Event || state.EverSucceeded);
            if (includeOk)
            {
                Append(ok, ChannelName(channel) + "=OK");
            }
            else if (state.Used && state.State != HealthState.Unknown)
            {
                Append(status, ChannelName(channel) + "=" + state.State.ToString().ToUpperInvariant());
            }
        }
        int seconds = Mathf.RoundToInt(summaryIntervalSec);
        if (enableOkSummary && ok.Length > 0) Debug.Log("[COMM][OK][" + seconds + "s] " + ok);
        if (enableStatusSummary && status.Length > 0) Debug.Log("[COMM][STATUS][" + seconds + "s] " + status);
    }

    private bool TryState(CommunicationChannel channel, out ChannelState state)
    {
        int index = (int)channel;
        state = index >= 0 && index < channels.Length ? channels[index] : null;
        return state != null;
    }

    private static ChannelKind KindOf(CommunicationChannel channel)
    {
        if (channel == CommunicationChannel.RosTcp || channel == CommunicationChannel.Photon)
            return ChannelKind.Connection;
        if (channel == CommunicationChannel.PoseStamped || channel == CommunicationChannel.PoseArray)
            return ChannelKind.Continuous;
        return ChannelKind.Event;
    }

    private static string ChannelName(CommunicationChannel channel)
    {
        switch (channel)
        {
            case CommunicationChannel.RosTcp: return "ROS_TCP";
            case CommunicationChannel.PoseStamped: return "POSE_STAMPED";
            case CommunicationChannel.PoseArray: return "POSE_ARRAY";
            case CommunicationChannel.Photon: return "PHOTON";
            case CommunicationChannel.BottleSync: return "BOTTLE_SYNC";
            case CommunicationChannel.GrabRpc: return "GRAB_RPC";
            case CommunicationChannel.TargetClaim: return "TARGET_CLAIM";
            default: return channel.ToString().ToUpperInvariant();
        }
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0) builder.Append(" | ");
        builder.Append(value);
    }
}
