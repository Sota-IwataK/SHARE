using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[InitializeOnLoad]
public static class SharePhotonSharedMRPlaySmokeTest
{
    private const string MainScenePath = "Assets/Scenes/main.unity";
    private const float TimeoutSeconds = 35f;
    private const float ObservationSeconds = 12f;
    private const string ActiveKey = "SharePhotonSharedMRPlaySmokeTest.Active";
    private const string StartTimeKey = "SharePhotonSharedMRPlaySmokeTest.StartTime";
    private const string EnteredPlayModeKey = "SharePhotonSharedMRPlaySmokeTest.EnteredPlayMode";
    private const string RoleSwitchedKey = "SharePhotonSharedMRPlaySmokeTest.RoleSwitched";
    private const string RelevantErrorCountKey = "SharePhotonSharedMRPlaySmokeTest.RelevantErrorCount";
    private const string FinishPendingKey = "SharePhotonSharedMRPlaySmokeTest.FinishPending";
    private const string LastSuccessKey = "SharePhotonSharedMRPlaySmokeTest.LastSuccess";
    private const string RequireRemoteKey = "SharePhotonSharedMRPlaySmokeTest.RequireRemote";
    private const string GrabBottleKey = "SharePhotonSharedMRPlaySmokeTest.GrabBottle";
    private const string GrabStartedKey = "SharePhotonSharedMRPlaySmokeTest.GrabStarted";
    private const string GrabStatusLoggedKey = "SharePhotonSharedMRPlaySmokeTest.GrabStatusLogged";
    private const string GrabStartTimeKey = "SharePhotonSharedMRPlaySmokeTest.GrabStartTime";
    private const string ProbeTimeoutKey = "SharePhotonSharedMRPlaySmokeTest.ProbeTimeout";
    private const string StatusLogTimeKey = "SharePhotonSharedMRPlaySmokeTest.StatusLogTime";
    private const string SessionStartRequestedKey = "SharePhotonSharedMRPlaySmokeTest.SessionStartRequested";
    private const string MetadataLoggedKey = "SharePhotonSharedMRPlaySmokeTest.MetadataLogged";
    private const string CursorFilterTestedKey = "SharePhotonSharedMRPlaySmokeTest.CursorFilterTested";
    private const string CursorFilterOffWorkedKey = "SharePhotonSharedMRPlaySmokeTest.CursorFilterOffWorked";
    private const string CursorFilterOnRestoredKey = "SharePhotonSharedMRPlaySmokeTest.CursorFilterOnRestored";
    private const string CursorVerifiedKey = "SharePhotonSharedMRPlaySmokeTest.CursorVerified";
    private const string SpawnRequestedKey = "SharePhotonSharedMRPlaySmokeTest.SpawnRequested";
    private const float GrabObservationSeconds = 4f;
    private const float StatusLogIntervalSeconds = 5f;

    private static double startTime;
    private static bool enteredPlayMode;
    private static bool roleSwitched;
    private static int relevantErrorCount;
    private static bool callbacksAttached;
    private static bool requireRemote;
    private static bool grabBottle;
    private static bool grabStarted;
    private static bool grabStatusLogged;
    private static double grabStartTime;
    private static float probeTimeoutSeconds = TimeoutSeconds;
    private static double lastStatusLogTime;
    private static bool sessionStartRequested;
    private static bool metadataLogged;
    private static bool cursorFilterTested;
    private static bool cursorFilterOffWorked;
    private static bool cursorFilterOnRestored;
    private static bool cursorVerified;
    private static bool spawnRequested;

    static SharePhotonSharedMRPlaySmokeTest()
    {
        if (SessionState.GetBool(ActiveKey, false) || SessionState.GetBool(FinishPendingKey, false))
        {
            LoadState();
            AttachCallbacks();
            EditorApplication.delayCall += ExitAfterFinishIfNeeded;
        }
    }

    public static void RunSingleClient()
    {
        StartPlayProbe(false, false, TimeoutSeconds);
    }

    public static void RunEditorTwoClientProbe()
    {
        StartPlayProbe(true, true, 75f);
    }

