#if UNITY_EDITOR && FUSION_WEAVER && FUSION2
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class P102SharedTeamStateSceneSetup
{
    private const string ScenePath = "Assets/Scenes/main.unity";
    private const string ObjectName = "SharedTeamStateSceneObject";

    [MenuItem("SHARE/P1-02/Ensure Shared Team State Scene Object")]
    public static void EnsureSceneObject()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject target = GameObject.Find(ObjectName);
        if (target == null)
        {
            target = new GameObject(ObjectName);
            SceneManager.MoveGameObjectToScene(target, scene);
        }

        NetworkObject networkObject = target.GetComponent<NetworkObject>();
        if (networkObject == null) networkObject = target.AddComponent<NetworkObject>();
        if (target.GetComponent<SharedTeamControlStateNetwork>() == null)
            target.AddComponent<SharedTeamControlStateNetwork>();
        if (target.GetComponent<SharedPairSemanticStateNetwork>() == null)
            target.AddComponent<SharedPairSemanticStateNetwork>();
        if (target.GetComponent<ManualHandoverSessionNetwork>() == null)
            target.AddComponent<ManualHandoverSessionNetwork>();
        if (target.GetComponent<LocalHoldStateIngress>() == null)
            target.AddComponent<LocalHoldStateIngress>();

        networkObject.Flags = (networkObject.Flags | NetworkObjectFlags.MasterClientObject)
            & ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves
            & ~NetworkObjectFlags.AllowStateAuthorityOverride;
        EditorUtility.SetDirty(target);
        EditorUtility.SetDirty(networkObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[P1-02] SharedTeamStateSceneObject ensured in " + ScenePath);
    }
}
#endif
