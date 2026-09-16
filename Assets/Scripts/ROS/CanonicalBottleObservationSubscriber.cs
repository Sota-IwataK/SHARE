using System;
using RosMessageTypes.ShareSemanticInterfaces;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

public static class CanonicalBottleObservationRosMapper
{
    public static bool TryMap(
        CanonicalBottleObservationMsg message,
        out CanonicalBottleObservation observation,
        out string reason)
    {
        observation = default;
        if (message == null)
        {
            reason = "MessageNull";
            return false;
        }
        if (message.observation_stamp == null)
        {
            reason = "ObservationStampMissing";
            return false;
        }
        if (message.lifecycle > CanonicalBottleObservationMsg.REMOVED)
        {
            reason = "UnknownLifecycle";
            return false;
        }

        try
        {
            observation = new CanonicalBottleObservation(
                new CanonicalBottleIdentity(
                    message.source_id,
                    message.session_id,
                    message.object_id),
                (CanonicalBottleLifecycle)message.lifecycle,
                new CanonicalBottleObservationMetadata(
                    message.observation_sequence,
                    message.observation_stamp.sec,
                    message.observation_stamp.nanosec,
                    message.observation_clock_domain,
                    message.frame_id));
        }
        catch (ArgumentException exception)
        {
            reason = exception.ParamName + ":" + exception.Message;
            return false;
        }

        reason = "Mapped";
        return true;
    }
}

/// <summary>
/// Identity-only subscriber. It never consumes pose/TrackId or publishes features.
/// </summary>
[DisallowMultipleComponent]
public sealed class CanonicalBottleObservationSubscriber : MonoBehaviour
{
    public const string DefaultTopic = "/share/bottles/canonical_observation";

    [SerializeField] private string topic = DefaultTopic;
    [SerializeField] private PhotonSharedBottleSpawner bottleSpawner;
    [SerializeField] private bool logAcceptedObservations = true;

    private static CanonicalBottleObservationSubscriber activeInstance;
    private ROSConnection ros;
    private string subscribedTopic;
    private CanonicalBottleObservationRouter router;
    private BottleIdentityMode? activeMode;

    public string Topic => topic;
    public bool IsSubscribed => !string.IsNullOrEmpty(subscribedTopic);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeInstance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSingleRuntimeSubscriber()
    {
        CanonicalBottleObservationSubscriber[] existing =
            FindObjectsOfType<CanonicalBottleObservationSubscriber>(true);
        if (existing.Length > 0)
        {
            CanonicalBottleObservationSubscriber keeper = activeInstance;
            if (keeper == null)
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] != null && existing[i].enabled)
                    {
                        keeper = existing[i];
                        break;
                    }
                }
            }
            if (keeper == null)
            {
                keeper = existing[0];
            }
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i] != keeper)
                {
                    existing[i].enabled = false;
                    Debug.LogError(
                        "[CanonicalBottleObservationSubscriber] Duplicate subscriber disabled.",
                        existing[i]);
                }
            }
            activeInstance = keeper;
            return;
        }

        PhotonSharedBottleSpawner[] spawners =
            FindObjectsOfType<PhotonSharedBottleSpawner>(true);
        if (spawners.Length != 1)
        {
            Debug.LogWarning(
                "[CanonicalBottleObservationSubscriber] Runtime subscriber not created: "
                + "expected exactly one PhotonSharedBottleSpawner, found " + spawners.Length
                + ". Canonical mode remains fail-closed.");
            return;
        }

        CanonicalBottleObservationSubscriber subscriber =
            spawners[0].gameObject.AddComponent<CanonicalBottleObservationSubscriber>();
        subscriber.bottleSpawner = spawners[0];
        activeInstance = subscriber;
    }

    private void Awake()
    {
        if (activeInstance != null && activeInstance != this)
        {
            enabled = false;
            Debug.LogError(
                "[CanonicalBottleObservationSubscriber] Duplicate instance rejected.",
                this);
            return;
        }
        activeInstance = this;
        ResolveSpawner();
    }

    private void Start()
    {
        ReconcileModeAndSubscription();
    }

    private void Update()
    {
        ReconcileModeAndSubscription();
    }

    private void OnDisable()
    {
        Unsubscribe();
        router = null;
        activeMode = null;
        if (activeInstance == this)
        {
            activeInstance = null;
        }
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }
    }

    private void ReconcileModeAndSubscription()
    {
        ResolveSpawner();
        if (bottleSpawner == null)
        {
            Unsubscribe();
            return;
        }

        BottleIdentityMode mode = bottleSpawner.BottleIdentityMode;
        if (!activeMode.HasValue || activeMode.Value != mode)
        {
            activeMode = mode;
            Debug.Log(
                "[CanonicalBottleObservationSubscriber] identityMode=" + mode
                + " topic=" + topic
                + " canonicalFeaturePublisher=OFF",
                this);
        }

        if (mode != BottleIdentityMode.Canonical)
        {
            Unsubscribe();
            router = null;
            return;
        }

        if (router == null)
        {
            router = new CanonicalBottleObservationRouter(bottleSpawner);
        }
        EnsureSubscribed();
    }

    private void EnsureSubscribed()
    {
        if (!string.IsNullOrEmpty(subscribedTopic))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(topic))
        {
            Debug.LogError(
                "[CanonicalBottleObservationSubscriber] Canonical topic is empty; fail-closed.",
                this);
            return;
        }

        Ros2MessageRegistryCompatibility.EnsureRegistered();
        CanonicalBottleObservationMsg.Register();
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<CanonicalBottleObservationMsg>(topic, ReceiveMessage);
        subscribedTopic = topic;
        Debug.Log(
            "[CanonicalBottleObservationSubscriber] subscribed topic=" + topic
            + " authority=full-key no-legacy-fallback",
            this);
    }

    private void Unsubscribe()
    {
        if (ros != null && !string.IsNullOrEmpty(subscribedTopic))
        {
            ros.Unsubscribe(subscribedTopic);
        }
        subscribedTopic = null;
    }

    private void ReceiveMessage(CanonicalBottleObservationMsg message)
    {
        if (bottleSpawner == null
            || bottleSpawner.BottleIdentityMode != BottleIdentityMode.Canonical
            || router == null)
        {
            Debug.LogError(
                "[CanonicalBottleObservationSubscriber] Observation rejected: "
                + "canonical mode is not active; no legacy fallback.",
                this);
            return;
        }
        if (!CanonicalBottleObservationRosMapper.TryMap(
            message,
            out CanonicalBottleObservation observation,
            out string mapReason))
        {
            Debug.LogWarning(
                "[CanonicalBottleObservationSubscriber] Invalid observation rejected reason="
                + mapReason,
                this);
            return;
        }

        CanonicalObservationDisposition disposition = router.Submit(
            observation,
            out string reason);
        if (disposition == CanonicalObservationDisposition.OlderRejected
            || disposition == CanonicalObservationDisposition.ConflictingDuplicateRejected
            || disposition == CanonicalObservationDisposition.SinkRejected)
        {
            Debug.LogWarning(
                "[CanonicalBottleObservationSubscriber] observation=" + disposition
                + " reason=" + reason + " " + observation,
                this);
            return;
        }
        if (logAcceptedObservations
            && disposition != CanonicalObservationDisposition.DuplicateIgnored)
        {
            Debug.Log(
                "[CanonicalBottleObservationSubscriber] observation=" + disposition
                + " " + observation,
                this);
        }
    }

    private void ResolveSpawner()
    {
        if (bottleSpawner == null)
        {
            bottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
        }
    }
}
