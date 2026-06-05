using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ButtonClick : MonoBehaviour
{
    public GameObject showObject;

    public AudioClip audioClip1;
    public AudioSource audioSource;

    private void Start()
    {
        if (showObject != null)
        {
            showObject.SetActive(false);
        }
        else
        {
            return;
        }
        
    }

    public void ObjectOnOff()
    {
        if (showObject != null)
        {
            showObject.SetActive(!showObject.activeSelf);
        }
        else
        {
            return;
        }
    }

    public void AudioPlay()
    {
        audioSource.clip = audioClip1;
        audioSource.Play();
    }
}
