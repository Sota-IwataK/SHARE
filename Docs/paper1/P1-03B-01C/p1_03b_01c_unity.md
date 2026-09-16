# P1-03B-01C Unity Integration

## Architecture

`ROS LocalHoldState -> typed LocalHoldStateIngress -> LocalHoldRosMapper -> LocalHoldState DTO -> PairSemanticStateReducer -> SharedPairSemanticStateNetwork -> SharedTeamState`

Unity is the state/FSM authority. Photon distributes compact semantic state only. Existing palm, gripper, base, robot-command, and teleoperation paths are unchanged.

## Formal ROS Binding

Status: **PASS**.

- Source: `C:\Users\23044\shear_ws_ros_lf\src\share_semantic_interfaces\msg\LocalHoldState.msg`
- Source commit: `48336aeb2185cd321fe99e59d06050f179352c9d`
- Source SHA256: `3AE5244682F82FAE65E56DFEE0E61975D5CADAB1EB47AB191F3C01B9FF365CB3`
- Generator: ROS-TCP-Connector v0.7.1 `MessageAutoGen.GenerateSingleMessage`
- Generated path: `Assets/RosMessages/ShareSemanticInterfaces/msg/LocalHoldStateMsg.cs`
- Namespace/class: `RosMessageTypes.ShareSemanticInterfaces.LocalHoldStateMsg`
- ROS name: `share_semantic_interfaces/LocalHoldState`
- Registration: generated `Register` method calls `MessageRegistry.Register`

The generated class was not edited or copied from an existing class.

## Topic and Lifecycle

The relative topic is `local_hold_state`. The existing symbolic participant mapping produces `/share/users/user_1/local_hold_state` or `/share/users/user_2/local_hold_state`; no per-user absolute topic constants or numeric enum casts are used. The scene ingress subscribes once after local avatar identity is ready using `ROSConnection.Subscribe<LocalHoldStateMsg>` and unsubscribes on disable.

## Mapper and PairSemanticState

`LocalHoldRosMapper` preserves every formal field. Compact semantic fields enter the existing DTO/reducer. Evidence and diagnostic strings/arrays stay in the ROS-only `LocalHoldRosSnapshot` and are not Photon network fields. UInt64 sequence/timestamps and Int64 object IDs remain lossless. Unknown/Unassigned identity and empty sessions are rejected. Invalid state clears hold/candidate/held semantics in the existing reducer.

## Session Policy

The existing Blocker 2 policy remains: strictly increasing sequence per session; fresh-session switch rejection; stale-gated unseen-session restart including sequence zero; four retired sessions; rollback rejection; independent User1/User2 state. Source timestamps are provenance only; freshness uses Unity/Fusion receive time and tick. Session changes do not reset TaskPhase or Ownership.

## Photon and Authority

Only compact validity, hold, candidate/held IDs and validity, session, sequence, timestamps, receive metadata, and stale state are replicated. Raw ROS diagnostics are not synchronized. State mutation remains guarded by `Object.HasStateAuthority`. `holds_object`, task Ownership, and Photon StateAuthority remain separate concepts.

## Tests and Compile

The automated verifier covers U1-U8, stale behavior, Blocker 2 session behavior, topic resolution, generated ROS contract, all-field mapping, UInt64/Int64 extremes, participant rejection, invalid clearing, and typed-message-to-existing-reducer sequence enforcement.

- Automated verifier: `AUTOMATED_VERIFICATION_PASS cases=16`
- Unity 2022.3.62f2 compile errors: 0
- Fusion Weaver errors: 0
- MessageGeneration errors: 0
- Existing unrelated warnings remain

## Scene Integrity

`SharedTeamStateSceneObject` contains one `SharedPairSemanticStateNetwork` and one `LocalHoldStateIngress`. Unity opened and saved the scene without serialization errors or missing scripts.

## Existing Control Path Impact

No palm/hand tracking, arm/base/gripper command, ROS command router, robot controller, bottle interaction, or teleoperation source was changed for formal binding. TaskPhase and Ownership were not modified.

## Remaining Runtime Validation

Actual ROS-PC traffic, two-Quest synchronization, master/State Authority failover, disconnect/reconnect, runtime publisher restart, FPS/profiling, hardware binding, and calibration remain outside this software PASS.

## Next Step

Proceed to the P1-03A Formal FSM Specification Freeze. Hardware PASS and two-Quest Runtime PASS remain NO.
