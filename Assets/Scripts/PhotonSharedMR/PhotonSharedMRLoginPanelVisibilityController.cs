using UnityEngine;

[DisallowMultipleComponent]
public class PhotonSharedMRLoginPanelVisibilityController : MonoBehaviour
{
    [SerializeField] private GameObject loginPanelRoot;
    [SerializeField] private GameObject loginCanvasRoot;

    public GameObject LoginPanelRoot => loginPanelRoot;
    public GameObject LoginCanvasRoot => loginCanvasRoot;

    public void SetTargets(GameObject panelRoot, GameObject canvasRoot)
    {
        loginPanelRoot = panelRoot;
        loginCanvasRoot = canvasRoot;
    }

    public void Close()
    {
        if (loginPanelRoot == null)
        {
            Debug.LogWarning("[SHARE-MR] Login panel root is missing; hide skipped.");
            return;
        }

        loginPanelRoot.SetActive(false);
        Debug.Log("[SHARE-MR] Login panel hidden.");
    }

    public void Open()
    {
        if (loginCanvasRoot != null)
        {
            loginCanvasRoot.SetActive(true);
        }

        if (loginPanelRoot == null)
        {
            Debug.LogWarning("[SHARE-MR] Login panel root is missing; show skipped.");
            return;
        }

        loginPanelRoot.SetActive(true);
        Debug.Log("[SHARE-MR] Login panel shown.");
    }

    public void Toggle()
    {
        if (loginPanelRoot == null)
        {
            Debug.LogWarning("[SHARE-MR] Login panel root is missing; toggle skipped.");
            return;
        }

        if (loginPanelRoot.activeSelf)
        {
            Close();
        }
        else
        {
            Open();
        }
    }
}
