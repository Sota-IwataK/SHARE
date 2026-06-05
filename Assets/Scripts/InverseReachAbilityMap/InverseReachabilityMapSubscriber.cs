using System.Collections.Generic;
using System.Linq;
using RosMessageTypes.Std;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class InverseReachMapSubscriber : RosTcpSubscriber<Float32MultiArrayMsg>
{
    [Tooltip("Score color gradient")]
    public Gradient colorByScore;

    private const float BaseOffsetX = -0.123f;
    private const float BaseOffsetY = 0.0f;
    private const float BaseOffsetZ = -0.056f;

    private Mesh mesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();

    private float[] latestData;
    private bool dataReceived;
    private readonly object dataLock = new object();

    protected override void Start()
    {
        base.Start();
        mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        var meshFilter = gameObject.GetComponent<MeshFilter>();
        meshFilter.mesh = mesh;
        var meshRenderer = gameObject.GetComponent<MeshRenderer>();
        meshRenderer.material = new Material(Shader.Find("Custom/PointCloudShader1"));
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
        vertices.Clear();
        colors.Clear();

        float minScore = float.MaxValue;
        float maxScore = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            float score = dataCopy[i * 4 + 3];
            if (score < minScore) minScore = score;
            if (score > maxScore) maxScore = score;
        }

        for (int i = 0; i < count; i++)
        {
            float rx = dataCopy[i * 4 + 0] + BaseOffsetX;
            float ry = dataCopy[i * 4 + 1] + BaseOffsetY;
            float rz = dataCopy[i * 4 + 2] + BaseOffsetZ;
            float score = dataCopy[i * 4 + 3];

            vertices.Add(new Vector3(-rx, rz, -ry));

            float t = Mathf.InverseLerp(minScore, maxScore, score);
            colors.Add(colorByScore.Evaluate(t));
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetIndices(Enumerable.Range(0, vertices.Count).ToArray(), MeshTopology.Points, 0);
    }
}
