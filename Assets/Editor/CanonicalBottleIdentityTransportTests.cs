#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.ShareSemanticInterfaces;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

public sealed class CanonicalBottleIdentityTransportTests
{
    private sealed class RecordingSink : ICanonicalBottleObservationSink
    {
        public bool Accept = true;
        public int Calls;
        public CanonicalBottleObservation Last;

        public bool TryApplyCanonicalBottleObservation(
            CanonicalBottleObservation observation,
            out string reason)
        {
            Calls++;
            Last = observation;
            reason = Accept ? "Accepted" : "RejectedForTest";
            return Accept;
        }
    }

    private static CanonicalBottleObservation Observation(
        string source = "tracker-a",
        string session = "session-a",
        ulong objectId = 16_777_217UL,
        ulong sequence = 1UL,
        CanonicalBottleLifecycle lifecycle = CanonicalBottleLifecycle.Observed,
        int stampSec = -1,
        uint stampNanosec = 999_999_999U,
        string clock = "ros_time",
        string frame = "camera/color_optical_frame")
    {
        return new CanonicalBottleObservation(
            new CanonicalBottleIdentity(source, session, objectId),
            lifecycle,
            new CanonicalBottleObservationMetadata(
                sequence,
                stampSec,
                stampNanosec,
                clock,
                frame));
    }

