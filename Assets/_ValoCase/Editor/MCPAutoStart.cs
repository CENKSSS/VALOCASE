using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ValoCase.EditorTools
{
    // Starts the MCP for Unity bridge automatically when this project opens, without the
    // package's built-in "Auto-Start on Editor Load" toggle (which launches the local HTTP
    // server in a visible console window via TerminalLauncher). This launches the same
    // uvx/http server command hidden instead, and logs its output to a file so failures
    // are still visible.
    [InitializeOnLoad]
    static class MCPAutoStart
    {
        private const string SessionKey = "ValoCase.MCPAutoStart.Ran";
        private const int MaxWaitAttempts = 30;

        static MCPAutoStart()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (Application.isBatchMode) return;

            SessionState.SetBool(SessionKey, true);

            // The package's own "Auto-Start on Editor Load" toggle launches the local HTTP
            // server via a visible console window (TerminalLauncher). Keep it off so it can
            // never race our hidden launch below; this script is the sole auto-start path.
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", false);

            EditorApplication.delayCall += () => _ = RunAsync();
        }

        private static async Task RunAsync()
        {
            try
            {
                var bridge = MCPServiceLocator.Bridge;
                if (bridge.IsRunning)
                {
                    Notify("MCP for Unity already running");
                    return;
                }

                var server = MCPServiceLocator.Server;
                bool localHttp = server.CanStartLocalServer();

                if (localHttp && !server.IsLocalHttpServerReachable())
                {
                    if (!TryLaunchLocalServerHidden(server, out string launchError))
                    {
                        Notify($"MCP for Unity failed to start: {launchError}", isError: true);
                        return;
                    }

                    if (!await WaitForReachableAsync(server))
                    {
                        Notify("MCP for Unity failed to start: local server did not become reachable", isError: true);
                        return;
                    }
                }

                bool started = await bridge.StartAsync();
                if (started)
                {
                    Notify("MCP for Unity started");
                }
                else
                {
                    Notify("MCP for Unity failed to start", isError: true);
                }
            }
            catch (Exception ex)
            {
                Notify($"MCP for Unity failed to start: {ex.Message}", isError: true);
            }
        }

        private static bool TryLaunchLocalServerHidden(IServerManagementService server, out string error)
        {
            error = null;

            if (!server.TryGetLocalHttpServerCommand(out string command, out string commandError))
            {
                error = commandError ?? "server command unavailable";
                return false;
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string mcpDir = Path.Combine(projectRoot, "Library", "MCPForUnity");
                Directory.CreateDirectory(mcpDir);

                string logPath = Path.Combine(mcpDir, "mcp-server.log");
                string scriptPath = Path.Combine(mcpDir, "mcp-server-hidden.cmd");

                File.WriteAllText(scriptPath,
                    "@echo off\r\n" +
                    $"{command} > \"{logPath}\" 2>&1\r\n");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = projectRoot
                };
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static async Task<bool> WaitForReachableAsync(IServerManagementService server)
        {
            var shortDelay = TimeSpan.FromMilliseconds(500);
            var longDelay = TimeSpan.FromSeconds(3);

            for (int attempt = 0; attempt < MaxWaitAttempts; attempt++)
            {
                if (server.IsLocalHttpServerReachable()) return true;
                try { await Task.Delay(attempt < 6 ? shortDelay : longDelay); }
                catch { return false; }
            }

            return false;
        }

        private static void Notify(string message, bool isError = false)
        {
            if (isError) Debug.LogError($"[MCP AutoStart] {message}");
            else Debug.Log($"[MCP AutoStart] {message}");

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.ShowNotification(new GUIContent(message));
                return;
            }

            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null) return;

            var gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
            if (gameViews.Length == 0) return;

            var method = gameViewType.GetMethod("ShowNotification", new[] { typeof(GUIContent) });
            method?.Invoke(gameViews[0], new object[] { new GUIContent(message) });
        }
    }
}
