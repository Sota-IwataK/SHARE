using System;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class SelectiveUnityLogFilter : ILogHandler
{
    private static readonly string[] SuppressedMarkers =
    {
        "[MetaXRFeature]",
        "[RosTopicProvider]",
        "[ROSTopicProvider]",
        "RosTopicProvider",
        "ROSTopicProvider",
        "ROS Topic Provider"
    };

    private readonly ILogHandler innerHandler;

    private SelectiveUnityLogFilter(ILogHandler innerHandler)
    {
        this.innerHandler = innerHandler;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InstallInEditor()
    {
        Install();
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallInPlayer()
    {
        Install();
    }

    private static void Install()
    {
        ILogHandler currentHandler = Debug.unityLogger.logHandler;
        if (currentHandler is SelectiveUnityLogFilter || IsLegacyFilter(currentHandler))
        {
            return;
        }

        Debug.unityLogger.logHandler = new SelectiveUnityLogFilter(currentHandler);
    }

    public void LogFormat(LogType logType, Object context, string format, params object[] args)
    {
        if (ShouldSuppress(logType, format, args))
        {
            return;
        }

        innerHandler?.LogFormat(logType, context, format, args);
    }

    public void LogException(Exception exception, Object context)
    {
        innerHandler?.LogException(exception, context);
    }

    private static bool ShouldSuppress(LogType logType, string format, object[] args)
    {
        bool suppressible = logType == LogType.Log || logType == LogType.Warning;
        if (!suppressible)
        {
            return false;
        }

        string message = FormatMessage(format, args);
        return ShouldSuppress(message);
    }

    private static bool ShouldSuppress(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        for (int i = 0; i < SuppressedMarkers.Length; i++)
        {
            string marker = SuppressedMarkers[i];
            if (message.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLegacyFilter(ILogHandler handler)
    {
        return handler != null
            && string.Equals(handler.GetType().Name, "MetaXRFeatureLogFilter", StringComparison.Ordinal);
    }

    private static string FormatMessage(string format, object[] args)
    {
        if (string.IsNullOrEmpty(format))
        {
            return string.Empty;
        }

        if (args == null || args.Length == 0)
        {
            return format;
        }

        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
