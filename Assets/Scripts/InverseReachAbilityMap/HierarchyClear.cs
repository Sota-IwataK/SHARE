using System.Collections.Generic;
using UnityEngine;

public class HierarchyClear : MonoBehaviour
{
    [Header("消したいオブジェクトが入っている親Transform")]
    public Transform parentContainer;

    [Header("個別に削除したい子オブジェクト名(部分一致でもOK)")]
    public List<string> targetNames = new List<string>();

    /// <summary>
    /// parentContainer の下から、targetNames にマッチする名前の子をすべて Destroy する
    /// </summary>
    [ContextMenu("Clear Selected Children")]
    public void ClearSelectedChildren()
    {
        if (parentContainer == null)
        {
            Debug.LogWarning("parentContainer が設定されていません");
            return;
        }

        // 子をまとめて消すので、一度リスト化してからループ
        var toDestroy = new List<Transform>();
        foreach (Transform child in parentContainer)
        {
            foreach (var name in targetNames)
            {
                if (child.name.Contains(name))
                {
                    toDestroy.Add(child);
                    break;
                }
            }
        }

        foreach (var t in toDestroy)
        {
            Destroy(t.gameObject);
        }
    }

    /// <summary>
    /// parentContainer の下にあるすべての子を Destroy する
    /// </summary>
    [ContextMenu("Clear All Children")]
    public void ClearAllChildren()
    {
        if (parentContainer == null)
        {
            Debug.LogWarning("parentContainer が設定されていません");
            return;
        }

        // 子をまとめて消すので、一度リスト化してからループ
        var toDestroy = new List<Transform>();
        foreach (Transform child in parentContainer)
            toDestroy.Add(child);

        foreach (var t in toDestroy)
            Destroy(t.gameObject);
    }
}
