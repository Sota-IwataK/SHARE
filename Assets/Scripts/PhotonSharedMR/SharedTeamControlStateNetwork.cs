using UnityEngine;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

/// <summary>Room-global state only. This component belongs on the dedicated scene NetworkObject.</summary>
[DisallowMultipleComponent]
public sealed class SharedTeamControlStateNetwork :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    public static SharedTeamControlStateNetwork Instance { get; private set; }
    [SerializeField] private bool enableEventLogs = true;
    private int lastObservedSequence = -1;
    private long lastObservedTimestamp = -1;

#if FUSION_WEAVER && FUSION2
    [Networked] public int TargetIdValue { get; private set; }
    [Networked] public int OwnerTypeValue { get; private set; }
    [Networked] public int OwnerIdValue { get; private set; }
    [Networked] public int TaskPhaseValue { get; private set; }
    [Networked] public int SequenceValue { get; private set; }
    [Networked] public long SharedTimestampValue { get; private set; }

    public override void Spawned()
    {
        Instance = this;
        if (HasStateAuthority && SequenceValue == 0)
        {
            TargetIdValue = -1;
            OwnerTypeValue = (int)SharedControlOwnerType.None;
            OwnerIdValue = -1;
            TaskPhaseValue = (int)TaskPhase.Independent;
            SharedTimestampValue = CurrentSharedTimestamp();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (SequenceValue == lastObservedSequence) return;
        if (SequenceValue < lastObservedSequence || SharedTimestampValue < lastObservedTimestamp)
            Debug.LogError("[SharedTeamState] non-monotonic control state sequence=" + SequenceValue
                + " previousSequence=" + lastObservedSequence + " timestamp=" + SharedTimestampValue
                + " previousTimestamp=" + lastObservedTimestamp);
        lastObservedSequence = SequenceValue;
        lastObservedTimestamp = SharedTimestampValue;
    }

    public bool TrySetTarget(int targetId) => TrySetControl(targetId, null, null);
    public bool TrySetOwnership(SharedControlOwnerType ownerType, int ownerId)
        => TrySetControl(null, ownerType, ownerId);

    public bool TryRequestTaskPhaseTransition(
        TaskPhase requested,
        CoordinationInputSnapshot inputs,
        out AuthoritativeTransitionRejection rejection)
    {
        bool runnerAvailable = Runner != null;
        bool hasStateAuthority = runnerAvailable && HasStateAuthority;
        if (!AuthoritativeTaskPhaseTransition.TryEvaluate(
                runnerAvailable,
                hasStateAuthority,
                TaskPhaseValue,
                requested,
                inputs,
                out int nextNetworkValue,
                out rejection))
            return false;

        TaskPhaseValue = nextNetworkValue;
        AdvanceVersion();
        LogControlUpdate();
        return true;
    }

    private bool TrySetControl(int? targetId, SharedControlOwnerType? ownerType, int? ownerId)
    {
        if (!HasStateAuthority || Runner == null) return false;
        if (targetId.HasValue) TargetIdValue = targetId.Value;
        if (ownerType.HasValue) OwnerTypeValue = (int)ownerType.Value;
        if (ownerId.HasValue) OwnerIdValue = ownerId.Value;
        AdvanceVersion();
        LogControlUpdate();
        return true;
    }

    private void AdvanceVersion()
    {
        SequenceValue = SequenceValue == int.MaxValue ? 1 : SequenceValue + 1;
        long timestamp = CurrentSharedTimestamp();
        SharedTimestampValue = timestamp > SharedTimestampValue ? timestamp : SharedTimestampValue + 1;
    }

    private void LogControlUpdate()
    {
        if (enableEventLogs)
            Debug.Log("[SharedTeamState] CONTROL sequence=" + SequenceValue
                + " timestamp=" + SharedTimestampValue + " phase=" + TaskPhaseValue);
    }

    private long CurrentSharedTimestamp() => Runner == null ? 0L : (long)(Runner.SimulationTime * 1000.0);
#else
    private void Awake() { Instance = this; }
#endif

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public static bool TryRead(out SharedControlState state)
    {
#if FUSION_WEAVER && FUSION2
        SharedTeamControlStateNetwork item = Instance;
        if (item != null && item.Object != null)
        {
            if (!CoordinationStateMachine.TryParsePhase(item.TaskPhaseValue, out TaskPhase phase))
            {
                state = default;
                return false;
            }
            state = new SharedControlState
            {
                target_id = item.TargetIdValue,
                owner_type = (SharedControlOwnerType)item.OwnerTypeValue,
                owner_id = item.OwnerIdValue,
                task_phase = phase,
                sequence = item.SequenceValue,
                shared_timestamp = item.SharedTimestampValue
            };
            return true;
        }
#endif
        state = default;
        return false;
    }
}
