using System;
using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

public enum RosInputTopicKey
{
    PalmPose,
    PalmPoseWorld,
    PalmPoseControlCenterWorld,
    PalmPoseHmdRelative,
    RightHandMecanumInput,
    GripperCommand,
    RobotTarget
}

public static class RosTopicProvider
{
    public const string PalmPose = "/palm_pose";
    public const string PalmPoseWorld = "/palm_pose_world";
    public const string PalmPoseControlCenterWorld = "/palm_pose_control_center_world";
    public const string PalmPoseHmdRelative = "/palm_pose_hmd_relative";
    public const string RightHandMecanumInput = "/amir/right_hand_mecanum_input";
    public const string GripperCommand = "/amir/gripper_cmd";
    public const string LegacyGraspCommand = "/grasp_command";
    public const string RobotTarget = "/robot_target";

    private const string SharedUserRoot = "/share/users/";
    private const float DuplicateScanIntervalSec = 0.25f;

    private static float nextDuplicateScanTime;
    private static string lastDuplicateKey;
    private static bool duplicateInUse;
    private static string duplicateReason;
    private static string lastLoggedStateKey;
    private static string lastRejectedReason;
    private static PhotonSharedMRLoginPanel cachedLoginPanel;

    public static bool TryResolveTopic(RosInputTopicKey key, string legacyTopic, out string topic, out string reason)
    {
        topic = NormalizeTopic(legacyTopic, GetDefaultLegacyTopic(key));
        reason = string.Empty;

        if (!IsSharedModeExpected())
        {
            return true;
        }

        if (!TryGetLocalRosUserId(out string rosUserId, out reason))
        {
            LogRejectedOnce(key, reason);
            return false;
        }

        if (!IsSharedRosterReady(out reason))
        {
            LogRejectedOnce(key, reason);
            return false;
        }

        if (IsDuplicateLocalParticipant(out reason))
        {
            LogRejectedOnce(key, reason);
            ReportUserSessionIssue(reason);
            return false;
        }

        if (IsControlInputTopic(key) && !LocalAvatarCanControl(out reason))
        {
            LogRejectedOnce(key, reason);
            return false;
        }

        topic = BuildSharedTopic(rosUserId, key);
        LogStateOnce(rosUserId, key, topic);
        return true;
    }

    public static bool CanPublish(RosInputTopicKey key, out string reason)
    {
        reason = string.Empty;
        if (!IsSharedModeExpected())
        {
            return true;
        }

        if (!TryGetLocalRosUserId(out _, out reason))
        {
            LogRejectedOnce(key, reason);
            return false;
        }

        if (!IsSharedRosterReady(out reason))
        {
            LogRejectedOnce(key, reason);
            return false;
        }

        if (IsDuplicateLocalParticipant(out reason))
        {
            LogRejectedOnce(key, reason);
            ReportUserSessionIssue(reason);
            return false;
        }

        if (IsControlInputTopic(key) && !LocalAvatarCanControl(out reason))
        {
            LogRejectedOnce(key, reason);
            return false;
        }

        return true;
    }

    public static string GetDefaultLegacyTopic(RosInputTopicKey key)
    {
        switch (key)
        {
            case RosInputTopicKey.PalmPose:
                return PalmPose;
            case RosInputTopicKey.PalmPoseWorld:
                return PalmPoseWorld;
            case RosInputTopicKey.PalmPoseControlCenterWorld:
                return PalmPoseControlCenterWorld;
            case RosInputTopicKey.PalmPoseHmdRelative:
                return PalmPoseHmdRelative;
            case RosInputTopicKey.RightHandMecanumInput:
                return RightHandMecanumInput;
            case RosInputTopicKey.GripperCommand:
                return GripperCommand;
            case RosInputTopicKey.RobotTarget:
                return RobotTarget;
            default:
                return PalmPose;
        }
    }

    public static string BuildSharedTopic(string rosUserId, RosInputTopicKey key)
    {
        return SharedUserRoot + rosUserId + "/" + GetSharedLeafName(key);
    }

    public static bool TryGetLocalRosUserId(out string rosUserId, out string reason)
    {
        rosUserId = string.Empty;
        reason = string.Empty;

        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        if (localAvatar == null)
        {
            reason = "Shared ROS input is waiting for the local Photon user.";
            return false;
        }

        if (!localAvatar.IsNetworkStateReady)
        {
            reason = "Shared ROS input is waiting for Photon user metadata.";
            return false;
        }

        if (!PhotonSharedMRSessionSettings.TryGetRosUserId(localAvatar.ParticipantId, out rosUserId))
        {
            reason = "Shared ROS input rejected because ParticipantId is unassigned.";
            return false;
        }

        return true;
    }