    private static void StartPlayProbe(bool nextRequireRemote, bool nextGrabBottle, float nextTimeoutSeconds)
    {
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        startTime = EditorApplication.timeSinceStartup;
        enteredPlayMode = false;
        roleSwitched = false;
        relevantErrorCount = 0;
        requireRemote = nextRequireRemote;
        grabBottle = nextGrabBottle;
        grabStarted = false;
        grabStatusLogged = false;
        grabStartTime = 0d;
        probeTimeoutSeconds = nextTimeoutSeconds;
        lastStatusLogTime = -StatusLogIntervalSeconds;
        sessionStartRequested = false;
        metadataLogged = false;
        cursorFilterTested = false;
        cursorFilterOffWorked = false;
        cursorFilterOnRestored = false;
        cursorVerified = false;
        spawnRequested = false;
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(FinishPendingKey, false);
        SaveState();

        AttachCallbacks();

        Debug.Log("[SharePhotonSharedMRPlaySmokeTest] Starting play smoke test requireRemote="
            + requireRemote + " grabBottle=" + grabBottle + " timeout=" + probeTimeoutSeconds);
        EditorApplication.EnterPlaymode();
    }

    private static void AttachCallbacks()
    {
        if (callbacksAttached)
        {
            return;
        }

        Application.logMessageReceived += OnLogMessageReceived;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += TickSingleClient;
        callbacksAttached = true;
    }

    private static void DetachCallbacks()
    {
        if (!callbacksAttached)
        {
            return;
        }

        Application.logMessageReceived -= OnLogMessageReceived;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.update -= TickSingleClient;
        callbacksAttached = false;
    }

