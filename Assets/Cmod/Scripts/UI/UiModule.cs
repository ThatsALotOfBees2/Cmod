using System.Collections.Generic;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    public static partial class CmodModules
    {
        /// <summary>
        /// UI boot step: creates the ui_scale convar, adds the WindowManager (which draws all windows,
        /// the taskbar, the start menu and message boxes, and owns the cursor lock), and registers
        /// two small UI test commands.
        /// </summary>
        public static partial void Ui_Init(GameObject root)
        {
            WindowManager.CreateConVars();
            if (root.GetComponent<WindowManager>() == null) root.AddComponent<WindowManager>();

            ConCommands.Register("ui_widgets", "Opens the Win98 widget gallery (a test window for every UI widget).",
                _ => WidgetGalleryWindow.Show());
            ConCommands.Register("msgbox", "msgbox [info|warning|error|question] <text>: shows a test message box.",
                args =>
                {
                    var kind = MessageBox.Kind.Info;
                    int start = 0;
                    if (args.Length > 0 && args[0].Length > 0 && !char.IsDigit(args[0][0]) && args[0][0] != '-' && args[0][0] != '+' &&
                        System.Enum.TryParse(args[0], true, out MessageBox.Kind k) && System.Enum.IsDefined(typeof(MessageBox.Kind), k))
                    {
                        kind = k;
                        start = 1;
                    }
                    string text = args.Length > start ? string.Join(" ", args, start, args.Length - start) : "Hello from Cmod 98!";
                    string[] buttons = kind == MessageBox.Kind.Question ? new[] { "Yes", "No", "Cancel" } : new[] { "OK" };
                    MessageBox.Show(CmodGame.GameName, text, kind, buttons, i => CmodLog.Info($"msgbox result: {i}"));
                });
        }
    }
}

namespace Cmod.UI
{
    /// <summary>A gallery of every Win98 widget, opened with the "ui_widgets" console command.
    /// Handy for checking the look at different ui_scale values.</summary>
    internal sealed class WidgetGalleryWindow : Win98Window
    {
        static WidgetGalleryWindow s_instance;

        static readonly string[] s_tabs = { "Controls", "Lists", "Icons" };
        static readonly string[] s_dropdownItems = { "Wood", "Metal", "Rubber", "Ice", "Concrete", "Glass", "Plastic", "Flesh", "Dirt", "Paper", "Cardboard", "Bouncy" };
        static readonly string[] s_iconNames = { "computer", "folder", "box", "gun", "tool", "console", "settings", "info", "warning", "error", "question", "cmod", "help", "globe", "document", "shutdown", "unknown" };
        readonly List<string> m_listItems = new List<string>();

        int m_tab;
        bool m_check = true, m_check2;
        int m_radio;
        float m_slider = 0.4f;
        string m_text = "Type here";
        string m_password = "secret";
        string m_area = "A multi-line\ntext area.";
        int m_dropdown;
        int m_listSel = 2;
        Vector2 m_listScroll, m_scroll;

        WidgetGalleryWindow()
        {
            Title = "Widget Gallery";
            IconName = "settings";
            Rect = new Rect(80, 60, 460, 380);
            Resizable = true;
            MinSize = new Vector2(360, 300);
            for (int i = 1; i <= 40; i++) m_listItems.Add("List item number " + i);
        }

        public static void Show()
        {
            if (s_instance == null)
            {
                s_instance = new WidgetGalleryWindow();
                WindowManager.Register(s_instance);
                s_instance.CenterOnScreen();
            }
            s_instance.Open();
        }

        public override void DrawContents(Rect c)
        {
            var tabs = new Rect(c.x + 4, c.y + 4, c.width - 8, c.height - 8 - 30);
            m_tab = Win98.Tabs(tabs, m_tab, s_tabs);
            var p = Win98.TabContentRect(tabs);

            switch (m_tab)
            {
                case 0: DrawControls(p); break;
                case 1: DrawLists(p); break;
                default: DrawIcons(p); break;
            }

            float by = c.yMax - 4 - 23;
            if (Win98.Button(new Rect(c.xMax - 4 - 75, by, 75, 23), "Close")) Close();
            if (Win98.Button(new Rect(c.xMax - 4 - 75 - 6 - 75, by, 75, 23), "Message..."))
                MessageBox.Show("Widget Gallery", "This is a Win98 message box. Enter picks the first button, Esc the last.",
                    MessageBox.Kind.Question, new[] { "Yes", "No", "Cancel" }, i => CmodLog.Info("Gallery message box: " + i));
            if (Win98.Button(new Rect(c.x + 4, by, 75, 23), "Disabled", false)) { }
        }

