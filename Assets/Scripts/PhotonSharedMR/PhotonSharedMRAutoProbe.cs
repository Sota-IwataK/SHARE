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
    private SharedMRParticipantId probeParticipantId = SharedMRParticipantId.User1;
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
    private bool p102Validation;
    private string p102Script = "none";
    private float p102NextActionTime;
    private int p102ActionIndex;
    private int p102LastLoggedSequence = int.MinValue;
    private int p102LastLoggedPlayers = -1;
    private float p102LastSnapshotTime = -5f;

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
        probe.probeParticipantId = GetArgEnum(args, "-sharePhotonProbeParticipantId", SharedMRParticipantId.User1);
        probe.probeRole = GetArgEnum(args, "-sharePhotonProbeRole", SharedUserRole.ManipulatorOperator);
        probe.probeDeviceType = GetArgEnum(args, "-sharePhotonProbeDeviceType", Application.isEditor ? ShareDeviceType.PCEditor : ShareDeviceType.Unknown);
        probe.probeRobotTarget = GetArgEnum(args, "-sharePhotonProbeRobotTarget", SharedMRRobotTarget.Amir);
        probe.probeIsHostLikeUser = GetArgBool(args, "-sharePhotonProbeHostLike", true);
        probe.requireRemote = HasArg(args, "-sharePhotonProbeRequireRemote");
        probe.grabBottle = HasArg(args, "-sharePhotonProbeGrab");
        probe.durationSeconds = Mathf.Max(10f, GetArgFloat(args, "-sharePhotonProbeDuration", DefaultDurationSeconds));
        probe.p102Validation = HasArg(args, "-p102Validation");
        probe.p102Script = GetArgValue(args, "-p102Script", "none");
        probe.startTime = Time.realtimeSinceStartup;

        Debug.Log("[PhotonSharedMRAutoProbe] START label=" + probe.probeLabel
            + " requireRemote=" + probe.requireRemote
            + " grabBottle=" + probe.grabBottle
            + " duration=" + probe.durationSeconds
            + " userName=" + probe.probeUserName
            + " participantId=" + probe.probeParticipantId
            + " role=" + probe.probeRole
            + " deviceType=" + probe.probeDeviceType
            + " robotTarget=" + probe.probeRobotTarget
            + " hostLike=" + probe.probeIsHostLikeUser
            + " p102Validation=" + probe.p102Validation
            + " p102Script=" + probe.p102Script);
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
            settings.participantId = probeParticipantId;
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
                + " participantId=" + settings.participantId
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

#if FUSION_WEAVER && FUSION2
        if (p102Validation && runnerRunning)
        {
            UpdateP102Validation(runner, activePlayers);
        }
