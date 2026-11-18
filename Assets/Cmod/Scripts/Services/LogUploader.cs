using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Cmod.Services
{
    /// <summary>
    /// Sends debug logs to GitHub issues labelled "cmod-log", so they can be read and fixed later
    /// (see docs/GITHUB.md).
    ///
    /// Automatic (convar log_upload_auto = 1 and a token is set): the first error of a session creates
    /// one issue: "[cmod-log] player vX.Y.Z Platform – first error line" with system info and the last
    /// 300 log lines. Later errors are batched into comments on that issue, at most one every 60 s and at
    /// most 20 per session. Manual: the log_upload [note] command and Start > Send Debug Log...
    ///
    /// Everything uploaded goes through <see cref="Redaction"/> (token and home path removed).
    /// Failures are logged as warnings, never errors, so an upload problem can't trigger another upload.
    /// </summary>
    public static class LogUploader
    {
        public const string Label = "cmod-log";
        public const float CommentInterval = 60f;
        public const int MaxAutoComments = 20;
        public const int MaxBodyChars = 60000;
        public const int MaxAttempts = 3;
        const float RetryDelay = 120f;

        public static ConVar AutoVar { get; private set; }

        /// <summary>This session's issue (null until one is created).</summary>
        public static int IssueNumber { get; private set; }
        public static string IssueUrl { get; private set; }
        public static bool Busy => s_busy;
        /// <summary>Fired on the main thread after an upload finishes (issue URL, or null on failure).</summary>
        public static event Action<string> UploadFinished;

        static bool s_busy;
        static bool s_labelEnsured;
        static int s_pendingErrors;          // errors not uploaded yet
        static string s_firstError;          // first error line of the session
        static long s_uploadedSeq;           // newest log sequence already uploaded
        static float s_lastUploadTime = -9999f;
        static int s_autoComments;
        static bool s_capWarned;
        static int s_failedAttempts;
        static float s_nextAttemptTime;

        internal static void Init()
        {
            AutoVar = ConCommands.CreateVar("log_upload_auto", "1",
                "Upload this session's errors to GitHub issues (label cmod-log) automatically when a token is set (0/1).",
                0f, 1f, archive: true);
            CmodLog.LineAdded += OnLine;
            // Errors logged during boot, before the services were up.
            var earlier = CmodLog.Lines;
            for (int i = 0; i < earlier.Count; i++)
                if (earlier[i].Level == LogLevel.Error) NoteError(earlier[i].Text);
        }

        static bool AutoEnabled => (AutoVar == null || AutoVar.Bool) && GitHubClient.HasToken;

        static void OnLine(LogLine line)
        {
            if (line.Level == LogLevel.Error) NoteError(line.Text);
        }

        static void NoteError(string text)
        {
            if (s_firstError == null) s_firstError = FirstLine(text);
            s_pendingErrors++;
        }

        /// <summary>Called every frame by ServiceHost: creates the issue on the first error and
        /// batches later errors into rate-limited comments.</summary>
        internal static void Tick()
        {
            if (s_busy || s_pendingErrors == 0 || !AutoEnabled) return;
            if (s_failedAttempts >= MaxAttempts) return;
            float now = Time.realtimeSinceStartup;
            if (now < s_nextAttemptTime) return;

            if (IssueNumber == 0)
            {
                ServiceHost.Run(CreateIssueRoutine(null, true, null), "log upload");
                return;
            }

            if (s_autoComments >= MaxAutoComments)
            {
                if (!s_capWarned)
                {
                    s_capWarned = true;
                    CmodLog.Warn($"[log upload] {MaxAutoComments} automatic log comments sent this session; no more will be sent automatically. Use log_upload to send more.");
                }
                return;
            }
            if (now - s_lastUploadTime < CommentInterval) return;
            ServiceHost.Run(CommentRoutine(null, true, null), "log upload");
        }

        internal static void OnQuit()
        {
            if (s_pendingErrors > 0 && IssueNumber != 0 && AutoEnabled)
                CmodLog.Info($"[log upload] {s_pendingErrors} error(s) were not uploaded before quitting; they are in {LogFile.CurrentPath}.");
        }

        /// <summary>Manual upload (log_upload command, Send Debug Log dialog). Adds a comment to this
        /// session's issue if there is one, otherwise creates an issue. `done` gets the issue URL or null.</summary>
        public static void UploadManual(string note, Action<string> done)
        {
            if (!GitHubClient.HasToken)
            {
                CmodLog.Warn("[log upload] No GitHub token set (Start > GitHub Settings...).");
                done?.Invoke(null);
                return;
            }
            if (s_busy)
            {
                CmodLog.Warn("[log upload] An upload is already running, try again in a moment.");
                done?.Invoke(null);
                return;
            }
            CmodLog.Info("[log upload] Uploading the debug log to GitHub...");
            ServiceHost.Run(IssueNumber == 0 ? CreateIssueRoutine(note, false, done) : CommentRoutine(note, false, done), "log upload");
        }

        // ---------------------------------------------------------------------------------------

        static IEnumerator EnsureLabel()
        {
            if (s_labelEnsured) yield break;
            s_labelEnsured = true;
            var json = JsonUtility.ToJson(new NewLabelJson
            {
                name = Label,
                color = "008080", // Cmod teal
                description = "Debug log uploaded by the Cmod game",
            });
            // 201 = created, 422 = already exists. Anything else is fine too: GitHub also creates
            // missing labels when an issue uses them (for users with push access).
            yield return GitHubClient.PostJson(GitHubClient.RepoApi + "/labels", json, r => { });
        }

        static IEnumerator CreateIssueRoutine(string note, bool automatic, Action<string> done)
        {
            s_busy = true;
            string url = null;
            try
            {
                yield return EnsureLabel();

                long seqAtStart = LogFile.LastSequence;
                int errorsAtStart = s_pendingErrors;
                var title = BuildTitle(note);
                var body = BuildIssueBody(note, automatic);
                var json = JsonUtility.ToJson(new NewIssueJson { title = title, body = body, labels = new[] { Label } });

                GitHubResponse resp = null;
                yield return GitHubClient.PostJson(GitHubClient.RepoApi + "/issues", json, r => resp = r);

                var issue = resp != null && resp.Ok ? GitHubClient.ParseJson<GitHubIssue>(resp.Text) : null;
                if (issue != null && issue.number > 0)
                {
                    IssueNumber = issue.number;
                    IssueUrl = issue.html_url;
                    url = IssueUrl;
                    s_uploadedSeq = seqAtStart;
                    s_pendingErrors = Math.Max(0, s_pendingErrors - errorsAtStart);
                    s_lastUploadTime = Time.realtimeSinceStartup;
                    s_failedAttempts = 0;
                    CmodLog.Info($"[log upload] Log uploaded: {IssueUrl}");
                }
                else
                {
                    Failed(resp, automatic);
                }
            }
            finally
            {
                s_busy = false;
            }
            Finish(done, url);
        }

        static IEnumerator CommentRoutine(string note, bool automatic, Action<string> done)
        {
            s_busy = true;
            string url = null;
            try
            {
                long seqAtStart = LogFile.LastSequence;
                int errorsAtStart = s_pendingErrors;
                var body = BuildCommentBody(note, automatic);
                var json = JsonUtility.ToJson(new NewCommentJson { body = body });

                GitHubResponse resp = null;
                yield return GitHubClient.PostJson($"{GitHubClient.RepoApi}/issues/{IssueNumber}/comments", json, r => resp = r);

                if (resp != null && resp.Ok)
                {
                    s_uploadedSeq = seqAtStart;
                    s_pendingErrors = Math.Max(0, s_pendingErrors - errorsAtStart);
                    s_lastUploadTime = Time.realtimeSinceStartup;
                    s_failedAttempts = 0;
                    if (automatic) s_autoComments++;
                    url = IssueUrl;
                    CmodLog.Info(automatic
                        ? $"[log upload] New errors added to {IssueUrl} ({s_autoComments}/{MaxAutoComments})"
                        : $"[log upload] Log added to {IssueUrl}");
                }
                else
                {
                    Failed(resp, automatic);
                }
            }
            finally
            {
                s_busy = false;
            }
            Finish(done, url);
        }

        static void Failed(GitHubResponse resp, bool automatic)
        {
            var msg = resp?.ErrorMessage ?? "no response";
            if (automatic)
            {
                s_failedAttempts++;
                s_nextAttemptTime = Time.realtimeSinceStartup + RetryDelay;
                CmodLog.Warn(s_failedAttempts >= MaxAttempts
                    ? $"[log upload] Upload failed ({msg}). Giving up on automatic uploads this session."
                    : $"[log upload] Upload failed ({msg}). Retrying in {RetryDelay:0} s.");
            }
            else
            {
                CmodLog.Warn($"[log upload] Upload failed: {msg}");
            }
        }

        static void Finish(Action<string> done, string url)
        {
            try { done?.Invoke(url); } catch (Exception ex) { CmodLog.Warn("[log upload] " + ex.Message); }
            try { UploadFinished?.Invoke(url); } catch (Exception ex) { CmodLog.Warn("[log upload] " + ex.Message); }
        }

        // ---- Text building -------------------------------------------------------------------

        static string BuildTitle(string note)
        {
            string what = !string.IsNullOrWhiteSpace(note) ? "manual: " + FirstLine(note)
                        : s_firstError ?? "manual report";
            what = Truncate(what, 80);
            var title = $"[cmod-log] {CmodSystemInfo.PlayerName} v{Application.version} {CmodSystemInfo.PlatformName} \u2013 {what}";
            return Redaction.Redact(title);
        }

        static string BuildIssueBody(string note, bool automatic)
        {
            var head = new StringBuilder();
            head.Append("Debug log uploaded ").Append(automatic ? "automatically after an error" : "manually")
                .Append(" by **").Append(EscapeMd(CmodSystemInfo.PlayerName)).Append("**.\n\n");
            if (!string.IsNullOrWhiteSpace(note))
                head.Append("### Note\n").Append(NoteText(note)).Append("\n\n");
            head.Append("### System\n| | |\n|---|---|\n");
            foreach (var line in CmodSystemInfo.Lines())
            {
                int c = line.IndexOf(':');
                head.Append("| ").Append(EscapeMd(line.Substring(0, c))).Append(" | ")
                    .Append(EscapeMd(line.Substring(c + 1).Trim())).Append(" |\n");
            }
            head.Append("| Session | ").Append(SessionTime()).Append(" |\n");
            if (s_firstError != null)
                head.Append("\n### First error\n```text\n").Append(Fence(s_firstError)).Append("\n```\n");
            head.Append("\n### Log (last ").Append(LogFile.RecentCapacity).Append(" lines)\n");

            return Redaction.Redact(WithLog(head.ToString(), LogFile.LinesSince(0),
                "\n_Later errors in this session are added as comments._"));
        }

        static string BuildCommentBody(string note, bool automatic)
        {
            var lines = LogFile.LinesSince(s_uploadedSeq);
            var head = new StringBuilder();
            if (automatic)
                head.Append("**New errors** (").Append(s_pendingErrors).Append(" since the last upload, session time ")
                    .Append(SessionTime()).Append(")\n\n");
            else
            {
                head.Append("**Manual log upload** (session time ").Append(SessionTime()).Append(")\n\n");
                if (!string.IsNullOrWhiteSpace(note)) head.Append("> ").Append(NoteText(note).Replace("\n", "\n> ")).Append("\n\n");
                if (lines.Count == 0) lines = LogFile.LinesSince(0); // nothing new: send the recent log again
            }
            return Redaction.Redact(WithLog(head.ToString(), lines, null));
        }

        /// <summary>head + the log lines in a code block (repeats collapsed) + footer, keeping the
        /// newest lines when the result would exceed <see cref="MaxBodyChars"/>.</summary>
        static string WithLog(string head, List<string> lines, string footer)
        {
            lines = CollapseRepeats(lines);
            const string open = "```text\n", close = "\n```\n";
            const string cutNote = "... (older lines cut to fit) ...\n";
            footer = footer ?? "";
            int budget = MaxBodyChars - head.Length - open.Length - close.Length - footer.Length - cutNote.Length - 64;

            // Walk backwards from the newest line until the budget is used up.
            int start = lines.Count;
            int used = 0;
            while (start > 0)
            {
                int len = lines[start - 1].Length + 1;
                if (used + len > budget) break;
                used += len;
                start--;
            }

            var sb = new StringBuilder(head.Length + used + 256);
            sb.Append(head).Append(open);
            if (start > 0) sb.Append(cutNote);
            if (start == lines.Count && lines.Count > 0)
            {
                // Even the newest line alone is too long: keep its end.
                var last = lines[lines.Count - 1];
                sb.Append(Fence(last.Substring(Math.Max(0, last.Length - Math.Max(0, budget)))));
            }
            for (int i = start; i < lines.Count; i++)
            {
                sb.Append(Fence(lines[i]));
                if (i < lines.Count - 1) sb.Append('\n');
            }
            if (lines.Count == 0) sb.Append("(no log lines)");
            sb.Append(close).Append(footer);
            var s = sb.ToString();
            return s.Length > MaxBodyChars ? s.Substring(0, MaxBodyChars) : s;
        }

        /// <summary>Collapses runs of identical messages (ignoring the timestamp), e.g. an exception
        /// thrown every frame, into the first line plus "(repeated N more times)".</summary>
        static List<string> CollapseRepeats(List<string> lines)
        {
            var result = new List<string>(lines.Count);
            string prevKey = null;
            int repeats = 0;
            foreach (var line in lines)
            {
                var key = line.Length > 15 && line[0] == '[' ? line.Substring(15) : line; // strip "[HH:mm:ss.fff] "
                if (key == prevKey) { repeats++; continue; }
                if (repeats > 0) result.Add($"                 (repeated {repeats} more time{(repeats == 1 ? "" : "s")})");
                repeats = 0;
                prevKey = key;
                result.Add(line);
            }
            if (repeats > 0) result.Add($"                 (repeated {repeats} more time{(repeats == 1 ? "" : "s")})");
            return result;
        }

        static string SessionTime()
        {
            var t = TimeSpan.FromSeconds(Time.realtimeSinceStartup);
            return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        }

        static string FirstLine(string s)
        {
            s = (s ?? "").Trim();
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl >= 0 ? s.Substring(0, nl).Trim() : s;
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 3) + "...";

        /// <summary>Keeps log text from closing the surrounding code fence.</summary>
        static string Fence(string s) => s.Replace("```", "'''");

        static string NoteText(string note) => (note ?? "").Trim().Replace("\r", "");

        static string EscapeMd(string s) => (s ?? "").Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
    }
}
