using UnityEngine;
using RosSharp.RosBridgeClient;
using std_msgs = RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{
    /// <summary>
    /// 任意オブジェクトの Z 軸回転角（ラジアン）を Float64 メッセージで配信します。
    /// </summary>
    public class ZRotationPublisher : UnityPublisher<std_msgs.Float64>
    {
        [Header("Rotation Source")]
        [Tooltip("回転角を取得するオブジェクト")]
        public Transform targetObject;

     

        private std_msgs.Float64 message;

        protected override void Start()
        {
            // ① base.Start() が RosConnector からトピックを Advertise してくれる
            base.Start();

            // ② メッセージインスタンスを生成
            message = new std_msgs.Float64();
        }

        private void FixedUpdate()
        {
            // ③ Z 軸回転角を取得・正規化・ラジアン変換
            float zDeg = targetObject.rotation.eulerAngles.z;
            if (zDeg > 180f) zDeg -= 360f;
            double zRad = zDeg * Mathf.Deg2Rad;

            // ④ メッセージにセットして Publish
            message.data = zRad;
            Publish(message);
        }
    }
}
