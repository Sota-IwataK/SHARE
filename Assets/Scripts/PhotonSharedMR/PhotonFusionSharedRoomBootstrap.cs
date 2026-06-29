using System;
using UnityEngine;
using UnityEngine.Serialization;

#if FUSION_WEAVER && FUSION2
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
#endif

[DisallowMultipleComponent]
public class PhotonFusionSharedRoomBootstrap : MonoBehaviour
#if FUSION_WEAVER && FUSION2
    , INetworkRunnerCallbacks
#endif
{
    [Header("Room")]
    public string roomName = "SHARE-MR-Room";
    [FormerlySerializedAs("autoStart")]
    public bool autoJoinOnStart = true;
    public int maxPlayers = 8;
    public SharedUserRole initialRole = SharedUserRole.ManipulatorOperator;
    public PhotonSharedMRSessionSettings defaultSessionSettings = PhotonSharedMRSessionSettings.CreateDefault();
    public bool useAutoHostOrClient = true;

    [Header("Prefabs")]
    public GameObject networkUserAvatarPrefab;

    [Header("Local Tracking")]
    public Transform headSource;
    public Transform leftHandSource;
    public Transform rightHandSource;

    private string lastJoinStatus = "NotStarted";
    private string lastError = string.Empty;
    private bool applicationQuitting;

    public string LastJoinStatus => lastJoinStatus;
    public string LastError => lastError;
    public string DebugRoomName => ResolveDebugRoomName();
    public bool Joined => IsRunning;
    public bool RunnerIsRunning => IsRunning;
    public bool RunnerExists => ResolveRunnerExists();
    public int ActivePlayersCount => ResolveActivePlayersCount();
    public string LocalPlayerDebugText => ResolveLocalPlayerDebugText();
    public SharedUserRole DebugCurrentRole => ResolveDebugCurrentRole();
    public ShareDeviceType DebugDeviceType => ResolveDebugDeviceType();
    public string DebugFixedRegion => ResolveDebugFixedRegion();
    public string DebugProtocol => ResolveDebugProtocol();
    public bool UsesAutoHostOrClient => useAutoHostOrClient;

#if FUSION_WEAVER && FUSION2
    private NetworkRunner runner;
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedAvatars = new Dictionary<PlayerRef, NetworkObject>();
    private PhotonSharedMRSessionSettings activeSessionSettings;
    private int nextDisplayPlayerNumber = 1;
    private int nextObserverDisplayNumber = 1;
    private string lastDisconnectReason = "None";
    private bool shutdownRequestSeen;
    private string lastShutdownRequestReason = "None";
    private bool lastShutdownRequestExplicitLeave;
    private bool lastShutdownRequestApplicationQuit;

    public NetworkRunner Runner => runner;
    public bool IsRunning => runner != null && runner.IsRunning;

    private async void Start()
    {
        if (autoJoinOnStart)
        {
            await StartSharedRoom(defaultSessionSettings);
        }
    }

    private void OnEnable()
    {
        PhotonSharedMRCalibrationGuard.LogBootstrapEnabled(transform);
    }

    private void OnDisable()
    {
        PhotonSharedMRCalibrationGuard.LogBootstrapDisabled(transform);
    }

    private void OnDestroy()
    {
        PhotonSharedMRCalibrationGuard.LogBootstrapDestroyed(transform);
    }

    private void OnApplicationQuit()
    {
        applicationQuitting = true;
        RequestRunnerShutdown("ApplicationQuit", false);
    }

    public async Task StartSharedRoom()
    {
        await StartSharedRoom(defaultSessionSettings);
    }

    public async Task StartSharedRoom(PhotonSharedMRSessionSettings sessionSettings)
    {
        if (runner != null && runner.IsRunning)
        {
            lastJoinStatus = "AlreadyRunning";
            lastError = string.Empty;
            return;
        }

        if (networkUserAvatarPrefab == null)
        {
            lastJoinStatus = "MissingAvatarPrefab";
            lastError = "networkUserAvatarPrefab is not assigned.";
            Debug.LogError("[PhotonFusionSharedRoomBootstrap] " + lastError);
            return;
        }

        lastJoinStatus = "Starting";
        lastError = string.Empty;
        lastDisconnectReason = "None";
        shutdownRequestSeen = false;
        lastShutdownRequestReason = "None";
        lastShutdownRequestExplicitLeave = false;
        lastShutdownRequestApplicationQuit = false;
        activeSessionSettings = sessionSettings != null
            ? sessionSettings.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();
        activeSessionSettings.Sanitize();
        roomName = activeSessionSettings.roomName;
        initialRole = activeSessionSettings.role;
        NetworkUserAvatar.SetPendingLocalSessionSettings(activeSessionSettings);

        runner = GetComponent<NetworkRunner>();
        if (runner == null)
        {
            runner = gameObject.AddComponent<NetworkRunner>();
        }

        runner.ProvideInput = false;
        runner.AddCallbacks(this);

        INetworkSceneManager sceneManager = runner.GetComponent<INetworkSceneManager>();
        if (sceneManager == null)
        {
            sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        INetworkObjectProvider objectProvider = runner.GetComponent<INetworkObjectProvider>();
        if (objectProvider == null)
        {
            objectProvider = runner.gameObject.AddComponent<NetworkObjectProviderDefault>();
        }

        NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
        {
            sceneInfo.AddSceneRef(SceneRef.FromIndex(activeScene.buildIndex), LoadSceneMode.Additive);
        }

        StartGameResult result;
        try
        {
            result = await runner.StartGame(new StartGameArgs
            {
                GameMode = useAutoHostOrClient ? GameMode.AutoHostOrClient : GameMode.Shared,
                SessionName = roomName,
                PlayerCount = maxPlayers,
                Scene = sceneInfo,
                SceneManager = sceneManager,
                ObjectProvider = objectProvider
            });
        }
        catch (Exception ex)
        {
            lastJoinStatus = "Exception";
            lastError = ex.GetType().Name + ": " + ex.Message;
            Debug.LogError("[PhotonFusionSharedRoomBootstrap] Exception while joining room "
                + roomName + " " + lastError);
            return;
        }

        if (!result.Ok)
        {
            lastJoinStatus = "Failed";
            lastError = "reason=" + result.ShutdownReason + " message=" + result.ErrorMessage;
            Debug.LogError("[PhotonFusionSharedRoomBootstrap] Failed to join room " + roomName
                + " " + lastError);
            return;
        }

        lastJoinStatus = "Joined";
        lastError = string.Empty;
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Joined shared room " + roomName);
    }

    public void LeaveRoom()
    {
        RequestRunnerShutdown("LeaveRoom", true);
    }

    public void SetLocalRole(SharedUserRole role)
    {
        initialRole = role;
        if (activeSessionSettings != null)
        {
            activeSessionSettings.role = role;
        }

        if (NetworkUserAvatar.Local != null)
        {
            NetworkUserAvatar.Local.SetRole(role);
        }
    }

    public void OnPlayerJoined(NetworkRunner joinedRunner, PlayerRef player)
    {
        Debug.Log("[PhotonFusionSharedRoomBootstrap] OnPlayerJoined"
            + " PlayerRef=" + player
            + " activePlayers=" + CountActivePlayers(joinedRunner)
            + " local=" + joinedRunner.LocalPlayer);

        bool sharedLikeLocalSpawn = joinedRunner.GameMode == GameMode.Shared
            || joinedRunner.GameMode == GameMode.AutoHostOrClient;
        bool shouldSpawnAvatar = joinedRunner.IsServer || (sharedLikeLocalSpawn && player == joinedRunner.LocalPlayer);
        if (!shouldSpawnAvatar || spawnedAvatars.ContainsKey(player))
        {
            return;
        }

        Vector3 spawnPosition = ResolveHeadSource() != null
            ? ResolveHeadSource().position
            : transform.position;
        Quaternion spawnRotation = ResolveHeadSource() != null
            ? ResolveHeadSource().rotation
            : transform.rotation;

        int displayPlayerNumber = AllocateDisplayPlayerNumber(joinedRunner);
        NetworkObject avatar = joinedRunner.Spawn(
            networkUserAvatarPrefab,
            spawnPosition,
            spawnRotation,
            player,
            (_, obj) =>
            {
                NetworkUserAvatar networkUser = obj.GetComponent<NetworkUserAvatar>();
                if (networkUser != null)
                {
                    if (player == joinedRunner.LocalPlayer)
                    {
                        networkUser.ConfigureLocalSources(headSource, leftHandSource, rightHandSource);
                        networkUser.ApplyLocalSessionSettings(ResolveActiveSessionSettings());
                    }

                    networkUser.SetDisplayPlayerNumber(displayPlayerNumber);
                    networkUser.EnsureObserverDisplayNumberAssigned();
                }
            });

        spawnedAvatars[player] = avatar;
        joinedRunner.SetPlayerObject(player, avatar);
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Spawned local avatar for " + player
            + " displayPlayerNumber=" + displayPlayerNumber
            + " activePlayers=" + CountActivePlayers(joinedRunner));
    }

    public void OnPlayerLeft(NetworkRunner leftRunner, PlayerRef player)
    {
        Debug.Log("[PhotonFusionSharedRoomBootstrap] OnPlayerLeft"
            + " PlayerRef=" + player
            + " activePlayers=" + CountActivePlayers(leftRunner));

        if (spawnedAvatars.TryGetValue(player, out NetworkObject avatar) && avatar != null)
        {
            leftRunner.Despawn(avatar);
        }

        spawnedAvatars.Remove(player);
    }

    private Transform ResolveHeadSource()
    {
        if (headSource != null)
        {
            return headSource;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            headSource = mainCamera.transform;
        }

        return headSource;
    }

    private PhotonSharedMRSessionSettings ResolveActiveSessionSettings()
    {
        if (activeSessionSettings == null)
        {
            activeSessionSettings = defaultSessionSettings != null
                ? defaultSessionSettings.Clone()
                : PhotonSharedMRSessionSettings.CreateDefault();
            activeSessionSettings.Sanitize();
            activeSessionSettings.roomName = roomName;
            activeSessionSettings.role = initialRole;
        }

        return activeSessionSettings;
    }

    public void OnConnectedToServer(NetworkRunner connectedRunner) { }
    public void OnConnectFailed(NetworkRunner failedRunner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        lastJoinStatus = "ConnectFailed";
        lastError = reason.ToString();
        lastDisconnectReason = "ConnectFailed:" + reason;
        Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] PHOTON_FUSION_CONNECT_FAILED"
            + " reason=" + reason
            + " remoteAddress=" + remoteAddress
            + BuildFusionRunnerDiagnostics(failedRunner, lastJoinStatus)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
    }
    public void OnConnectRequest(NetworkRunner requestRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner authRunner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner disconnectedRunner, NetDisconnectReason reason)
    {
        lastJoinStatus = "Disconnected";
        lastError = reason.ToString();
        lastDisconnectReason = reason.ToString();
        Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] PHOTON_FUSION_DISCONNECTED"
            + " disconnectReason=" + reason
            + BuildFusionRunnerDiagnostics(disconnectedRunner, lastJoinStatus)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
    }
    public void OnHostMigration(NetworkRunner migrationRunner, HostMigrationToken hostMigrationToken) { }
    public void OnInput(NetworkRunner inputRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner inputRunner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner aoiRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner aoiRunner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner dataRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner dataRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner sceneRunner) { }
    public void OnSceneLoadStart(NetworkRunner sceneRunner) { }
    public void OnSessionListUpdated(NetworkRunner sessionRunner, List<SessionInfo> sessionList) { }
    public void OnShutdown(NetworkRunner shutdownRunner, ShutdownReason shutdownReason)
    {
        string joinStatusBeforeShutdown = lastJoinStatus;
        Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] PHOTON_FUSION_SHUTDOWN"
            + " reason=" + shutdownReason
            + " shutdownReason=" + shutdownReason
            + " disconnectReason=" + lastDisconnectReason
            + " shutdownRequestSeen=" + shutdownRequestSeen
            + " shutdownRequestReason=" + lastShutdownRequestReason
            + " shutdownRequestExplicitLeave=" + lastShutdownRequestExplicitLeave
            + " shutdownRequestApplicationQuit=" + lastShutdownRequestApplicationQuit
            + BuildFusionRunnerDiagnostics(shutdownRunner, joinStatusBeforeShutdown)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace()));
        lastJoinStatus = "Shutdown";
        lastError = shutdownReason.ToString();
        PhotonSharedMRCalibrationGuard.LogRunnerShutdown(shutdownReason.ToString());
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Shutdown: " + shutdownReason);
        spawnedAvatars.Clear();
        runner = null;
        nextDisplayPlayerNumber = 1;
        nextObserverDisplayNumber = 1;
    }
    public void OnUserSimulationMessage(NetworkRunner messageRunner, SimulationMessagePtr message) { }

    private static int CountActivePlayers(NetworkRunner sourceRunner)
    {
        if (sourceRunner == null)
        {
            return 0;
        }

        int count = 0;
        foreach (PlayerRef _ in sourceRunner.ActivePlayers)
        {
            count++;
        }

        return count;
    }

    private int AllocateDisplayPlayerNumber(NetworkRunner sourceRunner)
    {
        if (sourceRunner != null && sourceRunner.IsServer)
        {
            return nextDisplayPlayerNumber++;
        }

        return Mathf.Max(1, CountActivePlayers(sourceRunner));
    }

    public int AllocateObserverDisplayNumberForStateAuthority()
    {
        int highestAssignedObserverDisplayNumber = GetHighestAssignedObserverDisplayNumber();
        int observerDisplayNumber = Mathf.Max(
            1,
            nextObserverDisplayNumber,
            highestAssignedObserverDisplayNumber + 1);
        nextObserverDisplayNumber = observerDisplayNumber + 1;
        Debug.Log("[PhotonFusionSharedRoomBootstrap] OBSERVER_DISPLAY_NUMBER_ALLOCATED"
            + " observerDisplayNumber=" + observerDisplayNumber
            + " nextObserverDisplayNumber=" + nextObserverDisplayNumber
            + " highestAssignedObserverDisplayNumber=" + highestAssignedObserverDisplayNumber
            + " usesPlayerRef=False");
        return observerDisplayNumber;
    }

    private static int GetHighestAssignedObserverDisplayNumber()
    {
        int highest = 0;
        NetworkUserAvatar[] avatars = FindObjectsOfType<NetworkUserAvatar>(true);
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar != null && avatar.ObserverDisplayNumber > highest)
            {
                highest = avatar.ObserverDisplayNumber;
            }
        }

        return highest;
    }

    private void RequestRunnerShutdown(string reason, bool explicitLeave)
    {
        if (runner == null)
        {
            return;
        }

        bool allowed = explicitLeave || applicationQuitting;
        shutdownRequestSeen = true;
        lastShutdownRequestReason = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;
        lastShutdownRequestExplicitLeave = explicitLeave;
        lastShutdownRequestApplicationQuit = applicationQuitting;
        string message = "[PhotonFusionSharedRoomBootstrap] PHOTON_RUNNER_SHUTDOWN_REQUEST"
            + " reason=" + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason)
            + " explicitLeave=" + explicitLeave
            + " applicationQuitting=" + applicationQuitting
            + " calibrationInProgress=" + PhotonSharedMRCalibrationGuard.CalibrationInProgress
            + PhotonSharedMRCalibrationGuard.BuildTimingContext(transform)
            + " stackTrace=" + FormatStackTraceForLog(StackTraceUtility.ExtractStackTrace());
        if (allowed)
        {
            Debug.Log(message);
        }
        else
        {
            Debug.LogWarning(message);
            return;
        }

        lastJoinStatus = explicitLeave ? "Leaving" : "ApplicationQuit";
        lastError = string.Empty;
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Leaving shared room " + DebugRoomName
            + " reason=" + reason);
        runner.Shutdown();
    }

    private string BuildFusionRunnerDiagnostics(NetworkRunner sourceRunner, string joinStatus)
    {
        return " runnerExists=" + (sourceRunner != null)
            + " runnerIsRunning=" + SafeRunnerIsRunning(sourceRunner)
            + " joinStatus=" + (string.IsNullOrWhiteSpace(joinStatus) ? "Unknown" : joinStatus)
            + " calibrationInProgress=" + PhotonSharedMRCalibrationGuard.CalibrationInProgress
            + " timeSinceCalibrationStartMs=" + FormatFloatForLog(PhotonSharedMRCalibrationGuard.CalibrationElapsedMs)
            + " gameMode=" + SafeGameMode(sourceRunner)
            + " sessionName=" + SafeSessionName(sourceRunner)
            + " localPlayer=" + SafeLocalPlayer(sourceRunner)
            + " activePlayers=" + SafeActivePlayers(sourceRunner)
            + PhotonSharedMRCalibrationGuard.BuildTimingContext(transform);
    }

    private static bool SafeRunnerIsRunning(NetworkRunner sourceRunner)
    {
        try
        {
            return sourceRunner != null && sourceRunner.IsRunning;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] Failed to read runner.IsRunning: " + ex.Message);
            return false;
        }
    }

    private static string SafeGameMode(NetworkRunner sourceRunner)
    {
        try
        {
            return sourceRunner != null ? sourceRunner.GameMode.ToString() : "None";
        }
        catch (Exception ex)
        {
            return "Unavailable:" + ex.GetType().Name;
        }
    }

    private static string SafeSessionName(NetworkRunner sourceRunner)
    {
        try
        {
            if (sourceRunner != null && sourceRunner.SessionInfo.IsValid)
            {
                return string.IsNullOrWhiteSpace(sourceRunner.SessionInfo.Name)
                    ? "None"
                    : sourceRunner.SessionInfo.Name;
            }
        }
        catch (Exception ex)
        {
            return "Unavailable:" + ex.GetType().Name;
        }

        return "None";
    }

    private static string SafeLocalPlayer(NetworkRunner sourceRunner)
    {
        try
        {
            return sourceRunner != null ? sourceRunner.LocalPlayer.ToString() : "None";
        }
        catch (Exception ex)
        {
            return "Unavailable:" + ex.GetType().Name;
        }
    }

    private static int SafeActivePlayers(NetworkRunner sourceRunner)
    {
        try
        {
            return CountActivePlayers(sourceRunner);
        }
        catch
        {
            return -1;
        }
    }
