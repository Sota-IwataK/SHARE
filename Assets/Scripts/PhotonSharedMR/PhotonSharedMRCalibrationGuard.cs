using System;
using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

public static class PhotonSharedMRCalibrationGuard
{
    private const float FrameLogIntervalSeconds = 0.25f;
    private const float FrameStallThresholdMs = 100f;

    private static int calibrationDepth;
    private static string currentSource = "None";
    private static float calibrationStartRealtime = -1f;
    private static float lastFrameRealtime = -1f;
    private static float nextFrameLogRealtime = -1f;
    private static float maxFrameDeltaMs;

    public static bool CalibrationInProgress => calibrationDepth > 0;
    public static string CurrentSource => currentSource;
    public static float CalibrationElapsedMs => calibrationStartRealtime >= 0f
        ? Mathf.Max(0f, (Time.realtimeSinceStartup - calibrationStartRealtime) * 1000f)
        : -1f;
    public static float MaxFrameDeltaMs => maxFrameDeltaMs;

    public static void BeginCalibration(string source)
    {
        if (calibrationDepth == 0)
        {
            calibrationStartRealtime = Time.realtimeSinceStartup;
            lastFrameRealtime = calibrationStartRealtime;
            nextFrameLogRealtime = calibrationStartRealtime;
            maxFrameDeltaMs = 0f;
        }

        calibrationDepth++;
        currentSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
    }

    public static void EndCalibration(string source)
    {
        calibrationDepth = Mathf.Max(0, calibrationDepth - 1);
        if (calibrationDepth == 0)
        {
            currentSource = "None";
        }
        else if (!string.IsNullOrWhiteSpace(source))
        {
            currentSource = source;
        }
    }

    public static void TickCalibrationFrame()
    {
        if (!CalibrationInProgress)
        {
            lastFrameRealtime = -1f;
            nextFrameLogRealtime = -1f;
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (lastFrameRealtime < 0f)
        {
            lastFrameRealtime = now;
        }

        float frameDeltaMs = Mathf.Max(0f, (now - lastFrameRealtime) * 1000f);
        float unscaledDeltaMs = Mathf.Max(0f, Time.unscaledDeltaTime * 1000f);
        lastFrameRealtime = now;
        maxFrameDeltaMs = Mathf.Max(maxFrameDeltaMs, frameDeltaMs);

        bool stalled = frameDeltaMs >= FrameStallThresholdMs;
        if (stalled)
        {
            Debug.LogWarning("[PhotonSharedMRCalibrationGuard] PHOTON_CALIBRATION_FRAME_STALL"
                + " frameDeltaMs=" + FormatFloat(frameDeltaMs)
                + " elapsedMs=" + FormatFloat(CalibrationElapsedMs)
                + BuildTimingContext(null)
                + BuildPhotonState());
        }

        if (stalled || nextFrameLogRealtime < 0f || now >= nextFrameLogRealtime)
        {
            Debug.Log("[PhotonSharedMRCalibrationGuard] PHOTON_CALIBRATION_FRAME"
                + " elapsedMs=" + FormatFloat(CalibrationElapsedMs)
                + " frameDeltaMs=" + FormatFloat(frameDeltaMs)
                + " unscaledDeltaMs=" + FormatFloat(unscaledDeltaMs)
                + " maxFrameDeltaMs=" + FormatFloat(maxFrameDeltaMs)
                + BuildPhotonState());
            nextFrameLogRealtime = now + FrameLogIntervalSeconds;
        }
    }

    public static void LogCalibrationState(string label, string source)
    {
        PhotonFusionSharedRoomBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<PhotonFusionSharedRoomBootstrap>(FindObjectsInactive.Include);
        GameObject photonRoot = GameObject.Find("PhotonSharedMR");
        Transform runnerTransform = null;
        bool runnerExists = false;
        bool runnerIsRunning = false;
        string joinStatus = bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap";

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = bootstrap != null
            ? bootstrap.Runner
            : UnityEngine.Object.FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        runnerExists = runner != null;
        runnerIsRunning = runner != null && runner.IsRunning;
        runnerTransform = runner != null ? runner.transform : null;
#else
        runnerTransform = bootstrap != null ? bootstrap.transform : null;
#endif

        Debug.Log("[PhotonSharedMRCalibrationGuard] " + label
            + " source=" + (string.IsNullOrWhiteSpace(source) ? "Unknown" : source)
            + BuildTimingContext(null)
            + " bootstrapExists=" + (bootstrap != null)
            + " bootstrapActive=" + (bootstrap != null && bootstrap.gameObject.activeInHierarchy)
            + " bootstrapHierarchy=" + FormatPath(bootstrap != null ? bootstrap.transform : null)
            + " runnerExists=" + runnerExists
            + " runnerIsRunning=" + runnerIsRunning
            + " runnerHierarchy=" + FormatPath(runnerTransform)
            + " joinStatus=" + joinStatus
            + " photonRootActive=" + (photonRoot != null && photonRoot.activeInHierarchy));
    }

    public static void LogCalibrationException(string source, Exception exception)
    {
        PhotonFusionSharedRoomBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<PhotonFusionSharedRoomBootstrap>(FindObjectsInactive.Include);
        Debug.LogError("[PhotonSharedMRCalibrationGuard] PHOTON_CALIBRATION_EXCEPTION"
            + " source=" + (string.IsNullOrWhiteSpace(source) ? "Unknown" : source)
            + " message=" + (exception != null ? exception.GetType().Name + ": " + exception.Message : "Unknown")
            + " stackTrace=" + FormatStackTraceForLog(exception != null ? exception.StackTrace : StackTraceUtility.ExtractStackTrace())
            + BuildTimingContext(null)
            + BuildPhotonState(bootstrap));
    }

    public static void LogRunnerShutdown(string reason)
    {
        Debug.Log("[PhotonSharedMRCalibrationGuard] PHOTON_RUNNER_SHUTDOWN"
            + " reason=" + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason)
            + BuildTimingContext(null)
            + BuildPhotonState());
    }