    private static void TickSingleClient()
    {
        LoadState();

        if (!SessionState.GetBool(ActiveKey, false))
        {
            ExitAfterFinishIfNeeded();
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - startTime;
        if (elapsed > probeTimeoutSeconds)
        {
            Finish("timeout", false);
            return;
        }

        if (!enteredPlayMode || !EditorApplication.isPlaying)
        {
            return;
        }

        PhotonFusionSharedRoomBootstrap bootstrap = Object.FindFirstObjectByType<PhotonFusionSharedRoomBootstrap>();
        PhotonSharedMRLoginPanel loginPanel = Object.FindFirstObjectByType<PhotonSharedMRLoginPanel>();
        PhotonSharedBottleSpawner bottleSpawner = Object.FindFirstObjectByType<PhotonSharedBottleSpawner>();
        RoleBasedInfoFilter filter = Object.FindFirstObjectByType<RoleBasedInfoFilter>();
        NetworkUserAvatar[] avatars = Object.FindObjectsOfType<NetworkUserAvatar>();
        NetworkedSharedSceneObject[] sharedObjects = Object.FindObjectsOfType<NetworkedSharedSceneObject>();
        NetworkedSharedSceneObject photonSharedBottle = FindPhotonSharedBottle(sharedObjects);
        NetworkedSharedSceneObject bottle = photonSharedBottle != null
            ? photonSharedBottle
            : FindSharedObject(sharedObjects, SharedNetworkObjectKind.Bottle);
#if FUSION_WEAVER && FUSION2
        NetworkRunner preStartRunner = bootstrap != null ? bootstrap.Runner : null;
        bool preStartRunnerRunning = preStartRunner != null && preStartRunner.IsRunning;
#endif

        if (!sessionStartRequested && bootstrap != null)
        {
            sessionStartRequested = true;
            SaveState();

            PhotonSharedMRSessionSettings settings = CreateEditorProbeSettings();

            if (loginPanel != null)
            {
                _ = loginPanel.StartSessionWithSettings(settings);
            }
            else
            {
                _ = bootstrap.StartSharedRoom(settings);
            }

            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] Session start requested through login flow."
                + " userName=" + settings.userName
                + " role=" + settings.role
                + " deviceType=" + settings.deviceType
                + " robotTarget=" + settings.robotTarget
                + " hostLike=" + settings.isHostLikeUser);
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] PRE_START loginPanel="
                + (loginPanel != null)
                + " autoJoinOnStart=" + bootstrap.autoJoinOnStart
#if FUSION_WEAVER && FUSION2
                + " runnerRunningBeforeStart=" + preStartRunnerRunning
#else
                + " runnerRunningBeforeStart=False"
#endif
                );
        }

        if (!roleSwitched && filter != null)
        {
            SharedUserRole expectedRole = CreateEditorProbeSettings().role;
            filter.SetScoutRole();
            filter.SetSupervisorRole();
            filter.SetManipulatorOperatorRole();
            filter.SetManualRole(expectedRole);
            roleSwitched = true;
            SaveState();
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] RoleBasedInfoFilter role switch completed."
                + " restoredRole=" + expectedRole);
        }

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = bootstrap != null ? bootstrap.Runner : null;
        bool runnerRunning = runner != null && runner.IsRunning;
        string sessionName = runnerRunning && runner.SessionInfo != null ? runner.SessionInfo.Name : "none";
        bool localAvatar = NetworkUserAvatar.Local != null;
        int activePlayers = CountActivePlayers(runner);
        bool remoteAvatar = avatars.Length >= 2 || activePlayers >= 2;
        HmdCursorStatus cursorStatus = CollectHmdCursorStatus();

        if (runnerRunning && localAvatar && bottleSpawner != null && !spawnRequested)
        {
            spawnRequested = true;
            SaveState();
            bottleSpawner.RequestSpawnInFrontOfHmd();
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] DYNAMIC_BOTTLE_SPAWN_REQUEST"
                + " prefab=" + (bottleSpawner.networkBottlePrefab != null ? bottleSpawner.networkBottlePrefab.name : "MissingPrefab")
                + " sharedBottleCountBefore=" + bottleSpawner.SharedNetworkBottleCount);
        }

        if (EditorApplication.timeSinceStartup - lastStatusLogTime >= StatusLogIntervalSeconds)
        {
            lastStatusLogTime = EditorApplication.timeSinceStartup;
            SaveState();
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] STATUS runnerRunning=" + runnerRunning
                + " session=" + sessionName
                + " activePlayers=" + activePlayers
                + " avatarCount=" + avatars.Length
                + " sharedObjectCount=" + sharedObjects.Length
                + " bottlePresent=" + (bottle != null)
                + " photonSharedBottlePresent=" + (photonSharedBottle != null)
                + " photonSharedBottleCount=" + (bottleSpawner != null ? bottleSpawner.SharedNetworkBottleCount : 0)
                + " localAvatar=" + localAvatar
                + " remoteAvatar=" + remoteAvatar
                + " hmdCursorRemoteVisible=" + cursorStatus.RemoteVisibleCount
                + " hmdCursorLocalVisible=" + cursorStatus.LocalVisibleCount
                + " hmdCursorMetadataLabel=" + cursorStatus.RemoteLabelHasMetadata
                + " hmdCursorFacingDot=" + cursorStatus.FacingDot.ToString("F3"));
        }

        if (remoteAvatar && !metadataLogged)
        {
            metadataLogged = true;
            SaveState();
            LogAvatarMetadata(avatars);
        }

        if (requireRemote && remoteAvatar && !cursorFilterTested && filter != null
            && cursorStatus.RemoteVisibleCount > 0
            && cursorStatus.RemoteLabelHasMetadata
            && cursorStatus.LocalVisibleCount == 0
            && cursorStatus.FacingDot >= 0.7f)
        {
            SharedUserRole activeRole = filter.ActiveRole;
            SetHmdCursorVisibilityForRole(filter, activeRole, false);
            filter.SetManualRole(activeRole);
            HmdCursorStatus offStatus = CollectHmdCursorStatus();

            SetHmdCursorVisibilityForRole(filter, activeRole, true);
            filter.SetManualRole(activeRole);
            HmdCursorStatus onStatus = CollectHmdCursorStatus();

            cursorFilterOffWorked = offStatus.RemoteVisibleCount == 0 && offStatus.LocalVisibleCount == 0;
            cursorFilterOnRestored = onStatus.RemoteVisibleCount > 0 && onStatus.LocalVisibleCount == 0;
            cursorVerified = cursorFilterOffWorked
                && cursorFilterOnRestored
                && onStatus.RemoteLabelHasMetadata
                && onStatus.FacingDot >= 0.7f;
            cursorFilterTested = true;
            SaveState();

            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] HMD_CURSOR_VERIFY"
                + " beforeRemoteVisible=" + cursorStatus.RemoteVisibleCount
                + " beforeLocalVisible=" + cursorStatus.LocalVisibleCount
                + " beforeLabel=\"" + cursorStatus.RemoteLabel + "\""
                + " beforeFacingDot=" + cursorStatus.FacingDot.ToString("F3")
                + " offRemoteVisible=" + offStatus.RemoteVisibleCount
                + " onRemoteVisible=" + onStatus.RemoteVisibleCount
                + " filterOffWorked=" + cursorFilterOffWorked
                + " filterOnRestored=" + cursorFilterOnRestored
                + " metadataLabel=" + onStatus.RemoteLabelHasMetadata
                + " verified=" + cursorVerified);
        }

        if (grabBottle && remoteAvatar && bottle != null && !grabStarted)
        {
            grabStarted = true;
            grabStartTime = EditorApplication.timeSinceStartup;
            SaveState();
            bool grabAccepted = bottle.TryBeginLocalGrab();
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] GRAB_TRY accepted=" + grabAccepted
                + " bottle=" + bottle.name);
