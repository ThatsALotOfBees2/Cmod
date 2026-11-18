using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cmod.Services
{
    public enum UpdateStage { Idle, Preparing, Downloading, Extracting, Installing, Failed, Cancelled }

    /// <summary>
    /// Downloads, unpacks and installs a release:
    ///  1. downloads the platform zip into persistentDataPath/update (UpdateWindow shows the progress),
    ///  2. extracts it to update/extracted (System.IO.Compression, with path-traversal checks),
    ///  3. writes an updater script (Windows .bat with robocopy, Linux/macOS .sh) that waits for this
    ///     process to exit, mirrors the new build over the install folder, relaunches Cmod and deletes itself,
    ///  4. starts the script and quits.
    /// Only the folders and files that the new build contains are replaced: every top-level folder of the
    /// build (Cmod_Data, MonoBleedingEdge, ...) is mirrored, top-level files are overwritten, and anything
    /// else in the install folder (the player's own files) is left alone. Saves, settings and logs live in
    /// persistentDataPath / PlayerPrefs, outside the install folder, so they are never touched.
    /// Private repos: the zip is fetched through the asset API with the token (GitHubClient.Download).
    /// </summary>
    public static class UpdateInstaller
    {
        public static bool Running { get; private set; }
        public static UpdateStage Stage { get; private set; }
        /// <summary>One-line status for the progress dialog.</summary>
        public static string Status { get; private set; } = "";
        /// <summary>Why the update failed (Stage == Failed).</summary>
        public static string Error { get; private set; }
        public static GitHubRelease Release { get; private set; }
        public static GitHubAsset Asset { get; private set; }
        public static DownloadProgress Download { get; private set; }
        /// <summary>0..1 while extracting.</summary>
        public static float ExtractFraction => s_extractTotal > 0 ? Mathf.Clamp01((float)s_extractDone / s_extractTotal) : 0f;
        /// <summary>Fired whenever Stage changes.</summary>
        public static event Action StageChanged;

        public static string UpdateDir => Path.Combine(Application.persistentDataPath, "update");
        public static string ExtractDir => Path.Combine(UpdateDir, "extracted");
        public static string UpdaterLogPath => Path.Combine(UpdateDir, "updater.log");

        static int s_extractDone, s_extractTotal;
        static volatile bool s_cancelExtract;

        /// <summary>Starts installing the release (shows the progress window). Ignored in the editor.</summary>
        public static void Begin(GitHubRelease release)
        {
            if (Running) { CmodServices.Update?.ShowProgress(); return; }
            if (release == null) return;
            Release = release;
            Asset = null;
            Error = null;
            Download = null;
            Running = true;
            SetStage(UpdateStage.Preparing, "Preparing the update...");
            CmodServices.Update?.ShowProgress();
            ServiceHost.Run(Run(), "update");
        }

        /// <summary>Cancels a download or extraction in progress (not once the installer has started).</summary>
        public static void Cancel()
        {
            if (!Running || Stage == UpdateStage.Installing) return;
            if (Download != null) Download.CancelRequested = true;
            s_cancelExtract = true;
        }

        /// <summary>Resets a finished (failed or cancelled) update so the window can close.</summary>
        public static void Dismiss()
        {
            if (Running) return;
            SetStage(UpdateStage.Idle, "");
        }

        static IEnumerator Run()
        {
            s_cancelExtract = false;

            // ---- 1. Checks ---------------------------------------------------------------------
            if (Application.isEditor) { Fail("Updates are never installed in the Unity Editor."); yield break; }

            Asset = AutoUpdater.PickAsset(Release);
            if (Asset == null)
            {
                Fail($"Release {Release.tag_name} has no download for {CmodSystemInfo.PlatformName}. " +
                     "Download it manually from the release page.");
                yield break;
            }

            InstallLayout layout;
            string layoutError;
            if (!InstallLayout.TryDetect(out layout, out layoutError)) { Fail(layoutError); yield break; }
            if (!IsWritable(layout.WriteCheckDir))
            {
                Fail($"Cmod can't write to its install folder:\n{layout.InstallPath}\n\n" +
                     "Move Cmod to a folder you own (not Program Files), or download the update manually.");
                yield break;
            }

            try
            {
                Directory.CreateDirectory(UpdateDir);
                CleanupLeftovers();
            }
            catch (Exception ex) { Fail("Could not prepare the update folder: " + ex.Message); yield break; }

            // ---- 2. Download -----------------------------------------------------------------------
            var zipPath = Path.Combine(UpdateDir, SafeFileName(Asset.name));
            Download = new DownloadProgress { Total = Asset.size > 0 ? (ulong)Asset.size : 0 };
            SetStage(UpdateStage.Downloading, $"Downloading {Asset.name}...");
            CmodLog.Info($"[update] Downloading {Asset.name} ({AutoUpdater.FormatBytes(Asset.size)}) from {Release.tag_name}...");

            // With a token, use the asset API (works for private repos); otherwise the public URL.
            bool viaApi = GitHubClient.HasToken && !string.IsNullOrEmpty(Asset.url);
            var url = viaApi ? Asset.url : Asset.browser_download_url;
            GitHubResponse resp = null;
            yield return GitHubClient.Download(url, viaApi, zipPath, Download, r => resp = r);

            if (resp == null || resp.Cancelled || Download.CancelRequested) { Cancelled(); yield break; }
            if (!resp.Ok)
            {
                Fail("The download failed. " + resp.ErrorMessage +
                     (!viaApi && resp.Status == 404 ? "\nIf the repository is private, add a token in GitHub Settings." : ""));
                yield break;
            }
            long actual = SafeLength(zipPath);
            if (Asset.size > 0 && actual != Asset.size)
            {
                Fail($"The download is incomplete ({AutoUpdater.FormatBytes(actual)} of {AutoUpdater.FormatBytes(Asset.size)}). Please try again.");
                yield break;
            }

            // ---- 3. Extract (on a worker thread) ---------------------------------------------------
            SetStage(UpdateStage.Extracting, "Extracting files...");
            s_extractDone = 0;
            s_extractTotal = 0;
            var task = Task.Run(() => Extract(zipPath, ExtractDir));
            while (!task.IsCompleted) yield return null;
            if (s_cancelExtract) { Cancelled(); yield break; }
            if (task.IsFaulted)
            {
                var ex = task.Exception?.GetBaseException();
                Fail("The update could not be extracted: " + (ex?.Message ?? "unknown error"));
                yield break;
            }

            string newRoot, newExe, findError;
            if (!layout.FindNewBuild(ExtractDir, out newRoot, out newExe, out findError)) { Fail(findError); yield break; }

            // ---- 4. Hand over to the updater script and quit ------------------------------------------
            SetStage(UpdateStage.Installing, "Installing. Cmod will close and restart...");
            string scriptPath;
            try
            {
                scriptPath = layout.WriteScript(UpdateDir, newRoot, newExe, zipPath);
            }
            catch (Exception ex) { Fail("Could not write the updater script: " + ex.Message); yield break; }

            AutoUpdater.MarkUpdating();
            CmodLog.Info($"[update] Installing {Release.tag_name}: Cmod will close, update itself and restart.");
            LogFile.Flush();

            yield return new WaitForSecondsRealtime(1.0f); // let the player read the dialog

            try { layout.StartScript(scriptPath, UpdateDir); }
            catch (Exception ex)
            {
                AutoUpdater.ClearUpdating();
                Fail("Could not start the updater: " + ex.Message);
                yield break;
            }

            Running = false;
            Application.Quit();
        }

        static void Extract(string zipPath, string destDir)
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            Directory.CreateDirectory(destDir);
            var root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using (var archive = ZipFile(zipPath))
            {
                s_extractTotal = archive.Entries.Count;
                var buffer = new byte[81920];
                foreach (var entry in archive.Entries)
                {
                    if (s_cancelExtract) return;
                    var name = entry.FullName.Replace('\\', '/');
                    var target = Path.GetFullPath(Path.Combine(destDir, name));
                    // Zip-slip guard: every entry must stay inside the destination folder.
                    if (!target.StartsWith(root, StringComparison.Ordinal))
                        throw new IOException("The archive contains an unsafe path: " + entry.FullName);

                    if (name.EndsWith("/")) { Directory.CreateDirectory(target); }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var src = entry.Open())
                        using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            int n;
                            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (s_cancelExtract) return;
                                dst.Write(buffer, 0, n);
                            }
                        }
                    }
                    Interlocked.Increment(ref s_extractDone);
                }
            }
        }

        static ZipArchive ZipFile(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new ZipArchive(fs, ZipArchiveMode.Read, false);
        }

        /// <summary>Removes old downloads and extracted files (keeps updater.log). Called at startup.</summary>
        internal static void CleanupLeftovers()
        {
            if (Running && Stage != UpdateStage.Preparing) return;
            if (!Directory.Exists(UpdateDir)) return;
            try { if (Directory.Exists(ExtractDir)) Directory.Delete(ExtractDir, true); } catch (Exception) { }
            foreach (var f in Directory.GetFiles(UpdateDir))
            {
                if (string.Equals(Path.GetFileName(f), "updater.log", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); } catch (Exception) { /* the script may still be deleting itself */ }
            }
        }

        static void Fail(string message)
        {
            Error = message;
            Running = false;
            SetStage(UpdateStage.Failed, "The update failed.");
            CmodLog.Warn("[update] " + message.Replace("\n", " "));
            CmodServices.Update?.ShowProgress();
        }

        static void Cancelled()
        {
            Running = false;
            SetStage(UpdateStage.Cancelled, "The update was cancelled.");
            CmodLog.Info("[update] Update cancelled.");
            try { CleanupLeftovers(); } catch (Exception) { }
        }

        static void SetStage(UpdateStage stage, string status)
        {
            Stage = stage;
            Status = status ?? "";
            try { StageChanged?.Invoke(); } catch (Exception ex) { CmodLog.Warn("[update] " + ex.Message); }
        }

        static bool IsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".cmod_write_test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch (Exception) { return -1; }
        }

        static string SafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrEmpty(name) ? "update.zip" : name;
        }

        // =========================================================================================

        /// <summary>Where the running game is installed and how to replace it, per platform.</summary>
        sealed class InstallLayout
        {
            public enum Kind { Windows, Linux, Mac }
            public Kind Platform;
            /// <summary>Windows/Linux: the folder with the executable. macOS: the .app bundle.</summary>
            public string InstallPath;
            public string WriteCheckDir;

            public static bool TryDetect(out InstallLayout layout, out string error)
            {
                layout = null;
                error = null;
                var data = Path.GetFullPath(Application.dataPath);
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.LinuxPlayer:
                        // dataPath = <install>/<Exe>_Data
                        var install = Path.GetDirectoryName(data);
                        if (string.IsNullOrEmpty(install)) { error = "Could not find the install folder."; return false; }
                        layout = new InstallLayout
                        {
                            Platform = Application.platform == RuntimePlatform.WindowsPlayer ? Kind.Windows : Kind.Linux,
                            InstallPath = install,
                            WriteCheckDir = install,
                        };
                        return true;
                    case RuntimePlatform.OSXPlayer:
                        // dataPath = <...>/Cmod.app/Contents
                        var app = Path.GetDirectoryName(data);
                        if (string.IsNullOrEmpty(app) || !app.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                        { error = "Could not find the Cmod.app bundle."; return false; }
                        layout = new InstallLayout { Platform = Kind.Mac, InstallPath = app, WriteCheckDir = data };
                        return true;
                    default:
                        error = $"Automatic updates are not supported on {Application.platform}.";
                        return false;
                }
            }

            /// <summary>Finds the build inside the extracted zip (it may be wrapped in one folder).
            /// newRoot = folder to copy from (macOS: the .app), newExe = executable path after install.</summary>
            public bool FindNewBuild(string extracted, out string newRoot, out string newExe, out string error)
            {
                newRoot = newExe = error = null;
                foreach (var dir in CandidateDirs(extracted))
                {
                    if (Platform == Kind.Mac)
                    {
                        foreach (var sub in Directory.GetDirectories(dir))
                            if (sub.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                            {
                                newRoot = sub;
                                newExe = InstallPath; // relaunch with `open <bundle>`
                                return true;
                            }
                        continue;
                    }

                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        var name = Path.GetFileName(sub);
                        if (!name.EndsWith("_Data", StringComparison.Ordinal)) continue;
                        var exeBase = name.Substring(0, name.Length - "_Data".Length);
                        var candidates = Platform == Kind.Windows
                            ? new[] { exeBase + ".exe" }
                            : new[] { exeBase + ".x86_64", exeBase, exeBase + ".x86" };
                        foreach (var c in candidates)
                            if (File.Exists(Path.Combine(dir, c)))
                            {
                                newRoot = dir;
                                newExe = Path.Combine(InstallPath, c);
                                return true;
                            }
                    }
                }
                error = "The downloaded update doesn't look like a Cmod build for " + CmodSystemInfo.PlatformName + ".";
                return false;
            }

            static IEnumerable<string> CandidateDirs(string extracted)
            {
                yield return extracted;
                // Zips often wrap everything in one folder; look one and two levels down.
                foreach (var a in Directory.GetDirectories(extracted))
                {
                    yield return a;
                    foreach (var b in Directory.GetDirectories(a)) yield return b;
                }
            }

            public string WriteScript(string updateDir, string newRoot, string newExe, string zipPath)
            {
                int pid = Process.GetCurrentProcess().Id;
                if (Platform == Kind.Windows)
                {
                    var path = Path.Combine(updateDir, "cmod_update.bat");
                    File.WriteAllText(path, WindowsScript(pid, newRoot, newExe, zipPath), new UTF8Encoding(false));
                    return path;
                }
                else
                {
                    var path = Path.Combine(updateDir, "cmod_update.sh");
                    // Unix line endings only.
                    File.WriteAllText(path, UnixScript(pid, newRoot, newExe, zipPath).Replace("\r\n", "\n"), new UTF8Encoding(false));
                    return path;
                }
            }

            public void StartScript(string scriptPath, string workDir)
            {
                ProcessStartInfo psi;
                if (Platform == Kind.Windows)
                {
                    psi = new ProcessStartInfo("cmd.exe", "/c \"\"" + scriptPath + "\"\"")
                    {
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    };
                }
                else
                {
                    psi = new ProcessStartInfo("/bin/sh", "\"" + scriptPath + "\"");
                }
                psi.UseShellExecute = false;
                psi.WorkingDirectory = workDir;
                var p = Process.Start(psi);
                if (p == null) throw new InvalidOperationException("the process did not start");
            }

            // ---- Windows .bat ------------------------------------------------------------------
            string WindowsScript(int pid, string src, string exe, string zip)
            {
                // Normalize to backslashes (persistentDataPath uses '/', Path.Combine adds '\\'), then escape
                // % which is special in batch files, even in quotes.
                string Q(string s)
                {
                    try { s = Path.GetFullPath(s); } catch (Exception) { /* keep as is */ }
                    return s.Replace('/', '\\').Replace("%", "%%");
                }
                var log = Q(UpdaterLogPath);
                var sb = new StringBuilder();
                sb.Append("@echo off\r\n");
                // The file is UTF-8; without this cmd reads it in the OEM code page and garbles
                // non-ASCII paths (e.g. C:\Users\José\...). cmd re-reads each line, so it applies from here on.
                sb.Append("chcp 65001 >NUL\r\n");
                sb.Append("rem Cmod updater: waits for the game to exit, copies the new build, relaunches it.\r\n");
                sb.Append("setlocal EnableExtensions\r\n");
                sb.Append($"set \"LOG={log}\"\r\n");
                sb.Append("echo Cmod updater started %DATE% %TIME% > \"%LOG%\"\r\n");
                sb.Append("set /a TRIES=0\r\n");
                sb.Append(":wait\r\n");
                sb.Append($"tasklist /FI \"PID eq {pid}\" /NH 2>NUL | find \" {pid} \" >NUL\r\n");
                sb.Append("if errorlevel 1 goto copy\r\n");
                sb.Append("set /a TRIES+=1\r\n");
                sb.Append($"if %TRIES% GEQ 60 taskkill /PID {pid} /F >NUL 2>&1\r\n");
                sb.Append("ping -n 2 127.0.0.1 >NUL\r\n");
                sb.Append("goto wait\r\n");
                sb.Append(":copy\r\n");
                // Mirror each top-level folder of the new build; /MIR only purges inside those folders.
                foreach (var dir in Directory.GetDirectories(src))
                {
                    var name = Path.GetFileName(dir);
                    sb.Append($"robocopy \"{Q(dir)}\" \"{Q(Path.Combine(InstallPath, name))}\" /MIR /R:10 /W:1 /NP /NFL /NDL >> \"%LOG%\"\r\n");
                    sb.Append("if errorlevel 8 goto fail\r\n");
                }
                // Top-level files (the exe, UnityPlayer.dll, ...): copy and overwrite, never delete others.
                sb.Append($"robocopy \"{Q(src)}\" \"{Q(InstallPath)}\" /IS /IT /R:10 /W:1 /NP /NFL /NDL >> \"%LOG%\"\r\n");
                sb.Append("if errorlevel 8 goto fail\r\n");
                sb.Append("echo OK >> \"%LOG%\"\r\n");
                sb.Append($"rmdir /S /Q \"{Q(ExtractDir)}\" >NUL 2>&1\r\n");
                sb.Append($"del /Q \"{Q(zip)}\" >NUL 2>&1\r\n");
                sb.Append($"start \"\" \"{Q(exe)}\"\r\n");
                sb.Append("goto done\r\n");
                sb.Append(":fail\r\n");
                sb.Append("echo FAILED: robocopy returned %ERRORLEVEL% >> \"%LOG%\"\r\n");
                sb.Append($"start \"\" \"{Q(exe)}\"\r\n");
                sb.Append(":done\r\n");
                // Deletes this script; the (goto) trick avoids "batch file cannot be found".
                sb.Append("(goto) 2>NUL & del \"%~f0\"\r\n");
                return sb.ToString();
            }

            // ---- Linux / macOS .sh -------------------------------------------------------------
            string UnixScript(int pid, string src, string exe, string zip)
            {
                string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
                var sb = new StringBuilder();
                sb.Append("#!/bin/sh\n");
                sb.Append("# Cmod updater: waits for the game to exit, copies the new build, relaunches it.\n");
                sb.Append("trap '' HUP\n");
                sb.Append($"PID={pid}\n");
                sb.Append($"SRC={Q(src)}\n");
                sb.Append($"DST={Q(InstallPath)}\n");
                sb.Append($"EXE={Q(exe)}\n");
                sb.Append($"ZIP={Q(zip)}\n");
                sb.Append($"EXTRACTED={Q(ExtractDir)}\n");
                sb.Append($"LOG={Q(UpdaterLogPath)}\n");
                sb.Append("echo \"Cmod updater started $(date)\" > \"$LOG\"\n");
                sb.Append("i=0\n");
                sb.Append("while kill -0 \"$PID\" 2>/dev/null; do\n");
                sb.Append("  i=$((i+1)); [ \"$i\" -ge 60 ] && kill -9 \"$PID\" 2>/dev/null\n");
                sb.Append("  sleep 1\n");
                sb.Append("done\n");
                sb.Append("mirror() {\n");
                sb.Append("  if command -v rsync >/dev/null 2>&1; then rsync -a --delete \"$1/\" \"$2/\" >> \"$LOG\" 2>&1\n");
                sb.Append("  else rm -rf \"$2\" && cp -R \"$1\" \"$2\" >> \"$LOG\" 2>&1; fi\n");
                sb.Append("}\n");
                sb.Append("fail() { echo \"FAILED: $1\" >> \"$LOG\"; relaunch; rm -f \"$0\"; exit 1; }\n");
                if (Platform == Kind.Mac)
                {
                    sb.Append("relaunch() { open \"$EXE\" >/dev/null 2>&1 & }\n");
                    sb.Append("mkdir -p \"$DST\"\n");
                    sb.Append("mirror \"$SRC\" \"$DST\" || fail \"copy app bundle\"\n");
                    sb.Append("chmod +x \"$DST\"/Contents/MacOS/* 2>/dev/null\n");
                    sb.Append("xattr -dr com.apple.quarantine \"$DST\" 2>/dev/null\n");
                }
                else
                {
                    sb.Append("relaunch() { nohup \"$EXE\" >/dev/null 2>&1 & }\n");
                    // Mirror every top-level folder of the new build, overwrite top-level files.
                    sb.Append("for d in \"$SRC\"/*/; do\n");
                    sb.Append("  [ -d \"$d\" ] || continue\n");
                    sb.Append("  n=$(basename \"$d\")\n");
                    sb.Append("  mirror \"$SRC/$n\" \"$DST/$n\" || fail \"copy $n\"\n");
                    sb.Append("done\n");
                    sb.Append("for f in \"$SRC\"/*; do\n");
                    sb.Append("  [ -f \"$f\" ] || continue\n");
                    sb.Append("  cp -f \"$f\" \"$DST/\" || fail \"copy $f\"\n");
                    sb.Append("done\n");
                    // Zip extraction drops the executable bit.
                    sb.Append("chmod +x \"$EXE\" 2>/dev/null\n");
                }
                sb.Append("echo OK >> \"$LOG\"\n");
                sb.Append("rm -rf \"$EXTRACTED\" \"$ZIP\"\n");
                sb.Append("relaunch\n");
                sb.Append("rm -f \"$0\"\n");
                return sb.ToString();
            }
        }
    }
}
