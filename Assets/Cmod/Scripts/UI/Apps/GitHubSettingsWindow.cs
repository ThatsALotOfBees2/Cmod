using System;
using System.Collections;
using System.IO;
using Cmod.Services;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// "GitHub Settings" (Start > GitHub Settings..., or the github_settings command): the personal
    /// access token used for log upload and private-repo updates, plus the auto upload / auto update
    /// switches. The token is typed into a masked field, tested with "Test" and stored in PlayerPrefs
    /// ("github_token") by "Save". It is never shown again, logged, or uploaded.
    /// </summary>
    public sealed class GitHubSettingsWindow : Win98Window
    {
        const float Pad = 8f;
        const float BtnH = 23f;
        const int MaxTokenLength = 255;
        const string TokenField = "cmod_github_token";

        string _tokenInput = "";
        bool _testing;

        public GitHubSettingsWindow()
        {
            Title = "GitHub Settings";
            IconName = "github";
            Rect = new Rect(0, 0, 440, 500);
            Resizable = false;
            Minimizable = true;
        }

        public override void OnOpened()
        {
            CenterOnScreen();
            _tokenInput = "";
        }

        public override void OnClosed()
        {
            _tokenInput = ""; // don't keep a typed secret around
        }

        public override void DrawContents(Rect c)
        {
            float w = c.width - Pad * 2;
            float x = c.x + Pad;
            float y = c.y + Pad;

            // ---- Header -----------------------------------------------------------------------
            Win98.Icon(new Rect(x, y, 32, 32), "github");
            Win98.Label(new Rect(x + 42, y, w - 42, 16), "Connect Cmod to GitHub", true);
            Win98.Label(new Rect(x + 42, y + 16, w - 42, 16), $"Repository: {GitHubClient.Owner}/{GitHubClient.Repo}");
            y += 42;

            // ---- Token --------------------------------------------------------------------------
            var g = new Rect(x, y, w, 166);
            Win98.GroupBox(g, "Access token");
            float gx = g.x + 10, gw = g.width - 20, gy = g.y + 18;
            gy += GhUi.Paragraph(new Rect(gx, gy, gw, 0),
                $"Create a fine-grained personal access token for {GitHubClient.Owner}/{GitHubClient.Repo} only, " +
                "with Issues: Read and write (plus Contents: Read-only if the repository is private). " +
                "The token is stored on this computer only and is never uploaded.") + 6;

            Win98.Label(new Rect(gx, gy + 2, 44, 16), "Token:");
            _tokenInput = Win98.PasswordField(new Rect(gx + 46, gy, gw - 46, 20), _tokenInput ?? "", '*', TokenField);
            if (_tokenInput.Length > MaxTokenLength) _tokenInput = _tokenInput.Substring(0, MaxTokenLength);
            gy += 24;

            string state = GitHubClient.HasToken ? "A token is saved." : "No token saved: logs can't be uploaded.";
            if (_tokenInput.Length > 0) state = "New token typed: press Save to keep it.";
            Win98.Label(new Rect(gx + 46, gy, gw - 46, 16), state, false, TextAnchor.MiddleLeft,
                GitHubClient.HasToken || _tokenInput.Length > 0 ? (Color?)null : new Color32(128, 0, 0, 255));
            gy += 20;

            float bw = (gw - 3 * 6) / 4f;
            if (Win98.Button(new Rect(gx, gy, bw, BtnH), "Create Token..."))
                Application.OpenURL(GitHubClient.NewTokenUrl);
            bool canTest = !_testing && (_tokenInput.Trim().Length > 0 || GitHubClient.HasToken);
            if (Win98.Button(new Rect(gx + (bw + 6), gy, bw, BtnH), _testing ? "Testing..." : "Test", canTest))
                StartTest();
            if (Win98.Button(new Rect(gx + (bw + 6) * 2, gy, bw, BtnH), "Save", _tokenInput.Trim().Length > 0))
                SaveToken();
            if (Win98.Button(new Rect(gx + (bw + 6) * 3, gy, bw, BtnH), "Clear", GitHubClient.HasToken))
                ConfirmClear();
            y = g.yMax + 8;

            // ---- Debug logs --------------------------------------------------------------------
            g = new Rect(x, y, w, 104);
            Win98.GroupBox(g, "Debug logs");
            gx = g.x + 10; gw = g.width - 20; gy = g.y + 18;
            var auto = LogUploader.AutoVar;
            if (auto != null)
            {
                bool v = Win98.Checkbox(new Rect(gx, gy, gw, 16), auto.Bool, "Upload errors to GitHub automatically (log_upload_auto)");
                if (v != auto.Bool) auto.SetBool(v);
            }
            gy += 20;
            var file = LogFile.CurrentPath != null ? Path.GetFileName(LogFile.CurrentPath) : "(not open)";
            Win98.Label(new Rect(gx, gy, gw, 16), "This session's log: " + file);
            gy += 18;
            if (LogUploader.IssueUrl != null)
                Win98.Label(new Rect(gx, gy, gw, 16), $"Uploaded to issue #{LogUploader.IssueNumber}", false,
                    TextAnchor.MiddleLeft, new Color32(0, 0, 128, 255));
            gy += 20;
            bw = (gw - 2 * 6) / 3f;
            if (Win98.Button(new Rect(gx, gy, bw, BtnH), "Open Log Folder")) LogFile.OpenFolder();
            if (Win98.Button(new Rect(gx + bw + 6, gy, bw, BtnH), "Send Debug Log...", !LogUploader.Busy))
                CmodServices.OpenSendLogDialog();
            if (Win98.Button(new Rect(gx + (bw + 6) * 2, gy, bw, BtnH), "Open Issue", LogUploader.IssueUrl != null))
                Application.OpenURL(LogUploader.IssueUrl);
            y = g.yMax + 8;

            // ---- Updates -------------------------------------------------------------------------
            g = new Rect(x, y, w, 84);
            Win98.GroupBox(g, "Updates");
            gx = g.x + 10; gw = g.width - 20; gy = g.y + 18;
            var upd = AutoUpdater.AutoVar;
            if (upd != null)
            {
                bool v = Win98.Checkbox(new Rect(gx, gy, gw, 16), upd.Bool, "Check for updates when Cmod starts (update_auto)");
                if (v != upd.Bool) upd.SetBool(v);
            }
            gy += 20;
            string latest = AutoUpdater.Checking ? "checking..."
                : AutoUpdater.Latest == null ? "not checked"
                : AutoUpdater.Latest.tag_name + (AutoUpdater.UpdateAvailable ? " (update available)" : " (up to date)");
            Win98.Label(new Rect(gx, gy + 4, gw - 110, 16), $"Installed: v{Application.version}    Latest: {latest}");
            if (Win98.Button(new Rect(gx + gw - 100, gy, 100, BtnH), "Check Now", !AutoUpdater.Checking && !UpdateInstaller.Running))
                AutoUpdater.CheckNow(true);

            // ---- Close ---------------------------------------------------------------------------
            if (Win98.Button(new Rect(c.xMax - Pad - 75, c.yMax - Pad - BtnH, 75, BtnH), "Close")) Close();
        }

        void SaveToken()
        {
            var t = (_tokenInput ?? "").Trim();
            if (t.Length == 0) return;
            if (t.IndexOfAny(new[] { ' ', '\t', '\n', '\r' }) >= 0)
            {
                MessageBox.Error("GitHub Settings", "A token can't contain spaces. Copy it again from GitHub.");
                return;
            }
            GitHubClient.Token = t;
            _tokenInput = "";
            GUIUtility.keyboardControl = 0;
            CmodLog.Info("[github] Token saved.");
            MessageBox.Info("GitHub Settings", "The token was saved.\n\nPress Test to check that it works.");
        }

        void ConfirmClear()
        {
            MessageBox.Show("GitHub Settings", "Remove the saved GitHub token from this computer?",
                MessageBox.Kind.Question, new[] { "Yes", "No" },
                r =>
                {
                    if (r != 0) return;
                    GitHubClient.Token = null;
                    CmodLog.Info("[github] Token removed.");
                });
        }

        void StartTest()
        {
            var typed = (_tokenInput ?? "").Trim();
            var token = typed.Length > 0 ? typed : GitHubClient.Token;
            if (token.Length == 0) return;
            ServiceHost.Run(TestRoutine(token, typed.Length > 0), "github test");
        }

        IEnumerator TestRoutine(string token, bool unsaved)
        {
            _testing = true;
            GitHubResponse repo = null, issues = null;
            try
            {
                yield return GitHubClient.Get(GitHubClient.RepoApi, r => repo = r, token);
                if (repo != null && repo.Ok)
                    yield return GitHubClient.Get(GitHubClient.RepoApi + "/issues?per_page=1&labels=" + LogUploader.Label,
                        r => issues = r, token);
            }
            finally { _testing = false; }

            if (repo == null || !repo.Ok)
            {
                string why;
                if (repo == null) why = "No response.";
                else if (repo.Status == 401) why = "GitHub rejected the token. It may be mistyped, expired or revoked.";
                else if (repo.Status == 404 || repo.Status == 403)
                    why = $"The token can't see {GitHubClient.Owner}/{GitHubClient.Repo}. Give it access to this repository.";
                else why = repo.ErrorMessage;
                MessageBox.Error("Token Test", "The token test failed.\n\n" + why);
                yield break;
            }

            var info = GitHubClient.ParseJson<GitHubRepoJson>(repo.Text);
            var msg = "The token works.\n\n" +
                      $"Repository: {info?.full_name ?? GitHubClient.Owner + "/" + GitHubClient.Repo}" +
                      (info != null ? (info.@private ? " (private)" : " (public)") : "") + "\n";
            if (issues != null && issues.Ok) msg += "Issues: readable. Write access is used when a log is uploaded.\n";
            else msg += "Issues: NOT readable. Give the token Issues: Read and write.\n";
            var remaining = repo.Header("X-RateLimit-Remaining");
            if (!string.IsNullOrEmpty(remaining)) msg += $"API requests left this hour: {remaining}\n";
            if (unsaved) msg += "\nPress Save to keep this token.";
            MessageBox.Show("Token Test", msg, issues != null && issues.Ok ? MessageBox.Kind.Info : MessageBox.Kind.Warning, new[] { "OK" });
        }
    }

    /// <summary>
    /// "Send Debug Log" (Start > Send Debug Log...): an optional note, then uploads the recent log to
    /// this session's cmod-log issue (or a new one) and offers to open it in the browser.
    /// </summary>
    public sealed class SendLogWindow : Win98Window
    {
        const float Pad = 10f;
        const float BtnW = 75f, BtnH = 23f;
        const int MaxNote = 2000;

        string _note = "";
        bool _sending;

        public SendLogWindow()
        {
            Title = "Send Debug Log";
            IconName = "bug";
            Rect = new Rect(0, 0, 400, 290);
            Minimizable = false;
        }

        public override void OnOpened()
        {
            CenterOnScreen();
            if (!_sending) _note = "";
        }

        public override void DrawContents(Rect c)
        {
            float x = c.x + Pad, w = c.width - Pad * 2, y = c.y + Pad;
            Win98.Icon(new Rect(x, y, 32, 32), "bug");
            y += GhUi.Paragraph(new Rect(x + 42, y, w - 42, 0),
                "Describe what you were doing when the problem happened (optional). " +
                "This helps whoever fixes it.");
            y = Mathf.Max(y, c.y + Pad + 36) + 4;

            float areaH = c.yMax - Pad - BtnH - 8 - 44 - y;
            _note = Win98.TextArea(new Rect(x, y, w, areaH), _note ?? "");
            if (_note.Length > MaxNote) _note = _note.Substring(0, MaxNote);
            y += areaH + 6;

            GhUi.Paragraph(new Rect(x, y, w, 0),
                $"Sends the last {LogFile.RecentCapacity} log lines and system info (version, OS, CPU, GPU, RAM) to " +
                $"{GitHubClient.Owner}/{GitHubClient.Repo} issues. Your token and home folder path are removed.",
                new Color32(64, 64, 64, 255));

            float by = c.yMax - Pad - BtnH;
            if (Win98.Button(new Rect(c.xMax - Pad - BtnW * 2 - 6, by, BtnW, BtnH), _sending ? "Sending..." : "Send", !_sending && !LogUploader.Busy))
                Send();
            if (Win98.Button(new Rect(c.xMax - Pad - BtnW, by, BtnW, BtnH), "Cancel", !_sending))
                Close();
        }

        void Send()
        {
            if (!GitHubClient.HasToken)
            {
                CmodServices.AskForToken("Sending a debug log needs a GitHub token. Open GitHub Settings now?");
                return;
            }
            _sending = true;
            LogUploader.UploadManual(_note, url =>
            {
                _sending = false;
                if (url == null)
                {
                    MessageBox.Error("Send Debug Log", "The log could not be uploaded. See the console for details.");
                    return;
                }
                _note = "";
                Close();
                MessageBox.Show("Send Debug Log", "The debug log was sent. Thank you!\n\n" + url,
                    MessageBox.Kind.Info, new[] { "OK", "Open in Browser" },
                    r => { if (r == 1) Application.OpenURL(url); });
            });
        }
    }

    /// <summary>Small IMGUI helpers shared by the GitHub windows.</summary>
    internal static class GhUi
    {
        static GUIStyle s_wrap;
        static readonly GUIContent s_content = new GUIContent();

        /// <summary>Word-wrapped Win98 text. Returns the height it used (r.height is ignored).</summary>
        public static float Paragraph(Rect r, string text, Color? color = null)
        {
            var st = WrapStyle();
            s_content.text = text ?? "";
            float h = st.CalcHeight(s_content, r.width);
            var old = st.normal.textColor;
            if (color.HasValue) st.normal.textColor = color.Value;
            GUI.Label(new Rect(r.x, r.y, r.width, h), s_content, st);
            st.normal.textColor = old;
            return h;
        }

        public static float Height(string text, float width)
        {
            s_content.text = text ?? "";
            return WrapStyle().CalcHeight(s_content, width);
        }

        public static GUIStyle WrapStyle()
        {
            if (s_wrap == null)
            {
                Win98.EnsureInit();
                s_wrap = new GUIStyle(Win98.TextStyle(false)) { wordWrap = true, alignment = TextAnchor.UpperLeft };
                s_wrap.normal.textColor = Color.black;
            }
            return s_wrap;
        }
    }
}
