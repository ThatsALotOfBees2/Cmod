using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// The always-visible Win98 taskbar (28px, bottom of the screen): a Start button with the cmod logo,
    /// one button per open window, and a sunken tray with an optional FPS readout (cl_showfps) and a clock.
    /// Drawn by WindowManager on its own input layer.
    /// </summary>
    internal static class Taskbar
    {
        const float ButtonH = 22f;
        const float StartW = 54f;
        const float WindowButtonMaxW = 160f;
        const float WindowButtonGap = 3f;

        static int s_clockMinute = -1;
        static string s_clockText = "";
        static ConVar s_fpsVar;
        static int s_fpsFrames;
        static float s_fpsTime;
        static string s_fpsText = "";

        internal static Rect BarRect
        {
            get
            {
                var s = WindowManager.ScreenSizeScaled;
                return new Rect(0, s.y - WindowManager.TaskbarHeight, s.x, WindowManager.TaskbarHeight);
            }
        }

        internal static Rect StartButtonRect
        {
            get
            {
                var bar = BarRect;
                return new Rect(bar.x + 2, bar.y + 4, StartW, ButtonH);
            }
        }

        static bool ShowFps
        {
            get
            {
                // cl_showfps belongs to the base commands; look it up lazily and never create it here.
                if (s_fpsVar == null) s_fpsVar = ConCommands.GetVar("cl_showfps");
                return s_fpsVar != null && s_fpsVar.Bool;
            }
        }

        /// <summary>Called every frame from WindowManager.Update: clock text and smoothed FPS.</summary>
        internal static void UpdateStats()
        {
            var now = DateTime.Now;
            int minute = now.Hour * 60 + now.Minute;
            if (minute != s_clockMinute)
            {
                s_clockMinute = minute;
                s_clockText = now.ToString("HH:mm");
            }

            s_fpsFrames++;
            s_fpsTime += Time.unscaledDeltaTime;
            if (s_fpsTime >= 0.5f)
            {
                int fps = Mathf.RoundToInt(s_fpsFrames / s_fpsTime);
                s_fpsText = fps + " FPS";
                s_fpsFrames = 0;
                s_fpsTime = 0f;
            }
        }

        internal static void Draw(bool isTarget)
        {
            var e = Event.current;
            var bar = BarRect;

            // ---- Bar background: face color with the raised top edge ----
            if (e.type == EventType.Repaint)
            {
                Win98.FillRect(bar, Win98.Face);
                Win98.FillRect(new Rect(bar.x, bar.y, bar.width, 1), Win98.Light);
                Win98.FillRect(new Rect(bar.x, bar.y + 1, bar.width, 1), Win98.Highlight);
            }

            // ---- Start button (toggles on mouse down, like Win98) ----
            var start = StartButtonRect;
            if (e.type == EventType.MouseDown && e.button == 0 && start.Contains(e.mousePosition))
            {
                StartMenu.Toggle();
                e.Use();
            }
            if (e.type == EventType.Repaint)
            {
                bool down = StartMenu.IsOpen;
                Win98.ButtonFrame(start, down);
                float o = down ? 1 : 0;
                Win98.Icon(new Rect(start.x + 4 + o, start.y + 3 + o, 16, 16), "cmod");
                Win98.DrawText(new Rect(start.x + 22 + o, start.y + o, start.width - 24, start.height), "Start", true,
                    TextAnchor.MiddleLeft, Color.black);

                // Etched vertical divider between Start and the window buttons.
                float dx = start.xMax + 3;
                Win98.FillRect(new Rect(dx, bar.y + 4, 1, ButtonH), Win98.Shadow);
                Win98.FillRect(new Rect(dx + 1, bar.y + 4, 1, ButtonH), Win98.Highlight);
            }

            // ---- Tray (sunken): FPS + clock ----
            bool showFps = ShowFps;
            float clockW = Mathf.Ceil(Win98.TextWidth(s_clockText)) + 12;
            float fpsW = showFps ? Mathf.Ceil(Win98.TextWidth(s_fpsText)) + 10 : 0;
            float trayW = Mathf.Max(60f, clockW + fpsW + 4);
            var tray = new Rect(bar.xMax - 2 - trayW, bar.y + 4, trayW, ButtonH);
            if (e.type == EventType.Repaint)
            {
                Win98.ThinSunken(tray);
                var clockRect = new Rect(tray.xMax - clockW - 2, tray.y, clockW, tray.height);
                Win98.DrawText(clockRect, s_clockText, false, TextAnchor.MiddleCenter, Color.black);
                if (showFps)
                    Win98.DrawText(new Rect(tray.x + 6, tray.y, fpsW, tray.height), s_fpsText, false, TextAnchor.MiddleLeft, Color.black);
            }

            // ---- Window buttons ----
            var open = WindowManager.OpenOrder;
            int count = 0;
            for (int i = 0; i < open.Count; i++)
                if (open[i].ShowInTaskbar) count++;
            if (count == 0) return;

            float left = start.xMax + 8;
            float avail = tray.x - 4 - left;
            if (avail <= 0) return;
            float bw = Mathf.Floor(Mathf.Min(WindowButtonMaxW, (avail - (count - 1) * WindowButtonGap) / count));
            if (bw < 24) bw = Mathf.Max(8f, bw);

            float x = left;
            Win98Window clicked = null;
            for (int i = 0; i < open.Count; i++)
            {
                var w = open[i];
                if (!w.ShowInTaskbar) continue;
                var r = new Rect(x, bar.y + 4, bw, ButtonH);
                x += bw + WindowButtonGap;

                if (Win98.ButtonBehavior(r, 7000 + w.WindowId, true, out bool held)) clicked = w;
                if (e.type != EventType.Repaint) continue;

                bool active = w.IsFocused && !w.IsMinimized;
                bool down = active || held;
                Win98.ButtonFrame(r, down);
                if (active && !held) Win98.Dither(Win98.Inset(r, 2));
                float o = down ? 1 : 0;
                Win98.Icon(new Rect(r.x + 4 + o, r.y + 3 + o, 16, 16), w.IconName);
                var tr = new Rect(r.x + 22 + o, r.y + o, r.width - 26, r.height);
                if (tr.width > 6)
                    Win98.DrawText(tr, Win98.Ellipsize(w.Title, tr.width, active), active, TextAnchor.MiddleLeft, Color.black);
            }
            if (clicked != null) WindowManager.TaskbarClick(clicked);
        }
    }

    /// <summary>Start menu implementation (contract in UI/Contracts.cs). Opened by the taskbar's Start button or Esc.</summary>
    public static partial class StartMenu
    {
        sealed class Item
        {
            public string Label;
            public string Icon;
            public Action OnClick;
            public int Group;
            public int Order;
            public int Seq;
        }

        const float Edge = 3f;        // raised frame
        const float BannerW = 21f;    // the vertical "Cmod 98" strip
        const float RowH = 34f;       // 32x32 icons, like the Win98 top-level start menu
        const float SepH = 9f;

        static readonly List<Item> s_items = new List<Item>();
        static int s_seq;
        static bool s_layoutDirty = true;
        static float s_itemsW, s_itemsH;
        static int s_highlight = -1;      // index into s_items (keyboard or hover)
        static Vector2 s_lastMouse;

        internal static bool IsOpen { get; private set; }

        public static partial void AddItem(string label, string iconName, Action onClick, int group, int order)
        {
            if (string.IsNullOrEmpty(label)) return;
            RemoveItem(label); // re-adding an item replaces it
            s_items.Add(new Item { Label = label, Icon = iconName, OnClick = onClick, Group = group, Order = order, Seq = s_seq++ });
            s_items.Sort((a, b) =>
            {
                int c = a.Group.CompareTo(b.Group);
                if (c == 0) c = a.Order.CompareTo(b.Order);
                if (c == 0) c = a.Seq.CompareTo(b.Seq);
                return c;
            });
            s_layoutDirty = true;
        }

        public static partial void RemoveItem(string label)
        {
            if (s_items.RemoveAll(i => i.Label == label) > 0) s_layoutDirty = true;
            if (s_highlight >= s_items.Count) s_highlight = -1;
        }

        internal static void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            s_highlight = -1;
            s_lastMouse = new Vector2(float.NaN, float.NaN);
            Win98.ClosePopup();
        }

        internal static void Close() => IsOpen = false;

        internal static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        static void Measure()
        {
            if (!s_layoutDirty) return;
            s_layoutDirty = false;
            float maxText = 0;
            float h = 0;
            for (int i = 0; i < s_items.Count; i++)
            {
                maxText = Mathf.Max(maxText, Win98.TextWidth(s_items[i].Label));
                if (i > 0 && s_items[i].Group != s_items[i - 1].Group) h += SepH;
                h += RowH;
            }
            if (s_items.Count == 0) h = RowH; // "(Empty)"
            s_itemsW = Mathf.Max(150f, Mathf.Ceil(6 + 32 + 10 + maxText + 24));
            s_itemsH = h;
        }

        /// <summary>Menu rect in scaled screen space, sitting on top of the taskbar at the left edge.</summary>
        internal static Rect MenuRect
        {
            get
            {
                Measure();
                float w = Edge + BannerW + s_itemsW + Edge;
                float h = Edge + s_itemsH + Edge;
                float bottom = WindowManager.ScreenSizeScaled.y - WindowManager.TaskbarHeight + 1;
                return new Rect(2, Mathf.Max(0, bottom - h), w, h);
            }
        }

        static void Activate(int index)
        {
            if (index < 0 || index >= s_items.Count) return;
            var item = s_items[index];
            Close();
            try { item.OnClick?.Invoke(); }
            catch (Exception ex) { CmodLog.Error($"[start menu '{item.Label}'] {ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>Arrow keys move the highlight, Enter runs it. Called from WindowManager.Update while open.</summary>
        internal static void HandleKeys()
        {
            if (s_items.Count == 0) return;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                s_highlight = s_highlight <= 0 ? s_items.Count - 1 : s_highlight - 1;
            else if (Input.GetKeyDown(KeyCode.DownArrow))
                s_highlight = s_highlight < 0 || s_highlight >= s_items.Count - 1 ? 0 : s_highlight + 1;
            else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && s_highlight >= 0)
                Activate(s_highlight);
        }

        internal static void Draw(bool isTarget)
        {
            var e = Event.current;
            var menu = MenuRect;
            Vector2 mp = e.mousePosition;

            // Mouse movement takes over the highlight from the keyboard.
            bool mouseMoved = isTarget && mp != s_lastMouse;
            if (isTarget) s_lastMouse = mp;

            if (e.type == EventType.Repaint)
            {
                Win98.RaisedPanel(menu);
                var banner = new Rect(menu.x + Edge, menu.y + Edge, BannerW, menu.height - 2 * Edge);
                Win98.VGradient(banner, Win98.TitleActive, Win98.TitleActiveEnd);
                DrawBannerText(banner);
            }

            float x0 = menu.x + Edge + BannerW;
            float y = menu.y + Edge;
            if (s_items.Count == 0)
            {
                Win98.DrawText(new Rect(x0 + 10, y, s_itemsW - 12, RowH), "(Empty)", false, TextAnchor.MiddleLeft, Win98.Shadow);
                if (isTarget && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
                return;
            }

            int clicked = -1;
            for (int i = 0; i < s_items.Count; i++)
            {
                var item = s_items[i];
                if (i > 0 && item.Group != s_items[i - 1].Group)
                {
                    Win98.Separator(new Rect(x0 + 2, y, s_itemsW - 4, SepH));
                    y += SepH;
                }
                var row = new Rect(x0, y, s_itemsW, RowH);
                y += RowH;

                bool over = isTarget && row.Contains(mp);
                if (over && (mouseMoved || e.type == EventType.MouseDown)) s_highlight = i;
                if (over && e.button == 0)
                {
                    if (e.type == EventType.MouseDown) e.Use();
                    else if (e.type == EventType.MouseUp) { clicked = i; e.Use(); }
                }

                if (e.type == EventType.Repaint)
                {
                    bool hi = i == s_highlight;
                    if (hi) Win98.FillRect(row, Win98.SelectionBg);
                    Win98.Icon(new Rect(row.x + 6, row.y + 1, 32, 32), item.Icon);
                    var tr = new Rect(row.x + 48, row.y, row.width - 52, row.height);
                    Win98.DrawText(tr, Win98.Ellipsize(item.Label, tr.width), false, TextAnchor.MiddleLeft, hi ? Color.white : Color.black);
                }
            }
            // Clicks on separators or the banner are swallowed too.
            if (isTarget && (e.type == EventType.MouseDown || e.type == EventType.MouseUp) && e.type != EventType.Used) e.Use();
            if (clicked >= 0) Activate(clicked);
        }

        /// <summary>"Cmod 98" written bottom-to-top in the banner (bold "Cmod", regular "98").</summary>
        static void DrawBannerText(Rect banner)
        {
            var style = Win98.BannerStyle;
            var oldMatrix = GUI.matrix;
            var pivot = new Vector2(banner.x, banner.yMax);
            // Rotate -90° around the pivot in GUI space, then apply the existing ui_scale matrix.
            // (GUIUtility.RotateAroundPivot uses the wrong pivot when GUI.matrix is scaled.)
            GUI.matrix = oldMatrix *
                         Matrix4x4.Translate(new Vector3(pivot.x, pivot.y, 0)) *
                         Matrix4x4.Rotate(Quaternion.Euler(0, 0, -90f)) *
                         Matrix4x4.Translate(new Vector3(-pivot.x, -pivot.y, 0));

            var oldStyle = style.fontStyle;
            var oldColor = style.normal.textColor;
            var content = BannerContent;
            style.fontStyle = FontStyle.Bold;
            content.text = "Cmod";
            float boldW = style.CalcSize(content).x;
            var r = new Rect(pivot.x + 8, pivot.y, boldW + 2, banner.width);
            style.Draw(r, content, false, false, false, false);
            style.fontStyle = FontStyle.Normal;
            style.normal.textColor = new Color(0.82f, 0.86f, 1f);
            content.text = "98";
            style.Draw(new Rect(r.xMax + 1, pivot.y, 40, banner.width), content, false, false, false, false);
            style.fontStyle = oldStyle;
            style.normal.textColor = oldColor;
            GUI.matrix = oldMatrix;
        }

        static GUIContent s_bannerContent;
        static GUIContent BannerContent => s_bannerContent ?? (s_bannerContent = new GUIContent());
    }
}
