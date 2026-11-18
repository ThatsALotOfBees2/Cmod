using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cmod
{
    /// <summary>A console command, like GMod's concommand.Add.</summary>
    public sealed class ConCommand
    {
        public string Name;
        public string Help;
        public Action<string[]> Run;
    }

    /// <summary>A console variable, like GMod's CreateConVar. The value is stored as a string.</summary>
    public sealed class ConVar
    {
        public string Name;
        public string Help;
        public string Default;
        public float Min = float.NegativeInfinity;
        public float Max = float.PositiveInfinity;
        /// <summary>Archived convars are saved to PlayerPrefs ("cvar_" + Name) and restored on the next launch.</summary>
        public bool Archive;
        public event Action<ConVar> Changed;

        string _value;
        public string Value
        {
            get => _value;
            set
            {
                var v = value ?? Default;
                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) &&
                    (f < Min || f > Max))
                {
                    f = Math.Max(Min, Math.Min(Max, f));
                    v = f.ToString(CultureInfo.InvariantCulture);
                }
                if (v == _value) return;
                _value = v;
                if (Archive)
                {
                    UnityEngine.PlayerPrefs.SetString("cvar_" + Name, v);
                    UnityEngine.PlayerPrefs.Save();
                }
                try { Changed?.Invoke(this); } catch (Exception ex) { CmodLog.Error($"[convar {Name}] {ex.Message}"); }
            }
        }

        public float Float => float.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        public int Int => (int)Float;
        public bool Bool => _value == "1" || string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase);

        public void SetFloat(float f) => Value = f.ToString(CultureInfo.InvariantCulture);
        public void SetBool(bool b) => Value = b ? "1" : "0";
    }

    /// <summary>Registry and parser for console commands and convars.</summary>
    public static class ConCommands
    {
        static readonly Dictionary<string, ConCommand> s_cmds = new Dictionary<string, ConCommand>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ConVar> s_vars = new Dictionary<string, ConVar>(StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<ConCommand> Commands => s_cmds.Values.OrderBy(c => c.Name);
        public static IEnumerable<ConVar> Vars => s_vars.Values.OrderBy(v => v.Name);

        public static void Register(string name, string help, Action<string[]> run)
        {
            s_cmds[name] = new ConCommand { Name = name, Help = help, Run = run };
        }

        /// <summary>Creates the convar, or returns the existing one with that name.</summary>
        public static ConVar CreateVar(string name, string defaultValue, string help,
            float min = float.NegativeInfinity, float max = float.PositiveInfinity, bool archive = false)
        {
            if (s_vars.TryGetValue(name, out var existing)) return existing;
            var cv = new ConVar { Name = name, Help = help, Default = defaultValue, Min = min, Max = max };
            cv.Value = archive ? UnityEngine.PlayerPrefs.GetString("cvar_" + name, defaultValue) : defaultValue;
            cv.Archive = archive;
            s_vars[name] = cv;
            return cv;
        }

        public static ConVar GetVar(string name) => s_vars.TryGetValue(name, out var v) ? v : null;
        public static ConCommand GetCommand(string name) => s_cmds.TryGetValue(name, out var c) ? c : null;

        /// <summary>Names that start with the prefix, for console autocomplete.</summary>
        public static List<string> Complete(string prefix)
        {
            prefix = prefix ?? "";
            return s_cmds.Keys.Concat(s_vars.Keys)
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n).ToList();
        }

        /// <summary>Runs a console line. Several commands can be separated with ';'.
        /// "name" on its own prints a convar; "name value" sets it.</summary>
        public static void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            foreach (var part in SplitStatements(line))
            {
                var args = Tokenize(part);
                if (args.Count == 0) continue;
                var name = args[0];
                var rest = args.Skip(1).ToArray();
                if (s_cmds.TryGetValue(name, out var cmd))
                {
                    try { cmd.Run?.Invoke(rest); }
                    catch (Exception ex) { CmodLog.Error($"[{name}] {ex.GetType().Name}: {ex.Message}"); }
                }
                else if (s_vars.TryGetValue(name, out var cv))
                {
                    if (rest.Length == 0) CmodLog.Info($"\"{cv.Name}\" = \"{cv.Value}\" (default \"{cv.Default}\") - {cv.Help}");
                    else cv.Value = string.Join(" ", rest);
                }
                else
                {
                    CmodLog.Warn($"Unknown command: {name}");
                }
            }
        }

        static IEnumerable<string> SplitStatements(string line)
        {
            var sb = new StringBuilder();
            bool quoted = false;
            foreach (var c in line)
            {
                if (c == '"') quoted = !quoted;
                if (c == ';' && !quoted) { yield return sb.ToString(); sb.Clear(); continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        /// <summary>Splits on whitespace. "Double quotes" group words together.</summary>
        public static List<string> Tokenize(string s)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in s)
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (any) { result.Add(sb.ToString()); sb.Clear(); any = false; }
                    continue;
                }
                sb.Append(c); any = true;
            }
            if (any) result.Add(sb.ToString());
            return result;
        }
    }
}
