# P1-03B-01E-Q Phase 6 final runtime evidence

- Date: 2026-09-16 (Asia/Tokyo)
- Verdict: PASS
- Branch: `feature/p103b-canonical-identity`
- HEAD: `15dd3c813215fd77bc866f662df2dc3c54f5b75d`
- Unity: `2022.3.62f2`
- ROS-TCP endpoint used at runtime: `192.168.11.46:10000`
- Topic: `/share/bottles/canonical_observation`
- Scope stopped at Phase 6. No Phase 7 work was performed.

## Ingress establishment

- The runtime harness recorded `targetHandshakeAndSubscription=True`, Canonical mode,
  Photon `Joined`, and the canonical feature publisher OFF before accepting the five
  messages.
- Windows TCP inspection while the harness was waiting showed an `Established`
  connection from `192.168.11.29` to `192.168.11.46:10000` owned by the Unity process.
- The ROS2 publisher independently reported a matching subscription before publishing
  message #1.
- The `rosGlobalError=True` value on the READY line is a known process-global flag set
  late by the cancelled prefab-default `.30` connection attempt. It is not scoped to
  the `.46` socket. The target handshake/subscription, established `.46` TCP socket,
  and all five actual callbacks establish the active transport independently.

## Five actual ROS messages

| # | Runtime disposition | Full key | Sequence | Stamp | Clock | Frame | Result |
|---|---|---|---:|---|---|---|---|
| 1 | `AcceptedNew` | `(A,S1,42)` | 1 | `100.100000001` | `ros_time` | `camera_color_optical_frame` | Exact |
| 2 | `AcceptedUpdate` | `(A,S1,42)` | 2 | `100.200000002` | `ros_time` | `camera_color_optical_frame` | Exact |
| 3 | `DuplicateIgnored` | `(A,S1,42)` | 2 | `100.200000002` | `ros_time` | `camera_color_optical_frame` | Exact duplicate observed |
| 4 | `ConflictingDuplicateRejected` | `(A,S1,42)` | 2 | `100.200000002` | `ros_time` | `camera_color_optical_frame_conflict` | Exact conflicting duplicate observed |
| 5 | `OlderRejected` | `(A,S1,42)` | 1 | `100.100000001` | `ros_time` | `camera_color_optical_frame` | Exact older observation observed |

ROS lifecycle value `0` was preserved as the generated/runtime enum value `Observed`.
No TrackId fallback or floating-point identity conversion was used.

## Commit and fail-closed checks

- `DuplicateIgnored` was reached in the production subscriber through a temporary
  semantic-neutral log. The router contract returns before sink application, recorded
  as `duplicateSinkBypass=RouterReturnedBeforeSink`.
- After both the conflicting duplicate and older observation, the router's latest
  accepted state remained `(A,S1,42)`, sequence 2, stamp `100.200000002`, clock
  `ros_time`, frame `camera_color_optical_frame`.
- The runtime record explicitly reports `conflictCommitted=False` and
  `oldCommitted=False`.
- Cleanup restored `BottleIdentityMode=Legacy`; the canonical subscriber reported
  `subscribed=False` and `noLegacyFallback=True`.
- Play Mode and the Unity Editor exited successfully.

## Temporary diagnostics and post-cleanup verification

- Temporary `P103B01EQPhase6CaptureHarness` source and `.meta` were deleted.
- The temporary `DuplicateIgnored` production log was reverted.
- `Assets/Resources/ROSConnectionPrefab.prefab`: no diff.
- `Assets/Scenes/main.unity`: no diff.
- Production code: no tracked diff.
- Unity post-cleanup compile: exit 0; no C# or assembly compile failure.
- `CanonicalBottleIdentityTransportTests`: 16 passed, 0 failed, 0 skipped.
- `git diff --check`: no output.
- Final `git status --short`: only the untracked evidence directory
  `Docs/paper1/P1-03B-01E-Q/evidence/windows-runtime-phase6/`.

## Evidence files

- `unity-phase6-full-capture-retry2-2026-09-16.log`
  - SHA-256: `8C042F1FC74BB06E8093CBE1D2946D25371F1CCC6EC72B922CA14F382A5B9D54`
  - READY: line 8210
  - messages #1-#5: lines 8501, 8562, 8598, 8634, 8670
  - latest state: line 8706
  - Legacy/unsubscribe: line 8740
  - harness success: line 8751
  - Editor exit: line 9027
- `unity-phase6-post-cleanup-compile-2026-09-16.log`
  - SHA-256: `3C642D40AD53A593D6263BFD56460182D8A2A64B6587DF6F6CF9D6713BF46284`
- `unity-phase6-post-cleanup-nunit.xml`
  - SHA-256: `0EA606A12DD4401257AB1B79B4F32F3465CA4797673CF25F406CEB7AAF839BB0`
- `unity-phase6-post-cleanup-nunit.log`
