using System;
using System.Collections.Generic;
using UnityEngine;

public enum BottleIdentityMode
{
    Legacy = 0,
    Canonical = 1
}

public enum CanonicalBottleLifecycle : byte
{
    Observed = 0,
    Lost = 1,
    Removed = 2
}

public enum CanonicalObservationDisposition
{
    AcceptedNew = 0,
    AcceptedUpdate = 1,
    DuplicateIgnored = 2,
    OlderRejected = 3,
    ConflictingDuplicateRejected = 4,
    SinkRejected = 5
}

/// <summary>Participant-independent full identity issued by the canonical tracker.</summary>
public readonly struct CanonicalBottleIdentity : IEquatable<CanonicalBottleIdentity>
{
    public string SourceId { get; }
    public string SessionId { get; }
    public ulong ObjectId { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(SourceId)
        && !string.IsNullOrWhiteSpace(SessionId);

    public CanonicalBottleIdentity(string sourceId, string sessionId, ulong objectId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("Canonical source_id must be nonempty.", nameof(sourceId));
        }
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Canonical session_id must be nonempty.", nameof(sessionId));
        }

        SourceId = sourceId;
        SessionId = sessionId;
        ObjectId = objectId;
    }

    public bool Equals(CanonicalBottleIdentity other)
    {
        return ObjectId == other.ObjectId
            && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is CanonicalBottleIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (SourceId == null ? 0 : StringComparer.Ordinal.GetHashCode(SourceId));
            hash = hash * 31 + (SessionId == null ? 0 : StringComparer.Ordinal.GetHashCode(SessionId));
            hash = hash * 31 + ObjectId.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(CanonicalBottleIdentity left, CanonicalBottleIdentity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CanonicalBottleIdentity left, CanonicalBottleIdentity right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return "(" + SourceId + "," + SessionId + "," + ObjectId + ")";
    }
}

/// <summary>Exact source observation metadata; receive time is deliberately absent.</summary>
public readonly struct CanonicalBottleObservationMetadata :
    IEquatable<CanonicalBottleObservationMetadata>
{
    public ulong ObservationSequence { get; }
    public int StampSec { get; }
    public uint StampNanosec { get; }
    public string ObservationClockDomain { get; }
    public string FrameId { get; }
    public bool IsValid => StampNanosec < 1_000_000_000U
        && !string.IsNullOrWhiteSpace(ObservationClockDomain)
        && !string.IsNullOrWhiteSpace(FrameId);

    public CanonicalBottleObservationMetadata(
        ulong observationSequence,
        int stampSec,
        uint stampNanosec,
        string observationClockDomain,
        string frameId)
    {
        if (stampNanosec >= 1_000_000_000U)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stampNanosec),
                "ROS Time nanosec must be below 1,000,000,000.");
        }
        if (string.IsNullOrWhiteSpace(observationClockDomain))
        {
            throw new ArgumentException(
                "observation_clock_domain must be nonempty.",
                nameof(observationClockDomain));
        }
        if (string.IsNullOrWhiteSpace(frameId))
        {
            throw new ArgumentException(
                "frame_id must be nonempty.",
                nameof(frameId));
        }

        ObservationSequence = observationSequence;
        StampSec = stampSec;
        StampNanosec = stampNanosec;
        ObservationClockDomain = observationClockDomain;
        FrameId = frameId;
    }

    public bool Equals(CanonicalBottleObservationMetadata other)
    {
        return ObservationSequence == other.ObservationSequence
            && StampSec == other.StampSec
            && StampNanosec == other.StampNanosec
            && string.Equals(
                ObservationClockDomain,
                other.ObservationClockDomain,
                StringComparison.Ordinal)
            && string.Equals(FrameId, other.FrameId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is CanonicalBottleObservationMetadata other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = ObservationSequence.GetHashCode();
            hash = hash * 31 + StampSec;
            hash = hash * 31 + StampNanosec.GetHashCode();
            hash = hash * 31 + (ObservationClockDomain == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(ObservationClockDomain));
            hash = hash * 31 + (FrameId == null
                ? 0
                : StringComparer.Ordinal.GetHashCode(FrameId));
            return hash;
        }
    }

}

