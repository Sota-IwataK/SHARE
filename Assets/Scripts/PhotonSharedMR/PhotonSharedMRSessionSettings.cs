using System;
using UnityEngine;

public enum ShareDeviceType
{
    PCEditor = 0,
    QuestStandalone = 1,
    QuestLink = 2,
    Unknown = 3,
    PC = 4
}

public enum SharedMRRobotTarget
{
    Amir = 0,
    Drone = 1,
    Observer = 2,
    Rover = 3
}

public enum SharedMRParticipantId
{
    Unassigned = 0,
    User1 = 1,
    User2 = 2,
    User3 = 3
}

[Serializable]
public class PhotonSharedMRSessionSettings
{
    public const string DefaultRoomName = "SHARE-MR-Room";
    public const string DefaultUserName = "SHARE User";
    public const string ObserverDisplayNamePrefix = "Observer ";

    public string userName = DefaultUserName;
    public bool isHostLikeUser = true;
    public SharedMRParticipantId participantId = SharedMRParticipantId.Unassigned;
    public ShareDeviceType deviceType = ShareDeviceType.PCEditor;
    public SharedUserRole role = SharedUserRole.ManipulatorOperator;
    public SharedMRRobotTarget robotTarget = SharedMRRobotTarget.Amir;
    public string roomName = DefaultRoomName;

    public static PhotonSharedMRSessionSettings CreateDefault()
    {
        PhotonSharedMRSessionSettings settings = new PhotonSharedMRSessionSettings();
        settings.Sanitize();
        return settings;
    }

    public static PhotonSharedMRSessionSettings CreatePcObserverDefaults(ShareDeviceType deviceType)
    {
        PhotonSharedMRSessionSettings settings = new PhotonSharedMRSessionSettings
        {
            userName = "Observer",
            isHostLikeUser = false,
            deviceType = IsPcObserverDevice(deviceType) ? deviceType : ShareDeviceType.PC,
            role = SharedUserRole.Supervisor,
            robotTarget = SharedMRRobotTarget.Observer,
            roomName = DefaultRoomName
        };
        settings.Sanitize();
        return settings;
    }

    public static bool IsPcObserverDevice(ShareDeviceType deviceType)
    {
        return deviceType == ShareDeviceType.PCEditor || deviceType == ShareDeviceType.PC;
    }

    public static string BuildObserverDisplayName(int observerDisplayNumber)
    {
        return ObserverDisplayNamePrefix + Mathf.Max(1, observerDisplayNumber);
    }

    public static string BuildRobotDisplayName(SharedMRRobotTarget robotTarget, SharedUserRole role)
    {
        switch (robotTarget)
        {
            case SharedMRRobotTarget.Amir:
                return "AMIR Operator";
            case SharedMRRobotTarget.Rover:
                return "Rover Operator";
            case SharedMRRobotTarget.Drone:
                return "Drone Scout";
            case SharedMRRobotTarget.Observer:
                return role == SharedUserRole.Supervisor ? "Observer" : DefaultUserName;
            default:
                return DefaultUserName;
        }
    }

    public static string BuildParticipantDisplayName(SharedMRParticipantId participantId)
    {
        switch (participantId)
        {
            case SharedMRParticipantId.User1:
                return "User 1";
            case SharedMRParticipantId.User2:
                return "User 2";
            case SharedMRParticipantId.User3:
                return "User 3";
            default:
                return "Unassigned";
        }
    }

    public static bool TryGetRosUserId(SharedMRParticipantId participantId, out string rosUserId)
    {
        switch (participantId)
        {
            case SharedMRParticipantId.User1:
                rosUserId = "user_1";
                return true;
            case SharedMRParticipantId.User2:
                rosUserId = "user_2";
                return true;
            case SharedMRParticipantId.User3:
                rosUserId = "user_3";
                return true;
            default:
                rosUserId = string.Empty;
                return false;
        }
    }

    public bool TryGetRosUserId(out string rosUserId)
    {
        return TryGetRosUserId(participantId, out rosUserId);
    }

    public PhotonSharedMRSessionSettings Clone()
    {
        return new PhotonSharedMRSessionSettings
        {
            userName = userName,
            isHostLikeUser = isHostLikeUser,
            participantId = participantId,
            deviceType = deviceType,
            role = role,
            robotTarget = robotTarget,
            roomName = roomName
        };
    }

    public void Sanitize()
    {
        userName = SanitizeText(userName, DefaultUserName, 48);
        roomName = SanitizeText(roomName, DefaultRoomName, 64);

        if (!Enum.IsDefined(typeof(ShareDeviceType), deviceType))
        {
            deviceType = ShareDeviceType.Unknown;
        }

        if (!Enum.IsDefined(typeof(SharedMRParticipantId), participantId))
        {
            participantId = SharedMRParticipantId.Unassigned;
        }

        if (!Enum.IsDefined(typeof(SharedUserRole), role))
        {
            role = SharedUserRole.ManipulatorOperator;
        }

        if (!Enum.IsDefined(typeof(SharedMRRobotTarget), robotTarget))
        {
            robotTarget = SharedMRRobotTarget.Observer;
        }
    }

    private static string SanitizeText(string value, string fallback, int maxLength)
    {
        string sanitized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength);
        }

        return sanitized;
    }
}
