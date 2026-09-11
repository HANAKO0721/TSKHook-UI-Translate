using BepInEx;
using UnityEngine;

namespace TSKHook.UI;

// BepInEx's IL2CPP Unity log bridge forwards the message but drops stackTrace.
internal static class UnityExceptionCapture
{
    private static Application.LogCallback? callback;

    internal static void Start()
    {
        var path = Path.Combine(Paths.PluginPath, "TSKHook.UI", "unity-exceptions.log");
        File.WriteAllText(path, "");
        callback = (Action<string, string, LogType>)((message, stackTrace, type) =>
        {
            if (type != LogType.Error && type != LogType.Exception) return;
            // Keep this independent of log sinks and attached through game teardown.
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {type}: {message}\n{stackTrace}\n\n");
        });
        Application.add_logMessageReceived(callback);
    }
}
