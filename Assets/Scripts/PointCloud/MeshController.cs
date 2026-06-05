using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class MeshController : MonoBehaviour
{
    public PointCloudSubscriber subscriber;

    Mesh mesh;
    MeshRenderer meshRenderer;
    MeshFilter mf;
    public Material _material;
    // The size, positions and colours of each of the pointcloud
    public float pointSize = 1f;

    [SerializeField] private BoxCollider BoundingBox;

    [Header("MAKE SURE THESE LISTS ARE MINIMISED OR EDITOR WILL CRASH")]
    private Vector3[] positions = new Vector3[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0) };
    private Color[] colours = new Color[] { new Color(1f, 0f, 0f), new Color(0f, 1f, 0f) };
    private Vector2 size;


    public Transform offset; // Put any gameobject that faciliatates adjusting the origin of the pointcloud in VR. 

    public Material pointCloudMaterial;
    public Texture2D pointTexture; // ポイントの見た目を指定するテクスチャ
    private bool Meshstatus = false;
    void Start()
    {
        pointCloudMaterial = new Material(Shader.Find("Custom/PointCloudShader"));
        pointCloudMaterial.SetTexture("_MainTex", pointTexture);

        // ポイントのサイズを調整するための変数
        float pointSize = 1000f; // 適切な値に調整してください
        pointCloudMaterial.SetFloat("_PointSize", pointSize);

        // Give all the required components to the gameObject
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        mf = gameObject.AddComponent<MeshFilter>();
        meshRenderer.material = new Material(_material);
        //meshRenderer.material = new Material(Shader.Find("Custom/PointCloudShader"));
        mesh = new Mesh
        {
            // Use 32 bit integer values for the mesh, allows for stupid amount of vertices (2,147,483,647 I think?)
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        //transform.position = offset.position;
        //transform.rotation = new Quaternion(offset.rotation.x,offset.rotation.y - 180,offset.rotation.z,offset.rotation.w);

    }

    IEnumerator UpdateMesh()
    {
        //positions = subscriber.pcl;
        positions = subscriber.GetPCL();
        colours = subscriber.GetPCLColor();
        size = subscriber.GetSize();

        //List<int> indices = new List<int>();
        //Debug.Log(size);
        if (positions == null)
        {
            yield return null;
        }
        yield return null;

        mesh.Clear();
        mesh.vertices = positions;
        mesh.colors = colours;


        if (positions != null&& Meshstatus == false)
        {
            Meshstatus = true;
            var indices = GetTriangleIndiceList();
            mesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);

            //mf.mesh = mesh;

            Vector3 center = Vector3.zero;
            foreach (Vector3 position in positions)
            {
                center += position;
            }
            center /= positions.Length;
            //center.x = 0f;
            //center.y = 0.135f;

            // Calculate bounding box
            float minx = float.MaxValue;
            float miny = float.MaxValue;
            float minz = float.MaxValue;

            float maxx = float.MinValue;
            float maxy = float.MinValue;
            float maxz = float.MinValue;

            foreach (Vector3 position in positions)
            {
                minx = Mathf.Min(minx, position.x);
                miny = Mathf.Min(miny, position.y);
                minz = Mathf.Min(minz, position.z);
                maxx = Mathf.Max(maxx, position.x);
                maxy = Mathf.Max(maxy, position.y);
                maxz = Mathf.Max(maxz, position.z);
            }

            BoundingBox.center = -center;

            BoundingBox.size = new Vector3(Mathf.Abs(minx) + Mathf.Abs(maxx), Mathf.Abs(miny) + Mathf.Abs(maxy), (Mathf.Abs(minz) + Mathf.Abs(maxz)) / 2);

            //Debug.Log("Center: " + center);
            //Debug.Log("MinX: " + minx);
            //Debug.Log("MinY: " + miny);
            //Debug.Log("MaxX: " + maxx);
            //Debug.Log("MaxY: " + maxy);

            //PointCLoudToMesh(positions);

        }

    }
    private void PointCLoudToMesh(Vector3[] positions)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> indices = new List<int>();
        // 点群密度の閾値（適宜調整してください）
        float densityThreshold = 0.01f;

        // 頂点のインデックスを管理する辞書
        Dictionary<Vector3, int> vertexDictionary = new Dictionary<Vector3, int>();

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 point = positions[i];

            // 頂点のインデックスを取得または追加
            int vertexIndex;
            if (vertexDictionary.ContainsKey(point))
            {
                vertexIndex = vertexDictionary[point];
            }
            else
            {
                vertexIndex = vertices.Count;
                vertices.Add(point);
                vertexDictionary.Add(point, vertexIndex);
            }

            // 頂点座標が (0, 0, 0) の場合はポリゴンを生成しない
            if (point == Vector3.zero)
            {
                continue;
            }
            // 密度解析用のカウンタと距離計算用の変数を初期化
            int neighborCount = 0;
            float totalDistance = 0f;

            // 点群間の距離を計算して密度を解析
            for (int j = 0; j < positions.Length; j++)
            {
                if (i == j)
                    continue;

                Vector3 neighbor = positions[j];
                float distance = Vector3.Distance(point, neighbor);

                if (distance < densityThreshold)
                {
                    // 密度が閾値以下の点をカウント
                    neighborCount++;
                    totalDistance += distance;
                }
            }

            // 密度が閾値以上の場合にトライアングルを生成
            if (neighborCount >= 3)
            {
                // 三角形インデックスを追加
                indices.Add(vertexIndex);
            }
        }

        // Meshの作成
        mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.RecalculateNormals();
        mesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);

        // Meshの更新
        mf.mesh = mesh;

    }


    // triangleのindiceを取得する関数
    private List<int> GetTriangleIndiceList()
    {
        int width = (int)size.x;
        int height = (int)size.y;
        List<int> indicateList = new List<int>();

        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int index = y * width + x;
                int a = index;
                int b = index + 1;
                int c = index + width;
                int d = index + width + 1;

                indicateList.Add(a);
                indicateList.Add(b);
                indicateList.Add(c);
                indicateList.Add(c);
                indicateList.Add(b);
                indicateList.Add(d);
            }
        }
        return indicateList;
    }

    public List<int> GetTriangleIndiceList1(Vector3[] positions)
    {
        int width = (int)size.x;
        int height = (int)size.y;

        List<int> indicateList = new List<int>();

        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int index = y * width + x;
                int a = index;
                int b = index + 1;
                int c = index + width;
                int d = index + width + 1;

                bool isVaridA = positions[a].magnitude != 0;
                bool isVaridB = positions[b].magnitude != 0;
                bool isVaridC = positions[c].magnitude != 0;
                bool isVaridD = positions[d].magnitude != 0;
                //Debug.Log("A : " + isVaridA + "B : " + isVaridB + "C : " + isVaridC + "D : " + isVaridD);
                if (isVaridA & isVaridB & isVaridC)
                {
                    indicateList.Add(a);
                    indicateList.Add(b);
                    indicateList.Add(c);
                }

                if (isVaridC & isVaridB & isVaridD)
                {
                    indicateList.Add(c);
                    indicateList.Add(b);
                    indicateList.Add(d);
                }
            }
        }
        return indicateList;
    }

    // Update is called once per frame
    void Update()
    {
        //transform.position = offset.position;
        //transform.rotation = new Quaternion(offset.rotation.x, offset.rotation.y - 180, offset.rotation.z, offset.rotation.w);
        meshRenderer.material.SetFloat("_PointSize", pointSize);
        //UpdateMesh();
        StartCoroutine("UpdateMesh");
    }
}
