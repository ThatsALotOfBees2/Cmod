using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>Optional: a tool that has a one-line state to show on the toolgun HUD ("Click the second object").</summary>
    public interface IToolStatus
    {
        string Status { get; }
    }

    /// <summary>
    /// Small layout helpers for tool settings panels (ToolMode.DrawSettings), built on the Win98 widget kit.
    /// Each helper draws one row at `y` inside `area` and advances `y`.
    /// Tool settings are stored in archived convars (like GMod's tool convars), so they persist and can be
    /// set from the console, e.g. "balloon_force 800".
    /// </summary>
    public static class ToolUi
    {
        public const float RowHeight = 22f;
        public const float LabelWidth = 92f;
        public const float ValueWidth = 42f;

        /// <summary>A heading label in bold.</summary>
        public static void Heading(Rect area, ref float y, string text)
        {
            Win98.Label(new Rect(area.x, y, area.width, 16f), text, true);
            y += 18f;
        }

        /// <summary>Wrapped help text.</summary>
        public static void Help(Rect area, ref float y, string text)
        {
            var style = WrapStyle;
            var content = TempContent(text);
            float h = style.CalcHeight(content, area.width);
            GUI.Label(new Rect(area.x, y, area.width, h), content, style);
            y += h + 4f;
        }

        // ---- Deferred writes while dragging --------------------------------------------------------
        // Archived convars save PlayerPrefs on every write, so while a slider is held (hotControl != 0) the new value
        // is kept here and only written to the convar once the mouse is released.
        static readonly Dictionary<ConVar, string> s_pending = new Dictionary<ConVar, string>();

        static string Shown(ConVar cv) => s_pending.TryGetValue(cv, out var p) ? p : cv.Value;

        static float ShownFloat(ConVar cv)
            => float.TryParse(Shown(cv), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        static void SetDeferred(ConVar cv, string value)
        {
            if (GUIUtility.hotControl != 0) { s_pending[cv] = value; return; }
            s_pending.Remove(cv);
            if (cv.Value != value) cv.Value = value;
        }

        static void FlushPending(ConVar cv)
        {
            if (GUIUtility.hotControl != 0 || !s_pending.TryGetValue(cv, out var p)) return;
            s_pending.Remove(cv);
            if (cv.Value != p) cv.Value = p;
        }

        /// <summary>Writes every held-back slider value now (call when the settings panel closes mid-drag).</summary>
        public static void FlushAllPending()
        {
            if (s_pending.Count == 0) return;
            var items = new List<KeyValuePair<ConVar, string>>(s_pending);
            s_pending.Clear();
            foreach (var kv in items) if (kv.Key.Value != kv.Value) kv.Key.Value = kv.Value;
        }

        /// <summary>"Label  [----slider----]  value" bound to a numeric convar.</summary>
        public static void Slider(Rect area, ref float y, string label, ConVar cv, float min, float max, string format = "0.##")
        {
            if (cv == null) return;
            Win98.Label(new Rect(area.x, y + 3f, LabelWidth, 16f), label);
            float sliderW = Mathf.Max(40f, area.width - LabelWidth - ValueWidth - 8f);
            FlushPending(cv);
            float cur = ShownFloat(cv);
            float v = Win98.Slider(new Rect(area.x + LabelWidth, y + 1f, sliderW, 20f), cur, min, max);
            Win98.Label(new Rect(area.x + LabelWidth + sliderW + 6f, y + 3f, ValueWidth, 16f),
                v.ToString(format, CultureInfo.InvariantCulture));
            if (!Mathf.Approximately(v, cur)) SetDeferred(cv, v.ToString(CultureInfo.InvariantCulture));
            y += RowHeight;
        }

        /// <summary>A checkbox bound to a boolean convar.</summary>
        public static void Checkbox(Rect area, ref float y, string label, ConVar cv)
        {
            if (cv == null) return;
            bool v = Win98.Checkbox(new Rect(area.x, y + 2f, area.width, 18f), cv.Bool, label);
            if (v != cv.Bool) cv.SetBool(v);
            y += RowHeight;
        }

        /// <summary>A dropdown bound to an integer convar.</summary>
        public static void Dropdown(Rect area, ref float y, string label, ConVar cv, string[] options, string id)
        {
            if (cv == null) return;
            Win98.Label(new Rect(area.x, y + 3f, LabelWidth, 16f), label);
            int cur = Mathf.Clamp(cv.Int, 0, options.Length - 1);
            int v = Win98.Dropdown(new Rect(area.x + LabelWidth, y + 1f, Mathf.Max(60f, area.width - LabelWidth), 20f), cur, options, id);
            if (v != cur) cv.SetFloat(v);
            y += RowHeight + 2f;
        }

        // ---- Colors -----------------------------------------------------------------------------

        /// <summary>Classic 16-color palette (plus a few extras) for the swatch grid.</summary>
        public static readonly Color[] Swatches =
        {
            C(255, 255, 255), C(192, 192, 192), C(128, 128, 128), C(0, 0, 0),
            C(255, 0, 0), C(128, 0, 0), C(255, 255, 0), C(128, 128, 0),
            C(0, 255, 0), C(0, 128, 0), C(0, 255, 255), C(0, 128, 128),
            C(0, 0, 255), C(0, 0, 128), C(255, 0, 255), C(128, 0, 128),
            C(255, 128, 0), C(255, 160, 192), C(160, 96, 48), C(16, 132, 208),
        };

        static Color C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        /// <summary>A color picker bound to a "r g b" (0-255) convar: preview, RGB sliders and a swatch grid.</summary>
        public static void ColorPicker(Rect area, ref float y, string label, ConVar cv)
        {
            if (cv == null) return;
            FlushPending(cv);
            var c = ParseColor(Shown(cv), Color.white);
            var orig = c;

            Win98.Label(new Rect(area.x, y, area.width, 16f), label, true);
            y += 18f;

            // Preview swatch
            var prev = new Rect(area.x, y, 44f, 64f);
            Win98.SunkenPanel(prev);
            Win98.FillRect(new Rect(prev.x + 2f, prev.y + 2f, prev.width - 4f, prev.height - 4f), c);

            // RGB sliders
            float sx = area.x + 52f, sw = Mathf.Max(40f, area.width - 52f - 34f);
            string[] names = { "R", "G", "B" };
            for (int i = 0; i < 3; i++)
            {
                float ry = y + i * 21f;
                Win98.Label(new Rect(sx, ry + 3f, 12f, 16f), names[i]);
                float v = Win98.Slider(new Rect(sx + 12f, ry + 1f, sw - 12f, 20f), c[i] * 255f, 0f, 255f);
                c[i] = Mathf.Round(v) / 255f;
                Win98.Label(new Rect(sx + sw + 4f, ry + 3f, 30f, 16f), Mathf.RoundToInt(c[i] * 255f).ToString(CultureInfo.InvariantCulture));
            }
            y += 68f;

            // Swatch grid
            const float sz = 18f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((area.width + 2f) / (sz + 2f)));
            for (int i = 0; i < Swatches.Length; i++)
            {
                var r = new Rect(area.x + (i % perRow) * (sz + 2f), y + (i / perRow) * (sz + 2f), sz, sz);
                Win98.SunkenPanel(r);
                Win98.FillRect(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), Swatches[i]);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) c = Swatches[i];
            }
            int rows = (Swatches.Length + perRow - 1) / perRow;
            y += rows * (sz + 2f) + 4f;

            if (c != orig) SetDeferred(cv, FormatColor(c));
        }

        public static Color ParseColor(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            var parts = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return fallback;
            var c = fallback;
            for (int i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return fallback;
                c[i] = Mathf.Clamp01(f / 255f);
            }
            c.a = 1f;
            return c;
        }

        public static string FormatColor(Color c)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f), b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
            return r.ToString(CultureInfo.InvariantCulture) + " " + g.ToString(CultureInfo.InvariantCulture) + " " + b.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Creates (or gets) an archived tool convar.</summary>
        public static ConVar Var(string name, string def, string help, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
            => ConCommands.CreateVar(name, def, help, min, max, archive: true);

        // ---- Styles ------------------------------------------------------------------------------

        static GUIStyle s_wrap;
        static readonly GUIContent s_tmp = new GUIContent();

        /// <summary>The Win98 text style with word wrap (must be used from OnGUI).</summary>
        public static GUIStyle WrapStyle
        {
            get
            {
                if (s_wrap == null)
                {
                    Win98.EnsureInit();
                    s_wrap = new GUIStyle(Win98.TextStyle()) { wordWrap = true, alignment = TextAnchor.UpperLeft };
                }
                return s_wrap;
            }
        }

        public static GUIContent TempContent(string text)
        {
            s_tmp.text = text;
            return s_tmp;
        }
    }

    /// <summary>Reference-identity comparer, so destroyed Unity objects (which compare equal to null) stay distinct dictionary keys.</summary>
    internal sealed class RefComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly RefComparer<T> Instance = new RefComparer<T>();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
    }
}
