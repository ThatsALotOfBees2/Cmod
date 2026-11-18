using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Cmod.Services
{
    /// <summary>
    /// The local log file: every CmodLog line (which includes Unity's own Debug.Log output) is written
    /// to persistentDataPath/logs/cmod_yyyyMMdd_HHmmss.log. Only the newest 10 files are kept.
    /// Errors flush immediately; other lines are flushed at least every half second, so a crash
    /// loses almost nothing. Exceptions also get their full stack trace (CmodLog keeps only the top frame).
    ///
    /// It also keeps the last <see cref="RecentCapacity"/> formatted lines in memory, with a running
    /// sequence number, for <see cref="LogUploader"/>.
    /// </summary>
    public static class LogFile
    {
        public const int KeepFiles = 10;
        public const int RecentCapacity = 300;
        const float FlushInterval = 0.5f;

        public static string Directory { get; private set; }
        /// <summary>Full path of this session's log file, or null if it couldn't be opened.</summary>
        public static string CurrentPath { get; private set; }
        public static DateTime SessionStart { get; private set; } = DateTime.Now;

        /// <summary>Sequence number of the newest line (0 before any line). Lines are numbered from 1.</summary>
        public static long LastSequence => s_seq;

        static StreamWriter s_writer;
        static bool s_dirty;
        static float s_nextFlush;
        static bool s_opened;
        static bool s_writing; // re-entrancy guard (a failing write must not log into itself)

        // Ring buffer of recent lines for the uploader.
        static readonly string[] s_ring = new string[RecentCapacity];
        static readonly long[] s_ringSeq = new long[RecentCapacity];
        static long s_seq;

        /// <summary>Opens the session log, writes a system-info header and the lines logged so far,
        /// then follows CmodLog. Safe to call twice.</summary>
        public static void Open()
        {
            if (s_opened) return;
            s_opened = true;
            SessionStart = DateTime.Now;

            try
            {
                Directory = Path.Combine(Application.persistentDataPath, "logs");
                System.IO.Directory.CreateDirectory(Directory);
                PruneOldFiles(KeepFiles - 1);
                CurrentPath = Path.Combine(Directory,
                    "cmod_" + SessionStart.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");
                var stream = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                s_writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
                s_writer.WriteLine($"==== {CmodGame.GameName} log, session started {SessionStart:yyyy-MM-dd HH:mm:ss} ====");
                foreach (var line in CmodSystemInfo.Lines()) s_writer.WriteLine("# " + line);
                s_writer.WriteLine();
                s_writer.Flush();
            }
            catch (Exception ex)
            {
                s_writer = null;
                CurrentPath = null;
                Debug.LogWarning("[log file] could not open the log file: " + ex.Message);
            }

            // Lines logged before the services started (the boot banner etc.).
            var earlier = CmodLog.Lines;
            for (int i = 0; i < earlier.Count; i++) OnLine(earlier[i]);

            CmodLog.LineAdded += OnLine;
            Application.logMessageReceived += OnUnityLog;
            Flush();
        }

        /// <summary>Flushes on a timer. Called every frame by ServiceHost.</summary>
        public static void Tick()
        {
            if (s_dirty && Time.realtimeSinceStartup >= s_nextFlush) Flush();
        }

        public static void Flush()
        {
            if (s_writer == null) { s_dirty = false; return; }
            try { s_writer.Flush(); }
            catch (Exception ex) { Fail(ex); }
            s_dirty = false;
            s_nextFlush = Time.realtimeSinceStartup + FlushInterval;
        }

        public static void Close()
        {
            CmodLog.LineAdded -= OnLine;
            Application.logMessageReceived -= OnUnityLog;
            if (s_writer == null) return;
            try
            {
                s_writer.WriteLine($"==== session ended {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
                s_writer.Flush();
                s_writer.Dispose();
            }
            catch (Exception) { /* closing anyway */ }
            s_writer = null;
        }

        /// <summary>Opens the log folder in the OS file browser.</summary>
        public static void OpenFolder()
        {
            if (string.IsNullOrEmpty(Directory)) return;
            try { Application.OpenURL(new Uri(Directory).AbsoluteUri); }
            catch (Exception ex) { CmodLog.Warn("Could not open the log folder: " + ex.Message); }
        }

        /// <summary>The recent lines with a sequence number greater than `afterSeq`, oldest first,
        /// at most `max` of them (the newest are kept when there are more).</summary>
        public static List<string> LinesSince(long afterSeq, int max = RecentCapacity)
        {
            var result = new List<string>();
            long first = Math.Max(afterSeq + 1, s_seq - RecentCapacity + 1);
            first = Math.Max(first, s_seq - max + 1);
            for (long s = Math.Max(1, first); s <= s_seq; s++)
            {
                int i = (int)(s % RecentCapacity);
                if (s_ringSeq[i] == s) result.Add(s_ring[i]);
            }
            return result;
        }

        /// <summary>Formats a log line the way the file and the uploads show it: "[HH:mm:ss.fff] E text".</summary>
        public static string Format(LogLevel level, string text)
        {
            char tag = level == LogLevel.Error ? 'E' : level == LogLevel.Warning ? 'W' : 'I';
            var time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            if (text.IndexOf('\n') >= 0) text = text.Replace("\n", "\n                 "); // indent continuation lines
            return $"[{time}] {tag} {text}";
        }

        // ---------------------------------------------------------------------------------------

        static void OnLine(LogLine line)
        {
            Append(Format(line.Level, line.Text), line.Level == LogLevel.Error);
        }

        /// <summary>CmodLog keeps only the first stack frame of an exception. Add the rest here so the
        /// file (and uploaded logs) have the full trace. Runs after CmodLog's own handler.</summary>
        static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception || string.IsNullOrEmpty(stackTrace)) return;
            var frames = stackTrace.Replace("\r", "").TrimEnd('\n').Split('\n');
            if (frames.Length <= 1) return;
            var sb = new StringBuilder();
            for (int i = 1; i < frames.Length && i < 40; i++)
            {
                if (frames[i].Length == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("                   ").Append(frames[i]);
            }
            if (sb.Length > 0) Append(sb.ToString(), true);
        }

        static void Append(string formatted, bool flushNow)
        {
            if (s_writing) return;
            s_writing = true;
            try
            {
                s_seq++;
                int i = (int)(s_seq % RecentCapacity);
                s_ring[i] = formatted;
                s_ringSeq[i] = s_seq;

                if (s_writer != null)
                {
                    try
                    {
                        s_writer.WriteLine(formatted);
                        s_dirty = true;
                        if (flushNow) Flush();
                    }
                    catch (Exception ex) { Fail(ex); }
                }
            }
            finally { s_writing = false; }
        }

        static void Fail(Exception ex)
        {
            // Stop writing first, so the warning below can't recurse into the file.
            var w = s_writer;
            s_writer = null;
            try { w?.Dispose(); } catch (Exception) { }
            Debug.LogWarning("[log file] writing failed, file logging disabled: " + ex.Message);
        }

        static void PruneOldFiles(int keep)
        {
            try
            {
                var files = new List<FileInfo>(new DirectoryInfo(Directory).GetFiles("cmod_*.log"));
                // Names embed the timestamp, so ordinal order is chronological; newest first.
                files.Sort((a, b) => string.CompareOrdinal(b.Name, a.Name));
                for (int i = keep; i < files.Count; i++)
                {
                    try { files[i].Delete(); } catch (Exception) { /* in use by another instance */ }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[log file] could not prune old logs: " + ex.Message); }
        }
    }

    /// <summary>System information for log headers and uploaded reports.</summary>
    public static class CmodSystemInfo
    {
        public static string PlatformName
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: return "Windows";
                    case RuntimePlatform.LinuxPlayer: case RuntimePlatform.LinuxEditor: return "Linux";
                    case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return "macOS";
                    default: return Application.platform.ToString();
                }
            }
        }

        /// <summary>The player's name ("name" convar, else the local player's name, else "player").</summary>
        public static string PlayerName
        {
            get
            {
                var n = ConCommands.GetVar("name")?.Value;
                if (string.IsNullOrWhiteSpace(n) && CmodPlayer.Local != null) n = CmodPlayer.Local.PlayerName;
                return string.IsNullOrWhiteSpace(n) ? "player" : n.Trim();
            }
        }

        /// <summary>"Key: value" lines: version, Unity, OS, CPU, GPU, RAM, resolution.</summary>
        public static List<string> Lines()
        {
            var l = new List<string>();
            void Add(string k, Func<string> v)
            {
                string s;
                try { s = v(); } catch (Exception ex) { s = "? (" + ex.GetType().Name + ")"; }
                l.Add(k + ": " + s);
            }
            Add("Version", () => "v" + Application.version + (Application.isEditor ? " (Editor)" : ""));
            Add("Unity", () => Application.unityVersion);
            Add("Platform", () => Application.platform.ToString());
            Add("OS", () => SystemInfo.operatingSystem);
            Add("CPU", () => $"{SystemInfo.processorType} ({SystemInfo.processorCount} threads)");
            Add("GPU", () => $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} MB VRAM)");
            Add("RAM", () => SystemInfo.systemMemorySize + " MB");
            Add("Resolution", () => $"{Screen.width}x{Screen.height}{(Screen.fullScreen ? " fullscreen" : " windowed")}");
            Add("Language", () => Application.systemLanguage.ToString());
            return l;
        }
    }
}
