/*
© Siemens AG, 2017-2018
Author: Dr. Martin Bischoff (martin.bischoff@siemens.com)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at
<http://www.apache.org/licenses/LICENSE-2.0>.
Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using RosMessageTypes.Geometry;
using UnityEngine;

public class PoseStampedSubscriber : RosTcpSubscriber<PoseStampedMsg>
{
    public Transform PublishedTransform;

    private Vector3 position;
    private Quaternion rotation;
    private bool isMessageReceived;

    private void Update()
    {
        if (isMessageReceived)
            ProcessMessage();
    }

    protected override void ReceiveMessage(PoseStampedMsg message)
    {
        position = Ros2Unity(GetPosition(message));
        rotation = Ros2Unity(GetRotation(message));
        isMessageReceived = true;
    }

    private void ProcessMessage()
    {
        if (PublishedTransform == null) return;

        PublishedTransform.position = position;
        PublishedTransform.rotation = rotation;
    }

    private static Vector3 GetPosition(PoseStampedMsg message)
    {
        return new Vector3(
            (float)message.pose.position.x,
            (float)message.pose.position.y,
            (float)message.pose.position.z);
    }

    private static Quaternion GetRotation(PoseStampedMsg message)
    {
        return new Quaternion(
            (float)message.pose.orientation.x,
            (float)message.pose.orientation.y,
            (float)message.pose.orientation.z,
            (float)message.pose.orientation.w);
    }

    private static Vector3 Ros2Unity(Vector3 vector)
    {
        return new Vector3(-vector.y, vector.z, vector.x);
    }

    private static Quaternion Ros2Unity(Quaternion quaternion)
    {
        return new Quaternion(quaternion.y, -quaternion.z, -quaternion.x, quaternion.w);
    }
}
