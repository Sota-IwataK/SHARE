using System;
using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

public class PhotonSharedMRAutoProbe : MonoBehaviour
{
    private const float DefaultDurationSeconds = 60f;
    private const float StatusLogIntervalSeconds = 5f;
    private const float GrabObservationSeconds = 4f;
    private const float RemoteSuccessHoldSeconds = 8f;

    private string probeLabel = "Probe";
    private string probeUserName = "Probe";
    private SharedUserRole probeRole = SharedUserRole.ManipulatorOperator;
    private ShareDeviceType probeDeviceType = ShareDeviceType.Unknown;
    private SharedMRRobotTarget probeRobotTarget = SharedMRRobotTarget.Amir;
    private bool probeIsHostLikeUser = true;
    private bool requireRemote;
    private bool grabBottle;
    private float durationSeconds = DefaultDurationSeconds;
    private float startTime;
    private float lastStatusLogTime = -StatusLogIntervalSeconds;
    private float grabStartTime;
    private bool roleSwitched;
    private bool grabStarted;
    private bool grabStatusLogged;
    private bool finished;
    private bool quitScheduled;
    private float quitTime;
    private int quitExitCode;
    private bool sessionStartRequested;
    private bool spawnRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!HasArg(args, "-sharePhotonProbe"))
        {
            return;
        }

        GameObject probeObject = new GameObject("PhotonSharedMRAutoProbe");
        DontDestroyOnLoad(probeObject);
        PhotonSharedMRAutoProbe probe = probeObject.AddComponent<PhotonSharedMRAutoProbe>();
        probe.probeLabel = GetArgValue(args, "-sharePhotonProbeLabel", "Probe");
        probe.probeUserName = GetArgValue(args, "-sharePhotonProbeUserName", probe.probeLabel);
        probe.probeRole = GetArgEnum(args, "-sharePhotonProbeRole", SharedUserRole.ManipulatorOperator);
        probe.probeDeviceType = GetArgEnum(args, "-sharePhotonProbeDeviceType", Application.isEditor ? ShareDeviceType.PCEditor : ShareDeviceType.Unknown);
        probe.probeRobotTarget = GetArgEnum(args, "-sharePhotonProbeRobotTarget", SharedMRRobotTarget.Amir);
        probe.probeIsHostLikeUser = GetArgBool(args, "-sharePhotonProbeHostLike", true);
        probe.requireRemote = HasArg(args, "-sharePhotonProbeRequireRemote");
        probe.grabBottle = HasArg(args, "-sharePhotonProbeGrab");
        probe.durationSeconds = Mathf.Max(10f, GetArgFloat(args, "-sharePhotonProbeDuration", DefaultDurationSeconds));
        probe.startTime = Time.realtimeSinceStartup;

        Debug.Log("[PhotonSharedMRAutoProbe] START label=" + probe.probeLabel
            + " requireRemote=" + probe.requireRemote
            + " grabBottle=" + probe.grabBottle
            + " duration=" + probe.durationSeconds
            + " userName=" + probe.probeUserName
            + " role=" + probe.probeRole
            + " deviceType=" + probe.probeDeviceType
            + " robotTarget=" + probe.probeRobotTarget
            + " hostLike=" + probe.probeIsHostLikeUser);
    }

    private void Update()
    {
        if (quitScheduled)
        {
            if (Time.realtimeSinceStartup >= quitTime)
            {
                Application.Quit(quitExitCode);
            }

            return;
        }

        if (finished)
        {
            return;
        }

        float elapsed = Time.realtimeSinceStartup - startTime;
        PhotonFusionSharedRoomBootstrap bootstrap = FindObjectOfType<PhotonFusionSharedRoomBootstrap>();
        PhotonSharedBottleSpawner bottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>();
        RoleBasedInfoFilter filter = FindObjectOfType<RoleBasedInfoFilter>();
        NetworkUserAvatar[] avatars = FindObjectsOfType<NetworkUserAvatar>();
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>();
        NetworkedSharedSceneObject photonSharedBottle = FindPhotonSharedBottle(sharedObjects);
        NetworkedSharedSceneObject bottle = photonSharedBottle != null
            ? photonSharedBottle
            : FindSharedObject(sharedObjects, SharedNetworkObjectKind.Bottle);

        if (!sessionStartRequested && bootstrap != null)
        {
            sessionStartRequested = true;
            PhotonSharedMRSessionSettings settings = PhotonSharedMRSessionSettings.CreateDefault();
            settings.userName = probeUserName;
            settings.role = probeRole;
            settings.deviceType = probeDeviceType;
            settings.robotTarget = probeRobotTarget;
            settings.isHostLikeUser = probeIsHostLikeUser;

            PhotonSharedMRLoginPanel loginPanel = FindObjectOfType<PhotonSharedMRLoginPanel>(true);
            if (loginPanel != null)
            {
                _ = loginPanel.StartSessionWithSettings(settings);
            }
            else
            {
                _ = bootstrap.StartSharedRoom(settings);
            }

            Debug.Log("[PhotonSharedMRAutoProbe] SESSION_START_REQUESTED label=" + probeLabel
                + " userName=" + settings.userName
                + " role=" + settings.role
                + " deviceType=" + settings.deviceType
                + " robotTarget=" + settings.robotTarget
                + " hostLike=" + settings.isHostLikeUser);
        }

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = bootstrap != null ? bootstrap.Runner : null;
        bool runnerRunning = runner != null && runner.IsRunning;
        string sessionName = runnerRunning && runner.SessionInfo != null ? runner.SessionInfo.Name : "none";
        int activePlayers = CountActivePlayers(runner);
#else
        bool runnerRunning = false;
        string sessionName = "fusion-inactive";
        int activePlayers = 0;
#endif
        bool localAvatar = NetworkUserAvatar.Local != null;
        bool remoteAvatar = avatars.Length >= 2 || activePlayers >= 2;

        if (runnerRunning && localAvatar && bottleSpawner != null && !spawnRequested
            && (!requireRemote || probeIsHostLikeUser))
        {
            spawnRequested = true;
            bottleSpawner.RequestSpawnInFrontOfHmd();
            Debug.Log("[PhotonSharedMRAutoProbe] DYNAMIC_BOTTLE_SPAWN_REQUEST label=" + probeLabel
                + " prefab=" + (bottleSpawner.networkBottlePrefab != null ? bottleSpawner.networkBottlePrefab.name : "MissingPrefab")
                + " sharedBottleCountBefore=" + bottleSpawner.SharedNetworkBottleCount);
        }

        if (runnerRunning && !roleSwitched && filter != null)
        {
            filter.SetScoutRole();
            filter.SetSupervisorRole();
            filter.SetManipulatorOperatorRole();
            roleSwitched = true;
            Debug.Log("[PhotonSharedMRAutoProbe] ROLE_SWITCH label=" + probeLabel);
        }

        if (elapsed - lastStatusLogTime >= StatusLogIntervalSeconds)
        {
            lastStatusLogTime = elapsed;
            Debug.Log("[PhotonSharedMRAutoProbe] STATUS label=" + probeLabel
                + " runnerRunning=" + runnerRunning
                + " session=" + sessionName
                + " activePlayers=" + activePlayers
                + " avatarCount=" + avatars.Length
                + " sharedObjectCount=" + sharedObjects.Length
                + " bottlePresent=" + (bottle != null)
                + " photonSharedBottlePresent=" + (photonSharedBottle != null)
                + " photonSharedBottleCount=" + (bottleSpawner != null ? bottleSpawner.SharedNetworkBottleCount : 0)
                + " localAvatar=" + localAvatar
                + " remoteAvatar=" + remoteAvatar);
        }

        if (grabBottle && remoteAvatar && bottle != null && !grabStarted)
        {
            grabStarted = true;
            grabStartTime = Time.realtimeSinceStartup;
            bool grabAccepted = bottle.TryBeginLocalGrab();
            Debug.Log("[PhotonSharedMRAutoProbe] GRAB_TRY label=" + probeLabel
                + " accepted=" + grabAccepted
                + " bottle=" + bottle.name);
#if FUSION_WEAVER && FUSION2
            if (!grabAccepted || bottle.HasStateAuthority)
            {
                grabStatusLogged = true;
                LogGrabStatus(bottle);
            }
#else
            grabStatusLogged = true;
            LogGrabStatus(bottle);
#endif
        }

        if (grabStarted && !grabStatusLogged && Time.realtimeSinceStartup - grabStartTime >= GrabObservationSeconds)
        {
            grabStatusLogged = true;
            LogGrabStatus(bottle);
        }

        bool pass = runnerRunning
            && localAvatar
            && roleSwitched
            && photonSharedBottle != null
            && (!requireRemote || remoteAvatar)
            && (!grabBottle || grabStatusLogged);

        if (pass)
        {
            Finish(true, "pass");
        }
        else if (elapsed > durationSeconds)
        {
            Finish(false, "timeout");
        }
    }

    private void LogGrabStatus(NetworkedSharedSceneObject bottle)
    {
        if (bottle == null)
        {
            Debug.Log("[PhotonSharedMRAutoProbe] GRAB_STATUS label=" + probeLabel + " bottlePresent=False");
            return;
        }

#if FUSION_WEAVER && FUSION2
        Debug.Log("[PhotonSharedMRAutoProbe] GRAB_STATUS label=" + probeLabel
            + " bottlePresent=True"
            + " hasStateAuthority=" + bottle.HasStateAuthority
            + " isGrabbed=" + bottle.IsGrabbed
            + " isLockedByOther=" + bottle.IsLockedByOther
            + " lockOwner=" + bottle.LockOwner
            + " localPlayer=" + (bottle.Runner != null ? bottle.Runner.LocalPlayer.ToString() : "none"));
#else
        Debug.Log("[PhotonSharedMRAutoProbe] GRAB_STATUS label=" + probeLabel
            + " bottlePresent=True fusionInactive=True");
#endif
    }

    private void Finish(bool success, string reason)
    {
        finished = true;
        Debug.Log("[PhotonSharedMRAutoProbe] FINISH label=" + probeLabel
            + " success=" + success
            + " reason=" + reason);
        quitExitCode = success ? 0 : 1;

        if (success && requireRemote)
        {
            quitScheduled = true;
            quitTime = Time.realtimeSinceStartup + RemoteSuccessHoldSeconds;
            Debug.Log("[PhotonSharedMRAutoProbe] HOLD_AFTER_PASS label=" + probeLabel
                + " seconds=" + RemoteSuccessHoldSeconds);
            return;
        }

        Application.Quit(quitExitCode);
    }

    private static NetworkedSharedSceneObject FindSharedObject(NetworkedSharedSceneObject[] sharedObjects, SharedNetworkObjectKind kind)
    {
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            if (sharedObjects[i] != null && sharedObjects[i].objectKind == kind)
            {
                return sharedObjects[i];
            }
        }

        return null;
    }

    private static NetworkedSharedSceneObject FindPhotonSharedBottle(NetworkedSharedSceneObject[] sharedObjects)
    {
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            if (sharedObjects[i] != null && sharedObjects[i].IsPhotonSharedNetworkBottle)
            {
                return sharedObjects[i];
            }
        }

        return null;
    }

#if FUSION_WEAVER && FUSION2
    private static int CountActivePlayers(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning)
        {
            return 0;
        }

        int count = 0;
        foreach (PlayerRef ignored in runner.ActivePlayers)
        {
            count++;
        }

        return count;
    }
#endif

    private static bool HasArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static float GetArgFloat(string[] args, string key, float fallback)
    {
        string value = GetArgValue(args, key, null);
        return float.TryParse(value, out float result) ? result : fallback;
    }

    private static bool GetArgBool(string[] args, string key, bool fallback)
    {
        string value = GetArgValue(args, key, null);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out bool result))
        {
            return result;
        }

        if (int.TryParse(value, out int intResult))
        {
            return intResult != 0;
        }

        return fallback;
    }

    private static T GetArgEnum<T>(string[] args, string key, T fallback) where T : struct, Enum
    {
        string value = GetArgValue(args, key, null);
        return Enum.TryParse(value, true, out T result) ? result : fallback;
    }
}
