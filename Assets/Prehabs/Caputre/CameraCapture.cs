using MixedReality.Toolkit.UX;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MixedReality.Toolkit.SpatialManipulation;
using MixedReality.Toolkit.Input;
using Microsoft.MixedReality.OpenXR;
using static MixedReality.Toolkit.SpatialManipulation.ObjectManipulator;
using UnityEngine.XR.Interaction.Toolkit;
using System.Runtime.Serialization;
using System.Reflection;
using System.Collections;
using UnityEngine;

public class CameraCapture : MonoBehaviour
{
    public ImagePublisher3 imagePublisher3; // ImagePublisher3 スクリプトを参照
    public Renderer displayRenderer; // 映像を表示するオブジェクトの Renderer

    private RenderTexture renderTexture;
    private Texture2D capturedTexture;
    private bool isCapturing = false;

    void Start()
    {
        // RenderTexture の初期化
        renderTexture = new RenderTexture(760, 540, 24, RenderTextureFormat.ARGB32);
        renderTexture.Create();

        // Texture2D の初期化（デフォルトサイズを設定）
        capturedTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);

        StartCoroutine(CaptureImageAndSetTexture());
    }

    private IEnumerator CaptureImageAndSetTexture()
    {
        var camera = Camera.main;  // ← 修正ここ！




        while (true)
        {
            yield return new WaitForSeconds(1 / 30f); // 30FPSでキャプチャ

            if (!isCapturing)
            {
                StartCoroutine(CaptureFrame(camera));
            }
        }
    }
    private IEnumerator CaptureFrame(Camera camera)
    {
        isCapturing = true;

        // RenderTexture への描画
        camera.targetTexture = renderTexture;
        camera.Render();
        camera.targetTexture = null;

        // GPU から CPU へのデータ転送
        RenderTexture.active = renderTexture;
        capturedTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        capturedTexture.Apply();
        RenderTexture.active = null;

        // 画像データを ImagePublisher3 に設定
        if (imagePublisher3 != null)
        {
            imagePublisher3.texture = capturedTexture;
        }

        // Renderer にテクスチャを表示
        if (displayRenderer != null)
        {
            displayRenderer.material.mainTexture = capturedTexture;
        }

        isCapturing = false;

        // フレームレート調整のためにスリープを追加
        yield return new WaitForSeconds(1 / 30f);
    }


    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            renderTexture = null;
        }

        if (capturedTexture != null)
        {
            Destroy(capturedTexture);
            capturedTexture = null;
        }
    }
}