/// <summary>One atomic identity/lifecycle observation. It carries no pose.</summary>
public readonly struct CanonicalBottleObservation : IEquatable<CanonicalBottleObservation>
{
    public CanonicalBottleIdentity Identity { get; }
    public CanonicalBottleLifecycle Lifecycle { get; }
    public CanonicalBottleObservationMetadata Metadata { get; }
    public bool Valid => Lifecycle == CanonicalBottleLifecycle.Observed;

    public CanonicalBottleObservation(
        CanonicalBottleIdentity identity,
        CanonicalBottleLifecycle lifecycle,
        CanonicalBottleObservationMetadata metadata)
    {
        if (!identity.IsValid)
        {
            throw new ArgumentException("Canonical identity is invalid.", nameof(identity));
        }
        if (!metadata.IsValid)
        {
            throw new ArgumentException("Canonical observation metadata is invalid.", nameof(metadata));
        }
        if (!Enum.IsDefined(typeof(CanonicalBottleLifecycle), lifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycle));
        }
        Identity = identity;
        Lifecycle = lifecycle;
        Metadata = metadata;
    }

    public bool Equals(CanonicalBottleObservation other)
    {
        return Identity.Equals(other.Identity)
            && Lifecycle == other.Lifecycle
            && Metadata.Equals(other.Metadata);
    }

    public override bool Equals(object obj)
    {
        return obj is CanonicalBottleObservation other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Identity.GetHashCode() * 397)
                ^ ((int)Lifecycle * 31)
                ^ Metadata.GetHashCode();
        }
    }

    public override string ToString()
    {
        return Identity
            + " sequence=" + Metadata.ObservationSequence
            + " lifecycle=" + Lifecycle
            + " stamp=" + Metadata.StampSec + "." + Metadata.StampNanosec.ToString("D9")
            + " clock=" + Metadata.ObservationClockDomain
            + " frame=" + Metadata.FrameId;
    }
}

/// <summary>Sequence state is scoped by the complete canonical key.</summary>
public sealed class CanonicalBottleObservationLedger
{
    private readonly Dictionary<CanonicalBottleIdentity, CanonicalBottleObservation> latest =
        new Dictionary<CanonicalBottleIdentity, CanonicalBottleObservation>();

    public CanonicalObservationDisposition Evaluate(
        CanonicalBottleObservation observation,
        out bool hasPrevious,
        out CanonicalBottleObservation previous)
    {
        hasPrevious = latest.TryGetValue(observation.Identity, out previous);
        if (!hasPrevious)
        {
            return CanonicalObservationDisposition.AcceptedNew;
        }

        ulong incoming = observation.Metadata.ObservationSequence;
        ulong current = previous.Metadata.ObservationSequence;
        if (incoming < current)
        {
            return CanonicalObservationDisposition.OlderRejected;
        }
        if (incoming == current)
        {
            return observation.Equals(previous)
                ? CanonicalObservationDisposition.DuplicateIgnored
                : CanonicalObservationDisposition.ConflictingDuplicateRejected;
        }
        return CanonicalObservationDisposition.AcceptedUpdate;
    }

    public void Commit(CanonicalBottleObservation observation)
    {
        latest[observation.Identity] = observation;
    }

    public bool TryGet(
        CanonicalBottleIdentity identity,
        out CanonicalBottleObservation observation)
    {
        return latest.TryGetValue(identity, out observation);
    }

    public int Count => latest.Count;
}

public interface ICanonicalBottleObservationSink
{
    bool TryApplyCanonicalBottleObservation(
        CanonicalBottleObservation observation,
        out string reason);
}

/// <summary>Commits ordering state only after the direct binding sink succeeds.</summary>
public sealed class CanonicalBottleObservationRouter
{
    private readonly CanonicalBottleObservationLedger ledger =
        new CanonicalBottleObservationLedger();
    private readonly ICanonicalBottleObservationSink sink;

    public CanonicalBottleObservationRouter(ICanonicalBottleObservationSink sink)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public CanonicalObservationDisposition Submit(
        CanonicalBottleObservation observation,
        out string reason)
    {
        CanonicalObservationDisposition disposition = ledger.Evaluate(
            observation,
            out _,
            out _);
        if (disposition == CanonicalObservationDisposition.DuplicateIgnored)
        {
            reason = "Duplicate";
            return disposition;
        }
        if (disposition == CanonicalObservationDisposition.OlderRejected)
        {
            reason = "OlderSequence";
            return disposition;
        }
        if (disposition == CanonicalObservationDisposition.ConflictingDuplicateRejected)
        {
            reason = "ConflictingDuplicate";
            return disposition;
        }
        if (!sink.TryApplyCanonicalBottleObservation(observation, out reason))
        {
            return CanonicalObservationDisposition.SinkRejected;
        }

        ledger.Commit(observation);
        return disposition;
    }

