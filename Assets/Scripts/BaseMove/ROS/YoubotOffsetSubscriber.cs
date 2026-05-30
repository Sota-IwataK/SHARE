using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// /youbot/offset トピックを購読し、ROS ↔ Unity の座標系差を考慮して
/// OriginObject を初期位置からの移動量分だけ動かすコンポーネント。
/// </summary>
public class YoubotOffsetSubscriber : UnitySubscriber<Vector3Stamped>
{
    /*[Tooltip("購読する ROS トピック名 (例: /youbot/offset)")]
    public string Topic = "/youbot/offset";*/

    [Tooltip("移動量を適用する Origin オブジェクト")]
    public GameObject OriginObject;
  

    // Unity 上で最初に設定されていた OriginObject のローカル位置
    private UnityEngine.Vector3 unityStartPos;

    // ROS オフセット（Unity 用）の初期値（最初に受信した値）
    private UnityEngine.Vector3 rosUnityOffset0;
    private bool isRosInit = false;
    public UnityEngine.Vector3 BaseMovePosition;
    /// <summary>
    /// 初期化時に OriginObject の開始位置をキャプチャ
    /// </summary>
    protected override void Start()
    {
        base.Start();
        
    }

    /// <summary>
    /// ROS メッセージを受信したときに呼ばれる
    /// </summary>
    protected override void ReceiveMessage(Vector3Stamped message)
    {
        // ROS の右手系データを Unity 左手系に変換: x'=-x, y'=z, z'=y
        UnityEngine.Vector3 rosUnityOffset = new UnityEngine.Vector3(
            -(float)message.vector.x,
            (float)message.vector.z,
            -(float)message.vector.y
        );

      
        // OriginObject を移動指定
        BaseMovePosition = rosUnityOffset;
        
    }

}
