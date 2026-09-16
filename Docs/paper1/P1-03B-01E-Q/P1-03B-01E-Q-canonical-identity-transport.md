# P1-03B-01E-Q Canonical Identity Transport / Unity Bottle Binding

監査起点はrepository `Sota-IwataK/SHARE`、branch
`feature/p103b-canonical-identity`、HEAD
`84ecd406aaeb7f11ac302b570e3e7cecc8138bfd`である。clone直後のtracked
working treeがcleanであることを確認してから変更した。

総合判定は **implementation complete / ROS validation PASS / Unity execution
HOLD / Q総合HOLD** とする。Unity EditorまたはC# toolchainがこのLinux環境にないため、
追加したUnity tests、message generator、Fusion Weaver、Editor compile、player buildは
まだ実行していない。未実行項目をPASSには数えない。

## 1. 確認した.msg

正本は`/home/dars-note-5070/iwata_ws/shear_ws/src/share_semantic_interfaces/msg`
の実ファイルである。

| Message | package / fields / dependency |
|---|---|
| `BottleFeatureSample` | `share_semantic_interfaces`; string、bool、uint8、uint64、float64。nested/array/Timeなし |
| `BottleSelectionFeature` | full key、observation metadata、`BottleFeatureSample[] samples` |
| `BottleSelectionFeatureArray` | participant/writer/snapshot metadata、`BottleSelectionFeature[] features` |
| `CanonicalBottleSelection` | participant/writer/decision metadata、full selected key、score/reason。nested/array/Timeなし |
| `CanonicalBottleObservation` | `source_id:string`, `session_id:string`, `object_id:uint64`, `lifecycle:uint8`, `observation_sequence:uint64`, `observation_stamp:builtin_interfaces/Time`, `observation_clock_domain:string`, `frame_id:string` |

Unity identity ingressに必要な正本は`CanonicalBottleObservation.msg`である。SHA-256は
`ce1aa87c58c507655f1c11844287c54c691a0d8bf70e91dc63d9bb03f3989846`。
`share_semantic_interfaces/CMakeLists.txt`と`package.xml`の
`builtin_interfaces` dependencyも確認した。topicは
`/share/bottles/canonical_observation`。live producerは存在せず、追加もしていない。

## 2. generated C# classes

`Assets/RosMessages/ShareSemanticInterfaces/msg/CanonicalBottleObservationMsg.cs`
をROS-TCP-Connector v0.7.1標準出力形式で追加した。

- namespace: `RosMessageTypes.ShareSemanticInterfaces`
- class: `CanonicalBottleObservationMsg`
- ROS name: `share_semantic_interfaces/CanonicalBottleObservation`
- `object_id`, `observation_sequence`: C# `ulong`
- stamp: `RosMessageTypes.BuiltinInterfaces.TimeMsg`
- registration: `MessageRegistry.Register(k_RosMessageName, Deserialize)`

`P103B01EQCanonicalRosMessageGenerator`は標準
`MessageAutoGen.GenerateSingleMessage`を呼び、上記正本SHAを検査してから生成する。
この環境ではUnity Editorを起動できないためgenerator自体の再実行はHOLDである。
Canonical feature messageは今回のconsumerではなくproducerもOFFなので、追加生成していない。

## 3. serialization結果

ROS CDR実行試験は`object_id=16,777,217`, `2^63+17`, `2^64-1`、
`observation_sequence=2^63+5`でbit-exactにPASSした。stamp、clock domain、frame、
source/sessionも一致した。隔離DDS transportも1件PASSした。

Unity CDR round-trip testには`16,777,217`と`ulong.MaxValue`、sequence
`ulong.MaxValue`、`Time(sec=int.MinValue,nanosec=999,999,999)`を含めた。
float/double中間変換はない。Standalone defineにも`ROS2`を追加し、ROS 2 Timeの
`sec:int32`を固定した。Unity test実行はEditor不在のためHOLDである。

## 4. Unity identity model

`CanonicalBottleIdentity`はimmutableな`SourceId`, `SessionId`, `ObjectId:ulong`を持ち、
equality/hashは3値すべてを使う。participant、TrackId、Photon IDを含めない。

