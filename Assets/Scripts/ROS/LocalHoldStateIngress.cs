using System.Collections;
using RosMessageTypes.ShareSemanticInterfaces;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

/// <summary>Typed ROS subscriber feeding the existing transport-neutral semantic reducer.</summary>
[DisallowMultipleComponent]
public sealed class LocalHoldStateIngress : MonoBehaviour
{
    public const string RelativeTopic = "local_hold_state";
    private ROSConnection ros;
    private string subscribedTopic;
    private PairSemanticStateReducer reducer;

    private void OnEnable() { StartCoroutine(SubscribeWhenReady()); }
    private IEnumerator SubscribeWhenReady()
    {
        while (enabled && string.IsNullOrEmpty(subscribedTopic) && !TrySubscribe()) yield return null;
    }
    private bool TrySubscribe()
    {
        NetworkUserAvatar avatar = NetworkUserAvatar.Local;
        if (avatar == null || !TryResolveTopic(avatar.ParticipantId, out string topic)) return false;
        ros = ROSConnection.GetOrCreateInstance();
        reducer = new PairSemanticStateReducer(avatar.ParticipantId);
        ros.Subscribe<LocalHoldStateMsg>(topic, ReceiveMessage);
        subscribedTopic = topic;
        Debug.Log("[LocalHoldState] subscribed topic=" + topic + " participant=" + avatar.ParticipantId, this);
        return true;
    }
    private void OnDisable()
    {
        if (ros != null && !string.IsNullOrEmpty(subscribedTopic)) ros.Unsubscribe(subscribedTopic);
        subscribedTopic = null;
        reducer = null;
    }
    private void ReceiveMessage(LocalHoldStateMsg message)
    {
        if (LocalHoldRosMapper.TryMap(message, out LocalHoldRosSnapshot snapshot)) Submit(snapshot.semantic);
    }
    public static bool TryResolveTopic(SharedMRParticipantId participant, out string topic)
    {
        if (!PhotonSharedMRSessionSettings.TryGetRosUserId(participant, out string rosUserId))
        { topic = string.Empty; return false; }
        topic = "/share/users/" + rosUserId + "/" + RelativeTopic;
        return true;
    }
    public bool Submit(LocalHoldState state)
    {
        if (reducer == null || !reducer.TryApply(state, MonotonicMilliseconds(), FusionTick(), out PairSemanticState reduced)) return false;
        SharedPairSemanticStateNetwork target = SharedPairSemanticStateNetwork.Instance;
        if (target == null) { Debug.LogWarning("[LocalHoldState] SharedTeamStateSceneObject is unavailable.", this); return false; }
        return target.SubmitLocal(reduced);
    }
    private static long MonotonicMilliseconds() => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
    private static int FusionTick()
    {
#if FUSION_WEAVER && FUSION2
        return NetworkUserAvatar.Local != null && NetworkUserAvatar.Local.Runner != null ? NetworkUserAvatar.Local.Runner.Tick.Raw : 0;
#else
        return 0;
#endif
    }
}
