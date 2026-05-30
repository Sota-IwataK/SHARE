using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{
    // Int32 メッセージを受け取って bottle_id フィールドに格納するサブスクライバ
    public class DetectBottleSubscriber : UnitySubscriber<Int32>
    {
        // 受信したボトルIDを格納する変数
        public int bottle_id =-1;
    

        protected override void Start()
        {
            base.Start();
        }

        // メッセージ受信時に呼ばれるコールバック
        protected override void ReceiveMessage(Int32 message)
        {
            // Int32 型メッセージのデータは message.data に入っている
            bottle_id = message.data;
            //Debug.Log($"[DetectBottleSubscriber] Received bottle_id = {bottle_id}");
        }
    }
}
