using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Cmod.Services
{
    /// <summary>
    /// Scrubs text before it leaves the machine: the saved GitHub token, anything that looks like a
    /// GitHub token, and the user's home directory path (replaced with "~"), in both slash styles.
    /// </summary>
    public static class Redaction
    {
        public const string Placeholder = "[REDACTED]";

        // Classic (ghp_, gho_, ghu_, ghs_, ghr_) and fine-grained (github_pat_) GitHub tokens.
        static readonly Regex s_tokenPattern = new Regex(@"(github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static List<Regex> s_homePatterns;

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            var token = GitHubClient.Token;
            if (token.Length >= 8) text = text.Replace(token, Placeholder);
            text = s_tokenPattern.Replace(text, Placeholder);

            foreach (var re in HomePatterns()) text = re.Replace(text, "~");
            return text;
        }

        static List<Regex> HomePatterns()
        {
            if (s_homePatterns != null) return s_homePatterns;
            s_homePatterns = new List<Regex>();
            var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { homes.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch (Exception) { }
            try { homes.Add(Environment.GetEnvironmentVariable("HOME")); } catch (Exception) { }
            try { homes.Add(Environment.GetEnvironmentVariable("USERPROFILE")); } catch (Exception) { }

            bool windows = Application.platform == RuntimePlatform.WindowsPlayer ||
                           Application.platform == RuntimePlatform.WindowsEditor;
            foreach (var h in homes)
            {
                if (string.IsNullOrEmpty(h)) continue;
                var home = h.TrimEnd('/', '\\');
                if (home.Length < 4) continue; // never redact "/" or "C:"
                // Match both separator styles: Unity prints "C:/Users/me", .NET prints "C:\Users\me".
                var parts = home.Split('/', '\\');
                var pattern = string.Join(@"[\\/]", Array.ConvertAll(parts, Regex.Escape)) + @"(?![A-Za-z0-9_.-])";
                var opts = RegexOptions.CultureInvariant | (windows ? RegexOptions.IgnoreCase : RegexOptions.None);
                try { s_homePatterns.Add(new Regex(pattern, opts)); } catch (ArgumentException) { }
            }
            return s_homePatterns;
        }
    }
}
