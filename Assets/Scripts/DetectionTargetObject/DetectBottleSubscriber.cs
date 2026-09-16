using RosMessageTypes.Std;
using UnityEngine;

public class DetectBottleSubscriber : RosTcpSubscriber<Int32Msg>
{
    private const int NoTargetId = -1;

    public int bottle_id = -1;
    [SerializeField, Tooltip("ROS ID used to explicitly clear the current target.")]
    private int noTargetId = NoTargetId;

    private int currentTargetId = NoTargetId;
    private NetworkedSharedSceneObject currentTarget;

    public int CurrentTargetId => currentTargetId;
    public NetworkedSharedSceneObject CurrentTarget => currentTarget;

    protected override void ReceiveMessage(Int32Msg message)
    {
        bottle_id = message.data;
        ApplyRosTarget(message.data);
    }

    private void ApplyRosTarget(int incomingId)
    {
        Debug.Log("[RosTarget] received bottle_id=" + incomingId, this);

        if (incomingId == currentTargetId
            && (incomingId == noTargetId || currentTarget != null))
        {
            return;
        }

        if (incomingId == noTargetId)
        {
            ChangeTarget(noTargetId, null);
            return;
        }

        NetworkedSharedSceneObject resolved = FindBottleByRosId(incomingId);
        if (resolved == null)
        {
            Debug.LogWarning("[RosTarget][WARN] unknown bottle_id=" + incomingId, this);
            return;
        }

        Debug.Log("[RosTarget] bottle_id=" + incomingId + " object=" + resolved.name, resolved);
        ChangeTarget(incomingId, resolved);
    }

    private void ChangeTarget(int newId, NetworkedSharedSceneObject newTarget)
    {
        int oldId = currentTargetId;
        NetworkedSharedSceneObject oldTarget = currentTarget;

        if (oldTarget != null && oldTarget != newTarget)
        {
            SetTargetEffect(oldTarget, LocalTargetEffectState.Normal);
        }

        currentTargetId = newId;
        currentTarget = newTarget;

        if (newTarget != null)
        {
            SetTargetEffect(newTarget, LocalTargetEffectState.Selected);
        }

        Debug.Log("[RosTarget] target changed old=" + oldId + " new=" + newId, this);
    }

    private static NetworkedSharedSceneObject FindBottleByRosId(int rosId)
    {
        NetworkedSharedSceneObject[] bottles = FindObjectsByType<NetworkedSharedSceneObject>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (NetworkedSharedSceneObject bottle in bottles)
        {
            if (bottle != null
                && bottle.IsPhotonSharedNetworkBottle
                && bottle.SharedOrigin == SharedBottleOrigin.RosDetected
                && bottle.SharedDetectedBottleTrackId == rosId)
            {
                return bottle;
            }
        }

        return null;
    }

    private static void SetTargetEffect(
        NetworkedSharedSceneObject bottle,
        LocalTargetEffectState state)
    {
        BottleTargetEffectController effect = bottle.GetComponent<BottleTargetEffectController>();
        if (effect == null)
        {
            Debug.LogWarning("[RosTarget][WARN] target effect missing object=" + bottle.name, bottle);
            return;
        }

        effect.SetLocalTargetState(state);
    }
}