    public bool TryGetLatest(
        CanonicalBottleIdentity identity,
        out CanonicalBottleObservation observation)
    {
        return ledger.TryGet(identity, out observation);
    }
}

/// <summary>Lossless Photon metadata payload; NetworkObject.Id is deliberately absent.</summary>
public readonly struct CanonicalBottlePhotonPayload
{
    public string SourceId { get; }
    public string SessionId { get; }
    public ulong ObjectId { get; }
    public CanonicalBottleLifecycle Lifecycle { get; }
    public ulong ObservationSequence { get; }
    public int StampSec { get; }
    public uint StampNanosec { get; }
    public string ObservationClockDomain { get; }
    public string FrameId { get; }

    private CanonicalBottlePhotonPayload(CanonicalBottleObservation observation)
    {
        SourceId = observation.Identity.SourceId;
        SessionId = observation.Identity.SessionId;
        ObjectId = observation.Identity.ObjectId;
        Lifecycle = observation.Lifecycle;
        ObservationSequence = observation.Metadata.ObservationSequence;
        StampSec = observation.Metadata.StampSec;
        StampNanosec = observation.Metadata.StampNanosec;
        ObservationClockDomain = observation.Metadata.ObservationClockDomain;
        FrameId = observation.Metadata.FrameId;
    }

    public static bool TryCreate(
        CanonicalBottleObservation observation,
        out CanonicalBottlePhotonPayload payload,
        out string reason)
    {
        if (!CanonicalBottlePhotonContract.Validate(observation, out reason))
        {
            payload = default;
            return false;
        }
        payload = new CanonicalBottlePhotonPayload(observation);
        return true;
    }

    public CanonicalBottleObservation ToObservation()
    {
        return new CanonicalBottleObservation(
            new CanonicalBottleIdentity(SourceId, SessionId, ObjectId),
            Lifecycle,
            new CanonicalBottleObservationMetadata(
                ObservationSequence,
                StampSec,
                StampNanosec,
                ObservationClockDomain,
                FrameId));
    }
}

public static class CanonicalBottlePhotonContract
{
    public const int SourceIdCapacity = 64;
    public const int SessionIdCapacity = 128;
    public const int ClockDomainCapacity = 64;
    public const int FrameIdCapacity = 128;

    public static bool Validate(CanonicalBottleObservation observation, out string reason)
    {
        if (!Fits(observation.Identity.SourceId, SourceIdCapacity))
        {
            reason = "SourceIdExceedsPhotonCapacity";
            return false;
        }
        if (!Fits(observation.Identity.SessionId, SessionIdCapacity))
        {
            reason = "SessionIdExceedsPhotonCapacity";
            return false;
        }
        if (!Fits(
            observation.Metadata.ObservationClockDomain,
            ClockDomainCapacity))
        {
            reason = "ClockDomainExceedsPhotonCapacity";
            return false;
        }
        if (!Fits(observation.Metadata.FrameId, FrameIdCapacity))
        {
            reason = "FrameIdExceedsPhotonCapacity";
            return false;
        }
        reason = "Valid";
        return true;
    }

    public static bool Fits(string value, int capacity)
    {
        if (value == null || capacity < 0)
        {
            return false;
        }

        int scalars = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (char.IsHighSurrogate(character))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return false;
                }
                i++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return false;
            }

            scalars++;
            if (scalars > capacity)
            {
                return false;
            }
        }
        return true;
    }
}

public static class BottleIdentityModeRuntime
{
    public static bool TryResolve(out BottleIdentityMode mode, out string reason)
    {
        PhotonSharedBottleSpawner[] spawners =
            UnityEngine.Object.FindObjectsOfType<PhotonSharedBottleSpawner>(true);
        if (spawners.Length == 0)
        {
            mode = BottleIdentityMode.Legacy;
            reason = "NoSpawnerDefaultsLegacy";
            return true;
        }

        mode = spawners[0].BottleIdentityMode;
        for (int i = 1; i < spawners.Length; i++)
        {
            if (spawners[i].BottleIdentityMode != mode)
            {
                reason = "ConflictingBottleIdentityModes";
                return false;
            }
        }
        reason = "Resolved";
        return true;
    }

    public static bool IsLegacyActive()
    {
        return TryResolve(out BottleIdentityMode mode, out _)
            && mode == BottleIdentityMode.Legacy;
    }
}