#if FUSION_WEAVER && FUSION2
            if (!grabAccepted || bottle.HasStateAuthority)
            {
                grabStatusLogged = true;
                SaveState();
                LogGrabStatus(bottle);
            }
#else
            grabStatusLogged = true;
            SaveState();
#endif
        }

        if (grabStarted && !grabStatusLogged
            && EditorApplication.timeSinceStartup - grabStartTime >= GrabObservationSeconds)
        {
            grabStatusLogged = true;
            SaveState();
            LogGrabStatus(bottle);
        }

        bool pass = runnerRunning
            && localAvatar
            && roleSwitched
            && photonSharedBottle != null
            && (!requireRemote || remoteAvatar)
            && (!requireRemote || cursorVerified)
            && (!grabBottle || grabStatusLogged);

        if (pass && (requireRemote || elapsed > ObservationSeconds))
        {
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] PASS runnerRunning=True session="
                + sessionName
                + " localAvatar=True roleSwitch=True"
                + " remoteAvatar=" + remoteAvatar
                + " photonSharedBottle=True"
                + " hmdCursorVerified=" + cursorVerified
                + " grabStatusLogged=" + grabStatusLogged);
            Finish("pass", relevantErrorCount == 0);
        }
#else
        if (elapsed > ObservationSeconds)
        {
            Debug.LogWarning("[SharePhotonSharedMRPlaySmokeTest] Fusion defines are not active.");
            Finish("fusion-defines-missing", false);
        }
