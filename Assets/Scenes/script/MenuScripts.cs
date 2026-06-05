using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MenuScripts : MonoBehaviour
{
    public Rigidbody[] ResetObjects;

    private Vector3[] _initialPositions;

    // Start is called before the first frame update
    private void Start()
    {
        _initialPositions = new Vector3[ResetObjects.Length];
        for (int i = 0; i < ResetObjects.Length; i++)
        {
            _initialPositions[i] = ResetObjects[i].position;
        }
    }

    public void Reset()
    {
        for (int i = 0; i < ResetObjects.Length; i++)
        {
            ResetObjects[i].useGravity = false;
            ResetObjects[i].velocity = Vector3.zero;
            ResetObjects[i].position
                = Camera.main.transform.position + Camera.main.transform.rotation * _initialPositions[i];
        }
    }

    public void ApplicationExit()
    {
        Application.Quit();
    }
}

