using System;
using System.Collections;
using System.Collections.Generic;
using Cmod.Services;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    public static partial class CmodModules
    {
        /// <summary>
        /// C-GITHUB boot step 1 (runs before the UI exists): the local log file, the GitHub log uploader,
        /// and the console commands / convars of both services. Nothing here touches WindowManager.
        /// </summary>
        public static partial void Services_Init(GameObject root)
        {
            ServiceHost.Ensure(root);
            LogFile.Open();
            LogUploader.Init();
            AutoUpdater.InitVars();
            CmodServices.RegisterCommands();
        }

        /// <summary>
        /// C-GITHUB boot step 2 (runs last, after the UI and app windows exist): registers the GitHub
        /// Settings / Send Debug Log / Update windows and their Start menu items, then starts the
        /// automatic release check.
        /// </summary>
        public static partial void Updater_Init(GameObject root)
        {
            ServiceHost.Ensure(root);
            CmodServices.InitWindows();
            AutoUpdater.Startup();
        }
    }
}

namespace Cmod.Services
{
    /// <summary>
    /// Public entry points of the GitHub services for other cards (main menu, chat, ...).
    /// Every method is safe to call before the windows exist; it then only logs.
    /// </summary>
    public static class CmodServices
    {
        public static GitHubSettingsWindow Settings { get; private set; }
        public static SendLogWindow SendLog { get; private set; }
        public static UpdateWindow Update { get; private set; }

        /// <summary>Opens the GitHub Settings window (token, auto upload, auto update).</summary>
        public static void OpenGitHubSettings()
        {
            if (Settings == null) { CmodLog.Warn("GitHub Settings is not available yet."); return; }
            Settings.Open();
        }

        /// <summary>Opens the "Send Debug Log" note dialog (or asks for a token first).</summary>
        public static void OpenSendLogDialog()
        {
            if (SendLog == null) { CmodLog.Warn("Send Debug Log is not available yet. Use the log_upload command."); return; }
            if (!GitHubClient.HasToken)
            {
                AskForToken("Sending a debug log needs a GitHub token. Open GitHub Settings now?");
                return;
            }
            SendLog.Open();
        }

        /// <summary>Asks (Yes/No) whether to open GitHub Settings, with the given explanation.</summary>
        public static void AskForToken(string text)
        {
            if (Settings == null) { CmodLog.Warn(text); return; }
            MessageBox.Show("GitHub", text, MessageBox.Kind.Question, new[] { "Yes", "No" },
                r => { if (r == 0) Settings.Open(); });
        }

        internal static void RegisterCommands()
        {
            ConCommands.Register("log_upload",
                "Uploads the recent log to GitHub as a cmod-log issue (or a comment on this session's issue). Usage: log_upload [note]",
                args =>
                {
                    var note = args != null && args.Length > 0 ? string.Join(" ", args) : null;
                    if (!GitHubClient.HasToken)
                    {
                        CmodLog.Warn("[log upload] No GitHub token set. Open Start > GitHub Settings... (or run github_settings).");
                        return;
                    }
                    LogUploader.UploadManual(note, null);
                });
            ConCommands.Register("update_check", "Checks GitHub for a newer Cmod release.",
                args => AutoUpdater.CheckNow(true));
            ConCommands.Register("github_settings", "Opens the GitHub Settings window (token, log upload, updates).",
                args => OpenGitHubSettings());
            ConCommands.Register("log_folder", "Prints the local log file path and opens the log folder.",
                args =>
                {
                    CmodLog.Info($"Log file: {LogFile.CurrentPath ?? "(not open)"}");
                    LogFile.OpenFolder();
                });
        }

        internal static void InitWindows()
        {
            if (Settings != null) return; // idempotent
            Settings = new GitHubSettingsWindow();
            SendLog = new SendLogWindow();
            Update = new UpdateWindow();
            WindowManager.Register(Settings);
            WindowManager.Register(SendLog);
            WindowManager.Register(Update);

            // "Windows Update" sat at the very top of the Win98 Start menu, above a separator.
            StartMenu.AddItem("Cmod Update...", "update", () => AutoUpdater.CheckNow(true), -10, 0);
            StartMenu.AddItem("Send Debug Log...", "bug", OpenSendLogDialog, 0, 80);
            StartMenu.AddItem("GitHub Settings...", "github", OpenGitHubSettings, 50, 20);
        }
    }

    /// <summary>
    /// A MonoBehaviour on the Cmod root that runs the services' coroutines, flushes the log file
    /// and ticks the uploader's batching timer. Exceptions never escape Update.
    /// </summary>
    public sealed class ServiceHost : MonoBehaviour
    {
        public static ServiceHost Instance { get; private set; }

        internal static void Ensure(GameObject root)
        {
            if (Instance != null) return;
            if (root == null) root = new GameObject("CmodServices");
            Instance = root.GetComponent<ServiceHost>();
            if (Instance == null) Instance = root.AddComponent<ServiceHost>();
        }

        /// <summary>Starts a coroutine on the host. Exceptions thrown inside it are logged as warnings
        /// (never as errors, so a failing upload can't trigger another upload).</summary>
        public static Coroutine Run(IEnumerator routine, string name = "service")
        {
            if (routine == null) return null;
            if (Instance == null) { CmodLog.Warn($"[{name}] service host missing, coroutine not started"); return null; }
            return Instance.StartCoroutine(Safe(routine, name));
        }

        // Drives nested IEnumerators itself (a stack instead of Unity's nested coroutines) so that an
        // exception in an inner routine (GitHubClient.Get, PostJson, ...) is also caught here and logged as a
        // warning. Unity yield instructions (WaitForSeconds, CustomYieldInstruction, AsyncOperation,
        // Coroutine) are passed through to Unity unchanged.
        static IEnumerator Safe(IEnumerator root, string name)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    object current;
                    try
                    {
                        var top = stack.Peek();
                        if (!top.MoveNext())
                        {
                            stack.Pop();
                            (top as IDisposable)?.Dispose();
                            continue;
                        }
                        current = top.Current;
                    }
                    catch (Exception ex)
                    {
                        CmodLog.Warn($"[{name}] {ex.GetType().Name}: {ex.Message}");
                        yield break; // finally below disposes the rest (runs their finally blocks)
                    }

                    if (current is IEnumerator nested && !(current is CustomYieldInstruction))
                    {
                        stack.Push(nested);
                        continue;
                    }
                    yield return current;
                }
            }
            finally
            {
                // Runs on completion, on an exception, and when the coroutine is stopped.
                while (stack.Count > 0)
                {
                    try { (stack.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception ex) { CmodLog.Warn($"[{name}] {ex.GetType().Name}: {ex.Message}"); }
                }
            }
        }

        void Update()
        {
            try { LogFile.Tick(); } catch (Exception ex) { Debug.LogWarning("[log file] " + ex.Message); }
            try { LogUploader.Tick(); } catch (Exception ex) { CmodLog.Warn("[log upload] " + ex.Message); }
        }

        void OnApplicationQuit()
        {
            try { LogUploader.OnQuit(); } catch (Exception) { /* quitting anyway */ }
            LogFile.Close();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
