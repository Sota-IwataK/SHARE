using UnityEngine;

public class ActiveSync : MonoBehaviour
{
    public GameObject source;   // AFŠÄ‹‘ÎÛ
    public GameObject target;   // BF“¯Šúæ

    private bool previousState;

    void Start()
    {
        if (source != null)
        {
            previousState = source.activeSelf;
            target?.SetActive(previousState);
        }
    }

    void Update()
    {
        if (source == null || target == null) return;

        if (source.activeSelf != previousState)
        {
            previousState = source.activeSelf;
            target.SetActive(previousState);
        }
    }
}
