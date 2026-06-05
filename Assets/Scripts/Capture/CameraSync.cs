using UnityEngine;

public class CameraSync : MonoBehaviour
{
    public GameObject mainCamera;  // Main Camera をインスペクタでアサイン

    void LateUpdate()
    {
        // Main Camera の位置と回転を同期
        transform.position = mainCamera.transform.position;
        transform.rotation = mainCamera.transform.rotation;
    }
}