#endif

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

        if (p102Validation)
        {
            return;
        }

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
    private void UpdateP102Validation(NetworkRunner runner, int activePlayers)
    {
        SharedTeamControlStateNetwork control = SharedTeamControlStateNetwork.Instance;
        bool hasControl = control != null && control.Object != null && control.Object.Id.IsValid;
        SharedControlState state = default;
        bool hasState = SharedTeamState.TryReadControl(out state);
        bool changed = activePlayers != p102LastLoggedPlayers
            || (hasState && state.sequence != p102LastLoggedSequence);
        if (changed || Time.realtimeSinceStartup - p102LastSnapshotTime >= 5f)
        {
            p102LastSnapshotTime = Time.realtimeSinceStartup;
            p102LastLoggedPlayers = activePlayers;
            if (hasState) p102LastLoggedSequence = state.sequence;
            NetworkUserAvatar local = NetworkUserAvatar.Local;
            Debug.Log("[P1-02D] event=SNAPSHOT"
                + " quest_label=" + probeLabel
                + " player_ref=" + runner.LocalPlayer
                + " participant_id=" + (local != null ? local.ParticipantId.ToString() : "Unassigned")
                + " room=" + (runner.SessionInfo != null ? runner.SessionInfo.Name : "none")
                + " master=" + (hasControl ? control.Object.StateAuthority.ToString() : "none")
                + " local_is_master=" + runner.IsSharedModeMasterClient
                + " state_authority=" + (hasControl ? control.Object.StateAuthority.ToString() : "none")
                + " network_object_id=" + (hasControl ? control.Object.Id.ToString() : "Invalid")
                + " participant_count=" + activePlayers
                + " human_count=" + FindObjectsOfType<NetworkUserAvatar>().Length
                + " sequence=" + (hasState ? state.sequence : -1)
                + " shared_timestamp=" + (hasState ? state.shared_timestamp : -1)
                + " task_phase=" + (hasState ? state.task_phase.ToString() : "Unavailable")
                + " owner_type=" + (hasState ? state.owner_type.ToString() : "Unavailable")
                + " owner_id=" + (hasState ? state.owner_id : -1));
        }

        if (!runner.IsSharedModeMasterClient || !hasControl || activePlayers < 2)
        {
            return;
        }

        if (p102NextActionTime <= 0f)
        {
            p102NextActionTime = Time.realtimeSinceStartup + 3f;
        }
        if (Time.realtimeSinceStartup < p102NextActionTime)
        {
            return;
        }

        bool attempted = false;
        string action = "none";
        if (string.Equals(p102Script, "t2t4", StringComparison.OrdinalIgnoreCase))
        {
            switch (p102ActionIndex)
            {
                case 0: action = "T2_TARGET"; attempted = control.TrySetTarget(101); break;
                case 1: action = "T3_PREPARING"; attempted = TryProbePhase(control, TaskPhase.Preparing); break;
                case 2: action = "T3_READY"; attempted = TryProbePhase(control, TaskPhase.Ready); break;
                case 3: action = "T3_TRANSFER"; attempted = TryProbePhase(control, TaskPhase.Transfer); break;
                case 4: action = "T3_RELEASED"; attempted = TryProbePhase(control, TaskPhase.Released); break;
                case 5: action = "T3_INDEPENDENT"; attempted = TryProbePhase(control, TaskPhase.Independent); break;
                case 6: action = "T4_NONE"; attempted = control.TrySetOwnership(SharedControlOwnerType.None, -1); break;
                case 7: action = "T4_NEW_AMIR"; attempted = control.TrySetOwnership(SharedControlOwnerType.Robot, 1); break;
                case 8: action = "T4_OLD_AMIR"; attempted = control.TrySetOwnership(SharedControlOwnerType.Robot, 2); break;
                default: return;
            }
        }
        else if (string.Equals(p102Script, "migration", StringComparison.OrdinalIgnoreCase))
        {
            switch (p102ActionIndex)
            {
                case 0: action = "T7_PREPARING"; attempted = TryProbePhase(control, TaskPhase.Preparing); break;
                case 1: action = "T7_READY"; attempted = TryProbePhase(control, TaskPhase.Ready); break;
                case 2: action = "T7_NEW_AMIR"; attempted = control.TrySetOwnership(SharedControlOwnerType.Robot, 1); break;
                default: return;
            }
        }
        else
        {
            return;
        }

        Debug.Log("[P1-02D] event=TEST_API action=" + action
            + " quest_label=" + probeLabel
            + " accepted=" + attempted);
        p102ActionIndex++;
        p102NextActionTime = Time.realtimeSinceStartup + 2f;
    }

    private static bool TryProbePhase(SharedTeamControlStateNetwork control, TaskPhase requested)
    {
        CoordinationInputSnapshot inputs = new CoordinationInputSnapshot(
            coordinationRequested: true,
            readyConditionMet: true,
            transferConditionMet: true,
            releaseConditionMet: true,
            completionConditionMet: true,
            participantsConnected: true,
            inputsFresh: true,
            ownershipConsistent: true);
        return control.TryRequestTaskPhaseTransition(requested, inputs, out _);
    }

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