    [TearDown]
    public void TearDown()
    {
        CanonicalBottleBinding.ClearRegistryForTests();
        Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null
                && transforms[i].gameObject.name.StartsWith(
                    "CanonicalBindingTest",
                    StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(transforms[i].gameObject);
            }
        }
    }

    [Test]
    public void GeneratedMessageUsesUint64AndRos2TimeTypes()
    {
        Assert.AreEqual(
            "share_semantic_interfaces/CanonicalBottleObservation",
            CanonicalBottleObservationMsg.k_RosMessageName);
        Assert.AreEqual(typeof(ulong),
            typeof(CanonicalBottleObservationMsg).GetField("object_id").FieldType);
        Assert.AreEqual(typeof(ulong),
            typeof(CanonicalBottleObservationMsg).GetField("observation_sequence").FieldType);
        Assert.AreEqual(typeof(TimeMsg),
            typeof(CanonicalBottleObservationMsg).GetField("observation_stamp").FieldType);
        Assert.AreEqual(typeof(int), typeof(TimeMsg).GetField("sec").FieldType);
        Assert.AreEqual(typeof(uint), typeof(TimeMsg).GetField("nanosec").FieldType);
    }

    [TestCase(16_777_217UL)]
    [TestCase(ulong.MaxValue)]
    public void RosCdrRoundTripPreservesLargeIdsUint64AndTime(ulong objectId)
    {
        CanonicalBottleObservationMsg input = new CanonicalBottleObservationMsg(
            "source-a",
            "session-a",
            objectId,
            CanonicalBottleObservationMsg.OBSERVED,
            ulong.MaxValue,
            new TimeMsg(int.MinValue, 999_999_999U),
            "ros_time",
            "camera/color_optical_frame");

        MessageSerializer serializer = new MessageSerializer();
        serializer.SerializeMessage(input);
        MessageDeserializer deserializer = new MessageDeserializer();
        deserializer.InitWithBuffer(serializer.GetBytes());
        CanonicalBottleObservationMsg output =
            CanonicalBottleObservationMsg.Deserialize(deserializer);

        Assert.AreEqual(input.source_id, output.source_id);
        Assert.AreEqual(input.session_id, output.session_id);
        Assert.AreEqual(objectId, output.object_id);
        Assert.AreEqual(ulong.MaxValue, output.observation_sequence);
        Assert.AreEqual(int.MinValue, output.observation_stamp.sec);
        Assert.AreEqual(999_999_999U, output.observation_stamp.nanosec);
        Assert.AreEqual(input.observation_clock_domain, output.observation_clock_domain);
        Assert.AreEqual(input.frame_id, output.frame_id);
    }

    [Test]
    public void FullKeyIncludesSourceSessionAndObjectWithoutParticipantOrTrackId()
    {
        CanonicalBottleIdentity a = new CanonicalBottleIdentity("source-a", "session-a", 7UL);
        CanonicalBottleIdentity b = new CanonicalBottleIdentity("source-a", "session-b", 7UL);
        CanonicalBottleIdentity c = new CanonicalBottleIdentity("source-b", "session-a", 7UL);

        Assert.AreNotEqual(a, b);
        Assert.AreNotEqual(a, c);
        string[] members = typeof(CanonicalBottleIdentity)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name)
            .ToArray();
        CollectionAssert.DoesNotContain(members, "TrackId");
        CollectionAssert.DoesNotContain(members, "ParticipantId");
        ParameterInfo[] constructorParameters = typeof(CanonicalBottleIdentity)
            .GetConstructors().Single().GetParameters();
        CollectionAssert.AreEqual(
            new[] { typeof(string), typeof(string), typeof(ulong) },
            constructorParameters.Select(parameter => parameter.ParameterType).ToArray());
    }

    [Test]
    public void SameCanonicalIdentityIsParticipantIndependent()
    {
        CanonicalBottleIdentity bottle =
            new CanonicalBottleIdentity("source-a", "session-a", 42UL);
        CanonicalBottleIdentity user1View = bottle;
        CanonicalBottleIdentity user2View = bottle;
        Assert.AreEqual(user1View, user2View);
        Assert.IsNull(typeof(CanonicalBottleIdentity).GetProperty("ParticipantId"));
    }

    [Test]
    public void MapperPreservesFullTupleSequenceStampClockAndFrame()
    {
        CanonicalBottleObservationMsg message = new CanonicalBottleObservationMsg(
            "source-a",
            "session-a",
            ulong.MaxValue,
            CanonicalBottleObservationMsg.LOST,
            ulong.MaxValue - 1UL,
            new TimeMsg(-42, 123_456_789U),
            "sim_time",
            "camera/frame_2");

        Assert.IsTrue(CanonicalBottleObservationRosMapper.TryMap(
            message,
            out CanonicalBottleObservation observation,
            out string reason), reason);
        Assert.AreEqual("source-a", observation.Identity.SourceId);
        Assert.AreEqual("session-a", observation.Identity.SessionId);
        Assert.AreEqual(ulong.MaxValue, observation.Identity.ObjectId);
        Assert.AreEqual(CanonicalBottleLifecycle.Lost, observation.Lifecycle);
        Assert.AreEqual(ulong.MaxValue - 1UL, observation.Metadata.ObservationSequence);
        Assert.AreEqual(-42, observation.Metadata.StampSec);
        Assert.AreEqual(123_456_789U, observation.Metadata.StampNanosec);
        Assert.AreEqual("sim_time", observation.Metadata.ObservationClockDomain);
        Assert.AreEqual("camera/frame_2", observation.Metadata.FrameId);
    }

    [Test]
    public void RosMessageMapsDirectlyToGameObjectFullKeyBinding()
    {
        CanonicalBottleObservationMsg message = new CanonicalBottleObservationMsg(
            "source-direct", "session-direct", 16_777_217UL,
            CanonicalBottleObservationMsg.OBSERVED, 77UL,
            new TimeMsg(123, 456U), "ros_time", "camera/frame");
        Assert.IsTrue(CanonicalBottleObservationRosMapper.TryMap(
            message,
            out CanonicalBottleObservation observation,
            out string mapReason), mapReason);

        GameObject target = new GameObject("CanonicalBindingTestRosIngress");
        CanonicalBottleBinding binding = target.AddComponent<CanonicalBottleBinding>();
        Assert.IsTrue(binding.TryApplyObservation(
            observation,
            out CanonicalObservationDisposition disposition,
            out string bindReason), bindReason);
        Assert.AreEqual(CanonicalObservationDisposition.AcceptedNew, disposition);
        Assert.AreEqual(
            new CanonicalBottleIdentity("source-direct", "session-direct", 16_777_217UL),
            binding.Identity);
        Assert.AreEqual(77UL, binding.ObservationSequence);
    }

    [Test]
    public void MapperRejectsUnknownLifecycleInvalidTimeAndInvalidFrame()
    {
        CanonicalBottleObservationMsg message = new CanonicalBottleObservationMsg(
            "source-a", "session-a", 1UL, 9, 1UL,
            new TimeMsg(1, 0), "ros_time", "camera/frame");
        Assert.IsFalse(CanonicalBottleObservationRosMapper.TryMap(message, out _, out _));

        message.lifecycle = CanonicalBottleObservationMsg.OBSERVED;
        message.observation_stamp.nanosec = 1_000_000_000U;
        Assert.IsFalse(CanonicalBottleObservationRosMapper.TryMap(message, out _, out _));

        message.observation_stamp.nanosec = 0;
        message.frame_id = "   ";
        Assert.IsFalse(CanonicalBottleObservationRosMapper.TryMap(message, out _, out _));
    }

    [Test]
    public void LedgerScopesOrderingByFullKeyAndRejectsConflicts()
    {
        CanonicalBottleObservationLedger ledger = new CanonicalBottleObservationLedger();
        CanonicalBottleObservation first = Observation(sequence: 4);
        Assert.AreEqual(CanonicalObservationDisposition.AcceptedNew,
            ledger.Evaluate(first, out _, out _));
        ledger.Commit(first);
        Assert.AreEqual(CanonicalObservationDisposition.DuplicateIgnored,
            ledger.Evaluate(first, out _, out _));
        Assert.AreEqual(CanonicalObservationDisposition.OlderRejected,
            ledger.Evaluate(Observation(sequence: 3), out _, out _));
        Assert.AreEqual(CanonicalObservationDisposition.ConflictingDuplicateRejected,
            ledger.Evaluate(Observation(sequence: 4, stampSec: 2), out _, out _));
        Assert.AreEqual(CanonicalObservationDisposition.AcceptedNew,
            ledger.Evaluate(Observation(session: "session-b", sequence: 1), out _, out _));
    }

    [Test]
    public void RouterCommitsOnlyAfterSinkAccepts()
    {
        RecordingSink sink = new RecordingSink { Accept = false };
        CanonicalBottleObservationRouter router = new CanonicalBottleObservationRouter(sink);
        CanonicalBottleObservation observation = Observation(sequence: 8);
        Assert.AreEqual(CanonicalObservationDisposition.SinkRejected,
            router.Submit(observation, out _));
        Assert.IsFalse(router.TryGetLatest(observation.Identity, out _));

        sink.Accept = true;
        Assert.AreEqual(CanonicalObservationDisposition.AcceptedNew,
            router.Submit(observation, out _));
        Assert.IsTrue(router.TryGetLatest(observation.Identity, out CanonicalBottleObservation stored));
        Assert.AreEqual(observation, stored);
        Assert.AreEqual(2, sink.Calls);
    }

    [Test]
    public void DirectBindingUsesFullKeyAndRejectsSecondGameObject()
    {
        GameObject firstObject = new GameObject("CanonicalBindingTestFirst");
        GameObject secondObject = new GameObject("CanonicalBindingTestSecond");
        CanonicalBottleBinding first = firstObject.AddComponent<CanonicalBottleBinding>();
        CanonicalBottleBinding second = secondObject.AddComponent<CanonicalBottleBinding>();
        CanonicalBottleObservation observation = Observation(sequence: 9);

        Assert.IsTrue(first.TryApplyObservation(observation, out _, out string firstReason), firstReason);
        Assert.IsTrue(CanonicalBottleBinding.TryFind(observation.Identity, out CanonicalBottleBinding found));
        Assert.AreSame(first, found);
        Assert.IsFalse(second.TryApplyObservation(observation, out _, out string secondReason));
        Assert.AreEqual("DuplicateFullKeyAlreadyBound", secondReason);
    }

    [Test]
    public void BindingPreservesLifecycleAndExactMetadata()
    {
        GameObject target = new GameObject("CanonicalBindingTestLifecycle");
        CanonicalBottleBinding binding = target.AddComponent<CanonicalBottleBinding>();
        CanonicalBottleObservation observed = Observation(sequence: 1);
        CanonicalBottleObservation lost = Observation(
            sequence: 2,
            lifecycle: CanonicalBottleLifecycle.Lost,
            stampSec: int.MaxValue,
            stampNanosec: 999_999_999U,
            clock: "sim_time",
            frame: "map/frame");

        Assert.IsTrue(binding.TryApplyObservation(observed, out _, out _));
        Assert.IsTrue(binding.Valid);
        Assert.IsTrue(binding.TryApplyObservation(lost, out _, out _));
        Assert.IsFalse(binding.Valid);
        Assert.AreEqual(2UL, binding.ObservationSequence);
        Assert.AreEqual(int.MaxValue, binding.ObservationStampSec);
        Assert.AreEqual(999_999_999U, binding.ObservationStampNanosec);
        Assert.AreEqual("sim_time", binding.ObservationClockDomain);
        Assert.AreEqual("map/frame", binding.FrameId);
    }

    [Test]
    public void PhotonPayloadRoundTripPreservesFullTupleAndHasNoNetworkObjectIdentity()
    {
        CanonicalBottleObservation input = Observation(
            source: "source-photon",
            session: "session-photon",
            objectId: ulong.MaxValue,
            sequence: ulong.MaxValue,
            lifecycle: CanonicalBottleLifecycle.Lost,
            stampSec: int.MinValue,
            stampNanosec: 999_999_999U,
            clock: "ros_time",
            frame: "camera/frame");
        Assert.IsTrue(CanonicalBottlePhotonPayload.TryCreate(
            input,
            out CanonicalBottlePhotonPayload payload,
            out string reason), reason);
        Assert.AreEqual(input, payload.ToObservation());
        Assert.IsNull(typeof(CanonicalBottlePhotonPayload).GetProperty("TrackId"));
        Assert.IsNull(typeof(CanonicalBottlePhotonPayload).GetProperty("NetworkObjectId"));
        Assert.IsNull(typeof(CanonicalBottlePhotonPayload).GetProperty("ParticipantId"));
    }

    [Test]
    public void RespawnedGameObjectKeepsCanonicalIdentityWithoutInstanceIdPromotion()
    {
        CanonicalBottleObservation observation = Observation(sequence: 12);
        GameObject firstObject = new GameObject("CanonicalBindingTestRespawnFirst");
        CanonicalBottleBinding first = firstObject.AddComponent<CanonicalBottleBinding>();
        Assert.IsTrue(first.TryApplyObservation(observation, out _, out _));
        int firstInstanceId = firstObject.GetInstanceID();
        UnityEngine.Object.DestroyImmediate(firstObject);

        GameObject secondObject = new GameObject("CanonicalBindingTestRespawnSecond");
        CanonicalBottleBinding second = secondObject.AddComponent<CanonicalBottleBinding>();
        Assert.IsTrue(second.TryApplyObservation(observation, out _, out _));
        Assert.AreNotEqual(firstInstanceId, secondObject.GetInstanceID());
        Assert.AreEqual(observation.Identity, second.Identity);
    }

    [Test]
    public void PhotonCapacityOverflowAndMalformedUnicodeAreRejectedWithoutTruncation()
    {
        CanonicalBottleObservation tooLong = Observation(
            source: new string('s', CanonicalBottlePhotonContract.SourceIdCapacity + 1));
        Assert.IsFalse(CanonicalBottlePhotonPayload.TryCreate(tooLong, out _, out string reason));
        Assert.AreEqual("SourceIdExceedsPhotonCapacity", reason);
        Assert.IsTrue(CanonicalBottlePhotonContract.Fits(
            string.Concat(Enumerable.Repeat("\U0001F9F4", CanonicalBottlePhotonContract.SourceIdCapacity)),
            CanonicalBottlePhotonContract.SourceIdCapacity));
        Assert.IsFalse(CanonicalBottlePhotonContract.Fits("\uD800", 64));
    }

    [Test]
    public void DefaultModeIsLegacyAndCanonicalSinkRejectsItWithoutFallback()
    {
        GameObject host = new GameObject("CanonicalBindingTestSpawner");
        host.SetActive(false);
        PhotonSharedBottleSpawner spawner = host.AddComponent<PhotonSharedBottleSpawner>();
        spawner.enableSpawnControls = false;
        Assert.AreEqual(BottleIdentityMode.Legacy, spawner.BottleIdentityMode);
        Assert.IsFalse(spawner.TryApplyCanonicalBottleObservation(
            Observation(lifecycle: CanonicalBottleLifecycle.Lost),
            out string reason));
        Assert.AreEqual("CanonicalModeInactiveNoLegacyFallback", reason);

        Assert.AreEqual(0, CanonicalBottleBinding.ActiveBindingCount);
        spawner.bottleIdentityMode = BottleIdentityMode.Canonical;
        Assert.IsFalse(spawner.HasDetectedBottleTrack(42));
        Assert.IsFalse(spawner.CanSpawnSharedBottle(out string legacyReason));
        Assert.AreEqual("CanonicalModeNoLegacySpawnFallback", legacyReason);
        UnityEngine.Object.DestroyImmediate(host);
    }
}
#endif
