using UnityEngine;

public class MockPointCloudSubscriber : MonoBehaviour
{
    [Header("Mock Mode (Use for testing without ROS)")]
    public bool useMockData = false;

    [SerializeField] private int mockSize = 10;           // 幅、高さ、奥行きを共通化
    [SerializeField] private float mockSpacing = 0.1f;

    private Vector3[] points;
    private Color[] colors;

    private MeshRenderer meshRenderer;
    private Material pointCloudMaterial;

    // Inspector で変更されたら、即座にポイントクラウドデータを更新
    private void OnValidate()
    {
        UpdatePointCloudData();
    }

    // ポイントクラウドデータの更新
    private void UpdatePointCloudData()
    {
        int size = mockSize;
        float spacing = mockSpacing;

        // 地面用の点群作成（y = 0）
        points = new Vector3[size * size];
        int index = 0;
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                // 地面の座標は y = 0 とし、x, z 座標で広げる
                points[index++] = new Vector3(x * spacing, 0, z * spacing);
            }
        }

        // 色も白で更新
        colors = new Color[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            colors[i] = Color.white;  // グラデーションなしで白に設定
        }

        // 壁用の点群作成（垂直の面を作る）
        int wallIndex = points.Length;
        Vector3[] tempPoints = new Vector3[points.Length + size * size];
        Color[] tempColors = new Color[colors.Length + size * size];

        // 既存の地面データをコピー
        points.CopyTo(tempPoints, 0);
        colors.CopyTo(tempColors, 0);

        // 壁1（Z方向の壁）
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                tempPoints[wallIndex++] = new Vector3(x * spacing, y * spacing, size * spacing);  // z = size の位置に壁を配置
                tempColors[wallIndex - 1] = Color.white; // 壁も白に設定
            }
        }

        points = tempPoints;
        colors = tempColors;

        // マテリアルを適用する
        ApplyMaterial();
    }

    // マテリアル適用
    private void ApplyMaterial()
    {
        // MeshRendererを取得または生成
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        // マテリアルを新しく作成し、カスタムシェーダーを設定
        if (pointCloudMaterial == null)
        {
            pointCloudMaterial = new Material(Shader.Find("Unlit/VertexColor"));
        }

        // 頂点カラーを反映するためにマテリアルに設定
        meshRenderer.material = pointCloudMaterial;
    }

    // 色を動的に変更するメソッド
    public void UpdatePointColor(int index, Color newColor)
    {
        if (index >= 0 && index < colors.Length)
        {
            colors[index] = newColor;
            ApplyMaterial();  // マテリアルを更新して変更を反映
        }
    }
    public Color GetPointColor(int index)
    {
        if (index >= 0 && index < colors.Length)
        {
            return colors[index];
        }
        else
        {
            Debug.LogWarning($"Invalid index: {index}");
            return Color.white; // エラー時は白色を返す
        }
    }


    public Vector3[] GetPCL()
    {
        return points;
    }

    public Color[] GetPCLColor()
    {
        return colors;
    }

    public Vector3 GetSize()
    {
        return new Vector3(mockSize, mockSize, mockSize);
    }
}


/*using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public class MockPointCloudSubscriber : MonoBehaviour
{
    private Vector3[] pcl;
    private Color[] pcl_color;
    private Vector2 size;

    public void LoadFromCSV(string filePath)
    {
        var pclList = new List<Vector3>();
        var colorList = new List<Color>();

        if (!File.Exists(filePath))
        {
            UnityEngine.Debug.LogError("CSV file not found at: " + filePath);
            return;
        }

        string[] lines = File.ReadAllLines(filePath);
        for (int i = 1; i < lines.Length; i++)  // Skip header
        {
            string[] tokens = lines[i].Split(',');
            if (tokens.Length >= 6 &&
                float.TryParse(tokens[0], out float x) &&
                float.TryParse(tokens[1], out float y) &&
                float.TryParse(tokens[2], out float z) &&
                int.TryParse(tokens[3], out int r) &&
                int.TryParse(tokens[4], out int g) &&
                int.TryParse(tokens[5], out int b))
            {
                pclList.Add(new Vector3(x, y, z));
                colorList.Add(new Color(r / 255f, g / 255f, b / 255f));
            }
        }

        pcl = pclList.ToArray();
        pcl_color = colorList.ToArray();
        size = new Vector2(Mathf.Sqrt(pcl.Length), Mathf.Sqrt(pcl.Length)); // 仮のサイズ推定

        UnityEngine.Debug.Log("Loaded point cloud from CSV with " + pcl.Length + " points.");
    }

    public Vector3[] GetPCL() => pcl;
    public Color[] GetPCLColor() => pcl_color;
    public Vector2 GetSize() => size;
}
*/