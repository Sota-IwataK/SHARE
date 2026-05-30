using RosSharp.RosBridgeClient.MessageTypes.Nav;
using System.Collections;
using System.Collections.Generic;
using UnityEngine; 

namespace RosSharp.RosBridgeClient
{
    public class IRMRouteSubscriber : UnitySubscriber<Path>
    {
        public Path messagePath;
        public bool isDirty = false;

        private List<GameObject> instantiatedObjects = new List<GameObject>(); // ���������I�u�W�F�N�g���Ǘ�

        protected override void Start()
        {
            base.Start();
        }

        protected override void ReceiveMessage(Path message)
        {
            messagePath = message;
            Debug.Log("Received IRMPath Message");
            isDirty = true; // ← 新規受信の印

        }

        
    }
}