    public static bool TryGetLocalRobotTargetValue(out string value, out string reason)
    {
        value = string.Empty;
        reason = string.Empty;

        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        if (localAvatar == null)
        {
            reason = "Shared ROS robot_target is waiting for the local Photon user.";
            return false;
        }

        if (!localAvatar.IsNetworkStateReady)
        {
            reason = "Shared ROS robot_target is waiting for Photon user metadata.";
            return false;
        }

        value = ConvertRobotTargetToRosValue(localAvatar.RobotTarget);
        return true;
    }

    public static void ReleaseLocalState(string source)
    {
        duplicateInUse = false;
        duplicateReason = string.Empty;
        lastDuplicateKey = string.Empty;
        lastLoggedStateKey = string.Empty;
        lastRejectedReason = string.Empty;
        Debug.Log("[RosTopicProvider] Released shared ROS input state. source=" + source);
    }

    private static bool IsSharedModeExpected()
    {
        PhotonFusionSharedRoomBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        if (bootstrap != null)
        {
            return true;
        }

        return UnityEngine.Object.FindObjectOfType<PhotonSharedMRLoginPanel>(true) != null;
    }

    private static bool IsSharedRosterReady(out string reason)
    {
        reason = string.Empty;
#if FUSION_WEAVER && FUSION2
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        if (localAvatar == null || localAvatar.Object == null || localAvatar.Runner == null || !localAvatar.Runner.IsRunning)
        {
            reason = "Shared ROS input is waiting for the Photon runner and local avatar.";
            return false;
        }

        NetworkUserAvatar[] avatars = UnityEngine.Object.FindObjectsOfType<NetworkUserAvatar>(true);
        foreach (PlayerRef player in localAvatar.Runner.ActivePlayers)
        {
            NetworkUserAvatar avatar = FindAvatarForPlayer(avatars, player);
            if (avatar == null)
            {
                reason = "Shared ROS input is waiting for every active Photon player avatar.";
                return false;
            }

            if (!avatar.IsNetworkStateReady)
            {
                reason = "Shared ROS input is waiting for every Photon avatar metadata record.";
                return false;
            }

            if (avatar.ParticipantId == SharedMRParticipantId.Unassigned)
            {
                reason = "Shared ROS input rejected because an active Photon participant is unassigned.";
                return false;
            }
        }
#endif
        return true;
    }

    private static bool IsDuplicateLocalParticipant(out string reason)
    {
        reason = string.Empty;
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        if (localAvatar == null)
        {
            return false;
        }

        SharedMRParticipantId localParticipantId = localAvatar.ParticipantId;
        string localKey = GetLocalDuplicateKey(localParticipantId);
        if (Time.unscaledTime < nextDuplicateScanTime && string.Equals(localKey, lastDuplicateKey, StringComparison.Ordinal))
        {
            reason = duplicateReason;
            return duplicateInUse;
        }

        nextDuplicateScanTime = Time.unscaledTime + DuplicateScanIntervalSec;
        lastDuplicateKey = localKey;
        duplicateInUse = false;
        duplicateReason = string.Empty;

        if (localParticipantId == SharedMRParticipantId.Unassigned)
        {
            return false;
        }

        int localOwnerOrder = GetPlayerOrder(localAvatar);
        NetworkUserAvatar[] avatars = UnityEngine.Object.FindObjectsOfType<NetworkUserAvatar>(true);
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar == null || avatar == localAvatar || avatar.IsLocalUser || !avatar.IsNetworkStateReady)
            {
                continue;
            }

            if (avatar.ParticipantId != localParticipantId)
            {
                continue;
            }

            int otherOwnerOrder = GetPlayerOrder(avatar);
            if (localOwnerOrder >= 0 && otherOwnerOrder >= 0 && localOwnerOrder < otherOwnerOrder)
            {
                continue;
            }

