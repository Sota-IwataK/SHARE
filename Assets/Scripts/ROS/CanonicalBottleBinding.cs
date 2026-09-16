using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Direct full-key binding attached to one canonical Bottle GameObject.</summary>
[DisallowMultipleComponent]
public sealed class CanonicalBottleBinding : MonoBehaviour
{
    private static readonly Dictionary<CanonicalBottleIdentity, CanonicalBottleBinding> Bindings =
        new Dictionary<CanonicalBottleIdentity, CanonicalBottleBinding>();

    [SerializeField] private bool hasIdentity;
    [SerializeField] private string sourceId = string.Empty;
    [SerializeField] private string sessionId = string.Empty;
    [SerializeField] private ulong objectId;
    [SerializeField] private CanonicalBottleLifecycle lifecycle;
    [SerializeField] private ulong observationSequence;
    [SerializeField] private int observationStampSec;
    [SerializeField] private uint observationStampNanosec;
    [SerializeField] private string observationClockDomain = string.Empty;
    [SerializeField] private string frameId = string.Empty;
    [SerializeField] private bool valid;

    public bool HasIdentity => hasIdentity;
    public bool Valid => valid;
    public CanonicalBottleLifecycle Lifecycle => lifecycle;
    public ulong ObservationSequence => observationSequence;
    public int ObservationStampSec => observationStampSec;
    public uint ObservationStampNanosec => observationStampNanosec;
    public string ObservationClockDomain => observationClockDomain;
    public string FrameId => frameId;

    public CanonicalBottleIdentity Identity
    {
        get
        {
            if (!hasIdentity)
            {
                throw new InvalidOperationException("Canonical binding has no identity.");
            }
            return new CanonicalBottleIdentity(sourceId, sessionId, objectId);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        Bindings.Clear();
    }

    private void OnEnable()
    {
        if (!hasIdentity)
        {
            return;
        }
        if (!TryRegister(Identity, this, out string reason))
        {
            Debug.LogError(
                "[CanonicalBottleBinding] Serialized duplicate rejected key="
                + Identity + " reason=" + reason,
                this);
        }
    }

    private void OnDisable()
    {
        UnregisterCurrentIdentity();
    }

    private void OnDestroy()
    {
        UnregisterCurrentIdentity();
    }

    public bool TryApplyObservation(
        CanonicalBottleObservation observation,
        out CanonicalObservationDisposition disposition,
        out string reason)
    {
        if (!hasIdentity)
        {
            if (!TryRegister(observation.Identity, this, out reason))
            {
                disposition = CanonicalObservationDisposition.ConflictingDuplicateRejected;
                return false;
            }
            Apply(observation);
            disposition = CanonicalObservationDisposition.AcceptedNew;
            reason = "Bound";
            return true;
        }

        CanonicalBottleIdentity currentIdentity = Identity;
        if (currentIdentity != observation.Identity)
        {
            disposition = CanonicalObservationDisposition.ConflictingDuplicateRejected;
            reason = "BindingIdentityMismatch";
            return false;
        }

        CanonicalBottleObservation current = CurrentObservation;
        ulong incomingSequence = observation.Metadata.ObservationSequence;
        if (incomingSequence < current.Metadata.ObservationSequence)
        {
            disposition = CanonicalObservationDisposition.OlderRejected;
            reason = "OlderSequence";
            return false;
        }
        if (incomingSequence == current.Metadata.ObservationSequence)
        {
            if (observation.Equals(current))
            {
                disposition = CanonicalObservationDisposition.DuplicateIgnored;
                reason = "Duplicate";
                return true;
            }
            disposition = CanonicalObservationDisposition.ConflictingDuplicateRejected;
            reason = "ConflictingDuplicate";
            return false;
        }

        Apply(observation);
        disposition = CanonicalObservationDisposition.AcceptedUpdate;
        reason = "Updated";
        return true;
    }

    public bool TryGetObservation(out CanonicalBottleObservation observation)
    {
        if (!hasIdentity)
        {
            observation = default;
            return false;
        }
        observation = CurrentObservation;
        return true;
    }

    public static bool TryFind(
        CanonicalBottleIdentity identity,
        out CanonicalBottleBinding binding)
    {
        if (Bindings.TryGetValue(identity, out binding))
        {
            if (binding != null && binding.hasIdentity && binding.Identity == identity)
            {
                return true;
            }
            Bindings.Remove(identity);
        }
        binding = null;
        return false;
    }

    public static int ActiveBindingCount
    {
        get
        {
            RemoveDestroyedBindings();
            return Bindings.Count;
        }
    }

    public static void ClearRegistryForTests()
    {
        Bindings.Clear();
    }

    private CanonicalBottleObservation CurrentObservation =>
        new CanonicalBottleObservation(
            Identity,
            lifecycle,
            new CanonicalBottleObservationMetadata(
                observationSequence,
                observationStampSec,
                observationStampNanosec,
                observationClockDomain,
                frameId));

    private void Apply(CanonicalBottleObservation observation)
    {
        hasIdentity = true;
        sourceId = observation.Identity.SourceId;
        sessionId = observation.Identity.SessionId;
        objectId = observation.Identity.ObjectId;
        lifecycle = observation.Lifecycle;
        observationSequence = observation.Metadata.ObservationSequence;
        observationStampSec = observation.Metadata.StampSec;
        observationStampNanosec = observation.Metadata.StampNanosec;
        observationClockDomain = observation.Metadata.ObservationClockDomain;
        frameId = observation.Metadata.FrameId;
        valid = observation.Valid;
    }

    private static bool TryRegister(
        CanonicalBottleIdentity identity,
        CanonicalBottleBinding candidate,
        out string reason)
    {
        if (TryFind(identity, out CanonicalBottleBinding existing)
            && existing != candidate)
        {
            reason = "DuplicateFullKeyAlreadyBound";
            return false;
        }
        Bindings[identity] = candidate;
        reason = "Registered";
        return true;
    }

    private void UnregisterCurrentIdentity()
    {
        if (!hasIdentity)
        {
            return;
        }
        CanonicalBottleIdentity identity = Identity;
        if (Bindings.TryGetValue(identity, out CanonicalBottleBinding current)
            && current == this)
        {
            Bindings.Remove(identity);
        }
    }

    private static void RemoveDestroyedBindings()
    {
        if (Bindings.Count == 0)
        {
            return;
        }
        List<CanonicalBottleIdentity> stale = null;
        foreach (KeyValuePair<CanonicalBottleIdentity, CanonicalBottleBinding> pair in Bindings)
        {
            if (pair.Value != null)
            {
                continue;
            }
            if (stale == null)
            {
                stale = new List<CanonicalBottleIdentity>();
            }
            stale.Add(pair.Key);
        }
        if (stale == null)
        {
            return;
        }
        for (int i = 0; i < stale.Count; i++)
        {
            Bindings.Remove(stale[i]);
        }
    }
}
