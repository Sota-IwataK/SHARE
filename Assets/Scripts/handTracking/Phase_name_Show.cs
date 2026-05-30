using UnityEngine;
using TMPro;
using RosSharp.RosBridgeClient;
using RosSharp.Urdf;
using System.Collections.Generic;
using RosSharp.RosBridgeClient.MessageTypes.Std;

public class Phase_name_Show : MonoBehaviour
{
    [Header("References")]
    public TMP_Text text;
    private Phase_name_Subscriber Phase_name_Subscriber;


    void Awake()
    {
        if (Phase_name_Subscriber == null)
            Phase_name_Subscriber = FindObjectOfType<Phase_name_Subscriber>();

        if (text == null)
            text = GetComponent<TMP_Text>();
    }

    void Update()
    {
        if (Phase_name_Subscriber == null) return;

         string name = Phase_name_Subscriber.phase_name;
        

        // テキスト表示
        if (text != null)
        {
            text.text = name;

        }
    }
}
