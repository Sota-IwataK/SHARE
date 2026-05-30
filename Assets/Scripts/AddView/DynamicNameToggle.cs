using UnityEngine;

public class DynamicNameToggle : MonoBehaviour
{
    public GameObject rootObject;
    public string childName;


    /// </summary>
    private Transform FindByName()
    {
        if (rootObject == null || string.IsNullOrEmpty(childName))
        {
            UnityEngine.Debug.LogError("rootObject ‚Ü‚½‚Í childName ‚ªİ’è‚³‚ê‚Ä‚¢‚Ü‚¹‚ñ");
            return null;
        }
        // q‚àŠÜ‚ß‘S‚Ä‚Ì Transform ‚ğ—ñ‹“‚µ‚Ä–¼‘O”äŠr
        var transforms = rootObject.GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            if (t.name == childName)
            {
                return t;
            }
        }
        UnityEngine.Debug.LogError($"'{childName}' ‚ª {rootObject.name} ˆÈ‰º‚ÉŒ©‚Â‚©‚è‚Ü‚¹‚ñ");
        return null;
    }

    public void ToggleActiveByName()
    {
        var target = FindByName();
        if (target == null) return;
        bool next = !target.gameObject.activeSelf;
        target.gameObject.SetActive(next);
        //Debug.Log($"{target.name} active -> {next}");
    }


    public void ActivateByName()
    {
        var target = FindByName();
        if (target == null) return;
        target.gameObject.SetActive(true);
        //Debug.Log($"{target.name} set active = true");
    }


    public void DeactivateByName()
    {
        var target = FindByName();
        if (target == null) return;
        target.gameObject.SetActive(false);
        //Debug.Log($"{target.name} set active = false");
    }
}
