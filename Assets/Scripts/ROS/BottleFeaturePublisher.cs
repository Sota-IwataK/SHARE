using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public sealed class BottleFeaturePublisher : RosTcpPublisher<Float32MultiArrayMsg>
{
    private const int FieldsPerBottle = 9;
    private const float MinimumDeltaTime = 0.0001f;
    private const float MaximumHistoryDeltaTime = 0.25f;

    [Header("Source")]
    [SerializeField] private DetectedBottlePoseSubscriber detectedBottleSubscriber;
    [SerializeField] private Transform hmdTransform;

    [Header("Publishing")]
    [SerializeField, Min(0.1f)] private float publishRateHz = 10f;

    [Header("Alignment")]
    [SerializeField, Range(0.1f, 179f)] private float handMaxAngleDeg = 60f;
    [SerializeField, Range(0.1f, 179f)] private float headMaxAngleDeg = 60f;

    [Header("Acceleration")]
    [SerializeField, Range(0f, 1f)] private float accelEmaAlpha = 0.25f;
    [SerializeField, Min(0.001f)] private float accelMaxMps2 = 1.5f;

    [Header("Fallback")]
    [SerializeField, Range(0f, 1f)] private float fallbackReachValue;

    [Header("Debug")]
    [SerializeField] private bool debugFeatureLogging;
    [SerializeField, Min(0.1f)] private float debugLogIntervalSec = 1f;

    private Float32MultiArrayMsg message;
    private float nextPublishTime;
    private float nextDebugLogTime;
    private bool hasPalmHistory;
    private Vector3 previousPalmPosition;
    private Vector3 previousPalmVelocity;
    private Vector3 filteredPalmAcceleration;
    private float previousPalmSampleTime;
    private bool palmTracked;
    private Vector3 palmPosition;
    private Vector3 palmForward;
    private bool lastHandsAggregatorAvailable;
    private float lastPalmDeltaTime;
    private Vector3 lastPalmVelocity;
    private Vector3 lastRawPalmAcceleration;
    private bool lastPalmHistoryValid;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePublisherExists()
    {
        if (FindObjectOfType<BottleFeaturePublisher>(true) != null)
        {
            return;
        }

        DetectedBottlePoseSubscriber subscriber =
            FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        GameObject host = subscriber != null
            ? subscriber.gameObject
            : new GameObject(nameof(BottleFeaturePublisher));
        BottleFeaturePublisher publisher = host.AddComponent<BottleFeaturePublisher>();
        publisher.detectedBottleSubscriber = subscriber;
    }

    private void Reset()
    {
        Topic = "/bottle_features";
    }

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(Topic))
        {
            Topic = "/bottle_features";
        }
    }

    protected override void Start()
    {
        base.Start();
        ResolveReferences();
        message = new Float32MultiArrayMsg
        {
            layout = new MultiArrayLayoutMsg
            {
                dim = new[]
                {
                    new MultiArrayDimensionMsg
                    {
                        label = "bottles",
                        size = 0,
                        stride = 0
                    },
                    new MultiArrayDimensionMsg
                    {
                        label = "fields",
                        size = FieldsPerBottle,
                        stride = FieldsPerBottle
                    }
                },
                data_offset = 0
            },
            data = new float[0]
        };
    }

    private void OnValidate()
    {
        publishRateHz = Mathf.Max(0.1f, publishRateHz);
        handMaxAngleDeg = Mathf.Clamp(handMaxAngleDeg, 0.1f, 179f);
        headMaxAngleDeg = Mathf.Clamp(headMaxAngleDeg, 0.1f, 179f);
        accelEmaAlpha = Mathf.Clamp01(accelEmaAlpha);
        accelMaxMps2 = Mathf.Max(0.001f, accelMaxMps2);
        fallbackReachValue = fallbackReachValue >= 0.5f ? 1f : 0f;
        debugLogIntervalSec = Mathf.Max(0.1f, debugLogIntervalSec);
    }

    private void Update()
    {
        UpdatePalmState();
        if (Time.unscaledTime < nextPublishTime)
        {
            return;
        }

        nextPublishTime = Time.unscaledTime + 1f / Mathf.Max(0.1f, publishRateHz);
        PublishFeatures();
    }

    private void PublishFeatures()
    {
        ResolveReferences();
        if (detectedBottleSubscriber == null || message == null)
        {
            return;
        }

        if (!detectedBottleSubscriber.TryGetLatestDetectedBottleTracks(
            out IReadOnlyList<DetectedBottleTrackSnapshot> tracks,
            out _))
        {
            SetMessageData(new float[0], 0);
            Publish(message);
            return;
        }

        bool hasHmd = TryGetHmdPose(out Vector3 headPosition, out Vector3 headForward);
        List<float> flattened = new List<float>(tracks.Count * FieldsPerBottle);
        bool logThisPublish = debugFeatureLogging && Time.unscaledTime >= nextDebugLogTime;
        if (logThisPublish)
        {
            LogPoseDiagnostics(hasHmd, headPosition, headForward);
            LogAccelerationDiagnostics();
        }

        for (int i = 0; i < tracks.Count; i++)
        {
            DetectedBottleTrackSnapshot track = tracks[i];
            if (!IsFinite(track.UnityPosition))
            {
                continue;
            }

            var fluPosition = track.UnityPosition.To<FLU>();
            Vector3 rosPosition = new Vector3(
                fluPosition.x,
                fluPosition.y,
                fluPosition.z);
            Vector3 handDirection = palmTracked
                ? track.UnityPosition - palmPosition
                : Vector3.zero;
            Vector3 headDirection = hasHmd
                ? track.UnityPosition - headPosition
                : Vector3.zero;

            float sHand = palmTracked
                ? CalculateAlignment(palmForward, handDirection, handMaxAngleDeg)
                : 0f;
            float sHead = hasHmd
                ? CalculateAlignment(headForward, headDirection, headMaxAngleDeg)
                : 0f;
            float sAccel = palmTracked
                ? CalculateAccelerationScore(filteredPalmAcceleration, handDirection)
                : 0f;
            float reach = fallbackReachValue >= 0.5f ? 1f : 0f;
            float touch = IsLocallyInteractingWithTrack(track.TrackId) ? 1f : 0f;

            AddRecord(
                flattened,
                track.TrackId,
                rosPosition,
                reach,
                touch,
                sHand,
                sHead,
                sAccel);

            if (logThisPublish)
            {
                Debug.Log("[BottleFeature]"
                    + "\nid=" + track.TrackId
                    + "\npos=" + rosPosition.ToString("F3")
                    + "\nreach=" + reach.ToString("F0")
                    + "\ntouch=" + touch.ToString("F0")
                    + "\nhand=" + sHand.ToString("F2")
                    + "\nhead=" + sHead.ToString("F2")
                    + "\naccel=" + sAccel.ToString("F2")
                    + "\nscoreInputValid=" + (palmTracked && hasHmd));
                LogBottleDiagnostics(
                    track,
                    headForward,
                    headDirection,
                    palmForward,
                    handDirection,
                    sHead,
                    sHand,
                    sAccel);
            }
        }

        if (logThisPublish)
        {
            nextDebugLogTime = Time.unscaledTime + debugLogIntervalSec;
        }

        int bottleCount = flattened.Count / FieldsPerBottle;
        SetMessageData(flattened.ToArray(), bottleCount);
        Publish(message);
    }

    private void UpdatePalmState()
    {
        if (!TryGetLeftPalmPose(out Vector3 currentPosition, out Vector3 currentForward))
        {
            ResetPalmHistory();
            return;
        }

        palmTracked = true;
        palmPosition = currentPosition;
        palmForward = currentForward;
        float now = Time.unscaledTime;
        if (!hasPalmHistory)
        {
            hasPalmHistory = true;
            previousPalmPosition = currentPosition;
            previousPalmVelocity = Vector3.zero;
            filteredPalmAcceleration = Vector3.zero;
            previousPalmSampleTime = now;
            lastPalmDeltaTime = 0f;
            lastPalmVelocity = Vector3.zero;
            lastRawPalmAcceleration = Vector3.zero;
            lastPalmHistoryValid = false;
            return;
        }

        float dt = now - previousPalmSampleTime;
        lastPalmDeltaTime = dt;
        if (!IsFinite(dt) || dt < MinimumDeltaTime || dt > MaximumHistoryDeltaTime)
        {
            previousPalmPosition = currentPosition;
            previousPalmVelocity = Vector3.zero;
            filteredPalmAcceleration = Vector3.zero;
            previousPalmSampleTime = now;
            lastPalmVelocity = Vector3.zero;
            lastRawPalmAcceleration = Vector3.zero;
            lastPalmHistoryValid = false;
            return;
        }

        Vector3 velocity = (currentPosition - previousPalmPosition) / dt;
        Vector3 acceleration = (velocity - previousPalmVelocity) / dt;
        lastPalmVelocity = velocity;
        lastRawPalmAcceleration = acceleration;
        if (!IsFinite(velocity) || !IsFinite(acceleration))
        {
            ResetPalmHistory();
            return;
        }

        filteredPalmAcceleration = Vector3.Lerp(
            filteredPalmAcceleration,
            acceleration,
            accelEmaAlpha);
        lastPalmHistoryValid = true;
        previousPalmPosition = currentPosition;
        previousPalmVelocity = velocity;
        previousPalmSampleTime = now;
    }

    private bool TryGetLeftPalmPose(out Vector3 position, out Vector3 forward)
    {
        position = default;
        forward = default;
        var aggregator = XRSubsystemHelpers.HandsAggregator;
        lastHandsAggregatorAvailable = aggregator != null;
        if (aggregator == null
            || !aggregator.TryGetJoint(
                TrackedHandJoint.Palm,
                XRNode.LeftHand,
                out HandJointPose palmPose))
        {
            return false;
        }

        position = palmPose.Position;
        forward = palmPose.Rotation * Vector3.forward;
        return IsFinite(position) && IsFinite(forward) && forward.sqrMagnitude > 0.000001f;
    }

    private void LogPoseDiagnostics(
        bool hasHmd,
        Vector3 headPosition,
        Vector3 headForward)
    {
        Transform resolvedHmd = hmdTransform != null
            ? hmdTransform
            : Camera.main != null ? Camera.main.transform : null;
        string hmdSource = hmdTransform != null
            ? "Inspector"
            : Camera.main != null ? "Camera.main" : "none";
        Debug.Log("[FeatureDiag][HMD]"
            + "\navailable=" + hasHmd
            + "\nsource=" + hmdSource
            + "\nname=" + (resolvedHmd != null ? resolvedHmd.gameObject.name : "none")
            + "\nposition=" + headPosition.ToString("F3")
            + "\nforward=" + headForward.ToString("F3"));
        if (hmdTransform == null && Camera.main == null)
        {
            Debug.LogWarning("[FeatureDiag][WARN] Camera.main unavailable");
        }

        Debug.Log("[FeatureDiag][Palm]"
            + "\nhandsAggregator=" + lastHandsAggregatorAvailable
            + "\ntracked=" + palmTracked
            + "\nposition=" + palmPosition.ToString("F3")
            + "\nforward=" + palmForward.ToString("F3"));
        if (!palmTracked)
        {
            Debug.LogWarning("[FeatureDiag][WARN] Left palm unavailable");
        }
    }

    private void LogAccelerationDiagnostics()
    {
        Debug.Log("[FeatureDiag][Accel]"
            + "\nhistoryValid=" + lastPalmHistoryValid
            + "\ndt=" + lastPalmDeltaTime.ToString("F6")
            + "\nvelocity=" + lastPalmVelocity.ToString("F3")
            + "\nrawAcceleration=" + lastRawPalmAcceleration.ToString("F3")
            + "\nfilteredAcceleration=" + filteredPalmAcceleration.ToString("F3"));
    }

    private static void LogBottleDiagnostics(
        DetectedBottleTrackSnapshot track,
        Vector3 headForward,
        Vector3 headDirection,
        Vector3 handForward,
        Vector3 handDirection,
        float sHead,
        float sHand,
        float sAccel)
    {
        bool hasHeadDirection = TryNormalize(headDirection, out Vector3 normalizedHeadDirection);
        bool hasHandDirection = TryNormalize(handDirection, out Vector3 normalizedHandDirection);
        bool hasHeadForward = TryNormalize(headForward, out Vector3 normalizedHeadForward);
        bool hasHandForward = TryNormalize(handForward, out Vector3 normalizedHandForward);
        float headDot = hasHeadDirection && hasHeadForward
            ? Vector3.Dot(normalizedHeadForward, normalizedHeadDirection)
            : 0f;
        float handDot = hasHandDirection && hasHandForward
            ? Vector3.Dot(normalizedHandForward, normalizedHandDirection)
            : 0f;
        float headAngle = hasHeadDirection && hasHeadForward
            ? Vector3.Angle(normalizedHeadForward, normalizedHeadDirection)
            : 0f;
        float handAngle = hasHandDirection && hasHandForward
            ? Vector3.Angle(normalizedHandForward, normalizedHandDirection)
            : 0f;

        Debug.Log("[FeatureDiag][Bottle]"
            + "\nid=" + track.TrackId
            + "\nposition=" + track.UnityPosition.ToString("F3")
            + "\nheadDir=" + normalizedHeadDirection.ToString("F3")
            + "\nheadDot=" + headDot.ToString("F4")
            + "\nheadAngleDeg=" + headAngle.ToString("F2")
            + "\ns_head=" + sHead.ToString("F4")
            + "\nhandDir=" + normalizedHandDirection.ToString("F3")
            + "\nhandDot=" + handDot.ToString("F4")
            + "\nhandAngleDeg=" + handAngle.ToString("F2")
            + "\ns_hand=" + sHand.ToString("F4")
            + "\ns_accel=" + sAccel.ToString("F4"));
    }

    private bool TryGetHmdPose(out Vector3 position, out Vector3 forward)
    {
        Transform resolvedHmd = hmdTransform != null
            ? hmdTransform
            : Camera.main != null ? Camera.main.transform : null;
        if (resolvedHmd == null)
        {
            position = default;
            forward = default;
            return false;
        }

        position = resolvedHmd.position;
        forward = resolvedHmd.forward;
        return IsFinite(position) && IsFinite(forward) && forward.sqrMagnitude > 0.000001f;
    }

    private float CalculateAccelerationScore(Vector3 acceleration, Vector3 bottleDirection)
    {
        if (!TryNormalize(bottleDirection, out Vector3 direction)
            || !IsFinite(acceleration))
        {
            return 0f;
        }

        float towardAcceleration = Vector3.Dot(acceleration, direction);
        return Mathf.Clamp01(towardAcceleration / Mathf.Max(0.001f, accelMaxMps2));
    }

    private static float CalculateAlignment(
        Vector3 sourceForward,
        Vector3 targetDirection,
        float maxAngleDeg)
    {
        if (!TryNormalize(sourceForward, out Vector3 forward)
            || !TryNormalize(targetDirection, out Vector3 direction))
        {
            return 0f;
        }

        float cosine = Vector3.Dot(forward, direction);
        float cosineThreshold = Mathf.Cos(Mathf.Deg2Rad * maxAngleDeg);
        float denominator = 1f - cosineThreshold;
        if (denominator <= 0.000001f)
        {
            return 0f;
        }

        return Mathf.Clamp01((cosine - cosineThreshold) / denominator);
    }

    private static bool IsLocallyInteractingWithTrack(int trackId)
    {
        NetworkedSharedSceneObject[] bottles =
            FindObjectsOfType<NetworkedSharedSceneObject>(true);
        for (int i = 0; i < bottles.Length; i++)
        {
            NetworkedSharedSceneObject bottle = bottles[i];
            if (bottle != null
                && bottle.IsPhotonSharedNetworkBottle
                && bottle.SharedOrigin == SharedBottleOrigin.RosDetected
                && bottle.SharedDetectedBottleTrackId == trackId
                && bottle.IsLocalGrabActive)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddRecord(
        List<float> destination,
        int trackId,
        Vector3 rosPosition,
        float reach,
        float touch,
        float sHand,
        float sHead,
        float sAccel)
    {
        destination.Add(trackId);
        destination.Add(rosPosition.x);
        destination.Add(rosPosition.y);
        destination.Add(rosPosition.z);
        destination.Add(reach);
        destination.Add(touch);
        destination.Add(Mathf.Clamp01(sHand));
        destination.Add(Mathf.Clamp01(sHead));
        destination.Add(Mathf.Clamp01(sAccel));
    }

    private void SetMessageData(float[] data, int bottleCount)
    {
        message.data = data;
        message.layout.dim[0].size = (uint)bottleCount;
        message.layout.dim[0].stride = (uint)data.Length;
    }

    private void ResolveReferences()
    {
        if (detectedBottleSubscriber == null)
        {
            detectedBottleSubscriber =
                FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        }
    }

    private void ResetPalmHistory()
    {
        palmTracked = false;
        hasPalmHistory = false;
        previousPalmPosition = default;
        previousPalmVelocity = default;
        filteredPalmAcceleration = default;
        previousPalmSampleTime = 0f;
        lastPalmDeltaTime = 0f;
        lastPalmVelocity = default;
        lastRawPalmAcceleration = default;
        lastPalmHistoryValid = false;
    }

    private static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        if (!IsFinite(value) || value.sqrMagnitude <= 0.000001f)
        {
            normalized = default;
            return false;
        }

        normalized = value.normalized;
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
