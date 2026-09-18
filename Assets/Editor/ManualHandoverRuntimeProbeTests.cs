using NUnit.Framework;

public sealed class ManualHandoverRuntimeProbeTests
{
    [Test]
    public void DevelopmentGateRequiresDebugBuildAndExplicitFlag()
    {
        Assert.IsFalse(ManualHandoverRuntimeProbe.IsDevelopmentProbeEnabled(
            false, new[] { ManualHandoverRuntimeProbe.EnableArgument }));
        Assert.IsFalse(ManualHandoverRuntimeProbe.IsDevelopmentProbeEnabled(
            true, new[] { "-unrelated" }));
        Assert.IsTrue(ManualHandoverRuntimeProbe.IsDevelopmentProbeEnabled(
            true, new[] { ManualHandoverRuntimeProbe.EnableArgument }));
    }

    [TestCase("init-a|trial_01", ManualHandoverProbeCommand.InitializeCaseA, "trial_01")]
    [TestCase("request-duplicate", ManualHandoverProbeCommand.DuplicateRequest, "fallback")]
    [TestCase("ready_wrong_object", ManualHandoverProbeCommand.WrongObjectReady, "fallback")]
    [TestCase("request-wrong-claim", ManualHandoverProbeCommand.WrongClaimedParticipantRequest, "fallback")]
    public void CommandParserProducesExplicitCommand(
        string raw,
        ManualHandoverProbeCommand expected,
        string expectedToken)
    {
        Assert.IsTrue(ManualHandoverRuntimeProbe.TryParseCommand(
            raw, "fallback", out ManualHandoverProbeCommandEnvelope envelope));
        Assert.AreEqual(expected, envelope.Command);
        Assert.AreEqual(expectedToken, envelope.RunToken);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown-command")]
    public void CommandParserRejectsUnknownOrEmptyCommand(string raw)
    {
        Assert.IsFalse(ManualHandoverRuntimeProbe.TryParseCommand(
            raw, "fallback", out _));
    }

    [TestCase(ManualHandoverProbeCase.CaseA,
        SharedMRParticipantId.User1, SharedMRParticipantId.User2, 101, 202, 103020201UL)]
    [TestCase(ManualHandoverProbeCase.CaseB,
        SharedMRParticipantId.User2, SharedMRParticipantId.User1, 202, 101, 103020202UL)]
    public void VerificationSessionUsesFrozenRolesAndFullCanonicalIdentity(
        ManualHandoverProbeCase probeCase,
        SharedMRParticipantId giver,
        SharedMRParticipantId receiver,
        int giverRobot,
        int receiverRobot,
        ulong objectId)
    {
        Assert.IsTrue(ManualHandoverRuntimeProbe.TryCreateVerificationSession(
            probeCase, "trial-01", 1234UL, out ManualHandoverSession session));
        Assert.AreEqual(giver, session.RoleBinding.GiverParticipantId);
        Assert.AreEqual(receiver, session.RoleBinding.ReceiverParticipantId);
        Assert.AreEqual(giverRobot, session.RoleBinding.GiverRobotId);
        Assert.AreEqual(receiverRobot, session.RoleBinding.ReceiverRobotId);
        Assert.AreEqual("p103a-runtime-probe", session.TargetBottleKey.SourceId);
        StringAssert.Contains("trial-01", session.TargetBottleKey.SessionId);
        Assert.AreEqual(objectId, session.TargetBottleKey.ObjectId);
        Assert.AreEqual(ManualHandoverRuntimeProbe.CreatedSequence, session.CreatedSequence);
    }
}
