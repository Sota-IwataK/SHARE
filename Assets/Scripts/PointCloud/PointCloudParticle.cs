using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;


public class PointCloudParticle : MonoBehaviour
{

    public PointCloudSubscriber subscriber;

    public Material _material;
    public float pointSize = 100f;

    private Vector3[] positions = new Vector3[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0) };
    private Color[] colours = new Color[] { new Color(1f, 0f, 0f), new Color(0f, 1f, 0f) };

    public Transform offset;
    public Texture2D pointTexture;

    public ParticleSystem pointCloudParticleSystem; // Particle SystemをアタッチしたGameObject

    private void Start()
    {
        pointCloudParticleSystem = GetComponent<ParticleSystem>();

        Material pointCloudMaterial = new Material(Shader.Find("Custom/PointCloudShader"));
        pointCloudMaterial.SetTexture("_MainTex", pointTexture);

        float adjustedPointSize = 1000f; // 適切な値に調整してください
        pointCloudMaterial.SetFloat("_PointSize", adjustedPointSize);

        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        meshRenderer.material = new Material(_material);

        Mesh mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
    }

    private void Update()
    {
        UpdatePointCloud();
    }

    private void UpdatePointCloud()
    {
        positions = subscriber.GetPCL();
        colours = subscriber.GetPCLColor();

        if (positions != null)
        {
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                particles[i].position = positions[i];
               
                particles[i].startSize = pointSize;
            }

            pointCloudParticleSystem.SetParticles(particles, particles.Length);
        }
        else
        {
            Debug.Log("null");
        }
    }
}