`CanonicalBottleObservationMetadata`は`ObservationSequence:ulong`, exact ROS Time
`StampSec:int`/`StampNanosec:uint`, `ObservationClockDomain`, `FrameId`を保持する。
`CanonicalBottleObservation`はidentity、lifecycle、metadataをatomicにまとめ、poseを
持たない。`CanonicalBottlePhotonPayload`も同じ値をlosslessに保持する。

## 5. subscriber

`CanonicalBottleObservationSubscriber`を追加した。Canonical modeだけでtyped ROS topicを
subscribeし、mapper、sequence router、`PhotonSharedBottleSpawner` sinkへ渡す。
unknown lifecycle、空source/session/clock/frame、null stamp、nanosecond範囲外をrejectする。
receive timeやUnity current timeでstampを置換しない。

`RuntimeInitializeOnLoadMethod`は既存componentを先に検索し、duplicateをdisableする。
`SubsystemRegistration`でstatic stateをresetし、scene reload時の二重subscriberを防ぐ。
Scene configuratorにもcomponent追加・個数検証を追加した。Build sceneは
`Assets/Scenes/main.unity`がenabledであり、component未serialized時も唯一のspawnerへ
runtime bootstrapされる。

## 6. GameObject binding

`CanonicalBottleBinding`をGameObject componentとして追加した。static registryは
`Dictionary<CanonicalBottleIdentity, CanonicalBottleBinding>`であり、受信したfull keyから
直接bindingする。pose、nearest neighbour、TrackIdを経由しない。

同一full keyの別GameObjectは`DuplicateFullKeyAlreadyBound`でrejectする。同一sequenceで
同一payloadはduplicate ignore、同一sequenceで内容が違う場合はconflicting duplicate、
古いsequenceはrejectする。source/session/object/sequence/stamp/clock/frame/lifecycle/validは
Inspector fieldとlogで追跡できる。

## 7. Photon transport

`NetworkedSharedSceneObject`へ次のNetworked stateを追加した。

- `NetworkString<_64>` source、`NetworkString<_128>` session
- `ulong` object ID、`ulong` observation sequence
- lifecycle、`int` stamp sec、`uint` stamp nanosec
- `NetworkString<_64>` clock domain、`NetworkString<_128>` frame

Fusion同梱XMLで`NetworkString`がfixed-size UTF-32であり、`Set`がcapacity超過時に
`false`を返してtrimする仕様を確認した。Unicode scalar数を事前検査し、`Set`の結果も
検査する。超過時はpayload全体をrejectし、silent truncationしない。

spawn callbackでcanonical observationをNetworkObjectへatomicに設定する。remote peerは
network stateから同じbindingを復元する。`NetworkObject.Id`はpayloadに含めず、canonical
identityに使わない。canonical objectのlegacy `DetectedBottleTrackId`は常に`-1`である。

## 8. session handling

`(A,S1,42)`と`(A,S2,42)`はdictionary上の別entryである。tracker restart後に同じnumeric
object IDが再利用されても旧sessionへbindingしない。旧session objectは`LOST`でinvalid
として保持し、`REMOVED`でdespawn/registry removalする。新sessionの出現だけでは旧sessionを
推測cleanupしない。

## 9. sequence

ordering ledgerはfull keyごとに管理する。`new > last`はaccept、`==`かつpayload同一は
ignore、`==`かつpayload相違はreject、`<`はrejectする。sinkが失敗した場合はledgerを
commitせず、同じobservationを再試行できる。sequenceをtimestampの代用にしない。

## 10. stamp/frame

ROS sourceの`sec:int32`、`nanosec:uint32`、clock domain、frameをmessageからdomain、
GameObject binding、Photon stateまで保持する。Unity receive time、current time、推測frame、
frame名だけの座標変換は行わない。canonical observationはposeを持たず、初回のUnity表示
位置は既存spawn anchorをdisplay-onlyに使う。Spatial Authorityには昇格しない。

## 11. mode gate

`BottleIdentityMode.Legacy|Canonical`を追加し、defaultはLegacyとした。