        void DrawControls(Rect p)
        {
            float half = Mathf.Floor((p.width - 8) / 2f);
            var left = new Rect(p.x, p.y, half, p.height);
            var right = new Rect(p.x + half + 8, p.y, p.width - half - 8, p.height);

            Win98.GroupBox(new Rect(left.x, left.y, left.width, 96), "Options");
            m_check = Win98.Checkbox(new Rect(left.x + 10, left.y + 16, left.width - 20, 18), m_check, "Enable gravity");
            m_check2 = Win98.Checkbox(new Rect(left.x + 10, left.y + 34, left.width - 20, 18), m_check2, "Freeze props");
            if (Win98.Radio(new Rect(left.x + 10, left.y + 54, left.width - 20, 18), m_radio == 0, "Small")) m_radio = 0;
            if (Win98.Radio(new Rect(left.x + 10, left.y + 72, left.width - 20, 18), m_radio == 1, "Large")) m_radio = 1;

            Win98.Label(new Rect(left.x, left.y + 104, left.width, 16), "Slider: " + Mathf.RoundToInt(m_slider * 100) + "%");
            m_slider = Win98.Slider(new Rect(left.x, left.y + 120, left.width, 22), m_slider, 0f, 1f);
            Win98.ProgressBar(new Rect(left.x, left.y + 150, left.width, 16), m_slider);
            Win98.Separator(new Rect(left.x, left.y + 172, left.width, 6));
            m_dropdown = Win98.Dropdown(new Rect(left.x, left.y + 184, left.width, 21), m_dropdown, s_dropdownItems, "gallery_material");

            Win98.Label(new Rect(right.x, right.y, right.width, 16), "Text field:", true);
            m_text = Win98.TextField(new Rect(right.x, right.y + 18, right.width, 21), m_text, "gallery_text");
            Win98.Label(new Rect(right.x, right.y + 44, right.width, 16), "Password field:");
            m_password = Win98.PasswordField(new Rect(right.x, right.y + 62, right.width, 21), m_password);
            Win98.Label(new Rect(right.x, right.y + 88, right.width, 16), "Text area:");
            m_area = Win98.TextArea(new Rect(right.x, right.y + 106, right.width, Mathf.Max(40, p.height - 140)), m_area);
            Win98.ToggleButton(new Rect(right.x, p.yMax - 26, 80, 23), "Toggle", m_check);
        }

        void DrawLists(Rect p)
        {
            float half = Mathf.Floor((p.width - 8) / 2f);
            m_listSel = Win98.ListBox(new Rect(p.x, p.y, half, p.height), m_listSel, m_listItems, ref m_listScroll);

            var view = new Rect(p.x + half + 8, p.y, p.width - half - 8, p.height);
            Win98.SunkenPanel(view, true);
            var inner = Win98.Inset(view, 2);
            var content = new Rect(0, 0, 400, 600);
            m_scroll = Win98.BeginScroll(inner, m_scroll, content);
            for (int i = 0; i < 20; i++)
            {
                if (Win98.Button(new Rect(8, 8 + i * 29, 120, 23), "Button " + (i + 1)))
                    CmodLog.Info("Gallery button " + (i + 1));
                Win98.Label(new Rect(140, 8 + i * 29, 250, 23), "Scroll both ways: row " + (i + 1));
            }
            Win98.EndScroll();
        }

        void DrawIcons(Rect p)
        {
            float x = p.x, y = p.y;
            for (int i = 0; i < s_iconNames.Length; i++)
            {
                if (x + 70 > p.xMax) { x = p.x; y += 56; }
                Win98.Icon(new Rect(x + 19, y, 32, 32), s_iconNames[i]);
                Win98.Icon(new Rect(x + 2, y + 8, 16, 16), s_iconNames[i]);
                Win98.Label(new Rect(x, y + 34, 70, 14), s_iconNames[i], false, TextAnchor.MiddleCenter);
                x += 72;
            }
        }
    }
}
