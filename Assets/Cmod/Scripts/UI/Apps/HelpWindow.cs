using UnityEngine;

namespace Cmod.UI
{
    /// <summary>Help: the list of controls, grouped, plus a few tips. F1 toggles it.</summary>
    public sealed class HelpWindow : Win98Window
    {
        const float Pad = 6f;
        const float RowH = 17f;
        const float KeyColW = 150f;
        const float BtnW = 75f, BtnH = 23f;

        /// <summary>A section heading (Key == null) or a "key - action" row.</summary>
        readonly struct Entry
        {
            public readonly string Key;
            public readonly string Text;
            public Entry(string key, string text) { Key = key; Text = text; }
        }

        static readonly Entry[] s_entries =
        {
            new Entry(null, "Moving around"),
            new Entry("W  A  S  D", "Walk"),
            new Entry("Space", "Jump"),
            new Entry("Shift", "Sprint"),
            new Entry("Ctrl", "Crouch"),
            new Entry("V", "Noclip: fly through walls (Space up, Ctrl down, Shift fast)"),
            new Entry("F", "Flashlight"),

            new Entry(null, "Weapons and tools"),
            new Entry("1 - 6, mouse wheel", "Select a weapon"),
            new Entry("Left mouse", "Fire, or use the selected tool"),
            new Entry("Right mouse", "Secondary fire, or the tool's second action"),
            new Entry("R", "Reload, or the tool's reload action"),
            new Entry("Toolgun", "Pick its mode in the spawn menu's Tools tab"),

            new Entry(null, "Interface"),
            new Entry("Q (hold)", "Spawn menu: props, weapons, tools, utilities"),
            new Entry("`", "Console (type \"help\" for every command)"),
            new Entry("Z", "Undo your last spawn or tool action"),
            new Entry("F1", "This help"),
            new Entry("Esc", "Close the window in front, otherwise the Start menu"),
        };

        static readonly string[] s_tips =
        {
            "Tip: \"spawnmenu_toggle 1\" in the console makes Q a toggle instead of hold.",
            "Tip: \"sv_gravity\" and \"host_timescale\" change gravity and game speed (also in the spawn menu's Utilities tab).",
            "Tip: \"cleanup\" removes everything you spawned.",
        };

        Vector2 _scroll;

        public HelpWindow()
        {
            Title = "Cmod Help";
            IconName = "help";
            Rect = new Rect(0, 0, 470, 460);
            Resizable = true;
            MinSize = new Vector2(340, 240);
        }

        public override void OnOpened()
        {
            CenterOnScreen();
            _scroll = Vector2.zero;
        }

        public override void DrawContents(Rect c)
        {
            // Header.
            Win98.Icon(new Rect(c.x + Pad + 2f, c.y + Pad + 2f, 32f, 32f), "help");
            Win98.Label(new Rect(c.x + Pad + 44f, c.y + Pad + 2f, c.width - 60f, 16f), "Cmod Controls", true);
            Win98.Label(new Rect(c.x + Pad + 44f, c.y + Pad + 18f, c.width - 60f, 16f), "Everything you need to start building.",
                false, TextAnchor.MiddleLeft, Win98.Shadow);

            var area = new Rect(c.x + Pad, c.y + Pad + 42f, c.width - 2 * Pad, c.height - 2 * Pad - 42f - BtnH - Pad);
            Win98.SunkenPanel(area, true);
            var inner = Win98.Inset(area, 2);
            float contentW = inner.width - 16f;

            // Measure.
            float h = 4f;
            foreach (var e in s_entries) h += e.Key == null ? RowH + 8f : RowH;
            h += 8f;
            foreach (var tip in s_tips) h += Win98.TextHeight(tip, contentW - 16f) + 4f;
            var content = new Rect(0, 0, contentW, h + 4f);

            _scroll = Win98.BeginScroll(inner, _scroll, content);
            try
            {
                float y = 4f;
                bool firstSection = true;
                foreach (var e in s_entries)
                {
                    if (e.Key == null)
                    {
                        if (!firstSection) y += 8f;
                        firstSection = false;
                        Win98.DrawText(new Rect(8f, y, contentW - 16f, RowH), e.Text, true, TextAnchor.MiddleLeft, Win98.SelectionBg);
                        Win98.FillRect(new Rect(8f, y + RowH - 1f, contentW - 16f, 1f), Win98.Shadow);
                        y += RowH;
                        continue;
                    }
                    Win98.DrawText(new Rect(16f, y, KeyColW - 16f, RowH), e.Key, true, TextAnchor.MiddleLeft, Color.black);
                    float tw = contentW - KeyColW - 8f;
                    Win98.DrawText(new Rect(KeyColW, y, tw, RowH), Win98.Ellipsize(e.Text, tw), false, TextAnchor.MiddleLeft, Color.black);
                    y += RowH;
                }
                y += 8f;
                foreach (var tip in s_tips)
                {
                    float th = Win98.TextHeight(tip, contentW - 16f);
                    Win98.DrawText(new Rect(8f, y, contentW - 16f, th), tip, false, TextAnchor.UpperLeft, Win98.Shadow, true);
                    y += th + 4f;
                }
            }
            finally
            {
                Win98.EndScroll();
            }

            var close = new Rect(c.xMax - Pad - BtnW, c.yMax - Pad - BtnH, BtnW, BtnH);
            if (Win98.Button(close, "Close")) Close();
            var about = new Rect(close.x - 6f - BtnW, close.y, BtnW, BtnH);
            if (Win98.Button(about, "About...")) CmodApps.About?.Open();
        }
    }
}
