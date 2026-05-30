using UnityEngine;
using RosSharp.RosBridgeClient;
using std_msgs = RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{
    /// <summary>
    /// Publishes a std_msgs/Bool recovery_flag topic continuously with the current state.
    /// </summary>
    public class RecoveryFlagPublisher : UnityPublisher<std_msgs.Bool>
    {
        private std_msgs.Bool message;
        [SerializeField]
        private bool currentState = false;

        protected override void Start()
        {
            base.Start();
            message = new std_msgs.Bool();
            message.data = currentState;
        }

        private void FixedUpdate()
        {
            Publish(message);
        }

        /// <summary>
        /// recovery_flag を常に false にセットして配信します
        /// </summary>
        public void SetRecoveryFlagFalse()
        {
            if (currentState != false)
            {
                currentState = false;
                message.data = currentState;
                Debug.Log("Recovery flag set to FALSE");
            }
        }

        /// <summary>
        /// （必要であれば）true にセットするメソッドも追加できます
        /// </summary>
        public void SetRecoveryFlagTrue()
        {
            if (currentState != true)
            {
                currentState = true;
                message.data = currentState;
                Debug.Log("Recovery flag set to TRUE");
            }
        }
    }
}
