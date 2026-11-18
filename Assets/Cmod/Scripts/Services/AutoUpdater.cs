using System;
using System.Collections;
using Cmod.UI;
using UnityEngine;

namespace Cmod.Services
{
    /// <summary>
    /// Checks GitHub for a newer release and offers to install it.
    /// At startup (convar update_auto = 1) it GETs /releases/latest and compares tag_name with
    /// Application.version (CmodGame.ParseVersion). If the release is newer, a message box offers
    /// Yes / No / Release Notes. "Yes" hands over to <see cref="UpdateInstaller"/>.
    /// In the editor it only checks and notifies, it never installs.
    /// </summary>
    public static class AutoUpdater
    {
        const float StartupDelay = 3f;
        const string UpdatedFromKey = "update_from_version";

        public static ConVar AutoVar { get; private set; }

        /// <summary>The latest release from the last successful check (null before that).</summary>
        public static GitHubRelease Latest { get; private set; }
        /// <summary>True when <see cref="Latest"/> is newer than the running version.</summary>
        public static bool UpdateAvailable { get; private set; }
        public static bool Checking { get; private set; }
        /// <summary>Fired after every check (main menu "Update available" balloon, settings window).</summary>
        public static event Action Checked;

        public static Version LatestVersion => Latest != null ? CmodGame.ParseVersion(Latest.tag_name) : null;

        internal static void InitVars()
        {
            AutoVar = ConCommands.CreateVar("update_auto", "1",
                "Check GitHub for a newer Cmod release at startup (0/1).", 0f, 1f, archive: true);
        }

        /// <summary>Boot: report a finished update, tidy the update folder, then (maybe) check.</summary>
        internal static void Startup()
        {
            ReportFinishedUpdate();
            if (AutoVar == null || AutoVar.Bool) ServiceHost.Run(DelayedStartupCheck(), "update");
        }

        static IEnumerator DelayedStartupCheck()
        {
            yield return new WaitForSecondsRealtime(StartupDelay);
            // The player may have started a check (update_check, Start menu) or an update during the delay.
            if (Checking || UpdateInstaller.Running || Latest != null) yield break;
            yield return CheckRoutine(false);
        }

        /// <summary>Checks now. `interactive` (Start menu, update_check) also reports "up to date" and errors in a message box.</summary>
        public static void CheckNow(bool interactive)
        {
            if (Checking) { if (interactive) CmodLog.Info("[update] A check is already running."); return; }
            if (UpdateInstaller.Running)
            {
                CmodServices.Update?.Open();
                return;
            }
            ServiceHost.Run(CheckRoutine(interactive), "update");
        }

        static IEnumerator CheckRoutine(bool interactive)
        {
            Checking = true;
            GitHubResponse resp = null;
            try
            {
                if (interactive) CmodLog.Info($"[update] Checking {GitHubClient.RepoWeb} for updates...");
                yield return GitHubClient.Get(GitHubClient.RepoApi + "/releases/latest", r => resp = r);
            }
            finally { Checking = false; }

            var current = CmodGame.GameVersion;
            if (resp == null || !resp.Ok)
            {
                string msg;
                if (resp != null && resp.Status == 404)
                    msg = GitHubClient.HasToken
                        ? "No published release was found for this game yet."
                        : "No published release was found. If the repository is private, add a token in GitHub Settings.";
                else
                    msg = "Could not check for updates. " + (resp?.ErrorMessage ?? "");
                CmodLog.Info("[update] " + msg);
                if (interactive) MessageBox.Show("Cmod Update", msg, MessageBox.Kind.Warning, new[] { "OK" });
                RaiseChecked();
                yield break;
            }

            var rel = GitHubClient.ParseJson<GitHubRelease>(resp.Text);
            if (rel == null || string.IsNullOrEmpty(rel.tag_name))
            {
                CmodLog.Warn("[update] The release information from GitHub could not be read.");
                if (interactive) MessageBox.Error("Cmod Update", "The release information from GitHub could not be read.");
                RaiseChecked();
                yield break;
            }

            Latest = rel;
            var latest = CmodGame.ParseVersion(rel.tag_name);
            UpdateAvailable = latest > current;
            RaiseChecked();

            if (!UpdateAvailable)
            {
                CmodLog.Info($"[update] Cmod is up to date (v{Short(current)}, latest release {rel.tag_name}).");
                if (interactive)
                    MessageBox.Info("Cmod Update", $"Cmod is up to date.\n\nYou have version {Short(current)}. The latest release is {rel.tag_name}.");
                yield break;
            }

            CmodLog.Info($"[update] A new version is available: {rel.tag_name} (you have v{Short(current)}).");
            PromptUpdate();
        }

