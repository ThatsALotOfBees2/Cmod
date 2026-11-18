using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    public enum LogLevel { Info, Warning, Error }

    public struct LogLine
    {
        public LogLevel Level;
        public string Text;
        public float Time;
    }

    /// <summary>
    /// Central log the in-game console shows. It also captures Unity's Debug.Log output
    /// (hooked up by CmodGame), so errors from any script appear in the console.
    /// </summary>
    public static class CmodLog
    {
        public const int MaxLines = 500;
        static readonly List<LogLine> s_lines = new List<LogLine>();

        public static IReadOnlyList<LogLine> Lines => s_lines;

        /// <summary>Fired after a line is added.</summary>
        public static event Action<LogLine> LineAdded;

        public static void Info(string text) => Add(LogLevel.Info, text);
        public static void Warn(string text) => Add(LogLevel.Warning, text);
        public static void Error(string text) => Add(LogLevel.Error, text);

        public static void Add(LogLevel level, string text)
        {
            var line = new LogLine { Level = level, Text = text ?? "", Time = UnityEngine.Time.realtimeSinceStartup };
            s_lines.Add(line);
            if (s_lines.Count > MaxLines) s_lines.RemoveRange(0, s_lines.Count - MaxLines);
            try { LineAdded?.Invoke(line); } catch (Exception) { /* never let a listener break logging */ }
        }

        public static void Clear() => s_lines.Clear();

        // --- Unity log capture -------------------------------------------------------------
        static bool s_capturing;

        internal static void BeginUnityCapture()
        {
            if (s_capturing) return;
            s_capturing = true;
            Application.logMessageReceived += OnUnityLog;
        }

        internal static void EndUnityCapture()
        {
            if (!s_capturing) return;
            s_capturing = false;
            Application.logMessageReceived -= OnUnityLog;
        }

        static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    var firstFrame = string.IsNullOrEmpty(stackTrace) ? "" : "\n  " + stackTrace.Split('\n')[0];
                    Add(LogLevel.Error, condition + firstFrame);
                    break;
                case LogType.Warning:
                    Add(LogLevel.Warning, condition);
                    break;
                default:
                    Add(LogLevel.Info, condition);
                    break;
            }
        }
    }
}
