using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;

namespace RosSharp.RosBridgeClient
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class InverseReachMapSubscriber : UnitySubscriber<Float32MultiArray>
    {
        [Tooltip("スコアに応じたグラデーション")]
        public Gradient colorByScore;

        // アーム根本基準のオフセット（PointCloudLoader と同じ）
        const float baseOffsetX = -0.123f;
        const float baseOffsetY = 0.0f;
        const float baseOffsetZ = -0.056f;

        private Mesh mesh;
        private List<Vector3> vertices = new List<Vector3>();
        private List<Color> colors = new List<Color>();

        // スレッドセーフにデータ受け渡し
        private float[] latestData;
        private bool dataReceived = false;
        private object dataLock = new object();

        protected override void Start()
        {
            base.Start();
            // MeshFilter／Renderer の準備
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var mf = gameObject.GetComponent<MeshFilter>();
            mf.mesh = mesh;
            var mr = gameObject.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Custom/PointCloudShader1"));
        }

        // ROS からデータを受け取るコールバック
        protected override void ReceiveMessage(Float32MultiArray message)
        {
            lock (dataLock)
            {
                latestData = message.data;
                dataReceived = true;
            }
        }

        void Update()
        {
            if (!dataReceived)
                return;

            float[] dataCopy;
            lock (dataLock)
            {
                dataCopy = latestData;
                dataReceived = false;
            }

            int count = dataCopy.Length / 4;
            vertices.Clear();
            colors.Clear();

            // スコアの min/max を算出
            float minScore = float.MaxValue;
            float maxScore = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float s = dataCopy[i * 4 + 3];
                if (s < minScore) minScore = s;
                if (s > maxScore) maxScore = s;
            }

            // 各ポイントを座標変換・カラー評価
            for (int i = 0; i < count; i++)
            {
                float rx = dataCopy[i * 4 + 0] + baseOffsetX;
                float ry = dataCopy[i * 4 + 1] + baseOffsetY;
                float rz = dataCopy[i * 4 + 2] + baseOffsetZ;
                float score = dataCopy[i * 4 + 3];

                // (-rx, rz, -ry) の変換
                Vector3 v = new Vector3(-rx, rz, -ry);
                vertices.Add(v);

                float t = Mathf.InverseLerp(minScore, maxScore, score);
                colors.Add(colorByScore.Evaluate(t));
            }

            // メッシュに反映
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(Enumerable.Range(0, vertices.Count).ToArray(),
                            MeshTopology.Points, 0);
        }
    }
}
