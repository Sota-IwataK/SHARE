using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{
    // Int32 メッセージを受け取って bottle_id フィールドに格納するサブスクライバ
    public class DetectBottle_score_subscriber : UnitySubscriber<Float32>
    {
        // 受信したボトルIDを格納する変数
        public float bottle_score ;
    

        protected override void Start()
        {
            base.Start();
        }

        // メッセージ受信時に呼ばれるコールバック
        protected override void ReceiveMessage(Float32 message)
        {
            bottle_score = message.data;
            
        }
    }
}
