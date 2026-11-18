using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// Implementation of the Windows 98 IMGUI widget kit (contract: UI/Contracts.cs).
    ///
    /// Design notes:
    /// - Every texture is generated in code (point filtered, HideAndDontSave). Frames are 9-slice GUIStyles
    ///   built from tiny 5x5 bevel textures, so the bevels stay pixel-exact at any size and integer ui_scale.
    /// - Widgets never call GUIUtility.GetControlID. "Pressed" state is tracked with a key built from the widget
    ///   rect (in screen space), a salt and the current layer (the window being drawn). That keeps IMGUI control
    ///   ids stable for text fields, even when windows change z-order or content changes between frames.
    /// - The WindowManager routes mouse events: layers that are not under the mouse see a far-away
    ///   mousePosition, so widgets only react in the topmost window.
    /// - Everything that can allocate is cached (GUIContent, styles, textures, ellipsized strings).
    /// </summary>
    public static partial class Win98
    {
        // =====================================================================================
        // Shared state
        // =====================================================================================

        /// <summary>Id of the layer (window) being drawn. Set by WindowManager; part of every widget key.</summary>
        internal static int CurrentLayer;

        /// <summary>Key of the widget held down with the left mouse button, 0 when none.</summary>
        static int s_press;
        /// <summary>Grab offset while dragging a scrollbar thumb.</summary>
        static float s_thumbGrab;
        /// <summary>Next auto-repeat time for held scroll arrows and track paging.</summary>
        static float s_repeatAt;

        /// <summary>A mouse position no widget rect can contain. Used to hide the mouse from inactive layers.</summary>
        internal static readonly Vector2 FarAway = new Vector2(-100000f, -100000f);

        static bool s_init;

        // Textures
        static Texture2D s_white, s_dither, s_ditherDark;
        static Texture2D s_check, s_radio, s_radioPressed, s_radioDot;
        static Texture2D s_arrowUp, s_arrowDown, s_arrowLeft, s_arrowRight;
        static Texture2D s_glyphClose, s_glyphMin, s_glyphMax, s_glyphHelp, s_thumb, s_grip;

        // 9-slice frame styles
        static GUIStyle s_raised, s_pressed, s_sunkenWhite, s_sunkenFace, s_thinSunken, s_flat;

        // Text styles
        static Font s_font;
        static GUIStyle s_text, s_textBold, s_field, s_area, s_banner;
        static readonly GUIContent s_content = new GUIContent();

        // Caches
        static readonly Dictionary<long, Texture2D> s_gradients = new Dictionary<long, Texture2D>();
        static readonly Dictionary<string, Texture2D> s_icons16 = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> s_icons32 = new Dictionary<string, Texture2D>();
        static readonly Dictionary<EllipsisKey, string> s_ellipsis = new Dictionary<EllipsisKey, string>();

        // Text field colors (applied around GUI.TextField calls, then restored)
        static readonly Color FieldCursor = Color.black;
        static readonly Color FieldSelection = new Color(0f, 0f, 0.5f, 0.35f);

        static readonly string[] s_preferredFonts =
        {
            "Tahoma", "Microsoft Sans Serif", "MS Sans Serif", "Segoe UI", "Verdana", "Arial",
            "Liberation Sans", "DejaVu Sans", "Helvetica",
        };

        // =====================================================================================
        // Init
        // =====================================================================================

        public static partial void EnsureInit()
        {
            // Unity null check also catches textures destroyed by a domain/scene teardown.
            if (s_init && s_white != null) return;
            s_init = true;

            s_gradients.Clear();
            s_icons16.Clear();
            s_icons32.Clear();
            s_ellipsis.Clear();

            s_white = MakeTex("w98_white", 1, 1, new Color32[] { new Color32(255, 255, 255, 255) }, false);
            s_dither = MakeTex("w98_dither", 2, 2, new Color32[] { Highlight, Face, Face, Highlight }, true);
            s_ditherDark = MakeTex("w98_dither_dark", 2, 2, new Color32[] { DarkShadow, Shadow, Shadow, DarkShadow }, true);

            // Card: outer bevel white/black, inner bevel light/dark gray. Pressed inverts it.
            s_raised = BevelStyle("w98_raised", Highlight, Light, Shadow, DarkShadow, Face);
            s_pressed = BevelStyle("w98_pressed", DarkShadow, Shadow, Light, Highlight, Face);
            s_sunkenWhite = BevelStyle("w98_sunken_white", Shadow, DarkShadow, Light, Highlight, Color.white);
            s_sunkenFace = BevelStyle("w98_sunken_face", Shadow, DarkShadow, Light, Highlight, Face);
            s_thinSunken = ThinStyle("w98_thin_sunken", Shadow, Highlight, Face);
            s_flat = ThinStyle("w98_flat", Shadow, Shadow, Face);

            s_check = FromPattern("w98_check", Patterns.Check);
            s_radio = FromPattern("w98_radio", Patterns.Radio);
            s_radioPressed = FromPattern("w98_radio_pressed", Replace(Patterns.Radio, 'W', 'G'));
            s_radioDot = FromPattern("w98_radio_dot", Patterns.RadioDot);
            s_arrowUp = FromPattern("w98_arrow_up", Patterns.ArrowUp);
            s_arrowDown = FromPattern("w98_arrow_down", Patterns.ArrowDown);
            s_arrowLeft = FromPattern("w98_arrow_left", Patterns.ArrowLeft);
            s_arrowRight = FromPattern("w98_arrow_right", Patterns.ArrowRight);
            s_glyphClose = FromPattern("w98_glyph_close", Patterns.GlyphClose);
            s_glyphMin = FromPattern("w98_glyph_min", Patterns.GlyphMin);
            s_glyphMax = FromPattern("w98_glyph_max", Patterns.GlyphMax);
            s_glyphHelp = FromPattern("w98_glyph_help", Patterns.GlyphHelp);
            s_thumb = FromPattern("w98_thumb", Patterns.Thumb);
            s_grip = MakeGrip();

            s_font = PickFont();
            s_text = MakeTextStyle(false);
            s_textBold = MakeTextStyle(true);

            s_field = MakeTextStyle(false);
            s_field.padding = new RectOffset(2, 2, 1, 1);
            SetAllTextColors(s_field, Color.black);

            s_area = MakeTextStyle(false);
            s_area.padding = new RectOffset(3, 3, 2, 2);
            s_area.alignment = TextAnchor.UpperLeft;
            s_area.wordWrap = true;
            SetAllTextColors(s_area, Color.black);

            s_banner = MakeTextStyle(true);
            s_banner.fontSize = 16;
            s_banner.normal.textColor = Color.white;
        }

        /// <summary>Cheap init guard for widgets (WindowManager calls the full EnsureInit once per OnGUI).</summary>
        static void Ready()
        {
            if (!s_init) EnsureInit();
        }

        static Font PickFont()
        {
            try
            {
                var installed = Font.GetOSInstalledFontNames();
                if (installed != null && installed.Length > 0)
                {
                    var set = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
                    foreach (var name in s_preferredFonts)
                    {
                        if (!set.Contains(name)) continue;
                        var f = Font.CreateDynamicFontFromOSFont(name, FontSize);
                        if (f != null)
                        {
                            f.hideFlags = HideFlags.HideAndDontSave;
                            return f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CmodLog.Warn("[ui] Could not load an OS font, using Unity's default: " + ex.Message);
            }
            return null; // GUIStyle.font == null means GUI.skin.font
        }

        static GUIStyle MakeTextStyle(bool bold)
        {
            var st = new GUIStyle
            {
                font = s_font,
                fontSize = FontSize,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            st.normal.textColor = Color.black;
            return st;
        }

        static void SetAllTextColors(GUIStyle st, Color c)
        {
            st.normal.textColor = c;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
            st.onNormal.textColor = c;
            st.onHover.textColor = c;
            st.onActive.textColor = c;
            st.onFocused.textColor = c;
        }

        // =====================================================================================
        // Texture helpers
        // =====================================================================================

        /// <summary>Creates a point-filtered texture from top-down pixel rows.</summary>
        static Texture2D MakeTex(string name, int w, int h, Color32[] topDown, bool repeat, bool bilinear = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[(h - 1 - y) * w + x] = topDown[y * w + x]; // Texture rows are bottom-up
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>5x5 bevel: 2px border (outer + inner ring) around a 1px stretched center.
        /// Bottom/right edges win the corners, exactly like Win98 (the top-right pixel is dark).</summary>
        static GUIStyle BevelStyle(string name, Color outerTL, Color innerTL, Color innerBR, Color outerBR, Color fill)
        {
            const int n = 5;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Color c;
                    if (x == n - 1 || y == n - 1) c = outerBR;
                    else if (x == 0 || y == 0) c = outerTL;
                    else if (x == n - 2 || y == n - 2) c = innerBR;
                    else if (x == 1 || y == 1) c = innerTL;
                    else c = fill;
                    px[y * n + x] = c;
                }
            }
            var st = new GUIStyle { border = new RectOffset(2, 2, 2, 2) };
            st.normal.background = MakeTex(name, n, n, px, false);
            return st;
        }

        /// <summary>3x3 one-pixel border (status-bar / tray style).</summary>
        static GUIStyle ThinStyle(string name, Color tl, Color br, Color fill)
        {
            const int n = 3;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = (x == n - 1 || y == n - 1) ? br : (x == 0 || y == 0) ? tl : fill;
            var st = new GUIStyle { border = new RectOffset(1, 1, 1, 1) };
            st.normal.background = MakeTex(name, n, n, px, false);
            return st;
        }

        static Color32 PaletteColor(char c)
        {
            switch (c)
            {
                case 'K': return new Color32(0, 0, 0, 255);
                case 'W': return new Color32(255, 255, 255, 255);
                case 'H': return new Color32(255, 255, 255, 255);
                case 'G': return new Color32(192, 192, 192, 255);
                case 'L': return new Color32(223, 223, 223, 255);
                case 'D': return new Color32(128, 128, 128, 255);
                case 'N': return new Color32(0, 0, 128, 255);
                case 'B': return new Color32(16, 132, 208, 255);
                case 'w': return new Color32(0, 0, 255, 255);
                case 'C': return new Color32(0, 128, 128, 255);
                case 'T': return new Color32(0, 255, 255, 255);
                case 'Y': return new Color32(255, 255, 0, 255);
                case 'O': return new Color32(128, 128, 0, 255);
                case 'R': return new Color32(255, 0, 0, 255);
                case 'M': return new Color32(128, 0, 0, 255);
                case 'g': return new Color32(0, 255, 0, 255);
                case 'E': return new Color32(0, 128, 0, 255);
                case 'b': return new Color32(198, 140, 72, 255);
                case 'n': return new Color32(128, 80, 32, 255);
                case 'y': return new Color32(236, 196, 128, 255);
                default: return new Color32(0, 0, 0, 0);
            }
        }

        /// <summary>Builds a texture from a char pattern (see PaletteColor). Rows are padded to the widest row.</summary>
        static Texture2D FromPattern(string name, string[] rows)
        {
            int h = rows.Length, w = 0;
            foreach (var r in rows) w = Mathf.Max(w, r.Length);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = x < rows[y].Length ? PaletteColor(rows[y][x]) : new Color32(0, 0, 0, 0);
            return MakeTex(name, w, h, px, false);
        }

        static string[] Replace(string[] rows, char from, char to)
        {
            var r = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++) r[i] = rows[i].Replace(from, to);
            return r;
        }

        /// <summary>Win98 size grip: three diagonal white/gray line pairs in the bottom-right corner.</summary>
        static Texture2D MakeGrip()
        {
            const int n = 12;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int d = x + y - (n - 1); // 0 on the anti-diagonal, growing toward the corner
                    Color32 c = new Color32(0, 0, 0, 0);
                    if (d >= 0 && d < 11)
                    {
                        int k = d % 4;
                        if (k == 0) c = Highlight;
                        else if (k == 1 || k == 2) c = Shadow;
                    }
                    px[y * n + x] = c;
                }
            }
            return MakeTex("w98_grip", n, n, px, false);
        }

        static Texture2D GradientTex(Color a, Color b, bool vertical)
        {
            Color32 ca = a, cb = b;
            long key = ((long)ca.r << 56) | ((long)ca.g << 48) | ((long)ca.b << 40) | ((long)ca.a << 32) |
                       ((long)cb.r << 24) | ((long)cb.g << 16) | ((long)cb.b << 8) | (vertical ? 1L : 0L);
            if (s_gradients.TryGetValue(key, out var tex) && tex != null) return tex;
            const int n = 64;
            var px = new Color32[n];
            for (int i = 0; i < n; i++) px[i] = Color32.Lerp(ca, cb, i / (float)(n - 1));
            tex = vertical ? MakeTex("w98_vgrad", 1, n, px, false, true) : MakeTex("w98_hgrad", n, 1, px, false, true);
            s_gradients[key] = tex;
            return tex;
        }

        // =====================================================================================
        // Low-level drawing helpers (internal so WindowManager / Taskbar / MessageBox can share them)
        // =====================================================================================

        static bool IsRepaint => Event.current.type == EventType.Repaint;

        internal static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2 * d, r.height - 2 * d);

        static void DrawStyle(GUIStyle st, Rect r)
        {
            if (IsRepaint && r.width > 0 && r.height > 0) st.Draw(r, false, false, false, false);
        }

        internal static void DrawTex(Rect r, Texture tex)
        {
            if (IsRepaint && tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
        }

        /// <summary>Draws a glyph texture at its native pixel size, centered in r (plus an offset).</summary>
        static void DrawGlyph(Rect r, Texture2D glyph, float dx = 0, float dy = 0)
        {
            if (!IsRepaint || glyph == null) return;
            float x = Mathf.Floor(r.x + (r.width - glyph.width) / 2f) + dx;
            float y = Mathf.Floor(r.y + (r.height - glyph.height) / 2f) + dy;
            GUI.DrawTexture(new Rect(x, y, glyph.width, glyph.height), glyph);
        }

        internal static void Dither(Rect r, bool dark = false)
        {
            if (!IsRepaint || r.width <= 0 || r.height <= 0) return;
            GUI.DrawTextureWithTexCoords(r, dark ? s_ditherDark : s_dither, new Rect(0, 0, r.width / 2f, r.height / 2f));
        }

        internal static void DrawGrip(Rect r) => DrawTex(r, s_grip);

        internal static void ButtonFrame(Rect r, bool pressed) => DrawStyle(pressed ? s_pressed : s_raised, r);
        internal static void ThinSunken(Rect r) => DrawStyle(s_thinSunken, r);

        /// <summary>1px rectangle outline.</summary>
        internal static void Outline(Rect r, Color c)
        {
            FillRect(new Rect(r.x, r.y, r.width, 1), c);
            FillRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            FillRect(new Rect(r.x, r.y, 1, r.height), c);
            FillRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        internal static void VGradient(Rect r, Color top, Color bottom)
        {
            if (!IsRepaint) return;
            GUI.DrawTexture(r, GradientTex(top, bottom, true), ScaleMode.StretchToFill, true);
        }

        internal static GUIStyle BannerStyle { get { Ready(); return s_banner; } }

        /// <summary>Draws text with the Win98 font. Only acts on Repaint.</summary>
        internal static void DrawText(Rect r, string text, bool bold, TextAnchor align, Color color, bool wrap = false)
        {
            if (!IsRepaint || string.IsNullOrEmpty(text)) return;
            Ready();
            var st = bold ? s_textBold : s_text;
            var oldAlign = st.alignment;
            var oldColor = st.normal.textColor;
            st.alignment = align;
            st.normal.textColor = color;
            st.wordWrap = wrap;
            s_content.text = text;
            st.Draw(r, s_content, false, false, false, false);
            st.wordWrap = false;
            st.alignment = oldAlign;
            st.normal.textColor = oldColor;
            s_content.text = null;
        }

        /// <summary>Text, or the Win98 embossed "disabled" look (white copy offset by 1px under gray text).</summary>
        static void DrawTextState(Rect r, string text, bool bold, TextAnchor align, bool enabled)
        {
            if (enabled)
            {
                DrawText(r, text, bold, align, Color.black);
            }
            else
            {
                DrawText(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, bold, align, Highlight);
                DrawText(r, text, bold, align, Shadow);
            }
        }

        internal static float TextWidth(string text, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            Ready();
            s_content.text = text;
            float w = (bold ? s_textBold : s_text).CalcSize(s_content).x;
            s_content.text = null;
            return w;
        }

        internal static float TextHeight(string text, float width, bool bold = false)
        {
            Ready();
            var st = bold ? s_textBold : s_text;
            s_content.text = string.IsNullOrEmpty(text) ? " " : text;
            st.wordWrap = true;
            float h = st.CalcHeight(s_content, Mathf.Max(1f, width));
            st.wordWrap = false;
            s_content.text = null;
            return h;
        }

        readonly struct EllipsisKey : IEquatable<EllipsisKey>
        {
            readonly string m_text;
            readonly int m_width;
            readonly bool m_bold;
            public EllipsisKey(string text, int width, bool bold) { m_text = text; m_width = width; m_bold = bold; }
            public bool Equals(EllipsisKey o) => m_width == o.m_width && m_bold == o.m_bold && string.Equals(m_text, o.m_text);
            public override bool Equals(object obj) => obj is EllipsisKey k && Equals(k);
            public override int GetHashCode() => ((m_text?.GetHashCode() ?? 0) * 31 + m_width) * 2 + (m_bold ? 1 : 0);
        }

        /// <summary>Returns the text shortened with "..." so it fits the width (cached per text/width).</summary>
        internal static string Ellipsize(string text, float width, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var key = new EllipsisKey(text, (int)width, bold);
            if (s_ellipsis.TryGetValue(key, out var result)) return result;
            if (TextWidth(text, bold) <= width)
            {
                result = text;
            }
            else
            {
                result = "...";
                // Binary search the longest prefix that fits.
                int lo = 0, hi = text.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var candidate = text.Substring(0, mid).TrimEnd() + "...";
                    if (TextWidth(candidate, bold) <= width) { result = candidate; lo = mid + 1; }
                    else hi = mid - 1;
                }
            }
            if (s_ellipsis.Count > 512) s_ellipsis.Clear();
            s_ellipsis[key] = result;
            return result;
        }

        // =====================================================================================
        // Press tracking (replaces control ids)
        // =====================================================================================

        static int Key(Rect r, int salt)
        {
            Vector2 p = GUIUtility.GUIToScreenPoint(r.position);
            unchecked
            {
                int h = (int)2166136261;
                h = (h ^ CurrentLayer) * 16777619;
                h = (h ^ salt) * 16777619;
                h = (h ^ Mathf.RoundToInt(p.x * 4f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(p.y * 4f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(r.width * 4f)) * 16777619;
                h = (h ^ Mathf.RoundToInt(r.height * 4f)) * 16777619;
                return h == 0 ? 1 : h;
            }
        }

        /// <summary>True while any Win98 widget is held down.</summary>
        internal static bool IsPressing => s_press != 0;

        /// <summary>Called by WindowManager after a MouseUp (or when the button was released elsewhere).</summary>
        internal static void ReleasePress() => s_press = 0;

        /// <summary>
        /// Shared push-button behavior: press on MouseDown inside, click on MouseUp inside.
        /// `held` is true while the button is down and the mouse is still over it (draw it pressed).
        /// </summary>
        internal static bool ButtonBehavior(Rect r, int salt, bool enabled, out bool held)
        {
            var e = Event.current;
            held = false;
            if (!enabled) return false;
            int key = Key(r, salt);
            bool over = r.Contains(e.mousePosition);
            held = s_press == key && over;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && over)
                    {
                        s_press = key;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (s_press == key) e.Use();
                    break;
                case EventType.MouseUp:
                    if (s_press == key)
                    {
                        s_press = 0;
                        e.Use();
                        if (over)
                        {
                            GUI.changed = true;
                            return true;
                        }
                    }
                    break;
            }
            return false;
        }

        // =====================================================================================
        // Frames
        // =====================================================================================

        public static partial void RaisedPanel(Rect r)
        {
            Ready();
            DrawStyle(s_raised, r);
        }

        public static partial void SunkenPanel(Rect r, bool white)
        {
            Ready();
            DrawStyle(white ? s_sunkenWhite : s_sunkenFace, r);
        }

        public static partial void GroupBox(Rect r, string title)
        {
            if (!IsRepaint) return;
            Ready();
            const float half = 6f;
            var box = new Rect(r.x, r.y + half, r.width, r.height - half);
            // Etched: white rectangle offset by one pixel, gray rectangle on top.
            Outline(new Rect(box.x + 1, box.y + 1, box.width - 1, box.height - 1), Highlight);
            Outline(new Rect(box.x, box.y, box.width - 1, box.height - 1), Shadow);
            if (!string.IsNullOrEmpty(title))
            {
                float tw = Mathf.Min(TextWidth(title), r.width - 20);
                FillRect(new Rect(r.x + 6, r.y, tw + 4, 13), Face);
                DrawText(new Rect(r.x + 8, r.y, tw, 13), Ellipsize(title, tw), false, TextAnchor.MiddleLeft, Color.black);
            }
        }

        public static partial void Separator(Rect r)
        {
            if (!IsRepaint) return;
            float y = Mathf.Floor(r.y + r.height / 2f) - 1;
            FillRect(new Rect(r.x, y, r.width, 1), Shadow);
            FillRect(new Rect(r.x, y + 1, r.width, 1), Highlight);
        }

        public static partial void FillRect(Rect r, Color c)
        {
            if (!IsRepaint || r.width <= 0 || r.height <= 0) return;
            Ready();
            var old = GUI.color;
            GUI.color = c * old; // respect callers that fade the whole UI with GUI.color
            GUI.DrawTexture(r, s_white);
            GUI.color = old;
        }

        public static partial void Gradient(Rect r, Color left, Color right)
        {
            if (!IsRepaint) return;
            Ready();
            GUI.DrawTexture(r, GradientTex(left, right, false), ScaleMode.StretchToFill, true);
        }

        // =====================================================================================
        // Widgets
        // =====================================================================================

        public static partial void Label(Rect r, string text, bool bold, TextAnchor align, Color? color)
        {
            // Tall label rects (more than about two lines) word-wrap; short ones stay single-line and clip.
            DrawText(r, text, bold, align, color ?? Color.black, r.height > 26f);
        }

        public static partial bool Button(Rect r, string text, bool enabled)
        {
            Ready();
            bool click = ButtonBehavior(r, 1, enabled, out bool held);
            if (IsRepaint)
            {
                DrawStyle(held ? s_pressed : s_raised, r);
                var tr = held ? new Rect(r.x + 1, r.y + 1, r.width, r.height) : r;
                DrawTextState(Inset(tr, 2), text, false, TextAnchor.MiddleCenter, enabled);
            }
            return click;
        }

        public static partial bool ToggleButton(Rect r, string text, bool on)
        {
            Ready();
            bool click = ButtonBehavior(r, 2, true, out bool held);
            if (IsRepaint)
            {
                bool down = on || held;
                DrawStyle(down ? s_pressed : s_raised, r);
                if (on && !held) Dither(Inset(r, 2));
                var tr = down ? new Rect(r.x + 1, r.y + 1, r.width, r.height) : r;
                DrawText(Inset(tr, 2), text, false, TextAnchor.MiddleCenter, Color.black);
            }
            return click;
        }

        public static partial bool Checkbox(Rect r, bool value, string label)
        {
            Ready();
            var box = new Rect(r.x, Mathf.Floor(r.y + (r.height - 13f) / 2f), 13, 13);
            if (ButtonBehavior(r, 4, true, out bool held)) value = !value;
            if (IsRepaint)
            {
                // Win98 turns the box gray while it is pressed.
                DrawStyle(held ? s_sunkenFace : s_sunkenWhite, box);
                if (value) GUI.DrawTexture(new Rect(box.x + 3, box.y + 3, 7, 7), s_check);
                if (!string.IsNullOrEmpty(label))
                    DrawText(new Rect(r.x + 19, r.y, r.width - 19, r.height), label, false, TextAnchor.MiddleLeft, Color.black);
            }
            return value;
        }

        public static partial bool Radio(Rect r, bool selected, string label)
        {
            Ready();
            var box = new Rect(r.x, Mathf.Floor(r.y + (r.height - 12f) / 2f), 12, 12);
            bool click = ButtonBehavior(r, 6, true, out bool held);
            if (IsRepaint)
            {
                GUI.DrawTexture(box, held ? s_radioPressed : s_radio);
                if (selected) GUI.DrawTexture(new Rect(box.x + 4, box.y + 4, 4, 4), s_radioDot);
                if (!string.IsNullOrEmpty(label))
                    DrawText(new Rect(r.x + 18, r.y, r.width - 18, r.height), label, false, TextAnchor.MiddleLeft, Color.black);
            }
            return click;
        }

        public static partial float Slider(Rect r, float value, float min, float max)
        {
            Ready();
            var e = Event.current;
            int key = Key(r, 5);
            const float thumbW = 11f;
            float thumbH = Mathf.Min(19f, r.height);
            float trackX = r.x + thumbW / 2f;
            float trackW = Mathf.Max(1f, r.width - thumbW);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        s_press = key;
                        value = ValueAt(e.mousePosition.x);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (s_press == key)
                    {
                        value = ValueAt(e.mousePosition.x);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (s_press == key)
                    {
                        s_press = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                {
                    float t = max > min ? Mathf.InverseLerp(min, max, value) : 0f;
                    float thumbY = Mathf.Floor(r.y + (r.height - thumbH) / 2f);
                    // Sunken 4px channel under the rectangular part of the thumb.
                    DrawStyle(s_sunkenFace, new Rect(r.x + 2, thumbY + Mathf.Floor(thumbH * 0.32f), r.width - 4, 4));
                    float tx = Mathf.Round(trackX + t * trackW - thumbW / 2f);
                    GUI.DrawTexture(new Rect(tx, thumbY, thumbW, thumbH), s_thumb);
                    break;
                }
            }
            return value;

            float ValueAt(float mx)
            {
                float t01 = Mathf.Clamp01((mx - trackX) / trackW);
                return Mathf.Lerp(min, max, t01);
            }
        }

        public static partial string TextField(Rect r, string text, string controlName)
        {
            Ready();
            DrawStyle(s_sunkenWhite, r);
            if (!string.IsNullOrEmpty(controlName)) GUI.SetNextControlName(controlName);
            var settings = GUI.skin.settings;
            Color oldCursor = settings.cursorColor, oldSel = settings.selectionColor;
            settings.cursorColor = FieldCursor;
            settings.selectionColor = FieldSelection;
            string result = GUI.TextField(Inset(r, 2), text ?? "", s_field);
            settings.cursorColor = oldCursor;
            settings.selectionColor = oldSel;
            return result;
        }

        public static partial string PasswordField(Rect r, string text, char mask, string controlName)
        {
            Ready();
            DrawStyle(s_sunkenWhite, r);
            if (!string.IsNullOrEmpty(controlName)) GUI.SetNextControlName(controlName);
            var settings = GUI.skin.settings;
            Color oldCursor = settings.cursorColor, oldSel = settings.selectionColor;
            settings.cursorColor = FieldCursor;
            settings.selectionColor = FieldSelection;
            string result = GUI.PasswordField(Inset(r, 2), text ?? "", mask, s_field);
            settings.cursorColor = oldCursor;
            settings.selectionColor = oldSel;
            return result;
        }

        public static partial string TextArea(Rect r, string text)
        {
            Ready();
            DrawStyle(s_sunkenWhite, r);
            var settings = GUI.skin.settings;
            Color oldCursor = settings.cursorColor, oldSel = settings.selectionColor;
            settings.cursorColor = FieldCursor;
            settings.selectionColor = FieldSelection;
            string result = GUI.TextArea(Inset(r, 2), text ?? "", s_area);
            settings.cursorColor = oldCursor;
            settings.selectionColor = oldSel;
            return result;
        }

        // ---- Dropdown (combo box) -----------------------------------------------------------
        // The popup list is drawn by WindowManager as an overlay above everything (DrawPopupOverlay),
        // so it is not clipped by the window. The chosen index comes back on the next Dropdown call.

        const float PopupRowH = 16f;
        const int PopupMaxRows = 10;
        static string s_popupId;
        static string[] s_popupItems;
        static int s_popupHover;
        static int s_popupFrame;
        static float s_popupWidth;
        static Rect s_popupAnchor;      // dropdown field in screen space (GUIToScreenPoint)
        static Vector2 s_popupScroll;
        static string s_popupResultId;
        static int s_popupResult;

        internal static bool PopupIsOpen => s_popupId != null;

        internal static void ClosePopup()
        {
            s_popupId = null;
            s_popupItems = null;
        }

        static Rect ToScreen(Rect r)
        {
            Vector2 a = GUIUtility.GUIToScreenPoint(r.min);
            Vector2 b = GUIUtility.GUIToScreenPoint(r.max);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        /// <summary>The popup list rect in top-level GUI coordinates (call from top-level OnGUI code only).</summary>
        internal static Rect PopupGuiRect()
        {
            if (s_popupId == null || s_popupItems == null) return Rect.zero;
            Vector2 a = GUIUtility.ScreenToGUIPoint(s_popupAnchor.min);
            Vector2 b = GUIUtility.ScreenToGUIPoint(s_popupAnchor.max);
            var anchor = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            int rows = Mathf.Clamp(s_popupItems.Length, 1, PopupMaxRows);
            float h = rows * PopupRowH + 2;
            float w = Mathf.Max(anchor.width, s_popupWidth);
            var rect = new Rect(anchor.x, anchor.yMax, w, h);
            var screen = WindowManager.ScreenSizeScaled;
            if (rect.yMax > screen.y && anchor.y - h >= 0) rect.y = anchor.y - h; // flip above when no room below
            if (rect.xMax > screen.x) rect.x = Mathf.Max(0, screen.x - rect.width);
            return rect;
        }

        public static partial int Dropdown(Rect r, int selected, string[] options, string id)
        {
            Ready();
            id = id ?? "";
            if (s_popupResultId == id)
            {
                selected = s_popupResult;
                s_popupResultId = null;
                GUI.changed = true;
            }
            var e = Event.current;
            bool open = s_popupId == id;
            if (open)
            {
                // Keep the overlay anchored to the field (windows can move) and alive.
                s_popupFrame = Time.frameCount;
                s_popupItems = options;
                s_popupAnchor = ToScreen(r);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                if (open)
                {
                    ClosePopup();
                }
                else if (options != null && options.Length > 0)
                {
                    s_popupId = id;
                    s_popupItems = options;
                    s_popupHover = Mathf.Clamp(selected, 0, options.Length - 1);
                    s_popupFrame = Time.frameCount;
                    s_popupAnchor = ToScreen(r);
                    float maxW = 0;
                    foreach (var o in options) maxW = Mathf.Max(maxW, TextWidth(o));
                    s_popupWidth = maxW + 10 + (options.Length > PopupMaxRows ? 16 : 0);
                    s_popupScroll = new Vector2(0, Mathf.Max(0, (s_popupHover - PopupMaxRows + 1) * PopupRowH));
                }
                e.Use();
            }

            if (IsRepaint)
            {
                DrawStyle(s_sunkenWhite, r);
                float bw = Mathf.Min(16f, r.height - 4);
                var btn = new Rect(r.xMax - 2 - bw, r.y + 2, bw, r.height - 4);
                DrawStyle(open ? s_pressed : s_raised, btn);
                DrawGlyph(btn, s_arrowDown, open ? 1 : 0, open ? 1 : 0);
                if (options != null && selected >= 0 && selected < options.Length)
                {
                    var tr = new Rect(r.x + 4, r.y + 2, btn.x - r.x - 6, r.height - 4);
                    DrawText(tr, Ellipsize(options[selected], tr.width), false, TextAnchor.MiddleLeft, Color.black);
                }
            }
            return selected;
        }

        /// <summary>Draws the open dropdown list. Called by WindowManager last, at top level.</summary>
        internal static void DrawPopupOverlay(bool isTarget)
        {
            if (s_popupId == null) return;
            // The dropdown that owns the popup stopped being drawn (its window closed): drop the popup.
            if (s_popupItems == null || Time.frameCount - s_popupFrame > 2) { ClosePopup(); return; }

            var e = Event.current;
            var pr = PopupGuiRect();
            FillRect(pr, DarkShadow);
            FillRect(Inset(pr, 1), Color.white);

            var items = s_popupItems;
            var inner = Inset(pr, 1);
            float contentH = items.Length * PopupRowH;
            bool needV = contentH > inner.height;
            var content = new Rect(0, 0, inner.width - (needV ? 16 : 0), contentH);
            s_popupScroll = BeginScroll(inner, s_popupScroll, content);
            int first = Mathf.Max(0, Mathf.FloorToInt(s_popupScroll.y / PopupRowH));
            int last = Mathf.Min(items.Length - 1, Mathf.CeilToInt((s_popupScroll.y + inner.height) / PopupRowH));
            string chosenId = null;
            int chosen = -1;
            for (int i = first; i <= last; i++)
            {
                var row = new Rect(0, i * PopupRowH, content.width, PopupRowH);
                bool over = isTarget && row.Contains(e.mousePosition);
                if (over && (e.type == EventType.Repaint || e.type == EventType.MouseMove || e.type == EventType.MouseDrag))
                    s_popupHover = i;
                if (over && e.type == EventType.MouseDown && e.button == 0)
                {
                    chosenId = s_popupId;
                    chosen = i;
                    e.Use();
                }
                if (IsRepaint)
                {
                    bool hi = i == s_popupHover;
                    if (hi) FillRect(row, SelectionBg);
                    DrawText(new Rect(row.x + 3, row.y, row.width - 4, row.height), items[i], false,
                        TextAnchor.MiddleLeft, hi ? Color.white : Color.black);
                }
            }
            EndScroll();

            if (chosenId != null)
            {
                s_popupResultId = chosenId;
                s_popupResult = chosen;
                ClosePopup();
            }
        }

        // ---- Tabs ----------------------------------------------------------------------------

        const float TabStripH = 20f;

        public static partial int Tabs(Rect r, int selected, string[] tabs)
        {
            Ready();
            var e = Event.current;
            int n = tabs?.Length ?? 0;
            var panel = new Rect(r.x, r.y + TabStripH, r.width, r.height - TabStripH);
            Rect selRect = default;
            bool haveSel = false;

            float x = r.x + 2;
            for (int i = 0; i < n; i++)
            {
                float w = Mathf.Max(30f, Mathf.Ceil(TextWidth(tabs[i]) + 14f));
                var tab = new Rect(x, r.y + 2, w, TabStripH - 2);
                bool isSel = i == selected;
                var hit = isSel ? new Rect(x - 2, r.y, w + 4, TabStripH + 1) : tab;
                if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
                {
                    if (selected != i) GUI.changed = true;
                    selected = i;
                    e.Use();
                }
                if (isSel)
                {
                    selRect = new Rect(x - 2, r.y, w + 4, TabStripH + 2);
                    haveSel = true;
                }
                else if (IsRepaint)
                {
                    DrawTab(tab);
                    DrawText(tab, tabs[i], false, TextAnchor.MiddleCenter, Color.black);
                }
                x += w;
            }

            DrawStyle(s_raised, panel);
            if (haveSel && IsRepaint)
            {
                // The selected tab is taller and covers the panel's top edge, so it joins the page.
                DrawTab(selRect);
                var tr = new Rect(selRect.x, selRect.y + 1, selRect.width, TabStripH - 2);
                DrawText(tr, tabs[Mathf.Clamp(selected, 0, n - 1)], false, TextAnchor.MiddleCenter, Color.black);
            }
            return selected;
        }

        /// <summary>A tab with rounded top corners and no bottom edge.</summary>
        static void DrawTab(Rect t)
        {
            FillRect(new Rect(t.x + 1, t.y + 1, t.width - 2, t.height - 1), Face);
            FillRect(new Rect(t.x + 2, t.y, t.width - 4, 1), Highlight);
            FillRect(new Rect(t.x + 1, t.y + 1, 1, 1), Highlight);
            FillRect(new Rect(t.x, t.y + 2, 1, t.height - 2), Highlight);
            FillRect(new Rect(t.x + 1, t.y + 2, 1, t.height - 2), Light);
            FillRect(new Rect(t.xMax - 2, t.y + 1, 1, 1), DarkShadow);
            FillRect(new Rect(t.xMax - 1, t.y + 2, 1, t.height - 2), DarkShadow);
            FillRect(new Rect(t.xMax - 2, t.y + 2, 1, t.height - 2), Shadow);
        }

        public static partial Rect TabContentRect(Rect tabControl)
        {
            return new Rect(tabControl.x + 6, tabControl.y + TabStripH + 6,
                tabControl.width - 12, tabControl.height - TabStripH - 12);
        }

        // ---- ListBox -------------------------------------------------------------------------

        public static partial int ListBox(Rect r, int selected, IList<string> items, ref Vector2 scroll, float rowHeight)
        {
            Ready();
            if (rowHeight < 1f) rowHeight = 16f;
            DrawStyle(s_sunkenWhite, r);
            var inner = Inset(r, 2);
            int n = items?.Count ?? 0;
            float contentH = n * rowHeight;
            bool needV = contentH > inner.height;
            var content = new Rect(0, 0, inner.width - (needV ? 16 : 0), contentH);
            scroll = BeginScroll(inner, scroll, content);

            var e = Event.current;
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / rowHeight));
            int last = Mathf.Min(n - 1, Mathf.CeilToInt((scroll.y + inner.height) / rowHeight));
            for (int i = first; i <= last; i++)
            {
                var row = new Rect(0, i * rowHeight, content.width, rowHeight);
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                {
                    if (selected != i) GUI.changed = true;
                    selected = i;
                    e.Use();
                }
                if (IsRepaint)
                {
                    bool sel = i == selected;
                    if (sel) FillRect(row, SelectionBg);
                    var text = items[i];
                    var tr = new Rect(row.x + 3, row.y, row.width - 5, row.height);
                    DrawText(tr, Ellipsize(text, tr.width), false, TextAnchor.MiddleLeft, sel ? Color.white : Color.black);
                }
            }
            EndScroll();
            return selected;
        }

        // ---- ProgressBar ---------------------------------------------------------------------

        public static partial void ProgressBar(Rect r, float t01)
        {
            if (!IsRepaint) return;
            Ready();
            DrawStyle(s_thinSunken, r);
            var inner = Inset(r, 2);
            if (inner.width <= 0 || inner.height <= 0) return;
            float blockW = Mathf.Max(2f, Mathf.Round(inner.height * 0.6f));
            const float gap = 2f;
            float fill = inner.width * Mathf.Clamp01(t01);
            for (float x = inner.x; x < inner.x + fill - 0.01f; x += blockW + gap)
            {
                float w = Mathf.Min(blockW, inner.xMax - x);
                if (w <= 0) break;
                FillRect(new Rect(x, inner.y, w, inner.height), SelectionBg);
            }
        }

        // ---- Scroll views --------------------------------------------------------------------

        const float SB = 16f; // scrollbar thickness

        struct ScrollState
        {
            public bool Redirected;
            public Vector2 SavedMouse;
            public Rect Area;
            public bool NeedV, NeedH;
            public int WheelKey;
        }

        static readonly Stack<ScrollState> s_scrollStack = new Stack<ScrollState>();
        // Wheel deltas taken in EndScroll (after the content had its chance at the event, so the innermost
        // view under the mouse wins); applied by the next BeginScroll for the same view, since EndScroll can't
        // return the new scroll position.
        static readonly Dictionary<int, Vector2> s_pendingWheel = new Dictionary<int, Vector2>();

        public static partial Vector2 BeginScroll(Rect r, Vector2 scroll, Rect content)
        {
            Ready();
            var e = Event.current;
            bool needV = content.height > r.height;
            bool needH = content.width > r.width - (needV ? SB : 0);
            if (needH && !needV) needV = content.height > r.height - SB;
            var view = new Rect(r.x, r.y, r.width - (needV ? SB : 0), r.height - (needH ? SB : 0));
            float maxX = Mathf.Max(0, content.width - view.width);
            float maxY = Mathf.Max(0, content.height - view.height);

            // Mouse wheel: a delta queued by EndScroll for this view (see s_pendingWheel).
            int wheelKey = Key(r, 101);
            if (s_pendingWheel.TryGetValue(wheelKey, out var wheel))
            {
                s_pendingWheel.Remove(wheelKey);
                scroll += wheel;
            }

            int salt = Key(r, 100);
            if (needV) scroll.y = Scrollbar(new Rect(view.xMax, r.y, SB, view.height), scroll.y, view.height, content.height, true, salt);
            if (needH) scroll.x = Scrollbar(new Rect(r.x, view.yMax, view.width, SB), scroll.x, view.width, content.width, false, salt);
            if (needV && needH) FillRect(new Rect(view.xMax, view.yMax, SB, SB), Face);

            scroll.x = Mathf.Clamp(scroll.x, 0, maxX);
            scroll.y = Mathf.Clamp(scroll.y, 0, maxY);

            // Hide the mouse from content that is scrolled out of view, so hidden rows can't be clicked.
            var state = new ScrollState
            {
                SavedMouse = e.mousePosition, Area = r, NeedV = needV, NeedH = needH, WheelKey = wheelKey,
            };
            if (!view.Contains(e.mousePosition) && s_press == 0 && GUIUtility.hotControl == 0)
            {
                state.Redirected = true;
                e.mousePosition = FarAway;
            }
            s_scrollStack.Push(state);

            GUI.BeginGroup(view);
            GUI.BeginGroup(new Rect(-scroll.x - content.x, -scroll.y - content.y,
                content.xMax + view.width + scroll.x, content.yMax + view.height + scroll.y));
            return scroll;
        }

        public static partial void EndScroll()
        {
            GUI.EndGroup();
            GUI.EndGroup();
            if (s_scrollStack.Count == 0) return;
            var state = s_scrollStack.Pop();
            var e = Event.current;
            if (state.Redirected) e.mousePosition = state.SavedMouse;

            // Mouse wheel scrolls the innermost view under the mouse (Shift scrolls horizontally): nested
            // views ended first, so if one of them took the wheel the event is already used here.
            if (e.type == EventType.ScrollWheel && (state.NeedV || state.NeedH) && state.Area.Contains(e.mousePosition))
            {
                float d = (Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x) * 16f;
                var delta = state.NeedV && !e.shift ? new Vector2(0, d) : new Vector2(d, 0);
                if (s_pendingWheel.Count > 64) s_pendingWheel.Clear(); // views that vanished before redrawing
                s_pendingWheel.TryGetValue(state.WheelKey, out var prev);
                s_pendingWheel[state.WheelKey] = prev + delta;
                e.Use();
            }
        }

        /// <summary>A Win98 scrollbar (arrow buttons, dithered track, beveled thumb). Returns the new value.</summary>
        static float Scrollbar(Rect r, float value, float viewSize, float contentSize, bool vertical, int salt)
        {
            var e = Event.current;
            float len = vertical ? r.height : r.width;
            float arrow = Mathf.Min(SB, Mathf.Floor(len / 2f));
            Rect a1 = vertical ? new Rect(r.x, r.y, r.width, arrow) : new Rect(r.x, r.y, arrow, r.height);
            Rect a2 = vertical ? new Rect(r.x, r.yMax - arrow, r.width, arrow) : new Rect(r.xMax - arrow, r.y, arrow, r.height);
            float trackStart = (vertical ? r.y : r.x) + arrow;
            float trackLen = Mathf.Max(0, len - 2 * arrow);
            Rect track = vertical ? new Rect(r.x, trackStart, r.width, trackLen) : new Rect(trackStart, r.y, trackLen, r.height);
            float maxV = Mathf.Max(0.0001f, contentSize - viewSize);
            float thumbLen = Mathf.Max(8f, Mathf.Floor(trackLen * Mathf.Clamp01(viewSize / contentSize)));
            bool showThumb = trackLen >= thumbLen + 1;
            float thumbPos = trackStart + Mathf.Round((trackLen - thumbLen) * Mathf.Clamp01(value / maxV));
            Rect thumb = vertical ? new Rect(r.x, thumbPos, r.width, thumbLen) : new Rect(thumbPos, r.y, thumbLen, r.height);

            int k1 = salt * 31 + (vertical ? 1 : 11);
            int k2 = k1 + 1, kThumb = k1 + 2, kTrack = k1 + 3;
            const float step = 16f;
            float now = Time.unscaledTime;
            Vector2 mp = e.mousePosition;
            float mAxis = vertical ? mp.y : mp.x;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !r.Contains(mp)) break;
                    if (a1.Contains(mp)) { s_press = k1; value -= step; }
                    else if (a2.Contains(mp)) { s_press = k2; value += step; }
                    else if (showThumb && thumb.Contains(mp)) { s_press = kThumb; s_thumbGrab = mAxis - thumbPos; }
                    else if (showThumb) { s_press = kTrack; value += mAxis < thumbPos ? -viewSize : viewSize; }
                    s_repeatAt = now + 0.35f;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (s_press == kThumb)
                    {
                        float p = mAxis - s_thumbGrab - trackStart;
                        float range = Mathf.Max(1f, trackLen - thumbLen);
                        value = Mathf.Clamp01(p / range) * maxV;
                        e.Use();
                    }
                    else if (s_press == k1 || s_press == k2 || s_press == kTrack) e.Use();
                    break;
                case EventType.MouseUp:
                    if (s_press == k1 || s_press == k2 || s_press == kThumb || s_press == kTrack)
                    {
                        s_press = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    // Auto-repeat while an arrow or the track is held.
                    if (now >= s_repeatAt)
                    {
                        if (s_press == k1 && a1.Contains(mp)) { value -= step; s_repeatAt = now + 0.05f; }
                        else if (s_press == k2 && a2.Contains(mp)) { value += step; s_repeatAt = now + 0.05f; }
                        else if (s_press == kTrack && track.Contains(mp) && !thumb.Contains(mp))
                        {
                            value += mAxis < thumbPos ? -viewSize : viewSize;
                            s_repeatAt = now + 0.08f;
                        }
                    }
                    break;
            }
            value = Mathf.Clamp(value, 0, Mathf.Max(0, contentSize - viewSize));

            if (IsRepaint)
            {
                Dither(track);
                if (s_press == kTrack && track.Contains(mp) && showThumb)
                {
                    // Win98 darkens the part of the track being paged.
                    Rect dark = vertical
                        ? (mAxis < thumbPos ? new Rect(r.x, trackStart, r.width, thumbPos - trackStart) : new Rect(r.x, thumb.yMax, r.width, track.yMax - thumb.yMax))
                        : (mAxis < thumbPos ? new Rect(trackStart, r.y, thumbPos - trackStart, r.height) : new Rect(thumb.xMax, r.y, track.xMax - thumb.xMax, r.height));
                    Dither(dark, true);
                }
                bool p1 = s_press == k1 && a1.Contains(mp), p2 = s_press == k2 && a2.Contains(mp);
                DrawStyle(p1 ? s_flat : s_raised, a1);
                DrawStyle(p2 ? s_flat : s_raised, a2);
                DrawGlyph(a1, vertical ? s_arrowUp : s_arrowLeft, p1 ? 1 : 0, p1 ? 1 : 0);
                DrawGlyph(a2, vertical ? s_arrowDown : s_arrowRight, p2 ? 1 : 0, p2 ? 1 : 0);
                if (showThumb) DrawStyle(s_raised, thumb);
            }
            return value;
        }

        // ---- Caption buttons & icons -------------------------------------------------------

        public static partial bool CaptionButton(Rect r, char glyph)
        {
            Ready();
            bool click = ButtonBehavior(r, 3 + glyph, true, out bool held);
            if (IsRepaint)
            {
                DrawStyle(held ? s_pressed : s_raised, r);
                float o = held ? 1 : 0;
                switch (glyph)
                {
                    case 'X': case 'x':
                        DrawGlyph(r, s_glyphClose, o, o);
                        break;
                    case '_':
                        GUI.DrawTexture(new Rect(Mathf.Floor(r.x + (r.width - 6) / 2f) - 1 + o, r.yMax - 5 + o, 6, 2), s_glyphMin);
                        break;
                    case '□': case 'O': case 'o': case 'M':
                        DrawGlyph(r, s_glyphMax, o, o);
                        break;
                    case '?':
                        DrawGlyph(r, s_glyphHelp, o, o);
                        break;
                    default:
                        DrawText(new Rect(r.x + o, r.y + o, r.width, r.height), glyph.ToString(), true, TextAnchor.MiddleCenter, Color.black);
                        break;
                }
            }
            return click;
        }

        public static partial void Icon(Rect r, string name)
        {
            if (!IsRepaint) return;
            Ready();
            var tex = GetIcon(name, r.width >= 24f || r.height >= 24f);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
        }

        public static partial GUIStyle TextStyle(bool bold)
        {
            Ready();
            return bold ? s_textBold : s_text;
        }

        // =====================================================================================
        // Icons (16x16 pixel art; the message box icons also have a 32x32 version)
        // =====================================================================================

        static Texture2D GetIcon(string name, bool big)
        {
            name = name ?? "";
            var cache = big ? s_icons32 : s_icons16;
            if (cache.TryGetValue(name, out var tex) && tex != null) return tex;
            string canon = Canonical(name.Trim().ToLowerInvariant());
            bool status = canon == "info" || canon == "warning" || canon == "error" || canon == "question";
            if (big && !status)
            {
                tex = GetIcon(name, false); // other icons are 16x16 pixel art, scaled up with point filtering
            }
            else
            {
                try { tex = BuildIcon(canon, big ? 32 : 16); }
                catch (Exception ex)
                {
                    CmodLog.Warn($"[ui] icon '{name}' failed: {ex.Message}");
                    tex = FromPattern("icon_window", Patterns.Window);
                }
            }
            cache[name] = tex;
            return tex;
        }

        static string Canonical(string n)
        {
            switch (n)
            {
                case "pc": case "mycomputer": case "my computer": return "computer";
                case "directory": case "open": return "folder";
                case "prop": case "props": case "spawn": case "spawnmenu": case "crate": case "entity": return "box";
                case "weapon": case "weapons": case "pistol": return "gun";
                case "tools": case "toolgun": case "wrench": return "tool";
                case "terminal": case "cmd": case "prompt": return "console";
                case "options": case "control": case "controlpanel": case "control panel": case "gear": return "settings";
                case "about": case "information": return "info";
                case "warn": return "warning";
                case "err": case "stop": return "error";
                case "logo": case "start": case "game": return "cmod";
                case "book": case "manual": return "help";
                case "update": case "internet": case "network": case "web": case "github": case "download": return "globe";
                case "log": case "text": case "notepad": case "file": case "bug": case "doc": return "document";
                case "power": case "quit": case "exit": return "shutdown";
                default: return n;
            }
        }

        static Texture2D BuildIcon(string canon, int size)
        {
            switch (canon)
            {
                case "info":
                case "warning":
                case "error":
                case "question":
                    return StatusIcon(canon, size);
                case "settings": return GearIcon();
                case "globe": return GlobeIcon();
                case "computer": return FromPattern("icon_computer", Patterns.Computer);
                case "shutdown": return FromPattern("icon_shutdown", Replace(Replace(Patterns.Computer, 'C', 'N'), 'T', 'N'));
                case "folder": return FromPattern("icon_folder", Patterns.Folder);
                case "box": return FromPattern("icon_box", Patterns.Box);
                case "gun": return FromPattern("icon_gun", Patterns.Gun);
                case "tool": return FromPattern("icon_tool", Patterns.Tool);
                case "console": return FromPattern("icon_console", Patterns.Console);
                case "cmod": return FromPattern("icon_cmod", Patterns.Cmod);
                case "help": return FromPattern("icon_help", Patterns.Help);
                case "document": return FromPattern("icon_document", Patterns.Document);
                default: return FromPattern("icon_window", Patterns.Window);
            }
        }

        static bool InsideStatusShape(string kind, float x, float y, int size)
        {
            float off = size / 16f;                    // room for the drop shadow
            float c = (size - 1) / 2f - off / 2f;
            if (kind == "warning")
            {
                float top = size * 0.06f, bottom = size * 0.86f;
                if (y < top || y > bottom) return false;
                float half = (y - top) / (bottom - top) * size * 0.47f + 0.5f;
                return Mathf.Abs(x - c) <= half;
            }
            float rad = size * 0.44f;
            float dx = x - c, dy = y - c;
            return dx * dx + dy * dy <= rad * rad;
        }

        static bool GlyphAt(string[] g, int gx, int gy, int k)
        {
            if (gx < 0 || gy < 0) return false;
            gx /= k; gy /= k;
            if (gy >= g.Length || gx >= g[gy].Length) return false;
            return g[gy][gx] == 'X';
        }

        /// <summary>Info / question / warning / error icons, drawn procedurally at 16 or 32 pixels.</summary>
        static Texture2D StatusIcon(string kind, int size)
        {
            var px = new Color32[size * size];
            int k = Mathf.Max(1, size / 16);
            float off = Mathf.Max(1, k);
            float c = (size - 1) / 2f - off / 2f;
            Color32 fill, outline, glyphColor;
            string[] glyph = null;
            switch (kind)
            {
                case "error": fill = new Color32(255, 0, 0, 255); outline = new Color32(128, 0, 0, 255); glyphColor = new Color32(255, 255, 255, 255); break;
                case "warning": fill = new Color32(255, 255, 0, 255); outline = new Color32(0, 0, 0, 255); glyphColor = new Color32(0, 0, 0, 255); glyph = Patterns.Bang; break;
                case "question": fill = new Color32(255, 255, 255, 255); outline = new Color32(0, 0, 0, 255); glyphColor = new Color32(0, 0, 255, 255); glyph = Patterns.Question; break;
                default: fill = new Color32(255, 255, 255, 255); outline = new Color32(0, 0, 0, 255); glyphColor = new Color32(0, 0, 255, 255); glyph = Patterns.InfoI; break;
            }
            int gw = 0, gh = 0, ox = 0, oy = 0;
            if (glyph != null)
            {
                foreach (var row in glyph) gw = Mathf.Max(gw, row.Length);
                gh = glyph.Length;
                ox = Mathf.RoundToInt(c - gw * k / 2f + 0.5f);
                oy = Mathf.RoundToInt(c - gh * k / 2f + 0.5f) + (kind == "warning" ? Mathf.RoundToInt(size * 0.1f) : 0);
            }
            float rad = size * 0.44f;
            float xr = rad * 0.42f, thick = 1.05f * k;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color32 col = new Color32(0, 0, 0, 0);
                    if (InsideStatusShape(kind, x, y, size))
                    {
                        bool edge = !InsideStatusShape(kind, x - 1, y, size) || !InsideStatusShape(kind, x + 1, y, size) ||
                                    !InsideStatusShape(kind, x, y - 1, size) || !InsideStatusShape(kind, x, y + 1, size);
                        col = edge ? outline : fill;
                        if (!edge)
                        {
                            if (kind == "error")
                            {
                                float u = x - c, v = y - c;
                                if (Mathf.Abs(u) <= xr && Mathf.Abs(v) <= xr && Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) <= thick)
                                    col = glyphColor;
                            }
                            else if (glyph != null && GlyphAt(glyph, x - ox, y - oy, k))
                            {
                                col = glyphColor;
                            }
                        }
                    }
                    else if (InsideStatusShape(kind, x - off, y - off, size))
                    {
                        col = new Color32(0, 0, 0, 100); // soft drop shadow
                    }
                    px[y * size + x] = col;
                }
            }
            return MakeTex("icon_" + kind + size, size, size, px, false);
        }

        /// <summary>Adds a 1px outline around every opaque pixel cluster.</summary>
        static void OutlinePixels(Color32[] px, int n, Color32 outline)
        {
            var copy = (Color32[])px.Clone();
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (copy[y * n + x].a == 0) continue;
                    bool edge = x == 0 || y == 0 || x == n - 1 || y == n - 1 ||
                                copy[y * n + x - 1].a == 0 || copy[y * n + x + 1].a == 0 ||
                                copy[(y - 1) * n + x].a == 0 || copy[(y + 1) * n + x].a == 0;
                    if (edge) px[y * n + x] = outline;
                }
            }
        }

        static Texture2D GearIcon()
        {
            const int n = 16;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx);
                    float outer = Mathf.Cos(a * 8f) > 0.15f ? 7.6f : 5.7f;
                    if (d > outer || d < 2.2f) continue;
                    float shade = dx + dy;
                    px[y * n + x] = shade < -3f ? (Color32)Light : shade > 3f ? (Color32)Shadow : (Color32)Face;
                }
            }
            OutlinePixels(px, n, new Color32(0, 0, 0, 255));
            return MakeTex("icon_gear", n, n, px, false);
        }

        static Texture2D GlobeIcon()
        {
            const int n = 16;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    if (dx * dx + dy * dy > 7.3f * 7.3f) continue;
                    bool land = Mathf.Sin(x * 0.9f) + Mathf.Cos(y * 1.1f + x * 0.35f) > 0.75f;
                    bool shine = (x - 5) * (x - 5) + (y - 5) * (y - 5) < 4;
                    px[y * n + x] = land ? new Color32(0, 168, 0, 255) : shine ? new Color32(96, 160, 255, 255) : new Color32(0, 64, 208, 255);
                }
            }
            OutlinePixels(px, n, new Color32(0, 0, 0, 255));
            return MakeTex("icon_globe", n, n, px, false);
        }

        /// <summary>Pixel-art data. Palette letters are listed in PaletteColor ('.' = transparent).</summary>
        static class Patterns
        {
            public static readonly string[] Check =
            {
                "......K",
                ".....KK",
                "K...KKK",
                "KK.KKK.",
                "KKKKK..",
                ".KKK...",
                "..K....",
            };

            public static readonly string[] Radio =
            {
                "....DDDD....",
                "..DDKKKKDD..",
                ".DKKWWWWKKH.",
                ".DKWWWWWWLH.",
                "DKWWWWWWWWLH",
                "DKWWWWWWWWLH",
                "DKWWWWWWWWLH",
                "DKWWWWWWWWLH",
                ".DKWWWWWWLH.",
                ".DLLWWWWLLH.",
                "..HHLLLLHH..",
                "....HHHH....",
            };

            public static readonly string[] RadioDot = { ".KK.", "KKKK", "KKKK", ".KK." };

            public static readonly string[] ArrowUp = { "...K...", "..KKK..", ".KKKKK.", "KKKKKKK" };
            public static readonly string[] ArrowDown = { "KKKKKKK", ".KKKKK.", "..KKK..", "...K..." };
            public static readonly string[] ArrowLeft = { "...K", "..KK", ".KKK", "KKKK", ".KKK", "..KK", "...K" };
            public static readonly string[] ArrowRight = { "K...", "KK..", "KKK.", "KKKK", "KKK.", "KK..", "K..." };

            public static readonly string[] GlyphClose =
            {
                "KK....KK",
                ".KK..KK.",
                "..KKKK..",
                "...KK...",
                "..KKKK..",
                ".KK..KK.",
                "KK....KK",
            };
            public static readonly string[] GlyphMin = { "KKKKKK", "KKKKKK" };
            public static readonly string[] GlyphMax =
            {
                "KKKKKKKKK",
                "KKKKKKKKK",
                "K.......K",
                "K.......K",
                "K.......K",
                "K.......K",
                "K.......K",
                "KKKKKKKKK",
            };
            public static readonly string[] GlyphHelp =
            {
                ".KKKK.",
                "KK..KK",
                "....KK",
                "...KK.",
                "..KK..",
                "..KK..",
                "......",
                "..KK..",
            };

            /// <summary>Trackbar thumb: a raised block that points down.</summary>
            public static readonly string[] Thumb =
            {
                "WWWWWWWWWWK",
                "WLLLLLLLLDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                "WLGGGGGGGDK",
                ".WLGGGGGDK.",
                "..WLGGGDK..",
                "...WLGDK...",
                "....WDK....",
                ".....K.....",
            };

            // Status icon glyphs ('X' = glyph pixel), scaled 1x or 2x.
            public static readonly string[] InfoI =
            {
                ".XX.",
                ".XX.",
                "....",
                "XXX.",
                ".XX.",
                ".XX.",
                ".XX.",
                ".XX.",
                "XXXX",
            };
            public static readonly string[] Question =
            {
                ".XXX.",
                "XX.XX",
                "...XX",
                "..XX.",
                ".XX..",
                ".XX..",
                ".....",
                ".XX..",
                ".XX..",
            };
            public static readonly string[] Bang =
            {
                "XX",
                "XX",
                "XX",
                "XX",
                "XX",
                "..",
                "XX",
            };

            public static readonly string[] Computer =
            {
                "................",
                "..KKKKKKKKKKKK..",
                "..KWWWWWWWWWGK..",
                "..KWCCCCCCCCGK..",
                "..KWCTCCCCCCGK..",
                "..KWCCCCCCCCGK..",
                "..KWCCCCCCCCGK..",
                "..KWCCCCCCCCGK..",
                "..KWCCCCCCCCGK..",
                "..KGGGGGGGGGGK..",
                "..KKKKKKKKKKKK..",
                "......KDDK......",
                "...KKKKKKKKKKK..",
                "...KWWWWWWWWWK..",
                "...KGGGGGGgGDK..",
                "...KKKKKKKKKKK..",
            };

            public static readonly string[] Folder =
            {
                "................",
                "................",
                "..KKKKK.........",
                ".KYWYWYK........",
                "KYWYWYWYKKKKKKK.",
                "KWYYYYYYYYYYYYOK",
                "KYYYYYYYYYYYYYOK",
                "KYYYYYYYYYYYYYOK",
                "KYYYYYYYYYYYYYOK",
                "KYYYYYYYYYYYYYOK",
                "KYYYYYYYYYYYYYOK",
                "KYYYYYYYYYYYYYOK",
                "KOOOOOOOOOOOOOOK",
                "KKKKKKKKKKKKKKKK",
                "................",
                "................",
            };

            public static readonly string[] Box =
            {
                "................",
                "....KKKKKKKKKKKK",
                "...KyyyyyyyyyKnK",
                "..KyyyyyyyyyKnnK",
                ".KKKKKKKKKKKnnnK",
                ".KbbbbbbbbbKnnnK",
                ".KbnbbbbbnbKnnnK",
                ".KbbnbbbnbbKnnnK",
                ".KbbbnbnbbbKnnnK",
                ".KbbbbnbbbbKnnnK",
                ".KbbbnbnbbbKnnnK",
                ".KbbnbbbnbbKnnnK",
                ".KbnbbbbbnbKnnnK",
                ".KbbbbbbbbbKnnK.",
                ".KbbbbbbbbbKnK..",
                ".KKKKKKKKKKKK...",
            };

            public static readonly string[] Gun =
            {
                "................",
                "................",
                "................",
                "..KKKKKKKKKKKKK.",
                "..KLLLLLLLLLLLDK",
                "..KGGGGGGGGGGGDK",
                "..KDDDDDDDDDDDDK",
                "..KKKKKKKKKKKKKK",
                "..KDDDDK.KDK....",
                "..KDDDK..KK.....",
                ".KDDDDK.........",
                ".KDDDK..........",
                "KDDDDK..........",
                "KDDDDK..........",
                "KKKKKK..........",
                "................",
            };

            public static readonly string[] Tool =
            {
                "..........KK.KK.",
                ".........KLK.KLK",
                ".........KLK.KDK",
                ".........KLLKDDK",
                "........KLLDDDK.",
                ".......KLLDDKK..",
                "......KLLDK.....",
                ".....KLLDK......",
                "....KLLDK.......",
                "...KLLDK........",
                "..KLLDK.........",
                ".KLLDK..........",
                "KLLDK...........",
                "KLDK............",
                ".KK.............",
                "................",
            };

            public static readonly string[] Console =
            {
                "DDDDDDDDDDDDDDDD",
                "DNNNNNNNBBBBBBBD",
                "DKKKKKKKKKKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DKWKKKKKKKKKKKKD",
                "DKKWKKKKKKKKKKKD",
                "DKKKWKKKKKKKKKKD",
                "DKKWKKKKKKKKKKKD",
                "DKWKKKWWWWKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DKKKKKKKKKKKKKKD",
                "DDDDDDDDDDDDDDDD",
            };

            public static readonly string[] Cmod =
            {
                "................",
                ".KKKKKKKKKKKKKK.",
                ".KTTTTTTTTTTTTK.",
                ".KTCCCCCCCCCCCK.",
                ".KTCCCWWWWWCCCK.",
                ".KTCCWWCCCWWCCK.",
                ".KTCWWCCCCCCCCK.",
                ".KTCWWCCCCCCCCK.",
                ".KTCWWCCCCCCCCK.",
                ".KTCWWCCCCCCCCK.",
                ".KTCCWWCCCWWCCK.",
                ".KTCCCWWWWWCCCK.",
                ".KTCCCCCCCCCYYK.",
                ".KTCCCCCCCCCYYK.",
                ".KKKKKKKKKKKKKK.",
                "................",
            };

            public static readonly string[] Help =
            {
                "................",
                "...KKKKKKKKKKK..",
                "..KNNNNNNNNNNNK.",
                "..KNNNNWWWNNNNK.",
                "..KNNNWNNNWNNNK.",
                "..KNNNNNNNWNNNK.",
                "..KNNNNNNWNNNNK.",
                "..KNNNNNWNNNNNK.",
                "..KNNNNNWNNNNNK.",
                "..KNNNNNNNNNNNK.",
                "..KNNNNNWNNNNNK.",
                "..KNNNNNNNNNNNK.",
                "..KKKKKKKKKKKKK.",
                "..KWWWWWWWWWWWK.",
                "..KLLLLLLLLLLLK.",
                "...KKKKKKKKKKK..",
            };

            public static readonly string[] Document =
            {
                "..KKKKKKKKK.....",
                "..KWWWWWWWKK....",
                "..KWWWWWWWKWK...",
                "..KWDDDDWWKKKK..",
                "..KWWWWWWWWWWK..",
                "..KWDDDDDDDWWK..",
                "..KWWWWWWWWWWK..",
                "..KWDDDDDDDWWK..",
                "..KWWWWWWWWWWK..",
                "..KWDDDDDDDWWK..",
                "..KWWWWWWWWWWK..",
                "..KWDDDDDWWWWK..",
                "..KWWWWWWWWWWK..",
                "..KWWWWWWWWWWK..",
                "..KKKKKKKKKKKK..",
                "................",
            };

            public static readonly string[] Window =
            {
                "................",
                "KKKKKKKKKKKKKKKK",
                "KNNNNNNNBBBBBBBK",
                "KNNNNNNNBBBBBBBK",
                "KGGGGGGGGGGGGGGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGWWWWWWWWWWWWGK",
                "KGGGGGGGGGGGGGGK",
                "KKKKKKKKKKKKKKKK",
                "................",
            };
        }
    }
}