            duplicateInUse = true;
            duplicateReason = "Shared ROS input rejected because "
                + PhotonSharedMRSessionSettings.BuildParticipantDisplayName(localParticipantId)
                + " is owned by another Photon player.";
            Debug.LogWarning("[RosTopicProvider] Duplicate ParticipantId detected."
                + " participantId=" + localParticipantId
                + " localUser=" + localAvatar.DisplayPlayerLabel
                + " localPlayerOrder=" + localOwnerOrder
                + " remoteUser=" + avatar.DisplayPlayerLabel);
            return true;
        }

        return false;
    }

    private static string GetLocalDuplicateKey(SharedMRParticipantId participantId)
    {
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        return participantId + "|" + GetPlayerOrder(localAvatar);
    }

    private static string GetSharedLeafName(RosInputTopicKey key)
    {
        switch (key)
        {
            case RosInputTopicKey.PalmPose:
                return "palm_pose";
            case RosInputTopicKey.PalmPoseWorld:
                return "palm_pose_world";
            case RosInputTopicKey.PalmPoseControlCenterWorld:
                return "palm_pose_control_center_world";
            case RosInputTopicKey.PalmPoseHmdRelative:
                return "palm_pose_hmd_relative";
            case RosInputTopicKey.RightHandMecanumInput:
                return "right_hand_mecanum_input";
            case RosInputTopicKey.GripperCommand:
                return "gripper_cmd";
            case RosInputTopicKey.RobotTarget:
                return "robot_target";
            default:
                return key.ToString();
        }
    }

    private static bool IsControlInputTopic(RosInputTopicKey key)
    {
        return key != RosInputTopicKey.RobotTarget;
    }

    private static bool LocalAvatarCanControl(out string reason)
    {
        reason = string.Empty;
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        if (localAvatar == null || !localAvatar.IsNetworkStateReady)
        {
            reason = "Shared ROS control input is waiting for local Photon metadata.";
            return false;
        }

        if (localAvatar.RobotTarget == SharedMRRobotTarget.Observer || localAvatar.CurrentRole == SharedUserRole.Supervisor)
        {
            reason = "Shared ROS control input rejected because the local role or RobotTarget has no control authority.";
            return false;
        }

        return true;
    }

    private static string ConvertRobotTargetToRosValue(SharedMRRobotTarget robotTarget)
    {
        switch (robotTarget)
        {
            case SharedMRRobotTarget.Amir:
                return "amir";
            case SharedMRRobotTarget.Rover:
                return "rover";
            case SharedMRRobotTarget.Drone:
                return "drone";
            case SharedMRRobotTarget.Observer:
                return "observer";
            default:
                return "observer";
        }
    }

#if FUSION_WEAVER && FUSION2
    private static NetworkUserAvatar FindAvatarForPlayer(NetworkUserAvatar[] avatars, PlayerRef player)
    {
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar == null || avatar.Object == null)
            {
                continue;
            }

            if (avatar.Object.InputAuthority == player)
            {
                return avatar;
            }
        }

        return null;
    }
#endif

    private static int GetPlayerOrder(NetworkUserAvatar avatar)
    {
#if FUSION_WEAVER && FUSION2
        if (avatar != null && avatar.Object != null)
        {
            return avatar.Object.InputAuthority.PlayerId;
        }
#endif
        return -1;
    }

    private static string NormalizeTopic(string topic, string fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(topic) ? fallback : topic.Trim();
        return normalized.StartsWith("/") ? normalized : "/" + normalized;
    }

    private static void LogStateOnce(string rosUserId, RosInputTopicKey key, string topic)
    {
        string stateKey = rosUserId + "|" + key + "|" + topic;
        if (stateKey == lastLoggedStateKey)
        {
            return;
        }

        lastLoggedStateKey = stateKey;
        NetworkUserAvatar localAvatar = NetworkUserAvatar.Local;
        Debug.Log("[RosTopicProvider] Shared ROS input resolved."
            + " selectedUser=" + (localAvatar != null ? localAvatar.DisplayPlayerLabel : "None")
            + " rosUserId=" + rosUserId
            + " mode=Shared"
            + " key=" + key
            + " topic=" + topic);
    }

    private static void LogRejectedOnce(RosInputTopicKey key, string reason)
    {
        string rejectKey = key + "|" + reason;
        if (rejectKey == lastRejectedReason)
        {
            return;
        }

        lastRejectedReason = rejectKey;
        Debug.LogWarning("[RosTopicProvider] Publish rejected."
            + " mode=Shared"
            + " key=" + key
            + " reason=" + reason);
    }

    private static void ReportUserSessionIssue(string reason)
    {
        if (cachedLoginPanel == null)
        {
            cachedLoginPanel = UnityEngine.Object.FindObjectOfType<PhotonSharedMRLoginPanel>(true);
        }

        if (cachedLoginPanel != null)
        {
            cachedLoginPanel.ShowExternalSessionError(reason);
        }
    }
}
