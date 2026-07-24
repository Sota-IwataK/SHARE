using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkedBottleTargetClaim : NetworkBehaviour
{
    [Networked] public NetworkBool HasTargetOwner { get; set; }
    [Networked] public PlayerRef TargetOwner { get; set; }

    [SerializeField, Min(0.25f)] private float disconnectedOwnerCheckIntervalSec = 1f;
    [SerializeField] private bool enableDebugLogs = true;

    private bool localRequestPending;
    private bool observedHasOwner;
    private PlayerRef observedOwner;
    private float nextOwnerCheck;

    public bool HasOwner => Object != null && HasTargetOwner;
    public PlayerRef Owner => HasOwner ? TargetOwner : PlayerRef.None;
    public bool IsOwnedByLocalPlayer
        => HasOwner && Runner != null && TargetOwner == Runner.LocalPlayer;
    public bool IsOwnedByOtherPlayer
        => HasOwner && Runner != null && TargetOwner != Runner.LocalPlayer;

    public event Action<PlayerRef, bool> TargetOwnerChanged;
    public event Action LocalClaimAccepted;
    public event Action<PlayerRef> LocalClaimRejected;

    public override void Spawned()
    {
        observedHasOwner = HasTargetOwner;
        observedOwner = TargetOwner;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !HasTargetOwner || Runner == null
            || Runner.SimulationTime < nextOwnerCheck)
        {
            return;
        }
        nextOwnerCheck = Runner.SimulationTime + disconnectedOwnerCheckIntervalSec;
        if (!IsPlayerActive(TargetOwner))
        {
            PlayerRef oldOwner = TargetOwner;
            HasTargetOwner = false;
            TargetOwner = PlayerRef.None;
            Log("Claim released networkId=" + NetworkIdText() + " owner=" + oldOwner
                + " reason=owner_disconnected");
        }
    }

    public override void Render()
    {
        ObserveOwnerChange();
    }

    public void RequestClaim()
    {
        if (localRequestPending || Object == null || Runner == null || !Runner.IsRunning)
        {
            return;
        }
        if (IsOwnedByLocalPlayer)
        {
            LocalClaimAccepted?.Invoke();
            return;
        }
        localRequestPending = true;
        PlayerRef requester = Runner.LocalPlayer;
        Log("Claim requested networkId=" + NetworkIdText() + " requester=" + requester);
        RPC_RequestTargetClaim(requester);
    }

    public void ReleaseClaim()
    {
        if (Object == null || Runner == null || !Runner.IsRunning)
        {
            localRequestPending = false;
            return;
        }
        RPC_ReleaseTargetClaim(Runner.LocalPlayer);
        localRequestPending = false;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_RequestTargetClaim(PlayerRef requester, RpcInfo info = default)
    {
        bool accepted = !HasTargetOwner || TargetOwner == requester;
        if (accepted)
        {
            HasTargetOwner = true;
            TargetOwner = requester;
        }
        PlayerRef owner = HasTargetOwner ? TargetOwner : PlayerRef.None;
        RPC_TargetClaimResult(requester, accepted, owner);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_TargetClaimResult(
        PlayerRef requester,
        NetworkBool accepted,
        PlayerRef currentOwner,
        RpcInfo info = default)
    {
        if (Runner == null || requester != Runner.LocalPlayer)
        {
            return;
        }
        localRequestPending = false;
        if (accepted)
        {
            CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.TargetClaim);
            Log("Claim accepted networkId=" + NetworkIdText() + " owner=" + currentOwner);
            LocalClaimAccepted?.Invoke();
        }
        else
        {
            // A rejection is a valid competitive claim response, not a transport failure.
            CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.TargetClaim);
            Log("Claim rejected networkId=" + NetworkIdText()
                + " requester=" + requester + " currentOwner=" + currentOwner);
            LocalClaimRejected?.Invoke(currentOwner);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    private void RPC_ReleaseTargetClaim(PlayerRef requester, RpcInfo info = default)
    {
        if (!HasTargetOwner || TargetOwner != requester)
        {
            return;
        }
        PlayerRef oldOwner = TargetOwner;
        HasTargetOwner = false;
        TargetOwner = PlayerRef.None;
        CommunicationHealthMonitor.ReportSuccess(CommunicationChannel.TargetClaim);
        Log("Claim released networkId=" + NetworkIdText() + " owner=" + oldOwner);
    }

    public string ResolveOwnerDisplayName(PlayerRef owner)
    {
        NetworkUserAvatar[] avatars = FindObjectsByType<NetworkUserAvatar>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar == null || avatar.Object == null)
            {
                continue;
            }
            if (avatar.Object.InputAuthority != owner && avatar.Object.StateAuthority != owner)
            {
                continue;
            }

            string userName = avatar.CurrentUserName;
            if (!string.IsNullOrWhiteSpace(userName)
                && !string.Equals(userName, PhotonSharedMRSessionSettings.DefaultUserName,
                    StringComparison.Ordinal))
            {
                return userName;
            }
            if (avatar.ParticipantId != SharedMRParticipantId.Unassigned)
            {
                return PhotonSharedMRSessionSettings.BuildParticipantDisplayName(avatar.ParticipantId);
            }
            return avatar.CurrentRole.ToString();
        }
        return "PLAYER " + owner.PlayerId;
    }

    private void ObserveOwnerChange()
    {
        bool hasOwner = HasTargetOwner;
        PlayerRef owner = hasOwner ? TargetOwner : PlayerRef.None;
        if (observedHasOwner == hasOwner && observedOwner == owner)
        {
            return;
        }
        observedHasOwner = hasOwner;
        observedOwner = owner;
        TargetOwnerChanged?.Invoke(owner, hasOwner);
    }

    private bool IsPlayerActive(PlayerRef player)
    {
        foreach (PlayerRef active in Runner.ActivePlayers)
        {
            if (active == player) return true;
        }
        return false;
    }

    private string NetworkIdText()
    {
        return Object != null && Object.Id.IsValid ? Object.Id.ToString() : "Invalid";
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            CommunicationHealthMonitor.Verbose(
                CommunicationChannel.TargetClaim,
                "[NetworkedBottleTargetClaim] " + message);
        }
    }
}
