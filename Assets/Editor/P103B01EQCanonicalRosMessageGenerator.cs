#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public static class P103B01EQCanonicalRosMessageGenerator
{
    private const string EnvironmentVariable = "SHARE_CANONICAL_BOTTLE_OBSERVATION_MSG";
    private const string WindowsSource =
        @"C:\Users\23044\shear_ws_ros_lf\src\share_semantic_interfaces\msg\CanonicalBottleObservation.msg";
    private const string ExpectedSha256 =
        "CE1AA87C58C507655F1C11844287C54C691A0D8BF70E91DC63D9BB03F3989846";

    [MenuItem("SHARE/P1-03B-01E-Q/Generate Canonical Bottle Observation Message")]
    public static void Generate()
    {
        string source = ResolveSource();
        VerifySchema(source);
        string outputRoot = Path.Combine(Application.dataPath, "RosMessages");
        List<string> warnings = MessageAutoGen.GenerateSingleMessage(
            source,
            outputRoot,
            verbose: true);
        for (int i = 0; i < warnings.Count; i++)
        {
            Debug.LogWarning("[P1-03B-01E-Q MessageGeneration] " + warnings[i]);
        }

        string generatedPath = MessageAutoGen.GetMessageClassPath(source, outputRoot);
        VerifyGeneratedSurface(generatedPath);
        AssetDatabase.Refresh();
        Debug.Log("[P1-03B-01E-Q] FORMAL_MESSAGE_GENERATION_COMPLETE source="
            + source + " output=" + generatedPath);
    }

    private static string ResolveSource()
    {
        string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        string projectSibling = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "..",
            "shear_ws",
            "src",
            "share_semantic_interfaces",
            "msg",
            "CanonicalBottleObservation.msg"));
        string[] candidates = { fromEnvironment, WindowsSource, projectSibling };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(candidates[i]) && File.Exists(candidates[i]))
            {
                return Path.GetFullPath(candidates[i]);
            }
        }

        throw new FileNotFoundException(
            "Canonical ROS schema missing. Set " + EnvironmentVariable
            + " to the authoritative CanonicalBottleObservation.msg path.");
    }

    private static void VerifySchema(string source)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(source))
        {
            string actual = BitConverter.ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty);
            if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Canonical ROS schema SHA256 mismatch: " + actual);
            }
        }
    }

    private static void VerifyGeneratedSurface(string generatedPath)
    {
        string generated = File.ReadAllText(generatedPath);
        string[] required =
        {
            "class CanonicalBottleObservationMsg",
            "public ulong object_id;",
            "public ulong observation_sequence;",
            "BuiltinInterfaces.TimeMsg observation_stamp;",
            "MessageRegistry.Register(k_RosMessageName, Deserialize);"
        };
        for (int i = 0; i < required.Length; i++)
        {
            if (generated.IndexOf(required[i], StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Generated message surface mismatch: missing " + required[i]);
            }
        }
    }
}
#endif