#else
    public bool IsRunning => false;

    private void Start()
    {
        if (autoJoinOnStart)
        {
            Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] Photon Fusion 2 is not active in this project. "
                + "Import Photon Fusion 2 so FUSION_WEAVER and FUSION2 are defined, then run the SHARE Photon setup again.");
        }
    }

    public System.Threading.Tasks.Task StartSharedRoom()
    {
        return StartSharedRoom(defaultSessionSettings);
    }

    public System.Threading.Tasks.Task StartSharedRoom(PhotonSharedMRSessionSettings sessionSettings)
    {
        if (sessionSettings != null)
        {
            roomName = sessionSettings.roomName;
            initialRole = sessionSettings.role;
        }

        lastJoinStatus = "FusionDisabled";
        lastError = "Photon Fusion 2 is not active.";
        Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] Photon Fusion 2 is not active; cannot join a shared room.");
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public void SetLocalRole(SharedUserRole role)
    {
        initialRole = role;
        if (NetworkUserAvatar.Local != null)
        {
            NetworkUserAvatar.Local.SetRole(role);
        }
    }

    public void LeaveRoom()
    {
        lastJoinStatus = "FusionDisabled";
        lastError = "Photon Fusion 2 is not active.";
    }
#endif

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

    private static string FormatFloatForLog(float value)
    {
        return value < 0f ? "None" : value.ToString("F1");
    }

    private bool ResolveRunnerExists()
    {
#if FUSION_WEAVER && FUSION2
        return runner != null;
#else
        return false;
#endif
    }

    private int ResolveActivePlayersCount()
    {
#if FUSION_WEAVER && FUSION2
        return runner != null && runner.IsRunning ? CountActivePlayers(runner) : 0;
#else
        return 0;
#endif
    }

    private string ResolveLocalPlayerDebugText()
    {
#if FUSION_WEAVER && FUSION2
        return runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None";
#else
        return "Unavailable";
#endif
    }

    private string ResolveDebugRoomName()
    {
#if FUSION_WEAVER && FUSION2
        if (runner != null && runner.IsRunning && runner.SessionInfo.IsValid && !string.IsNullOrWhiteSpace(runner.SessionInfo.Name))
        {
            return runner.SessionInfo.Name;
        }
#endif
        return string.IsNullOrWhiteSpace(roomName) ? PhotonSharedMRSessionSettings.DefaultRoomName : roomName;
    }

    private SharedUserRole ResolveDebugCurrentRole()
    {
        if (NetworkUserAvatar.Local != null)
        {
            return NetworkUserAvatar.Local.CurrentRole;
        }

#if FUSION_WEAVER && FUSION2
        if (activeSessionSettings != null)
        {
            return activeSessionSettings.role;
        }
#endif
        return initialRole;
    }

    private ShareDeviceType ResolveDebugDeviceType()
    {
        if (NetworkUserAvatar.Local != null)
        {
            return NetworkUserAvatar.Local.DeviceType;
        }

#if FUSION_WEAVER && FUSION2
        if (activeSessionSettings != null)
        {
            return activeSessionSettings.deviceType;
        }
#endif
        return defaultSessionSettings != null ? defaultSessionSettings.deviceType : ShareDeviceType.Unknown;
    }

    private string ResolveDebugFixedRegion()
    {
#if FUSION_WEAVER && FUSION2
        try
        {
            if (Fusion.Photon.Realtime.PhotonAppSettings.TryGetGlobal(out Fusion.Photon.Realtime.PhotonAppSettings settings)
                && settings != null
                && settings.AppSettings != null)
            {
                string fixedRegion = settings.AppSettings.FixedRegion;
                return string.IsNullOrWhiteSpace(fixedRegion) ? "Auto" : fixedRegion;
            }
        }
        catch (Exception ex)
        {
            return "Unavailable: " + ex.GetType().Name;
        }
#endif
        return "Unavailable";
    }

    private string ResolveDebugProtocol()
    {
#if FUSION_WEAVER && FUSION2
        try
        {
            if (Fusion.Photon.Realtime.PhotonAppSettings.TryGetGlobal(out Fusion.Photon.Realtime.PhotonAppSettings settings)
                && settings != null
                && settings.AppSettings != null)
            {
                return settings.AppSettings.Protocol.ToString();
            }
        }
        catch (Exception ex)
        {
            return "Unavailable: " + ex.GetType().Name;
        }
#endif
        return "Unavailable";
    }
}
