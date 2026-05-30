/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
// 1. 生成されたカスタムメッセージをusing
using RosSharp.RosBridgeClient.MessageTypes.ReachabilityMapVisualizer; // ←自動生成したC#クラスのnamespace



namespace RosSharp.RosBridgeClient
{
    public class TestSub : UnitySubscriber<WorkSpace>
    {
        [Header("Sphere Settings")]
        public GameObject pointPrefab;    // 球体プレハブ
        public Gradient colorByScore;     // スコア→色変換
        public float sphereScale = 0.02f; // 球体サイズ
        public int batchSize = 1000;      // フレーム分割数

        const float baseOffsetX = -0.123f;
        const float baseOffsetY = 0.0f;
        const float baseOffsetZ = -0.056f;

        private float[] latestData;
        private bool dataReceived = false;
        private object dataLock = new object();

        private GameObject parent;
        private List<GameObject> pool = new List<GameObject>();

        protected override void Start()
        {
            base.Start();
            parent = new GameObject("InverseReachMapSpheres");
            parent.transform.SetParent(transform, false);
            if (pointPrefab == null) Debug.LogError("pointPrefab が設定されていません。");
        }

        protected override void ReceiveMessage(WorkSpace message)
        {
            // WorkSpace型のデータ取り出し
            int count = message.WsSpheres.Length;

            float[] data = new float[count * 4];

            for (int i = 0; i < count; i++)
            {
                var sphere = message.WsSpheres[i];
                float x = (float)sphere.point.x;
                float y = (float)sphere.point.y;
                float z = (float)sphere.point.z;
                float score = (float)sphere.ri;

                data[i * 4 + 0] = x;
                data[i * 4 + 1] = y;
                data[i * 4 + 2] = z;
                data[i * 4 + 3] = score;
            }

            lock (dataLock)
            {
                latestData = data;
                dataReceived = true;
            }
        }


        void Update()
        {
            if (!dataReceived) return;

            float[] dataCopy;
            lock (dataLock)
            {
                dataCopy = latestData;
                dataReceived = false;
            }

            int count = dataCopy.Length / 4;

            // プールに必要分を確保
            if (pool.Count < count)
            {
                for (int i = pool.Count; i < count; i++)
                {
                    var go = Instantiate(pointPrefab, parent.transform);
                    go.transform.localScale = Vector3.one * sphereScale;
                    pool.Add(go);
                }
            }
            // 余剰オブジェクトは非アクティブ化
            for (int i = count; i < pool.Count; i++)
                pool[i].SetActive(false);

            StartCoroutine(UpdateSpheres(dataCopy, count));
        }

        private IEnumerator UpdateSpheres(float[] data, int count)
        {
            // スコアの min/max を算出
            float minScore = float.MaxValue, maxScore = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float s = data[i * 4 + 3];
                if (s < minScore) minScore = s;
                if (s > maxScore) maxScore = s;
            }

            for (int i = 0; i < count; i++)
            {
                var go = pool[i];
                go.SetActive(true);

                float rx = data[i * 4 + 0] + baseOffsetX;
                float ry = data[i * 4 + 1] + baseOffsetY;
                float rz = data[i * 4 + 2] + baseOffsetZ;
                float score = data[i * 4 + 3];

                Vector3 pos = new Vector3(-rx, rz, -ry);


                go.transform.localPosition = pos;

                float t = Mathf.InverseLerp(minScore, maxScore, score);
                var rend = go.GetComponent<Renderer>();
                if (rend != null)
                    rend.material.color = colorByScore.Evaluate(t);

                if (i % batchSize == 0)
                    yield return null;
            }
        }
    }
}
*/