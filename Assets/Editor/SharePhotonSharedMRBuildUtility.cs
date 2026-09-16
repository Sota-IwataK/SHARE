using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SharePhotonSharedMRBuildUtility
{
    private const string MainScenePath = "Assets/Scenes/main.unity";
    private const string BuildFolder = "Builds/PhotonSharedMR";
    private const string BuildPath = BuildFolder + "/SHAREPhotonSharedMR.exe";
    private const string AndroidBuildPath = BuildFolder + "/SHAREPhotonSharedMR.apk";

    public static void BuildAndroidDevelopmentPlayer()
    {
        PrepareBuildFolder();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { MainScenePath },
            locationPathName = AndroidBuildPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });

        BuildSummary summary = report.summary;
        Debug.Log("[SharePhotonSharedMRBuildUtility] Android build result=" + summary.result
            + " output=" + summary.outputPath + " sizeBytes=" + summary.totalSize
            + " errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings);
        EditorApplication.Exit(summary.result == BuildResult.Succeeded && summary.totalErrors == 0 ? 0 : 1);
    }

    public static void BuildWindowsSmokePlayer()
    {
        PrepareBuildFolder();

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        EditorBuildSettingsScene[] originalScenes = EditorBuildSettings.scenes;

        try
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log("[SharePhotonSharedMRBuildUtility] Build result=" + summary.result
                + " output=" + summary.outputPath
                + " sizeBytes=" + summary.totalSize
                + " errors=" + summary.totalErrors
                + " warnings=" + summary.totalWarnings);

            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
            {
                EditorApplication.Exit(1);
                return;
            }
        }
        finally
        {
            EditorBuildSettings.scenes = originalScenes;
        }

        EditorApplication.Exit(0);
    }

    private static void PrepareBuildFolder()
    {
        string projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
        string buildFolderFullPath = Path.GetFullPath(BuildFolder);
        if (!buildFolderFullPath.StartsWith(projectRoot, System.StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Refusing to clean build folder outside the project: " + buildFolderFullPath);
        }

        if (Directory.Exists(buildFolderFullPath))
        {
            Directory.Delete(buildFolderFullPath, true);
        }

        Directory.CreateDirectory(buildFolderFullPath);
    }
}
