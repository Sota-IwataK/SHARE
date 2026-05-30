using MixedReality.Toolkit.SpatialManipulation;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class ObjectDuplicator : MonoBehaviour
{
    [Tooltip("複製したい元のオブジェクト")]
    public GameObject objectToDuplicate;

    [Tooltip("複製時に位置をどれだけずらすか")]
    public Vector3 offset = new Vector3(1, 0, 0);

    [Tooltip("複製時にどれだけ回転させるか (Euler 角度)")]
    public Vector3 rotationEuler = Vector3.zero;

    [Tooltip("複製時のスケール")]
    public Vector3 scale = Vector3.one;
    // youbotを一時的に非アクティブにしたリスト
    private List<GameObject> duplicatedYoubots = new List<GameObject>();
    [Tooltip("複製先の親オブジェクト（空のTransformなど）")]
    public Transform targetParent;


    public void DuplicateObject()
    {
        GameObject duplicatedObject = null; // ← ここで宣言しておく

        if (objectToDuplicate != null)
        {
            // Rotationは元の回転 + 指定回転
            Quaternion newRotation = objectToDuplicate.transform.rotation * Quaternion.Euler(rotationEuler);

            // 複製して新しい位置・回転で生成
            duplicatedObject = Instantiate(
                objectToDuplicate,
                objectToDuplicate.transform.position + offset,
                newRotation,
                targetParent // ← Inspectorで指定されたオブジェクト配下に生成される
            );

            duplicatedObject.name = objectToDuplicate.name + "_Copy";
            // 複製された BoundingBox 本体のコンポーネントを有効化
            var bc = duplicatedObject.GetComponent<BoundsControl>();
            if (bc != null) bc.enabled = true;

            var om = duplicatedObject.GetComponent<ObjectManipulator>();
            if (om != null) om.enabled = true;

            var col = duplicatedObject.GetComponent<BoxCollider>();
            if (col != null) col.enabled = true;

            // RotationAxisConstraint を無効化
            var rotationConstraint = duplicatedObject.GetComponent<RotationAxisConstraint>();
            if (rotationConstraint != null)
            {
                rotationConstraint.enabled = false;
            }
            // Scaleは指定したものを適用（元のスケールを使いたいなら objectToDuplicate.transform.localScale に変更可）
            duplicatedObject.transform.localScale = scale;

            // PointCloudRenderer の subscriber の設定をコピー
            var originalRenderer = objectToDuplicate.GetComponentInChildren<PointCloudRenderer>();
            var originalSubscriber = originalRenderer?.subscriber;

            var duplicatedRenderer = duplicatedObject.GetComponentInChildren<PointCloudRenderer>();
            if (duplicatedRenderer != null && originalSubscriber != null)
            {
                duplicatedRenderer.subscriber = originalSubscriber;
            }
            DeactivateWaypointsInHierarchy(duplicatedObject);

            
            HandleYoubotActivation(objectToDuplicate, duplicatedObject);
        }
        else
        {
            UnityEngine.Debug.LogWarning("objectToDuplicateが設定されていません！");
        }
        // ここで使えるようになる
        if (duplicatedObject != null)
        {
            DeactivateWaypointsInHierarchy(duplicatedObject);
            // Instantiate直後に複製したBoundingBoxの中のボトルのタグを変更
            Transform[] allChildren = duplicatedObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in allChildren)
            {
                if (child.CompareTag("bottle"))
                {
                    child.tag = "SubBottle"; // タグ変更
                    //UnityEngine.Debug.Log($"タグ変更: {child.name} → SubBottle");
                }
            }

            foreach (Transform child in duplicatedObject.GetComponentsInChildren<Transform>(true))
            {
                if (child.CompareTag("Origin"))
                {
                    child.tag = "SubOrigin";
                    //Debug.Log($"子オブジェクトタグ変更: {child.name} → SubOrigin");
                }
            }


            // RadialView制御スクリプトを追加
            var toggleScript = duplicatedObject.AddComponent<RadialViewToggleOnManipulation>();
            var manipulator = duplicatedObject.GetComponent<ObjectManipulator>();
            var radialView = duplicatedObject.transform.parent?.GetComponent<RadialView>();

            toggleScript.interactable = manipulator;
            toggleScript.radialView = radialView;

        }
        SetTopMostFirstActive_OthersInactive();
        ReactivateYoubots();
        //BottleSync に parentB を渡す
        var bottleSync = FindObjectOfType<BottleSync>();
        if (bottleSync != null)
          {
                bottleSync.SetParentB(duplicatedObject.transform); // ←親として登録
           }

        var OriginSync = FindObjectOfType<OriginSync>();
        if (OriginSync != null)
        {
            OriginSync.SetParentB(duplicatedObject.transform); // ←親として登録
        }


    }
    

    void SetTopMostFirstActive_OthersInactive()
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        List<GameObject> matches = new List<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (obj.name == "BoundingBoxWithTraditionalHandles(Clone)" && obj.hideFlags == HideFlags.None)
            {
                matches.Add(obj);
            }
        }

        if (matches.Count == 0)
        {
            UnityEngine.Debug.LogWarning("BoundingBoxWithTraditionalHandles(Clone) が見つかりませんでした！");
            return;
        }

        // 階層が浅い順にソートし、同じ深さなら見つかった順（=そのままの順）
        matches.Sort((a, b) =>
        {
            int depthA = GetHierarchyDepth(a.transform);
            int depthB = GetHierarchyDepth(b.transform);
            return depthA.CompareTo(depthB); // depth が浅いほど先に
        });

        // 最初の1つをアクティブ、それ以外を非アクティブ
        bool found = false;
        foreach (var obj in matches)
        {
            if (!found)
            {
                obj.SetActive(true);
                found = true;
                UnityEngine.Debug.Log($"アクティブ化: {obj.name} ({GetHierarchyPath(obj.transform)})");
            }
            else
            {
                obj.SetActive(false);
            }
        }
    }

    int GetHierarchyDepth(Transform t)
    {
        int depth = 0;
        while (t.parent != null)
        {
            depth++;
            t = t.parent;
        }
        return depth;
    }

    string GetHierarchyPath(Transform t)
    {
        List<string> path = new List<string>();
        while (t != null)
        {
            path.Insert(0, t.name);
            t = t.parent;
        }
        return string.Join("/", path);
    }
    void DeactivateWaypointsInHierarchy(GameObject root)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true); // 非アクティブな子も含めて取得
        foreach (Transform child in children)
        {
            if (child.name == "WayPoints")
            {
                child.gameObject.SetActive(false);
                UnityEngine.Debug.Log($"Waypointsを非アクティブ化: {GetHierarchyPath(child)}");
            }
        }
    }
    void HandleYoubotActivation(GameObject original, GameObject duplicated)
    {
        // 元オブジェクト内のyoubotを探す
        var originalYoubot = FindYoubotInHierarchy(original);
        var duplicatedYoubot = FindYoubotInHierarchy(duplicated);

        if (originalYoubot != null && duplicatedYoubot != null)
        {
            if (originalYoubot.activeSelf)
            {
                duplicatedYoubot.SetActive(false); // 一時的に無効化
                duplicatedYoubots.Add(duplicatedYoubot); // 後で有効化するために保存
                UnityEngine.Debug.Log($"複製先の youbot を非アクティブ化: {GetHierarchyPath(duplicatedYoubot.transform)}");
            }
        }
    }

    GameObject FindYoubotInHierarchy(GameObject root)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in children)
        {
            if (t.name == "youbot")
            {
                return t.gameObject;
            }
        }
        return null;
    }

    void ReactivateYoubots()
    {
        foreach (var youbot in duplicatedYoubots)
        {
            if (youbot != null)
            {
                youbot.SetActive(true);
                UnityEngine.Debug.Log($"youBot再アクティブ化: {GetHierarchyPath(youbot.transform)}");
            }
        }
        duplicatedYoubots.Clear();
    }


}
