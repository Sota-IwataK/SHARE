using System.Collections;
using System.Collections.Generic;
using RosMessageTypes.Std;
using UnityEngine;

public class SphereMapSubscriber : RosTcpSubscriber<Float32MultiArrayMsg>
{
    [Header("Sphere Settings")]
    public GameObject pointPrefab;
    public Gradient colorByScore;
    public float sphereScale = 0.02f;
    public int batchSize = 1000;

    private const float BaseOffsetX = 0.0f;
    private const float BaseOffsetY = 0.0f;
    private const float BaseOffsetZ = 0.0f;

    private float[] latestData;
    private bool dataReceived;
    private readonly object dataLock = new object();

    private GameObject parent;
    private readonly List<GameObject> pool = new List<GameObject>();

    protected override void Start()
    {
        base.Start();
        parent = new GameObject("InverseReachMapSpheres");
        parent.transform.SetParent(transform, false);
        if (pointPrefab == null) Debug.LogError("pointPrefab is not assigned.");
    }

    protected override void ReceiveMessage(Float32MultiArrayMsg message)
    {
        lock (dataLock)
        {
            latestData = message.data;
            dataReceived = true;
        }
    }

    private void Update()
    {
        if (!dataReceived) return;

        float[] dataCopy;
        lock (dataLock)
        {
            dataCopy = latestData;
            dataReceived = false;
        }

        int count = dataCopy.Length / 4;

        if (pool.Count < count)
        {
            for (int i = pool.Count; i < count; i++)
            {
                var point = Instantiate(pointPrefab, parent.transform);
                point.transform.localScale = Vector3.one * sphereScale;
                pool.Add(point);
            }
        }

        for (int i = count; i < pool.Count; i++)
            pool[i].SetActive(false);

        StartCoroutine(UpdateSpheres(dataCopy, count));
    }

    private IEnumerator UpdateSpheres(float[] data, int count)
    {
        float minScore = float.MaxValue;
        float maxScore = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            float score = data[i * 4 + 3];
            if (score < minScore) minScore = score;
            if (score > maxScore) maxScore = score;
        }

        for (int i = 0; i < count; i++)
        {
            var point = pool[i];
            point.SetActive(true);

            float rx = data[i * 4 + 0] + BaseOffsetX;
            float ry = data[i * 4 + 1] + BaseOffsetY;
            float rz = data[i * 4 + 2] + BaseOffsetZ;
            float score = data[i * 4 + 3];

            point.transform.localPosition = new Vector3(-rx, rz, -ry);

            float t = Mathf.InverseLerp(minScore, maxScore, score);
            var renderer = point.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = colorByScore.Evaluate(t);

            if (i % batchSize == 0)
                yield return null;
        }
    }
}
