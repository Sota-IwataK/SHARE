using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RosMessageTypes.Sensor;
using UnityEngine;

public class PointCloudSubscriber : RosTcpSubscriber<PointCloud2Msg>
{
    private byte[] byteArray;
    private bool isMessageReceived;
    private int size;

    private Vector3[] pcl;
    private Color[] pclColor;
    public Color _color = Color.white;

    private int width;
    private int height;
    private int pointStep;

    private readonly List<Vector3> pclList = new List<Vector3>();
    private readonly List<Color> pclColorList = new List<Color>();

    public void Update()
    {
        if (!isMessageReceived) return;

        UnityEngine.Debug.Log(width);
        StartCoroutine(PointCloudRendering());
        isMessageReceived = false;
    }

    protected override void ReceiveMessage(PointCloud2Msg message)
    {
        size = message.data.GetLength(0);
        byteArray = message.data;

        width = (int)message.width;
        height = (int)message.height;
        UnityEngine.Debug.Log("width : " + width + " height : " + height);

        pointStep = (int)message.point_step;
        size /= pointStep;
        isMessageReceived = true;
    }

    private IEnumerator PointCloudRendering()
    {
        pclList.Clear();
        pclColorList.Clear();

        for (int n = 0; n < size; n++)
        {
            int xPos = n * pointStep + 0;
            int yPos = n * pointStep + 4;
            int zPos = n * pointStep + 8;

            float x = BitConverter.ToSingle(byteArray, xPos);
            float y = BitConverter.ToSingle(byteArray, yPos);
            float z = BitConverter.ToSingle(byteArray, zPos);

            pclList.Add(new Vector3(x, z, y));
            pclColorList.Add(_color);
        }

        pcl = pclList.ToArray();
        pclColor = pclColorList.ToArray();

        yield return null;
    }

    public void SavePointCloudToCSV(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
        {
            UnityEngine.Debug.LogError("Directory does not exist: " + directory);
            Directory.CreateDirectory(directory);
        }

        if (!HasWritePermission(path))
        {
            UnityEngine.Debug.LogError("No write permission: " + path);
            return;
        }

        if (pcl == null || pclColor == null)
        {
            UnityEngine.Debug.LogWarning("No point cloud data to save.");
            return;
        }

        using (StreamWriter writer = new StreamWriter(path))
        {
            writer.WriteLine("x,y,z,r,g,b");
            for (int i = 0; i < pcl.Length; i++)
            {
                Vector3 point = pcl[i];
                Color color = pclColor[i];
                int r = Mathf.RoundToInt(color.r * 255);
                int g = Mathf.RoundToInt(color.g * 255);
                int b = Mathf.RoundToInt(color.b * 255);
                writer.WriteLine($"{point.x},{point.y},{point.z},{r},{g},{b}");
            }
        }

        UnityEngine.Debug.Log("Point cloud saved to: " + path);
    }

    public void SavePointCloudFromButton()
    {
        string folder = "C:/PointCloudSaved";
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "pointcloud.csv");
        SavePointCloudToCSV(path);
    }

    private bool HasWritePermission(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write)) { }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void UpdatePointColor(int index, Color newColor)
    {
        if (pclColor != null && index >= 0 && index < pclColor.Length)
            pclColor[index] = newColor;
    }

    public void UpdateAllColors(Color newColor)
    {
        if (pclColor == null) return;

        for (int i = 0; i < pclColor.Length; i++)
            pclColor[i] = newColor;
    }

    public Vector3[] GetPCL()
    {
        return pcl;
    }

    public Color[] GetPCLColor()
    {
        return pclColor;
    }

    public Vector2 GetSize()
    {
        return new Vector2(width, height);
    }
}
