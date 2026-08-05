using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the project's local MCP for Unity HTTP server and Unity bridge alive.
/// The package already owns process creation and reconnect semantics; this class
/// only enables those built-in settings and repairs a stopped connection.
/// </summary>
[InitializeOnLoad]
public static class UnityMcpAutoConnectBootstrap
{
    private const string UseHttpTransportKey = "MCPForUnity.UseHttpTransport";
    private const string HttpTransportScopeKey = "MCPForUnity.HttpTransportScope";
    private const string HttpUrlKey = "MCPForUnity.HttpUrl";
    private const string AutoStartOnLoadKey = "MCPForUnity.AutoStartOnLoad";
    private const string LocalEndpoint = "http://127.0.0.1:8080";
    private const double InitialDelaySeconds = 5d;
    private const double WatchdogIntervalSeconds = 20d;

    private static double nextCheckAt;
    private static bool repairInProgress;

    static UnityMcpAutoConnectBootstrap()
    {
        ApplyStablePreferences();
        nextCheckAt = EditorApplication.timeSinceStartup + InitialDelaySeconds;
        EditorApplication.update -= WatchdogTick;
        EditorApplication.update += WatchdogTick;
    }

    [MenuItem("Tools/MCP/Repair Auto Connect")]
    public static void RepairNow()
    {
        nextCheckAt = 0d;
        WatchdogTick();
    }

    private static void ApplyStablePreferences()
    {
        EditorPrefs.SetBool(UseHttpTransportKey, true);
        EditorPrefs.SetString(HttpTransportScopeKey, "local");
        EditorPrefs.SetString(HttpUrlKey, LocalEndpoint);
        EditorPrefs.SetBool(AutoStartOnLoadKey, true);
    }

    private static void WatchdogTick()
    {
        if (repairInProgress || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (EditorApplication.timeSinceStartup < nextCheckAt)
            return;

        nextCheckAt = EditorApplication.timeSinceStartup + WatchdogIntervalSeconds;
        _ = EnsureConnectedAsync();
    }

    private static async Task EnsureConnectedAsync()
    {
        repairInProgress = true;
        try
        {
            ApplyStablePreferences();

            if (!MCPServiceLocator.Server.IsLocalHttpServerReachable())
            {
                if (!MCPServiceLocator.Server.StartLocalHttpServer(quiet: true))
                {
                    Debug.LogWarning("[Unity MCP Auto Connect] Local MCP server could not be started.");
                    return;
                }

                for (int attempt = 0; attempt < 60; attempt++)
                {
                    await Task.Delay(500);
                    if (MCPServiceLocator.Server.IsLocalHttpServerReachable())
                        break;
                }
            }

            if (!MCPServiceLocator.Server.IsLocalHttpServerReachable())
            {
                Debug.LogWarning("[Unity MCP Auto Connect] Server did not become reachable on 127.0.0.1:8080.");
                return;
            }

            if (!MCPServiceLocator.Bridge.IsRunning)
            {
                bool connected = await MCPServiceLocator.Bridge.StartAsync();
                if (!connected)
                    Debug.LogWarning("[Unity MCP Auto Connect] Unity bridge could not connect.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Unity MCP Auto Connect] Repair failed: {exception.Message}");
        }
        finally
        {
            repairInProgress = false;
        }
    }
}
