using UnityEngine;
using UnityEngine.Serialization;

#if FUSION_WEAVER && FUSION2
using Fusion;
using Fusion.Sockets;
using System;
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

    [Header("Prefabs")]
    public GameObject networkUserAvatarPrefab;

    [Header("Local Tracking")]
    public Transform headSource;
    public Transform leftHandSource;
    public Transform rightHandSource;

#if FUSION_WEAVER && FUSION2
    private NetworkRunner runner;
    private readonly Dictionary<PlayerRef, NetworkObject> spawnedAvatars = new Dictionary<PlayerRef, NetworkObject>();
    private PhotonSharedMRSessionSettings activeSessionSettings;

    public NetworkRunner Runner => runner;
    public bool IsRunning => runner != null && runner.IsRunning;

    private async void Start()
    {
        if (autoJoinOnStart)
        {
            await StartSharedRoom(defaultSessionSettings);
        }
    }

    public async Task StartSharedRoom()
    {
        await StartSharedRoom(defaultSessionSettings);
    }

    public async Task StartSharedRoom(PhotonSharedMRSessionSettings sessionSettings)
    {
        if (runner != null && runner.IsRunning)
        {
            return;
        }

        if (networkUserAvatarPrefab == null)
        {
            Debug.LogError("[PhotonFusionSharedRoomBootstrap] networkUserAvatarPrefab is not assigned.");
            return;
        }

        activeSessionSettings = sessionSettings != null
            ? sessionSettings.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();
        activeSessionSettings.Sanitize();
        roomName = activeSessionSettings.roomName;
        initialRole = activeSessionSettings.role;

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

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = maxPlayers,
            Scene = sceneInfo,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider
        });

        if (!result.Ok)
        {
            Debug.LogError("[PhotonFusionSharedRoomBootstrap] Failed to join room " + roomName
                + " reason=" + result.ShutdownReason + " message=" + result.ErrorMessage);
            return;
        }

        Debug.Log("[PhotonFusionSharedRoomBootstrap] Joined shared room " + roomName);
    }

    public void LeaveRoom()
    {
        if (runner == null)
        {
            return;
        }

        runner.Shutdown();
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
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Player joined " + player
            + " local=" + joinedRunner.LocalPlayer);

        if (player != joinedRunner.LocalPlayer)
        {
            return;
        }

        Vector3 spawnPosition = ResolveHeadSource() != null
            ? ResolveHeadSource().position
            : transform.position;
        Quaternion spawnRotation = ResolveHeadSource() != null
            ? ResolveHeadSource().rotation
            : transform.rotation;

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
                    networkUser.ConfigureLocalSources(headSource, leftHandSource, rightHandSource);
                    networkUser.ApplyLocalSessionSettings(ResolveActiveSessionSettings());
                }
            });

        spawnedAvatars[player] = avatar;
        joinedRunner.SetPlayerObject(player, avatar);
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Spawned local avatar for " + player);
    }

    public void OnPlayerLeft(NetworkRunner leftRunner, PlayerRef player)
    {
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
        Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] Connect failed: " + reason);
    }
    public void OnConnectRequest(NetworkRunner requestRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner authRunner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner disconnectedRunner, NetDisconnectReason reason)
    {
        Debug.LogWarning("[PhotonFusionSharedRoomBootstrap] Disconnected: " + reason);
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
        Debug.Log("[PhotonFusionSharedRoomBootstrap] Shutdown: " + shutdownReason);
        spawnedAvatars.Clear();
        runner = null;
    }
    public void OnUserSimulationMessage(NetworkRunner messageRunner, SimulationMessagePtr message) { }
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
#endif
}
