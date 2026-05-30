using UnityEngine;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(ParticleSystem))]
public class PointCloudAsParticles : MonoBehaviour
{
    public string fileName = "reachability_pointcloud.csv";
    public Gradient colorByScore;
    public float pointSize = 0.02f;   // ワールド単位のサイズ

    const float baseOffsetX = -0.123f;
    const float baseOffsetY = 0.0f;
    const float baseOffsetZ = -0.056f;

    IEnumerator Start()
    {
        // CSV 読み込み
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        string csv = File.ReadAllText(path);
        var lines = csv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        // スコアの min/max
        float minS = float.MaxValue, maxS = float.MinValue;
        foreach (var l in lines)
        {
            var s = float.Parse(l.Split(',')[3]);
            minS = Mathf.Min(minS, s);
            maxS = Mathf.Max(maxS, s);
        }

        // パーティクルセットアップ
        var ps = GetComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = lines.Length;
        main.startSize = pointSize;

        var particles = new ParticleSystem.Particle[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            float rx = float.Parse(parts[0]) + baseOffsetX;
            float ry = float.Parse(parts[1]) + baseOffsetY;
            float rz = float.Parse(parts[2]) + baseOffsetZ;
            float score = float.Parse(parts[3]);

            Vector3 pos = new Vector3(-rx, rz, -ry);
            float t = Mathf.InverseLerp(minS, maxS, score);
            Color col = colorByScore.Evaluate(t);

            particles[i].position = pos;
            particles[i].startColor = col;
            particles[i].startSize = pointSize;
        }

        ps.SetParticles(particles, particles.Length);
        ps.Play();
        yield return null;
    }
}
