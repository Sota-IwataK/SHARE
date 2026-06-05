using UnityEngine;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

public class PointCloudWithSpheres : MonoBehaviour
{
    [Header("CSV")]
    public string fileName = "reachability_pointcloud.csv";


    public GameObject pointPrefab;           // ���̃v���n�u�iSphere�j
    public Gradient colorByScore;            // �X�R�A �� �F�ϊ�
    public float sphereScale = 0.02f;        // ���̃T�C�Y

    [Header("��������")]
    public int batchSize = 1000;             // �t���[�������`��

    // �A�[�����{��̃I�t�Z�b�g
    // const float baseOffsetX = -0.123f;
    // const float baseOffsetY = 0.0f;
    // const float baseOffsetZ = -0.056f;

    const float baseOffsetX = -0f;
    const float baseOffsetY = 0.0f;
    const float baseOffsetZ = -0.0f;


    IEnumerator Start()
    {
        if (pointPrefab == null)
        {
            UnityEngine.Debug.LogError("pointPrefab�i���̃v���n�u�j���ݒ肳��Ă��܂���B");
            yield break;
        }

        // CSV�ǂݍ���
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        string csv = "";
#if UNITY_ANDROID && !UNITY_EDITOR
        var www = new UnityEngine.Networking.UnityWebRequest(path);
        www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
        yield return www.SendWebRequest();
        csv = www.downloadHandler.text;
#else
        csv = File.ReadAllText(path);
#endif

        var lines = csv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        float minS = lines.Min(l => float.Parse(l.Split(',')[3]));
        float maxS = lines.Max(l => float.Parse(l.Split(',')[3]));

        // �e�I�u�W�F�N�g
        GameObject parent = new GameObject("PointCloudSpheres");
        parent.transform.SetParent(this.transform);

        for (int i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            float rx = float.Parse(parts[0]) + baseOffsetX;
            float ry = float.Parse(parts[1]) + baseOffsetY;
            float rz = float.Parse(parts[2]) + baseOffsetZ;
            float score = float.Parse(parts[3]);

            Vector3 pos = new Vector3(-rx, rz, -ry);
            float t = Mathf.InverseLerp(minS, maxS, score);
            Color col = colorByScore.Evaluate(t);

            var go = Instantiate(pointPrefab, Vector3.zero, Quaternion.identity, parent.transform);
            go.transform.localScale = Vector3.one * sphereScale;

            //  ���S�����킹�邽�߁A���a���������ɕ␳
            float radius = sphereScale * 0.5f;
            go.transform.position = pos - Vector3.up * radius;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = colorByScore.Evaluate(t);
            }
    

            if (i % batchSize == 0)
                yield return null;
        }

        UnityEngine.Debug.Log($"{lines.Length}�̋��𐶐����܂����B");
        //  �Ō�ɋ����I�� localPosition ���[���ɐݒ�
        Transform parentTransform = transform.Find("PointCloudSpheres");
        if (parentTransform != null)
        {
            parentTransform.localPosition = Vector3.zero;
        }

    }
}