#endif
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            enteredPlayMode = true;
            SaveState();
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] Entered Play Mode.");
        }
        else if (state == PlayModeStateChange.EnteredEditMode && enteredPlayMode)
        {
            ExitAfterFinishIfNeeded();
        }
    }

    private static void OnLogMessageReceived(string condition, string stackTrace, UnityEngine.LogType type)
    {
        if (type != UnityEngine.LogType.Error
            && type != UnityEngine.LogType.Exception
            && type != UnityEngine.LogType.Assert)
        {
            return;
        }

        if (condition.Contains("Fusion") || condition.Contains("Photon") || condition.Contains("Network")
            || condition.Contains("PhotonSharedMR") || condition.Contains("SharePhotonSharedMR"))
        {
            relevantErrorCount++;
            SaveState();
        }
    }

    private static void Finish(string reason, bool success)
    {
        Debug.Log("[SharePhotonSharedMRPlaySmokeTest] Finish reason=" + reason
            + " success=" + success + " relevantErrors=" + relevantErrorCount);

        SessionState.SetBool(ActiveKey, false);
        SessionState.SetBool(FinishPendingKey, true);
        SessionState.SetBool(LastSuccessKey, success);
        SaveState();

        if (EditorApplication.isPlaying)
        {
            EditorApplication.ExitPlaymode();
        }
        else
        {
            DetachCallbacks();
            EditorApplication.Exit(success ? 0 : 1);
        }
    }

    private static void ExitAfterFinishIfNeeded()
    {
        if (!SessionState.GetBool(FinishPendingKey, false))
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                DetachCallbacks();
            }

            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        bool success = SessionState.GetBool(LastSuccessKey, relevantErrorCount == 0);
        SessionState.SetBool(FinishPendingKey, false);
        DetachCallbacks();
        EditorApplication.Exit(success ? 0 : 1);
    }

    private static void SaveState()
    {
        SessionState.SetFloat(StartTimeKey, (float)startTime);
        SessionState.SetBool(EnteredPlayModeKey, enteredPlayMode);
        SessionState.SetBool(RoleSwitchedKey, roleSwitched);
        SessionState.SetInt(RelevantErrorCountKey, relevantErrorCount);
        SessionState.SetBool(RequireRemoteKey, requireRemote);
        SessionState.SetBool(GrabBottleKey, grabBottle);
        SessionState.SetBool(GrabStartedKey, grabStarted);
        SessionState.SetBool(GrabStatusLoggedKey, grabStatusLogged);
        SessionState.SetFloat(GrabStartTimeKey, (float)grabStartTime);
        SessionState.SetFloat(ProbeTimeoutKey, probeTimeoutSeconds);
        SessionState.SetFloat(StatusLogTimeKey, (float)lastStatusLogTime);
        SessionState.SetBool(SessionStartRequestedKey, sessionStartRequested);
        SessionState.SetBool(MetadataLoggedKey, metadataLogged);
        SessionState.SetBool(CursorFilterTestedKey, cursorFilterTested);
        SessionState.SetBool(CursorFilterOffWorkedKey, cursorFilterOffWorked);
        SessionState.SetBool(CursorFilterOnRestoredKey, cursorFilterOnRestored);
        SessionState.SetBool(CursorVerifiedKey, cursorVerified);
        SessionState.SetBool(SpawnRequestedKey, spawnRequested);
    }

    private static void LoadState()
    {
        startTime = SessionState.GetFloat(StartTimeKey, (float)startTime);
        enteredPlayMode = SessionState.GetBool(EnteredPlayModeKey, enteredPlayMode);
        roleSwitched = SessionState.GetBool(RoleSwitchedKey, roleSwitched);
        relevantErrorCount = SessionState.GetInt(RelevantErrorCountKey, relevantErrorCount);
        requireRemote = SessionState.GetBool(RequireRemoteKey, requireRemote);
        grabBottle = SessionState.GetBool(GrabBottleKey, grabBottle);
        grabStarted = SessionState.GetBool(GrabStartedKey, grabStarted);
        grabStatusLogged = SessionState.GetBool(GrabStatusLoggedKey, grabStatusLogged);
        grabStartTime = SessionState.GetFloat(GrabStartTimeKey, (float)grabStartTime);
        probeTimeoutSeconds = SessionState.GetFloat(ProbeTimeoutKey, probeTimeoutSeconds);
        lastStatusLogTime = SessionState.GetFloat(StatusLogTimeKey, (float)lastStatusLogTime);
        sessionStartRequested = SessionState.GetBool(SessionStartRequestedKey, sessionStartRequested);
        metadataLogged = SessionState.GetBool(MetadataLoggedKey, metadataLogged);
        cursorFilterTested = SessionState.GetBool(CursorFilterTestedKey, cursorFilterTested);
        cursorFilterOffWorked = SessionState.GetBool(CursorFilterOffWorkedKey, cursorFilterOffWorked);
        cursorFilterOnRestored = SessionState.GetBool(CursorFilterOnRestoredKey, cursorFilterOnRestored);
        cursorVerified = SessionState.GetBool(CursorVerifiedKey, cursorVerified);
        spawnRequested = SessionState.GetBool(SpawnRequestedKey, spawnRequested);
    }

    private static PhotonSharedMRSessionSettings CreateEditorProbeSettings()
    {
        return PhotonSharedMRSessionSettings.CreatePcObserverDefaults(ShareDeviceType.PCEditor);
    }

    private static void LogAvatarMetadata(NetworkUserAvatar[] avatars)
    {
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar == null)
            {
                continue;
            }

            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] AVATAR_METADATA"
                + " index=" + i
                + " local=" + avatar.IsLocalUser
                + " userName=" + avatar.CurrentUserName
                + " role=" + avatar.CurrentRole
                + " deviceType=" + avatar.DeviceType
                + " robotTarget=" + avatar.RobotTarget
                + " displayPlayerNumber=" + avatar.DisplayPlayerNumber
                + " observerDisplayNumber=" + avatar.ObserverDisplayNumber
                + " hostLike=" + avatar.IsHostLikeUser);
        }
    }

    private static void SetHmdCursorVisibilityForRole(RoleBasedInfoFilter filter, SharedUserRole role, bool visible)
    {
        switch (role)
        {
            case SharedUserRole.ManipulatorOperator:
                filter.manipulatorShowsHmdOverheadCursors = visible;
                break;
            case SharedUserRole.Scout:
                filter.scoutShowsHmdOverheadCursors = visible;
                break;
            case SharedUserRole.Supervisor:
                filter.supervisorShowsHmdOverheadCursors = visible;
                break;
        }
    }

    private static HmdCursorStatus CollectHmdCursorStatus()
    {
        HmdCursorStatus status = new HmdCursorStatus();
        HmdOverheadCursor[] cursors = Object.FindObjectsOfType<HmdOverheadCursor>(true);
        status.CursorCount = cursors.Length;

        for (int i = 0; i < cursors.Length; i++)
        {
            HmdOverheadCursor cursor = cursors[i];
            if (cursor == null || cursor.avatar == null)
            {
                continue;
            }

            bool isLocal = cursor.avatar.IsLocalUser;
            if (isLocal)
            {
                if (cursor.IsCurrentlyVisible)
                {
                    status.LocalVisibleCount++;
                }

                continue;
            }

            status.RemoteCursorCount++;
            if (!cursor.IsCurrentlyVisible)
            {
                continue;
            }

            status.RemoteVisibleCount++;
            status.RemoteLabel = cursor.CurrentLabel;
            status.FacingDot = Mathf.Max(status.FacingDot, cursor.LastFacingDot);
            bool labelHasUserName = status.RemoteLabel.Contains(cursor.avatar.CurrentUserName);
            bool labelHasRole = status.RemoteLabel.Contains(cursor.avatar.CurrentRole.ToString());
            bool labelHasTarget = status.RemoteLabel.Contains(cursor.avatar.RobotTarget.ToString());
            status.RemoteLabelHasMetadata |= labelHasUserName && labelHasRole && labelHasTarget;
        }

        ObserverPositionCursor[] observerCursors = Object.FindObjectsOfType<ObserverPositionCursor>(true);
        status.CursorCount += observerCursors.Length;
        for (int i = 0; i < observerCursors.Length; i++)
        {
            ObserverPositionCursor cursor = observerCursors[i];
            if (cursor == null || cursor.avatar == null)
            {
                continue;
            }

            bool isLocal = cursor.avatar.IsLocalUser;
            if (isLocal)
            {
                if (cursor.IsCurrentlyVisible)
                {
                    status.LocalVisibleCount++;
                }

                continue;
            }

            status.RemoteCursorCount++;
            if (!cursor.IsCurrentlyVisible)
            {
                continue;
            }

            status.RemoteVisibleCount++;
            status.RemoteLabel = cursor.CurrentLabel;
            status.FacingDot = Mathf.Max(status.FacingDot, cursor.LastFacingDot);
            bool labelHasUserName = status.RemoteLabel.Contains(cursor.avatar.DisplayPlayerLabel);
            bool labelHasRole = status.RemoteLabel.Contains(cursor.avatar.CurrentRole.ToString());
            status.RemoteLabelHasMetadata |= labelHasUserName && labelHasRole;
        }

        return status;
    }

    private struct HmdCursorStatus
    {
        public int CursorCount;
        public int RemoteCursorCount;
        public int RemoteVisibleCount;
        public int LocalVisibleCount;
        public string RemoteLabel;
        public bool RemoteLabelHasMetadata;
        public float FacingDot;
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

    private static void LogGrabStatus(NetworkedSharedSceneObject bottle)
    {
        if (bottle == null)
        {
            Debug.Log("[SharePhotonSharedMRPlaySmokeTest] GRAB_STATUS bottlePresent=False");
            return;
        }

        Debug.Log("[SharePhotonSharedMRPlaySmokeTest] GRAB_STATUS bottlePresent=True"
            + " hasStateAuthority=" + bottle.HasStateAuthority
            + " isGrabbed=" + bottle.IsGrabbed
            + " isLockedByOther=" + bottle.IsLockedByOther
            + " lockOwner=" + bottle.LockOwner
            + " localPlayer=" + (bottle.Runner != null ? bottle.Runner.LocalPlayer.ToString() : "none"));
    }
#endif
}