        /// <summary>Shows the "update available" question for <see cref="Latest"/>.</summary>
        public static void PromptUpdate()
        {
            var rel = Latest;
            if (rel == null) return;
            var current = Short(CmodGame.GameVersion);
            var title = string.IsNullOrEmpty(rel.name) ? rel.tag_name : rel.name;

            if (Application.isEditor)
            {
                MessageBox.Show("Cmod Update",
                    $"A new version of Cmod is available: {title}.\nYou have version {current}.\n\n" +
                    "Updates are never installed in the Unity Editor. Pull the latest code instead.",
                    MessageBox.Kind.Info, new[] { "OK", "Release Notes" },
                    r => { if (r == 1) ShowReleaseNotes(); });
                return;
            }

            MessageBox.Show("Cmod Update",
                $"A new version of Cmod is available: {title}.\nYou have version {current}.\n\n" +
                "Do you want to download and install it now? Cmod will restart.",
                MessageBox.Kind.Question, new[] { "Yes", "No", "Release Notes" },
                r =>
                {
                    if (r == 0) UpdateInstaller.Begin(rel);
                    else if (r == 2) ShowReleaseNotes();
                });
        }

        /// <summary>Opens the release notes in the update window (falls back to the browser).</summary>
        public static void ShowReleaseNotes()
        {
            if (Latest == null) return;
            if (CmodServices.Update != null) CmodServices.Update.ShowNotes(Latest);
            else if (!string.IsNullOrEmpty(Latest.html_url)) Application.OpenURL(Latest.html_url);
        }

        /// <summary>Finds the zip for this platform: its name contains Windows / Linux / macOS (or Mac/OSX).</summary>
        public static GitHubAsset PickAsset(GitHubRelease rel)
        {
            if (rel?.assets == null) return null;
            string[] keys;
            switch (CmodSystemInfo.PlatformName)
            {
                case "Windows": keys = new[] { "windows", "win64", "win" }; break;
                case "Linux": keys = new[] { "linux" }; break;
                case "macOS": keys = new[] { "macos", "osx", "mac" }; break;
                default: return null;
            }
            foreach (var key in keys)
                foreach (var a in rel.assets)
                {
                    if (a == null || string.IsNullOrEmpty(a.name)) continue;
                    var n = a.name.ToLowerInvariant();
                    if (n.EndsWith(".zip") && n.Contains(key)) return a;
                }
            return null;
        }

        static void ReportFinishedUpdate()
        {
            try
            {
                var from = PlayerPrefs.GetString(UpdatedFromKey, "");
                if (from.Length > 0)
                {
                    PlayerPrefs.DeleteKey(UpdatedFromKey);
                    PlayerPrefs.Save();
                    if (CmodGame.ParseVersion(from) < CmodGame.GameVersion)
                        CmodLog.Info($"[update] Cmod was updated from v{from} to v{Application.version}.");
                    else
                        CmodLog.Warn($"[update] The update from v{from} did not complete (still v{Application.version}). " +
                                     $"See {UpdateInstaller.UpdaterLogPath}.");
                }
                UpdateInstaller.CleanupLeftovers();
            }
            catch (Exception ex) { CmodLog.Warn("[update] " + ex.Message); }
        }

        /// <summary>Remembers the version we updated from, so the next launch can report the result.</summary>
        internal static void MarkUpdating()
        {
            PlayerPrefs.SetString(UpdatedFromKey, Application.version);
            PlayerPrefs.Save();
        }

        /// <summary>Forgets <see cref="MarkUpdating"/> when the installer could not be started.</summary>
        internal static void ClearUpdating()
        {
            PlayerPrefs.DeleteKey(UpdatedFromKey);
            PlayerPrefs.Save();
        }

        static void RaiseChecked()
        {
            try { Checked?.Invoke(); } catch (Exception ex) { CmodLog.Warn("[update] " + ex.Message); }
        }

        static string Short(Version v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

        /// <summary>"12.3 MB" style size text.</summary>
        public static string FormatBytes(double bytes)
        {
            if (bytes >= 1024d * 1024d * 1024d) return (bytes / (1024d * 1024d * 1024d)).ToString("0.0") + " GB";
            if (bytes >= 1024d * 1024d) return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
            if (bytes >= 1024d) return (bytes / 1024d).ToString("0") + " KB";
            return bytes.ToString("0") + " bytes";
        }
    }
}
