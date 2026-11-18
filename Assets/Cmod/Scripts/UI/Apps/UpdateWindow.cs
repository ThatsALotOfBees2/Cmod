using System;
using System.Text;
using Cmod.Services;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// The update dialog, in two modes:
    ///  - Progress: a Win98 "Copying..." style dialog for UpdateInstaller (status, segmented progress bar,
    ///    "12.3 MB of 45.6 MB (1.2 MB/sec), 30 sec left", Cancel). Shows the error when it fails.
    ///  - Notes: the release notes of AutoUpdater.Latest with "Update Now", "View on GitHub" and "Close".
    /// </summary>
    public sealed class UpdateWindow : Win98Window
    {
        enum Mode { Progress, Notes }

        const float Pad = 10f;
        const float BtnW = 75f, BtnH = 23f;
        static readonly Vector2 ProgressSize = new Vector2(400, 190);
        static readonly Vector2 NotesSize = new Vector2(480, 400);

        Mode _mode = Mode.Progress;
        GitHubRelease _notesRelease;
        string _notesText = "";
        Vector2 _scroll;
        float _notesHeight = -1f, _notesWidth = -1f;

        public UpdateWindow()
        {
            Title = "Cmod Update";
            IconName = "update";
            Rect = new Rect(0, 0, ProgressSize.x, ProgressSize.y);
            Minimizable = true;
        }

        /// <summary>Shows the installer progress (called by UpdateInstaller).</summary>
        public void ShowProgress()
        {
            bool resize = _mode != Mode.Progress || !IsOpen;
            _mode = Mode.Progress;
            Title = "Cmod Update";
            if (resize)
            {
                Rect.width = ProgressSize.x;
                Rect.height = ProgressSize.y;
                CenterOnScreen();
            }
            Open();
        }

        /// <summary>Shows the release notes for a release.</summary>
        public void ShowNotes(GitHubRelease release)
        {
            if (release == null) return;
            if (UpdateInstaller.Running) { ShowProgress(); return; }
            _mode = Mode.Notes;
            _notesRelease = release;
            _notesText = CleanMarkdown(release.body);
            _notesHeight = -1f;
            _scroll = Vector2.zero;
            Title = "Release Notes - " + release.tag_name;
            Rect.width = NotesSize.x;
            Rect.height = NotesSize.y;
            CenterOnScreen();
            Open();
        }

        public override void OnClosed()
        {
            if (_mode != Mode.Progress) return;
            // Closing the progress dialog cancels a running download.
            if (UpdateInstaller.Running) UpdateInstaller.Cancel();
            else UpdateInstaller.Dismiss();
        }

        public override void DrawContents(Rect c)
        {
            if (_mode == Mode.Notes) DrawNotes(c);
            else DrawProgress(c);
        }

        // ---- Progress -----------------------------------------------------------------------------

        void DrawProgress(Rect c)
        {
            float x = c.x + Pad, w = c.width - Pad * 2, y = c.y + Pad;
            var stage = UpdateInstaller.Stage;
            var rel = UpdateInstaller.Release;
            var asset = UpdateInstaller.Asset;

            Win98.Icon(new Rect(x, y, 32, 32), stage == UpdateStage.Failed ? "error" : "update");
            float tx = x + 42, tw = w - 42;
            Win98.Label(new Rect(tx, y, tw, 16), UpdateInstaller.Status, true);
            var from = rel != null ? $"{rel.tag_name} from github.com/{GitHubClient.Owner}/{GitHubClient.Repo}" : "";
            Win98.Label(new Rect(tx, y + 16, tw, 16), from);
            y += 42;

            float by = c.yMax - Pad - BtnH;
            switch (stage)
            {
                case UpdateStage.Failed:
                {
                    float h = Mathf.Min(GhUi.Height(UpdateInstaller.Error, w), by - 6 - y);
                    GhUi.Paragraph(new Rect(x, y, w, h), UpdateInstaller.Error ?? "Unknown error.");
                    bool hasPage = rel != null && !string.IsNullOrEmpty(rel.html_url);
                    if (Win98.Button(new Rect(c.xMax - Pad - BtnW * 2 - 36, by, BtnW + 30, BtnH), "Release Page", hasPage))
                        Application.OpenURL(rel.html_url);
                    if (Win98.Button(new Rect(c.xMax - Pad - BtnW, by, BtnW, BtnH), "Close")) Close();
                    return;
                }
                case UpdateStage.Cancelled:
                case UpdateStage.Idle:
                    Win98.Label(new Rect(x, y, w, 16), stage == UpdateStage.Cancelled ? "Nothing was changed." : "");
                    if (Win98.Button(new Rect(c.xMax - Pad - BtnW, by, BtnW, BtnH), "Close")) Close();
                    return;
            }

            // Running stages: progress bar plus details.
            float frac;
            string detail;
            var dl = UpdateInstaller.Download;
            if (stage == UpdateStage.Downloading && dl != null)
            {
                frac = dl.Fraction;
                var speed = dl.BytesPerSecond;
                var sb = new StringBuilder();
                sb.Append(AutoUpdater.FormatBytes(dl.Bytes));
                if (dl.Total > 0) sb.Append(" of ").Append(AutoUpdater.FormatBytes(dl.Total));
                if (speed > 0) sb.Append(" (").Append(AutoUpdater.FormatBytes(speed)).Append("/sec)");
                if (speed > 0 && dl.Total > dl.Bytes) sb.Append(", ").Append(FormatEta((dl.Total - dl.Bytes) / speed)).Append(" left");
                detail = sb.ToString();
            }
            else if (stage == UpdateStage.Extracting)
            {
                frac = UpdateInstaller.ExtractFraction;
                detail = $"Unpacking {asset?.name ?? "update"}... {Mathf.RoundToInt(frac * 100)}%";
            }
            else if (stage == UpdateStage.Installing)
            {
                frac = 1f;
                detail = "Cmod will close now and restart with the new version.";
            }
            else
            {
                frac = 0f;
                detail = "";
            }

            Win98.ProgressBar(new Rect(x, y, w, 18), frac);
            y += 24;
            Win98.Label(new Rect(x, y, w, 16), detail);

            bool canCancel = stage == UpdateStage.Preparing || stage == UpdateStage.Downloading || stage == UpdateStage.Extracting;
            if (Win98.Button(new Rect(c.xMax - Pad - BtnW, by, BtnW, BtnH), "Cancel", canCancel))
                UpdateInstaller.Cancel();
        }

        static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "?";
            var s = (int)Math.Ceiling(seconds);
            if (s < 60) return s + " sec";
            if (s < 3600) return $"{s / 60} min {s % 60} sec";
            return $"{s / 3600} hr {s % 3600 / 60} min";
        }

        // ---- Release notes ------------------------------------------------------------------------

        void DrawNotes(Rect c)
        {
            var rel = _notesRelease;
            if (rel == null) { _mode = Mode.Progress; return; }
            float x = c.x + Pad, w = c.width - Pad * 2, y = c.y + Pad;

            Win98.Icon(new Rect(x, y, 32, 32), "update");
            var name = string.IsNullOrEmpty(rel.name) ? rel.tag_name : rel.name;
            Win98.Label(new Rect(x + 42, y, w - 42, 16), name, true);
            var sub = $"Version {CmodGame.ParseVersion(rel.tag_name)}";
            if (!string.IsNullOrEmpty(rel.published_at) && rel.published_at.Length >= 10) sub += ", released " + rel.published_at.Substring(0, 10);
            sub += $"  (you have {Application.version})";
            Win98.Label(new Rect(x + 42, y + 16, w - 42, 16), sub);
            y += 42;

            float by = c.yMax - Pad - BtnH;
            var box = new Rect(x, y, w, by - 8 - y);
            Win98.SunkenPanel(box, true);

            // Inner text width leaves room for the vertical scrollbar.
            float innerW = box.width - 4 - 16 - 8;
            if (!Mathf.Approximately(innerW, _notesWidth) || _notesHeight < 0f)
            {
                _notesWidth = innerW;
                _notesHeight = GhUi.Height(_notesText, innerW) + 8;
            }
            var view = new Rect(box.x + 2, box.y + 2, box.width - 4, box.height - 4);
            var content = new Rect(0, 0, innerW + 8, Mathf.Max(_notesHeight, view.height - 2));
            _scroll = Win98.BeginScroll(view, _scroll, content);
            GhUi.Paragraph(new Rect(4, 4, innerW, 0), _notesText);
            Win98.EndScroll();

            bool canUpdate = AutoUpdater.UpdateAvailable && AutoUpdater.Latest == rel && !Application.isEditor;
            float bx = c.xMax - Pad - BtnW;
            if (Win98.Button(new Rect(bx, by, BtnW, BtnH), "Close")) Close();
            bx -= BtnW + 36;
            if (Win98.Button(new Rect(bx, by, BtnW + 30, BtnH), "View on GitHub", !string.IsNullOrEmpty(rel.html_url)))
                Application.OpenURL(rel.html_url);
            bx -= BtnW + 16;
            if (canUpdate && Win98.Button(new Rect(bx, by, BtnW + 10, BtnH), "Update Now"))
            {
                UpdateInstaller.Begin(rel);
            }
        }

        /// <summary>Makes GitHub's Markdown readable as plain text: drops heading marks and emphasis,
        /// turns list markers into bullets.</summary>
        static string CleanMarkdown(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "(This release has no notes.)";
            var sb = new StringBuilder(md.Length);
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                var t = line.TrimStart();
                if (t.StartsWith("#")) line = t.TrimStart('#').Trim();
                else if (t.StartsWith("- ") || t.StartsWith("* ")) line = new string(' ', Math.Min(8, line.Length - t.Length)) + "• " + t.Substring(2);
                line = line.Replace("**", "").Replace("__", "").Replace("`", "");
                sb.Append(line).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }
    }
}
