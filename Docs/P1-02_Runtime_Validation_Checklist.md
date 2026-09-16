# P1-02 Runtime Validation Checklist

## Common setup and evidence

- Use the same build, Photon AppId/region, room name, calibration data, and ROS topics on both clients.
- Label logs `Q1`, `Q2`, `ROS`, and record UTC test start/end time, build commit, device serial, and role/participant ID.
- For every state observation log: object type/id, `source_timestamp_type`, `source_timestamp`, `shared_timestamp`, `sequence`, StateAuthority, InputAuthority, local PlayerRef, Master Client PlayerRef.
- Preserve Unity player logs, ROS bag/topic capture, Photon statistics export, and Profiler `.data` files.
- Do not invoke any Shared Team State API from AMIR command publishers.

## T1 Photon 2 Client connection

- Operation: Start Q1, join the test room, then start Q2 and join the same room. Keep both connected for 60 seconds.
- Observations: room/session, active player count, PlayerRefs, Master Client, avatar NetworkIds, SharedTeamStateSceneObject NetworkId and StateAuthority.
- PASS: both report two players; exactly one shared scene state object exists; its StateAuthority is the same Master Client on both; both avatars remain visible and interactive.
- On FAIL suspect: `PhotonFusionSharedRoomBootstrap`, scene object registration/bake, room/region mismatch, duplicate NetworkRunner, NetworkProjectConfig.
- Required logs: join/start result, runner inventory, `OnPlayerJoined`, avatar spawn, scene object `Spawned`, authority inventory.

## T2 sequence consistency

- Operation: On Master, perform at least 20 explicit target/ownership/task-phase test updates, one update at a time. Capture both clients after each acknowledged sequence.
- Observations: full `SharedControlState` and serialized hash on Q1/Q2 for each sequence.
- PASS: for every sequence observed by both clients, target, owner type/id, phase and shared timestamp are byte-equivalent; sequence is strictly increasing with no duplicate payload conflict.
- On FAIL suspect: multiple scene state objects, unauthorized writer, stale read, logger sampling race, scene object authority mismatch.
- Required logs: before/after state, writer PlayerRef, StateAuthority, sequence, shared timestamp, state hash on both clients.

## T3 TaskPhase synchronization

- Operation: Manually set `UNKNOWN → INDEPENDENT → PREPARING → READY → TRANSFER → RELEASED`; do not enable automatic transitions.
- Observations: phase, sequence and shared timestamp on both clients; AMIR command topic activity.
- PASS: phases arrive in requested order with matching sequence/payload; no phase changes without the manual test action; no ROS command is published.
- On FAIL suspect: enum serialization, non-Master writer, hidden UI callback/FSM, stale scene object.
- Required logs: phase event and complete control state on Q1/Q2; ROS command-topic capture proving no added command.

## T4 Ownership synchronization

- Operation: Manually set None, Human(Q1), Human(Q2), Robot(new AMIR), Robot(old AMIR), then None.
- Observations: owner type/id, target, phase, sequence and authority.
- PASS: both clients agree for each sequence; only Master scene object changes ownership; existing Bottle grab authority is unchanged.
- On FAIL suspect: owner ID mapping, control/Bottle ownership conflation, authority override, duplicate writer.
- Required logs: ownership request/result, full control state, Bottle StateAuthority/InputAuthority before and after.

## T5 client disconnect

- Operation: With Q1 Master and Q2 connected, disconnect the non-Master first; repeat with the current Master disconnected abruptly.
- Observations: avatar removal, connected state, scene state object survival, new Master/StateAuthority, last control sequence.
- PASS: departed avatar is removed/connected=false by absence; the dedicated scene object is not despawned; when Master leaves, authority moves to the new Master without control payload reset or timestamp/sequence regression.
- On FAIL suspect: `MasterClientObject` flag, `DestroyWhenStateAuthorityLeaves`, avatar-coupled registry, Fusion Master switch timing.
- Required logs: `OnPlayerLeft`, `Despawned`, old/new Master, scene object NetworkId and flags, pre/post state snapshot.

## T6 reconnect

- Operation: Rejoin the disconnected device with the same participant ID after 10 seconds; repeat after 60 seconds.
- Observations: new avatar NetworkId, participant ID uniqueness, current control snapshot, Robot connected/stale state.
- PASS: one active avatar per participant; rejoined client receives current team state without resetting sequence; existing MR interactions work.
- On FAIL suspect: stale static registry, duplicate participant, late scene synchronization, old PlayerRef ownership.
- Required logs: leave/rejoin timestamps, avatar inventory, scene state snapshot/hash, authority inventory.

