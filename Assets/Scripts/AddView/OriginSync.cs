using UnityEngine;
using System.Collections.Generic;

public class OriginSync : MonoBehaviour
{

    public Transform parentA;
    public Transform parentB;

    // Origin → SubOrigin の対応辞書
    private Dictionary<GameObject, GameObject> originToSub = new Dictionary<GameObject, GameObject>();


    void Start()
    {
        if (parentA == null || parentB == null)
        {
            //UnityEngine.Debug.Log("ParentAまたはParentBが設定されていません！");
            return;
        }

        List<GameObject> masterList = GetOrderedChildrenWithTag(parentA, "Origin");
        List<GameObject> subList = GetOrderedChildrenWithTag(parentB, "SubOrigin");
      
    }

    public void SetParentB(Transform newParentB)
    {
        parentB = newParentB;

        // 並び順に従って取得
        List<GameObject> masterList = GetOrderedChildrenWithTag(parentA, "Origin");
        List<GameObject> subList = GetOrderedChildrenWithTag(parentB, "SubOrigin");


        originToSub.Clear();

        int pairCount = Mathf.Min(masterList.Count, subList.Count);
        for (int i = 0; i < pairCount; i++)
        {
            GameObject master = masterList[i];
            GameObject sub = subList[i];

            originToSub[master] = sub;

        }

    }


    public Dictionary<GameObject, GameObject> GetMasterToSubMapping()
    {
        return originToSub;
    }


    void Update()
    {
        // マスターbottleの色やアウトラインをサブbottleに反映
        foreach (var entry in originToSub)
        {
            GameObject masterOrigin = entry.Key;
            GameObject subOrigin = entry.Value;

            if (masterOrigin != null && subOrigin != null)
            {
              

                // マスターの位置と回転をサブに反映
                Transform masterParent = masterOrigin.transform.parent;
                Vector3 masterLocalPosition = masterParent.InverseTransformPoint(masterOrigin.transform.position);
                Quaternion masterLocalRotation = Quaternion.Inverse(masterParent.rotation) * masterOrigin.transform.rotation;

                // サブの位置と回転を同期
                subOrigin.transform.localPosition = masterLocalPosition;
                subOrigin.transform.localRotation = masterLocalRotation;
            }
        }
    }

    public GameObject GetMasterFromSub(GameObject sub)
    {
        foreach (var kvp in originToSub)
        {
            if (kvp.Value == sub)
                return kvp.Key;
        }
        return null;
    }


    // 指定された親オブジェクトの子オブジェクトの中から特定のタグを持つものを取得
    private List<GameObject> GetOrderedChildrenWithTag(Transform parent, string tag)
    {
        List<GameObject> result = new List<GameObject>();

        foreach (Transform child in parent.GetComponentsInChildren<Transform>())
        {
            if (child != parent && child.CompareTag(tag))  // 自分自身は除く
            {
                result.Add(child.gameObject);
            }
        }

        return result;
    }

    public bool TryGetSubFromMaster(GameObject master, out GameObject sub)
    {
        return originToSub.TryGetValue(master, out sub);
    }

    

}
