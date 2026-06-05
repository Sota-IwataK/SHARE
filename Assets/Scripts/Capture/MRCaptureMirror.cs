using UnityEngine;

[RequireComponent(typeof(Camera))]
public class MRCaptureMirror : MonoBehaviour
{
    public RenderTexture renderTexture;
    private Camera cam;
    private Texture2D tex;

    void Start()
    {
        cam = GetComponent<Camera>();
        tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
    }

    void OnPostRender()
    {
        // カメラの描画結果を RenderTexture に Blit（コピー）
        Graphics.Blit(null, renderTexture);

        // RenderTexture から Texture2D へ読み取り
        RenderTexture.active = renderTexture;
        tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        // JPEGに変換してROS送信など
        byte[] jpg = tex.EncodeToJPG(75);
        // SendToROS(jpg);
    }
}