## T7 Master Client transition

- Operation: Record state at sequence N; terminate Master; wait for new Master; issue one manual update from new Master.
- Observations: scene object NetworkId, StateAuthority, state at N, next sequence and timestamp.
- PASS: NetworkId and payload survive; StateAuthority changes once; next sequence is N+1 and timestamp is greater; former/non-Master writers are rejected.
- On FAIL suspect: scene-object flags, initialization running after migration, multiple Masters, monotonic timestamp handling.
- Required logs: Fusion Master change, authority change, N and N+1 snapshots on both clients, rejected writer result.

## T8 RobotState ROS connection

- Operation: Connect the designated receiver Client separately to new AMIR and old AMIR telemetry. Publish known pose/gripper samples with known Header stamps; also test a message type without Header.
- Observations: robot ID, pose, gripper, connected, timestamp type/value, shared timestamp, sequence, reporting client authority.
- PASS: only the corresponding receiver writes that robot; Header samples retain exact sec/nanosec as nanoseconds and type `RosHeader`; headerless samples record callback receive time with type `ReceiveTime`; shared timestamp and sequence correlate each distributed update; no joint-state array is distributed.
- On FAIL suspect: ROS adapter mapping, timestamp unit conversion, wrong receiver authority, missing event hook, robot ID collision.
- Required logs: raw ROS message/header, callback receive time, reported RobotState on sender/Q1/Q2, ROS connection events.

## T9 ROS stale/disconnect

- Operation: Stop pose messages while keeping TCP connected, resume, then close ROS TCP; repeat for Robot and detected Bottle source.
- Observations: age from recorded source/receive time, valid/connected transitions, last sequence, pose stability.
- PASS: configured stale threshold is reported without fabricated timestamps; disconnect is event-driven; last pose is not silently presented as fresh; resume increases sequence; no automatic command/handover occurs.
- On FAIL suspect: missing freshness policy/adapter, mixed time domains, stale flag not propagated, subscriber reconnection handling.
- Required logs: last raw ROS stamp, receive time, stale/disconnect/recover events, state snapshots and sequences.

## T10 new/old AMIR operation regression

- Operation: With Shared Team State OFF and ON, execute the approved manual MR operation script for Bottle manipulation, new AMIR control, and old AMIR control. Use identical operator steps and safety conditions.
- Observations: input response, ROS command topics/payload/rate, robot response, Bottle grab/target authority, errors and latency.
- PASS: command topic names, publishers, payloads and rates are unchanged; no Shared Team State command is emitted; all baseline operations complete without new authority conflict or UI obstruction.
- On FAIL suspect: scene/prefab component side effects, authority contention, unintended callback wiring, performance regression; compare command-topic captures first.
- Required logs: operator timeline, ROS command-topic capture OFF/ON, Photon authority logs, Unity exceptions, completion result.

## T11 Shared Team State OFF/ON performance

- Operation: Produce two development builds differing only by Shared Team State component enablement. On each Quest run the same 5-minute idle, two-user movement, Bottle manipulation, and ROS telemetry phases three times. Disable verbose logs for measurements.
- Observations: FPS, Main Thread frame time, CPU frame time, GPU frame time, GC Alloc/frame, Photon send/receive bytes/s and packets/s. Record median, P95, P99 and peak where applicable.
- PASS: no sustained FPS tier drop; no new recurring GC allocation in Shared Team State update paths; no unexplained traffic spike; team-state traffic matches configured event/5–10 Hz policy. Project performance budgets, if stricter, take precedence.
- On FAIL suspect: object enumeration/read frequency, logging string allocation, pose written every Fusion tick, duplicate state objects, profiler/logging overhead, excessive Networked field churn.
- Required logs: Unity Profiler `.data`, Profile Analyzer comparison, OVR/Meta metrics capture, Photon statistics export, build configuration and run phase markers.

## Result matrix

| Area | PASS evidence | Initial status before runtime |
|---|---|---|
| P1-02A Architecture | Dedicated MasterClient scene object; avatar lifecycle separation; no command path | PASS |
| P1-02B Implementation | Schema/API/prefab/scene review complete; command path unchanged | PASS |
| P1-02C Compile | Unity compile and Fusion Weaver success | PASS |
| P1-02D 2-Client Sync | T1–T7 evidence | BLOCKED |
| P1-02E ROS Integration | T8–T10 evidence | BLOCKED |
| P1-02F Performance | T11 OFF/ON evidence | BLOCKED |
