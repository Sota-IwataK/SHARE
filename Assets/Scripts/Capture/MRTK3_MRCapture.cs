using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MRTK3_MRCapture : MonoBehaviour
{
    public RenderTexture renderTexture;
    private Texture2D texture;

    void Start()
    {
        texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
    }

    void Update()
    {
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;

        // JPGとしてバイトに変換（ROS送信用）
        byte[] bytes = texture.EncodeToJPG(75);

        // ROS送信関数に渡す
        // SendToROS(bytes);
    }
}