    public static void LogBootstrapEnabled(Transform bootstrap)
    {
        Debug.Log("[PhotonSharedMRCalibrationGuard] PHOTON_BOOTSTRAP_ENABLED"
            + BuildTimingContext(bootstrap)
            + BuildPhotonState()
            + " bootstrap=" + FormatPath(bootstrap));
    }

    public static void LogPhotonRootEnabled(Transform root)
    {
        Debug.Log("[PhotonSharedMRCalibrationGuard] PHOTON_ROOT_ENABLED"
            + BuildTimingContext(root)
            + BuildPhotonState()
            + " root=" + FormatPath(root));
    }

    public static void LogPhotonRootDisabled(Transform root)
    {
        Debug.LogWarning("[PhotonSharedMRCalibrationGuard] PHOTON_ROOT_DISABLED"
            + BuildTimingContext(root)
            + BuildPhotonState()
            + " root=" + FormatPath(root)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
    }

    public static void LogPhotonRootDestroyed(Transform root)
    {
        Debug.LogWarning("[PhotonSharedMRCalibrationGuard] PHOTON_ROOT_DESTROYED"
            + BuildTimingContext(root)
            + BuildPhotonState()
            + " root=" + FormatPath(root)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
    }

    public static void LogBootstrapDisabled(Transform bootstrap)
    {
        Debug.LogWarning("[PhotonSharedMRCalibrationGuard] PHOTON_BOOTSTRAP_DISABLED"
            + BuildTimingContext(bootstrap)
            + BuildPhotonState()
            + " bootstrap=" + FormatPath(bootstrap)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
    }

    public static void LogBootstrapDestroyed(Transform bootstrap)
    {
        Debug.LogWarning("[PhotonSharedMRCalibrationGuard] PHOTON_BOOTSTRAP_DESTROYED"
            + BuildTimingContext(bootstrap)
            + BuildPhotonState()
            + " bootstrap=" + FormatPath(bootstrap)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
    }

    public static string BuildTimingContext(Transform hierarchy)
    {
        return " calibrationInProgress=" + CalibrationInProgress
            + " calibrationElapsedMs=" + FormatFloat(CalibrationElapsedMs)
            + " currentSource=" + CurrentSource
            + " frameCount=" + Time.frameCount
            + " realtimeSinceStartup=" + FormatFloat(Time.realtimeSinceStartup)
            + " hierarchy=" + FormatPath(hierarchy);
    }

    private static string BuildPhotonState(PhotonFusionSharedRoomBootstrap bootstrap = null)
    {
        if (bootstrap == null)
        {
            bootstrap = UnityEngine.Object.FindFirstObjectByType<PhotonFusionSharedRoomBootstrap>(FindObjectsInactive.Include);
        }

        return " runnerIsRunning=" + (bootstrap != null && bootstrap.RunnerIsRunning)
            + " joinStatus=" + (bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap")
            + " room=" + (bootstrap != null ? bootstrap.DebugRoomName : "MissingBootstrap")
            + " activePlayers=" + (bootstrap != null ? bootstrap.ActivePlayersCount : 0);
    }

    private static string FormatFloat(float value)
    {
        return value < 0f ? "None" : value.ToString("F1");
    }

    private static string FormatStackTraceForLog(string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return "Unavailable";
        }

        return stackTrace
            .Replace("\r\n", " | ")
            .Replace('\n', '|')
            .Replace('\r', '|');
    }

    private static string FormatPath(Transform transform)
    {
        if (transform == null)
        {
            return "None";
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
