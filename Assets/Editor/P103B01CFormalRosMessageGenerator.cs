#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public static class P103B01CFormalRosMessageGenerator
{
    private const string Source = @"C:\Users\23044\shear_ws_ros_lf\src\share_semantic_interfaces\msg\LocalHoldState.msg";
    private const string ExpectedSha256 = "3AE5244682F82FAE65E56DFEE0E61975D5CADAB1EB47AB191F3C01B9FF365CB3";

    [MenuItem("SHARE/P1-03B-01C/Generate Formal LocalHoldState Message")]
    public static void Generate()
    {
        if (!File.Exists(Source)) throw new FileNotFoundException("Formal ROS schema missing", Source);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(Source))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Formal ROS schema SHA256 mismatch: " + actual);
        }

        string outputRoot = Path.Combine(Application.dataPath, "RosMessages");
        var warnings = MessageAutoGen.GenerateSingleMessage(Source, outputRoot, verbose: true);
        for (int i = 0; i < warnings.Count; i++) Debug.LogWarning("[P1-03B-01C MessageGeneration] " + warnings[i]);
        AssetDatabase.Refresh();
        Debug.Log("[P1-03B-01C] FORMAL_MESSAGE_GENERATION_COMPLETE output="
            + MessageAutoGen.GetMessageClassPath(Source, outputRoot));
    }
}
#endif