- Legacy: 既存TrackId pose/sync経路。canonical observation sinkはreject。
- Canonical: typed canonical subscriberとfull-key辞書だけをidentity authorityにする。
  TrackId spawn/update/despawn、manual legacy spawnは明示reject。

Canonical sourceがない場合もTrackIdへfallbackしない。既存`BottleFeaturePublisher`は
Canonical modeで生成されず、modeがruntime変更された場合も停止する。canonical feature
publisherは実装・有効化していない。

## 12. test結果

- ROS package regression: **299 tests / 0 failures / 0 errors / 37 intentional skips**
  （262 passed）。
- isolated DDS canonical observation: **1 passed / 7 deselected**。robot topicなし。
- Unity NUnit: **16 cases authored**。A/B/C/D/G/H/N/O/R/S/T/U/V、capacity overflow、
  CDR round-trip、transactional commitを含む。**Editor不在のため未実行**。
- static contract audit: **28 checks PASS**。
- C# delimiter/preprocessor balanceと`git diff --check`: PASS。

## 13. Unity build結果

**HOLD / 未実行。** Project versionはUnity `2022.3.62f2`、Build sceneは
`Assets/Scenes/main.unity`、ROS-TCP-Connectorはv0.7.1、Fusionはproject同梱版である。
このLinux環境には`Unity`, `unity-editor`, `unityhub`, `dotnet`, `csc`, `mcs`がなく、
Unity import、NUnit、Fusion Weaver、Windows/Android player buildを実行できない。
source-level verificationをUnity compile成功の代用にはしない。

## 14. 3視点レビュー

**正確性:** full tuple、64-bit、ROS Time、clock/frame、session isolation、sequence ruleを
atomicに保持する。object-only lookup、participant key、TrackId変換、receive-time補完はない。

**利用者目線:** subscriber logとbinding Inspectorでsource/session/object/sequence/
stamp/clock/frame/lifecycleを同時に追える。reject reasonはmode、capacity、authority、sequence、
duplicateを区別する。

**リスク:** source-levelで抑えたのはTrackId混入、float精度喪失、object-only join、Photon
truncate、sequence reorder、duplicate binding、mode fallback、duplicate subscriberである。
残るmaterial riskはUnity compile/Fusion Weaver未実行、実Photon peer間round-trip未実行、
実ROS-to-Unity callback未実行である。

## 15. canonical readiness gate再評価

Qの実装項目1〜14はsource上で完了したが、完了条件2〜4、9、15、16に必要なUnity実行証拠が
未取得なので **Q総合HOLD** とする。canonical feature producer readinessも
**0/10 fully satisfied、HOLD、live enable不可**を維持する。

Identity Authorityは **A2維持**、Spatial Authorityは **B3維持**、geometryは
**HOLD維持**。今回のidentity-only transportでauthority levelを上げない。

## 16. 未確定事項

- Unity標準generatorを実Editorで再実行した結果とimport/compile結果
- Unity NUnit 16 casesの実行結果
- Fusion Weaverと2 peer spawn/update/LOST/REMOVED/respawnの実行結果
- Windows/Android player build結果
- 現行Unity featureの意味・周期・clock（今回も未確認のまま。producerはOFF）
- live canonical observation producerとROS-to-Unity runtime callback

禁止されたgeometry、BottleNearEE、holds_object、held object commit、TOI/LocalHold live切替、
camera calibration、robot commandには進んでいない。

## 17. 次工程

Windows Unity 2022.3.62f2で次を順に実行する。

1. menu `SHARE/P1-03B-01E-Q/Generate Canonical Bottle Observation Message`
2. Unity Test Runnerで`CanonicalBottleIdentityTransportTests`
3. Fusion Weaverを含むEditor compile
4. isolated ROS publisherからUnity subscriberへのfull tuple確認
5. 2 peerでspawn/update/LOST/REMOVED/respawnとNetworkObject ID非依存を確認
6. Windows/Android build

全実行証拠が揃うまでLegacyをdefaultとし、canonical feature producerをOFFに保つ。
