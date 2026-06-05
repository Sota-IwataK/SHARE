using UnityEngine;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class PointCloudLoader : MonoBehaviour
{
    public string fileName = "reachability_pointcloud.csv";
    public Gradient colorByScore;

    [Header("点サイズ")]
    [Range(1, 200)]
    public float pointSize = 20f;

    // アーム根本基準のオフセット
    const float baseOffsetX = -0.123f;
    const float baseOffsetY = 0.0f;
    const float baseOffsetZ = -0.056f;

    IEnumerator Start()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, fileName);
        string fileContents = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        var www = new UnityEngine.Networking.UnityWebRequest(filePath);
        www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
        yield return www.SendWebRequest();
        fileContents = www.downloadHandler.text;
#else
        fileContents = File.ReadAllText(filePath);
#endif

        var lines = fileContents.Split('\n');
        var vertices = new List<Vector3>();
        var colors = new List<Color>();

        float minScore = float.MaxValue, maxScore = float.MinValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            float score = float.Parse(parts[3]);
            if (score < minScore) minScore = score;
            if (score > maxScore) maxScore = score;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            float rx = float.Parse(parts[0])+baseOffsetX;
            float ry = float.Parse(parts[1])+baseOffsetY;
            float rz = float.Parse(parts[2])+baseOffsetZ;
            float score = float.Parse(parts[3]);

   
            vertices.Add(new Vector3(
                -rx,
                 rz,
                -ry 
            ));

            float t = Mathf.InverseLerp(minScore, maxScore, score);
            colors.Add(colorByScore.Evaluate(t));
        }

        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetIndices(Enumerable.Range(0, vertices.Count).ToArray(), MeshTopology.Points, 0);

        var mf = gameObject.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        var mr = gameObject.AddComponent<MeshRenderer>();
        //mr.material = new Material(Shader.Find("Custom/PointCloudShader1"));

        var mat = new Material(Shader.Find("Custom/PointCloudShader1"));
        //var mat = new Material(Shader.Find("Custom/PointCloud"));


        mat.SetFloat("_PointSize", pointSize);
        mr.material = mat;
        yield return null;
    }
}
